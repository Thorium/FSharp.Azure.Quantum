namespace FSharp.Azure.Quantum.GroverSearch

open System
open System.Numerics

/// Generalized Amplitude Amplification Module
/// 
/// Amplitude amplification is a generalization of Grover's algorithm that works with
/// arbitrary initial state preparations, not just uniform superposition.
/// 
/// Grover's algorithm is a special case where:
/// - Initial state = uniform superposition H^⊗n|0⟩
/// - Reflection operator = reflection about uniform superposition
/// 
/// General amplitude amplification allows:
/// - Custom initial state preparation A|0⟩
/// - Reflection operator = reflection about A|0⟩
/// 
/// This enables quantum speedups for problems beyond simple search.
module AmplitudeAmplification =
    
    open FSharp.Azure.Quantum
    open FSharp.Azure.Quantum.Core
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.LocalSimulator
    open Oracle
    
    // ========================================================================
    // TYPES - Amplitude amplification configuration
    // ========================================================================
    
    /// State preparation function
    /// Takes initial |0⟩ state and prepares target superposition A|0⟩
    type StatePreparation = StateVector.StateVector -> StateVector.StateVector
    
    /// Reflection operator
    /// Reflects about a specific quantum state
    type ReflectionOperator = StateVector.StateVector -> StateVector.StateVector
    
    /// Configuration for amplitude amplification
    type AmplificationConfig = {
        /// Number of qubits in the system
        NumQubits: int
        
        /// State preparation operator A (prepares initial superposition)
        StatePreparation: StatePreparation
        
        /// Oracle O (marks "good" states)
        Oracle: CompiledOracle
        
        /// Reflection operator S₀ (reflects about prepared state)
        /// If None, uses standard reflection about StatePreparation|0⟩
        ReflectionOperator: ReflectionOperator option
        
        /// Number of amplification iterations
        Iterations: int
    }
    
    /// Result of amplitude amplification
    type AmplificationResult = {
        /// Final quantum state
        FinalState: StateVector.StateVector
        
        /// Number of iterations applied
        IterationsApplied: int
        
        /// Empirical success probability (probability of measuring a solution)
        SuccessProbability: float
        
        /// Measurement counts (if measured)
        MeasurementCounts: Map<int, int>
        
        /// Number of measurement shots
        Shots: int
    }
    
    // ========================================================================
    // REFLECTION OPERATORS - Core building blocks
    // ========================================================================
    
    /// Create reflection operator about a specific state |ψ⟩
    /// 
    /// Reflection operator: R_ψ = 2|ψ⟩⟨ψ| - I
    /// This reflects quantum states about |ψ⟩
    let reflectionAboutState (targetState: StateVector.StateVector) : ReflectionOperator =
        fun (state: StateVector.StateVector) ->
            let dimension = StateVector.dimension state
            
            // Calculate inner product ⟨ψ|φ⟩
            let innerProduct = StateVector.innerProduct targetState state
            
            // R_ψ|φ⟩ = 2⟨ψ|φ⟩|ψ⟩ - |φ⟩
            // CRITICAL: Must use complex multiplication for 2⟨ψ|φ⟩ψᵢ
            // This is where most quantum simulators fail - they only use the real part!
            let reflection =
                [| 0 .. dimension - 1 |]
                |> Array.map (fun i ->
                    let psiAmp = StateVector.getAmplitude i targetState
                    let phiAmp = StateVector.getAmplitude i state
                    
                    // 2⟨ψ|φ⟩ψᵢ - φᵢ (full complex multiplication)
                    let twoInnerProduct = 2.0 * innerProduct
                    let twoPsi = twoInnerProduct * psiAmp
                    twoPsi - phiAmp
                )
                |> StateVector.create
            
            reflection
    
    /// Create standard Grover reflection operator (reflection about uniform superposition)
    /// 
    /// This is the special case: reflection about |+⟩^⊗n = H^⊗n|0⟩
    let groverReflection (numQubits: int) : ReflectionOperator =
        fun (state: StateVector.StateVector) ->
            // Create uniform superposition |+⟩^⊗n using functional pipeline
            let uniformState = 
                [0 .. numQubits - 1]
                |> List.fold (fun s i -> Gates.applyH i s) (StateVector.init numQubits)
            
            // Reflect about uniform superposition
            let reflector = reflectionAboutState uniformState
            reflector state
    
    /// Create reflection operator from state preparation
    /// 
    /// Given state preparation A, creates reflection operator R_A = 2A|0⟩⟨0|A† - I
    /// This is equivalent to: A · (2|0⟩⟨0| - I) · A†
    let reflectionFromPreparation (numQubits: int) (statePrep: StatePreparation) : ReflectionOperator =
        // Prepare target state A|0⟩
        let initialState = StateVector.init numQubits
        let preparedState = statePrep initialState
        
        // Reflect about prepared state
        reflectionAboutState preparedState
    
    // ========================================================================
    // AMPLITUDE AMPLIFICATION - Core algorithm
    // ========================================================================
    
    /// Single amplitude amplification iteration
    /// 
    /// One iteration consists of:
    /// 1. Apply oracle O (mark good states)
    /// 2. Apply reflection S₀ (amplify good states)
    /// 
    /// This is analogous to Grover iteration but with custom reflection operator
    let applyAmplificationIteration (oracle: CompiledOracle) (reflection: ReflectionOperator) (state: StateVector.StateVector) : StateVector.StateVector =
        // Step 1: Apply oracle (phase flip good states)
        let afterOracle = oracle.LocalSimulation state
        
        // Step 2: Apply reflection operator (amplify good states)
        let afterReflection = reflection afterOracle
        
        afterReflection
    
    // ========================================================================
    // NOTE: This file contains both:
    // - the original StateVector-first building blocks (for educational purposes)
    // - `AmplitudeAmplification.Unified`, which provides intent→plan→execute against `IQuantumBackend`
    //
    // The old "Adapter" module references were removed; the unified-backend implementation now lives
    // in this file as `AmplitudeAmplification.Unified`.
    // ========================================================================

    // ========================================================================
    // UNIFIED BACKEND - Intent → Plan → Execute (ADR: intent-first algorithms)
    // ========================================================================

    module Unified =

        /// Execution exactness contract for amplitude amplification.
        type Exactness =
            | Exact
            | Approximate of epsilon: float

        /// A state preparation described as a gate circuit `A`.
        ///
        /// Using circuits makes `A†` well-defined via `CircuitBuilder.reverse`.
        type StatePreparationCircuit = CircuitBuilder.Circuit

        /// Canonical amplitude amplification intent.
        ///
        /// Applies `A` to prepare an initial superposition, then repeats:
        /// `Q = S_ψ · O` where `S_ψ = 2|ψ⟩⟨ψ| - I` and `|ψ⟩ = A|0...0⟩`.
        type AmplitudeAmplificationIntent = {
            NumQubits: int
            StatePreparation: StatePreparationCircuit
            Oracle: Oracle.CompiledOracle
            Iterations: int
            Exactness: Exactness
        }

        [<RequireQualifiedAccess>]
        type AmplitudeAmplificationPlan =
            /// Execute the prepared-state reflection using semantic Grover intent ops.
            ///
            /// This is only valid when `StatePreparation` is the uniform superposition `H^⊗n`.
            | ExecuteViaGroverIntents of prepOps: QuantumOperation list * oracleOp: QuantumOperation * diffusionOp: QuantumOperation * iterations: int * exactness: Exactness

            /// Execute via explicit lowering to supported gate operations.
            | ExecuteViaOps of prepOps: QuantumOperation list * iterationOps: QuantumOperation list * iterations: int * exactness: Exactness

        let private validateIntent (intent: AmplitudeAmplificationIntent) : Result<AmplitudeAmplificationIntent, QuantumError> =
            if intent.NumQubits <= 0 then
                Error (QuantumError.ValidationError ("NumQubits", "must be positive"))
            elif intent.Iterations < 0 then
                Error (QuantumError.ValidationError ("Iterations", "must be non-negative"))
            elif intent.Oracle.NumQubits <> intent.NumQubits then
                Error (QuantumError.ValidationError ("Oracle.NumQubits", $"must equal NumQubits ({intent.NumQubits}), got {intent.Oracle.NumQubits}"))
            elif intent.StatePreparation.QubitCount <> intent.NumQubits then
                Error (QuantumError.ValidationError ("StatePreparation.QubitCount", $"must equal NumQubits ({intent.NumQubits}), got {intent.StatePreparation.QubitCount}"))
            else
                match intent.Exactness with
                | Approximate epsilon when epsilon <= 0.0 ->
                    Error (QuantumError.ValidationError ("Exactness", "epsilon must be positive"))
                | _ -> Ok intent

        let private decomposeMCZGate (controls: int list) (target: int) (numQubits: int) : CircuitBuilder.Gate list =
            // Keep decomposition consistent with Grover unified implementation.
            let circuit = CircuitBuilder.empty numQubits
            let mczGate = CircuitBuilder.MCZ (controls, target)
            let circuitWithMCZ = CircuitBuilder.addGate mczGate circuit

            let transpiled1 = GateTranspiler.transpileForBackend "topological" circuitWithMCZ
            let transpiled2 = GateTranspiler.transpileForBackend "topological" transpiled1
            transpiled2.Gates

        let private lowerPreparedStateReflection (numQubits: int) : QuantumOperation list =
            // S0 (reflection about |0..0⟩) implemented as:
            // X^⊗n · (MCZ on |11..1⟩) · X^⊗n
            [
                for q in 0 .. numQubits - 1 do
                    yield QuantumOperation.Gate (CircuitBuilder.X q)

                let controls = [ 0 .. numQubits - 2 ]
                let targetQubit = numQubits - 1
                let decomposed = decomposeMCZGate controls targetQubit numQubits
                yield! decomposed |> List.map QuantumOperation.Gate

                for q in 0 .. numQubits - 1 do
                    yield QuantumOperation.Gate (CircuitBuilder.X q)
            ]

        let private enumerateSolutions (oracle: Oracle.CompiledOracle) : Result<int list, QuantumError> =
            let numQubits = oracle.NumQubits
            let searchSpaceSize = 1 <<< numQubits

            let solutions =
                [ 0 .. searchSpaceSize - 1 ]
                |> List.filter (Oracle.isSolution oracle.Spec)

            if List.isEmpty solutions then
                Error (QuantumError.ValidationError ("Oracle", "matches no solutions"))
            else
                Ok solutions

        let private lowerSingleTargetOracleOps (target: int) (numQubits: int) : QuantumOperation list =
            [
                // Step 1: X gates where target bit is 0
                for q in 0 .. numQubits - 1 do
                    let bitValue = (target >>> q) &&& 1
                    if bitValue = 0 then
                        yield QuantumOperation.Gate (CircuitBuilder.X q)

                // Step 2: Multi-controlled Z (phase flip when all qubits are |1⟩)
                let controls = [ 0 .. numQubits - 2 ]
                let targetQubit = numQubits - 1
                let decomposedMCZ = decomposeMCZGate controls targetQubit numQubits
                yield! decomposedMCZ |> List.map QuantumOperation.Gate

                // Step 3: Undo X gates
                for q in 0 .. numQubits - 1 do
                    let bitValue = (target >>> q) &&& 1
                    if bitValue = 0 then
                        yield QuantumOperation.Gate (CircuitBuilder.X q)
            ]

        let private lowerOracleOps (oracle: Oracle.CompiledOracle) : Result<QuantumOperation list, QuantumError> =
            result {
                let! solutions =
                    match oracle.Spec with
                    | Oracle.OracleSpec.SingleTarget t -> Ok [ t ]
                    | Oracle.OracleSpec.Solutions targets ->
                        if List.isEmpty targets then
                            Error (QuantumError.ValidationError ("Solutions", "list cannot be empty"))
                        else
                            Ok targets
                    | _ -> enumerateSolutions oracle

                // Lower to an explicit sequence that phase-flips each marked basis index.
                return solutions |> List.collect (fun t -> lowerSingleTargetOracleOps t oracle.NumQubits)
            }

        let private lowerUniformSuperpositionPrepOps (numQubits: int) : QuantumOperation list =

            [ 0 .. numQubits - 1 ]
            |> List.map (fun q -> QuantumOperation.Gate (CircuitBuilder.H q))

        let private isUniformSuperpositionCircuit (prep: CircuitBuilder.Circuit) : bool =
            // Heuristic: equal qubit count and exactly one H gate per qubit and nothing else.
            if prep.Gates.Length <> prep.QubitCount then
                false
            else
                let expectedSet = [ 0 .. prep.QubitCount - 1 ] |> List.map CircuitBuilder.H |> Set.ofList
                Set.ofList prep.Gates = expectedSet

        let private buildLoweredOps
            (intent: AmplitudeAmplificationIntent)
            : Result<QuantumOperation list * QuantumOperation list, QuantumError> =

            // Normalize prep circuit into a sequence of gate operations.
            let prepOps = intent.StatePreparation.Gates |> List.map QuantumOperation.Gate

            let reflectionOps =
                // Reflection about prepared state: A · S0 · A†
                let inversePrep = CircuitBuilder.reverse intent.StatePreparation
                prepOps
                @ lowerPreparedStateReflection intent.NumQubits
                @ (inversePrep.Gates |> List.map QuantumOperation.Gate)

            result {
                let! oracleOps = lowerOracleOps intent.Oracle
                return (prepOps, (oracleOps @ reflectionOps))
            }


        let plan
            (backend: IQuantumBackend)
            (intent: AmplitudeAmplificationIntent)
            : Result<AmplitudeAmplificationPlan, QuantumError> =

            result {
                let! intent = validateIntent intent

                // Annealing backends cannot support amplitude amplification.
                let! () =
                    match backend.NativeStateType with
                    | QuantumStateType.Annealing ->
                        Error (
                            QuantumError.OperationError (
                                "AmplitudeAmplification",
                                $"Backend '{backend.Name}' does not support amplitude amplification (native state type: {backend.NativeStateType})"
                            )
                        )
                    | _ ->
                        Ok ()

                // If state prep is uniform superposition and backend supports Grover intents,
                // we can reuse the native Grover operations as the special case.
                let groverPlan =
                    if isUniformSuperpositionCircuit intent.StatePreparation then
                        let prepare = QuantumOperation.Algorithm (AlgorithmOperation.GroverPrepare intent.NumQubits)
                        let oracle =
                            QuantumOperation.Algorithm (
                                AlgorithmOperation.GroverOraclePhaseFlip {
                                    NumQubits = intent.NumQubits
                                    IsMarked = Oracle.isSolution intent.Oracle.Spec
                                }
                            )
                        let diffusion = QuantumOperation.Algorithm (AlgorithmOperation.GroverDiffusion intent.NumQubits)

                        if backend.SupportsOperation prepare && backend.SupportsOperation oracle && backend.SupportsOperation diffusion then
                            let prepOps = lowerUniformSuperpositionPrepOps intent.NumQubits
                            Some (AmplitudeAmplificationPlan.ExecuteViaGroverIntents (prepOps, oracle, diffusion, intent.Iterations, intent.Exactness))
                        else
                            None
                    else
                        None

                match groverPlan with
                | Some plan ->
                    return plan
                | None ->
                    // Generic lowering path.
                    let! (prepOps, iterationOps) = buildLoweredOps intent

                    if (prepOps @ iterationOps) |> List.forall backend.SupportsOperation then
                        return AmplitudeAmplificationPlan.ExecuteViaOps (prepOps, iterationOps, intent.Iterations, intent.Exactness)
                    else
                        return!
                            Error (
                                QuantumError.OperationError (
                                    "AmplitudeAmplification",
                                    $"Backend '{backend.Name}' does not support required operations for amplitude amplification"
                                )
                            )
            }


        let private executePlan
            (backend: IQuantumBackend)
            (state: QuantumState)
            (plan: AmplitudeAmplificationPlan)
            : Result<QuantumState, QuantumError> =

            match plan with
            | AmplitudeAmplificationPlan.ExecuteViaGroverIntents (prepOps, oracleOp, diffusionOp, iterations, _) ->
                result {
                    let! prepared = UnifiedBackend.applySequence backend prepOps state
                    let! stateAfterIterations =
                        [ 1 .. iterations ]
                        |> List.fold (fun s _ -> s |> Result.bind (backend.ApplyOperation oracleOp) |> Result.bind (backend.ApplyOperation diffusionOp)) (Ok prepared)
                    return stateAfterIterations
                }

            | AmplitudeAmplificationPlan.ExecuteViaOps (prepOps, iterationOps, iterations, _) ->
                result {
                    let! prepared = UnifiedBackend.applySequence backend prepOps state
                    let applyIteration current = UnifiedBackend.applySequence backend iterationOps current
                    let! finalState =
                        [ 1 .. iterations ]
                        |> List.fold (fun s _ -> s |> Result.bind applyIteration) (Ok prepared)
                    return finalState
                }

        /// Execute amplitude amplification starting from |0...0⟩.
        let execute
            (backend: IQuantumBackend)
            (intent: AmplitudeAmplificationIntent)
            : Result<QuantumState, QuantumError> =

            result {
                let! plan = plan backend intent
                let! initial = backend.InitializeState intent.NumQubits
                return! executePlan backend initial plan
            }

    // ========================================================================
    // GROVER AS SPECIAL CASE - Show equivalence
    // ========================================================================
    
    /// Create amplitude amplification config for standard Grover search
    /// 
    /// This demonstrates that Grover is a special case of amplitude amplification:
    /// - State preparation = Hadamard on all qubits (uniform superposition)
    /// - Reflection = Grover diffusion operator
    let groverAsAmplification (oracle: CompiledOracle) (iterations: int) : AmplificationConfig =
        let numQubits = oracle.NumQubits
        
        // State preparation: H^⊗n (uniform superposition)
        let statePrep (state: StateVector.StateVector) : StateVector.StateVector =
            [0 .. numQubits - 1]
            |> List.fold (fun s qubitIdx -> Gates.applyH qubitIdx s) state
        
        // Reflection: Grover diffusion operator
        let reflection = groverReflection numQubits
        
        {
            NumQubits = numQubits
            StatePreparation = statePrep
            Oracle = oracle
            ReflectionOperator = Some reflection
            Iterations = iterations
        }
    
    // ========================================================================
    // NOTE: Legacy convenience functions removed
    // ========================================================================
    //
    // `executeGroverViaAmplification()` and `executeWithCustomPreparation()` were removed.
    // Use `AmplitudeAmplification.Unified.execute` for backend-compatible execution.
    
    /// Calculate optimal iterations for amplitude amplification
    /// 
    /// For M solutions in N-dimensional space with initial success probability p₀:
    /// k_opt = π/(4θ) where θ = arcsin(√p₀)
    let optimalIterations (searchSpaceSize: int) (numSolutions: int) (initialSuccessProb: float) : int =
        if initialSuccessProb >= 1.0 then
            0  // Already in solution space
        elif initialSuccessProb <= 0.0 then
            // Fall back to standard Grover formula
            let ratio = float searchSpaceSize / float numSolutions
            int (Math.Round((Math.PI / 4.0) * Math.Sqrt(ratio)))
        else
            // General formula: k = π/(4θ) where θ = arcsin(√p₀)
            let theta = Math.Asin(Math.Sqrt(initialSuccessProb))
            int (Math.Round(Math.PI / (4.0 * theta)))
    
    // ========================================================================
    // ADVANCED STATE PREPARATIONS - Examples
    // ========================================================================
    
    /// W-state preparation |W⟩ = (|100⟩ + |010⟩ + |001⟩)/√3
    /// 
    /// Example of non-uniform initial state
    let wStatePreparation (numQubits: int) : StatePreparation =
        fun (state: StateVector.StateVector) ->
            if numQubits <> 3 then
                failwith "W-state preparation only implemented for 3 qubits"
            
            let dimension = StateVector.dimension state
            let invSqrt3 = 1.0 / Math.Sqrt(3.0)
            
            // Create W-state: (|100⟩ + |010⟩ + |001⟩)/√3
            let amplitudes =
                [| 0 .. dimension - 1 |]
                |> Array.map (fun i ->
                    match i with
                    | 1 -> Complex(invSqrt3, 0.0)  // |001⟩
                    | 2 -> Complex(invSqrt3, 0.0)  // |010⟩
                    | 4 -> Complex(invSqrt3, 0.0)  // |100⟩
                    | _ -> Complex.Zero
                )
            
            StateVector.create amplitudes
    
    /// Partial uniform superposition over first k basis states
    /// 
    /// |ψ⟩ = (|0⟩ + |1⟩ + ... + |k-1⟩)/√k
    let partialUniformPreparation (numStates: int) (numQubits: int) : StatePreparation =
        fun (state: StateVector.StateVector) ->
            let dimension = StateVector.dimension state
            
            if numStates > dimension then
                failwith $"Cannot prepare uniform superposition over {numStates} states in {dimension}-dimensional space"
            
            let amplitude = Complex(1.0 / Math.Sqrt(float numStates), 0.0)
            
            let amplitudes =
                [| 0 .. dimension - 1 |]
                |> Array.map (fun i ->
                    if i < numStates then amplitude else Complex.Zero
                )
            
            StateVector.create amplitudes
    
    // ========================================================================
    // VERIFICATION - Compare with standard Grover
    // ========================================================================
    
    // NOTE: verifyGroverEquivalence() has been removed because:
    // - executeGroverViaAmplification() was deleted (legacy LocalSimulator dependency)
    // - Amplitude amplification now uses unified-backend execution
    // - To compare, use `Grover` and `AmplitudeAmplification.Unified` with the same backend


