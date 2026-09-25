/// Slow loop, quantum: choose which corridors to open with QAOA.
///
/// One qubit per candidate corridor. The value part of the QUBO is the
/// second-order expansion of the fast loop's own score:
///
///     E(x) = - sum_i V_i x_i  +  sum_{i<j} (V_i + V_j - S_ij) x_i x_j
///
/// where V_i is corridor i's value on its own and S_ij the value of opening i
/// and j together. The pair term is what the two corridors do to each other:
/// negative where they compete (a shared lake's fill slots, a sector's demand,
/// the fleet, a lane crossing), positive synergy where only together they
/// reach a sector's concentration floor. It is exact for plans of up to two
/// corridors and an approximation beyond, so every sampled plan is re-scored
/// by the real evaluator and only the best re-scored plan is returned.
///
/// The people on the ground add penalty terms: one extra qubit per lake ("a
/// crew is there") with x_i <= y_lake and sum y = crews, and sum x = drop
/// coordinators. Those are equality penalties standing in for "at most"; the
/// evaluator enforces the real inequality on every sample.
///
/// ADAPTING ON THE FLY: consecutive re-plans solve QUBOs of the same shape with
/// slightly different coefficients. After the first cold grid search the planner
/// reuses the previous QAOA angles and only probes their neighbourhood
/// (5 circuits instead of 40). This is parameter transfer; it only works
/// because the QUBO is normalised to the same scale every round.
module FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.QuantumPlanner

open System
open System.Diagnostics
open System.Threading

open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

open FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.AirBridge

type QaoaSettings =
    {
        Layers: int
        SearchShots: int
        FinalShots: int
        /// Distinct sampled plans re-scored by the evaluator.
        TopCandidates: int
    }

type QuantumOutcome =
    {
        Plan: bool[]
        Parameters: (float * float)[]
        Qubits: int
        Circuits: int
        WarmStarted: bool
        DistinctSamples: int
        ElapsedMs: int64
    }

/// QUBO over the candidate corridors: qubits 0..K-1 are corridors; when the
/// candidates use more lakes than there are crews, qubits K.. are "a crew is
/// at lake s" and carry the crew limit.
type PlannerQubo =
    {
        Terms: Map<int * int, float>
        Qubits: int
        CorridorQubits: int
    }

/// (sum_{i in vars} x_i - target)^2 * weight, constant dropped:
/// weight * [ (1 - 2 target) sum x_i + 2 sum_{i<j} x_i x_j ].
let private exactlyTerms (vars: int list) (target: int) (weight: float) =
    [
        for i in vars do
            ((i, i), weight * (1.0 - 2.0 * float target))

            for j in vars do
                if i < j then
                    ((i, j), 2.0 * weight)
    ]

