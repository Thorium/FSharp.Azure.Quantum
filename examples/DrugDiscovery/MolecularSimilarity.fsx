// ==============================================================================
// Molecular Similarity Screening Example
// ==============================================================================
// Demonstrates QUANTUM KERNEL SIMILARITY for virtual drug screening.
//
// Business Context:
// A pharmaceutical research team has identified a set of known active compounds
// against a target protein. They need to screen a library of candidate molecules
// to find structurally similar compounds that may also be active.
//
// This example shows:
// - Loading molecular data from SMILES strings
// - Computing molecular descriptors (MW, LogP, TPSA, etc.)
// - Generating molecular fingerprints
// - **QUANTUM kernel similarity using feature maps** (PRIMARY METHOD)
// - Classical Tanimoto similarity (baseline for comparison)
// - Ranking candidates by similarity to known actives
//
// Quantum Advantage:
// Quantum kernels can capture complex non-linear relationships in molecular
// feature space that may be missed by classical fingerprint methods. The
// ZZ-feature map encodes pairwise correlations that are classically intractable.
//
// IMPORTANT CAVEAT - Formulation Considerations:
// Similar molecular structures may require DIFFERENT formulation strategies!
// - Similar LogP does not guarantee similar BCS class
// - Polymorphism varies between compounds (see Ritonavir example)
// - Different salt forms may be needed for similar APIs
// See: _data/PHARMA_GLOSSARY.md -> "Formulation Strategies for Poorly Soluble Drugs"
//
// ==============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.IO
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Data
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI INTERFACE
// ==============================================================================

let args = Cli.parse (fsi.CommandLineArgs |> Array.skip 1)

args |> Cli.exitIfHelp "DrugDiscovery/MolecularSimilarity.fsx"
    "Screen candidate molecules by quantum kernel similarity to known actives"
    [ { Name = "actives";   Description = "SMILES/CSV file with known active compounds"; Default = Some "built-in 3 kinase inhibitors" }
      { Name = "library";   Description = "SMILES/CSV file with candidate library"; Default = Some "built-in 10 candidates" }
      { Name = "output";    Description = "Write results to JSON file"; Default = None }
      { Name = "csv";       Description = "Write results to CSV file"; Default = None }
      { Name = "top";       Description = "Return top N candidates by similarity"; Default = Some "5" }
      { Name = "threshold"; Description = "Similarity threshold for hit classification"; Default = Some "0.7" }
      { Name = "quiet";     Description = "Suppress detailed per-compound output (flag)"; Default = None } ]

let scriptDir = __SOURCE_DIRECTORY__
let activesFile = args |> Cli.tryGet "actives"
let libraryFile = args |> Cli.tryGet "library"
let outputFile = args |> Cli.tryGet "output"
let csvFile = args |> Cli.tryGet "csv"
let topN = args |> Cli.getIntOr "top" 5
let similarityThreshold = args |> Cli.getFloatOr "threshold" 0.7
let quiet = args |> Cli.hasFlag "quiet"

// ==============================================================================
// CONFIGURATION
// ==============================================================================

/// Number of fingerprint bits
let fingerprintBits = 1024

/// Number of quantum circuit shots
let quantumShots = 1000

// ==============================================================================
// DATA LOADING
// ==============================================================================

printfn "=============================================="
printfn " Quantum Molecular Similarity Screening"
printfn "=============================================="
printfn ""

/// Built-in known actives: kinase inhibitors
let builtinActives = [
    // Imatinib (Gleevec) - BCR-ABL kinase inhibitor for CML
    "CC1=C(C=C(C=C1)NC(=O)C2=CC=C(C=C2)CN3CCN(CC3)C)NC4=NC=CC(=N4)C5=CN=CC=C5"
    
    // Gefitinib (Iressa) - EGFR inhibitor  
    "COC1=C(C=C2C(=C1)N=CN=C2NC3=CC(=C(C=C3)F)Cl)OCCCN4CCOCC4"
    
    // Erlotinib (Tarceva) - EGFR inhibitor
    "COC1=C(C=C2C(=C1)C(=NC=N2)NC3=CC=CC(=C3)C#C)OCCOC"
]

