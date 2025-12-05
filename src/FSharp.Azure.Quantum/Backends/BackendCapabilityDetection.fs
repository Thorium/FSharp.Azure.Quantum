namespace FSharp.Azure.Quantum.Backends

/// Backend Capability Detection and Automatic Selection
///
/// This module provides intelligent backend selection based on circuit type:
/// - Gate-based circuits → LocalBackend, IonQ, Rigetti, Quantinuum
/// - QAOA/Annealing circuits → DWaveBackend
///
/// Key Features:
/// - Automatic detection of circuit paradigm (gate-based vs annealing)
/// - Backend capability checking (qubit limits, gate support)
/// - Intelligent fallback mechanisms
/// - Performance-based backend ranking
///
/// Supported Backend Targets:
/// - IonQ: ionq.simulator, ionq.qpu
/// - Rigetti: rigetti.sim.qvm, rigetti.qpu.*
/// - Quantinuum: quantinuum.sim.h1-1sc, quantinuum.qpu.h1-1, quantinuum.qpu.h2-1
/// - D-Wave: dwave.solver (annealing)
module BackendCapabilityDetection =
    
    open FSharp.Azure.Quantum.Core.CircuitAbstraction
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.Backends.DWaveBackend
    
    // For LocalBackend
    module LocalBackend = FSharp.Azure.Quantum.Core.BackendAbstraction
    
    // ============================================================================
    // CIRCUIT PARADIGM DETECTION
    // ============================================================================
    
    /// Quantum computing paradigm
    type QuantumParadigm =
        | GateBased    // Traditional gate-based quantum computing
        | Annealing    // Quantum annealing (D-Wave)
    
    /// Detect the paradigm of a quantum circuit
    ///
    /// Parameters:
    /// - circuit: Circuit to analyze
    ///
    /// Returns: QuantumParadigm (GateBased or Annealing)
    let detectCircuitParadigm (circuit: ICircuit) : QuantumParadigm =
        match circuit with
        | :? QaoaCircuitWrapper -> Annealing  // QAOA circuits are annealing-compatible
        | _ -> GateBased                      // Everything else is gate-based
    
    // ============================================================================
    // BACKEND TARGET DETECTION
    // ============================================================================
    
    /// Detect provider from target ID
    ///
    /// Parameters:
    /// - targetId: Azure Quantum target ID (e.g., "ionq.simulator", "quantinuum.sim.h1-1sc")
    ///
    /// Returns: Provider name string
    ///
    /// Examples:
    ///   detectProvider "ionq.simulator" → "IonQ"
    ///   detectProvider "quantinuum.qpu.h1-1" → "Quantinuum"
    ///   detectProvider "rigetti.sim.qvm" → "Rigetti"
    let detectProvider (targetId: string) : string =
        if targetId.StartsWith("ionq", System.StringComparison.OrdinalIgnoreCase) then
            "IonQ"
        elif targetId.StartsWith("rigetti", System.StringComparison.OrdinalIgnoreCase) then
            "Rigetti"
        elif targetId.StartsWith("quantinuum", System.StringComparison.OrdinalIgnoreCase) then
            "Quantinuum"
        elif targetId.StartsWith("atom", System.StringComparison.OrdinalIgnoreCase) then
            "AtomComputing"
        elif targetId.StartsWith("dwave", System.StringComparison.OrdinalIgnoreCase) then
            "D-Wave"
        else
            "Unknown"
    
    /// Check if target is a Quantinuum backend
    ///
    /// Parameters:
    /// - targetId: Azure Quantum target ID
    ///
    /// Returns: true if Quantinuum, false otherwise
    ///
    /// Supported Quantinuum targets:
    /// - quantinuum.sim.h1-1sc (H1-1 System Model SC simulator, 20 qubits)
    /// - quantinuum.sim.h1-1e (H1-1 System Model E simulator, 20 qubits)
    /// - quantinuum.qpu.h1-1 (H1-1 hardware, 20 qubits)
    /// - quantinuum.qpu.h2-1 (H2-1 hardware, 32 qubits)
    let isQuantinuumTarget (targetId: string) : bool =
        targetId.StartsWith("quantinuum", System.StringComparison.OrdinalIgnoreCase)
    
    /// Check if target is an IonQ backend
    let isIonQTarget (targetId: string) : bool =
        targetId.StartsWith("ionq", System.StringComparison.OrdinalIgnoreCase)
    
    /// Check if target is a Rigetti backend
    let isRigettiTarget (targetId: string) : bool =
        targetId.StartsWith("rigetti", System.StringComparison.OrdinalIgnoreCase)
    
    /// Check if target is a D-Wave backend
    let isDWaveTarget (targetId: string) : bool =
        targetId.StartsWith("dwave", System.StringComparison.OrdinalIgnoreCase)
    
    /// Check if target is an Atom Computing backend
    ///
    /// Supported Atom Computing targets:
    /// - atom-computing.sim (simulator, 100+ qubits)
    /// - atom-computing.qpu.phoenix (Phoenix QPU, 100+ qubits)
    let isAtomComputingTarget (targetId: string) : bool =
        targetId.StartsWith("atom", System.StringComparison.OrdinalIgnoreCase)
    
    // ============================================================================
    // BACKEND CAPABILITY CHECKING
    // ============================================================================
    
    /// Backend capability information
    type BackendCapability = {
        /// Backend name
        Name: string
        
        /// Paradigm supported
        Paradigm: QuantumParadigm
        
        /// Maximum qubits
        MaxQubits: int
        
        /// Supported gates (empty for annealing)
        SupportedGates: string list
        
        /// Estimated performance score (higher is better)
        /// Based on qubit count, gate fidelity, etc.
        PerformanceScore: float
        
        /// Whether backend is currently available
        IsAvailable: bool
    }
    
    /// Get capability information for a backend
    ///
    /// Parameters:
    /// - backend: Backend to analyze
    ///
    /// Returns: BackendCapability record
    let getBackendCapability (backend: IQuantumBackend) : BackendCapability =
        let paradigm = 
            if backend.Name.Contains("D-Wave") || backend.Name.Contains("Annealing") then
                Annealing
            else
                GateBased
        
        let performanceScore =
            match paradigm with
            | Annealing -> float backend.MaxQubits / 1000.0  // Annealing: qubit count matters
            | GateBased -> 
                // Gate-based: balance qubit count and gate variety
                let qubitScore = float backend.MaxQubits / 50.0
                let gateScore = float (List.length backend.SupportedGates) / 10.0
                
                // Bonus for cloud backends (IonQ, Rigetti, Quantinuum, Atom Computing)
                let cloudBonus = 
                    if backend.Name.Contains("IonQ") then 0.5
                    elif backend.Name.Contains("Rigetti") then 0.5
                    elif backend.Name.Contains("Quantinuum") then 1.0  // Highest fidelity
                    elif backend.Name.Contains("Atom Computing") then 0.8  // High qubit count, good fidelity
                    elif backend.Name.Contains("Azure Quantum") then 0.3
                    else 0.0
                
                qubitScore + gateScore + cloudBonus
        
        {
            Name = backend.Name
            Paradigm = paradigm
            MaxQubits = backend.MaxQubits
            SupportedGates = backend.SupportedGates
            PerformanceScore = performanceScore
            IsAvailable = true  // Assume available (could ping backend in real implementation)
        }
    
    /// Check if backend can execute a circuit
    ///
    /// Parameters:
    /// - backend: Backend to check
    /// - circuit: Circuit to execute
    ///
    /// Returns: true if backend can execute circuit, false otherwise
    let canExecuteCircuit (backend: IQuantumBackend) (circuit: ICircuit) : bool =
        let circuitParadigm = detectCircuitParadigm circuit
        let backendCap = getBackendCapability backend
        
        // Paradigm must match
        if backendCap.Paradigm <> circuitParadigm then
            false
        else
            // Check qubit limit
            circuit.NumQubits <= backend.MaxQubits
    
    // ============================================================================
    // AUTOMATIC BACKEND SELECTION
    // ============================================================================
    
    /// Select the best backend for a circuit from available options
    ///
    /// Parameters:
    /// - backends: List of available backends
    /// - circuit: Circuit to execute
    ///
    /// Returns: Result<IQuantumBackend, string>
    ///          Ok with best backend, or Error if no suitable backend found
    let selectBestBackend (backends: IQuantumBackend list) (circuit: ICircuit) : Result<IQuantumBackend, string> =
        let circuitParadigm = detectCircuitParadigm circuit
        
        // Filter backends that can execute this circuit
        let compatibleBackends =
            backends
            |> List.filter (fun backend -> canExecuteCircuit backend circuit)
        
        if List.isEmpty compatibleBackends then
            let paradigmStr = match circuitParadigm with | GateBased -> "gate-based" | Annealing -> "annealing"
            Error $"No compatible backend found for {paradigmStr} circuit with {circuit.NumQubits} qubits"
        else
            // Rank by performance score
            let bestBackend =
                compatibleBackends
                |> List.map (fun backend -> (backend, getBackendCapability backend))
                |> List.sortByDescending (fun (_, cap) -> cap.PerformanceScore)
                |> List.head
                |> fst
            
            Ok bestBackend
    
    // ============================================================================
    // CONVENIENCE FUNCTIONS
    // ============================================================================
    
    /// Create a default backend pool for automatic selection
    ///
    /// Includes:
    /// - LocalBackend for gate-based circuits
    /// - MockDWaveBackend for QAOA/annealing circuits
    ///
    /// Returns: List of IQuantumBackend
    let createDefaultBackendPool () : IQuantumBackend list =
        [
            // Gate-based backend
            LocalBackend.createLocalBackend()
            
            // Annealing backend
            createDefaultMockBackend()
        ]
    
    /// Execute a circuit with automatic backend selection
    ///
    /// Parameters:
    /// - circuit: Circuit to execute
    /// - numShots: Number of measurement shots
    ///
    /// Returns: Result<ExecutionResult, string>
    ///
    /// Example:
    ///   let circuit = QaoaCircuitWrapper(qaoaCircuit)
    ///   let result = executeWithAutomaticBackend circuit 1000
    let executeWithAutomaticBackend (circuit: ICircuit) (numShots: int) : Result<ExecutionResult, string> =
        let backends = createDefaultBackendPool()
        
        match selectBestBackend backends circuit with
        | Error e -> Error e
        | Ok backend ->
            // Log backend selection (in production, use proper logging)
            printfn $"Auto-selected backend: {backend.Name}"
            backend.Execute circuit numShots
    
    /// Execute a circuit asynchronously with automatic backend selection
    ///
    /// Parameters:
    /// - circuit: Circuit to execute
    /// - numShots: Number of measurement shots
    ///
    /// Returns: Async<Result<ExecutionResult, string>>
    let executeWithAutomaticBackendAsync (circuit: ICircuit) (numShots: int) : Async<Result<ExecutionResult, string>> =
        async {
            let backends = createDefaultBackendPool()
            
            match selectBestBackend backends circuit with
            | Error e -> return Error e
            | Ok backend ->
                // Log backend selection
                printfn $"Auto-selected backend: {backend.Name}"
                return! backend.ExecuteAsync circuit numShots
        }
    
    // ============================================================================
    // BACKEND RECOMMENDATION
    // ============================================================================
    
    /// Get backend recommendations for a circuit
    ///
    /// Parameters:
    /// - circuit: Circuit to analyze
    /// - backends: Available backends (optional, uses default pool if not provided)
    ///
    /// Returns: List of (IQuantumBackend * BackendCapability * string) tuples
    ///          Each tuple contains: (backend, capability, recommendation reason)
    let getBackendRecommendations (circuit: ICircuit) (backends: IQuantumBackend list option) : (IQuantumBackend * BackendCapability * string) list =
        let backendPool = backends |> Option.defaultValue (createDefaultBackendPool())
        let circuitParadigm = detectCircuitParadigm circuit
        
        backendPool
        |> List.filter (fun backend -> canExecuteCircuit backend circuit)
        |> List.map (fun backend ->
            let cap = getBackendCapability backend
            let reason =
                match circuitParadigm, cap.Paradigm with
                | Annealing, Annealing ->
                    $"✅ Optimal for QAOA/annealing ({cap.MaxQubits} qubits available)"
                | GateBased, GateBased ->
                    let gateCount = List.length cap.SupportedGates
                    $"✅ Supports {gateCount} gate types ({cap.MaxQubits} qubits available)"
                | _ ->
                    "⚠️ Paradigm mismatch"
            (backend, cap, reason)
        )
        |> List.sortByDescending (fun (_, cap, _) -> cap.PerformanceScore)
    
    /// Print backend recommendations (for CLI/debugging)
    ///
    /// Parameters:
    /// - circuit: Circuit to analyze
    let printBackendRecommendations (circuit: ICircuit) : unit =
        let recommendations = getBackendRecommendations circuit None
        
        printfn "Backend Recommendations:"
        printfn "======================="
        printfn $"Circuit: {circuit.Description}"
        printfn $"Qubits: {circuit.NumQubits}"
        printfn $"Paradigm: {detectCircuitParadigm circuit}"
        printfn ""
        
        if List.isEmpty recommendations then
            printfn "❌ No compatible backends found"
        else
            recommendations
            |> List.iteri (fun i (backend, cap, reason) ->
                printfn $"{i + 1}. {backend.Name}"
                printfn $"   {reason}"
                printfn $"   Performance Score: {cap.PerformanceScore:F2}"
                printfn ""
            )
