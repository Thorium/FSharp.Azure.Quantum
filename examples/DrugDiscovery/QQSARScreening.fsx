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

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Business

printfn "╔══════════════════════════════════════════════════════════════╗"
printfn "║           Q-QSAR Virtual Screening Pipeline                  ║"
printfn "╚══════════════════════════════════════════════════════════════╝"
printfn ""

// Execute the screening pipeline using the DSL
let screeningSummary =
    drugDiscovery {
        // 1. Target Definition
        // Load the 3D structure of the target protein (e.g., SARS-CoV-2 Mpro)
        target_protein_from_pdb "3CL_protease.pdb"
        
        // 2. Candidate Library
        // Load candidate molecules from a SMILES file
        // (Mock file path for this example)
        load_candidates_from_file "chembl_subset_kinase_inhibitors.smi"
        
        // 3. Strategy
        // Use Quantum Kernel SVM for non-linear classification
        use_method ScreeningMethod.QuantumKernelSVM
        
        // Map chemical features to quantum Hilbert space using ZZ interactions
        use_feature_map FeatureMap.ZZFeatureMap
        
        // 4. Execution
        set_batch_size 50
        run_virtual_screening
    }

// Display the output
printfn "%s" screeningSummary
printfn ""
printfn "Business Value:"
printfn "  - Non-linear separation of 'Active' vs 'Inactive' compounds."
printfn "  - Enhanced detection of novel scaffolds (scaffold hopping)."
printfn "  - Reduced false negatives compared to linear classical QSAR."
printfn ""
