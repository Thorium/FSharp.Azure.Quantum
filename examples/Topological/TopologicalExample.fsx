// ============================================================================
// Topological Quantum Computing Example
// Demonstrates the layered architecture with idiomatic F#
// ============================================================================

#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

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
// execute topological quantum operations. Uses unified IQuantumBackend.

module TopologicalBackends =
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    
    /// Create a local simulator backend for Ising anyons
    let createIsingSimulator maxAnyons =
        TopologicalUnifiedBackendFactory.createIsing maxAnyons
    
    /// Create a local simulator backend for Fibonacci anyons
    let createFibonacciSimulator maxAnyons =
        TopologicalUnifiedBackendFactory.createFibonacci maxAnyons
    
    /// Demonstrate backend capabilities
    let demonstrateBackendCapabilities () =
        printfn "=== Backend Capabilities ==="
        
        let backend = createIsingSimulator 10
        // Unified backend doesn't expose raw Capabilities object in the same way,
        // but we can query it.
        printfn "Backend Name: %s" backend.Name
        printfn "Native State Type: %A" backend.NativeStateType
        
        // We can check supported operations
        let supportsBraid = backend.SupportsOperation (QuantumOperation.Braid 0)
        let supportsMeasure = backend.SupportsOperation (QuantumOperation.Measure 0)
        
        printfn "Supports braiding: %b" supportsBraid
        printfn "Supports measurement: %b" supportsMeasure
        printfn ""

// ============================================================================
// LAYER 3: OPERATIONS - High-Level Quantum Operations
// ============================================================================
// This layer builds on backends to provide meaningful quantum operations.
// We use the unified QuantumOperation and QuantumState types.

module TopologicalCircuits =
    open FSharp.Azure.Quantum.Core
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    
    /// Encode a topological qubit using 4 sigma anyons
    /// This creates a qubit in the |0⟩ state (all fuse to vacuum)
    let encodeQubitZero (backend: IQuantumBackend) = 
        task {
            // Initialize 4 sigma anyons
            // Note: InitializeState returns Result<QuantumState, QuantumError>
            let result = backend.InitializeState 4
            return 
                match result with
                | Ok state -> state
                | Error e -> failwith $"Initialize failed: {e}"
        }
    
    /// Apply a braiding sequence to implement a quantum gate
    let applyBraidingGate 
        (backend: IQuantumBackend) 
        (state: QuantumState) =
        task {
            // Braid first pair of anyons
            // backend.ApplyOperation returns Result<QuantumState, QuantumError>
            let result1 = backend.ApplyOperation (QuantumOperation.Braid 0) state
            let state1 = 
                match result1 with
                | Ok s -> s
                | Error e -> failwith $"Braid failed: {e}"
            
            // Braid second pair
            let result2 = backend.ApplyOperation (QuantumOperation.Braid 2) state1
            let state2 = 
                match result2 with
                | Ok s -> s
                | Error e -> failwith $"Braid failed: {e}"
            
            // Braid first pair again
            let result3 = backend.ApplyOperation (QuantumOperation.Braid 0) state2
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
        
        // Inspect state (cast to specific type for details)
        match qubit with
        | QuantumState.FusionSuperposition fs ->
             // Need to cast to underlying type or access known properties
             // Since FusionSuperposition wraps ITopologicalSuperposition, we need to inspect the interface
             printfn "Initial state created: %d logical qubits" fs.LogicalQubits
        | _ -> printfn "State created (abstract)"
        
        // Apply braiding operations
        printfn "Applying braiding sequence (quantum gate)..."
        let! evolved = applyBraidingGate backend qubit
        
        match evolved with
        | QuantumState.FusionSuperposition fs ->
             printfn "State after braiding: %d logical qubits" fs.LogicalQubits
        | _ -> ()
        
        // Measure
        // For manual measurement with IQuantumBackend in this example, 
        // we'll use the TopologicalOperations helper directly to get the outcome,
        // as ApplyOperation(Measure) updates the state but returns void/state.
        printfn "Measuring fusion..."
        
        match evolved with
        | QuantumState.FusionSuperposition fs ->
            // Unwrap to native type
            match TopologicalOperations.fromInterface fs with
            | Some nativeState ->
                 // We measure the first term (assuming pure state for this simple demo)
                 let singleState = snd (List.head nativeState.Terms)
                 match TopologicalOperations.measureFusion 0 singleState with
                 | Ok outcomes ->
                     let (prob, result) = List.head outcomes
                     match result.ClassicalOutcome with
                     | Some outcome ->
                         printfn "Measurement outcome: %A (probability: %.4f)" outcome prob
                     | None -> printfn "Measurement collapsed state but no outcome"
                 | Error e -> printfn "Measurement failed: %s" e.Message
            | None -> printfn "Could not unwrap state"
        | _ -> printfn "Cannot measure abstract state manually in this demo"
        
        printfn ""
    }

