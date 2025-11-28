# API Coverage Comparison: F# Builders vs C# FluentAPI

## Overview

This document compares the F# domain-specific builders (TKT-80, TKT-81) against the underlying C# FluentAPI frameworks (TKT-90, TKT-91).

---

## Graph Coloring: TKT-80 (F#) vs TKT-90 (C#)

### Type Comparison

| Concept | F# GraphColoring (TKT-80) | C# GraphOptimization (TKT-90) |
|---------|---------------------------|-------------------------------|
| **Node** | `ColoredNode` (domain-specific) | `Node<'T>` (generic) |
| **Edge/Conflict** | `ConflictsWith: string list` | `Edge<'T>` with properties |
| **Colors** | `AvailableColors: string list` | `NumColors: int` (count only) |
| **Problem** | `GraphColoringProblem` | `GraphOptimizationProblem<'TNode, 'TEdge>` |
| **Solution** | `ColoringSolution` | `GraphOptimizationSolution<'TNode, 'TEdge>` |

### Feature Coverage

| Feature | F# Builder | C# FluentAPI | Coverage |
|---------|------------|--------------|----------|
| **Basic Node Definition** | ✅ `node "A" ["B"]` | ✅ `.Nodes([node("A", 0)])` | ✅ FULL |
| **Advanced Node (priority, fixed color)** | ✅ `coloredNode { }` | ⚠️ Via `Properties` map | ⚠️ PARTIAL |
| **Conflicts as Business Concept** | ✅ `conflictsWith` | ⚠️ `edges` (generic) | ✅ F# BETTER |
| **Colors as Strings** | ✅ `["Red"; "Green"]` | ❌ Only color count | ❌ C# LIMITATION |
| **Fixed Color Assignment** | ✅ `fixedColor "Red"` | ⚠️ Via `Properties` | ⚠️ PARTIAL |
| **Priority** | ✅ `priority 10.0` | ⚠️ Via `Properties` | ⚠️ PARTIAL |
| **Avoid Colors (Soft Constraint)** | ✅ `avoidColors ["X"]` | ❌ Not supported | ❌ F# ONLY |
| **Custom Properties** | ✅ `property "key" val` | ✅ `.Properties` map | ✅ FULL |
| **Multiple Objectives** | ⚠️ 3 types | ✅ 7+ types | ✅ C# BETTER |
| **Constraints** | ⚠️ Only NoAdjacentEqual | ✅ 8+ constraint types | ✅ C# BETTER |
| **QUBO Encoding** | ✅ Via TKT-90 | ✅ Native | ✅ FULL |
| **Classical Solver** | ✅ Greedy coloring | ✅ Multiple algorithms | ✅ FULL |

### Verdict: GraphColoring

**Coverage: ~70%** - F# builder covers common graph coloring use cases but lacks:
- ❌ Generic type parameters (intentionally domain-specific)
- ❌ Full constraint system (only NoAdjacentEqual)
- ❌ Advanced objectives (only MinimizeColors, MinimizeConflicts, BalanceColors)
- ✅ Better domain language (`conflictsWith` vs `edges`)
- ✅ String-based colors (better UX than color indices)

**Recommendation:**
- **Use F# builder**: Graph coloring problems (register allocation, frequency assignment, exam scheduling)
- **Use C# FluentAPI**: Generic graph algorithms (TSP, MaxCut, custom constraints)

---

## Task Scheduling: TKT-81 (F#) vs TKT-91 (C#)

### Type Comparison

| Concept | F# TaskScheduling (TKT-81) | C# Scheduling (TKT-91) |
|---------|----------------------------|------------------------|
| **Task** | `SchedulingTask` (non-generic) | `ScheduledTask<'T>` (generic) |
| **Resource** | `Resource` (simple) | `Resource<'T>` (generic) |
| **Dependencies** | `Dependencies: string list` | `Dependency` discriminated union |
| **Problem** | `SchedulingProblem` | `SchedulingProblem<'TTask, 'TResource>` |
| **Solution** | `Solution` | `Schedule` |

### Feature Coverage

