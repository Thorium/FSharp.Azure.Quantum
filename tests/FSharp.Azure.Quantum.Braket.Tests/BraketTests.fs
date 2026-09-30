namespace FSharp.Azure.Quantum.Braket.Tests

open System.Text.Json
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Braket
open FSharp.Azure.Quantum.Core

/// Tests for the pure AWS Braket helpers (action wrapping, result parsing, device ARNs).
/// The submission flow (BraketExecution) needs AWS credentials and isn't CI-testable.
module BraketTests =

    [<Fact>]
    let ``openQasmAction wraps OpenQASM 3.0 source in a valid Braket action`` () =
        let bell =
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.CNOT(0, 1))

        let action = Braket.openQasmAction (OpenQasm.exportV3 bell)
        use doc = JsonDocument.Parse(action) // must be valid JSON
        let root = doc.RootElement

        Assert.Equal(
            "braket.ir.openqasm.program",
            root.GetProperty("braketSchemaHeader").GetProperty("name").GetString()
        )
        // The source round-trips (newlines/quotes escaped correctly) and contains the circuit.
        let source = root.GetProperty("source").GetString()
        Assert.Contains("OPENQASM 3.0;", source)
        Assert.Contains("cx q[0],q[1];", source)

    [<Fact>]
    let ``parseGateResult reads a per-shot measurements array`` () =
        let json = """{ "measurements": [ [0,0], [1,1], [0,0], [1,1], [1,1] ] }"""
        let counts = Braket.parseGateResult json
        Assert.Equal(2, counts.["00"])
        Assert.Equal(3, counts.["11"])

    [<Fact>]
    let ``parseGateResult falls back to measurementProbabilities`` () =
        let json = """{ "measurementProbabilities": { "00": 0.5, "11": 0.5 } }"""
        let counts = Braket.parseGateResult json
        Assert.True(counts.ContainsKey "00" && counts.["00"] > 0)
        Assert.True(counts.ContainsKey "11" && counts.["11"] > 0)

    [<Fact>]
    let ``device ARNs are the expected Braket resources`` () =
        Assert.Equal("arn:aws:braket:eu-west-2::device/qpu/oqc/Lucy", Braket.Devices.oqcLucy)
        Assert.Equal("arn:aws:braket:us-east-1::device/qpu/infleqtion/Sqale", Braket.Devices.infleqtionSqale)
        Assert.Equal("arn:aws:braket:us-east-1::device/qpu/quera/Aquila", Braket.Devices.queraAquila)
        Assert.Contains("quantum-simulator/amazon/sv1", Braket.Devices.sv1)

    [<Fact>]
    let ``BraketBackend transpiles composite gates for each device before submission`` () =
        // A zero-job budget refuses at submission, after transpilation and OpenQASM 3.0 export,
        // so no AWS client is touched: QuotaExceeded proves the circuit exported.
        let circuit =
            CircuitBuilder.empty 4
            |> CircuitBuilder.addGates
                [
                    CircuitBuilder.H 0
                    CircuitBuilder.H 1
                    CircuitBuilder.H 2
                    CircuitBuilder.MCZ([ 0; 1; 2 ], 3)
                    CircuitBuilder.CCX(0, 1, 2)
                    CircuitBuilder.CP(0, 3, 0.4)
                    CircuitBuilder.CRZ(1, 2, 0.7)
                    CircuitBuilder.T 3
                    CircuitBuilder.TDG 2
                ]

        for device in [ Braket.Devices.sv1; Braket.Devices.ionqAria1; Braket.Devices.oqcLucy ] do
            let budget = FSharp.Azure.Quantum.Backends.CloudBackendHelpers.JobBudget.Limit 0

            let backend =
                BraketExecution.BraketBackend(
                    null,
                    null,
                    { Bucket = "b"; KeyPrefix = "k" },
                    device,
                    100,
                    jobBudget = budget
                )
                :> BackendAbstraction.IQuantumBackend

            match backend.ExecuteToState(CircuitAbstraction.wrapCircuit circuit) with
            | Error(QuantumError.AzureError(AzureQuantumError.QuotaExceeded _)) ->
                ()
            | other -> Assert.Fail($"%s{device}: expected the job-budget refusal, got %A{other}")

            Assert.Equal(0, budget.Submitted)
