namespace FSharp.Azure.Quantum.Business

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Data
open FSharp.Azure.Quantum.Data.ChemistryDataProviders

open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.Quantum

/// Screening methods available
[<Struct>]
type ScreeningMethod =
    /// Quantum Kernel SVM - Uses quantum feature maps for kernel-based classification
    | QuantumKernelSVM
    /// Variational Quantum Classifier - Uses parameterized quantum circuits for classification
    | VQCClassifier
    /// QAOA Diverse Selection - Uses QAOA to select diverse, high-value compounds within budget
    | QAOADiverseSelection

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
    /// VQC-specific: Number of variational ansatz layers (default: 2)
    VQCLayers: int
    /// VQC-specific: Maximum training epochs (default: 50)
    VQCMaxEpochs: int
    /// QAOA-specific: Budget constraint for diverse selection (default: 10.0)
    SelectionBudget: float
    /// QAOA-specific: Weight for diversity bonus (default: 0.5)
    DiversityWeight: float
}

/// A candidate molecule scored by the trained quantum model.
type ScoredCandidate = {
    /// Index of the molecule within the loaded candidate set.
    Index: int
    /// Molecule identifier (SMILES when available, otherwise a generated id).
    Identifier: string
    /// Quantum-derived screening score — higher means more promising. For the
    /// classifier methods this is the model's decision value / active-class
    /// probability; for QAOA it is the activity value of the selected compound.
    Score: float
    /// Whether a classifier predicts this candidate as an active hit.
    /// None for QAOA diverse selection (which selects rather than classifies).
    PredictedActive: bool option
}

