/// Quantum Error Correction Example
/// 
/// Demonstrates four standard quantum error correction codes:
///   1. **BitFlip** [[3,1,1]]: Corrects single X errors
///   2. **PhaseFlip** [[3,1,1]]: Corrects single Z errors
///   3. **Shor** [[9,1,3]]: Corrects any single-qubit error
///   4. **Steane** [[7,1,3]]: CSS code correcting any single-qubit error
/// 
/// **Key Insight**: Quantum errors are continuous, but the discretization
/// theorem shows that correcting X, Z, and Y (= iXZ) suffices to correct
/// ALL single-qubit errors, including arbitrary rotations.
/// 
/// **Production Use Cases**:
/// - Fault-tolerant quantum computation
/// - Quantum memory (preserving quantum states)
/// - Quantum communication (channel error correction)
/// - Benchmarking quantum hardware

(*
===============================================================================
 Background Theory
===============================================================================

Quantum Error Correction (QEC) protects quantum information from decoherence
and noise. Unlike classical error correction, QEC faces unique challenges:
  - No-cloning theorem: Cannot copy unknown quantum states
  - Measurement collapse: Observing a qubit destroys superposition
  - Continuous errors: Errors can be arbitrary rotations, not just bit flips

The breakthrough insight is that by encoding logical qubits into entangled
multi-qubit states, we can extract error *syndromes* without measuring the
encoded information, and then apply corrections.

Notation:
  [[n,k,d]] = n physical qubits, k logical qubits, d code distance
  Code distance d corrects floor((d-1)/2) errors

References:
  [1] Shor, "Scheme for reducing decoherence in quantum computer memory",
      Phys. Rev. A 52, R2493 (1995).
  [2] Steane, "Error Correcting Codes in Quantum Theory",
      Phys. Rev. Lett. 77, 793 (1996).
  [3] Calderbank & Shor, "Good quantum error-correcting codes exist",
      Phys. Rev. A 54, 1098 (1996).
  [4] Nielsen & Chuang, "Quantum Computation and Quantum Information",
      Cambridge University Press (2010), Chapter 10.
  [5] Wikipedia: Quantum error correction
      https://en.wikipedia.org/wiki/Quantum_error_correction
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum.Algorithms.QuantumErrorCorrection
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction

printfn "================================================================"
printfn "  Quantum Error Correction - Four Code Demo"
printfn "================================================================"
printfn ""

// Create a local quantum backend (noise-free simulator)
let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

printfn "Backend: %s" backend.Name
printfn ""

// ============================================================================
// DEMO 1: Code Parameters
// ============================================================================

printfn "--- Demo 1: Code Parameters ---"
printfn ""

let codes = [ BitFlipCode3; PhaseFlipCode3; ShorCode9; SteaneCode7 ]

for code in codes do
    printfn "  %s" (formatCodeParameters code)

printfn ""

// ============================================================================
// DEMO 2: 3-Qubit Bit-Flip Code
// ============================================================================

printfn "--- Demo 2: 3-Qubit Bit-Flip Code [[3,1,1]] ---"
printfn ""
printfn "  Encoding: |0> -> |000>,  |1> -> |111>"
printfn "  Corrects: Single X (bit-flip) errors"
printfn ""

// Test with no error
match BitFlip.roundTrip backend 0 None with
| Error err -> printfn "  ERROR: %A" err
| Ok result ->
    printfn "  No error:   encoded |%d> -> decoded |%d>  (success=%b)"
        result.LogicalBit result.DecodedBit result.Success

// Test with error on each data qubit
for q in 0..2 do
    for logicalBit in [0; 1] do
        match BitFlip.roundTrip backend logicalBit (Some q) with
        | Error err -> printfn "  ERROR: %A" err
        | Ok result ->
            printfn "  X on q%d:    encoded |%d> -> syndrome %A -> decoded |%d>  (success=%b)"
                q result.LogicalBit result.Syndrome.SyndromeBits
                result.DecodedBit result.Success

printfn ""

// ============================================================================
// DEMO 3: 3-Qubit Phase-Flip Code
// ============================================================================

printfn "--- Demo 3: 3-Qubit Phase-Flip Code [[3,1,1]] ---"
printfn ""
printfn "  Encoding: |0> -> |+++>,  |1> -> |--->"
printfn "  Corrects: Single Z (phase-flip) errors"
printfn ""

for q in 0..2 do
    match PhaseFlip.roundTrip backend 0 (Some q) with
    | Error err -> printfn "  ERROR: %A" err
    | Ok result ->
        printfn "  Z on q%d:    encoded |%d> -> syndrome %A -> decoded |%d>  (success=%b)"
            q result.LogicalBit result.Syndrome.SyndromeBits
            result.DecodedBit result.Success

// Encoding logical 1
match PhaseFlip.roundTrip backend 1 (Some 1) with
| Error err -> printfn "  ERROR: %A" err
| Ok result ->
    printfn "  Z on q1:    encoded |%d> -> syndrome %A -> decoded |%d>  (success=%b)"
        result.LogicalBit result.Syndrome.SyndromeBits
        result.DecodedBit result.Success

printfn ""

// ============================================================================
// DEMO 4: Shor 9-Qubit Code
// ============================================================================

printfn "--- Demo 4: Shor 9-Qubit Code [[9,1,3]] ---"
printfn ""
printfn "  First QEC code ever discovered (Shor, 1995)."
printfn "  Concatenation of phase-flip (outer) and bit-flip (inner) codes."
printfn "  Corrects ANY single-qubit error: X, Z, or Y = iXZ."
printfn ""

// Bit-flip error
match Shor.roundTrip backend 0 BitFlipError 1 with
| Error err -> printfn "  ERROR: %A" err
| Ok result ->
    printfn "  X on q1:    encoded |%d> -> decoded |%d>  (success=%b)"
        result.LogicalBit result.DecodedBit result.Success

// Phase-flip error
match Shor.roundTrip backend 0 PhaseFlipError 0 with
| Error err -> printfn "  ERROR: %A" err
| Ok result ->
    printfn "  Z on q0:    encoded |%d> -> decoded |%d>  (success=%b)"
        result.LogicalBit result.DecodedBit result.Success

// Combined (Y) error
match Shor.roundTrip backend 1 CombinedError 4 with
| Error err -> printfn "  ERROR: %A" err
| Ok result ->
    printfn "  Y on q4:    encoded |%d> -> decoded |%d>  (success=%b)"
        result.LogicalBit result.DecodedBit result.Success

printfn ""

// ============================================================================
// DEMO 5: Steane 7-Qubit Code
// ============================================================================

printfn "--- Demo 5: Steane 7-Qubit Code [[7,1,3]] ---"
printfn ""
printfn "  CSS code based on classical [7,4,3] Hamming code."
printfn "  Uses only 7 qubits (vs 9 for Shor) with same error correction."
printfn "  Supports transversal gates for fault-tolerant computation."
printfn ""

// Bit-flip error
match Steane.roundTrip backend 0 BitFlipError 3 with
| Error err -> printfn "  ERROR: %A" err
| Ok result ->
    printfn "  X on q3:    encoded |%d> -> decoded |%d>  (success=%b)"
        result.LogicalBit result.DecodedBit result.Success

// Phase-flip error
match Steane.roundTrip backend 0 PhaseFlipError 5 with
| Error err -> printfn "  ERROR: %A" err
| Ok result ->
    printfn "  Z on q5:    encoded |%d> -> decoded |%d>  (success=%b)"
        result.LogicalBit result.DecodedBit result.Success

// Combined error
match Steane.roundTrip backend 1 CombinedError 2 with
| Error err -> printfn "  ERROR: %A" err
| Ok result ->
    printfn "  Y on q2:    encoded |%d> -> decoded |%d>  (success=%b)"
        result.LogicalBit result.DecodedBit result.Success

printfn ""

// ============================================================================
// DEMO 6: Formatted Round-Trip Output
// ============================================================================

printfn "--- Demo 6: Detailed Round-Trip Report ---"
printfn ""

match Shor.roundTrip backend 1 CombinedError 7 with
| Error err -> printfn "  ERROR: %A" err
| Ok result -> printfn "%s" (formatRoundTrip result)

// ============================================================================
// DEMO 7: Code Comparison
// ============================================================================

printfn "--- Demo 7: Code Comparison ---"
printfn ""
printfn "  %-20s  %-10s  %-10s  %-8s  %-12s" "Code" "Qubits" "Distance" "Corrects" "Error Types"
printfn "  %-20s  %-10s  %-10s  %-8s  %-12s" "--------------------" "----------" "----------" "--------" "------------"

let codeInfo = [
    (BitFlipCode3,   "X only")
    (PhaseFlipCode3, "Z only")
    (ShorCode9,      "X, Z, Y")
    (SteaneCode7,    "X, Z, Y")
]

for (code, errorTypes) in codeInfo do
    let p = codeParameters code
    let name =
        match code with
        | BitFlipCode3 -> "Bit-Flip [[3,1,1]]"
        | PhaseFlipCode3 -> "Phase-Flip [[3,1,1]]"
        | ShorCode9 -> "Shor [[9,1,3]]"
        | SteaneCode7 -> "Steane [[7,1,3]]"
    printfn "  %-20s  %-10d  %-10d  %-8d  %-12s"
        name p.PhysicalQubits p.Distance p.CorrectableErrors errorTypes

printfn ""
printfn "================================================================"
printfn "  Quantum Error Correction Demo Complete"
printfn "================================================================"
