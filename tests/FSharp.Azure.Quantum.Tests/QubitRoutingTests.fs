namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.LocalSimulator

/// Tests for QubitRouting — SWAP insertion for connectivity-limited devices.
module QubitRoutingTests =

    [<Fact>]
    let ``shortestPath on a line returns the full path`` () =
        Assert.Equal<int list>([ 0; 1; 2; 3 ], (QubitRouting.shortestPath (QubitRouting.linear 4) 0 3).Value)

    [<Fact>]
    let ``adjacent qubits have a length-2 path`` () =
        Assert.Equal<int list>([ 1; 2 ], (QubitRouting.shortestPath (QubitRouting.linear 4) 1 2).Value)

    [<Fact>]
    let ``route makes a non-adjacent two-qubit gate adjacent`` () =
        let cm = QubitRouting.linear 4
        let c = empty 4 |> addGate (H 0) |> addGate (CNOT(0, 3))
        Assert.False(QubitRouting.respectsCoupling cm c)
        let routed, _ = QubitRouting.route cm c
        Assert.True(QubitRouting.respectsCoupling cm routed)

    [<Fact>]
    let ``route works when the device has more qubits than the circuit`` () =
        // Regression: the routing tables must be sized to the physical device, not the logical
        // circuit. Here logical qubits 0 and 1 connect only through physical qubit 2 (a star), so
        // the SWAP path visits a physical index >= the circuit's qubit count — which used to throw
        // IndexOutOfRange because pos/phys were sized by circuit.QubitCount (2) alone.
        let cm = QubitRouting.fromPairs 3 [ (0, 2); (1, 2) ]
        let c = empty 2 |> addGate (H 0) |> addGate (CNOT(0, 1))
        Assert.False(QubitRouting.respectsCoupling cm c)
        let routed, mapping = QubitRouting.route cm c   // must not throw
        Assert.True(QubitRouting.respectsCoupling cm routed)
        Assert.Equal(3, mapping.Length)

    [<Fact>]
    let ``route is a no-op when all gates already respect the coupling`` () =
        let cm = QubitRouting.linear 3
        let c = empty 3 |> addGate (CNOT(0, 1)) |> addGate (CNOT(1, 2))
        let routed, _ = QubitRouting.route cm c
        let swaps =
            getGates routed
            |> List.filter (function SWAP _ -> true | _ -> false)
            |> List.length
        Assert.Equal(0, swaps)
        Assert.True(QubitRouting.respectsCoupling cm routed)

    /// The strongest check: the routed circuit must compute the SAME quantum state
    /// as the original, up to the logical->physical permutation routing produces.
    [<Fact>]
    let ``routing preserves circuit semantics under the qubit permutation`` () =
        let n = 4
        let cm = QubitRouting.linear n
        let backend = LocalBackend() :> IQuantumBackend
        let runAmps (c: Circuit) =
            match backend.ExecuteToState(CircuitAbstraction.wrapCircuit c) with
            | Ok(QuantumState.StateVector sv) ->
                Array.init (1 <<< StateVector.numQubits sv) (fun i -> StateVector.getAmplitude i sv)
            | _ -> failwith "expected a state vector"

        // Detect the simulator's qubit<->bit convention (LSB vs MSB).
        let probe = runAmps (empty n |> addGate (X 0))
        let nz = [ 0 .. probe.Length - 1 ] |> List.maxBy (fun i -> probe.[i].Magnitude)
        let bitpos q = if nz = 1 then q else n - 1 - q

        let circuit =
            empty n
            |> addGate (H 0)
            |> addGate (CNOT(0, 3))
            |> addGate (RX(2, 0.7))
            |> addGate (CNOT(3, 1))

        let ampsOrig = runAmps circuit
        let routed, mapping = QubitRouting.route cm circuit
        let ampsRouted = runAmps routed

        let invMap = Array.zeroCreate n
        for l in 0 .. n - 1 do
            invMap.[mapping.[l]] <- l

        let toLogical (idxP: int) =
            let mutable idxL = 0
            for p in 0 .. n - 1 do
                let bit = (idxP >>> bitpos p) &&& 1
                idxL <- idxL ||| (bit <<< bitpos invMap.[p])
            idxL

        let maxDiff =
            [ 0 .. ampsRouted.Length - 1 ]
            |> List.map (fun i -> (ampsRouted.[i] - ampsOrig.[toLogical i]).Magnitude)
            |> List.max

        Assert.True(maxDiff < 1e-9, sprintf "max amplitude diff %g" maxDiff)
