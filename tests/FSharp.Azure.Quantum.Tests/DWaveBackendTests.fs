namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Backends.DWaveBackend
open FSharp.Azure.Quantum.Backends.DWaveTypes
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Unit tests for D-Wave Backend (MockDWaveBackend)
///
/// Tests cover:
/// - QAOA circuit execution via Execute
/// - Error handling (non-QAOA circuits, qubit limits)
/// - Deterministic results with seed
///
/// Note: MockDWaveBackend does NOT implement IQuantumBackend interface.
/// D-Wave annealing is fundamentally different from gate-based quantum computing.
/// Use Execute method directly.
module DWaveBackendTests =
    
    // ============================================================================
    // HELPER FUNCTIONS FOR TEST DATA
    // ============================================================================
    
    /// Create a simple QAOA circuit for testing
    let createSimpleQaoaCircuit () : ICircuit =
        let hamiltonian : ProblemHamiltonian = {
            NumQubits = 2
            Terms = [|
                { Coefficient = -1.0; QubitsIndices = [| 0 |]; PauliOperators = [| PauliZ |] }
                { Coefficient = 0.5; QubitsIndices = [| 0; 1 |]; PauliOperators = [| PauliZ; PauliZ |] }
            |]
        }
        let mixerHam = MixerHamiltonian.create 2
        let parameters = [| (0.5, 0.3) |]
        let qaoaCircuit = QaoaCircuit.build hamiltonian mixerHam parameters
        QaoaCircuitWrapper(qaoaCircuit) :> ICircuit
    
    /// Create MaxCut QAOA circuit
    let createMaxCutCircuit () : ICircuit =
        let hamiltonian : ProblemHamiltonian = {
            NumQubits = 3
            Terms = [|
                { Coefficient = -2.5; QubitsIndices = [| 0 |]; PauliOperators = [| PauliZ |] }
                { Coefficient = -4.0; QubitsIndices = [| 1 |]; PauliOperators = [| PauliZ |] }
                { Coefficient = -1.5; QubitsIndices = [| 2 |]; PauliOperators = [| PauliZ |] }
                { Coefficient = 1.25; QubitsIndices = [| 0; 1 |]; PauliOperators = [| PauliZ; PauliZ |] }
                { Coefficient = 0.75; QubitsIndices = [| 1; 2 |]; PauliOperators = [| PauliZ; PauliZ |] }
            |]
        }
        let mixerHam = MixerHamiltonian.create 3
        let qaoaCircuit = QaoaCircuit.build hamiltonian mixerHam [| (0.5, 0.3) |]
        QaoaCircuitWrapper(qaoaCircuit) :> ICircuit
    
    // ============================================================================
    // NOTE: Interface compliance tests removed
    // ============================================================================
    //
    // MockDWaveBackend does NOT implement IQuantumBackend interface.
    // D-Wave annealing backends work with QUBO/Ising problems, not quantum states.
    // Tests now use Execute method directly instead of IQuantumBackend interface.
    
    //==============================================================================
    // QAOA CIRCUIT EXECUTION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Execute succeeds for simple QAOA circuit`` () =
        let backend = createDefaultMockBackend ()
        let circuit = createSimpleQaoaCircuit ()
        
        let result = backend.Execute circuit 100
        
        match result with
        | Ok execResult ->
            Assert.Equal(100, execResult.NumShots)
            Assert.Equal(100, execResult.Measurements.Length)
            // All measurements should be 2-qubit bitstrings
            for bitstring in execResult.Measurements do
                Assert.Equal(2, bitstring.Length)
                Assert.All(bitstring, fun bit -> Assert.True(bit = 0 || bit = 1))
        | Error e -> Assert.True(false, $"Should not fail: {e}")
    
    [<Fact>]
    let ``Execute with seed produces deterministic results`` () =
        let backend1 = createMockDWaveBackend Advantage_System6_1 (Some 42)
        let backend2 = createMockDWaveBackend Advantage_System6_1 (Some 42)
        let circuit = createSimpleQaoaCircuit ()
        
        let result1 = backend1.Execute circuit 100
        let result2 = backend2.Execute circuit 100
        
        match result1, result2 with
        | Ok exec1, Ok exec2 ->
            // Results should be identical with same seed
            Assert.Equal(exec1.NumShots, exec2.NumShots)
            Assert.Equal(exec1.Measurements.Length, exec2.Measurements.Length)
            // Compare measurements element by element
            for i in 0 .. exec1.Measurements.Length - 1 do
                Assert.Equal<int[]>(exec1.Measurements.[i], exec2.Measurements.[i])
        | _ -> Assert.True(false, "Both executions should succeed")
    
    [<Fact>]
    let ``Execute without seed produces different results`` () =
        let backend1 = createMockDWaveBackend Advantage_System6_1 None
        let backend2 = createMockDWaveBackend Advantage_System6_1 None
        let circuit = createSimpleQaoaCircuit ()
        
        let result1 = backend1.Execute circuit 100
        let result2 = backend2.Execute circuit 100
        
        match result1, result2 with
        | Ok exec1, Ok exec2 ->
            // Results should differ (with very high probability)
            // Check if at least one measurement differs
            let allSame = 
                exec1.Measurements 
                |> Array.zip exec2.Measurements
                |> Array.forall (fun (m1, m2) -> m1 = m2)
            Assert.False(allSame, "Results should differ without seed")
        | _ -> Assert.True(false, "Both executions should succeed")
    
    [<Fact>]
    let ``Execute handles MaxCut problem`` () =
        let backend = createDefaultMockBackend ()
        let circuit = createMaxCutCircuit ()
        
        match backend.Execute circuit 200 with
        | Ok execResult ->
            Assert.Equal(200, execResult.NumShots)
            // All measurements should be 3-qubit bitstrings
            for bitstring in execResult.Measurements do
                Assert.Equal(3, bitstring.Length)
        | Error e -> Assert.True(false, $"MaxCut execution failed: {e}")
    
    [<Fact>]
    let ``Execute produces multiple distinct solutions`` () =
        let backend = createDefaultMockBackend ()
        let circuit = createSimpleQaoaCircuit ()
        
        match backend.Execute circuit 1000 with
        | Ok execResult ->
            // Count distinct bitstrings
            let distinctSolutions = 
                execResult.Measurements 
                |> Array.distinct 
                |> Array.length
            // Should have multiple distinct bitstrings (not just one)
            Assert.True(distinctSolutions > 1, $"Should produce multiple solutions, got {distinctSolutions}")
        | Error e -> Assert.True(false, $"Execution failed: {e}")
    
    // ============================================================================
    // ERROR HANDLING TESTS
    // ============================================================================
    
    [<Fact>]
    let ``Execute rejects non-QAOA circuit`` () =
        let backend = createDefaultMockBackend ()
        // Create a raw ICircuit that's not a QaoaCircuitWrapper
        let nonQaoaCircuit = 
            { new ICircuit with
                member _.NumQubits = 2
                member _.Description = "NonQAOA"
            }
        
        let result = backend.Execute nonQaoaCircuit 100
        
        match result with
        | Error err -> 
            // Error should be a QuantumError.ValidationError
            Assert.True(true)  // Successfully rejected
        | Ok _ -> Assert.True(false, "Should have failed")
    
    [<Fact>]
    let ``Execute rejects circuit exceeding qubit limit`` () =
        // Create small solver with limited qubits
        let smallBackend = createMockDWaveBackend Advantage_System1_1 None  // 5000 qubits
        
        // Create huge Hamiltonian (6000 qubits)
        let terms = [|
            for i in 0 .. 5999 do
                yield { Coefficient = -1.0; QubitsIndices = [| i |]; PauliOperators = [| PauliZ |] }
        |]
        let hugeHamiltonian : ProblemHamiltonian = { NumQubits = 6000; Terms = terms }
        let mixerHam = MixerHamiltonian.create 6000
        let circuit = QaoaCircuit.build hugeHamiltonian mixerHam [| (0.5, 0.3) |]
        let wrapper = QaoaCircuitWrapper(circuit) :> ICircuit
        
        let result = smallBackend.Execute wrapper 10
        
        match result with
        | Error _ ->
            // Successfully rejected - error is expected
            Assert.True(true)
        | Ok _ -> Assert.True(false, "Should have failed")
    
    [<Fact>]
    let ``Execute rejects zero shots`` () =
        let backend = createDefaultMockBackend ()
        let circuit = createSimpleQaoaCircuit ()
        
        let result = backend.Execute circuit 0
        
        match result with
        | Error _ -> Assert.True(true)  // Error expected for zero shots
        | Ok _ -> Assert.True(false, "Should reject zero shots")
    
    // ============================================================================
    // NOTE: Async execution tests removed
    // ============================================================================
    //
    // MockDWaveBackend only provides Execute (synchronous).
    // No ExecuteAsync available - backend doesn't implement IQuantumBackend interface.
    
    // ============================================================================
    // SOLVER CONFIGURATION TESTS
    // ============================================================================
    
    // NOTE: Removed test "Different solver types have different qubit counts"
    // MockDWaveBackend no longer exposes MaxQubits property (not part of interface)
    // Qubit limits are validated internally during Execute()
    
    [<Fact>]
    let ``All solver types can execute QAOA circuits`` () =
        let solvers = [
            Advantage_System1_1
            Advantage_System4_1
            Advantage_System6_1
            Advantage2_Prototype
        ]
        
        let circuit = createSimpleQaoaCircuit ()
        
        for solver in solvers do
            let backend = createMockDWaveBackend solver (Some 42)
            let result = backend.Execute circuit 10
            match result with
            | Ok _ -> Assert.True(true)
            | Error e -> Assert.True(false, $"Solver {solver} failed: {e}")
