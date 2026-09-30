# Computation Expressions (CE) Reference

This page lists every computation expression (CE) in FSharp.Azure.Quantum with its custom operations. Use it as a quick lookup when IntelliSense does not show the operations of a CE.

## Overview

Computation expressions give a declarative way to describe quantum problems, circuits and schedules. Each CE has its own custom operations. What a CE returns differs between builders, so each section below states the result type:

- **Problem builders** (`constraintSolver`, `patternMatcher`, `quantumTreeSearch`, `graphColoring`, `circuit`) return the problem or circuit and throw an exception if validation fails.
- **Validated problem builders** (`periodFinder`, `phaseEstimator`, `quantumArithmetic`, `linearSystemSolver`) return `Result<Problem, QuantumError>`; pass the `Ok` value to the module's solve function.
- **Run-on-evaluation builders** (the ML builders, `coverageOptimizer`, `resourcePairing`, `packingOptimizer`, `constraintScheduler`, `socialNetwork`, `quantumRiskEngine`, `drugDiscovery`) do the work when the CE is evaluated and return a `QuantumResult<_>` (that is, `Result<_, QuantumError>`).
- **Configuration builders** (`coloredNode`, `resource`, `scheduledTask`, `scheduling`) return the record they build. `quantumChemistry` returns a `ChemistryProblem` and throws if a required operation is missing.
- `optionPricing` returns `Async<QuantumResult<OptionPrice>>`; `topological` returns a program to pass to `TopologicalBuilder.execute`.

## Quick Reference Table

