#!/usr/bin/env dotnet fsi
// ============================================================================
// Quantum Circuit Visualization - Easy Integration Example
// ============================================================================
// 
// This example demonstrates how visualization is now easily integrated
// with CircuitBuilder using simple extension methods.
//
// KEY FEATURES:
// - Call .ToASCII() directly on any Circuit
// - Call .ToMermaid() directly on any Circuit
// - No manual conversion needed!
//
// ============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Visualization

// ============================================================================
// EXAMPLE 1: Bell State Visualization
// ============================================================================

printfn "=== Example 1: Bell State ===" 
printfn ""

let bellState = circuit {
    qubits 2
    H 0          // Hadamard on qubit 0
    CNOT (0, 1)  // Entangle qubits
}

printfn "ASCII Visualization:"
printfn "--------------------"
printfn "%s" (bellState.ToASCII())
printfn ""

printfn "Mermaid Sequence Diagram:"
printfn "-------------------------"
printfn "%s" (bellState.ToMermaid())
printfn ""


// ============================================================================
// EXAMPLE 2: Quantum Fourier Transform (3 qubits)
// ============================================================================

printfn "=== Example 2: QFT-3 ===" 
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

printfn "ASCII Visualization:"
printfn "--------------------"
printfn "%s" (qft3.ToASCII())
printfn ""

printfn "Mermaid Sequence Diagram:"
printfn "-------------------------"
printfn "%s" (qft3.ToMermaid())
printfn ""


// ============================================================================
// EXAMPLE 3: Simple Multi-Gate Circuit
// ============================================================================

printfn "=== Example 3: Rotation Gates ===" 
printfn ""

let rotations = circuit {
    qubits 3
    
    // Various rotation gates
    RX (0, Math.PI / 4.0)
    RY (1, Math.PI / 3.0)
    RZ (2, Math.PI / 2.0)
    
    // Controlled rotations
    CRX (0, 1, Math.PI / 6.0)
    CRY (1, 2, Math.PI / 8.0)
    
    // Measurement
    Measure 0
    Measure 1
    Measure 2
}

printfn "ASCII Visualization:"
printfn "--------------------"
printfn "%s" (rotations.ToASCII())
printfn ""


// ============================================================================
// SUMMARY
// ============================================================================

printfn ""
printfn "=== Easy Visualization Integration ==="
printfn ""
printfn "✓ Call .ToASCII() on any Circuit"
printfn "✓ Call .ToMermaid() on any Circuit"
printfn "✓ No manual conversion needed"
printfn "✓ Works with all CircuitBuilder gates"
printfn ""
printfn "Example usage:"
printfn "  let myCircuit = circuit { ... }"
printfn "  printfn \"%s\" (myCircuit.ToASCII())     // Terminal-friendly"
printfn "  printfn \"%s\" (myCircuit.ToMermaid())   // GitHub/docs"
printfn ""
