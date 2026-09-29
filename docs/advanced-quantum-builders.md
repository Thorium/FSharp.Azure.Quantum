# Advanced Quantum Builders

**Target Audience**: Researchers, algorithm developers, quantum computing enthusiasts

This guide covers builders for well-known quantum algorithms: Grover search (tree search, constraint solving, pattern matching), quantum arithmetic, Shor's period finding and quantum phase estimation. They are meant for research and teaching. The algorithms have theoretical advantages that need large, fault-tolerant quantum hardware; on today's hardware and on a simulator they only run toy-sized problems.

**⚠️ Current Limitations**: The builders run on the local simulator by default. Its qubit limit is derived from available memory and capped at 30, and most builders here set tighter limits of their own (16 qubits for the Grover-based builders, 20 for phase estimation precision). A simulator evaluates your predicates and evaluation functions classically for every basis state, so it shows how the algorithms work but gives no speedup. Real RSA-size problems need thousands of error-corrected qubits.

---

## Table of Contents

1. [Quantum Tree Search](#quantum-tree-search) - Game AI with Grover's algorithm
2. [Quantum Constraint Solver](#quantum-constraint-solver) - CSP solving (Sudoku, N-Queens)
3. [Quantum Pattern Matcher](#quantum-pattern-matcher) - Configuration search, hyperparameter tuning
4. [Quantum Arithmetic](#quantum-arithmetic) - Modular arithmetic for cryptography
5. [Period Finder (Shor's Algorithm)](#period-finder-shors-algorithm) - Integer factorization
6. [Phase Estimator](#phase-estimator) - Eigenvalue extraction
7. [When to Use These Builders](#when-to-use-these-builders)
8. [Query Counts in Theory](#query-counts-in-theory)
9. [Troubleshooting](#troubleshooting)

All examples on this page use these opens:

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

let localBackend = LocalBackend.LocalBackend() :> IQuantumBackend
```

---

## Quantum Tree Search

### What is Quantum Tree Search?

**Quantum Tree Search** uses Grover's algorithm to search a game tree or decision tree for paths whose evaluation is in the top fraction of all paths. In theory Grover search needs about √N oracle queries for N paths, where exhaustive classical search needs N. That matters most when evaluating a position is expensive.

### When to Use

✅ **Good Fits**:
- Game AI and decision trees, for research and teaching
- Evaluation functions that are expensive to compute
- Trees that fit the qubit limit: maxDepth × ⌈log₂(branchingFactor)⌉ ≤ 16

❌ **Not Suitable For**:
- Games already solved classically (tic-tac-toe)
- Deep trees (the builder allows at most depth 8, and the qubit limit usually binds first)
- Cheap evaluation functions, or trees where alpha-beta pruning already works well

### API Reference

**Basic Usage**:
```fsharp
open FSharp.Azure.Quantum.QuantumTreeSearch

// Toy game: the state is a running total, each move adds 1..4
let startingTotal = 0
let evaluatePosition (total: int) = float (total % 7)
let generateMoves (total: int) = [ for step in 1 .. 4 -> total + step ]

let treeProblem = quantumTreeSearch<int> {
    initialState startingTotal
    maxDepth 3                    // Search 3 moves ahead
    branchingFactor 4             // Moves per position (3 × 2 = 6 qubits)
    evaluateWith evaluatePosition // Evaluation function
    generateMovesWith generateMoves
    topPercentile 0.2             // Amplify the top 20% of paths
    backend localBackend
    shots 100
}

match QuantumTreeSearch.solve treeProblem with
| Ok result ->
    printfn "Best move: %d" result.BestMove
    printfn "Score: %.4f" result.Score
    printfn "Paths explored: %d" result.PathsExplored
| Error err ->
    printfn "Error: %s" err.Message
```

**Configuration Options**:

| Option | Type | Description | Default |
|--------|------|-------------|---------|
| `initialState` | `'T` | Starting game/decision state | *Required* |
| `maxDepth` | `int` | Search depth (1-8) | 3 |
| `branchingFactor` | `int` | Moves per position (2-256) | 16 |
| `evaluateWith` | `'T -> float` | Position evaluation (higher = better) | Constant 0.0 |
| `generateMovesWith` | `'T -> 'T list` | Successor states | No moves |
| `topPercentile` | `float` | Fraction of best paths to amplify, in (0.0, 1.0] | 0.2 |
| `backend` | `IQuantumBackend` | Quantum backend | LocalBackend |
| `shots` | `int` | Number of measurements | 50 (LocalBackend), 250 (other) |
| `solutionThreshold` | `float` | Minimum fraction of shots for a solution | 0.05 |
| `successThreshold` | `float` | Minimum total probability for success | 0.5 (LocalBackend), 0.6 (other) |
| `maxPaths` | `int` | Limit on paths searched | Full tree |
| `limitSearchSpace` | `bool` | Set `maxPaths` to a recommended limit | Off |

`maxIterations` is accepted, but the current solver always calculates the Grover iteration count from the search space size.

**Result Type**:
```fsharp
type TreeSearchSolution = {
    BestMove: int              // Index of the best first move
    Score: float               // Score of the best move
    PathsExplored: int         // Number of paths searched
    QuantumAdvantage: bool     // Reported by the search algorithm
    BackendName: string        // Backend used
    QubitsRequired: int        // Qubits needed
    AllSolutions: int list     // All solution paths found (for debugging)
}
```

### Use Cases

#### Game Engine with an Expensive Evaluation Function

**Problem**: A learned evaluation function is slow, so a deep classical search calls it too often.

**Idea**: Grover search needs fewer oracle queries in theory. On a simulator (and on today's hardware) the evaluation function still runs for every path, so this is a way to study the approach, not to speed up an engine.

<!-- fragment -->
```fsharp
let evaluateChessPosition (state: ChessState) : float =
    // Your learned evaluation (expensive)
    evaluationModel.Evaluate state.Board

let chessProblem = quantumTreeSearch<ChessState> {
    initialState chessInitial
    maxDepth 2
    branchingFactor 32    // 2 × 5 = 10 qubits; depth 4 would need 20 and is rejected
    evaluateWith evaluateChessPosition
    generateMovesWith generateChessMoves
    backend localBackend
}
```

#### Business Decision Trees

**Problem**: Each path through a multi-stage decision (marketing, pricing, launch) is scored by a slow simulation.

<!-- fragment -->
```fsharp
type BusinessState = {
    Marketing: MarketingDecision option
    Pricing: PricingDecision option
    Launch: LaunchDecision option
}

let simulateMarketImpact (state: BusinessState) : float =
    // Your market simulation
    runMarketSimulation state

let decisionProblem = quantumTreeSearch<BusinessState> {
    initialState initialDecision
    maxDepth 3        // 3-stage decision process
    branchingFactor 4 // 3 × 2 = 6 qubits
    evaluateWith simulateMarketImpact
    generateMovesWith generateDecisions
}
```

### Query Counts

**Classical exhaustive search**: b^d evaluations for branching factor b and depth d
- Example: 16^4 = 65,536 evaluations

**Grover search**: about √(b^d) oracle queries
- Example: √65,536 = 256 queries

These are query counts for an ideal quantum computer with an efficient oracle. The local simulator evaluates every path.

### See Working Examples

- [`examples/TreeSearch/GameAI.fsx`](../examples/TreeSearch/GameAI.fsx) - Tic-tac-toe, chess and decision-tree toys

---

## Quantum Constraint Solver

### What is Quantum Constraint Solver?

**Quantum Constraint Solver** uses Grover's algorithm to find an assignment of values to variables that satisfies all constraints of a Constraint Satisfaction Problem (CSP).

### When to Use

✅ **Good Fits**:
- Small constraint satisfaction problems (Sudoku-style puzzles, N-Queens, assignments)
- Search spaces up to 2^16 candidate assignments
- Finding **any** valid solution (not necessarily optimal)

❌ **Not Suitable For**:
- Optimization problems (use QAOA/VQE instead)
- Larger search spaces (the builder rejects more than 16 qubits)
- Problems with efficient classical algorithms (e.g., 2-SAT)

### API Reference

**Basic Usage**:
```fsharp
open FSharp.Azure.Quantum.QuantumConstraintSolver

// 3 variables over 1..4, all different, in increasing order
let checkAllConstraints (assignment: Map<int, int>) =
    assignment.[0] < assignment.[1] && assignment.[1] < assignment.[2]

let csp = constraintSolver<int> {
    searchSpace 3            // 3 variables (3 × log2(4) = 6 qubits)
    domain [1..4]            // Each variable in range 1-4
    satisfies checkAllConstraints
    backend localBackend
    shots 1000
}

match QuantumConstraintSolver.solve csp with
| Ok solution ->
    printfn "Solution: %A" solution.Assignment
    printfn "Constraints satisfied: %b" solution.AllConstraintsSatisfied
| Error err ->
    printfn "Error: %s" err.Message
```

**Configuration Options**:

| Option | Type | Description | Default |
|--------|------|-------------|---------|
| `searchSpace` | `int` | Number of variables (numVariables × log2(domainSize) must be ≤ 16 qubits) | *Required* |
| `domain` | `'T list` | Possible values for each variable | *Required* |
| `satisfies` | `Map<int,'T> -> bool` | Constraint; use it several times for several constraints | *At least one* |
| `backend` | `IQuantumBackend` | Quantum backend | LocalBackend |
| `maxIterations` | `int` | Grover iterations | Calculated |
| `shots` | `int` | Number of measurements | 1000 |

The builder validates the problem when the CE ends and throws an exception if it is invalid.

**Result Type**:
```fsharp
type ConstraintSolution<'T> = {
    Assignment: Map<int, 'T>   // Variable assignments (variable index -> value)
    SuccessProbability: float
    AllConstraintsSatisfied: bool
    BackendName: string
    QubitsRequired: int
    IterationsUsed: int
}
```

### Use Cases

#### Sudoku Solver

**Problem**: Fill the empty cells of a 4×4 grid with numbers 1-4 satisfying row, column, and box constraints.

**Note**: `searchSpace` is the number of variables, and the builder caps the register at 16 qubits — a full 9×9 grid (81 variables over 1..9 ≈ 257 qubits) is rejected. Use one variable per *empty* cell of a small grid instead. Classical Sudoku solvers with constraint propagation are much faster; this is a demonstration.

```fsharp
// 0 marks an empty cell
let puzzle =
    array2D [ [ 1; 0; 3; 0 ]
              [ 0; 4; 0; 2 ]
              [ 2; 0; 4; 0 ]
              [ 4; 3; 2; 1 ] ]

let emptyCells =
    [ for r in 0 .. 3 do
          for c in 0 .. 3 do
              if puzzle.[r, c] = 0 then yield (r, c) ]

let rows = [ for r in 0 .. 3 -> [ for c in 0 .. 3 -> (r, c) ] ]
let cols = [ for c in 0 .. 3 -> [ for r in 0 .. 3 -> (r, c) ] ]
let boxes =
    [ for br in [ 0; 2 ] do
          for bc in [ 0; 2 ] -> [ for r in br .. br + 1 do for c in bc .. bc + 1 -> (r, c) ] ]

let checkSudoku (assignment: Map<int, int>) =
    // Merge the assignment into the empty cells, then check rows, columns and boxes
    let grid = Array2D.copy puzzle
    emptyCells |> List.iteri (fun i (r, c) -> grid.[r, c] <- assignment.[i])

    (rows @ cols @ boxes)
    |> List.forall (fun group -> group |> List.map (fun (r, c) -> grid.[r, c]) |> List.distinct |> List.length = 4)

let sudoku = constraintSolver<int> {
    searchSpace emptyCells.Length   // one variable per empty cell (6 × log2(4) = 12 qubits)
    domain [1..4]
    satisfies checkSudoku
    backend localBackend
}
```

#### N-Queens Puzzle

**Problem**: Place N queens on an N×N chessboard with no attacks.

```fsharp
let checkQueens (assignment: Map<int, int>) =
    // assignment: row -> column
    let queens = Map.toList assignment
    // No two queens share a column or a diagonal
    queens
    |> List.forall (fun (r1, c1) ->
        queens |> List.forall (fun (r2, c2) -> r1 = r2 || (c1 <> c2 && abs (r1 - r2) <> abs (c1 - c2))))

let queens = constraintSolver<int> {
    searchSpace 4  // 4 queens, one variable per row (4 × log2(4) = 8 qubits;
                   // 8-queens over 0..7 would need 24 qubits — over the 16-qubit limit)
    domain [0..3]  // Columns 0-3
    satisfies checkQueens
}
```

#### Job Scheduling with Constraints

**Problem**: Assign workers to shifts respecting who may work each shift, with no worker on two shifts.

```fsharp
// shift -> workers allowed on it (skills and availability)
let allowedWorkers = Map [ 0, set [ 0; 1 ]; 1, set [ 1; 2 ]; 2, set [ 2; 3 ] ]

let checkSchedule (assignment: Map<int, int>) =
    // assignment: shift -> worker
    let workers = assignment |> Map.toList |> List.map snd
    (assignment |> Map.forall (fun shift worker -> allowedWorkers.[shift].Contains worker))
    && List.distinct workers = workers

let shifts = constraintSolver<int> {
    searchSpace 3  // 3 shifts
    domain [0..3]  // 4 workers (3 × 2 = 6 qubits)
    satisfies checkSchedule
}
```

### Query Counts

**Classical exhaustive search**: N evaluations for N candidate assignments
- Example: 5 variables over 5 values = 3,125 states → up to 3,125 evaluations

**Grover search**: about √N oracle queries
- Example: √3,125 ≈ 56 queries

On the local simulator the predicate is evaluated for every basis state, so the local run is not faster than classical enumeration.

### See Working Examples

- [`examples/ConstraintSolver/SudokuSolver.fsx`](../examples/ConstraintSolver/SudokuSolver.fsx) - Sudoku, N-Queens, job scheduling

---

## Quantum Pattern Matcher

### What is Quantum Pattern Matcher?

**Quantum Pattern Matcher** uses Grover's algorithm to find items in a search space that match a predicate. It suits searches where checking one item is expensive, such as configuration or hyperparameter searches.

### When to Use

✅ **Good Fits**:
- Configuration searches (database tuning, compiler flags)
- Hyperparameter and feature-subset searches
- Expensive checks per item

❌ **Not Suitable For**:
- Cheap checks (a classical scan is faster)
- Problems with structure (use the constraint solver)
- Search spaces over 2^16 items (rejected)

### API Reference

**Basic Usage**:
```fsharp
open FSharp.Azure.Quantum.QuantumPatternMatcher

type ServerConfig = { CacheMb: int; Workers: int }

let allConfigurations =
    [ for cache in [ 64; 128; 256; 512 ] do
          for workers in [ 1; 2; 4; 8 ] -> { CacheMb = cache; Workers = workers } ]

// Stand-in for a benchmark run
let meetsTarget (config: ServerConfig) = config.CacheMb >= 256 && config.Workers >= 4

// Option 1: Search over an explicit list
let configSearch = patternMatcher<ServerConfig> {
    searchSpace allConfigurations
    matchPattern meetsTarget
    findTop 2
    backend localBackend
}

// Option 2: Search over an index space 0..255
let indexSearch = patternMatcher<int> {
    searchSpace 256
    matchPattern (fun idx -> idx % 64 = 0)
    findTop 2
}

match QuantumPatternMatcher.solve configSearch with
| Ok solution ->
    printfn "Matches: %A" solution.Matches
    printfn "Success probability: %.2f" solution.SuccessProbability
| Error err ->
    printfn "Error: %s" err.Message
```

**Configuration Options**:

| Option | Type | Description | Default |
|--------|------|-------------|---------|
| `searchSpace` | `'T list` or `int` | Items to search, or the size of an index space | *Required* |
| `searchSpaceSize` | `int` | Size of an index space | — |
| `matchPattern` | `'T -> bool` | Pattern predicate (a later one replaces an earlier one) | *Required* |
| `findTop` | `int` | Number of matches to return | 1 |
| `backend` | `IQuantumBackend` | Quantum backend | LocalBackend |
| `maxIterations` | `int` | Grover iterations | Calculated |
| `shots` | `int` | Number of measurements | 1000 |

**Result Type**:
```fsharp
type PatternSolution<'T> = {
    Matches: 'T list           // Items matching the pattern
    SuccessProbability: float  // Search success probability
    BackendName: string
    QubitsRequired: int
    IterationsUsed: int
    SearchSpaceSize: int
}
```

### Use Cases

#### Database Configuration Tuning

**Problem**: Many configuration combinations, and each check is a long benchmark run.

<!-- fragment -->
```fsharp
type DbConfig = {
    CacheSize: int
    MaxConnections: int
    QueryTimeout: int
}

let testConfig (config: DbConfig) : bool =
    let results = runBenchmarkSuite config  // Your benchmark
    results.Throughput > 10000.0 &&
    results.P99Latency < 100.0

let dbSearch = patternMatcher<DbConfig> {
    searchSpace allDbConfigurations  // e.g. 1024 configs
    matchPattern testConfig
    findTop 5  // Top 5 configurations
}
```

#### ML Hyperparameter Tuning

**Problem**: 256 hyperparameter combinations, each needing a training run.

<!-- fragment -->
```fsharp
let configCount = 256  // 8 hyperparameters, 2 values each

let evaluateHyperparameters (idx: int) : bool =
    let hyperparameters = decodeHyperparameters idx
    let accuracy = trainModel hyperparameters  // Your training run
    accuracy > 0.95

let tuning = patternMatcher<int> {
    searchSpace configCount
    matchPattern evaluateHyperparameters
    findTop 3
}
```

#### Feature Selection

**Problem**: Choose a feature subset where each candidate needs a full training run.

<!-- fragment -->
```fsharp
let featureSets = generateFeatureSubsets allFeatures

let testFeatureSet (features: string list) : bool =
    let model = trainModel features  // Your training run
    model.Accuracy > 0.90 && features.Length < 20

let featureSearch = patternMatcher<string list> {
    searchSpace featureSets
    matchPattern testFeatureSet
    findTop 10
}
```

### Query Counts

**Classical scan**: N checks for N items
- Example: 1024 configs → up to 1024 checks

**Grover search**: about √N oracle queries
- Example: √1024 = 32 queries

This saving assumes the check runs inside a quantum oracle on fault-tolerant hardware. With a classical check (a benchmark, a training run), the simulator runs it for every item.

### See Working Examples

- [`examples/PatternMatcher/ConfigurationOptimizer.fsx`](../examples/PatternMatcher/ConfigurationOptimizer.fsx) - Configuration search
- Source: `src/FSharp.Azure.Quantum/Solvers/Quantum/QuantumPatternMatcherBuilder.fs`

---

## Quantum Arithmetic

### What is Quantum Arithmetic?

**Quantum Arithmetic** runs arithmetic (addition, multiplication, modular operations) as quantum circuits built on the Quantum Fourier Transform (QFT), following Draper and Beauregard. Modular exponentiation is the core of Shor's algorithm.

### When to Use

✅ **Good Fits**:
- Building blocks for Shor's algorithm
- Cryptographic demonstrations (toy RSA)
- Teaching quantum circuits
- Research into quantum arithmetic circuits

❌ **Not Suitable For**:
- General-purpose arithmetic (use the CPU)
- Production cryptography (use classical libraries)
- Large numbers: the register size is limited by the simulator's practical circuit width (20 qubits by default, including ancillas)

### API Reference

**Basic Usage**:
```fsharp
open FSharp.Azure.Quantum.QuantumArithmeticOps

let message = 5
let e = 3
let n = 33

// Modular exponentiation: m^e mod n (RSA encryption)
let modExp = quantumArithmetic {
    operands message e     // base, exponent
    operation ModularExponentiate
    modulus n              // RSA modulus
    qubits 6               // 6-bit registers; 2 × 6 + 5 = 17 qubits in total
}

match modExp with
| Ok op ->
    match execute op with
    | Ok result ->
        printfn "Result: %d" result.Value
        printfn "Qubits: %d" result.QubitsUsed
        printfn "Gates: %d" result.GateCount
    | Error err ->
        printfn "Execution Error: %s" err.Message
| Error err ->
    printfn "Builder Error: %s" err.Message
```

**Supported Operations**:

| Operation | Description | Total qubits for register size n |
|-----------|-------------|----------------------------------|
| `Add` | a + b | n |
| `Multiply` | a × b | n |
| `ModularAdd` | (a + b) mod N | n + 2 |
| `ModularMultiply` | (a × b) mod N | 2n + 3 |
| `ModularExponentiate` | a^b mod N | 2n + 5 |

**Configuration Options**:

| Option | Type | Description | Default |
|--------|------|-------------|---------|
| `operands` | `int int` | Operands a and b (for exponentiation: base and exponent) | 0, 0 |
| `operandA` / `operandB` / `exponent` | `int` | Set one operand | 0 |
| `operation` | `OperationType` | Arithmetic operation | `Add` |
| `modulus` | `int` | Modulus | *Required for modular operations* |
| `qubits` | `int` | Register size (minimum 2) | 8 |
| `backend` | `IQuantumBackend` | Quantum backend | LocalBackend |
| `shots` | `int` | Shots used to read the result register | 100 |

**Result Type**:
```fsharp
type ArithmeticResult = {
    Value: int                     // Computed result
    QubitsUsed: int                // Qubits required
    GateCount: int                 // Total quantum gates
    CircuitDepth: int              // Circuit depth
    OperationType: OperationType   // Operation performed
    BackendName: string            // Backend used
    IsModular: bool                // Whether modular arithmetic was used
}
```

### Use Cases

#### RSA Encryption (Educational)

**Problem**: Demonstrate RSA encryption using quantum circuits.

**Note**: This is for education/research only. Real RSA uses classical methods.

```fsharp
// RSA key setup (toy example)
let p = 3   // Prime 1
let q = 11  // Prime 2
let rsaModulus = p * q  // n = 33
let publicExponent = 3

let plaintext = 5

// Encrypt: c = m^e mod n
let encryptOp = quantumArithmetic {
    operands plaintext publicExponent
    operation ModularExponentiate
    modulus rsaModulus
    qubits 6
}

match encryptOp with
| Ok op ->
    match execute op with
    | Ok result ->
        let ciphertext = result.Value
        printfn "Encrypted: %d^%d mod %d = %d" plaintext publicExponent rsaModulus ciphertext
    | Error err ->
        printfn "Execution Error: %s" err.Message
| Error err ->
    printfn "Builder Error: %s" err.Message
```

#### Research: Circuit Size

**Problem**: Compare gate count and depth of quantum arithmetic circuits.

```fsharp
let testArithmeticCircuit (a: int) (b: int) (m: int) =
    let multiply = quantumArithmetic {
        operands a b
        operation ModularMultiply
        modulus m
        qubits 6   // 2 × 6 + 3 = 15 qubits in total
    }
    match multiply with
    | Ok op ->
        match execute op with
        | Ok result ->
            printfn "Gates: %d, Depth: %d" result.GateCount result.CircuitDepth
        | Error _ -> ()
    | Error _ -> ()
```

### Quantum Advantage

**None for standalone arithmetic** - quantum arithmetic is slower than CPU arithmetic.

**As a subroutine** - modular exponentiation is the expensive part of Shor's algorithm.

### See Working Examples

- [`examples/QuantumArithmetic/RSAEncryption.fsx`](../examples/QuantumArithmetic/RSAEncryption.fsx) - RSA encryption demo

---

## Period Finder (Shor's Algorithm)

### What is Period Finder?

**Period Finder** implements **Shor's algorithm** for integer factorization: it finds the period of modular exponentiation with quantum phase estimation, and derives factors from the period. On a fault-tolerant quantum computer Shor's algorithm runs in polynomial time, where the best known classical algorithms are super-polynomial.

### When to Use

✅ **Good Fits**:
- **Research**: Understanding Shor's algorithm
- **Education**: Demonstrating the quantum threat to RSA
- **Security Analysis**: Motivating post-quantum cryptography

❌ **Not Suitable For**:
- **Production cryptanalysis** (requires a fault-tolerant quantum computer)
- **Anything beyond toy numbers**: the builder accepts N up to 10000
- **Classical factorization** (use GNFS or other classical algorithms)

**⚠️ Hardware Reality**: Estimates for factoring RSA-2048 are in the thousands of error-corrected (logical) qubits, and many more physical qubits. Today's devices are far from that.

### API Reference

**Basic Usage**:
```fsharp
open FSharp.Azure.Quantum.QuantumPeriodFinder

// Factor integer n
let shor = periodFinder {
    number 15           // Number to factor
    precision 8         // QPE precision (qubits)
    maxAttempts 10      // Probabilistic algorithm
}

match shor with
| Ok prob ->
    match QuantumPeriodFinder.solve prob with
    | Ok result ->
        printfn "Base: %d" result.Base
        printfn "Period: %d" result.Period
        match result.Factors with
        | Some (p, q) ->
            printfn "Factors: %d × %d = %d" p q (p * q)
        | None ->
            printfn "Period found but no factors (retry)"
    | Error err ->
        printfn "Execution Error: %s" err.Message
| Error err ->
    printfn "Builder Error: %s" err.Message

// Or use the convenience function (returns the same Result<PeriodFinderProblem, _>)
let shor143 = factorInteger 143 8  // n=143, precision=8
```

**Configuration Options**:

| Option | Type | Description | Default |
|--------|------|-------------|---------|
| `number` | `int` | Integer to factor (4-10000) | 15 |
| `precision` | `int` | QPE precision qubits (1-20) | 8 |
| `chosenBase` | `int` | Base a with 2 ≤ a < N | Chosen automatically |
| `maxAttempts` | `int` | Maximum attempts (1-100) | 10 |
| `exactness` | `QPE.Exactness` | `Exact` or `Approximate epsilon` | `Exact` |
| `backend` | `IQuantumBackend` | Quantum backend | LocalBackend |
| `shots` | `int` | Accepted but not used; raise `maxAttempts` instead | — |

**Result Type**:
```fsharp
type PeriodFinderResult = {
    Number: int                  // Number analyzed
    Period: int                  // Period found
    Base: int                    // Base used
    Factors: (int * int) option  // Factors (if found)
    PhaseEstimate: float         // QPE phase estimate
    QubitsUsed: int              // Qubits used
    Attempts: int                // QPE shots used by the successful run
    Success: bool                // Whether factorization succeeded
    BackendName: string
    Message: string
}
```

### Use Cases

#### Security Assessment: Toy RSA Modulus

**Problem**: Show how factoring the modulus breaks RSA.

```fsharp
// Small RSA modulus (educational)
let smallRSA = 15  // 3 × 5

let attack = periodFinder {
    number smallRSA
    precision 4  // Reduced for local simulation
    maxAttempts 10
}

match attack with
| Ok prob ->
    match QuantumPeriodFinder.solve prob with
    | Ok result ->
        match result.Factors with
        | Some (p, q) ->
            printfn "RSA BROKEN: %d = %d × %d" smallRSA p q
            printfn "Attacker can now:"
            printfn "  1. Calculate φ(n) = (p-1)(q-1)"
            printfn "  2. Derive the private key from the public key"
            printfn "  3. Decrypt messages"
        | None ->
            printfn "Period found but no factors (try again)"
    | Error err ->
        printfn "Execution Error: %s" err.Message
| Error err ->
    printfn "Builder Error: %s" err.Message
```

#### Educational: Understanding Shor's Algorithm

```fsharp
let demonstrateShor (n: int) (precision: int) =
    printfn "Factoring %d using Shor's Algorithm" n
    printfn "Best known classical algorithms: super-polynomial (GNFS)"
    printfn "Shor's algorithm: polynomial time on a fault-tolerant quantum computer"
    printfn ""

    match factorInteger n precision with
    | Ok prob ->
        match QuantumPeriodFinder.solve prob with
        | Ok result ->
            printfn "✅ Found period %d, factors %A" result.Period result.Factors
        | Error err ->
            printfn "❌ Failed: %s" err.Message
    | Error err ->
        printfn "❌ Invalid problem: %s" err.Message

// Test with small numbers
demonstrateShor 15 4   // 3 × 5
demonstrateShor 21 6   // 3 × 7
```

### Complexity

**Classical**: the General Number Field Sieve runs in exp(O((log N)^(1/3) (log log N)^(2/3))) time.

**Quantum**: Shor's algorithm uses O((log N)^3) gates with schoolbook arithmetic.

The quantum advantage is exponential in theory, but it needs a fault-tolerant quantum computer.

### Hardware Requirements

| Key Size | Logical qubits (2n+3, Beauregard circuit) | Available Today? |
|----------|-------------------------------------------|------------------|
| 4-bit (N = 15) | ~11, plus precision qubits | ✅ Yes (LocalBackend) |
| 100-bit | ~200 | ❌ No |
| 2048-bit (standard) | ~4,100 logical, many more physical | ❌ No (needs fault tolerance) |
| 4096-bit (high-security) | ~8,200 logical | ❌ No |

### See Working Examples

- [`examples/CryptographicAnalysis/RSAFactorization.fsx`](../examples/CryptographicAnalysis/RSAFactorization.fsx) - Shor's algorithm for RSA factorization
- [`examples/CryptographicAnalysis/DiscreteLogAttack.fsx`](../examples/CryptographicAnalysis/DiscreteLogAttack.fsx) - Quantum discrete logarithm attack
- [`examples/CryptographicAnalysis/GroverAESThreat.fsx`](../examples/CryptographicAnalysis/GroverAESThreat.fsx) - Grover's algorithm threat to AES
- [`examples/CryptographicAnalysis/ECCBitcoinThreat.fsx`](../examples/CryptographicAnalysis/ECCBitcoinThreat.fsx) - Quantum ECDLP threat to Bitcoin/ECC
- [`examples/CryptographicAnalysis/QuantumMining.fsx`](../examples/CryptographicAnalysis/QuantumMining.fsx) - Quantum PoW mining with Grover's algorithm vs Bitcoin

---

## Phase Estimator

### What is Phase Estimator?

**Quantum Phase Estimation (QPE)** estimates the phase φ in U|ψ⟩ = e^(2πiφ)|ψ⟩ for a unitary U and an eigenvector |ψ⟩. It is a core subroutine in Shor's algorithm, the HHL linear system solver and quantum chemistry energy estimation.

### When to Use

✅ **Good Fits**:
- **Algorithm research**: QPE as a building block
- **Education**: Understanding eigenvalue problems
- **Small single-qubit phases**: the built-in unitaries (`TGate`, `SGate`, `PhaseGate`, `RotationZ`)

❌ **Not Suitable For**:
- **Classical eigenvalue problems** (use LAPACK/Eigen libraries)
- **Molecular Hamiltonians**: this builder takes the built-in gate unitaries; see `quantumChemistry` in the [Computation Expressions Reference](computation-expressions-reference.md) for energies
- **High precision**: precision is limited to 20 counting qubits

### API Reference

**Basic Usage**:
```fsharp
open FSharp.Azure.Quantum.Algorithms.QPE
open FSharp.Azure.Quantum.QuantumPhaseEstimator

// Estimate the phase of the T gate
let qpe = phaseEstimator {
    unitary TGate           // Quantum gate/operator
    precision 10            // 10-bit precision
    targetQubits 1          // Number of target qubits
}

match qpe with
| Ok prob ->
    match estimate prob with
    | Ok result ->
        printfn "Phase: %.6f" result.Phase
        printfn "Eigenvalue: %.4f + %.4fi"
            result.Eigenvalue.Real
            result.Eigenvalue.Imaginary
        printfn "Qubits: %d" result.TotalQubits
    | Error err ->
        printfn "Execution Error: %s" err.Message
| Error err ->
    printfn "Builder Error: %s" err.Message
```

**Supported Unitaries** (with the default eigenstate |1⟩):

| Unitary | Description | Phase φ |
|---------|-------------|---------|
| `TGate` | T gate | 1/8 |
| `SGate` | S gate | 1/4 |
| `PhaseGate θ` | Phase gate P(θ) | θ / 2π |
| `RotationZ θ` | Rz(θ) rotation | θ / 4π |

`ModularExponentiation` also exists in `UnitaryOperator`; it is used by period finding.

**Configuration Options**:

| Option | Type | Description | Default |
|--------|------|-------------|---------|
| `unitary` | `UnitaryOperator` | Operator to analyze | `TGate` |
| `precision` | `int` | Counting qubits, i.e. bits of φ (1-20) | 8 |
| `targetQubits` | `int` | Target qubits (1-10; precision + target ≤ 25) | 1 |
| `eigenstate` | `StateVector` | Eigenvector to prepare | \|1⟩ for the single-qubit gates |
| `applySwaps` / `swaps` | `bool` | Apply bit-reversal SWAPs in the circuit | false |
| `exactness` | `Exactness` | `Exact` or `Approximate epsilon` | `Exact` |
| `backend` | `IQuantumBackend` | Quantum backend | LocalBackend |
| `shots` | `int` | Measurement shots; the estimate is the most frequent outcome | 1024 (LocalBackend), 2048 (other) |

**Result Type**:
```fsharp
type PhaseEstimatorResult = {
    Phase: float                          // Estimated phase φ in [0, 1)
    Eigenvalue: System.Numerics.Complex   // λ = e^(2πiφ)
    MeasurementOutcome: int               // Most frequent counting-register outcome
    Precision: int                        // Counting qubits
    TargetQubits: int
    TotalQubits: int                      // Precision + target
    GateCount: int
    Unitary: string
    Success: bool
    Message: string
}
```

### Use Cases

#### Reading a Rotation Angle

**Problem**: Recover the angle of an Rz rotation from its phase. This is the kind of one-qubit stand-in used in the chemistry examples; a real molecular energy needs a Hamiltonian simulation, not a single rotation.

```fsharp
let theta = System.Math.PI / 3.0

let rotationProblem = phaseEstimator {
    unitary (RotationZ theta)
    precision 12
    targetQubits 1
}

match rotationProblem with
| Ok prob ->
    match estimate prob with
    | Ok result ->
        // With the |1⟩ eigenstate, φ = θ / 4π
        let recovered = result.Phase * 4.0 * System.Math.PI
        printfn "Estimated θ: %.6f (actual %.6f)" recovered theta
    | Error err ->
        printfn "Execution Error: %s" err.Message
| Error err ->
    printfn "Builder Error: %s" err.Message
```

#### Phase Gate

```fsharp
let phaseAngle = System.Math.PI / 4.0

let phaseProblem = phaseEstimator {
    unitary (PhaseGate phaseAngle)
    precision 12
}

match phaseProblem with
| Ok prob ->
    match estimate prob with
    | Ok result ->
        printfn "Phase: %.6f (expected %.6f)" result.Phase (phaseAngle / (2.0 * System.Math.PI))
    | Error _ -> ()
| Error _ -> ()
```

#### Algorithm Research: Building Blocks

**Problem**: QPE is a subroutine in Shor's algorithm and the HHL linear solver.

```fsharp
// Educational: Understand QPE fundamentals
let tGateProblem = phaseEstimator {
    unitary TGate
    precision 10
}

match tGateProblem with
| Ok prob ->
    match estimate prob with
    | Ok result ->
        printfn "T-gate eigenvalue: e^(iπ/4)"
        printfn "Estimated phase φ: %.6f" result.Phase
        printfn "Expected phase: 0.125 (1/8)"
        printfn "Error: %.6f" (abs (result.Phase - 0.125))
    | Error _ -> ()
| Error _ -> ()
```

### Complexity

QPE with n counting qubits resolves φ to 1/2^n using n controlled-U^(2^k) applications and an inverse QFT. Its advantage comes when U is a Hamiltonian evolution that a quantum computer can apply efficiently but a classical computer cannot simulate; the single-qubit gates here are for learning how QPE works.

### Precision vs. Qubits

| Precision (bits) | Resolution (1/2^n) | Qubits Required |
|------------------|--------------------|-----------------|
| 8 bits | ≈ 0.0039 | 8 + target |
| 10 bits | ≈ 0.00098 | 10 + target |
| 12 bits | ≈ 0.00024 | 12 + target |
| 16 bits | ≈ 0.000015 | 16 + target |

**Resolution**: the estimate is a multiple of 1/2^n. A phase that is an exact multiple (T gate: 1/8) is found exactly; other phases are rounded to a nearby multiple.

### See Working Examples

- [`examples/PhaseEstimation/MolecularEnergy.fsx`](../examples/PhaseEstimation/MolecularEnergy.fsx) - Phases of T, Rz(θ) and P(φ) read by QPE; the "molecular" scenario is a one-qubit stand-in, not a molecular Hamiltonian

---

## When to Use These Builders

### Decision Matrix

| Builder | Best For | Theoretical speedup | Qubits | Runs locally? |
|---------|----------|---------------------|--------|---------------|
| **Tree Search** | Game AI, decision trees | Quadratic (query count) | depth × ⌈log₂ b⌉ ≤ 16 | ✅ Toy trees |
| **Constraint Solver** | CSP (Sudoku, scheduling) | Quadratic (query count) | log₂(states) ≤ 16 | ✅ Toy problems |
| **Pattern Matcher** | Config and hyperparameter search | Quadratic (query count) | log₂(items) ≤ 16 | ✅ Up to 2^16 items |
| **Quantum Arithmetic** | Crypto demos, research | None (slower than CPU) | up to 2n + 5 | ✅ Small registers |
| **Period Finder** | Factorization, education | Exponential | precision + register | ✅ N ≤ 10000 |
| **Phase Estimator** | QPE research, education | Depends on U | precision + target | ✅ Up to 20 bits |

### Selection Criteria

**Use Tree Search if**:
- You are exploring game trees or decision trees
- The tree fits in 16 qubits

**Use Constraint Solver if**:
- You need any assignment that satisfies all constraints
- The search space has at most 2^16 states

**Use Pattern Matcher if**:
- You are searching configurations or hyperparameters
- The search space has at most 2^16 items
- You want the top-k matches

**Use Quantum Arithmetic if**:
- You are teaching or researching quantum circuits
- You need building blocks for other algorithms (Shor's)
- Never for production arithmetic

**Use Period Finder if**:
- You are demonstrating the quantum threat to RSA
- You are doing security research
- Never for production factorization

**Use Phase Estimator if**:
- You are studying QPE or algorithms built on it
- You are teaching eigenvalue extraction

---

## Query Counts in Theory

The Grover-based builders reduce the number of oracle queries from about N to about √N. The table shows query counts only; it says nothing about wall-clock time or cost. On the local simulator the oracle is evaluated for every state, and on current hardware noise limits circuit depth, so neither shows this saving in practice.

| Problem | Search space N | Classical checks (worst case) | Grover queries (≈ √N) |
|---------|----------------|-------------------------------|-----------------------|
| Tree search, b = 16, d = 4 | 65,536 | 65,536 | 256 |
| Constraint solver, 5 variables over 5 values | 3,125 | 3,125 | 56 |
| Pattern matcher, 1024 configurations | 1,024 | 1,024 | 32 |
| Pattern matcher, 256 hyperparameter sets | 256 | 256 | 16 |

Classical methods often do much better than the worst case: alpha-beta pruning for game trees, constraint propagation for Sudoku, random or Bayesian search for hyperparameters. Compare against those, not against exhaustive search.

For Shor's algorithm the advantage is exponential in theory, but it needs a fault-tolerant quantum computer with thousands of logical qubits for real key sizes.

---

## Troubleshooting

### Tree Search: Problem Rejected

**Symptom**: The builder throws `requires N qubits (depth=..., branching=...). Max: 16`.

**Cause**: maxDepth × ⌈log₂(branchingFactor)⌉ is over 16.

**Fix**: Reduce `maxDepth` or `branchingFactor`, or use `maxPaths`/`limitSearchSpace` to cap the number of paths searched.

```fsharp
// ❌ Rejected: 4 × 6 = 24 qubits
let tooBig () = quantumTreeSearch<int> {
    initialState 0
    maxDepth 4
    branchingFactor 35
    evaluateWith evaluatePosition
    generateMovesWith generateMoves
}

// ✅ Accepted: 2 × 6 = 12 qubits
let fits = quantumTreeSearch<int> {
    initialState 0
    maxDepth 2
    branchingFactor 35
    evaluateWith evaluatePosition
    generateMovesWith generateMoves
}
```

### Constraint Solver: No Solution Found

**Symptom**: `Error: Operation 'GroverSearch' failed: No solution found by quantum search`

**Causes**:
1. **Impossible constraints** (no valid solution exists)
   - *Fix*: Verify the constraints are satisfiable
2. **Too few shots** (the search is probabilistic)
   - *Fix*: Increase `shots`
3. **Search space too large** (over 16 qubits)
   - *Fix*: Reduce the number of variables or the domain size

**Example Fix**:
```fsharp
// ❌ Few shots
let fewShots = constraintSolver<int> {
    searchSpace 3
    domain [1..4]
    satisfies checkAllConstraints
    shots 100
}

// ✅ More shots
let moreShots = constraintSolver<int> {
    searchSpace 3
    domain [1..4]
    satisfies checkAllConstraints
    shots 5000
}
```

### Pattern Matcher: Low Success Probability

**Symptom**: `SuccessProbability < 0.1`

**Causes**:
1. **Pattern too restrictive** (very few matches)
   - *Fix*: Relax the pattern
2. **Search space too large** (over 2^16 items is rejected)
   - *Fix*: Reduce the search space

**Example Fix**:
```fsharp
// Check success probability
match QuantumPatternMatcher.solve configSearch with
| Ok solution when solution.SuccessProbability < 0.1 ->
    printfn "⚠️ Low success probability: %.2f" solution.SuccessProbability
    printfn "Consider: Reduce search space or relax pattern"
| Ok solution ->
    printfn "✅ Good success probability: %.2f" solution.SuccessProbability
| Error err ->
    printfn "Error: %s" err.Message
```

### Period Finder: Period Found But No Factors

**Symptom**: `Period = 6, Factors = None`

**Cause**: Shor's algorithm is **probabilistic**; some periods do not yield factors (for example an odd period).

**Fix**: Allow more attempts with `maxAttempts` (at most 100).

```fsharp
let moreAttempts = periodFinder {
    number 15
    precision 8
    maxAttempts 20  // Increase from default 10
}
```

### Phase Estimator: Large Precision Error

**Symptom**: The estimated phase is far from the expected value.

**Causes**:
1. **Precision too low**
   - *Fix*: Increase `precision` (at most 20)
2. **Hardware noise** (gate errors on a real device)
   - *Fix*: Use error mitigation, or compare against LocalBackend
3. **Wrong eigenstate** for a custom `eigenstate`
   - *Fix*: The state must be an eigenvector of U

**Example Fix**:
```fsharp
// ❌ Low precision
let lowPrecision = phaseEstimator {
    unitary (PhaseGate 1.0)
    precision 6  // Only 6 bits
}

// ✅ Higher precision
let highPrecision = phaseEstimator {
    unitary (PhaseGate 1.0)
    precision 12  // Resolution 1/4096
}
```

**Resolution Table**:

| Precision | Resolution (1/2^n) | Recommended For |
|-----------|--------------------|-----------------|
| 6 bits | ≈ 0.016 | Quick demos |
| 8 bits | ≈ 0.0039 | Basic research |
| 10 bits | ≈ 0.00098 | Standard use |
| 12 bits | ≈ 0.00024 | Finer estimates |
| 16 bits | ≈ 0.000015 | High precision (slow on the simulator) |

---

## Related Documentation

- [Getting Started](getting-started.md) - Installation and first quantum circuit
- [Computation Expressions Reference](computation-expressions-reference.md) - All builders and their operations
- [Quantum Machine Learning](quantum-machine-learning.md) - VQC, kernel SVM, feature maps
- [Business Problem Builders](business-problem-builders.md) - AutoML, fraud detection
- [Error Mitigation](error-mitigation.md) - ZNE, PEC, REM strategies
- [API Reference](api-reference.md) - Complete F# API documentation

---

## Academic References

### Tree Search
- Grover, L. K. (1996). "A fast quantum mechanical algorithm for database search". *Proceedings of STOC*.
- Dürr, C., & Høyer, P. (1996). "A quantum algorithm for finding the minimum". *arXiv:quant-ph/9607014*.

### Constraint Solving
- Cerf, N. J., et al. (2000). "Quantum search by local adiabatic evolution". *Physical Review A*.

### Period Finding (Shor's Algorithm)
- Shor, P. W. (1997). "Polynomial-time algorithms for prime factorization and discrete logarithms on a quantum computer". *SIAM Journal on Computing*, 26(5), 1484-1509.
- Vandersypen, L. M., et al. (2001). "Experimental realization of Shor's quantum factoring algorithm". *Nature*, 414(6866), 883-887.

### Phase Estimation
- Kitaev, A. Y. (1995). "Quantum measurements and the Abelian Stabilizer Problem". *arXiv:quant-ph/9511026*.
- Abrams, D. S., & Lloyd, S. (1999). "Quantum algorithm providing exponential speed increase for finding eigenvalues". *Physical Review Letters*, 83(24), 5162.

### Quantum Arithmetic
- Draper, T. G. (2000). "Addition on a quantum computer". *arXiv:quant-ph/0008033*.
- Beauregard, S. (2003). "Circuit for Shor's algorithm using 2n+3 qubits". *Quantum Information & Computation*, 3(2), 175-185.

---

**Next Steps**: Explore the [working examples](../examples/) to see these builders in action, or jump to [Quantum Machine Learning](quantum-machine-learning.md) for ML applications.

---

**Last Updated**: September 2026
