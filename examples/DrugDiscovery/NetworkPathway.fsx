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

/// Protein in a signaling pathway
type Protein = {
    Name: string
    /// Disease relevance score (0-1, higher = more relevant)
    DiseaseScore: float
    /// Known druggability (0-1, higher = easier to drug)
    Druggability: float
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
    Proteins: Protein list
    Interactions: Interaction list
}

// ==============================================================================
// HELPER FUNCTIONS
// ==============================================================================

/// Convert pathway network to Influence Maximization problem
let toInfluenceProblem (network: PathwayNetwork) (k: int) : InfluenceMaximization.Problem =
    // Build index for protein names
    let proteinIndex = 
        network.Proteins 
        |> List.mapi (fun i p -> p.Name, i)
        |> Map.ofList
    
    // Create nodes with combined disease/druggability score
    let nodes = 
        network.Proteins
        |> List.map (fun p -> 
            { InfluenceMaximization.Node.Id = p.Name
              InfluenceMaximization.Node.Score = p.DiseaseScore * p.Druggability })
    
    // Create edges with interaction strength as synergy weight
    let edges =
        network.Interactions
        |> List.map (fun i ->
            { InfluenceMaximization.Edge.Source = Map.find i.Source proteinIndex
              InfluenceMaximization.Edge.Target = Map.find i.Target proteinIndex
              InfluenceMaximization.Edge.Weight = i.Strength })
    
    { InfluenceMaximization.Problem.Nodes = nodes
      InfluenceMaximization.Problem.Edges = edges
      InfluenceMaximization.Problem.K = k
      InfluenceMaximization.Problem.SynergyWeight = 0.3 }

// ==============================================================================
// EXAMPLE: EGFR Signaling Pathway
// ==============================================================================

printfn "============================================================"
printfn "   Network Pathway Analysis - Key Target Identification"
printfn "============================================================"
printfn ""

