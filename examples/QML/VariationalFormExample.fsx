/// Quantum Machine Learning - Variational Form (Ansatz) Example
///
/// Demonstrates different variational form architectures for
/// parameterized quantum circuits

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"


open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.MachineLearning.VariationalForms
open FSharp.Azure.Quantum.CircuitBuilder

printfn "=========================================="
printfn "QML Variational Forms - Circuit Generation"
printfn "=========================================="
printfn ""

let numQubits = 4

// ============================================================================
// Example 1: Real Amplitudes Ansatz
// ============================================================================

printfn "Example 1: Real Amplitudes (depth=1)"
printfn "------------------------------------------------------------"
printfn "Strategy: Ry rotations + CZ entanglement"
printfn ""

let raDepth1 = 1
let raParams1 = randomParameters (RealAmplitudes raDepth1) numQubits (Some 42)

printfn "Parameters needed: %d (numQubits × depth)" raParams1.Length
printfn "Parameters: %A" (raParams1 |> Array.map (fun p -> Math.Round(p, 3)))
printfn ""

match buildVariationalForm (RealAmplitudes raDepth1) raParams1 numQubits with
| Ok circuit ->
    printfn "✓ Circuit generated successfully"
    printfn "  Qubits: %d" circuit.QubitCount
    printfn "  Gates: %d" (gateCount circuit)
    
    // Count gate types
    let gates = getGates circuit
    let ryCount = gates |> List.filter (function RY _ -> true | _ -> false) |> List.length
    let czCount = gates |> List.filter (function CZ _ -> true | _ -> false) |> List.length
    
    printfn "  - Ry rotations: %d" ryCount
    printfn "  - CZ gates: %d" czCount
    printfn ""
| Error e ->
    printfn "✗ Error: %s" e
    printfn ""

// ============================================================================
// Example 2: Real Amplitudes (Depth 2)
// ============================================================================

printfn "Example 2: Real Amplitudes (depth=2)"
printfn "------------------------------------------------------------"
printfn "Strategy: Two layers of Ry + CZ"
printfn ""

let raDepth2 = 2
let raParams2 = randomParameters (RealAmplitudes raDepth2) numQubits (Some 42)

printfn "Parameters needed: %d (numQubits × depth)" raParams2.Length
printfn ""

match buildVariationalForm (RealAmplitudes raDepth2) raParams2 numQubits with
| Ok circuit ->
    printfn "✓ Circuit generated successfully"
    printfn "  Qubits: %d" circuit.QubitCount
    printfn "  Gates: %d" (gateCount circuit)
    
    let gates = getGates circuit
    let ryCount = gates |> List.filter (function RY _ -> true | _ -> false) |> List.length
    let czCount = gates |> List.filter (function CZ _ -> true | _ -> false) |> List.length
    
    printfn "  - Ry rotations: %d" ryCount
    printfn "  - CZ gates: %d" czCount
    printfn "  More layers = more expressiveness"
    printfn ""
| Error e ->
    printfn "✗ Error: %s" e
    printfn ""

// ============================================================================
// Example 3: TwoLocal Ansatz
// ============================================================================

printfn "Example 3: TwoLocal (Ry + CZ, depth=1)"
printfn "------------------------------------------------------------"
printfn "Strategy: Flexible rotation + entanglement"
printfn ""

let tlDepth = 1
let tlParams = randomParameters (TwoLocal("Ry", "CZ", tlDepth)) numQubits (Some 42)

printfn "Parameters needed: %d" tlParams.Length
printfn ""

match buildVariationalForm (TwoLocal("Ry", "CZ", tlDepth)) tlParams numQubits with
| Ok circuit ->
    printfn "✓ Circuit generated successfully"
    printfn "  Qubits: %d" circuit.QubitCount
    printfn "  Gates: %d" (gateCount circuit)
    
    let gates = getGates circuit
    let ryCount = gates |> List.filter (function RY _ -> true | _ -> false) |> List.length
    let czCount = gates |> List.filter (function CZ _ -> true | _ -> false) |> List.length
    
    printfn "  - Ry rotations: %d" ryCount
    printfn "  - CZ gates: %d" czCount
    printfn ""
| Error e ->
    printfn "✗ Error: %s" e
    printfn ""

// ============================================================================
// Example 4: TwoLocal with Rx + CNOT
// ============================================================================

