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
            | Error(QuantumError.AzureError(AzureQuantumError.QuotaExceeded _)) -> ()
            | other -> Assert.Fail($"%s{device}: expected the job-budget refusal, got %A{other}")

            Assert.Equal(0, budget.Submitted)

    [<Fact>]
    let ``Devices.maxQubits knows the gate-model devices and nothing else`` () =
        Assert.Equal(ValueSome 25, Braket.Devices.maxQubits Braket.Devices.ionqAria1)
        Assert.Equal(ValueSome 36, Braket.Devices.maxQubits Braket.Devices.ionqForte1)
        Assert.Equal(ValueSome 84, Braket.Devices.maxQubits Braket.Devices.rigettiAnkaa3)
        Assert.Equal(ValueSome 20, Braket.Devices.maxQubits Braket.Devices.iqmGarnet)
        Assert.Equal(ValueSome 8, Braket.Devices.maxQubits Braket.Devices.oqcLucy)
        Assert.Equal(ValueSome 34, Braket.Devices.maxQubits Braket.Devices.sv1)
        Assert.Equal(ValueSome 17, Braket.Devices.maxQubits Braket.Devices.dm1)
        Assert.Equal(ValueSome 50, Braket.Devices.maxQubits Braket.Devices.tn1)
        // analog device, and a device this module does not name
        Assert.Equal(ValueNone, Braket.Devices.maxQubits Braket.Devices.queraAquila)
        Assert.Equal(ValueNone, Braket.Devices.maxQubits Braket.Devices.infleqtionSqale)
        Assert.Equal(ValueNone, Braket.Devices.maxQubits "arn:aws:braket:::device/qpu/unknown/Device")

    [<Fact>]
    let ``the Braket backend reports its device's qubits and bills every circuit`` () =
        let backendFor (device: string) =
            BraketExecution.BraketBackend(null, null, { Bucket = "b"; KeyPrefix = "k" }, device, 100)
            :> BackendAbstraction.IQuantumBackend

        Assert.Equal(Some 8, BackendAbstraction.UnifiedBackend.getRunnableQubits (backendFor Braket.Devices.oqcLucy))
        Assert.Equal(Some 34, BackendAbstraction.UnifiedBackend.getMaxQubits (backendFor Braket.Devices.sv1))
        Assert.Equal(None, BackendAbstraction.UnifiedBackend.getMaxQubits (backendFor Braket.Devices.infleqtionSqale))

        // A billed backend is split only on request (SplitPolicy.Always).
        let lucy = backendFor Braket.Devices.oqcLucy
        let settings = QaoaExecutionHelpers.defaultSplitSettings
        Assert.Equal(ValueNone, QaoaExecutionHelpers.splitPieceQubits settings lucy 20)

        Assert.Equal(
            ValueSome 8,
            QaoaExecutionHelpers.splitPieceQubits
                { settings with
                    Policy = QaoaExecutionHelpers.SplitPolicy.Always
                }
                lucy
                20
        )

    [<Fact>]
    let ``maxQubits replaces the built-in figure of a Braket device`` () =
        let backendWith (device: string) (qubits: int) =
            BraketExecution.BraketBackend(
                null,
                null,
                { Bucket = "b"; KeyPrefix = "k" },
                device,
                100,
                maxQubits = qubits
            )
            :> BackendAbstraction.IQuantumBackend

        // a device the table knows, grown since
        Assert.Equal(Some 64, BackendAbstraction.UnifiedBackend.getMaxQubits (backendWith Braket.Devices.ionqAria1 64))

        Assert.Equal(
            Some 64,
            BackendAbstraction.UnifiedBackend.getRunnableQubits (backendWith Braket.Devices.ionqAria1 64)
        )
        // a device the table does not know
        Assert.Equal(
            Some 24,
            BackendAbstraction.UnifiedBackend.getMaxQubits (backendWith Braket.Devices.infleqtionSqale 24)
        )

        Assert.Throws<System.ArgumentException>(fun () -> backendWith Braket.Devices.sv1 0 |> ignore)
        |> ignore

    [<Fact>]
    let ``qubitCountOfCapabilities reads paradigm.qubitCount of a device capabilities document`` () =
        let hardware =
            """{"braketSchemaHeader":{"name":"braket.device_schema.ionq.ionq_device_capabilities","version":"1"},
                "service":{"shotsRange":[1,5000]},
                "action":{"braket.ir.openqasm.program":{"actionType":"braket.ir.openqasm.program","version":["1"]}},
                "paradigm":{"qubitCount":36,"nativeGateSet":["GPI","GPI2","ZZ"],
                            "connectivity":{"fullyConnected":true,"connectivityGraph":{}}}}"""

        let simulator =
            """{"service":{"shotsRange":[0,100000]},"paradigm":{"qubitCount":34}}"""

        Assert.Equal(ValueSome 36, Braket.Devices.qubitCountOfCapabilities hardware)
        Assert.Equal(ValueSome 34, Braket.Devices.qubitCountOfCapabilities simulator)

        // documents that name no usable count
        for document in
            [
                """{"service":{}}"""
                """{"paradigm":{"nativeGateSet":["cz"]}}"""
                """{"paradigm":{"qubitCount":"many"}}"""
                """{"paradigm":{"qubitCount":0}}"""
                """{"paradigm":36}"""
                "[1, 2]"
                "not json"
                ""
                null
            ] do
            Assert.Equal(ValueNone, Braket.Devices.qubitCountOfCapabilities document)
