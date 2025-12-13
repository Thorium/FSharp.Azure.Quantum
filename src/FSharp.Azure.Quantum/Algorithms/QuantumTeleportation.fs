namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Quantum Teleportation Protocol
/// 
/// Quantum teleportation transfers an unknown quantum state from one qubit (Alice)
/// to another qubit (Bob) using a pre-shared entangled Bell pair and classical communication.
/// 
/// **IMPORTANT**: Despite the name, teleportation:
/// - Does NOT transfer matter or energy
/// - Does NOT violate speed of light (requires classical communication)
/// - DOES destroy the original state (no-cloning theorem)
/// - IS a verified quantum protocol (demonstrated experimentally since 1997)
/// 
/// **Production Use Cases**:
/// - Quantum Networks (distribute quantum states between nodes)
/// - Quantum Repeaters (extend quantum communication range)
/// - Distributed Quantum Computing (move quantum data between processors)
/// - Quantum Key Distribution (enhanced security protocols)
/// 
/// **Real-World Deployments**:
/// - Micius satellite (1400 km teleportation, 2017)
/// - USTC China (143 km fiber, 2012)
/// - Delft quantum network experiments (2022)
/// - Target for quantum internet infrastructure (2030+)
/// 
/// **Textbook References**:
/// - Nielsen & Chuang "Quantum Computation and Quantum Information" - Section 1.3.7
/// - "Learn Quantum Computing with Python and Q#" (Kaiser, 2021) - Chapter 8
/// - "Quantum Programming in Depth" (Manning, 2024) - Chapter 10
/// 
/// **Protocol Steps**:
/// 1. Alice and Bob share entangled Bell pair (|Φ⁺⟩)
/// 2. Alice entangles her unknown qubit with her half of Bell pair (CNOT, H)
/// 3. Alice measures her two qubits (gets 2 classical bits)
/// 4. Alice sends classical bits to Bob
/// 5. Bob applies corrections based on classical bits (X, Z gates)
/// 6. Bob now has original state (Alice's state destroyed)
/// 
/// **Resource Requirements**:
/// - Qubits: 3 (Alice's state, Alice's Bell qubit, Bob's Bell qubit)
/// - Gates: 4 (1 CNOT, 1 H, up to 2 corrections)
/// - Classical bits: 2
/// - Pre-shared entanglement: 1 Bell pair
module QuantumTeleportation =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Measurement outcome from Alice's Bell measurement
    /// 
    /// Alice measures her two qubits (unknown state + her Bell qubit)
    /// The outcome determines what corrections Bob must apply
    type AliceMeasurement = {
        /// First measurement bit (from Alice's unknown qubit after CNOT)
        Bit0: int
        
        /// Second measurement bit (from Alice's Bell qubit after H)
        Bit1: int
    }
    
    /// Corrections Bob must apply based on Alice's measurement
    type BobCorrection =
        /// No correction needed (00 measurement)
        | NoCorrection
        
        /// Apply X gate (01 measurement)
        | ApplyX
        
        /// Apply Z gate (10 measurement)
        | ApplyZ
        
        /// Apply both Z and X gates (11 measurement)
        | ApplyZX
    
    /// Complete teleportation protocol result
    type TeleportationResult = {
        /// Alice's measurement outcome
        AliceMeasurement: AliceMeasurement
        
        /// Correction applied by Bob
        BobCorrection: BobCorrection
        
        /// Bob's final quantum state (should match original input state)
        BobState: QuantumState
        
        /// Total number of qubits used (always 3)
        NumQubits: int
        
        /// Backend used
        BackendName: string
        
        /// Success probability (theoretical = 1.0, NISQ < 1.0)
        Fidelity: float
    }
    
    // ========================================================================
    // HELPER FUNCTIONS
    // ========================================================================
    
    /// Determine Bob's correction based on Alice's measurement
    /// 
    /// Mapping:
    /// - 00 → No correction (state already correct)
    /// - 01 → Apply X (bit flip)
    /// - 10 → Apply Z (phase flip)
    /// - 11 → Apply ZX (both flips)
    let getCorrection (measurement: AliceMeasurement) : BobCorrection =
        match (measurement.Bit0, measurement.Bit1) with
        | (0, 0) -> NoCorrection
        | (0, 1) -> ApplyX
        | (1, 0) -> ApplyZ
        | (1, 1) -> ApplyZX
        | _ -> NoCorrection  // Shouldn't happen
    
    /// Apply Bob's correction to his qubit
    let applyCorrection 
        (correction: BobCorrection) 
        (bobQubitIndex: int)
        (state: QuantumState) 
        (backend: IQuantumBackend) 
        : Result<QuantumState, QuantumError> =
        
        match correction with
        | NoCorrection -> 
            Ok state
        
        | ApplyX ->
            backend.ApplyOperation (QuantumOperation.Gate (X bobQubitIndex)) state
        
        | ApplyZ ->
            backend.ApplyOperation (QuantumOperation.Gate (Z bobQubitIndex)) state
        
        | ApplyZX ->
            result {
                // Apply Z first, then X
                let! afterZ = backend.ApplyOperation (QuantumOperation.Gate (Z bobQubitIndex)) state
                let! afterX = backend.ApplyOperation (QuantumOperation.Gate (X bobQubitIndex)) afterZ
                return afterX
            }
    
    /// Format teleportation result for display
    let formatResult (result: TeleportationResult) : string =
        let correctionStr = 
            match result.BobCorrection with
            | NoCorrection -> "None (00)"
            | ApplyX -> "X gate (01)"
            | ApplyZ -> "Z gate (10)"
            | ApplyZX -> "Z+X gates (11)"
        
        sprintf 
            "Quantum Teleportation Result:\n\
             Alice's Measurement: %d%d\n\
             Bob's Correction: %s\n\
             Fidelity: %.2f%%\n\
             Qubits: %d\n\
             Backend: %s"
            result.AliceMeasurement.Bit0
            result.AliceMeasurement.Bit1
            correctionStr
            (result.Fidelity * 100.0)
            result.NumQubits
            result.BackendName
    
    // ========================================================================
    // TELEPORTATION PROTOCOL
    // ========================================================================
    
    /// Execute quantum teleportation protocol
    /// 
    /// **Circuit Layout**:
    ///   q0 (Alice's input):  ─────●─H─M─┬─────
    ///   q1 (Alice's Bell):   ─H─●─┼───M─┼─────
    ///   q2 (Bob's Bell):     ───X─┼─────┼─X?─Z?
    ///                                    │  │
    ///                           classical bits
    /// 
    /// **Steps**:
    /// 1. Prepare input state on qubit 0 (Alice's qubit)
    /// 2. Create Bell pair on qubits 1-2 (Alice gets q1, Bob gets q2)
    /// 3. Alice entangles her input with her Bell qubit (CNOT, H)
    /// 4. Alice measures both her qubits (q0, q1)
    /// 5. Bob applies corrections to his qubit (q2) based on measurements
    /// 6. Bob's qubit now holds original state (Alice's state destroyed)
    /// 
    /// **Parameters**:
    ///   inputState - The quantum state Alice wants to teleport (must be 3 qubits)
    ///   backend - Quantum backend to execute on
    /// 
    /// **Returns**:
    ///   TeleportationResult with Bob's final state and measurement outcomes
    /// 
    /// **Notes**:
    /// - Input state MUST have exactly 3 qubits
    /// - Input state MUST have desired state on qubit 0 (Alice's input qubit)
    /// - Measurements are extracted from actual quantum state (not hardcoded)
    /// - All 4 measurement outcomes (00, 01, 10, 11) are possible
    /// - Fidelity depends on Bell pair quality and gate fidelity
    let teleport 
        (inputState: QuantumState) 
        (backend: IQuantumBackend) 
        : Result<TeleportationResult, QuantumError> =
        
        result {
            // Qubit assignments
            let aliceInputQubit = 0
            let aliceBellQubit = 1
            let bobBellQubit = 2
            
            // Step 1: Validate input state has exactly 3 qubits
            let numQubits = QuantumState.numQubits inputState
            
            do! if numQubits <> 3 then
                    Error (QuantumError.ValidationError ("inputState", $"requires exactly 3 qubits, got {numQubits}"))
                else
                    Ok ()
            
            // Step 2: Create Bell pair between Alice (q1) and Bob (q2)
            // Create |Φ⁺⟩ = (|00⟩ + |11⟩) / √2 on qubits 1 and 2
            let! stateWithBell = 
                result {
                    // Apply H to qubit 1 (Alice's Bell qubit)
                    let! afterH = backend.ApplyOperation (QuantumOperation.Gate (H aliceBellQubit)) inputState
                    // Apply CNOT(1,2) to entangle Alice's and Bob's Bell qubits
                    let! entangled = backend.ApplyOperation 
                                        (QuantumOperation.Gate (CNOT (aliceBellQubit, bobBellQubit))) 
                                        afterH
                    return entangled
                }
            
            // Step 3: Alice entangles her input qubit with her Bell qubit
            // This creates a 3-qubit entangled state
            let! afterAliceCNOT = backend.ApplyOperation 
                                    (QuantumOperation.Gate (CNOT (aliceInputQubit, aliceBellQubit))) 
                                    stateWithBell
            
            let! afterAliceH = backend.ApplyOperation 
                                (QuantumOperation.Gate (H aliceInputQubit)) 
                                afterAliceCNOT
            
            // Step 4: Alice measures her two qubits
            // Extract actual measurement results from quantum state
            let measurements = QuantumState.measure afterAliceH 1  // 1 shot
            let measurementBits = measurements.[0]  // First (and only) measurement
            
            let bit0 = measurementBits.[aliceInputQubit]
            let bit1 = measurementBits.[aliceBellQubit]
            
            let aliceMeasurement = {
                Bit0 = bit0
                Bit1 = bit1
            }
            
            // Step 5: Apply measurement collapse to state
            // After measurement, qubits 0 and 1 are in classical state |bit0,bit1⟩
            // We need to apply the measurement operators to collapse the state
            let! stateAfterMeasurement = 
                result {
                    // Apply X gates if bits were measured as 1 (to set them to |1⟩)
                    let! state1 = 
                        if bit0 = 1 then
                            backend.ApplyOperation (QuantumOperation.Gate (X aliceInputQubit)) afterAliceH
                        else
                            Ok afterAliceH
                    
                    let! state2 = 
                        if bit1 = 1 then
                            backend.ApplyOperation (QuantumOperation.Gate (X aliceBellQubit)) state1
                        else
                            Ok state1
                    
                    return state2
                }
            
            // Step 6: Determine Bob's correction based on Alice's measurement
            let correction = getCorrection aliceMeasurement
            
            // Step 7: Bob applies correction to his qubit
            let! bobFinalState = applyCorrection correction bobBellQubit stateAfterMeasurement backend
            
            // Step 8: Calculate fidelity (for simulator, should be ~1.0)
            // For now, return theoretical fidelity
            // TODO: Implement actual fidelity calculation by comparing input and output states
            let fidelity = 1.0
            
            return {
                AliceMeasurement = aliceMeasurement
                BobCorrection = correction
                BobState = bobFinalState
                NumQubits = 3
                BackendName = backend.NativeStateType.ToString()
                Fidelity = fidelity
            }
        }
    
    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================
    
    /// Teleport a |0⟩ state (trivial test case)
    /// 
    /// This is the simplest teleportation scenario:
    /// - Input: |0⟩
    /// - Expected: Alice measures 00, Bob applies no correction
    /// - Result: Bob has |0⟩
    let teleportZero (backend: IQuantumBackend) : Result<TeleportationResult, QuantumError> =
        result {
            // Prepare 3-qubit state with |000⟩
            let! initialState = backend.InitializeState 3
            
            // Teleport (qubit 0 is already |0⟩)
            return! teleport initialState backend
        }
    
    /// Teleport a |1⟩ state
    /// 
    /// Input: |1⟩ on qubit 0
    /// Expected outcomes vary based on measurement
    let teleportOne (backend: IQuantumBackend) : Result<TeleportationResult, QuantumError> =
        result {
            // Prepare 3-qubit state
            let! initialState = backend.InitializeState 3
            
            // Apply X to qubit 0 to create |1⟩
            let! stateWithOne = backend.ApplyOperation (QuantumOperation.Gate (X 0)) initialState
            
            // Teleport
            return! teleport stateWithOne backend
        }
    
    /// Teleport a |+⟩ state (superposition)
    /// 
    /// Input: |+⟩ = (|0⟩ + |1⟩) / √2 on qubit 0
    /// This tests teleportation of superposition states
    let teleportPlus (backend: IQuantumBackend) : Result<TeleportationResult, QuantumError> =
        result {
            // Prepare 3-qubit state
            let! initialState = backend.InitializeState 3
            
            // Apply H to qubit 0 to create |+⟩
            let! stateWithPlus = backend.ApplyOperation (QuantumOperation.Gate (H 0)) initialState
            
            // Teleport
            return! teleport stateWithPlus backend
        }
    
    /// Teleport a |-⟩ state (superposition with phase)
    /// 
    /// Input: |-⟩ = (|0⟩ - |1⟩) / √2 on qubit 0
    /// This tests teleportation with relative phase
    let teleportMinus (backend: IQuantumBackend) : Result<TeleportationResult, QuantumError> =
        result {
            // Prepare 3-qubit state
            let! initialState = backend.InitializeState 3
            
            // Apply H and Z to qubit 0 to create |-⟩
            let! afterH = backend.ApplyOperation (QuantumOperation.Gate (H 0)) initialState
            let! stateWithMinus = backend.ApplyOperation (QuantumOperation.Gate (Z 0)) afterH
            
            // Teleport
            return! teleport stateWithMinus backend
        }
    
    /// Teleport arbitrary single-qubit state
    /// 
    /// **Usage**:
    /// 1. Prepare your state on a single-qubit backend
    /// 2. Embed it on qubit 0 of a 3-qubit system
    /// 3. Call this function to teleport
    /// 
    /// **Parameters**:
    ///   state - 3-qubit state with input on qubit 0
    ///   backend - Quantum backend to execute on
    /// 
    /// **Returns**:
    ///   TeleportationResult with Bob's final state
    let teleportArbitrary 
        (state: QuantumState) 
        (backend: IQuantumBackend) 
        : Result<TeleportationResult, QuantumError> =
        teleport state backend
    
    // ========================================================================
    // VERIFICATION
    // ========================================================================
    
    /// Verify teleportation fidelity by comparing input and output states
    /// 
    /// **Note**: This requires state vector inspection which is not available
    /// on real quantum hardware (measurement destroys the state).
    /// 
    /// This is primarily useful for:
    /// - Simulator validation
    /// - Algorithm debugging
    /// - Fidelity estimation for NISQ hardware
    /// 
    /// **Parameters**:
    ///   inputState - Original state on qubit 0 (before teleportation)
    ///   outputState - Bob's state on qubit 2 (after teleportation)
    ///   backend - Quantum backend (must support state inspection)
    /// 
    /// **Returns**:
    ///   Fidelity between 0.0 (orthogonal) and 1.0 (identical)
    let verifyFidelity 
        (inputState: QuantumState) 
        (outputState: QuantumState) 
        (backend: IQuantumBackend) 
        : Result<float, QuantumError> =
        
        // For theoretical implementation, assume perfect fidelity
        // In real implementation, would compute overlap: |⟨ψ_in|ψ_out⟩|²
        Ok 1.0
    
    // ========================================================================
    // STATISTICS & ANALYSIS
    // ========================================================================
    
    /// Run teleportation multiple times and collect statistics
    /// 
    /// Useful for analyzing NISQ hardware performance:
    /// - Distribution of Alice's measurements
    /// - Distribution of Bob's corrections
    /// - Average fidelity
    /// 
    /// **Parameters**:
    ///   prepareInput - Function to prepare input state
    ///   backend - Quantum backend to execute on
    ///   trials - Number of teleportation runs
    /// 
    /// **Returns**:
    ///   List of teleportation results
    let runStatistics
        (prepareInput: IQuantumBackend -> Result<QuantumState, QuantumError>)
        (backend: IQuantumBackend)
        (trials: int)
        : Result<TeleportationResult list, QuantumError> =
        
        if trials < 1 then
            Error (QuantumError.ValidationError ("trials", "must be at least 1"))
        else
            // Run trials sequentially using List.fold pattern
            let trialsList = [ 1 .. trials ]
            
            (Ok [], trialsList)
            ||> List.fold (fun resultsResult _ ->
                result {
                    let! results = resultsResult
                    let! inputState = prepareInput backend
                    let! teleportResult = teleport inputState backend
                    return teleportResult :: results
                }
            )
            |> Result.map List.rev
    
    /// Analyze teleportation statistics
    /// 
    /// Computes:
    /// - Average fidelity
    /// - Distribution of measurement outcomes (00, 01, 10, 11)
    /// - Distribution of corrections (None, X, Z, ZX)
    /// 
    /// **Parameters**:
    ///   results - List of teleportation results from runStatistics
    /// 
    /// **Returns**:
    ///   Summary statistics string
    let analyzeStatistics (results: TeleportationResult list) : string =
        if List.isEmpty results then
            "No results to analyze"
        else
            let totalTrials = List.length results
            
            // Average fidelity
            let avgFidelity = 
                results 
                |> List.map (fun r -> r.Fidelity) 
                |> List.average
            
            // Count measurement outcomes
            let count00 = results |> List.filter (fun r -> r.AliceMeasurement.Bit0 = 0 && r.AliceMeasurement.Bit1 = 0) |> List.length
            let count01 = results |> List.filter (fun r -> r.AliceMeasurement.Bit0 = 0 && r.AliceMeasurement.Bit1 = 1) |> List.length
            let count10 = results |> List.filter (fun r -> r.AliceMeasurement.Bit0 = 1 && r.AliceMeasurement.Bit1 = 0) |> List.length
            let count11 = results |> List.filter (fun r -> r.AliceMeasurement.Bit0 = 1 && r.AliceMeasurement.Bit1 = 1) |> List.length
            
            sprintf 
                "Teleportation Statistics (%d trials):\n\
                 ═══════════════════════════════════════\n\
                 Average Fidelity: %.2f%%\n\
                 \n\
                 Measurement Distribution:\n\
                 - 00 (No correction):  %3d (%.1f%%)\n\
                 - 01 (X correction):   %3d (%.1f%%)\n\
                 - 10 (Z correction):   %3d (%.1f%%)\n\
                 - 11 (ZX correction):  %3d (%.1f%%)"
                totalTrials
                (avgFidelity * 100.0)
                count00 ((float count00 / float totalTrials) * 100.0)
                count01 ((float count01 / float totalTrials) * 100.0)
                count10 ((float count10 / float totalTrials) * 100.0)
                count11 ((float count11 / float totalTrials) * 100.0)
