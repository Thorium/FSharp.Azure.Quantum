#!/usr/bin/env dotnet fsi

// ============================================================================
// Quantum Circuit Builder - Computation Expression Examples
// ============================================================================
// 
// This example demonstrates the new computation expression (CE) syntax for
// building quantum circuits declaratively with support for loops and
// natural gate composition.
//
// KEY FEATURES:
// - Declarative circuit construction with `circuit { }` syntax
// - Support for `for` loops to apply gates to multiple qubits
// - Automatic circuit validation on construction
// - Clean, readable quantum algorithm implementations
//
// ============================================================================

#r "nuget: Microsoft.Quantum.Providers.Core"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.CircuitBuilder

// ============================================================================
// EXAMPLE 1: Bell State (Quantum Entanglement)
// ============================================================================
// Creates a maximally entangled pair of qubits:
// |Φ⁺⟩ = (|00⟩ + |11⟩) / √2

printfn "=== Example 1: Bell State ==="
printfn ""

let bellState = circuit {
    qubits 2
    H 0          // Apply Hadamard to qubit 0: creates superposition
    CNOT (0, 1)  // Entangle qubit 0 and 1
}

printfn "Bell State Circuit:"
printfn "  Qubits: %d" bellState.QubitCount
printfn "  Gates:  %d" (List.length bellState.Gates)
printfn "  Depth:  2 (H followed by CNOT)"
printfn ""

// Export to OpenQASM for execution on quantum hardware
let bellQASM = toOpenQASM bellState
printfn "OpenQASM Output:"
printfn "%s" bellQASM
printfn ""


// ============================================================================
// EXAMPLE 2: GHZ State (Multi-Qubit Entanglement with Loop)
// ============================================================================
// Creates an n-qubit GHZ state: |GHZ⟩ = (|00...0⟩ + |11...1⟩) / √2
// Demonstrates the power of `for` loops in circuit construction

printfn "=== Example 2: GHZ State (5 qubits) ==="
printfn ""

let ghzState = circuit {
    qubits 5
    H 0  // Hadamard on first qubit
    
    // Chain of CNOTs to propagate entanglement
    // NOTE: Custom operations don't work inside for loops (F# limitation)
    // Use yield! with singleGate() helper for loops
    for i in [0..3] do
        yield! singleGate (Gate.CNOT (i, i+1))
}

printfn "GHZ State Circuit:"
printfn "  Qubits: %d" ghzState.QubitCount
printfn "  Gates:  %d (1 H + 4 CNOTs)" (List.length ghzState.Gates)
printfn ""


// ============================================================================
// EXAMPLE 3: Quantum Fourier Transform (QFT) - 3 Qubits
// ============================================================================
// Implements the Quantum Fourier Transform, a key subroutine in
// many quantum algorithms (Shor's algorithm, phase estimation, etc.)

printfn "=== Example 3: Quantum Fourier Transform (3 qubits) ==="
printfn ""

let qft3 = circuit {
    qubits 3
    
    // QFT on qubit 0
    H 0
    CP (1, 0, Math.PI / 2.0)
    CP (2, 0, Math.PI / 4.0)
    
    // QFT on qubit 1
    H 1
    CP (2, 1, Math.PI / 2.0)
    
    // QFT on qubit 2
    H 2
    
    // SWAP for bit-reversal
    SWAP (0, 2)
}

printfn "QFT-3 Circuit:"
printfn "  Qubits: %d" qft3.QubitCount
printfn "  Gates:  %d" (List.length qft3.Gates)
printfn "  Structure: H + CP gates + final SWAP"
printfn ""


// ============================================================================
// EXAMPLE 4: Superposition of All Qubits (Loop Demonstration)
// ============================================================================
// Apply Hadamard to every qubit to create uniform superposition
// |ψ⟩ = (1/√2ⁿ) Σ|x⟩ for all n-bit strings x

printfn "=== Example 4: Uniform Superposition (8 qubits) ==="
printfn ""

let n = 8
let superposition = circuit {
    qubits n
    
    // Apply Hadamard to all qubits using a for loop
    // Use yield! with singleGate() helper for loops
    for q in [0..n-1] do
        yield! singleGate (Gate.H q)
}

