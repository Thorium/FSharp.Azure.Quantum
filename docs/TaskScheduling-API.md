# Task Scheduling Domain Builder - API Reference

## Overview

The Task Scheduling domain builder defines and solves task scheduling problems with dependencies, resource constraints and deadlines. It provides:

1. **F# computation expression builders** (`scheduledTask { }`, `resource { }`, `scheduling { }`) - dependencies are declared on the task that has them (`after "TaskA"`)
2. **C# helpers** (the `Scheduling` module: `Scheduling.task`, `Scheduling.SchedulingBuilder`) - method chaining for C#

Both produce the same `SchedulingProblem<'TTask, 'TResource>` record, which you solve with `solveQuantum` (QUBO + QAOA on an `IQuantumBackend`). A dependency-only classical scheduler, `ClassicalSolver.solve`, is also public.

---

## Design

- **Business terms, not quantum terms**: tasks, dependencies, resources and deadlines. The QUBO encoding and the QAOA circuit are internal.
- **Dependencies at the definition point**: `after` and `afterMultiple` sit inside the task that depends on something, so reading a task shows what it waits for.
- **Progressive disclosure**: a task needs only `taskId` and `duration`; add `after`, `requires`, `deadline`, `priority` or `earliestStart` when needed.
- **Real time units**: durations, deadlines and horizons are `TimeSpan` values, built with `minutes`, `hours` and `days`.

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.TaskScheduling

// Simple case
let simple : ScheduledTask<unit> = scheduledTask {
    taskId "Task1"
    duration (hours 2.0)
}

// Complex case adds only what's needed
let complex : ScheduledTask<unit> = scheduledTask {
    taskId "Task2"
    duration (hours 1.5)
    after "Task1"
    requires "Worker" 2.0
    priority 10.0
    deadline (minutes 180.0)
}

// Task templates are ordinary functions
let createSafetyTask (taskName: string) (durationMins: float) : ScheduledTask<unit> =
    scheduledTask {
        taskId taskName
        duration (minutes durationMins)
        priority 10.0
    }

let electricalSafety = createSafetyTask "SafetyElectrical" 15.0
let mechanicalSafety = createSafetyTask "SafetyMechanical" 20.0
```

The builders are generic in the task payload type (`ScheduledTask<'T>`); tasks built with `scheduledTask { }` carry no payload (`Value = None`), so the examples fix it to `unit` with a type annotation.

---

## Quick Start

### F# Computation Expression

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.TaskScheduling
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

// Define tasks with dependencies
let taskA : ScheduledTask<unit> = scheduledTask {
    taskId "TaskA"
    duration (hours 2.0)
}

let taskB : ScheduledTask<unit> = scheduledTask {
    taskId "TaskB"
    duration (minutes 30.0)
    after "TaskA"  // TaskB depends on TaskA
    deadline (minutes 180.0)
}

// Compose scheduling problem
let problem : SchedulingProblem<unit, unit> = scheduling {
    tasks [taskA; taskB]
    objective MinimizeMakespan
    timeHorizon (hours 3.0)  // Keep close to the expected makespan (see "How solveQuantum works")
}

// Solve on the local simulator (2 tasks x 6 time slots = 12 qubits)
let backend = LocalBackend.LocalBackend() :> IQuantumBackend

match solveQuantum backend problem |> Async.RunSynchronously with
| Ok solution ->
    printfn "Makespan: %.1f minutes" solution.Makespan.TotalMinutes
    exportGanttChart solution "schedule.txt"
| Error err ->
    printfn "Failed: %s" err.Message
```

`open FSharp.Azure.Quantum` brings the builders, `solveQuantum`, `exportGanttChart` and the time helpers into scope; `open FSharp.Azure.Quantum.TaskScheduling` brings the types (`ScheduledTask`, `SchedulingProblem`, `Solution`, the `Objective` cases, `Dependency`).

### C#

```csharp
using System;
using System.Threading;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using FSharp.Azure.Quantum;
using FSharp.Azure.Quantum.Backends;
using FSharp.Azure.Quantum.TaskScheduling;

// Define tasks (id, payload, duration)
var taskA = Scheduling.task("TaskA", "TaskA-Value", TimeSpan.FromHours(2.0));
var taskB = Scheduling.taskWithRequirements(
    "TaskB",
    "TaskB-Value",
    TimeSpan.FromMinutes(30.0),
    ListModule.OfSeq(new[] { Tuple.Create("Worker", 1.0) }));

