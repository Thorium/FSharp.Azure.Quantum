// ============================================================================
// Backend Comparison - Ising vs Fibonacci Anyons
// ============================================================================
//
// This example compares different topological backend configurations:
// 1. Ising anyons (Microsoft Majorana - experimentally realizable)
// 2. Fibonacci anyons (theoretical gold standard - universal braiding)
//
// Key Differences:
// - Ising: Clifford-only (needs magic states for universality)
// - Fibonacci: Universal (braiding alone is universal for QC)
// - Ising: Simpler fusion rules (easier to implement in hardware)
// - Fibonacci: More complex but more powerful
//
// ============================================================================

#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum.Topological
open System.Diagnostics

// ============================================================================
// Backend Configuration Comparison
// ============================================================================

printfn "=== Topological Backend Comparison ==="
printfn ""

// Create both backend types
// ITopologicalBackend for low-level access (Capabilities, Initialize, MeasureFusion)
let isingBackend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Ising 10
let fibonacciBackend = TopologicalBackend.createSimulator AnyonSpecies.AnyonType.Fibonacci 10

// IQuantumBackend for the topological builder
let isingUnifiedBackend = TopologicalUnifiedBackendFactory.createIsing 20
let fibonacciUnifiedBackend = TopologicalUnifiedBackendFactory.createFibonacci 20

printfn "┌─────────────────────────────────────────────────────────────┐"
printfn "│ Backend: Ising Anyons (Microsoft Majorana)                 │"
printfn "├─────────────────────────────────────────────────────────────┤"
printfn "│ Capabilities:                                               │"

let isingCaps = isingBackend.Capabilities
printfn "│   Supported Anyons: %A" isingCaps.SupportedAnyonTypes
printfn "│   Max Anyons: %A" isingCaps.MaxAnyons
printfn "│   Braiding: %b" isingCaps.SupportsBraiding
printfn "│   Measurement: %b" isingCaps.SupportsMeasurement
printfn "│   F-Moves: %b" isingCaps.SupportsFMoves
printfn "│   Error Correction: %b" isingCaps.SupportsErrorCorrection
printfn "└─────────────────────────────────────────────────────────────┘"
printfn ""

printfn "┌─────────────────────────────────────────────────────────────┐"
printfn "│ Backend: Fibonacci Anyons (Theoretical Universal)          │"
printfn "├─────────────────────────────────────────────────────────────┤"
printfn "│ Capabilities:                                               │"

let fibCaps = fibonacciBackend.Capabilities
printfn "│   Supported Anyons: %A" fibCaps.SupportedAnyonTypes
printfn "│   Max Anyons: %A" fibCaps.MaxAnyons
printfn "│   Braiding: %b" fibCaps.SupportsBraiding
printfn "│   Measurement: %b" fibCaps.SupportsMeasurement
printfn "│   F-Moves: %b" fibCaps.SupportsFMoves
printfn "│   Error Correction: %b" fibCaps.SupportsErrorCorrection
printfn "└─────────────────────────────────────────────────────────────┘"
printfn ""

// ============================================================================
// Fusion Rule Comparison
// ============================================================================

printfn "=== Fusion Rules Comparison ==="
printfn ""

printfn "Ising Anyons: {1, σ, ψ}"
printfn "  σ × σ = 1 + ψ   (creates superposition)"
printfn "  σ × ψ = σ       (fermion acts like Z gate)"
printfn "  ψ × ψ = 1       (fermions annihilate)"
printfn ""

printfn "Fibonacci Anyons: {1, τ}"
printfn "  τ × τ = 1 + τ   (creates superposition)"
printfn "  (Simpler particle set but MORE powerful!)"
printfn ""

// ============================================================================
// Performance Comparison: Same Operation on Both Backends
// ============================================================================

printfn "=== Performance Comparison: Initialize + Braid ==="
printfn ""

let measurePerformance (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) anyonType label = task {
    let sw = Stopwatch.StartNew()
    
    let program = topological backend {
        do! TopologicalBuilder.initialize anyonType 6
        do! TopologicalBuilder.braid 0
        do! TopologicalBuilder.braid 2
        do! TopologicalBuilder.braid 4
    }
    
    let! result = TopologicalBuilder.execute backend program
    
    sw.Stop()
    
    match result with
    | Ok () ->
        printfn "✅ %s: %.3f ms" label sw.Elapsed.TotalMilliseconds
        return Ok sw.Elapsed.TotalMilliseconds
    | Error err ->
        printfn "❌ %s: Failed - %s" label err.Message
        return Error err
}

let isingTime = measurePerformance isingUnifiedBackend AnyonSpecies.AnyonType.Ising "Ising Backend  "
                |> Async.AwaitTask |> Async.RunSynchronously

let fibTime = measurePerformance fibonacciUnifiedBackend AnyonSpecies.AnyonType.Fibonacci "Fibonacci Backend"
              |> Async.AwaitTask |> Async.RunSynchronously

printfn ""

match (isingTime, fibTime) with
| (Ok t1, Ok t2) ->
    if t1 < t2 then
        printfn "Ising backend is %.2fx faster" (t2 / t1)
    else
        printfn "Fibonacci backend is %.2fx faster" (t1 / t2)
| _ ->
    printfn "Performance comparison incomplete"

printfn ""

// ============================================================================
// Computational Power Comparison
// ============================================================================

