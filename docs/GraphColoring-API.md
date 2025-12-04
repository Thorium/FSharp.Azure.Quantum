# Graph Coloring API Reference

## Overview

The Graph Coloring Domain Builder provides an idiomatic F# computation expression API for solving graph coloring problems. Built on top of the Generic Graph Optimization Framework (TKT-90), it offers progressive disclosure - starting simple for common cases and scaling to advanced features when needed.

**Key Use Cases:**
- **Compiler Register Allocation** - Assign variables to CPU registers
- **Wireless Frequency Assignment** - Avoid interference between cell towers
- **Exam Scheduling** - Prevent student schedule conflicts
- **Meeting Room Assignment** - Optimize room/time slot allocation

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [Progressive Disclosure API](#progressive-disclosure-api)
3. [F# API Reference](#f-api-reference)
4. [C# FluentAPI Equivalent](#c-fluentapi-equivalent)
5. [Real-World Examples](#real-world-examples)
6. [F# Computation Expressions vs C# Fluent APIs](#f-computation-expressions-vs-c-fluent-apis)
7. [Performance Characteristics](#performance-characteristics)

---

## Quick Start

### Installation

```bash
dotnet add package FSharp.Azure.Quantum
```

### Hello World - Simple Graph Coloring

```fsharp
open FSharp.Azure.Quantum.GraphColoring

// Problem: Color 3 nodes (triangle) with minimal colors
let problem = graphColoring {
    node "A" ["B"; "C"]  // A conflicts with B and C
    node "B" ["A"; "C"]  // B conflicts with A and C
    node "C" ["A"; "B"]  // C conflicts with A and B
    colors ["Red"; "Green"; "Blue"]
}

match solve problem 3 None with
| Ok solution ->
    printfn "Used %d colors" solution.ColorsUsed  // Output: Used 3 colors
    printfn "Valid: %b" solution.IsValid          // Output: Valid: true
    
    for (nodeId, color) in Map.toList solution.Assignments do
        printfn "%s → %s" nodeId color
    // Output:
    // A → Red
    // B → Green
    // C → Blue
| Error msg ->
    eprintfn "Coloring failed: %s" msg
```

---

## Progressive Disclosure API

The API supports **three levels** of complexity, allowing you to start simple and add features as needed.

### Level 1: Inline Nodes (80% Use Case)

**For simple problems** - just node IDs and conflicts:

```fsharp
let problem = graphColoring {
    node "R1" ["R2"; "R3"]
    node "R2" ["R1"; "R4"]
    node "R3" ["R1"; "R4"]
    node "R4" ["R2"; "R3"]
    colors ["EAX"; "EBX"; "ECX"; "EDX"]
}

match solve problem 3 None with
| Ok solution -> printfn "Solution found with %d colors" solution.ColorsUsed
| Error msg -> eprintfn "Error: %s" msg
```

**Characteristics:**
- ✅ Minimal syntax
- ✅ Perfect for quick prototyping
- ✅ Reads like a specification
- ✅ No ceremony

### Level 2: Data-Driven with Logic (15% Use Case)

**For problems with patterns** - use loops and conditionals:

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
- ✅ Apply business logic
- ✅ Conditional node creation

### Level 3: Advanced Builder (5% Use Case)

**For complex requirements** - full control with `coloredNode { }`:

```fsharp
// High-priority variable with metadata
let criticalVar = coloredNode {
    id "R1"
    conflictsWith ["R2"; "R3"]
    fixedColor "EAX"        // Pre-assign to specific register
    priority 100.0          // High priority for allocation
    avoidColors ["EDX"]     // Soft constraint
    property "spill_cost" 1000.0
    property "live_range_start" 0
    property "live_range_end" 500
}

let normalVar = coloredNode {
    id "R2"
    conflictsWith ["R1"]
    priority 1.0
}

let problem = graphColoring {
    nodes [criticalVar; normalVar]
    colors ["EAX"; "EBX"; "ECX"; "EDX"]
    maxColors 3             // Hard constraint
    objective MinimizeColors
}
```

**Characteristics:**
- ✅ Fixed color assignments
- ✅ Priority-based allocation
- ✅ Soft constraints (avoid colors)
- ✅ Custom metadata
- ✅ MaxColors hard constraint

---

## F# API Reference

### Core Types

#### `ColoredNode`

```fsharp
type ColoredNode = {
    Id: string                      // Unique identifier
    ConflictsWith: string list      // Nodes that cannot have same color
    FixedColor: string option       // Pre-assigned color (optional)
    Priority: float                 // Tie-breaker (higher = assign first)
    AvoidColors: string list        // Soft constraint
    Properties: Map<string, obj>    // Custom metadata
}
```

#### `GraphColoringProblem`

```fsharp
type GraphColoringProblem = {
    Nodes: ColoredNode list         // All nodes in graph
    AvailableColors: string list    // Colors to assign
    Objective: ColoringObjective    // Optimization goal
    MaxColors: int option           // Maximum colors to use
    ConflictPenalty: float          // Penalty for conflicts (default 1.0)
}
```

#### `ColoringObjective`

```fsharp
type ColoringObjective =
    | MinimizeColors              // Minimize total colors used (default)
    | MinimizeConflicts           // Allow invalid, minimize conflicts
    | BalanceColors               // Balanced color distribution
```

#### `ColoringSolution`

```fsharp
type ColoringSolution = {
    Assignments: Map<string, string>   // Node → Color mapping
    ColorsUsed: int                    // Distinct colors used
    ConflictCount: int                 // Number of conflicts (0 = valid)
    IsValid: bool                      // No conflicts
    ColorDistribution: Map<string, int> // Color usage counts
    Cost: float                        // Objective value
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
| `objective MinimizeColors` | Set optimization goal | `objective MinimizeColors` |
| `maxColors 3` | Maximum colors constraint | `maxColors 3` |
| `conflictPenalty 100.0` | Penalty weight | `conflictPenalty 100.0` |

#### `coloredNode { }` - Advanced Node Builder

**Operations:**

| Operation | Description | Example |
|-----------|-------------|---------|
| `id "R1"` | Set node ID (required) | `id "Variable1"` |
| `conflictsWith ["R2"]` | Set conflicts (required) | `conflictsWith ["R2"; "R3"]` |
| `fixedColor "Red"` | Pre-assign color | `fixedColor "EAX"` |
| `priority 10.0` | Set priority (default 0.0) | `priority 100.0` |
| `avoidColors ["Blue"]` | Soft constraint | `avoidColors ["EDX"]` |
| `property "key" value` | Add metadata | `property "spill_cost" 500.0` |

### Helper Functions

```text
val node : id:string -> conflicts:string list -> ColoredNode
val solve : problem:GraphColoringProblem -> ColoringSolution
val validate : problem:GraphColoringProblem -> Result<unit, string>
val exportToDot : problem:GraphColoringProblem -> solution:ColoringSolution -> string
val describeSolution : solution:ColoringSolution -> string
```

---

## C# Usage

C# developers should use the **GraphOptimization module (TKT-90)** for graph coloring problems. This provides an idiomatic FluentAPI builder pattern designed for C#:

```csharp
using FSharp.Azure.Quantum;
using static FSharp.Azure.Quantum.GraphOptimization;

// Graph coloring in C# using GraphOptimization
var problem = new GraphOptimizationBuilder<int, Unit>()
    .Nodes(new[] {
        node("A", 0),  // Node "A" with value 0
        node("B", 0),  // Node "B" with value 0
        node("C", 0)   // Node "C" with value 0
    })
    .Edges(new[] {
        edge("A", "B", 1.0),  // A conflicts with B
        edge("A", "C", 1.0)   // A conflicts with C
    })
    .AddConstraint(GraphConstraint.NoAdjacentEqual)  // No adjacent nodes same color
    .Objective(GraphObjective.MinimizeColors)        // Minimize colors used
    .NumColors(2)                                     // 2 colors available
    .Build();

var solution = solveClassical(problem);

// Access results
Console.WriteLine($"Feasible: {solution.IsFeasible}");
if (solution.NodeAssignments.HasValue)
{
    var assignments = solution.NodeAssignments.Value;
    foreach (var kvp in assignments)
    {
        Console.WriteLine($"{kvp.Key} → Color {kvp.Value}");
    }
}
```

### Mapping Graph Coloring Concepts to GraphOptimization

| Graph Coloring Concept (F#) | GraphOptimization Concept (C#) |
|------------------------------|-------------------------------|
| `node "A" ["B"; "C"]` (conflicts) | `node("A", 0)` + `edge("A", "B")` + `edge("A", "C")` |
| `colors ["Red"; "Green"]` (string names) | `NumColors(2)` (count only, values are indices 0, 1) |
| `conflictsWith` (domain language) | `Edges` (generic graph edges) |
| `objective MinimizeColors` | `Objective(GraphObjective.MinimizeColors)` |
| `solve problem` | `solveClassical(problem)` |

**Note:** GraphOptimization uses integer indices for colors (0, 1, 2...) instead of string names ("Red", "Green", "Blue"). You can map indices to color names in your application code.

### Language-Specific APIs

| | F# | C# |
|---|----|----|
| **Module** | `GraphColoring` (TKT-80) | `GraphOptimization` (TKT-90) |
| **API Style** | Computation expression | FluentAPI builder |
| **Colors** | String names | Integer indices |
| **Conflicts** | `conflictsWith` list | `Edges` with `NoAdjacentEqual` |

Both provide excellent graph coloring capabilities tailored to each language's idioms.

---

## Real-World Examples

### Example 1: Compiler Register Allocation

**Problem:** Assign 8 live variables to 4 CPU registers.

```fsharp
open FSharp.Azure.Quantum.GraphColoring

// Variable interference graph (from liveness analysis)
let problem = graphColoring {
    node "v1" ["v2"; "v3"; "v4"]      // v1 live simultaneously with v2, v3, v4
    node "v2" ["v1"; "v5"]
    node "v3" ["v1"; "v6"]
    node "v4" ["v1"; "v7"]
    node "v5" ["v2"; "v8"]
    node "v6" ["v3"]
    node "v7" ["v4"]
    node "v8" ["v5"]
    
    // x86-64 general-purpose registers
    colors ["RAX"; "RBX"; "RCX"; "RDX"]
    objective MinimizeColors
}

match solve problem 8 None with
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
        printfn "Spilling required - not enough registers!"
| Error msg ->
    eprintfn "Register allocation failed: %s" msg
```

**ROI:**
- ✅ Minimize register spills (memory access)
- ✅ Faster code execution
- ✅ Automatic allocation (no manual tuning)

---

### Example 2: Wireless Frequency Assignment

**Problem:** Assign frequencies to cell towers to avoid interference.

```fsharp
// Load tower interference data from database
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
    colors ["2.4GHz"; "5GHz"; "6GHz"; "10GHz"]
    objective MinimizeColors
}

match solve problem 4 None with
| Ok solution ->
    printfn "Frequency Plan:"
    for tower in towers do
        let freq = solution.Assignments.[tower.Id]
        printfn "  %s: %s" tower.Id freq
    
    printfn "\nFrequencies needed: %d" solution.ColorsUsed
| Error msg ->
    eprintfn "Frequency allocation failed: %s" msg
```

**ROI:**
- ✅ Minimize frequency spectrum usage
- ✅ Avoid costly interference
- ✅ Scale to thousands of towers

---

### Example 3: Exam Scheduling

**Problem:** Schedule exams to avoid student conflicts.

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
    colors ["Monday 9am"; "Monday 2pm"; "Tuesday 9am"; "Tuesday 2pm"; "Wednesday 9am"]
    objective MinimizeColors
}

match solve problem 5 None with
| Ok solution ->
    printfn "Exam Schedule:"
    for exam in exams do
        let timeSlot = solution.Assignments.[exam.Course]
        printfn "  %s: %s" exam.Course timeSlot
    
    printfn "\nTime slots needed: %d" solution.ColorsUsed
| Error msg -> eprintfn "Error: %s" msg
```

**ROI:**
- ✅ No student schedule conflicts
- ✅ Minimize exam days
- ✅ Automatic scheduling (no manual spreadsheets)

---

## F# Computation Expressions vs C# Fluent APIs

### Language-Specific Strengths

Both F# computation expressions and C# fluent APIs are excellent patterns for building domain-specific APIs. The choice depends on your language preference and project context.

#### 1. **Control Flow Integration**

**F# Computation Expression:**
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

**C# with Generic Graph API:**
```csharp
using static FSharp.Azure.Quantum.GraphOptimization;

var builder = new GraphOptimizationBuilder<int, Unit>();
if (highPriority) {
    builder = builder.Nodes(new[] { node("Critical", 0) });
}

var problem = builder
    .Nodes(new[] { node("A", 0), node("B", 0) })
    .Edges(new[] { edge("A", "B", 1.0) })
    .AddConstraint(GraphConstraint.NoAdjacentEqual)
    .NumColors(2)
    .Build();
```

*Both approaches work well - F# integrates control flow into the computation expression, C# uses standard imperative conditionals with the generic graph framework.*

#### 2. **Iteration Support**

**F# Approach:**
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

**C# with Generic Graph API:**
```csharp
using static FSharp.Azure.Quantum.GraphOptimization;

var nodes = Enumerable.Range(1, 100)
    .Select(i => node($"Node{i}", 0))
    .ToArray();

var edges = Enumerable.Range(1, 100)
    .SelectMany(i => neighbors(i).Select(n => edge($"Node{i}", n, 1.0)))
    .ToArray();

var problem = new GraphOptimizationBuilder<int, Unit>()
    .Nodes(nodes)
    .Edges(edges)
    .AddConstraint(GraphConstraint.NoAdjacentEqual)
    .NumColors(availableColors.Length)
    .Build();
```

*Both use LINQ/pipeline operators effectively. F# uses domain language (conflicts), C# uses graph theory concepts (edges).*

#### 3. **Domain-Specific vs Generic APIs**

**F# Domain-Specific:**
```fsharp
let problem = graphColoring {
    node "Tower1" ["Tower2"; "Tower3"]  // Business language
    colors ["2.4GHz"; "5GHz"]
}
```

**C# Generic Framework:**
```csharp
var problem = new GraphOptimizationBuilder<int, Unit>()
    .Nodes(...)
    .Edges(...)  // Generic graph operations
    .AddConstraint(NoAdjacentEqual)
    .Build();
```

*F# API is optimized for graph coloring domains, C# API is flexible for any graph algorithm.*

#### 4. **Builder Finalization**

**F# Automatic:**
```fsharp
let problem = graphColoring {
    node "A" ["B"]
    colors ["Red"; "Green"]
}  // Run() called automatically by compiler
```

**C# Explicit:**
```csharp
using static FSharp.Azure.Quantum.GraphOptimization;

var problem = new GraphOptimizationBuilder<int, Unit>()
    .Nodes(new[] { node("A", 0), node("B", 0) })
    .Edges(new[] { edge("A", "B", 1.0) })
    .AddConstraint(GraphConstraint.NoAdjacentEqual)
    .NumColors(2)
    .Build();  // Explicit finalization required
```

*F# computation expression automates finalization via `Run()`, C# FluentAPI requires explicit `.Build()` call.*

#### 5. **Progressive Disclosure in F#**

```fsharp
// Mock function for example
let loadFromDatabase() = [node "DBNode1" []; node "DBNode2" []]

// Level 1: Simple inline
let _ = node "A" ["B"; "C"]

// Level 2: Data-driven
let nodesList2 = loadFromDatabase()

// Level 3: Full builder
let _ = coloredNode {
    id "A"
    conflictsWith ["B"]
    fixedColor "Red"
    priority 100.0
}
```

*F# computation expressions naturally support progressive API design.*

#### 6. **Type Inference**

**F# Inference:**
```fsharp
let problem = graphColoring {
    node "A" ["B"]  // Types inferred automatically
    colors ["Red"; "Green"]
}
```

**C# with var:**
```csharp
using static FSharp.Azure.Quantum.GraphOptimization;

var problem = new GraphOptimizationBuilder<int, Unit>()
    .Nodes(new[] { node("A", 0), node("B", 0) })
    .Edges(new[] { edge("A", "B", 1.0) })
    .AddConstraint(GraphConstraint.NoAdjacentEqual)
    .NumColors(2)
    .Build();
```

*F# has extensive type inference throughout. C# uses `var` for local variable type inference but requires explicit type parameters for generics.*

#### 7. **Composition and Reuse**

**F# Fragments:**
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
```

**C# Builder Reuse:**
```csharp
using static FSharp.Azure.Quantum.GraphOptimization;

var baseNodes = new[] { node("A", 0), node("B", 0) };
var baseEdges = new[] { edge("A", "B", 1.0) };

var baseBuilder = new GraphOptimizationBuilder<int, Unit>()
    .Nodes(baseNodes)
    .Edges(baseEdges)
    .AddConstraint(GraphConstraint.NoAdjacentEqual);

var problem1 = baseBuilder
    .NumColors(2)
    .Build();

var problem2 = baseBuilder
    .Nodes(baseNodes.Append(node("C", 0)).ToArray())
    .Edges(baseEdges.Append(edge("A", "C", 1.0)).ToArray())
    .NumColors(3)
    .Build();
```

*Both support composition - F# with list concatenation in the computation expression, C# with builder chaining and LINQ.*

### Summary

| Aspect | F# Computation Expression | C# Fluent API |
|--------|---------------------------|---------------|
| **Syntax** | Domain-specific, declarative | Method chaining, imperative |
| **Control Flow** | Integrated (`if`, `for`) | Standard language constructs |
| **Type Safety** | Inference + strong typing | Explicit types + generics |
| **Finalization** | Automatic (`Run()`) | Explicit (`.Build()`) |
| **Best For** | Domain problems, F# projects | Generic algorithms, C# projects |

**Choose based on your language and problem domain** - both are production-ready, well-designed APIs.

---

## Performance Characteristics

### Solver Performance

| Problem Size | Nodes | Edges | Time | Memory |
|--------------|-------|-------|------|--------|
| **Small** | 10 | 20 | <10ms | <1MB |
| **Medium** | 100 | 500 | <100ms | ~5MB |
| **Large** | 1000 | 5000 | <1s | ~50MB |
| **Very Large** | 10000 | 50000 | ~10s | ~500MB |

**Algorithm:** Greedy graph coloring (classical solver)

**Complexity:**
- Time: O(V + E) where V = nodes, E = edges
- Space: O(V + E)

### Validation Overhead

- **Parse-time validation**: <1ms for typical problems
- **Run() method**: Validates structure (IDs, conflicts, colors)
- **Early failure**: Catches errors before solving

---

## Best Practices

### ✅ DO

1. **Use inline syntax for simple problems** (80% case)
   ```fsharp
   node "A" ["B"; "C"]
   ```

2. **Load from data for dynamic problems**
   ```fsharp
   nodes (loadTowersFromDatabase())
   ```

3. **Use `coloredNode { }` for complex requirements**
   ```fsharp
   coloredNode { fixedColor "Red"; priority 100.0 }
   ```

4. **Validate early** - `Run()` validates at build time

5. **Use business domain language** - "conflicts", "colors", not "edges"

### Common Patterns

1. **Using loops with the builder**
   
   **Option A:** Generate nodes outside and pass them in (recommended for complex logic):
   ```fsharp
   // Generate nodes first
   let nodesList = [1..10] |> List.map (fun i -> node $"N{i}" [])
   
   // Then use in builder
   graphColoring { nodes nodesList }
   ```
   
   **Option B:** Use `for` loops with `yield!` inside the builder:
   ```fsharp
   // Use yield! with singleNode helper for loops
   let problem = graphColoring {
       colors ["Red"; "Green"; "Blue"]
       
       for i in [1..10] do
           yield! singleNode (node $"N{i}" [$"N{i+1}"])
   }
   ```
   
   *Note: Custom operations like `node` don't work directly in `for` loops (F# limitation). Use `yield! singleNode(...)` instead.*

2. **Always specify colors** - Required for all problems
   ```fsharp
   graphColoring {
       node "A" ["B"]
       colors ["Red"; "Green"]  // Required
   }
   ```

3. **Choose one node creation style per problem** - Either inline or builder, not mixed
   ```fsharp
   // Inline style
   graphColoring {
       node "A" ["B"]
       node "B" ["A"]
   }
   
   // OR builder style
   let nodes = [
       coloredNode { id "A"; conflictsWith ["B"] }
       coloredNode { id "B"; conflictsWith ["A"] }
   ]
   graphColoring { nodes nodes }
   ```

---

## Related Documentation

- [Generic Graph Optimization (TKT-90)](./GraphOptimization-API.md) - Low-level graph framework
- [Task Scheduling (TKT-81)](./TaskScheduling-API.md) - Similar computation expression pattern
- [AI Development Guide](../development/AI-DEVELOPMENT-GUIDE.md) - TDD methodology

---

## License

Unlicense - Public Domain

## Contributing

See main repository README for contribution guidelines.
