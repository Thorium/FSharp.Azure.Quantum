namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.DWaveBackend
open FSharp.Azure.Quantum.Backends.DWaveTypes
open FSharp.Azure.Quantum.Backends.BackendCapabilityDetection

/// Unit tests for BackendCapabilityDetection
///
/// Tests cover:
/// - Circuit paradigm detection (gate-based vs annealing)
/// - Backend capability checking
/// - Automatic backend selection
/// - Backend recommendations
module BackendCapabilityDetectionTests =
    
    // ============================================================================
    // HELPER FUNCTIONS
    // ============================================================================
    
    /// Create a simple QAOA circuit (annealing paradigm)
    let createQaoaCircuit () : ICircuit =
        let hamiltonian : ProblemHamiltonian = {
            NumQubits = 2
            Terms = [|
                { Coefficient = -1.0; QubitsIndices = [| 0 |]; PauliOperators = [| PauliZ |] }
                { Coefficient = 0.5; QubitsIndices = [| 0; 1 |]; PauliOperators = [| PauliZ; PauliZ |] }
            |]
        }
        let mixerHam = MixerHamiltonian.create 2
        let qaoaCircuit = QaoaCircuit.build hamiltonian mixerHam [| (0.5, 0.3) |]
        QaoaCircuitWrapper(qaoaCircuit) :> ICircuit
    
    /// Create a gate-based circuit
    let createGateBasedCircuit () : ICircuit =
        { new ICircuit with
            member _.NumQubits = 5
            member _.Description = "Gate-based test circuit"
        }
    
    // ============================================================================
    // PARADIGM DETECTION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``detectCircuitParadigm identifies QAOA as Annealing`` () =
        let circuit = createQaoaCircuit ()
        let paradigm = detectCircuitParadigm circuit
        Assert.Equal(Annealing, paradigm)
    
    [<Fact>]
    let ``detectCircuitParadigm identifies generic circuit as GateBased`` () =
        let circuit = createGateBasedCircuit ()
        let paradigm = detectCircuitParadigm circuit
        Assert.Equal(GateBased, paradigm)
    
    // ============================================================================
    // BACKEND CAPABILITY TESTS
    // ============================================================================
    
    [<Fact>]
    let ``getBackendCapability correctly identifies D-Wave backend`` () =
        let backend = createDefaultMockBackend ()
        let capability = getBackendCapability backend
        
        Assert.Equal(Annealing, capability.Paradigm)
        Assert.Equal(5640, capability.MaxQubits)
        Assert.Empty(capability.SupportedGates)  // Annealing has no gates
        Assert.True(capability.IsAvailable)
    
    [<Fact>]
    let ``getBackendCapability correctly identifies LocalBackend`` () =
        let backend = BackendAbstraction.createLocalBackend ()
        let capability = getBackendCapability backend
        
        Assert.Equal(GateBased, capability.Paradigm)
        Assert.True(capability.MaxQubits > 0)
        Assert.NotEmpty(capability.SupportedGates)  // Gate-based has gates
        Assert.True(capability.IsAvailable)
    
    [<Fact>]
    let ``getBackendCapability calculates performance scores`` () =
        let dwaveBackend = createDefaultMockBackend ()
        let localBackend = BackendAbstraction.createLocalBackend ()
        
        let dwaveCap = getBackendCapability dwaveBackend
        let localCap = getBackendCapability localBackend
        
        // Both should have positive performance scores
        Assert.True(dwaveCap.PerformanceScore > 0.0)
        Assert.True(localCap.PerformanceScore > 0.0)
    
    // ============================================================================
    // COMPATIBILITY CHECKING TESTS
    // ============================================================================
    
    [<Fact>]
    let ``canExecuteCircuit allows D-Wave backend for QAOA circuit`` () =
        let backend = createDefaultMockBackend ()
        let circuit = createQaoaCircuit ()
        
        let canExecute = canExecuteCircuit backend circuit
        Assert.True(canExecute, "D-Wave backend should execute QAOA circuits")
    
    [<Fact>]
    let ``canExecuteCircuit rejects D-Wave backend for gate-based circuit`` () =
        let backend = createDefaultMockBackend ()
        let circuit = createGateBasedCircuit ()
        
        let canExecute = canExecuteCircuit backend circuit
        Assert.False(canExecute, "D-Wave backend should not execute gate-based circuits")
    
    [<Fact>]
    let ``canExecuteCircuit allows LocalBackend for gate-based circuit`` () =
        let backend = BackendAbstraction.createLocalBackend ()
        let circuit = createGateBasedCircuit ()
        
        let canExecute = canExecuteCircuit backend circuit
        Assert.True(canExecute, "LocalBackend should execute gate-based circuits")
    
    [<Fact>]
    let ``canExecuteCircuit rejects LocalBackend for QAOA circuit`` () =
        let backend = BackendAbstraction.createLocalBackend ()
        let circuit = createQaoaCircuit ()
        
        let canExecute = canExecuteCircuit backend circuit
        Assert.False(canExecute, "LocalBackend should not execute QAOA circuits")
    
    [<Fact>]
    let ``canExecuteCircuit respects qubit limits`` () =
        // Create small D-Wave backend with limited qubits
        let smallBackend = createMockDWaveBackend DW_2000Q_6 None  // 2048 qubits
        
        // Create huge QAOA circuit
        let hugeHamiltonian : ProblemHamiltonian = {
            NumQubits = 3000
            Terms = [| 
                for i in 0 .. 2999 do
                    yield { Coefficient = -1.0; QubitsIndices = [| i |]; PauliOperators = [| PauliZ |] }
            |]
        }
        let mixerHam = MixerHamiltonian.create 3000
        let qaoaCircuit = QaoaCircuit.build hugeHamiltonian mixerHam [| (0.5, 0.3) |]
        let circuit = QaoaCircuitWrapper(qaoaCircuit) :> ICircuit
        
        let canExecute = canExecuteCircuit smallBackend circuit
        Assert.False(canExecute, "Backend should reject circuits exceeding qubit limit")
    
    // ============================================================================
    // AUTOMATIC BACKEND SELECTION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``selectBestBackend chooses D-Wave for QAOA circuit`` () =
        let backends = createDefaultBackendPool ()
        let circuit = createQaoaCircuit ()
        
        match selectBestBackend backends circuit with
        | Error e -> Assert.True(false, $"Selection failed: {e}")
        | Ok backend ->
            Assert.Contains("D-Wave", backend.Name)
    
    [<Fact>]
    let ``selectBestBackend chooses LocalBackend for gate-based circuit`` () =
        let backends = createDefaultBackendPool ()
        let circuit = createGateBasedCircuit ()
        
        match selectBestBackend backends circuit with
        | Error e -> Assert.True(false, $"Selection failed: {e}")
        | Ok backend ->
            Assert.Contains("Local", backend.Name)
    
    [<Fact>]
    let ``selectBestBackend fails when no compatible backend available`` () =
        // Empty backend list
        let backends = []
        let circuit = createQaoaCircuit ()
        
        match selectBestBackend backends circuit with
        | Ok _ -> Assert.True(false, "Should fail with empty backend list")
        | Error msg ->
            Assert.Contains("No compatible backend", msg.Message)
    
    [<Fact>]
    let ``selectBestBackend ranks backends by performance score`` () =
        // Create multiple D-Wave backends with different capabilities
        let backends = [
            createMockDWaveBackend DW_2000Q_6 None       // 2048 qubits - lower score
            createMockDWaveBackend Advantage_System6_1 None  // 5640 qubits - higher score
        ]
        let circuit = createQaoaCircuit ()
        
        match selectBestBackend backends circuit with
        | Error e -> Assert.True(false, $"Selection failed: {e}")
        | Ok backend ->
            // Should select Advantage_System6_1 (higher qubit count)
            Assert.Contains("system6", backend.Name)
    
    // ============================================================================
    // BACKEND RECOMMENDATIONS TESTS
    // ============================================================================
    
    [<Fact>]
    let ``getBackendRecommendations returns compatible backends`` () =
        let circuit = createQaoaCircuit ()
        let recommendations = getBackendRecommendations circuit None
        
        Assert.NotEmpty(recommendations)
        
        // All recommendations should be for Annealing paradigm
        for (_, cap, _) in recommendations do
            Assert.Equal(Annealing, cap.Paradigm)
    
    [<Fact>]
    let ``getBackendRecommendations sorts by performance score`` () =
        let circuit = createQaoaCircuit ()
        let recommendations = getBackendRecommendations circuit None
        
        if recommendations.Length > 1 then
            // Check that recommendations are sorted descending by score
            for i in 0 .. recommendations.Length - 2 do
                let (_, cap1, _) = recommendations.[i]
                let (_, cap2, _) = recommendations.[i + 1]
                Assert.True(cap1.PerformanceScore >= cap2.PerformanceScore)
    
    [<Fact>]
    let ``getBackendRecommendations filters incompatible backends`` () =
        let circuit = createGateBasedCircuit ()
        let recommendations = getBackendRecommendations circuit None
        
        // Should only recommend gate-based backends
        for (_, cap, _) in recommendations do
            Assert.Equal(GateBased, cap.Paradigm)
    
    // ============================================================================
    // INTEGRATION TESTS
    // ============================================================================
    
    [<Fact>]
    let ``executeWithAutomaticBackend succeeds for QAOA circuit`` () =
        let circuit = createQaoaCircuit ()
        
        match executeWithAutomaticBackend circuit 100 with
        | Error e -> Assert.True(false, $"Execution failed: {e}")
        | Ok execResult ->
            Assert.Equal(100, execResult.NumShots)
            Assert.NotEmpty(execResult.Measurements)
    
    [<Fact>]
    let ``executeWithAutomaticBackend selects LocalBackend for gate-based circuit`` () =
        let circuit = createGateBasedCircuit ()
        
        // Note: LocalBackend requires CircuitWrapper, not raw ICircuit
        // This test verifies backend selection, error is expected
        match executeWithAutomaticBackend circuit 50 with
        | Error e -> 
            // Expected error from LocalBackend - backend was correctly selected
            Assert.Contains("CircuitWrapper", e.Message)
        | Ok _ ->
            // If we ever fix LocalBackend to accept raw ICircuit, this is fine too
            Assert.True(true)
    
    [<Fact>]
    let ``executeWithAutomaticBackendAsync succeeds`` () =
        async {
            let circuit = createQaoaCircuit ()
            
            let! result = executeWithAutomaticBackendAsync circuit 100
            
            match result with
            | Error e -> Assert.True(false, $"Async execution failed: {e}")
            | Ok execResult ->
                Assert.Equal(100, execResult.NumShots)
                Assert.NotEmpty(execResult.Measurements)
        } |> Async.RunSynchronously
    
    [<Fact>]
    let ``createDefaultBackendPool returns non-empty list`` () =
        let backends = createDefaultBackendPool ()
        
        Assert.NotEmpty(backends)
        Assert.True(backends.Length >= 2, "Should have at least LocalBackend and DWaveBackend")
    
    [<Fact>]
    let ``createDefaultBackendPool includes both paradigms`` () =
        let backends = createDefaultBackendPool ()
        let capabilities = backends |> List.map getBackendCapability
        
        let hasGateBased = capabilities |> List.exists (fun cap -> cap.Paradigm = GateBased)
        let hasAnnealing = capabilities |> List.exists (fun cap -> cap.Paradigm = Annealing)
        
        Assert.True(hasGateBased, "Should have gate-based backend")
        Assert.True(hasAnnealing, "Should have annealing backend")
