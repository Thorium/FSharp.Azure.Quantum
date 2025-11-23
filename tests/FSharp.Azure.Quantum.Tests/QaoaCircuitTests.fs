module FSharp.Azure.Quantum.Tests.QaoaCircuitTests

open Xunit
open FSharp.Azure.Quantum.Core.QaoaCircuit

[<Fact>]
let ``Problem Hamiltonian should construct from 2x2 QUBO matrix`` () =
    // Simple 2-variable QUBO: minimize x0*x1
    // Q matrix:
    // [ 0    0.5 ]
    // [ 0.5  0   ]
    // Total: (0.5 + 0.5) * x0*x1 = 1.0 * x0*x1
    //
    // Converting to Ising with x_i = (1 - Z_i)/2:
    // 1.0 * x0*x1 = 1.0 * (1-Z0)/2 * (1-Z1)/2 
    //             = 0.25 * (1 - Z0 - Z1 + Z0*Z1)
    // ZZ term coefficient: 0.25
    let quboMatrix = array2D [
        [0.0; 0.5]
        [0.5; 0.0]
    ]
    
    let hamiltonian = ProblemHamiltonian.fromQubo quboMatrix
    
    Assert.NotNull(hamiltonian)
    Assert.Equal(2, hamiltonian.NumQubits)
    Assert.Equal(1, hamiltonian.Terms.Length)
    
    let term = hamiltonian.Terms[0]
    Assert.Equal(0.25, term.Coefficient)
    Assert.Equal<int seq>([| 0; 1 |], term.QubitsIndices)
    Assert.Equal<PauliOperator seq>([| PauliZ; PauliZ |], term.PauliOperators)

[<Fact>]
let ``Problem Hamiltonian should handle diagonal QUBO terms`` () =
    // QUBO with diagonal terms: minimize -x0 - 2*x1
    // Q matrix:
    // [ -1   0 ]
    // [  0  -2 ]
    //
    // Converting to Ising with x_i = (1 - Z_i)/2:
    // -1.0 * x0 = -1.0 * (1 - Z0)/2 = -0.5 * (1 - Z0) = -0.5 + 0.5*Z0
    // -2.0 * x1 = -2.0 * (1 - Z1)/2 = -1.0 * (1 - Z1) = -1.0 + 1.0*Z1
    // Z terms: 0.5*Z0 + 1.0*Z1
    let quboMatrix = array2D [
        [-1.0; 0.0]
        [0.0; -2.0]
    ]
    
    let hamiltonian = ProblemHamiltonian.fromQubo quboMatrix
    
    Assert.Equal(2, hamiltonian.NumQubits)
    Assert.Equal(2, hamiltonian.Terms.Length)
    
    // Check first term
    let term0 = hamiltonian.Terms[0]
    Assert.Equal(0.5, term0.Coefficient)
    Assert.Equal<int seq>([| 0 |], term0.QubitsIndices)
    Assert.Equal<PauliOperator seq>([| PauliZ |], term0.PauliOperators)
    
    // Check second term
    let term1 = hamiltonian.Terms[1]
    Assert.Equal(1.0, term1.Coefficient)
    Assert.Equal<int seq>([| 1 |], term1.QubitsIndices)
    Assert.Equal<PauliOperator seq>([| PauliZ |], term1.PauliOperators)

[<Fact>]
let ``Mixer Hamiltonian should create X operators for all qubits`` () =
    // Mixer Hamiltonian: H_mix = X0 + X1 + X2
    // Each term has coefficient 1.0
    let numQubits = 3
    
    let mixer = MixerHamiltonian.create numQubits
    
    Assert.Equal(3, mixer.NumQubits)
    Assert.Equal(3, mixer.Terms.Length)
    
    // Check all terms are single-qubit X operators with coefficient 1.0
    for i in 0 .. numQubits - 1 do
        let term = mixer.Terms[i]
        Assert.Equal(1.0, term.Coefficient)
        Assert.Equal<int seq>([| i |], term.QubitsIndices)
        Assert.Equal<PauliOperator seq>([| PauliX |], term.PauliOperators)

