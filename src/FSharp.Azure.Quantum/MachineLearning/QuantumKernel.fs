namespace FSharp.Azure.Quantum.MachineLearning

/// Quantum Kernel Methods for Machine Learning.
///
/// Implements quantum kernel computation K(x,y) = |⟨φ(x)|φ(y)⟩|²
/// where φ is a quantum feature map.
///
/// Reference: Havlíček et al., "Supervised learning with quantum-enhanced
/// feature spaces" Nature (2019)
///
/// Shots: on an exact simulator every kernel entry is estimated from `shots` samples of the
/// exact state. On a shot-sampling backend (IShotSamplingBackend: cloud hardware and cloud
/// simulators) each entry is read off one job's measured frequencies, whose shot count the
/// backend fixed when it was created; `shots` must equal that count, and anything else is an
/// Error naming both (as Primitives.sample does) before any job is submitted. The results are
/// never resampled to another count, which would mix classical randomness into the measurement.

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction

module QuantumKernels =

    // ========================================================================
    // KERNEL COMPUTATION
    // ========================================================================

    /// Build quantum circuit for kernel evaluation K(x, y)
    ///
    /// Circuit structure:
    ///   1. Start with |0⟩
    ///   2. Apply feature map U_φ(x)
    ///   3. Apply inverse feature map U_φ†(y)
    ///   4. Measure probability of |0⟩
    ///
    /// Kernel value K(x,y) = P(|0⟩) = |⟨0|U_φ†(y)U_φ(x)|0⟩|²
    let private buildKernelCircuit
        (featureMap: FeatureMapType)
        (x: float array)
        (y: float array)
        : QuantumResult<Circuit> =

        if x.Length <> y.Length then
            Error(
                QuantumError.ValidationError(
                    "Input",
                    $"Feature vectors must have same length: x={x.Length}, y={y.Length}"
                )
            )
        else
            // Build forward feature map for x
            let circuitX =
                match featureMap with
                | AngleEncoding -> FeatureMap.angleEncoding x
                | ZZFeatureMap depth -> FeatureMap.zzFeatureMap depth x
                | PauliFeatureMap(paulis, depth) -> FeatureMap.pauliFeatureMap paulis depth x
                | AmplitudeEncoding -> FeatureMap.amplitudeEncoding x

            // Build feature map for y
            let circuitY =
                match featureMap with
                | AngleEncoding -> FeatureMap.angleEncoding y
                | ZZFeatureMap depth -> FeatureMap.zzFeatureMap depth y
                | PauliFeatureMap(paulis, depth) -> FeatureMap.pauliFeatureMap paulis depth y
                | AmplitudeEncoding -> FeatureMap.amplitudeEncoding y

            // Create inverse of circuitY (adjoint: reverse gate order, negate rotation angles)
            // Gates are stored in reverse chronological order internally (prepend convention).
            // To form the adjoint: reverse the stored list (restoring forward order),
            // then map inverseGate to negate angles. The result is the adjoint in
            // prepend-storage order.
            let inverseGatesY =
                circuitY.Gates
                |> List.rev
                |> List.map (fun gate ->
                    match gate with
                    | H q -> H q
                    | X q -> X q
                    | Y q -> Y q
                    | Z q -> Z q
                    | RX(q, angle) -> RX(q, -angle)
                    | RY(q, angle) -> RY(q, -angle)
                    | RZ(q, angle) -> RZ(q, -angle)
                    | CNOT(c, t) -> CNOT(c, t)
                    | CZ(c, t) -> CZ(c, t)
                    | SWAP(q1, q2) -> SWAP(q1, q2)
                    | _ -> gate // Keep other gates as-is
                )

            // Combine: U_φ(x) followed by U_φ†(y)
            // In reversed storage: adjoint(Y) @ circuitX
            let combinedGates = inverseGatesY @ circuitX.Gates

            Ok
                {
                    QubitCount = circuitX.QubitCount
                    Gates = combinedGates
                }

    /// Kernel circuits a sampling backend (IShotSamplingBackend: cloud hardware, where every
    /// circuit is a separately queued and billed job) has in flight at once. Simulators are not
    /// limited. The shared bound: see BackendAbstraction.JobThrottle.
    [<Literal>]
    let MaxConcurrentSampledJobs = JobThrottle.MaxConcurrentSampledJobs

    /// Circuits in flight at once on `backend`: MaxConcurrentSampledJobs on a sampling backend,
    /// unlimited otherwise.
    let private maxConcurrency (backend: IQuantumBackend) : int = JobThrottle.maxConcurrency backend

    /// Start `jobs` with at most `limit` running at once, results in job order.
    let private throttled
        (limit: int)
        (cancellationToken: CancellationToken)
        (jobs: (unit -> Task<'T>)[])
        : Task<'T[]> =
        JobThrottle.throttled limit cancellationToken jobs

    /// Ok when `shots` can be honoured on `backend`: positive, and on a shot-sampling backend
    /// equal to the shots it measures per job (see the module notes).
    let private validateShots (backend: IQuantumBackend) (shots: int) : QuantumResult<unit> =
        if shots <= 0 then
            Error(QuantumError.ValidationError("Input", "Number of shots must be positive"))
        else
            match FSharp.Azure.Quantum.Primitives.shotsPerCircuit backend with
            | Some deviceShots when deviceShots <> shots ->
                Error(FSharp.Azure.Quantum.Primitives.fixedShotsError backend deviceShots shots)
            | _ -> Ok()

    /// Fidelity estimate P(|0…0⟩) of an executed kernel circuit. A sampling backend's state
    /// already holds its job's outcome frequencies, which are read as they are; an exact state
    /// (simulator) is sampled `shots` times.
    let private allZeroProbability (backend: IQuantumBackend) (state: QuantumState) (shots: int) : float =
        match backend with
        | :? IShotSamplingBackend as sampling when sampling.Shots > 0 ->
            QuantumState.probability (Array.zeroCreate (QuantumState.numQubits state)) state
        | _ ->
            let allZeroCount =
                QuantumState.measure state shots
                |> Array.filter (fun measurement -> measurement |> Array.forall ((=) 0))
                |> Array.length

            float allZeroCount / float shots

    /// Execute kernel circuit and measure probability of |0...0⟩ state asynchronously.
    /// Uses backend.ExecuteToStateAsync for non-blocking I/O.
    let private measureKernelCircuitAsync
        (backend: IQuantumBackend)
        (circuit: Circuit)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float>> =
        task {
            // Execute via the shared primitive (Primitives.getStateAsync), then read P(|0…0⟩).
            let! stateResult =
                FSharp.Azure.Quantum.Primitives.getStateAsync backend circuit cancellationToken

            return
                match stateResult with
                | Error e -> Error(QuantumError.ValidationError("Input", $"Quantum backend execution failed: {e}"))
                | Ok state -> Ok(allZeroProbability backend state shots)
        }

    /// Compute quantum kernel value K(x, y) = |⟨φ(x)|φ(y)⟩|² asynchronously.
    /// Uses backend.ExecuteToStateAsync for non-blocking I/O. On a shot-sampling backend
    /// `shots` must equal the backend's Shots (see the module notes).
    let computeKernelAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (x: float array)
        (y: float array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float>> =
        task {
            match validateShots backend shots with
            | Error e -> return Error e
            | Ok() when x.Length = 0 -> return Error(QuantumError.Other "Feature vectors cannot be empty")
            | Ok() ->
                match buildKernelCircuit featureMap x y with
                | Error e -> return Error e
                | Ok circuit -> return! measureKernelCircuitAsync backend circuit shots cancellationToken
        }

    // ========================================================================
    // KERNEL MATRIX COMPUTATION
    // ========================================================================

    /// Compute full kernel matrix for a dataset using Task.WhenAll.
    /// All upper-triangle kernel entries are computed concurrently via
    /// backend.ExecuteToStateAsync; a sampling backend (cloud) gets at most
    /// MaxConcurrentSampledJobs circuits at once.
    let computeKernelMatrixAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (data: float array array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float[,]>> =
        task {
            // Shots are checked once, before any circuit is submitted.
            match validateShots backend shots with
            | Error e -> return Error e
            | Ok() when data.Length = 0 -> return Error(QuantumError.Other "Dataset cannot be empty")
            | Ok() ->
                let n = data.Length

                let uniquePairs =
                    [|
                        for i in 0 .. n - 1 do
                            for j in i .. n - 1 do
                                yield (i, j)
                    |]

                let! kernelEntries =
                    uniquePairs
                    |> Array.map (fun (i, j) ->
                        fun () ->
                            task {
                                let! result =
                                    computeKernelAsync backend featureMap data.[i] data.[j] shots cancellationToken

                                return (i, j, result)
                            })
                    |> throttled (maxConcurrency backend) cancellationToken

                return
                    match kernelEntries |> Array.tryFind (fun (_, _, r) -> Result.isError r) with
                    | Some(i, j, Error e) ->
                        Error(QuantumError.ValidationError("Input", $"Kernel computation failed at ({i},{j}): {e}"))
                    | _ ->
                        let kernelMatrix = Array2D.zeroCreate n n

                        for (i, j, result) in kernelEntries do
                            match result with
                            | Ok kernelValue ->
                                kernelMatrix.[i, j] <- kernelValue

                                if i <> j then
                                    kernelMatrix.[j, i] <- kernelValue
                            | Error _ -> ()

                        Ok kernelMatrix
        }

    /// Compute kernel matrix between train and test sets using Task.WhenAll.
    /// All test-train kernel pairs are computed concurrently; a sampling backend (cloud) gets
    /// at most MaxConcurrentSampledJobs circuits at once.
    let computeKernelMatrixTrainTestAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (trainData: float array array)
        (testData: float array array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float[,]>> =
        task {
            // Shots are checked once, before any circuit is submitted.
            match validateShots backend shots with
            | Error e -> return Error e
            | Ok() when trainData.Length = 0 -> return Error(QuantumError.Other "Training dataset cannot be empty")
            | Ok() when testData.Length = 0 -> return Error(QuantumError.Other "Test dataset cannot be empty")
            | Ok() ->
                let nTest = testData.Length
                let nTrain = trainData.Length

                let allPairs =
                    [|
                        for i in 0 .. nTest - 1 do
                            for j in 0 .. nTrain - 1 do
                                yield (i, j)
                    |]

                let! kernelEntries =
                    allPairs
                    |> Array.map (fun (i, j) ->
                        fun () ->
                            task {
                                let! result =
                                    computeKernelAsync
                                        backend
                                        featureMap
                                        testData.[i]
                                        trainData.[j]
                                        shots
                                        cancellationToken

                                return (i, j, result)
                            })
                    |> throttled (maxConcurrency backend) cancellationToken

                return
                    match kernelEntries |> Array.tryFind (fun (_, _, result) -> Result.isError result) with
                    | Some(i, j, Error e) ->
                        Error(
                            QuantumError.ValidationError(
                                "Input",
                                $"Kernel computation failed at test[{i}], train[{j}]: {e}"
                            )
                        )
                    | _ ->
                        let kernelMatrix = Array2D.zeroCreate nTest nTrain

                        for (i, j, result) in kernelEntries do
                            result |> Result.iter (fun kernelValue -> kernelMatrix.[i, j] <- kernelValue)

                        Ok kernelMatrix
        }

    // ========================================================================
    // KERNEL PROPERTIES
    // ========================================================================

    /// Check if kernel matrix is symmetric (within tolerance)
    let isSymmetric (matrix: float[,]) (tolerance: float) : bool =
        let n = Array2D.length1 matrix
        let m = Array2D.length2 matrix

        if n <> m then
            false
        else
            seq {
                for i in 0 .. n - 1 do
                    for j in i + 1 .. n - 1 do
                        yield (i, j)
            }
            |> Seq.forall (fun (i, j) -> abs (matrix.[i, j] - matrix.[j, i]) <= tolerance)

    /// Check if kernel matrix is positive semi-definite
    ///
    /// A valid kernel matrix must be positive semi-definite, meaning
    /// all eigenvalues are non-negative.
    ///
    /// Note: This is a simplified check. Full eigenvalue computation
    /// would require linear algebra library.
    let isPositiveSemiDefinite (matrix: float[,]) : bool =
        // Simple check: diagonal elements should be non-negative
        let n = Array2D.length1 matrix
        [| 0 .. n - 1 |] |> Array.forall (fun i -> matrix.[i, i] >= 0.0)

    /// Normalize kernel matrix (divide by diagonal elements)
    ///
    /// Normalized kernel: K_norm[i,j] = K[i,j] / sqrt(K[i,i] * K[j,j])
    ///
    /// This ensures K_norm[i,i] = 1 for all i
    let normalizeKernelMatrix (matrix: float[,]) : QuantumResult<float[,]> =
        let n = Array2D.length1 matrix
        let m = Array2D.length2 matrix

        if n <> m then
            Error(QuantumError.ValidationError("Input", "Kernel matrix must be square for normalization"))
        else
            // Check diagonal elements are positive
            let diagonalPositive =
                [| 0 .. n - 1 |] |> Array.forall (fun i -> matrix.[i, i] > 0.0)

            if not diagonalPositive then
                Error(QuantumError.ValidationError("Input", "Cannot normalize: diagonal elements must be positive"))
            else
                let normalized = Array2D.zeroCreate n n

                for i in 0 .. n - 1 do
                    for j in 0 .. n - 1 do
                        let denominator = sqrt (matrix.[i, i] * matrix.[j, j])
                        normalized.[i, j] <- matrix.[i, j] / denominator

                Ok normalized

    // ========================================================================
    // HELPER FUNCTIONS
    // ========================================================================

    /// Get diagonal elements of kernel matrix
    let getDiagonal (matrix: float[,]) : float array =
        let n = min (Array2D.length1 matrix) (Array2D.length2 matrix)
        Array.init n (fun i -> matrix.[i, i])

    /// Compute kernel matrix statistics (for debugging/analysis)
    type KernelMatrixStats =
        {
            Mean: float
            StdDev: float
            Min: float
            Max: float
            DiagonalMean: float
            IsSymmetric: bool
            IsPositiveSemiDefinite: bool
        }

    /// Compute statistics for kernel matrix
    let computeStats (matrix: float[,]) : KernelMatrixStats =
        let n = Array2D.length1 matrix
        let m = Array2D.length2 matrix

        // Flatten matrix
        let values =
            [|
                for i in 0 .. n - 1 do
                    for j in 0 .. m - 1 -> matrix.[i, j]
            |]

        let mean = Array.average values
        let variance = Array.averageBy (fun x -> (x - mean) ** 2.0) values
        let stdDev = sqrt variance
        let minVal = Array.min values
        let maxVal = Array.max values

        let diagonal = getDiagonal matrix
        let diagonalMean = Array.average diagonal

        {
            Mean = mean
            StdDev = stdDev
            Min = minVal
            Max = maxVal
            DiagonalMean = diagonalMean
            IsSymmetric = isSymmetric matrix 1e-6
            IsPositiveSemiDefinite = isPositiveSemiDefinite matrix
        }
