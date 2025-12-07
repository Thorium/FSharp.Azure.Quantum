namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum.Core

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
    open FSharp.Azure.Quantum.Core
    open FSharp.Azure.Quantum.CircuitBuilder
    open FSharp.Azure.Quantum.Algorithms.QuantumFourierTransform
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
    /// Uses native CP gate instead of decomposition for simplicity and accuracy.
    let private addControlledPhase 
        (controlQubit: int) 
        (targetQubit: int) 
        (theta: float) 
        (circuit: Circuit) : Circuit =
        
        if not (System.Double.IsFinite(theta)) then
            failwith $"Invalid phase angle: {theta} (must be finite)"
        
        // Use native controlled-phase gate
        circuit |> addGate (CP(controlQubit, targetQubit, theta))
    
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
    let qftToCircuit (config: QFTConfig) : QuantumResult<Circuit> =
        try
            if config.NumQubits <= 0 then
                Error (QuantumError.ValidationError ("NumQubits", "must be positive"))
            elif config.NumQubits > 20 then
                Error (QuantumError.ValidationError ("NumQubits", "QFT with more than 20 qubits is not practical"))
            else
                // Start with empty circuit
                let initialCircuit = empty config.NumQubits
                
                // QFT main loop
                // Forward QFT: Process qubits from (n-1) down to 0
                // Inverse QFT: Process qubits from 0 up to (n-1) [REVERSED ORDER]
                let qubitProcessingOrder =
                    if config.Inverse then
                        [0 .. config.NumQubits - 1]  // Inverse: 0 → n-1
                    else
                        [(config.NumQubits - 1) .. -1 .. 0]  // Forward: n-1 → 0
                
                let circuitAfterQFT =
                    qubitProcessingOrder
                    |> List.fold (fun circuit j ->
                        if config.Inverse then
                            // Inverse QFT: Apply controlled-phase gates FIRST, then Hadamard
                            // (reverse order of forward QFT)
                            // Process CP gates in reverse order (from high k down to low k)
                            let circuitWithPhases =
                                [(config.NumQubits - 1) .. -1 .. (j + 1)]
                                |> List.fold (fun circ k ->
                                    let power = float (k - j + 1)
                                    let angle = -2.0 * Math.PI / (2.0 ** power)
                                    addControlledPhase k j angle circ
                                ) circuit
                            
                            addGate (H j) circuitWithPhases
                        else
                            // Forward QFT: Apply Hadamard FIRST, then controlled-phase gates
                            let circuitWithH = addGate (H j) circuit
                            
                            [j + 1 .. config.NumQubits - 1]
                            |> List.fold (fun circ k ->
                                let power = float (k - j + 1)
                                let angle = 2.0 * Math.PI / (2.0 ** power)
                                addControlledPhase k j angle circ
                            ) circuitWithH
                    ) initialCircuit
                
                // Step 3: Apply SWAP gates to reverse qubit order (if enabled)
                // For forward QFT: SWAP comes AFTER QFT operations
                // For inverse QFT: SWAP comes BEFORE inverse QFT operations (reversal!)
                let finalCircuit =
                    if config.ApplySwaps then
                        if config.Inverse then
                            // Inverse: Apply SWAPs FIRST, then inverse QFT operations
                            // (This reverses the forward order: QFT ops → SWAPs becomes SWAPs → inv-QFT ops)
                            let circuitWithSwaps =
                                [0 .. (config.NumQubits / 2 - 1)]
                                |> List.fold (fun circuit i ->
                                    let qubit1 = i
                                    let qubit2 = config.NumQubits - 1 - i
                                    addGate (SWAP(qubit1, qubit2)) circuit
                                ) initialCircuit
                            
                            // Now apply inverse QFT operations after SWAPs
                            qubitProcessingOrder
                            |> List.fold (fun circuit j ->
                                // Inverse QFT: CP gates first (reversed order), then H
                                let circuitWithPhases =
                                    [(config.NumQubits - 1) .. -1 .. (j + 1)]
                                    |> List.fold (fun circ k ->
                                        let power = float (k - j + 1)
                                        let angle = -2.0 * Math.PI / (2.0 ** power)
                                        addControlledPhase k j angle circ
                                    ) circuit
                                
                                addGate (H j) circuitWithPhases
                            ) circuitWithSwaps
                        else
                            // Forward: Apply SWAPs AFTER QFT operations (as before)
                            [0 .. (config.NumQubits / 2 - 1)]
                            |> List.fold (fun circuit i ->
                                let qubit1 = i
                                let qubit2 = config.NumQubits - 1 - i
                                addGate (SWAP(qubit1, qubit2)) circuit
                            ) circuitAfterQFT
                    else
                        circuitAfterQFT
                
                Ok finalCircuit
        
        with ex ->
            Error (QuantumError.Other $"QFT circuit synthesis failed: {ex.Message}")
    
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
    
    /// Execute QFT using IQuantumBackend (async version)
    /// 
    /// Parameters:
    /// - config: QFT configuration (qubits, swaps, inverse)
    /// - backend: Quantum backend to execute on
    /// - numShots: Number of measurement shots
    /// - inputState: Optional input basis state (default: |0⟩)
    /// 
    /// Returns: Async<Result<Map<int, int>, string>> - Async computation with measurement counts
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
    let executeQFTWithBackendAsync 
        (config: QFTConfig) 
        (backend: IQuantumBackend) 
        (numShots: int) 
        (inputState: int option) 
        : Async<Result<Map<int, int>, string>> = async {
        
        try
            // Step 1: Validate inputs
            if numShots <= 0 then
                return Error "NumShots must be positive"
            elif config.NumQubits > backend.MaxQubits then
                return Error $"QFT requires {config.NumQubits} qubits but backend '{backend.Name}' supports max {backend.MaxQubits}"
            else
                // Step 2: Convert QFT to circuit
                match qftToCircuit config with
                | Error err -> return Error err.Message
                | Ok qftCircuit ->
                    // Step 3: Prepend input state preparation (if not |0⟩)
                    let fullCircuit =
                        match inputState with
                        | None -> qftCircuit  // Start from |0⟩
                        | Some state ->
                            // Prepare input basis state using X gates
                            let prepCircuit =
                                [0 .. config.NumQubits - 1]
                                |> List.fold (fun circuit i ->
                                    if (state >>> i) &&& 1 = 1 then
                                        addGate (X i) circuit
                                    else
                                        circuit
                                ) (empty config.NumQubits)
                            
                            // Compose: input preparation + QFT
                            compose prepCircuit qftCircuit
                    
                    // Step 4: Wrap circuit in ICircuit interface
                    let circuitWrapper = CircuitWrapper(fullCircuit)
                    
                    // Step 5: Execute on backend asynchronously
                    let! execResult = backend.ExecuteAsync circuitWrapper numShots
                    
                    match execResult with
                    | Error err -> return Error (err |> QuantumResult.toString)
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
                        
                        return Ok counts
        
        with ex ->
            return Error $"QFT backend execution failed: {ex.Message}"
    }

    /// Execute QFT using IQuantumBackend (synchronous wrapper)
    /// 
    /// This is a synchronous wrapper around executeQFTWithBackendAsync for backward compatibility.
    /// For cloud backends (IonQ, Rigetti), prefer using executeQFTWithBackendAsync directly.
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
        executeQFTWithBackendAsync config backend numShots inputState
        |> Async.RunSynchronously
    
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
