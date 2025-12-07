namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Core

module CircuitAbstractionTests =

    // ========================================================================
    // ICircuit Interface Tests
    // ========================================================================

    [<Fact>]
    let ``CircuitWrapper should implement ICircuit correctly`` () =
        let circuit = CircuitBuilder.empty 3
        let wrapper = CircuitWrapper(circuit) :> ICircuit
        
        Assert.Equal(3, wrapper.NumQubits)
        Assert.Contains("3", wrapper.Description)
        Assert.Contains("0 gates", wrapper.Description)

    [<Fact>]
    let ``QaoaCircuitWrapper should implement ICircuit correctly`` () =
        let problemHam : ProblemHamiltonian = {
            NumQubits = 2
            Terms = [||]
        }
        let mixerHam = MixerHamiltonian.create 2
        let qaoaCircuit = {
            NumQubits = 2
            InitialStateGates = [| QuantumGate.H 0; QuantumGate.H 1 |]
            Layers = [||]
            ProblemHamiltonian = problemHam
            MixerHamiltonian = mixerHam
        }
        
        let wrapper = QaoaCircuitWrapper(qaoaCircuit) :> ICircuit
        
        Assert.Equal(2, wrapper.NumQubits)
        Assert.Contains("QAOA", wrapper.Description)
        Assert.Contains("2", wrapper.Description)

    // ========================================================================
    // CircuitAdapter - CircuitBuilder to QaoaCircuit Conversion Tests
    // ========================================================================

    [<Fact>]
    let ``circuitToQaoaCircuit should preserve qubit count`` () =
        let circuit = CircuitBuilder.empty 5
        
        let result = CircuitAdapter.circuitToQaoaCircuit circuit
        
        match result with
        | Ok qaoaCircuit -> Assert.Equal(5, qaoaCircuit.NumQubits)
        | Error err -> Assert.True(false, sprintf "Conversion failed: %s" err.Message)

    [<Fact>]
    let ``circuitToQaoaCircuit should convert Hadamard gates`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 1)
        
        let result = CircuitAdapter.circuitToQaoaCircuit circuit
        
        match result with
        | Ok qaoaCircuit ->
            Assert.Equal(1, qaoaCircuit.Layers.Length)
            let mixerGates = qaoaCircuit.Layers.[0].MixerGates
            Assert.Equal(2, mixerGates.Length)
            
            match mixerGates.[0] with
            | QuantumGate.H q -> Assert.Equal(0, q)
            | _ -> Assert.True(false, "Expected H gate")
            
            match mixerGates.[1] with
            | QuantumGate.H q -> Assert.Equal(1, q)
            | _ -> Assert.True(false, "Expected H gate")
        | Error err -> Assert.True(false, sprintf "Conversion failed: %s" err.Message)

    [<Fact>]
    let ``circuitToQaoaCircuit should convert RX gates with angles`` () =
        let circuit = 
            CircuitBuilder.empty 1
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.RX (0, 1.5))
        
        let result = CircuitAdapter.circuitToQaoaCircuit circuit
        
        match result with
        | Ok qaoaCircuit ->
            let mixerGates = qaoaCircuit.Layers.[0].MixerGates
            Assert.Equal(1, mixerGates.Length)
            
            match mixerGates.[0] with
            | QuantumGate.RX (q, angle) -> 
                Assert.Equal(0, q)
                Assert.Equal(1.5, angle, 6)
            | _ -> Assert.True(false, "Expected RX gate")
        | Error err -> Assert.True(false, sprintf "Conversion failed: %s" err.Message)

    [<Fact>]
    let ``circuitToQaoaCircuit should convert CNOT gates`` () =
        let circuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.CNOT (0, 1))
        
        let result = CircuitAdapter.circuitToQaoaCircuit circuit
        
        match result with
        | Ok qaoaCircuit ->
            let mixerGates = qaoaCircuit.Layers.[0].MixerGates
            Assert.Equal(1, mixerGates.Length)
            
            match mixerGates.[0] with
            | QuantumGate.CNOT (c, t) -> 
                Assert.Equal(0, c)
                Assert.Equal(1, t)
            | _ -> Assert.True(false, "Expected CNOT gate")
        | Error err -> Assert.True(false, sprintf "Conversion failed: %s" err.Message)

    [<Fact>]
    let ``circuitToQaoaCircuit should handle empty circuits`` () =
        let circuit = CircuitBuilder.empty 3
        
        let result = CircuitAdapter.circuitToQaoaCircuit circuit
        
        match result with
        | Ok qaoaCircuit ->
            Assert.Equal(3, qaoaCircuit.NumQubits)
            Assert.Equal(1, qaoaCircuit.Layers.Length)
            Assert.Equal(0, qaoaCircuit.Layers.[0].MixerGates.Length)
        | Error err -> Assert.True(false, sprintf "Conversion failed: %s" err.Message)

    [<Fact>]
    let ``circuitToQaoaCircuit should create placeholder Hamiltonians`` () =
        let circuit = CircuitBuilder.empty 2
        
        let result = CircuitAdapter.circuitToQaoaCircuit circuit
        
        match result with
        | Ok qaoaCircuit ->
            // Problem Hamiltonian should be empty placeholder
            Assert.Equal(2, qaoaCircuit.ProblemHamiltonian.NumQubits)
            Assert.Equal(0, qaoaCircuit.ProblemHamiltonian.Terms.Length)
            
            // Mixer Hamiltonian should have X terms
            Assert.Equal(2, qaoaCircuit.MixerHamiltonian.NumQubits)
            Assert.Equal(2, qaoaCircuit.MixerHamiltonian.Terms.Length)
        | Error err -> Assert.True(false, sprintf "Conversion failed: %s" err.Message)

    // ========================================================================
    // CircuitAdapter - QaoaCircuit to CircuitBuilder Conversion Tests
    // ========================================================================

    [<Fact>]
    let ``qaoaCircuitToCircuit should preserve qubit count`` () =
        let problemHam : ProblemHamiltonian = { NumQubits = 3; Terms = [||] }
        let mixerHam = MixerHamiltonian.create 3
        let qaoaCircuit = {
            NumQubits = 3
            InitialStateGates = [||]
            Layers = [||]
            ProblemHamiltonian = problemHam
            MixerHamiltonian = mixerHam
        }
        
        let circuit = CircuitAdapter.qaoaCircuitToCircuit qaoaCircuit
        
        Assert.Equal(3, circuit.QubitCount)

    [<Fact>]
    let ``qaoaCircuitToCircuit should convert initial state gates`` () =
        let problemHam : ProblemHamiltonian = { NumQubits = 2; Terms = [||] }
        let mixerHam = MixerHamiltonian.create 2
        let qaoaCircuit = {
            NumQubits = 2
            InitialStateGates = [| QuantumGate.H 0; QuantumGate.H 1 |]
            Layers = [||]
            ProblemHamiltonian = problemHam
            MixerHamiltonian = mixerHam
        }
        
        let circuit = CircuitAdapter.qaoaCircuitToCircuit qaoaCircuit
        
        Assert.Equal(2, circuit.Gates.Length)
        
        match circuit.Gates.[0] with
        | CircuitBuilder.Gate.H q -> Assert.Equal(0, q)
        | _ -> Assert.True(false, "Expected H gate")
        
        match circuit.Gates.[1] with
        | CircuitBuilder.Gate.H q -> Assert.Equal(1, q)
        | _ -> Assert.True(false, "Expected H gate")

    [<Fact>]
    let ``qaoaCircuitToCircuit should convert QAOA layers in sequence`` () =
        let problemHam : ProblemHamiltonian = { NumQubits = 2; Terms = [||] }
        let mixerHam = MixerHamiltonian.create 2
        
        let layer1 = {
            CostGates = [| QuantumGate.RZ (0, 0.5) |]
            MixerGates = [| QuantumGate.RX (0, 1.0) |]
            Gamma = 0.5
            Beta = 1.0
        }
        
        let layer2 = {
            CostGates = [| QuantumGate.RZ (1, 0.3) |]
            MixerGates = [| QuantumGate.RX (1, 0.8) |]
            Gamma = 0.3
            Beta = 0.8
        }
        
        let qaoaCircuit = {
            NumQubits = 2
            InitialStateGates = [||]
            Layers = [| layer1; layer2 |]
            ProblemHamiltonian = problemHam
            MixerHamiltonian = mixerHam
        }
        
        let circuit = CircuitAdapter.qaoaCircuitToCircuit qaoaCircuit
        
        // Should have: CostGates1 + MixerGates1 + CostGates2 + MixerGates2 = 4 gates
        Assert.Equal(4, circuit.Gates.Length)
        
        // Layer 1 cost gate
        match circuit.Gates.[0] with
        | CircuitBuilder.Gate.RZ (q, angle) -> 
            Assert.Equal(0, q)
            Assert.Equal(0.5, angle, 6)
        | _ -> Assert.True(false, "Expected RZ gate")
        
        // Layer 1 mixer gate
        match circuit.Gates.[1] with
        | CircuitBuilder.Gate.RX (q, angle) -> 
            Assert.Equal(0, q)
            Assert.Equal(1.0, angle, 6)
        | _ -> Assert.True(false, "Expected RX gate")

    [<Fact>]
    let ``qaoaCircuitToCircuit should convert RY gates`` () =
        let problemHam : ProblemHamiltonian = { NumQubits = 1; Terms = [||] }
        let mixerHam = MixerHamiltonian.create 1
        
        let layer = {
            CostGates = [||]
            MixerGates = [| QuantumGate.RY (0, 2.5) |]
            Gamma = 0.0
            Beta = 2.5
        }
        
        let qaoaCircuit = {
            NumQubits = 1
            InitialStateGates = [||]
            Layers = [| layer |]
            ProblemHamiltonian = problemHam
            MixerHamiltonian = mixerHam
        }
        
        let circuit = CircuitAdapter.qaoaCircuitToCircuit qaoaCircuit
        
        Assert.Equal(1, circuit.Gates.Length)
        
        match circuit.Gates.[0] with
        | CircuitBuilder.Gate.RY (q, angle) -> 
            Assert.Equal(0, q)
            Assert.Equal(2.5, angle, 6)
        | _ -> Assert.True(false, "Expected RY gate")

    // ========================================================================
    // Round-trip Conversion Tests
    // ========================================================================

    [<Fact>]
    let ``Round-trip conversion should preserve qubit count`` () =
        let originalCircuit = CircuitBuilder.empty 4
        
        let qaoaResult = CircuitAdapter.circuitToQaoaCircuit originalCircuit
        
        match qaoaResult with
        | Ok qaoaCircuit ->
            let finalCircuit = CircuitAdapter.qaoaCircuitToCircuit qaoaCircuit
            Assert.Equal(originalCircuit.QubitCount, finalCircuit.QubitCount)
        | Error err -> Assert.True(false, sprintf "Conversion failed: %s" err.Message)

    [<Fact>]
    let ``Round-trip conversion should preserve gate structure`` () =
        let originalCircuit = 
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.Gate.RX (1, 1.5))
        
        let qaoaResult = CircuitAdapter.circuitToQaoaCircuit originalCircuit
        
        match qaoaResult with
        | Ok qaoaCircuit ->
            let finalCircuit = CircuitAdapter.qaoaCircuitToCircuit qaoaCircuit
            Assert.Equal(originalCircuit.Gates.Length, finalCircuit.Gates.Length)
            
            // Check H gate preserved
            match finalCircuit.Gates.[0] with
            | CircuitBuilder.Gate.H q -> Assert.Equal(0, q)
            | _ -> Assert.True(false, "Expected H gate")
            
            // Check RX gate and angle preserved
            match finalCircuit.Gates.[1] with
            | CircuitBuilder.Gate.RX (q, angle) -> 
                Assert.Equal(1, q)
                Assert.Equal(1.5, angle, 6)
            | _ -> Assert.True(false, "Expected RX gate")
        | Error err -> Assert.True(false, sprintf "Conversion failed: %s" err.Message)

    // ========================================================================
    // Edge Cases and Error Handling
    // ========================================================================

    [<Fact>]
    let ``circuitToQaoaCircuit should handle single qubit circuits`` () =
        let circuit = CircuitBuilder.empty 1
        
        let result = CircuitAdapter.circuitToQaoaCircuit circuit
        
        match result with
        | Ok qaoaCircuit -> Assert.Equal(1, qaoaCircuit.NumQubits)
        | Error err -> Assert.True(false, sprintf "Conversion failed: %s" err.Message)

    [<Fact>]
    let ``circuitToQaoaCircuit should handle circuits with many gates`` () =
        let mutable circuit = CircuitBuilder.empty 3
        for i in 0 .. 2 do
            circuit <- CircuitBuilder.addGate (CircuitBuilder.Gate.H i) circuit
            circuit <- CircuitBuilder.addGate (CircuitBuilder.Gate.RX (i, float i)) circuit
        
        let result = CircuitAdapter.circuitToQaoaCircuit circuit
        
        match result with
        | Ok qaoaCircuit -> 
            Assert.Equal(3, qaoaCircuit.NumQubits)
            Assert.Equal(6, qaoaCircuit.Layers.[0].MixerGates.Length)
        | Error err -> Assert.True(false, sprintf "Conversion failed: %s" err.Message)

    [<Fact>]
    let ``qaoaCircuitToCircuit should handle empty layers`` () =
        let problemHam : ProblemHamiltonian = { NumQubits = 2; Terms = [||] }
        let mixerHam = MixerHamiltonian.create 2
        let qaoaCircuit = {
            NumQubits = 2
            InitialStateGates = [||]
            Layers = [||]
            ProblemHamiltonian = problemHam
            MixerHamiltonian = mixerHam
        }
        
        let circuit = CircuitAdapter.qaoaCircuitToCircuit qaoaCircuit
        
        Assert.Equal(2, circuit.QubitCount)
        Assert.Equal(0, circuit.Gates.Length)
