// ==============================================================================
// Combinatorial Screening - Diverse Compound Selection
// ==============================================================================
// Demonstrates using quantum Diverse Selection to select optimal compounds 
// from a screening library for drug discovery.
//
// Business Context:
// A pharmaceutical company has screened 10,000 compounds against a disease
// target. They need to select a diverse subset for follow-up testing, but
// have limited budget/resources (capacity constraint).
//
// This maps naturally to Diverse Selection (Quadratic Knapsack with Diversity):
// - Items = compounds with activity scores (value) and cost (weight)
// - Diversity = chemical dissimilarity between compounds
// - Capacity = budget or testing capacity
// - Goal = maximize total activity + diversity bonus within budget
//
// WHY NOT STANDARD KNAPSACK:
// Standard Knapsack only optimizes individual values. For drug discovery,
// we want DIVERSE compounds (different chemical scaffolds) to hedge risk.
// Diverse Selection adds pairwise diversity bonuses to the objective.
//
// Quantum Approach:
// - Use QAOA with diversity-aware QUBO formulation
// - Quadratic terms encode pairwise diversity bonuses
// - Constraint penalty ensures budget compliance
// - Note: QAOA is heuristic - may have soft constraint violations
//
// RULE1 COMPLIANCE:
// Uses DrugDiscoverySolvers.DiverseSelection.solve which uses IQuantumBackend.
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Quantum.DrugDiscoverySolvers

// ==============================================================================
// DOMAIN MODEL
// ==============================================================================

