namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.OpenQasmVersion

/// Tests for classically conditioned gates (Conditional)
module ConditionalGateTests =

    let private execute (circuit: Circuit) : StateVector.StateVector =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        match backend.ExecuteToState (CircuitWrapper(circuit) :> ICircuit) with
        | Ok (QuantumState.StateVector sv) -> sv
        | Ok _ -> failwith "Expected StateVector"
        | Error err -> failwith $"Execution failed: %A{err}"

    [<Fact>]
    let ``Conditional X fires when measurement yields 1`` () =
        // Prepare q0 = |1⟩ deterministically, measure it, then conditionally flip q1.
        // Result must be |11⟩.
        let circuit =
            empty 2
            |> addGate (X 0)
            |> addGate (Measure 0)
            |> addGate (Conditional (0, X 1))

        let sv = execute circuit
        Assert.True((StateVector.getAmplitude 3 sv).Magnitude > 0.999,
            "Expected |11⟩ after conditional X on measured |1⟩")

    [<Fact>]
    let ``Conditional X stays idle when measurement yields 0`` () =
        // q0 stays |0⟩, so the conditional X on q1 must not fire. Result |00⟩.
        let circuit =
            empty 2
            |> addGate (Measure 0)
            |> addGate (Conditional (0, X 1))

        let sv = execute circuit
        Assert.True((StateVector.getAmplitude 0 sv).Magnitude > 0.999,
            "Expected |00⟩ when conditional X does not fire")

    [<Fact>]
    let ``Conditional correction always yields |1> on target (deferred X)`` () =
        // q0 in superposition, measured; whatever the outcome, X q1 conditioned
        // on outcome 1 plus the measured bit must leave q0 = q1.
        // Run repeatedly: amplitude must always be on |00⟩ or |11⟩.
        for _ in 1 .. 20 do
            let circuit =
                empty 2
                |> addGate (H 0)
                |> addGate (Measure 0)
                |> addGate (Conditional (0, X 1))

            let sv = execute circuit
            let p00 = (StateVector.getAmplitude 0 sv).Magnitude
            let p11 = (StateVector.getAmplitude 3 sv).Magnitude
            Assert.True(p00 > 0.999 || p11 > 0.999,
                $"Expected |00⟩ or |11⟩ (correlated), got p00={p00}, p11={p11}")

    [<Fact>]
    let ``Conditional before measurement is an execution error`` () =
        let circuit =
            empty 2
            |> addGate (Conditional (0, X 1))

        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        match backend.ExecuteToState (CircuitWrapper(circuit) :> ICircuit) with
        | Error _ -> ()
        | Ok _ -> Assert.Fail("Expected error for conditional referencing unmeasured qubit")

    [<Fact>]
    let ``Conditional exports to OpenQASM 3 if-statement and reimports`` () =
        let circuit =
            empty 2
            |> addGate (H 0)
            |> addGate (Measure 0)
            |> addGate (Conditional (0, X 1))

        let qasm = OpenQasmExport.exportWithConfig (configFor V3_0) circuit
        Assert.Contains("if (c[0] == 1)", qasm)
        Assert.Contains("x q[1];", qasm)

        match OpenQasmImport.parse qasm with
        | Error msg -> Assert.Fail($"Reimport failed: {msg}")
        | Ok imported ->
            let hasConditional =
                imported.Gates |> List.exists (fun g ->
                    match g with
                    | Conditional (0, X 1) -> true
                    | _ -> false)
            Assert.True(hasConditional, "Expected Conditional(0, X 1) after reimport")

    [<Fact>]
    let ``Conditional export to OpenQASM 2 fails with clear message`` () =
        let circuit =
            empty 2
            |> addGate (Measure 0)
            |> addGate (Conditional (0, X 1))

        let ex = Record.Exception(fun () ->
            OpenQasmExport.exportWithConfig (configFor V2_0) circuit |> ignore)
        Assert.NotNull(ex)
        Assert.Contains("OpenQASM 3.0", ex.Message)

    [<Fact>]
    let ``validate rejects nested conditional and non-unitary bodies`` () =
        let nested = empty 2 |> addGate (Conditional (0, Conditional (1, X 0)))
        let measureBody = empty 2 |> addGate (Conditional (0, Measure 1))

        Assert.False((CircuitBuilder.validate nested).IsValid, "Expected nested conditional to be invalid")
        Assert.False((CircuitBuilder.validate measureBody).IsValid, "Expected Measure body to be invalid")

    [<Fact>]
    let ``reverse inverts the conditional body`` () =
        let circuit = empty 2 |> addGate (Conditional (0, S 1))
        let reversed = CircuitBuilder.reverse circuit

        match reversed.Gates with
        | [Conditional (0, SDG 1)] -> ()
        | other -> Assert.Fail($"Expected Conditional(0, SDG 1), got %A{other}")

    [<Fact>]
    let ``transpiler distributes decomposition over the condition`` () =
        // Conditional CZ for IonQ: CZ decomposes to H-CNOT-H, each conditioned
        let circuit = { QubitCount = 3; Gates = [Conditional (2, CZ (0, 1))] }
        let transpiled = GateTranspiler.transpileForBackend "ionq.simulator" circuit

        Assert.True(transpiled.Gates.Length > 1, "Expected CZ to decompose")
        transpiled.Gates
        |> List.iter (fun g ->
            match g with
            | Conditional (2, _) -> ()
            | other -> Assert.Fail($"Expected all gates conditioned on qubit 2, got %A{other}"))
