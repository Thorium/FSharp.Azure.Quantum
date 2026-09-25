/// The air bridge as a flow network.
///
/// A drone is not a node and not a path: the CORRIDOR (lake -> fire sector and
/// back) is the planned entity, and drones are units of flow circulating on it.
/// Little's law ties them together:
///
///     drones on a corridor = throughput (drones/min) * cycle time (min)
///
/// and the throughput is capped by the narrowest part of the pipe: the lake's
/// fill slots, the lane's in-trail spacing, or the drop slots at the fire.
/// Past that cap, extra drones deliver nothing; you need another corridor.
///
/// This module owns the two classical pieces of the planner:
/// - `evaluate` is the FAST LOOP. Given the open corridors it spreads the fleet
///   over them each minute (milliseconds, no quantum involved). Its score is
///   also the ground truth every candidate plan is judged by.
/// - `greedyPlan` / `exactPlan` are the classical baselines for the SLOW LOOP
///   (which corridors to open), used as the instant fallback and as the oracle
///   the quantum planner is measured against.
module FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.AirBridge

open System

open FSharp.Azure.Quantum.Examples.Drones.Domain
open FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.FireDomain

// =============================================================================
// CONDITIONS AND CORRIDORS
// =============================================================================

/// Operating conditions that change during the mission.
type Conditions =
    {
        WindToDeg: float
        WindSpeedMs: float
        ClosedSources: Set<string>
        FleetSize: int
    }

/// What a sector needs from the air bridge right now (set by the fire model).
type SectorNeed =
    {
        /// Water rate that would knock the sector down (or keep it wet), L/min.
        DemandLpm: float
        /// Value of one litre delivered there.
        Weight: float
        /// Suppression only works concentrated: many small drops spread over
        /// minutes mostly evaporate. Pre-wetting unburnt fuel has no such floor.
        Concentrated: bool
    }

/// One lake -> sector corridor with its outbound and return lanes.
type Corridor =
    {
        Id: string
        SourceIdx: int
        SectorIdx: int
        Source: WaterSource
        Sector: FireSector
        DistanceKm: float
        OutboundS: float
        ReturnS: float
        /// fill + outbound + drop + return, seconds.
        CycleS: float
        CyclesPerBattery: int
        /// Share of assigned drones flying rather than swapping batteries.
        Availability: float
        /// Minutes from opening the corridor until its first drop lands.
        WarmupMin: float
        /// Headway limit of the outbound lane, drones/min.
        LaneCapPerMin: float
        /// Drones/min one assigned drone contributes (Little's law, incl. swaps).
        RatePerDrone: float
    }

module Corridors =

    let private minGroundSpeedMs = 2.0

    /// Build one corridor, or None when the wind or endurance makes it unflyable.
    let tryBuild
        (fleet: DroneClass)
        (cond: Conditions)
        (sourceIdx: int, source: WaterSource)
        (sectorIdx: int, sector: FireSector)
        : Corridor option =
        let distanceKm = Geometry.distanceKm source.Pos sector.Pos
        let along = Geometry.unit source.Pos sector.Pos

        // Tailwind on the way out is a headwind on the way back.
        let windAlong =
            cond.WindSpeedMs * Geometry.dot (Geometry.bearing cond.WindToDeg) along

        let outboundGs = fleet.LoadedSpeedMs + windAlong
        let returnGs = fleet.EmptySpeedMs - windAlong

        if
            cond.WindSpeedMs > Safety.missionAbortWindSpeedMs
            || outboundGs < minGroundSpeedMs
            || returnGs < minGroundSpeedMs
        then
            None
        else
            let outboundS = distanceKm * 1000.0 / outboundGs
            let returnS = distanceKm * 1000.0 / returnGs
            let cycleS = source.FillTimeS + outboundS + fleet.DropTimeS + returnS

            let usableS =
                fleet.EnduranceMin * 60.0 * (1.0 - Battery.reserveBatteryPercent / 100.0)

            let cycles = int (Math.Floor(usableS / cycleS))

            if cycles < 1 then
                None
            else
                // Batteries are swapped at the lake by the ground crew.
                let flyingS = float cycles * cycleS
                let availability = flyingS / (flyingS + Scheduling.batterySwapTimeMin * 60.0)

                Some
                    {
                        Id = sprintf "%s>%s" source.Id sector.Id
                        SourceIdx = sourceIdx
                        SectorIdx = sectorIdx
                        Source = source
                        Sector = sector
                        DistanceKm = distanceKm
                        OutboundS = outboundS
                        ReturnS = returnS
                        CycleS = cycleS
                        CyclesPerBattery = cycles
                        Availability = availability
                        WarmupMin = (source.FillTimeS + outboundS + fleet.DropTimeS) / 60.0
                        LaneCapPerMin = 60.0 * outboundGs / fleet.LaneSpacingM
                        RatePerDrone = availability * 60.0 / cycleS
                    }

    /// Every flyable corridor from an open source to any sector.
    let buildAll (fleet: DroneClass) (cond: Conditions) (sources: WaterSource[]) (sectors: FireSector[]) =
        [|
            for si, s in Array.indexed sources do
                if not (cond.ClosedSources.Contains s.Id) then
                    for fi, f in Array.indexed sectors do
                        match tryBuild fleet cond (si, s) (fi, f) with
                        | Some c -> c
                        | None -> ()
        |]

    /// Source id of a corridor id ("NLAKE>S2" -> "NLAKE"); see `tryBuild`.
    let sourceIdOf (corridorId: string) =
        corridorId.Substring(0, corridorId.IndexOf '>')

    /// Two lanes conflict when they cross, overlap or pass within one lane
    /// spacing of each other anywhere. Lanes that share a lake or a drop zone
    /// meet there by design; the terminal-area check sequences those instead.
    let cross (spacingM: float) (a: Corridor) (b: Corridor) =
        a.Source.Id <> b.Source.Id
        && a.Sector.Id <> b.Sector.Id
        && Geometry.segmentDistanceKm a.Source.Pos a.Sector.Pos b.Source.Pos b.Sector.Pos
           * 1000.0
            <
            spacingM

