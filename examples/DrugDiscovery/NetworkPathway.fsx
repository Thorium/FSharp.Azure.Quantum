// ==============================================================================
// Network Pathway Analysis - Key Target Identification
// ==============================================================================
// Identifies key drug targets in protein interaction networks using quantum
// Influence Maximization (QAOA), selecting the k most influential proteins
// for drug development based on disease relevance and synergistic interactions.
//
// Influence Maximization maps directly to target identification:
//   - Nodes = proteins with combined scores (disease relevance * druggability)
//   - Edges = protein-protein interactions with synergy weights
//   - Goal = select k most influential proteins for drug targeting
//
// Accepts multiple proteins (built-in EGFR pathway or --input CSV), runs QAOA
// Influence Maximization, then outputs a ranked table showing which proteins
// were selected and a target assessment summary.
//
// Usage:
//   dotnet fsi NetworkPathway.fsx
//   dotnet fsi NetworkPathway.fsx -- --help
//   dotnet fsi NetworkPathway.fsx -- --proteins egfr,braf,pi3k
//   dotnet fsi NetworkPathway.fsx -- --input pathway.csv
//   dotnet fsi NetworkPathway.fsx -- --k 4 --synergy-weight 0.5 --shots 2000
//   dotnet fsi NetworkPathway.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Citri, A. & Yarden, Y. "EGF-ERBB signalling" Nature Rev. Mol. Cell Biol. (2006)
//   [2] Kempe, D. et al. "Maximizing the Spread of Influence" KDD (2003)
//   [3] Wikipedia: EGFR
//   [4] Wikipedia: Influence_maximization
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

