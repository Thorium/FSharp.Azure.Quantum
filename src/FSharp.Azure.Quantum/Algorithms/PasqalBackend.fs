namespace FSharp.Azure.Quantum.Algorithms

open System
open System.Text.Json
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.Types
open FSharp.Azure.Quantum.Algorithms.NeutralAtom

/// Pasqal neutral-atom QPU integration for Azure Quantum.
///
/// Pasqal is an **analog** neutral-atom device: it does not run gate circuits, it runs a
/// Pulser pulse sequence over an atom register. This module compiles a `RydbergProgram`
/// (the analog program from the `NeutralAtom` module) into the **Pulser abstract
/// representation** JSON and submits it to Azure Quantum Pasqal targets
/// ("pasqal.sim.emu-tn" emulator, "pasqal.qpu.fresnel" hardware).
///
/// So a single `RydbergProgram` has two execution paths: `NeutralAtom.simulate` (Trotterized
/// onto any gate backend / the local simulator) or `Pasqal.submitAndWaitForResultsAsync`
/// (native analog execution on Pasqal hardware).
///
/// NOTE ON UNITS/SCHEMA: register coordinates are written in µm, pulse durations rounded to
/// integer ns, and Ω/Δ passed through as given (rad/µs on hardware). The emitted JSON targets
/// Pulser's versioned abstract representation; align the `version`/`device`/param names with
/// your Pulser/Azure Pasqal version if the provider schema has moved on.
module Pasqal =

    // ========================================================================
    // COMPILE: RydbergProgram -> Pulser abstract-representation JSON
    // ========================================================================

    /// Serialize a `RydbergProgram` to a Pulser abstract-representation JSON string.
    /// Pure and dependency-free — useful on its own to hand a program to Pulser/Pasqal tools.
    let toPulserJson (program: RydbergProgram) : string =
        use stream = new System.IO.MemoryStream()
        (
            use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
            writer.WriteStartObject()
            writer.WriteString("version", "1")
            writer.WriteString("device", "AnalogDevice")

            // Atom register (coordinates in µm).
            writer.WriteStartArray "register"
            program.Register
            |> List.iteri (fun i atom ->
                writer.WriteStartObject()
                writer.WriteString("name", $"q%d{i}")
                writer.WriteNumber("x", atom.X)
                writer.WriteNumber("y", atom.Y)
                writer.WriteEndObject())
            writer.WriteEndArray()

            // A single global Rydberg channel drives every atom.
            writer.WriteStartObject "channels"
            writer.WriteString("rydberg_global", "rydberg_global")
            writer.WriteEndObject()

            writer.WriteStartObject "variables"
            writer.WriteEndObject()

            // Each pulse segment becomes a ramped pulse on the global channel.
            writer.WriteStartArray "operations"
            program.Schedule
            |> List.iter (fun segment ->
                // RydbergProgram durations are in microseconds (Ω is rad/µs); Pulser wants integer
                // nanoseconds, so convert µs→ns (×1000). Floor at the 4 ns device clock period.
                let durationNs = max 4 (int (Math.Round (segment.Duration * 1000.0)))
                let ramp (name: string) (start: float) (stop: float) =
                    writer.WriteStartObject name
                    writer.WriteString("kind", "ramp")
                    writer.WriteNumber("duration", durationNs)
                    writer.WriteNumber("start", start)
                    writer.WriteNumber("stop", stop)
                    writer.WriteEndObject()
                writer.WriteStartObject()
                writer.WriteString("op", "pulse")
                writer.WriteString("channel", "rydberg_global")
                writer.WriteString("protocol", "min-delay")
                ramp "amplitude" segment.RabiStart segment.RabiEnd
                ramp "detuning" segment.DetuningStart segment.DetuningEnd
                writer.WriteNumber("phase", 0.0)
                writer.WriteNumber("post_phase_shift", 0.0)
                writer.WriteEndObject())
            writer.WriteEndArray()

            writer.WriteString("measurement", "ground-rydberg")
            writer.WriteEndObject()
            writer.Flush()
        )
        System.Text.Encoding.UTF8.GetString(stream.ToArray())

    // ========================================================================
    // JOB SUBMISSION
    // ========================================================================

    /// Create an Azure Quantum job submission for a Pulser sequence.
    let createJobSubmission (pulserJson: string) (shots: int) (target: string) : JobSubmission =
        {
            JobId = Guid.NewGuid().ToString()
            Target = target
            Name = Some ($"Pasqal-%s{target}")
            InputData = pulserJson :> obj
            InputDataFormat = CircuitFormat.Custom "pasqal.pulser.abstract-repr.v1"
            InputParams = Map [ ("count", shots :> obj) ]
            Tags = Map.empty
        }

    /// Parse a Pasqal result JSON into a measurement histogram (bitstring → count). Accepts a
    /// "results" wrapper or a bare histogram object. Throws on malformed JSON — the caller wraps.
    let parsePasqalResult (jsonResult: string) : Map<string, int> =
        use doc = JsonDocument.Parse(jsonResult)
        let root = doc.RootElement
        let histogram =
            match root.TryGetProperty "results" with
            | true, element -> element
            | false, _ ->
                match root.TryGetProperty "counts" with
                | true, element -> element
                | false, _ -> root
        histogram.EnumerateObject()
        |> Seq.map (fun p -> p.Name, p.Value.GetInt32())
        |> Map.ofSeq

    /// Map Pasqal error codes to QuantumError.
    let mapPasqalError (errorCode: string) (errorMessage: string) : QuantumError =
        match errorCode with
        | "InvalidSequence" -> QuantumError.ValidationError ("pulser", errorMessage)
        | "TooManyAtoms" -> QuantumError.ValidationError ("register", $"Register too large: %s{errorMessage}")
        | "QuotaExceeded" -> QuantumError.AzureError (AzureQuantumError.QuotaExceeded errorMessage)
        | "BackendUnavailable" -> QuantumError.AzureError (AzureQuantumError.ServiceUnavailable (Some (TimeSpan.FromMinutes 5.0)))
        | _ -> QuantumError.AzureError (AzureQuantumError.UnknownError (0, $"Pasqal error: %s{errorCode} - %s{errorMessage}"))

    // ========================================================================
    // SUBMIT + WAIT
    // ========================================================================

    /// Compile a `RydbergProgram` to Pulser, submit it to a Pasqal target on Azure Quantum,
    /// wait for completion, and return the measurement histogram.
    ///
    /// Parameters:
    /// - httpClient   : authenticated HttpClient for the Azure Quantum API
    /// - workspaceUrl : Azure Quantum workspace URL
    /// - program      : the neutral-atom analog program to run
    /// - shots        : number of measurement shots
    /// - target       : e.g. "pasqal.sim.emu-tn" or "pasqal.qpu.fresnel"
    /// - cancellationToken : caller's token; honored by the polling loop
    let submitAndWaitForResultsAsync
        (httpClient: System.Net.Http.HttpClient)
        (workspaceUrl: string)
        (program: RydbergProgram)
        (shots: int)
        (target: string)
        (cancellationToken: System.Threading.CancellationToken)
        : Task<Result<Map<string, int>, QuantumError>> =
        task {
            let submission = createJobSubmission (toPulserJson program) shots target
            match! JobLifecycle.submitJobAsync httpClient workspaceUrl submission with
            | Error err -> return Error err
            | Ok jobId ->
                let timeout = TimeSpan.FromMinutes 10.0   // neutral-atom jobs can queue a while
                match! JobLifecycle.pollJobUntilCompleteAsync httpClient workspaceUrl jobId timeout cancellationToken with
                | Error err -> return Error err
                | Ok (job: QuantumJob) ->
                    match job.Status with
                    | JobStatus.Succeeded ->
                        match job.OutputDataUri with
                        | None ->
                            return Error (QuantumError.AzureError (AzureQuantumError.UnknownError (500, "Job completed but no output URI available")))
                        | Some uri ->
                            match! JobLifecycle.getJobResultAsync httpClient uri with
                            | Error err -> return Error err
                            | Ok jobResult ->
                                try
                                    let resultJson = jobResult.OutputData :?> string
                                    return Ok (parsePasqalResult resultJson)
                                with ex ->
                                    return Error (QuantumError.AzureError (AzureQuantumError.UnknownError (0, $"Failed to parse Pasqal results: %s{ex.Message}")))
                    | JobStatus.Failed (errorCode, errorMessage) ->
                        return Error (mapPasqalError errorCode errorMessage)
                    | JobStatus.Cancelled ->
                        return Error (QuantumError.OperationError ("Job execution", "Operation cancelled"))
                    | JobStatus.Waiting | JobStatus.Executing ->
                        return Error (QuantumError.AzureError (AzureQuantumError.UnknownError (0, $"Unexpected job status: %A{job.Status}")))
        }
