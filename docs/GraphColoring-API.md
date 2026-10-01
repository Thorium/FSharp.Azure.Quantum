# Graph Coloring API Reference

## Overview

The Graph Coloring domain builder is an F# computation expression API for graph coloring problems: assign a color to every node so that no two conflicting nodes share one. It offers progressive disclosure - inline nodes for simple cases, a `coloredNode { }` builder when a node needs more detail.

`GraphColoring.solveAsync` is quantum-first: it encodes the problem as a QUBO (one binary variable per node-color pair, with one-hot and conflict penalties) and samples it with a single-layer QAOA circuit on the backend you pass, or on `LocalBackend` when you pass `None`. See [Problem Size and Performance](#problem-size-and-performance) for what that means for problem size.

**Key Use Cases:**
- **Compiler Register Allocation** - Assign variables to CPU registers
- **Wireless Frequency Assignment** - Avoid interference between cell towers
- **Exam Scheduling** - Prevent student schedule conflicts
- **Meeting Room Assignment** - Room/time slot allocation

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [Progressive Disclosure API](#progressive-disclosure-api)
3. [F# API Reference](#f-api-reference)
4. [C# Usage](#c-usage)
5. [Real-World Examples](#real-world-examples)
6. [Composing Problems](#composing-problems)
7. [Problem Size and Performance](#problem-size-and-performance)
8. [Best Practices](#best-practices)

---

## Quick Start

### Installation

```bash
dotnet add package FSharp.Azure.Quantum
```

### Hello World - Simple Graph Coloring

```fsharp
open System.Threading
open FSharp.Azure.Quantum.GraphColoring

// Problem: Color 3 nodes (triangle) with minimal colors
let problem = graphColoring {
    node "A" ["B"; "C"]  // A conflicts with B and C
    node "B" ["A"; "C"]  // B conflicts with A and C
    node "C" ["A"; "B"]  // C conflicts with A and B
    colors ["Red"; "Green"; "Blue"]
}

// 3 colors, default backend (LocalBackend): 3 nodes x 3 colors = 9 qubits
task {
    match! solveAsync problem 3 None CancellationToken.None with
    | Ok solution ->
        printfn "Used %d colors" solution.ColorsUsed
        printfn "Valid: %b" solution.IsValid

        for (nodeId, color) in Map.toList solution.Assignments do
            printfn "%s → %s" nodeId color
        // One possible output:
        // A → Red
        // B → Green
        // C → Blue
    | Error err ->
        eprintfn "Coloring failed: %s" err.Message
}
```

The solver returns the best sample it measured (valid colorings first, then fewest colors). The result is sampled, so always check `IsValid`.

---

## Progressive Disclosure API

The API supports **three levels** of complexity, allowing you to start simple and add features as needed.

### Level 1: Inline Nodes

**For simple problems** - just node IDs and conflicts:

```fsharp
let problem = graphColoring {
    node "R1" ["R2"; "R3"]
    node "R2" ["R1"; "R4"]
    node "R3" ["R1"; "R4"]
    node "R4" ["R2"; "R3"]
    colors ["EAX"; "EBX"; "ECX"; "EDX"]
}

// Try 3 of the 4 colors: 4 nodes x 3 colors = 12 qubits
task {
    match! solveAsync problem 3 None CancellationToken.None with
    | Ok solution -> printfn "Solution found with %d colors" solution.ColorsUsed
    | Error err -> eprintfn "Error: %s" err.Message
}
```

**Characteristics:**
- ✅ Minimal syntax
- ✅ Reads like a specification

### Level 2: Data-Driven

**For problems loaded from data** - build the node list with ordinary F# and pass it in:

```fsharp
// Create nodes from data
let towers = [
    ("Tower1", ["Tower2"; "Tower3"])
    ("Tower2", ["Tower1"; "Tower4"])
    ("Tower3", ["Tower1"; "Tower4"; "Tower5"])
    ("Tower4", ["Tower2"; "Tower3"; "Tower6"])
    ("Tower5", ["Tower3"; "Tower6"])
    ("Tower6", ["Tower4"; "Tower5"])
]

let nodesList =
    towers
    |> List.map (fun (id, conflicts) -> node id conflicts)

let problem = graphColoring {
    nodes nodesList
    colors ["2.4GHz"; "5GHz"; "6GHz"]
    objective MinimizeColors
}
```

**Characteristics:**
- ✅ Load from database/file
- ✅ Generate programmatically

### Level 3: Advanced Node Builder

**For nodes that need more than an ID and conflicts** - use `coloredNode { }`:

```fsharp
// Variable pinned to a register, with metadata
let criticalVar = coloredNode {
    nodeId "R1"
    conflictsWith ["R2"; "R3"]
    fixedColor "EAX"        // Pre-assign to a specific register
    priority 100.0          // Tie-break: colored first (see below)
    avoidColors ["EDX"]     // Soft: prefer any other register
    property "spill_cost" 1000.0
    property "live_range_start" 0
    property "live_range_end" 500
}

let normalVar = coloredNode {
    nodeId "R2"
    conflictsWith ["R1"]
    priority 1.0
}

let problem = graphColoring {
    nodes [criticalVar; normalVar]
    node "R3" ["R1"]
    colors ["EAX"; "EBX"; "ECX"; "EDX"]
    maxColors 3             // Only EAX, EBX, ECX may be assigned
    objective MinimizeColors
}
```

**How `solveAsync` uses each option** (P is the solver's penalty weight, 10.0; n is the number of nodes):

| Option | Effect |
|--------|--------|
| `fixedColor` | The node always gets that color. |
| `conflictPenalty` | Multiplies the conflict penalty: an edge whose ends share a color costs `conflictPenalty` × P. Must be positive. See the guarantee below. |
| `maxColors` | Only the first `maxColors` entries of `colors` are encoded, so no other color can be assigned. A `fixedColor` outside them is a validation error. |
| `avoidColors` | Soft penalty of 0.3 × P / n when the node gets an avoided color (at most 0.3 × P over the whole graph), so another color is preferred when one is free. Must be in `colors`. |
| `objective MinimizeColors` | Cost that grows with the color index (0.2 × P × index / (n × (colors − 1)) per node, at most 0.2 × P over the whole graph); among valid samples the fewest colors wins. The QUBO minimum is the valid coloring with the smallest sum of color indices, which can use more colors than necessary (a square with one node fixed to the third of three colors: index sum 3 takes three colors, the two-color coloring has index sum 4); the color count enters only when the samples are ranked. |
| `objective BalanceColors` | Penalty 0.2 × P / n² × Σ (nodes per color)², at most 0.2 × P, which is smallest for even class sizes; among valid samples the most even one wins. |
| `objective MinimizeConflicts` | No color-count preference; samples are ranked by conflict count. |
| `priority` | Tie-break only: among samples that rank equal on the objective and QUBO energy, the one giving higher-priority nodes earlier colors wins; the greedy coloring (graphs without conflicts, `approximateChromaticNumber`) visits nodes in descending priority. |
| `property` | Metadata for your own use; not read by the solver. |

**Guarantee:** the soft terms are never negative and total at most 0.5 × P on any valid coloring, while breaking a constraint costs at least P (one color per node), 10 × P (a fixed color) or `conflictPenalty` × P (a conflict). So with `conflictPenalty` ≥ 1 (the default), and generally above 0.5, the lowest-energy state of the QUBO is a valid coloring whenever one exists with the encoded colors. At 0.5 or below, a coloring with conflicts can have lower energy than every valid one; use that with `MinimizeConflicts` when conflicts are acceptable. A conflict listed from both ends (`A` lists `B` and `B` lists `A`) is one edge. The number of colors the solver encodes is the `numColors` argument of `solveAsync`, capped by the number of colors and `maxColors`.

---

## F# API Reference

Everything below lives in the `FSharp.Azure.Quantum.GraphColoring` module (`open FSharp.Azure.Quantum.GraphColoring`).

### Core Types

#### `ColoredNode`

These listings mirror the library's type definitions for reference.

```text
type ColoredNode = {
    Id: string                      // Unique identifier
    ConflictsWith: string list      // Nodes that cannot have same color
    FixedColor: string option       // Pre-assigned color (optional)
    Priority: float                 // Tie-break, higher = first (default 0.0)
    AvoidColors: string list        // Soft: colors to avoid if possible
    Properties: Map<string, obj>    // Custom metadata
}
```

#### `GraphColoringProblem`

```text
type GraphColoringProblem = {
    Nodes: ColoredNode list         // All nodes in graph
    AvailableColors: string list    // Colors to assign
    Objective: ColoringObjective    // Soft goal (default MinimizeColors)
    MaxColors: int option           // Only the first MaxColors colors are used
    ConflictPenalty: float          // Conflict penalty multiplier (default 1.0)
}
```

#### `ColoringObjective`

```text
type ColoringObjective =
    | MinimizeColors              // Prefer fewer colors (default; see the options table)
    | MinimizeConflicts           // Allow invalid, minimize conflicts
    | BalanceColors               // Balanced color distribution
```

#### `ColoringSolution`

```text
type ColoringSolution = {
    Assignments: Map<string, string>    // Node → Color mapping
    ColorsUsed: int                     // Distinct colors used
    ConflictCount: int                  // Number of conflicts (0 = valid)
    IsValid: bool                       // No conflicts
    ColorDistribution: Map<string, int> // Color usage counts
    Cost: float                         // QUBO energy of the returned coloring
    BackendName: string                 // Backend name, or QuantumGraphColoringSolver.NoCircuitBackendName
    IsQuantum: bool                     // false when no circuit ran (no conflicts)
}
```

### Computation Expression Builders

#### `graphColoring { }` - Main Problem Builder

**Operations:**

| Operation | Description | Example |
|-----------|-------------|---------|
| `node "A" ["B"; "C"]` | Inline node with conflicts | `node "R1" ["R2"; "R3"]` |
| `nodes [n1; n2; n3]` | Add pre-built nodes | `nodes [criticalVar; normalVar]` |
| `colors ["A"; "B"]` | Set available colors (required) | `colors ["Red"; "Green"; "Blue"]` |
| `objective MinimizeColors` | Soft goal (see the table above) | `objective BalanceColors` |
| `maxColors 3` | Use only the first 3 colors | `maxColors 3` |
| `conflictPenalty 2.0` | Conflict penalty multiplier (positive) | `conflictPenalty 2.0` |

The builder validates the problem when the expression is evaluated and **throws** (`failwith`) if it is invalid: no nodes, no colors, empty or duplicate node IDs, a conflict naming an unknown node, a fixed or avoided color not in `colors`, `maxColors` outside 1..number of colors, a fixed color beyond the first `maxColors` colors, or a `conflictPenalty` that is not a positive finite number.

#### `coloredNode { }` - Advanced Node Builder

**Operations:**

| Operation | Description | Example |
|-----------|-------------|---------|
| `nodeId "R1"` | Set node ID (required) | `nodeId "Variable1"` |
| `conflictsWith ["R2"]` | Set conflicts | `conflictsWith ["R2"; "R3"]` |
| `fixedColor "Red"` | Pre-assign color | `fixedColor "EAX"` |
| `priority 10.0` | Tie-break, higher = colored first (default 0.0) | `priority 100.0` |
| `avoidColors ["Blue"]` | Soft: prefer other colors | `avoidColors ["EDX"]` |
| `property "key" value` | Add metadata | `property "spill_cost" 500.0` |

### Functions

```text
val node : id:string -> conflicts:string list -> ColoredNode
val singleNode : coloredNode:ColoredNode -> GraphColoringProblem
val solveAsync : problem:GraphColoringProblem -> numColors:int -> backend:IQuantumBackend option -> cancellationToken:CancellationToken -> Task<QuantumResult<ColoringSolution>>
val validate : problem:GraphColoringProblem -> QuantumResult<unit>
val isValidSolution : problem:GraphColoringProblem -> solution:ColoringSolution -> bool
val approximateChromaticNumber : problem:GraphColoringProblem -> int
val describeSolution : solution:ColoringSolution -> string
val registerAllocation : variables:string list -> conflicts:(string * string) list -> registers:string list -> GraphColoringProblem
val frequencyAssignment : towers:string list -> interferences:(string * string) list -> frequencies:string list -> GraphColoringProblem
val examScheduling : exams:string list -> studentConflicts:(string * string) list -> timeSlots:string list -> GraphColoringProblem
```

- `solveAsync problem numColors backend cancellationToken` uses `numColors` colors, capped by the number of colors and `maxColors`. `None` for the backend means `LocalBackend`. A graph with no conflicts at all runs no circuit: each node gets its fixed color; otherwise it prefers a color it does not avoid, and among those, under `MinimizeColors`, a color already in use (a new color only when none is), under `BalanceColors` the smallest class so far. The solution has `IsQuantum = false` and `BackendName = QuantumGraphColoringSolver.NoCircuitBackendName`. Errors (validation, backend failures) come back as `Error`.
- `approximateChromaticNumber` runs a classical greedy coloring (fixed colors first, then nodes in descending priority, each reusing a free color already in use before opening a new one; `avoidColors`, `objective` and `maxColors` are ignored here) and returns the number of colors it used (an upper bound, not the exact chromatic number); it falls back to the number of available colors if greedy fails.
- `registerAllocation`, `frequencyAssignment` and `examScheduling` build a problem from a list of IDs and a list of conflicting pairs, without going through the builder's validation. `registerAllocation` also sets `MaxColors` to the number of registers.
- `describeSolution` formats a solution as readable text.

```fsharp
let exams =
    examScheduling
        ["Math"; "Physics"; "Chemistry"]
        [("Math", "Physics"); ("Math", "Chemistry")]
        ["Mon"; "Tue"; "Wed"]

task {
    match! solveAsync exams 3 None CancellationToken.None with
    | Ok solution -> printfn "%s" (describeSolution solution)
    | Error err -> eprintfn "Error: %s" err.Message
}
```

---

## C# Usage

There is no separate C# builder for graph coloring; C# calls the same functions. F# lists are built with `ListModule.OfSeq`, and the optional backend is an `FSharpOption`:

```csharp
using System;
using System.Threading;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using FSharp.Azure.Quantum;
using FSharp.Azure.Quantum.Core;

var problem = GraphColoring.examScheduling(
    ListModule.OfSeq(new[] { "Math", "Physics", "Chemistry" }),
    ListModule.OfSeq(new[] { Tuple.Create("Math", "Physics"), Tuple.Create("Math", "Chemistry") }),
    ListModule.OfSeq(new[] { "Mon", "Tue", "Wed" }));

var result = await GraphColoring.solveAsync(problem, 3, FSharpOption<BackendAbstraction.IQuantumBackend>.None, CancellationToken.None);

if (result.IsOk)
{
    foreach (var kvp in result.ResultValue.Assignments)
        Console.WriteLine($"{kvp.Key} → {kvp.Value}");
}
else
{
    Console.WriteLine($"Failed: {result.ErrorValue.Message}");
}
```

The generic `GraphOptimization` module (`GraphOptimizationBuilder`) describes graph problems and encodes them as QUBO matrices (`toQubo`, `decodeSolution`), but it does not solve them; use `GraphColoring.solveAsync` to get a coloring.

---

## Real-World Examples

The qubit count of each example is nodes × colors used; all of these stay well within the local simulator.

### Example 1: Compiler Register Allocation

**Problem:** Assign 5 live variables to 3 CPU registers (5 × 3 = 15 qubits).

```fsharp
open System.Threading
open FSharp.Azure.Quantum.GraphColoring

// Variable interference graph (from liveness analysis)
let problem = graphColoring {
    node "v1" ["v2"; "v3"]            // v1 live simultaneously with v2, v3
    node "v2" ["v1"; "v3"; "v4"]
    node "v3" ["v1"; "v2"; "v5"]
    node "v4" ["v2"; "v5"]
    node "v5" ["v3"; "v4"]

    // x86-64 general-purpose registers
    colors ["RAX"; "RBX"; "RCX"]
    objective MinimizeColors
}

task {
    match! solveAsync problem 3 None CancellationToken.None with
    | Ok solution ->
        if solution.IsValid then
            printfn "Register allocation successful!"
            printfn "Registers used: %d" solution.ColorsUsed

            for (var, reg) in Map.toList solution.Assignments do
                printfn "  %s → %s" var reg

            // Output assembly with register assignments
            printfn "\nGenerated Assembly:"
            printfn "  MOV %s, 42     ; v1 = 42" (solution.Assignments.["v1"])
            printfn "  ADD %s, %s     ; v2 = v1 + ..."
                (solution.Assignments.["v2"])
                (solution.Assignments.["v1"])
        else
            printfn "No conflict-free assignment in the samples - spill or add registers"
    | Error err ->
        eprintfn "Register allocation failed: %s" err.Message
}
```

---

### Example 2: Wireless Frequency Assignment

**Problem:** Assign frequencies to cell towers to avoid interference (6 towers × 3 frequencies = 18 qubits).

```fsharp
// Tower interference data, e.g. loaded from a database
type Tower = { Id: string; InterferesWithin: string list }

let towers = [
    { Id = "Tower1"; InterferesWithin = ["Tower2"; "Tower3"] }
    { Id = "Tower2"; InterferesWithin = ["Tower1"; "Tower4"] }
    { Id = "Tower3"; InterferesWithin = ["Tower1"; "Tower4"; "Tower5"] }
    { Id = "Tower4"; InterferesWithin = ["Tower2"; "Tower3"; "Tower6"] }
    { Id = "Tower5"; InterferesWithin = ["Tower3"; "Tower6"] }
    { Id = "Tower6"; InterferesWithin = ["Tower4"; "Tower5"] }
]

let nodesList =
    towers
    |> List.map (fun t -> node t.Id t.InterferesWithin)

let problem = graphColoring {
    nodes nodesList
    colors ["2.4GHz"; "5GHz"; "6GHz"]
    objective MinimizeColors
}

task {
    match! solveAsync problem 3 None CancellationToken.None with
    | Ok solution ->
        printfn "Frequency Plan:"
        for tower in towers do
            let freq = solution.Assignments.[tower.Id]
            printfn "  %s: %s" tower.Id freq

        printfn "\nFrequencies needed: %d" solution.ColorsUsed
    | Error err ->
        eprintfn "Frequency allocation failed: %s" err.Message
}
```

---

### Example 3: Exam Scheduling

**Problem:** Schedule exams to avoid student conflicts (5 exams × 3 slots = 15 qubits).

```fsharp
type Exam = {
    Course: string
    StudentOverlapWith: string list  // Courses with shared students
}

let exams = [
    { Course = "Math 101"; StudentOverlapWith = ["Physics 101"; "Chemistry 101"] }
    { Course = "Physics 101"; StudentOverlapWith = ["Math 101"; "CompSci 101"] }
    { Course = "Chemistry 101"; StudentOverlapWith = ["Math 101"; "Biology 101"] }
    { Course = "CompSci 101"; StudentOverlapWith = ["Physics 101"] }
    { Course = "Biology 101"; StudentOverlapWith = ["Chemistry 101"] }
]

let nodesList =
    exams
    |> List.map (fun e -> node e.Course e.StudentOverlapWith)

let problem = graphColoring {
    nodes nodesList
    colors ["Monday 9am"; "Monday 2pm"; "Tuesday 9am"]
    objective MinimizeColors
}

task {
    match! solveAsync problem 3 None CancellationToken.None with
    | Ok solution ->
        printfn "Exam Schedule:"
        for exam in exams do
            let timeSlot = solution.Assignments.[exam.Course]
            printfn "  %s: %s" exam.Course timeSlot

        printfn "\nTime slots needed: %d" solution.ColorsUsed
    | Error err -> eprintfn "Error: %s" err.Message
}
```

---

## Composing Problems

### Control Flow Outside the Builder

```fsharp
let highPriority = true
let criticalNodes = if highPriority then [node "Critical" []] else []

let problem = graphColoring {
    nodes criticalNodes
    node "A" ["B"]
    node "B" ["A"]
    colors ["Red"; "Green"]
}
```

### Generating Nodes

```fsharp
let neighbors i = [sprintf "Node%d" ((i % 100) + 1)]
let availableColors = ["Red"; "Green"; "Blue"]

let nodesList =
    [1..100]
    |> List.map (fun i -> node $"Node{i}" (neighbors i))

let problem = graphColoring {
    nodes nodesList
    colors availableColors
}
```

A 100-node problem builds fine, but it is far too large for `solveAsync` on a simulator (100 × 3 = 300 qubits); see [Problem Size and Performance](#problem-size-and-performance).

### Mixing Inline and Builder Nodes

Inline `node` operations and `nodes` lists can be combined; nodes are appended in order.

```fsharp
let baseNodes = [
    node "A" ["B"]
    node "B" ["A"]
]

let problem1 = graphColoring {
    nodes baseNodes
    colors ["Red"; "Green"]
}

let problem2 = graphColoring {
    nodes baseNodes
    node "C" ["A"]  // Add more nodes
    colors ["Red"; "Green"; "Blue"]
}

let pinned = graphColoring {
    nodes [coloredNode { nodeId "A"; conflictsWith ["B"]; fixedColor "Red" }]
    node "B" ["A"]
    colors ["Red"; "Green"]
}
```

### Loops Inside the Builder

Custom operations such as `node` cannot appear inside a `for` loop in the builder. Either generate the nodes outside and pass them with `nodes`, or `yield!` a one-node problem made with `singleNode`. Inside the builder the name `node` means the custom operation, not the helper function, so build the node with `coloredNode { }`:

```fsharp
let problem = graphColoring {
    colors ["Red"; "Green"; "Blue"]

    for i in [1..10] do
        yield! singleNode (coloredNode {
            nodeId $"N{i}"
            conflictsWith (if i < 10 then [$"N{i+1}"] else [])
        })
}
```

---

## Problem Size and Performance

**Algorithm:** `solveAsync` builds a QUBO with one variable per (node, color) pair and runs a single QAOA layer with fixed angles (γ = β = 0.5) and 1000 shots. It decodes every shot and returns the best one for the objective: under `MinimizeColors` valid colorings first (fewest colors), under `BalanceColors` valid colorings first (most even classes), under `MinimizeConflicts` the fewest conflicts; ties go to the lowest QUBO energy, then to `priority`. There is no angle optimisation, so small, sparse graphs give the best results.

**Qubits:** nodes × the encoded color count (`numColors` capped by the number of colors and `maxColors`). On `LocalBackend` this must fit `StateVector.maxQubits`, which is derived from available memory and capped at 30; the state vector needs 16 bytes × 2^qubits. In practice keep problems around 20 qubits or fewer (for example 6 nodes × 3 colors, or 5 nodes × 4 colors). Larger problems need a cloud backend or a classical method.

**Graphs without conflicts:** no circuit runs; see `solveAsync` above.

**Classical alternative:** for graphs too large to simulate, `HybridSolver.solveGraphColoringAsync` with a forced `Classical` method runs a greedy coloring (fixed colors placed first, then vertices in order, each taking a color no neighbor uses, preferring one already in use, then the lowest index). A graph without edges runs no circuit even when `Quantum` is forced, so the solution reports `Method = Classical` and says why in `Reasoning`. It takes the lower-level `QuantumGraphColoringSolver.GraphColoringProblem` (color indices instead of names):

```fsharp
open System.Threading
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Quantum

let bigProblem : QuantumGraphColoringSolver.GraphColoringProblem = {
    Vertices = [ for i in 1..100 -> $"Node{i}" ]
    Edges = [ for i in 1..100 -> GraphOptimization.edge $"Node{i}" $"Node{(i % 100) + 1}" 1.0 ]
    NumColors = 3
    FixedColors = Map.empty
}

task {
    match! HybridSolver.solveGraphColoringAsync bigProblem 3 None None (Some HybridSolver.Classical) CancellationToken.None with
    | Ok solution -> printfn "Greedy used %d colors" solution.Result.ColorsUsed
    | Error err -> eprintfn "Error: %s" err.Message
}
```

---

## Best Practices

1. **Use inline syntax for simple problems** - `node "A" ["B"; "C"]`.
2. **Load from data for dynamic problems** - build the list outside the builder and pass it with `nodes`.
3. **Use `coloredNode { }` for per-node options** - `fixedColor` pins a node, `avoidColors` and `priority` steer it softly.
4. **Expect build-time exceptions** - the `graphColoring { }` builder throws on an invalid problem. Build the `GraphColoringProblem` record yourself and call `validate` if you need a `Result` instead.
5. **Always specify colors** - a problem without `colors` fails validation.
6. **Keep the qubit count small** - nodes × colors; check `IsValid` on every result.

---

## Related Documentation

- [Task Scheduling](./TaskScheduling-API.md) - Similar computation expression pattern

---

## License

Unlicense - Public Domain

## Contributing

See main repository README for contribution guidelines.