/// Compound from screening library
type Compound = {
    /// Compound identifier
    Id: string
    /// Chemical class (kinase inhibitor, GPCR ligand, etc.)
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

/// Screening library
type ScreeningLibrary = {
    Compounds: Compound list
    /// Total budget for follow-up testing
    Budget: float
}

// ==============================================================================
// HELPER FUNCTIONS
// ==============================================================================

/// Calculate compound value for selection (activity * selectivity * drug-likeness)
let compoundValue (c: Compound) : float =
    c.ActivityScore * c.Selectivity * c.DrugLikeness / 100.0

/// Calculate chemical diversity between two compounds (0-1, higher = more diverse)
/// Based on chemical class similarity
let chemicalDiversity (c1: Compound) (c2: Compound) : float =
    if c1.ChemicalClass = c2.ChemicalClass then 0.1  // Same class = low diversity
    else 0.8  // Different class = high diversity

/// Build diversity matrix from compound list
let buildDiversityMatrix (compounds: Compound list) : float[,] =
    let n = compounds.Length
    let matrix = Array2D.zeroCreate n n
    for i in 0 .. n - 1 do
        for j in 0 .. n - 1 do
            if i <> j then
                matrix.[i, j] <- chemicalDiversity compounds.[i] compounds.[j]
    matrix

/// Convert screening library to Diverse Selection problem
let toDiverseSelectionProblem (library: ScreeningLibrary) : DiverseSelection.Problem =
    let items = 
        library.Compounds
        |> List.map (fun c -> 
            { DiverseSelection.Item.Id = c.Id
              DiverseSelection.Item.Value = compoundValue c
              DiverseSelection.Item.Cost = c.FollowUpCost / 10000.0 }) // Normalize cost
    
    let diversity = buildDiversityMatrix library.Compounds
    
    { DiverseSelection.Problem.Items = items
      DiverseSelection.Problem.Diversity = diversity
      DiverseSelection.Problem.Budget = library.Budget / 10000.0  // Normalize budget
      DiverseSelection.Problem.DiversityWeight = 0.3 }

/// Get compound details by ID
let getCompound (library: ScreeningLibrary) (id: string) : Compound option =
    library.Compounds |> List.tryFind (fun c -> c.Id = id)

// ==============================================================================
// EXAMPLE: Kinase Inhibitor Screening
// ==============================================================================

printfn "============================================================"
printfn "   Combinatorial Screening - Diverse Compound Selection"
printfn "============================================================"
printfn ""

// Simulated screening hits from a kinase inhibitor campaign
let screeningLibrary : ScreeningLibrary = {
    Compounds = [
        // High activity hits
        { Id = "KIN-001"; ChemicalClass = "Type I"; ActivityScore = 95.0; 
          FollowUpCost = 15000.0; Selectivity = 0.8; DrugLikeness = 0.9 }
        { Id = "KIN-002"; ChemicalClass = "Type II"; ActivityScore = 88.0; 
          FollowUpCost = 12000.0; Selectivity = 0.95; DrugLikeness = 0.85 }
        { Id = "KIN-003"; ChemicalClass = "Type I"; ActivityScore = 82.0; 
          FollowUpCost = 10000.0; Selectivity = 0.7; DrugLikeness = 0.95 }
        
        // Medium activity hits
        { Id = "KIN-004"; ChemicalClass = "Allosteric"; ActivityScore = 75.0; 
          FollowUpCost = 20000.0; Selectivity = 0.99; DrugLikeness = 0.8 }
        { Id = "KIN-005"; ChemicalClass = "Type I"; ActivityScore = 70.0; 
          FollowUpCost = 8000.0; Selectivity = 0.6; DrugLikeness = 0.9 }
        { Id = "KIN-006"; ChemicalClass = "Type II"; ActivityScore = 68.0; 
          FollowUpCost = 9000.0; Selectivity = 0.85; DrugLikeness = 0.88 }
        
        // Lower activity but interesting scaffolds
        { Id = "KIN-007"; ChemicalClass = "Covalent"; ActivityScore = 55.0; 
          FollowUpCost = 25000.0; Selectivity = 0.98; DrugLikeness = 0.7 }
        { Id = "KIN-008"; ChemicalClass = "Type I"; ActivityScore = 50.0; 
          FollowUpCost = 6000.0; Selectivity = 0.5; DrugLikeness = 0.95 }
    ]
    Budget = 50000.0  // $50K budget for follow-up testing
}

printfn "Kinase Inhibitor Screening Campaign"
printfn "============================================================"
printfn ""
printfn "Compounds screened: %d" screeningLibrary.Compounds.Length
printfn "Follow-up budget: $%.0f" screeningLibrary.Budget
printfn ""

// Display compound library
printfn "Screening Hits:"
printfn "  %-8s %-12s %8s %10s %8s %8s %8s" "ID" "Class" "Activity" "Cost" "Select." "DrugLik" "Value"
printfn "  %s" (String.replicate 70 "-")
for c in screeningLibrary.Compounds do
    printfn "  %-8s %-12s %8.1f %10.0f %8.2f %8.2f %8.2f" 
        c.Id c.ChemicalClass c.ActivityScore c.FollowUpCost 
        c.Selectivity c.DrugLikeness (compoundValue c)
printfn ""

// ==============================================================================
// QUANTUM DIVERSE SELECTION SOLUTION
// ==============================================================================

printfn "Quantum Analysis (QAOA Diverse Selection)"
printfn "------------------------------------------------------------"
printfn ""

let problem = toDiverseSelectionProblem screeningLibrary

// Create local backend for simulation
let backend = LocalBackend() :> IQuantumBackend

match DiverseSelection.solve backend problem 1000 with
| Ok solution ->
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
    
    printfn "============================================================"
    printfn "              Selected Compounds (Diverse)"
    printfn "============================================================"
    printfn ""
    
    for item in solution.SelectedItems do
        match getCompound screeningLibrary item.Id with
        | Some compound ->
            printfn "  * %s (%s)" compound.Id compound.ChemicalClass
            printfn "     Activity: %.1f, Cost: $%.0f" compound.ActivityScore compound.FollowUpCost
            printfn "     Selectivity: %.2f, Drug-likeness: %.2f" compound.Selectivity compound.DrugLikeness
            printfn ""
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
        
        printfn "Selection Statistics:"
        printfn "------------------------------------------------------------"
        printfn "  Average Activity: %.1f" avgActivity
        printfn "  Average Selectivity: %.2f" avgSelectivity
        printfn "  Chemical Diversity: %d distinct classes" chemicalClasses.Length
        printfn "  Classes represented: %A" chemicalClasses
        printfn "  Total Cost: $%.0f / $%.0f budget (%.1f%% utilized)" 
            totalCost screeningLibrary.Budget (100.0 * totalCost / screeningLibrary.Budget)
        printfn ""

| Error err ->
    printfn "[ERROR] %s" err.Message

// ==============================================================================
// CLASSICAL COMPARISON
// ==============================================================================

printfn "Classical Comparison (Greedy)"
printfn "------------------------------------------------------------"

let classicalSolution = DiverseSelection.solveClassical problem
let classicalCompounds = 
    classicalSolution.SelectedItems
    |> List.choose (fun item -> getCompound screeningLibrary item.Id)
let classicalClasses = classicalCompounds |> List.map (fun c -> c.ChemicalClass) |> List.distinct

printfn "Classical Greedy Selection:"
printfn "  Compounds: %A" (classicalSolution.SelectedItems |> List.map (fun i -> i.Id))
printfn "  Total Value: %.2f" classicalSolution.TotalValue
printfn "  Diversity Bonus: %.2f" classicalSolution.DiversityBonus
printfn "  Combined Score: %.2f" (classicalSolution.TotalValue + classicalSolution.DiversityBonus)
printfn "  Chemical Classes: %d (%A)" classicalClasses.Length classicalClasses
printfn ""

// ==============================================================================
// DRUG DISCOVERY INSIGHTS
// ==============================================================================

printfn "============================================================"
printfn "              Drug Discovery Insights"
printfn "============================================================"
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
// SUMMARY
// ==============================================================================

printfn "============================================================"
printfn "  Summary"
printfn "============================================================"
printfn ""
printfn "[OK] Demonstrated quantum Diverse Selection for compound screening"
printfn "[OK] Used DrugDiscoverySolvers.DiverseSelection.solve (RULE1)"
printfn "[OK] Correct QUBO formulation with diversity bonus terms"
printfn "[OK] Compared quantum vs classical greedy solutions"
printfn ""
printfn "Key Insight:"
printfn "  Unlike standard Knapsack (linear objective), Diverse Selection"
printfn "  includes QUADRATIC diversity bonuses between selected items."
printfn "  This encourages chemical scaffold diversity in the selection."
printfn ""
printfn "QAOA Limitations:"
printfn "  Single-layer QAOA may slightly exceed budget constraint."
printfn "  Compare with classical greedy for production decisions."
printfn ""