// Compose scheduling problem (dependencies are added separately).
// Create is itself generic, so its type arguments are given explicitly.
var problem = Scheduling.SchedulingBuilder<string, string>.Create<string, string>()
    .Tasks(ListModule.OfSeq(new[] { taskA, taskB }))
    .AddDependency(Types.Dependency.NewFinishToStart("TaskA", "TaskB", TimeSpan.Zero))
    .Objective(Scheduling.SchedulingObjective.MinimizeMakespan)
    .TimeHorizon(TimeSpan.FromHours(3.0))
    .Build();

// Solve
var backend = new LocalBackend.LocalBackend();
var result = FSharpAsync.RunSynchronously(
    TaskSchedulingTypes.solveQuantum(backend, problem),
    FSharpOption<int>.None,
    FSharpOption<CancellationToken>.None);

if (result.IsOk)
{
    Console.WriteLine($"Makespan: {result.ResultValue.Makespan.TotalMinutes} minutes");
}
else
{
    Console.WriteLine($"Failed: {result.ErrorValue.Message}");
}
```

**Key Differences:**
- **F# builder**: dependencies declared on the task (`after "TaskA"`); tasks have no payload
- **C# helpers**: dependencies added separately (`.AddDependency(...)`); tasks carry a payload value
- Both use `TimeSpan` for durations, deadlines and the horizon
- The C# code references FSharp.Core (`ListModule`, `FSharpAsync`, `FSharpOption`), which comes with the package

---

## Builder Reference

### 1. `scheduledTask` Builder

Define individual tasks with duration, dependencies and constraints.

**Available Operations:**

| Operation | Parameters | Description | Example |
|-----------|------------|-------------|---------|
| `taskId` | `string` | ✅ **Required** - Unique task identifier | `taskId "TaskA"` |
| `duration` | `TimeSpan` | ✅ **Required** - Task duration | `duration (hours 2.0)` |
| `after` | `string` | Add single dependency (task ID) | `after "TaskA"` |
| `afterMultiple` | `string list` | Add multiple dependencies | `afterMultiple ["A"; "B"]` |
| `requires` | `string, float` | Add resource requirement (ID, quantity) | `requires "Worker" 2.0` |
| `priority` | `float` | Tie-breaking priority (higher = more important) | `priority 10.0` |
| `deadline` | `TimeSpan` | Latest completion time, as an offset from the schedule start | `deadline (minutes 180.0)` |
| `earliestStart` | `TimeSpan` | Earliest allowed start time, as an offset | `earliestStart (minutes 60.0)` |

Missed deadlines are reported in `Solution.DeadlineViolations`; they do not make `solveQuantum` fail. `priority` and `earliestStart` are honoured by `ClassicalSolver.solve` only; the quantum encoding ignores them.

**Examples:**

```fsharp
// Simple task
let taskA : ScheduledTask<unit> = scheduledTask {
    taskId "TaskA"
    duration (minutes 30.0)
}

// Task with single dependency
let taskB : ScheduledTask<unit> = scheduledTask {
    taskId "TaskB"
    duration (hours 1.0)
    after "TaskA"  // TaskB starts after TaskA completes
}

// Task with multiple dependencies
let taskC : ScheduledTask<unit> = scheduledTask {
    taskId "TaskC"
    duration (hours 2.0)
    afterMultiple ["TaskA"; "TaskB"]  // TaskC starts after both complete
}

// Task with all options
let taskD : ScheduledTask<unit> = scheduledTask {
    taskId "TaskD"
    duration (hours 1.5)
    after "TaskC"
    requires "Worker" 2.0
    requires "Machine" 1.0
    priority 10.0
    deadline (minutes 300.0)
    earliestStart (minutes 60.0)
}
```

---

### 2. `resource` Builder

Define resources with capacity and cost.

**Available Operations:**

| Operation | Parameters | Description | Example |
|-----------|------------|-------------|---------|
| `resourceId` | `string` | ✅ **Required** - Unique resource identifier | `resourceId "Worker"` |
| `capacity` | `float` | ✅ **Required** - Units available at any moment | `capacity 3.0` |
| `costPerUnit` | `float` | Cost per unit per minute (default 0.0) | `costPerUnit 50.0` |
| `availableWindow` | `float, float` | Availability window (start, end); stored, not used by the solvers | `availableWindow 0.0 480.0` |

Total cost is Σ `costPerUnit` × quantity × task duration in minutes, over every task's requirements.

**Examples:**

```fsharp
// Simple resource
let worker : Resource<unit> = resource {
    resourceId "Worker"
    capacity 3.0
}

