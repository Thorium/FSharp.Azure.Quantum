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
    // SPLITTING A PROBLEM WIDER THAN THE BACKEND
    // ================================================================================

    /// When a solver may split a problem that is wider than the backend into several
    /// circuits that fit (see QuboSplitting). A split trades qubits for circuit runs: the
    /// answer needs many runs where an unsplit problem needs one.
    [<RequireQualifiedAccess>]
    type SplitPolicy =
        /// Never split. A problem wider than the backend is sent as one circuit and the
        /// backend accepts or refuses it.
        | Never

        /// Split on simulators, where a run costs time only. On a backend that submits every
        /// circuit as a job (cloud hardware and cloud simulators, IShotSamplingBackend) the
        /// problem is sent as one circuit, so the number of billed jobs never multiplies
        /// without being asked for. The default.
        | OnSimulators

        /// Split on every backend. On a billed backend every piece is a billed job: set
        /// MaxFixedVariables and MaxShareRuns to what the budget allows and give the backend
        /// a JobBudget.
        | Always

    /// How a solver splits a problem that is wider than the backend. Part of
    /// QaoaSolverConfig (Splitting) and of the MaxCut and Knapsack configurations.
    type SplitSettings =
        {
            /// When splitting is allowed (default SplitPolicy.OnSimulators)
            Policy: SplitPolicy

            /// Widest circuit, in qubits; ValueNone takes the backend's own limit. A problem wider
            /// than this is split even when the backend could run it, and a value above the
            /// backend's limit is cut down to it. Narrower pieces run faster and need more of
            /// them: on the local simulator a 12-qubit piece takes about a second to optimise and
            /// a 20-qubit piece minutes.
            MaxPieceQubits: int voption

            /// Most QUBO variables fixed to cut a sparse QUBO into pieces (conditioning). A
            /// piece runs once per assignment of the fixed variables it is coupled to, so one
            /// that touches all of the default 8 runs 256 times. A QUBO that needs more is not
            /// split. Values below 0 count as 0 and values above 20 as 20.
            MaxFixedVariables: int

            /// Most circuit runs of a block split (shares): one run per block and share of the
            /// capacity. A problem that needs more is not split. Default 256.
            MaxShareRuns: int

            /// Most items in a block of a block split, one qubit each (default 12); never more
            /// than the widest piece.
            MaxBlockItems: int
        }

    /// Default split settings: split on simulators only, pieces as wide as the backend runs,
    /// at most 8 fixed variables, at most 256 block runs, blocks of at most 12 items.
    let defaultSplitSettings: SplitSettings =
        {
            Policy = SplitPolicy.OnSimulators
            MaxPieceQubits = ValueNone
            MaxFixedVariables = 8
            MaxShareRuns = 256
            MaxBlockItems = 12
        }

    /// How a solution was put together when its problem was split. Solutions carry it as
    /// Split; None means the problem ran as one circuit.
    [<Struct>]
    type SplitReport =
        {
            /// Circuit runs made in place of the single run (each run optimises or sets the
            /// angles and samples once). Conditioning: one per piece and assignment of the fixed
            /// variables that piece is coupled to
            Runs: int

            /// Number of QUBO variables that were fixed to cut the problem (conditioning); 0
            /// for a block split
            FixedVariables: int

            /// Blocks the items were cut into (block split); 0 for conditioning
            Blocks: int

            /// Widest circuit among the runs, in qubits
            WidestPieceQubits: int
        }

    /// The report of a solution joined from independently solved parts (connected
    /// components), each with its own report or None: runs, fixed variables and blocks add
    /// up, and the widest piece is the widest of any part. None when no part was split.
    let combineSplitReports (reports: SplitReport option list) : SplitReport option =
        match List.choose id reports with
        | [] -> None
        | split ->
            Some
                {
                    Runs = split |> List.sumBy (fun report -> report.Runs)
                    FixedVariables = split |> List.sumBy (fun report -> report.FixedVariables)
                    Blocks = split |> List.sumBy (fun report -> report.Blocks)
                    WidestPieceQubits = split |> List.map (fun report -> report.WidestPieceQubits) |> List.max
                }

    /// The widest piece a split may use on this backend, or ValueNone when the problem runs as
    /// one circuit: the policy forbids a split on this backend, or the problem fits. The
    /// widest circuit is the smaller of what the backend runs
    /// (UnifiedBackend.getRunnableQubits) and settings.MaxPieceQubits, and never below one
    /// qubit; with neither, nothing is split.
    let splitPieceQubits
        (settings: SplitSettings)
        (backend: BackendAbstraction.IQuantumBackend)
        (problemQubits: int)
        : int voption =
        let allowed =
            match settings.Policy with
            | SplitPolicy.Never -> false
            | SplitPolicy.Always -> true
            | SplitPolicy.OnSimulators ->
                match backend with
                | :? BackendAbstraction.IShotSamplingBackend -> false
                | _ -> true

        let asked = settings.MaxPieceQubits |> ValueOption.filter (fun piece -> piece >= 1)

        let widest =
            match BackendAbstraction.UnifiedBackend.getRunnableQubits backend, asked with
            | Some limit, ValueSome piece -> ValueSome(max 1 (min piece limit))
            | Some limit, ValueNone -> ValueSome(max 1 limit)
            | None, piece -> piece

        match widest with
        | ValueSome piece when allowed && problemQubits > piece -> ValueSome piece
        | _ -> ValueNone

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

            /// Shots per evaluation in the optimization phase. Used only when the backend returns
            /// samples; a state-vector backend scores the angles by the exact expected energy.
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

            /// When and how a problem wider than the backend is split into circuits that fit.
            /// Default: on simulators only (see SplitSettings).
            Splitting: SplitSettings
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
            Splitting = defaultSplitSettings
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
            Splitting = defaultSplitSettings
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
            Splitting = defaultSplitSettings
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
    // SAMPLE STATISTICS
    // ================================================================================

    /// How the returned solution stood among the samples of the final sampling run.
    /// HitRate estimates the per-shot probability p of the returned solution; it says nothing
    /// about solutions that were never sampled, and with fewer than about 3 hits it is only
    /// an upper bound.
    [<Struct>]
    type SampleStatistics =
        {
            /// Samples drawn in the final sampling run
            Shots: int

            /// Qubits measured per sample
            Qubits: int

            /// Samples that decode to the returned solution; 0 when only classical repair produced it
            Hits: int

            /// Samples that passed the solver's classical validity check, before any repair
            Valid: int
        }

        /// Fraction of samples that decode to the returned solution
        member this.HitRate =
            if this.Shots > 0 then
                float this.Hits / float this.Shots
            else
                0.0

        /// Fraction of samples that passed the solver's classical validity check
        member this.ValidRate =
            if this.Shots > 0 then
                float this.Valid / float this.Shots
            else
                0.0

        /// Per-shot probability of one given bitstring under uniform random sampling: 1 / 2^Qubits.
        /// A HitRate near this value means the circuit did not favour the returned solution.
        member this.UniformRate = 2.0 ** -(float this.Qubits)

        /// Shots that return this solution at least once with the given confidence (0 < confidence < 1)
        /// at the observed HitRate; ValueNone when it was never sampled.
        member this.ShotsFor(confidence: float) : int voption =
            if this.Hits <= 0 || confidence <= 0.0 || confidence >= 1.0 then
                ValueNone
            elif this.Hits >= this.Shots then
                ValueSome 1
            else
                ValueSome(int (ceil (log (1.0 - confidence) / log (1.0 - this.HitRate))))

    /// Statistics of a sampling run: isValid is the solver's classical validity check and
    /// isReturned recognises the samples that decode to the solution the solver returns.
    let sampleStatistics
        (numQubits: int)
        (isValid: int[] -> bool)
        (isReturned: int[] -> bool)
        (samples: int[][])
        : SampleStatistics =
        {
            Shots = samples.Length
            Qubits = numQubits
            Hits = samples |> Array.sumBy (fun sample -> if isReturned sample then 1 else 0)
            Valid = samples |> Array.sumBy (fun sample -> if isValid sample then 1 else 0)
        }

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
    /// optimum; returns every sample, the parameters, whether the optimizer converged and its
    /// iteration count.
    /// The Nelder-Mead evaluations run one after another on the calling thread (each simplex step
    /// depends on the previous evaluation) and check the token before each circuit; the final
    /// sampling is awaited.
    let private optimizeAndSampleAllAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (energyOf: int[] -> float)
        (config: QaoaSolverConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<int[][] * (float * float)[] * bool * int, QuantumError>> =
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
                    (measurements, optimizedParams, optimResult.Converged, optimResult.Iterations))
        }

    /// optimizeAndSampleAllAsync reduced to the lowest-energy sample.
    let private optimizeAndSampleAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (energyOf: int[] -> float)
        (config: QaoaSolverConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<int[] * (float * float)[] * bool, QuantumError>> =
        task {
            let! run =
                optimizeAndSampleAllAsync backend problemHam mixerHam energyOf config cancellationToken

            return
                run
                |> Result.map (fun (samples, parameters, converged, _) ->
                    (samples |> Array.minBy energyOf, parameters, converged))
        }

    /// Grid search over (γ, β) by expected energy, then FinalShots samples at the best grid
    /// point; returns every sample and the parameters. At most maxConcurrency grid points
    /// are in flight.
    let private gridSearchAndSampleAllAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problemHam: QaoaCircuit.ProblemHamiltonian)
        (mixerHam: QaoaCircuit.MixerHamiltonian)
        (energyOf: int[] -> float)
        (config: QaoaSolverConfig)
        (maxConcurrency: int)
        (cancellationToken: CancellationToken)
        : Task<Result<int[][] * (float * float)[], QuantumError>> =
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

                return finalResult |> Result.map (fun measurements -> (measurements, bestParams))
        }

    /// gridSearchAndSampleAllAsync reduced to the lowest-energy sample.
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
            let! run =
                gridSearchAndSampleAllAsync backend problemHam mixerHam energyOf config maxConcurrency cancellationToken

            return
                run
                |> Result.map (fun (samples, parameters) -> (samples |> Array.minBy energyOf, parameters))
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

    /// A finished QAOA run.
    type QaoaRun =
        {
            /// Every sample of the final sampling run
            Samples: int[][]

            /// The lowest-energy sample
            Best: int[]

            /// The (γ, β) angles the final run used
            Parameters: (float * float)[]

            /// Whether the optimizer converged; None after a grid search
            Converged: bool option

            /// Optimizer iterations used; ValueNone after a grid search
            Iterations: int voption
        }

    /// Execute QAOA the way the configuration asks: Nelder-Mead parameter optimization when
    /// config.EnableOptimization, else a sequential (maxConcurrency = 1) grid search.
    /// Returns every final sample along with the lowest-energy one.
    let runQaoaSampledAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (config: QaoaSolverConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<QaoaRun, QuantumError>> =
        match validateConfig config with
        | Error err -> Task.FromResult(Error err)
        | Ok() ->
            task {
                let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo qubo
                let mixerHam = QaoaCircuit.MixerHamiltonian.create (Array2D.length1 qubo)
                let energyOf = denseEnergy qubo

                let toRun converged iterations (samples: int[][]) parameters =
                    {
                        Samples = samples
                        Best = samples |> Array.minBy energyOf
                        Parameters = parameters
                        Converged = converged
                        Iterations = iterations
                    }

                if config.EnableOptimization then
                    let! optimized =
                        optimizeAndSampleAllAsync backend problemHam mixerHam energyOf config cancellationToken

                    return
                        optimized
                        |> Result.map (fun (samples, parameters, converged, iterations) ->
                            toRun (Some converged) (ValueSome iterations) samples parameters)
                else
                    let! searched =
                        gridSearchAndSampleAllAsync backend problemHam mixerHam energyOf config 1 cancellationToken

                    return
                        searched
                        |> Result.map (fun (samples, parameters) -> toRun None ValueNone samples parameters)
            }

    /// runQaoaSampledAsync reduced to (bestBitstring, parameters, converged), converged being
    /// None after a grid search.
    let runQaoaAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (qubo: float[,])
        (config: QaoaSolverConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<int[] * (float * float)[] option * bool option, QuantumError>> =
        task {
            let! run = runQaoaSampledAsync backend qubo config cancellationToken
            return run |> Result.map (fun run -> (run.Best, Some run.Parameters, run.Converged))
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
