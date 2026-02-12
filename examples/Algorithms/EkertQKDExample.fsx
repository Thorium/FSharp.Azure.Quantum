/// Ekert E91 Quantum Key Distribution Protocol Example
/// 
/// Demonstrates the E91 entanglement-based QKD protocol:
/// Alice and Bob share entangled Bell pairs, measure in randomly chosen bases,
/// and verify security via the CHSH inequality.
/// 
/// **Key Insight**: Unlike BB84 (prepare-and-measure), E91 derives security from
/// Bell's theorem. Eavesdropping destroys entanglement, reducing the CHSH
/// parameter S below the quantum bound 2*sqrt(2).
/// 
/// **Production Use Cases**:
/// - Quantum Networks (entanglement-based secure key exchange)
/// - Device-Independent QKD (security relies only on Bell violation)
/// - Satellite QKD (demonstrated on Micius satellite over 1200km)

(*
===============================================================================
 Background Theory
===============================================================================

The E91 protocol was proposed by Artur Ekert in 1991. It uses entangled Bell
pairs and the CHSH inequality to establish a shared secret key.

Protocol Steps:
  1. Source generates Bell pairs |Phi+> = (|00> + |11>) / sqrt(2)
  2. Alice measures her qubit in one of 3 bases: {0 deg, 45 deg, 90 deg}
  3. Bob measures his qubit in one of 3 bases: {0 deg, 45 deg, 135 deg}
  4. Both publicly announce basis choices (not results)
  5. Matching bases (0,0) and (45,45): used for key bits (~22% of pairs)
  6. Non-matching bases: compute CHSH parameter S for security test
  7. If |S| ~ 2*sqrt(2): secure. If |S| <= 2: eavesdropper detected

CHSH Inequality:
  S = E(a1,b1) - E(a1,b3) + E(a3,b1) + E(a3,b3)

  where E(a,b) = P(same) - P(different) for basis pair (a,b)
  
  Classical bound: |S| <= 2
  Quantum bound:   |S| = 2*sqrt(2) ~ 2.828

References:
  [1] Ekert, "Quantum Cryptography Based on Bell's Theorem",
      Phys. Rev. Lett. 67, 661 (1991).
  [2] Clauser, Horne, Shimony, Holt, "Proposed Experiment to Test Local
      Hidden-Variable Theories", Phys. Rev. Lett. 23, 880 (1969).
  [3] Nielsen & Chuang, "Quantum Computation and Quantum Information",
      Cambridge University Press (2010), Chapter 12.
  [4] Wikipedia: Ekert protocol
      https://en.wikipedia.org/wiki/Quantum_key_distribution#E91_protocol
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum.Algorithms.EkertQKD
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction

printfn "================================================================"
printfn "  Ekert E91 QKD Protocol - Quantum Key Distribution Demo"
printfn "================================================================"
printfn ""

// Create a local quantum backend (noise-free simulator)
let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

printfn "Backend: %s" backend.Name
printfn ""

// ============================================================================
// DEMO 1: Basis Combination Table
// ============================================================================

printfn "--- Demo 1: E91 Basis Combinations ---"
printfn ""
printfn "%s" (formatBasisTable ())

// ============================================================================
// DEMO 2: Run E91 Protocol (No Eavesdropper)
// ============================================================================

printfn "--- Demo 2: Secure Key Exchange (No Eavesdropper) ---"
printfn ""

let numPairs = 200

match run backend numPairs (Some 42) with
| Error err ->
    printfn "  ERROR: %A" err
| Ok result ->
    printfn "  Total pairs: %d" result.TotalPairs
    printfn "  Sifted key length: %d bits" result.SiftedKeyLength
    printfn "  Key rate: %.1f%%" (result.KeyRate * 100.0)
    printfn "  CHSH parameter |S|: %.4f" (abs result.CHSHTest.S)
    printfn "  Quantum bound: %.4f" result.CHSHTest.QuantumBound
    printfn "  Classical bound: %.4f" result.CHSHTest.ClassicalBound
    printfn "  Secure: %b" result.IsSecure
    printfn ""

    // Show first 20 key bits
    let keyPreview =
        result.KeyBits
        |> List.truncate 20
        |> List.map string
        |> String.concat ""
    printfn "  Key bits (first 20): %s" keyPreview

printfn ""

// ============================================================================
// DEMO 3: CHSH Inequality Details
// ============================================================================

printfn "--- Demo 3: CHSH Inequality Analysis ---"
printfn ""

match run backend 300 (Some 123) with
| Error err ->
    printfn "  ERROR: %A" err
| Ok result ->
    printfn "%s" (formatCHSH result.CHSHTest)

// ============================================================================
// DEMO 4: Eavesdropper Detection
// ============================================================================

printfn "--- Demo 4: Eavesdropper Detection (Eve Present) ---"
printfn ""

printfn "Running protocol WITHOUT Eve..."
let noEveResult = run backend 300 (Some 99)

printfn "Running protocol WITH Eve (intercept-resend attack)..."
let withEveResult = runWithEve backend 300 (Some 99)

match (noEveResult, withEveResult) with
| (Ok noEve, Ok withEve) ->
    printfn ""
    printfn "  Comparison:"
    printfn "  %-25s  %-12s  %-12s" "" "No Eve" "With Eve"
    printfn "  %-25s  %-12s  %-12s" "-------------------------" "------------" "------------"
    printfn "  %-25s  %-12.4f  %-12.4f" "|S| (CHSH parameter)" (abs noEve.CHSHTest.S) (abs withEve.CHSHTest.S)
    printfn "  %-25s  %-12s  %-12s" "Secure?" (string noEve.IsSecure) (string withEve.IsSecure)
    printfn "  %-25s  %-12d  %-12d" "Sifted key bits" noEve.SiftedKeyLength withEve.SiftedKeyLength
    printfn "  %-25s  %-12.1f  %-12.1f" "Key rate (%%)" (noEve.KeyRate * 100.0) (withEve.KeyRate * 100.0)
    printfn ""
    printfn "  Classical bound: |S| <= 2.0"
    printfn "  Quantum bound:   |S| =  2.828"
    printfn ""

    if noEve.IsSecure && not withEve.IsSecure then
        printfn "  Result: Eve's intercept-resend attack DETECTED!"
        printfn "  The CHSH parameter dropped below the quantum bound,"
        printfn "  indicating destroyed entanglement. Protocol aborted."
    elif noEve.IsSecure then
        printfn "  Result: Both runs appear secure (Eve's attack may have"
        printfn "  insufficient samples to show clear difference)."
    else
        printfn "  Result: Statistical fluctuations observed."
        printfn "  In production, use more pairs for reliable detection."

| (Error err, _) ->
    printfn "  ERROR (no Eve): %A" err
| (_, Error err) ->
    printfn "  ERROR (with Eve): %A" err

printfn ""

// ============================================================================
// DEMO 5: Full Formatted Output
// ============================================================================

printfn "--- Demo 5: Full Protocol Report ---"
printfn ""

match run backend 200 (Some 7) with
| Error err -> printfn "  ERROR: %A" err
| Ok result -> printfn "%s" (formatResult result)

printfn ""
printfn "================================================================"
printfn "  Ekert E91 QKD Demo Complete"
printfn "================================================================"
