namespace FSharp.Azure.Quantum.GroverSearch

open System

/// QFT Backend Adapter Module
/// 
/// Bridges Quantum Fourier Transform to IQuantumBackend interface, enabling execution
/// on cloud quantum hardware (IonQ, Rigetti) in addition to local simulation.
/// 
/// Key responsibilities:
/// - Convert QFT configuration to quantum circuit gates
/// - Handle controlled rotation gates and SWAP gates
/// - Execute QFT via IQuantumBackend
/// - Convert measurement results back to basis states
/// 
/// QFT Circuit Structure:
/// 1. For each qubit j (from n-1 down to 0):
///    - Apply H gate to qubit j
///    - For each qubit k > j:
///      - Apply controlled-Rz(2π/2^(k-j+1)) with control=k, target=j
/// 2. Apply SWAP gates to reverse qubit order (optional)
module QFTBackendAdapter =
    
    open FSharp.Azure.Quantum
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.Core.CircuitAbstraction
    open FSharp.Azure.Quantum.CircuitBuilder
    open FSharp.Azure.Quantum.GroverSearch.QuantumFourierTransform
    open FSharp.Azure.Quantum.LocalSimulator.StateVector
    
    // ========================================================================
    // CONTROLLED ROTATION CIRCUIT SYNTHESIS
    // ========================================================================
    
    /// Add controlled-phase gate CP(θ) to circuit
    /// 
    /// IMPORTANT: QFT uses controlled-phase gates, not controlled-Rz!
    /// 
    /// CP(θ) = |0⟩⟨0| ⊗ I + |1⟩⟨1| ⊗ P(θ)
    /// where P(θ) = [[1, 0], [0, e^(iθ)]]
    /// 
    /// This is mathematically equivalent to applying a phase e^(iθ) when control is |1⟩.
    /// The QFT requires these phase gates with angles 2π/2^k.
    /// 
    /// Standard decomposition using available gates:
    /// CP(θ) = Rz(θ/2) on control, Rz(θ/2) on target, CNOT, Rz(-θ/2) on target, CNOT
    /// 
    /// This 5-gate decomposition is exact and works on all quantum hardware backends.
    let private addControlledPhase 
        (controlQubit: int) 
        (targetQubit: int) 
        (theta: float) 
        (circuit: Circuit) : Circuit =
        
        if not (System.Double.IsFinite(theta)) then
            failwith $"Invalid phase angle: {theta} (must be finite)"
        
        let halfTheta = theta / 2.0
        
        // Full controlled-phase decomposition
        circuit
        |> addGate (RZ(controlQubit, halfTheta))
        |> addGate (RZ(targetQubit, halfTheta))
        |> addGate (CNOT(controlQubit, targetQubit))
        |> addGate (RZ(targetQubit, -halfTheta))
        |> addGate (CNOT(controlQubit, targetQubit))
    
    // ========================================================================
    // QFT CIRCUIT SYNTHESIS
    // ========================================================================
    
    /// Convert QFT configuration to quantum circuit
    /// 
    /// QFT algorithm:
    /// For j from n-1 down to 0:
    ///   1. Apply Hadamard to qubit j
    ///   2. For k from j+1 to n-1:
    ///      - Apply controlled-Rz(2π/2^(k-j+1)) with control=k, target=j
    /// 
    /// Optionally apply SWAP gates to reverse qubit order
    let qftToCircuit (config: QFTConfig) : Result<Circuit, string> =
        try
            if config.NumQubits <= 0 then
                Error "Number of qubits must be positive"
            elif config.NumQubits > 20 then
                Error "QFT with more than 20 qubits is not practical"
            else
                // Start with empty circuit
                let mutable circuit = empty config.NumQubits
                
                // QFT main loop (process qubits from n-1 down to 0)
                for j in (config.NumQubits - 1) .. -1 .. 0 do
                    // Step 1: Apply Hadamard to qubit j
                    circuit <- addGate (H j) circuit
                    
                    // Step 2: Apply controlled-phase rotations from higher qubits
                    for k in (j + 1) .. (config.NumQubits - 1) do
                        // Phase angle: 2π / 2^(k-j+1)
                        let power = float (k - j + 1)
                        let angle = 
                            if config.Inverse then
                                -2.0 * Math.PI / (2.0 ** power)  // Inverse QFT: negate angles
                            else
                                2.0 * Math.PI / (2.0 ** power)
                        
                        circuit <- addControlledPhase k j angle circuit
                
                // Step 3: Apply SWAP gates to reverse qubit order (if enabled)
                if config.ApplySwaps then
                    for i in 0 .. (config.NumQubits / 2 - 1) do
                        let qubit1 = i
                        let qubit2 = config.NumQubits - 1 - i
                        circuit <- addGate (SWAP(qubit1, qubit2)) circuit
                
                Ok circuit
        
        with ex ->
            Error $"QFT circuit synthesis failed: {ex.Message}"
    
    // ========================================================================
    // RESULT CONVERSION
    // ========================================================================
    
    /// Convert backend execution result to QFT result
    /// 
    /// Parameters:
    /// - executionResult: Raw measurement data from backend
    /// - config: QFT configuration used
    /// - gateCount: Number of gates in circuit
    /// 
    /// Returns: QFTResult with measurement counts and statistics
    let private executionResultToQFTResult 
        (executionResult: ExecutionResult) 
        (config: QFTConfig) 
        (gateCount: int) : QFTResult =
        
        // Convert measurements to basis state counts
        let counts = 
            executionResult.Measurements
            |> Array.map (fun bitstring ->
                // Convert bitstring to integer
                bitstring 
                |> Array.rev  // LSB first
                |> Array.fold (fun acc bit -> acc * 2 + bit) 0)
            |> Array.groupBy id
            |> Array.map (fun (state, instances) -> (state, Array.length instances))
            |> Map.ofArray
        
        // Note: We cannot reconstruct full StateVector from measurements alone
        // Backend execution gives us measurement statistics, not amplitudes
        // For QFT, the output is typically measured and used classically
        
        // Note: Backend execution CANNOT reconstruct full quantum state from measurements
        // We return measurement statistics (counts), not amplitudes or phases
        // This dummy StateVector exists only for type compatibility with QFTResult
        // 
        // For applications requiring state vector amplitudes, use local simulation:
        // QuantumFourierTransform.execute config state
        let dummyState = init 1  // Single qubit dummy state (type compatibility only)
        
        {
            FinalState = dummyState  // Cannot reconstruct - use measurement counts instead
            GateCount = gateCount
            Config = config
        }
    
    // ========================================================================
    // BACKEND EXECUTION
    // ========================================================================
    
    /// Execute QFT using IQuantumBackend
    /// 
    /// Parameters:
    /// - config: QFT configuration (qubits, swaps, inverse)
    /// - backend: Quantum backend to execute on
    /// - numShots: Number of measurement shots
    /// - inputState: Optional input basis state (default: |0⟩)
    /// 
    /// Returns: Measurement counts (basis state -> count)
    /// 
    /// IMPORTANT LIMITATIONS:
    /// - Backend execution returns MEASUREMENT STATISTICS ONLY, not full state vector
    /// - Quantum amplitudes and phases are LOST during measurement
    /// - Cannot reconstruct the full quantum state from measurement results
    /// - Only suitable for algorithms that measure QFT output (phase estimation, Shor's)
    /// - For amplitude/phase analysis, use local simulation instead
    /// 
    /// This is a fundamental limitation of quantum hardware - measurements collapse
    /// the quantum state, and repeated measurements only give probability distributions.
    let executeQFTWithBackend 
        (config: QFTConfig) 
        (backend: IQuantumBackend) 
        (numShots: int) 
        (inputState: int option) 
        : Result<Map<int, int>, string> =
        
        try
            // Step 1: Validate inputs
            if numShots <= 0 then
                Error "Number of shots must be positive"
            elif config.NumQubits > backend.MaxQubits then
                Error $"QFT requires {config.NumQubits} qubits but backend '{backend.Name}' supports max {backend.MaxQubits}"
            else
                // Step 2: Convert QFT to circuit
                match qftToCircuit config with
                | Error msg -> Error msg
                | Ok qftCircuit ->
                    // Step 3: Prepend input state preparation (if not |0⟩)
                    let fullCircuit =
                        match inputState with
                        | None -> qftCircuit  // Start from |0⟩
                        | Some state ->
                            // Prepare input basis state using X gates
                            let mutable prepCircuit = empty config.NumQubits
                            for i in 0 .. (config.NumQubits - 1) do
                                if (state >>> i) &&& 1 = 1 then
                                    prepCircuit <- addGate (X i) prepCircuit
                            
                            // Compose: input preparation + QFT
                            compose prepCircuit qftCircuit
                    
                    // Step 4: Wrap circuit in ICircuit interface
                    let circuitWrapper = CircuitWrapper(fullCircuit)
                    
                    // Step 5: Execute on backend
                    match backend.Execute circuitWrapper numShots with
                    | Error msg -> Error msg
                    | Ok executionResult ->
                        // Step 6: Convert measurements to basis state counts
                        let counts = 
                            executionResult.Measurements
                            |> Array.map (fun bitstring ->
                                bitstring 
                                |> Array.rev
                                |> Array.fold (fun acc bit -> acc * 2 + bit) 0)
                            |> Array.groupBy id
                            |> Array.map (fun (state, instances) -> (state, Array.length instances))
                            |> Map.ofArray
                        
                        Ok counts
        
        with ex ->
            Error $"QFT backend execution failed: {ex.Message}"
    
    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================
    
    /// Execute standard QFT (with swaps, forward transform)
    let executeStandardQFT 
        (numQubits: int) 
        (backend: IQuantumBackend) 
        (numShots: int) 
        : Result<Map<int, int>, string> =
        
        let config = {
            NumQubits = numQubits
            ApplySwaps = true
            Inverse = false
        }
        executeQFTWithBackend config backend numShots None
    
    /// Execute inverse QFT
    let executeInverseQFT 
        (numQubits: int) 
        (backend: IQuantumBackend) 
        (numShots: int) 
        : Result<Map<int, int>, string> =
        
        let config = {
            NumQubits = numQubits
            ApplySwaps = true
            Inverse = true
        }
        executeQFTWithBackend config backend numShots None
    
    /// Execute QFT on specific input state
    let executeQFTOnState 
        (numQubits: int) 
        (inputState: int) 
        (backend: IQuantumBackend) 
        (numShots: int) 
        : Result<Map<int, int>, string> =
        
        let config = {
            NumQubits = numQubits
            ApplySwaps = true
            Inverse = false
        }
        executeQFTWithBackend config backend numShots (Some inputState)
