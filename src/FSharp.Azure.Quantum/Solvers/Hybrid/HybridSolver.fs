namespace FSharp.Azure.Quantum

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core

/// Hybrid Solver - Orchestration layer that automatically routes problems
/// to either classical solvers OR quantum solvers based on problem analysis.
///
/// DECISION FRAMEWORK:
/// - Analyzes problem size, structure, and complexity
/// - Estimates quantum advantage potential
/// - Routes to classical solver (fast, free) or quantum backend (scalable, expensive)
/// - Optionally compares both methods for validation
///
/// SUPPORTED PROBLEM TYPES:
/// - TSP (Traveling Salesman Problem)
/// - Portfolio Optimization
/// - MaxCut (Graph Partitioning)
/// - Knapsack (0/1 Knapsack)
/// - Graph Coloring (K-Coloring)
///
/// CLASSICAL ROUTING:
/// - Small problems (< 50 variables): TspSolver, PortfolioSolver, etc.
/// - Executes on CPU (milliseconds, $0 cost)
///
/// QUANTUM ROUTING:
/// - Large problems (> 100 variables): Quantum solvers with backend parameter
/// - Executes on Azure Quantum (seconds to minutes, ~$10-100 cost)
///
/// Example:
///   task {
///       // TSP
///       match! HybridSolver.solveTspAsync distances None None None cancellationToken with
///       | Ok solution ->
///           printfn "Method: %A" solution.Method  // Classical or Quantum
///           printfn "Reasoning: %s" solution.Reasoning
///       | Error err -> printfn "Error: %s" err.Message
///
///       // MaxCut
///       match! HybridSolver.solveMaxCutAsync problem None None None cancellationToken with
///       | Ok solution -> printfn "Cut Value: %f" solution.Result.CutValue
///       | Error err -> printfn "Error: %s" err.Message
///
///       // Knapsack
///       match! HybridSolver.solveKnapsackAsync problem None None None cancellationToken with
///       | Ok solution -> printfn "Total Value: %f" solution.Result.TotalValue
///       | Error err -> printfn "Error: %s" err.Message
///
///       // Graph Coloring
///       match! HybridSolver.solveGraphColoringAsync problem 3 None None None cancellationToken with
///       | Ok solution -> printfn "Colors Used: %d" solution.Result.ColorsUsed
///       | Error err -> printfn "Error: %s" err.Message
///   }
///
/// The synchronous entry points (solveTsp, solveMaxCut, ...) block the calling thread
/// until the solver completes and are obsolete; use the ...Async variants.
///
/// ALL HYBRID SOLVER CODE IN SINGLE FILE (per TKT-26 requirements)
module HybridSolver =

    open FSharp.Azure.Quantum.Core.BackendAbstraction

    // ================================================================================
    // CORE TYPES
    // ================================================================================

    /// Solver method used for solving the problem
    [<Struct>]
    type SolverMethod =
        | Classical
        | Quantum

    /// Quantum backend selection (IonQ or Rigetti)
    type QuantumBackend =
        | IonQ of targetId: string
        | Rigetti of targetId: string

    /// Configuration for quantum execution
    type QuantumExecutionConfig =
        {
            /// Backend selection (IonQ or Rigetti)
            Backend: QuantumBackend

            /// Azure Quantum workspace ID
            WorkspaceId: string

            /// Azure location (e.g., "eastus")
            Location: string

            /// Azure resource group name
            ResourceGroup: string

            /// Azure subscription ID
            SubscriptionId: string

            /// Maximum cost limit in USD (optional guard)
            MaxCostUSD: float voption

            /// Enable comparison with classical solver
            EnableComparison: bool
        }

    /// Unified solution result from hybrid solver
    type Solution<'TResult> =
        {
            /// Method used to solve the problem (Classical or Quantum)
            Method: SolverMethod

            /// The actual solution result
            Result: 'TResult

            /// Human-readable reasoning for the solver selection
            Reasoning: string

            /// Time elapsed during solving (milliseconds)
            ElapsedMs: float

            /// Quantum Advisor recommendation (if available)
            Recommendation: QuantumAdvisor.Recommendation option
        }

    /// Comparison result between quantum and classical solutions
    type SolutionComparison<'TResult> =
        {
            /// Quantum solution
            QuantumSolution: Solution<'TResult>

            /// Classical solution (for comparison)
            ClassicalSolution: Solution<'TResult>

            /// Quantum cost in USD
            QuantumCost: float

            /// Whether quantum showed advantage over classical
            QuantumAdvantageObserved: bool

            /// Quality comparison notes
            ComparisonNotes: string
        }

    // ================================================================================
    // HELPER FUNCTIONS
    // ================================================================================

    let private createSolution<'T>
        (methodUsed: SolverMethod)
        (result: 'T)
        (reasoning: string)
        (startTime: DateTime)
        (recommendation: QuantumAdvisor.Recommendation option)
        =
        {
            Method = methodUsed
            Result = result
            Reasoning = reasoning
            ElapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds
            Recommendation = recommendation
        }

    /// Create a classical solution with standard timing
    let private createClassicalSolution<'T>
        (result: 'T)
        (reasoning: string)
        (startTime: DateTime)
        (recommendation: QuantumAdvisor.Recommendation option)
        =
        createSolution Classical result reasoning startTime recommendation

    let private createQuantumSolution<'T>
        (result: 'T)
        (reasoning: string)
        (startTime: DateTime)
        (recommendation: QuantumAdvisor.Recommendation option)
        =
        createSolution Quantum result reasoning startTime recommendation

    /// Report a failed quantum solver run as an OperationError of the named solver
    let private asOperationError (solverName: string) (run: Task<QuantumResult<'T>>) : Task<QuantumResult<'T>> =
        task {
            let! result = run

            return
                result
                |> Result.mapError (fun err -> QuantumError.OperationError(solverName, QuantumResult.toString err))
        }

    let private defaultHybridBackend () : IQuantumBackend =
        Backends.LocalBackend.LocalBackend() :> IQuantumBackend

    // ================================================================================
    // QUANTUM EXECUTION
    // ================================================================================

    /// Run the real quantum TSP solver (QAOA on a unified IQuantumBackend) and adapt the
    /// result to the classical TspSolution shape used by the legacy hybrid API.
    ///
    /// The legacy QuantumExecutionConfig only names a provider (IonQ/Rigetti) and carries no
    /// live HttpClient/credentials, so it cannot construct a cloud backend on its own. This
    /// executes genuine QAOA on the default unified backend (local simulator). To target a
    /// specific gate-based or topological cloud backend, use solveTspWithBackendAsync and pass it in.
    let private runQuantumTspCore
        (distances: float[,])
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<TspSolver.TspSolution>> =
        quantumResultTask {
            let backend = defaultHybridBackend ()

            let! quantumResult =
                QuantumTspSolver.solveAsync backend distances QuantumTspSolver.defaultConfig cancellationToken

            let solution: TspSolver.TspSolution =
                {
                    Tour = quantumResult.Tour
                    TourLength = quantumResult.TourLength
                    Iterations = 0 // Quantum solver doesn't track classical iterations
                    ElapsedMs = quantumResult.ElapsedMs
                }

            return solution
        }

    /// Execute TSP on the unified quantum backend using real QAOA (see runQuantumTspCore).
    let private executeQuantumTspTask
        (distances: float[,])
        (_quantumConfig: QuantumExecutionConfig)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<TspSolver.TspSolution>> =
        task {
            cancellationToken.ThrowIfCancellationRequested()
            return! runQuantumTspCore distances cancellationToken
        }

    // ================================================================================
    // COST ESTIMATION
    // ================================================================================

    /// Estimate the USD cost of one QAOA execution over `numVariables` qubits
    /// using the shared CostEstimation pricing tables.
    ///
    /// Models a single QAOA layer on a dense QUBO at 1000 shots: one Hadamard
    /// plus one mixer rotation per qubit, one two-qubit cost-Hamiltonian
    /// interaction per variable pair, and a full-register measurement.
    let internal estimateQaoaCostUSD (costBackend: CostEstimation.CostBackend) (numVariables: int) : float =

        let n = max 1 numVariables

        let profile: CostEstimation.CircuitCostProfile =
            {
                SingleQubitGates = (2 * n) * 1<CostEstimation.gate>
                TwoQubitGates = (n * (n - 1) / 2) * 1<CostEstimation.gate>
                Measurements = n * 1<CostEstimation.gate>
                QubitCount = n * 1<CostEstimation.qubit>
            }

        match CostEstimation.estimateCost costBackend profile 1000<CostEstimation.shot> with
        | Ok estimate -> float (estimate.ExpectedCost / 1.0M<CostEstimation.USD>)
        | Error _ ->
            // Conservative: an unknown cost must never pass a budget check
            Double.PositiveInfinity

    /// Cost estimate for the legacy QuantumExecutionConfig backend selection
    let internal estimateQuantumConfigCostUSD (backend: QuantumBackend) (numVariables: int) : float =
        match backend with
        | IonQ _ -> estimateQaoaCostUSD (CostEstimation.CostBackend.IonQ false) numVariables
        | Rigetti _ -> estimateQaoaCostUSD CostEstimation.CostBackend.Rigetti numVariables

    /// Cost estimate for a unified IQuantumBackend: cloud hardware is priced
    /// by provider (recognised from the backend name); anything else is
    /// treated as local simulation, which costs nothing.
    let internal estimateBackendCostUSD (backend: BackendAbstraction.IQuantumBackend) (numVariables: int) : float =
        let name = backend.Name.ToLowerInvariant()

        if name.Contains "ionq" then
            estimateQaoaCostUSD (CostEstimation.CostBackend.IonQ false) numVariables
        elif name.Contains "rigetti" then
            estimateQaoaCostUSD CostEstimation.CostBackend.Rigetti numVariables
        elif name.Contains "quantinuum" then
            estimateQaoaCostUSD CostEstimation.CostBackend.Quantinuum numVariables
        else
            0.0

    // ================================================================================
    // SOLVER ROUTING - TSP
    // ================================================================================

    /// Solve TSP problem using hybrid solver with quantum execution support (task-based, non-blocking).
    ///
    /// Routes between the classical TSP solver and QAOA on the default unified backend,
    /// awaiting the quantum run instead of blocking on it.
    ///
    /// NOTE: The quantum path runs real QAOA on the default unified backend (local simulator).
    /// To target a specific cloud backend, use solveTspWithBackendAsync.
    let solveTspWithQuantumAsync
        (distances: float[,])
        (quantumConfig: QuantumExecutionConfig option)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<TspSolver.TspSolution>>> =
        let startTime = DateTime.UtcNow
        let config = TspSolver.defaultConfig

        let solveClassical () =
            TspSolver.solveWithDistances distances config

        match forceMethod with
        | Some Classical ->
            let res =
                solveClassical () |> createClassicalSolution
                <| "Classical solver forced by user override. Quantum Advisor bypassed."
                <| startTime
                <| None
                |> Ok

            Task.FromResult(res)

        | Some Quantum when quantumConfig.IsNone ->
            task {
                return
                    Error(
                        QuantumError.ValidationError(
                            "Configuration",
                            "Quantum method forced but no quantum configuration provided."
                        )
                    )
            }

        | Some Quantum ->
            task {
                let! quantumResult =
                    executeQuantumTspTask distances quantumConfig.Value cancellationToken

                match quantumResult with
                | Ok quantumResult ->
                    return
                        createQuantumSolution quantumResult "Quantum solver forced by user override." startTime None
                        |> Ok
                | Error err -> return Error err
            }
        | None ->
            let recommendation = QuantumAdvisor.getRecommendation distances

            match recommendation with
            | Error err -> task { return Error err }
            | Ok recommendation ->
                match recommendation.RecommendationType, quantumConfig with
                | QuantumAdvisor.RecommendationType.StronglyRecommendQuantum, Some qConfig ->
                    match qConfig.MaxCostUSD with
                    | ValueSome limit when recommendation.EstimatedClassicalTimeMs.IsSome ->
                        let estimatedCost =
                            estimateQuantumConfigCostUSD
                                qConfig.Backend
                                ((Array2D.length1 distances) * (Array2D.length1 distances))

                        if estimatedCost > limit then
                            let reasoning =
                                $"Quantum advantage detected but estimated cost (${estimatedCost:F2}) exceeds limit (${limit:F2}). Falling back to classical."

                            let res =
                                solveClassical () |> createClassicalSolution
                                <| reasoning
                                <| startTime
                                <| Some recommendation
                                |> Ok

                            Task.FromResult(res)
                        else
                            task {
                                let! quantumResult = executeQuantumTspTask distances qConfig cancellationToken

                                match quantumResult with
                                | Ok quantumResult ->
                                    return
                                        createQuantumSolution
                                            quantumResult
                                            $"{recommendation.Reasoning} Routing to quantum backend."
                                            startTime
                                            (Some recommendation)
                                        |> Ok
                                | Error err -> return Error err
                            }
                    | _ ->
                        task {
                            let! quantumResult = executeQuantumTspTask distances qConfig cancellationToken

                            match quantumResult with
                            | Ok quantumResult ->
                                return
                                    createQuantumSolution
                                        quantumResult
                                        $"{recommendation.Reasoning} Routing to quantum backend."
                                        startTime
                                        (Some recommendation)
                                    |> Ok
                            | Error err -> return Error err
                        }
                | QuantumAdvisor.RecommendationType.StronglyRecommendQuantum, None ->
                    let reasoning =
                        $"{recommendation.Reasoning} Quantum solver not available - using classical fallback."

                    let res =
                        solveClassical () |> createClassicalSolution
                        <| reasoning
                        <| startTime
                        <| Some recommendation
                        |> Ok

                    Task.FromResult(res)

                | _ ->
                    let reasoning = $"{recommendation.Reasoning} Routing to classical TSP solver."

                    let res =
                        solveClassical () |> createClassicalSolution
                        <| reasoning
                        <| startTime
                        <| Some recommendation
                        |> Ok

                    Task.FromResult(res)

    /// Solve TSP problem using hybrid solver with an explicit QAOA configuration
    /// (task-based, non-blocking).
    ///
    /// Use this over `solveTspWithBackendAsync` when the backend makes the default
    /// variational loop too expensive. `QuantumTspSolver.defaultConfig` runs up to
    /// 1000 Nelder-Mead iterations, each a full QAOA circuit execution — negligible
    /// on a state-vector simulator, but hours on a topological backend, whose
    /// fusion-tree state carries 2^n explicit terms per gate. Pass
    /// `QuantumTspSolver.fastConfig` (or a smaller MaxOptimizationIterations) there.
    ///
    /// Parameters:
    ///   distances - Distance matrix for TSP problem
    ///   budget - Optional budget limit for quantum execution (USD)
    ///   timeout - Optional timeout for classical solver (milliseconds)
    ///   forceMethod - Optional override to force specific solver method
    ///   backend - Optional unified backend to use when forceMethod=Quantum
    ///   quantumConfig - QAOA shots, initial parameters and optimizer budget
    ///   cancellationToken - Cancels the quantum execution
    let solveTspWithBackendAndConfigAsync
        (distances: float[,])
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        (backend: IQuantumBackend option)
        (quantumConfig: QuantumTspSolver.QuantumTspConfig)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<TspSolver.TspSolution>>> =

        let startTime = DateTime.UtcNow
        let config = TspSolver.defaultConfig

        let solveClassical () =
            TspSolver.solveWithDistances distances config

        // Run the quantum TSP solver on the backend and convert its result to the classical TSP solution format
        let solveQuantum
            (actualBackend: IQuantumBackend)
            (reasoning: string)
            (recommendation: QuantumAdvisor.Recommendation option)
            =
            quantumResultTask {
                let! quantumResult =
                    QuantumTspSolver.solveAsync actualBackend distances quantumConfig cancellationToken
                    |> asOperationError "Quantum TSP solver"

                let classicalSolution: TspSolver.TspSolution =
                    {
                        Tour = quantumResult.Tour
                        TourLength = quantumResult.TourLength
                        Iterations = 0 // Quantum solver doesn't track iterations
                        ElapsedMs = quantumResult.ElapsedMs
                    }

                return createQuantumSolution classicalSolution reasoning startTime recommendation
            }

        quantumResultTask {
            match forceMethod with
            | Some Classical ->
                return
                    createClassicalSolution
                        (solveClassical ())
                        "Classical solver forced by user override. Quantum Advisor bypassed."
                        startTime
                        None

            | Some Quantum ->
                // Execute quantum TSP solver using provided backend (or default LocalBackend)
                let actualBackend = backend |> Option.defaultValue (defaultHybridBackend ())
                return! solveQuantum actualBackend "Quantum TSP solver forced by user override." None

            | None ->
                let! recommendation = QuantumAdvisor.getRecommendation distances

                match recommendation.RecommendationType with
                | QuantumAdvisor.RecommendationType.StronglyRecommendQuantum ->
                    match backend with
                    | None ->
                        let reasoning =
                            $"{recommendation.Reasoning} Quantum recommended but no quantum backend was provided - using classical fallback."

                        return createClassicalSolution (solveClassical ()) reasoning startTime (Some recommendation)

                    | Some actualBackend ->
                        let estimatedCost =
                            estimateBackendCostUSD
                                actualBackend
                                ((Array2D.length1 distances) * (Array2D.length1 distances))

                        match budget with
                        | Some limit when estimatedCost > limit ->
                            let reasoning =
                                $"{recommendation.Reasoning} Quantum recommended but estimated cost (${estimatedCost:F2}) exceeds limit (${limit:F2}). Falling back to classical."

                            return createClassicalSolution (solveClassical ()) reasoning startTime (Some recommendation)

                        | _ ->
                            return!
                                solveQuantum
                                    actualBackend
                                    $"{recommendation.Reasoning} Routing to quantum backend."
                                    (Some recommendation)

                | QuantumAdvisor.RecommendationType.StronglyRecommendClassical
                | QuantumAdvisor.RecommendationType.ConsiderQuantum ->
                    let reasoning = $"{recommendation.Reasoning} Routing to classical TSP solver."
                    return createClassicalSolution (solveClassical ()) reasoning startTime (Some recommendation)
        }

    /// Solve TSP problem using hybrid solver with optional backend override
    /// (task-based, non-blocking).
    ///
    /// This is an additive API that enables using HybridSolver with gate-based backends
    /// (e.g., LocalBackend) and topological backends (e.g., TopologicalUnifiedBackend).
    ///
    /// Uses `QuantumTspSolver.defaultConfig`; call `solveTspWithBackendAndConfigAsync` when
    /// the backend cannot afford its 1000-iteration variational loop.
    ///
    /// Parameters:
    ///   distances - Distance matrix for TSP problem
    ///   budget - Optional budget limit for quantum execution (USD)
    ///   timeout - Optional timeout for classical solver (milliseconds)
    ///   forceMethod - Optional override to force specific solver method
    ///   backend - Optional unified backend to use when forceMethod=Quantum
    ///   cancellationToken - Cancels the quantum execution
    let solveTspWithBackendAsync
        (distances: float[,])
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        (backend: IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<TspSolver.TspSolution>>> =
        solveTspWithBackendAndConfigAsync
            distances
            budget
            timeout
            forceMethod
            backend
            QuantumTspSolver.defaultConfig
            cancellationToken

    /// Solve TSP problem using hybrid solver with automatic quantum vs classical selection
    /// (task-based, non-blocking).
    ///
    /// Parameters:
    ///   distances - Distance matrix for TSP problem
    ///   budget - Optional budget limit for quantum execution (USD)
    ///   timeout - Optional timeout for classical solver (milliseconds)
    ///   forceMethod - Optional override to force specific solver method
    ///   cancellationToken - Cancels the quantum execution
    ///
    /// Returns:
    ///   Task of Result with Solution containing TSP result, method used, and reasoning
    let solveTspAsync
        (distances: float[,])
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<TspSolver.TspSolution>>> =
        solveTspWithBackendAsync distances budget timeout forceMethod None cancellationToken

    // ================================================================================
    // SOLVER ROUTING - PORTFOLIO
    // ================================================================================

    /// Portfolio routing shared by the public entry points. The covariance, when given, is
    /// validated first and passed to both the classical and the quantum solver.
    let private solvePortfolioCore
        (assets: PortfolioSolver.Asset list)
        (covariance: float[,] option)
        (constraints: PortfolioSolver.Constraints)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        (backend: IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<PortfolioSolver.PortfolioSolution>>> =

        let startTime = DateTime.UtcNow
        let config = PortfolioSolver.defaultConfig

        let solveClassical () =
            PortfolioSolver.solveGreedyByRatioWithCovariance assets covariance constraints config

        // Run the quantum portfolio solver on the backend and convert its result to the
        // classical portfolio solution format, keeping the solver's own elapsed time
        let solveQuantum (actualBackend: IQuantumBackend) =
            quantumResultTask {
                let quantumConfig = QuantumPortfolioSolver.defaultConfig

                let run =
                    match covariance with
                    | Some sigma ->
                        QuantumPortfolioSolver.solveWithCovarianceAsync
                            actualBackend
                            assets
                            sigma
                            constraints
                            quantumConfig
                            cancellationToken
                    | None ->
                        QuantumPortfolioSolver.solveAsync
                            actualBackend
                            assets
                            constraints
                            quantumConfig
                            cancellationToken

                let! quantumResult = run |> asOperationError "Quantum portfolio solver"

                let classicalSolution: PortfolioSolver.PortfolioSolution =
                    {
                        Allocations = quantumResult.Allocations
                        TotalValue = quantumResult.TotalValue
                        ExpectedReturn = quantumResult.ExpectedReturn
                        Risk = quantumResult.Risk
                        SharpeRatio = quantumResult.SharpeRatio
                        ElapsedMs = quantumResult.ElapsedMs
                    }

                return (classicalSolution, quantumResult.ElapsedMs)
            }

        let covarianceCheck =
            match covariance with
            | Some sigma -> PortfolioTypes.validateCovariance (List.length assets) sigma
            | None -> Ok()

        quantumResultTask {
            do! covarianceCheck

            match forceMethod with
            | Some Classical ->
                return
                    createClassicalSolution
                        (solveClassical ())
                        "Classical solver forced by user override. Quantum Advisor bypassed."
                        startTime
                        None

            | Some Quantum ->
                // Execute quantum portfolio solver using provided backend (or default LocalBackend)
                let actualBackend = backend |> Option.defaultValue (defaultHybridBackend ())
                let! (classicalSolution, elapsedMs) = solveQuantum actualBackend

                return
                    {
                        Method = Quantum
                        Result = classicalSolution
                        Reasoning = "Quantum portfolio solver forced by user override."
                        ElapsedMs = elapsedMs
                        Recommendation = None
                    }

            | None ->
                // Create problem representation for Quantum Advisor
                // Use asset count as approximation of problem complexity
                let numAssets = List.length assets

                let problemRepresentation =
                    Array2D.init numAssets numAssets (fun i j -> if i = j then 0.0 else 1.0)

                let! recommendation = QuantumAdvisor.getRecommendation problemRepresentation

                match recommendation.RecommendationType with
                | QuantumAdvisor.RecommendationType.StronglyRecommendQuantum ->
                    match backend with
                    | None ->
                        let reasoning =
                            $"{recommendation.Reasoning} Quantum recommended but no quantum backend was provided - using classical fallback."

                        return createClassicalSolution (solveClassical ()) reasoning startTime (Some recommendation)

                    | Some actualBackend ->
                        let estimatedCost = estimateBackendCostUSD actualBackend numAssets

                        match budget with
                        | Some limit when estimatedCost > limit ->
                            let reasoning =
                                $"{recommendation.Reasoning} Quantum recommended but estimated cost (${estimatedCost:F2}) exceeds limit (${limit:F2}). Falling back to classical."

                            return createClassicalSolution (solveClassical ()) reasoning startTime (Some recommendation)

                        | _ ->
                            let! (classicalSolution, _) = solveQuantum actualBackend

                            return
                                createQuantumSolution
                                    classicalSolution
                                    $"{recommendation.Reasoning} Routing to quantum backend."
                                    startTime
                                    (Some recommendation)

                | QuantumAdvisor.RecommendationType.StronglyRecommendClassical
                | QuantumAdvisor.RecommendationType.ConsiderQuantum ->
                    let reasoning = $"{recommendation.Reasoning} Routing to classical Portfolio solver."
                    return createClassicalSolution (solveClassical ()) reasoning startTime (Some recommendation)
        }

    /// Solve Portfolio optimization using hybrid solver with optional backend override
    /// (task-based, non-blocking).
    ///
    /// This is an additive API that enables using HybridSolver with gate-based backends
    /// (e.g., LocalBackend) and topological backends (e.g., TopologicalUnifiedBackend).
    /// The assets are treated as independent: Risk = sqrt(Σ (wᵢσᵢ)²). Use
    /// solvePortfolioWithCovarianceAsync to account for correlations.
    let solvePortfolioWithBackendAsync
        (assets: PortfolioSolver.Asset list)
        (constraints: PortfolioSolver.Constraints)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        (backend: IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<PortfolioSolver.PortfolioSolution>>> =
        solvePortfolioCore assets None constraints budget timeout forceMethod backend cancellationToken

    /// Solve mean-variance Portfolio optimization with a covariance matrix of the asset returns
    /// (task-based, non-blocking).
    ///
    /// The covariance (rows and columns in asset order) is validated - square, one row per
    /// asset, symmetric and positive semidefinite, otherwise ValidationError - and passed to
    /// the chosen path:
    /// - Classical: greedy by return/risk ratio; the reported Risk is sqrt(wᵀΣw).
    /// - Quantum: QAOA on a QUBO with the full covariance (see QuantumPortfolioSolver.toQubo);
    ///   the reported Risk is sqrt(wᵀΣw).
    ///
    /// Parameters:
    ///   assets - List of assets to optimize
    ///   covariance - Covariance matrix Σ of the asset returns
    ///   constraints - Portfolio constraints (budget, min/max holding)
    ///   budget - Optional budget limit for quantum execution (USD)
    ///   timeout - Optional timeout for classical solver (milliseconds)
    ///   forceMethod - Optional override to force specific solver method
    ///   backend - Optional quantum backend (a forced quantum run defaults to LocalBackend)
    ///   cancellationToken - Cancels the quantum execution
    let solvePortfolioWithCovarianceAsync
        (assets: PortfolioSolver.Asset list)
        (covariance: float[,])
        (constraints: PortfolioSolver.Constraints)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        (backend: IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<PortfolioSolver.PortfolioSolution>>> =
        solvePortfolioCore assets (Some covariance) constraints budget timeout forceMethod backend cancellationToken

    /// Solve Portfolio optimization using hybrid solver with automatic quantum vs classical selection
    /// (task-based, non-blocking).
    ///
    /// The assets are treated as independent: Risk = sqrt(Σ (wᵢσᵢ)²). Use
    /// solvePortfolioWithCovarianceAsync to account for correlations.
    ///
    /// Parameters:
    ///   assets - List of assets to optimize
    ///   constraints - Portfolio constraints (budget, min/max holding)
    ///   budget - Optional budget limit for quantum execution (USD)
    ///   timeout - Optional timeout for classical solver (milliseconds)
    ///   forceMethod - Optional override to force specific solver method
    ///   cancellationToken - Cancels the quantum execution
    ///
    /// Returns:
    ///   Task of Result with Solution containing Portfolio result, method used, and reasoning
    let solvePortfolioAsync
        (assets: PortfolioSolver.Asset list)
        (constraints: PortfolioSolver.Constraints)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<PortfolioSolver.PortfolioSolution>>> =
        solvePortfolioCore assets None constraints budget timeout forceMethod None cancellationToken

    // ================================================================================
    // SOLVER ROUTING - MAXCUT

    // ================================================================================

    /// Solve MaxCut problem using hybrid solver with optional backend override
    /// (task-based, non-blocking).
    ///
    /// This is an additive API that enables using HybridSolver with gate-based backends
    /// (e.g., LocalBackend) and topological backends (e.g., TopologicalUnifiedBackend).
    let solveMaxCutWithBackendAsync
        (problem: QuantumMaxCutSolver.MaxCutProblem)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        (backend: IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<QuantumMaxCutSolver.MaxCutSolution>>> =

        let startTime = DateTime.UtcNow

        let solveClassical () =
            QuantumMaxCutSolver.solveClassical problem

        // Run the quantum MaxCut solver on the backend
        let solveQuantum
            (actualBackend: IQuantumBackend)
            (reasoning: string)
            (recommendation: QuantumAdvisor.Recommendation option)
            =
            quantumResultTask {
                let quantumConfig = QuantumMaxCutSolver.defaultConfig

                let! quantumResult =
                    QuantumMaxCutSolver.solveAsync actualBackend problem quantumConfig cancellationToken
                    |> asOperationError "Quantum MaxCut solver"

                return createQuantumSolution quantumResult reasoning startTime recommendation
            }

        quantumResultTask {
            match forceMethod with
            | Some Classical ->
                return
                    createClassicalSolution
                        (solveClassical ())
                        "Classical MaxCut solver forced by user override. Quantum Advisor bypassed."
                        startTime
                        None

            | Some Quantum ->
                // Execute quantum MaxCut solver using provided backend (or default LocalBackend)
                let actualBackend = backend |> Option.defaultValue (defaultHybridBackend ())
                return! solveQuantum actualBackend "Quantum MaxCut solver forced by user override." None

            | None ->
                // Create problem representation for Quantum Advisor
                // Use vertex count as approximation of problem complexity
                let numVertices = problem.Vertices.Length

                let problemRepresentation =
                    Array2D.init numVertices numVertices (fun i j -> if i = j then 0.0 else 1.0)

                let! recommendation = QuantumAdvisor.getRecommendation problemRepresentation

                match recommendation.RecommendationType with
                | QuantumAdvisor.RecommendationType.StronglyRecommendQuantum ->
                    match backend with
                    | None ->
                        let reasoning =
                            $"{recommendation.Reasoning} Quantum recommended but no quantum backend was provided - using classical fallback."

                        return createClassicalSolution (solveClassical ()) reasoning startTime (Some recommendation)

                    | Some actualBackend ->
                        let estimatedCost = estimateBackendCostUSD actualBackend numVertices

                        match budget with
                        | Some limit when estimatedCost > limit ->
                            let reasoning =
                                $"{recommendation.Reasoning} Quantum recommended but estimated cost (${estimatedCost:F2}) exceeds limit (${limit:F2}). Falling back to classical."

                            return createClassicalSolution (solveClassical ()) reasoning startTime (Some recommendation)

                        | _ ->
                            return!
                                solveQuantum
                                    actualBackend
                                    $"{recommendation.Reasoning} Routing to quantum backend."
                                    (Some recommendation)

                | QuantumAdvisor.RecommendationType.StronglyRecommendClassical
                | QuantumAdvisor.RecommendationType.ConsiderQuantum ->
                    let reasoning = $"{recommendation.Reasoning} Routing to classical MaxCut solver."
                    return createClassicalSolution (solveClassical ()) reasoning startTime (Some recommendation)
        }

    /// Solve MaxCut problem using hybrid solver with automatic quantum vs classical selection
    /// (task-based, non-blocking).
    ///
    /// Parameters:
    ///   problem - MaxCut problem (vertices and edges)
    ///   budget - Optional budget limit for quantum execution (USD)
    ///   timeout - Optional timeout for classical solver (milliseconds)
    ///   forceMethod - Optional override to force specific solver method
    ///   cancellationToken - Cancels the quantum execution
    ///
    /// Returns:
    ///   Task of Result with Solution containing MaxCut result, method used, and reasoning
    let solveMaxCutAsync
        (problem: QuantumMaxCutSolver.MaxCutProblem)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<QuantumMaxCutSolver.MaxCutSolution>>> =
        solveMaxCutWithBackendAsync problem budget timeout forceMethod None cancellationToken

    // ================================================================================
    // SOLVER ROUTING - KNAPSACK

    // ================================================================================

    /// Solve Knapsack problem using hybrid solver with optional backend override
    /// (task-based, non-blocking).
    ///
    /// This is an additive API that enables using HybridSolver with gate-based backends
    /// (e.g., LocalBackend) and topological backends (e.g., TopologicalUnifiedBackend).
    let solveKnapsackWithBackendAsync
        (problem: QuantumKnapsackSolver.KnapsackProblem)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        (backend: IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<QuantumKnapsackSolver.KnapsackSolution>>> =

        let startTime = DateTime.UtcNow

        let solveClassical () =
            QuantumKnapsackSolver.solveClassical problem

        // Run the quantum Knapsack solver on the backend
        let solveQuantum
            (actualBackend: IQuantumBackend)
            (reasoning: string)
            (recommendation: QuantumAdvisor.Recommendation option)
            =
            quantumResultTask {
                let quantumConfig = QuantumKnapsackSolver.defaultConfig

                let! quantumResult =
                    QuantumKnapsackSolver.solveAsync actualBackend problem quantumConfig cancellationToken
                    |> asOperationError "Quantum Knapsack solver"

                return createQuantumSolution quantumResult reasoning startTime recommendation
            }

        quantumResultTask {
            match forceMethod with
            | Some Classical ->
                return
                    createClassicalSolution
                        (solveClassical ())
                        "Classical Knapsack solver forced by user override. Quantum Advisor bypassed."
                        startTime
                        None

            | Some Quantum ->
                // Execute quantum Knapsack solver using provided backend (or default LocalBackend)
                let actualBackend = backend |> Option.defaultValue (defaultHybridBackend ())
                return! solveQuantum actualBackend "Quantum Knapsack solver forced by user override." None

            | None ->
                // Create problem representation for Quantum Advisor
                // Use item count as approximation of problem complexity
                let numItems = problem.Items.Length

                let problemRepresentation =
                    Array2D.init numItems numItems (fun i j -> if i = j then 0.0 else 1.0)

                let! recommendation = QuantumAdvisor.getRecommendation problemRepresentation

                match recommendation.RecommendationType with
                | QuantumAdvisor.RecommendationType.StronglyRecommendQuantum ->
                    match backend with
                    | None ->
                        let reasoning =
                            $"{recommendation.Reasoning} Quantum recommended but no quantum backend was provided - using classical fallback."

                        return createClassicalSolution (solveClassical ()) reasoning startTime (Some recommendation)

                    | Some actualBackend ->
                        let estimatedCost = estimateBackendCostUSD actualBackend numItems

                        match budget with
                        | Some limit when estimatedCost > limit ->
                            let reasoning =
                                $"{recommendation.Reasoning} Quantum recommended but estimated cost (${estimatedCost:F2}) exceeds limit (${limit:F2}). Falling back to classical."

                            return createClassicalSolution (solveClassical ()) reasoning startTime (Some recommendation)

                        | _ ->
                            return!
                                solveQuantum
                                    actualBackend
                                    $"{recommendation.Reasoning} Routing to quantum backend."
                                    (Some recommendation)

                | QuantumAdvisor.RecommendationType.StronglyRecommendClassical
                | QuantumAdvisor.RecommendationType.ConsiderQuantum ->
                    let reasoning = $"{recommendation.Reasoning} Routing to classical Knapsack solver."
                    return createClassicalSolution (solveClassical ()) reasoning startTime (Some recommendation)
        }

    /// Solve Knapsack problem using hybrid solver with automatic quantum vs classical selection
    /// (task-based, non-blocking).
    ///
    /// Parameters:
    ///   problem - Knapsack problem (items, weights, values, capacity)
    ///   budget - Optional budget limit for quantum execution (USD)
    ///   timeout - Optional timeout for classical solver (milliseconds)
    ///   forceMethod - Optional override to force specific solver method
    ///   cancellationToken - Cancels the quantum execution
    ///
    /// Returns:
    ///   Task of Result with Solution containing Knapsack result, method used, and reasoning
    let solveKnapsackAsync
        (problem: QuantumKnapsackSolver.KnapsackProblem)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<QuantumKnapsackSolver.KnapsackSolution>>> =
        solveKnapsackWithBackendAsync problem budget timeout forceMethod None cancellationToken

    // ================================================================================
    // SOLVER ROUTING - GRAPH COLORING

    // ================================================================================

    /// Solve Graph Coloring problem using hybrid solver with optional backend override
    /// (task-based, non-blocking).
    ///
    /// This is an additive API that enables using HybridSolver with gate-based backends
    /// (e.g., LocalBackend) and topological backends (e.g., TopologicalUnifiedBackend).
    let solveGraphColoringWithBackendAsync
        (problem: QuantumGraphColoringSolver.GraphColoringProblem)
        (numColors: int)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        (backend: IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<QuantumGraphColoringSolver.GraphColoringSolution>>> =

        let startTime = DateTime.UtcNow

        let solveClassical () =
            QuantumGraphColoringSolver.solveClassical problem

        // A graph without edges is colored without a circuit: report it as classical
        let quantumPathSolution
            (result: QuantumGraphColoringSolver.GraphColoringSolution)
            (reasoning: string)
            (recommendation: QuantumAdvisor.Recommendation option)
            =
            if result.BackendName = QuantumGraphColoringSolver.NoCircuitBackendName then
                createClassicalSolution
                    result
                    $"{reasoning} No circuit ran because the graph has no edges: the vertices were colored directly by the classical greedy coloring."
                    startTime
                    recommendation
            else
                createQuantumSolution result reasoning startTime recommendation

        // Run the quantum Graph Coloring solver on the backend
        let solveQuantum
            (actualBackend: IQuantumBackend)
            (reasoning: string)
            (recommendation: QuantumAdvisor.Recommendation option)
            =
            quantumResultTask {
                let quantumConfig = QuantumGraphColoringSolver.defaultConfig numColors

                let! quantumResult =
                    QuantumGraphColoringSolver.solveAsync actualBackend problem quantumConfig cancellationToken
                    |> asOperationError "Quantum Graph Coloring solver"

                return quantumPathSolution quantumResult reasoning recommendation
            }

        quantumResultTask {
            match forceMethod with
            | Some Classical ->
                let! classicalResult = solveClassical ()

                return
                    createClassicalSolution
                        classicalResult
                        "Classical Graph Coloring solver forced by user override. Quantum Advisor bypassed."
                        startTime
                        None

            | Some Quantum ->
                // Execute quantum Graph Coloring solver using provided backend (or default LocalBackend)
                let actualBackend = backend |> Option.defaultValue (defaultHybridBackend ())
                return! solveQuantum actualBackend "Quantum Graph Coloring solver forced by user override." None

            | None ->
                // Create problem representation for Quantum Advisor
                // Use vertex count as approximation of problem complexity
                let numVertices = problem.Vertices.Length

                let problemRepresentation =
                    Array2D.init numVertices numVertices (fun i j -> if i = j then 0.0 else 1.0)

                let! recommendation = QuantumAdvisor.getRecommendation problemRepresentation

                match recommendation.RecommendationType with
                | QuantumAdvisor.RecommendationType.StronglyRecommendQuantum ->
                    match backend with
                    | None ->
                        let reasoning =
                            $"{recommendation.Reasoning} Quantum recommended but no quantum backend was provided - using classical fallback."

                        let! classicalResult = solveClassical ()
                        return createClassicalSolution classicalResult reasoning startTime (Some recommendation)

                    | Some actualBackend ->
                        let estimatedCost = estimateBackendCostUSD actualBackend numVertices

                        match budget with
                        | Some limit when estimatedCost > limit ->
                            let reasoning =
                                $"{recommendation.Reasoning} Quantum recommended but estimated cost (${estimatedCost:F2}) exceeds limit (${limit:F2}). Falling back to classical."

                            let! classicalResult = solveClassical ()
                            return createClassicalSolution classicalResult reasoning startTime (Some recommendation)

                        | _ ->
                            return!
                                solveQuantum
                                    actualBackend
                                    $"{recommendation.Reasoning} Routing to quantum backend."
                                    (Some recommendation)

                | QuantumAdvisor.RecommendationType.StronglyRecommendClassical
                | QuantumAdvisor.RecommendationType.ConsiderQuantum ->
                    let reasoning =
                        $"{recommendation.Reasoning} Routing to classical Graph Coloring solver."

                    let! classicalResult = solveClassical ()
                    return createClassicalSolution classicalResult reasoning startTime (Some recommendation)
        }

    /// Solve Graph Coloring problem using hybrid solver with automatic quantum vs classical selection
    /// (task-based, non-blocking).
    ///
    /// Parameters:
    ///   problem - Graph Coloring problem (vertices, edges, colors)
    ///   numColors - Number of colors to use
    ///   budget - Optional budget limit for quantum execution (USD)
    ///   timeout - Optional timeout for classical solver (milliseconds)
    ///   forceMethod - Optional override to force specific solver method
    ///   cancellationToken - Cancels the quantum execution
    ///
    /// Returns:
    ///   Task of Result with Solution containing Graph Coloring result, method used, and reasoning
    let solveGraphColoringAsync
        (problem: QuantumGraphColoringSolver.GraphColoringProblem)
        (numColors: int)
        (budget: float option)
        (timeout: float option)
        (forceMethod: SolverMethod option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<QuantumGraphColoringSolver.GraphColoringSolution>>> =
        solveGraphColoringWithBackendAsync problem numColors budget timeout forceMethod None cancellationToken

    // ================================================================================
    // LEGACY COMPATIBILITY

    // ================================================================================

    /// Legacy solve function for backward compatibility (TSP only, no optional parameters),
    /// task-based and non-blocking.
    let solveAsync
        (distances: float[,])
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Solution<TspSolver.TspSolution>>> =
        solveTspAsync distances None None None cancellationToken