printfn "Example 4: TwoLocal (Rx + CNOT, depth=1)"
printfn "------------------------------------------------------------"
printfn "Strategy: Different rotation and entanglement gates"
printfn ""

let tlParams2 = randomParameters (TwoLocal("Rx", "CNOT", 1)) numQubits (Some 42)

match buildVariationalForm (TwoLocal("Rx", "CNOT", 1)) tlParams2 numQubits with
| Ok circuit ->
    printfn "✓ Circuit generated successfully"
    printfn "  Qubits: %d" circuit.QubitCount
    printfn "  Gates: %d" (gateCount circuit)
    
    let gates = getGates circuit
    let rxCount = gates |> List.filter (function RX _ -> true | _ -> false) |> List.length
    let cnotCount = gates |> List.filter (function CNOT _ -> true | _ -> false) |> List.length
    
    printfn "  - Rx rotations: %d" rxCount
    printfn "  - CNOT gates: %d" cnotCount
    printfn ""
| Error e ->
    printfn "✗ Error: %s" e
    printfn ""

// ============================================================================
// Example 5: EfficientSU2 Ansatz
// ============================================================================

printfn "Example 5: EfficientSU2 (depth=1)"
printfn "------------------------------------------------------------"
printfn "Strategy: Ry + Rz rotations (full SU(2) coverage)"
printfn ""

let su2Depth = 1
let su2Params = randomParameters (EfficientSU2 su2Depth) numQubits (Some 42)

printfn "Parameters needed: %d (2 × numQubits × depth)" su2Params.Length
printfn "Note: Requires 2× parameters (Ry AND Rz per qubit)"
printfn ""

match buildVariationalForm (EfficientSU2 su2Depth) su2Params numQubits with
| Ok circuit ->
    printfn "✓ Circuit generated successfully"
    printfn "  Qubits: %d" circuit.QubitCount
    printfn "  Gates: %d" (gateCount circuit)
    
    let gates = getGates circuit
    let ryCount = gates |> List.filter (function RY _ -> true | _ -> false) |> List.length
    let rzCount = gates |> List.filter (function RZ _ -> true | _ -> false) |> List.length
    let czCount = gates |> List.filter (function CZ _ -> true | _ -> false) |> List.length
    
    printfn "  - Ry rotations: %d" ryCount
    printfn "  - Rz rotations: %d" rzCount
    printfn "  - CZ gates: %d" czCount
    printfn ""
| Error e ->
    printfn "✗ Error: %s" e
    printfn ""

// ============================================================================
// Example 6: Ansatz Comparison
// ============================================================================

printfn "=========================================="
printfn "Ansatz Comparison (4 qubits, depth=1)"
printfn "=========================================="
printfn ""

let ansatze = [
    ("RealAmplitudes", RealAmplitudes 1)
    ("TwoLocal(Ry+CZ)", TwoLocal("Ry", "CZ", 1))
    ("TwoLocal(Rx+CNOT)", TwoLocal("Rx", "CNOT", 1))
    ("EfficientSU2", EfficientSU2 1)
]

printfn "Ansatz               | Params | Gates | Rot Gates | Entangle"
printfn "---------------------+--------+-------+-----------+----------"

for (name, ansatz) in ansatze do
    let params = randomParameters ansatz numQubits (Some 42)
    match buildVariationalForm ansatz params numQubits with
    | Ok circuit ->
        let gates = getGates circuit
        let rotCount = 
            gates 
            |> List.filter (function RY _ | RZ _ | RX _ -> true | _ -> false) 
            |> List.length
        let entCount = 
            gates 
            |> List.filter (function CZ _ | CNOT _ -> true | _ -> false) 
            |> List.length
        
        printfn "%-20s | %6d | %5d | %9d | %8d" 
            name params.Length (gateCount circuit) rotCount entCount
    | Error _ ->
        printfn "%-20s | %6s | %5s | %9s | %8s" name "Error" "Error" "Error" "Error"

printfn ""

// ============================================================================
// Example 7: Parameter Initialization Strategies
// ============================================================================

printfn "=========================================="
printfn "Parameter Initialization Strategies"
printfn "=========================================="
printfn ""

let ansatz = RealAmplitudes 2

printfn "1. Zero Initialization:"
let zeroParams = zeroParameters ansatz numQubits
printfn "   First 5 params: %A" (zeroParams |> Array.take 5)
printfn ""

