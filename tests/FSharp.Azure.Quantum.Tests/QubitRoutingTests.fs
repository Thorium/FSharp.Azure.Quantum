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
        let routed, mapping = QubitRouting.route cm c // must not throw
        Assert.True(QubitRouting.respectsCoupling cm routed)
        Assert.Equal(3, mapping.Length)

    [<Fact>]
    let ``route widens the circuit to the physical qubits it uses`` () =
        // 5 logical qubits on a 3x3 grid. CNOT(2,3) is routed through physical 5 and 4, which
        // pushes logical 4 (already flipped) onto physical 5 — beyond the logical width.
        let cm = QubitRouting.grid 3 3
        let c = empty 5 |> addGate (X 4) |> addGate (CNOT(2, 3))
        let routed, mapping = QubitRouting.route cm c

        Assert.Equal<Gate list>([ X 4; SWAP(2, 5); SWAP(5, 4); CNOT(4, 3) ], getGates routed)
        Assert.Equal<int[]>([| 0; 1; 4; 3; 5; 2; 6; 7; 8 |], mapping)
        // Physical 5 holds logical 4, so the routed circuit spans physical 0-5.
        Assert.Equal(6, routed.QubitCount)
        Assert.True(QubitRouting.respectsCoupling cm routed)

    [<Fact>]
    let ``a routed circuit's width covers every logical qubit's final position and every gate`` () =
        let cm = QubitRouting.grid 3 3

        let circuits =
            [
                empty 5 |> addGate (X 4) |> addGate (CNOT(2, 3))
                empty 4 |> addGate (H 0) |> addGate (CNOT(0, 3)) |> addGate (CZ(1, 2))
                empty 6 |> addGate (CNOT(0, 5)) |> addGate (CNOT(1, 4)) |> addGate (RZZ(2, 3, 0.3))
                empty 2 |> addGate (H 1)
            ]

        for c in circuits do
            let routed, mapping = QubitRouting.route cm c

            for logical in 0 .. c.QubitCount - 1 do
                Assert.True(
                    mapping.[logical] < routed.QubitCount,
                    $"logical %d{logical} rests on physical %d{mapping.[logical]}, outside %d{routed.QubitCount} qubits"
                )

            Assert.True(routed.QubitCount >= c.QubitCount)
            Assert.True(routed.QubitCount <= cm.NumQubits)

            match validate routed with
            | result when result.IsValid -> ()
            | result -> Assert.Fail($"routed circuit is not valid: %A{result}")

    [<Fact>]
    let ``route keeps the width of a circuit that needs no qubit outside it`` () =
        let routed, _ =
            QubitRouting.route (QubitRouting.linear 6) (empty 3 |> addGate (CNOT(0, 2)))

        Assert.Equal(3, routed.QubitCount)

    [<Fact>]
    let ``route is a no-op when all gates already respect the coupling`` () =
        let cm = QubitRouting.linear 3
        let c = empty 3 |> addGate (CNOT(0, 1)) |> addGate (CNOT(1, 2))
        let routed, _ = QubitRouting.route cm c

        let swaps =
            getGates routed
            |> List.filter (function
                | SWAP _ -> true
                | _ -> false)
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
            List.init ampsRouted.Length (fun i -> (ampsRouted.[i] - ampsOrig.[toLogical i]).Magnitude)
            |> List.max

        Assert.True(maxDiff < 1e-9, $"max amplitude diff %g{maxDiff}")
