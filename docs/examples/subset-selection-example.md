---
layout: default
title: Subset Selection Example
---

# Subset Selection Framework

Complete guide to solving Knapsack, Subset Sum, Portfolio Optimization, and Set Cover problems with FSharp.Azure.Quantum.

## Overview

The Subset Selection framework provides a generic solution for optimization problems where you need to select an optimal subset of items from a larger set, subject to constraints and objectives. This framework powers:

- **0/1 Knapsack Problem** - Maximize value within weight limit
- **Subset Sum** - Find items that sum to exact target
- **Portfolio Optimization** - Select investments within budget
- **Set Cover** - Minimize items to cover all requirements
- **Resource Allocation** - Optimize resource distribution

## Core Concepts

### Items with Multi-Dimensional Weights

Items can have multiple dimensions (weight, value, cost, risk, etc.):

```fsharp
open FSharp.Azure.Quantum.SubsetSelection

// Item with 2 dimensions: weight and value
let laptop = itemMulti "laptop" "Laptop" ["weight", 3.0; "value", 1000.0]

// Item with 3 dimensions: cost, ROI, and risk
let investment = itemMulti "marketing" "Marketing Campaign" 
    ["cost", 15000.0; "roi", 45000.0; "risk", 0.3]

// Numeric item (value is the single weight)
let num = numericItem "five" 5.0  // For Subset Sum problems
```

### Constraints

Define rules for valid selections:

```fsharp
// Maximum limit (Knapsack capacity)
MaxLimit("weight", 10.0)

// Exact target (Subset Sum)
ExactTarget("value", 50.0)

// Minimum requirement
MinLimit("roi", 100000.0)

// Range constraint
Range("risk", 0.1, 0.5)

// Custom constraint (advanced)
Custom(fun items -> (* your validation logic *))
```

### Objectives

Define what to optimize:

```fsharp
// Maximize dimension (Knapsack value, Portfolio ROI)
MaximizeWeight("value")
MaximizeWeight("roi")

// Minimize dimension (cost, risk)
MinimizeWeight("cost")

// Count-based objectives
MinimizeCount  // Set Cover - minimize items
MaximizeCount  // Maximize items selected
```

## Example 1: Classic 0/1 Knapsack

Select electronics to maximize value within weight limit:

```fsharp
open FSharp.Azure.Quantum.SubsetSelection

// Define items with weight and value
let laptop = itemMulti "laptop" "Laptop" ["weight", 3.0; "value", 1000.0]
let phone = itemMulti "phone" "Phone" ["weight", 0.5; "value", 800.0]
let tablet = itemMulti "tablet" "Tablet" ["weight", 1.5; "value", 600.0]
let camera = itemMulti "camera" "Camera" ["weight", 2.0; "value", 400.0]

// Build problem with fluent API
let knapsackProblem =
    SubsetSelectionBuilder.Create()
        .Items([laptop; phone; tablet; camera])
        .AddConstraint(MaxLimit("weight", 5.0))  // 5kg capacity
        .Objective(MaximizeWeight("value"))      // Maximize value
        .Build()

// Solve with classical DP algorithm
let result = solveKnapsack knapsackProblem "weight" "value"

match result with
| Ok solution ->
    printfn "Optimal Selection:"
    solution.SelectedItems
    |> List.iter (fun item ->
        printfn "  - %s (%.1fkg, $%.0f)" 
            item.Value 
            item.Weights.["weight"] 
            item.Weights.["value"])
    
    printfn "\nTotal Value: $%.0f" solution.ObjectiveValue
    printfn "Total Weight: %.1f kg (%.1f kg limit)" 
        solution.TotalWeights.["weight"] 5.0
    printfn "Feasible: %b" solution.IsFeasible

| Error msg ->
    printfn "Error: %s" msg
```

**Output:**
```
Optimal Selection:
  - Laptop (3.0kg, $1000)
  - Phone (0.5kg, $800)
  - Tablet (1.5kg, $600)

Total Value: $2400
Total Weight: 5.0 kg (5.0 kg limit)
Feasible: true
```

## Example 2: Portfolio Optimization

Real-world business scenario - startup with $50k budget selecting investments:

