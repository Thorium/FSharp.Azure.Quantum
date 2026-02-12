// ==============================================================================
// Combinatorial Screening - Diverse Compound Selection
// ==============================================================================
// Demonstrates using quantum Diverse Selection to select optimal compounds
// from a screening library for drug discovery.
//
// Business Context:
// A pharmaceutical company has screened compounds against a disease target.
// They need to select a diverse subset for follow-up testing, but have a
// limited budget (capacity constraint). This maps to Diverse Selection
// (Quadratic Knapsack with Diversity):
//   - Items = compounds with activity scores (value) and cost (weight)
//   - Diversity = chemical dissimilarity between compounds
//   - Capacity = budget or testing capacity
//   - Goal = maximize total activity + diversity bonus within budget
//
// WHY NOT STANDARD KNAPSACK:
// Standard Knapsack only optimizes individual values. For drug discovery,
// we want DIVERSE compounds (different chemical scaffolds) to hedge risk.
// Diverse Selection adds pairwise diversity bonuses to the objective.
//
// Quantum Approach:
//   - QAOA with diversity-aware QUBO formulation
//   - Quadratic terms encode pairwise diversity bonuses
//   - Constraint penalty ensures budget compliance
//   - Note: QAOA is heuristic - may have soft constraint violations
//
// Usage:
//   dotnet fsi CombinatorialScreening.fsx
//   dotnet fsi CombinatorialScreening.fsx -- --help
//   dotnet fsi CombinatorialScreening.fsx -- --budget 75000 --diversity-weight 0.5
//   dotnet fsi CombinatorialScreening.fsx -- --shots 2000
//   dotnet fsi CombinatorialScreening.fsx -- --output results.json --csv results.csv --quiet
//
// ==============================================================================