/// Result of the screening pipeline
type ScreeningResult = {
    Message: string
    Method: ScreeningMethod
    MoleculesProcessed: int
    /// Candidates scored by the trained quantum model, ranked best-first. For the
    /// classifier methods this covers the whole candidate pool; for QAOA it is the
    /// selected diverse set.
    RankedCandidates: ScoredCandidate array
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
/// Helpers for scoring candidate molecules with a trained quantum model.
module internal ScreeningScoring =

    /// Collapse an array of Results into a Result of array, propagating the first error.
    let sequence (results: Result<'a, 'e> array) : Result<'a array, 'e> =
        (Ok [], results)
        ||> Array.fold (fun acc r ->
            match acc, r with
            | Error e, _ -> Error e
            | Ok xs, Ok x -> Ok (x :: xs)
            | Ok _, Error e -> Error e)
        |> Result.map (List.rev >> Array.ofList)

    /// Identifier for the i-th candidate: its SMILES when available, else a generated id.
    let identifierAt (identifiers: string array) (i: int) =
        if i < identifiers.Length && not (System.String.IsNullOrWhiteSpace identifiers.[i])
        then identifiers.[i]
        else $"Mol_{i}"

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
        VQCLayers = 2
        VQCMaxEpochs = 50
        SelectionBudget = 10.0
        DiversityWeight = 0.5
    }
    
    member _.Yield(_) = defaultConfig
    member _.Zero() = defaultConfig

    member _.Delay(f: unit -> DrugDiscoveryConfiguration) = f

    member _.For(state: DrugDiscoveryConfiguration, body: unit -> DrugDiscoveryConfiguration) =
        body()

    member private _.LoadCandidates(state: DrugDiscoveryConfiguration) =
        match state.CandidateSource with
        | Some (Provider provider) -> ProviderDataLoader.loadFromProvider provider
        | Some (ProviderAsync provider) ->
            // Note: This uses Async.RunSynchronously on a private member.
            // Use LoadCandidatesAsync for non-blocking execution.
            ProviderDataLoader.loadFromProviderAsync provider |> Async.RunSynchronously
        | Some (FilePath path) -> ProviderDataLoader.loadFromFilePath path
        | None ->
            match state.CandidatesPath with
            | Some path -> ProviderDataLoader.loadFromFilePath path
            | None -> Error (QuantumError.ValidationError ("Input", "No candidates specified. Use 'load_candidates_from_file' or 'load_candidates_from_provider'."))

    /// Non-blocking variant of LoadCandidates that awaits async providers via task CE.
    member private _.LoadCandidatesAsync(state: DrugDiscoveryConfiguration, cancellationToken: System.Threading.CancellationToken) =
        task {
            cancellationToken.ThrowIfCancellationRequested()
            match state.CandidateSource with
            | Some (Provider provider) -> return ProviderDataLoader.loadFromProvider provider
            | Some (ProviderAsync provider) ->
                let! result = ProviderDataLoader.loadFromProviderAsync provider |> Async.StartAsTask
                return result
            | Some (FilePath path) -> return ProviderDataLoader.loadFromFilePath path
            | None ->
                match state.CandidatesPath with
                | Some path -> return ProviderDataLoader.loadFromFilePath path
                | None -> return Error (QuantumError.ValidationError ("Input", "No candidates specified. Use 'load_candidates_from_file' or 'load_candidates_from_provider'."))
        }

    member private _.MapFeatureMap featureMap =
        match featureMap with
        | ZZFeatureMap -> FeatureMapType.ZZFeatureMap 2
        | PauliFeatureMap -> FeatureMapType.PauliFeatureMap (["Z"; "ZZ"], 2)
        | ZFeatureMap -> FeatureMapType.ZZFeatureMap 1

    member private _.TrainQuantumKernelSVM (backend: IQuantumBackend) featureMap (features: float[][]) (labels: int[]) batchSize shots (identifiers: string[]) state =
        let limit = min features.Length batchSize
        let trainData = features.[0..limit-1]
        let trainLabels = labels.[0..limit-1]

        let config = { QuantumKernelSVM.defaultConfig with Verbose = false; MaxIterations = 20 }

        QuantumKernelSVM.train backend featureMap trainData trainLabels config shots
        |> Result.mapError (fun e -> QuantumError.OperationError ("Training", $"Training Failed: {e.Message}"))
        |> Result.bind (fun model ->
            // Genuinely score the whole candidate pool with the trained model: the SVM
            // decision value is the screening score (signed distance from the hyperplane).
            features
            |> Array.mapi (fun i feat ->
                QuantumKernelSVM.predict backend model feat shots
                |> Result.map (fun pred ->
                    { Index = i
                      Identifier = ScreeningScoring.identifierAt identifiers i
                      Score = pred.DecisionValue
                      PredictedActive = Some (pred.Label = 1) }))
            |> ScreeningScoring.sequence
            |> Result.mapError (fun e -> QuantumError.OperationError ("Scoring", $"Candidate scoring failed: {e.Message}"))
            |> Result.map (fun scored ->
                let ranked = scored |> Array.sortByDescending (fun c -> c.Score)
                let hits = ranked |> Array.filter (fun c -> c.PredictedActive = Some true) |> Array.length
                { Message = $"Quantum Kernel SVM screening complete.\nSupport Vectors: {model.SupportVectorIndices.Length}\nBias: {model.Bias:F4}\nCandidates scored: {ranked.Length}\nPredicted active hits: {hits}"
                  Method = QuantumKernelSVM
                  MoleculesProcessed = features.Length
                  RankedCandidates = ranked
                  Configuration = state }))

    member private this.TrainVQCClassifier (backend: IQuantumBackend) featureMap (features: float[][]) (labels: int[]) (identifiers: string[]) state =
        let limit = min features.Length state.BatchSize
        let trainData = features.[0..limit-1]
        let trainLabels = labels.[0..limit-1]

        // Determine number of qubits from feature dimension
        let numQubits = if trainData.Length > 0 then trainData.[0].Length else 1

        // Create variational form with configured layers
        let variationalForm = VariationalForm.RealAmplitudes state.VQCLayers

        // Initialize random parameters
        let numParams = numQubits * state.VQCLayers
        let rng = System.Random(42)
        let initialParams = Array.init numParams (fun _ -> rng.NextDouble() * 2.0 * System.Math.PI)

        // Configure VQC training
        let vqcConfig = {
            VQC.defaultConfig with
                MaxEpochs = state.VQCMaxEpochs
                Shots = state.Shots
                Verbose = false
        }

        let mappedFeatureMap = this.MapFeatureMap state.FeatureMap

        // Train VQC model
        VQC.train backend mappedFeatureMap variationalForm initialParams trainData trainLabels vqcConfig
        |> Result.mapError (fun e -> QuantumError.OperationError ("VQCTraining", $"VQC Training Failed: {e.Message}"))
        |> Result.bind (fun result ->
            // Genuinely classify the whole candidate pool with the trained circuit: the
            // active-class probability is the screening score.
            features
            |> Array.mapi (fun i feat ->
                VQC.predict backend mappedFeatureMap variationalForm result.Parameters feat state.Shots
                |> Result.map (fun pred ->
                    { Index = i
                      Identifier = ScreeningScoring.identifierAt identifiers i
                      Score = pred.Probability
                      PredictedActive = Some (pred.Label = 1) }))
            |> ScreeningScoring.sequence
            |> Result.mapError (fun e -> QuantumError.OperationError ("Scoring", $"Candidate scoring failed: {e.Message}"))
            |> Result.map (fun scored ->
                let ranked = scored |> Array.sortByDescending (fun c -> c.Score)
                let hits = ranked |> Array.filter (fun c -> c.PredictedActive = Some true) |> Array.length
                { Message = $"VQC screening complete.\nEpochs: {result.Epochs}\nTrain Accuracy: {result.TrainAccuracy:P2}\nConverged: {result.Converged}\nCandidates scored: {ranked.Length}\nPredicted active hits: {hits}"
                  Method = VQCClassifier
                  MoleculesProcessed = features.Length
                  RankedCandidates = ranked
                  Configuration = state }))

    member private _.RunQAOADiverseSelection (backend: IQuantumBackend) (features: float[][]) (labelsOpt: int[] option) state =
        let limit = min features.Length state.BatchSize
        
        // Create items from molecules with activity scores
        // Use labels as activity scores if available, otherwise use feature-based heuristic
        let items = 
            features.[0..limit-1]
            |> Array.mapi (fun i feat ->
                let activityScore = 
                    match labelsOpt with
                    | Some labels when i < labels.Length -> float labels.[i]
                    | _ -> feat |> Array.sum |> abs  // Heuristic: sum of features
                { DrugDiscoverySolvers.DiverseSelection.Item.Id = $"Mol_{i}"
                  DrugDiscoverySolvers.DiverseSelection.Item.Value = activityScore
                  DrugDiscoverySolvers.DiverseSelection.Item.Cost = 1.0 })  // Equal cost per molecule
            |> Array.toList
        
        // Compute diversity matrix using Tanimoto distance from fingerprints
        let n = items.Length
        let diversityMatrix = Array2D.create n n 0.0
        for i in 0 .. n - 1 do
            for j in 0 .. n - 1 do
                if i <> j then
                    // Compute Tanimoto distance (1 - similarity)
                    let fp1 = features.[i]
                    let fp2 = features.[j]
                    let intersection = Array.map2 (fun a b -> min a b) fp1 fp2 |> Array.sum
                    let union = Array.map2 (fun a b -> max a b) fp1 fp2 |> Array.sum
                    let similarity = if union > 0.0 then intersection / union else 0.0
                    diversityMatrix.[i, j] <- 1.0 - similarity  // Higher = more diverse
        
        // Create the diverse selection problem
        let problem : DrugDiscoverySolvers.DiverseSelection.Problem = {
            Items = items
            Diversity = diversityMatrix
            Budget = state.SelectionBudget
            DiversityWeight = state.DiversityWeight
        }
        
        // Run QAOA solver
        DrugDiscoverySolvers.DiverseSelection.solve backend problem state.Shots
        |> Result.map (fun solution ->
            let selectedIds = solution.SelectedItems |> List.map (fun item -> item.Id) |> String.concat ", "
            // Expose the selected diverse set as ranked candidates (by activity value).
            let ranked =
                solution.SelectedItems
                |> List.map (fun item ->
                    let idx =
                        match System.Int32.TryParse(item.Id.Replace("Mol_", "")) with
                        | true, n -> n
                        | _ -> -1
                    { Index = idx
                      Identifier = item.Id
                      Score = item.Value
                      PredictedActive = None })
                |> List.sortByDescending (fun c -> c.Score)
                |> Array.ofList
            { Message = $"QAOA Diverse Selection Complete!\nSelected: {solution.SelectedItems.Length} compounds\nTotal Value: {solution.TotalValue:F2}\nDiversity Bonus: {solution.DiversityBonus:F2}\nTotal Cost: {solution.TotalCost:F2}\nFeasible: {solution.IsFeasible}\n\nSelected Compounds: {selectedIds}"
              Method = QAOADiverseSelection
              MoleculesProcessed = limit
              RankedCandidates = ranked
              Configuration = state })
        |> Result.mapError (fun e -> QuantumError.OperationError ("QAOASelection", $"QAOA Selection Failed: {e.Message}"))

    member this.Run(state: DrugDiscoveryConfiguration) : QuantumResult<ScreeningResult> =
        // Load and validate candidates
        this.LoadCandidates(state)
        |> Result.mapError (fun e -> QuantumError.OperationError ("DataLoading", $"Error loading molecular data: {e.Message}"))
        |> Result.bind (fun dataset ->
            // Extract features, retaining a stable identifier (SMILES) per molecule so the
            // scored candidates can be reported with their chemistry, not just an index.
            let identifiers = dataset.Molecules |> Array.map (fun m -> m.Smiles)
            let datasetWithFeats =
                dataset
                |> MolecularData.withDescriptors
                |> MolecularData.withFingerprints state.FingerprintSize

            MolecularData.toFeatureMatrix true true datasetWithFeats
            |> Result.mapError (fun e -> QuantumError.OperationError ("FeatureExtraction", $"Error generating features: {e.Message}"))
            |> Result.map (fun (features, labelsOpt) -> (features, labelsOpt, identifiers)))
        |> Result.bind (fun (features, labelsOpt, identifiers) ->
            // Run screening method
            match state.Method with
            | QuantumKernelSVM ->
                match state.Backend, labelsOpt with
                | None, _ -> Error (QuantumError.ValidationError ("Backend", "No backend provided. Use 'backend'."))
                | _, None ->
                    Error (QuantumError.ValidationError (
                        "labels",
                        "QuantumKernelSVM screening requires an activity/label column in the candidate dataset; found none. Provide labeled training data or use QAOADiverseSelection for unlabeled candidates."))
                | Some backend, Some labels ->
                    let featureMap = this.MapFeatureMap state.FeatureMap
                    this.TrainQuantumKernelSVM backend featureMap features labels state.BatchSize state.Shots identifiers state
            | VQCClassifier ->
                match state.Backend, labelsOpt with
                | None, _ -> Error (QuantumError.ValidationError ("Backend", "No backend provided. Use 'backend'."))
                | _, None ->
                    Error (QuantumError.ValidationError (
                        "labels",
                        "VQCClassifier screening requires an activity/label column in the candidate dataset; found none. Provide labeled training data or use QAOADiverseSelection for unlabeled candidates."))
                | Some backend, Some labels ->
                    let featureMap = this.MapFeatureMap state.FeatureMap
                    this.TrainVQCClassifier backend featureMap features labels identifiers state
            | QAOADiverseSelection ->
                match state.Backend with
                | None -> Error (QuantumError.ValidationError ("Backend", "No backend provided. Use 'backend'."))
                | Some backend ->
                    this.RunQAOADiverseSelection backend features labelsOpt state)

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

    /// Set the number of VQC ansatz layers (default: 2)
    [<CustomOperation("vqc_layers")>]
    member _.VQCLayers(state: DrugDiscoveryConfiguration, layers: int) =
        { state with VQCLayers = layers }

    /// Set the maximum VQC training epochs (default: 50)
    [<CustomOperation("vqc_max_epochs")>]
    member _.VQCMaxEpochs(state: DrugDiscoveryConfiguration, epochs: int) =
        { state with VQCMaxEpochs = epochs }

    /// Set the budget constraint for QAOA diverse selection (default: 10.0)
    [<CustomOperation("selection_budget")>]
    member _.SelectionBudget(state: DrugDiscoveryConfiguration, budget: float) =
        { state with SelectionBudget = budget }

    /// Set the diversity weight for QAOA selection (default: 0.5)
    [<CustomOperation("diversity_weight")>]
    member _.DiversityWeight(state: DrugDiscoveryConfiguration, weight: float) =
        { state with DiversityWeight = weight }

[<AutoOpen>]
module QuantumDrugDiscoveryDSL =
    let drugDiscovery = QuantumDrugDiscoveryBuilder()
