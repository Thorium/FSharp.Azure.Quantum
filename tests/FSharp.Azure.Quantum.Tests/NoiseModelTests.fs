namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder

/// Tests for NoiseModel — noise-aware routing + fidelity estimation.
module NoiseModelTests =

    [<Fact>]
    let ``noise-aware routing prefers the low-error path`` () =
        // Diamond: two 0->3 paths, via qubit 1 (noisy) or via qubit 2 (clean).
        let cm = QubitRouting.fromPairs 4 [ (0, 1); (1, 3); (0, 2); (2, 3) ]
        let noise =
            NoiseModel.create
                Map.empty
                (Map.ofList [ (1, 3), 0.5; (0, 1), 0.01; (0, 2), 0.01; (2, 3), 0.01 ])
                Map.empty
                (0.001, 0.02, 0.02)
        let circuit = empty 4 |> addGate (H 0) |> addGate (CNOT(0, 3))
        let routed, _ = NoiseModel.routeNoiseAware cm noise circuit
        let swaps = getGates routed |> List.choose (function SWAP(a, b) -> Some(a, b) | _ -> None)
        Assert.True(QubitRouting.respectsCoupling cm routed)
        Assert.NotEmpty(swaps)
        Assert.True(swaps |> List.forall (fun (a, b) -> a <> 1 && b <> 1),
            sprintf "routing should avoid the noisy qubit 1, got %A" swaps)

    [<Fact>]
    let ``fidelity is 1 with no noise and drops below 1 with noise`` () =
        let circuit = empty 2 |> addGate (H 0) |> addGate (CNOT(0, 1)) |> addMeasurement 0
        let perfect = NoiseModel.uniform 0.0 0.0 0.0
        let noisy = NoiseModel.uniform 0.001 0.01 0.02
        Assert.Equal(1.0, NoiseModel.estimateSuccessProbability perfect circuit, 9)
        let f = NoiseModel.estimateSuccessProbability noisy circuit
        Assert.True(f > 0.0 && f < 1.0, sprintf "expected 0<f<1, got %f" f)

    [<Fact>]
    let ``more gates never increase the estimated fidelity`` () =
        let noisy = NoiseModel.uniform 0.001 0.01 0.02
        let small = empty 2 |> addGate (CNOT(0, 1))
        let big = empty 2 |> addGate (CNOT(0, 1)) |> addGate (CNOT(0, 1)) |> addGate (CNOT(0, 1))
        Assert.True(NoiseModel.estimateSuccessProbability noisy big
                    <= NoiseModel.estimateSuccessProbability noisy small)
