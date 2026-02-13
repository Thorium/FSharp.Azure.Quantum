// HHL Algorithm Extensions - Production Rigetti Example
// Demonstrates new features:
// 1. Automatic condition number estimation
// 2. Comprehensive error bounds calculation
// 3. Adaptive eigenvalue inversion method selection
// 4. Auto-optimized configuration
// 5. Complete solve example with error budget
//
// NEW in this version: Smart configuration for quantum hardware

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Algorithms.HHL
open FSharp.Azure.Quantum.Algorithms.HHLTypes
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI setup
// ---------------------------------------------------------------------------
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv
Cli.exitIfHelp "HHL_Extensions_Rigetti_Example.fsx"
    "HHL extensions: condition number, error bounds, adaptive methods, auto-config"
    [ { Name = "feature";   Description = "Feature to demo: 1|2|3|4|5|all"; Default = Some "all" }
      { Name = "accuracy";  Description = "Target accuracy for optimized config"; Default = Some "0.01" }
      { Name = "fidelity";  Description = "Gate fidelity for error bounds"; Default = Some "0.998" }
      { Name = "output";    Description = "Write results to JSON file"; Default = None }
      { Name = "csv";       Description = "Write results to CSV file"; Default = None }
      { Name = "quiet";     Description = "Suppress informational output"; Default = None } ]
    args

let quiet       = Cli.hasFlag "quiet" args
let outputPath  = Cli.tryGet "output" args
let csvPath     = Cli.tryGet "csv" args
let feature     = Cli.getOr "feature" "all" args
let cliAccuracy = Cli.getFloatOr "accuracy" 0.01 args
let cliFidelity = Cli.getFloatOr "fidelity" 0.998 args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

// Rule 1: explicit IQuantumBackend
let quantumBackend = LocalBackend() :> IQuantumBackend

let results = ResizeArray<Map<string, string>>()

let shouldRun (n: string) = feature = "all" || feature = n

// Shared matrix & vector used across features 1-4
let matrix1 = createDiagonalMatrix [|2.0; 5.0|]
let vector1Res = createQuantumVector [|Complex(1.0, 0.0); Complex(1.0, 0.0)|]

// ============================================================================
// FEATURE 1: Automatic Condition Number Estimation
// ============================================================================

if shouldRun "1" then
    pr "--------------------------------------------------------------------"
    pr "FEATURE 1: Automatic Condition Number Estimation"
    pr "--------------------------------------------------------------------"
    pr ""

    pr "Creating a 2x2 diagonal matrix..."

    match matrix1 with
    | Error err ->
        pr "Error: %A" err
    | Ok mat ->
        pr "Matrix created: 2x2 diagonal"
        pr "  Eigenvalues: 2.0, 5.0"
        pr ""

        pr "Calculating condition number..."
        let matWithKappa = calculateConditionNumber mat

        match matWithKappa.ConditionNumber with
        | Some kappa ->
            pr "Condition number kappa = %.2f" kappa
            pr "   (kappa = lambda_max / lambda_min = 5.0 / 2.0 = 2.5)"
            pr ""
            pr "   Interpretation:"
            if kappa <= 10.0 then
                pr "   Well-conditioned! HHL will work great."
            elif kappa <= 100.0 then
                pr "   Moderately conditioned. HHL should work."
            else
                pr "   Poorly conditioned. Consider preconditioning."
            pr ""

            results.Add(Map.ofList [
                "feature", "1_condition_number"
                "matrix", "diag(2,5)"
                "condition_number", sprintf "%.2f" kappa
                "assessment", if kappa <= 10.0 then "well-conditioned" elif kappa <= 100.0 then "moderate" else "ill-conditioned"
            ])
        | None ->
            pr "Could not calculate condition number"

    pr ""

// ============================================================================
// FEATURE 2: Comprehensive Error Bounds
// ============================================================================

