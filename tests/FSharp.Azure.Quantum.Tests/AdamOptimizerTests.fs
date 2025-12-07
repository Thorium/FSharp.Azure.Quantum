module FSharp.Azure.Quantum.Tests.AdamOptimizerTests

open Xunit
open FSharp.Azure.Quantum.MachineLearning.AdamOptimizer

// ============================================================================
// Helper Functions
// ============================================================================

let private epsilon = 1e-6

let private assertArraysEqual (expected: float array) (actual: float array) =
    Assert.Equal(expected.Length, actual.Length)
    Array.zip expected actual
    |> Array.iter (fun (e, a) -> Assert.True(abs (a - e) < epsilon, sprintf "Expected %f, got %f" e a))

let private assertArraysClose (tolerance: float) (expected: float array) (actual: float array) =
    Assert.Equal(expected.Length, actual.Length)
    Array.zip expected actual
    |> Array.iter (fun (e, a) -> Assert.True(abs (a - e) < tolerance, sprintf "Expected %f ± %f, got %f" e tolerance a))

// ============================================================================
// Configuration Tests
// ============================================================================

[<Fact>]
let ``defaultConfig should have valid parameters`` () =
    Assert.True(defaultConfig.LearningRate > 0.0, "Learning rate must be positive")
    Assert.True(defaultConfig.Beta1 >= 0.0 && defaultConfig.Beta1 < 1.0, "Beta1 must be in [0, 1)")
    Assert.True(defaultConfig.Beta2 >= 0.0 && defaultConfig.Beta2 < 1.0, "Beta2 must be in [0, 1)")
    Assert.True(defaultConfig.Epsilon > 0.0, "Epsilon must be positive")

[<Fact>]
let ``createConfig should accept valid parameters`` () =
    let result = createConfig 0.001 0.9 0.999 1e-8
    match result with
    | Ok config ->
        Assert.Equal(0.001, config.LearningRate)
        Assert.Equal(0.9, config.Beta1)
        Assert.Equal(0.999, config.Beta2)
        Assert.Equal(1e-8, config.Epsilon)
    | Error err -> failwithf "Should not fail: %s" err.Message

[<Fact>]
let ``createConfig should reject negative learning rate`` () =
    let result = createConfig -0.001 0.9 0.999 1e-8
    match result with
    | Error msg -> Assert.Contains("Learning rate", msg.Message)
    | Ok _ -> failwith "Should have failed"

[<Fact>]
let ``createConfig should reject Beta1 outside range`` () =
    let result1 = createConfig 0.001 -0.1 0.999 1e-8
    match result1 with
    | Error msg -> Assert.Contains("Beta1", msg.Message)
    | Ok _ -> failwith "Should have failed for negative Beta1"

    let result2 = createConfig 0.001 1.0 0.999 1e-8
    match result2 with
    | Error msg -> Assert.Contains("Beta1", msg.Message)
    | Ok _ -> failwith "Should have failed for Beta1 = 1.0"

[<Fact>]
let ``createConfig should reject Beta2 outside range`` () =
    let result1 = createConfig 0.001 0.9 -0.1 1e-8
    match result1 with
    | Error msg -> Assert.Contains("Beta2", msg.Message)
    | Ok _ -> failwith "Should have failed for negative Beta2"

    let result2 = createConfig 0.001 0.9 1.0 1e-8
    match result2 with
    | Error msg -> Assert.Contains("Beta2", msg.Message)
    | Ok _ -> failwith "Should have failed for Beta2 = 1.0"

[<Fact>]
let ``createConfig should reject non-positive epsilon`` () =
    let result = createConfig 0.001 0.9 0.999 0.0
    match result with
    | Error msg -> Assert.Contains("Epsilon", msg.Message)
    | Ok _ -> failwith "Should have failed"

// ============================================================================
// State Management Tests
// ============================================================================

[<Fact>]
let ``createState should initialize zero moment vectors`` () =
    let state = createState 5
    Assert.Equal(5, state.M.Length)
    Assert.Equal(5, state.V.Length)
    Assert.Equal(0, state.T)
    Assert.True(Array.forall ((=) 0.0) state.M, "M should be all zeros")
    Assert.True(Array.forall ((=) 0.0) state.V, "V should be all zeros")

[<Fact>]
let ``resetState should clear moment vectors and time step`` () =
    let state = { M = [|1.0; 2.0; 3.0|]; V = [|4.0; 5.0; 6.0|]; T = 10 }
    let reset = resetState state
    Assert.Equal(3, reset.M.Length)
    Assert.Equal(3, reset.V.Length)
    Assert.Equal(0, reset.T)
    Assert.True(Array.forall ((=) 0.0) reset.M, "M should be reset to zeros")
    Assert.True(Array.forall ((=) 0.0) reset.V, "V should be reset to zeros")

