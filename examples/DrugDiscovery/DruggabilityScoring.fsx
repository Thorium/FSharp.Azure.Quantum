// ==============================================================================
// Druggability Scoring - Pharmacophore Feature Selection
// ==============================================================================
// Selects non-overlapping pharmacophore features from a protein binding pocket
// using quantum Maximum Weight Independent Set (MWIS/QAOA), ranking features
// by importance and spatial compatibility.
//
// The question: "Given a binding pocket with overlapping pharmacophore features,
// which non-overlapping subset has the highest total importance?"
//
// MWIS maps directly to pharmacophore selection:
//   - Nodes = features with importance weights
//   - Edges = spatial overlap (mutually exclusive pairs)
//   - Goal = max-weight subset with NO overlapping features
//
// Accepts multiple features (built-in EGFR pocket or --input CSV), runs QAOA
// Independent Set, then outputs a ranked table showing which features were
// selected and a druggability assessment.
//
// Usage:
//   dotnet fsi DruggabilityScoring.fsx
//   dotnet fsi DruggabilityScoring.fsx -- --help
//   dotnet fsi DruggabilityScoring.fsx -- --features hba-1,hbd-1,hyd-1
//   dotnet fsi DruggabilityScoring.fsx -- --input features.csv
//   dotnet fsi DruggabilityScoring.fsx -- --overlap-threshold 3.0 --shots 2000
//   dotnet fsi DruggabilityScoring.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Halgren, T.A. "Identifying and Characterizing Binding Sites" J. Chem. Inf. Model. (2009)
//   [2] Schmidtke, P. & Barril, X. "Druggability Assessment" J. Med. Chem. (2010)
//   [3] Wikipedia: Pharmacophore
//   [4] Wikipedia: Independent_set_(graph_theory)
// ==============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "nuget: MathNet.Numerics, 5.0.0"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Quantum.DrugDiscoverySolvers
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "DruggabilityScoring.fsx"
    "Quantum pharmacophore feature selection via Maximum Weight Independent Set"
    [ { Cli.OptionSpec.Name = "input"; Description = "CSV file with custom feature definitions"; Default = Some "built-in EGFR pocket" }
      { Cli.OptionSpec.Name = "features"; Description = "Comma-separated feature IDs to include (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "overlap-threshold"; Description = "Distance threshold for feature overlap (Angstroms)"; Default = Some "2.5" }
      { Cli.OptionSpec.Name = "shots"; Description = "Number of QAOA measurement shots"; Default = Some "1000" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputFile = args |> Cli.tryGet "input"
let featureFilter = args |> Cli.getCommaSeparated "features"
let overlapThreshold = Cli.getFloatOr "overlap-threshold" 2.5 args
let shots = Cli.getIntOr "shots" 1000 args

// ==============================================================================
// TYPES
// ==============================================================================

/// Type of pharmacophore feature.
type FeatureType =
    | HBondDonor
    | HBondAcceptor
    | Hydrophobic
    | Aromatic
    | PositiveCharge
    | NegativeCharge

/// Pharmacophore feature in a binding pocket.
type PharmacophoreFeature =
    { Id: string
      Type: FeatureType
      Position: float * float * float
      Importance: float }

/// Result for each feature after QAOA selection.
type FeatureResult =
    { Feature: PharmacophoreFeature
      Selected: bool
      Rank: int
      HasVqeFailure: bool }

// ==============================================================================
// HELPER FUNCTIONS
// ==============================================================================

/// Calculate Euclidean distance between two 3D points.
let distance (x1, y1, z1) (x2, y2, z2) =
    sqrt ((x2 - x1) ** 2.0 + (y2 - y1) ** 2.0 + (z2 - z1) ** 2.0)

/// Check if two features overlap (within threshold distance).
let featuresOverlap (threshold: float) (f1: PharmacophoreFeature) (f2: PharmacophoreFeature) =
    f1.Id <> f2.Id && distance f1.Position f2.Position < threshold

/// Get feature type display name.
let featureTypeName = function
    | HBondDonor -> "H-Bond Donor"
    | HBondAcceptor -> "H-Bond Acceptor"
    | Hydrophobic -> "Hydrophobic"
    | Aromatic -> "Aromatic"
    | PositiveCharge -> "Positive"
    | NegativeCharge -> "Negative"

/// Parse feature type from string.
let parseFeatureType (s: string) : FeatureType =
    match s.Trim().ToLowerInvariant() with
    | "hbonddonor" | "h-bond donor" | "donor" -> HBondDonor
    | "hbondacceptor" | "h-bond acceptor" | "acceptor" -> HBondAcceptor
    | "hydrophobic" -> Hydrophobic
    | "aromatic" -> Aromatic
    | "positive" | "positivecharge" -> PositiveCharge
    | "negative" | "negativecharge" -> NegativeCharge
    | _ -> Hydrophobic  // safe default

// ==============================================================================
// BUILT-IN FEATURE PRESETS (EGFR kinase ATP binding pocket, simplified from PDB 1M17)
// ==============================================================================

let private hba1 =
    { Id = "HBA-1"; Type = HBondAcceptor; Position = (0.0, 0.0, 0.0); Importance = 0.95 }

let private hba2 =
    { Id = "HBA-2"; Type = HBondAcceptor; Position = (1.5, 0.5, 0.0); Importance = 0.90 }

let private hbd1 =
    { Id = "HBD-1"; Type = HBondDonor; Position = (3.0, 0.0, 0.0); Importance = 0.85 }

let private hbd2 =
    { Id = "HBD-2"; Type = HBondDonor; Position = (3.5, 1.0, 0.5); Importance = 0.80 }

let private hyd1 =
    { Id = "HYD-1"; Type = Hydrophobic; Position = (-3.0, 2.0, 1.0); Importance = 0.70 }

let private hyd2 =
    { Id = "HYD-2"; Type = Hydrophobic; Position = (-2.5, 2.5, 1.5); Importance = 0.65 }

let private hyd3 =
    { Id = "HYD-3"; Type = Hydrophobic; Position = (-4.5, 3.0, 0.5); Importance = 0.60 }

let private aro1 =
    { Id = "ARO-1"; Type = Aromatic; Position = (5.0, -1.0, 0.0); Importance = 0.75 }

let private neg1 =
    { Id = "NEG-1"; Type = NegativeCharge; Position = (7.0, 2.0, -1.0); Importance = 0.55 }

/// All built-in presets keyed by lowercase ID.
let private builtinPresets : Map<string, PharmacophoreFeature> =
    [ hba1; hba2; hbd1; hbd2; hyd1; hyd2; hyd3; aro1; neg1 ]
    |> List.map (fun f -> f.Id.ToLowerInvariant(), f)
    |> Map.ofList

let private presetNames =
    builtinPresets |> Map.toList |> List.map fst |> String.concat ", "

// ==============================================================================
// CSV INPUT PARSING
// ==============================================================================

/// Load features from a CSV file.
/// Expected columns: id, feature_type, position_x, position_y, position_z, importance
/// OR: id, preset (to reference a built-in preset by ID)
let private loadFeaturesFromCsv (path: string) : PharmacophoreFeature list =
    let rows, errors = Data.readCsvWithHeaderWithErrors path
    if not (List.isEmpty errors) && not quiet then
        for err in errors do
            eprintfn "  Warning (CSV): %s" err

    rows
    |> List.choose (fun row ->
        let get key = row.Values |> Map.tryFind key
        let id = get "id" |> Option.defaultValue "Unknown"
        match get "preset" with
        | Some presetKey ->
            let key = presetKey.Trim().ToLowerInvariant()
            match builtinPresets |> Map.tryFind key with
            | Some feat -> Some { feat with Id = id }
            | None ->
                if not quiet then
                    eprintfn "  Warning: unknown preset '%s' (available: %s)" presetKey presetNames
                None
        | None ->
            let featureType = get "feature_type" |> Option.map parseFeatureType |> Option.defaultValue Hydrophobic
            let px = get "position_x" |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None) |> Option.defaultValue 0.0
            let py = get "position_y" |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None) |> Option.defaultValue 0.0
            let pz = get "position_z" |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None) |> Option.defaultValue 0.0
            let importance = get "importance" |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None) |> Option.defaultValue 0.5
            Some
                { Id = id
                  Type = featureType
                  Position = (px, py, pz)
                  Importance = importance })