if shouldRun "2" then
    pr "--------------------------------------------------------------------"
    pr "FEATURE 2: Error Bounds for Quantum Hardware"
    pr "--------------------------------------------------------------------"
    pr ""

    match matrix1, vector1Res with
    | Ok mat, Ok vec ->
        pr "Setting up HHL configuration..."
        match defaultConfig mat vec with
        | Error err ->
            pr "Config error: %A" err
        | Ok config ->
            pr "  QPE precision: %d qubits" config.EigenvalueQubits
            pr "  Inversion method: %A" config.InversionMethod
            pr ""

            pr "Calculating error bounds for different backends..."
            pr ""

            // Rigetti (superconducting qubits)
            pr "Rigetti (Aspen-M-3, superconducting qubits):"
            let rigettiErrors = calculateErrorBounds config (Some cliFidelity) None
            pr "   Gate fidelity: %.1f%%" (cliFidelity * 100.0)
            pr "   QPE precision error: %.6f" rigettiErrors.QPEPrecisionError
            pr "   Gate fidelity error: %.6f" rigettiErrors.GateFidelityError
            pr "   Inversion error: %.6f" rigettiErrors.InversionError
            pr "   Total error: %.6f" rigettiErrors.TotalError
            pr "   Success probability: %.4f (%.1f%%)"
                rigettiErrors.EstimatedSuccessProbability
                (rigettiErrors.EstimatedSuccessProbability * 100.0)
            pr ""

            // IonQ (trapped ion)
            pr "IonQ Aria (trapped ion qubits):"
            let ionqErrors = calculateErrorBounds config (Some 0.9999) None
            pr "   Gate fidelity: 99.99%%"
            pr "   QPE precision error: %.6f" ionqErrors.QPEPrecisionError
            pr "   Gate fidelity error: %.6f" ionqErrors.GateFidelityError
            pr "   Inversion error: %.6f" ionqErrors.InversionError
            pr "   Total error: %.6f" ionqErrors.TotalError
            pr "   Success probability: %.4f (%.1f%%)"
                ionqErrors.EstimatedSuccessProbability
                (ionqErrors.EstimatedSuccessProbability * 100.0)
            pr ""

            let ratio = rigettiErrors.TotalError / ionqErrors.TotalError
            pr "Analysis:"
            pr "   IonQ has %.2fx better total error than Rigetti" ratio
            pr "   (Due to higher gate fidelity: 99.99%% vs %.1f%%)" (cliFidelity * 100.0)
            pr ""

            results.Add(Map.ofList [
                "feature", "2_error_bounds"
                "rigetti_total_error", sprintf "%.6f" rigettiErrors.TotalError
                "rigetti_success_prob", sprintf "%.4f" rigettiErrors.EstimatedSuccessProbability
                "ionq_total_error", sprintf "%.6f" ionqErrors.TotalError
                "ionq_success_prob", sprintf "%.4f" ionqErrors.EstimatedSuccessProbability
                "improvement_ratio", sprintf "%.2f" ratio
            ])
    | Error err, _ ->
        pr "Matrix error: %A" err
    | _, Error err ->
        pr "Vector error: %A" err

    pr ""

// ============================================================================
// FEATURE 3: Adaptive Method Selection
// ============================================================================

if shouldRun "3" then
    pr "--------------------------------------------------------------------"
    pr "FEATURE 3: Adaptive Eigenvalue Inversion"
    pr "--------------------------------------------------------------------"
    pr ""

    pr "Testing adaptive method selection for different condition numbers..."
    pr ""

    let testConditionNumbers = [2.0; 15.0; 150.0; 5000.0]

    for kappa in testConditionNumbers do
        let method = selectInversionMethod kappa None
        let methodName =
            match method with
            | EigenvalueInversionMethod.ExactRotation _ -> "ExactRotation"
            | EigenvalueInversionMethod.LinearApproximation _ -> "LinearApproximation"
            | EigenvalueInversionMethod.PiecewiseLinear _ -> "PiecewiseLinear"
        pr "kappa = %.0f -> %s" kappa methodName

        results.Add(Map.ofList [
            "feature", "3_adaptive_method"
            "condition_number", sprintf "%.0f" kappa
            "selected_method", methodName
        ])

    pr ""
    pr "Insights:"
    pr "   kappa <= 10:     ExactRotation (best for well-conditioned)"
    pr "   10 < kappa <= 100: LinearApproximation (moderate)"
    pr "   kappa > 100:     PiecewiseLinear (handles wide eigenvalue range)"
    pr ""

