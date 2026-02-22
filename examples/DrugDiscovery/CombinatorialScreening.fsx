// ==============================================================================
// Combinatorial Screening - Diverse Compound Selection
// ==============================================================================
// Selects an optimal diverse subset of compounds from a screening library
// using quantum Diverse Selection (QAOA), balancing activity, diversity, and cost.
//
// The question: "Given a library of screening hits with limited follow-up budget,
// which diverse subset maximises total activity + chemical diversity?"
//
// Diverse Selection extends the classical knapsack with pairwise diversity bonuses:
//   maximize  Sum_i v_i*x_i + lambda * Sum_{i<j} d_ij*x_i*x_j
//   subject to  Sum_i c_i*x_i <= B
// The quadratic diversity term makes this NP-hard, a natural candidate for QAOA.
//
// Accepts multiple compounds (built-in presets or --input CSV), runs QAOA
// Diverse Selection, then outputs a ranked comparison table showing which
// compounds were selected and why.
//
// Usage:
//   dotnet fsi CombinatorialScreening.fsx
//   dotnet fsi CombinatorialScreening.fsx -- --help
//   dotnet fsi CombinatorialScreening.fsx -- --compounds kin-001,kin-004,kin-007
//   dotnet fsi CombinatorialScreening.fsx -- --input compounds.csv
//   dotnet fsi CombinatorialScreening.fsx -- --budget 75000 --diversity-weight 0.5
//   dotnet fsi CombinatorialScreening.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Thoma, G. et al. "Lead Optimization" J. Med. Chem. (2014)
//   [2] Stumpfe, D. & Bajorath, J. "Compound Similarity" J. Chem. Inf. Model. (2011)
//   [3] Wikipedia: Knapsack_problem
//   [4] Farhi, E. et al. "QAOA" arXiv:1411.4028 (2014)
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

