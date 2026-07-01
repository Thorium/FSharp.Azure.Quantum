namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum

/// Tests for the emulate-target mode (transpile → validate → local-run).
module EmulationTests =

    let private bell () =
        CircuitBuilder.empty 2
        |> CircuitBuilder.addGate (CircuitBuilder.H 0)
        |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 1))

    [<Fact>]
    let ``emulate on a known target validates and runs the Bell circuit`` () =
        match Emulation.emulate "ionq.qpu.aria-1" 1000 (bell ()) with
        | Error e -> failwith $"emulate failed: {e.Message}"
        | Ok report ->
            Assert.True(report.KnownTarget)
            Assert.Empty(report.ConstraintViolations)
            // Only the correlated Bell outcomes appear.
            let keys = report.Counts |> Map.toList |> List.map fst |> Set.ofList
            Assert.True(Set.isSubset keys (Set.ofList [ "00"; "11" ]))

    [<Fact>]
    let ``emulate on an unknown target still runs but skips validation`` () =
        match Emulation.emulate "iqm.qpu.garnet" 500 (bell ()) with
        | Error e -> failwith $"emulate failed: {e.Message}"
        | Ok report ->
            Assert.False(report.KnownTarget)
            Assert.Empty(report.ConstraintViolations)
            Assert.NotEmpty(report.Counts |> Map.toList)

    [<Fact>]
    let ``emulate reports a connectivity violation for a non-adjacent two-qubit gate`` () =
        // Rigetti Aspen-M-3 has limited connectivity; CNOT(0,4) is not a native edge.
        let wide =
            CircuitBuilder.empty 5
            |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 4))
        match Emulation.emulate "rigetti.qpu.aspen-m-3" 500 wide with
        | Error e -> failwith $"emulate failed: {e.Message}"
        | Ok report ->
            Assert.True(report.KnownTarget)
            Assert.NotEmpty(report.ConstraintViolations)
