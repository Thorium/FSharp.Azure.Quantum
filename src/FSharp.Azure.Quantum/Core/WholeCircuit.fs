namespace FSharp.Azure.Quantum.Core

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

    /// An oracle closure that carries its gates.
    ///
    /// It IS the function (an FSharpFunc subclass), so it can be passed wherever an oracle
    /// `QuantumState -> Result<QuantumState, QuantumError>` is expected, and a whole-circuit
    /// backend recognises it by type and submits its gates. Recognition by type rather than by
    /// reference matters: the F# optimiser may inline a function-valued top-level binding as a
    /// fresh lambda at each use site, so a closure registered by reference can arrive as a
    /// different object.
    type GateOracle(operations: QuantumOperation list, apply: QuantumState -> Result<QuantumState, QuantumError>) =
        inherit FSharpFunc<QuantumState, Result<QuantumState, QuantumError>>()

        /// The oracle's gates, in program order.
        member _.Operations = operations

        override _.Invoke(state: QuantumState) = apply state

    let private asOracle (oracle: GateOracle) : QuantumState -> Result<QuantumState, QuantumError> =
        unbox<QuantumState -> Result<QuantumState, QuantumError>> (box oracle)

    /// An oracle closure `state -> applySequence backend ops state` whose gates stay readable,
    /// so a whole-circuit backend can have the oracle submitted inside the algorithm's circuit.
    let gateOracle
        (backend: IQuantumBackend)
        (ops: QuantumOperation list)
        : QuantumState -> Result<QuantumState, QuantumError> =
        GateOracle(ops, (fun state -> UnifiedBackend.applySequence backend ops state)) |> asOracle

    /// The identity oracle: no gates, readable on whole-circuit backends like any `gateOracle`.
    let identityOracle: QuantumState -> Result<QuantumState, QuantumError> =
        GateOracle([], Ok) |> asOracle

    /// The gates of an oracle closure, for whole-circuit submission.
    ///
    /// Known for oracles that carry their gates (`GateOracle`: those built by `gateOracle`, and
    /// `identityOracle`). Any other closure is
    /// an Error: the oracle is a function, not a circuit, and nothing observable from outside
    /// it proves what it does. (Probing one with an empty state and calling it the identity
    /// when it returned that state unchanged let a closure that branches on its input be
    /// submitted as no gates at all, and a balanced oracle be reported constant.)
    let oracleOps
        (algorithm: string)
        (_numQubits: int)
        (oracle: QuantumState -> Result<QuantumState, QuantumError>)
        : Result<QuantumOperation list, QuantumError> =
        match box oracle with
        | :? GateOracle as known -> Ok known.Operations
        | _ ->
            Error(
                QuantumError.OperationError(
                    algorithm,
                    "This backend runs complete circuits only, and the oracle is an opaque function "
                    + "whose gates cannot be read into the circuit. Build it with the module's oracle "
                    + "constructors or WholeCircuit.gateOracle / WholeCircuit.identityOracle (their gates "
                    + "are submitted), or use a backend that applies operations incrementally."
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

    /// `ops` moved up by `offset` qubits: the same program on qubits offset, offset + 1, ...
    /// Gates, sequences and measurements only — what whole-circuit submission lowers; an
    /// algorithm intent or a topological operation is an Error.
    let rec shiftOps (offset: int) (ops: QuantumOperation list) : Result<QuantumOperation list, QuantumError> =
        ops
        |> List.map (fun op ->
            match op with
            | QuantumOperation.Gate gate -> Ok(QuantumOperation.Gate(CircuitBuilder.mapQubits ((+) offset) gate))
            | QuantumOperation.Sequence inner -> shiftOps offset inner |> Result.map QuantumOperation.Sequence
            | QuantumOperation.Measure q -> Ok(QuantumOperation.Measure(q + offset))
            | other ->
                Error(
                    QuantumError.OperationError(
                        "WholeCircuit.shiftOps",
                        $"only gate operations can be placed on other qubits; got %A{other}"
                    )
                ))
        |> List.fold
            (fun acc next -> acc |> Result.bind (fun done' -> next |> Result.map (fun op -> op :: done')))
            (Ok [])
        |> Result.map List.rev

    // ------------------------------------------------------------------------
    // Independent trials
    // ------------------------------------------------------------------------

    /// Widest circuit `runTrials` builds, unless the backend runs fewer qubits.
    [<Literal>]
    let MaxTrialWidth = 16

    /// One kind of independent trial, and how many outcomes of it are wanted.
    ///
    /// Trials of one kind are identical experiments (a BB84 transmission with given bits and
    /// bases, an E91 pair with given measurement angles), so any measured outcome of the kind
    /// serves any of them, and trials on disjoint qubits of one circuit do not interact.
    type TrialKind<'Key> =
        {
            /// Identifies the kind; outcomes are returned under it.
            Key: 'Key
            /// Qubits one trial occupies.
            Width: int
            /// Gates of one trial placed on qubits offset .. offset + Width - 1, program order.
            Gates: int -> QuantumOperation list
            /// Outcomes wanted.
            Count: int
        }

    /// Width `runTrials` packs circuits to on `backend`: its runnable qubits, at most
    /// MaxTrialWidth. A density-matrix simulator limited to 8 qubits gets 8-qubit circuits.
    let trialWidth (backend: IQuantumBackend) : int =
        match UnifiedBackend.getRunnableQubits backend with
        | Some runnable -> max 1 (min MaxTrialWidth runnable)
        | None -> MaxTrialWidth

    /// Run `kinds` of independent trials as whole circuits and return `Count` outcomes of each
    /// kind (one bit per trial qubit, qubit 0 of the trial first).
    ///
    /// A circuit holds several trials side by side on disjoint qubits, and every shot of it
    /// gives one outcome of each. A kind needing k outcomes from a backend that measures S
    /// shots per job gets ⌈k/S⌉ slots, and the slots are packed into circuits no wider than
    /// `trialWidth`: every job's shots are used, and the number of jobs grows with the number
    /// of distinct kinds rather than with the number of trials. A computed state (simulator)
    /// is sampled as often as its slots need. A job that returns fewer shots than expected
    /// is topped up by further jobs; one that returns none is an Error.
    let runTrials
        (algorithm: string)
        (backend: IQuantumBackend)
        (kinds: TrialKind<'Key> list)
        : Result<Map<'Key, int[][]>, QuantumError> =

        let width = trialWidth backend

        match kinds |> List.tryFind (fun kind -> kind.Width > width) with
        | Some kind ->
            Error(
                QuantumError.ValidationError(
                    "backend",
                    $"{algorithm}: one trial needs {kind.Width} qubits and backend '{backend.Name}' runs circuits of at most {width}"
                )
            )
        | None ->
            let shotsPerJob =
                match backend with
                | :? IShotSamplingBackend as sampling when sampling.Shots > 0 -> Some sampling.Shots
                | _ -> None

            let collected =
                kinds |> List.map (fun kind -> kind.Key, ResizeArray<int[]>()) |> dict

            let need (kind: TrialKind<'Key>) = kind.Count - collected.[kind.Key].Count

            // Slots of one circuit: (kind, qubit offset), first fit in kind order.
            let pack (slots: TrialKind<'Key> list) : (TrialKind<'Key> * int) list list =
                slots
                |> List.fold
                    (fun (circuits: ((TrialKind<'Key> * int) list * int) list) slot ->
                        match circuits with
                        | (current, used) :: rest when used + slot.Width <= width ->
                            ((slot, used) :: current, used + slot.Width) :: rest
                        | _ -> ([ slot, 0 ], slot.Width) :: circuits)
                    []
                |> List.rev
                |> List.map (fst >> List.rev)

            let runCircuit (placed: (TrialKind<'Key> * int) list) : Result<unit, QuantumError> =
                let circuitWidth = placed |> List.sumBy (fun (kind, _) -> kind.Width)
                let ops = placed |> List.collect (fun (kind, offset) -> kind.Gates offset)

                UnifiedBackend.submitAsCircuit backend circuitWidth ops
                |> Result.bind (fun state ->
                    let outcomes =
                        match UnifiedBackend.recordedShots state with
                        | Some recorded -> recorded
                        | None ->
                            let wanted = placed |> List.map (fst >> need) |> List.max |> max 1
                            UnifiedBackend.measureState state wanted

                    if outcomes.Length = 0 then
                        Error(
                            QuantumError.OperationError(
                                algorithm,
                                $"backend '{backend.Name}' returned a job with no measured shots"
                            )
                        )
                    else
                        for kind, offset in placed do
                            for bits in outcomes do
                                collected.[kind.Key].Add(Array.sub bits offset kind.Width)

                        Ok())

            let rec round () =
                let pending = kinds |> List.filter (fun kind -> need kind > 0)

                if List.isEmpty pending then
                    Ok()
                else
                    let slots =
                        pending
                        |> List.collect (fun kind ->
                            let perSlot = shotsPerJob |> Option.defaultValue (need kind)
                            List.replicate ((need kind + perSlot - 1) / perSlot) kind)

                    pack slots
                    |> List.fold (fun acc placed -> acc |> Result.bind (fun () -> runCircuit placed)) (Ok())
                    |> Result.bind round

            round ()
            |> Result.map (fun () ->
                kinds
                |> List.map (fun kind -> kind.Key, collected.[kind.Key] |> Seq.take kind.Count |> Array.ofSeq)
                |> Map.ofList)
