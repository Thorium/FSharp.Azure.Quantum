namespace FSharp.Azure.Quantum

open System.Numerics
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Algorithms.HHLTypes
open FSharp.Azure.Quantum.Algorithms.HHL

/// High-level Quantum Linear System Solver Builder - HHL Algorithm
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for researchers, engineers, and data scientists
/// who want to solve linear systems Ax = b using quantum computation without
/// understanding HHL circuit internals (QPE, eigenvalue inversion, ancilla qubits).
/// 
/// HHL ALGORITHM (Harrow-Hassidim-Lloyd):
/// - Solves Ax = b for vector x exponentially faster than classical algorithms
/// - Runtime: O(log(N) s² κ² / ε) vs classical O(N s κ)
/// - Requires Hermitian matrix A, outputs quantum state |x⟩
/// - Success probability ∝ 1/κ² (κ = condition number)
/// 
/// WHAT IS HHL:
/// Given Hermitian matrix A and vector |b⟩, find solution |x⟩ where A|x⟩ = |b⟩.
/// HHL uses quantum phase estimation to extract eigenvalues, applies eigenvalue
/// inversion via controlled rotations, then uncomputes to produce |x⟩.
/// 
/// USE CASES:
/// - Machine learning: quantum SVM, least squares regression, principal component analysis
/// - Engineering: solving PDEs/ODEs in finite element analysis, circuit simulation
/// - Finance: portfolio optimization with covariance matrices, risk modeling
/// - Chemistry: molecular dynamics, quantum chemistry simulations
/// - Data science: large-scale optimization, data fitting
/// - Research: quantum algorithm benchmarking, linear algebra speedup
/// 
/// IMPORTANT LIMITATIONS:
/// - Matrix must be Hermitian (A = A†) - non-Hermitian can be embedded
/// - Solution is quantum state |x⟩ - full readout requires tomography
/// - Best for sparse, well-conditioned matrices (small κ = λ_max/λ_min)
/// - Measurement gives probabilities, not exact amplitudes
/// - Practical speedup requires large N (>1000) with sparse structure
/// 
/// EXAMPLE USAGE:
///   // Simple 2×2 system
///   let problem = linearSystemSolver {
///       matrix [[3.0, 1.0]; [1.0, 3.0]]
///       vector [1.0; 0.0]
///       precision 4
///   }
///   
///   // Advanced: Diagonal system with custom settings
///   let problem = linearSystemSolver {
///       diagonalMatrix [2.0; 4.0; 8.0; 16.0]
///       vector [1.0; 1.0; 1.0; 1.0]
///       precision 8
///       eigenvalueQubits 6
///       inversionMethod (ExactRotation 1.0)
///       minEigenvalue 0.001
///       backend ionQBackend
///       shots 2000
///   }
///   
///   // Solve the problem
///   match solve problem with
///   | Ok result -> 
///       printfn "Success probability: %.4f" result.SuccessProbability
///       printfn "Solution found with %d gates" result.GateCount
///   | Error msg -> printfn "Error: %s" msg
/// 
/// QUANTUM SPEEDUP EXAMPLE:
///   Classical: Gaussian elimination = O(N³) operations
///   Quantum HHL: O(log(N) × poly(κ, log(ε))) operations
///   For N=1000, κ=10: Classical ~10⁹ ops, Quantum ~10³ ops (million-fold speedup!)

