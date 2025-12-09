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
            let stateVector = QuantumStateConversion.convert QuantumStateType.GateBased (QuantumState.FusionSuperposition (state :> obj, numQubits))
            
            match stateVector with
            | QuantumState.StateVector sv ->
                Ok [| for _ in 1 .. numShots do
                        yield LocalSimulator.Measurement.measureAll sv
                    |]
            | _ ->
                // Conversion failed - propagate error
                Error $"Failed to convert FusionSuperposition to StateVector for measurement sampling (got {stateVector.GetType().Name})"
        
        // ====================================================================
        // IQuantumBackend Implementation
        // ====================================================================
        
        interface IQuantumBackend with
            member this.ExecuteToState (circuit: ICircuit) : Result<QuantumState, QuantumError> =
                // Convert gate-based circuit to operations and apply sequentially
                match box circuit with
                | :? CircuitAbstraction.CircuitWrapper as wrapper ->
                    let cbCircuit = wrapper.Circuit
                    
                    result {
                        // Initialize state
                        let! initialState = (this :> IQuantumBackend).InitializeState cbCircuit.QubitCount
                        
                        // Convert gates to QuantumOperation.Gate and apply sequentially
                        let gateOperations = cbCircuit.Gates |> List.map QuantumOperation.Gate
                        
                        // Apply all operations using ApplyOperation (handles gate-to-braiding compilation)
                        let! finalState =
                            gateOperations
                            |> List.fold (fun stateResult op ->
                                stateResult |> Result.bind (fun s -> 
                                    (this :> IQuantumBackend).ApplyOperation op s)
                            ) (Ok initialState)
                        
                        return finalState
                    }
                | _ ->
                    Error (QuantumError.ValidationError ("Circuit", 
                        "TopologicalBackend requires CircuitBuilder.Circuit wrapped in CircuitWrapper."))
            
            member _.NativeStateType = QuantumStateType.TopologicalBraiding
            
            member this.ApplyOperation (operation: QuantumOperation) (state: QuantumState) : Result<QuantumState, QuantumError> =
                match state with
                | QuantumState.FusionSuperposition (fs, numQubits) ->
                    // Cast obj to TopologicalOperations.Superposition
                    let fusionState = fs :?> TopologicalOperations.Superposition
                    
                    try
                        match operation with
                        | QuantumOperation.Braid anyonIndex ->
                            // Apply braiding directly using TopologicalOperations
                            let result = TopologicalOperations.braidSuperposition anyonIndex fusionState
                            
                            match toResult result with
                            | Ok braided -> Ok (QuantumState.FusionSuperposition (braided :> obj, numQubits))
                            | Error errMsg -> Error (QuantumError.OperationError ("TopologicalBackend", errMsg))
                        
                        | QuantumOperation.Gate gate ->
                            // Compile gate to braiding operations using production-ready compiler
                            let tolerance = 1e-10  // High precision for gate approximation
                            
                            // Jordan-Wigner encoding: n qubits → n+1 anyonic strands
                            let numStrands = numQubits + 1
                            
                            match GateToBraid.compileGateToBraid gate numStrands tolerance with
                            | Ok decomposition ->
                                // Apply each braiding operation sequentially
                                let braidIndices = 
                                    decomposition.BraidSequence 
                                    |> List.collect (fun bw -> bw.Generators)
                                    |> List.map (fun gen -> gen.Index)
                                
                                // Apply braids sequentially using fold
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
                                | Ok finalState -> Ok (QuantumState.FusionSuperposition (finalState :> obj, numQubits))
                                | Error err -> Error err
                            
                            | Error topErr -> 
                                Error (QuantumError.OperationError ("TopologicalBackend", 
                                    $"Failed to compile gate {gate} to braiding: {topErr.Message}"))
                        
                        | QuantumOperation.FMove (direction, depth) ->
                            // Apply F-move (basis transformation) to each term in superposition
                            // direction is obj type, needs casting
                            let fmoveDir = 
                                match direction with
                                | :? TopologicalOperations.FMoveDirection as dir -> dir
                                | _ -> TopologicalOperations.FMoveDirection.LeftToRight  // Default
                            
                            // Apply F-move to each basis state in the superposition
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
                            
                            Ok (QuantumState.FusionSuperposition (newSuperposition :> obj, numQubits))
                        
                        | QuantumOperation.Measure anyonIndex ->
                            // Measure fusion - measureFusion works on FusionTree.State, need to apply to each term
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
                                        | Error _ -> []  // Skip terms that can't be measured
                                    )
                                
                                let newSuperposition : TopologicalOperations.Superposition = {
                                    Terms = newTerms
                                    AnyonType = fusionState.AnyonType
                                }
                                
                                Ok (QuantumState.FusionSuperposition (newSuperposition :> obj, numQubits))
                            with
                            | ex -> Error (QuantumError.OperationError ("TopologicalBackend", ex.Message))
                        
                        | QuantumOperation.Sequence ops ->
                            // Apply operations sequentially
                            let result =
                                ops
                                |> List.fold (fun stateResult op ->
                                    match stateResult with
                                    | Error err -> Error err
                                    | Ok currentState ->
                                        (this :> IQuantumBackend).ApplyOperation op currentState
                                ) (Ok state)
                            result
                    with
                    | ex -> Error (QuantumError.OperationError ("TopologicalBackend", ex.Message))
                
                | _ ->
                    // State is not in native format - try conversion
                    let convertedState = QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state
                    match convertedState with
                    | QuantumState.FusionSuperposition (fs, numQubits) ->
                        (this :> IQuantumBackend).ApplyOperation operation (QuantumState.FusionSuperposition (fs, numQubits))
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
                    
                    Ok (QuantumState.FusionSuperposition (initialSuperposition :> obj, numQubits))
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
