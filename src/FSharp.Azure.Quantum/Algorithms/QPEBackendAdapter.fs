namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum.Core

/// QPE Backend Adapter Module
/// 
/// Bridges Quantum Phase Estimation to IQuantumBackend interface, enabling execution
/// on cloud quantum hardware (IonQ, Rigetti) in addition to local simulation.
/// 
/// Key responsibilities:
/// - Convert QPE configuration to quantum circuit gates
/// - Handle controlled-U^(2^k) gate synthesis
/// - Execute QPE via IQuantumBackend with inverse QFT
/// - Extract phase from measurement histogram
/// 
/// QPE Circuit Structure:
/// 1. Apply H gates to counting qubits (create superposition)
/// 2. For each counting qubit j:
///    - Apply controlled-U^(2^j) to target qubits
/// 3. Apply inverse QFT to counting register
/// 4. Measure counting qubits to extract phase
module QPEBackendAdapter =
    
    open FSharp.Azure.Quantum
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.Core.CircuitAbstraction
    open FSharp.Azure.Quantum.CircuitBuilder
    open FSharp.Azure.Quantum.Algorithms.QuantumPhaseEstimation
    open FSharp.Azure.Quantum.Algorithms.QFTBackendAdapter
    
    // ========================================================================
    // CONTROLLED UNITARY CIRCUIT SYNTHESIS
    // ========================================================================
    
    /// Add controlled-U^k gate to circuit
    /// Applies U to target qubits k times, controlled by single qubit
    let private addControlledUnitaryPower
        (controlQubit: int)
        (targetQubits: int[])  // Array of target qubits
        (unitary: UnitaryOperator)
        (power: int)
        (circuit: Circuit) : Circuit =
        
        // For phase gates (T, S, PhaseGate), use controlled-phase (CP) gate directly
        // Phase gate P(θ) = diag(1, e^(iθ)) applies phase only to |1⟩ state
        // 
        // In QPE: Controlled-P applies e^(iθ) when both control=|1⟩ AND target=|1⟩
        // This matches the reference implementation in QuantumPhaseEstimation.fs lines 136-157
        match unitary with
        | PhaseGate theta ->
            let totalTheta = float power * theta
            circuit |> addGate (CP(controlQubit, targetQubits[0], totalTheta))
        
        | TGate ->
            // T = P(π/4), so T^k = P(k·π/4)
            let totalTheta = float power * Math.PI / 4.0
            circuit |> addGate (CP(controlQubit, targetQubits[0], totalTheta))
        
        | SGate ->
            // S = P(π/2), so S^k = P(k·π/2)
            let totalTheta = float power * Math.PI / 2.0
            circuit |> addGate (CP(controlQubit, targetQubits[0], totalTheta))
        
        | RotationZ theta ->
            // Controlled-Rz gate: Apply power times
            [1 .. power]
            |> List.fold (fun currentCircuit _ ->
                currentCircuit
                |> addGate (CNOT(controlQubit, targetQubits[0]))
                |> addGate (RZ(targetQubits[0], theta))
                |> addGate (CNOT(controlQubit, targetQubits[0]))
            ) circuit
        
        | HamiltonianEvolution (hamiltonianObj, time, trotterSteps) ->
            // Controlled-exp(-iHt)^power
            // hamiltonian is stored as obj to avoid circular dependency; cast it back
            let hamiltonian = hamiltonianObj :?> TrotterSuzuki.PauliHamiltonian
            
            // Apply Trotter-Suzuki decomposition, repeated 'power' times, all controlled
            let trotterConfig = {
                TrotterSuzuki.NumSteps = trotterSteps
                TrotterSuzuki.Time = time * float power  // U^k means evolve for time*k
                TrotterSuzuki.Order = 1
            }
            
            // For now, simplified: just apply uncontrolled Trotter
            // TODO: Implement proper controlled-Trotter decomposition
            TrotterSuzuki.synthesizeHamiltonianEvolution hamiltonian trotterConfig targetQubits circuit
        
        | CustomUnitary _ ->
            failwith "Custom unitary operators not supported in backend execution"
    
    // ========================================================================
    // QPE CIRCUIT SYNTHESIS
    // ========================================================================
    
    /// Convert QPE configuration to quantum circuit
    /// 
    /// QPE algorithm:
    /// 1. Apply H to all counting qubits (first n qubits)
    /// 2. For j from 0 to n-1:
    ///    - Apply controlled-U^(2^j) with control=counting[j]
    /// 3. Apply inverse QFT to counting register
    /// 4. Measure counting qubits
    let qpeToCircuit (config: QPEConfig) : QuantumResult<Circuit> =
        try
            if config.CountingQubits <= 0 then
                Error (QuantumError.ValidationError ("CountingQubits", "must be positive"))
            elif config.TargetQubits <= 0 then
                Error (QuantumError.ValidationError ("TargetQubits", "must be positive"))
            elif config.CountingQubits > 20 then
                Error (QuantumError.ValidationError ("CountingQubits", "more than 20 is not practical"))
            else
                let totalQubits = config.CountingQubits + config.TargetQubits
                
                // Start with empty circuit
                let initialCircuit = empty totalQubits
                
                // Step 1: Apply Hadamard to counting qubits (first n qubits)
                let circuitWithHadamards =
                    [0 .. config.CountingQubits - 1]
                    |> List.fold (fun circuit i ->
                        addGate (H i) circuit
                    ) initialCircuit
                
                // Step 1.5: Prepare target qubits in eigenvector state
                // For phase gates (T, S, PhaseGate), the eigenvector is |1⟩
                let circuitWithEigenvector =
                    match config.EigenVector with
                    | None ->
                        // For single-qubit phase gates, put target in |1⟩ state
                        match config.UnitaryOperator with
                        | TGate | SGate | PhaseGate _ when config.TargetQubits = 1 ->
                            // Flip target qubit from |0⟩ to |1⟩
                            let targetQubit = config.CountingQubits
                            addGate (X targetQubit) circuitWithHadamards
                        | _ -> circuitWithHadamards
                    | Some _ ->
                        // Custom eigenvector preparation would go here
                        circuitWithHadamards
                
                // Step 2: Apply controlled-U^(2^j) gates
                let circuitWithControlledU =
                    [0 .. config.CountingQubits - 1]
                    |> List.fold (fun circuit j ->
                        let controlQubit = j
                        let targetQubits = [| config.CountingQubits .. config.CountingQubits + config.TargetQubits - 1 |]
                        let power = 1 <<< j  // 2^j
                        
                        addControlledUnitaryPower controlQubit targetQubits config.UnitaryOperator power circuit
                    ) circuitWithEigenvector
                
                // Step 3: Apply inverse QFT to counting register
                // We need to apply QFT only to the first n qubits (counting register)
                let qftConfig = {
                    QuantumFourierTransform.QFTConfig.NumQubits = config.CountingQubits
                    QuantumFourierTransform.QFTConfig.ApplySwaps = true
                    QuantumFourierTransform.QFTConfig.Inverse = true
                }
                
                match qftToCircuit qftConfig with
                | Error err -> Error (QuantumError.OperationError ("QFT circuit creation", $"Failed to create inverse QFT circuit: {err.Message}"))
                | Ok qftCircuit ->
                    // Add QFT gates to main circuit
                    let qftGates = getGates qftCircuit
                    let finalCircuit =
                        qftGates
                        |> List.fold (fun circuit gate ->
                            addGate gate circuit
                        ) circuitWithControlledU
                    
                    Ok finalCircuit
        with
        | ex -> Error (QuantumError.OperationError ("QPE circuit synthesis", ex.Message))
    
    // ========================================================================
    // BACKEND EXECUTION
    // ========================================================================
    
    /// Execute QPE on backend and extract phase from measurement histogram (async version)
    let executeWithBackendAsync 
        (config: QPEConfig)
        (backend: IQuantumBackend)
        (shots: int) : Async<Result<Map<int, int>, string>> = async {
        
        match qpeToCircuit config with
        | Error err -> return Error err.Message
        | Ok circuit ->
            try
                // Execute circuit on backend asynchronously
                let circuitWrapper = Core.CircuitAbstraction.CircuitWrapper(circuit)
                
                let! execResult = backend.ExecuteAsync circuitWrapper shots
                
                match execResult with
                | Error err -> return Error $"Backend execution failed: {err.Message}"
                | Ok execResult ->
                    // Extract measurement histogram
                    // Convert int[][] measurements to Map<int, int> histogram
                    let histogram =
                        execResult.Measurements
                        |> Array.map (fun bitstring ->
                            // Extract counting bits (first n qubits)
                            bitstring
                            |> Array.take config.CountingQubits
                            |> Array.indexed
                            |> Array.fold (fun acc (i, bit) -> acc + (bit <<< i)) 0
                        )
                        |> Array.countBy id
                        |> Map.ofArray
                    
                    return Ok histogram
            with
            | ex -> return Error $"Backend execution failed: {ex.Message}"
    }

    /// Execute QPE on backend and extract phase from measurement histogram (synchronous wrapper)
    /// 
    /// This is a synchronous wrapper around executeWithBackendAsync for backward compatibility.
    /// For cloud backends (IonQ, Rigetti), prefer using executeWithBackendAsync directly.
    let executeWithBackend 
        (config: QPEConfig)
        (backend: IQuantumBackend)
        (shots: int) : Result<Map<int, int>, string> =
        executeWithBackendAsync config backend shots |> Async.RunSynchronously
    
    /// Extract most likely phase from measurement histogram
    let extractPhaseFromHistogram (histogram: Map<int, int>) (precision: int) : float =
        // Find measurement outcome with highest count
        let mostLikelyOutcome = 
            histogram
            |> Map.toSeq
            |> Seq.maxBy snd
            |> fst
        
        // Convert to phase: φ = m / 2^n
        float mostLikelyOutcome / float (1 <<< precision)
    
    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================
    
    /// Estimate phase of T gate using backend
    let estimateTGatePhaseBackend 
        (countingQubits: int)
        (backend: IQuantumBackend)
        (shots: int) : QuantumResult<float> =
        
        let config = {
            CountingQubits = countingQubits
            TargetQubits = 1
            UnitaryOperator = TGate
            EigenVector = None
        }
        
        match executeWithBackend config backend shots with
        | Error err -> Error (QuantumError.BackendError ("T-gate phase estimation", err))
        | Ok histogram ->
            let phase = extractPhaseFromHistogram histogram countingQubits
            Ok phase
    
    /// Estimate phase of S gate using backend
    let estimateSGatePhaseBackend 
        (countingQubits: int)
        (backend: IQuantumBackend)
        (shots: int) : QuantumResult<float> =
        
        let config = {
            CountingQubits = countingQubits
            TargetQubits = 1
            UnitaryOperator = SGate
            EigenVector = None
        }
        
        match executeWithBackend config backend shots with
        | Error err -> Error (QuantumError.BackendError ("S-gate phase estimation", err))
        | Ok histogram ->
            let phase = extractPhaseFromHistogram histogram countingQubits
            Ok phase
    
    /// Estimate phase of general phase gate using backend
    let estimatePhaseGateBackend 
        (theta: float)
        (countingQubits: int)
        (backend: IQuantumBackend)
        (shots: int) : QuantumResult<float> =
        
        let config = {
            CountingQubits = countingQubits
            TargetQubits = 1
            UnitaryOperator = PhaseGate theta
            EigenVector = None
        }
        
        match executeWithBackend config backend shots with
        | Error err -> Error (QuantumError.BackendError ("Phase gate estimation", err))
        | Ok histogram ->
            let phase = extractPhaseFromHistogram histogram countingQubits
            Ok phase
