// ============================================================================
// Basic Fusion Example - Topological Quantum Computing
// ============================================================================
//
// This example demonstrates the fundamental fusion rules of Ising anyons,
// which are the building blocks of Microsoft's topological quantum computer.
//
// Key Concepts:
// - Ising anyons: {1 (vacuum), σ (sigma), ψ (psi)}
// - Fusion rule: σ × σ = 1 + ψ (sigma + sigma can fuse to vacuum OR psi)
// - Measurement collapses the superposition to one outcome
//
// ============================================================================

#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"

open FSharp.Azure.Quantum.Topological

// ============================================================================
// Example 1: Create and Inspect Initial State
// ============================================================================

printfn "=== Example 1: Initialize Ising Anyons ==="
printfn ""

// Create a topological backend (simulator for Ising anyons)
let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10

// Initialize with 2 sigma anyons
let initialize2Anyons = task {
    let! result = backend.Initialize AnyonSpecies.AnyonType.Ising 2
    
    match result with
    | Ok state ->
        printfn "✅ Initialized 2 Ising anyons (σ particles)"
        printfn "State has %d terms in superposition" state.Terms.Length
        
        // Show the fusion tree structure
        for (amplitude, fusionState) in state.Terms do
            printfn "  Amplitude: %A" amplitude
            printfn "  Fusion tree: %A" fusionState.Tree
        
        return Ok state
    | Error err ->
        printfn "❌ Initialization failed: %s" err.Message
        return Error err
}

let state2 = initialize2Anyons |> Async.AwaitTask |> Async.RunSynchronously

printfn ""

// ============================================================================
// Example 2: Fusion Measurement (σ × σ = 1 + ψ)
// ============================================================================

printfn "=== Example 2: Measure Fusion of Two Sigma Anyons ==="
printfn ""

match state2 with
| Ok initialState ->
    let measureFusion = task {
        let! measureResult = backend.MeasureFusion 0 initialState
        
        match measureResult with
        | Ok (outcome, collapsedState, probability) ->
            printfn "✅ Fusion measurement complete!"
            printfn "Outcome: %A (probability: %.4f)" outcome probability
            
            match outcome with
            | AnyonSpecies.Particle.Vacuum ->
                printfn "  → Anyons fused to vacuum (trivial fusion)"
            | AnyonSpecies.Particle.Psi ->
                printfn "  → Anyons fused to psi fermion (non-trivial fusion)"
            | AnyonSpecies.Particle.Sigma ->
                printfn "  → Unexpected: sigma (shouldn't happen for σ×σ)"
            | _ ->
                printfn "  → Unknown outcome"
            
            printfn ""
            printfn "Collapsed state:"
            printfn "  Terms after measurement: %d" collapsedState.Terms.Length
            
            return Ok ()
        | Error err ->
            printfn "❌ Measurement failed: %s" err.Message
            return Error err
    }
    
    measureFusion |> Async.AwaitTask |> Async.RunSynchronously |> ignore
| Error _ ->
    printfn "⏭️  Skipping measurement (initialization failed)"

printfn ""

// ============================================================================
// Example 3: Multiple Measurements (Statistical Distribution)
// ============================================================================

printfn "=== Example 3: Fusion Statistics (1000 measurements) ==="
printfn ""

let runFusionStatistics numTrials = task {
    let mutable vacuumCount = 0
    let mutable psiCount = 0
    
    for i in 1..numTrials do
        // Initialize fresh state each time
        let! initResult = backend.Initialize AnyonSpecies.AnyonType.Ising 2
        
        match initResult with
        | Ok state ->
            let! measureResult = backend.MeasureFusion 0 state
            
            match measureResult with
            | Ok (outcome, _, _) ->
                match outcome with
                | AnyonSpecies.Particle.Vacuum -> vacuumCount <- vacuumCount + 1
                | AnyonSpecies.Particle.Psi -> psiCount <- psiCount + 1
                | _ -> ()
            | Error _ -> ()
        | Error _ -> ()
    
    printfn "Results from %d trials:" numTrials
    printfn "  Vacuum (1): %d times (%.1f%%)" vacuumCount ((float vacuumCount / float numTrials) * 100.0)
    printfn "  Psi (ψ):    %d times (%.1f%%)" psiCount ((float psiCount / float numTrials) * 100.0)
    printfn ""
    printfn "Expected distribution: ~50%% vacuum, ~50%% psi"
    printfn "  (from fusion rule σ × σ = 1 + ψ)"
}

runFusionStatistics 1000 |> Async.AwaitTask |> Async.RunSynchronously

printfn ""

// ============================================================================
// Example 4: Four Anyons (More Complex Fusion Tree)
// ============================================================================

printfn "=== Example 4: Four Ising Anyons (2-Qubit Equivalent) ==="
printfn ""

let fourAnyonExample = task {
    let! initResult = backend.Initialize AnyonSpecies.AnyonType.Ising 4
    
    match initResult with
    | Ok state ->
        printfn "✅ Initialized 4 anyons (creates 2-dimensional fusion space)"
        printfn "Terms in superposition: %d" state.Terms.Length
        
        // This creates a fusion tree that encodes quantum information
        // similar to 1 qubit in gate-based QC
        printfn ""
        printfn "Fusion tree structure:"
        for (amp, fusionState) in state.Terms do
            printfn "  Amplitude: %A" amp
        
        return Ok ()
    | Error err ->
        printfn "❌ Failed: %s" err.Message
        return Error err
}

fourAnyonExample |> Async.AwaitTask |> Async.RunSynchronously |> ignore

printfn ""
printfn "=== Basic Fusion Examples Complete ==="
printfn ""
printfn "Key Takeaways:"
printfn "1. Ising anyons (σ) are the building blocks of topological qubits"
printfn "2. Fusion rule σ × σ = 1 + ψ creates superposition"
printfn "3. Measurement collapses to one outcome (vacuum or psi)"
printfn "4. Statistics match quantum mechanical predictions"
printfn "5. More anyons create larger fusion spaces (quantum information)"