// Resource with cost
let machine : Resource<unit> = resource {
    resourceId "Machine"
    capacity 2.0
    costPerUnit 100.0
}

// Resource with an availability window (metadata)
let specialist : Resource<unit> = resource {
    resourceId "Specialist"
    capacity 1.0
    costPerUnit 200.0
    availableWindow 480.0 960.0
}
```

**Helper Function - `crew`:**

`crew id capacity costPerUnit` creates a `Resource<string>` whose payload is its ID:

```fsharp
// Using builder
let crew1 : Resource<unit> = resource {
    resourceId "SafetyCrew"
    capacity 2.0
    costPerUnit 100.0
}

// Using helper (same capacity and cost, payload Some "SafetyCrew")
let crew2 = crew "SafetyCrew" 2.0 100.0
```

---

### 3. `scheduling` Builder

Compose complete scheduling problems from tasks and resources.

**Available Operations:**

| Operation | Parameters | Description | Example |
|-----------|------------|-------------|---------|
| `tasks` | `ScheduledTask list` | ✅ **Required** - Tasks to schedule; their `after` dependencies are collected here | `tasks [taskA; taskB]` |
| `resources` | `Resource list` | Available resources (optional) | `resources [worker; machine]` |
| `objective` | `Objective` | Optimization goal (default `MinimizeMakespan`) | `objective MinimizeCost` |
| `timeHorizon` | `TimeSpan` | Scheduling window (default 1000 minutes) | `timeHorizon (hours 8.0)` |

**Available Objectives:**

| Objective | Description |
|-----------|-------------|
| `MinimizeMakespan` | Finish all tasks as early as possible (default) |
| `MinimizeCost` | Minimize total resource cost |
| `MaximizeResourceUtilization` | Keep resources busy |
| `MinimizeLateness` | Minimize total time past deadlines |

In this model every task always uses exactly its required resources for its full duration, so total cost and total usage are the same for every feasible schedule. `MinimizeCost` and `MaximizeResourceUtilization` therefore pick the shortest feasible schedule, like `MinimizeMakespan`. `MinimizeLateness` picks the schedule with the least total lateness (makespan breaks ties).

**Examples:**

```fsharp
// Simple problem (no resources)
let problem1 : SchedulingProblem<unit, unit> = scheduling {
    tasks [taskA; taskB; taskC]
    objective MinimizeMakespan
}

// Problem with resources
let problem2 : SchedulingProblem<unit, unit> = scheduling {
    tasks [taskA; taskB; taskC]
    resources [worker; machine]
    objective MinimizeCost
}

// Problem with a time horizon
let problem3 : SchedulingProblem<unit, unit> = scheduling {
    tasks [taskA; taskB; taskC; taskD]
    resources [worker; machine; specialist]
    objective MinimizeMakespan
    timeHorizon (hours 10.0)
}
```

The builders are F# computation expressions; from C#, use the `Scheduling` module helpers shown in [C# Interop](#c-interop).

---

## Time Unit Helpers

Readable duration specifications. Each returns a `System.TimeSpan`:

| Function | Example | Result |
|----------|---------|--------|
| `minutes` | `minutes 30.0` | `TimeSpan.FromMinutes 30.0` |
| `hours` | `hours 2.0` | `TimeSpan.FromHours 2.0` (120 minutes) |
| `days` | `days 1.0` | `TimeSpan.FromDays 1.0` (1440 minutes) |

**Examples:**

```fsharp
let task1 : ScheduledTask<unit> = scheduledTask {
    taskId "Task1"
    duration (minutes 30.0)
}

let task2 : ScheduledTask<unit> = scheduledTask {
    taskId "Task2"
    duration (hours 2.0)
}

let task3 : ScheduledTask<unit> = scheduledTask {
    taskId "Task3"
    duration (days 1.0)
}
```

From C#, use `TimeSpan.FromMinutes`, `TimeSpan.FromHours` and `TimeSpan.FromDays`.

---

## Functions

### `solveQuantum`

Solve the scheduling problem on a quantum backend.

**Signature:**

```text
val solveQuantum :
    backend:IQuantumBackend ->
    problem:SchedulingProblem<'TTask, 'TResource> ->
    Async<QuantumResult<Solution>>
