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
        let ionqAria1 = "arn:aws:braket:us-east-1::device/qpu/ionq/Aria-1"

        [<Literal>]
        let ionqForte1 = "arn:aws:braket:us-east-1::device/qpu/ionq/Forte-1"

        [<Literal>]
        let rigettiAnkaa3 = "arn:aws:braket:us-west-1::device/qpu/rigetti/Ankaa-3"

        [<Literal>]
        let iqmGarnet = "arn:aws:braket:eu-north-1::device/qpu/iqm/Garnet"

        [<Literal>]
        let oqcLucy = "arn:aws:braket:eu-west-2::device/qpu/oqc/Lucy"

        [<Literal>]
        let infleqtionSqale = "arn:aws:braket:us-east-1::device/qpu/infleqtion/Sqale"
        // Managed simulators
        [<Literal>]
        let sv1 = "arn:aws:braket:::device/quantum-simulator/amazon/sv1"

        [<Literal>]
        let dm1 = "arn:aws:braket:::device/quantum-simulator/amazon/dm1"

        [<Literal>]
        let tn1 = "arn:aws:braket:::device/quantum-simulator/amazon/tn1"
        // Neutral-atom analog QPU (uses AHS, not OpenQASM)
        [<Literal>]
        let queraAquila = "arn:aws:braket:us-east-1::device/qpu/quera/Aquila"

        /// Qubits of a gate-model device this module names, ValueNone for any other device (its own
        /// limit then applies when a task is submitted). Aquila is an analog device and has no
        /// gate-model width. The figures are defaults from when this version was released:
        /// BraketBackend takes a maxQubits argument that replaces them, and
        /// BraketBackend.CreateWithDeviceLimitAsync reads the device's own figure.
        let maxQubits (deviceArn: string) : int voption =
            [
                ionqAria1, 25
                ionqForte1, 36
                rigettiAnkaa3, 84
                iqmGarnet, 20
                oqcLucy, 8
                sv1, 34
                dm1, 17
                tn1, 50
            ]
            |> List.tryFind (fun (arn, _) -> arn = deviceArn)
            |> ValueOption.ofOption
            |> ValueOption.map snd

        /// Qubit count in a device's capabilities document (the DeviceCapabilities JSON of
        /// Braket's GetDevice): its paradigm.qubitCount. ValueNone when the document names none
        /// or is not a JSON object.
        let qubitCountOfCapabilities (capabilitiesJson: string) : int voption =
            if System.String.IsNullOrWhiteSpace capabilitiesJson then
                ValueNone
            else
                try
                    use doc = JsonDocument.Parse capabilitiesJson

                    if doc.RootElement.ValueKind <> JsonValueKind.Object then
                        ValueNone
                    else
                        match doc.RootElement.TryGetProperty "paradigm" with
                        | true, paradigm when paradigm.ValueKind = JsonValueKind.Object ->
                            match paradigm.TryGetProperty "qubitCount" with
                            | true, count when count.ValueKind = JsonValueKind.Number ->
                                match count.TryGetInt32() with
                                | true, qubits when qubits >= 1 -> ValueSome qubits
                                | _ -> ValueNone
                            | _ -> ValueNone
                        | _ -> ValueNone
                with :? JsonException ->
                    ValueNone

    /// Wrap an OpenQASM 3.0 source string in a Braket OpenQASM program action.
    let openQasmAction (source: string) : string =
        // JsonSerializer.Serialize handles escaping of newlines/quotes in the source.
        let escapedSource = JsonSerializer.Serialize(source)

        sprintf
            """{"braketSchemaHeader":{"name":"braket.ir.openqasm.program","version":"1"},"source":%s}"""
            escapedSource

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

                histogram
                |> Map.change bits (fun existing -> Some(Option.defaultValue 0 existing + 1)))
        | _ ->
            match root.TryGetProperty "measurementProbabilities" with
            | true, probabilities ->
                // Approximate integer counts (scaled) so downstream sees the distribution shape.
                probabilities.EnumerateObject()
                |> Seq.map (fun p -> p.Name, int (System.Math.Round(p.Value.GetDouble() * 10000.0)))
                |> Map.ofSeq
            | _ -> Map.empty