| Feature | F# Builder | C# FluentAPI | Coverage |
|---------|------------|--------------|----------|
| **Basic Task Definition** | ✅ `scheduledTask { }` | ✅ `task(id, value, duration)` | ✅ FULL |
| **Task Duration** | ✅ `duration (hours 2.0)` | ✅ `Duration` field | ✅ FULL |
| **Simple Dependencies (after)** | ✅ `after "TaskA"` | ⚠️ `FinishToStart("A", "B", 0.0)` | ✅ F# BETTER (UX) |
| **Dependency Types** | ❌ Only FinishToStart | ✅ 4 types (FS, SS, FF, SF) | ❌ C# BETTER |
| **Resource Requirements** | ✅ `requires "Worker" 1.0` | ✅ `ResourceRequirements` map | ✅ FULL |
| **Priority** | ✅ `priority 10.0` | ✅ `Priority` field | ✅ FULL |
| **Deadline** | ✅ `deadline 180.0` | ✅ `Deadline` field | ✅ FULL |
| **Earliest Start** | ✅ `earliestStart 60.0` | ✅ `EarliestStart` field | ✅ FULL |
| **Resource Capacity** | ✅ `capacity 2.0` | ✅ `Capacity` field | ✅ FULL |
| **Resource Cost** | ✅ `costPerUnit 50.0` | ✅ `CostPerUnit` field | ✅ FULL |
| **Resource Time Windows** | ✅ `availableWindow 0.0 480.0` | ✅ `AvailableWindows` list | ⚠️ PARTIAL (F# single window) |
| **Objectives** | ✅ 4 types | ✅ 5+ types | ✅ FULL |
| **Constraints** | ❌ Not exposed | ✅ 6+ constraint types | ❌ C# BETTER |
| **Generic Type Parameters** | ❌ String-based only | ✅ Generic `<'T>` | ❌ C# BETTER |
| **Time Unit Helpers** | ✅ `hours`, `minutes`, `days` | ❌ Raw floats | ✅ F# BETTER |

### Verdict: TaskScheduling

**Coverage: ~80%** - F# builder covers most common scheduling use cases but lacks:
- ❌ Generic type parameters (intentionally simplified)
- ❌ Advanced dependency types (only FinishToStart)
- ❌ Constraint system (NoOverlap, TimeWindow, etc.)
- ❌ Resource time windows (only single window)
- ✅ Better UX (`after "A"` vs `FinishToStart("A", "B", 0.0)`)
- ✅ Time unit helpers (`hours 2.0` vs `120.0`)

**Recommendation:**
- **Use F# builder**: Simple scheduling problems (project management, job shop basics)
- **Use C# FluentAPI**: Complex scheduling (multiple dependency types, advanced constraints)

---

## Summary: API Coverage

### Graph Coloring (TKT-80 vs TKT-90)

```
Coverage: 70%
Domain Focus: Graph Coloring only
Strengths: Domain language, string colors, UX
Gaps: Generic graphs, advanced constraints/objectives
```

### Task Scheduling (TKT-81 vs TKT-91)

```
Coverage: 80%
Domain Focus: Simple task scheduling
Strengths: UX, time helpers, simple dependencies
Gaps: Generic types, advanced dependencies, constraints
```

---

## Design Philosophy

### F# Domain Builders (TKT-80, TKT-81)

**Goal:** 80/20 rule - cover 80% of use cases with 20% of the API surface

**Trade-offs:**
- ✅ **Simple**: Fewer concepts, easier to learn
- ✅ **Domain-specific**: Business language (`conflictsWith` vs `edges`)
- ✅ **Progressive disclosure**: Simple inline → advanced builder
- ❌ **Not generic**: Fixed to specific domains
- ❌ **Feature subset**: Doesn't expose all underlying framework features

**Target audience:** Domain experts (compiler writers, schedulers) who need quick solutions

### C# FluentAPI Frameworks (TKT-90, TKT-91)

**Goal:** Complete, extensible frameworks for any graph/scheduling problem

**Trade-offs:**
- ✅ **Generic**: Works with any type `<'T>`
- ✅ **Complete**: All constraints, objectives, dependency types
- ✅ **Extensible**: Custom constraints/objectives
- ❌ **Complex**: Steeper learning curve
- ❌ **Technical**: Graph theory language

**Target audience:** Algorithm developers, researchers, custom problem solvers

---

## Missing Features in F# Builders

### GraphColoring (TKT-80)

**Not covered from TKT-90:**

1. **Generic Type Parameters**
   - TKT-90: `Node<'T>`, `Edge<'TEdge>`
   - TKT-80: Fixed to strings
   - **Impact**: Can't store custom business objects in nodes

2. **Advanced Constraints**
   - TKT-90: `VisitOnce`, `Acyclic`, `Connected`, `DegreeLimit`, `MinDegree`, `OneIncoming`, `OneOutgoing`
   - TKT-80: Only `NoAdjacentEqual` (implicit)
   - **Impact**: Can't solve TSP, spanning tree, or other graph problems

3. **Advanced Objectives**
   - TKT-90: `MinimizeTotalWeight`, `MaximizeCut`, `MinimizeSpanningTree`, `MinimizeMaxWeight`, `MaximizeEdges`, `MinimizeEdges`
   - TKT-80: Only `MinimizeColors`, `MinimizeConflicts`, `BalanceColors`
   - **Impact**: Limited to coloring problems

4. **Directed Graphs**
   - TKT-90: `directed` flag, `directedEdge`
   - TKT-80: Undirected only
   - **Impact**: Can't solve directed graph problems

5. **Edge Weights**
   - TKT-90: `Weight: float` on edges
   - TKT-80: No weights (conflicts are unweighted)
   - **Impact**: Can't optimize weighted graphs

### TaskScheduling (TKT-81)

**Not covered from TKT-91:**

1. **Generic Type Parameters**
   - TKT-91: `ScheduledTask<'T>`, `Resource<'T>`
   - TKT-81: Non-generic
   - **Impact**: Can't store typed business data

2. **Advanced Dependencies**
   - TKT-91: `StartToStart`, `FinishToFinish`, `StartToFinish`
   - TKT-81: Only `FinishToStart` (via `after`)
   - **Impact**: Can't model complex temporal relationships

3. **Constraints System**
   - TKT-91: `NoOverlap`, `TimeWindow`, `MaxMakespan`, `ResourceCapacity`
   - TKT-81: Not exposed
   - **Impact**: Constraints handled implicitly, less control

4. **Resource Time Windows**
   - TKT-91: Multiple `(start, end)` windows per resource
   - TKT-81: Single `availableWindow start end`
   - **Impact**: Can't model shift schedules or maintenance windows

5. **Custom Objectives/Constraints**
   - TKT-91: `Custom of (Schedule -> float/bool)`
   - TKT-81: Not exposed
   - **Impact**: Can't extend for domain-specific rules

---

## Recommendations

### When to Use F# Builders

✅ **Use GraphColoring (TKT-80) when:**
- Solving graph coloring problems (register allocation, frequency assignment, exam scheduling)
- Need simple, readable API
- String-based colors are sufficient
- Don't need advanced constraints

✅ **Use TaskScheduling (TKT-81) when:**
- Solving simple scheduling problems (project management, basic job shop)
- Only need FinishToStart dependencies
- Domain-specific UX is important (`hours 2.0` vs `120.0`)
- Don't need generic type parameters

### When to Use C# FluentAPI

✅ **Use GraphOptimization (TKT-90) when:**
- Solving general graph problems (TSP, MaxCut, spanning trees)
- Need custom constraints or objectives
- Need generic type parameters
- Need directed graphs or edge weights

✅ **Use Scheduling (TKT-91) when:**
- Solving complex scheduling problems
- Need advanced dependency types (SS, FF, SF)
- Need custom constraints or objectives
- Need generic type parameters
- Need multiple resource time windows

### Hybrid Approach

**Best practice:** Start with F# builder, fall back to C# FluentAPI if needed

```fsharp
// Try F# builder first (80% case)
let problem = graphColoring {
    node "A" ["B"; "C"]
    colors ["Red"; "Green"]
}

// If you hit limitations, use C# FluentAPI directly (20% case)
let complexProblem =
    GraphOptimizationBuilder()
        .Nodes([...])
        .Edges([...])
        .AddConstraint(DegreeLimit(3))  // Not available in F# builder
        .Objective(MinimizeTotalWeight)  // Not available in F# builder
        .Build()
```

---

## Future Enhancements

### Potential F# Builder Extensions

1. **Expose Constraints** (TaskScheduling)
   ```fsharp
   scheduling {
       tasks [...]
       constraint (NoOverlap ["Task1"; "Task2"])
       constraint (MaxMakespan 100.0)
   }
   ```

2. **Advanced Dependencies** (TaskScheduling)
   ```fsharp
   scheduledTask {
       startToStart "TaskA" lag:10.0
       finishToFinish "TaskB" lag:0.0
   }
   ```

3. **Multiple Resource Windows** (TaskScheduling)
   ```fsharp
   resource {
       id "Worker"
       capacity 2.0
       availableWindows [(0.0, 480.0); (960.0, 1440.0)]  // Two shifts
   }
   ```

4. **Directed Graphs** (GraphColoring)
   ```fsharp
   graphColoring {
       directed true
       node "A" successors:["B"; "C"]  // Directed edges
   }
   ```

### Low Priority (Use C# FluentAPI Instead)

- ❌ Generic type parameters (defeats purpose of domain-specific builders)
- ❌ Custom constraints/objectives (too advanced, use TKT-90/91 directly)

---

## Conclusion

The F# builders (TKT-80, TKT-81) are **intentionally simplified** to cover common use cases with excellent UX. They provide **70-80% coverage** of the underlying C# frameworks.

**Key principle:** Progressive disclosure
1. Start with F# builder (simple, domain-specific)
2. If you need advanced features, drop down to C# FluentAPI (generic, complete)

Both approaches are production-ready and complement each other well.