// =============================================================================
// FAST LOOP: SPREAD THE FLEET OVER THE OPEN CORRIDORS
// =============================================================================

/// Scarce people on the ground. They, not the drones, bound how many
/// corridors can fly, and they are what makes corridor choice combinatorial:
/// without them "open every useful corridor" would always be optimal.
type Limits =
    {
        /// Ground crews; every lake in use needs one (fills and battery swaps).
        Crews: int
        /// Drop coordinators; every open corridor needs one on the fire line.
        Coordinators: int
        /// Minutes for a crew to relocate to a lake it is not already at.
        CrewMoveMin: float
    }

/// Everything the evaluator needs, precomputed once per planning round so that
/// scoring a plan is a single pass (the exact oracle scores 2^K plans).
type EvalContext =
    {
        Corridors: Corridor[]
        PayloadL: float
        Fleet: float
        HorizonMin: float
        Limits: Limits
        SourceCount: int
        /// Weight of the water each corridor delivers, per drone, over the horizon.
        ValuePerDrone: float[]
        /// Corridor indices, best value per drone first.
        Order: int[]
        SourceCapPerMin: float[]
        DropCapPerMin: float[]
        /// Sector demand in drones/min (DemandLpm / payload).
        DemandPerMin: float[]
        /// Least drops/min that do anything on a concentrated sector (0 = no floor).
        FloorPerMin: float[]
        /// Pairs of corridors whose lanes cross; each open pair costs CrossPenalty.
        CrossPairs: (int * int)[]
        CrossPenalty: float
    }

type Allocation =
    {
        /// Drones assigned per corridor (fractional; rounded when reported).
        Drones: float[]
        /// Drops per minute per corridor.
        Throughput: float[]
        Score: float
        Crossings: int
    }

