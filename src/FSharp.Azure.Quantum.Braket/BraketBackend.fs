namespace FSharp.Azure.Quantum.Braket

open System
open System.Threading
open System.Threading.Tasks
open Amazon.Braket
open Amazon.Braket.Model
open Amazon.S3
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Algorithms.NeutralAtom

/// AWS Braket execution: submit a task, poll to completion, read the result from S3.
///
/// The AWS SDK dependency is isolated to this plugin. `BraketBackend` is a gate `IQuantumBackend`
/// (OpenQASM 3.0) that works for every Braket gate device by ARN — IonQ, Rigetti, IQM, OQC,
/// Infleqtion, and the SV1/DM1/TN1 simulators. `submitAhsAsync` submits a neutral-atom
/// `RydbergProgram` to QuEra Aquila via the Braket AHS format.
module BraketExecution =

    /// Where Braket writes task results.
    type S3Config = { Bucket: string; KeyPrefix: string }

    let private readS3Async (s3: IAmazonS3) (bucket: string) (key: string) (ct: CancellationToken) : Task<string> =
        task {
            let! response = s3.GetObjectAsync(bucket, key, ct)
            use reader = new System.IO.StreamReader(response.ResponseStream)
            return! reader.ReadToEndAsync ct
        }

    /// Submit a Braket action (OpenQASM or AHS JSON), poll until it completes, and parse the
    /// result JSON fetched from S3. Returns `Error` (not an exception) on any failure.
    let submitActionAsync
        (braket: IAmazonBraket)
        (s3: IAmazonS3)
        (s3Config: S3Config)
        (deviceArn: string)
        (action: string)
        (shots: int)
        (parseResult: string -> Map<string, int>)
        (pollInterval: TimeSpan)
        (timeout: TimeSpan)
        (ct: CancellationToken)
        : Task<Result<Map<string, int>, QuantumError>> =
        task {
            try
                let createRequest =
                    CreateQuantumTaskRequest(
                        Action = action,
                        ClientToken = Guid.NewGuid().ToString(),
                        DeviceArn = deviceArn,
                        OutputS3Bucket = s3Config.Bucket,
                        OutputS3KeyPrefix = s3Config.KeyPrefix,
                        Shots = int64 shots)
                let! createResponse = braket.CreateQuantumTaskAsync(createRequest, ct)
                let taskArn = createResponse.QuantumTaskArn

                // Wall-clock deadline so a task stuck in QUEUED/RUNNING never hangs the caller
                // forever (the synchronous ExecuteToState path passes CancellationToken.None).
                let deadline = DateTime.UtcNow + timeout

                let rec poll () : Task<Result<Map<string, int>, QuantumError>> =
                    task {
                        let! info = braket.GetQuantumTaskAsync(GetQuantumTaskRequest(QuantumTaskArn = taskArn), ct)
                        match info.Status.Value with
                        | "COMPLETED" ->
                            let key = $"%s{info.OutputS3Directory}/results.json"
                            let! json = readS3Async s3 info.OutputS3Bucket key ct
                            try return Ok (parseResult json)
                            with ex -> return Error (QuantumError.OperationError ("Braket", $"Failed to parse Braket result: %s{ex.Message}"))
                        | "FAILED" | "CANCELLED" ->
                            let reason = if isNull info.FailureReason then "" else info.FailureReason
                            return Error (QuantumError.OperationError ("Braket", $"Braket task %s{info.Status.Value}: %s{reason}"))
                        | _ when DateTime.UtcNow > deadline ->
                            return Error (QuantumError.OperationError ("Braket",
                                $"Braket task did not complete within %g{timeout.TotalMinutes} minutes (last status %s{info.Status.Value}); the task %s{taskArn} may still be running."))
                        | _ ->
                            do! Task.Delay(pollInterval, ct)
                            return! poll ()
                    }
                return! poll ()
            with ex ->
                return Error (QuantumError.OperationError ("Braket", $"Braket submission failed (check AWS credentials / device ARN): %s{ex.Message}"))
        }

    /// Largest circuit for which we materialise a DENSE state vector from the histogram.
    /// Must match `StateVector.create`'s 20-qubit limit (2^20 amplitudes).
    [<Literal>]
    let private maxDenseStateQubits = 20

    /// Largest circuit for which we materialise a SPARSE state from the histogram.
    /// `QuantumState.SparseState` keys basis indices as Int32, so 31 qubits is the
    /// representable ceiling. Wider results (Rigetti Ankaa ~84q, QuEra 256 atoms)
    /// are returned as `QuantumState.MeasurementHistogram`, which has no width
    /// limit — it holds at most `shots` entries regardless of qubit count.
    [<Literal>]
    let private maxSparseStateQubits = 31

    /// Basis index for a Braket measurement bitstring.
    /// Braket lists measurements qubit-0-first (leftmost char = qubit 0), but the
    /// library's simulators use qubit j = bit j (qubit 0 = LSB). Reverse the string
    /// so the reconstructed basis index matches LocalBackend for the same circuit;
    /// without this, every n >= 2 qubit state comes back bit-reversed.
    let private bitstringToIndex (bitstring: string) : int option =
        try Some (Convert.ToInt32(String(Array.rev (bitstring.ToCharArray())), 2))
        with _ -> None

    /// Build a `QuantumState` from a measurement histogram. Three tiers:
    /// - dense StateVector up to 20 qubits (amplitudes = √p, phases unknowable from counts)
    /// - SparseState up to 31 qubits (only observed outcomes carry amplitude)
    /// - MeasurementHistogram beyond that — the honest sampled-data representation
    ///   with NO width limit (≤ shots entries regardless of qubit count)
    let private histogramToState (histogram: Map<string, int>) (numQubits: int) : Result<QuantumState, QuantumError> =
        if numQubits > maxSparseStateQubits then
            // Braket's bitstring order (leftmost char = qubit 0) IS the
            // MeasurementHistogram key convention — pass through unchanged.
            Ok (QuantumState.MeasurementHistogram (histogram, numQubits))
        elif numQubits > maxDenseStateQubits then
            // Sparse reconstruction: only the observed outcomes carry amplitude.
            let total = histogram |> Map.fold (fun acc _ count -> acc + count) 0 |> max 1
            let amplitudes =
                histogram
                |> Map.toSeq
                |> Seq.choose (fun (bitstring, count) ->
                    bitstringToIndex bitstring
                    |> Option.map (fun index ->
                        (index, System.Numerics.Complex(sqrt (float count / float total), 0.0))))
                |> Map.ofSeq
            Ok (QuantumState.SparseState (amplitudes, numQubits))
        else
            let total = histogram |> Map.fold (fun acc _ count -> acc + count) 0 |> max 1
            let dim = 1 <<< numQubits
            let amplitudes = Array.create dim System.Numerics.Complex.Zero
            histogram
            |> Map.iter (fun bitstring count ->
                match bitstringToIndex bitstring with
                | Some index when index >= 0 && index < dim ->
                    amplitudes.[index] <- System.Numerics.Complex(sqrt (float count / float total), 0.0)
                | _ -> ())
            Ok (QuantumState.StateVector (StateVector.create amplitudes))

    /// Submit a neutral-atom `RydbergProgram` to a Braket AHS device (QuEra Aquila) and return
    /// the Rydberg-occupation histogram.
    let submitAhsAsync
        (braket: IAmazonBraket)
        (s3: IAmazonS3)
        (s3Config: S3Config)
        (deviceArn: string)
        (program: RydbergProgram)
        (shots: int)
        (ct: CancellationToken)
        : Task<Result<Map<string, int>, QuantumError>> =
        submitActionAsync braket s3 s3Config deviceArn (QuEra.toAhsProgram program) shots QuEra.parseAhsResult (TimeSpan.FromSeconds 3.0) (TimeSpan.FromMinutes 30.0) ct

    /// A gate `IQuantumBackend` backed by an AWS Braket device (submits OpenQASM 3.0).
    /// `deviceArn` selects the device — e.g. `Braket.Devices.oqcLucy`, `.infleqtionSqale`,
    /// `.ionqAria1`, `.sv1`.
    type BraketBackend(braket: IAmazonBraket, s3: IAmazonS3, s3Config: S3Config, deviceArn: string, ?shots: int) =

        let shots = defaultArg shots 1000

        let circuitToOpenQasm3 (circuit: ICircuit) : Result<string, QuantumError> =
            match CircuitAdapter.tryGetCircuit circuit with
            | Some builderCircuit ->
                try Ok (OpenQasm.exportV3 builderCircuit)
                with ex -> Error (QuantumError.OperationError ("OpenQASM3 export", ex.Message))
            | None ->
                Error (QuantumError.OperationError ("Circuit extraction", "Braket requires a gate circuit; wrap a CircuitBuilder.Circuit with CircuitWrapper."))

        /// Run a circuit and return the raw measurement histogram (bitstring → count,
        /// Braket qubit-0-first bit order). This is the natural result format for
        /// cloud-scale circuits: the histogram holds at most `shots` entries regardless
        /// of qubit count, so there is NO width limit here — unlike the QuantumState
        /// reconstruction in ExecuteToState (dense ≤ 20 qubits, sparse ≤ 31).
        member _.ExecuteToHistogramAsync (circuit: ICircuit, ct: CancellationToken) : Task<Result<Map<string, int>, QuantumError>> =
            (circuitToOpenQasm3 circuit) |> Result.map (fun source -> submitActionAsync braket s3 s3Config deviceArn (Braket.openQasmAction source) shots Braket.parseGateResult (TimeSpan.FromSeconds 3.0) (TimeSpan.FromMinutes 30.0) ct) |> Result.defaultWith (fun e -> Task.FromResult (Error e))

        interface IQuantumBackend with

            member _.Name = $"AWS Braket (%s{deviceArn})"

            member _.NativeStateType = QuantumStateType.GateBased

            member this.ExecuteToState (circuit: ICircuit) : Result<QuantumState, QuantumError> =
                (this :> IQuantumBackend).ExecuteToStateAsync circuit CancellationToken.None
                |> Async.AwaitTask
                |> Async.RunSynchronously

            member _.ExecuteToStateAsync (circuit: ICircuit) (ct: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                task {
                    match circuitToOpenQasm3 circuit with
                    | Error e -> return Error e
                    | Ok source ->
                        let! result =
                            submitActionAsync braket s3 s3Config deviceArn (Braket.openQasmAction source) shots Braket.parseGateResult (TimeSpan.FromSeconds 3.0) (TimeSpan.FromMinutes 30.0) ct
                        return result |> Result.bind (fun histogram -> histogramToState histogram circuit.NumQubits)
                }

            member _.InitializeState (numQubits: int) : Result<QuantumState, QuantumError> =
                Ok (QuantumState.StateVector (StateVector.init numQubits))

            member _.ApplyOperation (_op: QuantumOperation) (_state: QuantumState) : Result<QuantumState, QuantumError> =
                Error (QuantumError.OperationError ("ApplyOperation",
                    $"AWS Braket (%s{deviceArn}) does not support incremental ApplyOperation; use ExecuteToState with a complete circuit."))

            member this.ApplyOperationAsync (op: QuantumOperation) (state: QuantumState) (_ct: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                task { return (this :> IQuantumBackend).ApplyOperation op state }

            member _.SupportsOperation (op: QuantumOperation) : bool =
                match op with
                | QuantumOperation.Gate _ | QuantumOperation.Sequence _ -> true
                | _ -> false