```

> **Note:** This API returns F# `Async<_>`. Backend-level async operations use Task-based APIs with `CancellationToken`. See [Backend Switching](backend-switching.md) for `task { }` patterns.

**Parameters:**
- `backend` - Any `IQuantumBackend`, e.g. `LocalBackend.LocalBackend() :> IQuantumBackend`
- `problem` - Scheduling problem defined with `scheduling { ... }`

**Returns** (`QuantumResult<Solution>` is `Result<Solution, QuantumError>`):
- `Ok solution` - The best feasible schedule found
- `Error err` - Validation failure, a problem too large for the backend, or no feasible schedule among the measured samples

**Solution Fields:**

| Field | Type | Description |
|-------|------|-------------|
| `Assignments` | `TaskAssignment list` | Task start/end times and resources |
| `Makespan` | `TimeSpan` | Total completion time (max end time) |
| `TotalCost` | `float` | Total resource usage cost |
| `ResourceUtilization` | `Map<string, float>` | Utilization per resource (0.0-1.0) |
| `DeadlineViolations` | `string list` | Task IDs that missed deadlines |
| `IsValid` | `bool` | True if no deadline violations |

**TaskAssignment Fields:**

| Field | Type | Description |
|-------|------|-------------|
| `TaskId` | `string` | Task identifier |
| `StartTime` | `TimeSpan` | Start time, as an offset from the schedule start |
| `EndTime` | `TimeSpan` | End time, as an offset from the schedule start |
| `AssignedResources` | `Map<string, float>` | Resources allocated (ID -> quantity) |

**Example:**

```fsharp
match solveQuantum backend problem |> Async.RunSynchronously with
| Ok solution ->
    printfn "Makespan: %.1f minutes" solution.Makespan.TotalMinutes
    printfn "Total Cost: $%.2f" solution.TotalCost
    printfn "Valid: %b" solution.IsValid

    printfn "\nTask Assignments:"
    solution.Assignments
    |> List.sortBy (fun a -> a.StartTime)
    |> List.iter (fun a ->
        printfn "  %s: [%.1f - %.1f]" a.TaskId a.StartTime.TotalMinutes a.EndTime.TotalMinutes)

    if not (List.isEmpty solution.DeadlineViolations) then
        printfn "\nDeadline Violations:"
        solution.DeadlineViolations |> List.iter (printfn "  - %s")

| Error err ->
    printfn "Scheduling failed: %s" err.Message
```

**Validation Checks** (the same for both solvers):
- All tasks have non-empty, unique IDs
- All dependencies reference existing tasks

Circular dependencies are not rejected up front: `solveQuantum` then finds no feasible sample and returns an `Error`.

#### How `solveQuantum` works

1. **Time slots.** Time is split into a small grid of equal slots. The window is `max timeHorizon (total task duration)`. The slot count starts from window ÷ shortest task duration, is capped so that tasks × slots stays near 18 (at most 10 slots, at least 2), and is never below the length of the longest dependency chain. Slot length = window ÷ slot count, and every task starts on a slot boundary.
2. **QUBO.** One binary variable per (task, slot) - so **qubits = tasks × slots** - with penalty terms for "start exactly once", dependencies and resource capacity, plus the objective.
3. **QAOA.** One layer with fixed angles (γ = β = 0.5), 1000 shots.
4. **Decode and check.** Each shot is decoded to start times; shots that break a dependency or overload a resource are discarded, and the best remaining schedule by the objective is returned.

Consequences:
- **Set `timeHorizon` close to the expected makespan.** With the default 1000-minute window, a short problem gets slots of well over an hour, so tasks can only start at those coarse boundaries and the makespan is padded accordingly.
- **Size limit.** The backend must be able to run tasks × slots qubits. For `LocalBackend` the limit is the smaller of its memory capacity (`StateVector.maxQubits`, derived from available memory, at most 30) and its wall-clock budget (20 qubits by default, raised with the `FSAQ_MAX_CIRCUIT_QUBITS` environment variable). A larger problem returns an `Error` naming the qubits it needs; long dependency chains raise the slot count and hit this first.
- **Results are sampled.** A run can come back with `Error "No valid solutions found"`, or with a schedule that is feasible but not optimal.

### `ClassicalSolver.solve`

```text
val ClassicalSolver.solve :
    problem:SchedulingProblem<'TTask, 'TResource> -> QuantumResult<Solution>
