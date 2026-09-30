namespace FSharp.Azure.Quantum.Core

open System.Runtime.CompilerServices
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Record-then-submit execution for backends that only run complete circuits.
///
/// Cloud hardware refuses the incremental ApplyOperation loop a simulator accepts
/// (`UnifiedBackend.isIncrementalUnsupported`) and runs a whole circuit as one job instead.
/// Code whose operations do not depend on the state it is given (no measurement, no
/// amplitude read, no branch on an outcome) can be run once against an `OperationRecorder`,
/// and the recorded list submitted with `UnifiedBackend.submitAsCircuit`. There is one
/// implementation of the circuit, so the submitted circuit cannot drift from the one a
/// simulator applies gate by gate.
///
/// A job starts from |0…0⟩, so whole-circuit submission is only offered for an input state
/// that is |0…0⟩; any other input is an Error, never a guess.
module WholeCircuit =

    /// Records the operations applied to it and computes nothing.
    ///
    /// Sound only for state-independent code: the recorder's state carries no amplitudes, so
    /// code that reads it, measures it or branches on it would record the wrong circuit.
    /// Mid-circuit measurement is refused rather than recorded, because lowering drops a
    /// `Measure` and the gates after it would run on an unmeasured state.
    ///
    /// The state is `SparseState` with an empty amplitude map and the recorder's native type
    /// is `Sparse`, so no conversion ever allocates a 2^n vector: recording costs nothing at
    /// any width.
    ///
    /// Algorithm intents are reported unsupported, which keeps every nested planner on its
    /// gate lowering; the recorded list is then portable to any gate backend.
    type OperationRecorder(numQubits: int) =
        let recorded = ResizeArray<QuantumOperation>()

        /// Operations applied so far, in program order.
        member _.Recorded: QuantumOperation list = List.ofSeq recorded

        /// The state to hand to the recorded code.
        member _.State: QuantumState = QuantumState.SparseState(Map.empty, numQubits)

        interface IQuantumBackend with
            member _.Name = "operation-recorder"
            member _.NativeStateType = QuantumStateType.Sparse

            member _.InitializeState n =
                Ok(QuantumState.SparseState(Map.empty, n))

            member _.ApplyOperation operation state =
                match operation with
                // The incremental-support probe of `refusesIncremental`: applies nothing.
                | QuantumOperation.Sequence [] -> Ok state
                | QuantumOperation.Measure _ ->
                    Error(
                        QuantumError.OperationError(
                            "OperationRecorder",
                            "mid-circuit measurement cannot be recorded into a whole circuit"
                        )
                    )
                | _ ->
                    recorded.Add operation
                    Ok state

            member _.SupportsOperation operation =
                match operation with
                | QuantumOperation.Algorithm _ -> false
                | QuantumOperation.Measure _ -> false
                | _ -> true

            member _.ExecuteToState _ =
                Error(
                    QuantumError.OperationError(
                        "OperationRecorder",
                        "records operations only; it does not execute circuits"
                    )
                )

            member this.ExecuteToStateAsync circuit _ =
                System.Threading.Tasks.Task.FromResult((this :> IQuantumBackend).ExecuteToState circuit)

            member this.ApplyOperationAsync operation state _ =
                System.Threading.Tasks.Task.FromResult((this :> IQuantumBackend).ApplyOperation operation state)

    /// Run `program` against a recorder of `numQubits` qubits: its result and the operations it
    /// applied, in program order. `program` must be state-independent (see `OperationRecorder`).
    let record
        (numQubits: int)
        (program: IQuantumBackend -> QuantumState -> Result<'T, QuantumError>)
        : Result<'T * QuantumOperation list, QuantumError> =
        let recorder = OperationRecorder numQubits

        program (recorder :> IQuantumBackend) recorder.State
        |> Result.map (fun value -> value, recorder.Recorded)

    /// Whether `backend` refuses incremental application, asked with an empty sequence.
    ///
    /// An empty sequence changes no state, so this costs nothing on a simulator. Asking up
    /// front, rather than waiting for the first real operation to fail, is what keeps nested
    /// state-independent code from submitting a job per level: once the outermost caller has
    /// switched to recording, every nested call sees the recorder and records too.
    let refusesIncremental (backend: IQuantumBackend) (state: QuantumState) : bool =
        match backend.ApplyOperation (QuantumOperation.Sequence []) state with
        | Error e -> UnifiedBackend.isIncrementalUnsupported e
        | Ok _ -> false

    /// The error for a whole-circuit request whose input is not |0…0⟩.
    let notFromZeroError (algorithm: string) : QuantumError =
        QuantumError.OperationError(
            algorithm,
            "This backend runs complete circuits only, and a circuit job starts from |0…0⟩, so the "
            + "input state must be |0…0⟩. An arbitrary prepared state cannot be loaded into a job: "
            + "prepare it with gates inside the same circuit, or use a backend with state-vector access."
        )

    /// Run state-independent `program` on `backend`, gate by gate where the backend allows it.
    ///
    /// When the backend refuses incremental application, the program is recorded and submitted
    /// as one circuit from |0…0⟩, and `withState` puts the returned state into the program's
    /// result (everything else in that result was computed while recording, identically).
    /// Every other error surfaces unchanged.
    let run
        (algorithm: string)
        (backend: IQuantumBackend)
        (state: QuantumState)
        (program: IQuantumBackend -> QuantumState -> Result<'T, QuantumError>)
        (withState: 'T -> QuantumState -> 'T)
        : Result<'T, QuantumError> =

        let wholeCircuit () =
            if not (UnifiedBackend.isZeroState state) then
                Error(notFromZeroError algorithm)
            else
                let numQubits = QuantumState.numQubits state

                record numQubits program
                |> Result.bind (fun (value, ops) ->
                    UnifiedBackend.submitAsCircuit backend numQubits ops
                    |> Result.map (withState value))

        if refusesIncremental backend state then
            wholeCircuit ()
        else
            match program backend state with
            | Error e when UnifiedBackend.isIncrementalUnsupported e -> wholeCircuit ()
            | other -> other

    /// Apply `ops` gate by gate; when the backend refuses incremental application, submit
    /// them as one circuit instead (from |0…0⟩ only). Every other error surfaces unchanged.
    let applyOrSubmit
        (algorithm: string)
        (backend: IQuantumBackend)
        (ops: QuantumOperation list)
        (state: QuantumState)
        : Result<QuantumState, QuantumError> =
        match UnifiedBackend.applySequence backend ops state with
        | Error e when UnifiedBackend.isIncrementalUnsupported e ->
            if UnifiedBackend.isZeroState state then
                UnifiedBackend.submitAsCircuit backend (QuantumState.numQubits state) ops
            else
                Error(notFromZeroError algorithm)
        | other -> other

    // ------------------------------------------------------------------------
    // Oracle closures
    // ------------------------------------------------------------------------

    /// Gate lists of oracle closures built by `gateOracle`, keyed by the closure itself.
    let private gateOracles = ConditionalWeakTable<obj, QuantumOperation list>()

    /// An oracle closure `state -> applySequence backend ops state` whose gates stay readable,
    /// so a whole-circuit backend can have the oracle submitted inside the algorithm's circuit.
    let gateOracle
        (backend: IQuantumBackend)
        (ops: QuantumOperation list)
        : QuantumState -> Result<QuantumState, QuantumError> =
        let oracle = fun state -> UnifiedBackend.applySequence backend ops state
        gateOracles.AddOrUpdate(box oracle, ops)
        oracle

    /// The gates of an oracle closure, for whole-circuit submission.
    ///
    /// Known for closures built by `gateOracle`. Any other closure is asked once with an
    /// empty recording state: one that returns that very state unchanged is the identity and
    /// has no gates. For anything else the gates are unknown, and that is an Error: the
    /// oracle is a function, not a circuit.
    let oracleOps
        (algorithm: string)
        (numQubits: int)
        (oracle: QuantumState -> Result<QuantumState, QuantumError>)
        : Result<QuantumOperation list, QuantumError> =
        match gateOracles.TryGetValue(box oracle) with
        | true, ops -> Ok ops
        | _ ->
            let probe = QuantumState.SparseState(Map.empty, numQubits)

            let isIdentity =
                try
                    match oracle probe with
                    | Ok returned -> obj.ReferenceEquals(returned, probe)
                    | Error _ -> false
                with _ ->
                    false

            if isIdentity then
                Ok []
            else
                Error(
                    QuantumError.OperationError(
                        algorithm,
                        "This backend runs complete circuits only, and the oracle is an opaque function "
                        + "whose gates cannot be read into the circuit. Build it with the module's oracle "
                        + "constructors (their gates are submitted), or use a backend that applies "
                        + "operations incrementally."
                    )
                )

    /// `preOps`, the oracle closure, then `postOps`: gate by gate where the backend allows it,
    /// otherwise one submitted circuit with the oracle's gates (see `oracleOps`) in the middle,
    /// from |0…0⟩ only. Every other error surfaces unchanged.
    let applyWithOracle
        (algorithm: string)
        (backend: IQuantumBackend)
        (preOps: QuantumOperation list)
        (oracle: QuantumState -> Result<QuantumState, QuantumError>)
        (postOps: QuantumOperation list)
        (state: QuantumState)
        : Result<QuantumState, QuantumError> =
        let incremental =
            UnifiedBackend.applySequence backend preOps state
            |> Result.bind oracle
            |> Result.bind (UnifiedBackend.applySequence backend postOps)

        match incremental with
        | Error e when UnifiedBackend.isIncrementalUnsupported e ->
            if not (UnifiedBackend.isZeroState state) then
                Error(notFromZeroError algorithm)
            else
                let numQubits = QuantumState.numQubits state

                oracleOps algorithm numQubits oracle
                |> Result.bind (fun oracleGates ->
                    UnifiedBackend.submitAsCircuit backend numQubits (preOps @ oracleGates @ postOps))
        | other -> other
