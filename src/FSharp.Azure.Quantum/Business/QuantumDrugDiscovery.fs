namespace FSharp.Azure.Quantum.Business

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Data
open FSharp.Azure.Quantum.Data.ChemistryDataProviders

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

/// Source of candidate molecules for screening.
/// Supports both legacy file paths and the new provider architecture.
type CandidateSource =
    /// Legacy: Load from file path (auto-detects format by extension)
    | FilePath of string
    /// Use a dataset provider (new architecture)
    | Provider of IMoleculeDatasetProvider
    /// Use an async dataset provider
    | ProviderAsync of IMoleculeDatasetProviderAsync

/// Configuration for the Quantum Drug Discovery pipeline
type DrugDiscoveryConfiguration = {
    TargetPdbPath: string option
    CandidatesPath: string option
    /// New: Provider-based candidate source (takes precedence over CandidatesPath)
    CandidateSource: CandidateSource option
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

/// Internal module for provider-based data loading
module internal ProviderDataLoader =
    
    open System.IO
    
    /// Get provider for a file path based on extension.
    /// Supports: .sdf, .mol, .pdb, .fcidump (provider-based formats)
    let tryGetProviderForPath (path: string) =
        match Path.GetExtension(path).ToLowerInvariant() with
        | ".sdf" | ".mol" -> Some (SdfFileDatasetProvider(path) :> IMoleculeDatasetProvider)
        | ".pdb" -> Some (PdbLigandDatasetProvider(path) :> IMoleculeDatasetProvider)
        | ".fcidump" -> Some (FciDumpFileDatasetProvider(path) :> IMoleculeDatasetProvider)
        | _ -> None  // CSV, SMILES handled by legacy MolecularData module
    
    /// Calculate molecular formula using Hill system ordering (C first, H second, then alphabetical)
    let private calculateFormula (atoms: MolecularData.Atom array) =
        atoms
        |> Array.countBy (fun a -> a.Element)
        |> Array.sortBy (fun (elem, _) -> 
            match elem with
            | "C" -> "0C" | "H" -> "1H" | _ -> "2" + elem)
        |> Array.map (fun (elem, count) ->
            if count = 1 then elem else $"{elem}{count}")
        |> String.concat ""
    
    /// Convert MoleculeInstance (from provider) to MolecularData.Molecule.
    let toMolecularDataMolecule (instance: MoleculeInstance) : MolecularData.Molecule =
        let charge = instance.Topology.Charge |> Option.defaultValue 0
        
        let atoms : MolecularData.Atom array =
            instance.Topology.Atoms
            |> Array.mapi (fun idx element ->
                { MolecularData.Atom.Element = element
                  MolecularData.Atom.Index = idx
                  MolecularData.Atom.Charge = charge
                  MolecularData.Atom.IsAromatic = false
                  MolecularData.Atom.ExplicitHydrogens = 0
                  MolecularData.Atom.Mass = None })
        
        let bonds : MolecularData.Bond array =
            instance.Topology.Bonds
            |> Array.map (fun (a1, a2, orderOpt) ->
                { MolecularData.Bond.Atom1 = a1
                  MolecularData.Bond.Atom2 = a2
                  MolecularData.Bond.Order = orderOpt |> Option.map int |> Option.defaultValue 1 })
        
        let smiles = 
            instance.Topology.Metadata
            |> Map.tryFind "smiles"
            |> Option.defaultWith (fun () -> instance.Topology.Atoms |> String.concat "")
        
        { MolecularData.Molecule.Smiles = smiles
          MolecularData.Molecule.Atoms = atoms
          MolecularData.Molecule.Bonds = bonds
          MolecularData.Molecule.Formula = calculateFormula atoms
          MolecularData.Molecule.ParseErrors = [] }
    
    /// Convert provider dataset to MolecularDataset
    let private toMolecularDataset (dataset: MoleculeDataset) : MolecularData.MolecularDataset =
        { MolecularData.MolecularDataset.Molecules = dataset.Molecules |> Array.map toMolecularDataMolecule
          MolecularData.MolecularDataset.Descriptors = None
          MolecularData.MolecularDataset.Fingerprints = None
          MolecularData.MolecularDataset.Labels = dataset.Labels
          MolecularData.MolecularDataset.LabelColumn = dataset.LabelColumn }
    
    /// Load molecules from a provider and convert to MolecularDataset.
    let loadFromProvider (provider: IMoleculeDatasetProvider) =
        provider.Load(DatasetQuery.All)
        |> Result.map toMolecularDataset
    
    /// Load molecules from an async provider and convert to MolecularDataset.
    let loadFromProviderAsync (provider: IMoleculeDatasetProviderAsync) = async {
        let! result = provider.LoadAsync(DatasetQuery.All)
        return result |> Result.map toMolecularDataset
    }
    
    /// Load from file path using provider if available, otherwise legacy loaders
    let loadFromFilePath (path: string) =
        if not (File.Exists path) then
            Error (QuantumError.ValidationError ("Input", $"File not found at {path}"))
        else
            match tryGetProviderForPath path with
            | Some provider -> loadFromProvider provider
            | None ->
                // Fall back to legacy MolecularData loading for CSV/SMILES
                match Path.GetExtension(path).ToLowerInvariant() with
                | ".csv" -> MolecularData.loadFromCsv path "SMILES" (Some "Label")
                | _ -> File.ReadAllLines(path) |> Array.toList |> MolecularData.loadFromSmilesList

/// Builder for the Quantum Drug Discovery DSL
type QuantumDrugDiscoveryBuilder() =
    
    let defaultConfig = {
        TargetPdbPath = None
        CandidatesPath = None
        CandidateSource = None
        Method = QuantumKernelSVM
        FeatureMap = ZZFeatureMap
        BatchSize = 10
        FingerprintSize = 8  // Small size for local simulation compatibility
        Shots = 100
        Backend = None
    }
    
    member _.Yield(_) = defaultConfig
    member _.Zero() = defaultConfig

    member _.Delay(f: unit -> DrugDiscoveryConfiguration) = f

    member _.For(state: DrugDiscoveryConfiguration, body: unit -> DrugDiscoveryConfiguration) =
        body()

    member private _.LoadCandidates(state: DrugDiscoveryConfiguration) =
        match state.CandidateSource with
        | Some (Provider provider) -> ProviderDataLoader.loadFromProvider provider
        | Some (ProviderAsync provider) -> ProviderDataLoader.loadFromProviderAsync provider |> Async.RunSynchronously
        | Some (FilePath path) -> ProviderDataLoader.loadFromFilePath path
        | None ->
            match state.CandidatesPath with
            | Some path -> ProviderDataLoader.loadFromFilePath path
            | None -> Error (QuantumError.ValidationError ("Input", "No candidates specified. Use 'load_candidates_from_file' or 'load_candidates_from_provider'."))

    member private _.MapFeatureMap featureMap =
        match featureMap with
        | ZZFeatureMap -> FeatureMapType.ZZFeatureMap 2
        | PauliFeatureMap -> FeatureMapType.PauliFeatureMap (["Z"; "ZZ"], 2)
        | ZFeatureMap -> FeatureMapType.ZZFeatureMap 1

    member private _.TrainQuantumKernelSVM (backend: IQuantumBackend) featureMap (features: float[][]) (labels: int[]) batchSize shots state =
        let limit = min features.Length batchSize
        let trainData = features.[0..limit-1]
        let trainLabels = labels.[0..limit-1]
        
        let config = { QuantumKernelSVM.defaultConfig with Verbose = false; MaxIterations = 20 }
        
        QuantumKernelSVM.train backend featureMap trainData trainLabels config shots
        |> Result.map (fun model ->
            { Message = $"Success! Model Trained.\nSupport Vectors: {model.SupportVectorIndices.Length}\nBias: {model.Bias:F4}\n\nNote: In a real scenario, this model would now score the remaining candidates."
              Method = QuantumKernelSVM
              MoleculesProcessed = limit
              Configuration = state })
        |> Result.mapError (fun e -> QuantumError.OperationError ("Training", $"Training Failed: {e.Message}"))

    member this.Run(state: DrugDiscoveryConfiguration) : QuantumResult<ScreeningResult> =
        // Load and validate candidates
        this.LoadCandidates(state)
        |> Result.mapError (fun e -> QuantumError.OperationError ("DataLoading", $"Error loading molecular data: {e.Message}"))
        |> Result.bind (fun dataset ->
            // Extract features
            let datasetWithFeats =
                dataset
                |> MolecularData.withDescriptors
                |> MolecularData.withFingerprints state.FingerprintSize
            
            MolecularData.toFeatureMatrix true true datasetWithFeats
            |> Result.mapError (fun e -> QuantumError.OperationError ("FeatureExtraction", $"Error generating features: {e.Message}")))
        |> Result.bind (fun (features, labelsOpt) ->
            // Run screening method
            match state.Method with
            | QuantumKernelSVM ->
                match state.Backend with
                | None -> Error (QuantumError.ValidationError ("Backend", "No backend provided. Use 'backend'."))
                | Some backend ->
                    let labels = labelsOpt |> Option.defaultWith (fun () -> Array.create features.Length 0)
                    let featureMap = this.MapFeatureMap state.FeatureMap
                    this.TrainQuantumKernelSVM backend featureMap features labels state.BatchSize state.Shots state
            | method ->
                Error (QuantumError.OperationError ("NotImplemented", $"Method %A{method} is not yet implemented. Please use QuantumKernelSVM.")))

    member this.Run(f: unit -> DrugDiscoveryConfiguration) : QuantumResult<ScreeningResult> =
        this.Run(f())

    /// Load target protein structure from a PDB file
    [<CustomOperation("target_protein_from_pdb")>]
    member _.TargetProteinFromPdb(state: DrugDiscoveryConfiguration, path: string) =
        { state with TargetPdbPath = Some path }

    /// Load candidate molecules from a file (SMILES, SDF, etc.)
    [<CustomOperation("load_candidates_from_file")>]
    member _.LoadCandidatesFromFile(state: DrugDiscoveryConfiguration, path: string) =
        { state with CandidatesPath = Some path; CandidateSource = Some (FilePath path) }

    /// Load candidate molecules from a dataset provider (new architecture)
    /// 
    /// Example:
    ///   let sdfProvider = SdfFileDatasetProvider("molecules.sdf")
    ///   drugDiscovery {
    ///       load_candidates_from_provider sdfProvider
    ///       ...
    ///   }
    [<CustomOperation("load_candidates_from_provider")>]
    member _.LoadCandidatesFromProvider(state: DrugDiscoveryConfiguration, provider: IMoleculeDatasetProvider) =
        { state with CandidateSource = Some (Provider provider) }

    /// Load candidate molecules from an async dataset provider
    [<CustomOperation("load_candidates_from_provider_async")>]
    member _.LoadCandidatesFromProviderAsync(state: DrugDiscoveryConfiguration, provider: IMoleculeDatasetProviderAsync) =
        { state with CandidateSource = Some (ProviderAsync provider) }

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
