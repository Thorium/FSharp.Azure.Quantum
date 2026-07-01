namespace FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core

open System
open System.Text.Json
open FSharp.Azure.Quantum.Core.Types

/// IQM Backend Integration
///
/// Low-level module for IQM (superconducting) integration with Azure Quantum.
/// Submits OpenQASM 2.0 circuits to `iqm.*` targets, parses the result histogram,
/// and maps IQM error codes.
///
/// IQM is a gate-model, OpenQASM-consuming Azure Quantum provider, so this follows the
/// same shape as the Quantinuum and Atom Computing modules. The ICircuit → OpenQASM
/// conversion happens in the CloudBackends wrapper (compiled later), which keeps this
/// module free of any CircuitBuilder dependency.
module IqmBackend =

    // ============================================================================
    // JOB SUBMISSION
    // ============================================================================

    /// Create a JobSubmission for an IQM circuit (from an OpenQASM 2.0 string).
    ///
    /// Parameters:
    /// - qasmCode: OpenQASM 2.0 string (already transpiled/validated)
    /// - shots: Number of measurement shots
    /// - target: IQM backend target (e.g., "iqm.sim", "iqm.qpu.garnet")
    let createJobSubmission (qasmCode: string) (shots: int) (target: string) : JobSubmission =
        {
            JobId = Guid.NewGuid().ToString()
            Target = target
            Name = Some (sprintf "IQM-%s" target)
            InputData = qasmCode :> obj
            InputDataFormat = CircuitFormat.Custom "qasm.v2"  // OpenQASM 2.0
            InputParams = Map [ ("shots", shots :> obj) ]
            Tags = Map.empty
        }

    // ============================================================================
    // RESULT PARSING
    // ============================================================================

    /// Parse an IQM result JSON into a measurement histogram.
    ///
    /// Accepts the Azure Quantum gate-provider convention `{"results": {"00": n, ...}}`
    /// (also tolerates a "measurements" wrapper or a bare histogram object). Throws on
    /// malformed JSON — the caller wraps the call in try/with, matching the other
    /// OpenQASM cloud backends.
    let parseIqmResult (jsonResult: string) : Map<string, int> =
        use jsonDoc = JsonDocument.Parse(jsonResult)
        let root = jsonDoc.RootElement

        let results =
            match root.TryGetProperty("results") with
            | (true, element) -> element
            | (false, _) ->
                match root.TryGetProperty("measurements") with
                | (true, element) -> element
                | (false, _) -> root  // Fallback: root is the histogram itself

        results.EnumerateObject()
        |> Seq.map (fun prop -> (prop.Name, prop.Value.GetInt32()))
        |> Map.ofSeq

    // ============================================================================
    // ERROR MAPPING
    // ============================================================================

    /// Map IQM error codes to QuantumError types.
    let mapIqmError (errorCode: string) (errorMessage: string) : QuantumError =
        match errorCode with
        | "InvalidCircuit" ->
            QuantumError.ValidationError("circuit", errorMessage)
        | "TooManyQubits" ->
            QuantumError.ValidationError("circuit", sprintf "Circuit too large: %s" errorMessage)
        | "QuotaExceeded" ->
            QuantumError.AzureError (AzureQuantumError.QuotaExceeded errorMessage)
        | "BackendUnavailable" ->
            QuantumError.AzureError (AzureQuantumError.ServiceUnavailable (Some (TimeSpan.FromMinutes(5.0))))
        | _ ->
            QuantumError.AzureError (AzureQuantumError.UnknownError(0, sprintf "IQM error: %s - %s" errorCode errorMessage))
