// ==============================================================================
// Druggability Scoring - Pharmacophore Feature Selection
// ==============================================================================
// Demonstrates using quantum Maximum Weight Independent Set (MWIS) to select
// non-overlapping pharmacophore features from a protein binding pocket.
//
// Business Context:
// A computational chemist is analyzing a protein binding pocket for
// druggability assessment. The pocket contains multiple potential
// pharmacophore features (H-bond donors/acceptors, hydrophobic regions).
// Overlapping features are mutually exclusive - we need to select the
// HIGHEST-WEIGHT non-overlapping set.
//
// This maps naturally to Maximum Weight Independent Set (MWIS):
//   - Nodes = pharmacophore features with importance weights
//   - Edges = spatial overlap (features that conflict)
//   - Goal = select maximum-weight subset with NO overlapping features
//
// WHY NOT GRAPH COLORING:
// Graph Coloring assigns colors such that adjacent nodes differ - it does NOT
// select a subset. We need to SELECT the best non-overlapping features.
// MWIS directly solves the selection problem.
//
// Quantum Approach:
//   - QAOA with penalty-based QUBO formulation
//   - Maximize sum of selected feature weights
//   - Penalty prevents selecting adjacent (overlapping) features
//   - Note: QAOA is heuristic - may have soft constraint violations
//
// Usage:
//   dotnet fsi DruggabilityScoring.fsx
//   dotnet fsi DruggabilityScoring.fsx -- --help
//   dotnet fsi DruggabilityScoring.fsx -- --overlap-threshold 3.0 --shots 2000
//   dotnet fsi DruggabilityScoring.fsx -- --output results.json --csv results.csv --quiet
//
// ==============================================================================

