/// Forest-Fire Air Bridge Example
///
/// Drones shuttle water from lakes to fire hotspots: night or smoke mop-up
/// while helicopters are grounded, not front suppression (a bucket helicopter
/// moves tens of times the water of this whole fleet). The planned entity is
/// the CORRIDOR (lake -> sector -> lake), not the drone: drones are flow on it,
/// and Little's law gives how many a corridor can use before its lake, lane or
/// drop zone saturates.
///
/// What makes corridor choice hard is not the drones but the people and the
/// physics: each lake in use needs a ground crew (fills, battery swaps) and
/// moving one takes minutes; each corridor needs a drop coordinator; and
/// water only suppresses when it arrives concentrated, so drones must be
/// massed on a few hotspots rather than spread thin.
///
/// TWO LOOPS:
/// - Fast loop (every simulated minute, classical, milliseconds): spread the
///   fleet over the open corridors as demand shifts; the drop point moves
///   along the front while the corridors stay put.
/// - Slow loop (every few minutes and on every event, quantum): decide which
///   corridors to open. One qubit per candidate corridor; QAOA re-plans on
///   wind shifts, smoke-closed lakes, spot fires and lost drones, warm-started
///   from the previous round's angles.
///
/// ANYTIME PLANNING: a quantum answer is never waited for. At a re-plan the
/// instant classical greedy plan is adopted if it beats the plan in the air;
/// the QAOA plan arrives after a latency, is re-scored against conditions AT
/// ARRIVAL, and replaces the current plan only if it is still better.
///
/// The same scenario runs under four policies so the effect of adapting, and
/// of the quantum step, is measured rather than assumed:
///   static          plan once at t=0 (best available), never re-plan
///   adaptive-greedy re-plan with the classical greedy only
///   adaptive-exact  re-plan with brute force (oracle, only at small sizes)
///   adaptive-hybrid greedy now, QAOA after the latency (the proposed design)
///
/// It also writes the evidence a 1:N (one pilot, many aircraft) permission
/// asks for: deconfliction, range, altitude ceiling, drop-out behaviour.
namespace FSharp.Azure.Quantum.Examples.Drones.FireAirBridge

open System
open System.Diagnostics
open System.IO

open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

open FSharp.Azure.Quantum.Examples.Common
open FSharp.Azure.Quantum.Examples.Drones.Domain
open FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.FireDomain
open FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.AirBridge
open FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.FireSim
open FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.QuantumPlanner

// =============================================================================
// SETTINGS AND RECORDS
// =============================================================================

type Policy =
    | Static
    | AdaptiveGreedy
    | AdaptiveExact
    | AdaptiveHybrid

module Policy =
    let name =
        function
        | Static -> "static"
        | AdaptiveGreedy -> "adaptive-greedy"
        | AdaptiveExact -> "adaptive-exact"
        | AdaptiveHybrid -> "adaptive-hybrid"

    let all = [ Static; AdaptiveGreedy; AdaptiveExact; AdaptiveHybrid ]

    let tryParse (s: string) =
        all |> List.tryFind (fun p -> name p = s.ToLowerInvariant())

type Settings =
    {
        Minutes: int
        ReplanEvery: int
        HorizonMin: float
        MaxCorridors: int
        ExactLimit: int
        LatencyMin: int
        CrossPenalty: float
        Limits: Limits
        Qaoa: QaoaSettings
        Physics: FirePhysics
    }

type Scenario =
    {
        Sources: WaterSource[]
        Sectors: FireSector[]
        SectorIndex: Map<string, int>
        Fleet: DroneClass
        Events: TimedEvent list
    }

/// One slow-loop decision: a re-plan, or a QAOA answer arriving.
type Decision =
    {
        Policy: string
        Minute: int
        Reason: string
        Candidates: int
        IncumbentScore: float
        GreedyScore: float option
        ExactScore: float option
        QuantumScore: float option
        Adopted: string
        Plan: string
        Qubits: int
        QuantumCircuits: int
        WarmStarted: bool
        QuantumMs: int64
    }

/// One corridor as flown in one minute: the permission evidence is measured
/// over every configuration that actually flew, not only the final one.
type LaneSnapshot =
    {
        Corridor: Corridor
        OutboundAltM: float
        ReturnAltM: float
        /// Drones assigned (flying or swapping batteries).
        Drones: float
        /// Drops per minute vs. the lane's headway capacity.
        LaneLoad: float
        /// The corridor left the plan and its drones are finishing their cycle.
        Draining: bool
    }

type MinuteRecord =
    {
        Minute: int
        ActiveFireHa: float
        DeliveredL: float
        DronesFlying: float
        DronesIdle: float
        OpenCorridors: string
        Intensities: float[]
        Lanes: LaneSnapshot list
        /// Corridors still planned but no longer flyable, with drones on them.
        Stranded: (string * float) list
    }

type PolicyResult =
    {
        Policy: Policy
        Timeline: MinuteRecord list
        Decisions: Decision list
        /// The last corridors that flew (the fire may be out by the end).
        LastBridge: Corridor list
        NewIgnitions: string list
    }

type SimState =
    {
        Cond: Conditions
        Fire: SectorState[]
        Plan: Set<string>
        /// Lakes the ground crews are at (or heading to) and when they are ready.
        CrewsAt: Set<string>
        CrewReadyAt: Map<string, float>
        /// Minute each corridor last started flying (its pipe starts filling).
        OpenedAt: Map<string, int>
        Flying: Set<string>
        /// The most recent non-empty set of flying corridors (for the lane report).
        LastBridge: Corridor list
        /// Last minute's lanes, and lanes still draining (until minute).
        LastLanes: LaneSnapshot list
        Draining: (LaneSnapshot * int) list
        /// QAOA answer in transit: (due minute, plan, asked at).
        Pending: (int * Set<string> * int) option
        WarmParams: (float * float)[] option
    }

// =============================================================================
// PLANNING HELPERS
// =============================================================================

