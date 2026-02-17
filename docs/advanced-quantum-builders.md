# Advanced Quantum Builders

**Target Audience**: Researchers, algorithm developers, quantum computing enthusiasts

This guide covers advanced quantum computation builders designed for specialized research and educational applications. These tools demonstrate cutting-edge quantum algorithms that provide theoretical advantages but require fault-tolerant quantum hardware for practical use.

**⚠️ Current Limitations**: Most features require 100-4000+ qubits with low error rates. Current NISQ (Noisy Intermediate-Scale Quantum) hardware is limited to ~100 qubits with high error rates, so only toy examples work on real quantum computers today.

---

## Table of Contents

1. [Quantum Tree Search](#quantum-tree-search) - Game AI with Grover's algorithm
2. [Quantum Constraint Solver](#quantum-constraint-solver) - CSP solving (Sudoku, N-Queens)
3. [Quantum Pattern Matcher](#quantum-pattern-matcher) - Configuration optimization, hyperparameter tuning
4. [Quantum Arithmetic](#quantum-arithmetic) - Modular arithmetic for cryptography
5. [Period Finder (Shor's Algorithm)](#period-finder-shors-algorithm) - RSA factorization
6. [Phase Estimator](#phase-estimator) - Eigenvalue extraction for quantum chemistry
7. [When to Use These Builders](#when-to-use-these-builders)
8. [Performance & Cost Comparison](#performance--cost-comparison)

---

## Quantum Tree Search

### What is Quantum Tree Search?

**Quantum Tree Search** uses Grover's algorithm to explore game trees and decision trees quadratically faster than classical minimax search. It's ideal for scenarios where position evaluation is expensive (e.g., neural network evaluation in chess engines).

### When to Use

✅ **Good Fits**:
- Game AI (chess, go, gomoku, strategy games)
- Decision trees with expensive evaluation (100ms+ per position)
- Monte Carlo Tree Search (MCTS) acceleration
- Path planning with complex heuristics
- Branching factor: 8-64 moves per position
- Search depth: 2-5 moves ahead

❌ **Not Suitable For**:
- Simple games already solved classically (tic-tac-toe)
- Very deep search (>6 moves ahead on NISQ hardware)
- Fast evaluation functions (<1ms per position)
- Problems with strong alpha-beta pruning

### API Reference

**Basic Usage**:
```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumTreeSearch

let problem = quantumTreeSearch {
    initialState startingGameState
    maxDepth 4                    // Search 4 moves ahead
    branchingFactor 16            // Average moves per position
    evaluateWith evaluatePosition // Evaluation function
    generateMovesWith generateMoves
    topPercentile 0.2             // Consider top 20% of moves
    backend localBackend
    shots 100
}

match solve problem with
| Ok result ->
    printfn "Best move: %d" result.BestMove
    printfn "Evaluation score: %.4f" result.Score
    printfn "Paths explored: %d" result.PathsExplored
| Error err ->
    printfn "Error: %s" err.Message
```

**Configuration Options**:

| Option | Type | Description | Default |
|--------|------|-------------|---------|
| `initialState` | `'State` | Starting game/decision state | *Required* |
| `maxDepth` | `int` | Maximum search depth (plies) | *Required* |
| `branchingFactor` | `int` | Average moves per position | *Required* |
| `evaluateWith` | `'State -> float` | Position evaluation function | *Required* |
| `generateMovesWith` | `'State -> 'State list` | Generate successor states | *Required* |
| `topPercentile` | `float` | Consider top X% of moves (0.0-1.0) | 0.2 |
| `backend` | `IQuantumBackend` | Quantum backend | LocalBackend |
| `shots` | `int` | Number of measurements | 100 |

**Result Type**:
```fsharp
type TreeSearchResult = {
    BestMove: int              // Index of best move
    Score: float               // Evaluation score
    PathsExplored: int         // Number of paths searched
    QubitsRequired: int        // Qubits needed
    QuantumAdvantage: bool     // Whether quantum provided speedup
}
```

### Use Cases

#### Chess Engine with Neural Network Evaluation

**Problem**: Chess engines evaluate millions of positions per second. Deep learning models provide better evaluation but take 100ms+ per position, making deep search impractical.

**Solution**: Quantum tree search reduces evaluations from 16^4 = 65,536 to √65,536 = 256.

**ROI**: 256× speedup enables real-time play with ML evaluation.

```fsharp
let evaluateChessPosition (state: ChessState) : float =
    // Neural network evaluation (expensive: 100ms+)
    neuralNet.Evaluate(state.Board)

let problem = quantumTreeSearch {
    initialState chessInitial
    maxDepth 4
    branchingFactor 35  // Chess average
    evaluateWith evaluateChessPosition
    generateMovesWith generateChessMoves
    backend azureQuantum
}
```

#### Business Decision Trees

**Problem**: Business decisions require market simulations (5-10 minutes each). Exploring all paths is prohibitively expensive.

**Solution**: Quantum search: √512 = 22 simulations vs 512 classical.

**ROI**: 23× speedup: 40 minutes vs 15 hours.

```fsharp
type BusinessState = {
    Marketing: MarketingDecision option
    Pricing: PricingDecision option
    Launch: LaunchDecision option
}

let simulateMarketImpact (state: BusinessState) : float =
    // Monte Carlo market simulation (5-10 minutes)
    runMarketSimulation state

let problem = quantumTreeSearch {
    initialState initialDecision
    maxDepth 3  // 3-stage decision process
    branchingFactor 4
    evaluateWith simulateMarketImpact
    generateMovesWith generateDecisions
}
```

### Quantum Advantage

**Classical Complexity**: O(b^d) where b = branching factor, d = depth
- Example: 16^4 = 65,536 evaluations

**Quantum Complexity**: O(b^d/2) using Grover's algorithm
- Example: √65,536 = 256 evaluations
- **256× speedup**

**Break-even Point**: Evaluation time > 1ms (quantum overhead justified)

### See Working Examples

- [`examples/TreeSearch/GameAI.fsx`](../examples/TreeSearch/GameAI.fsx) - Tic-tac-toe, chess, decision trees

---

## Quantum Constraint Solver

### What is Quantum Constraint Solver?

**Quantum Constraint Solver** uses Grover's algorithm to find solutions to Constraint Satisfaction Problems (CSPs) quadratically faster than classical backtracking search.

### When to Use

✅ **Good Fits**:
- Constraint satisfaction problems (Sudoku, N-Queens)
- Small-to-medium search spaces (10³-10⁶ states)
- Expensive constraint evaluation
- Finding **any** valid solution (not necessarily optimal)

❌ **Not Suitable For**:
- Optimization problems (use QAOA/VQE instead)
- Very large search spaces (>10⁶ states)
- Problems with efficient classical algorithms (e.g., 2-SAT)

### API Reference

**Basic Usage**:
```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumConstraintSolver

let problem = constraintSolver {
    searchSpace 16           // 16 variables
    domain [1..4]            // Each variable in range 1-4
    satisfies checkAllConstraints
    backend localBackend
    shots 1000
}

match solve problem with
| Ok solution ->
    printfn "Solution: %A" solution.Assignment
    printfn "Constraints satisfied: %b" solution.AllConstraintsSatisfied
| Error err ->
    printfn "Error: %s" err.Message
```

**Configuration Options**:

| Option | Type | Description | Default |
|--------|------|-------------|---------|
| `searchSpace` | `int` | Number of variables | *Required* |
| `domain` | `int list` | Possible values for each variable | *Required* |
| `satisfies` | `Map<int,int> -> bool` | Constraint checking function | *Required* |
| `backend` | `IQuantumBackend` | Quantum backend | LocalBackend |
| `shots` | `int` | Number of measurements | 1000 |

**Result Type**:
```fsharp
type ConstraintSolution = {
    Assignment: Map<int, int>  // Variable assignments
    AllConstraintsSatisfied: bool
    SearchSpaceSize: int
    QubitsRequired: int
}
```

### Use Cases

#### Sudoku Solver

**Problem**: Fill a 9×9 grid with numbers 1-9 satisfying row, column, and box constraints.

**Solution**: Quantum search through 9^81 ≈ 10^77 states classically becomes √10^77 ≈ 10^38 quantum.

**Note**: Classical sudoku solvers are highly optimized (constraint propagation). Quantum advantage only for expensive constraint checking.

```fsharp
let checkSudoku (assignment: Map<int, int>) =
    // Check all rows, columns, boxes
    let grid = buildGrid assignment
    rowsValid grid && colsValid grid && boxesValid grid

let problem = constraintSolver {
    searchSpace 81  // 9×9 grid
    domain [1..9]
    satisfies checkSudoku
    backend localBackend
}
```

#### N-Queens Puzzle

**Problem**: Place N queens on an N×N chessboard with no attacks.

**Solution**: Classical: O(N!) backtracking. Quantum: O(√N!) using Grover's algorithm.

```fsharp
let checkQueens (assignment: Map<int, int>) =
    // assignment: row → column
    let positions = Map.toList assignment
    // Check no two queens share column or diagonal
    noDiagonalConflicts positions && uniqueColumns positions

let problem = constraintSolver {
    searchSpace 8  // 8 queens
    domain [0..7]  // Columns 0-7
    satisfies checkQueens
}
```

#### Job Scheduling with Constraints

**Problem**: Assign workers to shifts respecting skills, availability, and no overlaps.

**Solution**: Quantum search through valid assignments.

```fsharp
let checkSchedule (assignment: Map<int, int>) =
    // assignment: shift → worker
    skillsMatch assignment &&
    availabilityMatch assignment &&
    noDuplicateWorkers assignment

let problem = constraintSolver {
    searchSpace 5  // 5 shifts
    domain [0..4]  // 5 workers
    satisfies checkSchedule
}
```

### Quantum Advantage

**Classical Complexity**: O(N) unstructured search
- Example: 3,125 states → 3,125 evaluations

**Quantum Complexity**: O(√N) using Grover's algorithm
- Example: √3,125 = 56 evaluations
- **56× speedup**

### See Working Examples

- [`examples/ConstraintSolver/SudokuSolver.fsx`](../examples/ConstraintSolver/SudokuSolver.fsx) - Sudoku, N-Queens, job scheduling

---

## Quantum Pattern Matcher

### What is Quantum Pattern Matcher?

**Quantum Pattern Matcher** uses Grover's algorithm to find items in a search space that match a pattern predicate. Ideal for configuration optimization and hyperparameter tuning where evaluation is expensive.

### When to Use

✅ **Good Fits**:
- System configuration optimization (database tuning, compiler flags)
- Hyperparameter tuning for ML models
- Feature selection from large feature sets
- A/B testing at scale
- Expensive pattern evaluation (>1 second per check)

❌ **Not Suitable For**:
- Fast pattern matching (<10ms per check)
- Problems with structured search (use constraint solver)
- Very large search spaces (>2^16 items)

### API Reference

**Basic Usage**:
```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumPatternMatcher

// Option 1: Search over explicit list
let problem = patternMatcher {
    searchSpace allConfigurations
    matchPattern (fun config ->
        let perf = runBenchmark config  // Expensive!
        perf.Throughput > 1000.0 && perf.Latency < 50.0
    )
    findTop 10
    backend localBackend
}

// Option 2: Search over indexed space
let problem = patternMatcher {
    searchSpace 256  // 256 combinations
    matchPattern (fun idx ->
        let params = decodeHyperparameters idx
        trainModel params  // Expensive ML training
    )
    findTop 5
}

match solve problem with
| Ok solution ->
    printfn "Matches: %A" solution.Matches
    printfn "Success probability: %.2f" solution.SuccessProbability
| Error err ->
    printfn "Error: %s" err.Message
```

**Configuration Options**:

| Option | Type | Description | Default |
|--------|------|-------------|---------|
| `searchSpace` | `'T list` or `int` | Items to search or size | *Required* |
| `matchPattern` | `'T -> bool` | Pattern matching predicate | *Required* |
| `findTop` | `int` | Number of matches to return | 1 |
| `backend` | `IQuantumBackend` | Quantum backend | LocalBackend |
| `shots` | `int` | Number of measurements | 1000 |

**Result Type**:
```fsharp
type PatternSolution<'T> = {
    Matches: 'T list           // Items matching pattern
    SuccessProbability: float  // Search success probability
    BackendName: string
    QubitsRequired: int
    IterationsUsed: int
    SearchSpaceSize: int
}
```

### Use Cases

#### Database Configuration Tuning

**Problem**: 1000+ database configuration options. Testing each takes 10 minutes (full benchmark suite).

**Solution**: Quantum search: √1000 ≈ 32 tests vs 1000 classical.

**ROI**: 31× speedup: 5 hours vs 1 week.

```fsharp
type DbConfig = {
    CacheSize: int
    MaxConnections: int
    QueryTimeout: int
    // ... 20+ more parameters
}

let testConfig (config: DbConfig) : bool =
    let results = runBenchmarkSuite config  // 10 minutes
    results.Throughput > 10000 &&
    results.P99Latency < 100.0

let problem = patternMatcher {
    searchSpace allDbConfigurations  // 1024 configs
    matchPattern testConfig
    findTop 5  // Top 5 configurations
}
```

#### ML Hyperparameter Tuning

**Problem**: Train model with 256 hyperparameter combinations. Each training run: 1 hour.

**Solution**: Quantum search: √256 = 16 training runs vs 256 classical.

**ROI**: 16× speedup: 16 hours vs 10 days.

```fsharp
let searchSpace = 256  // 8 hyperparameters, 2 values each

let evaluateHyperparameters (idx: int) : bool =
    let params = decodeHyperparameters idx
    let accuracy = trainModel params  // 1 hour!
    accuracy > 0.95

let problem = patternMatcher {
    searchSpace searchSpace
    matchPattern evaluateHyperparameters
    findTop 3
}
```

#### Feature Selection

**Problem**: Select best subset from 100 features. Each feature set requires full model training (30 minutes).

**Solution**: Test √combinations instead of all combinations.

```fsharp
let featureSets = generateFeatureSubsets allFeatures

let testFeatureSet (features: string list) : bool =
    let model = trainModel features  // 30 minutes
    model.Accuracy > 0.90 && features.Length < 20

let problem = patternMatcher {
    searchSpace featureSets
    matchPattern testFeatureSet
    findTop 10
}
```

### Quantum Advantage

**Classical Complexity**: O(N) for N configurations
- Example: 1024 configs → 1024 evaluations

**Quantum Complexity**: O(√N) using Grover's algorithm
- Example: √1024 = 32 evaluations
- **32× speedup**

**Break-even Point**: Evaluation time > 1 second (quantum overhead justified)

### See Working Examples

- Pattern matcher examples integrated into various use cases throughout the examples directory
- Check source: `src/FSharp.Azure.Quantum/Solvers/Quantum/QuantumPatternMatcherBuilder.fs`

---

## Quantum Arithmetic

### What is Quantum Arithmetic?

**Quantum Arithmetic** provides quantum circuit implementations of arithmetic operations (addition, multiplication, modular exponentiation) using the Quantum Fourier Transform (QFT). Used as building blocks for cryptographic algorithms like RSA encryption.

### When to Use

✅ **Good Fits**:
- Building blocks for Shor's algorithm (RSA factorization)
- Cryptographic demonstrations (RSA encryption/decryption)
- Educational quantum circuit examples
- Research into quantum arithmetic algorithms

❌ **Not Suitable For**:
- General-purpose arithmetic (use classical CPU!)
- Production cryptography (use classical libraries)
- Large numbers (>32 bits on NISQ hardware)

### API Reference

**Basic Usage**:
```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumArithmeticOps

// Modular exponentiation: m^e mod n (RSA encryption)
let problem = quantumArithmetic {
    operands message e      // base, exponent
    operation ModularExponentiate
    modulus n              // RSA modulus
    qubits 8               // Sufficient for small numbers
}

match problem with
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

| Operation | Description | Use Case |
|-----------|-------------|----------|
| `ModularExponentiate` | Compute a^b mod n | RSA encryption |
| `ModularMultiply` | Compute (a × b) mod n | Modular arithmetic |
| `ModularAdd` | Compute (a + b) mod n | Basic arithmetic |

**Configuration Options**:

| Option | Type | Description | Default |
|--------|------|-------------|---------|
| `operands` | `int * int` | Input values (a, b) | *Required* |
| `operation` | `ArithmeticOp` | Arithmetic operation | *Required* |
| `modulus` | `int` | Modulus for operations | *Required* |
| `qubits` | `int` | Number of qubits | *Required* |

**Result Type**:
```fsharp
type ArithmeticResult = {
    Value: int            // Computed result
    QubitsUsed: int       // Qubits required
    GateCount: int        // Total quantum gates
    CircuitDepth: int     // Circuit depth
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
let n = p * q  // Modulus n = 33
let e = 3   // Public exponent

let message = 5  // Plaintext

// Encrypt: c = m^e mod n
let encryptOp = quantumArithmetic {
    operands message e
    operation ModularExponentiate
    modulus n
    qubits 8
}

match encryptOp with
| Ok op ->
    match execute op with
    | Ok result ->
        let ciphertext = result.Value
        printfn "Encrypted: %d^%d mod %d = %d" message e n ciphertext
    | Error err ->
        printfn "Execution Error: %s" err.Message
| Error err ->
    printfn "Builder Error: %s" err.Message
```

#### Research: Quantum Circuit Optimization

**Problem**: Optimize quantum arithmetic circuits for gate count and depth.

```fsharp
let testArithmeticCircuit (a: int) (b: int) (n: int) =
    let problem = quantumArithmetic {
        operands a b
        operation ModularMultiply
        modulus n
        qubits 16
    }
    match problem with
    | Ok op ->
        match execute op with
        | Ok result ->
            printfn "Gates: %d, Depth: %d" result.GateCount result.CircuitDepth
        | Error _ -> ()
    | Error _ -> ()
```

### Quantum Advantage

**None for standalone arithmetic** - quantum arithmetic is slower than classical CPU arithmetic.

**Advantage as subroutine** - enables exponential speedup in Shor's algorithm for integer factorization.

### See Working Examples

- [`examples/QuantumArithmetic/RSAEncryption.fsx`](../examples/QuantumArithmetic/RSAEncryption.fsx) - RSA encryption demo

---

## Period Finder (Shor's Algorithm)

### What is Period Finder?

**Period Finder** implements **Shor's algorithm** for integer factorization, which can break RSA encryption by finding the period of modular exponentiation. This is the most famous quantum algorithm demonstrating exponential speedup over classical methods.

### When to Use

✅ **Good Fits**:
- **Research**: Understanding Shor's algorithm
- **Education**: Demonstrating quantum threat to RSA
- **Security Analysis**: Assessing post-quantum cryptography needs

❌ **Not Suitable For**:
- **Production cryptanalysis** (requires fault-tolerant quantum computer)
- **Current hardware** (limited to toy examples, n < 1000)
- **Classical factorization** (use GNFS algorithm instead)

**⚠️ Quantum Threat Timeline**:
- **Today (2024-2025)**: Cannot factor RSA-2048 (need ~4096 qubits, have ~100)
- **2025-2030**: NISQ era, still insufficient for real RSA keys
- **2030+**: Fault-tolerant quantum computers may break RSA-2048

### API Reference

**Basic Usage**:
```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumPeriodFinder

// Factor integer n
let problem = periodFinder {
    number 15           // Number to factor
    precision 8         // QPE precision (qubits)
    maxAttempts 10      // Probabilistic algorithm
}

match problem with
| Ok prob ->
    match solve prob with
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

// Or use convenience function
let problem2 = factorInteger 143 8  // n=143, precision=8
```

**Configuration Options**:

| Option | Type | Description | Default |
|--------|------|-------------|---------|
| `number` | `int` | Integer to factor | *Required* |
| `precision` | `int` | QPE precision (qubits) | *Required* |
| `maxAttempts` | `int` | Maximum retry attempts | 10 |

**Result Type**:
```fsharp
type PeriodResult = {
    Base: int                  // Base used (random)
    Period: int                // Period found
    Factors: (int * int) option  // Prime factors (if found)
    QubitsUsed: int           // Qubits required
    Attempts: int             // Attempts taken
}
```

### Use Cases

#### Security Assessment: RSA Key Strength

**Problem**: Assess how long current RSA keys remain secure against quantum attacks.

```fsharp
// Small RSA modulus (educational)
let smallRSA = 15  // 3 × 5

let problem = periodFinder {
    number smallRSA
    precision 4  // Reduced for local simulation
    maxAttempts 10
}

match problem with
| Ok prob ->
    match solve prob with
    | Ok result ->
        match result.Factors with
        | Some (p, q) ->
            printfn "RSA BROKEN: %d = %d × %d" smallRSA p q
            printfn "Attacker can now:"
            printfn "  1. Calculate φ(n) = (p-1)(q-1)"
            printfn "  2. Derive private key from public key"
            printfn "  3. Decrypt all encrypted messages"
        | None ->
            printfn "Period found but no factors (try again)"
    | Error err ->
        printfn "Execution Error: %s" err.Message
| Error err ->
    printfn "Builder Error: %s" err.Message
```

#### Educational: Understanding Shor's Algorithm

**Problem**: Teach students how quantum computers threaten public-key cryptography.

```fsharp
let demonstrateShor (n: int) (precision: int) =
    printfn "Factoring %d using Shor's Algorithm" n
    printfn "Classical difficulty: Exponential (GNFS)"
    printfn "Quantum complexity: Polynomial time"
    printfn ""
    
    let problem = factorInteger n precision
    match problem with
    | Ok prob ->
        match solve prob with
        | Ok result ->
            printfn "✅ Quantum computer found factors!"
            printfn "Period: %d" result.Period
            printfn "Factors: %A" result.Factors
        | Error _ ->
            printfn "❌ Failed to find factors"
    | Error _ ->
        printfn "❌ Failed (NISQ hardware too limited)"

// Test with small numbers
demonstrateShor 15 4   // 3 × 5
demonstrateShor 143 8  // 11 × 13
```

#### Research: Post-Quantum Cryptography

**Problem**: Motivate transition to quantum-resistant algorithms (NIST standards).

```fsharp
let assessQuantumThreat (rsaKeyBits: int) =
    let qubitsNeeded = rsaKeyBits * 2  // Rough estimate
    let currentQubits = 100  // IBM Quantum, Google Sycamore
    
    printfn "RSA Key Size: %d bits" rsaKeyBits
    printfn "Qubits Required: ~%d" qubitsNeeded
    printfn "Current Hardware: ~%d qubits" currentQubits
    
    if qubitsNeeded > currentQubits then
        printfn "Status: ✅ SAFE (for now)"
        let yearsUntilThreat = (qubitsNeeded - currentQubits) / 20  // ~20 qubits/year
        printfn "Estimated threat: ~%d years" yearsUntilThreat
    else
        printfn "Status: ⚠️ VULNERABLE"
        printfn "Recommendation: Migrate to post-quantum crypto NOW"

assessQuantumThreat 2048  // Standard RSA key
assessQuantumThreat 4096  // High-security RSA key
```

### Quantum Advantage

**Classical Complexity**: O(e^(∛(log N))) using General Number Field Sieve (GNFS)
- RSA-2048: ~2^112 operations (~10^33 years on modern CPU)

**Quantum Complexity**: O((log N)^2 × log log N) using Shor's algorithm
- RSA-2048: ~10^9 operations (~hours on fault-tolerant quantum computer)

**Exponential speedup**: From intractable to practical.

### Hardware Requirements

| RSA Key Size | Qubits Required | Error Rate Required | Available Today? |
|--------------|-----------------|---------------------|------------------|
| 15-bit (toy) | ~10 qubits | 10^-2 | ✅ Yes (LocalBackend) |
| 100-bit | ~200 qubits | 10^-3 | ❌ No |
| 2048-bit (standard) | ~4,096 qubits | 10^-6 | ❌ No (need fault tolerance) |
| 4096-bit (high-security) | ~8,192 qubits | 10^-6 | ❌ No |

### See Working Examples

- [`examples/CryptographicAnalysis/RSAFactorization.fsx`](../examples/CryptographicAnalysis/RSAFactorization.fsx) - Shor's algorithm for RSA factorization
- [`examples/CryptographicAnalysis/DiscreteLogAttack.fsx`](../examples/CryptographicAnalysis/DiscreteLogAttack.fsx) - Quantum discrete logarithm attack
- [`examples/CryptographicAnalysis/GroverAESThreat.fsx`](../examples/CryptographicAnalysis/GroverAESThreat.fsx) - Grover's algorithm threat to AES
- [`examples/CryptographicAnalysis/ECCBitcoinThreat.fsx`](../examples/CryptographicAnalysis/ECCBitcoinThreat.fsx) - Quantum ECDLP threat to Bitcoin/ECC
- [`examples/CryptographicAnalysis/QuantumMining.fsx`](../examples/CryptographicAnalysis/QuantumMining.fsx) - Quantum PoW mining with Grover's algorithm vs Bitcoin

---

## Phase Estimator

### What is Phase Estimator?

**Quantum Phase Estimation (QPE)** extracts eigenvalues from unitary operators exponentially faster than classical methods. It's a core subroutine in quantum chemistry (VQE), Shor's algorithm, and HHL linear system solver.

### When to Use

✅ **Good Fits**:
- **Quantum Chemistry**: Molecular energy calculations (drug discovery)
- **Materials Science**: Electronic band structure (semiconductors, batteries)
- **Algorithm Research**: Building block for other quantum algorithms
- **Education**: Understanding eigenvalue problems

❌ **Not Suitable For**:
- **Classical eigenvalue problems** (use LAPACK/Eigen libraries)
- **Large molecules** (>50 atoms requires fault-tolerant hardware)
- **High precision** (>16 bits needs low-error qubits)

### API Reference

**Basic Usage**:
```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumPhaseEstimator

// Estimate phase of a quantum gate
let problem = phaseEstimator {
    unitary TGate           // Quantum gate/operator
    precision 10            // 10-bit precision
    targetQubits 1          // Number of target qubits
}

match problem with
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

**Supported Unitaries**:

| Unitary Type | Description | Use Case |
|--------------|-------------|----------|
| `TGate` | T-gate (π/4 phase) | Educational |
| `SGate` | S-gate (π/2 phase) | Educational |
| `RotationZ θ` | Rz(θ) rotation | Molecular Hamiltonian |
| `PhaseGate θ` | Phase shift | Material science |

**Configuration Options**:

| Option | Type | Description | Default |
|--------|------|-------------|---------|
| `unitary` | `UnitaryType` | Operator to analyze | *Required* |
| `precision` | `int` | Precision in bits (qubits) | *Required* |
| `targetQubits` | `int` | Number of target qubits | 1 |

**Result Type**:
```fsharp
type PhaseResult = {
    Phase: float              // Estimated phase φ
    Eigenvalue: Complex       // λ = e^(2πiφ)
    TotalQubits: int          // Qubits used
    GateCount: int            // Total gates
    Precision: int            // Precision bits
}
```

### Use Cases

#### Drug Discovery: Molecular Energy Calculation

**Problem**: Calculate ground state energy of drug molecule to predict binding affinity.

**Classical Method**: Density Functional Theory (DFT) - hours to days for large molecules.

**Quantum Method**: QPE extracts eigenvalues in polynomial time.

```fsharp
let theta = System.Math.PI / 3.0  // Simplified Hamiltonian

let molecularProblem = phaseEstimator {
    unitary (RotationZ theta)  // Molecular Hamiltonian
    precision 12               // High precision for accuracy
    targetQubits 1
}

match molecularProblem with
| Ok prob ->
    match estimate prob with
    | Ok result ->
        // Convert phase to energy (in atomic units)
        let energy = result.Phase * 2.0 * System.Math.PI
        
        printfn "Ground State Energy: %.6f a.u." energy
        printfn "Binding Affinity: %s" 
            (if energy < 0.5 then "STRONG" else "WEAK")
        printfn ""
        printfn "Pharmaceutical Impact:"
        printfn "  • Lower energy = More stable configuration"
        printfn "  • Predicts drug-protein binding"
        printfn "  • Guides molecular design"
    | Error err ->
        printfn "Execution Error: %s" err.Message
| Error err ->
    printfn "Builder Error: %s" err.Message
```

#### Materials Science: Electronic Band Structure

**Problem**: Predict semiconductor band gaps for solar cells and transistors.

**Application**: Battery materials, superconductors, photovoltaics.

```fsharp
let phaseAngle = System.Math.PI / 4.0  // Crystal lattice phase

let materialProblem = phaseEstimator {
    unitary (PhaseGate phaseAngle)
    precision 12
}

match materialProblem with
| Ok prob ->
    match estimate prob with
    | Ok result ->
        let blochPhase = result.Phase
        printfn "Bloch Phase: %.6f" blochPhase
        printfn ""
        printfn "Industrial Applications:"
        printfn "  • Semiconductor design (optimize band gaps)"
        printfn "  • Solar cell efficiency prediction"
        printfn "  • Superconductor discovery"
        printfn "  • Battery material optimization"
    | Error _ -> ()
| Error _ -> ()
```

#### Algorithm Research: Building Blocks

**Problem**: QPE is a subroutine in Shor's algorithm and HHL linear solver.

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

### Quantum Advantage

**Classical Complexity**: O(2^n) for n-qubit systems (exponential)
- 50-qubit system: 2^50 ≈ 10^15 states (intractable)

**Quantum Complexity**: O(log(1/ε) × poly(n)) where ε = precision
- 50-qubit system: Polynomial time (practical)

**Exponential speedup** for quantum chemistry and materials science.

### Precision vs. Qubits

| Precision (bits) | Accuracy | Qubits Required | Use Case |
|------------------|----------|-----------------|----------|
| 8 bits | ±0.4% | 8 + target | Educational |
| 10 bits | ±0.1% | 10 + target | Research |
| 12 bits | ±0.02% | 12 + target | Drug discovery |
| 16 bits | ±0.001% | 16 + target | High-precision chemistry |

**Accuracy Formula**: Error ≤ 1/2^n where n = precision bits

### See Working Examples

- [`examples/PhaseEstimation/MolecularEnergy.fsx`](../examples/PhaseEstimation/MolecularEnergy.fsx) - Drug discovery, materials science

---

## When to Use These Builders

### Decision Matrix

| Builder | Best For | Speedup | Qubit Requirement | NISQ-Ready? |
|---------|----------|---------|-------------------|-------------|
| **Tree Search** | Game AI, decision trees | O(√N) | log(b^d) ≈ d log b | ✅ Yes (depth ≤ 5) |
| **Constraint Solver** | CSP (Sudoku, scheduling) | O(√N) | log(N) | ✅ Yes (N < 10^6) |
| **Pattern Matcher** | Config optimization, hyperparameter tuning | O(√N) | log(N) | ✅ Yes (N < 10^5) |
| **Quantum Arithmetic** | Crypto demos, research | None (slower!) | O(log n) | ⚠️ Toy examples only |
| **Period Finder** | RSA factorization, education | Exponential | 2n (n = bit length) | ❌ No (toy only) |
| **Phase Estimator** | Quantum chemistry, materials | Exponential | precision + target | ⚠️ Limited precision |

### Selection Criteria

**Use Tree Search if**:
- Exploring game trees or decision trees
- Evaluation function is expensive (>10ms)
- Moderate branching factor (8-64)
- Search depth: 2-5 moves

**Use Constraint Solver if**:
- Need to satisfy multiple constraints
- Search space: 10^3 - 10^6 states
- Constraint checking is expensive
- Any valid solution is acceptable (not optimal)

**Use Pattern Matcher if**:
- Testing configurations or hyperparameters
- Evaluation is very expensive (>1 second)
- Search space: < 2^16 items
- Need top-k matches

**Use Quantum Arithmetic if**:
- Educational/research only
- Building blocks for other algorithms (Shor's)
- DO NOT use for production arithmetic!

**Use Period Finder if**:
- Educational demos of quantum threat to RSA
- Security research (assessing quantum timeline)
- NOT for production factorization (use classical methods)

**Use Phase Estimator if**:
- Quantum chemistry eigenvalue problems
- Materials science band structure
- Research into quantum algorithms
- Educational eigenvalue extraction

---

## Performance & Cost Comparison

### Tree Search: Chess Position Analysis

| Method | Evaluations | Time (100ms/eval) | Cost (Cloud) |
|--------|-------------|-------------------|--------------|
| Classical Minimax | 16^4 = 65,536 | 109 minutes | N/A |
| Alpha-Beta Pruning | ~6,000 | 10 minutes | N/A |
| **Quantum Tree Search** | √65,536 = 256 | **26 seconds** | $2-5 per search |

**ROI**: 250× speedup over minimax, 23× over alpha-beta.

**Break-even**: Evaluation time > 1ms (quantum overhead justified).

### Constraint Solver: Sudoku 9×9

| Method | States Explored | Time | Notes |
|--------|-----------------|------|-------|
| Classical Backtracking | ~10^9 | Seconds | With constraint propagation |
| **Quantum Grover** | ~10^4 | Milliseconds (in theory) | No advantage (classical is optimized) |

**Verdict**: ❌ No practical advantage - classical sudoku solvers are highly optimized.

**When quantum wins**: Expensive constraint checking (not sudoku).

### Pattern Matcher: Hyperparameter Tuning

| Method | Trials | Time (1hr/trial) | Cost |
|--------|--------|------------------|------|
| Grid Search | 256 | 10.5 days | $0 (local compute) |
| Random Search | 50 | 2 days | $0 |
| Bayesian Optimization | 30 | 1.25 days | $0 |
| **Quantum Pattern Match** | √256 = 16 | **16 hours** | $50-100 (cloud qubits) |

**ROI**: 16× speedup over grid search, 3× over random search, 2× over Bayesian.

**Break-even**: Evaluation time > 30 minutes (justify quantum cost).

### Period Finder: RSA Factorization

| RSA Key Size | Classical Time (GNFS) | Quantum Time (Shor's) | Qubits Needed | Available? |
|--------------|----------------------|----------------------|---------------|------------|
| 15-bit (toy) | Microseconds | Milliseconds | 10 | ✅ LocalBackend |
| 100-bit | Minutes | Seconds | 200 | ❌ No |
| 768-bit | Years | Hours | 1,536 | ❌ No |
| **2048-bit** (standard) | **Billions of years** | **Hours** | **4,096** | ❌ No (2030+?) |

**Verdict**: Exponential advantage BUT requires fault-tolerant quantum computer (not available yet).

### Phase Estimator: Molecular Energy

| System Size | Classical (DFT) | Quantum (QPE) | Qubits Needed | Available? |
|-------------|-----------------|---------------|---------------|------------|
| H2 (2 atoms) | Seconds | Milliseconds | 10 | ✅ Yes |
| H2O (3 atoms) | Minutes | Seconds | 20 | ⚠️ Limited |
| Aspirin (21 atoms) | Hours | Minutes | 50 | ❌ No |
| Protein (1000+ atoms) | Impossible | Tractable | 2000+ | ❌ No (future) |

**Verdict**: Exponential advantage for large molecules - awaiting fault-tolerant hardware.

---

## Troubleshooting

### Tree Search: No Quantum Advantage

**Symptom**: `QuantumAdvantage = false` in results.

**Causes**:
1. **Branching factor too low** (< 8 moves)
   - *Fix*: Quantum search needs moderate-to-large branching (8-64)

2. **Search depth too shallow** (depth < 2)
   - *Fix*: Increase `maxDepth` to 3-5

3. **Evaluation too fast** (< 1ms)
   - *Fix*: Quantum overhead not justified for fast evaluation

**Example Fix**:
```fsharp
// ❌ No advantage
let problem = quantumTreeSearch {
    maxDepth 1           // Too shallow!
    branchingFactor 4    // Too low!
    // ...
}

// ✅ Quantum advantage
let problem = quantumTreeSearch {
    maxDepth 4           // Deeper search
    branchingFactor 16   // Moderate branching
    // ...
}
```

### Constraint Solver: No Solution Found

**Symptom**: `Error: No solution found after 1000 shots`

**Causes**:
1. **Impossible constraints** (no valid solution exists)
   - *Fix*: Verify constraints are satisfiable

2. **Too few shots** (probabilistic algorithm)
   - *Fix*: Increase `shots` to 5000-10000

3. **Search space too large** (>10^6 states)
   - *Fix*: Reduce search space or use classical solver

**Example Fix**:
```fsharp
// ❌ Too few shots
let problem = constraintSolver {
    shots 100  // Too low!
    // ...
}

// ✅ More shots
let problem = constraintSolver {
    shots 5000  // Better success rate
    // ...
}
```

### Pattern Matcher: Low Success Probability

**Symptom**: `SuccessProbability < 0.1`

**Causes**:
1. **Pattern too restrictive** (very few matches)
   - *Fix*: Relax pattern or increase search space

2. **Search space too large** (>2^16)
   - *Fix*: Reduce search space size

**Example Fix**:
```fsharp
// Check success probability
match solve problem with
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

**Cause**: Shor's algorithm is **probabilistic** - period may not yield factors.

**Fix**: Retry with `maxAttempts` increased.

```fsharp
let problem = periodFinder {
    number 15
    precision 8
    maxAttempts 20  // Increase from default 10
}
```

**Success Rate**: Typically 50-75% chance per attempt. With 20 attempts: >99.9% success.

### Phase Estimator: Large Precision Error

**Symptom**: `Error = 0.05` (expected < 0.001)

**Causes**:
1. **Precision too low** (< 10 bits)
   - *Fix*: Increase `precision` to 12-16 bits

2. **NISQ hardware noise** (gate errors)
   - *Fix*: Use error mitigation or LocalBackend

**Example Fix**:
```fsharp
// ❌ Low precision
let problem = phaseEstimator {
    precision 6  // Only 6 bits!
    // ...
}

// ✅ High precision
let problem = phaseEstimator {
    precision 12  // 12 bits (±0.02% accuracy)
    // ...
}
```

**Accuracy Table**:

| Precision | Accuracy | Recommended For |
|-----------|----------|-----------------|
| 6 bits | ±1.6% | Educational demos |
| 8 bits | ±0.4% | Basic research |
| 10 bits | ±0.1% | Standard use |
| 12 bits | ±0.02% | Drug discovery |
| 16 bits | ±0.001% | High-precision chemistry |

---

## Related Documentation

- [Getting Started](getting-started.md) - Installation and first quantum circuit
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

**Next Steps**: Explore [working examples](../examples/) to see these builders in action, or jump to [Quantum Machine Learning](quantum-machine-learning.md) for practical ML applications.

---

**Last Updated**: December 2025