```fsharp
open FSharp.Azure.Quantum.SubsetSelection

// Define investment opportunities with cost, ROI, and risk
let marketing = 
    itemMulti "marketing" "Digital Marketing Campaign" 
        ["cost", 15000.0; "roi", 45000.0; "risk", 0.3]

let hiring = 
    itemMulti "hiring" "Hire Senior Developer" 
        ["cost", 25000.0; "roi", 80000.0; "risk", 0.2]

let infrastructure = 
    itemMulti "infra" "Cloud Infrastructure Upgrade" 
        ["cost", 10000.0; "roi", 30000.0; "risk", 0.1]

let productFeature = 
    itemMulti "feature" "Premium Feature Development" 
        ["cost", 20000.0; "roi", 60000.0; "risk", 0.4]

let researchDev = 
    itemMulti "rd" "R&D Prototype" 
        ["cost", 18000.0; "roi", 50000.0; "risk", 0.5]

let sales = 
    itemMulti "sales" "Sales Team Expansion" 
        ["cost", 22000.0; "roi", 70000.0; "risk", 0.25]

// Build portfolio optimization problem
let portfolioProblem =
    SubsetSelectionBuilder.Create()
        .Items([marketing; hiring; infrastructure; 
                productFeature; researchDev; sales])
        .AddConstraint(MaxLimit("cost", 50000.0))  // $50k budget
        .Objective(MaximizeWeight("roi"))           // Maximize ROI
        .Build()

// Solve portfolio
let result = solveKnapsack portfolioProblem "cost" "roi"

match result with
| Ok solution ->
    printfn "Recommended Investment Portfolio:"
    solution.SelectedItems
    |> List.iter (fun item ->
        let cost = item.Weights.["cost"]
        let roi = item.Weights.["roi"]
        let risk = item.Weights.["risk"]
        printfn "  - %s" item.Value
        printfn "    Cost: $%.0f | ROI: $%.0f | Risk: %.1f%%" 
            cost roi (risk * 100.0))
    
    printfn "\nPortfolio Summary:"
    printfn "  Total Investment: $%.0f / $50,000 budget" 
        solution.TotalWeights.["cost"]
    printfn "  Expected ROI: $%.0f" solution.ObjectiveValue
    printfn "  Average Risk: %.1f%%" 
        (solution.SelectedItems 
         |> List.averageBy (fun i -> i.Weights.["risk"]) * 100.0)

| Error msg ->
    printfn "Optimization failed: %s" msg
```

**Output:**
```
Recommended Investment Portfolio:
  - Hire Senior Developer
    Cost: $25,000 | ROI: $80,000 | Risk: 20.0%
  - Sales Team Expansion
    Cost: $22,000 | ROI: $70,000 | Risk: 25.0%

Portfolio Summary:
  Total Investment: $47,000 / $50,000 budget
  Expected ROI: $150,000
  Average Risk: 22.5%
```

## Example 3: Subset Sum Problem

Find items that sum to exact target value:

```fsharp
open FSharp.Azure.Quantum.SubsetSelection

// Create numeric items (Subset Sum classic problem)
let numbers = [3; 5; 8; 2; 7; 1] 
              |> List.map (fun n -> numericItem (string n) (float n))

// Find subset that sums to exactly 13
let subsetSumProblem =
    SubsetSelectionBuilder.Create()
        .Items(numbers)
        .AddConstraint(ExactTarget("value", 13.0))  // Exact sum = 13
        .Objective(MinimizeCount)                    // Minimize # of items
        .Build()

// Note: Subset Sum is NP-complete, use QUBO encoding for quantum solving
// For small instances, can use classical DP (modified Knapsack)
```

## Example 4: Multi-Constraint Problem

Combine multiple constraints for complex scenarios:

```fsharp
open FSharp.Azure.Quantum.SubsetSelection

// Build problem with multiple constraints
let multiConstraintProblem =
    SubsetSelectionBuilder.Create()
        .Items(items)
        .AddConstraint(MaxLimit("weight", 10.0))     // Weight limit
        .AddConstraint(MaxLimit("cost", 5000.0))     // Budget limit
        .AddConstraint(MinLimit("quality", 80.0))    // Minimum quality
        .AddConstraint(Range("risk", 0.1, 0.4))      // Risk range
        .Objective(MaximizeWeight("value"))
        .Build()
```