printfn "=== Computational Power ==="
printfn ""

printfn "┌──────────────────┬─────────────────┬──────────────────────┐"
printfn "│ Capability       │ Ising Anyons    │ Fibonacci Anyons     │"
printfn "├──────────────────┼─────────────────┼──────────────────────┤"
printfn "│ Clifford Gates   │ ✅ Yes          │ ✅ Yes               │"
printfn "│ T Gate           │ ❌ Magic States │ ✅ Braiding Only     │"
printfn "│ Universal QC     │ ⚠️  Hybrid      │ ✅ Pure Braiding     │"
printfn "│ Hardware Status  │ ✅ Experimental │ ❌ Theoretical       │"
printfn "│ Fusion Outcomes  │ 3 particles     │ 2 particles          │"
printfn "│ Complexity       │ Lower           │ Higher               │"
printfn "└──────────────────┴─────────────────┴──────────────────────┘"
printfn ""

// ============================================================================
// Fusion Measurement Statistics
// ============================================================================

printfn "=== Fusion Measurement Statistics ==="
printfn ""

let runFusionStatistics (backend: TopologicalBackend.ITopologicalBackend) anyonType label numTrials = task {
    let outcomes = System.Collections.Generic.Dictionary<AnyonSpecies.Particle, int>()
    
    for i in 1..numTrials do
        let! initResult = backend.Initialize anyonType 2
        
        match initResult with
        | Ok state ->
            let! measureResult = backend.MeasureFusion 0 state
            
            match measureResult with
            | Ok (outcome, _, _) ->
                if outcomes.ContainsKey(outcome) then
                    outcomes.[outcome] <- outcomes.[outcome] + 1
                else
                    outcomes.[outcome] <- 1
            | Error _ -> ()
        | Error _ -> ()
    
    printfn "%s (2 anyons, %d trials):" label numTrials
    for kvp in outcomes do
        let percentage = (float kvp.Value / float numTrials) * 100.0
        printfn "  %A: %d times (%.1f%%)" kvp.Key kvp.Value percentage
    printfn ""
}

runFusionStatistics isingBackend AnyonSpecies.AnyonType.Ising "Ising σ × σ" 1000
|> Async.AwaitTask |> Async.RunSynchronously

runFusionStatistics fibonacciBackend AnyonSpecies.AnyonType.Fibonacci "Fibonacci τ × τ" 1000
|> Async.AwaitTask |> Async.RunSynchronously

// ============================================================================
// Which Backend to Use?
// ============================================================================

printfn "=== Which Backend Should You Use? ==="
printfn ""

printfn "Choose ISING ANYONS if:"
printfn "  ✅ You want to match Microsoft's hardware roadmap"
printfn "  ✅ You're simulating Majorana-based topological computers"
printfn "  ✅ You need realistic hardware emulation"
printfn "  ✅ You're implementing Clifford circuits (H, S, CNOT, CZ)"
printfn "  ⚠️  Note: Requires magic state distillation for T gates"
printfn ""

printfn "Choose FIBONACCI ANYONS if:"
printfn "  ✅ You want theoretical exploration of universal braiding"
printfn "  ✅ You're researching topological quantum computation theory"
printfn "  ✅ You need pure braiding universality (no magic states)"
printfn "  ⚠️  Note: Not yet realized in physical systems"
printfn ""

// ============================================================================
// Example: Validation Test
// ============================================================================

printfn "=== Validation: Capability Check ==="
printfn ""

let validateBackend (backend: TopologicalBackend.ITopologicalBackend) requirements =
    let result = TopologicalBackend.validateCapabilities backend requirements
    
    match result with
    | Ok () ->
        printfn "✅ Backend meets all requirements"
    | Error err ->
        printfn "❌ Backend validation failed: %s" err.Message

// Require Ising anyons with braiding support
let isingRequirements = {
    TopologicalBackend.SupportedAnyonTypes = [AnyonSpecies.AnyonType.Ising]
    TopologicalBackend.MaxAnyons = Some 4
    TopologicalBackend.SupportsBraiding = true
    TopologicalBackend.SupportsMeasurement = true
    TopologicalBackend.SupportsFMoves = false
    TopologicalBackend.SupportsErrorCorrection = false
}

printfn "Testing Ising backend against requirements:"
validateBackend isingBackend isingRequirements

printfn ""

// Try to use Ising backend for Fibonacci (should fail)
let wrongRequirements = {
    TopologicalBackend.SupportedAnyonTypes = [AnyonSpecies.AnyonType.Fibonacci]
    TopologicalBackend.MaxAnyons = None
    TopologicalBackend.SupportsBraiding = true
    TopologicalBackend.SupportsMeasurement = true
    TopologicalBackend.SupportsFMoves = false
    TopologicalBackend.SupportsErrorCorrection = false
}

printfn "Testing Ising backend with Fibonacci requirements (should fail):"
validateBackend isingBackend wrongRequirements

printfn ""

printfn "=== Backend Comparison Complete ==="
printfn ""
printfn "Key Takeaways:"
printfn "1. Ising anyons match Microsoft's Majorana hardware"
printfn "2. Fibonacci anyons are theoretically more powerful"
printfn "3. Both backends support core topological operations"
printfn "4. Performance characteristics are similar for basic operations"
printfn "5. Choose based on use case (hardware emulation vs theory)"
