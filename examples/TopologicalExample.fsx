// ============================================================================
// Topological Quantum Computing Example
// Demonstrates the layered architecture with idiomatic F#
// ============================================================================

#r "../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"

open FSharp.Azure.Quantum.Topological
open System

// ============================================================================
// LAYER 1: CORE - Mathematical Foundation (Pure Functions)
// ============================================================================
// This layer contains the mathematical primitives: anyon species, fusion rules,
// braiding operators. These are pure functions with no side effects.

module CoreTopologicalMath =
    
    /// Create an Ising theory context
    let isingTheory = AnyonSpecies.AnyonType.Ising
    
    /// Get fusion channels for sigma × sigma
    let sigmaFusionChannels = 
        FusionRules.channels 
            AnyonSpecies.Particle.Sigma 
            AnyonSpecies.Particle.Sigma 
            isingTheory
    
    /// Demonstrate fusion rule: σ × σ = 1 + ψ
    let demonstrateFusionRule () =
        printfn "=== Core Fusion Rules ==="
        printfn "σ × σ fusion channels:"
        match sigmaFusionChannels with
        | Ok channels ->
            channels |> List.iter (fun channel -> printfn "  - %A" channel)
        | Error err ->
            printfn "  Error: %s" err.Message
        printfn ""
        
        // Calculate quantum dimension
        let sigmaDim = AnyonSpecies.quantumDimension AnyonSpecies.Particle.Sigma
        printfn "Quantum dimension of σ: %f" sigmaDim
        printfn ""

// ============================================================================
// LAYER 2: BACKENDS - Execution Abstraction
// ============================================================================
// This layer provides backend implementations (simulator, hardware) that
// execute topological quantum operations. Follows ITopologicalBackend interface.

module TopologicalBackends =
    
    /// Create a local simulator backend for Ising anyons
    let createIsingSimulator maxAnyons =
        TopologicalBackend.createSimulator 
            AnyonSpecies.AnyonType.Ising 
            maxAnyons
    
    /// Create a local simulator backend for Fibonacci anyons
    let createFibonacciSimulator maxAnyons =
        TopologicalBackend.createSimulator 
            AnyonSpecies.AnyonType.Fibonacci 
            maxAnyons
    
    /// Demonstrate backend capabilities
    let demonstrateBackendCapabilities () =
        printfn "=== Backend Capabilities ==="
        
        let backend = createIsingSimulator 10
        let caps = backend.Capabilities
        
        printfn "Supported anyon types: %A" caps.SupportedAnyonTypes
        printfn "Max anyons: %A" caps.MaxAnyons
        printfn "Supports braiding: %b" caps.SupportsBraiding
        printfn "Supports measurement: %b" caps.SupportsMeasurement
        printfn "Supports F-moves: %b" caps.SupportsFMoves
        printfn "Supports error correction: %b" caps.SupportsErrorCorrection
        printfn ""

// ============================================================================
// LAYER 3: OPERATIONS - High-Level Quantum Operations
// ============================================================================
// This layer builds on backends to provide meaningful quantum operations
// like braiding sequences, qubit encoding, and measurement protocols.

module TopologicalCircuits =
    
    /// Encode a topological qubit using 4 sigma anyons
    /// This creates a qubit in the |0⟩ state (all fuse to vacuum)
    let encodeQubitZero (backend: TopologicalBackend.ITopologicalBackend) = 
        task {
            // Initialize 4 sigma anyons
            let! result = backend.Initialize AnyonSpecies.AnyonType.Ising 4
            return 
                match result with
                | Ok state -> state
                | Error e -> failwith $"Initialize failed: {e}"
        }
    
    /// Apply a braiding sequence to implement a quantum gate
    /// (Simplified example - real gates require specific braiding patterns)
    let applyBraidingGate 
        (backend: TopologicalBackend.ITopologicalBackend) 
        (state: TopologicalOperations.Superposition) =
        task {
            // Braid first pair of anyons
            let! result1 = backend.Braid 0 state
            let state1 = 
                match result1 with
                | Ok s -> s
                | Error e -> failwith $"Braid failed: {e}"
            
            // Braid second pair
            let! result2 = backend.Braid 2 state1
            let state2 = 
                match result2 with
                | Ok s -> s
                | Error e -> failwith $"Braid failed: {e}"
            
            // Braid first pair again
            let! result3 = backend.Braid 0 state2
            let state3 = 
                match result3 with
                | Ok s -> s
                | Error e -> failwith $"Braid failed: {e}"
            
            return state3
        }
    
    /// Demonstrate a simple quantum circuit
    let demonstrateQuantumCircuit () = task {
        printfn "=== Topological Quantum Circuit ==="
        
        let backend = TopologicalBackends.createIsingSimulator 10
        
        // Initialize qubit
        printfn "Initializing 4-anyon qubit..."
        let! qubit = encodeQubitZero backend
        printfn "Initial state created: %d fusion tree(s)" qubit.Terms.Length
        
        // Apply braiding operations
        printfn "Applying braiding sequence (quantum gate)..."
        let! evolved = applyBraidingGate backend qubit
        printfn "State after braiding: %d fusion tree(s)" evolved.Terms.Length
        
        // Measure
        printfn "Measuring fusion..."
        let! measureResult = backend.MeasureFusion 0 evolved
        let (outcome, collapsed, probability) = 
            match measureResult with
            | Ok result -> result
            | Error e -> failwith $"Measurement failed: {e}"
        printfn "Measurement outcome: %A (probability: %.4f)" outcome probability
        printfn ""
    }

