namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.QaoaCircuit

module BackendAbstractionTests =

    // ========================================================================
    // Local Backend Tests
    // ========================================================================

    [<Fact>]
    let ``LocalBackend should have correct name`` () =
        let backend = createLocalBackend()
        Assert.Equal("Local QAOA Simulator", backend.Name)

    [<Fact>]
    let ``LocalBackend should support QAOA gates`` () =
        let backend = createLocalBackend()
        let gates = backend.SupportedGates
        
        Assert.Contains("H", gates)
        Assert.Contains("RX", gates)
        Assert.Contains("RY", gates)
        Assert.Contains("RZ", gates)
        Assert.Contains("CNOT", gates)
        Assert.Contains("RZZ", gates)

    [<Fact>]
    let ``LocalBackend should have max 10 qubits`` () =
        let backend = createLocalBackend()
        Assert.Equal(10, backend.MaxQubits)

    [<Fact>]
    let ``LocalBackend should reject negative shots`` () =
        let backend = createLocalBackend()
        let circuit = CircuitBuilder.empty 2
        let wrapper = CircuitWrapper(circuit) :> ICircuit
        
        let result = backend.Execute wrapper -1
        
        match result with
        | Error msg -> Assert.Contains("positive", msg)
        | Ok _ -> Assert.True(false, "Should have failed with negative shots")

    [<Fact>]
    let ``LocalBackend should reject zero shots`` () =
        let backend = createLocalBackend()
        let circuit = CircuitBuilder.empty 2
        let wrapper = CircuitWrapper(circuit) :> ICircuit
        
        let result = backend.Execute wrapper 0
        
        match result with
        | Error msg -> Assert.Contains("positive", msg)
        | Ok _ -> Assert.True(false, "Should have failed with zero shots")

    [<Fact>]
    let ``LocalBackend should reject circuits with too many qubits`` () =
        let backend = createLocalBackend()
        let circuit = CircuitBuilder.empty 15  // Exceeds 10 qubit limit
        let wrapper = CircuitWrapper(circuit) :> ICircuit
        
        let result = backend.Execute wrapper 10
        
        match result with
        | Error msg -> 
            Assert.Contains("10 qubits", msg)
        | Ok _ -> Assert.True(false, "Should have failed with too many qubits")

    [<Fact>]
    let ``LocalBackend should execute simple 2-qubit circuit`` () =
        let backend = createLocalBackend()
        
        // Create simple QAOA circuit
        let problemHam : ProblemHamiltonian = {
            NumQubits = 2
            Terms = [||]  // No cost terms for simplicity
        }
        let mixerHam = MixerHamiltonian.create 2
        
        let layer = {
            CostGates = [||]
            MixerGates = [| QuantumGate.RX (0, 0.5); QuantumGate.RX (1, 0.5) |]
            Gamma = 0.0
            Beta = 0.5
        }
        
        let qaoaCircuit = {
            NumQubits = 2
            InitialStateGates = [| QuantumGate.H 0; QuantumGate.H 1 |]
            Layers = [| layer |]
            ProblemHamiltonian = problemHam
            MixerHamiltonian = mixerHam
        }
        
        let wrapper = QaoaCircuitWrapper(qaoaCircuit) :> ICircuit
        let result = backend.Execute wrapper 100
        
        match result with
        | Ok execResult ->
            Assert.Equal(100, execResult.NumShots)
            Assert.Equal(100, execResult.Measurements.Length)
            Assert.Equal("Local QAOA Simulator", execResult.BackendName)
            
            // Each measurement should be a 2-element array
            for measurement in execResult.Measurements do
                Assert.Equal(2, measurement.Length)
                // Each qubit should be 0 or 1
                for qubit in measurement do
                    Assert.True(qubit = 0 || qubit = 1, sprintf "Expected 0 or 1, got %d" qubit)
        | Error msg ->
            Assert.True(false, sprintf "Execution failed: %s" msg)

    [<Fact>]
    let ``LocalBackend should handle empty circuit`` () =
        let backend = createLocalBackend()
        let circuit = CircuitBuilder.empty 3
        let wrapper = CircuitWrapper(circuit) :> ICircuit
        
        let result = backend.Execute wrapper 50
        
        match result with
        | Ok execResult ->
            Assert.Equal(50, execResult.NumShots)
            Assert.Equal(50, execResult.Measurements.Length)
            
            // Each measurement should be a 3-element array
            for measurement in execResult.Measurements do
                Assert.Equal(3, measurement.Length)
        | Error msg ->
            Assert.True(false, sprintf "Execution failed: %s" msg)

    [<Fact>]
    let ``LocalBackend should produce valid measurement bitstrings`` () =
        let backend = createLocalBackend()
        
        // Create QAOA circuit with superposition
        let problemHam : ProblemHamiltonian = {
            NumQubits = 2
            Terms = [||]
        }
        let mixerHam = MixerHamiltonian.create 2
        
        let qaoaCircuit = {
            NumQubits = 2
            InitialStateGates = [| QuantumGate.H 0; QuantumGate.H 1 |]  // Uniform superposition
            Layers = [||]  // No evolution - pure superposition
            ProblemHamiltonian = problemHam
            MixerHamiltonian = mixerHam
        }
        
        let wrapper = QaoaCircuitWrapper(qaoaCircuit) :> ICircuit
        let result = backend.Execute wrapper 100
        
        match result with
        | Ok execResult ->
            // With uniform superposition, we should see all 4 possible outcomes
            // [0,0], [0,1], [1,0], [1,1] with roughly equal probability
            let outcomes = 
                execResult.Measurements
                |> Array.map (fun bits -> (bits.[0], bits.[1]))
                |> Array.groupBy id
                |> Array.map (fun (outcome, instances) -> (outcome, instances.Length))
                |> Map.ofArray
            
            // We should have at least 2 different outcomes with 100 shots
            // (statistically very unlikely to get only one outcome)
            Assert.True(outcomes.Count > 1, 
                sprintf "Expected multiple outcomes, got only: %A" outcomes)
            
            // All outcomes should be valid bitstrings
            for (bit0, bit1) in outcomes |> Map.toSeq |> Seq.map fst do
                Assert.True(bit0 = 0 || bit0 = 1)
                Assert.True(bit1 = 0 || bit1 = 1)
        | Error msg ->
            Assert.True(false, sprintf "Execution failed: %s" msg)

    // ========================================================================
    // IonQ Backend Tests
    // ========================================================================

    [<Fact>]
    let ``IonQBackendWrapper should have correct name`` () =
        use httpClient = new System.Net.Http.HttpClient()
        let backend = createIonQBackend httpClient "https://fake-workspace.quantum.azure.com" "ionq.simulator"
        Assert.Equal("IonQ Simulator", backend.Name)

    [<Fact>]
    let ``IonQBackendWrapper should support IonQ gates`` () =
        use httpClient = new System.Net.Http.HttpClient()
        let backend = createIonQBackend httpClient "https://fake-workspace.quantum.azure.com" "ionq.simulator"
        let gates = backend.SupportedGates
        
        Assert.Contains("H", gates)
        Assert.Contains("X", gates)
        Assert.Contains("Y", gates)
        Assert.Contains("Z", gates)
        Assert.Contains("RX", gates)
        Assert.Contains("RY", gates)
        Assert.Contains("RZ", gates)
        Assert.Contains("CNOT", gates)
        Assert.Contains("S", gates)
        Assert.Contains("T", gates)

    [<Fact>]
    let ``IonQBackendWrapper should have max 29 qubits`` () =
        use httpClient = new System.Net.Http.HttpClient()
        let backend = createIonQBackend httpClient "https://fake-workspace.quantum.azure.com" "ionq.simulator"
        Assert.Equal(29, backend.MaxQubits)

    // Note: Execution tests removed - they would require real Azure Quantum workspace
    // Integration tests should be added separately with proper authentication

    // ========================================================================
    // Rigetti Backend Tests
    // ========================================================================

    [<Fact>]
    let ``RigettiBackendWrapper should have correct name`` () =
        use httpClient = new System.Net.Http.HttpClient()
        let backend = createRigettiBackend httpClient "https://fake-workspace.quantum.azure.com" "rigetti.sim.qvm"
        Assert.Equal("Rigetti QVM", backend.Name)

    [<Fact>]
    let ``RigettiBackendWrapper should support Rigetti gates`` () =
        use httpClient = new System.Net.Http.HttpClient()
        let backend = createRigettiBackend httpClient "https://fake-workspace.quantum.azure.com" "rigetti.sim.qvm"
        let gates = backend.SupportedGates
        
        Assert.Contains("H", gates)
        Assert.Contains("X", gates)
        Assert.Contains("Y", gates)
        Assert.Contains("Z", gates)
        Assert.Contains("RX", gates)
        Assert.Contains("RY", gates)
        Assert.Contains("RZ", gates)
        Assert.Contains("CZ", gates)  // Native Rigetti gate
        Assert.Contains("MEASURE", gates)

    [<Fact>]
    let ``RigettiBackendWrapper should have max 40 qubits`` () =
        use httpClient = new System.Net.Http.HttpClient()
        let backend = createRigettiBackend httpClient "https://fake-workspace.quantum.azure.com" "rigetti.sim.qvm"
        Assert.Equal(40, backend.MaxQubits)

    // Note: Execution tests removed - they would require real Azure Quantum workspace
    // Integration tests should be added separately with proper authentication

    // ========================================================================
    // Circuit Validation Tests
    // ========================================================================

    [<Fact>]
    let ``validateCircuitForBackend should accept compatible circuit`` () =
        let backend = createLocalBackend()
        let circuit = CircuitBuilder.empty 5
        let wrapper = CircuitWrapper(circuit) :> ICircuit
        
        let result = validateCircuitForBackend wrapper backend
        
        match result with
        | Ok () -> Assert.True(true)
        | Error msg -> Assert.True(false, sprintf "Validation should succeed: %s" msg)

    [<Fact>]
    let ``validateCircuitForBackend should reject too many qubits`` () =
        let backend = createLocalBackend()
        let circuit = CircuitBuilder.empty 15  // Exceeds 10 qubit limit
        let wrapper = CircuitWrapper(circuit) :> ICircuit
        
        let result = validateCircuitForBackend wrapper backend
        
        match result with
        | Error msg ->
            Assert.Contains("15 qubits", msg)
            Assert.Contains("10 qubits", msg)
        | Ok () -> Assert.True(false, "Validation should fail with too many qubits")

    [<Fact>]
    let ``validateCircuitForBackend should check backend limits`` () =
        use httpClient1 = new System.Net.Http.HttpClient()
        use httpClient2 = new System.Net.Http.HttpClient()
        let ionqBackend = createIonQBackend httpClient1 "https://fake-workspace.quantum.azure.com" "ionq.simulator"
        let rigettiBackend = createRigettiBackend httpClient2 "https://fake-workspace.quantum.azure.com" "rigetti.sim.qvm"
        
        // 25 qubits - valid for Rigetti (40 max) but valid for IonQ (29 max)
        let circuit25 = CircuitBuilder.empty 25
        let wrapper25 = CircuitWrapper(circuit25) :> ICircuit
        
        // IonQ: 25 qubits OK (< 29)
        match validateCircuitForBackend wrapper25 ionqBackend with
        | Ok () -> Assert.True(true)
        | Error msg -> Assert.True(false, sprintf "IonQ should accept 25 qubits: %s" msg)
        
        // Rigetti: 25 qubits OK (< 40)
        match validateCircuitForBackend wrapper25 rigettiBackend with
        | Ok () -> Assert.True(true)
        | Error msg -> Assert.True(false, sprintf "Rigetti should accept 25 qubits: %s" msg)

    [<Fact>]
    let ``validateCircuitForBackend should reject at exact limit`` () =
        use httpClient = new System.Net.Http.HttpClient()
        let ionqBackend = createIonQBackend httpClient "https://fake-workspace.quantum.azure.com" "ionq.simulator"
        
        // 30 qubits - exceeds IonQ 29 qubit limit
        let circuit30 = CircuitBuilder.empty 30
        let wrapper30 = CircuitWrapper(circuit30) :> ICircuit
        
        match validateCircuitForBackend wrapper30 ionqBackend with
        | Error msg ->
            Assert.Contains("30 qubits", msg)
            Assert.Contains("29 qubits", msg)
        | Ok () -> Assert.True(false, "IonQ should reject 30 qubits")

    // ========================================================================
    // ExecutionResult Tests
    // ========================================================================

    [<Fact>]
    let ``ExecutionResult should contain metadata`` () =
        let backend = createLocalBackend()
        let circuit = CircuitBuilder.empty 2
        let wrapper = CircuitWrapper(circuit) :> ICircuit
        
        let result = backend.Execute wrapper 10
        
        match result with
        | Ok execResult ->
            Assert.NotNull(execResult.Metadata)
            Assert.True(Map.isEmpty execResult.Metadata)  // Empty for local backend
        | Error msg ->
            Assert.True(false, sprintf "Execution failed: %s" msg)

    [<Fact>]
    let ``ExecutionResult measurements should match requested shots`` () =
        let backend = createLocalBackend()
        let circuit = CircuitBuilder.empty 3
        let wrapper = CircuitWrapper(circuit) :> ICircuit
        
        let testShots = [1; 10; 50; 100]
        
        for numShots in testShots do
            let result = backend.Execute wrapper numShots
            
            match result with
            | Ok execResult ->
                Assert.Equal(numShots, execResult.NumShots)
                Assert.Equal(numShots, execResult.Measurements.Length)
            | Error msg ->
                Assert.True(false, sprintf "Execution with %d shots failed: %s" numShots msg)