/// Built-in candidate library
let builtinLibrary = [
    // Potentially similar kinase inhibitors
    "CC1=C(C=C(C=C1)NC(=O)C2=CC=CC=C2)NC3=NC=CC(=N3)C4=CC=CN=C4"  // Simplified imatinib analog
    "COC1=CC2=C(C=C1OC)C(=NC=N2)NC3=CC(=CC=C3)Cl"                  // Gefitinib analog
    "COC1=CC2=NC=NC(=C2C=C1OCC)NC3=CC=CC=C3"                       // Erlotinib analog
    
    // Structurally different molecules (negative controls)
    "CC(=O)OC1=CC=CC=C1C(=O)O"                                      // Aspirin
    "CC(C)CC1=CC=C(C=C1)C(C)C(=O)O"                                 // Ibuprofen
    "CN1C=NC2=C1C(=O)N(C(=O)N2C)C"                                  // Caffeine
    "CC(=O)NC1=CC=C(C=C1)O"                                         // Paracetamol
    
    // Additional kinase-like scaffolds
    "C1=CC=C(C=C1)NC2=NC=NC3=CC=CC=C32"                            // Quinazoline scaffold
    "C1=CN=C(N=C1)NC2=CC=C(C=C2)F"                                 // Pyrimidine-aniline
    "CC1=CN=C(N=C1N)C2=CC=CC=C2"                                   // Another pyrimidine
]

/// Load SMILES from a file or return built-in data
let loadSmiles (filePath: string option) (builtinData: string list) (label: string) =
    match filePath with
    | Some path ->
        let resolved = Data.resolveRelative scriptDir path
        if not (File.Exists resolved) then
            eprintfn "Error: %s file not found: %s" label resolved
            exit 1
        printfn "Loading %s from: %s" label resolved
        let smiles = Data.readSmiles resolved
        if smiles.IsEmpty then
            eprintfn "Error: No SMILES found in %s" resolved
            exit 1
        printfn "  Loaded %d compounds" smiles.Length
        smiles
    | None ->
        builtinData

let knownActives = loadSmiles activesFile builtinActives "actives"
let candidateLibrary = loadSmiles libraryFile builtinLibrary "library"

let usingExternalInput = activesFile.IsSome || libraryFile.IsSome

// ==============================================================================
// MOLECULAR DATA PROCESSING
// ==============================================================================

printfn "Loading molecular data..."

let parseAndDescribe smiles =
    match MolecularData.parseSmiles smiles with
    | Ok mol -> 
        let desc = MolecularData.calculateDescriptors mol
        let fp = MolecularData.generateFingerprint mol fingerprintBits
        Some (mol, desc, fp)
    | Error _ -> 
        if not quiet then
            printfn "  Warning: Failed to parse SMILES: %s" (smiles.Substring(0, min 30 smiles.Length) + "...")
        None

let activeData = knownActives |> List.choose parseAndDescribe
let candidateData = candidateLibrary |> List.choose parseAndDescribe

printfn "  Parsed %d active compounds" activeData.Length
printfn "  Parsed %d candidate compounds" candidateData.Length
printfn ""

if candidateData.IsEmpty then
    eprintfn "Error: No valid candidate compounds to screen"
    exit 1

// ==============================================================================
// CLASSICAL SIMILARITY (BASELINE FOR COMPARISON)
// ==============================================================================

if not quiet then
    printfn "Computing classical Tanimoto similarity (baseline)..."
    printfn ""

/// Compute average Tanimoto similarity to all known actives
let computeAverageTanimoto (candidateFp: MolecularData.MolecularFingerprint) =
    activeData
    |> List.map (fun (_, _, activeFp) -> MolecularData.tanimotoSimilarity candidateFp activeFp)
    |> List.average

