namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QuantumBackend
open FSharp.Azure.Quantum.Core.QaoaCircuit

module QuantumBackendTests =
    
    /// Helper to create a simple 2-qubit MaxCut QAOA circuit
    let createSimpleQaoaCircuit () : QaoaCircuit =
        let numQubits = 2
        let gamma = Math.PI / 4.0
        let beta = Math.PI / 3.0
        
        // Simple QUBO for MaxCut edge (0,1): x0*x1
        let quboMatrix = array2D [
            [0.0; 0.5]
            [0.5; 0.0]
        ]
        
        let problemHamiltonian = ProblemHamiltonian.fromQubo quboMatrix
        let mixerHamiltonian = MixerHamiltonian.create numQubits
        
        // QAOA layer
        let layer = {
            CostGates = [| RZZ(0, 1, 2.0 * gamma * 0.25) |]  // Coefficient from QUBO conversion
            MixerGates = [| RX(0, 2.0 * beta); RX(1, 2.0 * beta) |]
            Gamma = gamma
            Beta = beta
        }
        
        {
            NumQubits = numQubits
            InitialStateGates = [| H(0); H(1) |]
            Layers = [| layer |]
            ProblemHamiltonian = problemHamiltonian
            MixerHamiltonian = mixerHamiltonian
        }
    
    [<Fact>]
    let ``Local backend executes 2-qubit QAOA circuit`` () =
        let circuit = createSimpleQaoaCircuit ()
        let shots = 100
        
        match Local.simulate circuit shots with
        | Ok result ->
            Assert.Equal("Local", result.Backend)
            Assert.Equal(shots, result.Shots)
            Assert.True(result.ExecutionTimeMs > 0.0, "Execution time should be positive")
            Assert.True(result.JobId.IsNone, "Local backend should not have JobId")
            
            // Should have 4 possible outcomes for 2 qubits: 00, 01, 10, 11
            Assert.True(result.Counts.Count > 0, "Should have at least one measurement outcome")
            Assert.True(result.Counts.Count <= 4, "Should have at most 4 outcomes for 2 qubits")
            
            // Verify bitstring format (2 qubits = 2 characters)
            for kvp in result.Counts do
                Assert.Equal(2, kvp.Key.Length)
                Assert.Matches("^[01]+$", kvp.Key)
            
            // Verify total counts equals shots
            let totalCounts = result.Counts |> Map.toSeq |> Seq.sumBy snd
            Assert.Equal(shots, totalCounts)
        | Error msg ->
            Assert.Fail($"Local simulation failed: {msg}")
    
    [<Fact>]
    let ``Local backend rejects circuits with more than 10 qubits`` () =
        let largeCircuit = createSimpleQaoaCircuit ()
        let largeCircuit' = { largeCircuit with NumQubits = 11 }
        
        match Local.simulate largeCircuit' 100 with
        | Ok _ -> Assert.Fail("Should have rejected 11-qubit circuit")
        | Error msg -> 
            Assert.Contains("10 qubits", msg)
            Assert.Contains("11", msg)
    
    [<Fact>]
    let ``Local backend rejects negative shots`` () =
        let circuit = createSimpleQaoaCircuit ()
        
        match Local.simulate circuit -1 with
        | Ok _ -> Assert.Fail("Should have rejected negative shots")
        | Error msg -> Assert.Contains("positive", msg)
    
    [<Fact>]
    let ``Local backend rejects zero shots`` () =
        let circuit = createSimpleQaoaCircuit ()
        
        match Local.simulate circuit 0 with
        | Ok _ -> Assert.Fail("Should have rejected zero shots")
        | Error msg -> Assert.Contains("positive", msg)
    
    [<Fact>]
    let ``Azure backend returns not implemented error`` () =
        let circuit = createSimpleQaoaCircuit ()
        
        match Azure.execute circuit 100 () with
        | Ok _ -> Assert.Fail("Azure backend should not be implemented yet")
        | Error msg -> Assert.Contains("not yet implemented", msg)
    
    [<Fact>]
    let ``Execute with Local backend type`` () =
        let circuit = createSimpleQaoaCircuit ()
        
        match execute Local circuit 100 with
        | Ok result -> 
            Assert.Equal("Local", result.Backend)
            Assert.Equal(100, result.Shots)
        | Error msg -> 
            Assert.Fail($"Execution failed: {msg}")
    
    [<Fact>]
    let ``Execute with Azure backend type returns error`` () =
        let circuit = createSimpleQaoaCircuit ()
        
        match execute (Azure ()) circuit 100 with
        | Ok _ -> Assert.Fail("Azure should not be implemented")
        | Error msg -> Assert.Contains("not yet implemented", msg)
    
    [<Fact>]
    let ``AutoExecute uses local for small circuits`` () =
        let circuit = createSimpleQaoaCircuit ()  // 2 qubits
        
        match autoExecute circuit 100 with
        | Ok result -> 
            Assert.Equal("Local", result.Backend)
        | Error msg -> 
            Assert.Fail($"AutoExecute failed: {msg}")
    
    [<Fact>]
    let ``AutoExecute rejects large circuits when Azure unavailable`` () =
        let circuit = createSimpleQaoaCircuit ()
        let largeCircuit = { circuit with NumQubits = 15 }
        
        match autoExecute largeCircuit 100 with
        | Ok _ -> Assert.Fail("Should reject circuits >10 qubits")
        | Error msg -> 
            Assert.Contains("15 qubits", msg)
            Assert.Contains("10 qubit local limit", msg)
    
    [<Fact>]
    let ``Interface-based backend - Local`` () =
        let backend = LocalBackend() :> IBackend
        let circuit = createSimpleQaoaCircuit ()
        
        match backend.Execute circuit 100 with
        | Ok result -> 
            Assert.Equal("Local", result.Backend)
        | Error msg -> 
            Assert.Fail($"Execution failed: {msg}")
    
    [<Fact>]
    let ``Interface-based backend - Azure placeholder`` () =
        let backend = AzureBackend(()) :> IBackend
        let circuit = createSimpleQaoaCircuit ()
        
        match backend.Execute circuit 100 with
        | Ok _ -> Assert.Fail("Azure backend should not be implemented")
        | Error msg -> Assert.Contains("not yet implemented", msg)
    
    [<Fact>]
    let ``Local backend produces consistent measurement statistics`` () =
        let circuit = createSimpleQaoaCircuit ()
        let shots = 1000
        
        // Run twice
        let result1 = Local.simulate circuit shots
        let result2 = Local.simulate circuit shots
        
        match result1, result2 with
        | Ok r1, Ok r2 ->
            // Both should have same shot count
            Assert.Equal(shots, r1.Shots)
            Assert.Equal(shots, r2.Shots)
            
            // Both should have same possible outcomes (though different counts)
            Assert.True(Set.ofSeq r1.Counts.Keys = Set.ofSeq r2.Counts.Keys || 
                       Set.intersect (Set.ofSeq r1.Counts.Keys) (Set.ofSeq r2.Counts.Keys) 
                       |> Set.count > 0,
                       "Results should have overlapping measurement outcomes")
        | _ -> 
            Assert.Fail("Both simulations should succeed")
    
    [<Fact>]
    let ``Local backend handles depth-2 QAOA circuit`` () =
        let baseCircuit = createSimpleQaoaCircuit ()
        
        // Create depth-2 circuit (2 layers)
        let layer2 = {
            CostGates = [| RZZ(0, 1, 2.0 * 0.4 * (-0.5)) |]
            MixerGates = [| RX(0, 2.0 * 0.5); RX(1, 2.0 * 0.5) |]
            Gamma = 0.4
            Beta = 0.5
        }
        
        let depth2Circuit = { 
            baseCircuit with 
                Layers = Array.append baseCircuit.Layers [| layer2 |]
        }
        
        match Local.simulate depth2Circuit 100 with
        | Ok result ->
            Assert.Equal(100, result.Shots)
            // Should have valid 2-character bitstrings
            let validBitstrings = 
                result.Counts 
                |> Map.toSeq 
                |> Seq.filter (fun (bs, _) -> bs.Length = 2)
                |> Seq.length
            Assert.True(validBitstrings > 0, "Should have valid bitstrings")
        | Error msg ->
            Assert.Fail($"Depth-2 circuit failed: {msg}")