printfn "Superposition Circuit:"
printfn "  Qubits: %d" superposition.QubitCount
printfn "  Gates:  %d Hadamards" (List.length superposition.Gates)
printfn "  Result: Uniform superposition over 2^%d = %d basis states" n (pown 2 n)
printfn ""


// ============================================================================
// EXAMPLE 5: Quantum Phase Kickback (Controlled Operations)
// ============================================================================
// Demonstrates phase kickback mechanism used in many quantum algorithms

printfn "=== Example 5: Phase Kickback Demo ==="
printfn ""

let phaseKickback = circuit {
    qubits 2
    
    // Prepare control qubit in superposition
    H 0
    
    // Prepare target qubit in |1⟩ (eigenstate of X)
    X 1
    H 1
    
    // Controlled-Z creates phase kickback
    CZ (0, 1)
    
    // Measure effect on control qubit
    H 0
}

printfn "Phase Kickback Circuit:"
printfn "  Qubits: %d" phaseKickback.QubitCount
printfn "  Gates:  %d" (List.length phaseKickback.Gates)
printfn "  Purpose: Demonstrates phase kickback mechanism"
printfn ""


// ============================================================================
// EXAMPLE 6: Toffoli Gate Usage (CCX - 3-Qubit Gate)
// ============================================================================
// Toffoli (CCNOT) is universal for classical reversible computation

printfn "=== Example 6: Toffoli Gate (CCX) ==="
printfn ""

let toffoliDemo = circuit {
    qubits 3
    
    // Prepare control qubits in |11⟩ state
    X 0
    X 1
    
    // Toffoli flips target if both controls are |1⟩
    CCX (0, 1, 2)
}

printfn "Toffoli Circuit:"
printfn "  Qubits: %d" toffoliDemo.QubitCount
printfn "  Gates:  %d" (List.length toffoliDemo.Gates)
printfn "  Effect: Target qubit flipped only if both controls are |1⟩"
printfn ""


// ============================================================================
// EXAMPLE 7: Circuit Optimization
// ============================================================================
// The circuit builder includes automatic optimization to reduce gate count

printfn "=== Example 7: Circuit Optimization ==="
printfn ""

let unoptimized = circuit {
    qubits 2
    H 0
    H 0  // Double H cancels out
    X 1
    X 1  // Double X cancels out
    S 0
    SDG 0  // S followed by S† cancels out
}

let optimized = optimize unoptimized

printfn "Before optimization: %d gates" (List.length unoptimized.Gates)
printfn "After optimization:  %d gates" (List.length optimized.Gates)
printfn ""
printfn "Optimization removes inverse gate pairs (H-H, X-X, S-SDG)"
printfn ""


// ============================================================================
// EXAMPLE 8: Error Handling - Invalid Circuit Detection
// ============================================================================
// The CE builder validates circuits automatically

printfn "=== Example 8: Automatic Validation ==="
printfn ""

try
    // This will fail: qubit index out of bounds
    let invalid = circuit {
        qubits 2
        H 5  // ERROR: Only 2 qubits (indices 0-1), but trying to use qubit 5
    }
    printfn "Should not reach here!"
with ex ->
    printfn "✓ Caught invalid circuit error (as expected):"
    printfn "  %s" ex.Message
    printfn ""


// ============================================================================
// SUMMARY
// ============================================================================

printfn ""
printfn "=== Summary of New CE Features ==="
printfn ""
printfn "✓ Declarative circuit construction with circuit { } syntax"
printfn "✓ Support for 'for' loops to apply gates to multiple qubits"
printfn "✓ Automatic validation prevents invalid circuits"
printfn "✓ All standard gates available as custom operations"
printfn "✓ Clean, readable quantum algorithm implementations"
printfn "✓ Idiomatic F# - uses Seq.fold and functional composition"
printfn ""
printfn "This makes quantum circuit construction as natural as:"
printfn "  - async { } for asynchronous workflows"
printfn "  - seq { } for sequences"
printfn "  - query { } for LINQ queries"
printfn ""
