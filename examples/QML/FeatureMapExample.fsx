/// Quantum Machine Learning - Feature Map Example
///
/// Demonstrates different feature map strategies for encoding classical data
/// into quantum states

(*
===============================================================================
 Background Theory
===============================================================================

Quantum feature maps are the bridge between classical data and quantum computation.
They encode classical input vectors x ∈ ℝᵈ into quantum states |ψ(x)⟩ in a 2ⁿ-
dimensional Hilbert space. The choice of feature map critically determines the
expressiveness and potential quantum advantage of variational quantum algorithms.
Different encoding strategies create different "quantum feature spaces" that may
capture patterns invisible to classical methods.

The most common encoding strategies are:
- **Angle Encoding**: Each feature xᵢ is encoded as a rotation angle, typically
  via Ry(π·xᵢ) or Rz(xᵢ) gates. Requires n qubits for n features. Simple and
  hardware-efficient but limited expressiveness.
- **Amplitude Encoding**: Features are encoded as amplitudes of a quantum state,
  |ψ⟩ = Σᵢ xᵢ|i⟩ (normalized). Exponentially efficient (log₂(d) qubits for d
  features) but requires complex state preparation circuits.
- **ZZ Feature Map**: Combines single-qubit rotations with two-qubit ZZ interactions
  that encode products of features: exp(i·φ(xᵢ,xⱼ)·ZᵢZⱼ). Creates entanglement
  and captures feature correlations, proven advantageous in Havlicek et al.

Key Equations:
  - Angle encoding: |ψ(x)⟩ = ⊗ᵢ Ry(π·xᵢ)|0⟩ = ⊗ᵢ [cos(πxᵢ/2)|0⟩ + sin(πxᵢ/2)|1⟩]
  - ZZ feature map layer: U_ZZ = exp(i·(π-xᵢ)(π-xⱼ)·ZᵢZⱼ) for connected qubits
  - Quantum kernel: K(x,x') = |⟨ψ(x)|ψ(x')⟩|² (overlap of encoded states)
  - Expressibility: measured by distribution of fidelities over parameter space

Quantum Advantage:
  The power of quantum feature maps lies in accessing feature spaces that are
  classically intractable to compute. Havlicek et al. showed that ZZ feature maps
  with depth O(n) create quantum kernels that cannot be efficiently computed
  classically (under standard complexity assumptions). The "expressibility vs.
  trainability" tradeoff is crucial: highly expressive maps may suffer from
  barren plateaus (vanishing gradients), while simple maps may not offer quantum
  advantage. Optimal depth for NISQ devices is typically 1-3 layers.

Dimensionality and Feature Spaces:
  Classical statistical learning faces the "curse of dimensionality" [5]: as the
  number of features grows, the volume of the feature space grows exponentially,
  causing data to become sparse and distances less meaningful. Quantum feature maps
  offer a unique approach—they map d-dimensional classical data into a 2ⁿ-dimensional
  Hilbert space, but this exponential space is structured by quantum mechanics rather
  than arbitrary. The quantum kernel K(x,x') = |⟨ψ(x)|ψ(x')⟩|² computes similarity
  in this space, analogous to classical kernel methods [5, Ch. 9] but with potentially
  classically-intractable kernels. This is the connection to Support Vector Machines:
  both use the "kernel trick" to operate in high-dimensional spaces implicitly.

Reproducing Kernel Hilbert Spaces (RKHS):
  The mathematical foundation connecting classical SVMs and quantum kernels is RKHS
  theory [6, Ch. 5.8]. A kernel K(x,x') implicitly defines a feature map φ such that
  K(x,x') = ⟨φ(x), φ(x')⟩. For quantum kernels, the feature map is explicit:
  φ(x) = |ψ(x)⟩, and the kernel is K(x,x') = |⟨ψ(x)|ψ(x')⟩|². The RKHS framework
  guarantees that kernel-based learning (SVMs, kernel regression) finds optimal
  solutions in this implicit feature space. Quantum advantage arises when the kernel
  is classically hard to compute but quantum-efficient to evaluate.

References:
  [1] Havlicek et al., "Supervised learning with quantum-enhanced feature spaces",
      Nature 567, 209-212 (2019). https://doi.org/10.1038/s41586-019-0980-2
  [2] Schuld & Killoran, "Quantum Machine Learning in Feature Hilbert Spaces",
      Phys. Rev. Lett. 122, 040504 (2019). https://doi.org/10.1103/PhysRevLett.122.040504
  [3] LaRose & Coyle, "Robust data encodings for quantum classifiers",
      Phys. Rev. A 102, 032420 (2020). https://doi.org/10.1103/PhysRevA.102.032420
  [4] Wikipedia: Quantum_machine_learning
      https://en.wikipedia.org/wiki/Quantum_machine_learning
  [5] James et al., "An Introduction to Statistical Learning with Applications
      in Python", Springer (2023). Chapters 2 (curse of dimensionality), 9 (SVMs/kernels).
      https://www.statlearning.com/
  [6] Hastie, Tibshirani, Friedman, "The Elements of Statistical Learning", 2nd ed.,
      Springer (2009). Ch. 5.8 (RKHS), Ch. 12 (SVMs). https://hastie.su.domains/ElemStatLearn/
*)

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
| Error err ->
    printfn "✗ Error: %s" err.Message
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
| Error err ->
    printfn "✗ Error: %s" err.Message
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
| Error err ->
    printfn "✗ Error: %s" err.Message
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
| Error err ->
    printfn "✗ Error: %s" err.Message
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
| Error err ->
    printfn "✗ Error: %s" err.Message
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
    | Error err ->
        printfn "  Error: %s" err.Message
    
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
