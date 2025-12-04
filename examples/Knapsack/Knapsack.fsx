/// Knapsack Example - Resource Allocation with Quantum QAOA
/// 
/// USE CASE: Select optimal set of items to maximize value within weight/capacity constraint
/// 
/// PROBLEM: Given items with weights and values, and a capacity limit,
/// select a subset that maximizes total value without exceeding capacity.
/// 
/// The 0/1 Knapsack Problem is a fundamental optimization problem with applications in:
/// - Resource allocation: Select projects within budget
/// - Portfolio optimization: Choose investments within capital limit
/// - Cargo loading: Maximize value on truck/ship
/// - Task scheduling: Select tasks within time constraint
/// - Budget planning: Choose features within sprint capacity

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"


open FSharp.Azure.Quantum
open System

printfn "======================================"
printfn "Knapsack - Resource Allocation"
printfn "======================================"
printfn ""

// ============================================================================
// EXAMPLE 1: Software Project Selection
// ============================================================================

printfn "Example 1: Software Project Selection (Budget Allocation)"
printfn "------------------------------------------------------------"

// Available projects with costs and expected benefits
let projects = [
    ("API Rewrite", 150000.0, 500000.0)          // High value, high cost
    ("Mobile App", 100000.0, 350000.0)           // Good value/cost ratio
    ("Dashboard UI", 50000.0, 200000.0)          // Excellent value/cost ratio
    ("CI/CD Pipeline", 75000.0, 180000.0)        // Good infrastructure investment
    ("Performance Optimization", 40000.0, 120000.0)  // Quick win
    ("Security Audit", 60000.0, 250000.0)        // High value, medium cost
    ("Database Migration", 120000.0, 280000.0)   // Major undertaking
    ("AI Integration", 200000.0, 600000.0)       // Highest value, very expensive
]

let quarterlyBudget = 300000.0

let projectProblem = Knapsack.budgetAllocation projects quarterlyBudget

printfn "Available Projects: %d" projectProblem.ItemCount
printfn "Quarterly Budget: $%.0f" quarterlyBudget
printfn "Total Value (all projects): $%.0f" projectProblem.TotalValue
printfn "Total Cost (all projects): $%.0f" projectProblem.TotalWeight
printfn ""

// Solve using quantum QAOA
printfn "Solving with quantum QAOA..."
match Knapsack.solve projectProblem None with
| Ok solution ->
    printfn "✓ Quantum Solution Found!"
    printfn "  Selected Projects:"
    for item in solution.SelectedItems do
        let ratio = item.Value / item.Weight
        printfn "    - %s (cost: $%.0f, benefit: $%.0f, ROI: %.1fx)" 
            item.Id item.Weight item.Value ratio
    printfn ""
    printfn "  Total Cost: $%.0f (budget: $%.0f)" solution.TotalWeight quarterlyBudget
    printfn "  Total Benefit: $%.0f" solution.TotalValue
    printfn "  Capacity Utilization: %.1f%%" solution.CapacityUtilization
    printfn "  Value/Cost Ratio: %.2fx" solution.Efficiency
    printfn "  Feasible: %b" solution.IsFeasible
    printfn "  Backend: %s" solution.BackendName
    printfn ""
| Error msg ->
    printfn "✗ Quantum solve failed: %s" msg
    printfn ""

// Compare with classical DP (optimal)
printfn "Solving with classical DP (optimal)..."
let dpSolution = Knapsack.solveClassicalDP projectProblem

printfn "✓ Classical DP Solution (Optimal):"
printfn "  Selected Projects:"
for item in dpSolution.SelectedItems do
    printfn "    - %s (cost: $%.0f, benefit: $%.0f)" 
        item.Id item.Weight item.Value
printfn ""
printfn "  Total Cost: $%.0f" dpSolution.TotalWeight
printfn "  Total Benefit: $%.0f" dpSolution.TotalValue
printfn "  Capacity Utilization: %.1f%%" dpSolution.CapacityUtilization
printfn "  Value/Cost Ratio: %.2fx" dpSolution.Efficiency
printfn ""

// Compare with greedy heuristic
printfn "Solving with classical greedy..."
let greedySolution = Knapsack.solveClassicalGreedy projectProblem

printfn "✓ Classical Greedy Solution:"
printfn "  Selected Projects: %d" greedySolution.SelectedItems.Length
printfn "  Total Cost: $%.0f" greedySolution.TotalWeight
printfn "  Total Benefit: $%.0f" greedySolution.TotalValue
printfn "  Capacity Utilization: %.1f%%" greedySolution.CapacityUtilization
printfn ""

