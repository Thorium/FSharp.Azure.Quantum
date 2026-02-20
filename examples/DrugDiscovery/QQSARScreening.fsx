// ==============================================================================
// Q-QSAR Virtual Screening
// ==============================================================================
// Demonstrates the use of the QuantumDrugDiscovery DSL for QSAR (Quantitative 
// Structure-Activity Relationship) modeling.
//
// Business Context:
// Pharmaceutical companies need to screen large libraries of molecules to identify
// potential drug candidates. "Q-QSAR" (Quantum QSAR) uses quantum kernels to 
// capture complex, non-linear relationships between molecular structure and 
// biological activity that classical linear models might miss.
//
// This example uses the high-level 'drugDiscovery' builder to:
// 1. Define the target protein and candidate library.
// 2. Select the Quantum Kernel SVM method.
// 3. Configure the feature map (ZZFeatureMap) for molecular encoding.
// 4. Run the screening pipeline.
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.IO
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI INTERFACE
// ==============================================================================

let args = Cli.parse (fsi.CommandLineArgs |> Array.skip 1)

args |> Cli.exitIfHelp "DrugDiscovery/QQSARScreening.fsx"
    "Run Q-QSAR (Quantum QSAR) screening using the drugDiscovery builder"
    [ { Name = "input";   Description = "CSV file with SMILES and labels"; Default = Some "_data/actives_tiny_labeled.csv" }
      { Name = "output";  Description = "Write results to JSON file"; Default = None }
      { Name = "method";  Description = "Screening method: kernel, vqc"; Default = Some "kernel" }
      { Name = "batch";   Description = "Batch size for processing"; Default = Some "50" }
      { Name = "quiet";   Description = "Suppress detailed output (flag)"; Default = None } ]

let scriptDir = __SOURCE_DIRECTORY__
let inputFile = args |> Cli.getOr "input" (Path.Combine(scriptDir, "_data", "actives_tiny_labeled.csv"))
let outputFile = args |> Cli.tryGet "output"
let methodChoice = args |> Cli.getOr "method" "kernel" |> fun s -> s.ToLowerInvariant()
let batchSize = args |> Cli.getIntOr "batch" 50
let quiet = args |> Cli.hasFlag "quiet"

// Resolve input path
let resolvedInput =
    if Path.IsPathRooted inputFile then inputFile
    else Data.resolveRelative scriptDir inputFile

if not (File.Exists resolvedInput) then
    eprintfn "Error: Input file not found: %s" resolvedInput
    exit 1

// Select screening method
let screeningMethod =
    match methodChoice with
    | "kernel" | "svm" -> ScreeningMethod.QuantumKernelSVM
    | "vqc" | "classifier" -> ScreeningMethod.VQCClassifier
    | other ->
        eprintfn "Error: Unknown method '%s'. Use: kernel, vqc" other
        exit 1

// ==============================================================================
// SCREENING PIPELINE
// ==============================================================================

printfn "================================================================"
printfn "           Q-QSAR Virtual Screening Pipeline"
printfn "================================================================"
printfn ""
printfn "Input: %s" resolvedInput
printfn "Method: %A" screeningMethod
printfn "Batch size: %d" batchSize
printfn ""

// Execute the screening pipeline using the DSL
let screeningResult =
    drugDiscovery {
        // 1. Target Definition
        target_protein_from_pdb "3CL_protease.pdb"

        // 2. Training Dataset
        load_candidates_from_file resolvedInput

        // 3. Strategy
        use_method screeningMethod
        use_feature_map FeatureMap.ZZFeatureMap

        // 4. Execution
        set_batch_size batchSize
        backend (FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend())
    }

// Display the output
match screeningResult with
| Ok result ->
    if not quiet then
        printfn "%s" result.Message
    printfn ""
    printfn "Molecules processed: %d" result.MoleculesProcessed
    printfn "Method: %A" result.Method
| Error e ->
    eprintfn "Screening failed: %A" e

printfn ""

if not quiet then
    printfn "Business Value:"
    printfn "  - Non-linear separation of 'Active' vs 'Inactive' compounds."
    printfn "  - Enhanced detection of novel scaffolds (scaffold hopping)."
    printfn "  - Reduced false negatives compared to linear classical QSAR."
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

match outputFile, screeningResult with
| Some path, Ok result ->
    let resultMap = Map.ofList [
        "Method", sprintf "%A" result.Method
        "MoleculesProcessed", string result.MoleculesProcessed
        "InputFile", resolvedInput
        "BatchSize", string batchSize
        "Message", result.Message
    ]
    Reporting.writeJson path resultMap
    printfn "Results written to: %s" path
| Some _, Error _ ->
    eprintfn "Cannot write output: screening failed"
| None, _ -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if outputFile.IsNone && inputFile = (Path.Combine(scriptDir, "_data", "actives_tiny_labeled.csv")) then
    printfn "================================================================"
    printfn " Try with your own data"
    printfn "================================================================"
    printfn ""
    printfn "  # Screen with a different labeled CSV:"
    printfn "  dotnet fsi QQSARScreening.fsx -- --input my_compounds.csv --output results.json"
    printfn ""
    printfn "  # Use VQC classifier instead of kernel SVM:"
    printfn "  dotnet fsi QQSARScreening.fsx -- --method vqc --batch 20"
    printfn ""
    printfn "  # Pipeline: Q-QSAR -> ADMET prediction:"
    printfn "  dotnet fsi QQSARScreening.fsx -- --input library.csv --output qsar_hits.json"
    printfn "  dotnet fsi ADMETPrediction.fsx -- --input qsar_hits.json --csv admet_results.csv"
    printfn ""

// Exit with appropriate code
match screeningResult with
| Ok _ -> exit 0
| Error _ -> exit 1