// ==============================================================================
// FEATURE SELECTION
// ==============================================================================

let features : PharmacophoreFeature list =
    let allFeatures =
        match inputFile with
        | Some path ->
            let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
            if not quiet then
                printfn "Loading features from: %s" resolved
            loadFeaturesFromCsv resolved
        | None ->
            builtinPresets |> Map.toList |> List.map snd

    match featureFilter with
    | [] -> allFeatures
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        allFeatures
        |> List.filter (fun f ->
            let key = f.Id.ToLowerInvariant()
            filterSet |> Set.exists (fun fil -> key.Contains fil))

if List.isEmpty features then
    eprintfn "Error: No features selected. Available presets: %s" presetNames
    exit 1

// ==============================================================================
// OVERLAP DETECTION
// ==============================================================================

/// Find all overlapping feature pairs as index pairs.
let findOverlapIndices (featureList: PharmacophoreFeature list) (threshold: float) : (int * int) list =
    [ for i in 0 .. featureList.Length - 1 do
        for j in i + 1 .. featureList.Length - 1 do
            if featuresOverlap threshold featureList.[i] featureList.[j] then
                yield (i, j) ]

let overlaps = findOverlapIndices features overlapThreshold

// ==============================================================================
// QUANTUM BACKEND (Rule 1: all QAOA via IQuantumBackend)
// ==============================================================================