Cli.exitIfHelp "CombinatorialScreening.fsx"
    "Quantum diverse compound selection from a screening library (QAOA)"
    [ { Cli.OptionSpec.Name = "input"; Description = "CSV file with custom compound definitions"; Default = Some "built-in presets" }
      { Cli.OptionSpec.Name = "compounds"; Description = "Comma-separated preset IDs to include (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "budget"; Description = "Follow-up testing budget ($)"; Default = Some "50000" }
      { Cli.OptionSpec.Name = "diversity-weight"; Description = "Weight for diversity bonus (0-1)"; Default = Some "0.3" }
      { Cli.OptionSpec.Name = "shots"; Description = "Number of QAOA measurement shots"; Default = Some "1000" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputFile = args |> Cli.tryGet "input"
let compoundFilter = args |> Cli.getCommaSeparated "compounds"
let budget = Cli.getFloatOr "budget" 50000.0 args
let diversityWeight = Cli.getFloatOr "diversity-weight" 0.3 args
let shots = Cli.getIntOr "shots" 1000 args

// ==============================================================================
// TYPES
// ==============================================================================

/// Compound from screening library.
type Compound =
    { Id: string
      ChemicalClass: string
      ActivityScore: float
      FollowUpCost: float
      Selectivity: float
      DrugLikeness: float }

/// Result for each compound after QAOA selection.
type CompoundResult =
    { Compound: Compound
      Value: float
      Selected: bool
      Rank: int
      HasVqeFailure: bool }

// ==============================================================================
// HELPER FUNCTIONS
// ==============================================================================

/// Calculate compound value for selection (activity * selectivity * drug-likeness).
let compoundValue (c: Compound) : float =
    c.ActivityScore * c.Selectivity * c.DrugLikeness / 100.0

/// Calculate chemical diversity between two compounds (0-1).
/// Same class = low diversity (0.1), different class = high diversity (0.8).
let chemicalDiversity (c1: Compound) (c2: Compound) : float =
    if c1.ChemicalClass = c2.ChemicalClass then 0.1 else 0.8

/// Build diversity matrix from compound list.
let buildDiversityMatrix (compounds: Compound list) : float[,] =
    let n = compounds.Length
    let matrix = Array2D.zeroCreate n n
    for i in 0 .. n - 1 do
        for j in 0 .. n - 1 do
            if i <> j then
                matrix.[i, j] <- chemicalDiversity compounds.[i] compounds.[j]
    matrix

// ==============================================================================
// BUILT-IN COMPOUND PRESETS
// ==============================================================================

let private kin001 =
    { Id = "KIN-001"; ChemicalClass = "Type I"; ActivityScore = 95.0
      FollowUpCost = 15000.0; Selectivity = 0.8; DrugLikeness = 0.9 }

let private kin002 =
    { Id = "KIN-002"; ChemicalClass = "Type II"; ActivityScore = 88.0
      FollowUpCost = 12000.0; Selectivity = 0.95; DrugLikeness = 0.85 }

let private kin003 =
    { Id = "KIN-003"; ChemicalClass = "Type I"; ActivityScore = 82.0
      FollowUpCost = 10000.0; Selectivity = 0.7; DrugLikeness = 0.95 }

let private kin004 =
    { Id = "KIN-004"; ChemicalClass = "Allosteric"; ActivityScore = 75.0
      FollowUpCost = 20000.0; Selectivity = 0.99; DrugLikeness = 0.8 }

let private kin005 =
    { Id = "KIN-005"; ChemicalClass = "Type I"; ActivityScore = 70.0
      FollowUpCost = 8000.0; Selectivity = 0.6; DrugLikeness = 0.9 }

let private kin006 =
    { Id = "KIN-006"; ChemicalClass = "Type II"; ActivityScore = 68.0
      FollowUpCost = 9000.0; Selectivity = 0.85; DrugLikeness = 0.88 }

let private kin007 =
    { Id = "KIN-007"; ChemicalClass = "Covalent"; ActivityScore = 55.0
      FollowUpCost = 25000.0; Selectivity = 0.98; DrugLikeness = 0.7 }

let private kin008 =
    { Id = "KIN-008"; ChemicalClass = "Type I"; ActivityScore = 50.0
      FollowUpCost = 6000.0; Selectivity = 0.5; DrugLikeness = 0.95 }

/// All built-in presets keyed by lowercase ID.
let private builtinPresets : Map<string, Compound> =
    [ kin001; kin002; kin003; kin004; kin005; kin006; kin007; kin008 ]
    |> List.map (fun c -> c.Id.ToLowerInvariant(), c)
    |> Map.ofList

let private presetNames =
    builtinPresets |> Map.toList |> List.map fst |> String.concat ", "

// ==============================================================================
// CSV INPUT PARSING
// ==============================================================================

/// Load compounds from a CSV file.
/// Expected columns: id, chemical_class, activity_score, follow_up_cost, selectivity, drug_likeness
/// OR: id, preset (to reference a built-in preset by ID)
let private loadCompoundsFromCsv (path: string) : Compound list =
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
            | Some compound -> Some { compound with Id = id }
            | None ->
                if not quiet then
                    eprintfn "  Warning: unknown preset '%s' (available: %s)" presetKey presetNames
                None
        | None ->
            let chemClass = get "chemical_class" |> Option.defaultValue "Unknown"
            let activity =
                get "activity_score"
                |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
                |> Option.defaultValue 50.0
            let cost =
                get "follow_up_cost"
                |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
                |> Option.defaultValue 10000.0
            let sel =
                get "selectivity"
                |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
                |> Option.defaultValue 0.5
            let drugLik =
                get "drug_likeness"
                |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
                |> Option.defaultValue 0.5
            Some
                { Id = id
                  ChemicalClass = chemClass
                  ActivityScore = activity
                  FollowUpCost = cost
                  Selectivity = sel
                  DrugLikeness = drugLik })

// ==============================================================================
// COMPOUND SELECTION
// ==============================================================================

let compounds : Compound list =
    let allCompounds =
        match inputFile with
        | Some path ->
            let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
            if not quiet then
                printfn "Loading compounds from: %s" resolved
            loadCompoundsFromCsv resolved
        | None ->
            builtinPresets |> Map.toList |> List.map snd

    match compoundFilter with
    | [] -> allCompounds
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        allCompounds
        |> List.filter (fun c ->
            let key = c.Id.ToLowerInvariant()
            filterSet |> Set.exists (fun fil -> key.Contains fil))

if List.isEmpty compounds then
    eprintfn "Error: No compounds selected. Available presets: %s" presetNames
    exit 1

// ==============================================================================
// QUANTUM BACKEND (Rule 1: all QAOA via IQuantumBackend)
// ==============================================================================

let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Combinatorial Screening - Diverse Compound Selection"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:        %s" backend.Name
    printfn "  Compounds:      %d" compounds.Length
    printfn "  Budget:         $%.0f" budget
    printfn "  Diversity wt:   %.2f" diversityWeight
    printfn "  QAOA shots:     %d" shots
    printfn ""
    printfn "  %-8s  %-12s  %8s  %10s  %8s  %8s  %8s"
        "ID" "Class" "Activity" "Cost ($)" "Select." "DrugLik" "Value"
    printfn "  %s" (String.replicate 70 "-")
    for c in compounds do
        printfn "  %-8s  %-12s  %8.1f  %10.0f  %8.2f  %8.2f  %8.2f"
            c.Id c.ChemicalClass c.ActivityScore c.FollowUpCost
            c.Selectivity c.DrugLikeness (compoundValue c)
    printfn ""

// ==============================================================================
// QAOA DIVERSE SELECTION
// ==============================================================================

if not quiet then
    printfn "Running QAOA Diverse Selection..."
    printfn ""

/// Convert compound list to Diverse Selection problem.
let toDiverseSelectionProblem (compoundList: Compound list) (bgt: float) (divWeight: float) : DiverseSelection.Problem =
    let items =
        compoundList
        |> List.map (fun c ->
            { DiverseSelection.Item.Id = c.Id
              DiverseSelection.Item.Value = compoundValue c
              DiverseSelection.Item.Cost = c.FollowUpCost / 10000.0 })

    let diversity = buildDiversityMatrix compoundList

    { DiverseSelection.Problem.Items = items
      DiverseSelection.Problem.Diversity = diversity
      DiverseSelection.Problem.Budget = bgt / 10000.0
      DiverseSelection.Problem.DiversityWeight = divWeight }

let problem = toDiverseSelectionProblem compounds budget diversityWeight

let startTime = DateTime.Now
let solveResult = DiverseSelection.solve backend problem shots
let elapsed = (DateTime.Now - startTime).TotalSeconds

let hasFailure = Result.isError solveResult

// Build per-compound results
let selectedIds =
    match solveResult with
    | Ok solution -> solution.SelectedItems |> List.map (fun item -> item.Id) |> Set.ofList
    | Error _ -> Set.empty

let compoundResults : CompoundResult list =
    compounds
    |> List.map (fun c ->
        { Compound = c
          Value = compoundValue c
          Selected = selectedIds |> Set.contains c.Id
          Rank = 0  // assigned after sort
          HasVqeFailure = hasFailure })

// Sort: selected first (by value descending), then unselected (by value descending).
// Failed â†’ bottom.
let ranked =
    compoundResults
    |> List.sortBy (fun r ->
        if r.HasVqeFailure then (2, 0.0, infinity)
        elif r.Selected then (0, -r.Value, 0.0)
        else (1, -r.Value, 0.0))
    |> List.mapi (fun i r -> { r with Rank = i + 1 })

// Solution-level stats
let solutionStats =
    match solveResult with
    | Ok solution ->
        if not quiet then
            printfn "  [OK] QAOA Diverse Selection complete (%.1fs)" elapsed
            printfn "       Selected: %d compounds" solution.SelectedItems.Length
            printfn "       Total value: %.2f" solution.TotalValue
            printfn "       Diversity bonus: %.2f" solution.DiversityBonus
            printfn "       Combined score: %.2f" (solution.TotalValue + solution.DiversityBonus)
            printfn "       Feasible: %b" solution.IsFeasible
            printfn "       Backend: %s" solution.BackendName
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
    printfn "  Compound Selection Results (QAOA Diverse Selection)"
    printfn "=================================================================="
    printfn ""
    printfn "  %-4s  %-8s  %-12s  %8s  %10s  %8s  %8s  %8s  %8s"
        "#" "ID" "Class" "Activity" "Cost ($)" "Select." "DrugLik" "Value" "Selected"
    printfn "  %s" (String('=', 95))

    for r in ranked do
        if r.HasVqeFailure then
            printfn "  %-4d  %-8s  %-12s  %8.1f  %10.0f  %8.2f  %8.2f  %8.2f  %8s"
                r.Rank r.Compound.Id r.Compound.ChemicalClass
                r.Compound.ActivityScore r.Compound.FollowUpCost
                r.Compound.Selectivity r.Compound.DrugLikeness
                r.Value "FAILED"
        else
            printfn "  %-4d  %-8s  %-12s  %8.1f  %10.0f  %8.2f  %8.2f  %8.2f  %8s"
                r.Rank r.Compound.Id r.Compound.ChemicalClass
                r.Compound.ActivityScore r.Compound.FollowUpCost
                r.Compound.Selectivity r.Compound.DrugLikeness
                r.Value (if r.Selected then "YES" else "no")

    printfn ""

    match solutionStats with
    | Some solution ->
        let selectedCompounds =
            ranked |> List.filter (fun r -> r.Selected && not r.HasVqeFailure)
        if not selectedCompounds.IsEmpty then
            let totalCost = selectedCompounds |> List.sumBy (fun r -> r.Compound.FollowUpCost)
            let avgActivity = selectedCompounds |> List.averageBy (fun r -> r.Compound.ActivityScore)
            let avgSelectivity = selectedCompounds |> List.averageBy (fun r -> r.Compound.Selectivity)
            let classes = selectedCompounds |> List.map (fun r -> r.Compound.ChemicalClass) |> List.distinct
            let budgetUtil = 100.0 * totalCost / budget

            printfn "  Selection Summary:"
            printfn "  %s" (String('-', 55))
            printfn "    Selected:       %d / %d compounds" selectedCompounds.Length compounds.Length
            printfn "    Total cost:     $%.0f / $%.0f (%.1f%% of budget)" totalCost budget budgetUtil
            printfn "    Avg activity:   %.1f" avgActivity
            printfn "    Avg selectivity:%.2f" avgSelectivity
            printfn "    Classes:        %d distinct (%s)" classes.Length (String.concat ", " classes)
            printfn "    QAOA score:     %.2f (value) + %.2f (diversity) = %.2f"
                solution.TotalValue solution.DiversityBonus
                (solution.TotalValue + solution.DiversityBonus)
            printfn "    Feasible:       %b" solution.IsFeasible
            printfn ""
    | None -> ()

// Always print â€” this is the primary output.
printTable ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    match solutionStats with
    | Some solution ->
        printfn "  Solve time:   %.1f seconds" elapsed
        printfn "  Quantum:      QAOA via IQuantumBackend [Rule 1 compliant]"
        printfn ""
    | None ->
        printfn "  QAOA selection failed."
        printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    ranked
    |> List.map (fun r ->
        let totalValue =
            match solutionStats with Some s -> sprintf "%.2f" s.TotalValue | None -> "FAILED"
        let diversityBonus =
            match solutionStats with Some s -> sprintf "%.2f" s.DiversityBonus | None -> "FAILED"
        let combinedScore =
            match solutionStats with Some s -> sprintf "%.2f" (s.TotalValue + s.DiversityBonus) | None -> "FAILED"
        let isFeasible =
            match solutionStats with Some s -> string s.IsFeasible | None -> "FAILED"
        let backendName =
            match solutionStats with Some s -> s.BackendName | None -> "N/A"

        [ "rank", string r.Rank
          "id", r.Compound.Id
          "chemical_class", r.Compound.ChemicalClass
          "activity_score", sprintf "%.1f" r.Compound.ActivityScore
          "follow_up_cost", sprintf "%.0f" r.Compound.FollowUpCost
          "selectivity", sprintf "%.2f" r.Compound.Selectivity
          "drug_likeness", sprintf "%.2f" r.Compound.DrugLikeness
          "value", sprintf "%.2f" r.Value
          "selected", string r.Selected
          "total_value", totalValue
          "diversity_bonus", diversityBonus
          "combined_score", combinedScore
          "is_feasible", isFeasible
          "budget", sprintf "%.0f" budget
          "budget_utilization_pct",
            (match solutionStats with
             | Some _ when r.Selected ->
                 let selCost = ranked |> List.filter (fun x -> x.Selected) |> List.sumBy (fun x -> x.Compound.FollowUpCost)
                 sprintf "%.1f" (100.0 * selCost / budget)
             | _ -> "")
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
        [ "rank"; "id"; "chemical_class"; "activity_score"; "follow_up_cost"
          "selectivity"; "drug_likeness"; "value"; "selected"
          "total_value"; "diversity_bonus"; "combined_score"; "is_feasible"
          "budget"; "budget_utilization_pct"; "backend"; "shots"
          "compute_time_s"; "has_vqe_failure" ]
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
    printfn "     --compounds kin-001,kin-004              Run specific compounds"
    printfn "     --input compounds.csv                    Load custom compounds from CSV"
    printfn "     --budget 75000                           Increase follow-up budget"
    printfn "     --diversity-weight 0.5                   Increase diversity preference"
    printfn "     --csv results.csv                        Export ranked table as CSV"
    printfn ""