module Evaluate =

    /// Share of the horizon a corridor delivers. An already-open corridor
    /// delivers throughout; a new one only once its pipe has filled, and later
    /// still if a crew must first relocate to its lake. This is the real cost
    /// of changing the plan, so re-planning cannot thrash for free.
    let private timeFactor
        (horizonMin: float)
        (limits: Limits)
        (alreadyOpen: Set<string>)
        (activeLakes: Set<string>)
        (c: Corridor)
        =
        if alreadyOpen.Contains c.Id then
            1.0
        else
            let crewDelay =
                if activeLakes.Contains c.Source.Id then
                    0.0
                else
                    limits.CrewMoveMin

            max 0.0 (horizonMin - c.WarmupMin - crewDelay) / horizonMin

    let context
        (fleet: DroneClass)
        (fleetSize: int)
        (sources: WaterSource[])
        (sectors: FireSector[])
        (needs: SectorNeed[])
        (alreadyOpen: Set<string>)
        (activeLakes: Set<string>)
        (horizonMin: float)
        (crossPenalty: float)
        (limits: Limits)
        (minSalvoLpm: float)
        (corridors: Corridor[])
        : EvalContext =
        let valuePerDrone =
            corridors
            |> Array.map (fun c ->
                needs.[c.SectorIdx].Weight
                * fleet.PayloadL
                * c.RatePerDrone
                * timeFactor horizonMin limits alreadyOpen activeLakes c
                * horizonMin)

        let order =
            Array.init corridors.Length id
            |> Array.sortByDescending (fun i -> valuePerDrone.[i])

        let crossPairs =
            [|
                for i in 0 .. corridors.Length - 1 do
                    for j in i + 1 .. corridors.Length - 1 do
                        if Corridors.cross fleet.LaneSpacingM corridors.[i] corridors.[j] then
                            (i, j)
            |]

        {
            Corridors = corridors
            PayloadL = fleet.PayloadL
            Fleet = float fleetSize
            HorizonMin = horizonMin
            Limits = limits
            SourceCount = sources.Length
            ValuePerDrone = valuePerDrone
            Order = order
            SourceCapPerMin = sources |> Array.map (fun s -> float s.FillSlots * 60.0 / s.FillTimeS)
            DropCapPerMin = sectors |> Array.map (fun f -> float f.DropSlots * 60.0 / fleet.DropTimeS)
            DemandPerMin = needs |> Array.map (fun n -> n.DemandLpm / fleet.PayloadL)
            FloorPerMin =
                needs
                |> Array.map (fun n ->
                    if n.Concentrated then
                        min minSalvoLpm n.DemandLpm / fleet.PayloadL
                    else
                        0.0)
            CrossPairs = crossPairs
            CrossPenalty = crossPenalty
        }

    /// Does the plan fit the crews and coordinators, with no two lanes in
    /// conflict? Conflicting lanes are refused outright rather than stacked
    /// in altitude layers: a failing aircraft descends straight down, and
    /// over a crossing it would fall through the lower lane.
    let private withinLimits (ctx: EvalContext) (isOpen: int -> bool) =
        let lakes = Array.zeroCreate ctx.SourceCount
        let mutable corridors = 0

        for i in 0 .. ctx.Corridors.Length - 1 do
            if isOpen i then
                corridors <- corridors + 1
                lakes.[ctx.Corridors.[i].SourceIdx] <- true

        corridors <= ctx.Limits.Coordinators
        && (lakes |> Array.filter id |> Array.length) <= ctx.Limits.Crews
        && ctx.CrossPairs |> Array.forall (fun (a, b) -> not (isOpen a && isOpen b))

    /// One allocation pass. Greedy by value per drone: each corridor takes
    /// drones until its lane, lake, drop zone or the sector's demand saturates,
    /// or the fleet runs out. Sectors in `skip` get nothing.
    let private pass
        (ctx: EvalContext)
        (isOpen: int -> bool)
        (skip: bool[])
        (drones: float[])
        (throughput: float[])
        (sectorRate: float[])
        =
        let srcLeft = Array.copy ctx.SourceCapPerMin
        let dropLeft = Array.copy ctx.DropCapPerMin
        let demandLeft = Array.copy ctx.DemandPerMin
        let mutable fleetLeft = ctx.Fleet
        let mutable score = 0.0
        Array.fill drones 0 drones.Length 0.0
        Array.fill throughput 0 throughput.Length 0.0
        Array.fill sectorRate 0 sectorRate.Length 0.0

        for i in ctx.Order do
            let c = ctx.Corridors.[i]

            if
                isOpen i
                && not skip.[c.SectorIdx]
                && fleetLeft > 1e-9
                && ctx.ValuePerDrone.[i] > 0.0
            then
                let cap =
                    min
                        (min c.LaneCapPerMin srcLeft.[c.SourceIdx])
                        (min dropLeft.[c.SectorIdx] demandLeft.[c.SectorIdx])

                if cap > 1e-9 then
                    let n = min fleetLeft (cap / c.RatePerDrone)
                    let th = n * c.RatePerDrone
                    fleetLeft <- fleetLeft - n
                    srcLeft.[c.SourceIdx] <- srcLeft.[c.SourceIdx] - th
                    dropLeft.[c.SectorIdx] <- dropLeft.[c.SectorIdx] - th
                    demandLeft.[c.SectorIdx] <- demandLeft.[c.SectorIdx] - th
                    score <- score + n * ctx.ValuePerDrone.[i]
                    drones.[i] <- n
                    throughput.[i] <- th
                    sectorRate.[c.SectorIdx] <- sectorRate.[c.SectorIdx] + th

        score

    /// Allocation with the concentration floor. A concentrated sector that
    /// would get less than its floor is abandoned and its drones go elsewhere,
    /// repeatedly, the way a commander stops dribbling water on a lost cause.
    /// The floor makes the objective non-additive: two corridors into one
    /// sector can be worth far more than twice one.
    let private run (ctx: EvalContext) (isOpen: int -> bool) =
        let drones = Array.zeroCreate ctx.Corridors.Length
        let throughput = Array.zeroCreate ctx.Corridors.Length
        let sectorRate = Array.zeroCreate ctx.DemandPerMin.Length
        let skip = Array.zeroCreate ctx.DemandPerMin.Length

        let rec settle () =
            let score = pass ctx isOpen skip drones throughput sectorRate

            let thin =
                sectorRate
                |> Array.indexed
                |> Array.filter (fun (j, r) -> r > 1e-9 && r < ctx.FloorPerMin.[j] - 1e-9)

            if thin.Length = 0 then
                score
            else
                skip.[thin |> Array.minBy snd |> fst] <- true
                settle ()

        let score = settle ()
        let mutable crossings = 0

        for (a, b) in ctx.CrossPairs do
            if isOpen a && isOpen b then
                crossings <- crossings + 1

        {
            Drones = drones
            Throughput = throughput
            Score = score - float crossings * ctx.CrossPenalty
            Crossings = crossings
        }

    /// Fast-loop allocation for a plan that is already flying.
    let allocate (ctx: EvalContext) (plan: bool[]) : Allocation = run ctx (fun i -> plan.[i])

    let private scoreWith (ctx: EvalContext) (isOpen: int -> bool) =
        if withinLimits ctx isOpen then
            (run ctx isOpen).Score
        else
            Double.NegativeInfinity

    /// Score of a candidate plan; -infinity when it needs more crews or
    /// coordinators than there are.
    let score (ctx: EvalContext) (plan: bool[]) : float = scoreWith ctx (fun i -> plan.[i])

    let scoreMask (ctx: EvalContext) (mask: int) : float =
        scoreWith ctx (fun i -> (mask >>> i) &&& 1 = 1)

    /// Score ignoring crews and coordinators; the QUBO carries those as
    /// separate penalty terms.
    let scoreRelaxed (ctx: EvalContext) (plan: bool[]) : float = (run ctx (fun i -> plan.[i])).Score

