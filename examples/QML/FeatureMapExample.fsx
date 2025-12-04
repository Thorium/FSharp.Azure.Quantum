/// Quantum Machine Learning - Feature Map Example
///
/// Demonstrates different feature map strategies for encoding classical data
/// into quantum states

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"


open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.CircuitBuilder

printfn "======================================"
printfn "QML Feature Maps - Circuit Generation"
printfn "======================================"
printfn ""

// Sample feature vector
let features = [| 0.5; 1.0; -0.3; 0.8 |]
printfn "Feature Vector: %A" features
printfn "Number of features: %d" features.Length
printfn ""

// ============================================================================
// Example 1: Angle Encoding
// ============================================================================

printfn "Example 1: Angle Encoding"
printfn "------------------------------------------------------------"
printfn "Strategy: Ry(π * x_i) on each qubit"
printfn ""

match FeatureMap.buildFeatureMap AngleEncoding features with
| Ok circuit ->
    printfn "✓ Circuit generated successfully"
    printfn "  Qubits: %d" circuit.QubitCount
    printfn "  Gates: %d" (gateCount circuit)
    printfn "  Structure: Ry rotations only (no entanglement)"
    printfn ""
| Error e ->
    printfn "✗ Error: %s" e
    printfn ""

// ============================================================================
// Example 2: ZZ Feature Map (Depth 1)
// ============================================================================

printfn "Example 2: ZZ Feature Map (depth=1)"
printfn "------------------------------------------------------------"
printfn "Strategy: Hadamard + Rz rotations + ZZ entanglement"
printfn ""

match FeatureMap.buildFeatureMap (ZZFeatureMap 1) features with
| Ok circuit ->
    printfn "✓ Circuit generated successfully"
    printfn "  Qubits: %d" circuit.QubitCount
    printfn "  Gates: %d" (gateCount circuit)
    
    // Count gate types
    let gates = getGates circuit
    let hCount = gates |> List.filter (function H _ -> true | _ -> false) |> List.length
    let rzCount = gates |> List.filter (function RZ _ -> true | _ -> false) |> List.length
    let cnotCount = gates |> List.filter (function CNOT _ -> true | _ -> false) |> List.length
    
    printfn "  - Hadamard gates: %d" hCount
    printfn "  - Rz rotations: %d" rzCount
    printfn "  - CNOT gates: %d" cnotCount
    printfn ""
| Error e ->
    printfn "✗ Error: %s" e
    printfn ""

// ============================================================================
// Example 3: ZZ Feature Map (Depth 2) 
// ============================================================================

printfn "Example 3: ZZ Feature Map (depth=2)"
printfn "------------------------------------------------------------"
printfn "Strategy: Two layers of entangling feature maps"
printfn ""

match FeatureMap.buildFeatureMap (ZZFeatureMap 2) features with
| Ok circuit ->
    printfn "✓ Circuit generated successfully"
    printfn "  Qubits: %d" circuit.QubitCount
    printfn "  Gates: %d" (gateCount circuit)
    printfn "  Depth increases with layers (more expressive)"
    printfn ""
| Error e ->
    printfn "✗ Error: %s" e
    printfn ""

// ============================================================================
// Example 4: Pauli Feature Map
// ============================================================================

printfn "Example 4: Pauli Feature Map"
printfn "------------------------------------------------------------"
printfn "Strategy: Custom Pauli string rotations"
printfn ""

match FeatureMap.buildFeatureMap (PauliFeatureMap(["ZZ"; "XX"], 1)) features with
| Ok circuit ->
    printfn "✓ Circuit generated successfully"
    printfn "  Qubits: %d" circuit.QubitCount
    printfn "  Gates: %d" (gateCount circuit)
    printfn "  Pauli strings: ZZ, XX"
    printfn ""
| Error e ->
    printfn "✗ Error: %s" e
    printfn ""

// ============================================================================
// Example 5: Amplitude Encoding
// ============================================================================

printfn "Example 5: Amplitude Encoding"
printfn "------------------------------------------------------------"
printfn "Strategy: Encode features as quantum state amplitudes"
printfn ""

match FeatureMap.buildFeatureMap AmplitudeEncoding features with
| Ok circuit ->
    printfn "✓ Circuit generated successfully"
    printfn "  Qubits: %d (log₂(%d) = %.1f)" 
        circuit.QubitCount 
        features.Length 
        (log (float features.Length) / log 2.0)
    printfn "  Gates: %d" (gateCount circuit)
    printfn "  Note: Placeholder implementation (H gates only)"
    printfn "        Full Mottonen state prep would be more complex"
    printfn ""
| Error e ->
    printfn "✗ Error: %s" e
    printfn ""

// ============================================================================
// Example 6: Feature Map Comparison
// ============================================================================

printfn "======================================"
printfn "Feature Map Comparison"
printfn "======================================"
printfn ""

let featureMaps = [
    ("AngleEncoding", AngleEncoding)
    ("ZZFeatureMap(1)", ZZFeatureMap 1)
    ("ZZFeatureMap(2)", ZZFeatureMap 2)
    ("PauliFeatureMap", PauliFeatureMap(["ZZ"], 1))
    ("AmplitudeEncoding", AmplitudeEncoding)
]

printfn "Feature Map          | Qubits | Gates | Entanglement"
printfn "---------------------+--------+-------+-------------"

for (name, fm) in featureMaps do
    match FeatureMap.buildFeatureMap fm features with
    | Ok circuit ->
        let hasEntanglement = 
            getGates circuit 
            |> List.exists (function CNOT _ | CZ _ | SWAP _ | CCX _ -> true | _ -> false)
        
        let entStr = if hasEntanglement then "Yes" else "No"
        printfn "%-20s | %6d | %5d | %s" name circuit.QubitCount (gateCount circuit) entStr
    | Error _ ->
        printfn "%-20s | %6s | %5s | %s" name "Error" "Error" "Error"

printfn ""

// ============================================================================
// Example 7: Small vs Large Feature Vectors
// ============================================================================

printfn "======================================"
printfn "Scaling: Small vs Large Features"
printfn "======================================"
printfn ""

let testFeatures = [
    ("Small (2 features)", [| 0.5; 1.0 |])
    ("Medium (4 features)", [| 0.5; 1.0; -0.3; 0.8 |])
    ("Large (8 features)", [| 0.1; 0.2; 0.3; 0.4; 0.5; 0.6; 0.7; 0.8 |])
]

for (desc, feat) in testFeatures do
    printfn "%s:" desc
    
    match FeatureMap.buildFeatureMap (ZZFeatureMap 1) feat with
    | Ok circuit ->
        printfn "  Qubits: %d, Gates: %d" circuit.QubitCount (gateCount circuit)
    | Error e ->
        printfn "  Error: %s" e
    
    printfn ""

printfn "======================================"
printfn "Summary"
printfn "======================================"
printfn ""
printfn "✅ Feature maps successfully generate quantum circuits"
printfn "✅ Multiple encoding strategies available"
printfn "✅ Circuits scale linearly with features (except Amplitude)"
printfn "✅ Ready for integration with VQC training"
printfn ""
printfn "Next Steps:"
printfn "  1. Implement Variational Quantum Classifier (VQC)"
printfn "  2. Add parameter optimization (gradient descent)"
printfn "  3. Create training loop with real data"
printfn "  4. Integrate with quantum backends"