// ============================================================================
// FEATURE 4: One-Function Optimized Configuration
// ============================================================================

if shouldRun "4" then
    pr "--------------------------------------------------------------------"
    pr "FEATURE 4: Auto-Optimized Configuration"
    pr "--------------------------------------------------------------------"
    pr ""

    match matrix1, vector1Res with
    | Ok mat, Ok vec ->
        pr "Using optimizedConfig for automatic setup..."
        pr "  Target accuracy: %.2f%%" (cliAccuracy * 100.0)
        pr ""

        match optimizedConfig mat vec (Some cliAccuracy) with
        | Error err ->
            pr "Config error: %A" err
        | Ok config ->
            let kappa = config.Matrix.ConditionNumber |> Option.defaultValue 0.0
            let methodName =
                match config.InversionMethod with
                | EigenvalueInversionMethod.ExactRotation _ -> "ExactRotation"
                | EigenvalueInversionMethod.LinearApproximation _ -> "LinearApproximation"
                | EigenvalueInversionMethod.PiecewiseLinear _ -> "PiecewiseLinear"

            pr "Configuration automatically optimized:"
            pr "   Matrix condition number: %.2f" kappa
            pr "   QPE precision: %d qubits (for %.1f%% accuracy)" config.EigenvalueQubits (cliAccuracy * 100.0)
            pr "   Inversion method: %s" methodName
            pr "   Min eigenvalue threshold: %.6f" config.MinEigenvalue
            pr "   Post-selection: %b" config.UsePostSelection
            pr ""

            pr "Recommended QPE precision for different targets:"
            let accuracies = [0.1; 0.01; 0.001; 0.0001]
            for acc in accuracies do
                let qpePrecision = recommendQPEPrecision acc
                pr "   %.2f%% accuracy -> %d qubits" (acc * 100.0) qpePrecision
            pr ""

            results.Add(Map.ofList [
                "feature", "4_optimized_config"
                "condition_number", sprintf "%.2f" kappa
                "qpe_qubits", sprintf "%d" config.EigenvalueQubits
                "inversion_method", methodName
                "min_eigenvalue", sprintf "%.6f" config.MinEigenvalue
                "post_selection", sprintf "%b" config.UsePostSelection
            ])
    | _ -> ()

    pr ""

// ============================================================================
// FEATURE 5: Complete Example - HHL with Auto-Optimization
// ============================================================================

if shouldRun "5" then
    pr "--------------------------------------------------------------------"
    pr "COMPLETE EXAMPLE: Solve 2x2 System with Auto-Optimization"
    pr "--------------------------------------------------------------------"
    pr ""

    pr "PROBLEM: Solve [[2, 0], [0, 3]] * x = [1, 1]"
    pr "Expected solution: x ~ [0.5, 0.333...]"
    pr ""

    let matrixResult = createDiagonalMatrix [|2.0; 3.0|]
    let vectorResult = createQuantumVector [|Complex(1.0, 0.0); Complex(1.0, 0.0)|]

    match matrixResult, vectorResult with
    | Ok matrix, Ok vector ->
        pr "Step 1: Auto-optimize configuration..."
        match optimizedConfig matrix vector (Some cliAccuracy) with
        | Error err ->
            pr "Config error: %A" err
        | Ok config ->
            let kappa = config.Matrix.ConditionNumber |> Option.defaultValue 0.0
            pr "  kappa = %.2f (well-conditioned)" kappa
            pr "  Method: %A" config.InversionMethod
            pr ""

            pr "Step 2: Calculate error budget (gate fidelity %.1f%%)..." (cliFidelity * 100.0)
            let errorBudget = calculateErrorBounds config (Some cliFidelity) None
            pr "  Total error: %.4f" errorBudget.TotalError
            pr "  Success probability: %.2f%%" (errorBudget.EstimatedSuccessProbability * 100.0)
            pr ""

            pr "Step 3: Execute HHL on local simulator..."

            match solve2x2Diagonal (2.0, 3.0) (Complex(1.0, 0.0), Complex(1.0, 0.0)) quantumBackend with
            | Error err ->
                pr "Error: %A" err
            | Ok result ->
                pr "SUCCESS!"
                pr ""
                pr "  Solution vector:"
                for i in 0 .. result.Solution.Length - 1 do
                    pr "    x[%d] = %.6f" i result.Solution[i].Real
                pr ""
                pr "  Actual success probability: %.4f" result.SuccessProbability
                pr "  Gates used: %d" result.GateCount
                pr ""
                pr "  Classical verification:"
                pr "    Expected: x[0] = 0.5, x[1] = 0.333..."
                pr "    Quantum:  x[0] = %.3f, x[1] = %.3f"
                    result.Solution[0].Real result.Solution[1].Real
                pr ""

                results.Add(Map.ofList [
                    "feature", "5_complete_solve"
                    "matrix", "diag(2,3)"
                    "vector", "[1,1]"
                    "x0", sprintf "%.6f" result.Solution[0].Real
                    "x1", sprintf "%.6f" result.Solution[1].Real
                    "success_probability", sprintf "%.6f" result.SuccessProbability
                    "gate_count", sprintf "%d" result.GateCount
                    "condition_number", sprintf "%.2f" kappa
                ])

    | Error err, _ -> pr "Matrix error: %A" err
    | _, Error err -> pr "Vector error: %A" err

    pr ""

