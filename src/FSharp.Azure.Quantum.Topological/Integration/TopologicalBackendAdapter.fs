namespace FSharp.Azure.Quantum.Topological.Integration

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Topological

/// Adapter that makes topological backends compatible with the standard IQuantumBackend interface
/// 
/// This adapter bridges the gap between:
/// - Gate-based quantum computing (IQuantumBackend, CircuitBuilder.Circuit)
/// - Topological quantum computing (ITopologicalBackend, braiding operations)
/// 
/// Architecture:
/// 1. **Input**: ICircuit (gate-based circuit abstraction)
/// 2. **Compilation**: Gates → Braids using GateToBraid compiler
/// 3. **Execution**: Braids → Topological operations on ITopologicalBackend
/// 4. **Output**: ExecutionResult (measurement bitstrings compatible with gate-based backends)
/// 
/// Key Challenges:
/// - **Fusion outcomes → measurement bits**: Anyonic fusion channels are not direct 0/1 bits
/// - **Approximation error**: Some gates require Solovay-Kitaev approximation
/// - **Anyon encoding**: n qubits → n+1 anyonic strands (Jordan-Wigner encoding)
/// 
/// Example Usage:
/// ```fsharp
/// // Create topological backend (Ising anyons)
/// let topoBackend = TopologicalBackend.createSimulator AnyonType.Ising 20
/// 
/// // Wrap in adapter to get IQuantumBackend interface
/// let backend = TopologicalBackendAdapter(topoBackend) :> IQuantumBackend
/// 
/// // Now use with standard gate-based APIs
/// let circuit = CircuitBuilder.create 3 |> CircuitBuilder.h 0 |> CircuitBuilder.cnot 0 1
/// let! result = backend.ExecuteAsync (CircuitWrapper(circuit) :> ICircuit) 100
/// ```
module TopologicalBackendAdapter =
    
    /// Adapter implementing IQuantumBackend using ITopologicalBackend
    /// 
    /// Provides Level 2 integration: Gate-based circuits can run on topological hardware
    /// via automatic compilation (Gates → Braids → Topological execution)
    type TopologicalBackendAdapter(topoBackend: TopologicalBackend.ITopologicalBackend, ?anyonType: AnyonSpecies.AnyonType) =
        
        // Default to Ising anyons if not specified
        let anyonType = defaultArg anyonType AnyonSpecies.AnyonType.Ising
        
        // Thread-safe cancellation token storage
        let mutable cancellationToken : CancellationToken option = None
        let lockObj = obj()
        
        /// Convert fusion measurement outcomes to classical measurement bits
        /// 
        /// **CRITICAL MAPPING**: Anyonic fusion channels are NOT direct 0/1 bits!
        /// 
        /// For Ising anyons (σ × σ = 1 ⊕ ψ):
        /// - Fusion outcome "Vacuum" (identity) → measurement bit 0
        /// - Fusion outcome "Psi" (fermion) → measurement bit 1
        /// 
        /// For Fibonacci anyons (τ × τ = 1 ⊕ τ):
        /// - Fusion outcome "Vacuum" (identity) → measurement bit 0
        /// - Fusion outcome "Tau" → measurement bit 1
        /// 
        /// This mapping preserves the computational basis encoding for gate-based algorithms.
        let fusionOutcomeToBit (particle: AnyonSpecies.Particle) : int =
            match particle with
            | AnyonSpecies.Particle.Vacuum -> 0     // Vacuum/identity → |0⟩
            | AnyonSpecies.Particle.Psi -> 1        // Fermion (Ising) → |1⟩
            | AnyonSpecies.Particle.Tau -> 1        // Tau (Fibonacci) → |1⟩
            | AnyonSpecies.Particle.Sigma -> 0      // Sigma anyon (unpaired) → treat as |0⟩
            | AnyonSpecies.Particle.SpinJ _ -> 0    // SU(2) anyons → default to 0
        
        /// Compile a gate-based circuit to topological braiding operations
        /// 
        /// Uses GateToBraid compiler to transform CircuitBuilder.Gate list into BraidWord list.
        /// This is the key compilation step that enables gate-based algorithms to run on
        /// topological hardware.
        let compileCircuitToBraids (circuit: CircuitBuilder.Circuit) : Result<GateToBraid.GateSequenceCompilation, QuantumError> =
            try
                // Create BraidToGate.GateSequence from circuit
                let gateSequence : BraidToGate.GateSequence = {
                    Gates = circuit.Gates
                    NumQubits = circuit.QubitCount
                    TotalPhase = System.Numerics.Complex.One
                    Depth = 0  // Will be computed if needed
                    TCount = BraidToGate.countTGates circuit.Gates
                }
                
                // Compile gate sequence to braid sequence
                // Signature: compileGateSequence (gateSequence: BraidToGate.GateSequence) (tolerance: float) (anyonType: AnyonSpecies.AnyonType)
                let tolerance = 1e-10
                let compilationResult = GateToBraid.compileGateSequence gateSequence tolerance anyonType
                
                match compilationResult with
                | Ok compilation ->
                    // Check if approximation error is acceptable (< 1%)
                    if compilation.TotalError > 0.01 then
                        Error (QuantumError.OperationError(
                            "Gate compilation",
                            $"Approximation error {compilation.TotalError:F6} exceeds threshold 0.01. " +
                            "Consider using higher-fidelity gate decomposition or adjust tolerance."))
                    else
                        Ok compilation
                
                | Error topoError ->
                    // Convert TopologicalError to QuantumError
                    Error (QuantumError.OperationError("Gate-to-braid compilation", topoError.ToString()))
            
            with ex ->
                Error (QuantumError.OperationError("Gate compilation", $"Compilation failed: {ex.Message}"))
        
        /// Execute compiled braids on topological backend
        /// 
        /// Converts BraidWord list to TopologicalOperation list and executes on backend.
        /// Handles initialization, braiding, and measurement.
        let executeTopologicalOperations (numQubits: int) (braids: BraidGroup.BraidWord list) (numShots: int) : Task<Result<ExecutionResult, QuantumError>> =
            task {
                try
                    // Step 1: Initialize topological backend with correct number of anyons
                    // n qubits → n+1 anyonic strands (Jordan-Wigner encoding)
                    let numAnyons = numQubits + 1
                    
                    let! initResult = topoBackend.Initialize anyonType numAnyons
                    
                    match initResult with
                    | Error topoError ->
                        return Error (QuantumError.BackendError("Topological initialization", topoError.ToString()))
                    
                    | Ok initialState ->
                        // Step 2: Convert BraidWord list to TopologicalOperation list
                        // Each BraidWord contains a list of generators
                        let operations = 
                            braids
                            |> List.collect (fun braid ->
                                // Apply each generator in the braid word
                                braid.Generators
                                |> List.map (fun generator ->
                                    // Each generator has an Index (which strand to braid)
                                    TopologicalBackend.TopologicalOperation.Braid generator.Index
                                )
                            )
                        
                        // Add measurement operations for all qubits (adjacent fusion measurements)
                        // CRITICAL: Each measurement fuses 2 anyons into 1, reducing anyon count
                        // Example: 3 anyons → measure index 0 → 2 anyons → measure index 0 → 1 anyon
                        // Always measure at index 0 since fusion shifts remaining anyons left
                        let measurementOps = 
                            List.replicate numQubits (TopologicalBackend.TopologicalOperation.Measure 0)
                        
                        let allOps = operations @ measurementOps
                        
                        // Step 3: Execute all operations on topological backend
                        let! execResult = topoBackend.Execute initialState allOps
                        
                        match execResult with
                        | Error topoError ->
                            return Error (QuantumError.BackendError("Topological execution", topoError.ToString()))
                        
                        | Ok topoResult ->
                            // Step 4: Convert fusion outcomes to measurement bitstrings
                            // For now, we only have one shot (topological backends are deterministic for fusion)
                            // To get multiple shots, we need to repeat the entire execution
                            
                            // Extract fusion outcomes and convert to bits
                            let singleMeasurement =
                                topoResult.MeasurementOutcomes
                                |> List.map (fun (particle, _prob) -> fusionOutcomeToBit particle)
                                |> Array.ofList
                            
                            // Repeat for numShots (ideally, we'd re-run with quantum randomness)
                            // For now, return the same measurement repeated (deterministic)
                            let measurements = 
                                Array.init numShots (fun _ -> singleMeasurement)
                            
                            return Ok {
                                BackendName = "Topological Backend Adapter"
                                NumShots = numShots
                                Measurements = measurements
                                Metadata = 
                                    Map.empty
                                        .Add("anyon_type", box anyonType)
                                        .Add("approximation_error", box 0.0)  // Placeholder
                                        .Add("execution_time_ms", box topoResult.ExecutionTimeMs)
                                        .Add("topological_messages", box topoResult.Messages)
                            }
                
                with ex ->
                    return Error (QuantumError.BackendError("Topological adapter", $"Execution failed: {ex.Message}\n{ex.StackTrace}"))
            }
        
        /// Core execution logic (shared by Execute and ExecuteAsync)
        member private _.ExecuteCore (circuit: ICircuit) (numShots: int) : Task<Result<ExecutionResult, QuantumError>> =
            task {
                // Check cancellation before starting (thread-safe read)
                let ct = lock lockObj (fun () -> cancellationToken)
                match ct with
                | Some token when token.IsCancellationRequested ->
                    return Error (QuantumError.OperationError("Circuit execution", "Operation cancelled before execution"))
                | _ ->
                
                // Validate parameters
                if numShots <= 0 then
                    return Error (QuantumError.ValidationError("numShots", "Number of shots must be positive"))
                else
                    // Step 1: Extract CircuitBuilder.Circuit from ICircuit wrapper
                    let builderCircuitOpt = CircuitAdapter.tryGetCircuit circuit
                    
                    match builderCircuitOpt with
                    | None ->
                        return Error (QuantumError.ValidationError(
                            "circuit",
                            "TopologicalBackendAdapter requires CircuitWrapper or QaoaCircuitWrapper"))
                    
                    | Some builderCircuit ->
                        // Validate qubit count against backend capabilities
                        match topoBackend.Capabilities.MaxAnyons with
                        | Some maxAnyons when builderCircuit.QubitCount + 1 > maxAnyons ->
                            return Error (QuantumError.BackendError(
                                "Topological backend",
                                $"Circuit requires {builderCircuit.QubitCount + 1} anyons but backend supports max {maxAnyons}"))
                        | _ ->
                        
                        // Step 2: Compile gates to braids
                        match compileCircuitToBraids builderCircuit with
                        | Error err -> return Error err
                        | Ok compilation ->
                            // Check cancellation after compilation
                            let ct2 = lock lockObj (fun () -> cancellationToken)
                            match ct2 with
                            | Some token when token.IsCancellationRequested ->
                                return Error (QuantumError.OperationError("Circuit execution", "Operation cancelled during compilation"))
                            | _ ->
                            
                            // Step 3: Execute on topological backend
                            return! executeTopologicalOperations builderCircuit.QubitCount compilation.CompiledBraids numShots
            }
        
        interface IQuantumBackend with
            member _.SetCancellationToken(token) = 
                lock lockObj (fun () -> cancellationToken <- token)
            
            member this.ExecuteAsync (circuit: ICircuit) (numShots: int) : Async<Result<ExecutionResult, QuantumError>> =
                this.ExecuteCore circuit numShots |> Async.AwaitTask
            
            member this.Execute (circuit: ICircuit) (numShots: int) : Result<ExecutionResult, QuantumError> =
                this.ExecuteCore circuit numShots 
                |> Async.AwaitTask 
                |> Async.RunSynchronously
            
            member _.Name = 
                $"Topological Backend Adapter ({anyonType})"
            
            member _.SupportedGates = 
                // Topological backends support universal gate sets via Solovay-Kitaev
                // All gates can be approximated with braiding operations
                [
                    "H"; "X"; "Y"; "Z"
                    "S"; "T"; "SDG"; "TDG"
                    "RX"; "RY"; "RZ"
                    "P"  // Phase gate
                    "CNOT"; "CZ"
                    "SWAP"
                    "CCX"  // Toffoli
                ]
            
            member _.MaxQubits = 
                match topoBackend.Capabilities.MaxAnyons with
                | Some maxAnyons -> maxAnyons - 1  // n qubits → n+1 anyons
                | None -> 20  // Conservative default if no limit specified
    
    // ========================================================================
    // FACTORY FUNCTIONS
    // ========================================================================
    
    /// Create a topological backend adapter from an ITopologicalBackend
    /// 
    /// This is the primary way to integrate topological backends with gate-based APIs.
    /// 
    /// Parameters:
    /// - topoBackend: Topological backend implementation (simulator or hardware)
    /// - anyonType: Type of anyons to use (default: Ising)
    /// 
    /// Returns: IQuantumBackend that can be used with standard gate-based algorithms
    /// 
    /// Example:
    /// ```fsharp
    /// let topoBackend = TopologicalBackend.createSimulator AnyonType.Ising 20
    /// let backend = createAdapter topoBackend AnyonType.Ising
    /// ```
    let createAdapter (topoBackend: TopologicalBackend.ITopologicalBackend) (anyonType: AnyonSpecies.AnyonType) : IQuantumBackend =
        TopologicalBackendAdapter(topoBackend, anyonType) :> IQuantumBackend
    
    /// Create a topological backend adapter with Ising anyons (default)
    /// 
    /// Ising anyons are the simplest non-Abelian anyons and are sufficient for
    /// universal quantum computation with Clifford+T gates.
    /// 
    /// Parameters:
    /// - topoBackend: Topological backend implementation
    /// 
    /// Returns: IQuantumBackend configured for Ising anyons
    let createIsingAdapter (topoBackend: TopologicalBackend.ITopologicalBackend) : IQuantumBackend =
        createAdapter topoBackend AnyonSpecies.AnyonType.Ising
    
    /// Create a topological backend adapter with Fibonacci anyons
    /// 
    /// Fibonacci anyons are universal for quantum computation and require fewer
    /// anyons than Ising anyons for equivalent computational power.
    /// 
    /// Parameters:
    /// - topoBackend: Topological backend implementation
    /// 
    /// Returns: IQuantumBackend configured for Fibonacci anyons
    let createFibonacciAdapter (topoBackend: TopologicalBackend.ITopologicalBackend) : IQuantumBackend =
        createAdapter topoBackend AnyonSpecies.AnyonType.Fibonacci
