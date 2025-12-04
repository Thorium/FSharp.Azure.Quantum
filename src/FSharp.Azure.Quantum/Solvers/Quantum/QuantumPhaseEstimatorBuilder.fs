namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Algorithms.QuantumPhaseEstimation
open FSharp.Azure.Quantum.Algorithms.QPEBackendAdapter
open FSharp.Azure.Quantum.Algorithms.TrotterSuzuki
open FSharp.Azure.Quantum.LocalSimulator

/// High-level Quantum Phase Estimator Builder - QPE for Eigenvalue Extraction
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for quantum algorithm researchers and educators
/// who want to estimate eigenvalues of unitary operators without understanding
/// QPE circuit internals (controlled-U gates, inverse QFT, measurement).
/// 
/// QUANTUM PHASE ESTIMATION:
/// - Estimates phase φ where U|ψ⟩ = e^(2πiφ)|ψ⟩
/// - Uses n counting qubits → φ estimated to n bits of precision
/// - Core subroutine in Shor's algorithm, quantum chemistry, HHL algorithm
/// - Exponentially faster than classical eigenvalue methods
/// 
/// WHAT IS PHASE ESTIMATION:
/// Given unitary U and eigenstate |ψ⟩, find eigenvalue λ = e^(2πiφ).
/// QPE extracts the phase φ ∈ [0, 1) to n-bit precision using:
///   1. Hadamard gates on counting register
///   2. Controlled-U^(2^k) operations
///   3. Inverse QFT
///   4. Measurement of counting register
/// 
/// USE CASES:
/// - Quantum chemistry: ground state energy estimation
/// - Algorithm research: eigenvalue problems, linear systems
/// - Cryptography: discrete logarithm, hidden subgroup
/// - Educational: teaching QPE, demonstrating quantum advantage
/// - Shor's algorithm: period finding via modular exponentiation phase
/// 
/// EXAMPLE USAGE:
///   // Simple: Estimate phase of T gate
///   let problem = phaseEstimator {
///       unitary TGate
///       precision 8
///   }
///   
///   // Advanced: Custom unitary with eigenvector
///   let problem = phaseEstimator {
///       unitary (PhaseGate (Math.PI / 4.0))
///       precision 12
///       targetQubits 2
///       eigenstate customEigenVector
///   }
///   
///   // Solve the problem
///   match estimate problem with
///   | Ok result -> 
///       printfn "Phase: %.6f" result.Phase
///       printfn "Eigenvalue: e^(2πi × %.6f)" result.Phase
///   | Error msg -> printfn "Error: %s" msg

