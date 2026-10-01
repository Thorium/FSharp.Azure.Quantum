---
layout: default
title: FAQ
---

# Frequently Asked Questions (FAQ)

Common questions about FSharp.Azure.Quantum.

## 🔥 Quick Troubleshooting Guide

**Start here if something isn't working!**

| Symptom | Quick Fix |
|---------|-----------|
| "Distance matrix must be square" | Ensure rows = columns = number of cities |
| "Budget ... is insufficient to purchase any asset" | Budget must be ≥ cheapest asset price |
| Compiler error on `solution.Result` | Use `match` on the `Result` first (see [Getting Started](getting-started#complete-error-handling-examples)) |
| Error has no `.Contains` / `%s` rejects it | Errors are `QuantumError` values, not strings: use `err.Message` |
| Very slow first run | Normal - .NET JIT compilation. Second run will be fast. |
| Suboptimal solutions | Raise the QAOA shot counts in the solver config, or run several times and keep the best (see [below](#solutions-seem-suboptimal)) |
| "needs N qubits ... supports at most M" | The encoding is too wide for the backend; shrink the problem or use a larger backend (see [problem sizes](#what-problem-sizes-can-i-solve)) |
| Type inference error | Add explicit type annotations: `list<string * float * float>` |
| "MinHolding ... cannot exceed MaxHolding" | Check constraint values: `MinHolding ≤ MaxHolding ≤ Budget` |

**Still stuck?** See detailed [Errors and Troubleshooting](#errors-and-troubleshooting) below.

## General Questions

### What is FSharp.Azure.Quantum?

FSharp.Azure.Quantum is a **quantum-first F# library** for solving combinatorial optimization problems using quantum algorithms (QAOA, VQE, QFT). It provides:
- Quantum optimization algorithms (QAOA for graph problems, VQE for quantum chemistry)
- QFT-based algorithms (Shor's factorization, Phase Estimation)
- LocalBackend for free quantum simulation (width derived from available memory)
- Optional HybridSolver that picks a classical solver for small problems
- Integration with Azure Quantum cloud backends (IonQ, Rigetti, Quantinuum, Atom Computing, IQM) and D-Wave annealers
- High-level computation expression APIs for intuitive problem specification

### Do I need an Azure account to use this library?

**No!** The LocalBackend (default) provides quantum simulation entirely offline without Azure credentials. You only need Azure access if you want to use cloud quantum backends for real quantum hardware or provider simulators.

### Is this production-ready?

The current package version is **1.5.1**. It is suitable for:
- ✅ Development and prototyping
- ✅ Academic research and learning
- ✅ Quantum algorithm experimentation
- ✅ Applications whose problems fit the simulator or the cloud backends' qubit limits

**LocalBackend** provides free quantum simulation; its width is derived from available memory (hard ceiling 30 qubits), and iterative algorithms such as QAOA are practical up to about 20 qubits. For more, cloud backends are available via Azure Quantum.

### Should I use quantum computing to solve my problem? Why not AI?

Quantum computing is for small-data, big-compute problems: the input fits on a page, but the number of possible answers explodes (routes, schedules, portfolios, molecular energies). For big-data problems, where the answer has to be learned from many examples, traditional AI could work better.

## Technical Questions

### When should I use quantum vs HybridSolver?

#### Quick Comparison Table

| Aspect | Direct Quantum API | HybridSolver (with classical fallback) |
|--------|-------------------|----------------------------------------|
| **Approach** | QAOA/VQE quantum algorithms | `QuantumAdvisor` picks classical or quantum per problem |
| **When quantum runs** | Always | Only when the advisor strongly recommends it (≥ 50 variables by default) *and* you pass a backend, or when you force `Some HybridSolver.Quantum` |
| **Cost** | LocalBackend: free; Cloud: provider pricing | Classical runs are free; optional budget guard for quantum |
| **Problem Size** | Limited by the backend's qubit count (LocalBackend: memory-derived, QAOA practical to ~20 qubits) | Classical path has no qubit limit |
| **Best For** | Learning, consistent quantum API | Variable-size workloads where small cases should stay classical |
| **Backend** | LocalBackend (default) or a cloud backend | Same, passed to the `solve*WithBackendAsync` functions |
| **Reproducible** | ⚠️ Probabilistic (quantum nature) | Classical path: ✅ Deterministic |

#### Decision Criteria

**Use Direct Quantum API when:**
- ✅ Learning quantum algorithms (QAOA, VQE, QFT)
- ✅ Want every run to go through a quantum backend
- ✅ The problem fits the backend (LocalBackend: memory-derived width)
- ✅ Developing/testing quantum algorithms

**Use HybridSolver when:**
- ⚡ Problem size varies significantly
- ⚡ Small problems should be solved classically, with the reasoning recorded
- ⚡ You want a budget guard on quantum cost

**Example:**
```fsharp
open System.Threading
open FSharp.Azure.Quantum
// QuantumMaxCutSolver lives in the Quantum namespace
open FSharp.Azure.Quantum.Quantum

let problem =
    MaxCut.createProblem ["A"; "B"; "C"; "D"]
        [ ("A", "B", 1.0); ("B", "C", 2.0); ("C", "D", 1.0); ("D", "A", 1.0) ]

// Direct Quantum API: QAOA on the local simulator
task {
    match! MaxCut.solveAsync problem None CancellationToken.None with
    | Ok solution -> printfn "Cut value: %.1f" solution.CutValue
    | Error err -> eprintfn "Error: %s" err.Message
}

// HybridSolver takes the solver-level problem type
let hybridProblem: QuantumMaxCutSolver.MaxCutProblem =
    { Vertices = problem.Vertices; Edges = problem.Edges }

task {
    match! HybridSolver.solveMaxCutAsync hybridProblem None None None CancellationToken.None with
    | Ok solution -> 
        printfn "Method: %A" solution.Method  // Classical or Quantum
        printfn "Reasoning: %s" solution.Reasoning
        printfn "Cut value: %.1f" solution.Result.CutValue
    | Error err -> eprintfn "Error: %s" err.Message
}
```

**Crossover Point:** With `QuantumAdvisor.defaultThresholds`, HybridSolver routes to classical below 50 variables. From 50 up it uses quantum if you supplied a backend (and the estimated cost is within any budget you set); otherwise it still runs classically and says so in `Reasoning`.

### How accurate are the solutions?

**Quantum algorithms (QAOA/VQE)** provide:
- Approximate solutions (QAOA = Quantum Approximate Optimization Algorithm)
- Solution quality depends on circuit depth (p), shot count, and problem structure
- No optimality guarantee; compare against a classical solver on your own instances
- Probabilistic nature means running multiple times may yield better results

**Solution quality improves with:**
- Higher shot counts (e.g., 1000 vs 100 shots)
- Deeper circuits (higher QAOA depth p)
- Parameter optimization (variational loop)
- Error mitigation techniques (ZNE, PEC, REM)

**Classical path (via HybridSolver)** provides:
- Heuristic solutions (TSP: nearest neighbour + 2-opt; Portfolio: greedy by return/risk ratio)
- Deterministic results
- Fast execution for small and medium problems

### How many shots do I need?

It depends on what the shots are for.

**Estimating a number** (an expectation value, a probability, an option price): the statistical error falls as 1/√shots, so four times the shots halves it. `Primitives.sampledExpectation` and the Monte Carlo results report their standard error; raise shots until that error is small against the difference you need to resolve.

**Finding a good solution** (QAOA, Grover-style search): what matters is the chance *p* that one shot returns an acceptable answer. The chance of seeing at least one in *N* shots is 1 − (1 − p)^N, so

| Wanted confidence | Shots needed |
|---|---|
| 95% | about 3 / p |
| 99% | about 4.6 / p |

**Read *p* off the solution.** Every QAOA solver solution carries `Sampling`, the standing of the returned answer among the final samples:

```fsharp
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers

// call as: report solution.Sampling
let report (sampling: SampleStatistics option) =
    match sampling with
    | Some s ->
        printfn "%d of %d shots returned this answer (p ≈ %.1f%%); %d were valid" s.Hits s.Shots (100.0 * s.HitRate) s.Valid
        printfn "uniform guessing: %.2f%% per shot; shots for 95%%: %A" (100.0 * s.UniformRate) (s.ShotsFor 0.95)
    | None -> printfn "no single sampling run produced this answer (decomposed, or no circuit needed)"
```

- `HitRate` estimates *p* for the answer you got. Trust it from about three hits upward; one hit in *N* shots reads as 1/N whatever the true value.
- `Hits = 0` with `WasRepaired = true` means classical repair produced the answer and no shot did.
- It says nothing about better answers that were never sampled.

On the 8-route supply-chain example the optimised circuit returns the optimum in 4–5% of shots, and 100 shots found it in 147 of 150 runs.

**Start small, then scale.** While you are still getting the model right (the encoding, the constraints, the backend configuration), run 10–50 shots on the local simulator: a wrong model shows up as an error or as `Valid = 0` however many shots you take, and short runs keep the loop fast. Once the answers are right, take a pilot run of 100–200 shots, read `HitRate`, and size the production run from the table.

**Check that the circuit is doing the work.** A problem on *n* qubits has 2^n bitstrings. If your shots approach or exceed that number, sampling is close to exhaustive and would find the answer with any circuit; random guessing finds a unique optimum with probability 1/2^n per shot (`UniformRate`). Compare `HitRate` with that baseline, and expect *p* to shrink as the problem grows — more layers and optimised angles raise it, more shots only compensate.

### Will my Azure bill grow exponentially with more shots?

No. The price of a job is linear in its shots: twice the shots costs about twice as much, never more. Each provider on Azure Quantum bills in proportion to shots — IonQ per gate and shot, Quantinuum in credits where the shot count multiplies the gate count, Rigetti by execution time — some with a minimum charge per job. `CostEstimation.estimateCost` gives a figure before you submit, and the [Azure Quantum pricing page](https://azure.microsoft.com/en-us/pricing/details/azure-quantum/) has the current rates. The local simulator is free.

Two things do need watching:

- **The shots you need can grow fast.** If the per-shot chance *p* of a good answer falls as the problem grows, 3 / *p* rises with it. That comes from the circuit, not from billing: raise *p* (more layers, optimised angles, a tighter encoding) instead of buying shots, and use `Sampling.HitRate` against `UniformRate` to see where you stand.
- **Jobs multiply shots.** On hardware every optimiser evaluation is a job of `OptimizationShots`, so a run costs about evaluations × `OptimizationShots` + `FinalShots`. Optimise the angles on the simulator where the problem fits, and pass a `JobBudget` to the backend so a run stops at a job count you chose (see [Backend Switching](backend-switching.md)).

### What problem sizes can I solve?

**With a quantum backend** the limit is the number of qubits the encoding needs:

| Problem | Qubits needed |
|---------|---------------|
| TSP | cities² (4 cities = 16 qubits) |
| Graph Coloring | nodes × colors |
| MaxCut | one per vertex |
| Knapsack | one per item + ⌈log₂(capacity + 1)⌉ slack bits (capacity in whole weight units) |
| Portfolio | one per affordable asset (+ slack bits when lots limit the number of holdings) |
| Network Flow | one per route |
| Task Scheduling | tasks × time slots |

The local simulator holds up to `StateVector.maxQubits` (derived from memory, at most 30), and QAOA is practical up to about 20 qubits by default. A problem wider than the backend is split into circuits that fit where its structure allows (next question); otherwise the solver returns an error that names the qubit count.

**With the classical path of HybridSolver** there is no qubit limit: the classical TSP heuristic accepts up to 10,000 cities (`TspSolver.maxCities`).

### My problem is wider than the backend. Can it still run on it?

Often, by trading qubits for circuit runs. `QuboSplitting` cuts a problem into pieces that fit, runs every piece on the backend, and joins the answers classically. Two splits exist:

| Split | Works when | Cost | Used by |
|---|---|---|---|
| **Conditioning** | the QUBO is sparse: fixing a few variables breaks it into pieces that fit | a piece runs once per assignment of the fixed variables it touches: 2^b runs for b of them (at most 8 fixed by default) | MaxCut, vertex cover, clique, matching, set cover, MAX-SAT, binary ILP and the drug-discovery solvers |
| **Blocks** | a linear objective under one additive integer limit (a knapsack) | one run per block and share of the capacity (at most 256 by default) | Knapsack (`solveAsync`, or `solveInBlocksAsync` to ask for it) |
| **Halves** | every subset that sums to a target is wanted (subset-sum) | one search per half and partial sum (at most 256 by default) | `QuantumKnapsackSolver.findAllExactCombinationsAsync` and the `Knapsack.findAll…Async` functions with a backend |

**When it happens.** Only when the problem needs more qubits than the backend runs (or than `MaxPieceQubits`, when you set it), and only where the split settings allow it. The default is `SplitPolicy.OnSimulators`:

| `Policy` | Simulators (local, topological) | Backends that bill every circuit (Azure Quantum, Braket) |
|---|---|---|
| `Never` | one circuit; the backend accepts or refuses it | one circuit |
| `OnSimulators` (default) | split | one circuit, so billed jobs never multiply unasked |
| `Always` | split | split: every piece is a billed job |

**How to set it.** The settings are the `Splitting` field of `QaoaSolverConfig` and of the MaxCut and Knapsack configurations:

```fsharp
open FSharp.Azure.Quantum.Core

let splitting: QaoaExecutionHelpers.SplitSettings =
    { QaoaExecutionHelpers.defaultSplitSettings with
        Policy = QaoaExecutionHelpers.SplitPolicy.Always // also on billed hardware
        MaxPieceQubits = ValueSome 12 // widest circuit: wider problems are split even if the backend could run them
        MaxFixedVariables = 6      // at most 2^6 = 64 runs per piece
        MaxShareRuns = 100         // at most 100 block runs
        MaxBlockItems = 10 }       // at most 10 items per block

let config =
    { QaoaExecutionHelpers.defaultConfig with
        Splitting = splitting }
```

The business builders (`coverageOptimizer`, `resourcePairing`, `socialNetwork`, …) and the `MaxCut` and `Knapsack` builders use the default settings; call the solver's `solveWithConfigAsync` (or `QuantumMaxCutSolver.solveAsync` / `QuantumKnapsackSolver.solveAsync`) to pass your own.

**How to see it.** A solution that was split carries `Split`, and has no `Sampling`:

```fsharp
open FSharp.Azure.Quantum.Core

let describe (split: QaoaExecutionHelpers.SplitReport option) =
    match split with
    | Some s -> printfn "%d runs, widest %d qubits, %d fixed variables, %d blocks" s.Runs s.WidestPieceQubits s.FixedVariables s.Blocks
    | None -> printfn "ran as one circuit"
```

What to expect:

- **Runs multiply.** On a simulator that is time; on hardware every run is billed, so `Always` belongs together with a `JobBudget` on the backend.
- **A problem that cannot be split runs as one circuit** while the backend can hold it, and otherwise returns an error that names the limit it hit (`MaxFixedVariables` or `MaxShareRuns`). The subset-sum search counts its searches as it goes, so it returns that error once it passes `MaxShareRuns`.
- **Pieces stay separate.** Every piece is a circuit of its own, never packed with another: a narrow circuit is faster to simulate and its best sample is not diluted by the other piece. A chain of 100 vertices in pieces of 12 takes 7 fixed vertices and 28 runs.
- **A graph of several components adds its reports up.** `Split` then counts the runs, fixed variables and blocks of all components.
- **The join is exact; the pieces are not.** If every piece returned its true optimum the joined answer would be the optimum of the whole problem. Each piece is sampled, so a missed piece optimum carries into the answer.
- **It is not a wider quantum computation.** No circuit is wider than a piece, so no entanglement spans the pieces; what crosses the cut is tried classically. The split extends the problem size a small device can take on, not the size of the quantum state.
- **Dense encodings do not split by conditioning.** A penalty that squares a sum couples every variable to every other (TSP, task scheduling, knapsack as one QUBO). Knapsack splits by blocks instead.
- **A block needs no slack bits.** It is asked for an exact share, which is an equality: a knapsack of 12 items with capacity 15 is 16 qubits as one QUBO and 6 qubits per block in two blocks.

### Can I use my own distance calculations?

Yes! Build the distance matrix yourself and pass it to `HybridSolver.solveTspAsync`:

```fsharp
open FSharp.Azure.Quantum

let cities = [| (0.0, 0.0); (1.0, 0.5); (2.0, 1.5); (3.0, 3.0) |]

// Custom distance function (Manhattan distance here)
let myDistance (x1, y1) (x2, y2) = abs (x1 - x2) + abs (y1 - y2)

// Build matrix
let n = cities.Length
let distances = 
    Array2D.init n n (fun i j ->
        if i = j then 0.0
        else myDistance cities.[i] cities.[j])

task {
    match! HybridSolver.solveTspAsync distances None None None CancellationToken.None with
    | Ok solution -> printfn "Tour length: %.2f (%A)" solution.Result.TourLength solution.Method
    | Error err -> printfn "Error: %s" err.Message
}
```

## Errors and Troubleshooting

### "Distance matrix must be square"

**Problem:** Your distance matrix has different number of rows and columns.

**Solution:**
```fsharp
// ❌ Wrong: 3x2 matrix
let wrong = array2D [[0.0; 1.0]; [2.0; 0.0]; [3.0; 4.0]]

// ✅ Correct: 3x3 matrix
let correct = array2D [
    [0.0; 1.0; 2.0]
    [1.0; 0.0; 3.0]
    [2.0; 3.0; 0.0]
]
```

### "Distance matrix contains negative values"

**Problem:** Negative distances aren't supported.

**Solution:** Ensure all distances are >= 0.0:
```fsharp
// Normalize or shift if needed
let normalized = 
    distances 
    |> Array2D.map (fun d -> max 0.0 d)
```

### Solutions seem suboptimal

**Try these improvements:**

1. **Give QAOA more shots** (quantum TSP; 4 cities = 16 qubits). Only a few percent of the shots are valid tours at 4 cities, and the result is the shortest of them, so more shots raise the chance that the optimum is among them. `NumLayers = 3` raises the share of valid tours and makes the optimization slower:
```fsharp
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

let config =
    { QuantumTspSolver.defaultConfig with
        OptimizationShots = 500
        FinalShots = 4000 }

let backend = LocalBackend() :> IQuantumBackend

task {
    match! HybridSolver.solveTspWithBackendAndConfigAsync distances None None (Some HybridSolver.Quantum) (Some backend) config CancellationToken.None with
    | Ok solution -> printfn "Tour length: %.2f" solution.Result.TourLength
    | Error err -> printfn "Error: %s" err.Message
}
```

2. **Compare with the classical heuristic:**
```fsharp
task {
    match! HybridSolver.solveTspAsync distances None None (Some HybridSolver.SolverMethod.Classical) CancellationToken.None with
    | Ok solution -> printfn "Tour length: %.2f" solution.Result.TourLength
    | Error err -> printfn "Error: %s" err.Message
}
```

3. **Run the quantum solver several times and keep the best:**
```fsharp
task {
    let solutions = ResizeArray()
    for _ in 1..10 do
        match! HybridSolver.solveTspWithBackendAsync distances None None (Some HybridSolver.Quantum) (Some backend) CancellationToken.None with
        | Ok solution -> solutions.Add solution
        | Error _ -> ()
    return solutions |> Seq.minBy (fun sol -> sol.Result.TourLength)
}
```

### How do I debug slow performance?

**Profile your problem:**

```fsharp
open System.Diagnostics

task {
    let sw = Stopwatch.StartNew()
    match! HybridSolver.solveTspAsync distances None None None CancellationToken.None with
    | Ok solution -> 
        sw.Stop()
        printfn "Size: %d cities" (distances.GetLength(0))
        printfn "Time: %d ms" sw.ElapsedMilliseconds
        printfn "Method: %A" solution.Method
        printfn "Solver time: %.1f ms" solution.ElapsedMs
        printfn "2-opt iterations: %d" solution.Result.Iterations
    | Error err -> printfn "Error: %s" err.Message
}
```

## Feature Questions

### Is quantum backend available yet?

**Status:** ✅ Quantum algorithms are implemented and available via LocalBackend (default) and Azure Quantum cloud backends (IonQ, Rigetti, Quantinuum, Atom Computing, IQM).

**Direct Quantum API:**
- QAOA for optimization problems (GraphColoring, MaxCut, Knapsack, TSP, Portfolio, NetworkFlow, Task Scheduling)
- VQE for quantum chemistry
- QFT-based algorithms (Shor's, Phase Estimation, Quantum Arithmetic)
- Runs on LocalBackend (free, memory-derived width) or cloud backends, where each algorithm submits complete circuits as whole-circuit jobs (every job billed; a `JobBudget` caps them)

**HybridSolver:** Chooses a classical solver for problems below the advisor's thresholds (50 variables by default), where quantum circuit overhead isn't beneficial.

### What quantum algorithms are used?

**Optimization Problems (QAOA-based):**
- GraphColoring: QAOA with K-coloring QUBO encoding
- MaxCut: QAOA with graph cut maximization
- Knapsack: QAOA with 0/1 knapsack constraints
- TSP: QAOA with tour feasibility constraints
- Portfolio: QAOA on the mean-variance QUBO −μᵀw + λ wᵀΣw (off-diagonal covariance terms when a covariance is given)
- NetworkFlow: QAOA with flow conservation

**Quantum Chemistry (VQE):**
- Variational Quantum Eigensolver for molecular ground state energies
- Supports custom Hamiltonians
- UCCSD ansatz: exact expectation values on a simulator; on a cloud backend, sampled whole circuits (one per commuting group of terms) optimised by SPSA
- Ground-state energies by quantum phase estimation (`GroundStateMethod.QPE`); the circuit is deep, so it is for simulators today

**QFT-Based Applications:**
- Shor's Algorithm: Period finding for integer factorization
- Phase Estimation: Eigenvalue extraction for quantum chemistry
- Quantum Arithmetic: Modular exponentiation using QFT

**Classical Fallback (HybridSolver only):**
- TSP: Nearest Neighbor + 2-opt local search
- Portfolio: Greedy selection by return/risk ratio (risk reported as sqrt(wᵀΣw) when a covariance is given)

### Can I add my own optimization problems?

Yes! The library is designed to be extensible:

1. Encode your problem as a QUBO (see [QUBO Encoding Strategies](qubo-encoding-strategies))
2. Run it with QAOA on any `IQuantumBackend` (see the `QaoaCircuit` / `QaoaOptimizer` modules and the existing solvers under `Solvers/Quantum`)
3. Use `QuantumAdvisor.getRecommendation` if you want a quantum-vs-classical recommendation

See [API Reference](api-reference) for the building blocks.

### Does it support GPU acceleration?

**Not in the local simulator**, which runs on the CPU. `CudaQBridge` can hand circuits to NVIDIA CUDA-Q as an external tool.

## Integration Questions

### How do I integrate with my existing F# code?

```fsharp
// Add package reference
// dotnet add package FSharp.Azure.Quantum

// Open namespaces
open System.Threading
open FSharp.Azure.Quantum

// Use in your code
let optimizeTour (distances: float[,]) (cancellationToken: CancellationToken) =
    task {
        match! HybridSolver.solveTspAsync distances None None None cancellationToken with
        | Ok solution -> return Some solution.Result
        | Error _ -> return None
    }
```

### Can I use this from C#?

Yes! F# libraries are interoperable; F# `option` parameters take `null` for `None`:

```csharp
using System.Threading;
using FSharp.Azure.Quantum;

var distances = new double[,] {
    {0.0, 2.0, 9.0},
    {1.0, 0.0, 6.0},
    {15.0, 7.0, 0.0}
};

var result = await HybridSolver.solveTspAsync(distances, null, null, null, CancellationToken.None);

if (result.IsOk) {
    var solution = result.ResultValue;
    Console.WriteLine($"Tour length: {solution.Result.TourLength}");
} else {
    Console.WriteLine($"Error: {result.ErrorValue.Message}");
}
```

The `CSharpBuilders` class and the C# extension methods offer a more idiomatic surface for the problem builders; see [API Reference](api-reference#c-interop).

### Does it work with .NET 8/9/10?

**Targets:** .NET 10.0

**Compatible with:** .NET 10.0 or later

**Not compatible:** .NET Framework, .NET Core 3.1, .NET 5–9

## Cost and Licensing

### How much does it cost?

**Library:** Free and open source (Unlicense license)

**LocalBackend (Quantum Simulation):** Free - runs entirely local; width derived from available memory

**Cloud Quantum Backends:** Azure Quantum pricing applies
- Pay-per-use; cost depends on provider, circuit size and shot count, and is linear in shots (see [Will my Azure bill grow exponentially with more shots?](#will-my-azure-bill-grow-exponentially-with-more-shots))
- Qubit limits the library enforces: IonQ Aria 25 / Forte 36, Rigetti 84, Quantinuum H1 32 / H2 56, Atom Computing 100, IQM 20; provider simulators 20
- `CostEstimation` gives rough per-provider estimates before you submit
- See [Azure Quantum Pricing](https://azure.microsoft.com/en-us/pricing/details/azure-quantum/)

### What license is it under?

**Unlicense** - public domain equivalent:
- ✅ Use commercially
- ✅ Modify freely
- ✅ No attribution required
- ✅ No warranty provided

## Support Questions

### Where can I get help?

- **GitHub Issues:** [Report bugs/request features](https://github.com/thorium/FSharp.Azure.Quantum/issues)
- **Documentation:** [Documentation home](index)
- **Examples:** [See examples/](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples)

### How do I report a bug?

1. Check [existing issues](https://github.com/thorium/FSharp.Azure.Quantum/issues)
2. Create minimal reproduction
3. Include:
   - F#/.NET version
   - Library version
   - Code sample
   - Expected vs actual behavior

### How can I contribute?

Contributions welcome!
- Fix bugs
- Add tests
- Improve documentation
- Suggest features

See `CONTRIBUTING.md` (if available) or open an issue to discuss.

## Performance Questions

### Why is my first call slow?

**.NET JIT compilation** - first call includes:
- Assembly loading
- JIT compilation
- Memory allocation

**Solution:** Warm up with a small problem first:
```fsharp
task {
    // Warm up JIT with a tiny problem
    let! _ = MaxCut.solveAsync (MaxCut.createProblem ["A"; "B"] [ ("A", "B", 1.0) ]) None CancellationToken.None

    // Now solve the real problem
    let! solution = MaxCut.solveAsync problem None CancellationToken.None
    return solution
}
```

### How do I parallelize multiple solves?

Start each run as a task on the thread pool and await them together; give each run its own backend instance:

```fsharp
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core

// Ten independent QAOA runs; keep the best cut
task {
    let! runs =
        [1..10]
        |> List.map (fun _ -> 
            Task.Run<QuantumResult<MaxCut.Solution>>(fun () ->
                let runBackend = LocalBackend() :> IQuantumBackend
                MaxCut.solveAsync problem (Some runBackend) CancellationToken.None))
        |> Task.WhenAll
    let best =
        runs
        |> Array.choose (function Ok s -> Some s | Error _ -> None)
        |> Array.maxBy (fun s -> s.CutValue)
    return best
}
```

Each run holds its own state vector, so memory grows with the number of parallel runs.

See [Backend Switching](backend-switching) for more task patterns.

---

## Still have questions?

- Check [Getting Started Guide](getting-started)
- Browse [Examples](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples)
- Review [API Reference](api-reference)
- Open a [GitHub Issue](https://github.com/thorium/FSharp.Azure.Quantum/issues)
