namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core

/// Tests for the CUDA-Q source hand-off. Only the (pure, deterministic) generation is
/// tested; the optional subprocess runner needs a local python+cudaq and isn't CI-testable.
module CudaQBridgeTests =

    let private bell () =
        CircuitBuilder.empty 2
        |> CircuitBuilder.addGate (CircuitBuilder.H 0)
        |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 1))

    [<Fact>]
    let ``toKernelSource emits a valid CUDA-Q kernel for a Bell circuit`` () =
        match CudaQBridge.toKernelSource "nvidia" 1000 (bell ()) with
        | Error e -> failwith $"toKernelSource failed: {e.Message}"
        | Ok src ->
            Assert.Contains("import cudaq", src)
            Assert.Contains("cudaq.qvector(2)", src)
            Assert.Contains("h(q[0])", src)
            Assert.Contains("x.ctrl(q[0], q[1])", src)      // CNOT
            Assert.Contains("mz(q)", src)
            Assert.Contains("cudaq.set_target(\"nvidia\")", src)
            Assert.Contains("cudaq.sample(program, shots_count=1000)", src)

    [<Fact>]
    let ``toKernelSource maps rotations, phase, adjoints and CZ`` () =
        let circuit =
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.RZ (0, 1.5))
            |> CircuitBuilder.addGate (CircuitBuilder.SDG 0)
            |> CircuitBuilder.addGate (CircuitBuilder.CZ (0, 1))
        match CudaQBridge.toKernelSource "qpp-cpu" 500 circuit with
        | Error e -> failwith $"toKernelSource failed: {e.Message}"
        | Ok src ->
            Assert.Contains("rz(1.5, q[0])", src)
            Assert.Contains("s.adj(q[0])", src)
            Assert.Contains("z.ctrl(q[0], q[1])", src)

    [<Fact>]
    let ``toKernelSource rejects gates with no CUDA-Q builtin`` () =
        let circuit = CircuitBuilder.empty 2 |> CircuitBuilder.addGate (CircuitBuilder.RZZ (0, 1, 0.5))
        match CudaQBridge.toKernelSource "nvidia" 1000 circuit with
        | Error (QuantumError.OperationError ("CudaQBridge", _)) -> ()
        | other -> failwith $"expected an unsupported-gate Error, got: {other}"

    [<Fact>]
    let ``toKernelSource handles an empty circuit`` () =
        match CudaQBridge.toKernelSource "nvidia" 100 (CircuitBuilder.empty 1) with
        | Error e -> failwith $"toKernelSource failed: {e.Message}"
        | Ok src ->
            Assert.Contains("pass", src)   // no gates → a valid empty kernel body
            Assert.Contains("mz(q)", src)
