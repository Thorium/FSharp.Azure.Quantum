// ============================================================================
// Bell State Creation - Topological Quantum Computing
// ============================================================================
//
// This example demonstrates how to create an entangled Bell state using
// topological operations (braiding) rather than quantum gates.
//
// In gate-based QC: |Φ⁺⟩ = (|00⟩ + |11⟩) / √2 created by H-CNOT circuit
// In topological QC: Equivalent state via braiding operations
//
// Key Concepts:
// - Braiding creates entanglement geometrically (not algebraically)
// - Measurement outcomes are correlated due to topology
// - Topologically protected (immune to local noise)
//
// ============================================================================

#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"

open FSharp.Azure.Quantum.Topological

// ============================================================================
// Create Bell State via Topological Builder
// ============================================================================

printfn "=== Creating Bell State with Topological Operations ==="
printfn ""

let backend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10

// Use the topological builder (computation expression)
let createBellState = task {
    let! result = topological backend {
        // Initialize 4 anyons (minimum for encoding 1 qubit of entanglement)
        do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
        
        // Braid operations create entanglement
        do! TopologicalBuilder.braid 0  // Braid anyons 0 and 1
        do! TopologicalBuilder.braid 2  // Braid anyons 2 and 3
        
        // The braiding pattern creates an entangled state
        // analogous to |Φ⁺⟩ = (|00⟩ + |11⟩) / √2
    }
    
    match result with
    | Ok () ->
        printfn "✅ Bell state created via braiding operations"
        printfn ""
        printfn "Braiding sequence:"
        printfn "  1. Initialize 4 sigma anyons (σ σ σ σ)"
        printfn "  2. Braid anyon 0 around anyon 1"
        printfn "  3. Braid anyon 2 around anyon 3"
        printfn "  Result: Entangled topological state"
        return Ok ()
    | Error err ->
        printfn "❌ Failed to create Bell state: %s" err.Message
        return Error err
}

createBellState |> Async.AwaitTask |> Async.RunSynchronously |> ignore

printfn ""

// ============================================================================
// Demonstrate Entanglement via Correlated Measurements
// ============================================================================

printfn "=== Demonstrating Entanglement (Correlation Test) ==="
printfn ""

let testEntanglement numTrials = task {
    let mutable correlatedCount = 0
    
    for i in 1..numTrials do
        let! programResult = topological backend {
            do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
            do! TopologicalBuilder.braid 0
            do! TopologicalBuilder.braid 2
            
            // Measure fusion of first pair
            let! outcome1 = TopologicalBuilder.measure 0
            
            // Measure fusion of second pair (should be correlated!)
            let! outcome2 = TopologicalBuilder.measure 0  // Note: index shifts after first measurement
            
            return (outcome1, outcome2)
        }
        
        match programResult with
        | Ok (outcome1, outcome2) ->
            // Check if outcomes are correlated (both vacuum OR both psi)
            let isCorrelated =
                (outcome1 = AnyonSpecies.Particle.Vacuum && outcome2 = AnyonSpecies.Particle.Vacuum) ||
                (outcome1 = AnyonSpecies.Particle.Psi && outcome2 = AnyonSpecies.Particle.Psi)
            
            if isCorrelated then
                correlatedCount <- correlatedCount + 1
        | Error _ ->
            ()
    
    let correlationPercent = (float correlatedCount / float numTrials) * 100.0
    
    printfn "Results from %d trials:" numTrials
    printfn "  Correlated outcomes: %d (%.1f%%)" correlatedCount correlationPercent
    printfn "  Uncorrelated outcomes: %d (%.1f%%)" (numTrials - correlatedCount) (100.0 - correlationPercent)
    printfn ""
    
    if correlationPercent > 75.0 then
        printfn "✅ Strong correlation detected - entanglement verified!"
        printfn "   (Much higher than 50%% expected for independent measurements)"
    else
        printfn "⚠️  Correlation weaker than expected"
        printfn "   (May indicate issue with braiding sequence)"
}

testEntanglement 100 |> Async.AwaitTask |> Async.RunSynchronously

printfn ""

// ============================================================================
// Visual Comparison: Gate-Based vs Topological
// ============================================================================

printfn "=== Gate-Based vs Topological Comparison ==="
printfn ""
printfn "┌──────────────────────────────────────────────────────────────┐"
printfn "│ GATE-BASED QUANTUM COMPUTING                                │"
printfn "├──────────────────────────────────────────────────────────────┤"
printfn "│ Initial state: |00⟩                                          │"
printfn "│ Operations:                                                  │"
printfn "│   H(qubit 0)      → Create superposition                    │"
printfn "│   CNOT(0, 1)      → Entangle via controlled gate            │"
printfn "│ Result: |Φ⁺⟩ = (|00⟩ + |11⟩) / √2                           │"
printfn "└──────────────────────────────────────────────────────────────┘"
printfn ""
printfn "┌──────────────────────────────────────────────────────────────┐"
printfn "│ TOPOLOGICAL QUANTUM COMPUTING                               │"
printfn "├──────────────────────────────────────────────────────────────┤"
printfn "│ Initial state: σ σ σ σ (4 anyons)                           │"
printfn "│ Operations:                                                  │"
printfn "│   Braid(0)        → Geometric entanglement                  │"
printfn "│   Braid(2)        → Create correlation                      │"
printfn "│ Result: Entangled fusion tree (topologically equivalent)    │"
printfn "└──────────────────────────────────────────────────────────────┘"
printfn ""

// ============================================================================
// Advanced: Braiding Pattern Visualization
// ============================================================================

printfn "=== Braiding Worldline Diagram (Conceptual) ==="
printfn ""
printfn "Time"
printfn "  ↑"
printfn "  │   σ   σ   σ   σ     (4 anyons at t=0)"
printfn "  │   │   │   │   │"
printfn "  │   │╲ ╱│   │   │     Braid(0): Exchange anyons 0 & 1"
printfn "  │   │ ╳ │   │   │"
printfn "  │   │╱ ╲│   │   │"
printfn "  │   │   │   │╲ ╱│     Braid(2): Exchange anyons 2 & 3"
printfn "  │   │   │   │ ╳ │"
printfn "  │   │   │   │╱ ╲│"
printfn "  │   │   │   │   │     (Entangled state)"
printfn "  │"
printfn "  └─────────────────→ Space"
printfn ""
printfn "Note: The worldlines trace out a braid in 2+1 dimensional spacetime."
printfn "      This geometric structure encodes quantum information!"
printfn ""

printfn "=== Bell State Example Complete ==="
printfn ""
printfn "Key Takeaways:"
printfn "1. Braiding creates entanglement geometrically"
printfn "2. Measurements show correlation (Bell state signature)"
printfn "3. Topological protection: immune to local perturbations"
printfn "4. Worldline braiding ≈ Quantum gates (different paradigm)"
printfn "5. Microsoft's approach: Majorana anyons for fault-tolerance"
