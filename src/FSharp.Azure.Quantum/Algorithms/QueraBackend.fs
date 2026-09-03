namespace FSharp.Azure.Quantum.Algorithms

open System.Text.Json
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Algorithms.NeutralAtom

/// QuEra Aquila neutral-atom QPU integration via AWS Braket (Analog Hamiltonian Simulation).
///
/// Like Pasqal, QuEra is an **analog** neutral-atom device — it runs a driving Hamiltonian over
/// an atom register, not a gate circuit. This module compiles a `RydbergProgram` (from the
/// `NeutralAtom` module) into a **Braket AHS program** (`braket.ir.ahs.program`, v1) and parses
/// AHS results back to a Rydberg-occupation histogram.
///
/// So a single `RydbergProgram` now has three execution paths: local gate simulation
/// (`NeutralAtom.simulate`), Pasqal via Azure Quantum (`Pasqal.submitAndWaitForResultsAsync`),
/// and QuEra via AWS Braket (compile with `QuEra.toAhsProgram`, then submit — see the note below).
///
/// SUBMISSION: AWS Braket has a .NET SDK (`AWSSDK.Braket`). To run on hardware, call
/// `AmazonBraketClient.CreateQuantumTask` with `deviceArn = QuEra.aquilaDeviceArn`, the
/// `action` set to `toAhsProgram program`, and an S3 output location; poll `GetQuantumTask`,
/// then read the result JSON from S3 and pass it to `parseAhsResult`. The SDK dependency is
/// intentionally not taken here so the core (compile + parse) stays dependency-free and testable.
///
/// UNITS: a `RydbergProgram` is interpreted physically as micrometres (coordinates),
/// microseconds (time) and rad/µs (Ω, Δ); AHS requires SI, so this converts to metres, seconds
/// and rad/s. (The same program is unit-agnostic when used with `NeutralAtom.simulate`.)
module QuEra =

    /// AWS Braket device ARN for the QuEra Aquila neutral-atom analog QPU.
    [<Literal>]
    let aquilaDeviceArn = "arn:aws:braket:us-east-1::device/qpu/quera/Aquila"

    [<Literal>]
    let private umToMetres = 1e-6
    [<Literal>]
    let private usToSeconds = 1e-6
    [<Literal>]
    let private radPerUsToRadPerS = 1e6

    /// Compile a `RydbergProgram` to a Braket AHS program JSON.
    let toAhsProgram (program: RydbergProgram) : string =
        // Build continuous piecewise-linear time series (times/values) from the segment ramps.
        let times = ResizeArray<float>()
        let amplitude = ResizeArray<float>()
        let detuning = ResizeArray<float>()
        match program.Schedule with
        | [] -> times.Add 0.0; amplitude.Add 0.0; detuning.Add 0.0
        | first :: _ ->
            times.Add 0.0
            amplitude.Add first.RabiStart
            detuning.Add first.DetuningStart
            let mutable t = 0.0
            // Values currently on the series at time t (end of the previous segment).
            let mutable prevRabi = first.RabiStart
            let mutable prevDetuning = first.DetuningStart
            for segment in program.Schedule do
                // A segment may start at a different Ω/Δ than the previous one ended — the
                // schedule is piecewise, not necessarily continuous. A single (times, values)
                // series cannot hold two values at one instant (AHS requires strictly
                // increasing times), so realise the jump as a steep ramp over the device's
                // 1 ns time resolution. Recording only the segment *end* values here would
                // silently compile a discontinuous schedule into one continuous linear ramp —
                // a different pulse than `NeutralAtom.simulate` and the Pasqal path execute.
                let jumpNeeded =
                    abs (segment.RabiStart - prevRabi) > 1e-12
                    || abs (segment.DetuningStart - prevDetuning) > 1e-12
                if jumpNeeded && segment.Duration > 0.0 then
                    let jumpTime = min 1e-3 (segment.Duration / 2.0)  // 1 ns, in µs
                    times.Add (t + jumpTime)
                    amplitude.Add segment.RabiStart
                    detuning.Add segment.DetuningStart
                t <- t + segment.Duration
                times.Add t
                amplitude.Add segment.RabiEnd
                detuning.Add segment.DetuningEnd
                prevRabi <- segment.RabiEnd
                prevDetuning <- segment.DetuningEnd
        let totalTime = times.[times.Count - 1]

        use stream = new System.IO.MemoryStream()
        (
            use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

            // A PhysicalField: { "time_series": { "values": [...], "times": [...] }, "pattern": "uniform" }.
            let writePhysicalField (name: string) (values: ResizeArray<float>) (valueScale: float) =
                writer.WriteStartObject name
                writer.WriteStartObject "time_series"
                writer.WriteStartArray "values"
                for v in values do writer.WriteNumberValue(v * valueScale)
                writer.WriteEndArray()
                writer.WriteStartArray "times"
                for tt in times do writer.WriteNumberValue(tt * usToSeconds)
                writer.WriteEndArray()
                writer.WriteEndObject()
                writer.WriteString("pattern", "uniform")
                writer.WriteEndObject()

            writer.WriteStartObject()

            writer.WriteStartObject "braketSchemaHeader"
            writer.WriteString("name", "braket.ir.ahs.program")
            writer.WriteString("version", "1")
            writer.WriteEndObject()

            // setup.ahs_register — atom sites (metres) and filling.
            writer.WriteStartObject "setup"
            writer.WriteStartObject "ahs_register"
            writer.WriteStartArray "sites"
            for atom in program.Register do
                writer.WriteStartArray()
                writer.WriteNumberValue(atom.X * umToMetres)
                writer.WriteNumberValue(atom.Y * umToMetres)
                writer.WriteEndArray()
            writer.WriteEndArray()
            writer.WriteStartArray "filling"
            for _ in program.Register do writer.WriteNumberValue 1
            writer.WriteEndArray()
            writer.WriteEndObject()   // ahs_register
            writer.WriteEndObject()   // setup

            // hamiltonian — a single global driving field (Ω, φ, Δ); no local detuning.
            writer.WriteStartObject "hamiltonian"
            writer.WriteStartArray "drivingFields"
            writer.WriteStartObject()
            writePhysicalField "amplitude" amplitude radPerUsToRadPerS
            // Phase held at 0 for the whole evolution.
            writer.WriteStartObject "phase"
            writer.WriteStartObject "time_series"
            writer.WriteStartArray "values"; writer.WriteNumberValue 0.0; writer.WriteNumberValue 0.0; writer.WriteEndArray()
            writer.WriteStartArray "times"; writer.WriteNumberValue 0.0; writer.WriteNumberValue(totalTime * usToSeconds); writer.WriteEndArray()
            writer.WriteEndObject()
            writer.WriteString("pattern", "uniform")
            writer.WriteEndObject()
            writePhysicalField "detuning" detuning radPerUsToRadPerS
            writer.WriteEndObject()   // driving field
            writer.WriteEndArray()    // drivingFields
            writer.WriteStartArray "localDetuning"; writer.WriteEndArray()
            writer.WriteEndObject()   // hamiltonian

            writer.WriteEndObject()
            writer.Flush()
        )
        System.Text.Encoding.UTF8.GetString(stream.ToArray())

    /// Parse an AHS task-result JSON into a Rydberg-occupation histogram (bit i = 1 means atom i
    /// ended in the Rydberg state). Braket's `postSequence` is 1 for ground and 0 for Rydberg
    /// (or empty), so the Rydberg bit is `1 - postSequence[i]`. Throws on malformed JSON.
    let parseAhsResult (jsonResult: string) : Map<string, int> =
        use doc = JsonDocument.Parse(jsonResult)
        let measurements = doc.RootElement.GetProperty "measurements"
        (Map.empty, measurements.EnumerateArray())
        ||> Seq.fold (fun histogram shot ->
            let post = shot.GetProperty("shotResult").GetProperty "postSequence"
            let bits =
                post.EnumerateArray()
                |> Seq.map (fun e -> string (1 - e.GetInt32()))
                |> String.concat ""
            histogram |> Map.change bits (fun existing -> Some (Option.defaultValue 0 existing + 1)))