let classicalResults =
    candidateData
    |> List.mapi (fun i (mol, desc, fp) ->
        let avgSim = computeAverageTanimoto fp
        (i, mol.Smiles, desc, avgSim))
    |> List.sortByDescending (fun (_, _, _, sim) -> sim)

if not quiet then
    printfn "Classical Tanimoto Similarity Results:"
    printfn "---------------------------------------"
    for (idx, smiles, desc, sim) in classicalResults |> List.take (min topN classicalResults.Length) do
        let shortSmiles = if smiles.Length > 40 then smiles.Substring(0, 37) + "..." else smiles
        let hitStatus = if sim >= similarityThreshold then "[HIT]" else "     "
        printfn "  %s %.3f | MW=%.1f LogP=%.2f | %s" hitStatus sim desc.MolecularWeight desc.LogP shortSmiles
    printfn ""

// ==============================================================================
// QUANTUM KERNEL SIMILARITY (PRIMARY METHOD)
// ==============================================================================

if not quiet then
    printfn "=============================================="
    printfn " QUANTUM KERNEL SIMILARITY (PRIMARY)"
    printfn "=============================================="
    printfn ""

// Create quantum backend
let backend = LocalBackend() :> IQuantumBackend
if not quiet then
    printfn "  Backend: %s" backend.Name

// Extract molecular features for quantum encoding
let extractFeatures (desc: MolecularData.MolecularDescriptors) : float array =
    [|
        desc.MolecularWeight / 600.0 |> min 1.0
        (desc.LogP + 3.0) / 10.0 |> max 0.0 |> min 1.0
        float desc.HydrogenBondDonors / 5.0 |> min 1.0
        float desc.HydrogenBondAcceptors / 10.0 |> min 1.0
        desc.TPSA / 150.0 |> min 1.0
        float desc.RotatableBonds / 12.0 |> min 1.0
        desc.FractionCsp3 |> min 1.0
        float desc.AromaticRingCount / 4.0 |> min 1.0
    |]

// Prepare feature vectors
let activeFeatures = 
    activeData 
    |> List.map (fun (_, desc, _) -> extractFeatures desc)
    |> List.toArray

let candidateFeatures =
    candidateData
    |> List.map (fun (_, desc, _) -> extractFeatures desc)
    |> List.toArray

if not quiet then
    printfn "  Feature dimensions: %d" (if activeFeatures.Length > 0 then activeFeatures.[0].Length else 0)
    printfn "  Active compounds: %d" activeFeatures.Length
    printfn "  Candidates: %d" candidateFeatures.Length
    printfn ""
    printfn "  Computing quantum kernels..."

let featureMap = FeatureMapType.ZZFeatureMap 2  // depth=2 for ZZ feature map

/// Compute quantum kernel similarity between two feature vectors
let computeQuantumSimilarity (x1: float array) (x2: float array) : float =
    let data = [| x1; x2 |]
    match QuantumKernels.computeKernelMatrix backend featureMap data quantumShots with
    | Ok matrix -> matrix.[0, 1]
    | Error _ -> 0.0

/// Compute average quantum similarity to all known actives
let computeAverageQuantumSimilarity (candidateVec: float array) =
    activeFeatures
    |> Array.map (fun activeVec -> computeQuantumSimilarity candidateVec activeVec)
    |> Array.average

let quantumResults =
    candidateData
    |> List.mapi (fun i (mol, desc, _) ->
        let features = candidateFeatures.[i]
        let avgSim = computeAverageQuantumSimilarity features
        (i, mol.Smiles, desc, avgSim))
    |> List.sortByDescending (fun (_, _, _, sim) -> sim)

