// Magic State Distillation Example
// 
// Demonstrates how to achieve universal quantum computation with Ising anyons
// using magic state distillation for T-gates.
//
// Ising anyons (Majorana zero modes) can only perform Clifford operations natively.
// To achieve universality, we need non-Clifford gates like T-gates.
// Magic state distillation allows us to implement T-gates with high fidelity.

#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"

open System
open FSharp.Azure.Quantum.Topological

printfn "=========================================="
printfn "Magic State Distillation Example"
printfn "=========================================="
printfn ""

// ========================================
// Example 1: Single Round Distillation
// ========================================

printfn "Example 1: Single Round 15-to-1 Distillation"
printfn "---------------------------------------------"

let random = Random()
let noisyErrorRate = 0.05  // 5% error

// Prepare 15 noisy magic states
printfn "Preparing 15 noisy magic states with %.1f%% error..." (noisyErrorRate * 100.0)

let noisyStates = 
    [1..15]
    |> List.map (fun _ -> 
        MagicStateDistillation.prepareNoisyMagicState noisyErrorRate AnyonSpecies.AnyonType.Ising
    )
    |> List.choose (function Ok state -> Some state | Error _ -> None)

match noisyStates with
| (states: MagicStateDistillation.MagicState list) when states.Length = 15 ->
    printfn "✓ Successfully prepared %d noisy states" states.Length
    printfn "  Average input fidelity: %.4f (%.2f%% error)" 
        (List.averageBy (fun (s: MagicStateDistillation.MagicState) -> s.Fidelity) states)
        ((List.averageBy (fun (s: MagicStateDistillation.MagicState) -> s.ErrorRate) states) * 100.0)
    
    // Distill to high-fidelity magic state
    printfn ""
    printfn "Applying 15-to-1 distillation protocol..."
    
    match MagicStateDistillation.distill15to1 random states with
    | Ok distillResult ->
        let purifiedState = distillResult.PurifiedState
        
        printfn "✓ Distillation successful!"
        printfn "  Output fidelity: %.6f (%.4f%% error)" 
            purifiedState.Fidelity
            (purifiedState.ErrorRate * 100.0)
        
        let errorSuppression = 
            (List.averageBy (fun (s: MagicStateDistillation.MagicState) -> s.ErrorRate) states) / purifiedState.ErrorRate
        
        printfn "  Error suppression: %.1fx" errorSuppression
        printfn "  Acceptance probability: %.4f" distillResult.AcceptanceProbability
        printfn "  Syndromes detected: %d/%d" 
            (distillResult.Syndromes |> List.filter id |> List.length)
            (distillResult.Syndromes.Length)
        
    | Error err ->
        printfn "✗ Distillation failed: %s" err.Message

| states ->
    printfn "✗ Failed to prepare sufficient states (got %d/15)" states.Length

printfn ""

// ========================================
// Example 2: Iterative Distillation
// ========================================

printfn "Example 2: Two Rounds of Iterative Distillation"
printfn "-----------------------------------------------"

// For 2 rounds, we need 15^2 = 225 initial states
let initialErrorRate = 0.10  // 10% error
let requiredStates = 225

printfn "Preparing %d noisy states with %.1f%% error..." requiredStates (initialErrorRate * 100.0)

let round1States = 
    [1..requiredStates]
    |> List.map (fun _ -> 
        MagicStateDistillation.prepareNoisyMagicState initialErrorRate AnyonSpecies.AnyonType.Ising
    )
    |> List.choose (function Ok state -> Some state | Error _ -> None)