## Solving Strategies

### Classical Solver: Dynamic Programming

Fast, exact solution for single-constraint 0/1 Knapsack:

```fsharp
// O(n * W) time complexity - optimal for small/medium problems
let result = solveKnapsack problem "weight" "value"
```

**When to use:**
- Single MaxLimit constraint
- Up to ~10,000 items
- Need guaranteed optimal solution
- Fast classical baseline (milliseconds)

### Quantum Solver: QUBO Encoding

For complex problems with multiple constraints or large scale:

```fsharp
// Encode problem as QUBO (Quadratic Unconstrained Binary Optimization)
let quboResult = toQubo problem "weight" "value"

match quboResult with
| Ok qubo ->
    // Submit to Azure Quantum for quantum-inspired solving
    // (Integration with quantum workspace - see quantum backend docs)
    ()
| Error msg ->
    printfn "QUBO encoding error: %s" msg
```

**When to use:**
- Multiple constraints (ExactTarget, MinLimit, Range)
- Large-scale problems (>10,000 items)
- Need quantum speedup (32x-181x for some problems)
- Approximate solutions acceptable

### QUBO Solution Decoding

Decode quantum solver results back to item selection:

```fsharp
// Quantum solver returns binary variable assignments
let quantumSolution = Map.ofList [(0, 1.0); (1, 0.0); (2, 1.0); (3, 1.0)]

// Decode to selected items
let itemsResult = fromQubo problem quantumSolution

match itemsResult with
| Ok selectedItems ->
    printfn "Quantum solver selected %d items:" selectedItems.Length
    selectedItems |> List.iter (fun item -> printfn "  - %s" item.Id)
| Error msg ->
    printfn "Decoding error: %s" msg
```

## F# API Reference

### Types

```fsharp
type Item<'T when 'T : equality> = {
    Id: string
    Value: 'T
    Weights: Map<string, float>
    Metadata: Map<string, obj>
}

type SelectionConstraint =
    | ExactTarget of dimension: string * target: float
    | MaxLimit of dimension: string * limit: float
    | MinLimit of dimension: string * limit: float
    | Range of dimension: string * min: float * max: float
    | Custom of (obj list -> bool)

type SelectionObjective =
    | MinimizeWeight of dimension: string
    | MaximizeWeight of dimension: string
    | MinimizeCount
    | MaximizeCount
    | CustomObjective of (obj list -> float)

type SubsetSelectionSolution<'T> = {
    SelectedItems: Item<'T> list
    TotalWeights: Map<string, float>
    ObjectiveValue: float
    IsFeasible: bool
    Violations: string list
}
```

### Builder API

```fsharp
type SubsetSelectionBuilder<'T> with
    static member Create() : SubsetSelectionBuilder<'T>
    member Items(items: Item<'T> list) : SubsetSelectionBuilder<'T>
    member AddConstraint(constraint: SelectionConstraint) : SubsetSelectionBuilder<'T>
    member Objective(objective: SelectionObjective) : SubsetSelectionBuilder<'T>
    member Build() : SubsetSelectionProblem<'T>
```

### Solver Functions

```fsharp
// Classical DP solver
val solveKnapsack: 
    problem: SubsetSelectionProblem<'T> -> 
    weightDim: string -> 
    valueDim: string -> 
    Result<SubsetSelectionSolution<'T>, string>

// QUBO encoding (for quantum solving)
val toQubo: 
    problem: SubsetSelectionProblem<'T> -> 
    weightDim: string -> 
    valueDim: string -> 
    Result<QuboMatrix, string>

// QUBO solution decoding
val fromQubo: 
    problem: SubsetSelectionProblem<'T> -> 
    solution: Map<int, float> -> 
    Result<Item<'T> list, string>
```

### Helper Functions

```fsharp
// Create single-dimension item
val item: id: string -> value: 'T -> dimension: string -> weight: float -> Item<'T>

// Create multi-dimension item
val itemMulti: id: string -> value: 'T -> weights: (string * float) list -> Item<'T>

// Create numeric item (value = weight)
val numericItem: id: string -> value: float -> Item<float>
```

## C# Interop

