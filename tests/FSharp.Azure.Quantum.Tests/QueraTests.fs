namespace FSharp.Azure.Quantum.Tests

open System.Text.Json
open Xunit
open FSharp.Azure.Quantum.Algorithms.NeutralAtom
open FSharp.Azure.Quantum.Algorithms

/// Tests for the QuEra (AWS Braket AHS) neutral-atom exporter. Only the pure AHS serialization
/// and result parsing are tested; task submission needs AWS credentials + the AWSSDK.Braket SDK.
module QueraTests =

    let private program () : RydbergProgram =
        let register = [ { X = 0.0; Y = 0.0 }; { X = 4.0; Y = 0.0 }; { X = 8.0; Y = 0.0 } ]
        maximumIndependentSetProgram register 30.0 1.0 4.0 3.0

    [<Fact>]
    let ``toAhsProgram emits a valid Braket AHS program`` () =
        let json = QuEra.toAhsProgram (program ())
        use doc = JsonDocument.Parse(json)   // must be valid JSON
        let root = doc.RootElement
        Assert.Equal("braket.ir.ahs.program", root.GetProperty("braketSchemaHeader").GetProperty("name").GetString())
        let register = root.GetProperty("setup").GetProperty("ahs_register")
        Assert.Equal(3, register.GetProperty("sites").GetArrayLength())
        Assert.Equal(3, register.GetProperty("filling").GetArrayLength())
        // A driving field carries amplitude/phase/detuning; local detuning is empty.
        let df = root.GetProperty("hamiltonian").GetProperty("drivingFields").[0]
        Assert.True(df.TryGetProperty("amplitude") |> fst)
        Assert.True(df.TryGetProperty("phase") |> fst)
        Assert.True(df.TryGetProperty("detuning") |> fst)
        Assert.True(df.GetProperty("amplitude").GetProperty("pattern").GetString() = "uniform")
        Assert.Equal(0, root.GetProperty("hamiltonian").GetProperty("localDetuning").GetArrayLength())

    [<Fact>]
    let ``toAhsProgram writes coordinates in metres`` () =
        let json = QuEra.toAhsProgram (program ())
        use doc = JsonDocument.Parse(json)
        let sites = doc.RootElement.GetProperty("setup").GetProperty("ahs_register").GetProperty("sites")
        // 4 µm → 4e-6 m, 8 µm → 8e-6 m
        Assert.Equal(4e-6, sites.[1].[0].GetDouble(), 12)
        Assert.Equal(8e-6, sites.[2].[0].GetDouble(), 12)

    [<Fact>]
    let ``the amplitude time-series matches the number of pulse-segment boundaries`` () =
        // A 3-segment schedule has 4 boundary time points.
        let json = QuEra.toAhsProgram (program ())
        use doc = JsonDocument.Parse(json)
        let series =
            doc.RootElement.GetProperty("hamiltonian").GetProperty("drivingFields").[0]
                .GetProperty("amplitude").GetProperty("time_series")
        Assert.Equal(4, series.GetProperty("values").GetArrayLength())
        Assert.Equal(4, series.GetProperty("times").GetArrayLength())

    [<Fact>]
    let ``toAhsProgram preserves discontinuous segment boundaries as steep ramps`` () =
        // Segment 2 starts at a different Ω than segment 1 ends. The single (times, values)
        // series must keep both boundary values as a steep ramp over the 1 ns resolution —
        // previously the start value was silently dropped, compiling the step into one long
        // linear ramp (a different pulse than NeutralAtom.simulate executes).
        let program : RydbergProgram = {
            Register = [ { X = 0.0; Y = 0.0 } ]
            C6 = 30.0
            Schedule =
                [ { Duration = 1.0; RabiStart = 0.0; RabiEnd = 1.0; DetuningStart = 0.0; DetuningEnd = 0.0 }
                  { Duration = 1.0; RabiStart = 2.0; RabiEnd = 2.0; DetuningStart = 0.0; DetuningEnd = 0.0 } ] }
        let json = QuEra.toAhsProgram program
        use doc = JsonDocument.Parse(json)
        let series =
            doc.RootElement.GetProperty("hamiltonian").GetProperty("drivingFields").[0]
                .GetProperty("amplitude").GetProperty("time_series")
        let values = [ for v in series.GetProperty("values").EnumerateArray() -> v.GetDouble() ]
        let times = [ for t in series.GetProperty("times").EnumerateArray() -> t.GetDouble() ]
        // 3 boundary points (0, seg1 end, seg2 end) + 1 inserted jump point.
        Assert.Equal(4, values.Length)
        // rad/µs → rad/s: both sides of the discontinuity survive (1.0 then 2.0).
        Assert.Equal<float list>([ 0.0; 1e6; 2e6; 2e6 ], values)
        // AHS requires strictly increasing times — the jump is a 1 ns ramp, not a duplicate point.
        times |> List.pairwise |> List.iter (fun (a, b) -> Assert.True(a < b, $"times not strictly increasing: {a} !< {b}"))

    [<Fact>]
    let ``parseAhsResult reads postSequence into a Rydberg-occupation histogram`` () =
        // postSequence: 1 = ground, 0 = Rydberg ⇒ Rydberg bit = 1 - post.
        let json =
            """{ "measurements": [
                   { "shotResult": { "preSequence": [1,1], "postSequence": [0,1] } },
                   { "shotResult": { "preSequence": [1,1], "postSequence": [1,0] } },
                   { "shotResult": { "preSequence": [1,1], "postSequence": [0,1] } } ] }"""
        let counts = QuEra.parseAhsResult json
        Assert.Equal(2, counts.["10"])   // atom 0 Rydberg, atom 1 ground — occurred twice
        Assert.Equal(1, counts.["01"])
