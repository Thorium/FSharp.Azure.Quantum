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
// RULE1 COMPLIANCE:
// This example follows RULE1 from QUANTUM_BUSINESS_EXAMPLES_ROADMAP.md:
// "All public APIs require backend: IQuantumBackend parameter"
// The quantum kernel computation uses IQuantumBackend throughout.
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Data
open FSharp.Azure.Quantum.MachineLearning

// ==============================================================================
// CONFIGURATION
// ==============================================================================

/// Number of fingerprint bits
let fingerprintBits = 1024

/// Number of quantum circuit shots
let quantumShots = 1000

/// Top-N candidates to return
let topN = 5

/// Similarity threshold for "hit" classification
let similarityThreshold = 0.7

// ==============================================================================
// SAMPLE DATA - Drug Discovery Dataset
// ==============================================================================
// SMILES strings for known active compounds against a hypothetical kinase target
// These are real drug molecules (approved kinase inhibitors)

/// Known active compounds (query set)
let knownActives = [
    // Imatinib (Gleevec) - BCR-ABL inhibitor
    "CC1=C(C=C(C=C1)NC(=O)C2=CC=C(C=C2)CN3CCN(CC3)C)NC4=NC=CC(=N4)C5=CN=CC=C5"
    
    // Gefitinib (Iressa) - EGFR inhibitor  
    "COC1=C(C=C2C(=C1)N=CN=C2NC3=CC(=C(C=C3)F)Cl)OCCCN4CCOCC4"
    
    // Erlotinib (Tarceva) - EGFR inhibitor
    "COC1=C(C=C2C(=C1)C(=NC=N2)NC3=CC=CC(=C3)C#C)OCCOC"
]

/// Candidate compounds to screen (library)
/// Mix of potentially active and inactive molecules
let candidateLibrary = [
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

// ==============================================================================
// MOLECULAR DATA PROCESSING
// ==============================================================================

printfn "=============================================="
printfn " Quantum Molecular Similarity Screening"
printfn "=============================================="
printfn ""

// Parse molecules
printfn "Loading molecular data..."

let parseAndDescribe smiles =
    match MolecularData.parseSmiles smiles with
    | Ok mol -> 
        let desc = MolecularData.calculateDescriptors mol
        let fp = MolecularData.generateFingerprint mol fingerprintBits
        Some (mol, desc, fp)
    | Error _ -> 
        printfn "  Warning: Failed to parse SMILES: %s" (smiles.Substring(0, min 30 smiles.Length) + "...")
        None

let activeData = knownActives |> List.choose parseAndDescribe
let candidateData = candidateLibrary |> List.choose parseAndDescribe

printfn "  Parsed %d active compounds" activeData.Length
printfn "  Parsed %d candidate compounds" candidateData.Length
printfn ""

// ==============================================================================
// CLASSICAL SIMILARITY (BASELINE FOR COMPARISON)
// ==============================================================================
// Classical Tanimoto similarity provides a baseline for validating
// quantum results. Quantum kernels should capture additional correlations.

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
// Quantum kernels use feature maps to embed molecular descriptors into
// a quantum Hilbert space. The kernel K(x,y) = |<φ(x)|φ(y)>|² captures
// complex non-linear relationships via quantum interference.
//
// ZZ Feature Map:
// The ZZ feature map encodes pairwise correlations between features using
// entangling ZZ gates: U(x) = ∏_i exp(i x_i Z_i) ∏_{i,j} exp(i x_i x_j Z_i Z_j)
// These correlations are classically intractable to simulate efficiently.

printfn "=============================================="
printfn " QUANTUM KERNEL SIMILARITY (PRIMARY)"
printfn "=============================================="
printfn ""

// Create quantum backend
let backend = LocalBackend() :> IQuantumBackend
printfn "  Backend: %s" backend.Name

// Extract molecular features for quantum encoding
// Use normalized descriptors as feature vectors
let extractFeatures (desc: MolecularData.MolecularDescriptors) : float array =
    [|
        // Normalize features to [0, 1] range for quantum encoding
        desc.MolecularWeight / 600.0 |> min 1.0   // MW typically < 500 for drug-like
        (desc.LogP + 3.0) / 10.0 |> max 0.0 |> min 1.0  // LogP typically -3 to 7
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

printfn "  Feature dimensions: %d" (if activeFeatures.Length > 0 then activeFeatures.[0].Length else 0)
printfn "  Active compounds: %d" activeFeatures.Length
printfn "  Candidates: %d" candidateFeatures.Length
printfn ""

// Compute quantum kernel similarities
printfn "  Computing quantum kernels..."

let featureMap = FeatureMapType.ZZFeatureMap 2  // depth=2 for ZZ feature map

/// Compute quantum kernel similarity between two feature vectors
let computeQuantumSimilarity (x1: float array) (x2: float array) : float =
    // Combine features into a single array for kernel matrix computation
    let data = [| x1; x2 |]
    match QuantumKernels.computeKernelMatrix backend featureMap data quantumShots with
    | Ok matrix -> matrix.[0, 1]  // Off-diagonal element is the similarity
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

printfn "=============================================="
printfn " Method Comparison"
printfn "=============================================="
printfn ""

// Compare rankings
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

printfn "Hit Summary (similarity >= %.1f):" similarityThreshold
printfn "  Classical Tanimoto: %d hits" classicalHits
printfn "  Quantum Kernel: %d hits" quantumHits
printfn ""

// ==============================================================================
// LIPINSKI'S RULE OF 5 CHECK (Drug-likeness)
// ==============================================================================

printfn "=============================================="
printfn " Drug-likeness Analysis (Lipinski's Rule of 5)"
printfn "=============================================="
printfn ""

/// Check Lipinski's Rule of 5
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

printfn "=============================================="
printfn " Screening Complete"
printfn "=============================================="
printfn ""
printfn "Next Steps:"
printfn "  1. Validate top hits experimentally"
printfn "  2. Perform docking studies on hits"
printfn "  3. Consider ADMET properties"
printfn "  4. Iterate with refined query compounds"