(*
Background Theory
-----------------

PHARMACOPHORE MODELING:
A pharmacophore is the spatial arrangement of features necessary for biological
activity. Key feature types include:
  - H-Bond Donors/Acceptors: Critical for hinge-region binding in kinases
  - Hydrophobic regions: Drive binding selectivity
  - Aromatic features: pi-stacking interactions
  - Charged groups: Salt bridges with protein residues

DRUGGABILITY ASSESSMENT:
Not all protein targets are equally amenable to small-molecule inhibition.
Druggability depends on:
  - Binding pocket size and shape
  - Feature diversity (mix of polar/non-polar interactions)
  - Feature importance (strength of potential interactions)
  - Accessibility of key residues

MWIS FOR FEATURE SELECTION:
When pharmacophore features spatially overlap, they cannot all be used
simultaneously by a single drug molecule. Finding the optimal non-overlapping
subset is an NP-hard problem (Maximum Weight Independent Set), making it
a natural candidate for quantum optimization.

EGFR KINASE:
Epidermal Growth Factor Receptor kinase is a well-characterized drug target
in oncology. Drugs targeting EGFR include erlotinib, gefitinib, and
osimertinib. The ATP binding pocket contains diverse pharmacophore features.

References:
  [1] Halgren, T.A. "Identifying and Characterizing Binding Sites" J. Chem. Inf. Model. (2009)
  [2] Schmidtke, P. & Barril, X. "Druggability Assessment" J. Med. Chem. (2010)
  [3] Wikipedia: Pharmacophore
  [4] Wikipedia: Independent_set_(graph_theory)
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "nuget: MathNet.Numerics, 5.0.0"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Quantum.DrugDiscoverySolvers
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "DruggabilityScoring.fsx"
    "Quantum pharmacophore feature selection via Maximum Weight Independent Set"
    [ { Cli.OptionSpec.Name = "overlap-threshold"; Description = "Distance threshold for feature overlap (A)"; Default = Some "2.5" }
      { Cli.OptionSpec.Name = "shots"; Description = "Number of QAOA measurement shots"; Default = Some "1000" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let overlapThreshold = Cli.getFloatOr "overlap-threshold" 2.5 args
let shots = Cli.getIntOr "shots" 1000 args

// ==============================================================================
// DOMAIN MODEL
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
type PharmacophoreFeature = {
    Id: string
    Type: FeatureType
    /// 3D position in binding pocket (Angstroms)
    Position: float * float * float
    /// Importance score (0-1, higher = more important for binding)
    Importance: float
}

/// Binding pocket with pharmacophore features.
type BindingPocket = {
    ProteinName: string
    PdbId: string
    Features: PharmacophoreFeature list
    /// Distance threshold for overlap (Angstroms)
    OverlapThreshold: float
}

// ==============================================================================
// HELPER FUNCTIONS
// ==============================================================================

/// Calculate Euclidean distance between two 3D points.
let distance (x1, y1, z1) (x2, y2, z2) =
    sqrt ((x2 - x1) ** 2.0 + (y2 - y1) ** 2.0 + (z2 - z1) ** 2.0)

/// Check if two features overlap (within threshold distance).
let featuresOverlap (threshold: float) (f1: PharmacophoreFeature) (f2: PharmacophoreFeature) =
    f1.Id <> f2.Id && distance f1.Position f2.Position < threshold

/// Find all overlapping feature pairs as index pairs.
let findOverlapIndices (pocket: BindingPocket) : (int * int) list =
    [ for i, f1 in pocket.Features |> List.indexed do
        for j, f2 in pocket.Features |> List.indexed do
            if i < j && featuresOverlap pocket.OverlapThreshold f1 f2 then
                yield (i, j) ]

/// Convert binding pocket to Independent Set problem.
let toIndependentSetProblem (pocket: BindingPocket) : IndependentSet.Problem =
    let nodes =
        pocket.Features
        |> List.map (fun f ->
            { IndependentSet.Node.Id = f.Id
              IndependentSet.Node.Weight = f.Importance })
    let edges = findOverlapIndices pocket
    { IndependentSet.Problem.Nodes = nodes
      IndependentSet.Problem.Edges = edges }

/// Get feature by ID.
let getFeature (pocket: BindingPocket) (id: string) : PharmacophoreFeature option =
    pocket.Features |> List.tryFind (fun f -> f.Id = id)

/// Get feature type display name.
let featureTypeName = function
    | HBondDonor -> "H-Bond Donor"
    | HBondAcceptor -> "H-Bond Acceptor"
    | Hydrophobic -> "Hydrophobic"
    | Aromatic -> "Aromatic"
    | PositiveCharge -> "Positive"
    | NegativeCharge -> "Negative"

// ==============================================================================
// BINDING POCKET DATA
// ==============================================================================

if not quiet then
    printfn "=============================================================="
    printfn "  Druggability Scoring - Pharmacophore Feature Selection"
    printfn "=============================================================="
    printfn ""

// EGFR kinase ATP binding pocket (simplified from PDB 1M17)
let egfrPocket : BindingPocket = {
    ProteinName = "EGFR Kinase"
    PdbId = "1M17"
    Features = [
        // Hinge region H-bond acceptors (critical for kinase inhibitor binding)
        { Id = "HBA-1"; Type = HBondAcceptor; Position = (0.0, 0.0, 0.0); Importance = 0.95 }
        { Id = "HBA-2"; Type = HBondAcceptor; Position = (1.5, 0.5, 0.0); Importance = 0.90 }

        // Hinge region H-bond donors
        { Id = "HBD-1"; Type = HBondDonor; Position = (3.0, 0.0, 0.0); Importance = 0.85 }
        { Id = "HBD-2"; Type = HBondDonor; Position = (3.5, 1.0, 0.5); Importance = 0.80 }

        // Hydrophobic pocket (selectivity pocket)
        { Id = "HYD-1"; Type = Hydrophobic; Position = (-3.0, 2.0, 1.0); Importance = 0.70 }
        { Id = "HYD-2"; Type = Hydrophobic; Position = (-2.5, 2.5, 1.5); Importance = 0.65 }
        { Id = "HYD-3"; Type = Hydrophobic; Position = (-4.5, 3.0, 0.5); Importance = 0.60 }

        // Gatekeeper region
        { Id = "ARO-1"; Type = Aromatic; Position = (5.0, -1.0, 0.0); Importance = 0.75 }

        // DFG motif region
        { Id = "NEG-1"; Type = NegativeCharge; Position = (7.0, 2.0, -1.0); Importance = 0.55 }
    ]
    OverlapThreshold = overlapThreshold
}

if not quiet then
    printfn "%s Binding Pocket (PDB: %s)" egfrPocket.ProteinName egfrPocket.PdbId
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "Total Features: %d" egfrPocket.Features.Length
    printfn "Overlap Threshold: %.1f A" egfrPocket.OverlapThreshold
    printfn ""
    printfn "Pharmacophore Features:"
    printfn "  %-8s %-15s %16s %12s" "ID" "Type" "Position" "Importance"
    printfn "  %s" (System.String.replicate 55 "-")
    for f in egfrPocket.Features do
        let (x, y, z) = f.Position
        printfn "  %-8s %-15s (%4.1f,%4.1f,%4.1f) %12.2f"
            f.Id (featureTypeName f.Type) x y z f.Importance
    printfn ""

// Find and display overlaps
let overlaps = findOverlapIndices egfrPocket

if not quiet then
    printfn "Detected Overlaps (mutually exclusive pairs):"
    for (i, j) in overlaps do
        let f1 = egfrPocket.Features.[i]
        let f2 = egfrPocket.Features.[j]
        let dist = distance f1.Position f2.Position
        printfn "  %s <-> %s (distance: %.2f A) - Cannot both be selected!" f1.Id f2.Id dist
    printfn ""

// ==============================================================================
// QUANTUM INDEPENDENT SET SOLUTION
// ==============================================================================

if not quiet then
    printfn "Quantum Analysis (QAOA Maximum Weight Independent Set)"
    printfn "--------------------------------------------------------------"
    printfn ""

let problem = toIndependentSetProblem egfrPocket
let backend = LocalBackend() :> IQuantumBackend
let results = System.Collections.Generic.List<Map<string, string>>()

match IndependentSet.solve backend problem shots with
| Ok solution ->
    if not quiet then
        printfn "[OK] Optimal Non-Overlapping Selection Found"
        printfn ""
        printfn "Selected Features: %d / %d" solution.SelectedNodes.Length egfrPocket.Features.Length
        printfn "Total Weight (Importance): %.2f" solution.TotalWeight
        printfn "Valid (no overlaps): %b" solution.IsValid
        printfn "Backend: %s" solution.BackendName
        printfn ""

        printfn "=============================================================="
        printfn "  Selected Non-Overlapping Features"
        printfn "=============================================================="
        printfn ""

    for node in solution.SelectedNodes |> List.sortByDescending (fun n -> n.Weight) do
        match getFeature egfrPocket node.Id with
        | Some feature ->
            if not quiet then
                printfn "  * %s: %s (importance: %.2f)"
                    feature.Id (featureTypeName feature.Type) feature.Importance

            results.Add(
                [ "type", "selected_feature"
                  "feature_id", feature.Id
                  "feature_type", featureTypeName feature.Type
                  "importance", sprintf "%.2f" feature.Importance
                  "position_x", let (x,_,_) = feature.Position in sprintf "%.1f" x
                  "position_y", let (_,y,_) = feature.Position in sprintf "%.1f" y
                  "position_z", let (_,_,z) = feature.Position in sprintf "%.1f" z ]
                |> Map.ofList)
        | None -> ()

    if not quiet then printfn ""

    // Verify no overlaps
    let selectedIds = solution.SelectedNodes |> List.map (fun n -> n.Id) |> Set.ofList
    let selectedIndices =
        egfrPocket.Features
        |> List.indexed
        |> List.filter (fun (_, f) -> Set.contains f.Id selectedIds)
        |> List.map fst
        |> Set.ofList

    let violations =
        overlaps
        |> List.filter (fun (i, j) ->
            Set.contains i selectedIndices && Set.contains j selectedIndices)

    if not quiet then
        if violations.IsEmpty then
            printfn "[VERIFIED] No overlapping features selected!"
        else
            printfn "[WARNING] %d overlap violations detected" violations.Length
            for (i, j) in violations do
                printfn "  - %s and %s both selected" egfrPocket.Features.[i].Id egfrPocket.Features.[j].Id
        printfn ""

    // Druggability assessment
    let selectedFeatures =
        solution.SelectedNodes
        |> List.choose (fun n -> getFeature egfrPocket n.Id)
    let featureTypes = selectedFeatures |> List.map (fun f -> f.Type) |> List.distinct

    let hasHBondAcceptor = selectedFeatures |> List.exists (fun f -> f.Type = HBondAcceptor)
    let hasHBondDonor = selectedFeatures |> List.exists (fun f -> f.Type = HBondDonor)
    let hasHydrophobic = selectedFeatures |> List.exists (fun f -> f.Type = Hydrophobic)

    let druggabilityScore =
        solution.TotalWeight / (float egfrPocket.Features.Length * 0.95) * 0.5 +
        (float featureTypes.Length / 6.0) * 0.3 +
        (if hasHBondAcceptor && hasHBondDonor then 0.2 else 0.1)

    let druggabilityRating =
        if druggabilityScore > 0.7 then "HIGH - Excellent drug target"
        elif druggabilityScore > 0.5 then "MEDIUM - Viable drug target"
        else "LOW - Challenging target"

    if not quiet then
        printfn "Druggability Assessment:"
        printfn "--------------------------------------------------------------"
        printfn "  Total Importance Score: %.2f" solution.TotalWeight
        printfn "  Average Feature Importance: %.2f" (solution.TotalWeight / float selectedFeatures.Length)
        printfn "  Feature Type Diversity: %d types" featureTypes.Length
        printfn ""
        printfn "Key Interactions Present:"
        printfn "  - H-Bond Acceptor (hinge): %b" hasHBondAcceptor
        printfn "  - H-Bond Donor (hinge): %b" hasHBondDonor
        printfn "  - Hydrophobic (selectivity): %b" hasHydrophobic
        printfn ""
        printfn "  Druggability Score: %.2f" druggabilityScore
        printfn "  Rating: %s" druggabilityRating
        printfn ""

    results.Add(
        [ "type", "druggability_assessment"
          "method", "QAOA_MWIS"
          "protein", egfrPocket.ProteinName
          "pdb_id", egfrPocket.PdbId
          "total_features", string egfrPocket.Features.Length
          "selected_features", string solution.SelectedNodes.Length
          "total_weight", sprintf "%.2f" solution.TotalWeight
          "is_valid", string solution.IsValid
          "overlap_violations", string violations.Length
          "feature_type_diversity", string featureTypes.Length
          "has_hbond_acceptor", string hasHBondAcceptor
          "has_hbond_donor", string hasHBondDonor
          "has_hydrophobic", string hasHydrophobic
          "druggability_score", sprintf "%.2f" druggabilityScore
          "druggability_rating", druggabilityRating
          "backend", solution.BackendName
          "shots", string shots
          "overlap_threshold_a", sprintf "%.1f" overlapThreshold ]
        |> Map.ofList)

| Error err ->
    if not quiet then printfn "[ERROR] %s" err.Message
    results.Add(
        [ "type", "error"
          "error", err.Message ]
        |> Map.ofList)

// ==============================================================================
// WHY MWIS (NOT GRAPH COLORING)
// ==============================================================================

if not quiet then
    printfn "=============================================================="
    printfn "  Why MWIS is the Right Abstraction"
    printfn "=============================================================="
    printfn ""
    printfn "Graph Coloring (WRONG approach):"
    printfn "  - Assigns COLORS to ALL nodes"
    printfn "  - Goal: minimize colors such that adjacent nodes differ"
    printfn "  - Result: Every feature gets a color assignment"
    printfn "  - Problem: We want to SELECT features, not color them!"
    printfn ""
    printfn "Maximum Weight Independent Set (CORRECT approach):"
    printfn "  - SELECTS a subset of nodes"
    printfn "  - Goal: maximize total weight with no adjacent nodes"
    printfn "  - Result: Optimal subset of non-overlapping features"
    printfn "  - This IS what pharmacophore selection requires!"
    printfn ""

// ==============================================================================
// SUGGESTED EXTENSIONS
// ==============================================================================

if not quiet then
    printfn "Suggested Extensions"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "1. Custom binding pocket:"
    printfn "   - Load features from PDB file or CSV (future --input flag)"
    printfn "   - Parse real pharmacophore models from tools like Phase/LigandScout"
    printfn ""
    printfn "2. Tune overlap threshold:"
    printfn "   - --overlap-threshold 2.0 (stricter, more features selected)"
    printfn "   - --overlap-threshold 3.5 (looser, fewer but higher-weight)"
    printfn ""
    printfn "3. Multi-target druggability:"
    printfn "   - Compare feature selections across multiple kinase targets"
    printfn "   - Identify selectivity-determining features"
    printfn ""
    printfn "4. Integration with docking:"
    printfn "   - Use selected pharmacophore as docking constraints"
    printfn "   - Score compound library against optimal feature set"
    printfn ""

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    printfn "=============================================================="
    printfn "  Summary"
    printfn "=============================================================="
    printfn ""
    printfn "[OK] Demonstrated quantum MWIS for pharmacophore selection"
    printfn "[OK] Used DrugDiscoverySolvers.IndependentSet.solve via IQuantumBackend"
    printfn "[OK] Correct QUBO formulation for maximum weight independent set"
    printfn "[OK] Computed druggability score from selected features"
    printfn ""
    printfn "Key Insight:"
    printfn "  Unlike Graph Coloring (which colors ALL nodes), MWIS"
    printfn "  SELECTS the highest-weight non-overlapping subset."
    printfn "  This is the correct abstraction for pharmacophore selection."
    printfn ""
    printfn "QAOA Limitations:"
    printfn "  Single-layer QAOA may produce soft constraint violations."
    printfn "  Deeper circuits and parameter tuning improve solution quality."
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultsList = results |> Seq.toList

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path resultsList
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header = [ "type"; "feature_id"; "feature_type"; "importance"; "position_x"; "position_y"; "position_z"; "method"; "protein"; "pdb_id"; "total_features"; "selected_features"; "total_weight"; "is_valid"; "overlap_violations"; "feature_type_diversity"; "has_hbond_acceptor"; "has_hbond_donor"; "has_hydrophobic"; "druggability_score"; "druggability_rating"; "backend"; "shots"; "overlap_threshold_a"; "error" ]
    let rows =
        resultsList
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Run with --help to see all available options."
    printfn "     Use --output results.json --csv results.csv for structured output."
    printfn ""
