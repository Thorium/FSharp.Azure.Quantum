namespace FSharp.Azure.Quantum.Algorithms

open System
open System.Diagnostics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Quantum Fourier Transform (QFT) - Unified Backend Edition
/// 
/// This module provides a backend-agnostic implementation of the Quantum Fourier Transform
/// using the unified backend interface (IQuantumBackend).
/// 
/// The QFT transforms computational basis states into frequency basis:
/// |j⟩ → (1/√N) Σₖ e^(2πijk/N) |k⟩
/// 
/// Key features:
/// - Works seamlessly with gate-based and topological backends
/// - State-based execution for efficiency
/// - Pure functional design with Result-based error handling
/// - Idiomatic F# with computation expressions
/// - O(n²) gate complexity
/// 
/// Applications:
/// - Shor's factoring algorithm
/// - Quantum phase estimation (QPE)
/// - Period finding
/// - Quantum signal processing
/// 
/// Usage:
///   let backend = LocalBackend.LocalBackend() :> IQuantumBackend
///   let! result = QFT.execute 3 backend defaultConfig
module QFT =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Configuration for Quantum Fourier Transform
    type QFTConfig = {
        /// Whether to apply bit-reversal SWAPs at the end
        /// (Standard QFT includes SWAPs, but some algorithms omit them)
        ApplySwaps: bool
        
        /// Whether to compute inverse QFT (QFT†)
        Inverse: bool
        
        /// Number of measurement shots (for verification)
        Shots: int
    }
    
    /// Result of QFT execution
    type QFTResult = {
        /// Transformed quantum state
        FinalState: QuantumState
        
        /// Number of gates applied
        GateCount: int
        
        /// Configuration used
        Config: QFTConfig
        
        /// Execution time (milliseconds)
        ExecutionTimeMs: float
    }
    
    // ========================================================================
    // DEFAULT CONFIGURATION
    // ========================================================================
    
    /// Default QFT configuration
    let defaultConfig = {
        ApplySwaps = true
        Inverse = false
        Shots = 1000
    }

    // ========================================================================
    // GATE COUNT ESTIMATION
    // ========================================================================

    /// Calculate total gate count for QFT
    /// 
    /// Formula:
    /// - QFT steps: n Hadamards + Σᵢ (n-i-1) controlled phases = n + n(n-1)/2
    /// - Bit reversal: ⌊n/2⌋ SWAPs
    /// 
    /// Total with swaps: n + n(n-1)/2 + ⌊n/2⌋ ≈ n²/2 + 3n/2
    let estimateGateCount (numQubits: int) (applySwaps: bool) : int =
        let qftGates = numQubits + (numQubits * (numQubits - 1) / 2)
        let swapGates = if applySwaps then numQubits / 2 else 0
        qftGates + swapGates
    
    // ========================================================================
    // HELPER: Phase Angle Calculation
    // ========================================================================
    
    /// Calculate phase angle for controlled rotation
    /// 
    /// Formula: θ = 2π / 2^k
    /// where k is the distance between qubits
    let private calculatePhaseAngle (k: int) (inverse: bool) : float =
        let angle = 2.0 * Math.PI / float (1 <<< k)
        if inverse then -angle else angle
    
    // ========================================================================
    // INTENT → PLAN → EXECUTION (ADR: intent-first algorithms)
    // ========================================================================

    /// Execution exactness contract for QFT.
    type Exactness =
        | Exact
        | Approximate of epsilon: float

    /// Canonical, algorithm-level intent for QFT execution.
    type QftExecutionIntent = {
        NumQubits: int
        Config: QFTConfig
        Exactness: Exactness
    }

    [<RequireQualifiedAccess>]
    type QftPlan =
        /// Execute the semantic QFT intent natively, if supported by backend.
        | ExecuteNatively of intent: QftIntent * exactness: Exactness

        /// Execute via explicit lowering to operations, if supported.
        | ExecuteViaOps of ops: QuantumOperation list * exactness: Exactness

    /// Build an intent-first representation for QFT.
    let private toCoreIntent (intent: QftExecutionIntent) : QftIntent =
        {
            NumQubits = intent.NumQubits
            Inverse = intent.Config.Inverse
            ApplySwaps = intent.Config.ApplySwaps
        }

    /// Create a gate-sequence lowering for QFT.
    ///
    /// Note: gate sequences are a *strategy*, not the canonical definition of QFT.
    let private buildLoweringOps
        (numQubits: int)
        (config: QFTConfig)
        (exactness: Exactness)
        : QuantumOperation list =

        let shouldIncludeControlledPhase (angle: float) =
            match exactness with
            | Exact -> true
            | Approximate epsilon -> abs angle >= epsilon

        let swapSequence =
            if config.ApplySwaps then
                [0 .. numQubits / 2 - 1]
                |> List.map (fun i ->
                    let j = numQubits - 1 - i
                    QuantumOperation.Gate (CircuitBuilder.SWAP (i, j)))
            else
                []

        let qftForwardSequence =
            let applyQftStepOps targetQubit =
                let hOp = QuantumOperation.Gate (CircuitBuilder.H targetQubit)
                let phases =
                    [targetQubit + 1 .. numQubits - 1]
                    |> List.choose (fun k ->
                        let power = k - targetQubit + 1
                        let angle = calculatePhaseAngle power false
                        if shouldIncludeControlledPhase angle then
                            Some (QuantumOperation.Gate (CircuitBuilder.CP (k, targetQubit, angle)))
                        else
                            None)
                hOp :: phases

            [0 .. numQubits - 1]
            |> List.collect applyQftStepOps

        let qftInverseSequence =
            [numQubits - 1 .. -1 .. 0]
            |> List.collect (fun targetQubit ->
                let phases =
                    [numQubits - 1 .. -1 .. targetQubit + 1]
                    |> List.choose (fun k ->
                        let power = k - targetQubit + 1
                        let angle = calculatePhaseAngle power true
                        if shouldIncludeControlledPhase angle then
                            Some (QuantumOperation.Gate (CircuitBuilder.CP (k, targetQubit, angle)))
                        else
                            None)
                let hOp = QuantumOperation.Gate (CircuitBuilder.H targetQubit)
                phases @ [hOp])

        if config.Inverse then
            swapSequence @ qftInverseSequence
        else
            qftForwardSequence @ swapSequence

    let plan (backend: IQuantumBackend) (intent: QftExecutionIntent) : Result<QftPlan, QuantumError> =
        // QFT requires a gate-based backend (or explicit native algorithm op support).
        // Some backends (e.g., annealing) cannot support this, and we should fail explicitly.
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error (QuantumError.OperationError ("QFT", $"Backend '{backend.Name}' does not support QFT (native state type: {backend.NativeStateType})"))
        | _ ->
            if intent.NumQubits <= 0 then
                Error (QuantumError.ValidationError ("NumQubits", "must be positive"))
            else
                match intent.Exactness with
                | Approximate epsilon when epsilon <= 0.0 ->
                    Error (QuantumError.ValidationError ("Exactness", "epsilon must be positive"))
                | _ ->
                    let coreIntent = toCoreIntent intent
                    let nativeOp = QuantumOperation.Algorithm (AlgorithmOperation.QFT coreIntent)

                    if backend.SupportsOperation nativeOp then
                        Ok (QftPlan.ExecuteNatively (coreIntent, intent.Exactness))
                    else
                        // Provide a valid lowering plan when the backend doesn't claim native support.
                        // This is not a *silent* fallback: the chosen plan is explicit and inspectable.
                        let lowerOps = buildLoweringOps intent.NumQubits intent.Config intent.Exactness
                        if lowerOps |> List.forall backend.SupportsOperation then
                            Ok (QftPlan.ExecuteViaOps (lowerOps, intent.Exactness))
                        else
                            Error (QuantumError.OperationError ("QFT", $"Backend '{backend.Name}' does not support required operations for QFT"))

    let private executePlan
        (backend: IQuantumBackend)
        (state: QuantumState)
        (plan: QftPlan)
        : Result<QuantumState, QuantumError> =

        match plan with
        | QftPlan.ExecuteNatively (intent, _) ->
            let op = QuantumOperation.Algorithm (AlgorithmOperation.QFT intent)
            backend.ApplyOperation op state
        | QftPlan.ExecuteViaOps (ops, _) ->
            UnifiedBackend.applySequence backend ops state

    let private executePlanned
        (backend: IQuantumBackend)
        (intent: QftExecutionIntent)
        (state: QuantumState)
        : Result<QuantumState * int, QuantumError> =

        result {
            let! qftPlan = plan backend intent
            let! evolved = executePlan backend state qftPlan

            let gateCount =
                match qftPlan with
                | QftPlan.ExecuteNatively _ -> estimateGateCount intent.NumQubits intent.Config.ApplySwaps
                | QftPlan.ExecuteViaOps (ops, _) -> ops.Length

            return (evolved, gateCount)
        }

    // ========================================================================
    // MAIN QFT ALGORITHM
    // ========================================================================
    
    /// Execute Quantum Fourier Transform
    /// 
    /// Algorithm:
    /// 1. For each qubit j from 0 to n-1:
    ///    a. Apply Hadamard to qubit j
    ///    b. Apply controlled phase rotations from qubits k > j
    /// 2. Apply bit-reversal SWAPs (optional)
    /// 
    /// Time Complexity: O(n²) gates
    /// 
    /// Parameters:
    ///   numQubits - Number of qubits to transform
    ///   backend - Quantum backend (gate-based or topological)
    ///   config - QFT configuration
    /// 
    /// Returns:
    ///   Result with transformed state or error
    let execute
        (numQubits: int)
        (backend: IQuantumBackend)
        (config: QFTConfig)
        : Result<QFTResult, QuantumError> =
        
        let stopwatch = Stopwatch.StartNew()
        
        result {
            // Step 1: Initialize state to |0⟩^⊗n
            let! initialState = backend.InitializeState numQubits
            
            // Step 2: Build intent, plan execution strategy, execute plan.
            let executionIntent: QftExecutionIntent =
                {
                    NumQubits = numQubits
                    Config = config
                    Exactness = Exact
                }

            let! (finalState, gateCount) =
                executePlanned backend executionIntent initialState
            
            // Calculate execution time
            let elapsedMs = stopwatch.Elapsed.TotalMilliseconds
            
            // Return result
            return {
                FinalState = finalState
                GateCount = gateCount
                Config = config
                ExecutionTimeMs = elapsedMs
            }
        }
    
    /// Execute QFT on existing quantum state
    /// 
    /// Applies QFT transformation to given state instead of starting from |0⟩
    /// Useful for multi-stage algorithms
    let executeOnState
        (state: QuantumState)
        (backend: IQuantumBackend)
        (config: QFTConfig)
        : Result<QFTResult, QuantumError> =
        
        let stopwatch = Stopwatch.StartNew()
        let numQubits = QuantumState.numQubits state
        
        result {
            let executionIntent: QftExecutionIntent =
                {
                    NumQubits = numQubits
                    Config = config
                    Exactness = Exact
                }

            let! (finalState, gateCount) =
                executePlanned backend executionIntent state
            
            // Calculate execution time
            let elapsedMs = stopwatch.Elapsed.TotalMilliseconds
            
            return {
                FinalState = finalState
                GateCount = gateCount
                Config = config
                ExecutionTimeMs = elapsedMs
            }
        }
    
    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================
    
    /// Execute inverse QFT (QFT†)
    /// 
    /// Inverse QFT is used in many algorithms to convert from frequency basis
    /// back to computational basis
    let executeInverse
        (numQubits: int)
        (backend: IQuantumBackend)
        (shots: int)
        : Result<QFTResult, QuantumError> =
        
        let inverseConfig = { 
            defaultConfig with 
                Inverse = true
                Shots = shots
        }
        execute numQubits backend inverseConfig
    
    /// Execute QFT without bit-reversal SWAPs
    /// 
    /// Some algorithms (like QPE) don't require bit-reversal, saving gates
    let executeNoSwaps
        (numQubits: int)
        (backend: IQuantumBackend)
        (shots: int)
        : Result<QFTResult, QuantumError> =
        
        let noSwapConfig = { 
            defaultConfig with 
                ApplySwaps = false
                Shots = shots
        }
        execute numQubits backend noSwapConfig
    
    // (gate count estimation moved earlier)
    
    // ========================================================================
    // VERIFICATION HELPERS
    // ========================================================================
    
    /// Verify QFT correctness by checking round-trip: QFT → QFT† ≈ I
    /// 
    /// Applies QFT followed by inverse QFT and checks if result ≈ original state
    let verifyRoundTrip
        (numQubits: int)
        (backend: IQuantumBackend)
        : Result<bool, QuantumError> =
        
        result {
            // Start with |0⟩^⊗n
            let! initialState = backend.InitializeState numQubits
            
            // Apply QFT
            let! qftResult = execute numQubits backend defaultConfig
            
            // Apply inverse QFT
            let! inverseResult = 
                executeOnState qftResult.FinalState backend { defaultConfig with Inverse = true }
            
            // Check if we're back to |0⟩^⊗n
            // (Measure multiple times and verify all outcomes are 0)
            let measurements = UnifiedBackend.measureState inverseResult.FinalState 100
            let allZeros = 
                measurements 
                |> Array.forall (fun bits -> Array.forall ((=) 0) bits)
            
            return allZeros
        }
    
    /// <summary>
    /// Verify that QFT preserves state norm (unitarity check)
    /// </summary>
    /// <remarks>
    /// QFT is a unitary transformation, meaning it preserves quantum state properties.
    /// This function verifies unitarity by applying QFT followed by inverse QFT
    /// and checking that the result matches the original state.
    /// 
    /// The test uses measurement statistics: if QFT is unitary, then
    /// QFT(QFT†(|ψ⟩)) = |ψ⟩, so measurements should return to original distribution.
    /// 
    /// This is useful for:
    /// - Testing backend correctness
    /// - Debugging QFT implementations
    /// - Validating numerical stability
    /// </remarks>
    /// <param name="numQubits">Number of qubits to test</param>
    /// <param name="backend">Quantum backend to use</param>
    /// <param name="config">QFT configuration</param>
    /// <returns>
    /// <c>Ok true</c> if unitarity is preserved (round-trip successful), 
    /// <c>Ok false</c> if unitarity is violated,
    /// <c>Error</c> if execution fails
    /// </returns>
    /// <example>
    /// <code>
    /// let backend = LocalBackend.LocalBackend() :> IQuantumBackend
    /// match verifyUnitarity 3 backend defaultConfig with
    /// | Ok true -> printfn "QFT is unitary ✓"
    /// | Ok false -> printfn "QFT unitarity violated!"
    /// | Error err -> printfn "Error: %A" err
    /// </code>
    /// </example>
    let verifyUnitarity
        (numQubits: int)
        (backend: IQuantumBackend)
        (config: QFTConfig)
        : Result<bool, QuantumError> =
        
        result {
            // Start with initialized state |0⟩^⊗n
            let! initialState = backend.InitializeState numQubits
            
            // Verify state is normalized
            if not (QuantumState.isNormalized initialState) then
                return false
            else
                // Apply QFT
                let! qftResult = executeOnState initialState backend config
                
                // Verify QFT output is normalized (unitary preserves norm)
                if not (QuantumState.isNormalized qftResult.FinalState) then
                    return false
                else
                    // Apply inverse QFT
                    let inverseConfig = { config with Inverse = not config.Inverse }
                    let! inverseResult = executeOnState qftResult.FinalState backend inverseConfig
                    
                    // Verify inverse QFT output is normalized
                    if not (QuantumState.isNormalized inverseResult.FinalState) then
                        return false
                    else
                        // Check if we're back to |0⟩^⊗n by measuring
                        let measurements = UnifiedBackend.measureState inverseResult.FinalState 100
                        let allZeros = 
                            measurements 
                            |> Array.forall (fun bits -> Array.forall ((=) 0) bits)
                        
                        return allZeros
        }
    
    // ========================================================================
    // APPLICATIONS - Example use cases
    // ========================================================================
    
    /// <summary>
    /// Apply QFT to computational basis state |j⟩
    /// </summary>
    /// <remarks>
    /// Creates a computational basis state |j⟩ and applies QFT, resulting in:
    /// 
    /// QFT|j⟩ = (1/√N) Σₖ e^(2πijk/N) |k⟩
    /// 
    /// This creates an equal superposition with specific phase relationships
    /// determined by the basis index j.
    /// 
    /// Applications:
    /// - Quantum phase estimation initialization
    /// - Period finding algorithms
    /// - Testing QFT behavior on known states
    /// </remarks>
    /// <param name="numQubits">Number of qubits</param>
    /// <param name="basisIndex">Index of basis state (0 to 2^n - 1)</param>
    /// <param name="backend">Quantum backend to use</param>
    /// <param name="config">QFT configuration</param>
    /// <returns>QFT result containing transformed state</returns>
    /// <example>
    /// <code>
    /// // Transform |5⟩ in 3-qubit space
    /// let backend = LocalBackend.LocalBackend() :> IQuantumBackend
    /// match transformBasisState 3 5 backend defaultConfig with
    /// | Ok result -> printfn "Transformed |5⟩: %s" (formatResult result)
    /// | Error err -> printfn "Error: %A" err
    /// </code>
    /// </example>
    let transformBasisState
        (numQubits: int)
        (basisIndex: int)
        (backend: IQuantumBackend)
        (config: QFTConfig)
        : Result<QFTResult, QuantumError> =
        
        let maxIndex = (1 <<< numQubits) - 1
        if basisIndex < 0 || basisIndex > maxIndex then
            Error (QuantumError.ValidationError ("BasisIndex", $"must be between 0 and {maxIndex} for {numQubits} qubits"))
        else
            result {
                // Initialize to |0⟩^⊗n
                let! initialState = backend.InitializeState numQubits
                
                // Apply X gates to set state to |basisIndex⟩
                // Convert basisIndex to binary and flip corresponding qubits
                let xOps =
                    [0 .. numQubits - 1]
                    |> List.choose (fun qubitIdx ->
                        let bitValue = (basisIndex >>> qubitIdx) &&& 1
                        if bitValue = 1 then
                            Some (QuantumOperation.Gate (CircuitBuilder.X qubitIdx))
                        else
                            None)

                let! basisState = UnifiedBackend.applySequence backend xOps initialState
                
                // Apply QFT to the basis state
                return! executeOnState basisState backend config
            }
    
    /// <summary>
    /// Encode integer into quantum state and apply QFT
    /// </summary>
    /// <remarks>
    /// This is a convenience function equivalent to `transformBasisState`.
    /// It's commonly used as the first step in quantum algorithms like Shor's factoring.
    /// 
    /// The integer value is encoded as a computational basis state |value⟩,
    /// then QFT is applied to create a superposition with phase encoding.
    /// </remarks>
    /// <param name="numQubits">Number of qubits</param>
    /// <param name="value">Integer value to encode (0 to 2^n - 1)</param>
    /// <param name="backend">Quantum backend to use</param>
    /// <param name="config">QFT configuration</param>
    /// <returns>QFT result containing transformed state</returns>
    /// <example>
    /// <code>
    /// // Encode value 7 and transform in 4-qubit space
    /// let backend = LocalBackend.LocalBackend() :> IQuantumBackend
    /// match encodeAndTransform 4 7 backend defaultConfig with
    /// | Ok result -> 
    ///     printfn "Encoded and transformed value 7"
    ///     printfn "%s" (formatResult result)
    /// | Error err -> printfn "Error: %A" err
    /// </code>
    /// </example>
    let encodeAndTransform
        (numQubits: int)
        (value: int)
        (backend: IQuantumBackend)
        (config: QFTConfig)
        : Result<QFTResult, QuantumError> =
        
        transformBasisState numQubits value backend config
    
    // ========================================================================
    // PRETTY PRINTING
    // ========================================================================
    
    /// Format QFT result as human-readable string
    let formatResult (result: QFTResult) : string =
        let qftType = if result.Config.Inverse then "Inverse QFT" else "QFT"
        let swapStr = if result.Config.ApplySwaps then "with SWAPs" else "without SWAPs"
        
        sprintf "%s (%s)\nGates: %d | Time: %.2f ms"
            qftType
            swapStr
            result.GateCount
            result.ExecutionTimeMs