// ============================================================================
// EXAMPLE 2: Cargo Loading Optimization
// ============================================================================

printfn ""
printfn "Example 2: Cargo Loading (Maximize Value on Truck)"
printfn "---------------------------------------------------"

let cargo = [
    ("Electronics", 100.0, 50000.0)      // High value density
    ("Furniture", 500.0, 15000.0)        // Low value density
    ("Textiles", 200.0, 20000.0)         // Medium value density
    ("Appliances", 300.0, 35000.0)       // Good value density
    ("Jewelry", 10.0, 80000.0)           // Excellent value density!
    ("Books", 150.0, 5000.0)             // Very low value density
    ("Computers", 80.0, 60000.0)         // Excellent value density
    ("Tools", 120.0, 18000.0)            // Medium-low value density
]

let truckCapacity = 600.0  // kg

let cargoProblem = Knapsack.cargoLoading cargo truckCapacity

printfn "Available Cargo: %d items" cargoProblem.ItemCount
printfn "Truck Capacity: %.0f kg" truckCapacity
printfn ""

match Knapsack.solve cargoProblem None with
| Ok solution ->
    printfn "✓ Optimal Loading Plan:"
    printfn "  Selected Cargo:"
    for item in solution.SelectedItems |> List.sortByDescending (fun i -> i.Value / i.Weight) do
        printfn "    - %s (%.0f kg, $%.0f, $%.0f/kg)" 
            item.Id item.Weight item.Value (item.Value / item.Weight)
    printfn ""
    printfn "  Total Weight: %.0f kg (capacity: %.0f kg)" solution.TotalWeight truckCapacity
    printfn "  Total Value: $%.0f" solution.TotalValue
    printfn "  Utilization: %.1f%%" solution.CapacityUtilization
    printfn "  Average Value Density: $%.0f/kg" solution.Efficiency
| Error msg ->
    printfn "✗ Failed: %s" msg

printfn ""

// ============================================================================
// EXAMPLE 3: Sprint Task Selection
// ============================================================================

printfn ""
printfn "Example 3: Sprint Task Selection (Time-Constrained)"
printfn "---------------------------------------------------"

let tasks = [
    ("Critical Bug Fix", 4.0, 100.0)         // Very high priority
    ("Feature Request A", 8.0, 60.0)         // Medium priority, long
    ("Code Review", 2.0, 40.0)               // Quick, important
    ("Refactoring", 12.0, 50.0)              // Long, medium priority
    ("Documentation", 3.0, 30.0)             // Quick, lower priority
    ("Unit Tests", 5.0, 70.0)                // High priority
    ("Performance Tuning", 6.0, 80.0)        // High priority
    ("Security Update", 4.0, 90.0)           // Very high priority
]

let sprintHours = 40.0  // 1 week sprint

let sprintProblem = Knapsack.taskScheduling tasks sprintHours

printfn "Available Tasks: %d" sprintProblem.ItemCount
printfn "Sprint Capacity: %.0f hours" sprintHours
printfn ""

match Knapsack.solve sprintProblem None with
| Ok solution ->
    printfn "✓ Sprint Plan:"
    printfn "  Selected Tasks:"
    for item in solution.SelectedItems |> List.sortByDescending (fun i -> i.Value) do
        printfn "    - %s (%.0fh, priority: %.0f)" 
            item.Id item.Weight item.Value
    printfn ""
    printfn "  Total Time: %.0fh (available: %.0fh)" solution.TotalWeight sprintHours
    printfn "  Total Priority Points: %.0f" solution.TotalValue
    printfn "  Time Utilization: %.1f%%" solution.CapacityUtilization
    printfn "  Remaining Time: %.0fh" (sprintHours - solution.TotalWeight)
| Error msg ->
    printfn "✗ Failed: %s" msg

printfn ""

// ============================================================================
// EXAMPLE 4: Small Classic Knapsack
// ============================================================================

printfn ""
printfn "Example 4: Classic Knapsack (Textbook Example)"
printfn "----------------------------------------------"

let classicItems = [
    ("Gold Bar", 10.0, 60.0)
    ("Silver Bar", 20.0, 100.0)
    ("Bronze Bar", 30.0, 120.0)
]

let knapsackCapacity = 50.0
let classicProblem = Knapsack.createProblem classicItems knapsackCapacity