// ============================================================================
// LAYER 4: ALGORITHMS - Domain-Specific Quantum Algorithms
// ============================================================================
// This layer implements algorithms specific to topological quantum computing.
// Note: These are DIFFERENT from gate-based algorithms (HHL, Shor's, etc.)

module TopologicalAlgorithms =
    
    /// Calculate Kauffman bracket invariant via topological measurement
    /// This is a topological-specific algorithm (not gate-based equivalent)
    let calculateKnotInvariant 
        (backend: TopologicalBackend.ITopologicalBackend)
        (braidingSequence: int list) =
        task {
            // Create anyon pairs
            let! initResult = backend.Initialize AnyonSpecies.AnyonType.Ising 6
            let initialState = 
                match initResult with
                | Ok s -> s
                | Error e -> failwith $"Initialize failed: {e}"
            
            // Apply braiding sequence to form knot
            let mutable state = initialState
            for index in braidingSequence do
                let! braidResult = backend.Braid index state
                state <- 
                    match braidResult with
                    | Ok s -> s
                    | Error e -> failwith $"Braid failed: {e}"
            
            // Bring anyons back together and measure
            // Probability of reannihilation ∝ |Kauffman invariant|²
            let! measureResult = backend.MeasureFusion 0 state
            let (outcome, _, probability) = 
                match measureResult with
                | Ok result -> result
                | Error e -> failwith $"Measurement failed: {e}"
            
            return (outcome, probability)
        }
    
    /// Demonstrate topological-specific algorithm
    let demonstrateKnotInvariantCalculation () = task {
        printfn "=== Topological Algorithm: Knot Invariant ==="
        
        let backend = TopologicalBackends.createIsingSimulator 10
        
        // Define a simple braiding pattern (trefoil knot)
        let braidingPattern = [0; 2; 0; 2; 0; 2]
        
        printfn "Calculating Kauffman invariant via braiding pattern: %A" braidingPattern
        
        let! (outcome, probability) = calculateKnotInvariant backend braidingPattern
        
        printfn "Knot invariant measurement:"
        printfn "  Fusion outcome: %A" outcome
        printfn "  Reannihilation probability: %.6f" probability
        printfn "  (Related to |Kauffman bracket|²)"
        printfn ""
    }

// ============================================================================
// LAYER 5: BUSINESS BUILDERS - User-Friendly DSL
// ============================================================================
// This layer provides computation expression builders for composing
// topological quantum programs in an idiomatic F# style.

