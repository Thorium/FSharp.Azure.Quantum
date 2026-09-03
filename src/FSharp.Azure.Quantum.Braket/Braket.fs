namespace FSharp.Azure.Quantum.Braket

open System.Text.Json

/// Pure AWS Braket helpers — device ARNs, action-JSON wrapping, and result parsing.
/// No AWS SDK dependency (that lives in `BraketBackend`); these are testable in isolation.
module Braket =

    /// Well-known AWS Braket device ARNs. Regions/names track the Braket console — update if a
    /// device is retired or added. Gate devices consume OpenQASM 3.0; QuEra consumes AHS.
    module Devices =
        // Gate QPUs
        [<Literal>]
        let ionqAria1        = "arn:aws:braket:us-east-1::device/qpu/ionq/Aria-1"
        [<Literal>]
        let ionqForte1       = "arn:aws:braket:us-east-1::device/qpu/ionq/Forte-1"
        [<Literal>]
        let rigettiAnkaa3    = "arn:aws:braket:us-west-1::device/qpu/rigetti/Ankaa-3"
        [<Literal>]
        let iqmGarnet        = "arn:aws:braket:eu-north-1::device/qpu/iqm/Garnet"
        [<Literal>]
        let oqcLucy          = "arn:aws:braket:eu-west-2::device/qpu/oqc/Lucy"
        [<Literal>]
        let infleqtionSqale  = "arn:aws:braket:us-east-1::device/qpu/infleqtion/Sqale"
        // Managed simulators
        [<Literal>]
        let sv1              = "arn:aws:braket:::device/quantum-simulator/amazon/sv1"
        [<Literal>]
        let dm1              = "arn:aws:braket:::device/quantum-simulator/amazon/dm1"
        [<Literal>]
        let tn1              = "arn:aws:braket:::device/quantum-simulator/amazon/tn1"
        // Neutral-atom analog QPU (uses AHS, not OpenQASM)
        [<Literal>]
        let queraAquila      = "arn:aws:braket:us-east-1::device/qpu/quera/Aquila"

    /// Wrap an OpenQASM 3.0 source string in a Braket OpenQASM program action.
    let openQasmAction (source: string) : string =
        // JsonSerializer.Serialize handles escaping of newlines/quotes in the source.
        let escapedSource = JsonSerializer.Serialize(source)
        sprintf """{"braketSchemaHeader":{"name":"braket.ir.openqasm.program","version":"1"},"source":%s}""" escapedSource

    /// Parse a Braket gate-model task result JSON into a measurement histogram
    /// (`bitstring -> count`). Handles both the per-shot `measurements` array and the
    /// `measurementProbabilities` map that some devices/simulators return.
    let parseGateResult (json: string) : Map<string, int> =
        use doc = JsonDocument.Parse(json)
        let root = doc.RootElement
        match root.TryGetProperty "measurements" with
        | true, measurements when measurements.ValueKind = JsonValueKind.Array ->
            (Map.empty, measurements.EnumerateArray())
            ||> Seq.fold (fun histogram shot ->
                let bits =
                    shot.EnumerateArray()
                    |> Seq.map (fun bit -> string (bit.GetInt32()))
                    |> String.concat ""
                histogram |> Map.change bits (fun existing -> Some (Option.defaultValue 0 existing + 1)))
        | _ ->
            match root.TryGetProperty "measurementProbabilities" with
            | true, probabilities ->
                // Approximate integer counts (scaled) so downstream sees the distribution shape.
                probabilities.EnumerateObject()
                |> Seq.map (fun p -> p.Name, int (System.Math.Round(p.Value.GetDouble() * 10000.0)))
                |> Map.ofSeq
            | _ -> Map.empty