module Planning =

    let private planIds (ctx: EvalContext) (mask: bool[]) =
        ctx.Corridors
        |> Array.indexed
        |> Array.choose (fun (i, c) -> if mask.[i] then Some c.Id else None)
        |> Set.ofArray

    let private maskOf (ctx: EvalContext) (plan: Set<string>) =
        ctx.Corridors |> Array.map (fun c -> plan.Contains c.Id)

    let describe (plan: Set<string>) =
        if plan.IsEmpty then "(none)" else String.Join(" ", plan)

    let private context (sc: Scenario) (st: Settings) (s: SimState) (needs: SectorNeed[]) corridors =
        Evaluate.context
            sc.Fleet
            s.Cond.FleetSize
            sc.Sources
            sc.Sectors
            needs
            s.Plan
            s.CrewsAt
            st.HorizonMin
            st.CrossPenalty
            st.Limits
            st.Physics.MinSalvoLpm
            corridors

    /// Switch to a new plan. Crews go to the lakes it uses; a crew sent to a
    /// lake it is not at is ready only after the relocation time.
    let setPlan (st: Settings) (minute: int) (plan: Set<string>) (s: SimState) =
        let lakes = plan |> Set.map Corridors.sourceIdOf
        let ready = float minute + st.Limits.CrewMoveMin

        { s with
            Plan = plan
            CrewsAt = lakes
            CrewReadyAt =
                (lakes - s.CrewsAt)
                |> Set.fold (fun (m: Map<string, float>) lake -> m.Add(lake, ready)) s.CrewReadyAt
        }

    /// Candidate corridors for the slow loop: flyable, worth something on their
    /// own, the open ones kept first, the rest by solo value, capped at the
    /// qubit budget. The cap is a classical pre-pass; it bounds the QUBO size.
    let candidates (sc: Scenario) (st: Settings) (s: SimState) (needs: SectorNeed[]) =
        let all = Corridors.buildAll sc.Fleet s.Cond sc.Sources sc.Sectors
        let plan = s.Plan

        // Rank without the concentration floor: a corridor too weak to reach a
        // sector's floor alone can still be half of a pair that does.
        let ctx =
            Evaluate.context
                sc.Fleet
                s.Cond.FleetSize
                sc.Sources
                sc.Sectors
                needs
                plan
                s.CrewsAt
                st.HorizonMin
                st.CrossPenalty
                st.Limits
                0.0
                all

        all
        |> Array.mapi (fun i c -> (c, Evaluate.score ctx (Array.init all.Length ((=) i))))
        |> Array.filter (fun (_, v) -> v > 0.0)
        |> Array.sortBy (fun (c, v) -> ((if plan.Contains c.Id then 0 else 1), -v))
        |> Array.truncate st.MaxCorridors
        |> Array.map fst

    /// Run the slow loop for one policy. Returns the new state and its log rows.
    let replan
        (backend: IQuantumBackend)
        (sc: Scenario)
        (st: Settings)
        (policy: Policy)
        (minute: int)
        (reason: string)
        (s: SimState)
        : SimState * Decision list =
        let needs = FireSim.needs st.Physics s.Cond sc.Sectors sc.SectorIndex s.Fire
        let cands = candidates sc st s needs
        let ctx = context sc st s needs cands
        let score = Evaluate.score ctx
        let incumbent = maskOf ctx s.Plan
        let greedy = ClassicalPlanner.greedyPlan ctx

        let exact =
            if cands.Length <= st.ExactLimit then
                Some(ClassicalPlanner.exactPlan ctx)
            else
                None

        let incScore, greedyScore = score incumbent, score greedy

        let row adopted plan =
            {
                Policy = Policy.name policy
                Minute = minute
                Reason = reason
                Candidates = cands.Length
                IncumbentScore = incScore
                GreedyScore = Some greedyScore
                ExactScore = exact |> Option.map score
                QuantumScore = None
                Adopted = adopted
                Plan = describe plan
                Qubits = 0
                QuantumCircuits = 0
                WarmStarted = false
                QuantumMs = 0L
            }

        // Take a classical plan if it beats what is flying.
        let takeIfBetter name (mask: bool[]) =
            if score mask > incScore + 1e-9 then
                (name, planIds ctx mask)
            else
                ("kept", planIds ctx incumbent)

        match policy with
        | Static
        | AdaptiveExact ->
            let adopted, plan =
                takeIfBetter (if exact.IsSome then "exact" else "greedy") (defaultArg exact greedy)

            (setPlan st minute plan s, [ row adopted plan ])
        | AdaptiveGreedy ->
            let adopted, plan = takeIfBetter "greedy" greedy
            (setPlan st minute plan s, [ row adopted plan ])
        | AdaptiveHybrid when cands.Length = 0 -> (setPlan st minute Set.empty s, [ row "nothing to fly" Set.empty ])
        | AdaptiveHybrid ->
            let adopted, plan = takeIfBetter "greedy" greedy
            let s' = setPlan st minute plan s

            match QuantumPlanner.solve backend st.Qaoa s.WarmParams ctx with
            | Ok q ->
                // The answer cannot arrive before it was computed: the measured
                // wall time is a floor under the configured latency (queueing).
                let computeMin = int (Math.Ceiling(float q.ElapsedMs / 60000.0))

                ({ s' with
                    Pending = Some(minute + max st.LatencyMin computeMin, planIds ctx q.Plan, minute)
                    WarmParams = Some q.Parameters
                 },
                 [
                     { row adopted plan with
                         QuantumScore = Some(score q.Plan)
                         Qubits = q.Qubits
                         QuantumCircuits = q.Circuits
                         WarmStarted = q.WarmStarted
                         QuantumMs = q.ElapsedMs
                     }
                 ])
            | Error e ->
                (s',
                 [
                     { row adopted plan with
                         Reason = reason + " (QAOA failed: " + e.Message + ")"
                     }
                 ])

    /// A QAOA answer arrives: re-score it against the conditions NOW (the fire
    /// kept moving during the latency) and adopt it only if it still wins.
    let adoptPending
        (sc: Scenario)
        (st: Settings)
        (policy: Policy)
        (minute: int)
        (s: SimState)
        : SimState * Decision list =
        match s.Pending with
        | Some(due, quantumPlan, askedAt) when due <= minute ->
            let needs = FireSim.needs st.Physics s.Cond sc.Sectors sc.SectorIndex s.Fire

            let corridors =
                Corridors.buildAll sc.Fleet s.Cond sc.Sources sc.Sectors
                |> Array.filter (fun c -> s.Plan.Contains c.Id || quantumPlan.Contains c.Id)

            let ctx = context sc st s needs corridors
            let current = Evaluate.score ctx (maskOf ctx s.Plan)
            let arrived = Evaluate.score ctx (maskOf ctx quantumPlan)
            let adopt = arrived > current + 1e-9

            let plan =
                if adopt then
                    planIds ctx (maskOf ctx quantumPlan)
                else
                    s.Plan

            let verdict =
                if adopt then "quantum"
                elif quantumPlan = s.Plan then "same plan"
                else "kept"

            ({ setPlan st minute plan s with
                Pending = None
             },
             [
                 {
                     Policy = Policy.name policy
                     Minute = minute
                     Reason = sprintf "QAOA answer asked at t=%d arrives" askedAt
                     Candidates = corridors.Length
                     IncumbentScore = current
                     GreedyScore = None
                     ExactScore = None
                     QuantumScore = Some arrived
                     Adopted = verdict
                     Plan = describe plan
                     Qubits = 0
                     QuantumCircuits = 0
                     WarmStarted = false
                     QuantumMs = 0L
                 }
             ])
        | _ -> (s, [])

// =============================================================================
// SIMULATION
// =============================================================================