```

A dependency-only greedy scheduler (in `FSharp.Azure.Quantum.TaskScheduling`): tasks are taken in dependency order and each starts as early as its predecessors and `earliestStart` allow. It has no size limit and gives the exact earliest-start schedule when resources don't matter, but it **ignores resource capacities**: tasks competing for the same resource may overlap.

```fsharp
match ClassicalSolver.solve problem with
| Ok solution -> printfn "Makespan: %.1f minutes" solution.Makespan.TotalMinutes
| Error err -> printfn "Failed: %s" err.Message
```

---

### `exportGanttChart`

Export the schedule as a Gantt chart in text format.

**Signature:**

```text
val exportGanttChart : solution:Solution -> filePath:string -> unit
```

**Parameters:**
- `solution` - Scheduling solution from `solveQuantum` or `ClassicalSolver.solve`
- `filePath` - Output file path (e.g., "schedule.txt"); the file is overwritten

**Output Format:**
- Header with makespan (minutes), total cost, and validity
- Task assignments sorted by start time, names cut to 12 characters
- Bars of █ characters, one per minute of duration
- Deadline violations list (if any)

**Example:**

```fsharp
match solveQuantum backend problem |> Async.RunSynchronously with
| Ok solution ->
    exportGanttChart solution "my-schedule.txt"
    printfn "Gantt chart saved!"
| Error err ->
    printfn "Failed: %s" err.Message
```

**Example Output File** (the powerplant schedule from Example 4):

```
# Gantt Chart - Task Schedule

Makespan: 140.0 minutes
Total Cost: $0.00
Valid: true

Task Assignments:
----------------
SafetyElectr [   0.0 -   15.0] ███████████████
SafetyMechan [   0.0 -   20.0] ████████████████████
InitControl  [  15.0 -   40.0] █████████████████████████
InitCooling  [  20.0 -   50.0] ██████████████████████████████
StartPump1   [  50.0 -   60.0] ██████████
StartPump2   [  50.0 -   60.0] ██████████
StartTurbine [  60.0 -  105.0] █████████████████████████████████████████████
SyncGrid     [ 105.0 -  120.0] ███████████████
FullPower    [ 120.0 -  140.0] ████████████████████
```

---

## Complete Examples

### Example 1: Simple 3-Task Chain

#### F# Computation Expression

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.TaskScheduling

// Define tasks A → B → C
let taskA : ScheduledTask<unit> = scheduledTask {
    taskId "TaskA"
    duration (minutes 10.0)
}

let taskB : ScheduledTask<unit> = scheduledTask {
    taskId "TaskB"
    duration (minutes 20.0)
    after "TaskA"  // ✅ Dependency visible at definition
}

let taskC : ScheduledTask<unit> = scheduledTask {
    taskId "TaskC"
    duration (minutes 15.0)
    after "TaskB"  // ✅ Dependency visible at definition
}

let problem : SchedulingProblem<unit, unit> = scheduling {
    tasks [taskA; taskB; taskC]
    objective MinimizeMakespan
    timeHorizon (minutes 60.0)  // 6 slots of 10 minutes: 3 tasks x 6 slots = 18 qubits
}

let result = solveQuantum backend problem |> Async.RunSynchronously

// Best possible: makespan 45 minutes (sequential execution)
```

#### C#

```csharp
using System;
using Microsoft.FSharp.Collections;
using FSharp.Azure.Quantum;
using FSharp.Azure.Quantum.TaskScheduling;

// Define tasks (dependencies added separately)
var taskA = Scheduling.task("TaskA", "A", TimeSpan.FromMinutes(10.0));
var taskB = Scheduling.task("TaskB", "B", TimeSpan.FromMinutes(20.0));
var taskC = Scheduling.task("TaskC", "C", TimeSpan.FromMinutes(15.0));

var problem = Scheduling.SchedulingBuilder<string, string>.Create<string, string>()
    .Tasks(ListModule.OfSeq(new[] { taskA, taskB, taskC }))
    .AddDependency(Types.Dependency.NewFinishToStart("TaskA", "TaskB", TimeSpan.Zero))  // TaskB after TaskA
    .AddDependency(Types.Dependency.NewFinishToStart("TaskB", "TaskC", TimeSpan.Zero))  // TaskC after TaskB
    .Objective(Scheduling.SchedulingObjective.MinimizeMakespan)
    .TimeHorizon(TimeSpan.FromMinutes(60.0))
    .Build();

// Solve with TaskSchedulingTypes.solveQuantum as in the Quick Start
```

### Example 2: Parallel Tasks with Resources