[<Fact>]
let ``QAOA layer should construct cost and mixer gates`` () =
    // Create simple 2-qubit problem: x0*x1
    let quboMatrix = array2D [
        [0.0; 0.5]
        [0.5; 0.0]
    ]
    let problemHam = ProblemHamiltonian.fromQubo quboMatrix
    let mixerHam = MixerHamiltonian.create 2
    
    let gamma = 0.5
    let beta = 0.3
    
    let layer = QaoaCircuit.buildLayer problemHam mixerHam gamma beta
    
    // Cost gates: Should have RZZ gate for the ZZ interaction
    Assert.NotEmpty(layer.CostGates)
    Assert.Equal(gamma, layer.Gamma)
    
    // Mixer gates: Should have RX gates for both qubits
    Assert.Equal(2, layer.MixerGates.Length)
    Assert.Equal(beta, layer.Beta)
    
    // Check mixer gates are RX rotations
    match layer.MixerGates[0] with
    | RX (qubit, angle) ->
        Assert.Equal(0, qubit)
        Assert.Equal(2.0 * beta, angle)  // RX(2β) for mixer layer
    | _ -> Assert.True(false, "Expected RX gate")
    
    match layer.MixerGates[1] with
    | RX (qubit, angle) ->
        Assert.Equal(1, qubit)
        Assert.Equal(2.0 * beta, angle)
    | _ -> Assert.True(false, "Expected RX gate")

[<Fact>]
let ``Complete QAOA circuit should have initial state and multiple layers`` () =
    // Create 2-qubit QAOA circuit with p=2 layers
    let quboMatrix = array2D [
        [0.0; 0.5]
        [0.5; 0.0]
    ]
    let problemHam = ProblemHamiltonian.fromQubo quboMatrix
    let mixerHam = MixerHamiltonian.create 2
    
    // Parameters for 2 layers: [(γ1, β1), (γ2, β2)]
    let parameters = [| (0.5, 0.3); (0.7, 0.4) |]
    
    let circuit = QaoaCircuit.build problemHam mixerHam parameters
    
    // Check circuit structure
    Assert.Equal(2, circuit.NumQubits)
    Assert.Equal(2, circuit.Layers.Length)
    
    // Check initial state gates (Hadamard on all qubits)
    Assert.Equal(2, circuit.InitialStateGates.Length)
    for i in 0 .. 1 do
        match circuit.InitialStateGates[i] with
        | H qubit -> Assert.Equal(i, qubit)
        | _ -> Assert.True(false, "Expected Hadamard gate")
    
    // Check first layer parameters
    Assert.Equal(0.5, circuit.Layers[0].Gamma)
    Assert.Equal(0.3, circuit.Layers[0].Beta)
    
    // Check second layer parameters
    Assert.Equal(0.7, circuit.Layers[1].Gamma)
    Assert.Equal(0.4, circuit.Layers[1].Beta)
    
    // Verify Hamiltonians are stored
    Assert.Equal(problemHam.NumQubits, circuit.ProblemHamiltonian.NumQubits)
    Assert.Equal(mixerHam.NumQubits, circuit.MixerHamiltonian.NumQubits)

[<Fact>]
let ``QAOA circuit should serialize to OpenQASM format`` () =
    // Create simple 2-qubit QAOA circuit
    let quboMatrix = array2D [
        [0.0; 0.5]
        [0.5; 0.0]
    ]
    let problemHam = ProblemHamiltonian.fromQubo quboMatrix
    let mixerHam = MixerHamiltonian.create 2
    let parameters = [| (0.5, 0.3) |]  // Single layer
    
    let circuit = QaoaCircuit.build problemHam mixerHam parameters
    let qasm = QaoaCircuit.toOpenQasm circuit
    
    // Check OpenQASM header
    Assert.Contains("OPENQASM 2.0", qasm)
    Assert.Contains("include \"qelib1.inc\"", qasm)
    
    // Check qubit declaration
    Assert.Contains("qreg q[2]", qasm)
    
    // Check initial state (Hadamard gates)
    Assert.Contains("h q[0]", qasm)
    Assert.Contains("h q[1]", qasm)
    
    // Check cost layer gates (RZZ for ZZ interaction)
    // ZZ interaction coefficient: 0.25 (from QUBO conversion)
    // Angle: 2 * 0.25 * 0.5 = 0.25
    Assert.Contains("rzz(0.25)", qasm)
    
    // Check mixer layer gates (RX rotations)
    // Angle: 2 * 1.0 * 0.3 = 0.6
    Assert.Contains("rx(0.6) q[0]", qasm)
    Assert.Contains("rx(0.6) q[1]", qasm)
