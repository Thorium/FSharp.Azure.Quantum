namespace FSharp.Azure.Quantum.Backends

open System
open System.Numerics
open System.Threading
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.LocalSimulator

/// Local quantum simulator backend implementing unified quantum backend interface
/// 
/// Features:
/// - Gate-based quantum simulation using state vectors
/// - Native StateVector representation (no conversion needed)
/// - Supports all standard gates (H, X, Y, Z, RX, RY, RZ, CNOT, CZ, etc.)
/// - Efficient for circuits up to 30 qubits (practical limit ~20 qubits depending on available memory)
/// - Implements both IQuantumBackend and IQuantumBackend
/// 
/// Usage:
///   let backend = LocalBackend()
///   let! state = backend.ExecuteToState circuit  // Get quantum state
///   let! result = backend.Execute circuit 1000   // Get measurements
///   
///   // State-based execution
///   let! initialState = backend.InitializeState 3
///   let! evolved = backend.ApplyOperation (QuantumOperation.Gate (CircuitBuilder.H 0)) initialState
module LocalBackend =
    
    /// Local simulator backend
    type LocalBackend() =
        
        // ====================================================================
        // HELPER: Gate Execution on StateVector
        // ====================================================================
        
        /// Apply single gate to state vector
        let applyGate (gate: CircuitBuilder.Gate) (state: StateVector.StateVector) : StateVector.StateVector =
            match gate with
            // Single-qubit gates - Pauli
            | CircuitBuilder.H q -> Gates.applyH q state
            | CircuitBuilder.X q -> Gates.applyX q state
            | CircuitBuilder.Y q -> Gates.applyY q state
            | CircuitBuilder.Z q -> Gates.applyZ q state
            
            // Single-qubit gates - Phase
            | CircuitBuilder.S q -> Gates.applyS q state
            | CircuitBuilder.SDG q -> Gates.applySDG q state
            | CircuitBuilder.T q -> Gates.applyT q state
            | CircuitBuilder.TDG q -> Gates.applyTDG q state
            | CircuitBuilder.P (q, angle) -> Gates.applyP q angle state
            
            // Single-qubit gates - Rotation
            | CircuitBuilder.RX (q, angle) -> Gates.applyRx q angle state
            | CircuitBuilder.RY (q, angle) -> Gates.applyRy q angle state
            | CircuitBuilder.RZ (q, angle) -> Gates.applyRz q angle state
            
            // Single-qubit gates - Universal
            | CircuitBuilder.U3 (q, theta, phi, lambda) ->
                // U3(θ,φ,λ) = Rz(φ)·Ry(θ)·Rz(λ)
                state
                |> Gates.applyRz q lambda
                |> Gates.applyRy q theta
                |> Gates.applyRz q phi
            
            // Two-qubit gates - Standard
            | CircuitBuilder.CNOT (ctrl, target) -> Gates.applyCNOT ctrl target state
            | CircuitBuilder.CZ (ctrl, target) -> Gates.applyCZ ctrl target state
            | CircuitBuilder.SWAP (q1, q2) -> Gates.applySWAP q1 q2 state
            
            // Two-qubit gates - Controlled (proper implementations)
            | CircuitBuilder.CP (ctrl, target, angle) ->
                Gates.applyCP ctrl target angle state
            | CircuitBuilder.CRX (ctrl, target, angle) ->
                Gates.applyCRX ctrl target angle state
            | CircuitBuilder.CRY (ctrl, target, angle) ->
                Gates.applyCRY ctrl target angle state
            | CircuitBuilder.CRZ (ctrl, target, angle) ->
                Gates.applyCRZ ctrl target angle state
            
            // Three-qubit gates
            | CircuitBuilder.CCX (ctrl1, ctrl2, target) ->
                Gates.applyCCX ctrl1 ctrl2 target state
            
            // Multi-qubit gates
            | CircuitBuilder.MCZ (controls, target) ->
                Gates.applyMultiControlledZ controls target state
            
            // Measurement - should not appear in circuit execution
            | CircuitBuilder.Measure q ->
                failwith "Measurement gates should be handled separately"
        
        /// Execute circuit on state vector
        let executeCircuit (circuit: CircuitBuilder.Circuit) (numQubits: int) : Result<StateVector.StateVector, QuantumError> =
            try
                // Initialize to |0⟩^⊗n
                let initialState = StateVector.init numQubits
                
                // Apply gates sequentially
                let finalState =
                    circuit.Gates
                    |> List.fold (fun state gate ->
                        applyGate gate state
                    ) initialState
                
                Ok finalState
            with
            | :? System.OperationCanceledException ->
                Error (QuantumError.OperationError ("LocalBackend", "Execution was cancelled"))
            | ex ->
                Error (QuantumError.OperationError ("LocalBackend", ex.Message))
        
        /// Sample measurements from state vector
        let sampleMeasurements (state: StateVector.StateVector) (numShots: int) : int[][] =
            [| for _ in 1 .. numShots do
                yield Measurement.measureAll state
            |]
        
        // ====================================================================
        // IQuantumBackend Implementation
        // ====================================================================
        
        interface IQuantumBackend with
            member _.Name = "Local Simulator"
            
            member this.ExecuteToState (circuit: CircuitAbstraction.ICircuit) : Result<QuantumState, QuantumError> =
                let numQubits = circuit.NumQubits
                
                // Pattern match on specific circuit wrapper types to extract gates
                match box circuit with
                | :? CircuitAbstraction.CircuitWrapper as wrapper ->
                    // Extract CircuitBuilder.Circuit and apply gates
                    let cbCircuit = wrapper.Circuit
                    try
                        let initialState = StateVector.init numQubits
                        let finalState =
                            cbCircuit.Gates
                            |> List.fold (fun state gate ->
                                applyGate gate state
                            ) initialState
                        Ok (QuantumState.StateVector finalState)
                    with
                    | :? System.OperationCanceledException ->
                        Error (QuantumError.OperationError ("LocalBackend", "Execution was cancelled"))
                    | ex ->
                        Error (QuantumError.OperationError ("LocalBackend", ex.Message))
                
                | :? CircuitAbstraction.QaoaCircuitWrapper as qaoaWrapper ->
                    // Convert QaoaCircuit to CircuitBuilder.Circuit and execute
                    let qaoaCircuit = qaoaWrapper.QaoaCircuit
                    let cbCircuit = CircuitAbstraction.CircuitAdapter.qaoaCircuitToCircuit qaoaCircuit
                    try
                        let initialState = StateVector.init numQubits
                        let finalState =
                            cbCircuit.Gates
                            |> List.fold (fun state gate ->
                                applyGate gate state
                            ) initialState
                        Ok (QuantumState.StateVector finalState)
                    with
                    | :? System.OperationCanceledException ->
                        Error (QuantumError.OperationError ("LocalBackend", "Execution was cancelled"))
                    | ex ->
                        Error (QuantumError.OperationError ("LocalBackend", ex.Message))
                
                | _ ->
                    // For unknown circuit types, cannot execute directly
                    Error (QuantumError.OperationError ("LocalBackend", $"Circuit type {circuit.GetType().Name} not supported by LocalBackend.ExecuteToState - wrap with CircuitWrapper or QaoaCircuitWrapper"))
            
            member _.NativeStateType = QuantumStateType.GateBased
            
            member self.ApplyOperation (operation: QuantumOperation) (state: QuantumState) : Result<QuantumState, QuantumError> =
                match state with
                | QuantumState.StateVector sv ->
                    try
                        match operation with
                        | QuantumOperation.Algorithm (AlgorithmOperation.QFT intent) ->
                            // Execute QFT intent by lowering to gates locally.
                             let qftOps =
                                  let numQubits = intent.NumQubits
                                  let inverse = intent.Inverse

                                  let swapSequence =
                                      if intent.ApplySwaps then
                                          [0 .. numQubits / 2 - 1]
                                          |> List.map (fun i ->
                                              let j = numQubits - 1 - i
                                              QuantumOperation.Gate (CircuitBuilder.SWAP (i, j)))
                                      else
                                          []

                                  let qftForwardSequence =
                                      let applyQftStepOps targetQubit =
                                          let hOp = QuantumOperation.Gate (CircuitBuilder.H targetQubit)
                                          let phases =
                                              [targetQubit + 1 .. numQubits - 1]
                                              |> List.map (fun k ->
                                                  let power = k - targetQubit + 1
                                                  let angle = 2.0 * Math.PI / float (1 <<< power)
                                                  QuantumOperation.Gate (CircuitBuilder.CP (k, targetQubit, angle)))
                                          hOp :: phases

                                      [0 .. numQubits - 1]
                                      |> List.collect applyQftStepOps

                                  let qftInverseSequence =
                                      [numQubits - 1 .. -1 .. 0]
                                      |> List.collect (fun targetQubit ->
                                          let phases =
                                              [numQubits - 1 .. -1 .. targetQubit + 1]
                                              |> List.map (fun k ->
                                                  let power = k - targetQubit + 1
                                                  let angle = -2.0 * Math.PI / float (1 <<< power)
                                                  QuantumOperation.Gate (CircuitBuilder.CP (k, targetQubit, angle)))
                                          let hOp = QuantumOperation.Gate (CircuitBuilder.H targetQubit)
                                          phases @ [hOp])

                                  if inverse then
                                      swapSequence @ qftInverseSequence
                                  else
                                      qftForwardSequence @ swapSequence
 
                             (self :> IQuantumBackend).ApplyOperation (QuantumOperation.Sequence qftOps) state

                         | QuantumOperation.Algorithm (AlgorithmOperation.HHL intent) ->
                              // HHL intent: diagonal-only (native intent payload is diagonal eigenvalues), encoded as a single ancilla rotation.
                             //
                             // Expected qubit layout:
                             //   [0 .. EigenvalueQubits-1] = eigenvalue register (reserved)
                             //   [EigenvalueQubits .. EigenvalueQubits+SolutionQubits-1] = solution register (|b⟩)
                             //   [EigenvalueQubits+SolutionQubits] = ancilla
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
                                     (self :> IQuantumBackend).ApplyOperation (QuantumOperation.Gate (CircuitBuilder.RY (ancillaQubit, theta))) state


                        | QuantumOperation.Algorithm (AlgorithmOperation.GroverPrepare numQubits) ->
                            // Uniform superposition is Hadamard on all qubits.
                            let ops =
                                [0 .. numQubits - 1]
                                |> List.map (fun q -> QuantumOperation.Gate (CircuitBuilder.H q))
                            (self :> IQuantumBackend).ApplyOperation (QuantumOperation.Sequence ops) state

                        | QuantumOperation.Algorithm (AlgorithmOperation.GroverOraclePhaseFlip intent) ->
                            // Apply oracle via direct state-vector phase flips.
                            match state with
                            | QuantumState.StateVector sv ->
                                let dim = 1 <<< intent.NumQubits
                                let amps =
                                    [| 0 .. dim - 1 |]
                                    |> Array.map (fun i ->
                                        let amp = StateVector.getAmplitude i sv
                                        if intent.IsMarked i then -amp else amp)
                                let newSv = StateVector.create amps
                                Ok (QuantumState.StateVector newSv)
                            | _ ->
                                Error (QuantumError.OperationError ("LocalBackend", "Expected StateVector for Grover oracle"))

                        | QuantumOperation.Algorithm (AlgorithmOperation.GroverDiffusion numQubits) ->
                             // Diffusion is inversion about the mean amplitude.
                             match state with
                             | QuantumState.StateVector sv ->
                                 let dim = 1 <<< numQubits
                                 let sumAmp =
                                     [| 0 .. dim - 1 |]
                                     |> Array.fold (fun acc i -> acc + StateVector.getAmplitude i sv) System.Numerics.Complex.Zero
                                 let meanAmp = sumAmp / System.Numerics.Complex(float dim, 0.0)
                                 let amps =
                                     [| 0 .. dim - 1 |]
                                     |> Array.map (fun i ->
                                         let a = StateVector.getAmplitude i sv
                                         (meanAmp * System.Numerics.Complex(2.0, 0.0)) - a)
                                 let newSv = StateVector.create amps
                                 Ok (QuantumState.StateVector newSv)
                             | _ ->
                                 Error (QuantumError.OperationError ("LocalBackend", "Expected StateVector for Grover diffusion"))
 
                          | QuantumOperation.Algorithm (AlgorithmOperation.QPE intent) ->
                              // Execute QPE intent by lowering to gates locally.
                              //
                              // Note: `intent.ApplySwaps` controls whether the final bit-reversal SWAPs
                              // are applied. QPE can also omit swaps and undo bit order classically.
                             if intent.CountingQubits <= 0 then
                                 Error (QuantumError.ValidationError ("CountingQubits", "must be positive"))
                             elif intent.TargetQubits <> 1 then
                                 Error (QuantumError.ValidationError ("TargetQubits", "only TargetQubits = 1 is supported by QPE intent"))
                             else
                                 let totalQubits = intent.CountingQubits + intent.TargetQubits
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

                                 // Inverse QFT on counting register.
                                 // Important: inverse processes from n-1 down to 0, phases first then H.
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
                                 if QuantumState.numQubits state <> totalQubits then
                                     Error (QuantumError.ValidationError ("state", $"Expected {totalQubits} qubits for QPE intent, got {QuantumState.numQubits state}"))
                                 else
                                     (self :> IQuantumBackend).ApplyOperation (QuantumOperation.Sequence ops) state
 
                         | QuantumOperation.Gate gate ->

                            let evolved = applyGate gate sv
                            Ok (QuantumState.StateVector evolved)

                        | QuantumOperation.Extension ext ->
                            match ext with
                            | :? IApplyToStateVectorExtension as svExt ->
                                let newSv = svExt.ApplyToStateVector sv
                                Ok (QuantumState.StateVector newSv)
                            | :? ILowerToOperationsExtension as lowerable ->
                                let ops =
                                    lowerable.LowerToGates()
                                    |> List.map QuantumOperation.Gate
                                (self :> IQuantumBackend).ApplyOperation (QuantumOperation.Sequence ops) state
                            | _ ->
                                Error (QuantumError.OperationError ("LocalBackend", $"Extension operation '{ext.Id}' is not supported"))
                        
                        | QuantumOperation.Sequence ops ->
                            // Apply operations sequentially
                            let result =
                                ops
                                |> List.fold (fun stateResult op ->
                                    match stateResult with
                                    | Error err -> Error err
                                    | Ok currentState ->
                                        (self :> IQuantumBackend).ApplyOperation op currentState
                                ) (Ok state)
                            result
                        
                        | QuantumOperation.Measure qubitIdx ->
                            // Single qubit measurement
                            let outcome = Measurement.measure qubitIdx sv
                            let collapsed = Measurement.collapse qubitIdx outcome sv
                            Ok (QuantumState.StateVector collapsed)
                        
                        | QuantumOperation.Braid _ ->
                            Error (QuantumError.OperationError ("LocalBackend", "Braiding operations not supported by gate-based backend"))
                        
                        | QuantumOperation.FMove _ ->
                            Error (QuantumError.OperationError ("LocalBackend", "F-move operations not supported by gate-based backend"))
                    with
                    | ex -> Error (QuantumError.OperationError ("LocalBackend", ex.Message))
                
                | _ ->
                    // State is not in native format - try conversion
                    let converted = QuantumStateConversion.convert QuantumStateType.GateBased state
                    match converted with
                    | QuantumState.StateVector sv ->
                        (self :> IQuantumBackend).ApplyOperation operation (QuantumState.StateVector sv)
                    | _ ->
                        Error (QuantumError.OperationError ("LocalBackend", "State conversion failed unexpectedly"))
            
             member _.SupportsOperation (operation: QuantumOperation) : bool =
                 match operation with
                 | QuantumOperation.Algorithm (AlgorithmOperation.QFT _) -> true
                 | QuantumOperation.Algorithm (AlgorithmOperation.QPE _) -> true
                 | QuantumOperation.Algorithm (AlgorithmOperation.HHL _) -> true
                 | QuantumOperation.Algorithm (AlgorithmOperation.GroverPrepare _) -> true
                 | QuantumOperation.Algorithm (AlgorithmOperation.GroverOraclePhaseFlip _) -> true
                 | QuantumOperation.Algorithm (AlgorithmOperation.GroverDiffusion _) -> true
                 | QuantumOperation.Gate _ -> true
                 | QuantumOperation.Sequence _ -> true
                 | QuantumOperation.Measure _ -> true
                 | QuantumOperation.Extension ext ->
                     match ext with
                     | :? IApplyToStateVectorExtension -> true
                     | :? ILowerToOperationsExtension -> true
                     | _ -> false
                 | QuantumOperation.Braid _ -> false  // No braiding in gate-based backend
                 | QuantumOperation.FMove _ -> false  // No F-moves in gate-based backend


            
            member _.InitializeState (numQubits: int) : Result<QuantumState, QuantumError> =
                try
                    let initialState = StateVector.init numQubits
                    Ok (QuantumState.StateVector initialState)
                with
                | ex -> Error (QuantumError.OperationError ("LocalBackend", ex.Message))

/// Factory functions for creating local backend instances
module LocalBackendFactory =
    
    /// Create a new local simulator backend
    let create () : LocalBackend.LocalBackend = LocalBackend.LocalBackend()
    
    /// Create and cast to IQuantumBackend
    let createUnified () : IQuantumBackend = create () :> IQuantumBackend
    
    /// Create and cast to IQuantumBackend (for backward compatibility)
    let createStandard () : IQuantumBackend = create () :> IQuantumBackend