```fsharp
// Two tasks requiring the same resource
let jobA : ScheduledTask<unit> = scheduledTask {
    taskId "JobA"
    duration (hours 1.0)
    requires "Worker" 1.0
}

let jobB : ScheduledTask<unit> = scheduledTask {
    taskId "JobB"
    duration (hours 1.5)
    requires "Worker" 1.0
}

// Only 1 worker available
let oneWorker : Resource<unit> = resource {
    resourceId "Worker"
    capacity 1.0
    costPerUnit 50.0
}

let sharedProblem : SchedulingProblem<unit, unit> = scheduling {
    tasks [jobA; jobB]
    resources [oneWorker]
    objective MinimizeCost
    timeHorizon (hours 3.0)  // 3 slots of 1 hour: 2 tasks x 3 slots = 6 qubits
}

let sharedResult = solveQuantum backend sharedProblem |> Async.RunSynchronously
// Tasks are serialized because the worker has capacity 1
```

### Example 3: Deadline-Constrained Scheduling

```fsharp
let prep : ScheduledTask<unit> = scheduledTask {
    taskId "Prep"
    duration (hours 1.0)
}

let delivery : ScheduledTask<unit> = scheduledTask {
    taskId "Delivery"
    duration (hours 2.0)
    after "Prep"
    deadline (minutes 150.0)  // Cannot be met: Prep + Delivery take 180 minutes
}

let deadlineProblem : SchedulingProblem<unit, unit> = scheduling {
    tasks [prep; delivery]
    objective MinimizeLateness
    timeHorizon (hours 3.0)
}

match solveQuantum backend deadlineProblem |> Async.RunSynchronously with
| Ok solution ->
    if solution.IsValid then
        printfn "✅ All deadlines met!"
    else
        printfn "⚠️ Deadline violations: %A" solution.DeadlineViolations  // ["Delivery"]
| Error err ->
    printfn "Failed: %s" err.Message
```

---

### Example 3.5: Data-Driven Scheduling

**Scenario**: Load tasks from a database and build the schedule conditionally.

Custom operations such as `after` or `deadline` cannot be placed inside `if`/`match` in the builder. For conditional data, build the records directly - `ScheduledTask` and `SchedulingProblem` are ordinary F# records - and add the dependencies to the problem:

```fsharp
open System

// Database task model
type DbTask = {
    Name: string
    DurationMinutes: int
    Rank: int
    DependsOn: string option
    DeadlineMinutes: float option
    NeedsWorker: bool
}

// Rows as they might come from a database query
let dbTasks = [
    { Name = "Extract"; DurationMinutes = 30; Rank = 8; DependsOn = None; DeadlineMinutes = None; NeedsWorker = true }
    { Name = "Transform"; DurationMinutes = 45; Rank = 6; DependsOn = Some "Extract"; DeadlineMinutes = Some 120.0; NeedsWorker = false }
    { Name = "Archive"; DurationMinutes = 20; Rank = 2; DependsOn = None; DeadlineMinutes = None; NeedsWorker = false }
]

let environment = "Production"

let toTask (row: DbTask) : ScheduledTask<unit> = {
    Id = row.Name
    Value = None
    Duration = minutes (float row.DurationMinutes)
    EarliestStart = None
    Deadline = row.DeadlineMinutes |> Option.map minutes
    ResourceRequirements = if row.NeedsWorker then Map.ofList ["Worker", 1.0] else Map.empty
    Priority = float row.Rank
    Properties = Map.empty
}

// Filter high-priority rows; a dependency on a filtered-out task would fail validation
let selected = dbTasks |> List.filter (fun t -> t.Rank >= 5)

let dependencies =
    selected
    |> List.choose (fun t -> t.DependsOn |> Option.map (fun pred -> FinishToStart(pred, t.Name, TimeSpan.Zero)))

let dbProblem : SchedulingProblem<unit, unit> =
    let baseProblem = scheduling {
        tasks (selected |> List.map toTask)
        objective (if environment = "Production" then MinimizeMakespan else MinimizeCost)
        timeHorizon (hours 2.0)
    }
    { baseProblem with Dependencies = dependencies }
```

From C#, the same is done with a loop that fills a task list and calls `.AddDependency(...)` for each dependency before `.Build()`.

---

### Example 4: Powerplant Startup

A nine-task startup sequence with a six-task critical path.

