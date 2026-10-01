namespace FSharp.Azure.Quantum.Quantum

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GraphOptimization

/// Quantum Network Flow Solver using QAOA and Backend Abstraction
///
/// ALGORITHM-LEVEL API (for advanced users):
/// This module provides direct access to quantum network flow optimization via QAOA.
/// Solves min-cost flow problems on directed graphs with capacity constraints.
///
/// RULE 1 COMPLIANCE:
/// ✅ Requires IQuantumBackend parameter (explicit quantum execution)
///
/// TECHNICAL DETAILS:
/// - Execution: Quantum hardware/simulator via backend
/// - Algorithm: QAOA (Quantum Approximate Optimization Algorithm)
/// - Speed: Seconds to minutes (includes job queue wait for cloud backends)
/// - Cost: ~$10-100 per run on real quantum hardware (IonQ, Rigetti)
/// - LocalBackend: Free simulation (limited to ~16 qubits)
///
/// QUANTUM PIPELINE:
/// 1. Network Flow Problem → QUBO Matrix (min-cost flow encoding)
/// 2. QUBO → QAOA Circuit (Hamiltonians + Layers)
/// 3. Execute on Quantum Backend (IonQ/Rigetti/Local)
/// 4. Decode Measurements → Flow Assignments
/// 5. Return Best Solution
///
/// Example:
///   let backend = LocalBackend() :> IQuantumBackend
///   match! QuantumNetworkFlowSolver.solveWithShotsAsync backend problem 1000 CancellationToken.None with
///   | Ok solution -> printfn "Total cost: %f" solution.TotalCost
///   | Error msg -> printfn "Error: %s" msg
module QuantumNetworkFlowSolver =

    // ================================================================================
    // PROBLEM DEFINITION
    // ================================================================================

    /// Network flow problem specification
    type NetworkFlowProblem =
        {
            /// Source nodes (suppliers)
            Sources: string list

            /// Sink nodes (customers/demand points)
            Sinks: string list

            /// Intermediate nodes (warehouses, distributors)
            IntermediateNodes: string list

            /// All edges with transport costs
            Edges: Edge<float> list

            /// Node capacities (max flow through node)
            Capacities: Map<string, int>

            /// Demand at each sink node
            Demands: Map<string, int>

            /// Supply at each source node
            Supplies: Map<string, int>
        }

    /// Network flow solution result
    type NetworkFlowSolution =
        {
            /// Edges selected for flow (with flow amounts)
            SelectedEdges: Edge<float> list

            /// Total cost of flow
            TotalCost: float

            /// Flow amounts on each selected edge
            FlowAmounts: Map<(string * string), float>

            /// Total demand satisfied
            DemandSatisfied: float

            /// Total demand required
            TotalDemand: float

            /// Fill rate (demand satisfied / total demand)
            FillRate: float

            /// Backend used for execution
            BackendName: string

            /// Number of measurement shots
            NumShots: int

            /// Execution time in milliseconds
            ElapsedMs: float

            /// QUBO objective value (energy)
            BestEnergy: float

            /// Standing of this solution among the final samples; None when no sampling run produced it
            Sampling: QaoaExecutionHelpers.SampleStatistics option
        }

    // ================================================================================
    // QUBO ENCODING FOR MIN-COST FLOW
    // ================================================================================

    /// Encode min-cost flow problem as QUBO
    ///
    /// Variables: x_e = 1 if edge e is selected for flow
    /// Objective: Minimize Σ (cost_e * x_e)
    ///
    /// Constraints (as penalty terms):
    /// 1. Flow conservation: For each intermediate node, inflow = outflow
    /// 2. Demand satisfaction: Each sink receives exactly its demand
    ///
    /// Supply limits and node capacities are inequalities and are not in the QUBO;
    /// every decoded sample is checked against them classically before it can be returned.
    let toQubo (problem: NetworkFlowProblem) : Result<QuboMatrix, QuantumError> =
        try
            // Create node index mapping
            let allNodes =
                problem.Sources @ problem.IntermediateNodes @ problem.Sinks |> List.distinct

            let nodeIndexMap = allNodes |> List.mapi (fun i node -> node, i) |> Map.ofList

            // Create edge index mapping (one variable per edge)
            let edgeIndexMap =
                problem.Edges
                |> List.mapi (fun i edge -> (edge.Source, edge.Target), i)
                |> Map.ofList

            let numEdges = problem.Edges.Length
            let numVars = numEdges

            if numVars = 0 then
                Error(QuantumError.ValidationError("numEdges", "Network flow problem has no edges"))
            else
                // Penalty weight for constraint violations using Lucas Rule
                let penaltyWeight =
                    let maxCost = problem.Edges |> List.map (fun e -> e.Weight) |> List.max
                    let numNodes = allNodes.Length
                    Qubo.computeLucasPenalties maxCost numNodes

                // ========================================================================
                // OBJECTIVE: Minimize total transport cost
                // ========================================================================

                // Functional accumulation: Linear terms: cost_e * x_e → diagonal Q[i,i] = cost_e
                let objectiveTerms =
                    problem.Edges
                    |> List.choose (fun edge ->
                        Map.tryFind (edge.Source, edge.Target) edgeIndexMap
                        |> Option.map (fun edgeIdx -> ((edgeIdx, edgeIdx), edge.Weight)))

                // ========================================================================
                // CONSTRAINT 1: Flow Conservation (intermediate nodes)
                // For each intermediate node: inflow - outflow = 0
                // Penalty: (Σ x_in - Σ x_out)^2
                // ========================================================================

                let flowConservationTerms =
                    problem.IntermediateNodes
                    |> List.collect (fun node ->
                        // Find incoming edges
                        let incomingEdges =
                            problem.Edges
                            |> List.filter (fun e -> e.Target = node)
                            |> List.choose (fun e -> Map.tryFind (e.Source, e.Target) edgeIndexMap)

                        // Find outgoing edges
                        let outgoingEdges =
                            problem.Edges
                            |> List.filter (fun e -> e.Source = node)
                            |> List.choose (fun e -> Map.tryFind (e.Source, e.Target) edgeIndexMap)

                        // Functional collection: penalty terms for (inflow - outflow)^2
                        // Expansion: (Σ x_in)^2 - 2*(Σ x_in)*(Σ x_out) + (Σ x_out)^2
                        //
                        // (Σ x_e)^2 = Σ x_e (diagonal, since x² = x) + 2·Σ_{i<j} x_i·x_j.
                        // Iterate UNORDERED pairs only: iterating all ORDERED pairs with 2λ
                        // each and normalising both orderings to the same upper-triangle key
                        // would accumulate 4λ per pair — punishing perfectly balanced flows.
                        // Hand check (2-in {a,b} / 2-out {c,d}):
                        //   (x_a+x_b-x_c-x_d)² = Σ x + 2x_a x_b + 2x_c x_d
                        //                        - 2x_a x_c - 2x_a x_d - 2x_b x_c - 2x_b x_d
                        // so the balanced flow a=c=1, b=d=0 scores λ+λ-2λ = 0.
                        let squaredTerms (edges: int list) =
                            let diagonal = edges |> List.map (fun i -> ((i, i), penaltyWeight))

                            let offDiagonal =
                                [
                                    for i in edges do
                                        for j in edges do
                                            if i < j then
                                                yield ((i, j), 2.0 * penaltyWeight)
                                ]

                            diagonal @ offDiagonal

                        let incomingSquared = squaredTerms incomingEdges

                        let crossTerms =
                            [
                                for i in incomingEdges do
                                    for j in outgoingEdges do
                                        let (vi, vj) = if i <= j then (i, j) else (j, i)
                                        ((vi, vj), -2.0 * penaltyWeight)
                            ]

                        let outgoingSquared = squaredTerms outgoingEdges

                        incomingSquared @ crossTerms @ outgoingSquared)

                // ========================================================================
                // CONSTRAINT 2: Demand Satisfaction (sink nodes)
                // For each sink: Σ x_in = demand
                // ========================================================================

                let sinkDemandTerms =
                    problem.Sinks
                    |> List.collect (fun sink ->
                        let demand = Map.tryFind sink problem.Demands |> Option.defaultValue 1

                        // Find incoming edges to this sink
                        let incomingEdges =
                            problem.Edges
                            |> List.filter (fun e -> e.Target = sink)
                            |> List.choose (fun e -> Map.tryFind (e.Source, e.Target) edgeIndexMap)

                        // Penalty λ (demand − Σ x_in)². For binary x it expands to
                        // λ (1 − 2·demand) on each diagonal and 2λ on each unordered pair; the
                        // constant λ·demand² shifts every state alike and is left out. A sink
                        // that receives more than its demand is penalised like one that
                        // receives less, so the lowest-energy state is a valid flow.
                        let diagonal =
                            incomingEdges
                            |> List.map (fun i -> ((i, i), penaltyWeight * (1.0 - 2.0 * float demand)))

                        let pairs =
                            [
                                for i in incomingEdges do
                                    for j in incomingEdges do
                                        if i < j then
                                            yield ((i, j), 2.0 * penaltyWeight)
                            ]

                        diagonal @ pairs)

                // ========================================================================
                // Build QUBO Matrix
                // ========================================================================

                // Combine all terms functionally
                let allQuboTerms = objectiveTerms @ flowConservationTerms @ sinkDemandTerms

                // Aggregate terms with same indices (add coefficients)
                let aggregatedTerms =
                    allQuboTerms
                    |> List.groupBy fst
                    |> List.map (fun (key, terms) ->
                        let totalCoeff = terms |> List.sumBy snd
                        key, totalCoeff)
                    |> Map.ofList

                Ok
                    {
                        NumVariables = numVars
                        Q = aggregatedTerms
                    }

        with ex ->
            Error(QuantumError.OperationError("QuboEncoding", $"Failed to encode network flow as QUBO: %s{ex.Message}"))

    // ================================================================================
    // SOLUTION DECODING
    // ================================================================================

    /// Classical validation of a decoded flow (1 unit of flow per selected edge).
    /// A decoded edge set is a valid flow when:
    ///   - flow is conserved at every intermediate node (inflow = outflow),
    ///   - node capacities are respected (flow through the node <= capacity),
    ///   - no source ships more than its supply,
    ///   - no sink receives more than its demand.
    /// The QUBO penalties only BIAS sampling toward such states; measurements must
    /// still be checked classically before one can be returned as the solution.
    let private isValidFlow (problem: NetworkFlowProblem) (selectedEdges: Edge<float> list) : bool =
        let inflow node =
            selectedEdges |> List.filter (fun e -> e.Target = node) |> List.length

        let outflow node =
            selectedEdges |> List.filter (fun e -> e.Source = node) |> List.length

        let conservationOk =
            problem.IntermediateNodes
            |> List.forall (fun node -> inflow node = outflow node)

        let capacityOk =
            problem.Capacities
            |> Map.forall (fun node capacity -> max (inflow node) (outflow node) <= capacity)

        let supplyOk =
            problem.Sources
            |> List.forall (fun source ->
                match Map.tryFind source problem.Supplies with
                | Some supply -> outflow source <= supply
                | None -> true)

        let demandOk =
            problem.Sinks
            |> List.forall (fun sink ->
                match Map.tryFind sink problem.Demands with
                | Some demand -> inflow sink <= demand
                | None -> true)

        conservationOk && capacityOk && supplyOk && demandOk

    /// Decode QUBO solution bitstring to network flow solution
    let private decodeSolution (problem: NetworkFlowProblem) (bitstring: int array) : NetworkFlowSolution option =

        // Create edge index mapping
        let edgeIndexMap = problem.Edges |> List.mapi (fun i edge -> i, edge) |> Map.ofList

        // Extract selected edges (where bit = 1)
        let selectedEdges =
            bitstring
            |> Array.mapi (fun i bit -> i, bit)
            |> Array.filter (fun (_, bit) -> bit = 1)
            |> Array.choose (fun (idx, _) -> Map.tryFind idx edgeIndexMap)
            |> Array.toList

        if selectedEdges.IsEmpty then
            None
        else
            // Calculate total cost
            let totalCost = selectedEdges |> List.sumBy (fun e -> e.Weight)

            // Calculate flow amounts (simplified: 1 unit per selected edge)
            let flowAmounts =
                selectedEdges |> List.map (fun e -> (e.Source, e.Target), 1.0) |> Map.ofList

            // Calculate demand satisfaction
            let totalDemand = problem.Demands |> Map.toList |> List.sumBy snd |> float

            // Actual delivered quantity per sink: inflow units (1 per selected edge),
            // capped at that sink's demand so FillRate can never exceed 1.0.
            let demandSatisfied =
                problem.Sinks
                |> List.sumBy (fun sink ->
                    let inflow = selectedEdges |> List.filter (fun e -> e.Target = sink) |> List.length
                    let demand = Map.tryFind sink problem.Demands |> Option.defaultValue 0
                    float (min inflow demand))

            let fillRate =
                if totalDemand > 0.0 then
                    demandSatisfied / totalDemand
                else
                    0.0

            Some
                {
                    SelectedEdges = selectedEdges
                    TotalCost = totalCost
                    FlowAmounts = flowAmounts
                    DemandSatisfied = demandSatisfied
                    TotalDemand = totalDemand
                    FillRate = fillRate
                    BackendName = "" // Will be set by caller
                    NumShots = 0 // Will be set by caller
                    ElapsedMs = 0.0 // Will be set by caller
                    BestEnergy = totalCost
                    Sampling = None
                }

    // ================================================================================
    // QUANTUM SOLVER
    // ================================================================================

    /// Configuration for quantum network flow solving
    type QuantumFlowConfig =
        {
            /// Number of shots for execution
            NumShots: int

            /// QAOA angles (gamma, beta), in units of the normalised cost Hamiltonian
            /// (minimisation convention, see Core.QaoaCircuit)
            InitialParameters: float * float

            /// Optional progress reporter for QAOA iterations
            ProgressReporter: Progress.IProgressReporter option
        }

    /// Default configuration. The angles are single-layer values that concentrate
    /// sampling on valid flows whatever the cost scale (the cost Hamiltonian is normalised).
    let defaultConfig =
        {
            NumShots = 1000
            InitialParameters = (0.3, 0.3)
            ProgressReporter = None
        }

    let private report (reporter: Progress.IProgressReporter option) (phase: string) (detail: string) =
        reporter
        |> Option.iter (fun r -> r.Report(Progress.PhaseChanged(phase, Some detail)))

    /// The valid flow that meets the most demand, the cheapest among equals, of the samples.
    let private selectFlow
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: NetworkFlowProblem)
        (shots: int)
        (stopwatch: Stopwatch)
        (reporter: Progress.IProgressReporter option)
        (measurements: int array array)
        : Result<NetworkFlowSolution, QuantumError> =
        report reporter "Network Flow QAOA" "Decoding solutions..."

        // Decode every measurement, then keep only CLASSICALLY VALID flows
        // (conservation at intermediate nodes, node capacities, supply and
        // demand limits): a low-cost bitstring need not be a flow at all.
        let flowResults =
            measurements
            |> Array.choose (decodeSolution problem)
            |> Array.filter (fun sol -> isValidFlow problem sol.SelectedEdges)

        if flowResults.Length = 0 then
            Error(
                QuantumError.OperationError(
                    "DecodeSolution",
                    "No valid network flow solutions found in quantum measurements"
                )
            )
        else
            // Validity only bounds flows from above, so a flow serving one
            // customer is valid too, and cheaper than one serving all.
            // Most demand met wins; cost breaks ties.
            let bestSolution =
                flowResults |> Array.minBy (fun sol -> (-sol.DemandSatisfied, sol.TotalCost))

            report reporter "Network Flow Complete" $"Found solution with cost {bestSolution.TotalCost:F2}"

            let sampling: QaoaExecutionHelpers.SampleStatistics =
                {
                    Shots = measurements.Length
                    Qubits = problem.Edges.Length
                    Hits =
                        flowResults
                        |> Array.filter (fun sol -> sol.SelectedEdges = bestSolution.SelectedEdges)
                        |> Array.length
                    Valid = flowResults.Length
                }

            Ok
                { bestSolution with
                    BackendName = backend.Name
                    NumShots = shots
                    ElapsedMs = stopwatch.Elapsed.TotalMilliseconds
                    Sampling = Some sampling
                }

    /// Encode the problem, let `chooseParameters` pick the QAOA angles for its QUBO,
    /// sample `shots` times at those angles and select the flow.
    let private solveCoreAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: NetworkFlowProblem)
        (shots: int)
        (reporter: Progress.IProgressReporter option)
        (chooseParameters: float[,] -> Task<Result<(float * float)[], QuantumError>>)
        (cancellationToken: CancellationToken)
        : Task<Result<NetworkFlowSolution, QuantumError>> =
        let stopwatch = Stopwatch.StartNew()
        let numEdges = problem.Edges.Length

        if numEdges = 0 then
            Task.FromResult(Error(QuantumError.ValidationError("numEdges", "Network flow problem has no edges")))
        elif shots <= 0 then
            Task.FromResult(Error(QuantumError.ValidationError("numShots", "Number of shots must be positive")))
        else
            task {
                try
                    report reporter "Network Flow QAOA" $"Encoding {numEdges} edges to QUBO..."

                    match toQubo problem with
                    | Error err -> return Error err
                    | Ok quboMatrix ->
                        let quboArray = Qubo.toDenseArray quboMatrix.NumVariables quboMatrix.Q
                        report reporter "Network Flow QAOA" "Choosing QAOA angles..."

                        match! chooseParameters quboArray with
                        | Error err -> return Error err
                        | Ok parameters ->
                            report reporter "Network Flow QAOA" $"Executing on {backend.Name}..."

                            match!
                                QaoaExecutionHelpers.executeFromQuboAsync
                                    backend
                                    quboArray
                                    parameters
                                    shots
                                    cancellationToken
                            with
                            | Error err -> return Error err
                            | Ok measurements -> return selectFlow backend problem shots stopwatch reporter measurements
                with ex when not (ex :? OperationCanceledException) ->
                    return
                        Error(
                            QuantumError.OperationError(
                                "QuantumNetworkFlowSolver",
                                $"Quantum network flow solver failed: %s{ex.Message}"
                            )
                        )
            }

    /// Solve network flow problem using quantum backend via QAOA at the given angles.
    ///
    /// Full Pipeline:
    /// 1. Network flow problem → QUBO matrix (min-cost flow encoding)
    /// 2. QUBO → single-layer QAOA circuit at config.InitialParameters (one circuit, no search)
    /// 3. Execute circuit on quantum backend asynchronously
    /// 4. Decode measurements → flow assignments
    /// 5. Return the valid flow that meets the most demand, the cheapest among equals
    ///
    /// Parameters:
    ///   backend - Quantum backend to execute on (LocalBackend, IonQ, Rigetti)
    ///   problem - Network flow problem specification
    ///   config - Configuration for execution
    ///
    /// Returns:
    ///   Task<Result<NetworkFlowSolution, QuantumError>> - Task with result or error
    let solveAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: NetworkFlowProblem)
        (config: QuantumFlowConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<NetworkFlowSolution, QuantumError>> =
        solveCoreAsync
            backend
            problem
            config.NumShots
            config.ProgressReporter
            (fun _ -> Task.FromResult(Ok [| config.InitialParameters |]))
            cancellationToken

    /// Solve network flow with QAOA angles chosen for the problem: the shared QAOA
    /// configuration decides the layers and whether the angles come from Nelder-Mead
    /// optimisation or a grid search, then config.FinalShots samples are taken at those
    /// angles. Every angle evaluation is one backend execution, so on a cloud backend
    /// this submits many jobs; `solveAsync` submits one.
    let solveWithQaoaConfigAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: NetworkFlowProblem)
        (config: QaoaExecutionHelpers.QaoaSolverConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<NetworkFlowSolution, QuantumError>> =
        let chooseParameters (qubo: float[,]) =
            task {
                match! QaoaExecutionHelpers.runQaoaAsync backend qubo config cancellationToken with
                | Error err -> return Error err
                | Ok(_, Some parameters, _) -> return Ok parameters
                | Ok(_, None, _) -> return Ok [| defaultConfig.InitialParameters |]
            }

        solveCoreAsync backend problem config.FinalShots None chooseParameters cancellationToken

    /// Solve network flow with custom number of shots (asynchronous).
    ///
    /// On a backend that returns exact states (a simulator) the angles are optimised for
    /// the problem (QaoaExecutionHelpers.defaultConfig: two layers, Nelder-Mead). On a
    /// shot-sampling backend, where each evaluation is a separately billed job, one
    /// circuit runs at the default angles; ask for optimisation there with
    /// solveWithQaoaConfigAsync.
    let solveWithShotsAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: NetworkFlowProblem)
        (numShots: int)
        (cancellationToken: CancellationToken)
        : Task<Result<NetworkFlowSolution, QuantumError>> =
        match backend with
        | :? BackendAbstraction.IShotSamplingBackend ->
            solveAsync
                backend
                problem
                { defaultConfig with
                    NumShots = numShots
                }
                cancellationToken
        | _ ->
            solveWithQaoaConfigAsync
                backend
                problem
                { QaoaExecutionHelpers.defaultConfig with
                    FinalShots = numShots
                }
                cancellationToken

    /// Solve network flow with default configuration (asynchronous); see solveWithShotsAsync.
    let solveWithDefaultsAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: NetworkFlowProblem)
        (cancellationToken: CancellationToken)
        : Task<Result<NetworkFlowSolution, QuantumError>> =
        solveWithShotsAsync backend problem defaultConfig.NumShots cancellationToken