let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Druggability Scoring - Pharmacophore Feature Selection"
    printfn "=================================================================="
    printfn ""
    printfn "  Protein:        EGFR Kinase (PDB: 1M17)"
    printfn "  Backend:        %s" backend.Name
    printfn "  Features:       %d" features.Length
    printfn "  Overlap threshold: %.1f A" overlapThreshold
    printfn "  Overlap pairs:  %d" overlaps.Length
    printfn "  QAOA shots:     %d" shots
    printfn ""
    printfn "  %-8s  %-16s  %16s  %10s"
        "ID" "Type" "Position" "Importance"
    printfn "  %s" (String('-', 55))
    for f in features do
        let (x, y, z) = f.Position
        printfn "  %-8s  %-16s  (%4.1f,%4.1f,%4.1f)  %10.2f"
            f.Id (featureTypeName f.Type) x y z f.Importance
    printfn ""

    if not overlaps.IsEmpty then
        printfn "  Overlap pairs (mutually exclusive):"
        for (i, j) in overlaps do
            let f1 = features.[i]
            let f2 = features.[j]
            let dist = distance f1.Position f2.Position
            printfn "    %s <-> %s (%.2f A)" f1.Id f2.Id dist
        printfn ""

// ==============================================================================
// QAOA INDEPENDENT SET (MWIS)
// ==============================================================================

if not quiet then
    printfn "Running QAOA Maximum Weight Independent Set..."
    printfn ""

/// Convert to IndependentSet problem.
let toIndependentSetProblem (featureList: PharmacophoreFeature list) (overlapPairs: (int * int) list) : IndependentSet.Problem =
    let nodes =
        featureList
        |> List.map (fun f ->
            { IndependentSet.Node.Id = f.Id
              IndependentSet.Node.Weight = f.Importance })
    { IndependentSet.Problem.Nodes = nodes
      IndependentSet.Problem.Edges = overlapPairs }