// ============================================================================
// Production Workflow Guide (always shown unless quiet + specific feature)
// ============================================================================

if feature = "all" then
    pr "--------------------------------------------------------------------"
    pr "PRODUCTION WORKFLOW for Rigetti/IonQ/Quantinuum"
    pr "--------------------------------------------------------------------"
    pr ""
    pr "1. Create your matrix and vector (domain-specific problem)"
    pr "   let matrix = createDiagonalMatrix [|lambda1; lambda2; ...|]"
    pr "   let vector = createQuantumVector [|b1; b2; ...|]"
    pr ""
    pr "2. Use optimizedConfig for automatic setup"
    pr "   let config = optimizedConfig matrix vector (Some 0.01)"
    pr ""
    pr "3. Check error budget BEFORE running on expensive hardware"
    pr "   let errors = calculateErrorBounds config (Some 0.998) None"
    pr ""
    pr "4. Execute on quantum backend"
    pr "   let quantumBackend = LocalBackend() :> IQuantumBackend"
    pr "   match HHL.execute config quantumBackend with ..."
    pr ""
    pr "5. Analyze results and error margins"
    pr ""

pr "--------------------------------------------------------------------"
pr "Key Takeaways:"
pr "  - Condition number predicts HHL success (kappa < 100 is good)"
pr "  - Error bounds help choose hardware (IonQ vs Rigetti vs Quantinuum)"
pr "  - Adaptive methods optimize for matrix properties"
pr "  - optimizedConfig() does everything automatically"
pr ""

// ---------------------------------------------------------------------------
// Structured output
// ---------------------------------------------------------------------------

if outputPath.IsSome then
    let payload = {| script = "HHL_Extensions_Rigetti_Example.fsx"
                     timestamp = DateTime.UtcNow
                     feature = feature
                     accuracy = cliAccuracy
                     fidelity = cliFidelity
                     results = results |> Seq.toArray |}
    Reporting.writeJson outputPath.Value payload
    pr "Results written to %s" outputPath.Value

if csvPath.IsSome then
    let header = ["feature"; "condition_number"; "selected_method"; "success_probability";
                  "gate_count"; "x0"; "x1"; "rigetti_total_error"; "ionq_total_error"]
    let rows =
        results
        |> Seq.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
        |> Seq.toList
    Reporting.writeCsv csvPath.Value header rows
    pr "CSV written to %s" csvPath.Value

// Usage hints
if argv.Length = 0 && outputPath.IsNone && csvPath.IsNone then
    pr ""
    pr "Usage hints:"
    pr "  dotnet fsi HHL_Extensions_Rigetti_Example.fsx -- --feature 5"
    pr "  dotnet fsi HHL_Extensions_Rigetti_Example.fsx -- --accuracy 0.001"
    pr "  dotnet fsi HHL_Extensions_Rigetti_Example.fsx -- --fidelity 0.9999"
    pr "  dotnet fsi HHL_Extensions_Rigetti_Example.fsx -- --quiet --output r.json"
    pr "  dotnet fsi HHL_Extensions_Rigetti_Example.fsx -- --help"