```fsharp
// Phase 1: Safety checks (parallel)
let safetyElectrical : ScheduledTask<unit> = scheduledTask {
    taskId "SafetyElectrical"
    duration (minutes 15.0)
    priority 10.0
}

let safetyMechanical : ScheduledTask<unit> = scheduledTask {
    taskId "SafetyMechanical"
    duration (minutes 20.0)
    priority 10.0
}

// Phase 2: System initialization
let initCooling : ScheduledTask<unit> = scheduledTask {
    taskId "InitCooling"
    duration (minutes 30.0)
    afterMultiple ["SafetyElectrical"; "SafetyMechanical"]
}

let initControl : ScheduledTask<unit> = scheduledTask {
    taskId "InitControl"
    duration (minutes 25.0)
    after "SafetyElectrical"
}

// Phase 3: Component startup
let startPump1 : ScheduledTask<unit> = scheduledTask {
    taskId "StartPump1"
    duration (minutes 10.0)
    after "InitCooling"
}

let startPump2 : ScheduledTask<unit> = scheduledTask {
    taskId "StartPump2"
    duration (minutes 10.0)
    after "InitCooling"
}

let startTurbine : ScheduledTask<unit> = scheduledTask {
    taskId "StartTurbine"
    duration (minutes 45.0)
    afterMultiple ["StartPump1"; "StartPump2"; "InitControl"]
}

// Phase 4: Power generation
let syncGrid : ScheduledTask<unit> = scheduledTask {
    taskId "SyncGrid"
    duration (minutes 15.0)
    after "StartTurbine"
}

let fullPower : ScheduledTask<unit> = scheduledTask {
    taskId "FullPower"
    duration (minutes 20.0)
    after "SyncGrid"
    deadline (minutes 180.0)  // Must reach full power within 180 minutes
}

let startupProblem : SchedulingProblem<unit, unit> = scheduling {
    tasks [
        safetyElectrical; safetyMechanical
        initCooling; initControl
        startPump1; startPump2; startTurbine
        syncGrid; fullPower
    ]
    objective MinimizeMakespan
    timeHorizon (minutes 180.0)
}

// No shared resources, so the dependency-only classical scheduler is exact here
match ClassicalSolver.solve startupProblem with
| Ok solution ->
    printfn "Powerplant Startup Schedule"
    printfn "Makespan: %.1f minutes" solution.Makespan.TotalMinutes
    exportGanttChart solution "powerplant-schedule.txt"
| Error err ->
    printfn "Failed: %s" err.Message
```

**Result:**
- Critical path: SafetyMechanical → InitCooling → StartPump1/StartPump2 → StartTurbine → SyncGrid → FullPower
- Makespan: 140 minutes; the 180-minute deadline is met

`solveQuantum` is not an option for this problem on the local simulator: the six-task chain needs at least 6 slots, so the grid is 9 tasks × 6 slots = 54 qubits, and `LocalBackend` returns an `Error` saying so.

---

## C# Interop

The computation expression builders are F#-only. C# uses the `Scheduling` module in `FSharp.Azure.Quantum`:

| Member | Description |
|--------|-------------|
| `Scheduling.task(id, value, duration)` | Task with a payload and a `TimeSpan` duration |
| `Scheduling.taskWithRequirements(id, value, duration, requirements)` | Same, plus an F# list of `(resourceId, quantity)` tuples |
| `Scheduling.SchedulingBuilder<TTask, TResource>.Create<TTask, TResource>()` | Fluent builder: `.Tasks`, `.Resources`, `.AddDependency`, `.Objective`, `.TimeHorizon`, `.Build()`. `Create` has its own type parameters, which C# cannot infer, so pass them explicitly |
| `Scheduling.SchedulingObjective.MinimizeMakespan` (etc.) | The `Objective` values |
| `Types.Dependency.NewFinishToStart(pred, succ, lag)` | A finish-to-start dependency with a `TimeSpan` lag |
| `TaskSchedulingTypes.solveQuantum(backend, problem)` | The solver; returns `FSharpAsync<FSharpResult<Solution, QuantumError>>` |
| `TaskSchedulingTypes.exportGanttChart(solution, path)` | Gantt chart export |

### Awaiting the Result

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;

var solveTask = FSharpAsync.StartAsTask(
    TaskSchedulingTypes.solveQuantum(backend, problem),
    FSharpOption<TaskCreationOptions>.None,
    FSharpOption<CancellationToken>.None);