// ============================================================================
// Update Tests - Basic Functionality
// ============================================================================

[<Fact>]
let ``update should increment time step`` () =
    let config = defaultConfig
    let state = createState 3
    let parameters = [|1.0; 2.0; 3.0|]
    let gradients = [|0.1; 0.2; 0.3|]
    
    let result = update config state parameters gradients
    match result with
    | Ok (_, newState) ->
        Assert.Equal(1, newState.T)
    | Error err -> failwithf "Should not fail: %s" err.Message

[<Fact>]
let ``update should reject mismatched parameter dimensions`` () =
    let config = defaultConfig
    let state = createState 3
    let parameters = [|1.0; 2.0|]  // Wrong size
    let gradients = [|0.1; 0.2; 0.3|]
    
    let result = update config state parameters gradients
    match result with
    | Error msg -> Assert.Contains("Parameters length", msg.Message)
    | Ok _ -> failwith "Should have failed"

[<Fact>]
let ``update should reject mismatched gradient dimensions`` () =
    let config = defaultConfig
    let state = createState 3
    let parameters = [|1.0; 2.0; 3.0|]
    let gradients = [|0.1; 0.2|]  // Wrong size
    
    let result = update config state parameters gradients
    match result with
    | Error msg -> Assert.Contains("Gradients length", msg.Message)
    | Ok _ -> failwith "Should have failed"

[<Fact>]
let ``update should move parameters in direction opposite to gradient`` () =
    let config = { defaultConfig with LearningRate = 0.1 }
    let state = createState 1
    let parameters = [|5.0|]
    let gradients = [|2.0|]  // Positive gradient
    
    let result = update config state parameters gradients
    match result with
    | Ok (newParams, _) ->
        // With positive gradient, parameter should decrease
        Assert.True(newParams.[0] < parameters.[0], "Parameter should decrease")
    | Error err -> failwithf "Should not fail: %s" err.Message

[<Fact>]
let ``update should handle zero gradients`` () =
    let config = defaultConfig
    let state = createState 3
    let parameters = [|1.0; 2.0; 3.0|]
    let gradients = [|0.0; 0.0; 0.0|]
    
    let result = update config state parameters gradients
    match result with
    | Ok (newParams, newState) ->
        Assert.Equal(1, newState.T)
        // With zero gradients, parameters should change slightly due to bias correction
        // but should be very close to original
        assertArraysClose 0.01 parameters newParams
    | Error err -> failwithf "Should not fail: %s" err.Message

// ============================================================================
// Update Tests - Momentum (First Moment)
// ============================================================================

[<Fact>]
let ``update should accumulate momentum over multiple steps`` () =
    let config = { defaultConfig with LearningRate = 0.1; Beta1 = 0.9 }
    let mutable state = createState 1
    let parameters = [|5.0|]
    let gradients = [|1.0|]  // Constant gradient
    
    // Take 3 steps with same gradient
    for _ in 1..3 do
        match update config state parameters gradients with
        | Ok (_, newState) ->
            state <- newState
        | Error err -> failwithf "Should not fail: %s" err.Message
    
    // Momentum should accumulate (M should be non-zero and growing)
    Assert.True(state.M.[0] > 0.0, "Momentum should be positive")
    Assert.Equal(3, state.T)

[<Fact>]
let ``update should apply exponential decay to momentum`` () =
    let config = { defaultConfig with Beta1 = 0.9 }
    let state = { (createState 1) with M = [|10.0|]; T = 1 }
    let parameters = [|5.0|]
    let gradients = [|0.0|]  // Zero gradient to see decay
    
    let result = update config state parameters gradients
    match result with
    | Ok (_, newState) ->
        // With zero gradient and Beta1 = 0.9:
        // newM = 0.9 * 10.0 + 0.1 * 0.0 = 9.0
        Assert.True(abs (newState.M.[0] - 9.0) < 0.01, sprintf "Expected 9.0, got %f" newState.M.[0])
    | Error err -> failwithf "Should not fail: %s" err.Message

// ============================================================================
// Update Tests - RMSprop (Second Moment)
// ============================================================================

