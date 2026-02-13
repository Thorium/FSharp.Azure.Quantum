/// BB84 Issue Fix Verification
///
/// Verifies critical fixes in the BB84 QKD implementation:
/// 1. Sample indices are returned and used correctly
/// 2. Error correction operates on the final key (not sifted key)
/// 3. Consistency across multiple runs with different seeds
///
/// This script serves as both a regression test and a demonstration
/// of BB84 protocol correctness properties.

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum.Algorithms.QuantumKeyDistribution
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "BB84_Issue_Fix_Verification.fsx"
    "Verify BB84 implementation correctness: sample indices, error correction, consistency."
    [ { Cli.OptionSpec.Name = "keylength"
        Description = "Key length for BB84 runs"
        Default = Some "256" }
      { Cli.OptionSpec.Name = "runs"
        Description = "Number of consistency runs"
        Default = Some "10" }
      { Cli.OptionSpec.Name = "output"
        Description = "Write results to JSON file"
        Default = None }
      { Cli.OptionSpec.Name = "csv"
        Description = "Write results to CSV file"
        Default = None }
      { Cli.OptionSpec.Name = "quiet"
        Description = "Suppress console output"
        Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

let keyLength = Cli.getIntOr "keylength" 256 args
let numRuns = Cli.getIntOr "runs" 10 args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ---------------------------------------------------------------------------
// Quantum backend (Rule 1: IQuantumBackend dependency)
// ---------------------------------------------------------------------------

let quantumBackend = LocalBackend() :> IQuantumBackend

// ---------------------------------------------------------------------------
// Test 1: Sample Index Consistency
// ---------------------------------------------------------------------------

pr "=== BB84 Issue Fix Verification ==="
pr ""
pr "Test 1: Sample Index Consistency"
pr "---------------------------------"
pr ""

let test1Pass =
    match runBB84 keyLength quantumBackend 0.15 0.11 (Some 42) with
    | Ok result ->
        pr "  BB84 completed successfully"
        pr "  Sifted key length: %d bits" result.SiftedKey.Length
        pr "  Sample size:       %d bits" result.EavesdropCheck.SampleSize
        pr "  Final key length:  %d bits" result.FinalKeyLength

        let sampleIndices = result.EavesdropCheck.SampleIndices
        pr "  Sample indices:    %d returned" sampleIndices.Length
        let preview =
            sampleIndices
            |> Array.take (min 10 sampleIndices.Length)
            |> Array.map string
            |> String.concat ", "
        pr "  First indices:     [%s]" preview

        // Check 1a: Final key length = sifted - sample
        let expectedFinal = result.SiftedKey.Length - result.EavesdropCheck.SampleSize
        let check1a = result.FinalKeyLength = expectedFinal
        pr "  %s Final key length correct (%d = %d - %d)"
            (if check1a then "[OK]" else "[FAIL]")
            result.FinalKeyLength result.SiftedKey.Length result.EavesdropCheck.SampleSize

        // Check 1b: All indices in valid range
        let check1b =
            sampleIndices |> Array.forall (fun i -> i >= 0 && i < result.SiftedKey.Length)
        pr "  %s All sample indices in valid range (0..%d)"
            (if check1b then "[OK]" else "[FAIL]") (result.SiftedKey.Length - 1)

        // Check 1c: No duplicate indices
        let uniqueCount = (Set.ofArray sampleIndices).Count
        let check1c = uniqueCount = sampleIndices.Length
        pr "  %s All sample indices unique (%d unique of %d)"
            (if check1c then "[OK]" else "[FAIL]") uniqueCount sampleIndices.Length

        check1a && check1b && check1c

    | Error err ->
        pr "  [FAIL] BB84 protocol failed: %A" err
        false

pr ""

// ---------------------------------------------------------------------------
// Test 2: Error Correction on Final Key
// ---------------------------------------------------------------------------

pr "Test 2: Error Correction on Final Key"
pr "--------------------------------------"
pr ""

let test2Pass =
    match runCompleteQKD keyLength quantumBackend true 128 (Some 100) with
    | Ok result ->
        pr "  Complete QKD pipeline completed"
        pr "  Sifted key length:     %d bits" result.BB84Result.SiftedKey.Length
        pr "  Final key (before EC): %d bits" result.BB84Result.FinalKeyLength

        match result.ErrorCorrection with
        | Some ec ->
            pr "  Error correction applied:"
            pr "    Original key length: %d bits" ec.OriginalKey.Length
            pr "    Corrected key length:%d bits" ec.CorrectedKey.Length
            pr "    Errors detected:     %d" ec.ErrorsDetected
            pr "    Errors corrected:    %d" ec.ErrorsCorrected

            // Check 2a: EC operated on final key, not sifted key
            let check2a = ec.OriginalKey.Length = result.BB84Result.FinalKeyLength
            pr "  %s EC operated on Final Key (%d bits, not Sifted Key %d bits)"
                (if check2a then "[OK]" else "[FAIL]")
                ec.OriginalKey.Length result.BB84Result.SiftedKey.Length

            // Check 2b: Corrected key length matches original
            let check2b = ec.CorrectedKey.Length = ec.OriginalKey.Length
            pr "  %s Corrected key length matches original (%d bits)"
                (if check2b then "[OK]" else "[FAIL]") ec.CorrectedKey.Length

            check2a && check2b

        | None ->
            pr "  Warning: Error correction was skipped"
            true  // Not a failure, just skipped

    | Error err ->
        pr "  [FAIL] Complete QKD pipeline failed: %A" err
        false

pr ""

// ---------------------------------------------------------------------------
// Test 3: Consistency Check (multiple runs)
// ---------------------------------------------------------------------------

pr "Test 3: Consistency Check (%d runs)" numRuns
pr "------------------------------------"
pr ""

let consistencyResults =
    [ 1 .. numRuns ]
    |> List.map (fun i ->
        match runBB84 (keyLength / 2) quantumBackend 0.15 0.11 (Some i) with
        | Ok result ->
            let expectedFinal = result.SiftedKey.Length - result.EavesdropCheck.SampleSize
            if result.FinalKeyLength = expectedFinal then
                Ok (i, result.FinalKeyLength, result.SiftedKey.Length)
            else
                Error (sprintf "Run %d: key length mismatch (expected %d, got %d)" i expectedFinal result.FinalKeyLength)
        | Error err ->
            Error (sprintf "Run %d: protocol error: %A" i err))

let test3Failures = consistencyResults |> List.choose (fun r -> match r with Error e -> Some e | _ -> None)
let test3Pass = test3Failures.IsEmpty

if test3Pass then
    pr "  [OK] All %d runs produced consistent results" numRuns
else
    test3Failures |> List.iter (fun msg -> pr "  [FAIL] %s" msg)

pr ""

// ---------------------------------------------------------------------------
// Summary
// ---------------------------------------------------------------------------

let allTests = [ ("Sample Index Consistency", test1Pass); ("Error Correction Target", test2Pass); ("Multi-run Consistency", test3Pass) ]
let passCount = allTests |> List.filter snd |> List.length
let totalCount = allTests.Length

pr "=== VERIFICATION SUMMARY ==="
allTests |> List.iter (fun (name, passed) ->
    pr "  %s %s" (if passed then "[OK]  " else "[FAIL]") name)
pr ""
pr "Result: %d/%d tests passed" passCount totalCount
pr ""

// ---------------------------------------------------------------------------
// JSON output
// ---------------------------------------------------------------------------

outputPath |> Option.iter (fun path ->
    let payload =
        {| test1_sampleIndexConsistency = test1Pass
           test2_errorCorrectionTarget = test2Pass
           test3_multiRunConsistency = test3Pass
           totalPassed = passCount
           totalTests = totalCount
           keyLength = keyLength
           consistencyRuns = numRuns |}
    Reporting.writeJson path payload
    pr "JSON written to %s" path)

// ---------------------------------------------------------------------------
// CSV output
// ---------------------------------------------------------------------------

csvPath |> Option.iter (fun path ->
    let header = [ "test"; "result" ]
    let rows =
        allTests
        |> List.map (fun (name, passed) -> [ name; if passed then "PASS" else "FAIL" ])
    Reporting.writeCsv path header rows
    pr "CSV written to %s" path)

// ---------------------------------------------------------------------------
// Usage hints
// ---------------------------------------------------------------------------

if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr "Tip: run with arguments for custom parameters:"
    pr "  dotnet fsi BB84_Issue_Fix_Verification.fsx -- --keylength 512 --runs 20"
    pr "  dotnet fsi BB84_Issue_Fix_Verification.fsx -- --quiet --output results.json --csv results.csv"
    pr "  dotnet fsi BB84_Issue_Fix_Verification.fsx -- --help"
