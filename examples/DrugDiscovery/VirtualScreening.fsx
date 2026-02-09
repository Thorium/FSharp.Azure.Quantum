// ==============================================================================
// Virtual Screening - Quantum Drug Discovery Pipeline
// ==============================================================================
// Demonstrates the drugDiscovery computation expression with multiple
// screening methods: QuantumKernelSVM, VQCClassifier, and QAOADiverseSelection.
//
// Business Context:
// A pharmaceutical company wants to screen a library of candidate molecules
// to identify potential drug leads. The pipeline:
// 1. Classifies molecules by predicted activity (VQC or Kernel SVM)
// 2. Selects a diverse subset of promising candidates (QAOA)
//
// This example shows how to use each screening method.
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "nuget: MathNet.Numerics, 5.0.0"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.IO
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI INTERFACE
// ==============================================================================

let args = Cli.parse (fsi.CommandLineArgs |> Array.skip 1)

args |> Cli.exitIfHelp "DrugDiscovery/VirtualScreening.fsx"
    "Screen candidate molecules using quantum ML methods"
    [ { Name = "input";   Description = "SMILES/CSV/SDF file with candidate molecules"; Default = Some "built-in 10 compounds" }
      { Name = "output";  Description = "Write results to JSON file"; Default = None }
      { Name = "csv";     Description = "Write results to CSV file"; Default = None }
      { Name = "method";  Description = "Screening method: all, kernel, vqc, qaoa"; Default = Some "all" }
      { Name = "shots";   Description = "Quantum circuit shots"; Default = Some "100" }
      { Name = "batch";   Description = "Batch size for processing"; Default = Some "5" }
      { Name = "budget";  Description = "QAOA selection budget (float)"; Default = Some "5.0" }
      { Name = "diversity"; Description = "QAOA diversity weight (0-1)"; Default = Some "0.6" }
      { Name = "quiet";   Description = "Suppress detailed per-method output (flag)"; Default = None } ]

let scriptDir = __SOURCE_DIRECTORY__
let inputFile = args |> Cli.tryGet "input"
let outputFile = args |> Cli.tryGet "output"
let csvFile = args |> Cli.tryGet "csv"
let methodFilter = args |> Cli.getOr "method" "all" |> fun s -> s.ToLowerInvariant()
let numShots = args |> Cli.getIntOr "shots" 100
let batchSize = args |> Cli.getIntOr "batch" 5
let selectionBudget = args |> Cli.getFloatOr "budget" 5.0
let diversityWeight = args |> Cli.getFloatOr "diversity" 0.6
let quiet = args |> Cli.hasFlag "quiet"

// ==============================================================================
// DATA LOADING
// ==============================================================================

printfn "============================================================"
printfn "   Quantum Drug Discovery - Virtual Screening Pipeline"
printfn "============================================================"
printfn ""

/// Built-in SMILES data (used when no --input provided)
let builtinSmiles = [
    "CCO"
    "CCCO"
    "CCCCO"
    "CC(C)O"
    "CC(=O)O"
    "c1ccccc1"
    "c1ccc(O)cc1"
    "c1ccc(N)cc1"
    "CC(=O)Oc1ccccc1C(=O)O"
    "CN1C=NC2=C1C(=O)N(C(=O)N2C)C"
]

/// Write SMILES to a temp file for the drugDiscovery builder
let prepareSmilesFile (smilesList: string list) : string =
    let tempFile = Path.GetTempFileName() + ".smi"
    File.WriteAllText(tempFile, smilesList |> String.concat "\n")
    tempFile

let smilesData, usingExternalInput =
    match inputFile with
    | Some path ->
        let resolved = Data.resolveRelative scriptDir path
        if not (File.Exists resolved) then
            eprintfn "Error: Input file not found: %s" resolved
            exit 1
        printfn "Loading candidates from: %s" resolved
        let smiles = Data.readSmiles resolved
        if smiles.IsEmpty then
            eprintfn "Error: No SMILES found in %s" resolved
            exit 1
        printfn "Loaded %d compounds" smiles.Length
        (smiles, true)
    | None ->
        printfn "Using built-in compound library (use --input to load your own)"
        (builtinSmiles, false)

let smilesFile = prepareSmilesFile smilesData

printfn "Compounds: %d" smilesData.Length
printfn ""

// Create local backend for simulation
let localBackend = LocalBackend() :> IQuantumBackend

// ==============================================================================
// SCREENING METHODS
// ==============================================================================

/// Record for collecting results across methods
type MethodResult = {
    MethodName: string
    Status: string
    MoleculesProcessed: int
    Message: string
}

let runMethods = [
    if methodFilter = "all" || methodFilter = "kernel" then "kernel"
    if methodFilter = "all" || methodFilter = "vqc" then "vqc"
    if methodFilter = "all" || methodFilter = "qaoa" then "qaoa"
]

if runMethods.IsEmpty then
    eprintfn "Error: Unknown method '%s'. Use: all, kernel, vqc, qaoa" methodFilter
    exit 1