let problem = toIndependentSetProblem features overlaps

let startTime = DateTime.Now
let solveResult = IndependentSet.solve backend problem shots
let elapsed = (DateTime.Now - startTime).TotalSeconds

let hasFailure = Result.isError solveResult

// Build per-feature results
let selectedIds =
    match solveResult with
    | Ok solution -> solution.SelectedNodes |> List.map (fun n -> n.Id) |> Set.ofList
    | Error _ -> Set.empty

let featureResults : FeatureResult list =
    features
    |> List.map (fun f ->
        { Feature = f
          Selected = selectedIds |> Set.contains f.Id
          Rank = 0
          HasVqeFailure = hasFailure })

// Sort: selected first (by importance descending), then unselected.
let ranked =
    featureResults
    |> List.sortBy (fun r ->
        if r.HasVqeFailure then (2, 0.0)
        elif r.Selected then (0, -r.Feature.Importance)
        else (1, -r.Feature.Importance))
    |> List.mapi (fun i r -> { r with Rank = i + 1 })

// Solution-level stats
let solutionStats =
    match solveResult with
    | Ok solution ->
        if not quiet then
            printfn "  [OK] QAOA MWIS complete (%.1fs)" elapsed
            printfn "       Selected: %d / %d features" solution.SelectedNodes.Length features.Length
            printfn "       Total weight: %.2f" solution.TotalWeight
            printfn "       Valid (no overlaps): %b" solution.IsValid
            printfn "       Backend: %s" solution.BackendName
            printfn ""
        Some solution
    | Error err ->
        if not quiet then
            printfn "  [ERROR] QAOA failed: %s (%.1fs)" err.Message elapsed
            printfn ""
        None

// ==============================================================================
// DRUGGABILITY ASSESSMENT
// ==============================================================================

let druggabilityScore, druggabilityRating =
    match solutionStats with
    | Some solution ->
        let selectedFeatures = ranked |> List.filter (fun r -> r.Selected) |> List.map (fun r -> r.Feature)
        let featureTypes = selectedFeatures |> List.map (fun f -> f.Type) |> List.distinct

        let hasHBondAcceptor = selectedFeatures |> List.exists (fun f -> f.Type = HBondAcceptor)
        let hasHBondDonor = selectedFeatures |> List.exists (fun f -> f.Type = HBondDonor)

        let score =
            solution.TotalWeight / (float features.Length * 0.95) * 0.5 +
            (float featureTypes.Length / 6.0) * 0.3 +
            (if hasHBondAcceptor && hasHBondDonor then 0.2 else 0.1)

        let rating =
            if score > 0.7 then "HIGH - Excellent drug target"
            elif score > 0.5 then "MEDIUM - Viable drug target"
            else "LOW - Challenging target"

        (score, rating)
    | None ->
        (0.0, "N/A - QAOA failed")

// ==============================================================================
// RANKED COMPARISON TABLE
// ==============================================================================

