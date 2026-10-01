namespace FSharp.Azure.Quantum.Quantum

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Core

/// Quantum TSP Solver using QAOA and Backend Abstraction
///
/// ALGORITHM-LEVEL API (for advanced users):
/// This module provides direct access to quantum TSP solving via QAOA.
/// For business-domain API, use the TSP module instead.
///
/// COMPARISON:
///   // Business Domain (Recommended for most users):
///   open FSharp.Azure.Quantum
///   let! tour = TSP.solveAsync cities None CancellationToken.None  // Automatic LocalBackend
///
///   // Algorithm Level (This module - for experts):
///   open FSharp.Azure.Quantum.Quantum
///   let backend = BackendAbstraction.createIonQBackend(...)
///   let! result = QuantumTspSolver.solveAsync backend distances config CancellationToken.None
///
/// RULE 1 COMPLIANCE:
/// ✅ Requires IQuantumBackend parameter (explicit quantum execution)
///
/// TECHNICAL DETAILS:
/// - Execution: Quantum hardware/simulator via backend
/// - Algorithm: QAOA (Quantum Approximate Optimization Algorithm)
/// - Speed: Seconds to minutes (includes job queue wait for cloud backends)
/// - Cost: ~$10-100 per run on real quantum hardware (IonQ, Rigetti)
/// - LocalBackend: Free simulation (limited to ~16 qubits, which is 4 cities)
///
/// QUANTUM PIPELINE:
/// 1. TSP Distance Matrix → GraphOptimization Problem (one directed edge per ordered city pair)
/// 2. GraphOptimization → QUBO Matrix (N² variables: city i in time slot t, closed tour)
/// 3. QUBO → QAOA Circuit (Hamiltonians + Layers)
/// 4. Execute on Quantum Backend (IonQ/Rigetti/Local)
/// 5. Decode Measurements → TSP Tours (a measurement is a tour only when it is a
///    permutation matrix; other measurements count as invalid and are dropped)
/// 6. Return the shortest tour that was measured
///
/// A tour is one of N! bitstrings among 2^(N²), so valid measurements are a small share of
/// the shots. When no measurement is a tour the result is an Error; no tour is constructed
/// classically.
///
/// Example:
///   let backend = LocalBackend() :> IQuantumBackend
///   let config = QuantumTspSolver.defaultConfig
///   match! QuantumTspSolver.solveAsync backend distances config CancellationToken.None with
///   | Ok result -> printfn "Tour length: %f" result.TourLength
///   | Error msg -> printfn "Error: %s" msg
module QuantumTspSolver =

    /// Configuration for quantum TSP solving
    type QuantumTspConfig =
        {
            /// Number of QAOA layers (p). More layers put more probability on valid tours
            /// and cost a deeper circuit.
            NumLayers: int

            /// Number of shots per optimization step when the backend returns no state
            /// vector (a state-vector backend gives the exact expected energy instead)
            OptimizationShots: int

            /// Number of shots for final execution (high for accuracy)
            FinalShots: int

            /// Enable QAOA parameter optimization via the shared classical optimizer
            /// (QaoaExecutionHelpers.runQaoaSampledAsync: Nelder-Mead over the angles of
            /// every layer). When false a single circuit runs at InitialParameters.
            EnableOptimization: bool

            /// (gamma, beta) of every layer when optimization is disabled. Not read when
            /// optimization is enabled: the optimizer starts from the shared ramp schedule.
            /// Units: QaoaExecutionHelpers' normalised Hamiltonian, minimisation convention
            /// (see Core.QaoaCircuit).
            InitialParameters: float * float

            /// Upper bound on Nelder-Mead iterations when EnableOptimization is true.
            ///
            /// Each iteration executes a full QAOA circuit, so this is the knob that
            /// makes the variational loop affordable on an expensive backend: a
            /// state-vector simulator runs 1000 iterations in milliseconds, while a
            /// topological (fusion-tree) backend carries 2^n explicit terms per gate
            /// and would need hours for the same budget.
            MaxOptimizationIterations: int
        }

    /// Default configuration for quantum TSP solving
    let defaultConfig =
        {
            NumLayers = 2
            OptimizationShots = 100
            FinalShots = 1000
            EnableOptimization = true
            InitialParameters = (0.5, 0.5)
            MaxOptimizationIterations = 1000
        }

    /// Configuration for quick prototyping: no variational loop at all.
    /// Runs a single one-layer QAOA circuit at the initial parameters, which is what you
    /// want when the backend is slow. Expect fewer valid tours per shot than with
    /// defaultConfig.
    let fastConfig =
        { defaultConfig with
            NumLayers = 1
            OptimizationShots = 50
            FinalShots = 500
            EnableOptimization = false
        }

    /// Quantum TSP solution with execution details
    type QuantumTspSolution =
        {
            /// Best tour found, starting at city 0
            Tour: int array

            /// Tour length (distance), the return to the first city included
            TourLength: float

            /// Backend used for execution
            BackendName: string

            /// Number of measurement shots
            NumShots: int

            /// Execution time in milliseconds
            ElapsedMs: float

            /// Length of the best tour (the QUBO energy of a tour is its length plus a constant)
            BestEnergy: float

            /// The shortest measured tours with frequencies (tour, length, count). The
            /// bitstrings of one cycle (its rotations and, for symmetric distances, its two
            /// directions) count as one tour.
            TopSolutions: (int array * float * int) list

            /// First layer's optimized (gamma, beta) if optimization was enabled;
            /// LayerParameters has every layer
            OptimizedParameters: (float * float) option

            /// Optimizer iterations used; None when optimization was disabled
            OptimizationIterations: int option

            /// Whether parameter optimization converged
            OptimizationConverged: bool option

            /// Standing of this solution among the final samples: Hits counts the samples that
            /// decode to Tour, Valid the samples that are permutation matrices
            Sampling: QaoaExecutionHelpers.SampleStatistics option

            /// The (gamma, beta) of every layer of the final circuit
            LayerParameters: (float * float)[]
        }

    /// Node id of a city: fixed width, so that ids sort in city order and QUBO variable
    /// i·n + t belongs to city i.
    let private cityId (city: int) = city.ToString "D6"

    /// The GraphOptimization problem of a distance matrix: one directed edge per ordered
    /// pair of cities, so that distances.[i, j] is charged for the step i → j only.
    let private graphProblem (distances: float[,]) =
        let numCities = distances.GetLength 0

        let nodes =
            List.init (max 0 numCities) (fun i -> GraphOptimization.node (cityId i) i)

        let edges =
            [
                for i in 0 .. numCities - 1 do
                    for j in 0 .. numCities - 1 do
                        if i <> j then
                            yield GraphOptimization.directedEdge (cityId i) (cityId j) distances.[i, j]
            ]

        GraphOptimization
            .GraphOptimizationBuilder()
            .Nodes(nodes)
            .Edges(edges)
            .Objective(GraphOptimization.MinimizeTotalWeight)
            .Build()

    /// The QUBO the solver runs for a distance matrix: variable i·n + t is 1 when city i
    /// is visited in time slot t. Its minima are the permutation matrices of the shortest
    /// closed tours (see GraphOptimization.toQubo).
    let toQubo (distances: float[,]) : float[,] =
        let quboMatrix = GraphOptimization.toQubo (graphProblem distances)
        Qubo.toDenseArray quboMatrix.NumVariables quboMatrix.Q

    /// Whether both directions of every step have the same distance.
    let private isSymmetric (distances: float[,]) =
        let n = distances.GetLength 0

        seq {
            for i in 0 .. n - 1 do
                for j in i + 1 .. n - 1 -> distances.[i, j] = distances.[j, i]
        }
        |> Seq.forall id

    /// One representative of a closed tour: rotated to start at city 0 and, when both
    /// directions have the same length, walked in the direction whose second city has the
    /// lower index.
    let private canonicalTour (symmetric: bool) (tour: int[]) : int[] =
        let n = tour.Length
        let start = Array.findIndex ((=) 0) tour
        let rotated = Array.init n (fun k -> tour.[(start + k) % n])

        if symmetric && n > 2 && rotated.[n - 1] < rotated.[1] then
            Array.init n (fun k -> rotated.[(n - k) % n])
        else
            rotated

    /// Decoder of measurements for a distance matrix (see tryDecodeTour).
    let private tourDecoder (distances: float[,]) : int[] -> int[] option =
        let problem = graphProblem distances
        let symmetric = isSymmetric distances

        fun (measurement: int[]) ->
            GraphOptimization.tryDecodeTour problem (Array.toList measurement)
            |> Option.map (List.map int >> Array.ofList >> canonicalTour symmetric)

    /// The tour one measurement encodes, or None when the measurement is not a permutation
    /// matrix (exactly one city per time slot and one time slot per city). Nothing is
    /// repaired or filled in.
    ///
    /// The tour is returned in its canonical form: starting at city 0 and, for symmetric
    /// distances, in the direction whose second city has the lower index. The 2n bitstrings
    /// of one cycle (n when distances depend on direction) therefore decode to the same tour.
    let tryDecodeTour (distances: float[,]) (measurement: int[]) : int[] option = tourDecoder distances measurement

    /// The first reason a distance matrix cannot be encoded, if any.
    let private distanceError (distances: float[,]) : QuantumError option =
        let rows = distances.GetLength 0
        let columns = distances.GetLength 1

        if rows <> columns then
            Some(QuantumError.ValidationError("distances", $"Distance matrix must be square, got {rows}x{columns}"))
        else
            seq {
                for i in 0 .. rows - 1 do
                    for j in 0 .. rows - 1 do
                        if i <> j then
                            yield (i, j, distances.[i, j])
            }
            |> Seq.tryFind (fun (_, _, d) -> Double.IsNaN d || Double.IsInfinity d || d < 0.0)
            |> Option.map (fun (i, j, d) ->
                QuantumError.ValidationError(
                    "distances",
                    $"Distances must be finite and non-negative, but the distance from city {i} to city {j} is {d}"
                ))

    /// Shared implementation of solveAsync.
    let private solveCoreAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (distances: float[,])
        (config: QuantumTspConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<QuantumTspSolution, QuantumError>> =
        task {
            let stopwatch = Stopwatch.StartNew()

            // Validate inputs
            let numCities = distances.GetLength 0
            let requiredQubits = numCities * numCities // TSP uses N^2 qubits for N cities

            // Backend capacity, when the backend declares one (IQubitLimitedBackend).
            // Checking here — rather than letting the backend fail deep inside circuit
            // execution — lets the message name the problem size, the backend and its
            // limit, instead of a bare "qubits must be between 0 and N".
            let capacityError =
                BackendAbstraction.UnifiedBackend.getRunnableQubits backend
                |> Option.bind (fun maxQubits ->
                    if requiredQubits > maxQubits then
                        Some(
                            QuantumError.ValidationError(
                                "numCities",
                                $"TSP with {numCities} cities needs {requiredQubits} qubits (N²), but backend '{backend.Name}' supports at most {maxQubits} qubits"
                            )
                        )
                    else
                        None)

            let matrixError = distanceError distances

            if numCities < 2 then
                return Error(QuantumError.ValidationError("numCities", "TSP requires at least 2 cities"))
            elif config.FinalShots <= 0 then
                return Error(QuantumError.ValidationError("numShots", "Number of shots must be positive"))
            elif config.NumLayers <= 0 then
                return Error(QuantumError.ValidationError("NumLayers", $"must be > 0, got {config.NumLayers}"))
            elif config.EnableOptimization && config.MaxOptimizationIterations <= 0 then
                return
                    Error(
                        QuantumError.ValidationError(
                            "MaxOptimizationIterations",
                            $"must be > 0 when optimization is enabled, got {config.MaxOptimizationIterations}"
                        )
                    )
            elif matrixError.IsSome then
                return Error matrixError.Value
            elif capacityError.IsSome then
                return Error capacityError.Value
            else
                try
                    // Steps 1-3: distance matrix → GraphOptimization problem → QUBO
                    let qubo = toQubo distances
                    let decode = tourDecoder distances

                    // Step 4: run QAOA. With optimization the shared helper tunes the angles
                    // of every layer and samples at the optimum; without it a single circuit
                    // runs at the configured angles.
                    let! run =
                        if config.EnableOptimization then
                            task {
                                let sharedConfig: QaoaExecutionHelpers.QaoaSolverConfig =
                                    {
                                        NumLayers = config.NumLayers
                                        OptimizationShots = config.OptimizationShots
                                        FinalShots = config.FinalShots
                                        EnableOptimization = true
                                        EnableConstraintRepair = false
                                        MaxOptimizationIterations = config.MaxOptimizationIterations
                                        Splitting = QaoaExecutionHelpers.defaultSplitSettings
                                    }

                                let! sampled =
                                    QaoaExecutionHelpers.runQaoaSampledAsync backend qubo sharedConfig cancellationToken

                                return
                                    sampled
                                    |> Result.map (fun r ->
                                        (r.Samples, r.Parameters, r.Converged, ValueOption.toOption r.Iterations))
                            }
                        else
                            task {
                                let parameters = Array.create config.NumLayers config.InitialParameters

                                let! sampled =
                                    QaoaExecutionHelpers.executeFromQuboAsync
                                        backend
                                        qubo
                                        parameters
                                        config.FinalShots
                                        cancellationToken

                                return sampled |> Result.map (fun samples -> (samples, parameters, None, None))
                            }

                    match run with
                    | Error err -> return Error err
                    | Ok(measurements, parameters, converged, iterations) ->

                        // Step 5: a measurement is a tour only when it is a permutation matrix
                        let tours = measurements |> Array.choose decode

                        if tours.Length = 0 then
                            return
                                Error(
                                    QuantumError.OperationError(
                                        "DecodeSolution",
                                        $"No valid tour in {measurements.Length} shots: no measurement was a permutation matrix "
                                        + $"({numCities} cities, {requiredQubits} qubits, NumLayers = {parameters.Length}). "
                                        + "Take more shots (FinalShots) or use more layers (NumLayers)."
                                    )
                                )
                        else
                            // Group by tour and count frequencies: shortest first, then most
                            // frequent, then by city order
                            let tourFrequencies =
                                tours
                                |> Array.countBy id
                                |> Array.map (fun (tour, frequency) ->
                                    (tour, TspSolver.calculateTourLength distances tour, frequency))
                                |> Array.sortBy (fun (tour, length, frequency) -> (length, -frequency, tour))

                            // Step 6: best tour (shortest)
                            let (bestTour, bestLength, _) = tourFrequencies.[0]

                            let sampling =
                                QaoaExecutionHelpers.sampleStatistics
                                    requiredQubits
                                    (fun sample -> (decode sample).IsSome)
                                    (fun sample -> decode sample = Some bestTour)
                                    measurements

                            let elapsedMs = stopwatch.Elapsed.TotalMilliseconds

                            return
                                Ok
                                    {
                                        Tour = bestTour
                                        TourLength = bestLength
                                        BackendName = backend.Name
                                        NumShots = config.FinalShots
                                        ElapsedMs = elapsedMs
                                        BestEnergy = bestLength
                                        TopSolutions =
                                            tourFrequencies |> Array.take (min 5 tourFrequencies.Length) |> Array.toList
                                        OptimizedParameters =
                                            if config.EnableOptimization then
                                                Array.tryHead parameters
                                            else
                                                None
                                        OptimizationIterations = iterations
                                        OptimizationConverged = converged
                                        Sampling = Some sampling
                                        LayerParameters = parameters
                                    }

                with ex when not (ex :? OperationCanceledException) ->
                    return
                        Error(
                            QuantumError.OperationError(
                                "QuantumTspSolver",
                                $"Quantum TSP solver failed: %s{ex.Message}"
                            )
                        )
        }

    /// Solve TSP using quantum backend via QAOA (async).
    ///
    /// Full Pipeline:
    /// 1. Distance matrix → GraphOptimization problem
    /// 2. GraphOptimization → QUBO matrix
    /// 3. QUBO → QaoaCircuit (Hamiltonians + layers)
    /// 4. (Optional) Optimize the QAOA parameters of every layer using the shared classical
    ///    optimizer; its evaluations run one after another (each step depends on the previous one)
    /// 5. Execute circuit on quantum backend with the final parameters
    /// 6. Decode measurements → tours (permutation matrices only)
    /// 7. Return the shortest measured tour, or an Error when no measurement is a tour
    let solveAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (distances: float[,])
        (config: QuantumTspConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<QuantumTspSolution, QuantumError>> =
        task {
            cancellationToken.ThrowIfCancellationRequested()
            return! solveCoreAsync backend distances config cancellationToken
        }