/// Build the normalised QUBO (upper-triangle keys) for the candidate corridors.
let buildQubo (ctx: EvalContext) : PlannerQubo =
    let k = ctx.Corridors.Length

    let open' (idx: int list) =
        Array.init k (fun m -> List.contains m idx)

    let single = Array.init k (fun i -> Evaluate.scoreRelaxed ctx (open' [ i ]))

    let valueTerms =
        [
            for i in 0 .. k - 1 do
                ((i, i), -single.[i])

                for j in i + 1 .. k - 1 do
                    let interaction =
                        single.[i] + single.[j] - Evaluate.scoreRelaxed ctx (open' [ i; j ])

                    if abs interaction > 1e-9 then
                        ((i, j), interaction)
        ]

    // Penalties must outweigh any value a violation could buy.
    let penalty = 2.0 * (single |> Array.fold max 1.0)

    let lakes =
        ctx.Corridors
        |> Array.map (fun c -> c.SourceIdx)
        |> Array.distinct
        |> Array.sort

    let lakeQubit =
        if lakes.Length > ctx.Limits.Crews then
            lakes |> Array.mapi (fun n s -> (s, k + n)) |> Map.ofArray
        else
            Map.empty

    let crewTerms =
        if lakeQubit.IsEmpty then
            []
        else
            [
                // A corridor may only fly from a lake with a crew: x_i (1 - y_s).
                for i in 0 .. k - 1 do
                    let y = lakeQubit.[ctx.Corridors.[i].SourceIdx]
                    ((i, i), penalty)
                    ((i, y), -penalty)

                yield! exactlyTerms (lakeQubit |> Map.toList |> List.map snd) ctx.Limits.Crews penalty
            ]

    // Asking for exactly as many corridors as coordinators: with positive
    // values more corridors never hurt, and the evaluator re-scores anyway.
    let coordinatorTerms =
        if k > ctx.Limits.Coordinators then
            exactlyTerms [ 0 .. k - 1 ] ctx.Limits.Coordinators penalty
        else
            []

    // Conflicting lanes are refused by the evaluator; the QUBO says so too.
    let conflictTerms = [ for i, j in ctx.CrossPairs -> ((i, j), penalty) ]

    let terms =
        (valueTerms @ crewTerms @ coordinatorTerms @ conflictTerms)
        |> List.groupBy fst
        |> List.map (fun (key, vs) -> (key, vs |> List.sumBy snd))

    let scale =
        terms
        |> List.map (snd >> abs)
        |> List.fold max 0.0
        |> fun m -> if m > 0.0 then m else 1.0

    {
        Terms = terms |> List.map (fun (key, v) -> (key, v / scale)) |> Map.ofList
        Qubits = k + lakeQubit.Count
        CorridorQubits = k
    }

let private gammaGrid = [| 0.1; 0.25; 0.5; 0.75; 1.0; 1.5; 2.0; 2.5 |]
let private betaGrid = [| 0.1; 0.2; 0.3; 0.4; 0.6 |]

let private run
    (backend: IQuantumBackend)
    (k: int)
    (qubo: Map<int * int, float>)
    (parameters: (float * float)[])
    shots
    =
    QaoaExecutionHelpers.executeQaoaCircuitSparseAsync backend k qubo parameters shots CancellationToken.None
    |> Async.AwaitTask
    |> Async.RunSynchronously

let private meanEnergy qubo (samples: int[][]) =
    samples |> Array.averageBy (QaoaExecutionHelpers.evaluateQuboSparse qubo)

/// Run QAOA over the candidate corridors. `warm` carries the previous round's
/// angles; None forces a cold grid search.
let solve
    (backend: IQuantumBackend)
    (settings: QaoaSettings)
    (warm: (float * float)[] option)
    (ctx: EvalContext)
    : Result<QuantumOutcome, QuantumError> =
    let sw = Stopwatch.StartNew()
    let qubo = buildQubo ctx
    let n = qubo.Qubits
    let layered (g, b) = Array.create settings.Layers (g, b)

    let warmStarted, probes =
        match warm with
        | Some ps when ps.Length = settings.Layers ->
            let scaleAll fg fb =
                ps |> Array.map (fun (g, b) -> (g * fg, b * fb))

            (true, [ ps; scaleAll 0.8 1.0; scaleAll 1.25 1.0; scaleAll 1.0 0.8; scaleAll 1.0 1.25 ])
        | _ ->
            (false,
             [
                 for g in gammaGrid do
                     for b in betaGrid do
                         layered (g, b)
             ])

    // Pick the angles with the lowest mean energy (the QAOA objective).
    let searched =
        probes
        |> List.map (fun ps ->
            run backend n qubo.Terms ps settings.SearchShots
            |> Result.map (fun s -> (ps, meanEnergy qubo.Terms s)))

    match
        searched
        |> List.choose (function
            | Ok r -> Some r
            | Error _ -> None)
    with
    | [] ->
        searched
        |> List.tryPick (function
            | Error e -> Some e
            | Ok _ -> None)
        |> Option.defaultValue (QuantumError.Other "QAOA produced no results")
        |> Error
    | results ->
        let bestParams = results |> List.minBy snd |> fst

        run backend n qubo.Terms bestParams settings.FinalShots
        |> Result.map (fun samples ->
            let distinct = samples |> Array.distinctBy (fun bits -> String.Join("", bits))

            let plan =
                distinct
                |> Array.sortBy (QaoaExecutionHelpers.evaluateQuboSparse qubo.Terms)
                |> Array.truncate settings.TopCandidates
                |> Array.map (fun bits -> Array.init qubo.CorridorQubits (fun i -> bits.[i] = 1))
                |> Array.maxBy (Evaluate.score ctx)

            {
                Plan = plan
                Parameters = bestParams
                Qubits = n
                Circuits = probes.Length + 1
                WarmStarted = warmStarted
                DistinctSamples = distinct.Length
                ElapsedMs = sw.ElapsedMilliseconds
            })