let printTable () =
    printfn "=================================================================="
    printfn "  Pharmacophore Feature Selection Results (QAOA MWIS)"
    printfn "=================================================================="
    printfn ""
    printfn "  %-4s  %-8s  %-16s  %16s  %10s  %8s"
        "#" "ID" "Type" "Position" "Importance" "Selected"
    printfn "  %s" (String('=', 75))

    for r in ranked do
        let (x, y, z) = r.Feature.Position
        if r.HasVqeFailure then
            printfn "  %-4d  %-8s  %-16s  (%4.1f,%4.1f,%4.1f)  %10.2f  %8s"
                r.Rank r.Feature.Id (featureTypeName r.Feature.Type)
                x y z r.Feature.Importance "FAILED"
        else
            printfn "  %-4d  %-8s  %-16s  (%4.1f,%4.1f,%4.1f)  %10.2f  %8s"
                r.Rank r.Feature.Id (featureTypeName r.Feature.Type)
                x y z r.Feature.Importance (if r.Selected then "YES" else "no")

    printfn ""

    match solutionStats with
    | Some solution ->
        let selectedFeatures = ranked |> List.filter (fun r -> r.Selected) |> List.map (fun r -> r.Feature)
        if not selectedFeatures.IsEmpty then
            let featureTypes = selectedFeatures |> List.map (fun f -> f.Type) |> List.distinct
            let hasHBondAcceptor = selectedFeatures |> List.exists (fun f -> f.Type = HBondAcceptor)
            let hasHBondDonor = selectedFeatures |> List.exists (fun f -> f.Type = HBondDonor)
            let hasHydrophobic = selectedFeatures |> List.exists (fun f -> f.Type = Hydrophobic)

            printfn "  Druggability Assessment:"
            printfn "  %s" (String('-', 55))
            printfn "    Selected:      %d / %d features" selectedFeatures.Length features.Length
            printfn "    Total weight:  %.2f" solution.TotalWeight
            printfn "    Valid:         %b (no overlap violations)" solution.IsValid
            printfn "    Type diversity:%d distinct types (%s)"
                featureTypes.Length
                (featureTypes |> List.map featureTypeName |> String.concat ", ")
            printfn "    H-bond acc:    %b" hasHBondAcceptor
            printfn "    H-bond don:    %b" hasHBondDonor
            printfn "    Hydrophobic:   %b" hasHydrophobic
            printfn "    Score:         %.2f" druggabilityScore
            printfn "    Rating:        %s" druggabilityRating
            printfn ""
    | None -> ()

// Always print â€” this is the primary output.
printTable ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    match solutionStats with
    | Some _ ->
        printfn "  Solve time:   %.1f seconds" elapsed
        printfn "  Quantum:      QAOA MWIS via IQuantumBackend [Rule 1 compliant]"
        printfn ""
    | None ->
        printfn "  QAOA MWIS selection failed."
        printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    ranked
    |> List.map (fun r ->
        let (x, y, z) = r.Feature.Position
        let totalWeight =
            match solutionStats with Some s -> sprintf "%.2f" s.TotalWeight | None -> "FAILED"
        let isValid =
            match solutionStats with Some s -> string s.IsValid | None -> "FAILED"
        let backendName =
            match solutionStats with Some s -> s.BackendName | None -> "N/A"

        [ "rank", string r.Rank
          "feature_id", r.Feature.Id
          "feature_type", featureTypeName r.Feature.Type
          "position_x", sprintf "%.1f" x
          "position_y", sprintf "%.1f" y
          "position_z", sprintf "%.1f" z
          "importance", sprintf "%.2f" r.Feature.Importance
          "selected", string r.Selected
          "total_weight", totalWeight
          "is_valid", isValid
          "overlap_threshold_a", sprintf "%.1f" overlapThreshold
          "druggability_score", sprintf "%.2f" druggabilityScore
          "druggability_rating", druggabilityRating
          "backend", backendName
          "shots", string shots
          "compute_time_s", sprintf "%.1f" elapsed
          "has_vqe_failure", string r.HasVqeFailure ]
        |> Map.ofList)

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path resultMaps
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header =
        [ "rank"; "feature_id"; "feature_type"; "position_x"; "position_y"; "position_z"
          "importance"; "selected"; "total_weight"; "is_valid"
          "overlap_threshold_a"; "druggability_score"; "druggability_rating"
          "backend"; "shots"; "compute_time_s"; "has_vqe_failure" ]
    let rows =
        resultMaps
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Run with --help to see all options."
    printfn "     --features hba-1,hbd-1,aro-1             Run specific features"
    printfn "     --input features.csv                     Load custom features from CSV"
    printfn "     --overlap-threshold 3.0                  Adjust overlap distance"
    printfn "     --csv results.csv                        Export ranked table as CSV"
    printfn ""