if not quiet then
    printfn ""
    printfn "Quantum Kernel Similarity Results:"
    printfn "-----------------------------------"
    for (idx, smiles, desc, sim) in quantumResults |> List.take (min topN quantumResults.Length) do
        let shortSmiles = if smiles.Length > 40 then smiles.Substring(0, 37) + "..." else smiles
        let hitStatus = if sim >= similarityThreshold then "[HIT]" else "     "
        printfn "  %s %.3f | MW=%.1f LogP=%.2f | %s" hitStatus sim desc.MolecularWeight desc.LogP shortSmiles
    printfn ""

// ==============================================================================
// COMPARISON AND ANALYSIS
// ==============================================================================

if not quiet then
    printfn "=============================================="
    printfn " Method Comparison"
    printfn "=============================================="
    printfn ""

    let classicalRanks = 
        classicalResults 
        |> List.mapi (fun rank (i, _, _, _) -> (i, rank + 1))
        |> Map.ofList

    let quantumRanks =
        quantumResults
        |> List.mapi (fun rank (i, _, _, _) -> (i, rank + 1))
        |> Map.ofList

    printfn "Rank Comparison (Candidate Index -> Classical Rank | Quantum Rank):"
    printfn ""
    for i in 0 .. candidateData.Length - 1 do
        let classicalRank = classicalRanks.[i]
        let quantumRank = quantumRanks.[i]
        let movement = 
            if quantumRank < classicalRank then sprintf "(+%d)" (classicalRank - quantumRank)
            elif quantumRank > classicalRank then sprintf "(-%d)" (quantumRank - classicalRank)
            else "(=)"
        printfn "  Candidate %d: Classical #%d | Quantum #%d %s" i classicalRank quantumRank movement
    printfn ""

// Count hits by each method
let classicalHits = 
    classicalResults 
    |> List.filter (fun (_, _, _, sim) -> sim >= similarityThreshold)
    |> List.length

let quantumHits =
    quantumResults
    |> List.filter (fun (_, _, _, sim) -> sim >= similarityThreshold)
    |> List.length

// ==============================================================================
// LIPINSKI'S RULE OF 5 CHECK (Drug-likeness)
// ==============================================================================

if not quiet then
    printfn "=============================================="
    printfn " Drug-likeness Analysis (Lipinski's Rule of 5)"
    printfn "=============================================="
    printfn ""

    let checkLipinski (desc: MolecularData.MolecularDescriptors) =
        let violations = [
            if desc.MolecularWeight > 500.0 then "MW > 500"
            if desc.LogP > 5.0 then "LogP > 5"
            if desc.HydrogenBondDonors > 5 then "HBD > 5"
            if desc.HydrogenBondAcceptors > 10 then "HBA > 10"
        ]
        violations

    printfn "Top Quantum Hits - Lipinski Analysis:"
    for (idx, smiles, desc, sim) in quantumResults |> List.take (min 5 quantumResults.Length) do
        let violations = checkLipinski desc
        let status = if violations.IsEmpty then "PASS" else sprintf "FAIL: %s" (String.concat ", " violations)
        let shortSmiles = if smiles.Length > 35 then smiles.Substring(0, 32) + "..." else smiles
        printfn "  Sim=%.3f | %s | %s" sim status shortSmiles
    printfn ""

// ==============================================================================
// SUMMARY (always shown)
// ==============================================================================

printfn "=============================================="
printfn " Screening Summary"
printfn "=============================================="
printfn ""
printfn "Active compounds: %d" activeData.Length
printfn "Candidates screened: %d" candidateData.Length
printfn "Similarity threshold: %.2f" similarityThreshold
printfn ""
printfn "Hit Summary (similarity >= %.2f):" similarityThreshold
printfn "  Classical Tanimoto: %d hits" classicalHits
printfn "  Quantum Kernel: %d hits" quantumHits
printfn ""

printfn "Next Steps:"
printfn "  1. Validate top hits experimentally"
printfn "  2. Perform docking studies on hits"
printfn "  3. Consider ADMET properties (see ADMETPrediction.fsx)"
printfn "  4. Evaluate BCS class for formulation planning"
printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

