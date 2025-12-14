/// BB84 Issue Fix Verification
/// ============================
///
/// This test verifies that the critical issues found in the BB84 implementation
/// have been properly fixed.

#r "../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum.Algorithms.QuantumKeyDistribution
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction

let backend = LocalBackend() :> IQuantumBackend

printfn "======================================"
printfn "BB84 ISSUE FIX VERIFICATION"
printfn "======================================"
printfn ""

// ============================================================================
// TEST 1: Verify Sample Indices Are Returned and Used Correctly (Issue #1)
// ============================================================================

printfn "Test 1: Sample Index Consistency"
printfn "--------------------------------"
printfn ""

match runBB84 256 backend 0.15 0.11 (Some 42) with
| Ok result ->
    printfn "✓ BB84 protocol completed successfully"
    printfn "  Sifted key length: %d bits" result.SiftedKey.Length
    printfn "  Sample size: %d bits" result.EavesdropCheck.SampleSize
    printfn "  Final key length: %d bits" result.FinalKeyLength
    printfn ""
    
    // Verify sample indices are returned
    let sampleIndices = result.EavesdropCheck.SampleIndices
    printfn "  Sample indices returned: %d indices" sampleIndices.Length
    printfn "  Sample indices: [%s]" (sampleIndices |> Array.take (min 10 sampleIndices.Length) |> Array.map string |> String.concat ", ")
    
    // Verify final key length = sifted key length - sample size
    let expectedFinalLength = result.SiftedKey.Length - result.EavesdropCheck.SampleSize
    let actualFinalLength = result.FinalKeyLength
    
    if expectedFinalLength = actualFinalLength then
        printfn "  ✅ PASS: Final key length correct (%d = %d - %d)" 
            actualFinalLength result.SiftedKey.Length result.EavesdropCheck.SampleSize
    else
        printfn "  ❌ FAIL: Final key length mismatch (expected %d, got %d)" 
            expectedFinalLength actualFinalLength
    
    // Verify sample indices are within valid range
    let allIndicesValid = 
        sampleIndices 
        |> Array.forall (fun i -> i >= 0 && i < result.SiftedKey.Length)
    
    if allIndicesValid then
        printfn "  ✅ PASS: All sample indices are valid (0 <= i < %d)" result.SiftedKey.Length
    else
        printfn "  ❌ FAIL: Some sample indices are out of range"
    
    // Verify sample indices are unique (no duplicates)
    let uniqueIndices = Set.ofArray sampleIndices
    if uniqueIndices.Count = sampleIndices.Length then
        printfn "  ✅ PASS: All sample indices are unique (no duplicates)"
    else
        printfn "  ❌ FAIL: Found duplicate sample indices"
    
    printfn ""
    
| Error err ->
    printfn "❌ FAIL: BB84 protocol failed: %A" err

printfn ""

// ============================================================================
// TEST 2: Verify Error Correction Uses Correct Keys (Issue #2)
// ============================================================================

printfn "Test 2: Error Correction on Final Key"
printfn "-------------------------------------"
printfn ""

match runCompleteQKD 256 backend true 128 (Some 100) with
| Ok result ->
    printfn "✓ Complete QKD pipeline completed successfully"
    printfn "  Sifted key length: %d bits" result.BB84Result.SiftedKey.Length
    printfn "  Final key (before EC): %d bits" result.BB84Result.FinalKeyLength
    printfn ""
    
    match result.ErrorCorrection with
    | Some ec ->
        printfn "  Error correction applied:"
        printfn "    Original key length: %d bits" ec.OriginalKey.Length
        printfn "    Corrected key length: %d bits" ec.CorrectedKey.Length
        printfn "    Errors detected: %d" ec.ErrorsDetected
        printfn "    Errors corrected: %d" ec.ErrorsCorrected
        printfn ""
        
        // Verify EC operated on final key, not sifted key
        if ec.OriginalKey.Length = result.BB84Result.FinalKeyLength then
            printfn "  ✅ PASS: Error correction operated on Final Key (%d bits)" ec.OriginalKey.Length
            printfn "         (not on Sifted Key which was %d bits)" result.BB84Result.SiftedKey.Length
        else
            printfn "  ❌ FAIL: Error correction operated on wrong key"
            printfn "         Expected: %d bits (Final Key)" result.BB84Result.FinalKeyLength
            printfn "         Got: %d bits" ec.OriginalKey.Length
            printfn "         Sifted Key was: %d bits (should NOT match)" result.BB84Result.SiftedKey.Length
        
        // Verify corrected key length matches original (EC doesn't change length)
        if ec.CorrectedKey.Length = ec.OriginalKey.Length then
            printfn "  ✅ PASS: Corrected key length matches original (%d bits)" ec.CorrectedKey.Length
        else
            printfn "  ❌ FAIL: Corrected key length mismatch"
        
    | None ->
        printfn "  ⚠️  Error correction was not applied (skipped)"
    
    printfn ""
    printfn "  Privacy amplification:"
    printfn "    Input length: %d bits" result.PrivacyAmplification.OriginalLength
    printfn "    Final secure key: %d bits" result.FinalKeyLength
    printfn "    Security level: %d bits" result.SecurityLevel
    printfn ""
    
| Error err ->
    printfn "❌ FAIL: Complete QKD pipeline failed: %A" err

printfn ""

// ============================================================================
// TEST 3: Consistency Check - Multiple Runs
// ============================================================================

printfn "Test 3: Consistency Check (10 runs)"
printfn "-----------------------------------"
printfn ""

let mutable allPassed = true

for i in 1..10 do
    match runBB84 128 backend 0.15 0.11 (Some i) with
    | Ok result ->
        let expectedFinal = result.SiftedKey.Length - result.EavesdropCheck.SampleSize
        if result.FinalKeyLength <> expectedFinal then
            printfn "  Run %d: ❌ FAIL - Key length mismatch" i
            allPassed <- false
    | Error _ ->
        printfn "  Run %d: ❌ FAIL - Protocol error" i
        allPassed <- false

if allPassed then
    printfn "  ✅ PASS: All 10 runs produced consistent results"
else
    printfn "  ❌ FAIL: Some runs failed consistency check"

printfn ""
printfn "======================================"
printfn "VERIFICATION COMPLETE"
printfn "======================================"
printfn ""