module QuantumPhaseEstimator =
    
    // ============================================================================
    // TYPES - Builder API types (wrapper around QPEConfig/QPEResult)
    // ============================================================================
    
    /// <summary>
    /// Complete quantum phase estimation problem specification.
    /// Used to configure QPE for eigenvalue extraction.
    /// </summary>
    type PhaseEstimatorProblem = {
        /// Unitary operator U to estimate phase of
        Unitary: UnitaryOperator
        
        /// Number of counting qubits (n bits precision for φ)
        /// Higher precision = more accurate phase estimate
        Precision: int
        
        /// Number of target qubits (for eigenvector |ψ⟩)
        /// Default: 1 (single-qubit gates like T, S, Phase)
        TargetQubits: int
        
        /// Initial eigenvector |ψ⟩ (must be eigenstate of U)
        /// None = use |0⟩ (works for diagonal gates)
        EigenVector: StateVector.StateVector option
        
        /// Quantum backend to use (None = LocalBackend)
        Backend: BackendAbstraction.IQuantumBackend option
        
        /// Number of measurement shots for phase estimation (None = auto-scale: 1024 for Local, 2048 for Cloud)
        /// Higher shots = better statistical accuracy of phase estimate
        Shots: int option
    }
    
    /// <summary>
    /// Result of quantum phase estimation.
    /// Contains estimated phase, eigenvalue, and execution metadata.
    /// </summary>
    type PhaseEstimatorResult = {
        /// Estimated phase φ ∈ [0, 1)
        Phase: float
        
        /// Eigenvalue λ = e^(2πiφ) as complex number
        Eigenvalue: System.Numerics.Complex
        
        /// Measurement outcome (binary representation of φ)
        MeasurementOutcome: int
        
        /// Precision used (n counting qubits)
        Precision: int
        
        /// Target qubits used
        TargetQubits: int
        
        /// Total qubits used (precision + target)
        TotalQubits: int
        
        /// Gate count (controlled-U + inverse QFT)
        GateCount: int
        
        /// Unitary operator used
        Unitary: string
        
        /// Success indicator
        Success: bool
        
        /// Execution details or error message
        Message: string
    }
    
    // ============================================================================
    // VALIDATION HELPERS
    // ============================================================================
    
    /// <summary>
    /// Validates a phase estimation problem specification.
    /// Checks precision bounds, target qubits, and configuration validity.
    /// </summary>
    let private validate (problem: PhaseEstimatorProblem) : Result<unit, string> =
        // Check precision
        if problem.Precision < 1 then
            Error "Precision must be at least 1 qubit"
        elif problem.Precision > 20 then
            Error "Precision exceeds practical limit (20 qubits) for NISQ devices"
        
        // Check target qubits
        elif problem.TargetQubits < 1 then
            Error "Target qubits must be at least 1"
        elif problem.TargetQubits > 10 then
            Error "Target qubits exceeds simulation limit (10 qubits)"
        
        // Check total qubits
        elif problem.Precision + problem.TargetQubits > 25 then
            Error "Total qubits (precision + target) exceeds limit (25 qubits)"
        
        else
            Ok ()
    
    // ============================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ============================================================================
    
    /// <summary>
    /// Computation expression builder for quantum phase estimation problems.
    /// Provides F#-idiomatic DSL for constructing QPE configurations.
    /// </summary>
    type PhaseEstimatorBuilder() =
        
        /// Default problem (user must specify unitary and precision)
        let defaultProblem = {
            Unitary = TGate              // Default to T gate (classic example)
            Precision = 8                // 8 bits precision (1/256 resolution)
            TargetQubits = 1             // Single target qubit
            EigenVector = None           // Use |0⟩ eigenstate
            Backend = None               // Use LocalBackend by default
            Shots = None                 // Auto-scale based on backend
        }
        
        /// Initialize builder with default problem
        member _.Yield(_) = defaultProblem
        
        /// <summary>Set unitary operator.</summary>
        /// <param name="u">Unitary operator</param>
        [<CustomOperation("unitary")>]
        member _.Unitary(problem: PhaseEstimatorProblem, u: UnitaryOperator) : PhaseEstimatorProblem =
            { problem with Unitary = u }
        
        /// <summary>Set precision (counting qubits).</summary>
        /// <param name="n">Number of precision qubits</param>
        [<CustomOperation("precision")>]
        member _.Precision(problem: PhaseEstimatorProblem, n: int) : PhaseEstimatorProblem =
            { problem with Precision = n }
        
        /// <summary>Set target qubits.</summary>
        /// <param name="n">Number of target qubits</param>
        [<CustomOperation("targetQubits")>]
        member _.TargetQubits(problem: PhaseEstimatorProblem, n: int) : PhaseEstimatorProblem =
            { problem with TargetQubits = n }
        
        /// <summary>Set eigenvector.</summary>
        /// <param name="vec">Eigenstate vector</param>
        [<CustomOperation("eigenstate")>]
        member _.Eigenstate(problem: PhaseEstimatorProblem, vec: StateVector.StateVector) : PhaseEstimatorProblem =
            { problem with EigenVector = Some vec }
        
        /// <summary>Set quantum backend.</summary>
        /// <param name="b">Quantum backend instance</param>
        [<CustomOperation("backend")>]
        member _.Backend(problem: PhaseEstimatorProblem, b: BackendAbstraction.IQuantumBackend) : PhaseEstimatorProblem =
            { problem with Backend = Some b }
        
        /// <summary>
        /// Set the number of measurement shots for phase estimation.
        /// Higher shot counts improve statistical accuracy of the phase estimate.
        /// </summary>
        /// <param name="shots">Number of measurements (typical: 1024-4096)</param>
        /// <remarks>
        /// If not specified, auto-scales based on backend:
        /// - LocalBackend: 1024 shots
        /// - Cloud backends: 2048 shots
        /// Multiple measurements reduce statistical error in phase estimation.
        /// </remarks>
        [<CustomOperation("shots")>]
        member _.Shots(problem: PhaseEstimatorProblem, shots: int) : PhaseEstimatorProblem =
            { problem with Shots = Some shots }
        
        /// Finalize and validate the problem
        member _.Run(problem: PhaseEstimatorProblem) : Result<PhaseEstimatorProblem, string> =
            validate problem |> Result.map (fun _ -> problem)
    
    /// Global computation expression instance for phase estimation
    let phaseEstimator = PhaseEstimatorBuilder()
    
    // ============================================================================
    // SOLVER FUNCTION
    // ============================================================================
    
    /// Solve quantum phase estimation problem
    /// 
    /// Executes:
    ///   1. Validate problem configuration
    ///   2. Create QPEConfig from problem
    ///   3. Execute QPE via backend (QPEBackendAdapter.executeWithBackend)
    ///   4. Map result to PhaseEstimatorResult with eigenvalue
    /// 
    /// Example:
    ///   let problem = phaseEstimator {
    ///       unitary TGate
    ///       precision 8
    ///       backend myBackend
    ///   }
    ///   match estimate problem with
    ///   | Ok result -> printfn "Phase: %.6f, Eigenvalue: %A" result.Phase result.Eigenvalue
    ///   | Error msg -> printfn "Error: %s" msg
    let estimate (problem: PhaseEstimatorProblem) : Result<PhaseEstimatorResult, string> =
        try
            // Validate problem first
            match validate problem with
            | Error msg -> Error msg
            | Ok () ->
                
                // Convert to QPEConfig
                let config = {
                    CountingQubits = problem.Precision
                    TargetQubits = problem.TargetQubits
                    UnitaryOperator = problem.Unitary
                    EigenVector = problem.EigenVector
                }
                
                // Get backend (default to LocalBackend)
                let backend = 
                    problem.Backend 
                    |> Option.defaultValue (BackendAbstraction.LocalBackend() :> BackendAbstraction.IQuantumBackend)
                
                // Execute QPE on backend
                let shots = 1000  // Default shots for measurement statistics
                
                match executeWithBackend config backend shots with
                | Error msg -> Error msg
                | Ok histogram ->
                    
                    // Extract phase from histogram
                    let phase = extractPhaseFromHistogram histogram problem.Precision
                    
                    // Get most likely measurement outcome
                    let measurementOutcome = 
                        histogram
                        |> Map.toSeq
                        |> Seq.maxBy snd
                        |> fst
                    
                    // Calculate eigenvalue λ = e^(2πiφ)
                    let angle = 2.0 * System.Math.PI * phase
                    let eigenvalue = System.Numerics.Complex.FromPolarCoordinates(1.0, angle)
                    
                    // Describe unitary operator
                    let unitaryName =
                        match problem.Unitary with
                        | TGate -> "T Gate (π/8 gate)"
                        | SGate -> "S Gate (phase gate)"
                        | PhaseGate theta -> sprintf "Phase Gate (θ=%.4f)" theta
                        | RotationZ theta -> sprintf "Rz Gate (θ=%.4f)" theta
                        | CustomUnitary _ -> "Custom Unitary"
                        | HamiltonianEvolution (hamiltonianObj, t, steps) -> 
                            // hamiltonian is stored as obj; cast to get term count
                            let h = hamiltonianObj :?> PauliHamiltonian
                            sprintf "Hamiltonian Evolution (t=%.4f, %d Trotter steps, %d Pauli terms)" 
                                t steps h.Terms.Length
                    
                    // Estimate gate count (histogram doesn't provide this)
                    let estimatedGateCount = 
                        problem.Precision + 
                        (problem.Precision * problem.Precision) +  // Controlled-U gates
                        (problem.Precision * (problem.Precision + 1) / 2)  // IQFT gates
                    
                    // Build result
                    let result = {
                        Phase = phase
                        Eigenvalue = eigenvalue
                        MeasurementOutcome = measurementOutcome
                        Precision = problem.Precision
                        TargetQubits = problem.TargetQubits
                        TotalQubits = problem.Precision + problem.TargetQubits
                        GateCount = estimatedGateCount
                        Unitary = unitaryName
                        Success = true
                        Message = sprintf "Phase estimation successful: φ ≈ %.6f (eigenvalue λ = e^(2πi×%.6f))" phase phase
                    }
                    
                    Ok result
        
        with
        | ex -> Error $"Phase estimation failed: {ex.Message}"
    
    // ============================================================================
    // CONVENIENCE HELPERS
    // ============================================================================
    
    /// Quick helper for estimating phase of common gates
    /// 
    /// Example: estimateTGate 10 (Some backend)
    let estimateTGate (p: int) (backend: BackendAbstraction.IQuantumBackend option) : Result<PhaseEstimatorProblem, string> =
        phaseEstimator {
            unitary TGate
            precision p
        }
        |> Result.map (fun problem -> 
            match backend with
            | Some b -> { problem with Backend = Some b }
            | None -> problem
        )
    
    /// Quick helper for estimating phase of S gate
    /// 
    /// Example: estimateSGate 10 (Some backend)
    let estimateSGate (p: int) (backend: BackendAbstraction.IQuantumBackend option) : Result<PhaseEstimatorProblem, string> =
        phaseEstimator {
            unitary SGate
            precision p
        }
        |> Result.map (fun problem -> 
            match backend with
            | Some b -> { problem with Backend = Some b }
            | None -> problem
        )
    
    /// Quick helper for estimating phase of custom phase gate
    /// 
    /// Example: estimatePhaseGate (Math.PI / 4.0) 12 (Some backend)
    let estimatePhaseGate (theta: float) (p: int) (backend: BackendAbstraction.IQuantumBackend option) : Result<PhaseEstimatorProblem, string> =
        phaseEstimator {
            unitary (PhaseGate theta)
            precision p
        }
        |> Result.map (fun problem -> 
            match backend with
            | Some b -> { problem with Backend = Some b }
            | None -> problem
        )
    
    /// Quick helper for estimating phase of rotation gate
    /// 
    /// Example: estimateRotationZ (Math.PI / 3.0) 12 (Some backend)
    let estimateRotationZ (theta: float) (p: int) (backend: BackendAbstraction.IQuantumBackend option) : Result<PhaseEstimatorProblem, string> =
        phaseEstimator {
            unitary (RotationZ theta)
            precision p
        }
        |> Result.map (fun problem -> 
            match backend with
            | Some b -> { problem with Backend = Some b }
            | None -> problem
        )
    
    /// Estimate resource requirements without executing
    /// Returns human-readable string with qubit and gate estimates
    let estimateResources (p: int) (targetQubits: int) : string =
        let totalQubits = p + targetQubits
        
        // Gate count estimates
        let hadamardGates = p  // Hadamard on counting register
        let controlledUGates = p * (pown 2 p - 1) / 2  // Approximate controlled-U^(2^k) count
        let inverseQFTGates = p * (p + 1) / 2  // IQFT gate count
        let totalGates = hadamardGates + controlledUGates + inverseQFTGates
        
        // Circuit depth (critical path)
        let depth = p + (p * (p - 1) / 2)  // Controlled-U + IQFT depth
        
        sprintf """Quantum Phase Estimation Resource Estimate:
  Precision (Counting Qubits): %d
  Target Qubits: %d
  Total Qubits: %d
  Hadamard Gates: %d
  Controlled-U Gates: ~%d
  Inverse QFT Gates: ~%d
  Total Gates: ~%d
  Circuit Depth: ~%d
  Phase Resolution: 1/%d (%.6f)
  Feasibility: %s
  Classical Equivalent: O(2^n) exponential complexity
  Quantum Advantage: Polynomial O(n²) gate complexity"""
            p
            targetQubits
            totalQubits
            hadamardGates
            controlledUGates
            inverseQFTGates
            totalGates
            depth
            (pown 2 p)
            (1.0 / float (pown 2 p))
            (if totalQubits <= 20 then "✓ Feasible on NISQ devices" else "✗ Requires fault-tolerant quantum computer")
    
    /// Export result to human-readable string
    let describeResult (result: PhaseEstimatorResult) : string =
        let eigenvalueStr = 
            sprintf "λ = %.6f + %.6fi (magnitude: %.6f, angle: %.6f rad)" 
                result.Eigenvalue.Real
                result.Eigenvalue.Imaginary
                result.Eigenvalue.Magnitude
                result.Eigenvalue.Phase
        
        let successIcon = if result.Success then "✓" else "✗"
        
        sprintf """=== Quantum Phase Estimation Result ===
 %s Success: Phase estimation complete

Input:
  Unitary: %s
  Precision: %d qubits (resolution: 1/%d ≈ %.6f)
  Target Qubits: %d

Result:
  Estimated Phase: φ ≈ %.8f
  Eigenvalue: %s
  Measurement: %d (binary: %s)
  
Resources Used:
  Total Qubits: %d (%d precision + %d target)
  Gates Applied: %d
  
Interpretation:
  The eigenvalue equation is: U|ψ⟩ = e^(2πi×%.8f)|ψ⟩
  This means U rotates |ψ⟩ by %.4f° in the complex plane
  
%s"""
            successIcon
            result.Unitary
            result.Precision
            (pown 2 result.Precision)
            (1.0 / float (pown 2 result.Precision))
            result.TargetQubits
            result.Phase
            eigenvalueStr
            result.MeasurementOutcome
            (System.Convert.ToString(result.MeasurementOutcome, 2).PadLeft(result.Precision, '0'))
            result.TotalQubits
            result.Precision
            result.TargetQubits
            result.GateCount
            result.Phase
            (result.Phase * 360.0)
            result.Message
