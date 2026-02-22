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

(*
===============================================================================
 Background Theory
===============================================================================

The 0/1 Knapsack Problem is a classic NP-hard combinatorial optimization problem:
given n items with weights wáµ¢ and values váµ¢, and a knapsack capacity W, select a
subset S to maximize Î£áµ¢âˆˆS váµ¢ subject to Î£áµ¢âˆˆS wáµ¢ â‰¤ W. Each item is either fully
included (1) or excluded (0)â€”no fractional selections. While dynamic programming
solves this in O(nW) pseudo-polynomial time, truly large instances (n > 100,
W > 10â¶) or variants with additional constraints remain computationally challenging.

The problem maps naturally to QUBO (Quadratic Unconstrained Binary Optimization)
formulation suitable for quantum optimization. Binary variables xáµ¢ âˆˆ {0,1} indicate
item selection. The objective maximizes Î£áµ¢ váµ¢xáµ¢ while a penalty term enforces the
capacity constraint: minimize -Î£áµ¢ váµ¢xáµ¢ + Î»(Î£áµ¢ wáµ¢xáµ¢ - W)Â² where Î» is a penalty
strength. QAOA then searches for the ground state of this cost Hamiltonian.

Key Equations:
  - Objective function: max Î£áµ¢ váµ¢xáµ¢  where xáµ¢ âˆˆ {0,1}
  - Capacity constraint: Î£áµ¢ wáµ¢xáµ¢ â‰¤ W
  - QUBO formulation: min -Î£áµ¢ váµ¢xáµ¢ + Î»Â·(Î£áµ¢ wáµ¢xáµ¢ - W - s)Â²
    where s is a slack variable for inequality â†’ equality conversion
  - Penalty strength: Î» > max(váµ¢) ensures feasibility dominates
  - Dynamic programming: O(nW) time, O(W) space (classical baseline)

Quantum Advantage:
  QAOA explores the 2â¿ solution space in superposition, potentially finding
  high-quality solutions faster than classical local search for large instances.
  The quantum approach is particularly promising for knapsack variants with
  complex constraints (multi-dimensional knapsack, multiple knapsacks, quadratic
  knapsack) where classical DP becomes impractical. Current NISQ devices handle
  n â‰ˆ 20-50 items; fault-tolerant systems could address industrial-scale problems
  with thousands of items and constraints.

References:
  [1] Martello & Toth, "Knapsack Problems: Algorithms and Computer Implementations",
      Wiley (1990). https://doi.org/10.1002/9781118033142
  [2] Lucas, "Ising formulations of many NP problems", Front. Phys. 2, 5 (2014).
      https://doi.org/10.3389/fphy.2014.00005
  [3] Glover et al., "Quantum Bridge Analytics I: A Tutorial on Formulating and
      Using QUBO Models", 4OR 17, 335-371 (2019). https://doi.org/10.1007/s10288-019-00424-y
  [4] Wikipedia: Knapsack_problem
      https://en.wikipedia.org/wiki/Knapsack_problem

Usage:
  dotnet fsi Knapsack.fsx                                          (defaults)
  dotnet fsi Knapsack.fsx -- --help                                (show options)
  dotnet fsi Knapsack.fsx -- --input items.csv --capacity 300000
  dotnet fsi Knapsack.fsx -- --example cargo                       (run cargo example)
  dotnet fsi Knapsack.fsx -- --quiet --output results.json         (pipeline mode)
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "Knapsack.fsx"
    "Quantum-ready 0/1 Knapsack optimization using QAOA."
    [ { Cli.OptionSpec.Name = "input";    Description = "CSV file with items (id,weight,value)";       Default = None }
      { Cli.OptionSpec.Name = "capacity"; Description = "Knapsack capacity constraint";                Default = Some "300000" }
      { Cli.OptionSpec.Name = "example";  Description = "Built-in example: projects|cargo|sprint|classic|validation|random"; Default = Some "projects" }
      { Cli.OptionSpec.Name = "output";   Description = "Write results to JSON file";                  Default = None }
      { Cli.OptionSpec.Name = "csv";      Description = "Write results to CSV file";                   Default = None }
      { Cli.OptionSpec.Name = "quiet";    Description = "Suppress informational output";               Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputPath = Cli.tryGet "input" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let exampleName = Cli.getOr "example" "projects" args

// ==============================================================================
// BUILT-IN DATASETS
// ==============================================================================

/// Software projects with costs and expected benefits
let builtInProjects =
    [ ("API Rewrite",              150000.0, 500000.0)
      ("Dashboard UI",              50000.0, 200000.0)
      ("Performance Optimization",  40000.0, 120000.0)
      ("Security Audit",            60000.0, 250000.0)
      ("Database Migration",       120000.0, 280000.0) ]

/// Cargo items with weight (kg) and value ($)
let builtInCargo =
    [ ("Electronics",  100.0, 50000.0)
      ("Furniture",    500.0, 15000.0)
      ("Textiles",     200.0, 20000.0)
      ("Appliances",   300.0, 35000.0)
      ("Jewelry",       10.0, 80000.0)
      ("Books",        150.0,  5000.0)
      ("Computers",     80.0, 60000.0)
      ("Tools",        120.0, 18000.0) ]

/// Sprint tasks with time (hours) and priority score
let builtInTasks =
    [ ("Critical Bug Fix",     4.0, 100.0)
      ("Feature Request A",    8.0,  60.0)
      ("Code Review",          2.0,  40.0)
      ("Refactoring",         12.0,  50.0)
      ("Documentation",        3.0,  30.0)
      ("Unit Tests",            5.0,  70.0)
      ("Performance Tuning",   6.0,  80.0)
      ("Security Update",      4.0,  90.0) ]

/// Classic textbook items
let builtInClassic =
    [ ("Gold Bar",   10.0,  60.0)
      ("Silver Bar", 20.0, 100.0)
      ("Bronze Bar", 30.0, 120.0) ]

/// Small validation items
let builtInValidation =
    [ ("Item1", 2.0, 10.0)
      ("Item2", 3.0, 15.0)
      ("Item3", 5.0, 30.0)
      ("Item4", 7.0, 35.0) ]

// ==============================================================================
// DATA LOADING
// ==============================================================================

/// Load items from a CSV file with columns: id, weight, value
let loadItemsFromCsv (path: string) : (string * float * float) list =
    let rows = Data.readCsvWithHeader path
    rows
    |> List.map (fun row ->
        let id =
            row.Values
            |> Map.tryFind "id"
            |> Option.orElse (row.Values |> Map.tryFind "name")
            |> Option.defaultValue "Unknown"
        let weight =
            row.Values
            |> Map.tryFind "weight"
            |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
            |> Option.defaultValue 0.0
        let value =
            row.Values
            |> Map.tryFind "value"
            |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
            |> Option.defaultValue 0.0
        (id, weight, value))

// ==============================================================================
// DISPLAY HELPERS
// ==============================================================================

let printHeader title =
    if not quiet then
        printfn ""
        printfn "%s" title
        printfn "%s" (String.replicate (String.length title) "-")

let printSolution (label: string) (solution: Knapsack.Solution) (capacity: float) =
    if not quiet then
        printfn "%s" label
        for item in solution.SelectedItems do
            let ratio = if item.Weight > 0.0 then item.Value / item.Weight else 0.0
            printfn "    - %s (weight: %.0f, value: %.0f, ratio: %.1fx)"
                item.Id item.Weight item.Value ratio
        printfn "  Total Weight: %.0f / %.0f" solution.TotalWeight capacity
        printfn "  Total Value: %.0f" solution.TotalValue
        printfn "  Utilization: %.1f%%" solution.CapacityUtilization
        printfn "  Efficiency: %.2f value/weight" solution.Efficiency
        printfn "  Feasible: %b" solution.IsFeasible
        printfn "  Backend: %s" solution.BackendName
        printfn ""

// ==============================================================================
// SOLVE AND COLLECT RESULTS
// ==============================================================================

/// Solve a problem and return structured result row for output
let solveAndReport
    (label: string)
    (items: (string * float * float) list)
    (capacity: float)
    (createProblem: (string * float * float) list -> float -> Knapsack.Problem)
    : Map<string, string> option =

    let problem = createProblem items capacity

    if not quiet then
        printfn "Items: %d | Capacity: %.0f | Total Weight: %.0f | Total Value: %.0f"
            problem.ItemCount capacity problem.TotalWeight problem.TotalValue

    match Knapsack.solve problem None with
    | Ok solution ->
        printSolution "  Quantum QAOA solution:" solution capacity

        Some (Map.ofList
            [ "example",           label
              "method",            "QAOA"
              "items_count",       sprintf "%d" problem.ItemCount
              "capacity",          sprintf "%.0f" capacity
              "selected_count",    sprintf "%d" solution.SelectedItems.Length
              "selected_items",    solution.SelectedItems |> List.map (fun i -> i.Id) |> String.concat "; "
              "total_weight",      sprintf "%.2f" solution.TotalWeight
              "total_value",       sprintf "%.2f" solution.TotalValue
              "utilization_pct",   sprintf "%.1f" solution.CapacityUtilization
              "efficiency",        sprintf "%.2f" solution.Efficiency
              "feasible",          sprintf "%b" solution.IsFeasible
              "backend",           solution.BackendName ])

    | Error err ->
        if not quiet then printfn "  Failed: %A" err
        None

// ==============================================================================
// MAIN EXECUTION
// ==============================================================================

if not quiet then
    printfn "======================================"
    printfn "Knapsack - Resource Allocation"
    printfn "======================================"

let allResults = ResizeArray<Map<string, string>>()

match inputPath with
| Some path ->
    // External data mode: load from CSV
    let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
    if not quiet then printfn "Loading items from: %s" resolved

    let items = loadItemsFromCsv resolved
    let capacity = Cli.getFloatOr "capacity" 300000.0 args

    printHeader "Custom Input"
    solveAndReport "custom" items capacity Knapsack.createProblem
    |> Option.iter allResults.Add

| None ->
    // Built-in examples mode
    match exampleName.ToLowerInvariant() with
    | "all" ->
        // Run all built-in examples
        printHeader "Example 1: Software Project Selection (Budget Allocation)"
        solveAndReport "projects" builtInProjects 300000.0 Knapsack.budgetAllocation
        |> Option.iter allResults.Add

        // Classical greedy comparison
        let greedySol = Knapsack.solveClassicalGreedy (Knapsack.budgetAllocation builtInProjects 300000.0)
        if not quiet then
            printfn "  Classical Greedy comparison:"
            printfn "    Selected: %d items | Value: %.0f | Weight: %.0f"
                greedySol.SelectedItems.Length greedySol.TotalValue greedySol.TotalWeight
            printfn ""

        printHeader "Example 2: Cargo Loading (Maximize Value on Truck)"
        solveAndReport "cargo" builtInCargo 600.0 Knapsack.cargoLoading
        |> Option.iter allResults.Add

        printHeader "Example 3: Sprint Task Selection (Time-Constrained)"
        solveAndReport "sprint" builtInTasks 40.0 Knapsack.taskScheduling
        |> Option.iter allResults.Add

        printHeader "Example 4: Classic Knapsack (Textbook Example)"
        solveAndReport "classic" builtInClassic 50.0 Knapsack.createProblem
        |> Option.iter allResults.Add

        printHeader "Example 5: Solution Validation and Metrics"
        let testProblem = Knapsack.createProblem builtInValidation 10.0
        solveAndReport "validation" builtInValidation 10.0 Knapsack.createProblem
        |> Option.iter allResults.Add

        // Manual selection analysis
        if not quiet then
            let manualSelection =
                testProblem.Items
                |> List.filter (fun item -> item.Id = "Item2" || item.Id = "Item3")
            let isFeasible = Knapsack.isFeasible testProblem manualSelection
            let tw = Knapsack.totalWeight manualSelection
            let tv = Knapsack.totalValue manualSelection
            let eff = Knapsack.efficiency manualSelection
            printfn "  Manual selection (Item2, Item3):"
            printfn "    Feasible: %b (weight %.0f <= %.0f)" isFeasible tw testProblem.Capacity
            printfn "    Value: %.0f | Efficiency: %.1f value/weight" tv eff

            let optSol = Knapsack.solveClassicalDP testProblem
            printfn "  Optimal (DP): %A -> Value: %.0f"
                (optSol.SelectedItems |> List.map (fun i -> i.Id)) optSol.TotalValue
            if tv = optSol.TotalValue then
                printfn "    Manual selection is optimal!"
            else
                printfn "    Manual is %.1f%% of optimal" (tv / optSol.TotalValue * 100.0)
            printfn ""

        printHeader "Example 6: Random Problem Instance"
        let randomProblem = Knapsack.randomInstance 8 100.0 500.0 0.5
        if not quiet then
            printfn "Generated: %d items, capacity %.0f"
                randomProblem.ItemCount randomProblem.Capacity
        match Knapsack.solve randomProblem None with
        | Ok solution ->
            printSolution "  Quantum QAOA solution:" solution randomProblem.Capacity
            allResults.Add (Map.ofList
                [ "example",           "random"
                  "method",            "QAOA"
                  "items_count",       sprintf "%d" randomProblem.ItemCount
                  "capacity",          sprintf "%.0f" randomProblem.Capacity
                  "selected_count",    sprintf "%d" solution.SelectedItems.Length
                  "selected_items",    solution.SelectedItems |> List.map (fun i -> i.Id) |> String.concat "; "
                  "total_weight",      sprintf "%.2f" solution.TotalWeight
                  "total_value",       sprintf "%.2f" solution.TotalValue
                  "utilization_pct",   sprintf "%.1f" solution.CapacityUtilization
                  "efficiency",        sprintf "%.2f" solution.Efficiency
                  "feasible",          sprintf "%b" solution.IsFeasible
                  "backend",           solution.BackendName ])
        | Error err ->
            if not quiet then printfn "  Failed: %A" err

    | "projects" ->
        printHeader "Software Project Selection (Budget Allocation)"
        let capacity = Cli.getFloatOr "capacity" 300000.0 args
        solveAndReport "projects" builtInProjects capacity Knapsack.budgetAllocation
        |> Option.iter allResults.Add

    | "cargo" ->
        printHeader "Cargo Loading (Maximize Value on Truck)"
        let capacity = Cli.getFloatOr "capacity" 600.0 args
        solveAndReport "cargo" builtInCargo capacity Knapsack.cargoLoading
        |> Option.iter allResults.Add

    | "sprint" ->
        printHeader "Sprint Task Selection (Time-Constrained)"
        let capacity = Cli.getFloatOr "capacity" 40.0 args
        solveAndReport "sprint" builtInTasks capacity Knapsack.taskScheduling
        |> Option.iter allResults.Add

    | "classic" ->
        printHeader "Classic Knapsack (Textbook Example)"
        let capacity = Cli.getFloatOr "capacity" 50.0 args
        solveAndReport "classic" builtInClassic capacity Knapsack.createProblem
        |> Option.iter allResults.Add

    | "validation" ->
        printHeader "Solution Validation and Metrics"
        let capacity = Cli.getFloatOr "capacity" 10.0 args
        solveAndReport "validation" builtInValidation capacity Knapsack.createProblem
        |> Option.iter allResults.Add

    | "random" ->
        printHeader "Random Problem Instance"
        let capacity = Cli.getFloatOr "capacity" 250.0 args
        let randomProblem = Knapsack.randomInstance 8 100.0 500.0 0.5
        if not quiet then
            printfn "Generated: %d items, capacity %.0f"
                randomProblem.ItemCount randomProblem.Capacity
        match Knapsack.solve randomProblem None with
        | Ok solution ->
            printSolution "  Quantum QAOA solution:" solution randomProblem.Capacity
            allResults.Add (Map.ofList
                [ "example",           "random"
                  "method",            "QAOA"
                  "items_count",       sprintf "%d" randomProblem.ItemCount
                  "capacity",          sprintf "%.0f" randomProblem.Capacity
                  "selected_count",    sprintf "%d" solution.SelectedItems.Length
                  "selected_items",    solution.SelectedItems |> List.map (fun i -> i.Id) |> String.concat "; "
                  "total_weight",      sprintf "%.2f" solution.TotalWeight
                  "total_value",       sprintf "%.2f" solution.TotalValue
                  "utilization_pct",   sprintf "%.1f" solution.CapacityUtilization
                  "efficiency",        sprintf "%.2f" solution.Efficiency
                  "feasible",          sprintf "%b" solution.IsFeasible
                  "backend",           solution.BackendName ])
        | Error err ->
            if not quiet then printfn "  Failed: %A" err

    | other ->
        eprintfn "Unknown example: '%s'. Use: projects|cargo|sprint|classic|validation|random|all" other
        exit 1

if not quiet then
    printfn "======================================"
    printfn "Knapsack Examples Complete!"
    printfn "======================================"

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultRows = allResults |> Seq.toList

match outputPath with
| Some path ->
    Reporting.writeJson path resultRows
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "example"; "method"; "items_count"; "capacity"; "selected_count";
          "selected_items"; "total_weight"; "total_value"; "utilization_pct";
          "efficiency"; "feasible"; "backend" ]
    let rows =
        resultRows
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Run with --help to see all options:"
    printfn "   dotnet fsi Knapsack.fsx -- --help"
    printfn "   dotnet fsi Knapsack.fsx -- --example all"
    printfn "   dotnet fsi Knapsack.fsx -- --input items.csv --capacity 500"
    printfn "   dotnet fsi Knapsack.fsx -- --quiet --output results.json  (pipeline mode)"
    printfn ""