match round1States with
| states when states.Length = requiredStates ->
    printfn "✓ Successfully prepared %d noisy states" states.Length
    
    printfn ""
    printfn "Applying 2 rounds of distillation (15^2 = 225 → 1)..."
    
    match MagicStateDistillation.distillIterative random 2 states with
    | Ok finalState ->
        printfn "✓ Iterative distillation successful!"
        printfn "  Input error:  %.4f (%.2f%%)" initialErrorRate (initialErrorRate * 100.0)
        printfn "  Output error: %.8f (%.6f%%)" finalState.ErrorRate (finalState.ErrorRate * 100.0)
        
        let totalSuppression = initialErrorRate / finalState.ErrorRate
        printfn "  Total error suppression: %.1fx" totalSuppression
        
        printfn ""
        printfn "  Theoretical prediction: p_out ≈ 35^2 * p_in^9"
        let theoreticalOutput = 35.0 * 35.0 * (initialErrorRate ** 9.0)
        printfn "  Theoretical output error: %.8f" theoreticalOutput
        
    | Error err ->
        printfn "✗ Iterative distillation failed: %s" err.Message

| states ->
    printfn "✗ Failed to prepare sufficient states (got %d/%d)" states.Length requiredStates

printfn ""

// ========================================
// Example 3: Resource Estimation
// ========================================

printfn "Example 3: Resource Estimation"
printfn "------------------------------"

let targetFidelity = 0.9999  // 99.99% target fidelity
let noisyFidelity = 0.95     // 95% initial fidelity

printfn "Target fidelity: %.2f%%" (targetFidelity * 100.0)
printfn "Noisy state fidelity: %.2f%%" (noisyFidelity * 100.0)
printfn ""

let estimate = 
    MagicStateDistillation.estimateResources targetFidelity noisyFidelity

printfn "%s" (MagicStateDistillation.displayResourceEstimate estimate)

printfn ""

// ========================================
// Example 4: Applying T-Gate
// ========================================

printfn "Example 4: Applying T-Gate to Topological Qubit"
printfn "-----------------------------------------------"

// Create a topological qubit in |0⟩ state (4 sigma anyons)
let sigma = AnyonSpecies.Particle.Sigma
let vacuum = AnyonSpecies.Particle.Vacuum

let dataQubit = 
    let tree =
        FusionTree.fuse
            (FusionTree.fuse
                (FusionTree.fuse
                    (FusionTree.leaf sigma)
                    (FusionTree.leaf sigma)
                    vacuum)
                (FusionTree.leaf sigma)
                vacuum)
            (FusionTree.leaf sigma)
            vacuum
    FusionTree.create tree AnyonSpecies.AnyonType.Ising

printfn "Created topological qubit |0⟩ (4 sigma anyons)"

// Prepare high-fidelity magic state for T-gate
let magicStatesForTGate = 
    [1..15]
    |> List.map (fun _ -> 
        MagicStateDistillation.prepareNoisyMagicState 0.05 AnyonSpecies.AnyonType.Ising
    )
    |> List.choose (function Ok state -> Some state | Error _ -> None)

match magicStatesForTGate with
| states when states.Length = 15 ->
    match MagicStateDistillation.distill15to1 random states with
    | Ok distillResult ->
        let purifiedMagicState = distillResult.PurifiedState
        printfn "Prepared purified magic state (fidelity: %.6f)" purifiedMagicState.Fidelity
        
        printfn ""
        printfn "Applying T-gate using magic state injection..."
        
        match MagicStateDistillation.applyTGate random dataQubit purifiedMagicState with
        | Ok tGateResult ->
            printfn "✓ T-gate applied successfully!"
            printfn "  Gate fidelity: %.6f" tGateResult.GateFidelity
            printfn "  Output state: T|0⟩"
            printfn ""
            printfn "  With Clifford gates + T-gate → Universal quantum computation!"
            
        | Error err ->
            printfn "✗ T-gate application failed: %s" err.Message
    
    | Error err ->
        printfn "✗ Magic state distillation failed: %s" err.Message

| states ->
    printfn "✗ Failed to prepare sufficient magic states (got %d/15)" states.Length

printfn ""
printfn "=========================================="
printfn "Example completed successfully!"
printfn "=========================================="