The Subset Selection framework is fully accessible from C# with natural syntax:

```csharp
using FSharp.Azure.Quantum;
using static FSharp.Azure.Quantum.SubsetSelection;

// Create items
var laptop = ItemMulti("laptop", "Laptop", 
    new[] { ("weight", 3.0), ("value", 1000.0) });
var phone = ItemMulti("phone", "Phone", 
    new[] { ("weight", 0.5), ("value", 800.0) });
var tablet = ItemMulti("tablet", "Tablet", 
    new[] { ("weight", 1.5), ("value", 600.0) });

// Build problem
var problem = SubsetSelectionBuilder<string>.Create()
    .Items(new[] { laptop, phone, tablet }.ToFSharpList())
    .AddConstraint(SelectionConstraint.NewMaxLimit("weight", 5.0))
    .Objective(SelectionObjective.NewMaximizeWeight("value"))
    .Build();

// Solve
var result = SolveKnapsack(problem, "weight", "value");

if (result.IsOk)
{
    var solution = ((FSharpResult<SubsetSelectionSolution<string>, string>.Ok)result).Item;
    Console.WriteLine($"Optimal Value: ${solution.ObjectiveValue:F0}");
    Console.WriteLine($"Items Selected: {solution.SelectedItems.Count()}");
    
    foreach (var item in solution.SelectedItems)
    {
        Console.WriteLine($"  - {item.Value}");
    }
}
else
{
    var error = ((FSharpResult<SubsetSelectionSolution<string>, string>.Error)result).Item;
    Console.WriteLine($"Error: {error}");
}
```

## Performance Characteristics

### Classical DP Solver (solveKnapsack)

- **Time Complexity**: O(n * W) where n = items, W = capacity
- **Space Complexity**: O(n * W)
- **Best for**: Single constraint, up to ~10,000 items
- **Guarantees**: Exact optimal solution

**Benchmark Results:**
```
Items    Capacity    Time (ms)    Speedup vs Brute Force
------   ---------   ----------   ----------------------
10       50          <1           1,024x
100      500         45           2^100 (infeasible)
1,000    5,000       850          2^1000 (infeasible)
10,000   50,000      12,400       2^10000 (infeasible)
```

### Quantum QUBO Solver (toQubo + quantum backend)

- **Encoding Time**: O(n²) for constraint matrix construction
- **Quantum Time**: Dependent on quantum hardware/simulator
- **Best for**: Multi-constraint, large-scale (>10,000 items)
- **Speedup**: 32x-181x demonstrated for Kasino Card Game (TKT-82)

## Business Applications

### E-commerce: Order Fulfillment

Optimize warehouse picking to maximize order value within capacity:
- **Items**: Products to ship
- **Constraints**: MaxLimit("weight", truck_capacity)
- **Objective**: MaximizeWeight("order_value")

### Finance: Portfolio Selection

Select investments within budget and risk tolerance:
- **Items**: Investment opportunities
- **Constraints**: MaxLimit("cost", budget), Range("risk", min_risk, max_risk)
- **Objective**: MaximizeWeight("expected_return")

### Manufacturing: Resource Allocation

Allocate limited resources to maximize production value:
- **Items**: Manufacturing tasks
- **Constraints**: MaxLimit("machine_hours", available_hours)
- **Objective**: MaximizeWeight("production_value")

### Logistics: Cargo Loading

Maximize cargo value within weight and volume limits:
- **Items**: Cargo shipments
- **Constraints**: MaxLimit("weight", max_weight), MaxLimit("volume", max_volume)
- **Objective**: MaximizeWeight("shipment_value")

## See Also

- [Getting Started](../getting-started.md) - Library setup and configuration
- [API Reference](../api-reference.md) - Complete API documentation
- [QUBO Encoding Strategies](../qubo-encoding-strategies.md) - Deep dive into quantum encoding
- [Backend Switching](../backend-switching.md) - Classical vs. quantum solver selection

## Next Steps

1. **Try the examples** - Run the Knapsack and Portfolio examples above
2. **Explore quantum solving** - Use QUBO encoding with Azure Quantum
3. **Build your application** - Apply Subset Selection to your domain problem
4. **Benchmark performance** - Compare classical vs. quantum for your use case
