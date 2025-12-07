namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Topological

module MagicStateDistillationTests =
    
    // ========================================================================
    // MAGIC STATE PREPARATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Should prepare noisy magic state with valid error rate`` () =
        let errorRate = 0.01  // 1% error
        
        match MagicStateDistillation.prepareNoisyMagicState errorRate AnyonSpecies.AnyonType.Ising with
        | Error err -> failwith $"Failed to prepare magic state: {err.Message}"
        | Ok state ->
            Assert.Equal(0.99, state.Fidelity, 6)
            Assert.Equal(0.01, state.ErrorRate, 6)
            Assert.Equal(AnyonSpecies.AnyonType.Ising, state.QubitState.AnyonType)
    
    [<Fact>]
    let ``Should reject negative error rate`` () =
        match MagicStateDistillation.prepareNoisyMagicState -0.1 AnyonSpecies.AnyonType.Ising with
        | Ok _ -> failwith "Should have rejected negative error rate"
        | Error err -> Assert.Contains("Error rate must be in [0, 1]", err.Message)
    
    [<Fact>]
    let ``Should reject error rate greater than 1`` () =
        match MagicStateDistillation.prepareNoisyMagicState 1.5 AnyonSpecies.AnyonType.Ising with
        | Ok _ -> failwith "Should have rejected error rate > 1"
        | Error err -> Assert.Contains("Error rate must be in [0, 1]", err.Message)
    
    [<Fact>]
    let ``Should reject non-Ising anyons for magic states`` () =
        match MagicStateDistillation.prepareNoisyMagicState 0.01 AnyonSpecies.AnyonType.Fibonacci with
        | Ok _ -> failwith "Should have rejected Fibonacci anyons"
        | Error err -> Assert.Contains("only applicable to Ising anyons", err.Message)
    
    [<Fact>]
    let ``Perfect magic state should have zero error`` () =
        match MagicStateDistillation.prepareNoisyMagicState 0.0 AnyonSpecies.AnyonType.Ising with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok state ->
            Assert.Equal(1.0, state.Fidelity)
            Assert.Equal(0.0, state.ErrorRate)
    
    // ========================================================================
    // FIDELITY CALCULATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Should calculate distilled fidelity for small error regime`` () =
        let inputFidelity = 0.99  // 1% error
        let outputFidelity = MagicStateDistillation.calculateDistilledFidelity inputFidelity
        
        // Expected: p_out = 35 * (0.01)^3 = 35 * 0.000001 = 0.000035
        // Output fidelity = 1 - 0.000035 = 0.999965
        Assert.True(outputFidelity > 0.9999)
        Assert.True(outputFidelity > inputFidelity)  // Should improve
    
    [<Fact>]
    let ``Should show cubic error suppression`` () =
        let fidelity90 = MagicStateDistillation.calculateDistilledFidelity 0.90  // 10% error
        let fidelity99 = MagicStateDistillation.calculateDistilledFidelity 0.99  // 1% error
        
        // 99% should give much better output than 90%
        Assert.True(fidelity99 > fidelity90)
        
        // Cubic suppression: 10x lower input error → ~1000x lower output error
        let errorRatio = (1.0 - fidelity99) / (1.0 - fidelity90)
        Assert.True(errorRatio < 0.01)  // Should be << 1%
    
    [<Fact>]
    let ``Perfect input should give perfect output`` () =
        let outputFidelity = MagicStateDistillation.calculateDistilledFidelity 1.0
        Assert.Equal(1.0, outputFidelity, 10)
    
    // ========================================================================
    // 15-TO-1 DISTILLATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Should perform 15-to-1 distillation`` () =
        // Prepare 15 noisy states
        let inputStates =
            [1..15]
            |> List.map (fun _ ->
                match MagicStateDistillation.prepareNoisyMagicState 0.01 AnyonSpecies.AnyonType.Ising with
                | Ok s -> s
                | Error _ -> failwith "Failed to prepare state"
            )
        
        match MagicStateDistillation.distill15to1 (System.Random()) inputStates with
        | Error err -> failwith $"Distillation failed: {err.Message}"
        | Ok result ->
            Assert.Equal(15, result.InputStatesConsumed)
            Assert.True(result.PurifiedState.Fidelity > 0.99)
            Assert.Equal(14, result.Syndromes.Length)  // 14 syndrome bits
    
    [<Fact>]
    let ``Should reject wrong number of input states`` () =
        // Try with 10 states (should be 15)
        let inputStates =
            [1..10]
            |> List.map (fun _ ->
                match MagicStateDistillation.prepareNoisyMagicState 0.01 AnyonSpecies.AnyonType.Ising with
                | Ok s -> s
                | Error _ -> failwith "Failed to prepare state"
            )
        
        match MagicStateDistillation.distill15to1 (System.Random()) inputStates with
        | Ok _ -> failwith "Should have rejected 10 states"
        | Error err -> Assert.Contains("requires exactly 15 input states", err.Message)
    
    [<Fact>]
    let ``Should improve fidelity through distillation`` () =
        let inputFidelity = 0.95  // 5% error
        
        let inputStates =
            [1..15]
            |> List.map (fun _ ->
                match MagicStateDistillation.prepareNoisyMagicState (1.0 - inputFidelity) AnyonSpecies.AnyonType.Ising with
                | Ok s -> s
                | Error _ -> failwith "Failed to prepare state"
            )
        
        match MagicStateDistillation.distill15to1 (System.Random()) inputStates with
        | Error err -> failwith $"Distillation failed: {err.Message}"
        | Ok result ->
            // Output should be better than input
            Assert.True(result.PurifiedState.Fidelity > inputFidelity)
            
            // Acceptance probability should be reasonable
            Assert.True(result.AcceptanceProbability >= 0.0)
            Assert.True(result.AcceptanceProbability <= 1.0)
    
    [<Fact>]
    let ``Should handle mixed fidelity inputs`` () =
        // Create states with varying fidelities
        let inputStates =
            [1..15]
            |> List.mapi (fun i _ ->
                let errorRate = 0.01 + (float i * 0.001)  // 1% to 2.4%
                match MagicStateDistillation.prepareNoisyMagicState errorRate AnyonSpecies.AnyonType.Ising with
                | Ok s -> s
                | Error _ -> failwith "Failed to prepare state"
            )
        
        match MagicStateDistillation.distill15to1 (System.Random()) inputStates with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok result ->
            Assert.Equal(15, result.InputStatesConsumed)
            Assert.True(result.PurifiedState.Fidelity > 0.0)
    
    // ========================================================================
    // ITERATIVE DISTILLATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Should perform 1 round of iterative distillation`` () =
        // Need 15 states for 1 round
        let inputStates =
            [1..15]
            |> List.map (fun _ ->
                match MagicStateDistillation.prepareNoisyMagicState 0.05 AnyonSpecies.AnyonType.Ising with
                | Ok s -> s
                | Error _ -> failwith "Failed to prepare state"
            )
        
        match MagicStateDistillation.distillIterative (System.Random()) 1 inputStates with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok finalState ->
            Assert.True(finalState.Fidelity > 0.95)
    
    [<Fact>]
    let ``Should reject insufficient states for iterative distillation`` () =
        // Need 15^2 = 225 states for 2 rounds
        let inputStates =
            [1..100]  // Only 100
            |> List.map (fun _ ->
                match MagicStateDistillation.prepareNoisyMagicState 0.01 AnyonSpecies.AnyonType.Ising with
                | Ok s -> s
                | Error _ -> failwith "Failed to prepare state"
            )
        
        match MagicStateDistillation.distillIterative (System.Random()) 2 inputStates with
        | Ok _ -> failwith "Should have rejected insufficient states"
        | Error err -> Assert.Contains("Need", err.Message)
    
    [<Fact>]
    let ``Should reject too many rounds`` () =
        let inputStates = []  // Doesn't matter, will fail before using them
        
        match MagicStateDistillation.distillIterative (System.Random()) 10 inputStates with
        | Ok _ -> failwith "Should have rejected 10 rounds"
        | Error err -> Assert.Contains("More than 5 rounds is impractical", err.Message)
    
    [<Fact>]
    let ``Should reject zero rounds`` () =
        let inputStates =
            [1..15]
            |> List.map (fun _ ->
                match MagicStateDistillation.prepareNoisyMagicState 0.01 AnyonSpecies.AnyonType.Ising with
                | Ok s -> s
                | Error _ -> failwith "Failed to prepare state"
            )
        
        match MagicStateDistillation.distillIterative (System.Random()) 0 inputStates with
        | Ok _ -> failwith "Should have rejected 0 rounds"
        | Error err -> Assert.Contains("at least 1", err.Message)
    
    // ========================================================================
    // T-GATE SYNTHESIS TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Should apply T-gate with high-fidelity magic state`` () =
        // Create a high-fidelity magic state
        match MagicStateDistillation.prepareNoisyMagicState 0.001 AnyonSpecies.AnyonType.Ising with
        | Error err -> failwith $"Failed to prepare magic state: {err.Message}"
        | Ok magicState ->
            // Create a data qubit
            let sigma = AnyonSpecies.Particle.Sigma
            let vacuum = AnyonSpecies.Particle.Vacuum
            let dataQubit =
                FusionTree.create
                    (FusionTree.fuse
                        (FusionTree.fuse
                            (FusionTree.leaf sigma)
                            (FusionTree.leaf sigma)
                            vacuum)
                        (FusionTree.fuse
                            (FusionTree.leaf sigma)
                            (FusionTree.leaf sigma)
                            vacuum)
                        vacuum)
                    AnyonSpecies.AnyonType.Ising
            
            match MagicStateDistillation.applyTGate (System.Random()) dataQubit magicState with
            | Error err -> failwith $"T-gate failed: {err.Message}"
            | Ok result ->
                Assert.Equal(AnyonSpecies.AnyonType.Ising, result.OutputState.AnyonType)
                Assert.True(result.GateFidelity >= 0.999)
    
    [<Fact>]
    let ``Should reject low-fidelity magic state for T-gate`` () =
        match MagicStateDistillation.prepareNoisyMagicState 0.05 AnyonSpecies.AnyonType.Ising with  // Only 95% fidelity
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok lowFidelityState ->
            let sigma = AnyonSpecies.Particle.Sigma
            let vacuum = AnyonSpecies.Particle.Vacuum
            let dataQubit =
                FusionTree.create
                    (FusionTree.fuse (FusionTree.leaf sigma) (FusionTree.leaf sigma) vacuum)
                    AnyonSpecies.AnyonType.Ising
            
            match MagicStateDistillation.applyTGate (System.Random()) dataQubit lowFidelityState with
            | Ok _ -> failwith "Should have rejected low-fidelity magic state"
            | Error err -> Assert.Contains("fidelity too low", err.Message)
    
    [<Fact>]
    let ``Should reject non-Ising qubit for T-gate`` () =
        // Try with Fibonacci anyon (invalid)
        let tau = AnyonSpecies.Particle.Tau
        let vacuum = AnyonSpecies.Particle.Vacuum
        let fibQubit =
            FusionTree.create
                (FusionTree.fuse (FusionTree.leaf tau) (FusionTree.leaf tau) vacuum)
                AnyonSpecies.AnyonType.Fibonacci
        
        match MagicStateDistillation.prepareNoisyMagicState 0.001 AnyonSpecies.AnyonType.Ising with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok magicState ->
            match MagicStateDistillation.applyTGate (System.Random()) fibQubit magicState with
            | Ok _ -> failwith "Should have rejected Fibonacci qubit"
            | Error err -> Assert.Contains("only applicable to Ising anyons", err.Message)
    
    // ========================================================================
    // RESOURCE ESTIMATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Should estimate resources for target fidelity`` () =
        let estimate = MagicStateDistillation.estimateResources 0.9999 0.95
        
        Assert.True(estimate.DistillationRounds > 0)
        Assert.True(estimate.NoisyStatesRequired >= 15)
        Assert.True(estimate.OutputFidelity >= estimate.TargetFidelity ||
                    estimate.DistillationRounds >= 5)  // Or maxed out
        Assert.Equal(estimate.OverheadFactor, estimate.NoisyStatesRequired)
    
    [<Fact>]
    let ``Should show exponential resource growth`` () =
        // Higher target fidelity requires more rounds
        let estimate99 = MagicStateDistillation.estimateResources 0.99 0.90
        let estimate9999 = MagicStateDistillation.estimateResources 0.9999 0.90
        
        Assert.True(estimate9999.DistillationRounds >= estimate99.DistillationRounds)
        Assert.True(estimate9999.NoisyStatesRequired >= estimate99.NoisyStatesRequired)
    
    [<Fact>]
    let ``Should handle already-sufficient fidelity`` () =
        // Input fidelity already meets target
        let estimate = MagicStateDistillation.estimateResources 0.95 0.99
        
        Assert.Equal(0, estimate.DistillationRounds)
        Assert.Equal(1, estimate.NoisyStatesRequired)
    
    [<Fact>]
    let ``Should cap rounds at 5`` () =
        // Unrealistic target with poor input
        let estimate = MagicStateDistillation.estimateResources 0.999999 0.50
        
        Assert.True(estimate.DistillationRounds <= 5)
    
    // ========================================================================
    // DISPLAY FUNCTION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Should display magic state information`` () =
        match MagicStateDistillation.prepareNoisyMagicState 0.01 AnyonSpecies.AnyonType.Ising with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok state ->
            let display = MagicStateDistillation.displayMagicState state
            Assert.Contains("99.00%", display)
            Assert.Contains("0.010000", display)
    
    [<Fact>]
    let ``Should display distillation result`` () =
        let inputStates =
            [1..15]
            |> List.map (fun _ ->
                match MagicStateDistillation.prepareNoisyMagicState 0.01 AnyonSpecies.AnyonType.Ising with
                | Ok s -> s
                | Error _ -> failwith "Failed"
            )
        
        match MagicStateDistillation.distill15to1 (System.Random()) inputStates with
        | Error err -> failwith $"Failed: {err.Message}"
        | Ok result ->
            let display = MagicStateDistillation.displayDistillationResult result
            Assert.Contains("Acceptance Probability", display)
            Assert.Contains("Input States Consumed: 15", display)
            Assert.Contains("Syndromes:", display)
    
    [<Fact>]
    let ``Should display resource estimate`` () =
        let estimate = MagicStateDistillation.estimateResources 0.9999 0.95
        let display = MagicStateDistillation.displayResourceEstimate estimate
        
        Assert.Contains("99.99%", display)
        Assert.Contains("Distillation Rounds:", display)
        Assert.Contains("Noisy States Required:", display)
        Assert.Contains("Overhead Factor:", display)
    
    // ========================================================================
    // INTEGRATION TEST: UNIVERSAL QUANTUM COMPUTATION
    // ========================================================================
    
    [<Fact>]
    let ``Integration: Complete universal quantum computation with Ising anyons`` () =
        // This test demonstrates the full pipeline for achieving universal quantum
        // computation with Ising anyons (which only support Clifford operations natively).
        //
        // Steps:
        // 1. Prepare noisy magic states
        // 2. Distill to high-fidelity magic states
        // 3. Use magic states to implement T-gates
        // 4. Combine with native Clifford operations for universal computation
        
        let random = System.Random(42)  // Deterministic for testing
        
        // ===== STEP 1: Prepare Noisy Magic States =====
        let noisyErrorRate = 0.05  // 5% error (95% fidelity)
        let numNoisyStates = 15
        
        let noisyStates = 
            [1..numNoisyStates]
            |> List.map (fun _ -> 
                MagicStateDistillation.prepareNoisyMagicState noisyErrorRate AnyonSpecies.AnyonType.Ising
            )
            |> List.map (function 
                | Ok state -> state
                | Error err -> failwith $"Failed to prepare noisy state: {err.Message}"
            )
        
        let avgNoisyFidelity = noisyStates |> List.averageBy (fun s -> s.Fidelity)
        
        Assert.Equal(15, noisyStates.Length)
        Assert.InRange(avgNoisyFidelity, 0.94, 0.96)  // Should be ~95%
        
        // ===== STEP 2: Distill to High-Fidelity Magic States =====
        let distillationResult = MagicStateDistillation.distill15to1 random noisyStates
        
        match distillationResult with
        | Error err -> failwith $"Distillation failed: {err.Message}"
        | Ok result ->
            let purifiedFidelity = result.PurifiedState.Fidelity
            
            // Verify cubic error suppression (p_out ~ 35 * p_in^3)
            // With 95% input: p_in = 0.05, p_out = 35 * (0.05)^3 = 35 * 0.000125 = 0.004375
            // Expected output fidelity: 1 - 0.004375 = 0.995625 (99.56%)
            Assert.True(purifiedFidelity > avgNoisyFidelity, "Should improve fidelity")
            Assert.True(purifiedFidelity > 0.995, "Should achieve >99.5% fidelity")
            
            let purifiedState = result.PurifiedState
            
            // ===== STEP 3: Resource Estimation =====
            let targetFidelity = 0.9999  // 99.99% fidelity
            let resourceEstimate = 
                MagicStateDistillation.estimateResources targetFidelity (1.0 - noisyErrorRate)
            
            Assert.True(resourceEstimate.DistillationRounds >= 1)
            Assert.True(resourceEstimate.NoisyStatesRequired >= 15)
            
            // ===== STEP 4: Create Data Qubit =====
            let sigma = AnyonSpecies.Particle.Sigma
            let vacuum = AnyonSpecies.Particle.Vacuum
            
            // Create |0⟩ state: two sigma anyons fused to vacuum channel
            let left = FusionTree.leaf sigma
            let right = FusionTree.leaf sigma
            let dataQubitTree = FusionTree.fuse left right vacuum
            
            let dataQubit = FusionTree.create dataQubitTree AnyonSpecies.AnyonType.Ising
            
            Assert.Equal(AnyonSpecies.AnyonType.Ising, dataQubit.AnyonType)
            
            // ===== STEP 5: Apply T-Gate =====
            let tGateResult = MagicStateDistillation.applyTGate random dataQubit purifiedState
            
            match tGateResult with
            | Error err -> failwith $"T-gate application failed: {err.Message}"
            | Ok tResult ->
                Assert.True(tResult.GateFidelity > 0.99)
                Assert.Equal(AnyonSpecies.AnyonType.Ising, tResult.OutputState.AnyonType)
                
                // ===== STEP 6: Verify Universal Computation Capability =====
                // Toffoli gate decomposition: H·CNOT·T†·CNOT·T·CNOT·T†·CNOT·T·H
                // = 6 Clifford ops + 4 T-gates
                let toffoliTGates = 4
                let statesNeeded = toffoliTGates * resourceEstimate.NoisyStatesRequired
                
                // Verify we can implement universal gates
                Assert.True(tResult.GateFidelity >= purifiedFidelity * 0.99)
                Assert.True(statesNeeded > 0, "Should calculate resource requirements")
                
                // Success: demonstrated universal quantum computation with Ising anyons!
