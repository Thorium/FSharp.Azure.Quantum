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

open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

printfn "============================================================"
printfn "   Quantum Drug Discovery - Virtual Screening Pipeline"
printfn "============================================================"
printfn ""

// Create local backend for simulation
let localBackend = LocalBackend() :> IQuantumBackend

// ==============================================================================
// EXAMPLE DATA
// ==============================================================================
// In production, you would load from SDF, CSV, or other molecular data files.
// For this example, we create a simple SMILES file.

let exampleSmiles = """CCO
CCCO
CCCCO
CC(C)O
CC(=O)O
c1ccccc1
c1ccc(O)cc1
c1ccc(N)cc1
CC(=O)Oc1ccccc1C(=O)O
CN1C=NC2=C1C(=O)N(C(=O)N2C)C"""

// Write example data to temp file
let tempSmilesFile = System.IO.Path.GetTempFileName() + ".smi"
System.IO.File.WriteAllText(tempSmilesFile, exampleSmiles)

printfn "Example molecules: %d compounds" (exampleSmiles.Split('\n').Length)
printfn "Temp file: %s" tempSmilesFile
printfn ""

// ==============================================================================
// METHOD 1: Quantum Kernel SVM
// ==============================================================================
// Uses quantum feature maps to compute kernels for SVM classification.
// Best for: Binary classification with labeled training data.

printfn "============================================================"
printfn " Method 1: Quantum Kernel SVM"
printfn "============================================================"
printfn ""

let kernelSvmResult = drugDiscovery {
    load_candidates_from_file tempSmilesFile
    use_method QuantumKernelSVM
    use_feature_map ZZFeatureMap
    set_batch_size 5
    shots 100
    backend localBackend
}

match kernelSvmResult with
| Ok result ->
    printfn "[OK] Quantum Kernel SVM completed"
    printfn "  Method: %A" result.Method
    printfn "  Molecules Processed: %d" result.MoleculesProcessed
    printfn "  Result:"
    result.Message.Split('\n') |> Array.iter (printfn "    %s")
| Error err ->
    printfn "[ERROR] %s" err.Message
printfn ""

// ==============================================================================
// METHOD 2: VQC Classifier
// ==============================================================================
// Trains a Variational Quantum Classifier using parameterized circuits.
// Best for: Learning complex decision boundaries with gradient optimization.

printfn "============================================================"
printfn " Method 2: VQC Classifier"
printfn "============================================================"
printfn ""

let vqcResult = drugDiscovery {
    load_candidates_from_file tempSmilesFile
    use_method VQCClassifier
    use_feature_map ZZFeatureMap
    
    // VQC-specific configuration
    vqc_layers 1              // Number of variational layers
    vqc_max_epochs 3          // Maximum training epochs (reduced for demo)
    
    set_batch_size 3
    shots 50
    backend localBackend
}

match vqcResult with
| Ok result ->
    printfn "[OK] VQC Classifier completed"
    printfn "  Method: %A" result.Method
    printfn "  Molecules Processed: %d" result.MoleculesProcessed
    printfn "  Result:"
    result.Message.Split('\n') |> Array.iter (printfn "    %s")
| Error err ->
    printfn "[ERROR] %s" err.Message
printfn ""

// ==============================================================================
// METHOD 3: QAOA Diverse Selection
// ==============================================================================
// Uses QAOA to select a diverse subset of compounds within a budget.
// Best for: Building diverse screening libraries, avoiding redundancy.

printfn "============================================================"
printfn " Method 3: QAOA Diverse Selection"
printfn "============================================================"
printfn ""

let qaoaResult = drugDiscovery {
    load_candidates_from_file tempSmilesFile
    use_method QAOADiverseSelection
    
    // QAOA-specific configuration
    selection_budget 5.0      // Total "cost" budget for selection
    diversity_weight 0.6      // Weight for diversity vs value (0-1)
    
    set_batch_size 8
    shots 200
    backend localBackend
}

match qaoaResult with
| Ok result ->
    printfn "[OK] QAOA Diverse Selection completed"
    printfn "  Method: %A" result.Method
    printfn "  Molecules Processed: %d" result.MoleculesProcessed
    printfn "  Result:"
    result.Message.Split('\n') |> Array.iter (printfn "    %s")
| Error err ->
    printfn "[ERROR] %s" err.Message
printfn ""

// ==============================================================================
// COMPARISON TABLE
// ==============================================================================

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

System.IO.File.Delete(tempSmilesFile)

printfn "============================================================"
printfn " Summary"
printfn "============================================================"
printfn ""
printfn "[OK] Demonstrated all three implemented screening methods:"
printfn "  1. QuantumKernelSVM - Quantum kernel-based classification"
printfn "  2. VQCClassifier - Variational quantum classifier"
printfn "  3. QAOADiverseSelection - QAOA-based diverse selection"
printfn ""
printfn "Note: VQEBindingAffinity and QuantumGNN are planned but not yet implemented."
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
