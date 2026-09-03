namespace FSharp.Azure.Quantum.Tests

open System.Text.Json
open Xunit
open FSharp.Azure.Quantum.Algorithms.NeutralAtom
open FSharp.Azure.Quantum.Algorithms

/// Tests for the Pasqal neutral-atom cloud backend. Only the (pure) Pulser serialization and
/// result parsing are tested; job submission needs Azure Quantum credentials.
module PasqalTests =

    let private program () : RydbergProgram =
        let register = [ { X = 0.0; Y = 0.0 }; { X = 4.0; Y = 0.0 }; { X = 8.0; Y = 0.0 } ]
        maximumIndependentSetProgram register 30.0 1.0 4.0 900.0   // 3 pulse segments

    [<Fact>]
    let ``toPulserJson emits a valid Pulser abstract-representation`` () =
        let json = Pasqal.toPulserJson (program ())
        use doc = JsonDocument.Parse(json)   // must be valid JSON
        let root = doc.RootElement
        Assert.Equal("1", root.GetProperty("version").GetString())
        Assert.Equal(3, root.GetProperty("register").GetArrayLength())          // 3 atoms
        Assert.Equal(3, root.GetProperty("operations").GetArrayLength())        // 3 pulse segments
        Assert.True(root.GetProperty("channels").TryGetProperty("rydberg_global") |> fst)
        Assert.Equal("ground-rydberg", root.GetProperty("measurement").GetString())

    [<Fact>]
    let ``toPulserJson writes each atom's coordinates`` () =
        let json = Pasqal.toPulserJson (program ())
        use doc = JsonDocument.Parse(json)
        let atoms = doc.RootElement.GetProperty "register"
        Assert.Equal("q0", atoms.[0].GetProperty("name").GetString())
        Assert.Equal(0.0, atoms.[0].GetProperty("x").GetDouble(), 6)
        Assert.Equal(8.0, atoms.[2].GetProperty("x").GetDouble(), 6)

    [<Fact>]
    let ``each operation is a ramped pulse on the global channel`` () =
        let json = Pasqal.toPulserJson (program ())
        use doc = JsonDocument.Parse(json)
        let op = doc.RootElement.GetProperty("operations").[0]
        Assert.Equal("pulse", op.GetProperty("op").GetString())
        Assert.Equal("rydberg_global", op.GetProperty("channel").GetString())
        Assert.Equal("ramp", op.GetProperty("amplitude").GetProperty("kind").GetString())
        Assert.True(op.GetProperty("amplitude").GetProperty("duration").GetInt32() > 0)

    [<Fact>]
    let ``pulse durations are converted from microseconds to nanoseconds`` () =
        // program () spans 900 µs over 3 equal segments → 300 µs each = 300000 ns (Pulser uses ns).
        let json = Pasqal.toPulserJson (program ())
        use doc = JsonDocument.Parse(json)
        let op = doc.RootElement.GetProperty("operations").[0]
        Assert.Equal(300000, op.GetProperty("amplitude").GetProperty("duration").GetInt32())

    [<Fact>]
    let ``createJobSubmission uses the Pulser format and a shot count`` () =
        let submission = Pasqal.createJobSubmission "{}" 500 "pasqal.sim.emu-tn"
        Assert.Equal("pasqal.sim.emu-tn", submission.Target)
        Assert.Equal("pasqal.pulser.abstract-repr.v1", submission.InputDataFormat.ToFormatString())
        Assert.True(submission.InputParams.ContainsKey "count")

    [<Fact>]
    let ``parsePasqalResult reads a results histogram`` () =
        let counts = Pasqal.parsePasqalResult """{ "results": { "010": 640, "101": 360 } }"""
        Assert.Equal(640, counts.["010"])
        Assert.Equal(360, counts.["101"])