module TopologicalBuilders =
    
    /// Computation expression builder for topological quantum programs
    type TopologicalProgramBuilder() =
        
        member _.Bind(operation, continuation) = task {
            let! result = operation
            return! continuation result
        }
        
        member _.Return(value) = task {
            return value
        }
        
        member _.ReturnFrom(operation) = operation
        
        member _.Zero() = task {
            return ()
        }
    
    /// Global instance of the builder
    let topological = TopologicalProgramBuilder()
    
    /// Example using the builder
    let exampleProgram (backend: TopologicalBackend.ITopologicalBackend) = topological {
        // Initialize
        let! initResult = backend.Initialize AnyonSpecies.AnyonType.Ising 4
        let initialState = 
            match initResult with
            | Ok s -> s
            | Error e -> failwith $"Initialize failed: {e}"
        
        // Apply braiding
        let! braidResult1 = backend.Braid 0 initialState
        let braided1 = 
            match braidResult1 with
            | Ok s -> s
            | Error e -> failwith $"Braid failed: {e}"
        
        let! braidResult2 = backend.Braid 2 braided1
        let braided2 = 
            match braidResult2 with
            | Ok s -> s
            | Error e -> failwith $"Braid failed: {e}"
        
        let! braidResult3 = backend.Braid 0 braided2
        let braided3 = 
            match braidResult3 with
            | Ok s -> s
            | Error e -> failwith $"Braid failed: {e}"
        
        // Measure
        let! measureResult = backend.MeasureFusion 0 braided3
        let (outcome, collapsed, prob) = 
            match measureResult with
            | Ok result -> result
            | Error e -> failwith $"Measurement failed: {e}"
        
        return (outcome, prob)
    }
    
    /// Demonstrate builder usage
    let demonstrateBuilder () = task {
        printfn "=== Builder Pattern (Idiomatic F#) ==="
        
        let backend = TopologicalBackends.createIsingSimulator 10
        
        let! (outcome, probability) = exampleProgram backend
        
        printfn "Program result:"
        printfn "  Outcome: %A" outcome
        printfn "  Probability: %.4f" probability
        printfn ""
    }

// ============================================================================
// LAYER 6: BUSINESS DOMAIN - Real-World Applications
// ============================================================================
// This layer maps business problems to topological quantum solutions.

module BusinessApplications =
    
    /// Quantum error detection using topological properties
    /// Business problem: Detect if a qubit has suffered an error
    let topologicalErrorDetection 
        (backend: TopologicalBackend.ITopologicalBackend)
        (qubitState: TopologicalOperations.Superposition) =
        task {
            // In topological QC, errors manifest as unwanted anyon pairs
            // Measure syndrome by checking fusion outcomes
            let! measureResult = backend.MeasureFusion 0 qubitState
            let (outcome, _, _) = 
                match measureResult with
                | Ok result -> result
                | Error e -> failwith $"Measurement failed: {e}"
            
            let errorDetected = 
                match outcome with
                | AnyonSpecies.Particle.Vacuum -> false  // No error
                | AnyonSpecies.Particle.Psi -> true      // Error detected!
                | _ -> false
            
            return errorDetected
        }
    
    /// Demonstrate business application
    let demonstrateErrorDetection () = task {
        printfn "=== Business Application: Error Detection ==="
        
        let backend = TopologicalBackends.createIsingSimulator 10
        
        // Create a qubit
        let! qubitResult = backend.Initialize AnyonSpecies.AnyonType.Ising 4
        let qubit = 
            match qubitResult with
            | Ok s -> s
            | Error e -> failwith $"Initialize failed: {e}"
        
        // Simulate some operations (which might introduce errors)
        let! evolveResult = backend.Braid 0 qubit
        let evolved = 
            match evolveResult with
            | Ok s -> s
            | Error e -> failwith $"Braid failed: {e}"
        
        // Check for errors
        let! hasError = topologicalErrorDetection backend evolved
        
        printfn "Error detection result: %s" 
            (if hasError then "ERROR DETECTED!" else "No errors")
        printfn ""
    }

// ============================================================================
// MAIN DEMONSTRATION
// ============================================================================

let runAllExamples () = task {
    printfn "╔══════════════════════════════════════════════════════════════╗"
    printfn "║  Topological Quantum Computing - Layered Architecture Demo  ║"
    printfn "╚══════════════════════════════════════════════════════════════╝"
    printfn ""
    
    // Layer 1: Core
    CoreTopologicalMath.demonstrateFusionRule ()
    
    // Layer 2: Backends
    TopologicalBackends.demonstrateBackendCapabilities ()
    
    // Layer 3: Operations
    do! TopologicalCircuits.demonstrateQuantumCircuit ()
    
    // Layer 4: Algorithms
    do! TopologicalAlgorithms.demonstrateKnotInvariantCalculation ()
    
    // Layer 5: Builders
    do! TopologicalBuilders.demonstrateBuilder ()
    
    // Layer 6: Business
    do! BusinessApplications.demonstrateErrorDetection ()
    
    printfn "╔══════════════════════════════════════════════════════════════╗"
    printfn "║  All examples completed successfully!                       ║"
    printfn "╚══════════════════════════════════════════════════════════════╝"
}

// Run the demonstration
runAllExamples() |> Async.AwaitTask |> Async.RunSynchronously
