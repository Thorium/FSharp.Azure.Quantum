namespace FSharp.Azure.Quantum.Backends

open System
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.Types
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.LocalSimulator

/// Cloud backend wrappers that implement IQuantumBackend for real quantum hardware.
///
/// These classes bridge the gap between the utility modules (RigettiBackend, IonQBackend,
/// QuantinuumBackend, AtomComputingBackend) and the unified IQuantumBackend interface,
/// enabling ML/QAOA pipelines to run on real cloud hardware.
///
/// Each wrapper:
/// 1. Accepts an ICircuit and converts it to the hardware-specific format
/// 2. Submits the job via JobLifecycle (task-based async)
/// 3. Polls for completion
/// 4. Parses the histogram result back into a QuantumState.StateVector
///
/// Architecture:
///   ICircuit → CircuitAdapter.toXxxFormat → submitAndWaitForResultsAsync → histogram → QuantumState
module CloudBackends =

    // ============================================================================
    // RIGETTI CLOUD BACKEND
    // ============================================================================

    /// IQuantumBackend implementation for Rigetti hardware via Azure Quantum.
    ///
    /// Converts ICircuit to Quil assembly, submits to rigetti.sim.qvm or rigetti.qpu.*,
    /// and returns approximate QuantumState from measurement histogram.
    ///
    /// Parameters:
    ///   httpClient   - Authenticated HttpClient for Azure Quantum API
    ///   workspaceUrl - Azure Quantum workspace URL
    ///   target       - Rigetti target (e.g., "rigetti.sim.qvm", "rigetti.qpu.ankaa-3")
    ///   shots        - Number of measurement shots (default 1000)
    ///   timeout      - Job timeout (default 5 minutes)
    type RigettiCloudBackend
        (
            httpClient: HttpClient,
            workspaceUrl: string,
            target: string,
            ?shots: int,
            ?timeout: TimeSpan
        ) =

        let shots = defaultArg shots 1000
        let timeout = defaultArg timeout (TimeSpan.FromMinutes(5.0))

        interface IQuantumBackend with

            member _.Name = sprintf "Rigetti Cloud (%s)" target

            member _.NativeStateType = QuantumStateType.GateBased

            member this.ExecuteToState (circuit: ICircuit) : Result<QuantumState, QuantumError> =
                (this :> IQuantumBackend).ExecuteToStateAsync circuit CancellationToken.None
                |> Async.AwaitTask
                |> Async.RunSynchronously

            member _.ExecuteToStateAsync (circuit: ICircuit) (cancellationToken: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                task {
                    // Step 1: Convert ICircuit → QuilProgram
                    match CircuitAdapter.toQuilProgram circuit with
                    | Error err -> return Error err
                    | Ok quilProgram ->
                        // Step 2: Submit and wait for results
                        let submission = RigettiBackend.createJobSubmission quilProgram shots target None
                        let! submitResult = JobLifecycle.submitJobAsync httpClient workspaceUrl submission
                        match submitResult with
                        | Error err -> return Error err
                        | Ok jobId ->
                            let! pollResult = JobLifecycle.pollJobUntilCompleteAsync httpClient workspaceUrl jobId timeout cancellationToken
                            match pollResult with
                            | Error err -> return Error err
                            | Ok job ->
                                match job.Status with
                                | JobStatus.Succeeded ->
                                    match job.OutputDataUri with
                                    | None ->
                                        return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(500, "Job completed but no output URI available")))
                                    | Some uri ->
                                        let! resultData = JobLifecycle.getJobResultAsync httpClient uri
                                        match resultData with
                                        | Error err -> return Error err
                                        | Ok jobResult ->
                                            try
                                                let resultJson = jobResult.OutputData :?> string
                                                match RigettiBackend.parseRigettiResults resultJson with
                                                | Error err -> return Error err
                                                | Ok histogram ->
                                                    let numQubits =
                                                        CloudBackendHelpers.inferNumQubits histogram
                                                        |> Option.defaultValue circuit.NumQubits
                                                    return Ok (CloudBackendHelpers.histogramToQuantumState histogram numQubits)
                                            with
                                            | ex ->
                                                return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Failed to parse Rigetti results: %s" ex.Message)))
                                | JobStatus.Failed (errorCode, errorMessage) ->
                                    return Error (RigettiBackend.mapRigettiError errorCode errorMessage)
                                | JobStatus.Cancelled ->
                                    return Error (QuantumError.OperationError("Job execution", "Operation cancelled"))
                                | _ ->
                                    return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Unexpected job status: %A" job.Status)))
                }

            member _.InitializeState (numQubits: int) : Result<QuantumState, QuantumError> =
                Ok (QuantumState.StateVector (StateVector.init numQubits))

            member this.ApplyOperation (op: QuantumOperation) (state: QuantumState) : Result<QuantumState, QuantumError> =
                let iface = (this :> IQuantumBackend)
                iface.ApplyOperationAsync op state CancellationToken.None
                |> Async.AwaitTask
                |> Async.RunSynchronously

            member _.ApplyOperationAsync (op: QuantumOperation) (_state: QuantumState) (cancellationToken: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                task {
                    // Cloud backends don't support incremental state operations efficiently.
                    // For single-operation application, we'd need to reconstruct the full circuit.
                    // This is a fundamental limitation of cloud hardware: you can't "apply one more gate"
                    // to an existing remote quantum state.
                    return Error (QuantumError.OperationError(
                        "ApplyOperation",
                        sprintf "Rigetti Cloud (%s) does not support incremental ApplyOperation. Use ExecuteToState with a complete circuit instead." target))
                }

            member _.SupportsOperation (op: QuantumOperation) : bool =
                CloudBackendHelpers.isCloudSupportedOperation op

        interface IQubitLimitedBackend with
            member _.MaxQubits =
                // Rigetti QVM simulator: effectively unlimited for small circuits
                // Rigetti QPU Ankaa-3: 84 qubits
                if target.Contains("qpu") then Some 84
                elif target.Contains("sim") then Some 20  // StateVector.create limit
                else None

    // ============================================================================
    // IONQ CLOUD BACKEND
    // ============================================================================

    /// IQuantumBackend implementation for IonQ hardware via Azure Quantum.
    ///
    /// Converts ICircuit to IonQ JSON format, submits to ionq.simulator or ionq.qpu.*,
    /// and returns approximate QuantumState from measurement histogram.
    ///
    /// Parameters:
    ///   httpClient   - Authenticated HttpClient for Azure Quantum API
    ///   workspaceUrl - Azure Quantum workspace URL
    ///   target       - IonQ target (e.g., "ionq.simulator", "ionq.qpu.aria-1")
    ///   shots        - Number of measurement shots (default 1000)
    ///   timeout      - Job timeout (default 5 minutes)
    type IonQCloudBackend
        (
            httpClient: HttpClient,
            workspaceUrl: string,
            target: string,
            ?shots: int,
            ?timeout: TimeSpan
        ) =

        let shots = defaultArg shots 1000
        let timeout = defaultArg timeout (TimeSpan.FromMinutes(5.0))

        interface IQuantumBackend with

            member _.Name = sprintf "IonQ Cloud (%s)" target

            member _.NativeStateType = QuantumStateType.GateBased

            member this.ExecuteToState (circuit: ICircuit) : Result<QuantumState, QuantumError> =
                (this :> IQuantumBackend).ExecuteToStateAsync circuit CancellationToken.None
                |> Async.AwaitTask
                |> Async.RunSynchronously

            member _.ExecuteToStateAsync (circuit: ICircuit) (cancellationToken: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                task {
                    // Step 1: Convert ICircuit → IonQCircuit
                    match CircuitAdapter.toIonQCircuit circuit with
                    | Error err -> return Error err
                    | Ok ionqCircuit ->
                        // Step 2: Submit and wait for results
                        let submission = IonQBackend.createJobSubmission ionqCircuit shots target
                        let! submitResult = JobLifecycle.submitJobAsync httpClient workspaceUrl submission
                        match submitResult with
                        | Error err -> return Error err
                        | Ok jobId ->
                            let! pollResult = JobLifecycle.pollJobUntilCompleteAsync httpClient workspaceUrl jobId timeout cancellationToken
                            match pollResult with
                            | Error err -> return Error err
                            | Ok job ->
                                match job.Status with
                                | JobStatus.Succeeded ->
                                    match job.OutputDataUri with
                                    | None ->
                                        return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(500, "Job completed but no output URI available")))
                                    | Some uri ->
                                        let! resultData = JobLifecycle.getJobResultAsync httpClient uri
                                        match resultData with
                                        | Error err -> return Error err
                                        | Ok jobResult ->
                                            try
                                                let resultJson = jobResult.OutputData :?> string
                                                let histogram = IonQBackend.parseIonQResult resultJson
                                                let numQubits =
                                                    CloudBackendHelpers.inferNumQubits histogram
                                                    |> Option.defaultValue circuit.NumQubits
                                                return Ok (CloudBackendHelpers.histogramToQuantumState histogram numQubits)
                                            with
                                            | ex ->
                                                return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Failed to parse IonQ results: %s" ex.Message)))
                                | JobStatus.Failed (errorCode, errorMessage) ->
                                    return Error (IonQBackend.mapIonQError errorCode errorMessage)
                                | JobStatus.Cancelled ->
                                    return Error (QuantumError.OperationError("Job execution", "Operation cancelled"))
                                | _ ->
                                    return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Unexpected job status: %A" job.Status)))
                }

            member _.InitializeState (numQubits: int) : Result<QuantumState, QuantumError> =
                Ok (QuantumState.StateVector (StateVector.init numQubits))

            member this.ApplyOperation (op: QuantumOperation) (state: QuantumState) : Result<QuantumState, QuantumError> =
                let iface = (this :> IQuantumBackend)
                iface.ApplyOperationAsync op state CancellationToken.None
                |> Async.AwaitTask
                |> Async.RunSynchronously

            member _.ApplyOperationAsync (op: QuantumOperation) (_state: QuantumState) (cancellationToken: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                task {
                    return Error (QuantumError.OperationError(
                        "ApplyOperation",
                        sprintf "IonQ Cloud (%s) does not support incremental ApplyOperation. Use ExecuteToState with a complete circuit instead." target))
                }

            member _.SupportsOperation (op: QuantumOperation) : bool =
                CloudBackendHelpers.isCloudSupportedOperation op

        interface IQubitLimitedBackend with
            member _.MaxQubits =
                // IonQ simulator: 29 qubits
                // IonQ Aria-1: 25 qubits
                // IonQ Forte: 36 qubits
                if target.Contains("aria") then Some 25
                elif target.Contains("forte") then Some 36
                elif target.Contains("simulator") then Some 20  // StateVector.create limit
                else None

    // ============================================================================
    // QUANTINUUM CLOUD BACKEND
    // ============================================================================

    /// IQuantumBackend implementation for Quantinuum H-Series via Azure Quantum.
    ///
    /// Converts ICircuit to OpenQASM 2.0, submits to quantinuum.sim.* or quantinuum.qpu.*,
    /// and returns approximate QuantumState from measurement histogram.
    ///
    /// Parameters:
    ///   httpClient   - Authenticated HttpClient for Azure Quantum API
    ///   workspaceUrl - Azure Quantum workspace URL
    ///   target       - Quantinuum target (e.g., "quantinuum.sim.h1-1sc", "quantinuum.qpu.h1-1")
    ///   shots        - Number of measurement shots (default 1000)
    ///   timeout      - Job timeout (default 5 minutes)
    type QuantinuumCloudBackend
        (
            httpClient: HttpClient,
            workspaceUrl: string,
            target: string,
            ?shots: int,
            ?timeout: TimeSpan
        ) =

        let shots = defaultArg shots 1000
        let timeout = defaultArg timeout (TimeSpan.FromMinutes(5.0))

        /// Convert ICircuit to OpenQASM 2.0 string for Quantinuum.
        let circuitToOpenQasm (circuit: ICircuit) : Result<string, QuantumError> =
            match CircuitAdapter.tryGetCircuit circuit with
            | Some builderCircuit ->
                try
                    Ok (FSharp.Azure.Quantum.OpenQasmExport.export builderCircuit)
                with ex ->
                    Error (QuantumError.OperationError("OpenQASM export", sprintf "Failed to export circuit to OpenQASM: %s" ex.Message))
            | None ->
                // Try QAOA circuit path
                match CircuitAdapter.tryGetQaoaCircuit circuit with
                | Some qaoaCircuit ->
                    try
                        Ok (QaoaCircuit.toOpenQasm qaoaCircuit)
                    with ex ->
                        Error (QuantumError.OperationError("OpenQASM export", sprintf "Failed to export QAOA circuit to OpenQASM: %s" ex.Message))
                | None ->
                    Error (QuantumError.OperationError("Circuit extraction", "Cannot extract circuit from ICircuit wrapper for OpenQASM export"))

        interface IQuantumBackend with

            member _.Name = sprintf "Quantinuum Cloud (%s)" target

            member _.NativeStateType = QuantumStateType.GateBased

            member this.ExecuteToState (circuit: ICircuit) : Result<QuantumState, QuantumError> =
                (this :> IQuantumBackend).ExecuteToStateAsync circuit CancellationToken.None
                |> Async.AwaitTask
                |> Async.RunSynchronously

            member _.ExecuteToStateAsync (circuit: ICircuit) (cancellationToken: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                task {
                    // Step 1: Convert ICircuit → OpenQASM 2.0 string
                    match circuitToOpenQasm circuit with
                    | Error err -> return Error err
                    | Ok qasmCode ->
                        // Step 2: Submit and wait for results
                        let submission = QuantinuumBackend.createJobSubmission qasmCode shots target
                        let! submitResult = JobLifecycle.submitJobAsync httpClient workspaceUrl submission
                        match submitResult with
                        | Error err -> return Error err
                        | Ok jobId ->
                            let! pollResult = JobLifecycle.pollJobUntilCompleteAsync httpClient workspaceUrl jobId timeout cancellationToken
                            match pollResult with
                            | Error err -> return Error err
                            | Ok job ->
                                match job.Status with
                                | JobStatus.Succeeded ->
                                    match job.OutputDataUri with
                                    | None ->
                                        return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(500, "Job completed but no output URI available")))
                                    | Some uri ->
                                        let! resultData = JobLifecycle.getJobResultAsync httpClient uri
                                        match resultData with
                                        | Error err -> return Error err
                                        | Ok jobResult ->
                                            try
                                                let resultJson = jobResult.OutputData :?> string
                                                let histogram = QuantinuumBackend.parseQuantinuumResult resultJson
                                                let numQubits =
                                                    CloudBackendHelpers.inferNumQubits histogram
                                                    |> Option.defaultValue circuit.NumQubits
                                                return Ok (CloudBackendHelpers.histogramToQuantumState histogram numQubits)
                                            with
                                            | ex ->
                                                return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Failed to parse Quantinuum results: %s" ex.Message)))
                                | JobStatus.Failed (errorCode, errorMessage) ->
                                    return Error (QuantinuumBackend.mapQuantinuumError errorCode errorMessage)
                                | JobStatus.Cancelled ->
                                    return Error (QuantumError.OperationError("Job execution", "Operation cancelled"))
                                | _ ->
                                    return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Unexpected job status: %A" job.Status)))
                }

            member _.InitializeState (numQubits: int) : Result<QuantumState, QuantumError> =
                Ok (QuantumState.StateVector (StateVector.init numQubits))

            member this.ApplyOperation (op: QuantumOperation) (state: QuantumState) : Result<QuantumState, QuantumError> =
                let iface = (this :> IQuantumBackend)
                iface.ApplyOperationAsync op state CancellationToken.None
                |> Async.AwaitTask
                |> Async.RunSynchronously

            member _.ApplyOperationAsync (op: QuantumOperation) (_state: QuantumState) (cancellationToken: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                task {
                    return Error (QuantumError.OperationError(
                        "ApplyOperation",
                        sprintf "Quantinuum Cloud (%s) does not support incremental ApplyOperation. Use ExecuteToState with a complete circuit instead." target))
                }

            member _.SupportsOperation (op: QuantumOperation) : bool =
                CloudBackendHelpers.isCloudSupportedOperation op

        interface IQubitLimitedBackend with
            member _.MaxQubits =
                // Quantinuum H1-1SC simulator: 32 qubits
                // Quantinuum H1-1 hardware: 32 qubits
                // Quantinuum H2: 56 qubits
                if target.Contains("h2") then Some 56
                elif target.Contains("h1") then Some 32
                else Some 20  // Conservative default, StateVector limit

    // ============================================================================
    // ATOM COMPUTING CLOUD BACKEND
    // ============================================================================

    /// IQuantumBackend implementation for Atom Computing Phoenix via Azure Quantum.
    ///
    /// Converts ICircuit to OpenQASM 2.0, submits to atom-computing.sim or atom-computing.qpu.*,
    /// and returns approximate QuantumState from measurement histogram.
    ///
    /// Parameters:
    ///   httpClient   - Authenticated HttpClient for Azure Quantum API
    ///   workspaceUrl - Azure Quantum workspace URL
    ///   target       - Atom Computing target (e.g., "atom-computing.sim", "atom-computing.qpu.phoenix")
    ///   shots        - Number of measurement shots (default 1000)
    ///   timeout      - Job timeout (default 10 minutes, longer for neutral atom hardware)
    type AtomComputingCloudBackend
        (
            httpClient: HttpClient,
            workspaceUrl: string,
            target: string,
            ?shots: int,
            ?timeout: TimeSpan
        ) =

        let shots = defaultArg shots 1000
        let timeout = defaultArg timeout (TimeSpan.FromMinutes(10.0))  // Longer default for Atom Computing

        /// Convert ICircuit to OpenQASM 2.0 string for Atom Computing.
        let circuitToOpenQasm (circuit: ICircuit) : Result<string, QuantumError> =
            match CircuitAdapter.tryGetCircuit circuit with
            | Some builderCircuit ->
                try
                    Ok (FSharp.Azure.Quantum.OpenQasmExport.export builderCircuit)
                with ex ->
                    Error (QuantumError.OperationError("OpenQASM export", sprintf "Failed to export circuit to OpenQASM: %s" ex.Message))
            | None ->
                match CircuitAdapter.tryGetQaoaCircuit circuit with
                | Some qaoaCircuit ->
                    try
                        Ok (QaoaCircuit.toOpenQasm qaoaCircuit)
                    with ex ->
                        Error (QuantumError.OperationError("OpenQASM export", sprintf "Failed to export QAOA circuit to OpenQASM: %s" ex.Message))
                | None ->
                    Error (QuantumError.OperationError("Circuit extraction", "Cannot extract circuit from ICircuit wrapper for OpenQASM export"))

        interface IQuantumBackend with

            member _.Name = sprintf "Atom Computing Cloud (%s)" target

            member _.NativeStateType = QuantumStateType.GateBased

            member this.ExecuteToState (circuit: ICircuit) : Result<QuantumState, QuantumError> =
                (this :> IQuantumBackend).ExecuteToStateAsync circuit CancellationToken.None
                |> Async.AwaitTask
                |> Async.RunSynchronously

            member _.ExecuteToStateAsync (circuit: ICircuit) (cancellationToken: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                task {
                    // Step 1: Convert ICircuit → OpenQASM 2.0 string
                    match circuitToOpenQasm circuit with
                    | Error err -> return Error err
                    | Ok qasmCode ->
                        // Step 2: Submit and wait for results
                        let submission = AtomComputingBackend.createJobSubmission qasmCode shots target
                        let! submitResult = JobLifecycle.submitJobAsync httpClient workspaceUrl submission
                        match submitResult with
                        | Error err -> return Error err
                        | Ok jobId ->
                            let! pollResult = JobLifecycle.pollJobUntilCompleteAsync httpClient workspaceUrl jobId timeout cancellationToken
                            match pollResult with
                            | Error err -> return Error err
                            | Ok job ->
                                match job.Status with
                                | JobStatus.Succeeded ->
                                    match job.OutputDataUri with
                                    | None ->
                                        return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(500, "Job completed but no output URI available")))
                                    | Some uri ->
                                        let! resultData = JobLifecycle.getJobResultAsync httpClient uri
                                        match resultData with
                                        | Error err -> return Error err
                                        | Ok jobResult ->
                                            try
                                                let resultJson = jobResult.OutputData :?> string
                                                let histogram = AtomComputingBackend.parseAtomComputingResult resultJson
                                                let numQubits =
                                                    CloudBackendHelpers.inferNumQubits histogram
                                                    |> Option.defaultValue circuit.NumQubits
                                                return Ok (CloudBackendHelpers.histogramToQuantumState histogram numQubits)
                                            with
                                            | ex ->
                                                return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Failed to parse Atom Computing results: %s" ex.Message)))
                                | JobStatus.Failed (errorCode, errorMessage) ->
                                    return Error (AtomComputingBackend.mapAtomComputingError errorCode errorMessage)
                                | JobStatus.Cancelled ->
                                    return Error (QuantumError.OperationError("Job execution", "Operation cancelled"))
                                | _ ->
                                    return Error (QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "Unexpected job status: %A" job.Status)))
                }

            member _.InitializeState (numQubits: int) : Result<QuantumState, QuantumError> =
                Ok (QuantumState.StateVector (StateVector.init numQubits))

            member this.ApplyOperation (op: QuantumOperation) (state: QuantumState) : Result<QuantumState, QuantumError> =
                let iface = (this :> IQuantumBackend)
                iface.ApplyOperationAsync op state CancellationToken.None
                |> Async.AwaitTask
                |> Async.RunSynchronously

            member _.ApplyOperationAsync (op: QuantumOperation) (_state: QuantumState) (cancellationToken: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                task {
                    return Error (QuantumError.OperationError(
                        "ApplyOperation",
                        sprintf "Atom Computing Cloud (%s) does not support incremental ApplyOperation. Use ExecuteToState with a complete circuit instead." target))
                }

            member _.SupportsOperation (op: QuantumOperation) : bool =
                CloudBackendHelpers.isCloudSupportedOperation op

        interface IQubitLimitedBackend with
            member _.MaxQubits =
                // Atom Computing Phoenix: 100+ qubits
                // Simulator: limited by state vector size
                if target.Contains("qpu") then Some 100
                elif target.Contains("sim") then Some 20  // StateVector.create limit
                else None

    // ============================================================================
    // FACTORY MODULE
    // ============================================================================

    /// Factory functions for creating cloud backend instances.
    ///
    /// These are convenience functions that create properly configured
    /// cloud backends for use with ML/QAOA pipelines.
    module CloudBackendFactory =

        /// Create a Rigetti cloud backend.
        let createRigetti (httpClient: HttpClient) (workspaceUrl: string) (target: string) (shots: int) : IQuantumBackend =
            RigettiCloudBackend(httpClient, workspaceUrl, target, shots) :> IQuantumBackend

        /// Create an IonQ cloud backend.
        let createIonQ (httpClient: HttpClient) (workspaceUrl: string) (target: string) (shots: int) : IQuantumBackend =
            IonQCloudBackend(httpClient, workspaceUrl, target, shots) :> IQuantumBackend

        /// Create a Quantinuum cloud backend.
        let createQuantinuum (httpClient: HttpClient) (workspaceUrl: string) (target: string) (shots: int) : IQuantumBackend =
            QuantinuumCloudBackend(httpClient, workspaceUrl, target, shots) :> IQuantumBackend

        /// Create an Atom Computing cloud backend.
        let createAtomComputing (httpClient: HttpClient) (workspaceUrl: string) (target: string) (shots: int) : IQuantumBackend =
            AtomComputingCloudBackend(httpClient, workspaceUrl, target, shots) :> IQuantumBackend
