/// What the air bridge is for: the DEMAND model.
///
/// The corridor planner only knows that targets want units per tick and that
/// a drop delivers some units. This module supplies the two models behind
/// that: a forest fire wanting litres of water, and a set of things wanting
/// to be touched (a demo: drones fly out and touch each can once). Both give
/// the planner the same interface, so the lanes, the fast and slow loops and
/// the permission evidence are shared.
module FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.Demand

open System

open FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.FireDomain
open FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.AirBridge

/// Touch demand: every target owes `Touches` drops, and wants them within
/// `TargetS` seconds of a corridor opening to it.
type TouchRules =
    {
        /// Seconds within which an open target should get its touches. It sets
        /// the demand rate, and so how many drones one target can absorb.
        TargetS: float
    }

let defaultTouchRules = { TargetS = 30.0 }

type Demand =
    | Fire of FireSim.FirePhysics
    | Touches of TouchRules

/// The per-target state of whichever model is running.
type TargetState =
    | FireState of FireSim.SectorState[]
    /// Touches still owed per target.
    | TouchState of float[]

module Demand =

    let name =
        function
        | Fire _ -> "fire"
        | Touches _ -> "touches"

    let tryParse (s: string) =
        match s.Trim().ToLowerInvariant() with
        | "fire" -> Some(Fire FireSim.defaultPhysics)
        | "touches"
        | "touch" -> Some(Touches defaultTouchRules)
        | _ -> None

    /// What one drop delivers.
    let unitName =
        function
        | Fire _ -> "L"
        | Touches _ -> "touches"

    /// The headline measure of how much is left to do.
    let metricName =
        function
        | Fire _ -> "fire ha"
        | Touches _ -> "touches left"

    /// What the end-of-run list of leftovers is.
    let unresolvedName =
        function
        | Fire _ -> "new ignitions"
        | Touches _ -> "targets left"

    let unitsPerDrop (demand: Demand) (fleet: DroneClass) =
        match demand with
        | Fire _ -> fleet.PayloadL
        | Touches _ -> 1.0

    /// Units per tick below which a concentrated target's drops are wasted.
    let floorPerTick (demand: Demand) (tick: Tick) =
        match demand with
        | Fire phys -> FireSim.floorPerTick phys tick
        | Touches _ -> 0.0

    /// Why this scenario cannot be run under this demand, if it cannot. A run
    /// in which nothing ever wants anything flies nothing, and an evidence
    /// pack over no flights would pass vacuously.
    let validate (demand: Demand) (fleet: DroneClass) (sectors: FireSector[]) (events: TimedEvent list) : string option =
        let ignitions =
            events
            |> List.exists (fun e ->
                match e.Event with
                | SpotFire _ -> true
                | _ -> false)

        match demand with
        | Fire _ when fleet.PayloadL <= 0.0 -> Some "a fire demand needs a fleet with payload_l > 0"
        | Fire _ when not (sectors |> Array.exists (fun s -> s.InitialIntensity > 0.0)) && not ignitions ->
            Some "a fire demand needs a sector with initial_intensity > 0 or a spot_fire event; nothing would burn"
        | Fire _ when
            events
            |> List.exists (fun e ->
                match e.Event with
                | SpotFire(_, v) -> v > 1.0
                | _ -> false)
            ->
            Some "a spot_fire intensity must be in (0, 1]"
        | Touches _ when not (sectors |> Array.exists (fun s -> s.Touches > 0)) && not ignitions ->
            Some "a touch demand needs a target with touches > 0 or a retouch event"
        | _ -> None

    /// Event targets that name no source or target.
    let unknownEventTargets (sources: WaterSource[]) (sectors: FireSector[]) (events: TimedEvent list) =
        let sourceIds = sources |> Array.map (fun s -> s.Id) |> Set.ofArray
        let sectorIds = sectors |> Array.map (fun s -> s.Id) |> Set.ofArray

        events
        |> List.choose (fun e ->
            match e.Event with
            | CloseSource id
            | OpenSource id when not (sourceIds.Contains id) -> Some(sprintf "t=%d: no source '%s'" e.At id)
            | SpotFire(id, _) when not (sectorIds.Contains id) -> Some(sprintf "t=%d: no target '%s'" e.At id)
            | _ -> None)

    let initial (demand: Demand) (sectors: FireSector[]) : TargetState =
        match demand with
        | Fire _ -> FireState(FireSim.initial sectors)
        | Touches _ -> TouchState(sectors |> Array.map (fun s -> float s.Touches))

    let private mismatch () =
        invalidOp "demand model and target state do not match"

    let needs
        (demand: Demand)
        (tick: Tick)
        (cond: Conditions)
        (sectors: FireSector[])
        (index: Map<string, int>)
        (state: TargetState)
        : SectorNeed[] =
        match demand, state with
        | Fire phys, FireState fire -> FireSim.needs phys tick cond sectors index fire
        | Touches rules, TouchState left ->
            let targetTicks = max 1.0 (rules.TargetS / Tick.seconds tick)

            sectors
            |> Array.mapi (fun j s ->
                if left.[j] > 1e-9 then
                    // A drop is a whole thing: a target owed 0.4 of a touch still
                    // needs one more drop, so its demand does not fade as the
                    // flow model's fractional deliveries accumulate. Otherwise a
                    // half-served target loses to a fresh one and the plan thrashes.
                    {
                        DemandPerTick = Math.Ceiling(left.[j] - 1e-9) / targetTicks
                        Weight = s.AssetPriority
                        Concentrated = false
                    }
                else
                    {
                        DemandPerTick = 0.0
                        Weight = 0.0
                        Concentrated = false
                    })
        | _ -> mismatch ()

    /// Advance the targets one tick given the units that landed on each.
    let step
        (demand: Demand)
        (tick: Tick)
        (cond: Conditions)
        (sectors: FireSector[])
        (index: Map<string, int>)
        (state: TargetState)
        (delivered: float[])
        : TargetState =
        match demand, state with
        | Fire phys, FireState fire -> FireState(FireSim.step phys tick cond sectors index fire delivered)
        | Touches _, TouchState left -> TouchState(Array.map2 (fun l d -> max 0.0 (l - d)) left delivered)
        | _ -> mismatch ()

    /// A scenario event at target j: a spot fire of the given intensity, or
    /// that many more touches owed.
    let disturb (demand: Demand) (state: TargetState) (j: int) (value: float) : TargetState =
        match demand, state with
        | Fire _, FireState fire -> FireState(FireSim.spotFire fire j value)
        | Touches _, TouchState left -> TouchState(left |> Array.mapi (fun i l -> if i = j then l + max 0.0 value else l))
        | _ -> mismatch ()

    /// The headline metric now: burning ha, or touches still owed.
    let active (demand: Demand) (sectors: FireSector[]) (state: TargetState) : float =
        match demand, state with
        | Fire _, FireState fire -> FireSim.activeFireHa sectors fire
        | Touches _, TouchState left -> Array.sum left
        | _ -> mismatch ()

    /// Per target, in [0, 1]: fire intensity, or the share of touches still owed.
    let progress (demand: Demand) (sectors: FireSector[]) (state: TargetState) : float[] =
        match demand, state with
        | Fire _, FireState fire -> fire |> Array.map (fun (f: FireSim.SectorState) -> f.Intensity)
        | Touches _, TouchState left -> Array.map2 (fun (s: FireSector) l -> l / float (max 1 s.Touches)) sectors left
        | _ -> mismatch ()

    /// Units each target still owes, for sizing a sortie: touches left, or
    /// unbounded for a fire, which wants water for as long as it burns.
    let remaining (demand: Demand) (state: TargetState) : float[] =
        match demand, state with
        | Fire _, FireState fire -> fire |> Array.map (fun _ -> Double.PositiveInfinity)
        | Touches _, TouchState left -> Array.copy left
        | _ -> mismatch ()

    /// Targets that still want something: the ones a corridor is worth opening to.
    let wanted (demand: Demand) (sectors: FireSector[]) (state: TargetState) : Set<string> =
        match demand, state with
        | Fire _, FireState fire ->
            Array.map2
                (fun (s: FireSector) (f: FireSim.SectorState) -> if f.Intensity > 0.0 then Some s.Id else None)
                sectors
                fire
            |> Array.choose id
            |> Set.ofArray
        | Touches _, TouchState left ->
            Array.map2 (fun (s: FireSector) l -> if l > 1e-9 then Some s.Id else None) sectors left
            |> Array.choose id
            |> Set.ofArray
        | _ -> mismatch ()

    /// What the run leaves behind: sectors that caught fire during the run,
    /// or targets still owed touches at the end.
    let unresolved (demand: Demand) (sectors: FireSector[]) (state: TargetState) : string list =
        match demand, state with
        | Fire _, FireState fire ->
            Array.map2
                (fun (s: FireSector) (f: FireSim.SectorState) ->
                    if f.EverIgnited && s.InitialIntensity = 0.0 then Some s.Id else None)
                sectors
                fire
            |> Array.choose id
            |> List.ofArray
        | Touches _, TouchState left ->
            Array.map2 (fun (s: FireSector) l -> if l > 1e-9 then Some s.Id else None) sectors left
            |> Array.choose id
            |> List.ofArray
        | _ -> mismatch ()
