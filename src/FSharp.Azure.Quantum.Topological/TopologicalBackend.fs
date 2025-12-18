namespace FSharp.Azure.Quantum.Topological

open System
open System.Numerics
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.GroverSearch

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
        // Native Grover Primitives (no gate compilation)
        // ====================================================================

        let bitsToIntLsbFirst (bits: int[]) : int =
            bits
            |> Array.mapi (fun q b -> b <<< q)
            |> Array.sum

        let intToBitsLsbFirst (numQubits: int) (value: int) : int[] =
            [| for q in 0 .. numQubits - 1 -> (value >>> q) &&& 1 |]

        let negateMarkedTerms (oracle: Oracle.CompiledOracle) (fusionState: TopologicalOperations.Superposition) : TopologicalOperations.Superposition =
            let newTerms =
                fusionState.Terms
                |> List.map (fun (amp, st) ->
                    let bits = FusionTree.toComputationalBasis st.Tree |> List.toArray
                    let x = bitsToIntLsbFirst bits
                    if Oracle.isSolution oracle.Spec x then
                        (-amp, st)
                    else
                        (amp, st))

            { fusionState with Terms = newTerms }

        let applyDiffusionOnTerms (numQubits: int) (fusionState: TopologicalOperations.Superposition) : TopologicalOperations.Superposition =
            // Implement diffusion in the *computational basis* by mapping measurement outcomes
            // to amplitudes, applying: a_x' = (2*mean - a_x), and then re-encoding.
            //
            // This is a pure algorithmic reflection and does not require gate compilation.
            // It assumes terms correspond to computational basis states produced by the encoding.

            let combined = TopologicalOperations.combineLikeTerms fusionState

            // Aggregate amplitudes by computational basis index.
            let ampByBasis =
                combined.Terms
                |> List.fold (fun acc (amp, st) ->
                    let bits = FusionTree.toComputationalBasis st.Tree |> List.toArray
                    let x = bitsToIntLsbFirst bits
                    let existing = acc |> Map.tryFind x |> Option.defaultValue Complex.Zero
                    acc |> Map.add x (existing + amp)
                ) Map.empty

            let dim = 1 <<< numQubits

            // Compute mean amplitude across all 2^n computational basis states.
            let sumAmp =
                [ 0 .. dim - 1 ]
                |> List.fold (fun acc x ->
                    let a = ampByBasis |> Map.tryFind x |> Option.defaultValue Complex.Zero
                    acc + a
                ) Complex.Zero

            let meanAmp = sumAmp / Complex(float dim, 0.0)

            // Produce new terms by reflecting each basis amplitude about the mean.
            let newTerms =
                [ 0 .. dim - 1 ]
                |> List.choose (fun x ->
                    let a = ampByBasis |> Map.tryFind x |> Option.defaultValue Complex.Zero
                    let reflected = (meanAmp * Complex(2.0, 0.0)) - a
                    if Complex.Abs reflected <= 1e-14 then
                        None
                    else
                        let bits = intToBitsLsbFirst numQubits x |> Array.toList
                        let tree = FusionTree.fromComputationalBasis bits anyonType
                        let state = FusionTree.create tree anyonType
                        Some (reflected, state))

            let diffused : TopologicalOperations.Superposition =
                { Terms = newTerms; AnyonType = fusionState.AnyonType }

            diffused
            |> TopologicalOperations.normalize

        // ====================================================================
        // Helper Functions for Operation Application
        // ====================================================================
        
        /// Apply braiding operation to fusion superposition
        member private this.ApplyBraid (anyonIndex: int) (fusionState: TopologicalOperations.Superposition) : Result<QuantumState, QuantumError> =
            let result = TopologicalOperations.braidSuperposition anyonIndex fusionState
            match toResult result with
            | Ok braided -> Ok (QuantumState.FusionSuperposition (TopologicalOperations.toInterface braided))
            | Error errMsg -> Error (QuantumError.OperationError ("TopologicalBackend", errMsg))
        
        /// Apply gate operation by compiling to a braiding sequence.
        ///
        /// Note: We compile via `compileGateSequence` (not `compileGateToBraid`) so that
        /// complex gates like CP/CRZ/SWAP are transpiled first.
        member private this.ApplyGate (gate: CircuitBuilder.Gate) (fusionState: TopologicalOperations.Superposition) (numQubits: int) : Result<QuantumState, QuantumError> =
            let tolerance = 1e-10

            let gateSequence: BraidToGate.GateSequence =
                { NumQubits = numQubits
                  Gates = [ gate ]
                  TotalPhase = Complex.One
                  Depth = 1
                  TCount = 0 }

            match GateToBraid.compileGateSequence gateSequence tolerance anyonType with
            | Ok compilation ->
                let braidSteps =
                    compilation.CompiledBraids
                    |> List.collect (fun bw -> bw.Generators)
                    |> List.map (fun gen -> (gen.Index, gen.IsClockwise))

                let finalResult =
                    braidSteps
                    |> List.fold (fun stateResult (braidIdx, isClockwise) ->
                        stateResult |> Result.bind (fun currentState ->
                            TopologicalOperations.braidSuperpositionDirected braidIdx isClockwise currentState
                            |> toResult
                            |> Result.mapError (fun err -> QuantumError.OperationError ("TopologicalBackend", err))
                        )
                    ) (Ok fusionState)

                match finalResult with
                | Ok finalState -> Ok (QuantumState.FusionSuperposition (TopologicalOperations.toInterface finalState))
                | Error err -> Error err

            | Error topErr ->
                Error (QuantumError.OperationError ("TopologicalBackend", $"Failed to compile gate {gate} to braiding: {topErr.Message}"))
        
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
                            | QuantumOperation.Algorithm (AlgorithmOperation.QFT intent) ->
                                // QFT intent execution for the topological model.
                                // Currently implemented as explicit lowering to gate operations.
                                let qftOps =
                                    let applyQftStepOps targetQubit =
                                        let hOp = QuantumOperation.Gate (CircuitBuilder.H targetQubit)
                                        let phases =
                                            [targetQubit + 1 .. intent.NumQubits - 1]
                                            |> List.map (fun k ->
                                                let power = k - targetQubit + 1
                                                let angle = 2.0 * Math.PI / float (1 <<< power)
                                                let angle = if intent.Inverse then -angle else angle
                                                QuantumOperation.Gate (CircuitBuilder.CP (k, targetQubit, angle)))
                                        hOp :: phases

                                    let qftSequence =
                                        [0 .. intent.NumQubits - 1]
                                        |> List.collect applyQftStepOps

                                    let swapSequence =
                                        if intent.ApplySwaps then
                                            [0 .. intent.NumQubits / 2 - 1]
                                            |> List.map (fun i ->
                                                let j = intent.NumQubits - 1 - i
                                                QuantumOperation.Gate (CircuitBuilder.SWAP (i, j)))
                                        else
                                            []

                                    qftSequence @ swapSequence

                                (this :> IQuantumBackend).ApplyOperation (QuantumOperation.Sequence qftOps) state

                            | QuantumOperation.Algorithm (AlgorithmOperation.GroverPrepare numQubits) ->
                                // Build |s⟩ over computational basis.
                                try
                                    let dim = 1 <<< numQubits

                                    let states =
                                        [ 0 .. dim - 1 ]
                                        |> List.map (fun x ->
                                            let bits = intToBitsLsbFirst numQubits x |> Array.toList
                                            let tree = FusionTree.fromComputationalBasis bits anyonType
                                            FusionTree.create tree anyonType)

                                    let superposition =
                                        TopologicalOperations.uniform states anyonType
                                        |> TopologicalOperations.normalize

                                    Ok (QuantumState.FusionSuperposition (TopologicalOperations.toInterface superposition))
                                with
                                | ex -> Error (QuantumError.OperationError ("TopologicalBackend", ex.Message))

                            | QuantumOperation.Algorithm (AlgorithmOperation.GroverOraclePhaseFlip groverIntent) ->
                                // Negate marked computational basis terms.
                                let compiledOracle : Oracle.CompiledOracle =
                                    { Spec = Oracle.OracleSpec.Predicate groverIntent.IsMarked
                                      NumQubits = groverIntent.NumQubits
                                      LocalSimulation = id
                                      ExpectedSolutions = None }

                                let newState = negateMarkedTerms compiledOracle fusionState
                                Ok (QuantumState.FusionSuperposition (TopologicalOperations.toInterface newState))

                            | QuantumOperation.Algorithm (AlgorithmOperation.GroverDiffusion numQubits) ->
                                 let diffused = applyDiffusionOnTerms numQubits fusionState
                                 Ok (QuantumState.FusionSuperposition (TopologicalOperations.toInterface diffused))

                             | QuantumOperation.Algorithm (AlgorithmOperation.QPE intent) ->
                                 // Execute QPE intent by lowering to gate operations.
                                 //
                                 // Note: `intent.ApplySwaps` controls whether the final bit-reversal SWAPs
                                 // are applied. QPE can omit swaps and undo bit order classically.
                                 if intent.CountingQubits <= 0 then
                                     Error (QuantumError.ValidationError ("CountingQubits", "must be positive"))
                                 elif intent.TargetQubits <> 1 then
                                     Error (QuantumError.ValidationError ("TargetQubits", "only TargetQubits = 1 is supported by QPE intent"))
                                 elif QuantumState.numQubits state <> (intent.CountingQubits + intent.TargetQubits) then
                                     Error (QuantumError.ValidationError ("state", "state qubit count does not match QPE intent"))
                                 else
                                     let targetQubit = intent.CountingQubits
 
                                     let hadamardOps =
                                         [0 .. intent.CountingQubits - 1]
                                         |> List.map (fun q -> QuantumOperation.Gate (CircuitBuilder.H q))
 
                                     let eigenPrepOps =
                                         if intent.PrepareTargetOne then
                                             [ QuantumOperation.Gate (CircuitBuilder.X targetQubit) ]
                                         else
                                             []
 
                                     let controlledOps =
                                         [0 .. intent.CountingQubits - 1]
                                         |> List.map (fun j ->
                                             let applications = 1 <<< j
 
                                             match intent.Unitary with
                                             | QpeUnitary.PhaseGate theta ->
                                                 let totalTheta = float applications * theta
                                                 QuantumOperation.Gate (CircuitBuilder.CP (j, targetQubit, totalTheta))
                                             | QpeUnitary.TGate ->
                                                 let totalTheta = float applications * Math.PI / 4.0
                                                 QuantumOperation.Gate (CircuitBuilder.CP (j, targetQubit, totalTheta))
                                             | QpeUnitary.SGate ->
                                                 let totalTheta = float applications * Math.PI / 2.0
                                                 QuantumOperation.Gate (CircuitBuilder.CP (j, targetQubit, totalTheta))
                                             | QpeUnitary.RotationZ theta ->
                                                 let totalTheta = float applications * theta
                                                 QuantumOperation.Gate (CircuitBuilder.CRZ (j, targetQubit, totalTheta)))
 
                                     let inverseQftOps =
                                         [(intent.CountingQubits - 1) .. -1 .. 0]
                                         |> List.collect (fun tq ->
                                             let phases =
                                                 [tq + 1 .. intent.CountingQubits - 1]
                                                 |> List.map (fun k ->
                                                     let power = k - tq + 1
                                                     let angle = -2.0 * Math.PI / float (1 <<< power)
                                                     QuantumOperation.Gate (CircuitBuilder.CP (k, tq, angle)))
                                             let h = QuantumOperation.Gate (CircuitBuilder.H tq)
                                             phases @ [ h ])
 
                                     let swapOps =
                                         if intent.ApplySwaps then
                                             [0 .. intent.CountingQubits / 2 - 1]
                                             |> List.map (fun i ->
                                                 let j = intent.CountingQubits - 1 - i
                                                 QuantumOperation.Gate (CircuitBuilder.SWAP (i, j)))
                                         else
                                             []
 
                                     let ops = hadamardOps @ eigenPrepOps @ controlledOps @ inverseQftOps @ swapOps
                                     (this :> IQuantumBackend).ApplyOperation (QuantumOperation.Sequence ops) state

                             | QuantumOperation.Algorithm (AlgorithmOperation.HHL intent) ->
                                 // Educational HHL intent: diagonal-only, encoded as a single ancilla rotation.
                                 let totalQubits = intent.EigenvalueQubits + intent.SolutionQubits + 1
                                 if QuantumState.numQubits state <> totalQubits then
                                     Error (QuantumError.ValidationError ("state", $"Expected {totalQubits} qubits for HHL intent, got {QuantumState.numQubits state}"))
                                 elif intent.DiagonalEigenvalues.Length <> (1 <<< intent.SolutionQubits) then
                                     Error (QuantumError.ValidationError ("DiagonalEigenvalues", $"Expected {1 <<< intent.SolutionQubits} eigenvalues for HHL intent, got {intent.DiagonalEigenvalues.Length}"))
                                 else
                                     let eigenvalue = intent.DiagonalEigenvalues[0]
                                     if abs eigenvalue < intent.MinEigenvalue then
                                         Error (QuantumError.ValidationError ("eigenvalue", $"too small: {eigenvalue}"))
                                     else
                                         let clampToUnit x =
                                             if x > 1.0 then 1.0
                                             elif x < -1.0 then -1.0
                                             else x

                                         let invLambda =
                                             match intent.InversionMethod with
                                             | HhlEigenvalueInversionMethod.ExactRotation c
                                             | HhlEigenvalueInversionMethod.LinearApproximation c -> c / eigenvalue
                                             | HhlEigenvalueInversionMethod.PiecewiseLinear segments ->
                                                 let absLambda = abs eigenvalue
                                                 let constant =
                                                     segments
                                                     |> Array.tryFind (fun (minL, maxL, _) -> absLambda >= minL && absLambda < maxL)
                                                     |> Option.map (fun (_, _, c) -> c)
                                                     |> Option.defaultValue 1.0
                                                 constant / eigenvalue

                                         let theta = 2.0 * Math.Asin(clampToUnit invLambda)
                                         let ancillaQubit = intent.EigenvalueQubits + intent.SolutionQubits
                                         (this :> IQuantumBackend).ApplyOperation (QuantumOperation.Gate (CircuitBuilder.RY (ancillaQubit, theta))) state
                              
                              | QuantumOperation.Braid anyonIndex ->


                                this.ApplyBraid anyonIndex fusionState
                            
                            | QuantumOperation.Gate gate ->
                                this.ApplyGate gate fusionState numQubits

                            | QuantumOperation.Extension ext ->
                                match ext with
                                | :? ILowerToOperationsExtension as lowerable ->
                                    let ops =
                                        lowerable.LowerToGates()
                                        |> List.map QuantumOperation.Gate
                                    (this :> IQuantumBackend).ApplyOperation (QuantumOperation.Sequence ops) state
                                | _ ->
                                    Error (QuantumError.OperationError ("TopologicalBackend", $"Extension operation '{ext.Id}' is not supported"))
                            
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
                | QuantumOperation.Algorithm (AlgorithmOperation.QFT _) -> true
                | QuantumOperation.Algorithm (AlgorithmOperation.QPE _) -> true
                | QuantumOperation.Algorithm (AlgorithmOperation.HHL _) -> true
                | QuantumOperation.Algorithm (AlgorithmOperation.GroverPrepare _)
                | QuantumOperation.Algorithm (AlgorithmOperation.GroverOraclePhaseFlip _)
                | QuantumOperation.Algorithm (AlgorithmOperation.GroverDiffusion _) -> true

                | QuantumOperation.Braid _ -> true      // Native topological operation
                | QuantumOperation.FMove _ -> true      // Native topological operation
                | QuantumOperation.Measure _ -> true    // Native topological measurement
                | QuantumOperation.Extension ext ->
                    match ext with
                    | :? ILowerToOperationsExtension -> true
                    | _ -> false
                | QuantumOperation.Sequence ops ->
                    // Sequence supported if all operations are supported
                    ops |> List.forall (fun op -> (this :> IQuantumBackend).SupportsOperation op)
                | QuantumOperation.Gate gate ->         // Gate compilation via GateToBraid
                    // Check if this specific gate can be compiled.
                    // Compilation expects the *logical qubit count* for the circuit that contains the gate,
                    // not the backend's anyon limit.
                    let requiredQubits =
                        match gate with
                        | CircuitBuilder.X q
                        | CircuitBuilder.Y q
                        | CircuitBuilder.Z q
                        | CircuitBuilder.H q
                        | CircuitBuilder.S q
                        | CircuitBuilder.SDG q
                        | CircuitBuilder.T q
                        | CircuitBuilder.TDG q
                        | CircuitBuilder.P (q, _)
                        | CircuitBuilder.RX (q, _)
                        | CircuitBuilder.RY (q, _)
                        | CircuitBuilder.RZ (q, _)
                        | CircuitBuilder.Measure q -> q + 1
                        | CircuitBuilder.U3 (q, _, _, _) -> q + 1
                        | CircuitBuilder.CNOT (c, t)
                        | CircuitBuilder.CZ (c, t)
                        | CircuitBuilder.CP (c, t, _)
                        | CircuitBuilder.CRX (c, t, _)
                        | CircuitBuilder.CRY (c, t, _)
                        | CircuitBuilder.CRZ (c, t, _)
                        | CircuitBuilder.SWAP (c, t) -> max c t + 1
                        | CircuitBuilder.CCX (c1, c2, t) -> max c1 (max c2 t) + 1
                        | CircuitBuilder.MCZ (controls, target) ->
                            (target :: controls) |> List.max |> fun q -> q + 1

                    try
                        let requiredAnyons =
                            FusionTree.fromComputationalBasis (List.replicate requiredQubits 0) anyonType
                            |> FusionTree.size

                        if requiredAnyons > maxAnyons then
                            false
                        else
                            match GateToBraid.compileGateToBraid gate requiredQubits 1e-10 with
                            | Ok _ -> true
                            | Error _ -> false
                    with
                    | _ -> false
            
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
                    // Initialize to computational basis |0...0⟩ using FusionTree encoding.
                    // For Ising encoding we include an extra σ-pair parity ancilla.
                    let initialTree = FusionTree.fromComputationalBasis (List.replicate numQubits 0) anyonType
                    let requiredAnyons = FusionTree.size initialTree

                    if requiredAnyons > maxAnyons then
                        Error (QuantumError.ValidationError ("numQubits", $"Requested {numQubits} logical qubits requires {requiredAnyons} anyons, but backend maxAnyons is {maxAnyons}"))
                    else
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