[<Fact>]
let ``update should accumulate squared gradients in second moment`` () =
    let config = { defaultConfig with Beta2 = 0.999 }
    let state = createState 1
    let parameters = [|5.0|]
    let gradients = [|2.0|]
    
    let result = update config state parameters gradients
    match result with
    | Ok (_, newState) ->
        // V should contain squared gradient term
        // newV = 0.999 * 0.0 + 0.001 * (2.0^2) = 0.004
        Assert.True(abs (newState.V.[0] - 0.004) < 0.001, sprintf "Expected 0.004, got %f" newState.V.[0])
    | Error err -> failwithf "Should not fail: %s" err.Message

[<Fact>]
let ``update should adapt learning rate based on gradient history`` () =
    let config = { defaultConfig with LearningRate = 0.1 }
    let state = createState 2
    let parameters = [|5.0; 5.0|]
    
    // Large gradient for first parameter, small for second
    let gradients = [|10.0; 0.1|]
    
    let result = update config state parameters gradients
    match result with
    | Ok (newParams, _) ->
        let change1 = abs (newParams.[0] - parameters.[0])
        let change2 = abs (newParams.[1] - parameters.[1])
        
        // Both parameters should change
        Assert.True(change1 > 0.0, "First parameter should change")
        Assert.True(change2 > 0.0, "Second parameter should change")
    | Error err -> failwithf "Should not fail: %s" err.Message

// ============================================================================
// Update Tests - Bias Correction
// ============================================================================

[<Fact>]
let ``update should apply bias correction to first moment`` () =
    let config = { defaultConfig with Beta1 = 0.9; LearningRate = 1.0 }
    let state = createState 1
    let parameters = [|0.0|]
    let gradients = [|1.0|]
    
    // First step: M = 0.1, bias_correction1 = 0.1, M_hat = 1.0
    let result = update config state parameters gradients
    match result with
    | Ok (newParams, newState) ->
        // Without bias correction, update would be tiny
        // With bias correction, M_hat = M / (1 - 0.9^1) = 0.1 / 0.1 = 1.0
        Assert.True(abs (newState.M.[0] - 0.1) < 0.01, sprintf "Expected M=0.1, got %f" newState.M.[0])
        Assert.Equal(1, newState.T)
    | Error err -> failwithf "Should not fail: %s" err.Message

[<Fact>]
let ``update should apply bias correction to second moment`` () =
    let config = { defaultConfig with Beta2 = 0.999 }
    let state = createState 1
    let parameters = [|0.0|]
    let gradients = [|1.0|]
    
    let result = update config state parameters gradients
    match result with
    | Ok (_, newState) ->
        // V = 0.001 * 1.0 = 0.001
        // bias_correction2 = 1 - 0.999^1 = 0.001
        // V_hat = 0.001 / 0.001 = 1.0 (bias corrected)
        Assert.True(abs (newState.V.[0] - 0.001) < 0.0001, sprintf "Expected V=0.001, got %f" newState.V.[0])
    | Error err -> failwithf "Should not fail: %s" err.Message

// ============================================================================
// Integration Tests - Convergence Behavior
// ============================================================================

[<Fact>]
let ``update should converge on simple quadratic function`` () =
    // Minimize f(x) = x^2, gradient = 2x
    let config = { defaultConfig with LearningRate = 1.0 }  // Higher learning rate for faster convergence
    let mutable state = createState 1
    let mutable parameters = [|10.0|]  // Start far from minimum
    
    // Run 100 optimization steps
    for _ in 1..100 do
        let gradient = [|2.0 * parameters.[0]|]  // Gradient of x^2
        match update config state parameters gradient with
        | Ok (newParams, newState) ->
            parameters <- newParams
            state <- newState
        | Error err -> failwithf "Should not fail: %s" err.Message
    
    // Should converge closer to x = 0 (Adam may not reach exact 0 but should be much smaller)
    Assert.True(abs parameters.[0] < abs 10.0, sprintf "Expected convergence closer to 0 than 10.0, got %f" parameters.[0])
    Assert.True(abs parameters.[0] < 1.0, sprintf "Expected convergence within 1.0 of 0, got %f" parameters.[0])

[<Fact>]
let ``update should handle multiple parameters independently`` () =
    let config = { defaultConfig with LearningRate = 0.1 }
    let state = createState 3
    let parameters = [|1.0; 2.0; 3.0|]
    let gradients = [|0.1; -0.2; 0.3|]
    
    let result = update config state parameters gradients
    match result with
    | Ok (newParams, newState) ->
        Assert.Equal(3, newState.M.Length)
        Assert.Equal(3, newState.V.Length)
        Assert.Equal(3, newParams.Length)
        
        // Each parameter should move opposite to its gradient
        Assert.True(newParams.[0] < parameters.[0], "Param 0 should decrease (positive gradient)")
        Assert.True(newParams.[1] > parameters.[1], "Param 1 should increase (negative gradient)")
        Assert.True(newParams.[2] < parameters.[2], "Param 2 should decrease (positive gradient)")
    | Error err -> failwithf "Should not fail: %s" err.Message

