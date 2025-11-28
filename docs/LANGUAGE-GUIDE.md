# Language-Specific API Guide

## Quick Reference

| Problem Domain | F# Developers | C# Developers |
|----------------|---------------|---------------|
| **Graph Coloring** | Use `GraphColoring` module (TKT-80) | Use `GraphOptimization` module (TKT-90) |
| **Task Scheduling** | Use `TaskScheduling` module (TKT-81) | Use `Scheduling` module (TKT-91) |
| **Generic Graph Problems** | Use `GraphOptimization` module (TKT-90) | Use `GraphOptimization` module (TKT-90) |
| **Generic Scheduling** | Use `Scheduling` module (TKT-91) | Use `Scheduling` module (TKT-91) |

---

## Why Different Recommendations?

### F# Computation Expressions (F#-Only Feature)

F# computation expressions like `graphColoring { }` and `scheduledTask { }` are **F#-specific language features** that don't exist in C#. They provide:

- ✅ Domain-specific syntax (`conflictsWith` vs `edges`)
- ✅ Progressive disclosure (simple → advanced)
- ✅ Time helpers (`hours 2.0` instead of `120.0`)
- ✅ Automatic validation via `Run()`
- ✅ Control flow integration (`if`, `for`)

**These are F# language features, not available in C#.**

### C# Should Use Underlying Frameworks

C# developers should use the **underlying generic frameworks** directly:
- `GraphOptimization` (TKT-90) - Generic graph algorithms
- `Scheduling` (TKT-91) - Generic scheduling framework