module QuantumLinearSystemSolver =
    
    // ============================================================================
    // TYPES - Builder API types (wrapper around HHLConfig/HHLResult)
    // ============================================================================
    
    /// <summary>
    /// Complete quantum linear system solver problem specification.
    /// Used to configure HHL algorithm for solving Ax = b.
    /// </summary>
    type LinearSystemProblem = {
        /// Hermitian matrix A (must satisfy A = A†)
        Matrix: HermitianMatrix
        
        /// Input vector |b⟩ (will be normalized)
        InputVector: QuantumVector
        
        /// Number of qubits for eigenvalue estimation precision
        /// Higher precision = more accurate eigenvalue extraction
        /// Recommendation: log₂(condition_number) + 3 qubits
        EigenvalueQubits: int
        
        /// Eigenvalue inversion method
        /// ExactRotation: Most accurate for well-separated eigenvalues
        /// LinearApproximation: Simpler, good for small eigenvalues
        /// PiecewiseLinear: Handles wide eigenvalue ranges
        InversionMethod: EigenvalueInversionMethod
        
        /// Minimum eigenvalue threshold (avoid division by zero)
        /// Eigenvalues below this are treated as zero
        /// Recommendation: 1e-6 for well-conditioned, 1e-3 for ill-conditioned
        MinEigenvalue: float
        
        /// Whether to use post-selection on ancilla qubit
        /// True = higher accuracy, lower success rate (recommended)
        /// False = lower accuracy, higher success rate
        UsePostSelection: bool
        
        /// Quantum backend to use (None = LocalBackend)
        Backend: BackendAbstraction.IQuantumBackend option
        
        /// Number of measurement shots (None = auto: 1024 for Local, 2048 for Cloud)
        Shots: int option
    }
    
    /// <summary>
    /// Result of quantum linear system solver.
    /// Contains solution amplitudes, success probability, and execution metadata.
    /// </summary>
    type LinearSystemSolution = {
        /// Success probability (probability of ancilla = |1⟩)
        /// Higher for well-conditioned matrices
        /// Range: 0.0 to 1.0
        SuccessProbability: float
        
        /// Estimated eigenvalues of matrix A
        /// Useful for condition number analysis
        EstimatedEigenvalues: float[]
        
        /// Condition number estimate (λ_max / λ_min)
        /// Lower is better (κ < 100 recommended)
        ConditionNumber: float option
        
        /// Number of gates applied in circuit
        GateCount: int
        
        /// Whether post-selection was successful
        PostSelectionSuccess: bool
        
        /// Solution amplitude distribution (basis state → amplitude)
        /// Only available for local simulation
        /// For backend execution, use measurement statistics
        SolutionAmplitudes: Map<int, Complex> option
        
        /// Backend used for execution
        BackendName: string
        
        /// Whether quantum hardware was used
        IsQuantum: bool
        
        /// Success indicator
        Success: bool
        
        /// Execution details or error message
        Message: string
    }
    
    // ============================================================================
    // VALIDATION HELPERS
    // ============================================================================
    
    /// <summary>
    /// Validates a linear system problem specification.
    /// Checks matrix properties, vector compatibility, and qubit limits.
    /// </summary>
    let private validate (problem: LinearSystemProblem) : Result<unit, QuantumError> =
        // Check eigenvalue qubits
        if problem.EigenvalueQubits < 2 then
            Error (QuantumError.ValidationError ("EigenvalueQubits", "must be at least 2 (4 eigenvalue bins minimum)"))
        elif problem.EigenvalueQubits > 12 then
            Error (QuantumError.ValidationError ("EigenvalueQubits", "exceeds practical limit (12 qubits = 4096 bins)"))
        
        // Check matrix dimension
        elif problem.Matrix.Dimension < 2 then
            Error (QuantumError.ValidationError ("MatrixDimension", "must be at least 2×2"))
        elif problem.Matrix.Dimension > 16 then
            Error (QuantumError.ValidationError ("MatrixDimension", "exceeds simulation limit (16×16)"))
        
        // Check matrix is power of 2
        elif (problem.Matrix.Dimension &&& (problem.Matrix.Dimension - 1)) <> 0 then
            Error (QuantumError.ValidationError ("MatrixDimension", $"must be power of 2, got {problem.Matrix.Dimension}"))
        
        // Check vector matches matrix
        elif problem.InputVector.Dimension <> problem.Matrix.Dimension then
            Error (QuantumError.ValidationError ("VectorDimension", $"({problem.InputVector.Dimension}) must match matrix ({problem.Matrix.Dimension})"))
        
        // Calculate solution qubits
        else
            let solutionQubits = 
                let rec log2 n = if n <= 1 then 0 else 1 + log2 (n / 2)
                log2 problem.Matrix.Dimension
            
            let totalQubits = problem.EigenvalueQubits + solutionQubits + 1  // +1 for ancilla
            
            if totalQubits > 20 then
                Error (QuantumError.ValidationError ("TotalQubits", $"({totalQubits}) exceeds practical limit (20 qubits)"))
            else
                Ok ()
    
    // ============================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ============================================================================
    
    /// <summary>
    /// Computation expression builder for linear system problems.
    /// Provides F#-idiomatic DSL for constructing HHL configurations.
    /// </summary>
    type LinearSystemSolverBuilder() =
        
        /// Default problem (user must specify matrix and vector)
        let defaultProblem = {
            Matrix = { Dimension = 2; Elements = [||]; IsDiagonal = false; ConditionNumber = None }
            InputVector = { Dimension = 2; Components = [||] }
            EigenvalueQubits = 4
            InversionMethod = ExactRotation 1.0
            MinEigenvalue = 1e-6
            UsePostSelection = true
            Backend = None
            Shots = None
        }
        
        /// Initialize builder with default problem
        member _.Yield(_) = defaultProblem
        
        /// <summary>Set matrix from 2D array.</summary>
        /// <param name="elements">2D list of matrix elements</param>
        [<CustomOperation("matrix")>]
        member _.Matrix(problem: LinearSystemProblem, elements: float list list) : LinearSystemProblem =
            let n = elements.Length
            if n = 0 || elements |> List.exists (fun row -> row.Length <> n) then
                failwith "Matrix must be square and non-empty"
            
            let array2D = Array2D.init n n (fun i j -> Complex(elements[i][j], 0.0))
            
            match createHermitianMatrix array2D with
            | Ok matrix -> { problem with Matrix = matrix }
            | Error err -> failwith err.Message
        
        /// <summary>Set diagonal matrix from eigenvalues.</summary>
        /// <param name="eigenvalues">List of eigenvalues for diagonal matrix</param>
        [<CustomOperation("diagonalMatrix")>]
        member _.DiagonalMatrix(problem: LinearSystemProblem, eigenvalues: float list) : LinearSystemProblem =
            match createDiagonalMatrix (List.toArray eigenvalues) with
            | Ok matrix -> { problem with Matrix = matrix }
            | Error err -> failwith err.Message
        
        /// <summary>Set input vector.</summary>
        /// <param name="components">List of vector components</param>
        [<CustomOperation("vector")>]
        member _.Vector(problem: LinearSystemProblem, components: float list) : LinearSystemProblem =
            let complexComponents = components |> List.map (fun x -> Complex(x, 0.0)) |> List.toArray
            
            match createQuantumVector complexComponents with
            | Ok vector -> { problem with InputVector = vector }
            | Error err -> failwith err.Message
        
        /// <summary>Set eigenvalue qubits (precision).</summary>
        /// <param name="n">Number of eigenvalue qubits</param>
        [<CustomOperation("eigenvalueQubits")>]
        member _.EigenvalueQubits(problem: LinearSystemProblem, n: int) : LinearSystemProblem =
            { problem with EigenvalueQubits = n }
        
        /// <summary>Alias for eigenvalueQubits.</summary>
        /// <param name="n">Number of eigenvalue qubits</param>
        [<CustomOperation("precision")>]
        member this.Precision(problem: LinearSystemProblem, n: int) : LinearSystemProblem =
            this.EigenvalueQubits(problem, n)
        
        /// <summary>Set eigenvalue inversion method.</summary>
        /// <param name="method">Eigenvalue inversion method</param>
        [<CustomOperation("inversionMethod")>]
        member _.InversionMethod(problem: LinearSystemProblem, method: EigenvalueInversionMethod) : LinearSystemProblem =
            { problem with InversionMethod = method }
        
        /// <summary>Set minimum eigenvalue threshold.</summary>
        /// <param name="threshold">Minimum eigenvalue threshold</param>
        [<CustomOperation("minEigenvalue")>]
        member _.MinEigenvalue(problem: LinearSystemProblem, threshold: float) : LinearSystemProblem =
            { problem with MinEigenvalue = threshold }
        
        /// <summary>Enable/disable post-selection.</summary>
        /// <param name="enable">True to enable post-selection</param>
        [<CustomOperation("postSelection")>]
        member _.PostSelection(problem: LinearSystemProblem, enable: bool) : LinearSystemProblem =
            { problem with UsePostSelection = enable }
        
        /// <summary>Set quantum backend.</summary>
        /// <param name="backend">Quantum backend instance</param>
        [<CustomOperation("backend")>]
        member _.Backend(problem: LinearSystemProblem, backend: BackendAbstraction.IQuantumBackend) : LinearSystemProblem =
            { problem with Backend = Some backend }
        
        /// <summary>Set number of measurement shots.</summary>
        /// <param name="n">Number of shots</param>
        [<CustomOperation("shots")>]
        member _.Shots(problem: LinearSystemProblem, n: int) : LinearSystemProblem =
            { problem with Shots = Some n }
        
        /// <summary>
        /// Finalize and validate the problem.
        /// Checks matrix properties, vector compatibility, and qubit limits.
        /// </summary>
        /// <param name="problem">Linear system problem to validate</param>
        /// <returns>Validated problem or error message</returns>
        /// <remarks>
        /// Validation checks:
        /// - Eigenvalue qubits: 2-12 (practical range)
        /// - Matrix dimension: 2-16 (power of 2)
        /// - Vector dimension matches matrix
        /// - Total qubits ≤ 20 (eigenvalue + solution + ancilla)
        /// 
        /// This method enables early error detection before executing the algorithm.
        /// </remarks>
        member _.Run(problem: LinearSystemProblem) : Result<LinearSystemProblem, QuantumError> =
            validate problem |> Result.map (fun _ -> problem)
    
    /// <summary>
    /// Create computation expression builder instance.
    /// Usage: linearSystemSolver { ... }
    /// </summary>
    let linearSystemSolver = LinearSystemSolverBuilder()
    
    // ============================================================================
    // SOLVER FUNCTIONS
    // ============================================================================
    
    /// <summary>
    /// Solves linear system Ax = b using HHL quantum algorithm.
    /// Automatically selects local simulation or backend execution.
    /// </summary>
    /// <param name="problem">Linear system problem specification</param>
    /// <returns>Solution with success probability and amplitudes</returns>
    let solve (problem: LinearSystemProblem): Result<LinearSystemSolution, QuantumError> =
        result {
            // Validate problem
            do! validate problem
            
            // Determine solution qubits
            let solutionQubits = 
                let rec log2 n = if n <= 1 then 0 else 1 + log2 (n / 2)
                log2 problem.Matrix.Dimension
            
            // Create HHL configuration
            let config = {
                Matrix = problem.Matrix
                InputVector = problem.InputVector
                EigenvalueQubits = problem.EigenvalueQubits
                SolutionQubits = solutionQubits
                InversionMethod = problem.InversionMethod
                MinEigenvalue = problem.MinEigenvalue
                UsePostSelection = problem.UsePostSelection
                QPEPrecision = problem.EigenvalueQubits
            }
            
            // Determine backend
            let backend = 
                match problem.Backend with
                | Some b -> b
                | None -> LocalBackend.LocalBackend() :> IQuantumBackend
            
            let backendName = 
                match problem.Backend with
                | Some b -> b.GetType().Name
                | None -> "LocalSimulator"
            
            // Execute HHL algorithm with backend
            let! hhlResult = execute config backend
            
            // Calculate condition number from estimated eigenvalues
            let conditionNumber =
                if hhlResult.EstimatedEigenvalues.Length > 0 then
                    let nonZero = hhlResult.EstimatedEigenvalues |> Array.filter (fun x -> abs x > 1e-10)
                    if nonZero.Length > 0 then
                        let maxEig = nonZero |> Array.max
                        let minEig = nonZero |> Array.min
                        Some (maxEig / minEig)
                    else
                        None
                else
                    None
            
            let isQuantum = backendName.Contains("IonQ") || backendName.Contains("Rigetti") || backendName.Contains("Quantinuum")
            
            // Map HHL result to LinearSystemSolution
            return {
                SuccessProbability = hhlResult.SuccessProbability
                EstimatedEigenvalues = hhlResult.EstimatedEigenvalues
                ConditionNumber = conditionNumber
                GateCount = hhlResult.GateCount
                PostSelectionSuccess = hhlResult.PostSelectionSuccess
                SolutionAmplitudes = hhlResult.SolutionAmplitudes
                BackendName = backendName
                IsQuantum = isQuantum
                Success = hhlResult.PostSelectionSuccess
                Message = 
                    if hhlResult.PostSelectionSuccess then
                        "HHL algorithm succeeded"
                    else
                        "HHL algorithm completed but post-selection was not successful"
            }
        }
    
    // ============================================================================
    // CONVENIENCE FUNCTIONS
    // ============================================================================
    
    /// <summary>
    /// Solve simple 2×2 linear system.
    /// Quick helper for small systems without builder syntax.
    /// </summary>
    /// <param name="a11">Matrix element (1,1)</param>
    /// <param name="a12">Matrix element (1,2)</param>
    /// <param name="a21">Matrix element (2,1)</param>
    /// <param name="a22">Matrix element (2,2)</param>
    /// <param name="b1">Vector component 1</param>
    /// <param name="b2">Vector component 2</param>
    let solve2x2 (a11: float) (a12: float) (a21: float) (a22: float) (b1: float) (b2: float) 
        : Result<LinearSystemSolution, QuantumError> =
        
        let problemResult = linearSystemSolver {
            matrix [[a11; a12]; [a21; a22]]
            vector [b1; b2]
            precision 4
        }
        
        match problemResult with
        | Error err -> Error err
        | Ok problem -> solve problem
    
    /// <summary>
    /// Solve diagonal system (eigenvalues known).
    /// Optimized for diagonal matrices - faster and more accurate.
    /// </summary>
    /// <param name="eigenvalues">Diagonal elements of matrix</param>
    /// <param name="inputVector">Input vector components</param>
    let solveDiagonal (eigenvalues: float list) (inputVector: float list) 
        : Result<LinearSystemSolution, QuantumError> =
        
        let problemResult = linearSystemSolver {
            diagonalMatrix eigenvalues
            vector inputVector
            precision 6
        }
        
        match problemResult with
        | Error err -> Error err
        | Ok problem -> solve problem