// Define EGFR signaling pathway (simplified)
// This is a well-known oncogenic pathway in many cancers
let egfrPathway : PathwayNetwork = {
    Proteins = [
        { Name = "EGFR"; DiseaseScore = 0.95; Druggability = 0.9 }   // Receptor - highly druggable
        { Name = "KRAS"; DiseaseScore = 0.90; Druggability = 0.3 }   // Hard to drug historically
        { Name = "BRAF"; DiseaseScore = 0.85; Druggability = 0.8 }   // Kinase - druggable
        { Name = "MEK1"; DiseaseScore = 0.70; Druggability = 0.85 }  // Kinase - druggable
        { Name = "ERK1"; DiseaseScore = 0.60; Druggability = 0.7 }   // Downstream effector
        { Name = "PI3K"; DiseaseScore = 0.80; Druggability = 0.75 }  // Lipid kinase
        { Name = "AKT1"; DiseaseScore = 0.75; Druggability = 0.8 }   // Kinase - druggable
        { Name = "PTEN"; DiseaseScore = 0.50; Druggability = 0.2 }   // Tumor suppressor
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

printfn "EGFR Signaling Pathway"
printfn "============================================================"
printfn ""
printfn "Proteins: %d" egfrPathway.Proteins.Length
printfn "Interactions: %d" egfrPathway.Interactions.Length
printfn ""

// Display pathway structure
printfn "Pathway Structure:"
printfn "  %-6s %10s %12s %10s" "Name" "Disease" "Druggability" "Score"
printfn "  %s" (String.replicate 42 "-")
for p in egfrPathway.Proteins do
    let score = p.DiseaseScore * p.Druggability
    printfn "  %-6s %10.2f %12.2f %10.2f" p.Name p.DiseaseScore p.Druggability score
printfn ""

// ==============================================================================
// QUANTUM INFLUENCE MAXIMIZATION SOLUTION
// ==============================================================================

printfn "Quantum Analysis (QAOA Influence Maximization)"
printfn "------------------------------------------------------------"
printfn ""

// Select top 3 targets (reasonable for combination therapy)
let k = 3
let problem = toInfluenceProblem egfrPathway k

printfn "Goal: Select %d key targets for drug development" k
printfn ""

// Create local backend for simulation
let backend = LocalBackend() :> IQuantumBackend

match InfluenceMaximization.solve backend problem 1000 with
| Ok solution ->
    printfn "[OK] Key Targets Identified"
    printfn ""
    printfn "Selected %d targets (goal: %d):" solution.SelectedNodes.Length k
    printfn "  Total Score: %.3f" solution.TotalScore
    printfn "  Synergy Bonus: %.3f" solution.SynergyBonus
    printfn "  Combined Value: %.3f" (solution.TotalScore + solution.SynergyBonus)
    printfn "  Backend: %s" solution.BackendName
    
    if solution.SelectedNodes.Length <> k then
        printfn ""
        printfn "  Note: QAOA is heuristic - may not find exact k targets."
        printfn "  The cardinality constraint is encoded but soft violations occur."
    printfn ""
    
    printfn "============================================================"
    printfn "                   Key Drug Targets"
    printfn "============================================================"
    printfn ""
    
    // Map back to original protein data
    let proteinMap = egfrPathway.Proteins |> List.map (fun p -> p.Name, p) |> Map.ofList
    
    for node in solution.SelectedNodes |> List.sortByDescending (fun n -> n.Score) do
        match Map.tryFind node.Id proteinMap with
        | Some protein ->
            let priority = 
                if protein.Druggability > 0.7 && protein.DiseaseScore > 0.7 then "HIGH"
                elif protein.Druggability > 0.5 || protein.DiseaseScore > 0.5 then "MEDIUM"
                else "LOW"
            printfn "  * %s: Priority=%s" node.Id priority
            printfn "      Disease Score: %.2f" protein.DiseaseScore
            printfn "      Druggability: %.2f" protein.Druggability
            printfn "      Combined Score: %.2f" node.Score
            printfn ""
        | None -> ()
    
    // Show interactions between selected targets
    let selectedIds = solution.SelectedNodes |> List.map (fun n -> n.Id) |> Set.ofList
    let selectedInteractions = 
        egfrPathway.Interactions
        |> List.filter (fun i -> 
            Set.contains i.Source selectedIds && Set.contains i.Target selectedIds)
    
    if not selectedInteractions.IsEmpty then
        printfn "Synergistic Interactions (between selected targets):"
        for i in selectedInteractions do
            printfn "  %s <-> %s (strength: %.2f)" i.Source i.Target i.Strength
        printfn ""

| Error err ->
    printfn "[ERROR] %s" err.Message

// ==============================================================================
// CLASSICAL COMPARISON
// ==============================================================================

printfn "Classical Comparison (Greedy)"
printfn "------------------------------------------------------------"

let classicalSolution = InfluenceMaximization.solveClassical problem
printfn "Classical Greedy Selection:"
printfn "  Targets: %A" (classicalSolution.SelectedNodes |> List.map (fun n -> n.Id))
printfn "  Total Score: %.3f" classicalSolution.TotalScore
printfn "  Synergy Bonus: %.3f" classicalSolution.SynergyBonus
printfn "  Combined Value: %.3f" (classicalSolution.TotalScore + classicalSolution.SynergyBonus)
printfn ""

// ==============================================================================
// DRUG DISCOVERY INSIGHTS
// ==============================================================================

printfn "============================================================"
printfn "              Drug Discovery Insights"
printfn "============================================================"
printfn ""

printfn "Recommendations:"
printfn ""
printfn "1. COMBINATION THERAPY:"
printfn "   - Selected targets can be used for multi-drug combination"
printfn "   - Synergy bonus indicates potential for enhanced efficacy"
printfn ""
printfn "2. DRUGGABILITY CONSIDERATION:"
printfn "   - EGFR, BRAF, AKT1 have approved inhibitors"
printfn "   - KRAS was historically 'undruggable' (now targeted by sotorasib)"
printfn "   - PTEN is a tumor suppressor - reactivation, not inhibition"
printfn ""
printfn "3. PATHWAY COVERAGE:"
printfn "   - EGFR blocks both MAPK and PI3K pathways at source"
printfn "   - BRAF/MEK/ERK target MAPK cascade"
printfn "   - PI3K/AKT1 target survival pathway"
printfn ""

// ==============================================================================
// SUMMARY
// ==============================================================================

printfn "============================================================"
printfn "  Summary"
printfn "============================================================"
printfn ""
printfn "[OK] Demonstrated quantum Influence Maximization for target ID"
printfn "[OK] Used DrugDiscoverySolvers.InfluenceMaximization.solve"
printfn "[OK] Correct QUBO formulation with cardinality constraint"
printfn "[OK] Compared quantum vs classical greedy solutions"
printfn ""
printfn "Key Insight:"
printfn "  Unlike MaxCut (which partitions ALL nodes), Influence Maximization"
printfn "  SELECTS the k most influential nodes. This is the correct"
printfn "  abstraction for multi-target drug discovery."
printfn ""
printfn "QAOA Limitations:"
printfn "  Single-layer QAOA is heuristic and may not find exact k targets."
printfn "  Production use would require deeper circuits and parameter tuning."
printfn ""
