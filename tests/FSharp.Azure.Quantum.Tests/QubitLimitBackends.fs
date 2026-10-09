namespace FSharp.Azure.Quantum.Tests

open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

/// Backend that simulates nothing, for testing admission against a backend's qubit limit.
/// It declares `limit` as its capacity (None: no limit), records the width of every state or
/// circuit asked of it, and fails every request with an error containing `CapacityStubBackend.Marker`.
/// A problem that reaches this error got past every width check without a state being allocated.
/// It accepts every gate, and algorithm intents (native QPE, ...) only when `nativeAlgorithms`.
type CapacityStubBackend(name: string, limit: int option, ?nativeAlgorithms: bool) =
    let widths = ResizeArray<int>()

    let refuse (qubits: int) : Result<QuantumState, QuantumError> =
        lock widths (fun () -> widths.Add qubits)
        Error(QuantumError.OperationError(name, CapacityStubBackend.Marker))

    /// Text of every error this backend returns.
    static member Marker = "capacity stub: nothing is simulated"

    /// Width of each request, in order.
    member _.Widths: int list = lock widths (fun () -> List.ofSeq widths)

    /// Widest state or circuit requested so far (0 when none).
    member this.Widest = List.fold max 0 this.Widths

    /// Requests that reached the backend.
    member this.Requests = this.Widths.Length

    interface IQubitLimitedBackend with
        member _.MaxQubits = limit

    interface IQuantumBackend with
        member _.Name = name
        member _.NativeStateType = QuantumStateType.GateBased

        member _.SupportsOperation operation =
            match operation with
            | QuantumOperation.Algorithm _ -> defaultArg nativeAlgorithms false
            | _ -> true

        member _.InitializeState numQubits = refuse numQubits
        member _.ApplyOperation _ state = refuse (QuantumState.numQubits state)
        member _.ExecuteToState circuit = refuse circuit.NumQubits

        member _.ExecuteToStateAsync circuit _ =
            Task.FromResult(refuse circuit.NumQubits)

        member _.ApplyOperationAsync _ state _ =
            Task.FromResult(refuse (QuantumState.numQubits state))

/// LocalBackend that declares `maxQubits` as its limit and records the widest state it was
/// asked for, so a test can show a refused problem never reached the simulator.
type LimitedLocalBackend(maxQubits: int) =
    let inner = LocalBackend.LocalBackend() :> IQuantumBackend
    let widest = ref 0

    /// Widest state or circuit requested so far (0 when none).
    member _.WidestRequested = widest.Value

    interface IQuantumBackend with
        member _.Name = $"limited-local-{maxQubits}"
        member _.NativeStateType = inner.NativeStateType
        member _.SupportsOperation op = inner.SupportsOperation op

        member _.InitializeState n =
            widest.Value <- max widest.Value n
            inner.InitializeState n

        member _.ApplyOperation op state = inner.ApplyOperation op state

        member _.ExecuteToState circuit =
            widest.Value <- max widest.Value circuit.NumQubits
            inner.ExecuteToState circuit

        member _.ExecuteToStateAsync circuit ct =
            widest.Value <- max widest.Value circuit.NumQubits
            inner.ExecuteToStateAsync circuit ct

        member _.ApplyOperationAsync op state ct = inner.ApplyOperationAsync op state ct

    interface IQubitLimitedBackend with
        member _.MaxQubits = Some maxQubits

/// Checks on results from the backends above.
module QubitLimitBackends =

    /// True when `result` failed with CapacityStubBackend's error, i.e. validation admitted the problem.
    let reachedBackend (result: Result<'a, QuantumError>) : bool =
        match result with
        | Error(QuantumError.OperationError(_, msg)) -> msg = CapacityStubBackend.Marker
        | _ -> false

    /// The message of a ValidationError result; fails the test on anything else.
    let validationMessage (result: Result<'a, QuantumError>) : string =
        match result with
        | Error(QuantumError.ValidationError(_, msg)) -> msg
        | Error e -> failwith $"Expected a ValidationError, got {e}"
        | Ok _ -> failwith "Expected a ValidationError, got Ok"
