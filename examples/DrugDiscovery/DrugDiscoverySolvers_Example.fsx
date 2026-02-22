// ============================================================================
// DrugDiscoverySolvers Example
// ============================================================================
// Demonstrates the three quantum drug-discovery solvers:
//   1. IndependentSet  - Find maximum-weight independent set (conflict-free nodes)
//   2. InfluenceMaximization - Select K most influential nodes in a network
//   3. DiverseSelection - Select diverse items within a budget
//
// Business Use Cases:
// - Compound Screening: Select non-conflicting drug candidates
// - Target Identification: Find most influential genes in a pathway
// - Library Design: Maximize chemical diversity in compound libraries
//
// Usage:
//   dotnet fsi DrugDiscoverySolvers_Example.fsx
//   dotnet fsi DrugDiscoverySolvers_Example.fsx -- --example independent
//   dotnet fsi DrugDiscoverySolvers_Example.fsx -- --shots 2000 --quiet
//   dotnet fsi DrugDiscoverySolvers_Example.fsx -- --output results.json --csv results.csv
// ============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
open FSharp.Azure.Quantum.Examples.Common

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Quantum

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv
Cli.exitIfHelp "DrugDiscoverySolvers_Example.fsx" "Quantum drug-discovery solvers (IndependentSet, Influence, Diverse)" [
    { Name = "example"; Description = "Which example: all, independent, influence, diverse"; Default = Some "all" }
    { Name = "shots"; Description = "Number of measurement shots"; Default = Some "1000" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress printed output"; Default = None }
] args

let exampleName = Cli.getOr "example" "all" args
let cliShots = Cli.getIntOr "shots" 1000 args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

let runAll = (exampleName = "all")

// Accumulate results for JSON/CSV export
let mutable jsonResults : obj list = []
let mutable csvRows : string list list = []

// --- Quantum Backend (Rule 1) ---
let quantumBackend = LocalBackend() :> IQuantumBackend

// ============================================================================
// Example 1: IndependentSet - Compound screening
// ============================================================================
// Select the highest-value compounds from a candidate library, avoiding
// pairs that have known chemical conflicts (toxicity, interference, etc.)

if runAll || exampleName = "independent" then
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"
    pr " Example 1: Independent Set - Compound Screening"
    pr " Select highest-value drug candidates avoiding chemical conflicts."
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"

    let problem : DrugDiscoverySolvers.IndependentSet.Problem = {
        Nodes = [
            { Id = "Aspirin";       Weight = 0.8 }
            { Id = "Ibuprofen";     Weight = 0.7 }
            { Id = "Paracetamol";   Weight = 0.9 }
            { Id = "Naproxen";      Weight = 0.6 }
            { Id = "Celecoxib";     Weight = 0.5 }
        ]
        Edges = [
            (0, 1)   // Aspirin conflicts with Ibuprofen (both NSAIDs)
            (1, 3)   // Ibuprofen conflicts with Naproxen (same mechanism)
            (3, 4)   // Naproxen conflicts with Celecoxib (COX inhibitors)
        ]
    }

    let result = DrugDiscoverySolvers.IndependentSet.solve quantumBackend problem cliShots

    match result with
    | Ok sol ->
        pr "IndependentSet Complete"
        pr ""
        pr "  %-16s  %8s  %10s" "Compound" "Weight" "Selected"
        pr "  %-16s  %8s  %10s" "----------------" "--------" "----------"
        let selectedIds = sol.SelectedNodes |> List.map (fun n -> n.Id) |> Set.ofList
        for n in problem.Nodes do
            let sel = selectedIds.Contains n.Id
            pr "  %-16s  %8.2f  %10s" n.Id n.Weight (if sel then "YES" else "-")
            jsonResults <- (box {| Example = "IndependentSet"; Compound = n.Id; Weight = n.Weight; Selected = sel |}) :: jsonResults
            csvRows <- [ "IndependentSet"; n.Id; sprintf "%.2f" n.Weight; (if sel then "true" else "false") ] :: csvRows
        pr ""
        pr "  Total weight:  %.2f" sol.TotalWeight
        pr "  Valid:         %b (no conflicts)" sol.IsValid
        pr "  Repaired:      %b" sol.WasRepaired
        pr "  Backend:       %s (%d shots)" sol.BackendName sol.NumShots

        // Classical comparison
        let classical = DrugDiscoverySolvers.IndependentSet.solveClassical problem
        pr ""
        pr "  Classical greedy: %.2f (quantum: %.2f)" classical.TotalWeight sol.TotalWeight
    | Error e ->
        pr "IndependentSet FAILED: %A" e

// ============================================================================
// Example 2: InfluenceMaximization - Gene pathway analysis
// ============================================================================
// Select K genes with highest influence in a regulatory network, considering
// both individual scores and synergistic interactions.

if runAll || exampleName = "influence" then
    pr ""
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"
    pr " Example 2: Influence Maximization - Gene Pathway Analysis"
    pr " Select top-K genes with highest combined influence in a regulatory network."
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"

    let problem : DrugDiscoverySolvers.InfluenceMaximization.Problem = {
        Nodes = [
            { Id = "TP53";   Score = 0.95 }
            { Id = "BRCA1";  Score = 0.85 }
            { Id = "EGFR";   Score = 0.80 }
            { Id = "MYC";    Score = 0.75 }
            { Id = "KRAS";   Score = 0.70 }
        ]
        Edges = [
            { Source = 0; Target = 1; Weight = 0.9 }   // TP53 -> BRCA1 strong
            { Source = 0; Target = 3; Weight = 0.6 }   // TP53 -> MYC
            { Source = 1; Target = 2; Weight = 0.7 }   // BRCA1 -> EGFR
            { Source = 2; Target = 4; Weight = 0.5 }   // EGFR -> KRAS
            { Source = 3; Target = 4; Weight = 0.4 }   // MYC -> KRAS
        ]
        K = 3
        SynergyWeight = 0.5
    }

    let result = DrugDiscoverySolvers.InfluenceMaximization.solve quantumBackend problem cliShots

    match result with
    | Ok sol ->
        pr "InfluenceMaximization Complete"
        pr ""
        pr "  %-10s  %8s  %10s" "Gene" "Score" "Selected"
        pr "  %-10s  %8s  %10s" "----------" "--------" "----------"
        let selectedIds = sol.SelectedNodes |> List.map (fun n -> n.Id) |> Set.ofList
        for n in problem.Nodes do
            let sel = selectedIds.Contains n.Id
            pr "  %-10s  %8.2f  %10s" n.Id n.Score (if sel then "YES" else "-")
            jsonResults <- (box {| Example = "Influence"; Gene = n.Id; Score = n.Score; Selected = sel |}) :: jsonResults
            csvRows <- [ "Influence"; n.Id; sprintf "%.2f" n.Score; (if sel then "true" else "false") ] :: csvRows
        pr ""
        pr "  Total score:    %.2f" sol.TotalScore
        pr "  Synergy bonus:  %.2f" sol.SynergyBonus
        pr "  Selected:       %d / %d (K=%d)" sol.NumSelected (List.length problem.Nodes) problem.K
        pr "  Backend:        %s (%d shots)" sol.BackendName sol.NumShots

        let classical = DrugDiscoverySolvers.InfluenceMaximization.solveClassical problem
        pr ""
        pr "  Classical greedy: %.2f (quantum: %.2f)" classical.TotalScore sol.TotalScore
    | Error e ->
        pr "InfluenceMaximization FAILED: %A" e

// ============================================================================
// Example 3: DiverseSelection - Compound library design
// ============================================================================
// Select a subset of compounds that maximizes total value and chemical
// diversity while staying within a budget constraint.

if runAll || exampleName = "diverse" then
    pr ""
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"
    pr " Example 3: Diverse Selection - Compound Library Design"
    pr " Select diverse high-value compounds within a budget."
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"

    let items : DrugDiscoverySolvers.DiverseSelection.Item list = [
        { Id = "CompoundA"; Value = 0.9; Cost = 100.0 }
        { Id = "CompoundB"; Value = 0.7; Cost = 80.0 }
        { Id = "CompoundC"; Value = 0.8; Cost = 120.0 }
        { Id = "CompoundD"; Value = 0.6; Cost = 60.0 }
    ]

    // Pairwise diversity scores (higher = more diverse)
    let diversity =
        array2D [
            [ 0.0; 0.8; 0.3; 0.9 ]
            [ 0.8; 0.0; 0.7; 0.4 ]
            [ 0.3; 0.7; 0.0; 0.6 ]
            [ 0.9; 0.4; 0.6; 0.0 ]
        ]

    let problem : DrugDiscoverySolvers.DiverseSelection.Problem = {
        Items = items
        Diversity = diversity
        Budget = 250.0
        DiversityWeight = 0.5
    }

    let result = DrugDiscoverySolvers.DiverseSelection.solve quantumBackend problem cliShots

    match result with
    | Ok sol ->
        pr "DiverseSelection Complete"
        pr ""
        pr "  %-14s  %8s  %8s  %10s" "Compound" "Value" "Cost" "Selected"
        pr "  %-14s  %8s  %8s  %10s" "--------------" "--------" "--------" "----------"
        let selectedIds = sol.SelectedItems |> List.map (fun i -> i.Id) |> Set.ofList
        for item in items do
            let sel = selectedIds.Contains item.Id
            pr "  %-14s  %8.2f  %8.1f  %10s" item.Id item.Value item.Cost (if sel then "YES" else "-")
            jsonResults <- (box {| Example = "DiverseSelection"; Compound = item.Id; Value = item.Value; Cost = item.Cost; Selected = sel |}) :: jsonResults
            csvRows <- [ "DiverseSelection"; item.Id; sprintf "%.2f" item.Value; (if sel then "true" else "false") ] :: csvRows
        pr ""
        pr "  Total value:      %.2f" sol.TotalValue
        pr "  Total cost:       %.1f / %.1f budget" sol.TotalCost problem.Budget
        pr "  Diversity bonus:  %.2f" sol.DiversityBonus
        pr "  Feasible:         %b" sol.IsFeasible
        pr "  Backend:          %s (%d shots)" sol.BackendName sol.NumShots

        let classical = DrugDiscoverySolvers.DiverseSelection.solveClassical problem
        pr ""
        pr "  Classical greedy: %.2f (quantum: %.2f)" classical.TotalValue sol.TotalValue
    | Error e ->
        pr "DiverseSelection FAILED: %A" e

// --- JSON output ---
outputPath |> Option.iter (fun path ->
    Reporting.writeJson path (jsonResults |> List.rev)
    pr "JSON written to %s" path
)

// --- CSV output ---
csvPath |> Option.iter (fun path ->
    let header = [ "Example"; "Item"; "Score"; "Selected" ]
    Reporting.writeCsv path header (csvRows |> List.rev)
    pr "CSV written to %s" path
)

// --- Usage hints ---
if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr "-------------------------------------------"
    pr "Tip: Use --example independent|influence|diverse to run one."
    pr "     Use --shots N to change measurement count (default 1000)."
    pr "     Use --output results.json or --csv results.csv to export."
    pr "     Use --help for all options."
