namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open CircuitBuilder

/// Tests for functional composition operators and circuit patterns (Phase 1)
module CircuitCompositionTests =
    
    [<Fact>]
    let ``bellState creates H followed by CNOT`` () =
        let bell = bellState 0 1
        Assert.Equal(2, bell.Gates.Length)
        Assert.Equal(Gate.H 0, bell.Gates.[0])
        Assert.Equal(Gate.CNOT(0, 1), bell.Gates.[1])
    
    [<Fact>]
    let ``ghzState for three qubits has H and two CNOTs`` () =
        let ghz = ghzState [0; 1; 2]
        Assert.Equal(3, ghz.Gates.Length)
    
    [<Fact>]
    let ``qft single qubit equals Hadamard`` () =
        let qftCircuit = qft [0]
        Assert.Equal(1, qftCircuit.Gates.Length)
        Assert.Equal(Gate.H 0, qftCircuit.Gates.[0])
    
    [<Fact>]
    let ``reverse inverts T to TDG`` () =
        let circuit = empty 1 |> addGate (Gate.T 0)
        let rev = reverse circuit
        Assert.Equal(1, rev.Gates.Length)
        Assert.Equal(Gate.TDG 0, rev.Gates.[0])
    
    [<Fact>]
    let ``reverse inverts S to SDG`` () =
        let circuit = empty 1 |> addGate (Gate.S 0)
        let rev = reverse circuit
        Assert.Equal(Gate.SDG 0, rev.Gates.[0])
    
    [<Fact>]
    let ``reverse keeps H unchanged`` () =
        let circuit = empty 1 |> addGate (Gate.H 0)
        let rev = reverse circuit
        Assert.Equal(Gate.H 0, rev.Gates.[0])
    
    [<Fact>]
    let ``mapGates transforms H to X`` () =
        let circuit = empty 1 |> addGate (Gate.H 0)
        let transformH g = match g with | Gate.H q -> Gate.X q | other -> other
        let mapped = mapGates transformH circuit
        Assert.Equal(Gate.X 0, mapped.Gates.[0])
    
    [<Fact>]
    let ``filterGates removes measurements`` () =
        let circuit = empty 2 |> addGate (Gate.H 0) |> addGate (Gate.Measure 0) |> addGate (Gate.X 1)
        let notMeasurement g = match g with | Gate.Measure _ -> false | _ -> true
        let filtered = filterGates notMeasurement circuit
        Assert.Equal(2, filtered.Gates.Length)
    
    [<Fact>]
    let ``swapViaCNOT produces three CNOTs`` () =
        let swap = swapViaCNOT 0 1
        Assert.Equal(3, swap.Gates.Length)
    
    [<Fact>]
    let ``toffoliViaCliffordT contains seven T gates`` () =
        let toffoli = toffoliViaCliffordT 0 1 2
        let isT g = match g with | Gate.T _ | Gate.TDG _ -> true | _ -> false
        let tGates = toffoli.Gates |> List.filter isT
        Assert.Equal(7, tGates.Length)
    
    // ========================================================================
    // PHASE 2: OPTIMIZATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``commute returns true for disjoint gates`` () =
        let g1 = Gate.H 0
        let g2 = Gate.X 1
        Assert.True(commute g1 g2)
    
    [<Fact>]
    let ``commute returns true for Z on CNOT control`` () =
        let g1 = Gate.Z 0
        let g2 = Gate.CNOT(0, 1)
        Assert.True(commute g1 g2)
    
    [<Fact>]
    let ``commute returns false for non-commuting gates`` () =
        let g1 = Gate.H 0
        let g2 = Gate.X 0
        Assert.False(commute g1 g2)
    
    [<Fact>]
    let ``statistics counts gate types correctly`` () =
        let circuit = 
            empty 2
            |> addGate (Gate.H 0)
            |> addGate (Gate.CNOT(0, 1))
            |> addGate (Gate.T 1)
            |> addGate (Gate.Measure 0)
        
        let stats = statistics circuit
        Assert.Equal(4, stats.TotalGates)
        Assert.Equal(2, stats.SingleQubitGates)  // H and T
        Assert.Equal(1, stats.TwoQubitGates)     // CNOT
        Assert.Equal(1, stats.MeasurementCount)
    
    [<Fact>]
    let ``twoQubitGateCount counts CNOTs`` () =
        let circuit =
            bellState 0 1  // H + CNOT
        
        let count = twoQubitGateCount circuit
        Assert.Equal(1, count)
    
    [<Fact>]
    let ``depth calculates correct circuit depth`` () =
        // Sequential gates on same qubit: depth = 2
        let circuit1 =
            empty 1
            |> addGate (Gate.H 0)
            |> addGate (Gate.X 0)
        Assert.Equal(2, depth circuit1)
        
        // Parallel gates on different qubits: depth = 1
        let circuit2 =
            empty 2
            |> addGate (Gate.H 0)
            |> addGate (Gate.X 1)
        Assert.Equal(1, depth circuit2)
    
    [<Fact>]
    let ``removeIdentities removes RZ with zero angle`` () =
        let circuit =
            empty 1
            |> addGate (Gate.RZ(0, 0.0))
            |> addGate (Gate.H 0)
        
        let cleaned = removeIdentities circuit
        Assert.Equal(1, cleaned.Gates.Length)
        Assert.Equal(Gate.H 0, cleaned.Gates.[0])
    
    [<Fact>]
    let ``optimizeFully combines all optimizations`` () =
        // H-H should cancel, RZ(0) should be removed
        let circuit =
            empty 1
            |> addGate (Gate.H 0)
            |> addGate (Gate.H 0)
            |> addGate (Gate.RZ(0, 0.0))
            |> addGate (Gate.X 0)
        
        let optimized = optimizeFully circuit
        Assert.Equal(1, optimized.Gates.Length)  // Only X remains
        Assert.Equal(Gate.X 0, optimized.Gates.[0])
    
    [<Fact>]
    let ``optimize merges consecutive rotations`` () =
        let angle1 = System.Math.PI / 4.0
        let angle2 = System.Math.PI / 2.0
        
        let circuit =
            empty 1
            |> addGate (Gate.RZ(0, angle1))
            |> addGate (Gate.RZ(0, angle2))
        
        let optimized = optimize circuit
        Assert.Equal(1, optimized.Gates.Length)
        
        match optimized.Gates.[0] with
        | Gate.RZ(q, angle) ->
            Assert.Equal(0, q)
            Assert.Equal(angle1 + angle2, angle, 5)  // 5 decimal places
        | _ -> Assert.True(false, "Expected RZ gate")
