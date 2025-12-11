namespace FSharp.Azure.Quantum.Topological

open System
open System.Numerics
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Topological quantum backend implementing unified quantum backend interface
/// 
/// Features:
/// - Anyon-based quantum simulation using fusion trees
/// - Native FusionSuperposition representation (no gate compilation)
/// - Supports braiding operations, F-moves, and topological measurements
/// - Can compile gate-based circuits to braiding operations
/// - Efficient for topological codes and Clifford+T circuits
/// - Implements both IQuantumBackend and IQuantumBackend
/// 
/// Usage:
///   let backend = TopologicalUnifiedBackend(AnyonType.Ising, 20)
///   let! state = backend.ExecuteToState circuit  // Get quantum state
///   
///   // Braiding-based execution (no gate compilation)
///   let! initialState = backend.InitializeState 3
///   let! evolved = backend.ApplyOperation (QuantumOperation.Braid 0) initialState
module TopologicalUnifiedBackend =
    
    /// Topological quantum backend with unified interface
    type TopologicalUnifiedBackend(anyonType: AnyonSpecies.AnyonType, maxAnyons: int) =
        let mutable cancellationToken: CancellationToken option = None
        
        // Helper to convert TopologicalResult to Result with error message extraction
        let toResult (topResult: TopologicalResult<'T>) : Result<'T, string> =
            match topResult with
            | Ok value -> Ok value
            | Error err -> Error err.Message
        
        // ====================================================================
        // GATE COMPILATION VIA GateToBraid MODULE
        // ====================================================================
        
        /// Gate-to-braiding compilation using production-ready GateToBraid module
        /// 
        /// The TopologicalBackend now supports gate-based circuits through automatic
        /// compilation to braiding operations. This enables:
        /// - Running standard quantum algorithms (Grover, QFT, etc.) on topological hardware
        /// - Backend-agnostic algorithm implementation (same code works on Local and Topological)
        /// - Transparent gate-to-braiding translation with error tracking
        /// 
        /// Supported gates:
        /// - Clifford gates: H, X, Y, Z, S, S†, CNOT, CZ
        /// - Non-Clifford: T, T†, Rz(θ) (via Solovay-Kitaev approximation)
        /// - Multi-qubit gates (decomposed to single/two-qubit gates first)
        /// 
        /// The GateToBraid module handles:
        /// - Solovay-Kitaev algorithm for arbitrary rotations
        /// - Optimal Clifford synthesis
        /// - Braiding sequence optimization
        /// - Approximation error tracking
        /// 
        /// For best performance, use native braiding operations directly via
        /// ApplyOperation (QuantumOperation.Braid, QuantumOperation.FMove).
        
        /// Sample measurements from topological state (returns Result for proper error handling)
        let sampleMeasurements (state: TopologicalOperations.Superposition) (numQubits: int) (numShots: int) : Result<int[][], string> =
            // Convert to state vector for measurement sampling
            let stateInterface = TopologicalOperations.toInterface state
            let stateVector = QuantumStateConversion.convert QuantumStateType.GateBased (QuantumState.FusionSuperposition stateInterface)
            
            match stateVector with
            | QuantumState.StateVector sv ->
                Ok [| for _ in 1 .. numShots do
                        yield LocalSimulator.Measurement.measureAll sv
                    |]
            | _ ->
                // Conversion failed - propagate error
                Error $"Failed to convert FusionSuperposition to StateVector for measurement sampling (got {stateVector.GetType().Name})"
        
        /// Measure all qubits in FusionSuperposition state
        /// 
        /// Directly samples from topological superposition without conversion to StateVector.
        /// Uses TopologicalOperations.measureAll for native topological measurement.
        /// 
        /// Parameters:
        ///   state - QuantumState in FusionSuperposition form
        ///   shots - Number of measurement samples
        /// 
        /// Returns:
        ///   Array of bitstrings (int[][])
        let measureFusionState (state: QuantumState) (shots: int) : int[][] =
            match state with
            | QuantumState.FusionSuperposition fs ->
                // Use interface method directly (no cast needed)
                fs.MeasureAll shots
            | _ ->
                failwith $"Expected FusionSuperposition, got {state.GetType().Name}"
        
        /// Calculate probability of measuring specific bitstring in FusionSuperposition state
        /// 
        /// Parameters:
        ///   bitstring - Target bitstring [|b0; b1; ...; bn-1|]
        ///   state - QuantumState in FusionSuperposition form
        /// 
        /// Returns:
        ///   Probability ∈ [0, 1]
        let probabilityFusionState (bitstring: int[]) (state: QuantumState) : float =
            match state with
            | QuantumState.FusionSuperposition fs ->
                // Use interface method directly (no cast needed)
                fs.Probability bitstring
            | _ ->
                failwith $"Expected FusionSuperposition, got {state.GetType().Name}"
        
        // ====================================================================
        // Helper Functions for Operation Application
        // ====================================================================
        
        /// Apply braiding operation to fusion superposition
        member private this.ApplyBraid (anyonIndex: int) (fusionState: TopologicalOperations.Superposition) : Result<QuantumState, QuantumError> =
            let result = TopologicalOperations.braidSuperposition anyonIndex fusionState
            match toResult result with
            | Ok braided -> Ok (QuantumState.FusionSuperposition (TopologicalOperations.toInterface braided))
            | Error errMsg -> Error (QuantumError.OperationError ("TopologicalBackend", errMsg))
        
        /// Apply gate operation by compiling to braiding sequence
        member private this.ApplyGate (gate: CircuitBuilder.Gate) (fusionState: TopologicalOperations.Superposition) (numQubits: int) : Result<QuantumState, QuantumError> =
            let tolerance = 1e-10
            let numStrands = numQubits + 1
            
            match GateToBraid.compileGateToBraid gate numStrands tolerance with
            | Ok decomposition ->
                let braidIndices = 
                    decomposition.BraidSequence 
                    |> List.collect (fun bw -> bw.Generators)
                    |> List.map (fun gen -> gen.Index)
                
                let finalResult =
                    braidIndices
                    |> List.fold (fun stateResult braidIdx ->
                        stateResult |> Result.bind (fun currentState ->
                            TopologicalOperations.braidSuperposition braidIdx currentState
                            |> toResult
                            |> Result.mapError (fun err -> QuantumError.OperationError ("TopologicalBackend", err))
                        )
                    ) (Ok fusionState)
                
                match finalResult with
                | Ok finalState -> Ok (QuantumState.FusionSuperposition (TopologicalOperations.toInterface finalState))
                | Error err -> Error err
            
            | Error topErr -> 
                Error (QuantumError.OperationError ("TopologicalBackend", 
                    $"Failed to compile gate {gate} to braiding: {topErr.Message}"))
        
        /// Apply F-move operation
        member private this.ApplyFMove (direction: obj) (depth: int) (fusionState: TopologicalOperations.Superposition) : Result<QuantumState, QuantumError> =
            let fmoveDir = 
                match direction with
                | :? TopologicalOperations.FMoveDirection as dir -> dir
                | _ -> TopologicalOperations.FMoveDirection.LeftToRight
            
            let newTerms =
                fusionState.Terms
                |> List.collect (fun (amp, state) ->
                    let fmoveResult = TopologicalOperations.fMove fmoveDir depth state
                    fmoveResult.Terms |> List.map (fun (amp2, state2) -> (amp * amp2, state2))
                )
            
            let newSuperposition : TopologicalOperations.Superposition = {
                Terms = newTerms
                AnyonType = fusionState.AnyonType
            }
            
            Ok (QuantumState.FusionSuperposition (TopologicalOperations.toInterface newSuperposition))
        
        /// Apply measurement operation
        member private this.ApplyMeasure (anyonIndex: int) (fusionState: TopologicalOperations.Superposition) : Result<QuantumState, QuantumError> =
            try
                let newTerms =
                    fusionState.Terms
                    |> List.collect (fun (amp, state) ->
                        match TopologicalOperations.measureFusion anyonIndex state |> toResult with
                        | Ok outcomes ->
                            outcomes |> List.map (fun (prob, opResult) ->
                                let newAmp = amp * Complex(sqrt prob, 0.0)
                                (newAmp, opResult.State)
                            )
                        | Error _ -> []
                    )
                
                let newSuperposition : TopologicalOperations.Superposition = {
                    Terms = newTerms
                    AnyonType = fusionState.AnyonType
                }
                
                Ok (QuantumState.FusionSuperposition (TopologicalOperations.toInterface newSuperposition))
            with
            | ex -> Error (QuantumError.OperationError ("TopologicalBackend", ex.Message))
        
        // ====================================================================
        // IQuantumBackend Implementation
        // ====================================================================
        
        interface IQuantumBackend with
            
            member this.ApplyOperation (operation: QuantumOperation) (state: QuantumState) : Result<QuantumState, QuantumError> =
                match state with
                | QuantumState.FusionSuperposition fs ->
                    // Extract underlying Superposition from interface
                    match TopologicalOperations.fromInterface fs with
                    | None -> 
                        Error (QuantumError.ValidationError("state", "FusionSuperposition does not contain a valid Superposition"))
                    | Some fusionState ->
                        let numQubits = fs.LogicalQubits
                        
                        try
                            match operation with
                            | QuantumOperation.Braid anyonIndex ->
                                this.ApplyBraid anyonIndex fusionState
                            
                            | QuantumOperation.Gate gate ->
                                this.ApplyGate gate fusionState numQubits
                            
                            | QuantumOperation.FMove (direction, depth) ->
                                this.ApplyFMove direction depth fusionState
                            
                            | QuantumOperation.Measure anyonIndex ->
                                this.ApplyMeasure anyonIndex fusionState
                            
                            | QuantumOperation.Sequence ops ->
                                ops
                                |> List.fold (fun stateResult op ->
                                    match stateResult with
                                    | Error err -> Error err
                                    | Ok currentState ->
                                        (this :> IQuantumBackend).ApplyOperation op currentState
                                ) (Ok state)
                        with
                        | ex -> Error (QuantumError.OperationError ("TopologicalBackend", ex.Message))
                
                | _ ->
                    // State is not in native format - try conversion
                    let convertedState = QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state
                    match convertedState with
                    | QuantumState.FusionSuperposition fs ->
                        (this :> IQuantumBackend).ApplyOperation operation (QuantumState.FusionSuperposition fs)
                    | _ ->
                        Error (QuantumError.OperationError ("TopologicalBackend", "State conversion failed or returned non-fusion state"))
            
            member this.SupportsOperation (operation: QuantumOperation) : bool =
                match operation with
                | QuantumOperation.Braid _ -> true      // Native topological operation
                | QuantumOperation.FMove _ -> true      // Native topological operation  
                | QuantumOperation.Measure _ -> true    // Native topological measurement
                | QuantumOperation.Gate gate ->         // Gate compilation via GateToBraid
                    // Check if this specific gate can be compiled
                    // Most common gates (H, CNOT, T, S, RZ) are supported
                    // Returns false for gates that cannot be compiled to braiding
                    match GateToBraid.compileGateToBraid gate maxAnyons 1e-10 with
                    | Ok _ -> true
                    | Error _ -> false
                | QuantumOperation.Sequence ops ->
                    // Sequence supported if all operations are supported
                    ops |> List.forall (fun op -> (this :> IQuantumBackend).SupportsOperation op)
            
            member _.Name = "Topological Quantum Backend"
            
            member _.NativeStateType = QuantumStateType.TopologicalBraiding
            
            member this.ExecuteToState (circuit: ICircuit) : Result<QuantumState, QuantumError> =
                // Initialize state
                let initialResult = (this :> IQuantumBackend).InitializeState circuit.NumQubits
                
                match initialResult with
                | Error err -> Error err
                | Ok initialState ->
                    // Extract gates from circuit wrapper
                    let operations =
                        match circuit with
                        | :? CircuitWrapper as wrapper -> 
                            wrapper.Circuit.Gates |> List.map QuantumOperation.Gate
                        | _ -> 
                            []  // Empty circuit if not a CircuitWrapper
                    
                    // Apply each operation in sequence
                    operations
                    |> List.fold (fun stateResult operation ->
                        match stateResult with
                        | Error err -> Error err
                        | Ok currentState ->
                            (this :> IQuantumBackend).ApplyOperation operation currentState
                    ) (Ok initialState)
            
            member _.InitializeState (numQubits: int) : Result<QuantumState, QuantumError> =
                try
                    // Jordan-Wigner encoding: n qubits → n+1 anyonic strands
                    let numAnyons = numQubits + 1
                    
                    // Use Vacuum as identity particle (exists in all theories)
                    let vacuumParticle = AnyonSpecies.Vacuum
                    
                    // Create initial fusion tree: n anyons all in ground state
                    // For computational basis |0...0⟩
                    let initialTree =
                        List.replicate numAnyons vacuumParticle
                        |> List.map FusionTree.leaf
                        |> List.reduce (fun left right -> FusionTree.fuse left right vacuumParticle)
                    
                    let initialFusionState = FusionTree.create initialTree anyonType
                    let initialSuperposition = TopologicalOperations.pureState initialFusionState
                    
                    Ok (QuantumState.FusionSuperposition (TopologicalOperations.toInterface initialSuperposition))
                with
                | ex -> Error (QuantumError.OperationError ("TopologicalBackend", ex.Message))

/// Factory functions for creating topological backend instances
module TopologicalUnifiedBackendFactory =
    
    /// Create a new topological simulator backend
    let create (anyonType: AnyonSpecies.AnyonType) (maxAnyons: int) : TopologicalUnifiedBackend.TopologicalUnifiedBackend =
        TopologicalUnifiedBackend.TopologicalUnifiedBackend(anyonType, maxAnyons)
    
    /// Create and cast to IQuantumBackend
    let createUnified (anyonType: AnyonSpecies.AnyonType) (maxAnyons: int) : IQuantumBackend =
        create anyonType maxAnyons :> IQuantumBackend
    
    /// Create and cast to IQuantumBackend (for backward compatibility)
    let createStandard (anyonType: AnyonSpecies.AnyonType) (maxAnyons: int) : IQuantumBackend =
        create anyonType maxAnyons :> IQuantumBackend
    
    /// Create Ising anyon backend (most common)
    let createIsing (maxAnyons: int) : IQuantumBackend =
        createUnified AnyonSpecies.AnyonType.Ising maxAnyons
    
    /// Create Fibonacci anyon backend
    let createFibonacci (maxAnyons: int) : IQuantumBackend =
        createUnified AnyonSpecies.AnyonType.Fibonacci maxAnyons
