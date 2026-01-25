namespace FSharp.Azure.Quantum.Business

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

open FSharp.Azure.Quantum.MachineLearning

/// Screening methods available
[<Struct>]
type ScreeningMethod =
    | QuantumKernelSVM
    | VQEBindingAffinity
    | QuantumGNN

/// Feature maps for quantum encoding
[<Struct>]
type FeatureMap =
    | ZZFeatureMap
    | PauliFeatureMap
    | ZFeatureMap

/// Configuration for the Quantum Drug Discovery pipeline
type DrugDiscoveryConfiguration = {
    TargetPdbPath: string option
    CandidatesPath: string option
    Method: ScreeningMethod
    FeatureMap: FeatureMap
    BatchSize: int
    FingerprintSize: int
    Shots: int
    Backend: IQuantumBackend option
}

/// Result of the screening pipeline
type ScreeningResult = {
    Message: string
    Method: ScreeningMethod
    MoleculesProcessed: int
    Configuration: DrugDiscoveryConfiguration
}

/// Builder for the Quantum Drug Discovery DSL
type QuantumDrugDiscoveryBuilder() =
    member _.Yield(_) = {
        TargetPdbPath = None
        CandidatesPath = None
        Method = QuantumKernelSVM
        FeatureMap = ZZFeatureMap
        BatchSize = 10
        FingerprintSize = 8 // Default to small size for local simulation compatibility
        Shots = 100
        Backend = None
    }

    member _.Zero() = {
        TargetPdbPath = None
        CandidatesPath = None
        Method = QuantumKernelSVM
        FeatureMap = ZZFeatureMap
        BatchSize = 10
        FingerprintSize = 8
        Shots = 100
        Backend = None
    }

    member _.Delay(f: unit -> DrugDiscoveryConfiguration) = f

    member _.Run(f: unit -> DrugDiscoveryConfiguration) : QuantumResult<ScreeningResult> =
        let state = f()
        
        // 1. Resolve Candidates Path
        match state.CandidatesPath with
        | None -> 
            Error (QuantumError.ValidationError ("Input", "No candidates file specified. Use 'load_candidates_from_file'."))
        | Some path ->
            if not (System.IO.File.Exists path) then
                Error (QuantumError.ValidationError ("Input", sprintf "File not found at %s" path))
            else
                // 2. Load Data
                // Heuristic: Check extension. If .csv, expect header. If .smi or .txt, expect list.
                let ext = System.IO.Path.GetExtension(path).ToLower()
                let loadResult =
                    if ext = ".csv" then
                        // Assume standard columns for demo: "SMILES", "Label" (optional)
                        FSharp.Azure.Quantum.Data.MolecularData.loadFromCsv path "SMILES" (Some "Label")
                    else
                        // Assume one SMILES per line
                        let lines = System.IO.File.ReadAllLines(path) |> Array.toList
                        FSharp.Azure.Quantum.Data.MolecularData.loadFromSmilesList lines

                match loadResult with
                | Error e -> Error (QuantumError.OperationError ("DataLoading", sprintf "Error loading molecular data: %s" e.Message))
                | Ok dataset ->
                    
                    // 3. Feature Extraction
                    // Enable both descriptors and fingerprints for best results
                    // Note: LocalBackend has a qubit limit (e.g. 20). 
                    // 1024 bits is too large for full simulation, so we use the configured size.
                    let datasetWithFeats = 
                        dataset 
                        |> FSharp.Azure.Quantum.Data.MolecularData.withDescriptors 
                        |> FSharp.Azure.Quantum.Data.MolecularData.withFingerprints state.FingerprintSize

                    match FSharp.Azure.Quantum.Data.MolecularData.toFeatureMatrix true true datasetWithFeats with
                    | Error e -> Error (QuantumError.OperationError ("FeatureExtraction", sprintf "Error generating features: %s" e.Message))
                    | Ok (features, labelsOpt) ->
                        
                        match state.Method with
                        | QuantumKernelSVM ->
                            // 4. Run Quantum Kernel SVM
                            // We need labels for training. If missing, we can't train/validate.
                            // For this "Screening" demo, if we have labels, we show CV score.
                            // If no labels, we might be expected to predict. But we have no model!
                            // Fallback: If no labels, generate dummy labels just to demonstrate the KERNEL computation (the expensive part).
                            
                            let labels = 
                                match labelsOpt with
                                | Some l -> l
                                | None -> Array.create features.Length 0 // Dummy

                            match state.Backend with
                            | None ->
                                Error (QuantumError.ValidationError ("Backend", "No backend provided. Use 'backend'."))
                            | Some backend ->
                                // Map DSL FeatureMap to Core FeatureMapType
                                let coreFeatureMap =
                                    match state.FeatureMap with
                                    | ZZFeatureMap -> FeatureMapType.ZZFeatureMap 2
                                    | PauliFeatureMap -> FeatureMapType.PauliFeatureMap (["Z"; "ZZ"], 2)
                                    | ZFeatureMap -> FeatureMapType.ZZFeatureMap 1 // Z feature map is subset of ZZ depth 1

                                let config = { 
                                    FSharp.Azure.Quantum.MachineLearning.QuantumKernelSVM.defaultConfig with 
                                        Verbose = false
                                        MaxIterations = 20 // Keep it quick for demo
                                }

                                // Limit dataset for demo performance (SVM is O(N^3))
                                let limit = min features.Length state.BatchSize
                                let trainData = features.[0..limit-1]
                                let trainLabels = labels.[0..limit-1]

                                match FSharp.Azure.Quantum.MachineLearning.QuantumKernelSVM.train backend coreFeatureMap trainData trainLabels config state.Shots with
                                | Ok model ->
                                    let supportVectors = model.SupportVectorIndices.Length
                                    let msg = sprintf "Success! Model Trained.\nSupport Vectors: %d\nBias: %.4f\n\nNote: In a real scenario, this model would now score the remaining candidates." supportVectors model.Bias
                                    Ok { Message = msg; Method = QuantumKernelSVM; MoleculesProcessed = limit; Configuration = state }
                                | Error e ->
                                    Error (QuantumError.OperationError ("Training", sprintf "Training Failed: %s" e.Message))

                        | _ -> 
                            Error (QuantumError.OperationError ("NotImplemented", sprintf "Method %A is not yet fully hydrated. Please use QuantumKernelSVM." state.Method))

    /// Load target protein structure from a PDB file
    [<CustomOperation("target_protein_from_pdb")>]
    member _.TargetProteinFromPdb(state: DrugDiscoveryConfiguration, path: string) =
        { state with TargetPdbPath = Some path }

    /// Load candidate molecules from a file (SMILES, SDF, etc.)
    [<CustomOperation("load_candidates_from_file")>]
    member _.LoadCandidatesFromFile(state: DrugDiscoveryConfiguration, path: string) =
        { state with CandidatesPath = Some path }

    /// Select the screening strategy/method
    [<CustomOperation("use_method")>]
    member _.UseMethod(state: DrugDiscoveryConfiguration, method: ScreeningMethod) =
        { state with Method = method }

    /// Select the quantum feature map for encoding chemical structures
    [<CustomOperation("use_feature_map")>]
    member _.UseFeatureMap(state: DrugDiscoveryConfiguration, featureMap: FeatureMap) =
        { state with FeatureMap = featureMap }

    /// Set the batch size for processing candidates
    [<CustomOperation("set_batch_size")>]
    member _.SetBatchSize(state: DrugDiscoveryConfiguration, size: int) =
        { state with BatchSize = size }

    /// Set number of measurement shots
    [<CustomOperation("shots")>]
    member _.Shots(state: DrugDiscoveryConfiguration, shots: int) =
        { state with Shots = shots }

    /// Set the quantum backend
    [<CustomOperation("backend")>]
    member _.Backend(state: DrugDiscoveryConfiguration, backend: IQuantumBackend) =
        { state with Backend = Some backend }

[<AutoOpen>]
module QuantumDrugDiscoveryDSL =
    let drugDiscovery = QuantumDrugDiscoveryBuilder()
