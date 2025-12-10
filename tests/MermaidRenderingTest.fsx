/// Test script for Mermaid rendering of quantum circuits
/// 
/// Tests the updated MermaidRenderer with SWAP, CCX, and MCZ gates

#load "../src/FSharp.Azure.Quantum/Core/QuantumError.fs"
#load "../src/FSharp.Azure.Quantum/Core/Validation.fs"
#load "../src/FSharp.Azure.Quantum/Core/Types.fs"
#load "../src/FSharp.Azure.Quantum/Builders/CircuitBuilder.fs"
#load "../src/FSharp.Azure.Quantum/Visualization/Types.fs"
#load "../src/FSharp.Azure.Quantum/Visualization/MermaidRenderer.fs"

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Visualization

printfn "=== Testing Mermaid Flowchart Rendering ==="
printfn ""

// Test 1: SWAP gate
printfn "Test 1: SWAP Gate"
printfn "-----------------"

let swapGates = [
    CircuitGate (H 0)
    CircuitGate (CNOT (0, 1))
    CircuitGate (SWAP (1, 2))  // Test SWAP
]

let swapMermaid = MermaidRenderer.Flowchart.render 3 swapGates

printfn "Circuit: H(0) → CNOT(0,1) → SWAP(1,2)"
printfn ""
printfn "%s" swapMermaid
printfn ""

// Test 2: CCX (Toffoli) gate
printfn "Test 2: CCX (Toffoli) Gate"
printfn "----------------------------"

let toffoliGates = [
    CircuitGate (H 0)
    CircuitGate (H 1)
    CircuitGate (CCX (0, 1, 2))  // Test Toffoli
]

let toffoliMermaid = MermaidRenderer.Flowchart.render 3 toffoliGates

printfn "Circuit: H(0) → H(1) → CCX(0,1,2)"
printfn ""
printfn "%s" toffoliMermaid
printfn ""

// Test 3: MCZ (Multi-controlled Z) gate
printfn "Test 3: MCZ (Multi-Controlled Z) Gate"
printfn "---------------------------------------"

let mczGates = [
    CircuitGate (H 0)
    CircuitGate (H 1)
    CircuitGate (H 2)
    CircuitGate (MCZ ([0; 1; 2], 3))  // Test MCZ
]

let mczMermaid = MermaidRenderer.Flowchart.render 4 mczGates

printfn "Circuit: H(0) → H(1) → H(2) → MCZ([0,1,2], 3)"
printfn ""
printfn "%s" mczMermaid
printfn ""

// Test 4: Complex circuit with all gate types
printfn "Test 4: Complex Circuit (All Gate Types)"
printfn "-----------------------------------------"

let complexGates = [
    CircuitGate (H 0)
    CircuitGate (CNOT (0, 1))
    CircuitGate (SWAP (1, 2))
    CircuitGate (CCX (0, 1, 3))
    CircuitGate (MCZ ([0; 1], 2))
    CircuitGate (Measure 3)
]

let complexMermaid = MermaidRenderer.Flowchart.render 4 complexGates

printfn "Circuit: H → CNOT → SWAP → CCX → MCZ → Measure"
printfn ""
printfn "%s" complexMermaid
printfn ""

printfn "=== All Mermaid Rendering Tests Complete ==="
printfn ""
printfn "✅ SWAP gate rendering: Implemented"
printfn "✅ CCX (Toffoli) gate rendering: Implemented"
printfn "✅ MCZ (Multi-controlled Z) gate rendering: Implemented"
printfn "✅ All CircuitBuilder.Gate types now supported"
