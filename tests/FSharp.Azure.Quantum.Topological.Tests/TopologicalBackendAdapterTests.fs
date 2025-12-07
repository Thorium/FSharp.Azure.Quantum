namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Topological.Integration.TopologicalBackendAdapter

module TopologicalBackendAdapterTests =
    
    [<Fact>]
    let ``TopologicalBackendAdapter - Creates adapter from topological backend`` () =
        // Arrange: Create a topological backend (Ising anyons, 10 max anyons)
        let topoBackend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        
        // Act: Create adapter
        let adapter = createIsingAdapter topoBackend
        
        // Assert: Check interface implementation
        Assert.Equal("Topological Backend Adapter (Ising)", adapter.Name)
        Assert.Equal(9, adapter.MaxQubits)  // 10 anyons → 9 qubits
        Assert.Contains("H", adapter.SupportedGates)
        Assert.Contains("CNOT", adapter.SupportedGates)
        Assert.Contains("T", adapter.SupportedGates)
    
    [<Fact>]
    let ``TopologicalBackendAdapter - Executes simple single-qubit circuit`` () =
        // Arrange: Create topological backend and adapter
        let topoBackend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        let backend = createIsingAdapter topoBackend
        
        // Create simple circuit: T gate on qubit 0
        let circuit : CircuitBuilder.Circuit = {
            QubitCount = 1
            Gates = [CircuitBuilder.Gate.T 0]
        }
        
        let icircuit = CircuitWrapper(circuit) :> ICircuit
        
        // Act: Execute circuit
        let result = backend.Execute icircuit 10
        
        // Assert: Check result structure
        match result with
        | Ok execResult ->
            Assert.Equal(10, execResult.NumShots)
            Assert.Equal(10, execResult.Measurements.Length)
            Assert.Equal(1, execResult.Measurements.[0].Length)  // 1 qubit
            Assert.Equal("Topological Backend Adapter", execResult.BackendName)
        | Error err ->
            Assert.True(false, $"Execution failed: {err}")
    
    [<Fact>]
    let ``TopologicalBackendAdapter - Executes multi-qubit circuit with CNOT`` () =
        // Arrange: Create topological backend and adapter
        let topoBackend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        let backend = createIsingAdapter topoBackend
        
        // Create circuit: H-CNOT entangling circuit
        let circuit : CircuitBuilder.Circuit = {
            QubitCount = 2
            Gates = [
                CircuitBuilder.Gate.H 0
                CircuitBuilder.Gate.CNOT (0, 1)
            ]
        }
        
        let icircuit = CircuitWrapper(circuit) :> ICircuit
        
        // Act: Execute circuit
        let result = backend.Execute icircuit 5
        
        // Assert: Check result structure
        match result with
        | Ok execResult ->
            Assert.Equal(5, execResult.NumShots)
            Assert.Equal(5, execResult.Measurements.Length)
            Assert.Equal(2, execResult.Measurements.[0].Length)  // 2 qubits
            
            // Check metadata
            Assert.True(execResult.Metadata.ContainsKey("anyon_type"))
            Assert.True(execResult.Metadata.ContainsKey("approximation_error"))
        | Error err ->
            Assert.True(false, $"Execution failed: {err}")
    
    [<Fact>]
    let ``TopologicalBackendAdapter - Validates qubit count limits`` () =
        // Arrange: Create topological backend with 5 max anyons (4 qubits)
        let topoBackend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 5
        let backend = createIsingAdapter topoBackend
        
        // Create circuit with 5 qubits (requires 6 anyons - exceeds limit)
        let circuit : CircuitBuilder.Circuit = { QubitCount = 5; Gates = [] }
        let icircuit = CircuitWrapper(circuit) :> ICircuit
        
        // Act: Try to execute
        let result = backend.Execute icircuit 1
        
        // Assert: Should fail with validation error
        match result with
        | Ok _ ->
            Assert.True(false, "Should have failed due to qubit limit")
        | Error err ->
            Assert.True(err.IsBackendError, "Should be a backend error")
    
    [<Fact>]
    let ``TopologicalBackendAdapter - Fibonacci adapter uses correct anyon type`` () =
        // Arrange: Create Fibonacci topological backend
        let topoBackend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Fibonacci 10
        
        // Act: Create Fibonacci adapter
        let adapter = createFibonacciAdapter topoBackend
        
        // Assert: Check name includes Fibonacci
        Assert.Contains("Fibonacci", adapter.Name)
        Assert.Equal(9, adapter.MaxQubits)
    
    [<Fact>]
    let ``TopologicalBackendAdapter - Cancellation token support`` () =
        // Arrange: Create backend with adapter
        let topoBackend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
        let backend = createIsingAdapter topoBackend
        
        // Create cancellation token and cancel it immediately
        use cts = new System.Threading.CancellationTokenSource()
        cts.Cancel()
        
        backend.SetCancellationToken(Some cts.Token)
        
        // Create simple circuit
        let circuit : CircuitBuilder.Circuit = { QubitCount = 1; Gates = [CircuitBuilder.Gate.T 0] }
        let icircuit = CircuitWrapper(circuit) :> ICircuit
        
        // Act: Try to execute with cancelled token
        let result = backend.Execute icircuit 1
        
        // Assert: Should fail with operation cancelled
        match result with
        | Ok _ ->
            Assert.True(false, "Should have been cancelled")
        | Error err ->
            Assert.True(err.IsOperationError, "Should be an operation error")
