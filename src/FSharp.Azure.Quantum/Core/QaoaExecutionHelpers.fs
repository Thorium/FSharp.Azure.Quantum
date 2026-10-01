namespace FSharp.Azure.Quantum.Core

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.LocalSimulator

/// Shared QAOA execution infrastructure for all quantum solvers.
///
/// Consolidates the QAOA execution pattern from DrugDiscoverySolvers (the most mature pattern)
/// into reusable functions. Addresses technical debt:
/// - Debt 2: Eliminates 6 private copies of quboMapToArray across solver files
/// - Debt 3: Defines unified QaoaSolverConfig replacing 7 incompatible config types
/// - Debt 6: Extracts private QAOA helpers from DrugDiscoverySolvers into shared module
///
/// RULE 1 COMPLIANCE:
/// All execution functions require IQuantumBackend parameter (explicit quantum execution).
module QaoaExecutionHelpers =

    // ================================================================================
    // UNIFIED QAOA CONFIGURATION (Decision 7)
    // ================================================================================

    /// Unified QAOA execution configuration.
    /// Captures all core QAOA execution fields shared across solvers.
    /// Solvers with domain-specific config fields (NumColors, RiskAversion, etc.)
    /// should compose this type with their own domain-specific config type.
    type QaoaSolverConfig =
        {
            /// Number of QAOA layers (p parameter). Higher p = better solutions but slower.
            NumLayers: int

            /// Number of shots for optimization phase (lower = faster)
            OptimizationShots: int

            /// Number of shots for final execution (higher = better sampling)
            FinalShots: int

            /// Enable Nelder-Mead parameter optimization.
            /// When false, uses grid search (faster but lower quality).
            EnableOptimization: bool

            /// Enable constraint repair post-processing
            EnableConstraintRepair: bool

            /// Maximum optimization iterations for Nelder-Mead
            MaxOptimizationIterations: int
        }

    /// Default QAOA configuration (balanced speed/quality)
    let defaultConfig: QaoaSolverConfig =
        {
            NumLayers = 2
            OptimizationShots = 100
            FinalShots = 1000
            EnableOptimization = true
            EnableConstraintRepair = true
            // 1000 matches the budget that was hardcoded in the optimizer before
            // MaxOptimizationIterations was honored — smaller values here would
            // silently cut optimization quality for existing callers.
            MaxOptimizationIterations = 1000
        }

    /// Fast configuration (for quick prototyping / grid search only)
    let fastConfig: QaoaSolverConfig =
        {
            NumLayers = 1
            OptimizationShots = 50
            FinalShots = 500
            EnableOptimization = false
            EnableConstraintRepair = true
            // Unused while EnableOptimization = false; kept low deliberately so
            // turning optimization on in a copied fast config stays fast.
            MaxOptimizationIterations = 100
        }

    /// High-quality configuration (for production workloads)
    let highQualityConfig: QaoaSolverConfig =
        {
            NumLayers = 3
            OptimizationShots = 200
            FinalShots = 2000
            EnableOptimization = true
            EnableConstraintRepair = true
            // Not lower than the pre-fix hardcoded 1000 (see defaultConfig note)
            MaxOptimizationIterations = 1000
        }

    // ================================================================================
    // CONFIGURATION VALIDATION
    // ================================================================================

    /// Validate QAOA solver configuration, returning Error if invalid.
    let private validateConfig (config: QaoaSolverConfig) : Result<unit, QuantumError> =
        if config.NumLayers <= 0 then
            Error(QuantumError.ValidationError("NumLayers", $"must be > 0, got {config.NumLayers}"))
        elif config.OptimizationShots <= 0 then
            Error(QuantumError.ValidationError("OptimizationShots", $"must be > 0, got {config.OptimizationShots}"))
        elif config.FinalShots <= 0 then
            Error(QuantumError.ValidationError("FinalShots", $"must be > 0, got {config.FinalShots}"))
        elif config.MaxOptimizationIterations <= 0 then
            Error(
                QuantumError.ValidationError(
                    "MaxOptimizationIterations",
                    $"must be > 0, got {config.MaxOptimizationIterations}"
                )
            )
        else
            Ok()

    // ================================================================================
    // QUBO CONVERSION UTILITIES (Debt 2 consolidation)
    // ================================================================================

    /// Convert GraphOptimization.QuboMatrix (sparse Map) to dense float[,].
    /// Delegates to Qubo.toDenseArray — kept as convenience wrapper for QuboMatrix input.
    /// For new solvers that build QUBO as Map<int*int, float>, use Qubo.toDenseArray directly.
    let quboMapToArray (quboMatrix: GraphOptimization.QuboMatrix) : float[,] =
        Qubo.toDenseArray quboMatrix.NumVariables quboMatrix.Q

    // ================================================================================
    // SHARED QAOA EXECUTION FUNCTIONS (Debt 6 extraction)
    // ================================================================================

    /// Evaluate QUBO objective for a bitstring.
    /// Returns the energy: sum of Q[i,j] * bits[i] * bits[j] for all i,j.
    let evaluateQubo (qubo: float[,]) (bits: int[]) : float =
        let n = Array2D.length1 qubo

        seq {
            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    yield qubo.[i, j] * float bits.[i] * float bits.[j]
        }
        |> Seq.sum

    /// The QAOA circuit every solver executes: the cost Hamiltonian is normalised to a largest
    /// |coefficient| of 1 (ProblemHamiltonian.normalize) before the circuit is built, so solver
    /// angles are on a problem-independent scale. The mixer is used as given.
    let private buildSolverCircuit
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (parameters: (float * float)[])
        : CircuitAbstraction.ICircuit =
        let qaoaCircuit =
            QaoaCircuit.QaoaCircuit.build (QaoaCircuit.ProblemHamiltonian.normalize problemHam) mixerHam parameters

        CircuitAbstraction.QaoaCircuitWrapper(qaoaCircuit) :> CircuitAbstraction.ICircuit

    /// Execute a single QAOA circuit asynchronously with given parameters and return measurements.
    /// Uses backend.ExecuteToStateAsync for non-blocking I/O against cloud backends.
    /// Pipeline: QUBO -> ProblemHamiltonian (normalised) -> MixerHamiltonian -> QaoaCircuit -> ICircuit -> backend
    /// Angles follow QaoaCircuit's minimisation convention, in units of the normalised Hamiltonian.
    let executeQaoaCircuitAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (parameters: (float * float)[])
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[][], QuantumError>> =
        task {
            let! result =
                backend.ExecuteToStateAsync (buildSolverCircuit problemHam mixerHam parameters) cancellationToken

            return result |> Result.map (fun state -> QuantumState.measure state shots)
        }

    // ================================================================================
    // ENERGY OBJECTIVE (shared by grid search and Nelder-Mead)
    // ================================================================================

    /// Energy of every computational basis state, where bit q of the index is variable q
    /// (the StateVector convention).
    let private basisEnergies (numQubits: int) (energyOf: int[] -> float) : float[] =
        let bits = Array.zeroCreate numQubits

        Array.init (1 <<< numQubits) (fun index ->
            for q in 0 .. numQubits - 1 do
                bits.[q] <- (index >>> q) &&& 1

            energyOf bits)

    /// Dense QUBO energy over the non-zero entries only.
    let private denseEnergy (qubo: float[,]) : int[] -> float =
        let n = Array2D.length1 qubo

        let entries =
            [|
                for i in 0 .. n - 1 do
                    for j in 0 .. n - 1 do
                        if qubo.[i, j] <> 0.0 then
                            yield struct (i, j, qubo.[i, j])
            |]

        fun (bits: int[]) ->
            let mutable total = 0.0

            for struct (i, j, v) in entries do
                if bits.[i] = 1 && bits.[j] = 1 then
                    total <- total + v

            total

    /// Expected QUBO energy of a backend state: exact over the amplitudes when the backend
    /// returns a state vector of the problem's width, otherwise the mean energy of `shots`
    /// samples. Lower is better.
    let private expectedEnergy
        (energyOf: int[] -> float)
        (energies: Lazy<float[]>)
        (numQubits: int)
        (shots: int)
        (state: QuantumState)
        : float =
        match state with
        | QuantumState.StateVector sv when StateVector.numQubits sv = numQubits ->
            let e = energies.Value
            let mutable total = 0.0

            for index in 0 .. e.Length - 1 do
                let amplitude = StateVector.getAmplitude index sv

                total <-
                    total
                    + e.[index]
                      * (amplitude.Real * amplitude.Real + amplitude.Imaginary * amplitude.Imaginary)

            total
        | _ -> QuantumState.measure state shots |> Array.averageBy energyOf

    /// Expected energy of the QAOA state at `parameters`, or the backend's error.
    let private evaluateParameters
        (backend: BackendAbstraction.IQuantumBackend)
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (energyOf: int[] -> float)
        (energies: Lazy<float[]>)
        (shots: int)
        (parameters: (float * float)[])
        : Result<float, QuantumError> =
        backend.ExecuteToState(buildSolverCircuit problemHam mixerHam parameters)
        |> Result.map (expectedEnergy energyOf energies problemHam.NumQubits shots)

    /// Asynchronous evaluateParameters.
    let private evaluateParametersAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (energyOf: int[] -> float)
        (energies: Lazy<float[]>)
        (shots: int)
        (parameters: (float * float)[])
        (cancellationToken: CancellationToken)
        : Task<Result<float, QuantumError>> =
        task {
            let! result =
                backend.ExecuteToStateAsync (buildSolverCircuit problemHam mixerHam parameters) cancellationToken

            return
                result
                |> Result.map (expectedEnergy energyOf energies problemHam.NumQubits shots)
        }

    /// Nelder-Mead objective over flat [γ₁; β₁; γ₂; β₂; …]: the expected energy, or
    /// Double.MaxValue when execution fails.
    let private flatObjective
        (evaluate: (float * float)[] -> Result<float, QuantumError>)
        (numLayers: int)
        : float[] -> float =
        fun (flatParams: float[]) ->
            let parameters =
                Array.init numLayers (fun i -> (flatParams.[2 * i], flatParams.[2 * i + 1]))

            (evaluate parameters) |> Result.defaultWith (fun _ -> Double.MaxValue)

    /// Nelder-Mead starting point: a linear ramp (γ rising, β falling across the layers, as a
    /// discretised annealing schedule) in normalised-Hamiltonian units.
    let private rampInitialParameters (numLayers: int) : float[] =
        Array.init (2 * numLayers) (fun i ->
            let fraction = (float (i / 2) + 0.5) / float numLayers

            if i % 2 = 0 then
                0.75 * fraction
            else
                0.75 * (1.0 - fraction))

    /// γ values of the grid search, in normalised-Hamiltonian units.
    let private gridGammas = [| 0.1; 0.3; 0.5; 0.7; 1.0; 1.5; Math.PI / 4.0 |]

    /// β values of the grid search.
    let private gridBetas = [| 0.1; 0.3; 0.5; 0.7; 1.0 |]

    /// Every grid point as a p-layer parameter set (the same (γ, β) in each layer).
    let private gridParameterSets (numLayers: int) : (float * float)[][] =
        [|
            for gamma in gridGammas do
                for beta in gridBetas do
                    Array.init numLayers (fun _ -> (gamma, beta))
        |]

    /// Lowest-energy evaluated grid point, or the last error when none evaluated.
    let private pickGridPoint
        (results: ((float * float)[] * Result<float, QuantumError>)[])
        : Result<(float * float)[], QuantumError> =
        let evaluated =
            results
            |> Array.choose (fun (parameters, r) ->
                match r with
                | Ok energy -> Some(parameters, energy)
                | Error _ -> None)

        if evaluated.Length > 0 then
            evaluated |> Array.minBy snd |> fst |> Ok
        else
            results
            |> Array.choose (fun (_, r) -> r |> Result.map (fun _ -> None) |> Result.defaultWith (fun err -> Some err))
            |> Array.tryLast
            |> Option.defaultValue (QuantumError.OperationError("QAOA", "No valid solution found"))
            |> Error

    /// Execute QAOA from a dense QUBO matrix asynchronously.
    /// Builds Hamiltonians, circuit, executes via backend.ExecuteToStateAsync,
    /// and returns measurements.
    let executeFromQuboAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (parameters: (float * float)[])
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[][], QuantumError>> =

        let n = Array2D.length1 qubo
        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
        let mixerHam = QaoaCircuit.MixerHamiltonian.create n
        executeQaoaCircuitAsync backend problemHam mixerHam parameters shots cancellationToken

    /// Execute a single QAOA circuit from sparse QUBO representation asynchronously.
    /// Avoids allocating dense float[,] array — calls ProblemHamiltonian.fromQuboSparse.
    let executeQaoaCircuitSparseAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (numQubits: int)
        (quboMap: Map<int * int, float>)
        (parameters: (float * float)[])
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[][], QuantumError>> =

        let problemHam = QaoaCircuit.ProblemHamiltonian.fromQuboSparse numQubits quboMap
        let mixerHam = QaoaCircuit.MixerHamiltonian.create numQubits
        executeQaoaCircuitAsync backend problemHam mixerHam parameters shots cancellationToken

    /// Create objective function closure for Nelder-Mead optimization.
    /// The returned function converts the flat parameter array [γ₁; β₁; γ₂; β₂; …] to
    /// (gamma, beta) pairs, executes the QAOA circuit, and returns the expected QUBO energy
    /// (lower = better): exact over the amplitudes on a state-vector backend, otherwise the
    /// mean energy of `shots` samples.
    let createObjectiveFunction
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (numLayers: int)
        (shots: int)
        : float[] -> float =

        if
            Array2D.length1 qubo <> problemHam.NumQubits
            || Array2D.length2 qubo <> problemHam.NumQubits
        then
            invalidArg
                (nameof qubo)
                $"QUBO is {Array2D.length1 qubo}x{Array2D.length2 qubo} but the problem Hamiltonian has {problemHam.NumQubits} qubits"

        let energyOf = denseEnergy qubo
        let energies = lazy (basisEnergies (Array2D.length1 qubo) energyOf)
        flatObjective (evaluateParameters backend problemHam mixerHam energyOf energies shots) numLayers

    /// Nelder-Mead over the expected energy from the ramp start, then FinalShots samples at the
    /// optimum; returns the lowest-energy sample, the parameters and whether the optimizer converged.
    /// The Nelder-Mead evaluations run one after another on the calling thread (each simplex step
    /// depends on the previous evaluation) and check the token before each circuit; the final
    /// sampling is awaited.
    let private optimizeAndSampleAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (energyOf: int[] -> float)
        (config: QaoaSolverConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<int[] * (float * float)[] * bool, QuantumError>> =
        task {
            let energies = lazy (basisEnergies problemHam.NumQubits energyOf)

            let evaluate (parameters: (float * float)[]) =
                cancellationToken.ThrowIfCancellationRequested()
                evaluateParameters backend problemHam mixerHam energyOf energies config.OptimizationShots parameters

            let objectiveFunc = flatObjective evaluate config.NumLayers

            let initialParams = rampInitialParameters config.NumLayers

            // γ ∈ [0, π], β ∈ [0, π/2]
            let lowerBounds = Array.zeroCreate (2 * config.NumLayers)

            let upperBounds =
                Array.init (2 * config.NumLayers) (fun i -> if i % 2 = 0 then Math.PI else Math.PI / 2.0)

            // On non-convergence the optimizer returns the best evaluation seen so far
            // with Converged = false instead of throwing.
            let optimResult =
                QaoaOptimizer.Optimizer.minimizeWithBounds
                    objectiveFunc
                    initialParams
                    lowerBounds
                    upperBounds
                    1e-6
                    config.MaxOptimizationIterations

            let optimizedParams =
                Array.init config.NumLayers (fun i ->
                    (optimResult.OptimizedParameters.[2 * i], optimResult.OptimizedParameters.[2 * i + 1]))

            let! finalResult =
                executeQaoaCircuitAsync backend problemHam mixerHam optimizedParams config.FinalShots cancellationToken

            return
                finalResult
                |> Result.map (fun measurements ->
                    (measurements |> Array.minBy energyOf, optimizedParams, optimResult.Converged))
        }

    /// Grid search over (γ, β) by expected energy, then FinalShots samples at the best grid
    /// point; returns the lowest-energy sample and the parameters. At most maxConcurrency
    /// grid points are in flight.
    let private gridSearchAndSampleAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (energyOf: int[] -> float)
        (config: QaoaSolverConfig)
        (maxConcurrency: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[] * (float * float)[], QuantumError>> =
        task {
            let energies = lazy (basisEnergies problemHam.NumQubits energyOf)
            let parameterSets = gridParameterSets config.NumLayers
            let concurrency = max 1 (min maxConcurrency parameterSets.Length)
            use semaphore = new SemaphoreSlim(concurrency, concurrency)

            let executeOne (parameters: (float * float)[]) =
                task {
                    do! semaphore.WaitAsync cancellationToken

                    try
                        let! result =
                            evaluateParametersAsync
                                backend
                                problemHam
                                mixerHam
                                energyOf
                                energies
                                config.OptimizationShots
                                parameters
                                cancellationToken

                        return (parameters, result)
                    finally
                        semaphore.Release() |> ignore
                }

            let! results = parameterSets |> Array.map executeOne |> Task.WhenAll

            match pickGridPoint results with
            | Error err -> return Error err
            | Ok bestParams ->
                let! finalResult =
                    executeQaoaCircuitAsync backend problemHam mixerHam bestParams config.FinalShots cancellationToken

                return
                    finalResult
                    |> Result.map (fun measurements -> (measurements |> Array.minBy energyOf, bestParams))
        }

    /// Execute QAOA with Nelder-Mead parameter optimization asynchronously.
    /// Returns: (bestBitstring, optimizedParameters, converged)
    /// Uses QaoaOptimizer.minimizeWithBounds for bounded Nelder-Mead on the expected energy
    /// (see createObjectiveFunction), starting from a γ-rising, β-falling ramp.
    /// The optimizer's evaluations run sequentially; the final sampling is awaited.
    let executeQaoaWithOptimizationAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (config: QaoaSolverConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<int[] * (float * float)[] * bool, QuantumError>> =
        match validateConfig config with
        | Error err -> Task.FromResult(Error err)
        | Ok() ->
            let n = Array2D.length1 qubo

            optimizeAndSampleAsync
                backend
                (QaoaCircuit.ProblemHamiltonian.fromQubo qubo)
                (QaoaCircuit.MixerHamiltonian.create n)
                (denseEnergy qubo)
                config
                cancellationToken

    /// Execute QAOA with grid search asynchronously.
    /// Returns: (bestBitstring, bestParameters)
    /// Keeps the grid point with the lowest expected energy (see createObjectiveFunction).
    ///
    /// The maxConcurrency parameter controls how many grid search evaluations
    /// run concurrently. Default is 1 (sequential) to limit memory usage on
    /// local simulators. Set higher (e.g. 10-35) for cloud backends where
    /// submissions are I/O-bound and memory is remote.
    let executeQaoaWithGridSearchAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (config: QaoaSolverConfig)
        (maxConcurrency: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[] * (float * float)[], QuantumError>> =
        match validateConfig config with
        | Error err -> Task.FromResult(Error err)
        | Ok() ->
            let n = Array2D.length1 qubo

            gridSearchAndSampleAsync
                backend
                (QaoaCircuit.ProblemHamiltonian.fromQubo qubo)
                (QaoaCircuit.MixerHamiltonian.create n)
                (denseEnergy qubo)
                config
                maxConcurrency
                cancellationToken

    /// Execute QAOA the way the configuration asks: Nelder-Mead parameter optimization when
    /// config.EnableOptimization, else a sequential (maxConcurrency = 1) grid search.
    /// Returns: (bestBitstring, parameters, converged), converged being None after a grid search.
    let runQaoaAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (config: QaoaSolverConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<int[] * (float * float)[] option * bool option, QuantumError>> =
        task {
            if config.EnableOptimization then
                let! optimized = executeQaoaWithOptimizationAsync backend qubo config cancellationToken

                return
                    optimized
                    |> Result.map (fun (bits, optParams, converged) -> (bits, Some optParams, Some converged))
            else
                let! searched = executeQaoaWithGridSearchAsync backend qubo config 1 cancellationToken
                return searched |> Result.map (fun (bits, optParams) -> (bits, Some optParams, None))
        }

    // ================================================================================
    // SPARSE QUBO EXECUTION (Task 1 — memory-efficient path)
    // ================================================================================

    /// Evaluate sparse QUBO objective for a bitstring.
    /// Returns the energy: sum of Q[(i,j)] * bits[i] * bits[j] for all entries.
    let evaluateQuboSparse (quboMap: Map<int * int, float>) (bits: int[]) : float =
        quboMap
        |> Map.fold (fun acc (i, j) qij -> acc + qij * float bits.[i] * float bits.[j]) 0.0

    /// Execute QAOA with Nelder-Mead optimization from sparse QUBO asynchronously.
    /// Returns: (bestBitstring, optimizedParameters, converged)
    /// Same objective and start as executeQaoaWithOptimizationAsync.
    let executeQaoaWithOptimizationSparseAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (numQubits: int)
        (quboMap: Map<int * int, float>)
        (config: QaoaSolverConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<int[] * (float * float)[] * bool, QuantumError>> =
        match validateConfig config with
        | Error err -> Task.FromResult(Error err)
        | Ok() ->
            optimizeAndSampleAsync
                backend
                (QaoaCircuit.ProblemHamiltonian.fromQuboSparse numQubits quboMap)
                (QaoaCircuit.MixerHamiltonian.create numQubits)
                (evaluateQuboSparse quboMap)
                config
                cancellationToken

    /// Execute QAOA with grid search from sparse QUBO asynchronously.
    /// Returns: (bestBitstring, bestParameters)
    /// Keeps the grid point with the lowest expected energy.
    ///
    /// The maxConcurrency parameter controls how many grid search evaluations
    /// run concurrently. Default is 1 (sequential) to limit memory usage on
    /// local simulators. Set higher for cloud backends.
    let executeQaoaWithGridSearchSparseAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (numQubits: int)
        (quboMap: Map<int * int, float>)
        (config: QaoaSolverConfig)
        (maxConcurrency: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[] * (float * float)[], QuantumError>> =
        match validateConfig config with
        | Error err -> Task.FromResult(Error err)
        | Ok() ->
            gridSearchAndSampleAsync
                backend
                (QaoaCircuit.ProblemHamiltonian.fromQuboSparse numQubits quboMap)
                (QaoaCircuit.MixerHamiltonian.create numQubits)
                (evaluateQuboSparse quboMap)
                config
                maxConcurrency
                cancellationToken

    // ================================================================================
    // BUDGET-CONSTRAINED EXECUTION (Task 5)
    // ================================================================================

    /// Capacity-check strategy for budget-constrained execution.
    ///
    /// Mirrors ProblemDecomposition.DecompositionStrategy but lives here to avoid
    /// a compile-order dependency (QaoaExecutionHelpers compiles before ProblemDecomposition).
    [<Struct>]
    type BudgetDecompositionStrategy =
        /// Run as-is (no capacity check).
        | NoBudgetDecomposition
        /// Error if problem exceeds this fixed qubit limit.
        | FixedQubitLimit of maxQubits: int
        /// Error if problem exceeds backend's MaxQubits (IQubitLimitedBackend).
        | AdaptiveToBudgetBackend

    /// Budget constraints for QAOA execution.
    ///
    /// Controls total resource usage and provides a safety check against
    /// exceeding backend qubit capacity.
    type ExecutionBudget =
        {
            /// Maximum total measurement shots across all sub-problems.
            /// Shots are divided equally among decomposed sub-problems.
            MaxTotalShots: int

            /// Optional wall-clock time limit in milliseconds.
            /// Execution stops early if time is exceeded (best-effort).
            MaxTimeMs: int option

            /// Capacity-check strategy for large problems.
            Decomposition: BudgetDecompositionStrategy
        }

    /// Default execution budget: 1000 shots, no time limit, adaptive capacity check.
    let defaultBudget: ExecutionBudget =
        {
            MaxTotalShots = 1000
            MaxTimeMs = None
            Decomposition = AdaptiveToBudgetBackend
        }

    /// Execute QAOA with budget constraints and capacity checking asynchronously.
    ///
    /// This is the highest-level async QAOA execution entry point. It:
    /// 1. Validates configuration and budget
    /// 2. Checks backend capacity (MaxQubits via IQubitLimitedBackend)
    /// 3. Returns clear error if problem exceeds capacity
    /// 4. Applies shot budget limit to config
    /// 5. Respects optional time limit
    ///
    /// The maxConcurrency parameter controls grid search parallelism.
    /// Default 1 = sequential. Set higher for cloud backends.
    ///
    /// Nelder-Mead evaluations run one after another (each step depends on the
    /// previous evaluation); the final execution is awaited.
    let executeWithBudgetAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (config: QaoaSolverConfig)
        (budget: ExecutionBudget)
        (maxConcurrency: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[] * (float * float)[] * bool, QuantumError>> =
        match validateConfig config with
        | Error err -> task { return Error err }
        | Ok() ->

            if budget.MaxTotalShots <= 0 then
                task {
                    return
                        Error(QuantumError.ValidationError("MaxTotalShots", $"must be > 0, got {budget.MaxTotalShots}"))
                }
            else

                let n = Array2D.length1 qubo
                let stopwatch = System.Diagnostics.Stopwatch.StartNew()

                let isTimeExceeded () =
                    match budget.MaxTimeMs with
                    | Some maxMs -> stopwatch.ElapsedMilliseconds > int64 maxMs
                    | None -> false

                let maxQubits = BackendAbstraction.UnifiedBackend.getRunnableQubits backend

                let exceedsCapacity =
                    match budget.Decomposition with
                    | NoBudgetDecomposition -> false
                    | FixedQubitLimit limit -> n > limit
                    | AdaptiveToBudgetBackend ->
                        match maxQubits with
                        | Some limit -> n > limit
                        | None -> false

                if isTimeExceeded () then
                    task {
                        return
                            Error(QuantumError.OperationError("QAOA", "Time budget exceeded before execution started"))
                    }
                elif exceedsCapacity then
                    let limitStr =
                        match maxQubits with
                        | Some limit -> $"{limit}"
                        | None -> "unknown"

                    task {
                        return
                            Error(
                                QuantumError.OperationError(
                                    "QAOA",
                                    $"Problem requires {n} qubits but backend supports {limitStr}. "
                                    + "Use solver-level decomposition (solveWithConfig) for automatic splitting, "
                                    + "or reduce problem size."
                                )
                            )
                    }
                else
                    let adjustedConfig =
                        { config with
                            FinalShots = min config.FinalShots budget.MaxTotalShots
                        }

                    if config.EnableOptimization then
                        executeQaoaWithOptimizationAsync backend qubo adjustedConfig cancellationToken
                    else
                        task {
                            let! result =
                                executeQaoaWithGridSearchAsync
                                    backend
                                    qubo
                                    adjustedConfig
                                    maxConcurrency
                                    cancellationToken

                            return result |> Result.map (fun (bits, ps) -> (bits, ps, false))
                        }