let mutable methodResults : MethodResult list = []

// ==============================================================================
// METHOD 1: Quantum Kernel SVM
// ==============================================================================
// Uses quantum feature maps to compute kernels for SVM classification.
// Best for: Binary classification with labeled training data.

if runMethods |> List.contains "kernel" then
    if not quiet then
        printfn "============================================================"
        printfn " Method 1: Quantum Kernel SVM"
        printfn "============================================================"
        printfn ""

    let kernelSvmResult = drugDiscovery {
        load_candidates_from_file smilesFile
        use_method QuantumKernelSVM
        use_feature_map ZZFeatureMap
        set_batch_size batchSize
        shots numShots
        backend localBackend
    }

    match kernelSvmResult with
    | Ok result ->
        if not quiet then
            printfn "[OK] Quantum Kernel SVM completed"
            printfn "  Method: %A" result.Method
            printfn "  Molecules Processed: %d" result.MoleculesProcessed
            printfn "  Result:"
            result.Message.Split('\n') |> Array.iter (printfn "    %s")
        methodResults <- methodResults @ [{
            MethodName = "QuantumKernelSVM"
            Status = "OK"
            MoleculesProcessed = result.MoleculesProcessed
            Message = result.Message
        }]
    | Error err ->
        if not quiet then
            printfn "[ERROR] %s" err.Message
        methodResults <- methodResults @ [{
            MethodName = "QuantumKernelSVM"
            Status = "ERROR"
            MoleculesProcessed = 0
            Message = err.Message
        }]
    if not quiet then printfn ""

// ==============================================================================
// METHOD 2: VQC Classifier
// ==============================================================================
// Trains a Variational Quantum Classifier using parameterized circuits.
// Best for: Learning complex decision boundaries with gradient optimization.

if runMethods |> List.contains "vqc" then
    if not quiet then
        printfn "============================================================"
        printfn " Method 2: VQC Classifier"
        printfn "============================================================"
        printfn ""

    let vqcResult = drugDiscovery {
        load_candidates_from_file smilesFile
        use_method VQCClassifier
        use_feature_map ZZFeatureMap
        
        // VQC-specific configuration
        vqc_layers 1              // Number of variational layers
        vqc_max_epochs 3          // Maximum training epochs (reduced for demo)
        
        set_batch_size (min batchSize 3)
        shots (min numShots 50)
        backend localBackend
    }

    match vqcResult with
    | Ok result ->
        if not quiet then
            printfn "[OK] VQC Classifier completed"
            printfn "  Method: %A" result.Method
            printfn "  Molecules Processed: %d" result.MoleculesProcessed
            printfn "  Result:"
            result.Message.Split('\n') |> Array.iter (printfn "    %s")
        methodResults <- methodResults @ [{
            MethodName = "VQCClassifier"
            Status = "OK"
            MoleculesProcessed = result.MoleculesProcessed
            Message = result.Message
        }]
    | Error err ->
        if not quiet then
            printfn "[ERROR] %s" err.Message
        methodResults <- methodResults @ [{
            MethodName = "VQCClassifier"
            Status = "ERROR"
            MoleculesProcessed = 0
            Message = err.Message
        }]
    if not quiet then printfn ""

// ==============================================================================
// METHOD 3: QAOA Diverse Selection
// ==============================================================================
// Uses QAOA to select a diverse subset of compounds within a budget.
// Best for: Building diverse screening libraries, avoiding redundancy.

if runMethods |> List.contains "qaoa" then
    if not quiet then
        printfn "============================================================"
        printfn " Method 3: QAOA Diverse Selection"
        printfn "============================================================"
        printfn ""

    let qaoaResult = drugDiscovery {
        load_candidates_from_file smilesFile
        use_method QAOADiverseSelection
        
        // QAOA-specific configuration
        selection_budget selectionBudget
        diversity_weight diversityWeight
        
        set_batch_size (min batchSize 8)
        shots (max numShots 200)
        backend localBackend
    }

    match qaoaResult with
    | Ok result ->
        if not quiet then
            printfn "[OK] QAOA Diverse Selection completed"
            printfn "  Method: %A" result.Method
            printfn "  Molecules Processed: %d" result.MoleculesProcessed
            printfn "  Result:"
            result.Message.Split('\n') |> Array.iter (printfn "    %s")
        methodResults <- methodResults @ [{
            MethodName = "QAOADiverseSelection"
            Status = "OK"
            MoleculesProcessed = result.MoleculesProcessed
            Message = result.Message
        }]
    | Error err ->
        if not quiet then
            printfn "[ERROR] %s" err.Message
        methodResults <- methodResults @ [{
            MethodName = "QAOADiverseSelection"
            Status = "ERROR"
            MoleculesProcessed = 0
            Message = err.Message
        }]
    if not quiet then printfn ""

// ==============================================================================
// COMPARISON TABLE
// ==============================================================================