(*
Background Theory
-----------------

DIVERSE COMPOUND SELECTION IN DRUG DISCOVERY:
After high-throughput screening (HTS) of large compound libraries (10K-1M
compounds), medicinal chemists must select a manageable subset for follow-up
testing. The selection must balance:
  - Activity: higher-scoring compounds are more promising
  - Diversity: different chemical scaffolds reduce risk
  - Cost: each compound has different follow-up costs
  - Budget: total resources for follow-up are limited

QUADRATIC KNAPSACK WITH DIVERSITY:
The Diverse Selection problem extends the classical knapsack with pairwise
diversity bonuses:

    maximize  Sum_i v_i*x_i + lambda * Sum_{i<j} d_ij*x_i*x_j
    subject to  Sum_i c_i*x_i <= B

Where v_i = value, c_i = cost, d_ij = diversity, B = budget, lambda = weight.
The quadratic diversity term makes this NP-hard even for small instances,
making it a natural candidate for quantum optimization (QAOA).

KINASE INHIBITOR CLASSES:
  - Type I: Bind active (DFG-in) conformation of ATP pocket
  - Type II: Bind inactive (DFG-out) conformation
  - Allosteric: Bind outside ATP pocket (high selectivity)
  - Covalent: Form irreversible bond with target (e.g., osimertinib)

References:
  [1] Thoma, G. et al. "Lead Optimization" J. Med. Chem. (2014)
  [2] Stumpfe, D. & Bajorath, J. "Compound Similarity" J. Chem. Inf. Model. (2011)
  [3] Wikipedia: Knapsack_problem
  [4] Farhi, E. et al. "QAOA" arXiv:1411.4028 (2014)
*)

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
// CLI PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "CombinatorialScreening.fsx"
    "Quantum diverse compound selection from a screening library (QAOA)"
    [ { Cli.OptionSpec.Name = "budget"; Description = "Follow-up testing budget ($)"; Default = Some "50000" }
      { Cli.OptionSpec.Name = "diversity-weight"; Description = "Weight for diversity bonus (0-1)"; Default = Some "0.3" }
      { Cli.OptionSpec.Name = "shots"; Description = "Number of QAOA measurement shots"; Default = Some "1000" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let budget = Cli.getFloatOr "budget" 50000.0 args
let diversityWeight = Cli.getFloatOr "diversity-weight" 0.3 args
let shots = Cli.getIntOr "shots" 1000 args

// ==============================================================================
// DOMAIN MODEL
// ==============================================================================

/// Compound from screening library.
type Compound = {
    /// Compound identifier
    Id: string
    /// Chemical class (kinase inhibitor type)
    ChemicalClass: string
    /// Activity score from primary screen (0-100)
    ActivityScore: float
    /// Estimated follow-up cost ($)
    FollowUpCost: float
    /// Selectivity index (higher = more selective)
    Selectivity: float
    /// Drug-likeness score (Lipinski compliance)
    DrugLikeness: float
}

/// Screening library with budget constraint.
type ScreeningLibrary = {
    Compounds: Compound list
    Budget: float
}

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

/// Convert screening library to Diverse Selection problem.
let toDiverseSelectionProblem (library: ScreeningLibrary) (divWeight: float) : DiverseSelection.Problem =
    let items =
        library.Compounds
        |> List.map (fun c ->
            { DiverseSelection.Item.Id = c.Id
              DiverseSelection.Item.Value = compoundValue c
              DiverseSelection.Item.Cost = c.FollowUpCost / 10000.0 })

    let diversity = buildDiversityMatrix library.Compounds

    { DiverseSelection.Problem.Items = items
      DiverseSelection.Problem.Diversity = diversity
      DiverseSelection.Problem.Budget = library.Budget / 10000.0
      DiverseSelection.Problem.DiversityWeight = divWeight }

/// Get compound details by ID.
let getCompound (library: ScreeningLibrary) (id: string) : Compound option =
    library.Compounds |> List.tryFind (fun c -> c.Id = id)

// ==============================================================================
// SCREENING LIBRARY DATA
// ==============================================================================

if not quiet then
    printfn "=============================================================="
    printfn "  Combinatorial Screening - Diverse Compound Selection"
    printfn "=============================================================="
    printfn ""

// Simulated screening hits from a kinase inhibitor campaign
let screeningLibrary : ScreeningLibrary = {
    Compounds = [
        // High activity hits
        { Id = "KIN-001"; ChemicalClass = "Type I"; ActivityScore = 95.0
          FollowUpCost = 15000.0; Selectivity = 0.8; DrugLikeness = 0.9 }
        { Id = "KIN-002"; ChemicalClass = "Type II"; ActivityScore = 88.0
          FollowUpCost = 12000.0; Selectivity = 0.95; DrugLikeness = 0.85 }
        { Id = "KIN-003"; ChemicalClass = "Type I"; ActivityScore = 82.0
          FollowUpCost = 10000.0; Selectivity = 0.7; DrugLikeness = 0.95 }

        // Medium activity hits
        { Id = "KIN-004"; ChemicalClass = "Allosteric"; ActivityScore = 75.0
          FollowUpCost = 20000.0; Selectivity = 0.99; DrugLikeness = 0.8 }
        { Id = "KIN-005"; ChemicalClass = "Type I"; ActivityScore = 70.0
          FollowUpCost = 8000.0; Selectivity = 0.6; DrugLikeness = 0.9 }
        { Id = "KIN-006"; ChemicalClass = "Type II"; ActivityScore = 68.0
          FollowUpCost = 9000.0; Selectivity = 0.85; DrugLikeness = 0.88 }

        // Lower activity but interesting scaffolds
        { Id = "KIN-007"; ChemicalClass = "Covalent"; ActivityScore = 55.0
          FollowUpCost = 25000.0; Selectivity = 0.98; DrugLikeness = 0.7 }
        { Id = "KIN-008"; ChemicalClass = "Type I"; ActivityScore = 50.0
          FollowUpCost = 6000.0; Selectivity = 0.5; DrugLikeness = 0.95 }
    ]
    Budget = budget
}

if not quiet then
    printfn "Kinase Inhibitor Screening Campaign"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "Compounds screened: %d" screeningLibrary.Compounds.Length
    printfn "Follow-up budget: $%.0f" screeningLibrary.Budget
    printfn "Diversity weight: %.2f" diversityWeight
    printfn ""
    printfn "Screening Hits:"
    printfn "  %-8s %-12s %8s %10s %8s %8s %8s" "ID" "Class" "Activity" "Cost" "Select." "DrugLik" "Value"
    printfn "  %s" (String.replicate 70 "-")
    for c in screeningLibrary.Compounds do
        printfn "  %-8s %-12s %8.1f %10.0f %8.2f %8.2f %8.2f"
            c.Id c.ChemicalClass c.ActivityScore c.FollowUpCost
            c.Selectivity c.DrugLikeness (compoundValue c)
    printfn ""

// ==============================================================================
// QUANTUM DIVERSE SELECTION
// ==============================================================================

if not quiet then
    printfn "Quantum Analysis (QAOA Diverse Selection)"
    printfn "--------------------------------------------------------------"
    printfn ""

let problem = toDiverseSelectionProblem screeningLibrary diversityWeight
let backend = LocalBackend() :> IQuantumBackend

let results = System.Collections.Generic.List<Map<string, string>>()

match DiverseSelection.solve backend problem shots with
| Ok solution ->
    if not quiet then
        printfn "[OK] Optimal Diverse Selection Found"
        printfn ""
        printfn "Selected Compounds: %d" solution.SelectedItems.Length
        printfn "Total Value: %.2f" solution.TotalValue
        printfn "Total Cost: %.2f (normalized)" solution.TotalCost
        printfn "Diversity Bonus: %.2f" solution.DiversityBonus
        printfn "Combined Score: %.2f" (solution.TotalValue + solution.DiversityBonus)
        printfn "Feasible (within budget): %b" solution.IsFeasible
        printfn "Backend: %s" solution.BackendName
        printfn ""

        printfn "=============================================================="
        printfn "  Selected Compounds (Diverse)"
        printfn "=============================================================="
        printfn ""

    for item in solution.SelectedItems do
        match getCompound screeningLibrary item.Id with
        | Some compound ->
            if not quiet then
                printfn "  * %s (%s)" compound.Id compound.ChemicalClass
                printfn "     Activity: %.1f, Cost: $%.0f" compound.ActivityScore compound.FollowUpCost
                printfn "     Selectivity: %.2f, Drug-likeness: %.2f" compound.Selectivity compound.DrugLikeness
                printfn ""

            results.Add(
                [ "type", "selected_compound"
                  "id", compound.Id
                  "chemical_class", compound.ChemicalClass
                  "activity_score", sprintf "%.1f" compound.ActivityScore
                  "follow_up_cost", sprintf "%.0f" compound.FollowUpCost
                  "selectivity", sprintf "%.2f" compound.Selectivity
                  "drug_likeness", sprintf "%.2f" compound.DrugLikeness
                  "value", sprintf "%.2f" (compoundValue compound) ]
                |> Map.ofList)
        | None -> ()

    // Summary statistics
    let selectedCompounds =
        solution.SelectedItems
        |> List.choose (fun item -> getCompound screeningLibrary item.Id)

    if not selectedCompounds.IsEmpty then
        let avgActivity = selectedCompounds |> List.averageBy (fun c -> c.ActivityScore)
        let avgSelectivity = selectedCompounds |> List.averageBy (fun c -> c.Selectivity)
        let chemicalClasses = selectedCompounds |> List.map (fun c -> c.ChemicalClass) |> List.distinct
        let totalCost = selectedCompounds |> List.sumBy (fun c -> c.FollowUpCost)
        let budgetUtil = 100.0 * totalCost / screeningLibrary.Budget

        if not quiet then
            printfn "Selection Statistics:"
            printfn "--------------------------------------------------------------"
            printfn "  Average Activity: %.1f" avgActivity
            printfn "  Average Selectivity: %.2f" avgSelectivity
            printfn "  Chemical Diversity: %d distinct classes" chemicalClasses.Length
            printfn "  Classes represented: %A" chemicalClasses
            printfn "  Total Cost: $%.0f / $%.0f budget (%.1f%% utilized)"
                totalCost screeningLibrary.Budget budgetUtil
            printfn ""

        results.Add(
            [ "type", "quantum_summary"
              "method", "QAOA_DiverseSelection"
              "compounds_selected", string solution.SelectedItems.Length
              "total_value", sprintf "%.2f" solution.TotalValue
              "total_cost_normalized", sprintf "%.2f" solution.TotalCost
              "diversity_bonus", sprintf "%.2f" solution.DiversityBonus
              "combined_score", sprintf "%.2f" (solution.TotalValue + solution.DiversityBonus)
              "is_feasible", string solution.IsFeasible
              "avg_activity", sprintf "%.1f" avgActivity
              "avg_selectivity", sprintf "%.2f" avgSelectivity
              "distinct_classes", string chemicalClasses.Length
              "total_cost_dollars", sprintf "%.0f" totalCost
              "budget_utilization_pct", sprintf "%.1f" budgetUtil
              "backend", solution.BackendName
              "shots", string shots ]
            |> Map.ofList)

| Error err ->
    if not quiet then printfn "[ERROR] %s" err.Message
    results.Add(
        [ "type", "quantum_error"
          "error", err.Message ]
        |> Map.ofList)

// ==============================================================================
// DRUG DISCOVERY INSIGHTS
// ==============================================================================

if not quiet then
    printfn "=============================================================="
    printfn "  Drug Discovery Insights"
    printfn "=============================================================="
    printfn ""
    printfn "Why Diversity Matters:"
    printfn ""
    printfn "1. RISK HEDGING:"
    printfn "   - Different scaffolds may have different off-target profiles"
    printfn "   - If one scaffold fails (toxicity), others may succeed"
    printfn ""
    printfn "2. INTELLECTUAL PROPERTY:"
    printfn "   - Multiple chemical series provide backup options"
    printfn "   - Freedom to operate around competitor patents"
    printfn ""
    printfn "3. MECHANISM COVERAGE:"
    printfn "   - Type I/II inhibitors have different binding modes"
    printfn "   - Allosteric inhibitors offer orthogonal mechanisms"
    printfn "   - Covalent inhibitors provide irreversible target engagement"
    printfn ""
    printfn "4. ADMET OPTIMIZATION:"
    printfn "   - Different scaffolds have different ADMET properties"
    printfn "   - Diverse series increases chance of finding optimal profile"
    printfn ""

// ==============================================================================
// SUGGESTED EXTENSIONS
// ==============================================================================

if not quiet then
    printfn "Suggested Extensions"
    printfn "--------------------------------------------------------------"
    printfn ""
    printfn "1. Custom compound library:"
    printfn "   - Load from CSV via --input compounds.csv (future)"
    printfn "   - Columns: id, class, activity, cost, selectivity, drug_likeness"
    printfn ""
    printfn "2. Tune diversity weight:"
    printfn "   - --diversity-weight 0.0 = pure value optimization (knapsack)"
    printfn "   - --diversity-weight 1.0 = heavy diversity preference"
    printfn "   - --diversity-weight 0.3 = balanced (default)"
    printfn ""
    printfn "3. Multi-objective screening:"
    printfn "   - Add toxicity/ADMET scores as additional objectives"
    printfn "   - Pareto front of value vs diversity vs cost"
    printfn ""
    printfn "4. Scale up:"
    printfn "   - Larger libraries (100+ compounds) with Azure Quantum backends"
    printfn "   - Real Tanimoto similarity for diversity (from RDKit fingerprints)"
    printfn ""

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    printfn "=============================================================="
    printfn "  Summary"
    printfn "=============================================================="
    printfn ""
    printfn "[OK] Demonstrated quantum Diverse Selection for compound screening"
    printfn "[OK] Used DrugDiscoverySolvers.DiverseSelection.solve via IQuantumBackend"
    printfn "[OK] Correct QUBO formulation with diversity bonus terms"
    printfn "[OK] Budget-constrained selection with chemical diversity"
    printfn ""
    printfn "Key Insight:"
    printfn "  Unlike standard Knapsack (linear objective), Diverse Selection"
    printfn "  includes QUADRATIC diversity bonuses between selected items."
    printfn "  This encourages chemical scaffold diversity in the selection."
    printfn ""
    printfn "QAOA Limitations:"
    printfn "  Single-layer QAOA may slightly exceed budget constraint."
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
    let header = [ "type"; "id"; "chemical_class"; "activity_score"; "follow_up_cost"; "selectivity"; "drug_likeness"; "value"; "method"; "compounds_selected"; "total_value"; "total_cost_normalized"; "diversity_bonus"; "combined_score"; "is_feasible"; "avg_activity"; "avg_selectivity"; "distinct_classes"; "total_cost_dollars"; "budget_utilization_pct"; "backend"; "shots"; "error" ]
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
