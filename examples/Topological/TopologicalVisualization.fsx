// Topological Quantum Computing Visualization Example
// Demonstrates visualization of fusion trees and quantum superpositions

#r "nuget: FSharp.Azure.Quantum"
#load "../src/FSharp.Azure.Quantum.Topological/TopologicalError.fs"
#load "../src/FSharp.Azure.Quantum.Topological/AnyonSpecies.fs"
#load "../src/FSharp.Azure.Quantum.Topological/FusionRules.fs"
#load "../src/FSharp.Azure.Quantum.Topological/BraidingOperators.fs"
#load "../src/FSharp.Azure.Quantum.Topological/FusionTree.fs"
#load "../src/FSharp.Azure.Quantum.Topological/TopologicalOperations.fs"
#load "../src/FSharp.Azure.Quantum.Topological/Visualization.fs"

open FSharp.Azure.Quantum.Topological
open System.Numerics

printfn "=== TOPOLOGICAL QUANTUM COMPUTING VISUALIZATION ==="
printfn ""

// ============================================================================
// EXAMPLE 1: Fusion Tree Visualization (Topological Qubit)
// ============================================================================

printfn "EXAMPLE 1: Topological Qubit Encoding"
printfn "======================================"
printfn ""

// A topological qubit is encoded in the fusion outcome of 2 sigma anyons:
// |0⟩ ≡ σ × σ → 1 (vacuum)
// |1⟩ ≡ σ × σ → ψ (fermion)

let sigma = FusionTree.leaf AnyonSpecies.Particle.Sigma

// Computational basis state |0⟩
let qubitZero = 
    FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
    |> fun tree -> FusionTree.create tree AnyonSpecies.AnyonType.Ising

printfn "Qubit |0⟩:"
printfn "%s" (qubitZero.ToASCII())
printfn ""
printfn "Mermaid diagram:"
printfn "%s" (qubitZero.ToMermaid())
printfn ""

// Computational basis state |1⟩
let qubitOne = 
    FusionTree.fuse sigma sigma AnyonSpecies.Particle.Psi
    |> fun tree -> FusionTree.create tree AnyonSpecies.AnyonType.Ising

printfn "Qubit |1⟩:"
printfn "%s" (qubitOne.ToASCII())
printfn ""

// ============================================================================
// EXAMPLE 2: Complex Fusion Tree (4 Anyons)
// ============================================================================

printfn "EXAMPLE 2: Four Sigma Anyons Fusion Tree"
printfn "=========================================="
printfn ""

// Create: ((σ × σ → 1) × (σ × σ → 1)) → 1
let leftPair = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
let rightPair = FusionTree.fuse sigma sigma AnyonSpecies.Particle.Vacuum
let fourSigmaTree = 
    FusionTree.fuse leftPair rightPair AnyonSpecies.Particle.Vacuum
    |> fun tree -> FusionTree.create tree AnyonSpecies.AnyonType.Ising

printfn "%s" (fourSigmaTree.ToASCII())
printfn ""
printfn "Mermaid diagram:"
printfn "%s" (fourSigmaTree.ToMermaid())
printfn ""

// ============================================================================
// EXAMPLE 3: Quantum Superposition Visualization
// ============================================================================

printfn "EXAMPLE 3: Quantum Superposition State"
printfn "======================================="
printfn ""

// Create a uniform superposition of |0⟩ and |1⟩: (|0⟩ + |1⟩)/√2
let bellState = TopologicalOperations.uniform [qubitZero; qubitOne] AnyonSpecies.AnyonType.Ising

printfn "%s" (bellState.ToASCII())
printfn ""
printfn "Mermaid diagram:"
printfn "%s" (bellState.ToMermaid())
printfn ""

// ============================================================================
// EXAMPLE 4: Fibonacci Anyons
// ============================================================================

printfn "EXAMPLE 4: Fibonacci Anyon Fusion Tree"
printfn "======================================="
printfn ""

// Fibonacci anyons: τ × τ = 1 + τ (Fibonacci recurrence!)
let tau = FusionTree.leaf AnyonSpecies.Particle.Tau

// Create τ × τ → τ
let fibTree = 
    FusionTree.fuse tau tau AnyonSpecies.Particle.Tau
    |> fun tree -> FusionTree.create tree AnyonSpecies.AnyonType.Fibonacci

printfn "%s" (fibTree.ToASCII())
printfn ""
printfn "Mermaid diagram:"
printfn "%s" (fibTree.ToMermaid())
printfn ""

// ============================================================================
// EXAMPLE 5: Superposition After Braiding
// ============================================================================

printfn "EXAMPLE 5: Superposition After Braiding Operation"
printfn "=================================================="
printfn ""

// Start with pure |0⟩ state
let initialState = TopologicalOperations.pureState qubitZero

// Apply braiding operation (braid anyons at positions 0 and 1)
match TopologicalOperations.braidSuperposition 0 initialState with
| Ok braidedState ->
    printfn "After braiding anyon 0 and 1:"
    printfn "%s" (braidedState.ToASCII())
    printfn ""
    printfn "Mermaid diagram:"
    printfn "%s" (braidedState.ToMermaid())
| Error err ->
    printfn "Error during braiding: %s" err.Message

printfn ""
printfn "=== VISUALIZATION EXAMPLES COMPLETE ==="
printfn ""
printfn "Key Insights:"
printfn "1. Fusion trees encode quantum states via anyon fusion outcomes"
printfn "2. Different fusion channels = orthogonal quantum states"
printfn "3. Superpositions combine multiple fusion tree basis states"
printfn "4. Braiding operations transform quantum amplitudes topologically"
printfn "5. Mermaid diagrams provide visual understanding of topological structures"