if not quiet && methodResults.Length > 1 then
    printfn "============================================================"
    printfn " Screening Method Comparison"
    printfn "============================================================"
    printfn ""
    printfn "| %-20s | %-35s | %-20s |" "Method" "Best For" "Key Parameters"
    printfn "|%s|%s|%s|" (String.replicate 22 "-") (String.replicate 37 "-") (String.replicate 22 "-")
    printfn "| %-20s | %-35s | %-20s |" "QuantumKernelSVM" "Binary classification" "feature_map, shots"
    printfn "| %-20s | %-35s | %-20s |" "VQCClassifier" "Trainable classifier" "vqc_layers, epochs"
    printfn "| %-20s | %-35s | %-20s |" "QAOADiverseSelection" "Diverse subset selection" "budget, diversity_weight"
    printfn ""

// ==============================================================================
// CLEANUP
// ==============================================================================

File.Delete(smilesFile)

// ==============================================================================
// SUMMARY (always shown)
// ==============================================================================

printfn "============================================================"
printfn " Summary"
printfn "============================================================"
printfn ""

let okCount = methodResults |> List.filter (fun r -> r.Status = "OK") |> List.length
let errCount = methodResults |> List.filter (fun r -> r.Status = "ERROR") |> List.length

printfn "Compounds screened: %d" smilesData.Length
printfn "Methods run: %d (%d succeeded, %d failed)" methodResults.Length okCount errCount
printfn ""

for r in methodResults do
    let statusMark = if r.Status = "OK" then "[OK]" else "[FAIL]"
    printfn "  %s %-22s  Molecules: %d" statusMark r.MethodName r.MoleculesProcessed

printfn ""

if methodFilter = "all" then
    printfn "Note: VQEBindingAffinity and QuantumGNN are planned but not yet implemented."
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

/// Convert a MethodResult to a serializable map
let resultToMap (r: MethodResult) : Map<string, string> =
    Map.ofList [
        "MethodName", r.MethodName
        "Status", r.Status
        "MoleculesProcessed", string r.MoleculesProcessed
        "Message", r.Message
        "InputCompounds", string smilesData.Length
        "Shots", string numShots
        "BatchSize", string batchSize
    ]

match outputFile with
| Some path ->
    let maps = methodResults |> List.map resultToMap
    Reporting.writeJson path maps
    printfn "Results written to: %s" path
| None -> ()

match csvFile with
| Some path ->
    let header = [ "MethodName"; "Status"; "MoleculesProcessed"; "InputCompounds"; "Message" ]
    let rows =
        methodResults |> List.map (fun r ->
            [ r.MethodName; r.Status; string r.MoleculesProcessed; string smilesData.Length
              r.Message.Replace('\n', ' ') ])
    Reporting.writeCsv path header rows
    printfn "CSV results written to: %s" path
| None -> ()

// ==============================================================================
// USAGE HINTS (only when using built-in data)
// ==============================================================================

if not usingExternalInput && outputFile.IsNone && csvFile.IsNone then
    printfn "============================================================"
    printfn " Try with your own data"
    printfn "============================================================"
    printfn ""
    printfn "  # Screen a SMILES library with all methods:"
    printfn "  dotnet fsi VirtualScreening.fsx -- --input library.smi --output results.json"
    printfn ""
    printfn "  # Run only QAOA diverse selection with custom budget:"
    printfn "  dotnet fsi VirtualScreening.fsx -- --input library.smi --method qaoa --budget 8.0"
    printfn ""
    printfn "  # Quick kernel SVM screen, export CSV:"
    printfn "  dotnet fsi VirtualScreening.fsx -- --input compounds.csv --method kernel --csv hits.csv --quiet"
    printfn ""
    printfn "  # Pipeline: VirtualScreening -> ADMET filtering:"
    printfn "  dotnet fsi VirtualScreening.fsx -- --input library.smi --output hits.json"
    printfn "  dotnet fsi ADMETPrediction.fsx -- --input hits.json --csv final_candidates.csv"
    printfn ""

// ==============================================================================
// ADVANCED: Using Different Data Providers
// ==============================================================================
(*
// SDF file with 3D structures
let sdfResult = drugDiscovery {
    load_candidates_from_file "library.sdf"
    use_method VQCClassifier
    backend localBackend
}

// CSV with SMILES and labels
let csvResult = drugDiscovery {
    load_candidates_from_file "compounds.csv"  // Must have "SMILES" and "Label" columns
    use_method QuantumKernelSVM
    backend localBackend
}

// Using a provider directly
open FSharp.Azure.Quantum.Data.ChemistryDataProviders

let sdfProvider = SdfFileDatasetProvider("molecules.sdf")
let providerResult = drugDiscovery {
    load_candidates_from_provider sdfProvider
    use_method QAOADiverseSelection
    selection_budget 10.0
    backend localBackend
}
*)

// Exit with appropriate code
if errCount > 0 && okCount = 0 then
    eprintfn "Error: All screening methods failed"
    exit 1
else
    exit 0