module Simulation =

    let private describeEvent (sc: Scenario) =
        function
        | WindDirection deg -> sprintf "wind now blowing toward %.0f deg" deg
        | WindSpeed ms -> sprintf "wind %.0f m/s" ms
        | CloseSource id -> sprintf "smoke closes %s" id
        | OpenSource id -> sprintf "%s usable again" id
        | SpotFire(id, i) -> sprintf "spot fire in %s (%.2f)" id i
        | LoseDrones n -> sprintf "%d drones lost" n

    let private apply (sc: Scenario) (s: SimState) (event: ScenarioEvent) =
        match event with
        | WindDirection deg ->
            { s with
                Cond = { s.Cond with WindToDeg = deg }
            }
        | WindSpeed ms ->
            { s with
                Cond = { s.Cond with WindSpeedMs = ms }
            }
        | CloseSource id ->
            { s with
                Cond =
                    { s.Cond with
                        ClosedSources = s.Cond.ClosedSources.Add id
                    }
            }
        | OpenSource id ->
            { s with
                Cond =
                    { s.Cond with
                        ClosedSources = s.Cond.ClosedSources.Remove id
                    }
            }
        | SpotFire(id, i) ->
            match sc.SectorIndex.TryFind id with
            | Some j ->
                { s with
                    Fire = FireSim.spotFire s.Fire j i
                }
            | None -> s
        | LoseDrones n ->
            { s with
                Cond =
                    { s.Cond with
                        FleetSize = max 0 (s.Cond.FleetSize - n)
                    }
            }

    /// Fast loop: spread the fleet over the flyable open corridors and land
    /// water in each sector (a newly flying corridor delivers after warm-up).
    let private fly (sc: Scenario) (st: Settings) (minute: int) (s: SimState) =
        let needs = FireSim.needs st.Physics s.Cond sc.Sectors sc.SectorIndex s.Fire

        let open' =
            Corridors.buildAll sc.Fleet s.Cond sc.Sources sc.Sectors
            |> Array.filter (fun c -> s.Plan.Contains c.Id)

        let flying = open' |> Array.map (fun c -> c.Id) |> Set.ofArray

        let openedAt =
            open'
            |> Array.fold
                (fun (m: Map<string, int>) c -> if s.Flying.Contains c.Id then m else m.Add(c.Id, minute))
                s.OpenedAt

        // Horizon 1 with everything "already open": this is allocation only.
        let ctx =
            Evaluate.context
                sc.Fleet
                s.Cond.FleetSize
                sc.Sources
                sc.Sectors
                needs
                flying
                s.CrewsAt
                1.0
                0.0
                st.Limits
                st.Physics.MinSalvoLpm
                open'

        let alloc = Evaluate.allocate ctx (Array.create open'.Length true)
        let delivered = Array.zeroCreate sc.Sectors.Length

        open'
        |> Array.iteri (fun i c ->
            // Drones start cycling once both the corridor and its lake crew are up.
            let crewReady = s.CrewReadyAt.TryFind c.Source.Id |> Option.defaultValue 0.0

            let firstDrop = max (float openedAt.[c.Id]) crewReady + c.WarmupMin
            let share = Math.Clamp(float (minute + 1) - firstDrop, 0.0, 1.0)
            delivered.[c.SectorIdx] <- delivered.[c.SectorIdx] + alloc.Throughput.[i] * sc.Fleet.PayloadL * share)

        let dronesFlying = Array.sum alloc.Drones

        let current =
            Lanes.assignLayers sc.Fleet.LaneSpacingM (List.ofArray open')
            |> List.map (fun (c, outAlt, retAlt) ->
                let i = Array.findIndex (fun (o: Corridor) -> o.Id = c.Id) open'

                {
                    Corridor = c
                    OutboundAltM = outAlt
                    ReturnAltM = retAlt
                    Drones = alloc.Drones.[i]
                    LaneLoad = alloc.Throughput.[i] / c.LaneCapPerMin
                    Draining = false
                })

        // Lanes that stopped flying since last minute. Still in the plan but
        // no longer flyable (wind): the drones on it are stranded. Taken out
        // of the plan: its drones finish their cycle along it, so the lane
        // stays in the evidence for one cycle.
        let gone =
            s.LastLanes
            |> List.filter (fun l -> l.Drones > 1e-9 && not (flying.Contains l.Corridor.Id))

        let stranded = gone |> List.filter (fun l -> s.Plan.Contains l.Corridor.Id)

        let draining =
            (s.Draining |> List.filter (fun (_, until) -> until > minute))
            @ (gone
               |> List.filter (fun l -> not (s.Plan.Contains l.Corridor.Id))
               |> List.map (fun l ->
                   ({ l with Draining = true }, minute + int (Math.Ceiling(l.Corridor.CycleS / 60.0)))))

        let record =
            {
                Minute = minute
                ActiveFireHa = FireSim.activeFireHa sc.Sectors s.Fire
                DeliveredL = Array.sum delivered
                DronesFlying = dronesFlying
                DronesIdle = float s.Cond.FleetSize - dronesFlying
                OpenCorridors = Planning.describe flying
                Intensities = s.Fire |> Array.map (fun f -> f.Intensity)
                Lanes = current @ (draining |> List.map fst)
                Stranded = stranded |> List.map (fun l -> (l.Corridor.Id, l.Drones))
            }

        let fire = FireSim.step st.Physics s.Cond sc.Sectors sc.SectorIndex s.Fire delivered

        ({ s with
            Fire = fire
            OpenedAt = openedAt
            Flying = flying
            LastBridge =
                if open'.Length > 0 then
                    List.ofArray open'
                else
                    s.LastBridge
            LastLanes = current
            Draining = draining
         },
         record)

    let run
        (backend: IQuantumBackend)
        (sc: Scenario)
        (st: Settings)
        (policy: Policy)
        (cond0: Conditions)
        : PolicyResult =
        let initialState =
            {
                Cond = cond0
                Fire = FireSim.initial sc.Sectors
                Plan = Set.empty
                CrewsAt = Set.empty
                CrewReadyAt = Map.empty
                OpenedAt = Map.empty
                Flying = Set.empty
                LastBridge = []
                LastLanes = []
                Draining = []
                Pending = None
                WarmParams = None
            }

        let step (s: SimState, timeline, decisions) minute =
            let events = sc.Events |> List.filter (fun e -> e.Minute = minute)
            let s = events |> List.fold (fun acc e -> apply sc acc e.Event) s

            let reason =
                [
                    if minute = 0 then
                        "initial plan"
                    elif minute % st.ReplanEvery = 0 then
                        "periodic"
                    yield! events |> List.map (fun e -> describeEvent sc e.Event)
                ]

            let replans = policy <> Static || minute = 0

            let s, planned =
                if replans && not reason.IsEmpty then
                    Planning.replan backend sc st policy minute (String.Join("; ", reason)) s
                else
                    (s, [])

            let s, arrived = Planning.adoptPending sc st policy minute s
            let s, record = fly sc st minute s
            (s, record :: timeline, decisions @ planned @ arrived)

        let final, timeline, decisions =
            [ 0 .. st.Minutes - 1 ] |> List.fold step (initialState, [], [])

        {
            Policy = policy
            Timeline = List.rev timeline
            Decisions = decisions
            LastBridge = final.LastBridge
            NewIgnitions =
                Array.map2
                    (fun (sec: FireSector) (f: SectorState) ->
                        if f.EverIgnited && sec.InitialIntensity = 0.0 then
                            Some sec.Id
                        else
                            None)
                    sc.Sectors
                    final.Fire
                |> Array.choose id
                |> List.ofArray
        }

// =============================================================================
// REPORTING
// =============================================================================

type PolicySummary =
    {
        policy: string
        water_delivered_l: float
        fire_ha_minutes: float
        peak_active_ha: float
        final_active_ha: float
        new_ignitions: string
        mean_idle_drones: float
        replans: int
        quantum_adopted: int
    }

type Metrics =
    {
        run_id: string
        minutes: int
        fleet_model: string
        fleet_size: int
        sources: int
        sectors: int
        max_corridors: int
        crews: int
        coordinators: int
        qaoa_layers: int
        qaoa_latency_min: int
        policies: PolicySummary list
        qaoa_rounds: int
        qaoa_warm_rounds: int
        qaoa_circuits: int
        qaoa_ms: int64
        qaoa_matched_exact: int
        greedy_matched_exact: int
        elapsed_ms: int64
    }

module Report =

    let summarize (r: PolicyResult) : PolicySummary =
        let tl = r.Timeline

        {
            policy = Policy.name r.Policy
            water_delivered_l = tl |> List.sumBy (fun m -> m.DeliveredL)
            fire_ha_minutes = tl |> List.sumBy (fun m -> m.ActiveFireHa)
            peak_active_ha = tl |> List.map (fun m -> m.ActiveFireHa) |> List.max
            final_active_ha = (List.last tl).ActiveFireHa
            new_ignitions =
                if r.NewIgnitions.IsEmpty then
                    "-"
                else
                    String.Join(" ", r.NewIgnitions)
            mean_idle_drones = tl |> List.averageBy (fun m -> m.DronesIdle)
            replans = r.Decisions |> List.filter (fun d -> d.GreedyScore.IsSome) |> List.length
            quantum_adopted = r.Decisions |> List.filter (fun d -> d.Adopted = "quantum") |> List.length
        }

    let private f1 (x: float) = sprintf "%.1f" x
    let private f3 (x: float) = sprintf "%.3f" x

    let private optF (x: float option) =
        x |> Option.map f1 |> Option.defaultValue ""

    /// Little's law per lake at the initial conditions: the ceiling on water
    /// each lake can feed, and the drones it takes to reach it.
    let printBottlenecks (sc: Scenario) (cond: Conditions) =
        printfn "── WHERE THE PIPE IS NARROWEST (t=0, Little's law) ──"

        let corridors = Corridors.buildAll sc.Fleet cond sc.Sources sc.Sectors

        for s in sc.Sources do
            let fillCap = float s.FillSlots * 60.0 / s.FillTimeS
            let mine = corridors |> Array.filter (fun c -> c.Source.Id = s.Id)

            if mine.Length > 0 then
                let meanCycleMin = (mine |> Array.averageBy (fun c -> c.CycleS)) / 60.0
                let meanAvail = mine |> Array.averageBy (fun c -> c.Availability)

                printfn
                    "  %-16s %d slots x %2.0fs fill -> %4.1f drones/min = %4.0f L/min;  saturates at ~%3.0f drones (cycle %.1f min, %.0f%% flying)"
                    s.Name
                    s.FillSlots
                    s.FillTimeS
                    fillCap
                    (fillCap * sc.Fleet.PayloadL)
                    (fillCap * meanCycleMin / meanAvail)
                    meanCycleMin
                    (meanAvail * 100.0)

        printfn
            "  Fleet: %d x %s (%.0f L). Drones beyond a lake's saturation point add no water there."
            sc.Fleet.Count
            sc.Fleet.Model
            sc.Fleet.PayloadL

        printfn ""

    let private scoreText (x: float) =
        if Double.IsNegativeInfinity x then
            "infeasible"
        else
            sprintf "%.0f" x

    let printDecision (d: Decision) =
        let q =
            match d.QuantumScore, d.QuantumCircuits with
            | Some qs, n when n > 0 ->
                sprintf
                    " qaoa=%s (%d qubits, %d circuits %s, %d ms)"
                    (scoreText qs)
                    d.Qubits
                    n
                    (if d.WarmStarted then "warm" else "cold")
                    d.QuantumMs
            | Some qs, _ -> sprintf " qaoa=%s" (scoreText qs)
            | None, _ -> ""

        let g =
            d.GreedyScore
            |> Option.map (scoreText >> sprintf " greedy=%s")
            |> Option.defaultValue ""

        let x =
            d.ExactScore
            |> Option.map (scoreText >> sprintf " exact=%s")
            |> Option.defaultValue ""

        printfn "  t=%2d %s" d.Minute d.Reason
        printfn "       now=%s%s%s%s -> %s" (scoreText d.IncumbentScore) g x q d.Adopted
        printfn "        plan: %s" d.Plan

    let printComparison (summaries: PolicySummary list) =
        printfn ""
        printfn "── POLICY COMPARISON (same fire, same events) ──"

        printfn
            "  %-16s %10s %12s %9s %9s %10s %9s  %s"
            "policy"
            "water (L)"
            "fire ha*min"
            "peak ha"
            "final ha"
            "idle drns"
            "replans"
            "new ignitions"

        for s in summaries do
            printfn
                "  %-16s %10.0f %12.1f %9.2f %9.2f %10.1f %9d  %s"
                s.policy
                s.water_delivered_l
                s.fire_ha_minutes
                s.peak_active_ha
                s.final_active_ha
                s.mean_idle_drones
                s.replans
                s.new_ignitions

    let printLanes (laneSpacingM: float) (plan: Corridor list) =
        printfn ""
        printfn "── FINAL AIR BRIDGE (adaptive-hybrid): lanes and altitude layers ──"

        for c, outAlt, retAlt in Lanes.assignLayers laneSpacingM plan do
            let flag =
                if retAlt > Regulations.maxAltitudeAglMeters then
                    "  ⚠ above AGL ceiling"
                else
                    ""

            printfn
                "  %-12s %4.2f km  out %3.0f m / back %3.0f m  cycle %5.1f s  first drop after %.1f min%s"
                c.Id
                c.DistanceKm
                outAlt
                retAlt
                c.CycleS
                c.WarmupMin
                flag

    let writeFiles (outDir: string) (sc: Scenario) (results: PolicyResult list) (hybrid: PolicyResult option) =
        let sectorCols = sc.Sectors |> Array.map (fun s -> "i_" + s.Id) |> List.ofArray

        Reporting.writeCsv
            (Path.Combine(outDir, "timeline.csv"))
            ([
                "policy"
                "minute"
                "active_fire_ha"
                "delivered_l"
                "drones_flying"
                "drones_idle"
                "open_corridors"
             ]
             @ sectorCols)
            [
                for r in results do
                    for m in r.Timeline do
                        [
                            Policy.name r.Policy
                            string m.Minute
                            f3 m.ActiveFireHa
                            f1 m.DeliveredL
                            f1 m.DronesFlying
                            f1 m.DronesIdle
                            m.OpenCorridors
                        ]
                        @ (m.Intensities |> Array.map f3 |> List.ofArray)
            ]

        Reporting.writeCsv
            (Path.Combine(outDir, "decisions.csv"))
            [
                "policy"
                "minute"
                "reason"
                "candidates"
                "incumbent_score"
                "greedy_score"
                "exact_score"
                "quantum_score"
                "adopted"
                "plan"
                "qubits"
                "qaoa_circuits"
                "warm_started"
                "qaoa_ms"
            ]
            [
                for r in results do
                    for d in r.Decisions do
                        [
                            d.Policy
                            string d.Minute
                            d.Reason
                            string d.Candidates
                            f1 d.IncumbentScore
                            optF d.GreedyScore
                            optF d.ExactScore
                            optF d.QuantumScore
                            d.Adopted
                            d.Plan
                            string d.Qubits
                            string d.QuantumCircuits
                            string d.WarmStarted
                            string d.QuantumMs
                        ]
            ]

        hybrid
        |> Option.iter (fun h ->
            Reporting.writeCsv
                (Path.Combine(outDir, "lanes.csv"))
                [
                    "corridor"
                    "source"
                    "sector"
                    "distance_km"
                    "outbound_alt_m"
                    "return_alt_m"
                    "cycle_s"
                    "warmup_min"
                ]
                [
                    for c, o, b in Lanes.assignLayers sc.Fleet.LaneSpacingM h.LastBridge do
                        [
                            c.Id
                            c.Source.Id
                            c.Sector.Id
                            f3 c.DistanceKm
                            f1 o
                            f1 b
                            f1 c.CycleS
                            f1 c.WarmupMin
                        ]
                ])

// =============================================================================
// 1:N PERMISSION EVIDENCE
// =============================================================================

module Evidence =

    module Ev = FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence

    let private c2BandMhz = 900.0
    let private c2FadeMarginDb = 10.0

    /// Minutes during which each lake was closed by the scenario's events.
    let private closedMinutes (sc: Scenario) (minutes: int) =
        [ 0 .. minutes - 1 ]
        |> List.map (fun m ->
            let closed =
                sc.Events
                |> List.filter (fun e -> e.Minute <= m)
                |> List.sortBy (fun e -> e.Minute)
                |> List.fold
                    (fun (acc: Set<string>) e ->
                        match e.Event with
                        | CloseSource id -> acc.Add id
                        | OpenSource id -> acc.Remove id
                        | _ -> acc)
                    Set.empty

            (m, closed))
        |> Map.ofList

    let private laneAltitudes (l: LaneSnapshot) =
        [
            (l.Corridor.Id + " outbound", l.OutboundAltM)
            (l.Corridor.Id + " return", l.ReturnAltM)
        ]

    let build
        (sc: Scenario)
        (st: Settings)
        (pilots: int)
        (vehicleConfig: (string * Map<string, string>) option)
        (r: PolicyResult)
        : Ev.Pack =
        let minutes = r.Timeline

        let allLanes =
            minutes |> List.collect (fun m -> m.Lanes |> List.map (fun l -> (m.Minute, l)))

        // --- Deconfliction --------------------------------------------------
        let peakLoad = allLanes |> List.map (fun (_, l) -> l.LaneLoad) |> List.fold max 0.0

        let inLane =
            {
                Area = Ev.Deconfliction
                Claim = "Aircraft on the same lane keep in-trail spacing"
                Method =
                    "lane spacing vs. the in-trail minimum; lane load = drops/min over the lane's headway capacity (speed / spacing), every minute flown"
                Measured = sprintf "spacing %.0f m, peak lane load %.0f%%" sc.Fleet.LaneSpacingM (peakLoad * 100.0)
                Limit = sprintf "spacing >= %.0f m, load <= 100%%" Safety.formationFollowingDistanceMeters
                Status =
                    if
                        sc.Fleet.LaneSpacingM >= Safety.formationFollowingDistanceMeters
                        && peakLoad <= 1.0 + 1e-9
                    then
                        Ev.Pass
                    else
                        Ev.Fail
                Details = []
            }
            : Ev.Check

        // Every pair of lanes whose ground tracks cross, in the same minute.
        let crossings =
            minutes
            |> List.collect (fun m ->
                [
                    for a in m.Lanes do
                        for b in m.Lanes do
                            if
                                a.Corridor.Id < b.Corridor.Id
                                && Corridors.cross sc.Fleet.LaneSpacingM a.Corridor b.Corridor
                            then
                                let vertical =
                                    [
                                        for x in [ a.OutboundAltM; a.ReturnAltM ] do
                                            for y in [ b.OutboundAltM; b.ReturnAltM ] do
                                                abs (x - y)
                                    ]
                                    |> List.min

                                (m.Minute, a, b, vertical)
                ])

        let ownLaneGap =
            allLanes
            |> List.map (fun (_, l) -> l.ReturnAltM - l.OutboundAltM)
            |> List.fold min Double.MaxValue

        let minGap =
            (crossings |> List.map (fun (_, _, _, v) -> v))
            @ (if allLanes.IsEmpty then [] else [ ownLaneGap ])
            |> List.fold min Double.MaxValue

        let crossingCheck =
            let worst = crossings |> List.sortBy (fun (_, _, _, v) -> v)

            {
                Area = Ev.Deconfliction
                Claim = "Lanes that cross, and a corridor's outbound vs. return lane, are vertically separated"
                Method =
                    "altitude-layer assignment per minute; smallest vertical gap between any two lanes whose ground tracks cross"
                Measured =
                    match worst with
                    | (m, a, b, v) :: _ ->
                        sprintf
                            "%d crossing lane-pair-minutes, closest %.0f m (%s / %s at t=%d); own-lane gap %.0f m"
                            crossings.Length
                            v
                            a.Corridor.Id
                            b.Corridor.Id
                            m
                            ownLaneGap
                    | [] when allLanes.IsEmpty -> "no lanes flown"
                    | [] -> sprintf "no crossing lanes; own-lane gap %.0f m" ownLaneGap
                Limit = sprintf ">= %.0f m vertical" Safety.minSwarmSeparationMeters
                Status =
                    if minGap >= Safety.minSwarmSeparationMeters then
                        Ev.Pass
                    else
                        Ev.Fail
                Details = []
            }
            : Ev.Check

        // Terminal areas. All lanes into one lake (or one drop zone) merge into
        // a single in-trail sequence before the slots, the way arrivals merge
        // at an airport. That is safe when (a) the merged arrival rate fits one
        // lane's headway capacity, (b) the lanes have diverged by one spacing
        // before the merge point, which must lie inside the shortest lane, and
        // (c) the slots sit on a ring whose neighbours are one spacing apart.
        let spacing = sc.Fleet.LaneSpacingM

        let groundSpeeds (c: Corridor) =
            let m = c.DistanceKm * 1000.0
            min (m / c.OutboundS) (m / c.ReturnS)

        let ringRadius (slots: int) =
            if slots <= 1 then
                0.0
            else
                spacing / (2.0 * Math.Sin(Math.PI / float slots))

        /// Distance from the endpoint at which two lanes are one spacing apart.
        let mergeRadius (a: Corridor) (b: Corridor) (atSource: bool) =
            let dir (c: Corridor) =
                if atSource then
                    Geometry.unit c.Source.Pos c.Sector.Pos
                else
                    Geometry.unit c.Sector.Pos c.Source.Pos

            let cosT = Math.Clamp(Geometry.dot (dir a) (dir b), -1.0, 1.0)
            let half = Math.Acos cosT / 2.0

            if half < 1e-6 then
                Double.PositiveInfinity
            else
                spacing / (2.0 * Math.Sin half)

        let terminals =
            minutes
            |> List.collect (fun m ->
                let group (key: LaneSnapshot -> string) (atSource: bool) (slots: LaneSnapshot -> int) (label: string) =
                    m.Lanes
                    |> List.groupBy key
                    |> List.map (fun (id, ls) ->
                        let merged = ls |> List.sumBy (fun l -> l.LaneLoad * l.Corridor.LaneCapPerMin)

                        let capacity =
                            ls |> List.map (fun l -> 60.0 * groundSpeeds l.Corridor / spacing) |> List.min

                        let merge, shortest =
                            [
                                for a in ls do
                                    for b in ls do
                                        if a.Corridor.Id < b.Corridor.Id then
                                            (mergeRadius a.Corridor b.Corridor atSource,
                                             1000.0 * min a.Corridor.DistanceKm b.Corridor.DistanceKm)
                            ]
                            |> function
                                | [] -> (0.0, Double.PositiveInfinity)
                                | xs -> xs |> List.maxBy (fun (r, len) -> r / len)

                        {|
                            Name = sprintf "%s %s" label id
                            Minute = m.Minute
                            Lanes = ls.Length
                            Load = merged / capacity
                            Merge = merge
                            Shortest = shortest
                            Ring = ringRadius (slots ls.Head)
                        |})

                group (fun l -> l.Corridor.Source.Id) true (fun l -> l.Corridor.Source.FillSlots) "lake"
                @ group (fun l -> l.Corridor.Sector.Id) false (fun l -> l.Corridor.Sector.DropSlots) "drop zone")

        let terminalOk
            (t:
                {|
                    Name: string
                    Minute: int
                    Lanes: int
                    Load: float
                    Merge: float
                    Shortest: float
                    Ring: float
                |})
            =
            t.Load <= 1.0 + 1e-9 && t.Merge <= t.Shortest

        let convergence =
            let worstPerTerminal =
                terminals
                |> List.groupBy (fun t -> t.Name)
                |> List.map (fun (_, ts) -> ts |> List.maxBy (fun t -> max t.Load (t.Merge / t.Shortest)))
                |> List.sortByDescending (fun t -> max t.Load (t.Merge / t.Shortest))

            {
                Area = Ev.Deconfliction
                Claim = "Aircraft converging on a shared lake or drop zone stay separated"
                Method =
                    "every minute, per lake and drop zone: lanes merge into one in-trail sequence; merged drops/min vs. one lane's headway capacity (slowest ground speed / spacing), merge radius (where two lanes are one spacing apart) vs. the shorter lane, slot ring radius for one spacing between neighbouring slots"
                Measured =
                    match worstPerTerminal with
                    | t :: _ ->
                        sprintf
                            "%d terminals; busiest %s at %.0f%% of one sequence (t=%d), widest merge %.0f m"
                            worstPerTerminal.Length
                            t.Name
                            (t.Load * 100.0)
                            t.Minute
                            (terminals |> List.map (fun t -> t.Merge) |> List.fold max 0.0)
                    | [] -> "no lanes flown"
                Limit = "load <= 100%, merge radius <= shorter lane length"
                Status =
                    if terminals |> List.forall terminalOk then
                        Ev.Pass
                    else
                        Ev.Fail
                Details =
                    worstPerTerminal
                    |> List.map (fun t ->
                        let merge =
                            if t.Lanes = 1 then
                                "single lane"
                            else
                                sprintf "%d lanes merge at %.0f m (shorter lane %.0f m)" t.Lanes t.Merge t.Shortest

                        sprintf
                            "%s: %s, %.0f%% of one sequence, slot ring radius %.0f m%s"
                            t.Name
                            merge
                            (t.Load * 100.0)
                            t.Ring
                            (if terminalOk t then "" else "  <-- FAIL"))
            }
            : Ev.Check

        // --- Endurance ------------------------------------------------------
        let usableS =
            sc.Fleet.EnduranceMin * 60.0 * (1.0 - Battery.reserveBatteryPercent / 100.0)

        let endurance =
            allLanes
            |> List.map (fun (m, l) -> (l.Corridor.Id, float l.Corridor.CyclesPerBattery * l.Corridor.CycleS, m))
            |> List.groupBy (fun (id, _, _) -> id)
            |> List.map (fun (id, xs) ->
                let _, need, m = xs |> List.maxBy (fun (_, n, _) -> n)
                (sprintf "%s (worst wind, t=%d)" id m, need / 60.0, usableS / 60.0))
            |> Ev.Checks.endurance
                "min"
                "per corridor, cycles flown per battery x cycle time (fill, loaded outbound, drop, empty return, with the wind at the time) vs. endurance minus reserve; batteries swap at the lake"

        // --- C2 link --------------------------------------------------------
        // The pilot station is the incident command post at the scenario origin.
        let origin = { X = 0.0; Y = 0.0 }

        let c2 =
            allLanes
            |> List.collect (fun (_, l) ->
                [
                    (l.Corridor.Source.Name, Geometry.distanceKm origin l.Corridor.Source.Pos)
                    (l.Corridor.Sector.Name, Geometry.distanceKm origin l.Corridor.Sector.Pos)
                ])
            |> List.distinct
            |> Ev.Checks.c2Link "the incident command post" c2BandMhz c2FadeMarginDb

        // --- Altitude -------------------------------------------------------
        let altitude =
            allLanes
            |> List.collect (snd >> laneAltitudes)
            |> List.distinct
            |> Ev.Checks.altitude

        // --- Contingency ----------------------------------------------------
        let rate (from: int) (until: int) =
            minutes
            |> List.filter (fun m -> m.Minute >= from && m.Minute < until)
            |> function
                | [] -> 0.0
                | ms -> ms |> List.averageBy (fun m -> m.DeliveredL)

        // Every minute after a loss is inside the checks above, so the loss is
        // handled exactly when they all pass.
        // A corridor still in the plan that stops being flyable (a wind shift
        // pushes its cycle past the battery or its ground speed below the
        // minimum) leaves the drones already on it with no flyable way round.
        let strandedCheck =
            let stranded =
                minutes
                |> List.collect (fun m -> m.Stranded |> List.map (fun (id, n) -> (m.Minute, id, n)))

            Ev.Checks.contingency
                "No aircraft is left on a lane that stops being flyable"
                "every minute: corridors still in the plan whose drones were flying the minute before, but which the current wind or endurance no longer allows"
                (sprintf "%d stranded lane-minute(s)" stranded.Length)
                (if stranded.IsEmpty then Ev.Pass else Ev.Fail)
                (stranded
                 |> List.truncate 10
                 |> List.map (fun (m, id, n) ->
                     sprintf "t=%d %s: %.0f drone(s) on a lane that can no longer be flown" m id n))
            |> fun c -> { c with Area = Ev.EnduranceRange }

        // A lane that changes altitude while drones are on it would move them
        // through another layer; layers are only valid if they stay put.
        let layerCheck =
            let moves =
                allLanes
                |> List.filter (fun (_, l) -> not l.Draining)
                |> List.groupBy (fun (_, l) -> l.Corridor.Id)
                |> List.choose (fun (id, ls) ->
                    match ls |> List.map (fun (_, l) -> (l.OutboundAltM, l.ReturnAltM)) |> List.distinct with
                    | [ _ ] -> None
                    | alts ->
                        Some(
                            sprintf
                                "%s flew at %s"
                                id
                                (alts |> List.map (fun (o, r) -> sprintf "%.0f/%.0f m" o r) |> String.concat ", ")
                        ))

            {
                Area = Ev.Deconfliction
                Claim = "A lane keeps its altitude layer for as long as it flies"
                Method = "outbound/return altitudes of every corridor across all the minutes it flew"
                Measured = sprintf "%d lane(s) changed layer" moves.Length
                Limit = "none"
                Status = if moves.IsEmpty then Ev.Pass else Ev.Fail
                Details = moves
            }
            : Ev.Check

        let afterLossOk =
            [
                inLane
                crossingCheck
                layerCheck
                convergence
                endurance
                strandedCheck
                c2
                altitude
            ]
            |> List.forall (fun c -> c.Status = Ev.Pass)

        let lossChecks =
            sc.Events
            |> List.choose (fun e ->
                match e.Event with
                | LoseDrones n when e.Minute < st.Minutes ->
                    let before = minutes |> List.tryFind (fun m -> m.Minute = e.Minute - 1)
                    let after = minutes |> List.tryFind (fun m -> m.Minute = e.Minute)

                    Some(
                        Ev.Checks.contingency
                            (sprintf
                                "Losing %d aircraft at once (t=%d) leaves the operation deconflicted and in range"
                                n
                                e.Minute)
                            "fast loop re-spreads the remaining fleet over the open lanes the same minute; the minutes after the loss are included in every check above"
                            (sprintf
                                "fleet -%d; drones assigned %.0f -> %.0f; water %.0f -> %.0f L/min (3-min averages)"
                                n
                                (before |> Option.map (fun m -> m.DronesFlying) |> Option.defaultValue 0.0)
                                (after |> Option.map (fun m -> m.DronesFlying) |> Option.defaultValue 0.0)
                                (rate (e.Minute - 3) e.Minute)
                                (rate e.Minute (e.Minute + 3)))
                            (if afterLossOk then Ev.Pass else Ev.Fail)
                            []
                    )
                | _ -> None)

        let closed = closedMinutes sc st.Minutes

        // Draining lanes are drones finishing a cycle already under way when
        // the lake closed; a lane planned from a closed lake is the failure.
        let flewClosed =
            allLanes
            |> List.filter (fun (m, l) -> not l.Draining && closed.[m].Contains l.Corridor.Source.Id)

        let lakeCheck =
            let closures =
                sc.Events
                |> List.filter (fun e ->
                    match e.Event with
                    | CloseSource _ -> true
                    | _ -> false)

            Ev.Checks.contingency
                "Losing a water source (smoke) takes its lanes out of use at once"
                "every minute: no lane flew from a lake closed at that minute; the closure triggers an immediate re-plan"
                (sprintf
                    "%d closure event(s); %d lane-minutes flown from a closed lake"
                    closures.Length
                    flewClosed.Length)
                (if flewClosed.IsEmpty then Ev.Pass else Ev.Fail)
                (flewClosed
                 |> List.truncate 5
                 |> List.map (fun (m, l) -> sprintf "t=%d %s" m l.Corridor.Id))

        // Lost link and low battery. The deconflicted way home is to finish the
        // current cycle along the lanes to the lake's recovery slot: those
        // lanes are in every check above, and a whole cycle fits in the usable
        // battery (endurance check). What the plan cannot show is that the
        // vehicles are set up to do that, so this reads the operator's
        // configuration baseline and checks it against the modelled procedure.
        let baselineCheck (claim: string) (key: string) (expected: string) (wrong: Map<string, string>) =
            match vehicleConfig with
            | None ->
                Ev.Checks.notEvidenced
                    Ev.Contingency
                    claim
                    (sprintf
                        "No vehicle configuration baseline given (--vehicle-config); the modelled procedure is '%s'."
                        expected)
            | Some(file: string, cfg: Map<string, string>) ->
                let actual = cfg.TryFind key |> Option.defaultValue "(not set)"

                Ev.Checks.contingency
                    claim
                    (sprintf
                        "operator configuration baseline %s, setting '%s', vs. the procedure the evidence models"
                        (Path.GetFileName file)
                        key)
                    (sprintf "%s = %s" key actual)
                    (if actual = expected then Ev.Pass else Ev.Fail)
                    [
                        match wrong.TryFind actual with
                        | Some why -> why
                        | None when actual <> expected -> sprintf "expected '%s'" expected
                        | None -> "matches the modelled procedure; the loaded parameters are verified at preflight"
                    ]

        let lostLink =
            baselineCheck
                "An aircraft that loses its C2 link returns without crossing traffic"
                "lost_link_action"
                "continue_to_recovery"
                (Map.ofList
                    [
                        "rtl", "RTL flies straight home at one altitude, across other lanes"
                        "smart_rtl", "SmartRTL retraces the outbound lane, against the traffic behind it"
                        "land", "landing in place puts an aircraft down in the fire area, under other lanes"
                    ])

        let lostLinkTimeout =
            match vehicleConfig |> Option.bind (fun (_, cfg) -> cfg.TryFind "lost_link_timeout_s") with
            | Some v ->
                let ok, s =
                    Double.TryParse(v, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture)

                Ev.Checks.contingency
                    "The lost-link action starts before the aircraft drifts out of its lane"
                    "operator configuration baseline, setting 'lost_link_timeout_s'"
                    (sprintf "lost_link_timeout_s = %s" v)
                    (if ok && s >= 0.0 && s <= Safety.signalLossRthTriggerSec then
                         Ev.Pass
                     else
                         Ev.Fail)
                    [
                        sprintf "limit %.0f s (Safety.signalLossRthTriggerSec)" Safety.signalLossRthTriggerSec
                    ]
            | None ->
                Ev.Checks.notEvidenced
                    Ev.Contingency
                    "The lost-link action starts before the aircraft drifts out of its lane"
                    "No lost_link_timeout_s in a vehicle configuration baseline."

        let lowBattery =
            baselineCheck
                "An aircraft on low battery gets home without crossing traffic"
                "low_battery_action"
                "continue_to_recovery"
                (Map.ofList [ "rtl", "RTL flies straight home at one altitude, across other lanes" ])

        let descentPoints =
            crossings
            |> List.map (fun (_, a, b, _) ->
                if a.Corridor.Id < b.Corridor.Id then
                    (a.Corridor.Id, b.Corridor.Id)
                else
                    (b.Corridor.Id, a.Corridor.Id))
            |> List.distinct

        let laneFailureAction =
            vehicleConfig |> Option.bind (fun (_, cfg) -> cfg.TryFind "lane_failure_action")

        let failDescent =
            match laneFailureAction with
            | None ->
                Ev.Checks.notEvidenced
                    Ev.Contingency
                    "An aircraft failing in a lane descends without passing through another lane"
                    "No lane_failure_action in a vehicle configuration baseline: the modelled controlled descent (descend_in_place) is not shown to be configured."
            | Some action when action <> "descend_in_place" ->
                Ev.Checks.contingency
                    "An aircraft failing in a lane descends without passing through another lane"
                    "operator configuration baseline, setting 'lane_failure_action'"
                    (sprintf "lane_failure_action = %s" action)
                    Ev.Fail
                    [ "the evidence models a controlled descent straight down (descend_in_place)" ]
            | Some _ when descentPoints.IsEmpty ->
                Ev.Checks.contingency
                    "An aircraft failing in a lane descends without passing through another lane"
                    "controlled descent straight down from its lane; no other lane lies below any lane"
                    "no crossing lanes flown"
                    Ev.Pass
                    []
            | Some _ ->
                Ev.Checks.contingency
                    "An aircraft failing in a lane descends without passing through another lane"
                    "controlled descent straight down from its lane vs. every lane pair flown in conflict (the planner refuses conflicting lanes; draining lanes during a plan change can still meet new ones)"
                    (sprintf "%d conflicting lane pair(s) flown" descentPoints.Length)
                    Ev.Fail
                    (descentPoints
                     |> List.map (fun (a, b) ->
                         sprintf "%s x %s: a failure in the upper lane descends through the lower one" a b))

        // --- Supervisor workload ---------------------------------------------
        let worstCycleS =
            allLanes |> List.map (fun (_, l) -> l.Corridor.CycleS) |> List.fold max 0.0

        let highestLaneM =
            allLanes |> List.map (fun (_, l) -> l.ReturnAltM) |> List.fold max 0.0

        let dronesOnLake (lake: string) (minute: int) =
            minutes
            |> List.tryFind (fun m -> m.Minute = minute - 1)
            |> Option.map (fun m ->
                m.Lanes
                |> List.filter (fun l -> l.Corridor.Source.Id = lake)
                |> List.sumBy (fun l -> l.Drones))
            |> Option.defaultValue 0.0
            |> Math.Ceiling
            |> int

        let scripted =
            sc.Events
            |> List.filter (fun e -> e.Minute < st.Minutes)
            |> List.choose (fun e ->
                let at = float e.Minute * 60.0

                let one name aircraft response =
                    Some(
                        sprintf "%s (t=%d min)" name e.Minute,
                        [
                            {
                                Ev.Event = name
                                Ev.Affected = aircraft
                                Ev.StartS = at
                                Ev.DurationS = 60.0
                                Ev.Handling = Ev.Automatic
                                Ev.Response = response
                            }
                        ]
                    )

                match e.Event with
                | LoseDrones n ->
                    one
                        (sprintf "%d aircraft lost at once" n)
                        n
                        "fast loop re-spreads the rest over the open lanes the same minute"
                | CloseSource lake ->
                    one
                        (sprintf "smoke closes %s" lake)
                        (dronesOnLake lake e.Minute)
                        "its lanes leave the plan; re-plan the same minute (the ground crew relocates, not the pilot)"
                | WindDirection _
                | WindSpeed _ -> one "wind shift" 0 "re-plan; lanes and cycle times recomputed"
                | SpotFire(id, _) -> one (sprintf "spot fire in %s" id) 0 "re-plan"
                | OpenSource _ -> None)
            // Direction and speed of one wind shift are two rows of one event.
            |> List.distinctBy fst

        // One aircraft's failsafe: automatic only when the vehicles are set up
        // to do what the evidence models; anything else is the pilot's.
        let failsafeScenario (name: string) (key: string) (expected: string) (durationS: float) (response: string) =
            let configured = vehicleConfig |> Option.bind (fun (_, cfg) -> cfg.TryFind key)

            let automatic = configured = Some expected

            (name,
             [
                 {
                     Ev.Event = name
                     Ev.Affected = 1
                     Ev.StartS = 0.0
                     Ev.DurationS = if automatic then durationS else Ev.decisionTimeS
                     Ev.Handling = if automatic then Ev.Automatic else Ev.PilotDecision
                     Ev.Response =
                         if automatic then
                             response
                         else
                             sprintf "%s is '%s', not the modelled '%s'" key (defaultArg configured "not set") expected
                 }
             ])

        let workload =
            Ev.Checks.workload
                pilots
                (scripted
                 @ [
                     failsafeScenario
                         "one aircraft loses its C2 link"
                         "lost_link_action"
                         "continue_to_recovery"
                         worstCycleS
                         "finishes its cycle along the lanes to the recovery slot"
                     failsafeScenario
                         "one aircraft reaches low battery"
                         "low_battery_action"
                         "continue_to_recovery"
                         worstCycleS
                         "finishes its cycle along the lanes to the recovery slot"
                     failsafeScenario
                         "one aircraft fails in a lane"
                         "lane_failure_action"
                         "descend_in_place"
                         (highestLaneM / Safety.imuFailureDescentRateMs)
                         "controlled descent out of the lane; the fast loop re-spreads the rest"
                 ])

        // --- Ratio ----------------------------------------------------------
        let peakAirborne =
            minutes
            // Draining drones are already counted on the lanes they moved to.
            |> List.map (fun m ->
                m.Lanes
                |> List.filter (fun l -> not l.Draining)
                |> List.sumBy (fun l -> l.Drones * l.Corridor.Availability))
            |> List.fold max 0.0
            |> Math.Ceiling
            |> int

        {
            Example = "FireAirBridge"
            Operation =
                sprintf
                    "Hotspot suppression air bridge, %s policy, %d x %s over %d min"
                    (Policy.name r.Policy)
                    sc.Fleet.Count
                    sc.Fleet.Model
                    st.Minutes
            Pilots = pilots
            Aircraft = sc.Fleet.Count
            PeakAirborne = peakAirborne
            Checks =
                [
                    inLane
                    crossingCheck
                    layerCheck
                    convergence
                    endurance
                    strandedCheck
                    c2
                    altitude
                ]
                @ lossChecks
                @ [
                    lakeCheck
                    lostLink
                    lostLinkTimeout
                    lowBattery
                    failDescent
                    Ev.Checks.pilotRatio pilots sc.Fleet.Count peakAirborne
                    workload
                ]
            Assumptions =
                [
                    "Aircraft fly the lane centre lines at the planned altitudes; navigation error is inside the spacing margins."
                    sprintf
                        "Pilot station at the incident command post, taken as the scenario centroid; C2 over %.0f MHz."
                        c2BandMhz
                    "Batteries are swapped at the lake by the ground crew; an aircraft never starts a cycle it cannot finish with reserve."
                    "The airspace is a segregated emergency volume: no traffic other than this fleet."
                    "Wind is uniform over the area and changes only at scenario events."
                ]
        }

// =============================================================================
// MAIN
// =============================================================================

module Program =

    let private printHelp () =
        printfn "FOREST-FIRE AIR BRIDGE — drones shuttle water from lakes to a moving fire"
        printfn ""
        printfn "OPTIONS:"
        printfn "  --sources <path>       water sources CSV   (default examples/Drones/_data/fire_water_sources.csv)"
        printfn "  --sectors <path>       fire sectors CSV    (default examples/Drones/_data/fire_sectors.csv)"
        printfn "  --fleet <path>         drone class CSV     (default examples/Drones/_data/fire_fleet.csv)"
        printfn "  --events <path>        scenario events CSV (default examples/Drones/_data/fire_events.csv)"
        printfn "  --out <dir>            output directory    (default runs/drone/fire-air-bridge)"

        printfn
            "  --policy <p>           all | static | adaptive-greedy | adaptive-exact | adaptive-hybrid (default all)"

        printfn "  --minutes <n>          simulated minutes (default 60)"
        printfn "  --replan-every <n>     periodic slow-loop interval, minutes (default 10); events always re-plan"
        printfn "  --horizon <n>          minutes a plan is scored over (default 20)"
        printfn "  --crews <n>            ground crews; each lake in use needs one (default 2)"
        printfn "  --coordinators <n>     drop coordinators; each open corridor needs one (default 5)"
        printfn "  --crew-move <n>        minutes for a crew to relocate to another lake (default 6)"
        printfn "  --max-corridors <n>    candidate corridors per round; QAOA adds one qubit per lake (default 12)"
        printfn "  --exact-limit <n>      brute-force oracle up to this many candidates (default 20)"
        printfn "  --latency <n>          minutes of queueing before a QAOA answer is usable (default 1);"
        printfn "                         the measured compute time is always added as a floor"
        printfn "  --layers <n>           QAOA layers p (default 1)"
        printfn "  --shots <n>            final QAOA shots (default 1000)"
        printfn "  --pilots <n>           remote pilots for the 1:N permission evidence (default 1)"

        printfn
            "  --vehicle-config <p>   operator failsafe baseline CSV (default examples/Drones/_data/fire_vehicle_config.csv)"

    let private fail (msg: string) =
        printfn "❌ %s" msg
        1

    [<EntryPoint>]
    let main argv =
        let args = Cli.parse argv

        if Cli.hasFlag "help" args || Cli.hasFlag "h" args then
            printHelp ()
            0
        else
            let sw = Stopwatch.StartNew()

            let sourcesPath =
                Cli.getOr "sources" "examples/Drones/_data/fire_water_sources.csv" args

            let sectorsPath = Cli.getOr "sectors" "examples/Drones/_data/fire_sectors.csv" args
            let fleetPath = Cli.getOr "fleet" "examples/Drones/_data/fire_fleet.csv" args
            let eventsPath = Cli.getOr "events" "examples/Drones/_data/fire_events.csv" args
            let outDir = Cli.getOr "out" (Path.Combine("runs", "drone", "fire-air-bridge")) args

            let settings =
                {
                    Minutes = max 1 (Cli.getIntOr "minutes" 60 args)
                    ReplanEvery = max 1 (Cli.getIntOr "replan-every" 10 args)
                    HorizonMin = float (max 1 (Cli.getIntOr "horizon" 20 args))
                    MaxCorridors = Math.Clamp(Cli.getIntOr "max-corridors" 12 args, 1, 20)
                    ExactLimit = Math.Clamp(Cli.getIntOr "exact-limit" 20 args, 0, 24)
                    LatencyMin = max 0 (Cli.getIntOr "latency" 1 args)
                    CrossPenalty = 200.0
                    Limits =
                        {
                            Crews = max 1 (Cli.getIntOr "crews" 2 args)
                            Coordinators = max 1 (Cli.getIntOr "coordinators" 5 args)
                            CrewMoveMin = float (max 0 (Cli.getIntOr "crew-move" 6 args))
                        }
                    Qaoa =
                        {
                            Layers = max 1 (Cli.getIntOr "layers" 1 args)
                            SearchShots = 200
                            FinalShots = max 1 (Cli.getIntOr "shots" 1000 args)
                            TopCandidates = 32
                        }
                    Physics = FireSim.defaultPhysics
                }

            let policies =
                match Cli.getOr "policy" "all" args with
                | "all" -> Ok Policy.all
                | p ->
                    match Policy.tryParse p with
                    | Some policy -> Ok [ policy ]
                    | None -> Error(sprintf "unknown policy '%s'" p)

            let rawSources, e1 = Parse.readSources sourcesPath
            let rawSectors, e2 = Parse.readSectors sectorsPath
            let fleets, e3 = Parse.readFleet fleetPath
            let events, e4 = Parse.readEvents eventsPath

            for e in e1 @ e2 @ e3 @ e4 do
                printfn "⚠ %s" e

            match policies, fleets with
            | Error msg, _ -> fail msg
            | _, [] -> fail "no drone class loaded"
            | _ when rawSources.IsEmpty || rawSectors.IsEmpty ->
                fail "need at least one water source and one fire sector"
            | Ok policies, fleet :: rest ->
                if not rest.IsEmpty then
                    printfn "⚠ %d extra fleet rows ignored: this example flies one drone class" rest.Length

                if fleet.LaneSpacingM < Safety.formationFollowingDistanceMeters then
                    printfn
                        "⚠ lane spacing %.0f m is below the %.0f m in-trail minimum"
                        fleet.LaneSpacingM
                        Safety.formationFollowingDistanceMeters

                let points = (rawSources |> List.map fst) @ (rawSectors |> List.map fst)
                let origin = (points |> List.averageBy fst, points |> List.averageBy snd)

                let sources =
                    Parse.project rawSources (fun p s -> { s with Pos = p }) origin |> Array.ofList

                let sectors =
                    Parse.project rawSectors (fun p s -> { s with Pos = p }) origin |> Array.ofList

                let scenario =
                    {
                        Sources = sources
                        Sectors = sectors
                        SectorIndex = sectors |> Array.mapi (fun i s -> (s.Id, i)) |> Map.ofArray
                        Fleet = fleet
                        Events = events
                    }

                // Wind at t=0 comes from the minute-0 events; the rest play out in the run.
                let cond0 =
                    events
                    |> List.filter (fun e -> e.Minute = 0)
                    |> List.fold
                        (fun c e ->
                            match e.Event with
                            | WindDirection d -> { c with WindToDeg = d }
                            | WindSpeed ms -> { c with WindSpeedMs = ms }
                            | _ -> c)
                        {
                            WindToDeg = 0.0
                            WindSpeedMs = 0.0
                            ClosedSources = Set.empty
                            FleetSize = fleet.Count
                        }

                Data.ensureDirectory outDir
                let runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss")

                printfn ""

                printfn
                    "FOREST-FIRE AIR BRIDGE — %d lakes, %d sectors, %d drones, %d min"
                    sources.Length
                    sectors.Length
                    fleet.Count
                    settings.Minutes

                printfn ""
                Report.printBottlenecks scenario cond0

                let backend = LocalBackend() :> IQuantumBackend

                let results =
                    policies
                    |> List.map (fun p ->
                        let r = Simulation.run backend scenario settings p cond0

                        if p = AdaptiveHybrid || policies.Length = 1 then
                            printfn "── SLOW-LOOP DECISIONS (%s) ──" (Policy.name p)
                            r.Decisions |> List.iter Report.printDecision
                            printfn ""

                        r)

                let summaries = results |> List.map Report.summarize
                Report.printComparison summaries

                let hybrid = results |> List.tryFind (fun r -> r.Policy = AdaptiveHybrid)

                hybrid
                |> Option.iter (fun h -> Report.printLanes scenario.Fleet.LaneSpacingM h.LastBridge)

                // How the QAOA answers compare with the oracle at the same rounds.
                let rounds =
                    hybrid
                    |> Option.map (fun h -> h.Decisions |> List.filter (fun d -> d.QuantumCircuits > 0))
                    |> Option.defaultValue []

                let matches (pick: Decision -> float option) =
                    rounds
                    |> List.filter (fun d ->
                        match pick d, d.ExactScore with
                        | Some s, Some x -> s >= x - 1e-6
                        | _ -> false)
                    |> List.length

                sw.Stop()

                Report.writeFiles outDir scenario results hybrid

                // Evidence for the proposed design, or the only policy run.
                let evidence =
                    let configPath =
                        Cli.getOr "vehicle-config" "examples/Drones/_data/fire_vehicle_config.csv" args

                    // The operator's configuration baseline: without it the
                    // failsafe items stay NOT EVIDENCED.
                    let vehicleConfig =
                        if File.Exists configPath then
                            let rows, _ = Data.readCsvWithHeaderWithErrors configPath

                            let settings =
                                rows
                                |> List.choose (fun r ->
                                    match r.Values.TryFind "setting", r.Values.TryFind "value" with
                                    | Some k, Some v -> Some(k.Trim(), v.Trim().ToLowerInvariant())
                                    | _ -> None)
                                |> Map.ofList

                            Some(configPath, settings)
                        else
                            printfn
                                "⚠ no vehicle configuration baseline at %s: failsafe evidence stays NOT EVIDENCED"
                                configPath

                            None

                    Evidence.build
                        scenario
                        settings
                        (max 1 (Cli.getIntOr "pilots" 1 args))
                        vehicleConfig
                        (defaultArg hybrid results.Head)

                FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence.print evidence
                FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence.write outDir evidence

                Reporting.writeJson
                    (Path.Combine(outDir, "metrics.json"))
                    {
                        run_id = runId
                        minutes = settings.Minutes
                        fleet_model = fleet.Model
                        fleet_size = fleet.Count
                        sources = sources.Length
                        sectors = sectors.Length
                        max_corridors = settings.MaxCorridors
                        crews = settings.Limits.Crews
                        coordinators = settings.Limits.Coordinators
                        qaoa_layers = settings.Qaoa.Layers
                        qaoa_latency_min = settings.LatencyMin
                        policies = summaries
                        qaoa_rounds = rounds.Length
                        qaoa_warm_rounds = rounds |> List.filter (fun d -> d.WarmStarted) |> List.length
                        qaoa_circuits = rounds |> List.sumBy (fun d -> d.QuantumCircuits)
                        qaoa_ms = rounds |> List.sumBy (fun d -> d.QuantumMs)
                        qaoa_matched_exact = matches (fun d -> d.QuantumScore)
                        greedy_matched_exact = matches (fun d -> d.GreedyScore)
                        elapsed_ms = sw.ElapsedMilliseconds
                    }

                if not rounds.IsEmpty then
                    printfn ""

                    printfn
                        "QAOA: %d rounds (%d warm-started), %d circuits, %d ms; matched the exact oracle in %d/%d rounds (greedy: %d/%d)"
                        rounds.Length
                        (rounds |> List.filter (fun d -> d.WarmStarted) |> List.length)
                        (rounds |> List.sumBy (fun d -> d.QuantumCircuits))
                        (rounds |> List.sumBy (fun d -> d.QuantumMs))
                        (matches (fun d -> d.QuantumScore))
                        rounds.Length
                        (matches (fun d -> d.GreedyScore))
                        rounds.Length

                printfn ""
                printfn "Results written to: %s" outDir
                0