var result = await solveTask;
if (result.IsOk)
{
    foreach (var assignment in result.ResultValue.Assignments)
    {
        Console.WriteLine($"{assignment.TaskId}: [{assignment.StartTime} - {assignment.EndTime}]");
    }
}
```

---

## Best Practices

### 1. Use Time Unit Helpers

✅ **Good:**

<!-- fragment -->
```fsharp
duration (hours 2.0)
duration (minutes 30.0)
```

Durations are `TimeSpan` values, so a bare number such as `duration 120.0` does not compile - the unit is always explicit.

### 2. Co-locate Dependencies

✅ **Good:**
```fsharp
let taskB : ScheduledTask<unit> = scheduledTask {
    taskId "TaskB"
    duration (hours 1.0)
    after "TaskA"  // Dependency visible at definition
}
```

### 3. Use Meaningful Task IDs

Prefer `"InitCoolingSystem"`, `"StartPump1"`, `"SafetyElectricalCheck"` over `"Task1"`, `"T2"`, `"X"` - the IDs appear in results, errors and Gantt charts (cut to 12 characters there).

### 4. Set `timeHorizon` for `solveQuantum`

Pick a horizon near the expected makespan (the total duration of the critical path, or of all tasks if they share one resource). The slot grid is built from it; see [How `solveQuantum` works](#how-solvequantum-works).

### 5. Use Deadlines for Time-Critical Tasks

```fsharp
let criticalTask : ScheduledTask<unit> = scheduledTask {
    taskId "EmergencyShutdown"
    duration (minutes 10.0)
    deadline (minutes 60.0)  // Must complete within 60 minutes
}
```

---

## Troubleshooting

### Issue: "Duplicate task IDs found"

**Cause:** Two or more tasks have the same ID.

**Solution:** Ensure all task IDs are unique:

```fsharp
// ❌ Bad
let dup1 : ScheduledTask<unit> = scheduledTask { taskId "Task"; duration (minutes 5.0) }
let dup2 : ScheduledTask<unit> = scheduledTask { taskId "Task"; duration (minutes 5.0) }  // Duplicate!

// ✅ Good
let uniqueA : ScheduledTask<unit> = scheduledTask { taskId "TaskA"; duration (minutes 5.0) }
let uniqueB : ScheduledTask<unit> = scheduledTask { taskId "TaskB"; duration (minutes 5.0) }
```

### Issue: "Invalid task dependencies reference non-existent tasks"

**Cause:** A task depends on a task ID that is not in the problem.

**Solution:** Check all `after` and `afterMultiple` references against the `tasks` list:

```fsharp
// ❌ Bad: TaskX is not part of the problem
let orphan : ScheduledTask<unit> = scheduledTask {
    taskId "TaskB"
    duration (minutes 5.0)
    after "TaskX"
}
```

### Issue: "the schedule needs N qubits ... and (backend) can run M"

**Cause:** tasks × time slots exceeds what the backend can run (see [How `solveQuantum` works](#how-solvequantum-works)).

**Solution:** Split the problem, shorten long dependency chains, use a larger backend, or - when resources don't matter - use `ClassicalSolver.solve`.

### Issue: "No valid solutions found from quantum measurements"

**Cause:** None of the 1000 samples decoded to a schedule that respects every dependency and resource limit. This is more likely with tight horizons and many constraints.

**Solution:** Run again, give the problem a little more room in `timeHorizon`, or reduce the problem size.

### Issue: Tasks overlap on a shared resource

**Cause:** `ClassicalSolver.solve` ignores resource capacities.

**Solution:** Use `solveQuantum`, which rejects samples that overload a resource, or add explicit dependencies to force serialization:

```fsharp
let first : ScheduledTask<unit> = scheduledTask {
    taskId "First"
    duration (hours 1.0)
}

let second : ScheduledTask<unit> = scheduledTask {
    taskId "Second"
    duration (hours 1.0)
    after "First"  // Force serialization
}
```

---

## Limitations

- `solveQuantum` handles small problems only (tasks × slots within the backend's qubit budget; about 20 qubits on `LocalBackend` by default).
- `solveQuantum` ignores `earliestStart` and `priority`; start times fall on slot boundaries.
- `ClassicalSolver.solve` ignores resource capacities.
- `availableWindow` is stored on resources but not used by either solver.
- Circular dependencies are not detected up front.

---

## See Also

- [Getting Started Guide](getting-started) - Installation and first steps
- [API Reference](api-reference) - Complete API documentation
- [Graph Coloring API](GraphColoring-API) - Similar computation expression pattern
- [Computation Expressions Reference](computation-expressions-reference) - All custom operations
- [Working Examples](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/JobScheduling) - Task scheduling examples on GitHub

---

## Support & Feedback

- **GitHub**: [Thorium/FSharp.Azure.Quantum](https://github.com/Thorium/FSharp.Azure.Quantum)
- **Issues**: Report bugs or request features
- **Documentation**: See `docs/` folder for more examples

---

**License**: Unlicense (Public Domain)
