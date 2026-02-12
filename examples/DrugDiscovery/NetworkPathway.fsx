// ==============================================================================
// Network Pathway Analysis - Key Target Identification
// ==============================================================================
// Demonstrates using quantum Influence Maximization to identify key drug
// targets in protein interaction networks.
//
// Business Context:
// A pharmaceutical research team wants to identify the most important
// proteins in a disease signaling pathway. Key targets are proteins that,
// when inhibited, maximally disrupt the disease network.
//
// This maps naturally to Influence Maximization:
// - Nodes = proteins with disease relevance scores
// - Edges = protein-protein interactions with synergy weights
// - Goal = select k most influential proteins for drug targeting
//
// WHY NOT MAXCUT:
// MaxCut partitions a graph into two sets - it doesn't select "key targets".
// MaxCut would identify ALL proteins as "key" if they have cross-partition edges.
// Influence Maximization selects exactly k nodes that maximize combined influence.
//
// Quantum Approach:
// - Use QAOA with cardinality constraint (select exactly k proteins)
// - Optimize: individual scores + synergy bonus for interacting targets
// - Note: QAOA is heuristic - may not find exact k, but finds good solutions
//
// Usage:
//   dotnet fsi NetworkPathway.fsx
//   dotnet fsi NetworkPathway.fsx -- --help
//   dotnet fsi NetworkPathway.fsx -- --k 3 --synergy-weight 0.3 --shots 1000
//   dotnet fsi NetworkPathway.fsx -- --output results.json --csv results.csv --quiet
//
// ==============================================================================

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
Cli.exitIfHelp "NetworkPathway.fsx" "Quantum Influence Maximization for key drug target identification in protein interaction networks"
    [ { Cli.OptionSpec.Name = "k"; Description = "Number of targets to select (default: 3)"; Default = Some "3" }
      { Cli.OptionSpec.Name = "synergy-weight"; Description = "Weight for synergy bonus between interacting targets (default: 0.3)"; Default = Some "0.3" }
      { Cli.OptionSpec.Name = "shots"; Description = "Number of QAOA measurement shots (default: 1000)"; Default = Some "1000" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let k = Cli.getIntOr "k" 3 args
let synergyWeight = Cli.getFloatOr "synergy-weight" 0.3 args
let shots = Cli.getIntOr "shots" 1000 args

// ==============================================================================
// DOMAIN MODEL
// ==============================================================================

/// Protein in a signaling pathway
type Protein = {
    Name: string
    /// Disease relevance score (0-1, higher = more relevant)
    DiseaseScore: float
    /// Known druggability (0-1, higher = easier to drug)
    Druggability: float
    /// Functional role in the pathway
    Role: string
}

/// Protein-protein interaction
type Interaction = {
    Source: string
    Target: string
    /// Interaction strength (0-1)
    Strength: float
}

/// Signaling pathway network
type PathwayNetwork = {
    Name: string
    Proteins: Protein list
    Interactions: Interaction list
}

// ==============================================================================
// HELPER FUNCTIONS
// ==============================================================================

/// Convert pathway network to Influence Maximization problem
let toInfluenceProblem (network: PathwayNetwork) (targetCount: int) (synWeight: float) : InfluenceMaximization.Problem =
    let proteinIndex =
        network.Proteins
        |> List.mapi (fun i p -> p.Name, i)
        |> Map.ofList

    let nodes =
        network.Proteins
        |> List.map (fun p ->
            { InfluenceMaximization.Node.Id = p.Name
              InfluenceMaximization.Node.Score = p.DiseaseScore * p.Druggability })

    let edges =
        network.Interactions
        |> List.map (fun i ->
            { InfluenceMaximization.Edge.Source = Map.find i.Source proteinIndex
              InfluenceMaximization.Edge.Target = Map.find i.Target proteinIndex
              InfluenceMaximization.Edge.Weight = i.Strength })

    { InfluenceMaximization.Problem.Nodes = nodes
      InfluenceMaximization.Problem.Edges = edges
      InfluenceMaximization.Problem.K = targetCount
      InfluenceMaximization.Problem.SynergyWeight = synWeight }

// ==============================================================================
// EGFR SIGNALING PATHWAY
// ==============================================================================
// This is a well-known oncogenic pathway in many cancers (lung, breast,
// colorectal). EGFR signaling drives cell proliferation, survival, and
// migration through two major downstream cascades: RAS-MAPK and PI3K-AKT.

let results = System.Collections.Generic.List<Map<string, string>>()

let egfrPathway : PathwayNetwork = {
    Name = "EGFR Signaling"
    Proteins = [
        { Name = "EGFR"; DiseaseScore = 0.95; Druggability = 0.9; Role = "Receptor tyrosine kinase (erlotinib, gefitinib)" }
        { Name = "KRAS"; DiseaseScore = 0.90; Druggability = 0.3; Role = "GTPase (historically undruggable, sotorasib)" }
        { Name = "BRAF"; DiseaseScore = 0.85; Druggability = 0.8; Role = "Serine/threonine kinase (vemurafenib, dabrafenib)" }
        { Name = "MEK1"; DiseaseScore = 0.70; Druggability = 0.85; Role = "Dual-specificity kinase (trametinib, cobimetinib)" }
        { Name = "ERK1"; DiseaseScore = 0.60; Druggability = 0.7; Role = "MAP kinase, downstream effector" }
        { Name = "PI3K"; DiseaseScore = 0.80; Druggability = 0.75; Role = "Lipid kinase (alpelisib, idelalisib)" }
        { Name = "AKT1"; DiseaseScore = 0.75; Druggability = 0.8; Role = "Serine/threonine kinase (capivasertib)" }
        { Name = "PTEN"; DiseaseScore = 0.50; Druggability = 0.2; Role = "Tumor suppressor phosphatase (loss-of-function)" }
    ]
    Interactions = [
        // EGFR activates two major pathways
        { Source = "EGFR"; Target = "KRAS"; Strength = 0.9 }   // RAS-MAPK pathway
        { Source = "EGFR"; Target = "PI3K"; Strength = 0.8 }   // PI3K-AKT pathway
        // RAS-MAPK cascade
        { Source = "KRAS"; Target = "BRAF"; Strength = 0.9 }
        { Source = "BRAF"; Target = "MEK1"; Strength = 0.95 }
        { Source = "MEK1"; Target = "ERK1"; Strength = 0.9 }
        // PI3K-AKT pathway
        { Source = "PI3K"; Target = "AKT1"; Strength = 0.85 }
        // PTEN inhibits PI3K (negative regulation)
        { Source = "PTEN"; Target = "PI3K"; Strength = 0.7 }
        // Cross-talk between pathways
        { Source = "ERK1"; Target = "AKT1"; Strength = 0.3 }
    ]
}

if not quiet then
    printfn "============================================================"
    printfn "   Network Pathway Analysis - Key Target Identification"
    printfn "============================================================"
    printfn ""
    printfn "Pathway: %s" egfrPathway.Name
    printfn "Proteins: %d" egfrPathway.Proteins.Length
    printfn "Interactions: %d" egfrPathway.Interactions.Length
    printfn "Goal: Select %d key targets for drug development" k
    printfn "Synergy weight: %.2f" synergyWeight
    printfn ""

// Store protein details
for p in egfrPathway.Proteins do
    let score = p.DiseaseScore * p.Druggability
    results.Add(
        [ "type", "protein"
          "name", p.Name
          "disease_score", sprintf "%.2f" p.DiseaseScore
          "druggability", sprintf "%.2f" p.Druggability
          "combined_score", sprintf "%.2f" score
          "role", p.Role ]
        |> Map.ofList)

if not quiet then
    printfn "Pathway Structure:"
    printfn "  %-6s %10s %12s %10s  %s" "Name" "Disease" "Druggability" "Score" "Role"
    printfn "  %s" (String.replicate 80 "-")
    for p in egfrPathway.Proteins do
        let score = p.DiseaseScore * p.Druggability
        printfn "  %-6s %10.2f %12.2f %10.2f  %s" p.Name p.DiseaseScore p.Druggability score p.Role
    printfn ""

// ==============================================================================
// QUANTUM INFLUENCE MAXIMIZATION
// ==============================================================================

if not quiet then
    printfn "Quantum Analysis (QAOA Influence Maximization)"
    printfn "--------------------------------------------------------------"
    printfn ""

let problem = toInfluenceProblem egfrPathway k synergyWeight
let backend = LocalBackend() :> IQuantumBackend

match InfluenceMaximization.solve backend problem shots with
| Ok solution ->
    if not quiet then
        printfn "[OK] Key Targets Identified"
        printfn ""
        printfn "Selected %d targets (goal: %d):" solution.SelectedNodes.Length k
        printfn "  Total Score: %.3f" solution.TotalScore
        printfn "  Synergy Bonus: %.3f" solution.SynergyBonus
        printfn "  Combined Value: %.3f" (solution.TotalScore + solution.SynergyBonus)
        printfn "  Backend: %s" solution.BackendName

        if solution.SelectedNodes.Length <> k then
            printfn ""
            printfn "  Note: QAOA is heuristic -- may not find exact k targets."
            printfn "  The cardinality constraint is encoded but soft violations occur."
        printfn ""

    // Map back to original protein data for priority assessment
    let proteinMap = egfrPathway.Proteins |> List.map (fun p -> p.Name, p) |> Map.ofList

    if not quiet then
        printfn "============================================================"
        printfn "                   Key Drug Targets"
        printfn "============================================================"
        printfn ""

    for node in solution.SelectedNodes |> List.sortByDescending (fun n -> n.Score) do
        match Map.tryFind node.Id proteinMap with
        | Some protein ->
            let priority =
                if protein.Druggability > 0.7 && protein.DiseaseScore > 0.7 then "HIGH"
                elif protein.Druggability > 0.5 || protein.DiseaseScore > 0.5 then "MEDIUM"
                else "LOW"

            if not quiet then
                printfn "  * %s: Priority=%s" node.Id priority
                printfn "      Disease Score: %.2f" protein.DiseaseScore
                printfn "      Druggability: %.2f" protein.Druggability
                printfn "      Combined Score: %.2f" node.Score
                printfn "      Role: %s" protein.Role
                printfn ""

            results.Add(
                [ "type", "selected_target"
                  "name", node.Id
                  "priority", priority
                  "disease_score", sprintf "%.2f" protein.DiseaseScore
                  "druggability", sprintf "%.2f" protein.Druggability
                  "combined_score", sprintf "%.2f" node.Score
                  "role", protein.Role ]
                |> Map.ofList)
        | None -> ()

    // Show interactions between selected targets
    let selectedIds = solution.SelectedNodes |> List.map (fun n -> n.Id) |> Set.ofList
    let selectedInteractions =
        egfrPathway.Interactions
        |> List.filter (fun i ->
            Set.contains i.Source selectedIds && Set.contains i.Target selectedIds)

    if not quiet && not selectedInteractions.IsEmpty then
        printfn "Synergistic Interactions (between selected targets):"
        for i in selectedInteractions do
            printfn "  %s <-> %s (strength: %.2f)" i.Source i.Target i.Strength
        printfn ""

    // Summary result map
    results.Add(
        [ "type", "quantum_summary"
          "method", "QAOA Influence Maximization"
          "targets_selected", string solution.SelectedNodes.Length
          "target_goal", string k
          "total_score", sprintf "%.3f" solution.TotalScore
          "synergy_bonus", sprintf "%.3f" solution.SynergyBonus
          "combined_value", sprintf "%.3f" (solution.TotalScore + solution.SynergyBonus)
          "synergy_interactions", string selectedInteractions.Length
          "backend", solution.BackendName
          "shots", string shots
          "synergy_weight", sprintf "%.2f" synergyWeight ]
        |> Map.ofList)

| Error err ->
    if not quiet then printfn "[ERROR] %s" err.Message
    results.Add(
        [ "type", "error"
          "error", err.Message ]
        |> Map.ofList)

// ==============================================================================
// DRUG DISCOVERY INSIGHTS
// ==============================================================================

if not quiet then
    printfn "============================================================"
    printfn "              Drug Discovery Insights"
    printfn "============================================================"
    printfn ""
    printfn "1. COMBINATION THERAPY:"
    printfn "   - Selected targets can be used for multi-drug combination"
    printfn "   - Synergy bonus indicates potential for enhanced efficacy"
    printfn "   - EGFR + MEK dual blockade is an active clinical strategy"
    printfn ""
    printfn "2. DRUGGABILITY CONSIDERATION:"
    printfn "   - EGFR, BRAF, AKT1, MEK1 have approved inhibitors"
    printfn "   - KRAS was historically 'undruggable' (now targeted by sotorasib)"
    printfn "   - PTEN is a tumor suppressor -- reactivation, not inhibition"
    printfn ""
    printfn "3. PATHWAY COVERAGE:"
    printfn "   - EGFR blocks both MAPK and PI3K pathways at source"
    printfn "   - BRAF/MEK/ERK target the MAPK cascade specifically"
    printfn "   - PI3K/AKT1 target the survival/proliferation pathway"
    printfn ""
    printfn "4. RESISTANCE MECHANISMS:"
    printfn "   - Single-target therapy often leads to resistance"
    printfn "   - Multi-target selection addresses bypass signaling"
    printfn "   - Influence Maximization naturally selects synergistic pairs"
    printfn ""

// ==============================================================================
// SUGGESTED EXTENSIONS
// ==============================================================================

if not quiet then
    printfn "Suggested Extensions"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "1. Custom pathway network:"
    printfn "   - Load from CSV (future --input flag)"
    printfn "   - Columns: protein, disease_score, druggability, role"
    printfn ""
    printfn "2. Tune selection parameters:"
    printfn "   - --k 5 for larger combination panels"
    printfn "   - --synergy-weight 0.5 for stronger synergy preference"
    printfn "   - --synergy-weight 0.0 for pure score-based ranking"
    printfn ""
    printfn "3. Multi-pathway analysis:"
    printfn "   - Compare target sets across different cancer pathways"
    printfn "   - Identify pan-cancer vs tissue-specific targets"
    printfn ""
    printfn "4. Scale up with Azure Quantum:"
    printfn "   - Larger networks (50+ proteins) with hardware backends"
    printfn "   - Deeper QAOA circuits for better cardinality enforcement"
    printfn ""

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    printfn "============================================================"
    printfn "  Summary"
    printfn "============================================================"
    printfn ""
    printfn "[OK] Demonstrated quantum Influence Maximization for target ID"
    printfn "[OK] Used DrugDiscoverySolvers.InfluenceMaximization.solve via IQuantumBackend"
    printfn "[OK] Correct QUBO formulation with cardinality constraint"
    printfn "[OK] Identified synergistic interactions between selected targets"
    printfn ""
    printfn "Key Insight:"
    printfn "  Unlike MaxCut (which partitions ALL nodes), Influence Maximization"
    printfn "  SELECTS the k most influential nodes. This is the correct"
    printfn "  abstraction for multi-target drug discovery."
    printfn ""
    printfn "QAOA Limitations:"
    printfn "  Single-layer QAOA is heuristic and may not find exact k targets."
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
    let header = [ "type"; "name"; "disease_score"; "druggability"; "combined_score"; "role"; "priority"; "method"; "targets_selected"; "target_goal"; "total_score"; "synergy_bonus"; "combined_value"; "synergy_interactions"; "backend"; "shots"; "synergy_weight"; "error" ]
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