/// Convert screening result to a serializable map
let resultToMap (idx: int, smiles: string, desc: MolecularData.MolecularDescriptors, quantumSim: float) =
    let classicalSim =
        classicalResults
        |> List.tryFind (fun (i, _, _, _) -> i = idx)
        |> Option.map (fun (_, _, _, sim) -> sim)
        |> Option.defaultValue 0.0
    Map.ofList [
        "CandidateIndex", string idx
        "Smiles", smiles
        "QuantumSimilarity", sprintf "%.4f" quantumSim
        "ClassicalSimilarity", sprintf "%.4f" classicalSim
        "MolecularWeight", sprintf "%.1f" desc.MolecularWeight
        "LogP", sprintf "%.2f" desc.LogP
        "HBondDonors", string desc.HydrogenBondDonors
        "HBondAcceptors", string desc.HydrogenBondAcceptors
        "TPSA", sprintf "%.1f" desc.TPSA
        "RotatableBonds", string desc.RotatableBonds
        "IsQuantumHit", string (quantumSim >= similarityThreshold)
        "IsClassicalHit", string (classicalSim >= similarityThreshold)
    ]

let topResults = quantumResults |> List.take (min topN quantumResults.Length)

match outputFile with
| Some path ->
    let maps = topResults |> List.map resultToMap
    Reporting.writeJson path maps
    printfn "Results written to: %s" path
| None -> ()

match csvFile with
| Some path ->
    let header = [
        "CandidateIndex"; "Smiles"; "QuantumSimilarity"; "ClassicalSimilarity"
        "MolecularWeight"; "LogP"; "TPSA"; "IsQuantumHit"
    ]
    let rows =
        topResults |> List.map (fun (idx, smiles, desc, qSim) ->
            let cSim =
                classicalResults
                |> List.tryFind (fun (i, _, _, _) -> i = idx)
                |> Option.map (fun (_, _, _, sim) -> sim)
                |> Option.defaultValue 0.0
            [ string idx; smiles; sprintf "%.4f" qSim; sprintf "%.4f" cSim
              sprintf "%.1f" desc.MolecularWeight; sprintf "%.2f" desc.LogP
              sprintf "%.1f" desc.TPSA; string (qSim >= similarityThreshold) ])
    Reporting.writeCsv path header rows
    printfn "CSV results written to: %s" path
| None -> ()

// ==============================================================================
// USAGE HINTS (only when using built-in data)
// ==============================================================================

if not usingExternalInput && outputFile.IsNone && csvFile.IsNone then
    printfn "=============================================="
    printfn " Try with your own data"
    printfn "=============================================="
    printfn ""
    printfn "  # Screen a library against your own actives:"
    printfn "  dotnet fsi MolecularSimilarity.fsx -- --actives my_actives.smi --library candidates.smi"
    printfn ""
    printfn "  # Export top 10 hits as JSON for pipeline use:"
    printfn "  dotnet fsi MolecularSimilarity.fsx -- --library compounds.csv --top 10 --output hits.json"
    printfn ""
    printfn "  # Strict threshold, CSV export, quiet mode:"
    printfn "  dotnet fsi MolecularSimilarity.fsx -- --actives actives.csv --library library.smi --threshold 0.8 --csv hits.csv --quiet"
    printfn ""
    printfn "  # Pipeline: Similarity screening -> ADMET prediction:"
    printfn "  dotnet fsi MolecularSimilarity.fsx -- --library library.smi --output sim_hits.json"
    printfn "  dotnet fsi ADMETPrediction.fsx -- --input sim_hits.json --csv admet_results.csv"
    printfn ""
    printfn "NOTE: Similar structures may need different formulations!"
    printfn "  See: _data/PHARMA_GLOSSARY.md -> Formulation Strategies"
    printfn ""

// Exit with appropriate code
if candidateData.IsEmpty then
    exit 1
else
    exit 0