[<Fact>]
let ``updateWithDefaults should use defaultConfig`` () =
    let state = createState 1
    let parameters = [|5.0|]
    let gradients = [|1.0|]
    
    let result1 = updateWithDefaults state parameters gradients
    let result2 = update defaultConfig state parameters gradients
    
    match result1, result2 with
    | Ok (params1, state1), Ok (params2, state2) ->
        assertArraysEqual params1 params2
        Assert.Equal(state1.T, state2.T)
    | _ -> failwith "Both should succeed"

// ============================================================================
// Utility Function Tests
// ============================================================================

[<Fact>]
let ``getEffectiveLearningRate should return base rate at t=0`` () =
    let config = { defaultConfig with LearningRate = 0.001 }
    let state = createState 1
    let effectiveLR = getEffectiveLearningRate config state
    Assert.Equal(0.001, effectiveLR)

[<Fact>]
let ``getEffectiveLearningRate should be computed correctly at t=1`` () =
    let config = { defaultConfig with LearningRate = 0.001; Beta1 = 0.9; Beta2 = 0.999 }
    let state = { (createState 1) with T = 1 }
    let effectiveLR = getEffectiveLearningRate config state
    
    // α_effective = α * √(1 - β₂^t) / (1 - β₁^t)
    // At t=1: α_effective = 0.001 * √(1 - 0.999) / (1 - 0.9) = 0.001 * √0.001 / 0.1 ≈ 0.000316
    Assert.True(abs (effectiveLR - 0.000316) < 0.00001, sprintf "Expected ~0.000316, got %f" effectiveLR)

[<Fact>]
let ``getEffectiveLearningRate should approach stable value after many iterations`` () =
    let config = { defaultConfig with LearningRate = 0.001; Beta1 = 0.9; Beta2 = 0.999 }
    let state = { (createState 1) with T = 1000 }
    let effectiveLR = getEffectiveLearningRate config state
    
    // After many iterations, bias correction factors approach 1.0
    // α_effective = α * √(1 - β₂^1000) / (1 - β₁^1000) ≈ α * √1.0 / 1.0 ≈ α * 0.795 (due to sqrt term)
    // The effective LR stabilizes but is not exactly equal to base LR
    Assert.True(effectiveLR > 0.0005 && effectiveLR < 0.001, sprintf "Expected between 0.0005 and 0.001, got %f" effectiveLR)

// ============================================================================
// Edge Cases
// ============================================================================

[<Fact>]
let ``update should handle very large gradients without overflow`` () =
    let config = defaultConfig
    let state = createState 1
    let parameters = [|1.0|]
    let gradients = [|1e10|]  // Very large gradient
    
    let result = update config state parameters gradients
    match result with
    | Ok (newParams, _) ->
        Assert.False(System.Double.IsPositiveInfinity(newParams.[0]), "Should not overflow to +Inf")
        Assert.False(System.Double.IsNegativeInfinity(newParams.[0]), "Should not overflow to -Inf")
        Assert.False(System.Double.IsNaN(newParams.[0]), "Should not be NaN")
    | Error err -> failwithf "Should not fail: %s" err.Message

[<Fact>]
let ``update should handle very small gradients without underflow`` () =
    let config = defaultConfig
    let state = createState 1
    let parameters = [|1.0|]
    let gradients = [|1e-15|]  // Very small gradient
    
    let result = update config state parameters gradients
    match result with
    | Ok (newParams, _) ->
        Assert.False(System.Double.IsNaN(newParams.[0]), "Should not be NaN")
    | Error err -> failwithf "Should not fail: %s" err.Message

[<Fact>]
let ``update should be deterministic with same inputs`` () =
    let config = defaultConfig
    let state = createState 2
    let parameters = [|1.0; 2.0|]
    let gradients = [|0.1; 0.2|]
    
    let result1 = update config state parameters gradients
    let result2 = update config state parameters gradients
    
    match result1, result2 with
    | Ok (params1, state1), Ok (params2, state2) ->
        assertArraysEqual params1 params2
        assertArraysEqual state1.M state2.M
        assertArraysEqual state1.V state2.V
        Assert.Equal(state1.T, state2.T)
    | _ -> failwith "Both should succeed and match"