match Knapsack.solve classicProblem None with
| Ok solution ->
    printfn "Items: %A" (classicItems |> List.map (fun (id, w, v) -> sprintf "%s(w=%.0f,v=%.0f)" id w v))
    printfn "Capacity: %.0f" knapsackCapacity
    printfn ""
    printfn "✓ Quantum Solution:"
    printfn "  Selected: %A" (solution.SelectedItems |> List.map (fun i -> i.Id))
    printfn "  Weight: %.0f / %.0f" solution.TotalWeight knapsackCapacity
    printfn "  Value: %.0f" solution.TotalValue
    printfn "  Efficiency: %.2f value/weight" solution.Efficiency
| Error msg ->
    printfn "✗ Failed: %s" msg

printfn ""

// ============================================================================
// EXAMPLE 5: Different Problem Sizes
// ============================================================================

printfn ""
printfn "Example 5: Multiple Test Cases"
printfn "--------------------------------"

// Small problem
let smallItems = [
    ("Item1", 2.0, 10.0)
    ("Item2", 3.0, 15.0)
    ("Item3", 5.0, 30.0)
    ("Item4", 7.0, 35.0)
]

let smallProblem = Knapsack.createProblem smallItems 10.0

printfn "Small Problem (4 items, capacity 10):"
match Knapsack.solve smallProblem None with
| Ok solution ->
    printfn "  Selected: %A" (solution.SelectedItems |> List.map (fun i -> i.Id))
    printfn "  Total Value: %.0f" solution.TotalValue
    printfn "  Total Weight: %.0f" solution.TotalWeight
    printfn "  Efficiency: %.2f" solution.Efficiency
    printfn "  Feasible: %b" solution.IsFeasible
| Error msg ->
    printfn "  ✗ Failed: %s" msg

printfn ""
printfn "Example 5: Solution Validation and Metrics"
printfn "------------------------------------------"

// Create test problem
let testItems = [
    ("Item1", 2.0, 10.0)
    ("Item2", 3.0, 15.0)
    ("Item3", 5.0, 30.0)
    ("Item4", 7.0, 35.0)
]

let testCapacity = 10.0
let testProblem = Knapsack.createProblem testItems testCapacity

// Manual selection
let manualSelection = 
    testProblem.Items 
    |> List.filter (fun item -> item.Id = "Item2" || item.Id = "Item3")

printfn "Test Problem:"
printfn "  Items: %d" testProblem.ItemCount
printfn "  Capacity: %.0f" testCapacity
printfn ""

printfn "Manual Selection: Item2, Item3"
let isFeasible = Knapsack.isFeasible testProblem manualSelection
let totalWeight = Knapsack.totalWeight manualSelection
let totalValue = Knapsack.totalValue manualSelection
let efficiency = Knapsack.efficiency manualSelection

printfn "  Feasible: %b (weight %.0f ≤ %.0f)" isFeasible totalWeight testCapacity
printfn "  Total Value: %.0f" totalValue
printfn "  Efficiency: %.1f value/weight" efficiency
printfn ""

// Find optimal
let optimalSol = Knapsack.solveClassicalDP testProblem
printfn "Optimal Solution:"
printfn "  Selected: %A" (optimalSol.SelectedItems |> List.map (fun i -> i.Id))
printfn "  Total Value: %.0f" optimalSol.TotalValue
printfn "  Efficiency: %.1f" optimalSol.Efficiency

if totalValue = optimalSol.TotalValue then
    printfn "  ✓ Manual selection is optimal!"
else
    printfn "  ⚠ Manual selection is suboptimal (%.1f%% of optimal)" 
        (totalValue / optimalSol.TotalValue * 100.0)

printfn ""

// ============================================================================
// EXAMPLE 6: Random Problem Generation
// ============================================================================

printfn ""
printfn "Example 6: Random Problem Instance"
printfn "-----------------------------------"

let randomProblem = Knapsack.randomInstance 8 100.0 500.0 0.5

printfn "Random Problem: %d items, capacity %.0f" 
    randomProblem.ItemCount randomProblem.Capacity
printfn ""

// Quantum QAOA
match Knapsack.solve randomProblem None with
| Ok solution ->
    printfn "Quantum QAOA Solution:"
    printfn "  Selected Items: %d" solution.SelectedItems.Length
    printfn "  Value: %.0f" solution.TotalValue
    printfn "  Weight: %.0f / %.0f" solution.TotalWeight randomProblem.Capacity
    printfn "  Utilization: %.1f%%" solution.CapacityUtilization
    printfn "  Efficiency: %.2f value/weight" solution.Efficiency
    printfn "  Feasible: %b" solution.IsFeasible
| Error msg ->
    printfn "✗ Failed: %s" msg

printfn ""

printfn "======================================"
printfn "Knapsack Examples Complete!"
printfn "======================================"