printfn "2. Constant Initialization (π/4):"
let constParams = constantParameters ansatz numQubits (Math.PI / 4.0)
printfn "   First 5 params: %A" (constParams |> Array.take 5 |> Array.map (fun p -> Math.Round(p, 3)))
printfn ""

printfn "3. Random Initialization (seeded):"
let randParams = randomParameters ansatz numQubits (Some 42)
printfn "   First 5 params: %A" (randParams |> Array.take 5 |> Array.map (fun p -> Math.Round(p, 3)))
printfn ""

// ============================================================================
// Example 8: Composing Feature Map + Variational Form
// ============================================================================

printfn "=========================================="
printfn "Composing Feature Map + Variational Form"
printfn "=========================================="
printfn ""

let features = [| 0.5; 1.0; -0.3; 0.8 |]
let featureMap = ZZFeatureMap 1
let variationalForm = RealAmplitudes 1
let varParams = randomParameters variationalForm numQubits (Some 42)

printfn "1. Generate feature map circuit"
match FeatureMap.buildFeatureMap featureMap features with
| Ok fmCircuit ->
    printfn "   ✓ Feature map: %d gates" (gateCount fmCircuit)
    
    printfn ""
    printfn "2. Generate variational form circuit"
    match buildVariationalForm variationalForm varParams numQubits with
    | Ok vfCircuit ->
        printfn "   ✓ Variational form: %d gates" (gateCount vfCircuit)
        
        printfn ""
        printfn "3. Compose circuits"
        match composeWithFeatureMap fmCircuit vfCircuit with
        | Ok composedCircuit ->
            printfn "   ✓ Composed circuit: %d gates" (gateCount composedCircuit)
            printfn "   Total gates = %d (feature map) + %d (ansatz)" 
                (gateCount fmCircuit) (gateCount vfCircuit)
            printfn ""
            printfn "   This is the complete VQC forward pass circuit!"
            printfn ""
        | Error e ->
            printfn "   ✗ Composition error: %s" e
    | Error e ->
        printfn "   ✗ Variational form error: %s" e
| Error e ->
    printfn "   ✗ Feature map error: %s" e

printfn ""

// ============================================================================
// Example 9: Scaling with Depth
// ============================================================================

printfn "=========================================="
printfn "Scaling: Impact of Depth"
printfn "=========================================="
printfn ""

printfn "Real Amplitudes (4 qubits):"
printfn ""
printfn "Depth | Params | Gates | Ry | CZ"
printfn "------+--------+-------+----+----"

for depth in [1; 2; 3; 5] do
    let ansatz = RealAmplitudes depth
    let params = randomParameters ansatz numQubits (Some 42)
    
    match buildVariationalForm ansatz params numQubits with
    | Ok circuit ->
        let gates = getGates circuit
        let ryCount = gates |> List.filter (function RY _ -> true | _ -> false) |> List.length
        let czCount = gates |> List.filter (function CZ _ -> true | _ -> false) |> List.length
        
        printfn "%5d | %6d | %5d | %2d | %2d" 
            depth params.Length (gateCount circuit) ryCount czCount
    | Error _ ->
        printfn "%5d | %6s | %5s | %2s | %2s" depth "Error" "Error" "--" "--"

printfn ""

// ============================================================================
// Summary
// ============================================================================

printfn "=========================================="
printfn "Summary"
printfn "=========================================="
printfn ""
printfn "✅ Multiple variational form architectures available:"
printfn "   - RealAmplitudes: Simple, hardware-efficient"
printfn "   - TwoLocal: Flexible rotation + entanglement"
printfn "   - EfficientSU2: Full SU(2) coverage with 2× parameters"
printfn ""
printfn "✅ Parameter initialization strategies:"
printfn "   - Zero: Starting from |0⟩ state"
printfn "   - Constant: Uniform initialization"
printfn "   - Random: Exploration of parameter space"
printfn ""
printfn "✅ Circuits compose with feature maps for VQC"
printfn ""
printfn "✅ Depth controls expressiveness (more layers = more flexibility)"
printfn ""
printfn "Next Steps:"
printfn "  1. Implement VQC training loop"
printfn "  2. Add gradient-based optimization (parameter shift rule)"
printfn "  3. Add loss functions for classification"
printfn "  4. Create end-to-end training example with real data"
printfn ""