Cli.exitIfHelp "NetworkPathway.fsx"
    "Quantum Influence Maximization for key drug target identification in protein interaction networks"
    [ { Cli.OptionSpec.Name = "input"; Description = "CSV file with custom protein/interaction definitions"; Default = Some "built-in EGFR pathway" }
      { Cli.OptionSpec.Name = "proteins"; Description = "Comma-separated protein names to include (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "k"; Description = "Number of targets to select"; Default = Some "3" }
      { Cli.OptionSpec.Name = "synergy-weight"; Description = "Weight for synergy bonus between interacting targets"; Default = Some "0.3" }
      { Cli.OptionSpec.Name = "shots"; Description = "Number of QAOA measurement shots"; Default = Some "1000" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputFile = args |> Cli.tryGet "input"
let proteinFilter = args |> Cli.getCommaSeparated "proteins"
let k = Cli.getIntOr "k" 3 args
let synergyWeight = Cli.getFloatOr "synergy-weight" 0.3 args
let shots = Cli.getIntOr "shots" 1000 args

// ==============================================================================
// TYPES
// ==============================================================================

/// Protein in a signaling pathway.
type Protein =
    { Name: string
      DiseaseScore: float
      Druggability: float
      Role: string }

/// Protein-protein interaction.
type Interaction =
    { Source: string
      Target: string
      Strength: float }

/// Result for each protein after QAOA selection.
type ProteinResult =
    { Protein: Protein
      CombinedScore: float
      Selected: bool
      Priority: string
      Rank: int
      HasVqeFailure: bool }

// ==============================================================================
// HELPER FUNCTIONS
// ==============================================================================

/// Calculate combined score for a protein.
let combinedScore (p: Protein) : float =
    p.DiseaseScore * p.Druggability

/// Determine drug development priority based on disease relevance and druggability.
let priorityRating (p: Protein) : string =
    if p.Druggability > 0.7 && p.DiseaseScore > 0.7 then "HIGH"
    elif p.Druggability > 0.5 || p.DiseaseScore > 0.5 then "MEDIUM"
    else "LOW"

/// Convert protein list + interactions to Influence Maximization problem.
let toInfluenceProblem (proteins: Protein list) (interactions: Interaction list) (targetCount: int) (synWeight: float) : InfluenceMaximization.Problem =
    let proteinIndex =
        proteins
        |> List.mapi (fun i p -> p.Name, i)
        |> Map.ofList

    let nodes =
        proteins
        |> List.map (fun p ->
            { InfluenceMaximization.Node.Id = p.Name
              InfluenceMaximization.Node.Score = combinedScore p })

    let edges =
        interactions
        |> List.choose (fun i ->
            match Map.tryFind i.Source proteinIndex, Map.tryFind i.Target proteinIndex with
            | Some src, Some tgt ->
                Some
                    { InfluenceMaximization.Edge.Source = src
                      InfluenceMaximization.Edge.Target = tgt
                      InfluenceMaximization.Edge.Weight = i.Strength }
            | _ -> None)

    { InfluenceMaximization.Problem.Nodes = nodes
      InfluenceMaximization.Problem.Edges = edges
      InfluenceMaximization.Problem.K = targetCount
      InfluenceMaximization.Problem.SynergyWeight = synWeight }

// ==============================================================================
// BUILT-IN PROTEIN PRESETS (EGFR signaling pathway)
// ==============================================================================

let private egfr =
    { Name = "EGFR"; DiseaseScore = 0.95; Druggability = 0.9; Role = "Receptor tyrosine kinase (erlotinib, gefitinib)" }

let private kras =
    { Name = "KRAS"; DiseaseScore = 0.90; Druggability = 0.3; Role = "GTPase (historically undruggable, sotorasib)" }

let private braf =
    { Name = "BRAF"; DiseaseScore = 0.85; Druggability = 0.8; Role = "Serine/threonine kinase (vemurafenib, dabrafenib)" }

let private mek1 =
    { Name = "MEK1"; DiseaseScore = 0.70; Druggability = 0.85; Role = "Dual-specificity kinase (trametinib, cobimetinib)" }

let private erk1 =
    { Name = "ERK1"; DiseaseScore = 0.60; Druggability = 0.7; Role = "MAP kinase, downstream effector" }

let private pi3k =
    { Name = "PI3K"; DiseaseScore = 0.80; Druggability = 0.75; Role = "Lipid kinase (alpelisib, idelalisib)" }

let private akt1 =
    { Name = "AKT1"; DiseaseScore = 0.75; Druggability = 0.8; Role = "Serine/threonine kinase (capivasertib)" }

let private pten =
    { Name = "PTEN"; DiseaseScore = 0.50; Druggability = 0.2; Role = "Tumor suppressor phosphatase (loss-of-function)" }

/// All built-in protein presets keyed by lowercase name.
let private builtinPresets : Map<string, Protein> =
    [ egfr; kras; braf; mek1; erk1; pi3k; akt1; pten ]
    |> List.map (fun p -> p.Name.ToLowerInvariant(), p)
    |> Map.ofList

let private presetNames =
    builtinPresets |> Map.toList |> List.map fst |> String.concat ", "

/// Built-in interactions for the EGFR pathway.
let private builtinInteractions : Interaction list =
    [ // EGFR activates two major pathways
      { Source = "EGFR"; Target = "KRAS"; Strength = 0.9 }
      { Source = "EGFR"; Target = "PI3K"; Strength = 0.8 }
      // RAS-MAPK cascade
      { Source = "KRAS"; Target = "BRAF"; Strength = 0.9 }
      { Source = "BRAF"; Target = "MEK1"; Strength = 0.95 }
      { Source = "MEK1"; Target = "ERK1"; Strength = 0.9 }
      // PI3K-AKT pathway
      { Source = "PI3K"; Target = "AKT1"; Strength = 0.85 }
      // PTEN inhibits PI3K (negative regulation)
      { Source = "PTEN"; Target = "PI3K"; Strength = 0.7 }
      // Cross-talk between pathways
      { Source = "ERK1"; Target = "AKT1"; Strength = 0.3 } ]

// ==============================================================================
// CSV INPUT PARSING
// ==============================================================================

/// Load proteins from a CSV file.
/// Expected columns: name, disease_score, druggability, role
/// OR: name, preset (to reference a built-in preset by name)
let private loadProteinsFromCsv (path: string) : Protein list =
    let rows, errors = Data.readCsvWithHeaderWithErrors path
    if not (List.isEmpty errors) && not quiet then
        for err in errors do
            eprintfn "  Warning (CSV): %s" err

    rows
    |> List.choose (fun row ->
        let get key = row.Values |> Map.tryFind key
        let name = get "name" |> Option.defaultValue "Unknown"
        match get "preset" with
        | Some presetKey ->
            let key = presetKey.Trim().ToLowerInvariant()
            match builtinPresets |> Map.tryFind key with
            | Some prot -> Some { prot with Name = name }
            | None ->
                if not quiet then
                    eprintfn "  Warning: unknown preset '%s' (available: %s)" presetKey presetNames
                None
        | None ->
            let diseaseScore =
                get "disease_score"
                |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
                |> Option.defaultValue 0.5
            let druggability =
                get "druggability"
                |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
                |> Option.defaultValue 0.5
            let role = get "role" |> Option.defaultValue "Unknown"
            Some
                { Name = name
                  DiseaseScore = diseaseScore
                  Druggability = druggability
                  Role = role })

/// Load interactions from a CSV file.
/// Expected columns: source, target, strength
let private loadInteractionsFromCsv (path: string) : Interaction list =
    let rows, errors = Data.readCsvWithHeaderWithErrors path
    if not (List.isEmpty errors) && not quiet then
        for err in errors do
            eprintfn "  Warning (CSV): %s" err

    rows
    |> List.choose (fun row ->
        let get key = row.Values |> Map.tryFind key
        match get "source", get "target" with
        | Some src, Some tgt ->
            let strength =
                get "strength"
                |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
                |> Option.defaultValue 0.5
            Some { Source = src; Target = tgt; Strength = strength }
        | _ -> None)

// ==============================================================================
// PROTEIN & INTERACTION SELECTION
// ==============================================================================

let proteins : Protein list =
    let allProteins =
        match inputFile with
        | Some path ->
            let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
            if not quiet then
                printfn "Loading proteins from: %s" resolved
            loadProteinsFromCsv resolved
        | None ->
            builtinPresets |> Map.toList |> List.map snd

    match proteinFilter with
    | [] -> allProteins
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        allProteins
        |> List.filter (fun p ->
            let key = p.Name.ToLowerInvariant()
            filterSet |> Set.exists (fun fil -> key.Contains fil))

if List.isEmpty proteins then
    eprintfn "Error: No proteins selected. Available presets: %s" presetNames
    exit 1

/// Get relevant interactions (only between proteins in the selected set).
let interactions : Interaction list =
    let proteinNames = proteins |> List.map (fun p -> p.Name) |> Set.ofList
    let allInteractions =
        match inputFile with
        | Some path ->
            // Try loading interactions from a companion file (same directory, *_interactions.csv)
            let dir = System.IO.Path.GetDirectoryName(Data.resolveRelative __SOURCE_DIRECTORY__ path)
            let baseName = System.IO.Path.GetFileNameWithoutExtension(path)
            let interPath = System.IO.Path.Combine(dir, baseName + "_interactions.csv")
            if System.IO.File.Exists interPath then
                if not quiet then
                    printfn "Loading interactions from: %s" interPath
                loadInteractionsFromCsv interPath
            else
                if not quiet then
                    printfn "No interaction file found (%s), using built-in interactions" (baseName + "_interactions.csv")
                builtinInteractions
        | None -> builtinInteractions
    allInteractions
    |> List.filter (fun i ->
        Set.contains i.Source proteinNames && Set.contains i.Target proteinNames)

// ==============================================================================
// QUANTUM BACKEND (Rule 1: all QAOA via IQuantumBackend)
// ==============================================================================

let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Network Pathway Analysis - Key Target Identification"
    printfn "=================================================================="
    printfn ""
    printfn "  Pathway:        EGFR Signaling"
    printfn "  Backend:        %s" backend.Name
    printfn "  Proteins:       %d" proteins.Length
    printfn "  Interactions:   %d" interactions.Length
    printfn "  Target count k: %d" k
    printfn "  Synergy weight: %.2f" synergyWeight
    printfn "  QAOA shots:     %d" shots
    printfn ""
    printfn "  %-6s  %8s  %12s  %8s  %s"
        "Name" "Disease" "Druggability" "Score" "Role"
    printfn "  %s" (String('-', 80))
    for p in proteins do
        printfn "  %-6s  %8.2f  %12.2f  %8.2f  %s"
            p.Name p.DiseaseScore p.Druggability (combinedScore p) p.Role
    printfn ""

// ==============================================================================
// QAOA INFLUENCE MAXIMIZATION
// ==============================================================================

if not quiet then
    printfn "Running QAOA Influence Maximization..."
    printfn ""

let problem = toInfluenceProblem proteins interactions k synergyWeight

let startTime = DateTime.Now
let solveResult = InfluenceMaximization.solve backend problem shots
let elapsed = (DateTime.Now - startTime).TotalSeconds

let hasFailure = Result.isError solveResult

// Build per-protein results
let selectedIds =
    match solveResult with
    | Ok solution -> solution.SelectedNodes |> List.map (fun n -> n.Id) |> Set.ofList
    | Error _ -> Set.empty

let proteinResults : ProteinResult list =
    proteins
    |> List.map (fun p ->
        { Protein = p
          CombinedScore = combinedScore p
          Selected = selectedIds |> Set.contains p.Name
          Priority = priorityRating p
          Rank = 0
          HasVqeFailure = hasFailure })

// Sort: selected first (by score descending), then unselected. Failed -> bottom.
let ranked =
    proteinResults
    |> List.sortBy (fun r ->
        if r.HasVqeFailure then (2, 0.0)
        elif r.Selected then (0, -r.CombinedScore)
        else (1, -r.CombinedScore))
    |> List.mapi (fun i r -> { r with Rank = i + 1 })

// Solution-level stats
let solutionStats =
    match solveResult with
    | Ok solution ->
        if not quiet then
            printfn "  [OK] QAOA Influence Maximization complete (%.1fs)" elapsed
            printfn "       Selected: %d / %d proteins (goal: %d)" solution.SelectedNodes.Length proteins.Length k
            printfn "       Total score: %.3f" solution.TotalScore
            printfn "       Synergy bonus: %.3f" solution.SynergyBonus
            printfn "       Combined value: %.3f" (solution.TotalScore + solution.SynergyBonus)
            printfn "       Backend: %s" solution.BackendName
            if solution.NumSelected <> k then
                printfn "       Note: QAOA is heuristic -- may not find exact k targets."
            printfn ""
        Some solution
    | Error err ->
        if not quiet then
            printfn "  [ERROR] QAOA failed: %s (%.1fs)" err.Message elapsed
            printfn ""
        None

// ==============================================================================
// RANKED COMPARISON TABLE
// ==============================================================================

let printTable () =
    printfn "=================================================================="
    printfn "  Key Drug Targets (QAOA Influence Maximization)"
    printfn "=================================================================="
    printfn ""
    printfn "  %-4s  %-6s  %8s  %12s  %8s  %8s  %8s"
        "#" "Name" "Disease" "Druggability" "Score" "Priority" "Selected"
    printfn "  %s" (String('=', 70))

    for r in ranked do
        if r.HasVqeFailure then
            printfn "  %-4d  %-6s  %8.2f  %12.2f  %8.2f  %8s  %8s"
                r.Rank r.Protein.Name r.Protein.DiseaseScore r.Protein.Druggability
                r.CombinedScore r.Priority "FAILED"
        else
            printfn "  %-4d  %-6s  %8.2f  %12.2f  %8.2f  %8s  %8s"
                r.Rank r.Protein.Name r.Protein.DiseaseScore r.Protein.Druggability
                r.CombinedScore r.Priority (if r.Selected then "YES" else "no")

    printfn ""

    match solutionStats with
    | Some solution ->
        let selectedProteins = ranked |> List.filter (fun r -> r.Selected && not r.HasVqeFailure)
        if not selectedProteins.IsEmpty then
            let selectedNames = selectedProteins |> List.map (fun r -> r.Protein.Name) |> Set.ofList
            let synergyInteractions =
                interactions
                |> List.filter (fun i ->
                    Set.contains i.Source selectedNames && Set.contains i.Target selectedNames)
            let avgDiseaseScore = selectedProteins |> List.averageBy (fun r -> r.Protein.DiseaseScore)
            let avgDruggability = selectedProteins |> List.averageBy (fun r -> r.Protein.Druggability)
            let highPriorityCount = selectedProteins |> List.filter (fun r -> r.Priority = "HIGH") |> List.length

            printfn "  Target Assessment:"
            printfn "  %s" (String('-', 55))
            printfn "    Selected:         %d / %d proteins (goal: %d)" selectedProteins.Length proteins.Length k
            printfn "    Total score:      %.3f" solution.TotalScore
            printfn "    Synergy bonus:    %.3f" solution.SynergyBonus
            printfn "    Combined value:   %.3f" (solution.TotalScore + solution.SynergyBonus)
            printfn "    Synergy pairs:    %d interactions between selected targets" synergyInteractions.Length
            printfn "    Avg disease rel.: %.2f" avgDiseaseScore
            printfn "    Avg druggability: %.2f" avgDruggability
            printfn "    High priority:    %d / %d selected" highPriorityCount selectedProteins.Length
            printfn ""

            if not synergyInteractions.IsEmpty then
                printfn "  Synergistic Interactions:"
                for i in synergyInteractions do
                    printfn "    %s <-> %s (strength: %.2f)" i.Source i.Target i.Strength
                printfn ""
    | None -> ()

// Always print -- this is the primary output.
printTable ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    match solutionStats with
    | Some _ ->
        printfn "  Solve time:   %.1f seconds" elapsed
        printfn "  Quantum:      QAOA Influence Maximization via IQuantumBackend [Rule 1 compliant]"
        printfn ""
    | None ->
        printfn "  QAOA Influence Maximization failed."
        printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    ranked
    |> List.map (fun r ->
        let totalScore =
            match solutionStats with Some s -> sprintf "%.3f" s.TotalScore | None -> "FAILED"
        let synergyBonus =
            match solutionStats with Some s -> sprintf "%.3f" s.SynergyBonus | None -> "FAILED"
        let combinedValue =
            match solutionStats with Some s -> sprintf "%.3f" (s.TotalScore + s.SynergyBonus) | None -> "FAILED"
        let backendName =
            match solutionStats with Some s -> s.BackendName | None -> "N/A"
        let numSelected =
            match solutionStats with Some s -> string s.NumSelected | None -> "FAILED"

        [ "rank", string r.Rank
          "name", r.Protein.Name
          "disease_score", sprintf "%.2f" r.Protein.DiseaseScore
          "druggability", sprintf "%.2f" r.Protein.Druggability
          "combined_score", sprintf "%.2f" r.CombinedScore
          "role", r.Protein.Role
          "priority", r.Priority
          "selected", string r.Selected
          "total_score", totalScore
          "synergy_bonus", synergyBonus
          "combined_value", combinedValue
          "num_selected", numSelected
          "target_goal", string k
          "synergy_weight", sprintf "%.2f" synergyWeight
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
        [ "rank"; "name"; "disease_score"; "druggability"; "combined_score"
          "role"; "priority"; "selected"; "total_score"; "synergy_bonus"
          "combined_value"; "num_selected"; "target_goal"; "synergy_weight"
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
    printfn "     --proteins egfr,braf,pi3k                Run specific proteins"
    printfn "     --input pathway.csv                      Load custom proteins from CSV"
    printfn "     --k 4 --synergy-weight 0.5               Adjust selection parameters"
    printfn "     --csv results.csv                        Export ranked table as CSV"
    printfn ""