| CE Name | Description | Custom Operations |
|---------|-------------|-------------------|
| **anomalyDetection** | Detect outliers and anomalies in data | `trainOnNormalData`, `sensitivity`, `contaminationRate`, `backend`, `shots`, `verbose`, `saveModelTo`, `note`, `progressReporter`, `cancellationToken` |
| **autoML** | Automated ML - tries several model types and returns the best | `trainWith`, `tryBinaryClassification`, `tryMultiClass`, `tryAnomalyDetection`, `tryRegression`, `trySimilaritySearch`, `tryArchitectures`, `maxTrials`, `maxTimeMinutes`, `validationSplit`, `backend`, `verbose`, `saveModelTo`, `randomSeed`, `progressReporter`, `cancellationToken` |
| **binaryClassification** | Classify items into two categories | `trainWith`, `architecture`, `learningRate`, `maxEpochs`, `convergenceThreshold`, `backend`, `shots`, `verbose`, `saveModelTo`, `note`, `progressReporter`, `cancellationToken` |
| **circuit** | Build quantum circuits gate by gate | `qubits`, `H`, `X`, `Y`, `Z`, `S`, `SDG`, `T`, `TDG`, `P`, `RX`, `RY`, `RZ`, `CNOT`, `CZ`, `CP`, `SWAP`, `RXX`, `RYY`, `RZZ`, `CCX`, `gate`, `Measure`, `Reset`, `Barrier` |
| **coloredNode** | Define a node in a graph coloring problem | `nodeId`, `conflictsWith`, `fixedColor`, `priority`, `avoidColors`, `property` |
| **constraintScheduler** | Task-to-resource scheduling with hard and soft constraints | `task`, `tasks`, `resource`, `resourceWithCapacity`, `conflict`, `require`, `precedence`, `prefer`, `optimizeFor`, `maxBudget`, `backend`, `shots`, `useGrover`, `useQaoa` |
| **constraintSolver<'T>** | Constraint satisfaction problems (CSP) with Grover search | `searchSpace`, `domain`, `satisfies`, `backend`, `maxIterations`, `shots`, `onProgress` |
| **coverageOptimizer** | Set coverage (minimum-cost covering) | `element`, `universeSize`, `option`, `backend`, `shots` |
| **drugDiscovery** | Virtual screening of drug candidates | `load_candidates_from_file`, `load_candidates_from_provider`, `load_candidates_from_provider_async`, `target_protein_from_pdb`, `use_method`, `use_feature_map`, `set_batch_size`, `shots`, `backend`, `vqc_layers`, `vqc_max_epochs`, `selection_budget`, `diversity_weight` |
| **graphColoring** | Graph coloring problems | `node`, `nodes`, `colors`, `maxColors`, `objective`, `conflictPenalty` |
| **linearSystemSolver** | Linear systems Ax = b with the HHL algorithm | `matrix`, `diagonalMatrix`, `vector`, `eigenvalueQubits`, `precision`, `inversionMethod`, `minEigenvalue`, `postSelection`, `backend`, `shots` |
| **optionPricing** | Price options with quantum Monte Carlo (amplitude estimation) | `spotPrice`, `strikePrice`, `riskFreeRate`, `volatility`, `expiry`, `optionType`, `qubits`, `iterations`, `shots`, `backend`, `cancellation_token` |
| **packingOptimizer** | Bin packing (minimize containers) | `item`, `containerCapacity`, `backend`, `shots` |
| **patternMatcher<'T>** | Find items matching a predicate with Grover search | `searchSpace`, `searchSpaceSize`, `matchPattern`, `findTop`, `backend`, `maxIterations`, `shots` |
| **periodFinder** | Period finding (Shor's algorithm) | `number`, `chosenBase`, `precision`, `exactness`, `maxAttempts`, `backend`, `shots` |
| **phaseEstimator** | Quantum phase estimation (QPE) | `unitary`, `precision`, `targetQubits`, `eigenstate`, `applySwaps`, `swaps`, `exactness`, `backend`, `shots` |
| **predictiveModel** | Predict continuous values or categories | `trainWith`, `problemType`, `architecture`, `learningRate`, `maxEpochs`, `convergenceThreshold`, `backend`, `shots`, `verbose`, `saveModelTo`, `note`, `progressReporter`, `cancellationToken` |
| **quantumArithmetic** | Quantum arithmetic operations | `operands`, `operandA`, `operandB`, `operation`, `modulus`, `qubits`, `exponent`, `backend`, `shots` |
| **quantumChemistry** | Ground state energy with VQE or QPE | `molecule`, `basis`, `ansatz`, `groundStateMethod`, `optimizer`, `maxIterations`, `initialParameters`, `integralProvider`, `molecule_from_xyz`, `molecule_from_fcidump`, `molecule_from_provider`, `molecule_from_name` |
| **quantumRiskEngine** | Portfolio risk metrics (VaR, CVaR) | `load_market_data`, `set_confidence_level`, `set_simulation_paths`, `use_amplitude_estimation`, `use_error_mitigation`, `calculate_metric`, `cancellation_token`, `qubits`, `iterations`, `shots`, `backend` |
| **quantumTreeSearch<'T>** | Game-tree and decision-tree search with Grover search | `initialState`, `maxDepth`, `branchingFactor`, `evaluateWith`, `generateMovesWith`, `topPercentile`, `backend`, `shots`, `solutionThreshold`, `successThreshold`, `maxPaths`, `limitSearchSpace`, `maxIterations`, `onProgress` |
| **resource<'T>** | A resource for task scheduling | `resourceId`, `capacity`, `costPerUnit`, `availableWindow` |
| **resourcePairing** | 1:1 pairing that maximizes total compatibility | `participant`, `participants`, `compatibility`, `backend`, `shots` |
| **scheduledTask<'T>** | A task for task scheduling | `taskId`, `duration`, `after`, `afterMultiple`, `requires`, `priority`, `deadline`, `earliestStart` |
| **scheduling<'TTask, 'TResource>** | A complete task scheduling problem | `tasks`, `resources`, `objective`, `timeHorizon` |
| **similaritySearch<'T>** | Find similar items (cosine, Euclidean or quantum kernel) | `indexItems`, `similarityMetric`, `threshold`, `backend`, `shots`, `verbose`, `saveIndexTo`, `note`, `progressReporter`, `cancellationToken` |
| **socialNetwork** | Communities, monitor sets and pairings in a network | `person`, `people`, `connection`, `connections`, `findCommunities`, `findLargestCommunity`, `findMonitorSet`, `findPairings`, `backend`, `useGrover`, `useQaoa`, `shots` |
| **topological** | Topological programs with anyon braiding | No custom operations: `let!`/`do!` with `TopologicalBuilder.initialize`, `braid`, `measure`, `braidSequence`, `getState`, `getResults`, `getLog`, `getContext` |

---

## Detailed Documentation

The examples on this page share these opens and a local simulator backend:

```fsharp
open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

let localBackend = LocalBackend.LocalBackend() :> IQuantumBackend
```

The local simulator's qubit limit is derived from available memory and capped at 30 qubits. Most builders below apply tighter limits of their own, stated in each section.

### 1. circuit

**Module**: `FSharp.Azure.Quantum.CircuitBuilder`

**Purpose**: Build quantum circuits declaratively. The result is a `CircuitBuilder.Circuit`; the builder validates the circuit when the CE finishes and throws an exception if it is invalid (for example a gate on a qubit index outside `qubits`).

**Example**:
```fsharp
open FSharp.Azure.Quantum.CircuitBuilder

let bellState = circuit {
    qubits 2
    H 0
    CNOT (0, 1)
}
```

**Custom Operations**:
- `qubits n` - Number of qubits (set it before the gates)
- **Single-qubit gates**: `H`, `X`, `Y`, `Z`, `S`, `SDG`, `T`, `TDG` take a qubit index; `P` takes a qubit and an angle
- **Rotation gates**: `RX`, `RY`, `RZ` take a qubit and an angle
- **Two-qubit gates**: `CNOT`, `CZ`, `SWAP` take two qubits; `CP` takes two qubits and an angle
- **Ising interaction gates**: `RXX`, `RYY`, `RZZ` take two qubits and an angle
- **Three-qubit gate**: `CCX` (Toffoli) takes two controls and a target
- `gate g` - Add any `CircuitBuilder.Gate` value (for gates without their own operation, such as `U3`, `CRX` or `MCZ`)
- `Measure q`, `Reset q`, `Barrier [qubits]`

Multi-argument gates accept either a tuple (`CNOT (0, 1)`) or separate arguments (`CNOT 0 1`).

**Loops**: custom operations cannot use a `for` loop variable. Inside a loop, use `yield!` with the `singleGate` or `multiGate` helpers (see [For Loops in CEs](#for-loops-in-ces) and [Computation Expression Composition](computation-expression-composition.md)).

---

### 2. coloredNode

**Module**: `FSharp.Azure.Quantum.GraphColoring`

**Purpose**: Define one node of a graph coloring problem. Returns a `ColoredNode`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.GraphColoring

let node1 = coloredNode {
    nodeId "R1"
    conflictsWith ["R2"; "R3"]
    priority 1.0
}
```

**Custom Operations**:
- `nodeId` - Unique identifier for the node
- `conflictsWith` - Node IDs that must not get the same color
- `fixedColor` - Pre-assign a color
- `priority` - Priority for tie-breaking (higher = assigned first; default 0.0)
- `avoidColors` - Colors to avoid if possible (soft constraint)
- `property key value` - Add a metadata entry (`value` is `obj`)

---

### 3. graphColoring

**Module**: `FSharp.Azure.Quantum.GraphColoring`

**Purpose**: Define a complete graph coloring problem. Returns a `GraphColoringProblem` (validated; invalid problems throw). Solve it with `GraphColoring.solve problem numColors backendOption`.

**Example**:
```fsharp
let coloring = graphColoring {
    node "R1" ["R2"]
    node "R2" ["R1"; "R3"]
    nodes [ coloredNode { nodeId "R3"; conflictsWith ["R2"] } ]
    colors ["EAX"; "EBX"; "ECX"]
    objective MinimizeColors
}

match GraphColoring.solve coloring 3 None with
| Ok solution -> printfn "Colors used: %d, valid: %b" solution.ColorsUsed solution.IsValid
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `node id conflicts` - Add a node from its ID and the IDs it conflicts with
- `nodes` - Add a list of `ColoredNode` values (built with `coloredNode`)
- `colors` - Available colors
- `maxColors` - Use only the first `maxColors` colors
- `objective` - `MinimizeColors` (default) | `MinimizeConflicts` | `BalanceColors`
- `conflictPenalty` - Multiplier on the conflict penalty (positive; default 1.0)

---

### 4. constraintSolver<'T>

**Module**: `FSharp.Azure.Quantum.QuantumConstraintSolver`

**Purpose**: Find an assignment of values to variables that satisfies all constraints, using Grover search. Returns a `ConstraintProblem<'T>` (validated; invalid problems throw). Solve it with `QuantumConstraintSolver.solve`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.QuantumConstraintSolver

let allDifferent (assignment: Map<int, int>) =
    let values = assignment |> Map.toList |> List.map snd
    List.length (List.distinct values) = List.length values

let rowProblem = constraintSolver<int> {
    searchSpace 4   // 4 variables (4 × log2(4) = 8 qubits)
    domain [1..4]
    satisfies allDifferent
    shots 1000
}

match QuantumConstraintSolver.solve rowProblem with
| Ok solution -> printfn "Assignment: %A" solution.Assignment
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `searchSpace` - Number of variables; each takes one value from `domain`. numVariables × log2(domainSize) must be ≤ 16 qubits
- `domain` - Values each variable can take
- `satisfies` - Add a constraint `Map<int, 'T> -> bool` (keys are variable indices 0..n-1); use it several times to add several constraints, all must hold
- `backend` - Quantum backend (default: LocalBackend)
- `maxIterations` - Grover iterations (default: calculated from the search space)
- `shots` - Measurement shots (default: 1000)
- `onProgress` - Progress reporter (`Progress.IProgressReporter`)

**Notes**:
- At least one `satisfies` is required.
- The oracle evaluates your predicates; on the local simulator every candidate assignment is evaluated, so there is no speedup over classical enumeration there.

---

### 5. patternMatcher<'T>

**Module**: `FSharp.Azure.Quantum.QuantumPatternMatcher`

**Purpose**: Find items that match a predicate, using Grover search. Returns a `PatternProblem<'T>` (validated; invalid problems throw). Solve it with `QuantumPatternMatcher.solve`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.QuantumPatternMatcher

type Config = { Name: string; Performance: float; Cost: float }

let allConfigs =
    [ for i in 0 .. 15 -> { Name = $"cfg{i}"; Performance = float i / 16.0; Cost = float (i * 10) } ]

let search = patternMatcher<Config> {
    searchSpace allConfigs
    matchPattern (fun cfg -> cfg.Performance > 0.8 && cfg.Cost < 150.0)
    findTop 2
    shots 500
}

match QuantumPatternMatcher.solve search with
| Ok solution -> printfn "Matches: %A" solution.Matches
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `searchSpace` - A `'T list` of items to search, or an `int` search space size
- `searchSpaceSize` - Search space size as an integer (items are then the indices)
- `matchPattern` - Predicate `'T -> bool`; a later `matchPattern` replaces an earlier one, so combine conditions in one predicate
- `findTop` - Number of matches to return (default: 1)
- `backend` - Quantum backend (default: LocalBackend)
- `maxIterations` - Grover iterations (default: calculated)
- `shots` - Measurement shots (default: 1000)

**Notes**: the search space may hold at most 2^16 items.

---

### 6. periodFinder

**Module**: `FSharp.Azure.Quantum.QuantumPeriodFinder`

**Purpose**: Period finding for integer factorization (Shor's algorithm). Returns `Result<PeriodFinderProblem, QuantumError>`; solve the `Ok` value with `QuantumPeriodFinder.solve`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.QuantumPeriodFinder

let shorProblem = periodFinder {
    number 15
    precision 8
    maxAttempts 10
}

match shorProblem |> Result.bind QuantumPeriodFinder.solve with
| Ok result -> printfn "Period: %d, factors: %A" result.Period result.Factors
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `number` - Number to factor (4 to 10000; default 15)
- `chosenBase` - Base a with 2 ≤ a < N. If not set, a base is chosen automatically
- `precision` - QPE precision qubits (1 to 20; default 8; recommended 2·log₂(N) + 3)
- `exactness` - `QPE.Exact` (default) or `QPE.Approximate epsilon`
- `maxAttempts` - Maximum attempts (1 to 100; default 10)
- `backend` - Quantum backend (default: LocalBackend)
- `shots` - Accepted but **not used**: each attempt reads one measurement and turns it into a period candidate; raise `maxAttempts` for more samples

**Notes**:
- Uses quantum phase estimation internally.
- Higher `precision` gives a better phase estimate but needs more qubits.

---

### 7. phaseEstimator

**Module**: `FSharp.Azure.Quantum.QuantumPhaseEstimator` (unitaries such as `TGate` are in `FSharp.Azure.Quantum.Algorithms.QPE`)

**Purpose**: Estimate the phase φ in U|ψ⟩ = e^(2πiφ)|ψ⟩ with quantum phase estimation. Returns `Result<PhaseEstimatorProblem, QuantumError>`; run the `Ok` value with `QuantumPhaseEstimator.estimate`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.Algorithms.QPE
open FSharp.Azure.Quantum.QuantumPhaseEstimator

let qpeProblem = phaseEstimator {
    unitary TGate
    precision 8
    targetQubits 1
    shots 1024
}

match qpeProblem |> Result.bind estimate with
| Ok result -> printfn "Phase: %.6f (expected 0.125)" result.Phase
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `unitary` - Unitary operator: `TGate`, `SGate`, `PhaseGate theta`, `RotationZ theta` (default: `TGate`)
- `precision` - Counting qubits, i.e. bits of φ (1 to 20; default 8)
- `targetQubits` - Qubits for the eigenvector |ψ⟩ (1 to 10; default 1). Precision + target qubits must not exceed 25
- `eigenstate` - Initial eigenvector |ψ⟩ as a `StateVector`. When it is not set, the target qubit is prepared in |1⟩ for the single-qubit gates `TGate`, `SGate`, `PhaseGate` and `RotationZ` (so `TGate` gives φ = 1/8, `PhaseGate θ` gives θ/2π and `RotationZ θ` gives θ/4π)
- `applySwaps` / `swaps` - Apply the final bit-reversal SWAPs in the circuit (default: false, the bit order is fixed classically)
- `exactness` - `Exact` (default) or `Approximate epsilon`
- `backend` - Quantum backend (default: LocalBackend)
- `shots` - Measurement shots; the estimate is the most frequent outcome. Default: 1024 on LocalBackend, 2048 on any other backend

**Notes**:
- Higher `precision` gives a finer phase estimate; more `shots` makes the most frequent outcome more reliable.
- QPE is also used inside period finding and HHL.

---

### 8. quantumArithmetic

**Module**: `FSharp.Azure.Quantum.QuantumArithmeticOps`

**Purpose**: Run arithmetic (addition, multiplication, modular operations) as quantum circuits. Returns `Result<ArithmeticOperation, QuantumError>`; run the `Ok` value with `QuantumArithmeticOps.execute`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.QuantumArithmeticOps

let addition = quantumArithmetic {
    operands 42 17
    operation Add
    qubits 8
    shots 100
}

match addition |> Result.bind execute with
| Ok result -> printfn "42 + 17 = %d" result.Value
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `operands a b` - Set both operands
- `operandA` - Set the first operand
- `operandB` - Set the second operand (the exponent for `ModularExponentiate`)
- `operation` - `Add` (default) | `Multiply` | `ModularAdd` | `ModularMultiply` | `ModularExponentiate`
- `modulus` - Modulus (required for the modular operations; operands must be smaller than it for `ModularAdd` and `ModularMultiply`)
- `qubits` - Register size in qubits (default 8, minimum 2); modular operations use extra ancilla qubits (`ModularAdd` n + 2, `ModularMultiply` 2n + 3, `ModularExponentiate` 2n + 5), and the total must stay within the simulator's practical circuit width (20 qubits by default; the `FSAQ_MAX_CIRCUIT_QUBITS` environment variable raises it, up to the memory-based limit)
- `exponent` - Same as `operandB`
- `backend` - Quantum backend (default: LocalBackend)
- `shots` - Measurement shots used to read the result register (default: 100)

**Notes**: the result is deterministic on a noiseless simulator; standalone quantum arithmetic is slower than CPU arithmetic and is mainly a building block (for example for Shor's algorithm).

---

### 9. quantumTreeSearch<'T>

**Module**: `FSharp.Azure.Quantum.QuantumTreeSearch`

**Purpose**: Search game trees and decision trees with Grover search. Returns a `TreeSearchProblem<'T>` (validated; invalid problems throw). Solve it with `QuantumTreeSearch.solve`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.QuantumTreeSearch

// Toy game: the state is a running total, each move adds 1..4
let evaluateTotal (total: int) = float (total % 7)
let legalMoves (total: int) = [ for step in 1 .. 4 -> total + step ]

let treeSearch = quantumTreeSearch<int> {
    initialState 0
    maxDepth 2
    branchingFactor 4
    evaluateWith evaluateTotal
    generateMovesWith legalMoves
    topPercentile 0.2
    shots 100
}

match QuantumTreeSearch.solve treeSearch with
| Ok solution -> printfn "Best move: %d (score %.3f)" solution.BestMove solution.Score
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `initialState` - Starting state (required)
- `maxDepth` - Depth to explore (1 to 8; default 3)
- `branchingFactor` - Moves per position (2 to 256; default 16). The tree needs maxDepth × ⌈log₂(branchingFactor)⌉ qubits, at most 16
- `evaluateWith` - Evaluation function `'T -> float` (higher = better)
- `generateMovesWith` - Move generator `'T -> 'T list`
- `topPercentile` - Fraction of best paths to amplify, in (0.0, 1.0] (default 0.2)
- `backend` - Quantum backend (default: LocalBackend)
- `shots` - Measurements (default: 50 on LocalBackend, 250 on other backends)
- `solutionThreshold` - Minimum fraction of shots for a state to count as a solution (default: 5%)
- `successThreshold` - Minimum total probability for the search to succeed (default: 50% on LocalBackend, 60% on other backends)
- `maxPaths` - Limit on the number of paths searched (default: full tree)
- `limitSearchSpace` - `true` sets `maxPaths` to a recommended limit for the current `maxDepth` and `branchingFactor`, so put it after those two
- `maxIterations` - Stored, but the current solver does not pass it on: Grover iterations are always calculated from the search space size
- `onProgress` - Progress reporter

**Notes**: the oracle evaluates your evaluation function; on the local simulator every path is evaluated, so the local run gives no speedup over classical search.

---

### 10. linearSystemSolver

**Module**: `FSharp.Azure.Quantum.QuantumLinearSystemSolver`

**Purpose**: Solve Ax = b with the HHL algorithm. Returns `Result<LinearSystemProblem, QuantumError>`; solve the `Ok` value with `QuantumLinearSystemSolver.solve`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.QuantumLinearSystemSolver

let linearProblem = linearSystemSolver {
    matrix [ [ 2.0; 0.0 ]; [ 0.0; 1.0 ] ]
    vector [ 1.0; 1.0 ]
    eigenvalueQubits 4
}

match linearProblem |> Result.bind QuantumLinearSystemSolver.solve with
| Ok solution -> printfn "Success probability: %.4f" solution.SuccessProbability
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `matrix` - Hermitian matrix A as rows (`float list list`); throws if it is not square or not Hermitian
- `diagonalMatrix` - A diagonal matrix from its eigenvalues
- `vector` - Right-hand side b (normalized internally)
- `eigenvalueQubits` / `precision` - Qubits for eigenvalue estimation (default 4)
- `inversionMethod` - `ExactRotation c` (default `ExactRotation 1.0`), `LinearApproximation c` or `PiecewiseLinear segments`
- `minEigenvalue` - Eigenvalues below this are treated as zero (default 1e-6)
- `postSelection` - Post-select on the ancilla qubit (default true)
- `backend` - Quantum backend (default: LocalBackend)
- `shots` - Accepted but **not used**: the solver reads the solution and its success probability from the state vector exactly

---

### 11. resource<'T>

**Module**: `FSharp.Azure.Quantum.TaskScheduling.Builders` (also re-exported from `FSharp.Azure.Quantum`)

**Purpose**: Define a resource for task scheduling. Returns a `Resource<'T>`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.TaskScheduling

let cpu = resource<string> {
    resourceId "CPU"
    capacity 4.0
    costPerUnit 10.0
    availableWindow 0.0 100.0
}
```

**Custom Operations**:
- `resourceId` - Unique identifier (required)
- `capacity` - Capacity available (required)
- `costPerUnit` - Cost per unit per time unit
- `availableWindow start finish` - Time window when the resource is available

The `crew id capacity costPerUnit` function is a shortcut for a `Resource<string>`.

---

### 12. scheduledTask<'T>

**Module**: `FSharp.Azure.Quantum.TaskScheduling.Builders` (also re-exported from `FSharp.Azure.Quantum`)

**Purpose**: Define a task for scheduling. Returns a `ScheduledTask<'T>`. Durations and times are `TimeSpan` values; build them with `minutes`, `hours` or `days`.

**Example**:
```fsharp
let task1 = scheduledTask<string> {
    taskId "Task1"
    duration (hours 2.0)
    requires "CPU" 2.0
    priority 1.0
    deadline (hours 8.0)
}

let task2 = scheduledTask<string> {
    taskId "Task2"
    duration (minutes 30.0)
    after "Task1"
}
```

**Custom Operations**:
- `taskId` - Unique identifier
- `duration` - Task duration (`TimeSpan`)
- `after` - Must start after the named task finishes
- `afterMultiple` - Must start after all the named tasks finish
- `requires resourceId quantity` - Resource requirement
- `priority` - Priority for tie-breaking (higher = scheduled first)
- `deadline` - Latest completion time (`TimeSpan` from schedule start)
- `earliestStart` - Earliest start time (`TimeSpan` from schedule start)

---

### 13. scheduling<'TTask, 'TResource>

**Module**: `FSharp.Azure.Quantum.TaskScheduling.Builders` (also re-exported from `FSharp.Azure.Quantum`)

**Purpose**: Define a complete scheduling problem. Returns a `SchedulingProblem<'TTask, 'TResource>`; solve it with `solveQuantum backend problem` (returns `Async<QuantumResult<Solution>>`).

**Example**:
```fsharp
let schedulingProblem = scheduling<string, string> {
    tasks [task1; task2]
    resources [cpu]
    objective MinimizeMakespan
    timeHorizon (hours 12.0)
}

match solveQuantum localBackend schedulingProblem |> Async.RunSynchronously with
| Ok solution -> printfn "Makespan: %O" solution.Makespan
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `tasks` - Tasks to schedule (dependencies declared with `after` are picked up here)
- `resources` - Available resources
- `objective` - `MinimizeMakespan` (default) | `MinimizeCost` | `MaximizeResourceUtilization` | `MinimizeLateness`
- `timeHorizon` - Total time available (`TimeSpan`; default 1000 minutes)

---

### 14. constraintScheduler

**Module**: `FSharp.Azure.Quantum.Business.ConstraintScheduler`

**Purpose**: Assign tasks to resources under hard constraints (conflicts, required resources, precedence) and weighted preferences. Solves when evaluated and returns `QuantumResult<SchedulingResult>`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.Business.ConstraintScheduler

let shiftPlan = constraintScheduler {
    tasks ["Morning"; "Evening"]
    resource "Alice" 20.0
    resource "Bob" 25.0
    conflict "Morning" "Evening"
    prefer "Morning" "Alice" 1.0
    optimizeFor MinimizeCost
}

match shiftPlan with
| Ok result -> printfn "%s" result.Message
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `task` / `tasks` - Add one task / several tasks by ID
- `resource id cost` - Add a resource with a cost
- `resourceWithCapacity id cost capacity` - Add a resource with a limit on concurrent tasks
- `conflict task1 task2` - The two tasks must not share a resource
- `require task resource` - The task must use the resource
- `precedence before after` - Ordering constraint
- `prefer task resource weight` - Soft preference
- `optimizeFor` - `MinimizeCost` | `MaximizeSatisfaction` | `Balanced` (default)
- `maxBudget` - Budget limit
- `backend` - Quantum backend (default: LocalBackend)
- `shots` - Measurement shots (default: 1000)
- `useGrover` / `useQaoa` - Force the algorithm (default: chosen automatically)

---

### 15. anomalyDetection

**Module**: `FSharp.Azure.Quantum.Business.AnomalyDetector`

**Purpose**: Detect outliers with a quantum-kernel detector trained on normal examples only. Trains when evaluated and returns `QuantumResult<Detector>`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.AnomalyDetector

let normalTransactions : float[][] =
    [| [| 0.10; 0.20 |]; [| 0.15; 0.22 |]; [| 0.12; 0.18 |]; [| 0.11; 0.21 |] |]

let suspiciousTransaction = [| 0.90; 0.95 |]

let detector = anomalyDetection {
    trainOnNormalData normalTransactions
    sensitivity High
    contaminationRate 0.1
    shots 1000
}

match detector with
| Ok model ->
    match AnomalyDetector.check suspiciousTransaction model with
    | Ok result when result.IsAnomaly -> printfn "Anomaly detected. Score: %.2f" result.AnomalyScore
    | Ok _ -> printfn "Looks normal"
    | Error err -> printfn "Check failed: %s" err.Message
| Error err -> printfn "Training failed: %s" err.Message
```

**Custom Operations**:
- `trainOnNormalData` - Training data (`float[][]`, normal examples only)
- `sensitivity` - `Low` | `Medium` (default) | `High` | `VeryHigh`
- `contaminationRate` - Expected fraction of anomalies in the training data, 0.0 to 0.5 (default: 0.05)
- `backend` - Quantum backend (default: LocalBackend)
- `shots` - Measurement shots (default: 1000)
- `verbose` - Verbose logging (default: false)
- `saveModelTo` - Path to save the trained detector
- `note` - Note stored with the model
- `progressReporter` - Progress reporter (default: none)
- `cancellationToken` - Cancellation token (default: none)

Other functions: `AnomalyDetector.checkBatch`, `explain`, `save`, `load`.

**Use Cases**: fraud detection, security monitoring, quality control, system monitoring.

---

### 16. autoML

**Module**: `FSharp.Azure.Quantum.Business.AutoML`

**Purpose**: Try several model types, architectures and hyperparameters and return the best model. Runs the search when evaluated and returns `QuantumResult<AutoMLResult>`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.Business.AutoML

let features : float[][] =
    [| [| 0.1; 0.2 |]; [| 0.9; 0.8 |]; [| 0.2; 0.1 |]; [| 0.8; 0.9 |]; [| 0.15; 0.25 |]; [| 0.85; 0.75 |] |]

let labels = [| 0.0; 1.0; 0.0; 1.0; 0.0; 1.0 |]

let autoMLResult = autoML {
    trainWith features labels

    // Search space
    tryBinaryClassification true
    tryRegression false
    tryAnomalyDetection false
    tryArchitectures [Quantum; Hybrid]

    // Search budget
    maxTrials 10
    maxTimeMinutes 10

    verbose true
}

match autoMLResult with
| Ok best ->
    printfn "Best model: %s (score %.2f)" best.BestModelType best.Score
    match AutoML.predict [| 0.2; 0.2 |] best with
    | Ok prediction -> printfn "Prediction: %A" prediction
    | Error err -> printfn "Prediction failed: %s" err.Message
| Error err -> printfn "AutoML failed: %s" err.Message
```

**Custom Operations**:
- `trainWith features labels` - Features (`float[][]`) and labels (`float[]`)
- `tryBinaryClassification` - Include binary classification (default: true)
- `tryMultiClass n` - Include multi-class classification with n classes (default: detected from the labels)
- `tryAnomalyDetection` - Include anomaly detection (default: true)
- `tryRegression` - Include regression (default: true)
- `trySimilaritySearch` - Include similarity search (default: false)
- `tryArchitectures` - Architectures to try (default: `[Quantum; Hybrid]`)
- `maxTrials` - Maximum number of trials (default: 20)
- `maxTimeMinutes` - Time limit in minutes (default: none)
- `validationSplit` - Fraction held out for validation (default: 0.2)
- `backend` - Quantum backend (default: LocalBackend)
- `verbose` - Verbose logging (default: false)
- `saveModelTo` - Path to save the best model
- `randomSeed` - Random seed for reproducibility (default: none)
- `progressReporter` - Progress reporter (default: none)
- `cancellationToken` - Cancellation token (default: none)

---

### 17. binaryClassification

**Module**: `FSharp.Azure.Quantum.Business.BinaryClassifier`

**Purpose**: Classify items into two categories. Trains when evaluated and returns `QuantumResult<Classifier>`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.Business.BinaryClassifier

let trainFeatures = features
let trainLabels = [| 0; 1; 0; 1; 0; 1 |]

let classifier = binaryClassification {
    trainWith trainFeatures trainLabels
    architecture Quantum
    learningRate 0.01
    maxEpochs 100
    shots 1000
}

match classifier with
| Ok model ->
    match BinaryClassifier.predict [| 0.9; 0.9 |] model with
    | Ok prediction when prediction.IsPositive -> printfn "Positive (confidence %.2f)" prediction.Confidence
    | Ok _ -> printfn "Negative"
    | Error err -> printfn "Prediction failed: %s" err.Message
| Error err -> printfn "Training failed: %s" err.Message
```

**Custom Operations**:
- `trainWith features labels` - Features (`float[][]`) and labels (`int[]`, 0 or 1)
- `architecture` - `Quantum` (default) | `Hybrid` | `Classical`
- `learningRate` - Learning rate (default: 0.01)
- `maxEpochs` - Maximum training epochs (default: 100)
- `convergenceThreshold` - Convergence threshold (default: 0.001)
- `backend` - Quantum backend (default: LocalBackend)
- `shots` - Measurement shots (default: 1000)
- `verbose` - Verbose logging (default: false)
- `saveModelTo` - Path to save the trained model
- `note` - Note stored with the model
- `progressReporter` - Progress reporter (default: none)
- `cancellationToken` - Cancellation token (default: none)

Other functions: `BinaryClassifier.evaluate`, `save`, `load`.

**Use Cases**: fraud detection, spam filtering, churn prediction (yes/no), credit approval, pass/fail quality control.

---

### 18. predictiveModel

**Module**: `FSharp.Azure.Quantum.Business.PredictiveModel`

**Purpose**: Predict continuous values (regression) or categories (multi-class classification). Trains when evaluated and returns `QuantumResult<Model>`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.Business.PredictiveModel

let customerFeatures = features
let revenueTargets = [| 10.0; 42.0; 12.0; 40.0; 11.0; 39.0 |]
let churnLabels = [| 0.0; 1.0; 0.0; 2.0; 0.0; 3.0 |] // 0=Stay, 1=Churn30, 2=Churn60, 3=Churn90

// Regression: predict revenue
let revenueModel = predictiveModel {
    trainWith customerFeatures revenueTargets
    problemType Regression
    learningRate 0.01
    maxEpochs 100
}

// Multi-class: predict churn timing
let churnModel = predictiveModel {
    trainWith customerFeatures churnLabels
    problemType (MultiClass 4)
    architecture Quantum
    shots 1000
}

match churnModel with
| Ok model ->
    match PredictiveModel.predictCategory [| 0.8; 0.9 |] model None None with
    | Ok prediction when prediction.Category = 0 -> printfn "Customer will stay"
    | Ok prediction -> printfn "Churn risk, category %d" prediction.Category
    | Error err -> printfn "Prediction failed: %s" err.Message
| Error err -> printfn "Training failed: %s" err.Message
```

`PredictiveModel.predict features model backend shots` returns a regression prediction; `predictCategory` returns a category. Pass `None` for the backend and shots to use the model's own settings.

**Custom Operations**:
- `trainWith features targets` - Features (`float[][]`) and targets (`float[]`; class indices for multi-class)
- `problemType` - `Regression` (default) | `MultiClass n`
- `architecture` - `Quantum` (default) | `Hybrid` | `Classical`
- `learningRate` - Learning rate (default: 0.01)
- `maxEpochs` - Maximum training epochs (default: 100)
- `convergenceThreshold` - Convergence threshold (default: 0.001)
- `backend` - Quantum backend (default: LocalBackend)
- `shots` - Measurement shots (default: 1000)
- `verbose` - Verbose logging (default: false)
- `saveModelTo` - Path to save the trained model
- `note` - Note stored with the model
- `progressReporter` - Progress reporter (default: none)
- `cancellationToken` - Cancellation token (default: none)

**Use Cases**:
- **Regression**: revenue forecasting, demand prediction, customer lifetime value
- **Multi-class**: churn timing, customer segmentation, risk levels

---

### 19. similaritySearch<'T>

**Module**: `FSharp.Azure.Quantum.Business.SimilaritySearch`

**Purpose**: Index items by feature vectors and find similar items. Builds the index when evaluated and returns `QuantumResult<SearchIndex<'T>>`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.Business.SimilaritySearch

type Product = { Sku: string }

let productCatalog : (Product * float[])[] =
    [| { Sku = "A" }, [| 0.9; 0.1 |]
       { Sku = "B" }, [| 0.8; 0.2 |]
       { Sku = "C" }, [| 0.1; 0.9 |] |]

let finder = similaritySearch<Product> {
    indexItems productCatalog
    similarityMetric Cosine
    threshold 0.7
    shots 1000
}

match finder with
| Ok index ->
    match SimilaritySearch.findSimilar { Sku = "query" } [| 0.85; 0.15 |] 2 index with
    | Ok results ->
        for m in results.Matches do
            printfn "  %s: %.2f similar" m.Item.Sku m.Similarity
    | Error err -> printfn "Search failed: %s" err.Message
| Error err -> printfn "Indexing failed: %s" err.Message
```

**Custom Operations**:
- `indexItems` - Items to index (`('T * float[])[]`)
- `similarityMetric` - `Cosine` (default) | `Euclidean` | `QuantumKernel`
- `threshold` - Minimum similarity (default: 0.7)
- `backend` - Quantum backend (default: LocalBackend)
- `shots` - Measurement shots (default: 1000)
- `verbose` - Verbose logging (default: false)
- `saveIndexTo` - Path to save the index
- `note` - Note stored with the index
- `progressReporter` - Progress reporter (default: none)
- `cancellationToken` - Cancellation token (default: none)

Other functions: `SimilaritySearch.findAllSimilar`, `findDuplicates`, `cluster`, `save`, `load`.

**Use Cases**: product recommendations, duplicate detection, content similarity, clustering.

---

### 20. optionPricing

**Module**: `FSharp.Azure.Quantum.Business.OptionPricing`

**Purpose**: Price European and Asian options with quantum Monte Carlo (amplitude estimation). Returns `Async<QuantumResult<OptionPrice>>`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.Business.OptionPricing

let pricing = optionPricing {
    spotPrice 100.0
    strikePrice 105.0
    riskFreeRate 0.05
    volatility 0.2
    expiry 1.0
    optionType EuropeanCall
    qubits 6
    iterations 5
    shots 1000
    backend localBackend
}

match pricing |> Async.RunSynchronously with
| Ok price -> printfn "Option price: %.4f (±%.4f)" price.Price price.ConfidenceInterval
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `spotPrice` - Spot price of the underlying (S₀; default 100.0)
- `strikePrice` - Strike price (K; default 100.0)
- `riskFreeRate` - Annual risk-free rate (r; default 0.05)
- `volatility` - Annual volatility (σ; default 0.2)
- `expiry` - Time to expiry in years (T; default 1.0)
- `optionType` - `EuropeanCall` (default), `EuropeanPut`, `AsianCall n`, `AsianPut n`
- `qubits` - Qubits for price discretization (2 to 10; default 6)
- `iterations` - Grover iterations for amplitude estimation (0 to 100; default 5)
- `shots` - Measurement shots (default 1000)
- `backend` - Quantum backend (**required**; without it the result is a validation error)
- `cancellation_token` - Cancellation token

**Option Types**:
- `EuropeanCall` - max(S_T - K, 0)
- `EuropeanPut` - max(K - S_T, 0)
- `AsianCall n` - average-price call with n monitoring dates (priced with the geometric-average approximation)
- `AsianPut n` - average-price put with n monitoring dates (geometric-average approximation)

**Notes**:
- Uses Möttönen state preparation to encode the price distribution.
- The price is the maximum-likelihood amplitude estimate (`QuantumMonteCarlo.estimateBoundedExpectation`) over Grover powers 0, 1, 2, 4, … up to `iterations`, and the interval is 1.96 × its standard error. On a cloud backend every power is one whole-circuit job and its probability comes from the job's counts; the method name then says "whole circuits sampled at N shots".
- In theory amplitude estimation needs O(1/ε) oracle queries for accuracy ε, where classical Monte Carlo needs O(1/ε²) samples. The local simulator does not show that advantage; the `Speedup` field of the result is the theoretical factor, not a measured one.

**Greeks Calculation**:
```fsharp
// Option sensitivities (Delta, Gamma, Vega, Theta, Rho)
match OptionPricing.greeksEuropeanCall 100.0 105.0 0.05 0.2 1.0 localBackend |> Async.RunSynchronously with
| Ok greeks -> printfn "Delta %.4f, Gamma %.4f, Vega %.4f" greeks.Delta greeks.Gamma greeks.Vega
| Error err -> printfn "Error: %s" err.Message
```

---

### 21. quantumRiskEngine

**Module**: `FSharp.Azure.Quantum.Business` (the builder is auto-opened)

**Purpose**: Portfolio risk metrics. Runs synchronously when evaluated and returns `QuantumResult<RiskReport>`.

**Example**:
```fsharp
let riskReport = quantumRiskEngine {
    load_market_data "returns.csv"
    set_confidence_level 0.99
    calculate_metric ValueAtRisk
    calculate_metric ConditionalVaR
    use_amplitude_estimation true
    backend localBackend
}

match riskReport with
| Ok report -> printfn "VaR: %A, CVaR: %A" report.VaR report.CVaR
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `load_market_data` - Path to a file of returns. Without it the engine uses generated sample returns
- `set_confidence_level` - Confidence level (default 0.95)
- `set_simulation_paths` - Number of simulation paths (default 10000)
- `use_amplitude_estimation` - Use quantum amplitude estimation (default false); each metric is estimated with `QuantumMonteCarlo.estimateBoundedExpectation`, as whole-circuit jobs on a cloud backend
- `use_error_mitigation` - Enable error mitigation (default false)
- `calculate_metric` - Add a metric: `ValueAtRisk` | `ConditionalVaR` | `ExpectedShortfall` | `Volatility`
- `cancellation_token` - Cancellation token
- `qubits` - Qubits for amplitude estimation (default 5)
- `iterations` - Grover iterations (default 2)
- `shots` - Measurement shots (default 100)
- `backend` - Quantum backend (default: LocalBackend)

---

### 22. drugDiscovery

**Module**: `FSharp.Azure.Quantum.Business` (the builder is auto-opened)

**Purpose**: Virtual screening of candidate molecules. Runs when evaluated and returns `QuantumResult<ScreeningResult>`.

**Example**:
```fsharp
let screening = drugDiscovery {
    load_candidates_from_file "candidates.sdf"
    use_method QuantumKernelSVM
    use_feature_map ZZFeatureMap
    set_batch_size 10
    shots 100
}

match screening with
| Ok result -> printfn "%s (%d molecules)" result.Message result.MoleculesProcessed
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `target_protein_from_pdb` - Target protein from a PDB file
- `load_candidates_from_file` - Candidate molecules from a file
- `load_candidates_from_provider` / `load_candidates_from_provider_async` - Candidates from a dataset provider
- `use_method` - `QuantumKernelSVM` (default) | `VQCClassifier` | `QAOADiverseSelection`
- `use_feature_map` - `ZZFeatureMap` (default) | `PauliFeatureMap` | `ZFeatureMap`
- `set_batch_size` - Batch size (default 10)
- `shots` - Measurement shots (default 100)
- `backend` - Quantum backend (default: LocalBackend)
- `vqc_layers` - Layers for `VQCClassifier` (default 2)
- `vqc_max_epochs` - Epochs for `VQCClassifier` (default 50)
- `selection_budget` - Budget for `QAOADiverseSelection` (default 10.0)
- `diversity_weight` - Diversity weight for `QAOADiverseSelection` (default 0.5)

---

### 23. topological

**Module**: `FSharp.Azure.Quantum.Topological` (`topological` is auto-opened; the operations are in `TopologicalBuilder`)

**Purpose**: Compose topological programs from anyon operations. `topological backend { ... }` builds a program; `TopologicalBuilder.execute backend program` runs it and returns `Task<Result<_, QuantumError>>`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.Topological

let isingBackend = TopologicalUnifiedBackendFactory.createIsing 10

let program = topological isingBackend {
    do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
    do! TopologicalBuilder.braid 0
    do! TopologicalBuilder.braid 2
    let! outcome = TopologicalBuilder.measure 0
    return outcome
}

match (TopologicalBuilder.execute isingBackend program).Result with
| Ok particle -> printfn "Measured: %A" particle
| Error err -> printfn "Error: %s" err.Message
```

**Builder functions** (use with `do!` for operations and `let!` for values):
- `TopologicalBuilder.initialize anyonType count` - Initialize anyons
- `TopologicalBuilder.braid index` - Braid the anyons at `index` and `index + 1`
- `TopologicalBuilder.measure index` - Measure the fusion outcome at `index` (use with `let!`)
- `TopologicalBuilder.braidSequence indices` - Apply several braids
- `TopologicalBuilder.getState` - Current quantum state (use with `let!`)
- `TopologicalBuilder.getResults` - Measurement results so far (use with `let!`)
- `TopologicalBuilder.getLog` - Execution log (use with `let!`)
- `TopologicalBuilder.getContext` - Full context, for visualization (use with `let!`)

**Anyon types**: `AnyonSpecies.AnyonType.Ising` (Majorana-based; braiding alone gives Clifford gates only) and `AnyonSpecies.AnyonType.Fibonacci` (braiding is universal).

**Execution functions**:
- `TopologicalBuilder.execute backend program` - Run and return the result
- `TopologicalBuilder.executeWithContext backend program` - Run and return the result with the final context

---

### 24. quantumChemistry

**Module**: `FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder`

**Purpose**: Ground state energy calculations with VQE, or with quantum phase estimation (`groundStateMethod GroundStateMethod.QPE`). The CE returns a `ChemistryProblem` and throws if `molecule` (or a `molecule_from_*` operation) or `basis` is missing, or `ansatz` is missing for VQE. `solve problem` returns `Async<Result<ChemistryResult, QuantumError>>`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder

// H2 at 0.74 Å bond length
let h2Problem = quantumChemistry {
    molecule (h2 0.74)
    basis "sto-3g"
    ansatz UCCSD
}

match solve h2Problem |> Async.RunSynchronously with
| Ok result -> printfn "Ground state energy: %.6f Ha" result.GroundStateEnergy
| Error err -> printfn "Error: %s" err.Message
```
The same molecule by quantum phase estimation (12 qubits, about 7 s on the local simulator):

```fsharp
open FSharp.Azure.Quantum.QuantumChemistry

let h2Qpe = quantumChemistry {
    molecule (h2 0.7414)
    basis "sto-3g"
    groundStateMethod GroundStateMethod.QPE
}

match solve h2Qpe |> Async.RunSynchronously with
| Ok result ->
    printfn "QPE energy: %.6f Ha (%A)" result.GroundStateEnergy result.Source
    result.Notes |> List.iter (printfn "  %s")
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `molecule mol` - Molecule instance
- `molecule_from_xyz path` - Load the molecule from an XYZ file (read in `solve`)
- `molecule_from_fcidump path` - Run VQE on an FCIDump file's integrals (read in `solve`; the molecule is a placeholder, since the file has no geometry)
- `molecule_from_provider provider name` - Load the molecule from a dataset provider
- `molecule_from_name name` - Load the molecule from the built-in molecule library
- `basis basisSet` - Basis set name (required)
- `ansatz ansatzType` - `UCCSD`, `HEA` or `ADAPT` (required)
- `optimizer name` - Optimizer name (default "COBYLA")
- `maxIterations n` - Maximum VQE iterations (default: 100)
- `initialParameters params` - Initial UCCSD amplitudes for a warm start (the count must match the ansatz)
- `integralProvider provider` - Molecular integrals for VQE (a PySCF/Psi4 wrapper, `FciDumpIntegrals.fromFile`, ...)
- `groundStateMethod method` - `GroundStateMethod.QPE` runs quantum phase estimation of the Trotterised time evolution (`QPE.runWith QPE.defaultSettings` in the problem's basis; no `ansatz` needed); `VQE`, `Automatic` or no method runs UCCSD-VQE

**Current behaviour of `solve`**: it runs UCCSD-VQE on the local simulator, or quantum phase estimation with `groundStateMethod GroundStateMethod.QPE` (see [Bring Your Own Hamiltonian](bring-your-own-hamiltonian.md) for its design and accuracy). The integrals come from `integralProvider`, else from the `molecule_from_fcidump` file, else from `VQE.run`'s own selection. That selection is integrals the library computes for molecules of H and He atoms, an `Error` for H2O and LiH, and the empirical prototype Hamiltonian otherwise. `basis` chooses the basis of the computed integrals. STO-3G and 6-31G are supported; any other basis is an `Error`. Integrals from a provider or an FCIDUMP file carry their own basis. The result's `Source` says which integrals were used. The `ansatz` value and the optimizer name are stored in the problem (`ansatz` is required), but `solve` does not use them yet: with integrals it runs UCCSD with its own BFGS optimizer. `maxIterations` and `initialParameters` are used. `optimizer` copies the iteration count at the point where it appears, so put `maxIterations` before `optimizer`.

**Pre-built molecules**:
```fsharp
let hydrogen = h2 0.74             // H2, bond length 0.74 Å
let water = h2o 0.96 104.5         // H2O, O-H 0.96 Å, angle 104.5°
let lithiumHydride = lih 1.6       // LiH, bond length 1.6 Å
```

**Loading from files and the library**:
```fsharp
let fromXyz = quantumChemistry {
    molecule_from_xyz "caffeine.xyz"
    basis "sto-3g"
    ansatz UCCSD
}

let fromFciDump = quantumChemistry {
    molecule_from_fcidump "h2o.fcidump"
    basis "6-31g"
    ansatz HEA
}

let fromLibrary = quantumChemistry {
    molecule_from_name "benzene"
    basis "sto-3g"
    ansatz UCCSD
}
```

**Result fields**:
- `GroundStateEnergy` - Ground state energy in Hartrees
- `OptimalParameters` - Optimal VQE parameters found
- `Iterations` - Number of VQE iterations performed
- `Convergence` - Whether VQE converged within tolerance
- `BondLengths` - Bond lengths (e.g. "H-H" -> 0.74)
- `DipoleMoment` - Dipole moment, if computed
- `Source` - What produced the energy (`EnergySource`: `ProviderIntegrals`, `ComputedSto3gIntegrals`, `Computed631gIntegrals`, `EmpiricalHamiltonian`, `QpeTrotterEvolution`, ...)
- `Estimation` - How the energy was estimated (`EnergyEstimation`); for QPE, `PhaseEstimation` with the evolution time, shift, Trotter order and steps, counting qubits, bin width and every peak of the outcome distribution
- `Notes` - Caveats on the energy; for QPE, how strongly the Hartree-Fock state overlaps the reported eigenvalue and any other peaks

---

### 25. coverageOptimizer

**Module**: `FSharp.Azure.Quantum.Business.CoverageOptimizer`

**Purpose**: Set coverage: select the cheapest options that together cover every required element (shifts, facilities, service packages). Solves when evaluated and returns `QuantumResult<CoverageResult>`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.Business.CoverageOptimizer

let coverage = coverageOptimizer {
    universeSize 3

    option "MorningShift" [0; 1] 25.0    // Covers slots 0,1 at cost 25
    option "AfternoonShift" [1; 2] 20.0  // Covers slots 1,2 at cost 20
    option "FullDay" [0; 1; 2] 40.0      // Covers all at cost 40

    backend localBackend
}

match coverage with
| Ok r ->
    printfn "Selected %d options, total cost: %.2f" r.SelectedOptions.Length r.TotalCost
    printfn "Coverage: %d/%d elements" r.ElementsCovered r.TotalElements
| Error err -> printfn "Coverage optimization failed: %s" err.Message
```

**Custom Operations**:
- `element` - Add an element index (grows the universe size if needed)
- `universeSize` - Set the number of elements directly
- `option id coveredElements cost` - Add an option (`string`, `int list`, `float`)
- `backend` - Quantum backend (default: LocalBackend)
- `shots` - Measurement shots (default: 1000)

**Notes**:
- Uses QAOA on a QUBO formulation of set cover.
- Element indices are 0-based and must be in [0, universeSize); costs must be non-negative.

---

### 26. resourcePairing

**Module**: `FSharp.Azure.Quantum.Business.ResourcePairing`

**Purpose**: 1:1 pairing of participants that maximizes total compatibility. Solves when evaluated and returns `QuantumResult<PairingResult>`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.Business.ResourcePairing

let pairing = resourcePairing {
    participant "Alice"
    participant "Bob"
    participant "Carol"

    compatibility "Alice" "Bob" 0.9
    compatibility "Alice" "Carol" 0.5
    compatibility "Bob" "Carol" 0.7

    backend localBackend
}

match pairing with
| Ok r ->
    printfn "Found %d pairings, total score: %.2f" r.Pairings.Length r.TotalScore
    for p in r.Pairings do
        printfn "  %s <-> %s (%.2f)" p.Participant1 p.Participant2 p.Weight
| Error err -> printfn "Resource pairing failed: %s" err.Message
```

**Custom Operations**:
- `participant` - Add a participant by ID
- `participants` - Add several participants (`string list`)
- `compatibility p1 p2 weight` - Compatibility score between two participants
- `backend` - Quantum backend (default: LocalBackend)
- `shots` - Measurement shots (default: 1000)

**Notes**:
- Uses QAOA on a QUBO formulation of maximum weight matching.
- Needs at least 2 participants; weights must be non-negative; every participant in a compatibility must be declared.

---

### 27. packingOptimizer

**Module**: `FSharp.Azure.Quantum.Business.PackingOptimizer`

**Purpose**: Bin packing: assign items to bins so that as few bins as possible are used. Solves when evaluated and returns `QuantumResult<PackingResult>`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.Business.PackingOptimizer

let packing = packingOptimizer {
    containerCapacity 100.0

    item "Crate-A" 45.0
    item "Crate-B" 35.0
    item "Crate-C" 25.0
    item "Crate-D" 50.0

    backend localBackend
}

match packing with
| Ok r ->
    printfn "Packed %d items into %d bins" r.ItemsAssigned r.BinsUsed
    for a in r.Assignments do
        printfn "  %s (size %.1f) -> Bin %d" a.Item.Id a.Item.Size a.BinIndex
| Error err -> printfn "Packing optimization failed: %s" err.Message
```

**Custom Operations**:
- `item id size` - Add an item
- `containerCapacity` - Capacity of every bin
- `backend` - Quantum backend (default: LocalBackend)
- `shots` - Measurement shots (default: 1000)

**Notes**:
- Uses QAOA on a QUBO formulation of bin packing.
- Item sizes must be positive and no larger than the bin capacity; at least one item is required.

---

### 28. socialNetwork

**Module**: `FSharp.Azure.Quantum.Business.SocialNetworkAnalyzer`

**Purpose**: Find communities, a minimum monitor set, or pairings in a network of people. Solves when evaluated and returns `QuantumResult<SocialNetworkResult>`.

**Example**:
```fsharp
open FSharp.Azure.Quantum.Business.SocialNetworkAnalyzer

let analysis = socialNetwork {
    people ["Alice"; "Bob"; "Carol"; "Dave"]
    connections [ "Alice", "Bob"; "Bob", "Carol"; "Alice", "Carol"; "Carol", "Dave" ]
    findLargestCommunity
}

match analysis with
| Ok result -> printfn "%s" result.Message
| Error err -> printfn "Error: %s" err.Message
```

**Custom Operations**:
- `person` / `people` - Add one person / several people
- `connection p1 p2` / `connections pairs` - Add connections
- `findCommunities size` - Find communities of the given size
- `findLargestCommunity` - Find the largest community
- `findMonitorSet` - Smallest set of people covering all connections
- `findPairings` - Pair people along connections
- `backend` - Quantum backend (default: LocalBackend)
- `useGrover` / `useQaoa` - Force the algorithm
- `shots` - Measurement shots

---

## Common Patterns

### For Loops in CEs

Custom operations cannot see a `for` loop variable, so a loop body must produce a value of the builder's state type with `yield!`. Custom operations must also come before the first `yield!`, `for` or `if` in the expression (otherwise error FS3086). The circuit and graph coloring builders support this:

```fsharp
// Circuit: Hadamard on every qubit
let superposition = circuit {
    qubits 5
    for q in [0..4] do
        yield! singleGate (Gate.H q)
}

// Graph coloring: add nodes from data
let fromData = graphColoring {
    colors ["Red"; "Green"; "Blue"]
    for (id, conflicts) in [ "A", ["B"]; "B", ["A"; "C"]; "C", ["B"] ] do
        yield! singleNode (coloredNode { nodeId id; conflictsWith conflicts })
}
```

The other builders take lists instead of loops: `satisfies` can be used several times in `constraintSolver`, and list-valued operations such as `nodes`, `tasks`, `participants` or `people` add many items at once.

### Backend Configuration

Every quantum builder defaults to the local simulator when `backend` is not set (except `optionPricing`, which requires it):

```fsharp
// Default: LocalBackend (simulation)
let onSimulator = periodFinder {
    number 15
    precision 8
}

// Explicit backend: any IQuantumBackend, for example a cloud backend
// created with CloudBackends.CloudBackendFactory (see Backend Switching)
let onBackend (qpu: IQuantumBackend) = periodFinder {
    number 15
    precision 8
    backend qpu
}
```

### Default Shots and Thresholds

| Builder | `shots` default | Other defaults |
|---------|-----------------|----------------|
| `constraintSolver`, `patternMatcher` | 1000 | Grover iterations calculated |
| `phaseEstimator` | 1024 on LocalBackend, 2048 on other backends | |
| `quantumTreeSearch` | 50 on LocalBackend, 250 on other backends | `solutionThreshold` 5%; `successThreshold` 50% local, 60% other |
| `quantumArithmetic` | 100 | |
| `periodFinder`, `linearSystemSolver` | not used | |
| ML builders, `optionPricing`, `coverageOptimizer`, `resourcePairing`, `packingOptimizer`, `constraintScheduler` | 1000 | |
| `quantumRiskEngine`, `drugDiscovery` | 100 | |

### Progress Reporting and Cancellation

The ML builders (`autoML`, `binaryClassification`, `predictiveModel`, `anomalyDetection`, `similaritySearch`) accept a progress reporter and a cancellation token. `constraintSolver` and `quantumTreeSearch` accept a reporter through `onProgress`.

```fsharp
open System.Threading
open FSharp.Azure.Quantum.Core.Progress

// Example 1: Console progress reporter
let consoleReporter = createConsoleReporter (Some true) None

let result1 = autoML {
    trainWith features labels
    maxTrials 20
    progressReporter consoleReporter
}

// Example 2: Event-based progress with cancellation
let cts = new CancellationTokenSource()
let reporter = createEventReporter ()
reporter.SetCancellationToken(cts.Token)

reporter.ProgressChanged.Add(fun event ->
    match event with
    | TrialCompleted(id, score, elapsed) ->
        printfn $"Trial {id}: {score * 100.0:F1}%% in {elapsed:F1}s"
        if score > 0.95 then cts.Cancel()  // Early exit
    | _ -> ())

let result2 = autoML {
    trainWith features labels
    maxTrials 50
    progressReporter (reporter :> IProgressReporter)
    cancellationToken cts.Token
}

// Example 3: Timeout-based cancellation
let ctsTimeout = new CancellationTokenSource()
ctsTimeout.CancelAfter(TimeSpan.FromMinutes 5.0)

let result3 = binaryClassification {
    trainWith trainFeatures trainLabels
    maxEpochs 1000
    cancellationToken ctsTimeout.Token
}
```

**Progress Event Types**:
- `TrialStarted(trialId, totalTrials, modelType)` - AutoML trial starting
- `TrialCompleted(trialId, score, elapsedSeconds)` - AutoML trial completed
- `TrialFailed(trialId, error)` - AutoML trial failed
- `ProgressUpdate(percentComplete, message)` - General progress update
- `PhaseChanged(phaseName, message)` - Algorithm phase changed (`message` is a `string option`)
- `IterationUpdate(current, total, currentBest)` - Iteration progress (`currentBest` is a `float option`)
- `BackendExecutionStarted(backendName, numShots)` / `BackendExecutionCompleted(backendName, elapsedSeconds)` - Backend calls

**Built-in Reporters**:
- `createConsoleReporter verbose cancellationToken` - Console output (both arguments are options)
- `createEventReporter ()` - Raises `ProgressChanged` events, for UI integration
- `createNullReporter ()` - Does nothing
- `createAggregatingReporter reporters` - Forwards to several reporters

**Use Cases**: monitoring long AutoML searches, progress bars in a UI, logging, timeouts, stopping early once a result is good enough.

---

## IntelliSense Tips

### If IntelliSense Doesn't Show Operations

**1. Type the CE name and open the braces first:**

```fsharp
let problem = periodFinder {
    // Now press Ctrl+Space to see operations
    number 21
}
```

**2. Use the CE instance name, not the builder type:**
- ✅ `periodFinder { }` - Correct
- ❌ `PeriodFinderBuilder { }` - Wrong

**3. For generic CEs, give the type parameter:**

```fsharp
let search = patternMatcher<Config> {
    // The type parameter helps IntelliSense
    searchSpace allConfigs
    matchPattern (fun cfg -> cfg.Cost < 50.0)
}
```

**4. Use this reference table** when IntelliSense fails.

---

## See Also

- [Getting Started Guide](getting-started.md)
- [API Reference](api-reference.md)
- [Architecture Overview](architecture-overview.md)
- [Computation Expression Composition](computation-expression-composition.md)
