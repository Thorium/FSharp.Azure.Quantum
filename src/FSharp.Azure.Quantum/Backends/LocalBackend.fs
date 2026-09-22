namespace FSharp.Azure.Quantum.Backends

open System
open System.Numerics
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.LocalSimulator

/// Local quantum simulator backend implementing unified quantum backend interface
///
/// Features:
/// - Gate-based quantum simulation using state vectors
/// - Native StateVector representation (no conversion needed)
/// - Supports all standard gates (H, X, Y, Z, RX, RY, RZ, CNOT, CZ, etc.)
/// - Width limited by StateVector.maxQubits: derived from available memory, capped at 30
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
            | CircuitBuilder.P(q, angle) -> Gates.applyP q angle state

            // Single-qubit gates - Rotation
            | CircuitBuilder.RX(q, angle) -> Gates.applyRx q angle state
            | CircuitBuilder.RY(q, angle) -> Gates.applyRy q angle state
            | CircuitBuilder.RZ(q, angle) -> Gates.applyRz q angle state

            // Single-qubit gates - Universal
            | CircuitBuilder.U3(q, theta, phi, lambda) ->
                // U3(θ,φ,λ) = Rz(φ)·Ry(θ)·Rz(λ)
                state |> Gates.applyRz q lambda |> Gates.applyRy q theta |> Gates.applyRz q phi

            // Two-qubit gates - Standard
            | CircuitBuilder.CNOT(ctrl, target) -> Gates.applyCNOT ctrl target state
            | CircuitBuilder.CZ(ctrl, target) -> Gates.applyCZ ctrl target state
            | CircuitBuilder.SWAP(q1, q2) -> Gates.applySWAP q1 q2 state

            // Two-qubit gates - Ising interactions
            | CircuitBuilder.RXX(q1, q2, angle) -> Gates.applyRxx q1 q2 angle state
            | CircuitBuilder.RYY(q1, q2, angle) -> Gates.applyRyy q1 q2 angle state
            | CircuitBuilder.RZZ(q1, q2, angle) -> Gates.applyRzz q1 q2 angle state

            // Two-qubit gates - Controlled (proper implementations)
            | CircuitBuilder.CP(ctrl, target, angle) -> Gates.applyCP ctrl target angle state
            | CircuitBuilder.CRX(ctrl, target, angle) -> Gates.applyCRX ctrl target angle state
            | CircuitBuilder.CRY(ctrl, target, angle) -> Gates.applyCRY ctrl target angle state
            | CircuitBuilder.CRZ(ctrl, target, angle) -> Gates.applyCRZ ctrl target angle state

            // Three-qubit gates
            | CircuitBuilder.CCX(ctrl1, ctrl2, target) -> Gates.applyCCX ctrl1 ctrl2 target state

            // Multi-qubit gates
            | CircuitBuilder.MCZ(controls, target) -> Gates.applyMultiControlledZ controls target state

            // Measurement - should not appear in circuit execution
            | CircuitBuilder.Measure q -> failwith "Measurement gates should be handled separately"

            // Reset - resets qubit to |0⟩ by measuring and conditionally flipping
            | CircuitBuilder.Reset q ->
                let rng = Random()
                let outcome = Measurement.measureSingleQubit rng q state
                let collapsed = Measurement.collapseAfterMeasurement q outcome state
                if outcome = 1 then Gates.applyX q collapsed else collapsed

            // Barrier - synchronization directive, no physical effect on simulation
            | CircuitBuilder.Barrier _ -> state

            // Conditional - requires measurement-outcome tracking (applyGateTracked)
            | CircuitBuilder.Conditional _ ->
                failwith "Conditional gates require outcome tracking; executed via applyGateTracked"

        /// Apply a gate while tracking mid-circuit measurement outcomes.
        ///
        /// Measure collapses the state and records the classical outcome;
        /// Conditional applies its inner gate only when the recorded outcome
        /// of the referenced qubit is 1.
        let applyGateTracked
            (gate: CircuitBuilder.Gate)
            (state: StateVector.StateVector, outcomes: Map<int, int>)
            : StateVector.StateVector * Map<int, int> =
            match gate with
            | CircuitBuilder.Measure q ->
                let rng = Random()
                let outcome = Measurement.measureSingleQubit rng q state
                let collapsed = Measurement.collapseAfterMeasurement q outcome state
                (collapsed, outcomes |> Map.add q outcome)
            | CircuitBuilder.Conditional(q, inner) ->
                match outcomes |> Map.tryFind q with
                | Some 1 -> (applyGate inner state, outcomes)
                | Some _ -> (state, outcomes)
                | None -> failwith $"Conditional gate references qubit {q} before it was measured"
            | g -> (applyGate g state, outcomes)

        /// Apply a gate by overwriting an owned state, or report that it has no
        /// in-place kernel and must go through the allocating path.
        ///
        /// Returns true when it handled the gate. Gates without a kernel here
        /// (RXX/RYY, which mix four indices at once, and anything that measures)
        /// fall back rather than being silently skipped.
        let tryApplyGateInPlace (numQubits: int) (gate: CircuitBuilder.Gate) (owned: StateVector.Owned) : bool =
            // The in-place kernels index by bit mask and do not range-check, where the
            // allocating gates `failwith` on a bad index. Rather than duplicate their
            // validation, refuse the gate: the fallback path then raises exactly the
            // error it always did. Silently computing a wrong state for an invalid
            // circuit would be far worse than the lost optimisation.
            let inRange indices =
                indices |> List.forall (fun q -> q >= 0 && q < numQubits)

            let single index matrix =
                inRange [ index ]
                && (Gates.InPlace.applySingleQubitGate index matrix owned
                    true)

            let controlled controls target matrix =
                // Distinctness matters too: a control that is also the target would
                // make the mask/target overlap and silently misbehave.
                inRange (target :: controls)
                && not (List.contains target controls)
                && List.distinct controls = controls
                && (let controlMask = controls |> List.fold (fun acc c -> acc ||| (1 <<< c)) 0

                    Gates.InPlace.applyControlledGate controlMask target matrix owned
                    true)

            match gate with
            | CircuitBuilder.H q -> single q Gates.Matrices.h
            | CircuitBuilder.X q -> single q Gates.Matrices.x
            | CircuitBuilder.Y q -> single q Gates.Matrices.y
            | CircuitBuilder.Z q -> single q Gates.Matrices.z
            | CircuitBuilder.S q -> single q Gates.Matrices.s
            | CircuitBuilder.SDG q -> single q Gates.Matrices.sdg
            | CircuitBuilder.T q -> single q Gates.Matrices.t
            | CircuitBuilder.TDG q -> single q Gates.Matrices.tdg
            | CircuitBuilder.P(q, angle) -> single q (Gates.Matrices.p angle)
            | CircuitBuilder.RX(q, angle) -> single q (Gates.Matrices.rx angle)
            | CircuitBuilder.RY(q, angle) -> single q (Gates.Matrices.ry angle)
            | CircuitBuilder.RZ(q, angle) -> single q (Gates.Matrices.rz angle)

            // U3(θ,φ,λ) = Rz(φ)·Ry(θ)·Rz(λ), same decomposition as applyGate.
            | CircuitBuilder.U3(q, theta, phi, lambda) ->
                inRange [ q ]
                && (Gates.InPlace.applySingleQubitGate q (Gates.Matrices.rz lambda) owned
                    Gates.InPlace.applySingleQubitGate q (Gates.Matrices.ry theta) owned
                    Gates.InPlace.applySingleQubitGate q (Gates.Matrices.rz phi) owned
                    true)

            | CircuitBuilder.CNOT(control, target) -> controlled [ control ] target Gates.Matrices.x
            | CircuitBuilder.CZ(control, target) -> controlled [ control ] target Gates.Matrices.z
            | CircuitBuilder.CP(control, target, angle) -> controlled [ control ] target (Gates.Matrices.p angle)
            | CircuitBuilder.CRX(control, target, angle) -> controlled [ control ] target (Gates.Matrices.rx angle)
            | CircuitBuilder.CRY(control, target, angle) -> controlled [ control ] target (Gates.Matrices.ry angle)
            | CircuitBuilder.CRZ(control, target, angle) -> controlled [ control ] target (Gates.Matrices.rz angle)
            | CircuitBuilder.CCX(control1, control2, target) ->
                controlled [ control1; control2 ] target Gates.Matrices.x
            | CircuitBuilder.MCZ(controls, target) -> controlled controls target Gates.Matrices.z

            | CircuitBuilder.SWAP(q1, q2) ->
                inRange [ q1; q2 ]
                && q1 <> q2
                && (Gates.InPlace.applySwap q1 q2 owned
                    true)

            | CircuitBuilder.Barrier _ -> true // synchronisation directive, no effect

            // RXX/RYY mix four indices at once; Measure/Conditional/Reset need the
            // outcome-tracking path. Both go through the allocating fold.
            | _ -> false

        /// Execute circuit on state vector
        let executeCircuit
            (circuit: CircuitBuilder.Circuit)
            (numQubits: int)
            : Result<StateVector.StateVector, QuantumError> =
            try
                // Initialize to |0⟩^⊗n
                let initialState = StateVector.init numQubits

                // Gates are stored most-recent-first; List.rev restores program order.
                let gates = circuit.Gates |> List.rev

                // Nothing else holds a reference to the states produced inside this
                // fold — each is consumed by the next gate and never read again — so
                // gates may overwrite them rather than allocating a fresh 2^n array
                // apiece. At 30 qubits that is 16 GB of allocation per gate avoided.
                // Gates without an in-place kernel fall back to the allocating path,
                // and ownership is re-taken on the state they return.
                let finalState =
                    gates
                    |> List.fold
                        (fun (state: StateVector.StateVector, outcomes: Map<int, int>) gate ->
                            let owned = StateVector.takeOwnership state

                            if tryApplyGateInPlace numQubits gate owned then
                                (StateVector.release owned, outcomes)
                            else
                                applyGateTracked gate (state, outcomes))
                        (initialState, Map.empty)
                    |> fst

                Ok finalState
            with
            | :? OperationCanceledException ->
                Error(QuantumError.OperationError("LocalBackend", "Execution was cancelled"))
            | ex -> Error(QuantumError.OperationError("LocalBackend", ex.Message))

        // (A `sampleMeasurements` helper used to sit here with no callers — measurement
        // goes through QuantumState.measure / Measurement.measureAll directly. It was
        // removed for the same reason `executeCircuit` had to be reconnected above:
        // unreachable code in this file reads as if it were the implementation, and an
        // optimisation applied to it silently does nothing.)

        // ====================================================================
        // IQuantumBackend Implementation
        // ====================================================================

        interface IQuantumBackend with
            member _.Name = "Local Simulator"

            member this.ExecuteToState(circuit: CircuitAbstraction.ICircuit) : Result<QuantumState, QuantumError> =
                let numQubits = circuit.NumQubits

                // Pattern match on specific circuit wrapper types to extract gates
                // Both wrappers run the SAME fold. They used to carry a copy of it
                // each, which is how `executeCircuit` below ended up unreachable —
                // and an optimisation applied there had no effect on either caller.
                match box circuit with
                | :? CircuitAbstraction.CircuitWrapper as wrapper ->
                    executeCircuit wrapper.Circuit numQubits |> Result.map QuantumState.StateVector

                | :? CircuitAbstraction.QaoaCircuitWrapper as qaoaWrapper ->
                    let cbCircuit =
                        CircuitAbstraction.CircuitAdapter.qaoaCircuitToCircuit qaoaWrapper.QaoaCircuit

                    executeCircuit cbCircuit numQubits |> Result.map QuantumState.StateVector

                | _ ->
                    // For unknown circuit types, cannot execute directly
                    Error(
                        QuantumError.OperationError(
                            "LocalBackend",
                            $"Circuit type {circuit.GetType().Name} not supported by LocalBackend.ExecuteToState - wrap with CircuitWrapper or QaoaCircuitWrapper"
                        )
                    )

            member _.NativeStateType = QuantumStateType.GateBased

            member self.ApplyOperation
                (operation: QuantumOperation)
                (state: QuantumState)
                : Result<QuantumState, QuantumError> =
                match state with
                | QuantumState.StateVector sv ->
                    try
                        match operation with
                        | QuantumOperation.Algorithm(AlgorithmOperation.QFT intent) ->
                            // Execute QFT intent by lowering to gates locally.
                            let qftOps =
                                let numQubits = intent.NumQubits
                                let inverse = intent.Inverse

                                let swapSequence =
                                    if intent.ApplySwaps then
                                        [ 0 .. numQubits / 2 - 1 ]
                                        |> List.map (fun i ->
                                            let j = numQubits - 1 - i
                                            QuantumOperation.Gate(CircuitBuilder.SWAP(i, j)))
                                    else
                                        []

                                let qftForwardSequence =
                                    let applyQftStepOps targetQubit =
                                        let hOp = QuantumOperation.Gate(CircuitBuilder.H targetQubit)

                                        let phases =
                                            [ targetQubit + 1 .. numQubits - 1 ]
                                            |> List.map (fun k ->
                                                let power = k - targetQubit + 1
                                                let angle = 2.0 * Math.PI / float (1 <<< power)
                                                QuantumOperation.Gate(CircuitBuilder.CP(k, targetQubit, angle)))

                                        hOp :: phases

                                    [ 0 .. numQubits - 1 ] |> List.collect applyQftStepOps

                                let qftInverseSequence =
                                    [ numQubits - 1 .. -1 .. 0 ]
                                    |> List.collect (fun targetQubit ->
                                        let phases =
                                            [ numQubits - 1 .. -1 .. targetQubit + 1 ]
                                            |> List.map (fun k ->
                                                let power = k - targetQubit + 1
                                                let angle = -2.0 * Math.PI / float (1 <<< power)
                                                QuantumOperation.Gate(CircuitBuilder.CP(k, targetQubit, angle)))

                                        let hOp = QuantumOperation.Gate(CircuitBuilder.H targetQubit)
                                        phases @ [ hOp ])

                                if inverse then
                                    swapSequence @ qftInverseSequence
                                else
                                    qftForwardSequence @ swapSequence

                            (self :> IQuantumBackend).ApplyOperation (QuantumOperation.Sequence qftOps) state

                        | QuantumOperation.Algorithm(AlgorithmOperation.HHL intent) ->
                            // Diagonal HHL inversion via the shared multiplexed multi-controlled RY,
                            // so the gate simulator and the topological backend invert identically and
                            // correctly for any solution-register size.
                            applyHhlInversion (self :> IQuantumBackend) intent state


                        | QuantumOperation.Algorithm(AlgorithmOperation.GroverPrepare numQubits) ->
                            // Uniform superposition is Hadamard on all qubits.
                            let ops =
                                [ 0 .. numQubits - 1 ] |> List.map (CircuitBuilder.H >> QuantumOperation.Gate)

                            (self :> IQuantumBackend).ApplyOperation (QuantumOperation.Sequence ops) state

                        | QuantumOperation.Algorithm(AlgorithmOperation.GroverOraclePhaseFlip intent) ->
                            // Apply oracle via direct state-vector phase flips.
                            match state with
                            | QuantumState.StateVector sv ->
                                let dim = 1 <<< intent.NumQubits

                                // Array.init, not a map over `[| 0 .. dim - 1 |]`: that
                                // allocates a second 2ⁿ array of ints to iterate over.
                                // The result is handed over rather than copied, since
                                // nothing else references it.
                                let amps =
                                    Array.init dim (fun i ->
                                        let amp = StateVector.getAmplitude i sv
                                        if intent.IsMarked i then -amp else amp)

                                Ok(QuantumState.StateVector(StateVector.ofAmplitudesOwned amps))
                            | _ ->
                                Error(
                                    QuantumError.OperationError(
                                        "LocalBackend",
                                        "Expected StateVector for Grover oracle"
                                    )
                                )

                        | QuantumOperation.Algorithm(AlgorithmOperation.GroverDiffusion numQubits) ->
                            // Diffusion is inversion about the mean amplitude.
                            match state with
                            | QuantumState.StateVector sv ->
                                let dim = 1 <<< numQubits

                                // Sum in a plain loop: folding over `[| 0 .. dim - 1 |]`
                                // allocated a 2ⁿ array of ints just to have something
                                // to fold, and the map below allocated a second one.
                                let mutable sumAmp = Complex.Zero

                                for i in 0 .. dim - 1 do
                                    sumAmp <- sumAmp + StateVector.getAmplitude i sv

                                let meanAmp = sumAmp / Complex(float dim, 0.0)
                                let twiceMean = meanAmp * Complex(2.0, 0.0)

                                let amps =
                                    Array.init dim (fun i -> twiceMean - StateVector.getAmplitude i sv)

                                Ok(QuantumState.StateVector(StateVector.ofAmplitudesOwned amps))
                            | _ ->
                                Error(
                                    QuantumError.OperationError(
                                        "LocalBackend",
                                        "Expected StateVector for Grover diffusion"
                                    )
                                )

                        | QuantumOperation.Algorithm(AlgorithmOperation.QPE intent) ->
                            // Execute QPE intent by lowering to gates locally.
                            //
                            // Note: `intent.ApplySwaps` controls whether the final bit-reversal SWAPs
                            // are applied. QPE can also omit swaps and undo bit order classically.
                            if intent.CountingQubits <= 0 then
                                Error(QuantumError.ValidationError("CountingQubits", "must be positive"))
                            elif intent.TargetQubits <> 1 then
                                Error(
                                    QuantumError.ValidationError(
                                        "TargetQubits",
                                        "only TargetQubits = 1 is supported by QPE intent"
                                    )
                                )
                            elif
                                (match intent.Unitary with
                                 | QpeUnitary.ModularExponentiation _ -> true
                                 | _ -> false)
                            then
                                Error(
                                    QuantumError.OperationError(
                                        "LocalBackend",
                                        "ModularExponentiation QPE cannot be executed via LocalBackend's native QPE handler. "
                                        + "Use Shor.estimateModExpPhase which orchestrates the full Beauregard arithmetic circuit."
                                    )
                                )
                            else
                                let totalQubits = intent.CountingQubits + intent.TargetQubits
                                let targetQubit = intent.CountingQubits

                                let hadamardOps =
                                    [ 0 .. intent.CountingQubits - 1 ]
                                    |> List.map (CircuitBuilder.H >> QuantumOperation.Gate)

                                let eigenPrepOps =
                                    if intent.PrepareTargetOne then
                                        [ QuantumOperation.Gate(CircuitBuilder.X targetQubit) ]
                                    else
                                        []

                                let controlledOps =
                                    [ 0 .. intent.CountingQubits - 1 ]
                                    |> List.map (fun j ->
                                        let applications = 1 <<< j

                                        match intent.Unitary with
                                        | QpeUnitary.PhaseGate theta ->
                                            let totalTheta = float applications * theta
                                            QuantumOperation.Gate(CircuitBuilder.CP(j, targetQubit, totalTheta))
                                        | QpeUnitary.TGate ->
                                            let totalTheta = float applications * Math.PI / 4.0
                                            QuantumOperation.Gate(CircuitBuilder.CP(j, targetQubit, totalTheta))
                                        | QpeUnitary.SGate ->
                                            let totalTheta = float applications * Math.PI / 2.0
                                            QuantumOperation.Gate(CircuitBuilder.CP(j, targetQubit, totalTheta))
                                        | QpeUnitary.RotationZ theta ->
                                            let totalTheta = float applications * theta
                                            QuantumOperation.Gate(CircuitBuilder.CRZ(j, targetQubit, totalTheta))
                                        | QpeUnitary.ModularExponentiation _ ->
                                            // ModularExponentiation requires multi-qubit Beauregard circuits
                                            // that cannot be expressed as single controlled gate ops.
                                            // This case is unreachable when going through QPE.plan() which
                                            // rejects ModularExponentiation early, but we handle it for
                                            // exhaustive matching if the intent is constructed directly.
                                            failwith
                                                "ModularExponentiation QPE cannot be executed via LocalBackend's native QPE handler. Use Shor.estimateModExpPhase instead.")

                                // Inverse QFT on counting register.
                                // Important: inverse processes from n-1 down to 0, phases first then H.
                                let inverseQftOps =
                                    [ (intent.CountingQubits - 1) .. -1 .. 0 ]
                                    |> List.collect (fun tq ->
                                        let phases =
                                            [ tq + 1 .. intent.CountingQubits - 1 ]
                                            |> List.map (fun k ->
                                                let power = k - tq + 1
                                                let angle = -2.0 * Math.PI / float (1 <<< power)
                                                QuantumOperation.Gate(CircuitBuilder.CP(k, tq, angle)))

                                        let h = QuantumOperation.Gate(CircuitBuilder.H tq)
                                        phases @ [ h ])

                                let swapOps =
                                    if intent.ApplySwaps then
                                        [ 0 .. intent.CountingQubits / 2 - 1 ]
                                        |> List.map (fun i ->
                                            let j = intent.CountingQubits - 1 - i
                                            QuantumOperation.Gate(CircuitBuilder.SWAP(i, j)))
                                    else
                                        []

                                let ops = hadamardOps @ eigenPrepOps @ controlledOps @ inverseQftOps @ swapOps

                                if QuantumState.numQubits state <> totalQubits then
                                    Error(
                                        QuantumError.ValidationError(
                                            "state",
                                            $"Expected {totalQubits} qubits for QPE intent, got {QuantumState.numQubits state}"
                                        )
                                    )
                                else
                                    (self :> IQuantumBackend).ApplyOperation (QuantumOperation.Sequence ops) state

                        | QuantumOperation.Gate gate ->

                            let evolved = applyGate gate sv
                            Ok(QuantumState.StateVector evolved)

                        | QuantumOperation.Extension ext ->
                            match ext with
                            | :? IApplyToStateVectorExtension as svExt ->
                                let newSv = svExt.ApplyToStateVector sv
                                Ok(QuantumState.StateVector newSv)
                            | :? ILowerToOperationsExtension as lowerable ->
                                let ops = lowerable.LowerToGates() |> List.map QuantumOperation.Gate
                                (self :> IQuantumBackend).ApplyOperation (QuantumOperation.Sequence ops) state
                            | _ ->
                                Error(
                                    QuantumError.OperationError(
                                        "LocalBackend",
                                        $"Extension operation '{ext.Id}' is not supported"
                                    )
                                )

                        | QuantumOperation.Sequence ops ->
                            // Apply operations sequentially
                            let result =
                                ops
                                |> List.fold
                                    (fun stateResult op ->
                                        stateResult
                                        |> Result.bind (fun currentState ->
                                            (self :> IQuantumBackend).ApplyOperation op currentState))
                                    (Ok state)

                            result

                        | QuantumOperation.Measure qubitIdx ->
                            // Single qubit measurement
                            let outcome = Measurement.measure qubitIdx sv
                            let collapsed = Measurement.collapse qubitIdx outcome sv
                            Ok(QuantumState.StateVector collapsed)

                        | QuantumOperation.Braid _ ->
                            Error(
                                QuantumError.OperationError(
                                    "LocalBackend",
                                    "Braiding operations not supported by gate-based backend"
                                )
                            )

                        | QuantumOperation.FMove _ ->
                            Error(
                                QuantumError.OperationError(
                                    "LocalBackend",
                                    "F-move operations not supported by gate-based backend"
                                )
                            )
                    with ex ->
                        Error(QuantumError.OperationError("LocalBackend", ex.Message))

                | _ ->
                    // State is not in native format - try conversion
                    match QuantumStateConversion.convert QuantumStateType.GateBased state with
                    | Ok(QuantumState.StateVector sv) ->
                        (self :> IQuantumBackend).ApplyOperation operation (QuantumState.StateVector sv)
                    | Ok _ ->
                        Error(
                            QuantumError.OperationError(
                                "LocalBackend",
                                "State conversion returned non-StateVector type"
                            )
                        )
                    | Error e -> Error e

            member _.SupportsOperation(operation: QuantumOperation) : bool =
                match operation with
                | QuantumOperation.Algorithm(AlgorithmOperation.QFT _) -> true
                | QuantumOperation.Algorithm(AlgorithmOperation.QPE _) -> true
                | QuantumOperation.Algorithm(AlgorithmOperation.HHL _) -> true
                | QuantumOperation.Algorithm(AlgorithmOperation.GroverPrepare _) -> true
                | QuantumOperation.Algorithm(AlgorithmOperation.GroverOraclePhaseFlip _) -> true
                | QuantumOperation.Algorithm(AlgorithmOperation.GroverDiffusion _) -> true
                | QuantumOperation.Gate _ -> true
                | QuantumOperation.Sequence _ -> true
                | QuantumOperation.Measure _ -> true
                | QuantumOperation.Extension ext ->
                    match ext with
                    | :? IApplyToStateVectorExtension -> true
                    | :? ILowerToOperationsExtension -> true
                    | _ -> false
                | QuantumOperation.Braid _ -> false // No braiding in gate-based backend
                | QuantumOperation.FMove _ -> false // No F-moves in gate-based backend



            member _.InitializeState(numQubits: int) : Result<QuantumState, QuantumError> =
                try
                    let initialState = StateVector.init numQubits
                    Ok(QuantumState.StateVector initialState)
                with ex ->
                    Error(QuantumError.OperationError("LocalBackend", ex.Message))

            member this.ExecuteToStateAsync
                (circuit: ICircuit)
                (_ct: CancellationToken)
                : Task<Result<QuantumState, QuantumError>> =
                task { return (this :> IQuantumBackend).ExecuteToState circuit }

            member this.ApplyOperationAsync
                (operation: QuantumOperation)
                (state: QuantumState)
                (_ct: CancellationToken)
                : Task<Result<QuantumState, QuantumError>> =
                task { return (this :> IQuantumBackend).ApplyOperation operation state }

        interface IQubitLimitedBackend with
            /// However wide a dense state vector this machine can actually hold.
            ///
            /// An n-qubit state is 2ⁿ × 16 bytes, so the answer depends on installed
            /// memory rather than on any fixed number: StateVector.maxQubits derives it
            /// from GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, clamped to the
            /// structural maximum of 30 (Array.MaxLength cannot hold 2³¹ amplitudes)
            /// and floored at 20. Override with the FSAQ_MAX_QUBITS environment variable.
            member _.MaxQubits = Some StateVector.maxQubits

/// Factory functions for creating local backend instances
module LocalBackendFactory =

    /// Create a new local simulator backend
    let create () : LocalBackend.LocalBackend = LocalBackend.LocalBackend()

    /// Create and cast to IQuantumBackend
    let createUnified () : IQuantumBackend = create () :> IQuantumBackend

    /// Create and cast to IQuantumBackend (for backward compatibility)
    let createStandard () : IQuantumBackend = create () :> IQuantumBackend