These provide:
- ✅ FluentAPI builder pattern (C#-friendly)
- ✅ Generic type parameters `<T>`
- ✅ Full feature set (all constraints, objectives)
- ✅ Extensibility (custom constraints/objectives)

---

## Detailed Usage Guide

### Graph Coloring Problems

#### F# Developers: Use GraphColoring (TKT-80)

```fsharp
open FSharp.Azure.Quantum.GraphColoring

// Simple, domain-specific API
let problem = graphColoring {
    node "R1" ["R2"; "R3"]
    node "R2" ["R1"; "R4"]
    node "R3" ["R1"; "R4"]
    node "R4" ["R2"; "R3"]
    colors ["EAX"; "EBX"; "ECX"; "EDX"]
    objective MinimizeColors
}

let solution = solve problem
printfn "Used %d colors" solution.ColorsUsed
```

**Why?** Domain-specific computation expression syntax is idiomatic F#.

#### C# Developers: Use GraphOptimization (TKT-90)

```csharp
using FSharp.Azure.Quantum;
using static FSharp.Azure.Quantum.GraphOptimization;

// Generic graph framework with FluentAPI
var problem = new GraphOptimizationBuilder<int, Unit>()
    .Nodes(new[] {
        node("R1", 0),
        node("R2", 0),
        node("R3", 0),
        node("R4", 0)
    })
    .Edges(new[] {
        edge("R1", "R2", 1.0),
        edge("R1", "R3", 1.0),
        edge("R2", "R4", 1.0),
        edge("R3", "R4", 1.0)
    })
    .AddConstraint(GraphConstraint.NoAdjacentEqual)
    .Objective(GraphObjective.MinimizeColors)
    .NumColors(4)
    .Build();

var solution = solveClassical(problem);
Console.WriteLine($"Colors used: {solution.NodeAssignments.Value.Count}");
```

**Why?** FluentAPI builder pattern is idiomatic C#. Generic framework provides full power and flexibility.

---

### Task Scheduling Problems

#### F# Developers: Use TaskScheduling (TKT-81)

```fsharp
open FSharp.Azure.Quantum.TaskScheduling

// Domain-specific with time helpers
let taskA = scheduledTask {
    id "TaskA"
    duration (hours 2.0)
    priority 10.0
}

let taskB = scheduledTask {
    id "TaskB"
    duration (minutes 30.0)
    after "TaskA"
    requires "Worker" 1.0
}

let worker = resource {
    id "Worker"
    capacity 2.0
    costPerUnit 50.0
}

let problem = scheduling {
    tasks [taskA; taskB]
    resources [worker]
    objective MinimizeMakespan
}

let! solution = solve problem
printfn "Makespan: %.1f minutes" solution.Makespan
```

**Why?** Time helpers (`hours`, `minutes`) and simple dependencies (`after "TaskA"`) provide better UX.

#### C# Developers: Use Scheduling (TKT-91)

```csharp
using FSharp.Azure.Quantum;
using static FSharp.Azure.Quantum.Scheduling;

// Generic scheduling framework
var taskA = new ScheduledTask<string> {
    Id = "TaskA",
    Value = "Task A",
    Duration = 120.0,  // minutes
    Priority = 10.0,
    EarliestStart = FSharpOption<double>.None,
    Deadline = FSharpOption<double>.None,
    ResourceRequirements = new Dictionary<string, double>().ToFSharpMap(),
    Properties = new Dictionary<string, object>().ToFSharpMap()
};

var taskB = new ScheduledTask<string> {
    Id = "TaskB",
    Value = "Task B",
    Duration = 30.0,
    Priority = 0.0,
    EarliestStart = FSharpOption<double>.None,
    Deadline = FSharpOption<double>.None,
    ResourceRequirements = new Dictionary<string, double> 
        { { "Worker", 1.0 } }.ToFSharpMap(),
    Properties = new Dictionary<string, object>().ToFSharpMap()
};

var problem = new SchedulingBuilder<string, string>()
    .Tasks(new[] { taskA, taskB }.ToFSharpList())
    .AddDependency(Dependency.NewFinishToStart("TaskA", "TaskB", 0.0))
    .Objective(SchedulingObjective.MinimizeMakespan)
    .Build();

var result = solveClassical(problem);
Console.WriteLine($"Makespan: {result.Value.Makespan}");
```

**Why?** Generic types allow storing business objects. Full dependency types (FinishToStart, StartToStart, etc.) available.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                         │
├─────────────────────────┬───────────────────────────────────┤
│                         │                                   │
│   F# Domain Builders    │    C# Should Skip This Layer     │
│   (TKT-80, TKT-81)      │    (Computation expressions      │
│                         │     don't exist in C#)           │
│   - GraphColoring       │                                   │
│   - TaskScheduling      │                                   │
│                         │                                   │
├─────────────────────────┴───────────────────────────────────┤
│                                                              │
│              Generic Framework Layer                         │
│         (Both F# and C# Use This Directly)                  │
│                                                              │
│   - GraphOptimization (TKT-90)                              │
│   - Scheduling (TKT-91)                                     │
│                                                              │
│   FluentAPI Builders:                                       │
│   - GraphOptimizationBuilder<TNode, TEdge>                 │
│   - SchedulingBuilder<TTask, TResource>                    │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

### Key Points

1. **F# Domain Builders** (TKT-80, TKT-81)
   - Use F#-specific computation expressions
   - Provide domain-specific syntax
   - Cover 70-80% of common use cases
   - **F# developers only**

2. **Generic Frameworks** (TKT-90, TKT-91)
   - Use FluentAPI builder pattern
   - Work in both F# and C#
   - Provide 100% of features
   - **Recommended for C# developers**
   - **Also available to F# developers for advanced scenarios**

---

## Decision Tree

### I'm an F# Developer

```
Are you solving a graph coloring problem?
├─ Yes → Use GraphColoring (TKT-80)
│        ✅ Domain syntax: conflictsWith
│        ✅ String colors: ["Red", "Green"]
│        ✅ Simple API
│
└─ No → What kind of graph problem?
   ├─ TSP, MaxCut, Spanning Tree → Use GraphOptimization (TKT-90)
   └─ Custom constraints needed → Use GraphOptimization (TKT-90)

Are you solving a scheduling problem?
├─ Simple (only FinishToStart dependencies)?
│  └─ Yes → Use TaskScheduling (TKT-81)
│           ✅ Time helpers: hours 2.0
│           ✅ Simple syntax: after "TaskA"
│
└─ No (complex dependencies, constraints)?
   └─ Use Scheduling (TKT-91)
      ✅ All dependency types (SS, FF, SF)
      ✅ Constraint system
      ✅ Generic types
```

### I'm a C# Developer

```
Are you solving a graph problem?
└─ Use GraphOptimization (TKT-90)
   ✅ FluentAPI builder pattern
   ✅ Generic types <TNode, TEdge>
   ✅ All objectives and constraints
   ✅ Works for: coloring, TSP, MaxCut, spanning trees, etc.

Are you solving a scheduling problem?
└─ Use Scheduling (TKT-91)
   ✅ FluentAPI builder pattern
   ✅ Generic types <TTask, TResource>
   ✅ All dependency types
   ✅ Full constraint system
```

**Simple rule for C# developers:** Skip the domain builders (TKT-80, TKT-81), go straight to the generic frameworks (TKT-90, TKT-91).

---

## Can C# Use the F# Domain Builders?

**Technically yes, but not recommended.**

### Why Not Recommended?

1. **No Computation Expression Syntax**
   ```csharp
   // ❌ C# doesn't have this syntax
   graphColoring {
       node "A" ["B"]
   }
   ```

2. **Awkward F# Interop**
   ```csharp
   // ⚠️ Possible but ugly
   var node1 = GraphColoring.node("A", 
       new[] { "B" }.ToFSharpList());
   
   var problem = new GraphColoring.GraphColoringProblem(
       Nodes: new[] { node1 }.ToFSharpList(),
       AvailableColors: new[] { "Red" }.ToFSharpList(),
       // ... lots of FSharpOption, ToFSharpList conversions
   );
   ```

3. **Generic Framework is Better for C#**
   ```csharp
   // ✅ Idiomatic C# with FluentAPI
   var problem = new GraphOptimizationBuilder<int, Unit>()
       .Nodes(new[] { node("A", 0) })
       .Edges(new[] { edge("A", "B", 1.0) })
       .Build();
   ```

### When C# Might Use F# Builders

**Only if:**
- You want the exact same API across F# and C# teams
- You're willing to write wrapper methods to hide F# types
- You value consistency over idiomatic C#

**Most C# developers should just use the generic frameworks directly.**

---

## Summary Table

| Aspect | F# Domain Builders | Generic Frameworks |
|--------|-------------------|-------------------|
| **Target Language** | F# only | F# and C# |
| **Syntax** | Computation expressions | FluentAPI builders |
| **Coverage** | 70-80% of common cases | 100% of features |
| **API Style** | Domain-specific | Generic, extensible |
| **Type Parameters** | Fixed (strings, etc.) | Generic `<T>` |
| **F# Recommendation** | ✅ Use for domain problems | Use for advanced scenarios |
| **C# Recommendation** | ❌ Skip, use generic instead | ✅ Use always |

---

## Examples Summary

### Graph Coloring

| Language | Recommended API | Module |
|----------|----------------|--------|
| **F#** | `graphColoring { }` | `GraphColoring` (TKT-80) |
| **C#** | `GraphOptimizationBuilder<int, Unit>()` | `GraphOptimization` (TKT-90) |

### Task Scheduling

| Language | Recommended API | Module |
|----------|----------------|--------|
| **F#** | `scheduledTask { }`, `scheduling { }` | `TaskScheduling` (TKT-81) |
| **C#** | `SchedulingBuilder<T, T>()` | `Scheduling` (TKT-91) |

### Generic Graph Algorithms

| Language | Recommended API | Module |
|----------|----------------|--------|
| **F#** | `GraphOptimizationBuilder<T, T>()` | `GraphOptimization` (TKT-90) |
| **C#** | `GraphOptimizationBuilder<T, T>()` | `GraphOptimization` (TKT-90) |

### Generic Scheduling

| Language | Recommended API | Module |
|----------|----------------|--------|
| **F#** | `SchedulingBuilder<T, T>()` | `Scheduling` (TKT-91) |
| **C#** | `SchedulingBuilder<T, T>()` | `Scheduling` (TKT-91) |

---

## Final Recommendation

### For F# Developers
✅ **Start with domain builders** (TKT-80, TKT-81) for common cases  
✅ **Fall back to generic frameworks** (TKT-90, TKT-91) when you need advanced features

### For C# Developers
✅ **Use generic frameworks directly** (TKT-90, TKT-91)  
✅ **Skip domain builders** - they're F#-specific computation expressions

This gives the best experience for developers in each language while maintaining a unified underlying framework.