// =============================================================================
// SLOW LOOP, CLASSICAL: WHICH CORRIDORS TO OPEN
// =============================================================================

module ClassicalPlanner =

    /// Add the single best corridor while it improves the score. Instant, and
    /// myopic: its first pick sends a crew to the lake of the best single
    /// corridor, and it cannot see that a different pair of lakes would have
    /// served the whole fire better once the crews are all committed.
    let greedyPlan (ctx: EvalContext) : bool[] =
        let plan = Array.zeroCreate ctx.Corridors.Length

        let rec loop current =
            let best =
                [ 0 .. plan.Length - 1 ]
                |> List.filter (fun i -> not plan.[i])
                |> List.map (fun i ->
                    plan.[i] <- true
                    let s = Evaluate.score ctx plan
                    plan.[i] <- false
                    (i, s))
                |> List.sortByDescending snd
                |> List.tryHead

            match best with
            | Some(i, s) when s > current + 1e-9 ->
                plan.[i] <- true
                loop s
            | _ -> ()

        loop (Evaluate.score ctx plan)
        plan

    /// Brute force over all 2^K plans. Only an oracle for measuring the other
    /// planners at simulable sizes; at a real incident's scale it is hopeless.
    let exactPlan (ctx: EvalContext) : bool[] =
        let k = ctx.Corridors.Length
        let mutable bestMask = 0
        let mutable best = Evaluate.scoreMask ctx 0

        for mask in 1 .. (1 <<< k) - 1 do
            let s = Evaluate.scoreMask ctx mask

            if s > best then
                best <- s
                bestMask <- mask

        Array.init k (fun i -> (bestMask >>> i) &&& 1 = 1)

// =============================================================================
// LANES: ALTITUDE LAYERS FOR CROSSING CORRIDORS
// =============================================================================

module Lanes =

    let private baseAltitudeM = 40.0
    let private layerStepM = 20.0

    /// Greedy colouring of the crossing graph: corridors that cross get
    /// different altitude layers; the return lane flies 10 m above the outbound.
    let assignLayers (spacingM: float) (corridors: Corridor list) : (Corridor * float * float) list =
        corridors
        |> List.fold
            (fun (assigned: (Corridor * int) list) c ->
                let taken =
                    assigned
                    |> List.filter (fun (o, _) -> Corridors.cross spacingM o c)
                    |> List.map snd
                    |> Set.ofList

                let layer = Seq.initInfinite id |> Seq.find (fun l -> not (taken.Contains l))
                assigned @ [ (c, layer) ])
            []
        |> List.map (fun (c, layer) ->
            let outbound = baseAltitudeM + float layer * layerStepM
            (c, outbound, outbound + 10.0))