// ============================================================================
// LAYER 4: ALGORITHMS - Domain-Specific Quantum Algorithms
// ============================================================================
// This layer implements algorithms specific to topological quantum computing.

module TopologicalAlgorithms =
    open FSharp.Azure.Quantum.Core
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    
    /// Calculate Kauffman bracket invariant via topological measurement
    let calculateKnotInvariant 
        (backend: IQuantumBackend)
        (braidingSequence: int list) =
        task {
            // Create anyon pairs
            let initResult = backend.InitializeState 6
            let initialState = 
                match initResult with
                | Ok s -> s
                | Error e -> failwith $"Initialize failed: {e}"
            
            // Apply braiding sequence to form knot
            let mutable state = initialState
            for index in braidingSequence do
                let braidResult = backend.ApplyOperation (QuantumOperation.Braid index) state
                state <- 
                    match braidResult with
                    | Ok s -> s
                    | Error e -> failwith $"Braid failed: {e}"
            
            // Measure manually
            match state with
            | QuantumState.FusionSuperposition fs ->
                match TopologicalOperations.fromInterface fs with
                | Some nativeState ->
                     let singleState = snd (List.head nativeState.Terms)
                     match TopologicalOperations.measureFusion 0 singleState with
                     | Ok outcomes ->
                         let (prob, result) = List.head outcomes
                         match result.ClassicalOutcome with
                         | Some outcome -> return (outcome, prob)
                         | None -> return failwith "No outcome"
                     | Error e -> return failwith e.Message
                | None -> return failwith "Invalid state"
            | _ -> return failwith "Invalid state type"
        }
    
    /// Demonstrate topological-specific algorithm
    let demonstrateKnotInvariantCalculation () = task {
        printfn "=== Topological Algorithm: Knot Invariant ==="
        
        // Increase capacity to support complex braids
        let backend = TopologicalBackends.createIsingSimulator 20
        
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
// WE USE THE LIBRARY'S BUILDER HERE.

module TopologicalBuilders =
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.Core
    open System.Threading.Tasks
    
    // We define a local helper to execute the builder programs
    // because the direct usage of the library builder in F# scripts 
    // sometimes has type inference issues with overloads.
    
    /// Example using the builder
    let exampleProgram (backend: IQuantumBackend) = topological backend {
        // Initialize
        do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
        
        // Apply braiding
        do! TopologicalBuilder.braid 0
        do! TopologicalBuilder.braid 2
        do! TopologicalBuilder.braid 0
        
        // Measure
        // Explicit type annotation helps resolution
        let! (outcome: AnyonSpecies.Particle) = TopologicalBuilder.measure 0
        
        return outcome
    }
    
    /// Demonstrate builder usage
    let demonstrateBuilder () = task {
        printfn "=== Builder Pattern (Idiomatic F#) ==="
        
        let backend = TopologicalBackends.createIsingSimulator 10
        
        // IMPORTANT: The builder returns a function (Context -> Task<...>)
        // We must execute it using the runner.
        let program = exampleProgram backend
        let! result = TopologicalBuilder.execute backend program
        
        match result with
        | Ok (outcome) ->
            printfn "Program result:"
            printfn "  Outcome: %A" outcome
            
        | Error e ->
            printfn "Program failed: %A" e
            
        printfn ""
    }

// ============================================================================
// LAYER 6: BUSINESS DOMAIN - Real-World Applications
// ============================================================================
// This layer maps business problems to topological quantum solutions.

module BusinessApplications =
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.Core
    
    /// Quantum error detection using topological properties
    let topologicalErrorDetection 
        (backend: IQuantumBackend) = 
        topological backend {
            // Create a qubit
            do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
            
            // Simulate operations (braid)
            do! TopologicalBuilder.braid 0
            
            // Measure syndrome
            let! (outcome: AnyonSpecies.Particle) = TopologicalBuilder.measure 0
            
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
        
        // Execute the program
        let program = topologicalErrorDetection backend
        let! result = TopologicalBuilder.execute backend program
        
        match result with
        | Ok (hasError) ->
            printfn "Error detection result: %s" 
                (if hasError then "ERROR DETECTED!" else "No errors")
        | Error e ->
            printfn "Error: %A" e
            
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
