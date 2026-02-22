namespace FSharp.Azure.Quantum.MachineLearning

/// Quantum Kernel Methods for Machine Learning.
///
/// Implements quantum kernel computation K(x,y) = |⟨φ(x)|φ(y)⟩|²
/// where φ is a quantum feature map.
///
/// Reference: Havlíček et al., "Supervised learning with quantum-enhanced 
/// feature spaces" Nature (2019)

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
            Error (QuantumError.ValidationError ("Input", $"Feature vectors must have same length: x={x.Length}, y={y.Length}"))
        else
            // Build forward feature map for x
            let circuitX = 
                match featureMap with
                | AngleEncoding -> FeatureMap.angleEncoding x
                | ZZFeatureMap depth -> FeatureMap.zzFeatureMap depth x
                | PauliFeatureMap (paulis, depth) -> FeatureMap.pauliFeatureMap paulis depth x
                | AmplitudeEncoding -> FeatureMap.amplitudeEncoding x
            
            // Build feature map for y  
            let circuitY =
                match featureMap with
                | AngleEncoding -> FeatureMap.angleEncoding y
                | ZZFeatureMap depth -> FeatureMap.zzFeatureMap depth y
                | PauliFeatureMap (paulis, depth) -> FeatureMap.pauliFeatureMap paulis depth y
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
                    | RX (q, angle) -> RX (q, -angle)
                    | RY (q, angle) -> RY (q, -angle)
                    | RZ (q, angle) -> RZ (q, -angle)
                    | CNOT (c, t) -> CNOT (c, t)
                    | CZ (c, t) -> CZ (c, t)
                    | SWAP (q1, q2) -> SWAP (q1, q2)
                    | _ -> gate  // Keep other gates as-is
                )
            
            // Combine: U_φ(x) followed by U_φ†(y)
            // In reversed storage: adjoint(Y) @ circuitX
            let combinedGates = inverseGatesY @ circuitX.Gates
            
            Ok { 
                QubitCount = circuitX.QubitCount
                Gates = combinedGates
            }
    
    /// Execute kernel circuit and measure probability of |0...0⟩ state
    [<Obsolete("Use measureKernelCircuitAsync for non-blocking I/O against cloud backends.")>]
    let private measureKernelCircuit
        (backend: IQuantumBackend)
        (circuit: Circuit)
        (shots: int)
        : QuantumResult<float> =
        
        // Wrap circuit for backend execution (like VQC does)
        let wrappedCircuit = CircuitWrapper(circuit)
        
        match backend.ExecuteToState wrappedCircuit with
        | Error e -> Error (QuantumError.ValidationError ("Input", $"Quantum backend execution failed: {e}"))
        | Ok state ->
            
            // Perform measurements on quantum state
            let measurements = QuantumState.measure state shots
            
            // Count measurements where all qubits are |0⟩
            let allZeroCount = 
                measurements
                |> Array.filter (fun measurement ->
                    measurement |> Array.forall ((=) 0)
                )
                |> Array.length
            
            let probability = float allZeroCount / float shots
            Ok probability

    /// Execute kernel circuit and measure probability of |0...0⟩ state asynchronously.
    /// Uses backend.ExecuteToStateAsync for non-blocking I/O.
    let private measureKernelCircuitAsync
        (backend: IQuantumBackend)
        (circuit: Circuit)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float>> =
        task {
            let wrappedCircuit = CircuitWrapper(circuit)
            let! stateResult = backend.ExecuteToStateAsync wrappedCircuit cancellationToken
            return
                match stateResult with
                | Error e -> Error (QuantumError.ValidationError ("Input", $"Quantum backend execution failed: {e}"))
                | Ok state ->
                    let measurements = QuantumState.measure state shots
                    let allZeroCount =
                        measurements
                        |> Array.filter (fun measurement ->
                            measurement |> Array.forall ((=) 0))
                        |> Array.length
                    let probability = float allZeroCount / float shots
                    Ok probability
        }
    
    /// Compute quantum kernel value K(x, y) = |⟨φ(x)|φ(y)⟩|²
    ///
    /// Parameters:
    ///   backend - Quantum backend for circuit execution
    ///   featureMap - Quantum feature map (e.g., AngleEncoding)
    ///   x - First feature vector
    ///   y - Second feature vector
    ///   shots - Number of measurement shots
    ///
    /// Returns:
    ///   Kernel value in [0, 1] or error message
    [<Obsolete("Use computeKernelAsync for non-blocking I/O against cloud backends.")>]
    let computeKernel
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (x: float array)
        (y: float array)
        (shots: int)
        : QuantumResult<float> =
        
        if shots <= 0 then
            Error (QuantumError.ValidationError ("Input", "Number of shots must be positive"))
        elif x.Length = 0 then
            Error (QuantumError.Other "Feature vectors cannot be empty")
        else
            result {
                let! circuit = buildKernelCircuit featureMap x y
                return! measureKernelCircuit backend circuit shots
            }

    /// Compute quantum kernel value K(x, y) = |⟨φ(x)|φ(y)⟩|² asynchronously.
    /// Uses backend.ExecuteToStateAsync for non-blocking I/O.
    let computeKernelAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (x: float array)
        (y: float array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float>> =
        task {
            if shots <= 0 then
                return Error (QuantumError.ValidationError ("Input", "Number of shots must be positive"))
            elif x.Length = 0 then
                return Error (QuantumError.Other "Feature vectors cannot be empty")
            else
                match buildKernelCircuit featureMap x y with
                | Error e -> return Error e
                | Ok circuit -> return! measureKernelCircuitAsync backend circuit shots cancellationToken
        }
    
    // ========================================================================
    // KERNEL MATRIX COMPUTATION
    // ========================================================================
    
    /// Compute full kernel matrix for a dataset
    ///
    /// For dataset X = [x₁, x₂, ..., xₙ], computes matrix K where
    /// K[i,j] = K(xᵢ, xⱼ) = |⟨φ(xᵢ)|φ(xⱼ)⟩|²
    ///
    /// The matrix is symmetric: K[i,j] = K[j,i]
    ///
    /// Parameters:
    ///   backend - Quantum backend for circuit execution
    ///   featureMap - Quantum feature map
    ///   data - Array of feature vectors
    ///   shots - Number of measurement shots per kernel evaluation
    ///
    /// Returns:
    ///   Kernel matrix (n × n) or error message
    [<Obsolete("Use computeKernelMatrixAsync for genuine task-based parallelism with cloud backends.")>]
    let computeKernelMatrix
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (data: float array array)
        (shots: int)
        : QuantumResult<float[,]> =
        
        if data.Length = 0 then
            Error (QuantumError.Other "Dataset cannot be empty")
        else
            let n = data.Length
            
            // Compute all unique kernel entries in parallel
            // For symmetric matrix, only compute upper triangle (i,j) where j >= i
            let uniquePairs = 
                [| for i in 0 .. n - 1 do
                    for j in i .. n - 1 do
                        yield (i, j) |]
            
            let kernelEntries =
                uniquePairs
                |> Array.map (fun (i, j) -> 
                    async { 
                        return (i, j, computeKernel backend featureMap data.[i] data.[j] shots)
                    })
                |> Async.Parallel
                |> Async.RunSynchronously
            
            // Check for errors first
            match kernelEntries |> Array.tryFind (fun (_, _, r) -> Result.isError r) with
            | Some (i, j, Error e) ->
                Error (QuantumError.ValidationError ("Input", $"Kernel computation failed at ({i},{j}): {e}"))
            | _ ->
                // Build symmetric matrix from successful results
                let kernelMatrix = Array2D.zeroCreate n n
                for (i, j, result) in kernelEntries do
                    match result with
                    | Ok kernelValue ->
                        kernelMatrix.[i, j] <- kernelValue
                        if i <> j then
                            kernelMatrix.[j, i] <- kernelValue
                    | Error _ -> () // Already handled above
                Ok kernelMatrix
    
    /// Compute full kernel matrix for a dataset using Task.WhenAll.
    /// All upper-triangle kernel entries are computed concurrently via
    /// backend.ExecuteToStateAsync — a massive win for cloud backends.
    let computeKernelMatrixAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (data: float array array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float[,]>> =
        task {
            if data.Length = 0 then
                return Error (QuantumError.Other "Dataset cannot be empty")
            else
                let n = data.Length

                let uniquePairs =
                    [| for i in 0 .. n - 1 do
                        for j in i .. n - 1 do
                            yield (i, j) |]

                let! kernelEntries =
                    uniquePairs
                    |> Array.map (fun (i, j) ->
                        task {
                            let! result = computeKernelAsync backend featureMap data.[i] data.[j] shots cancellationToken
                            return (i, j, result)
                        })
                    |> Task.WhenAll

                return
                    match kernelEntries |> Array.tryFind (fun (_, _, r) -> Result.isError r) with
                    | Some (i, j, Error e) ->
                        Error (QuantumError.ValidationError ("Input", $"Kernel computation failed at ({i},{j}): {e}"))
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

    /// Compute kernel matrix between train and test sets
    ///
    /// For train set X_train = [x₁, ..., xₘ] and test set X_test = [y₁, ..., yₙ],
    /// computes matrix K where K[i,j] = K(yᵢ, xⱼ)
    ///
    /// This is used for prediction: test samples (rows) vs train samples (columns)
    ///
    /// Parameters:
    ///   backend - Quantum backend
    ///   featureMap - Quantum feature map
    ///   trainData - Training feature vectors (m samples)
    ///   testData - Test feature vectors (n samples)
    ///   shots - Number of shots per kernel evaluation
    ///
    /// Returns:
    ///   Kernel matrix (n × m) where n = test samples, m = train samples
    [<Obsolete("Use computeKernelMatrixTrainTestAsync for genuine task-based parallelism with cloud backends.")>]
    let computeKernelMatrixTrainTest
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (trainData: float array array)
        (testData: float array array)
        (shots: int)
        : QuantumResult<float[,]> =
        
        if trainData.Length = 0 then
            Error (QuantumError.Other "Training dataset cannot be empty")
        elif testData.Length = 0 then
            Error (QuantumError.Other "Test dataset cannot be empty")
        else
            let nTest = testData.Length
            let nTrain = trainData.Length
            
            // 🚀 PARALLELIZED: Compute all kernel entries in parallel
            // Each test-train pair is independent
            let allPairs = 
                [| for i in 0 .. nTest - 1 do
                    for j in 0 .. nTrain - 1 do
                        yield (i, j) |]
            
            let kernelEntries =
                allPairs
                |> Array.map (fun (i, j) ->
                    async {
                        return (i, j, computeKernel backend featureMap testData.[i] trainData.[j] shots)
                    })
                |> Async.Parallel
                |> Async.RunSynchronously
            
            // Check for errors first
            match kernelEntries |> Array.tryFind (fun (i, j, result) -> Result.isError result) with
            | Some (i, j, Error e) -> 
                Error (QuantumError.ValidationError ("Input", $"Kernel computation failed at test[{i}], train[{j}]: {e}"))
            | _ ->
                // Build matrix from successful results
                let kernelMatrix = Array2D.zeroCreate nTest nTrain
                for (i, j, result) in kernelEntries do
                    match result with
                    | Ok kernelValue -> kernelMatrix.[i, j] <- kernelValue
                    | Error _ -> () // Already handled above
                Ok kernelMatrix
    
    /// Compute kernel matrix between train and test sets using Task.WhenAll.
    /// All test-train kernel pairs are computed concurrently.
    let computeKernelMatrixTrainTestAsync
        (backend: IQuantumBackend)
        (featureMap: FeatureMapType)
        (trainData: float array array)
        (testData: float array array)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float[,]>> =
        task {
            if trainData.Length = 0 then
                return Error (QuantumError.Other "Training dataset cannot be empty")
            elif testData.Length = 0 then
                return Error (QuantumError.Other "Test dataset cannot be empty")
            else
                let nTest = testData.Length
                let nTrain = trainData.Length

                let allPairs =
                    [| for i in 0 .. nTest - 1 do
                        for j in 0 .. nTrain - 1 do
                            yield (i, j) |]

                let! kernelEntries =
                    allPairs
                    |> Array.map (fun (i, j) ->
                        task {
                            let! result = computeKernelAsync backend featureMap testData.[i] trainData.[j] shots cancellationToken
                            return (i, j, result)
                        })
                    |> Task.WhenAll

                return
                    match kernelEntries |> Array.tryFind (fun (_, _, result) -> Result.isError result) with
                    | Some (i, j, Error e) ->
                        Error (QuantumError.ValidationError ("Input", $"Kernel computation failed at test[{i}], train[{j}]: {e}"))
                    | _ ->
                        let kernelMatrix = Array2D.zeroCreate nTest nTrain
                        for (i, j, result) in kernelEntries do
                            match result with
                            | Ok kernelValue -> kernelMatrix.[i, j] <- kernelValue
                            | Error _ -> ()
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
            |> Seq.forall (fun (i, j) ->
                abs (matrix.[i, j] - matrix.[j, i]) <= tolerance)
    
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
            Error (QuantumError.ValidationError ("Input", "Kernel matrix must be square for normalization"))
        else
            // Check diagonal elements are positive
            let diagonalPositive = 
                [| 0 .. n - 1 |]
                |> Array.forall (fun i -> matrix.[i, i] > 0.0)
            
            if not diagonalPositive then
                Error (QuantumError.ValidationError ("Input", "Cannot normalize: diagonal elements must be positive"))
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
    type KernelMatrixStats = {
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
            [| for i in 0 .. n - 1 do
                for j in 0 .. m - 1 -> matrix.[i, j] |]
        
        let mean = Array.average values
        let variance = values |> Array.map (fun x -> (x - mean) ** 2.0) |> Array.average
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
