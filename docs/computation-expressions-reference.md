# Computation Expressions (CE) Reference

This document provides a complete reference for all computation expressions (CEs) available in FSharp.Azure.Quantum. Use this as a quick lookup when F# IntelliSense is not showing CE operations.

## Overview

Computation expressions provide a declarative, F#-idiomatic way to construct quantum problems, circuits, and schedules. Each CE supports custom operations that are context-specific to its domain.

## Quick Reference Table

| CE Name | Description | Custom Operations |
|---------|-------------|-------------------|
| **anomalyDetection** | Detect outliers and anomalies in data | `trainOnNormalData`, `sensitivity`, `contaminationRate`, `backend`, `shots`, `verbose`, `saveModelTo`, `note`, `progressReporter`, `cancellationToken` |
| **autoML** | Automated ML - finds best model automatically | `trainWith`, `tryBinaryClassification`, `tryMultiClass`, `tryAnomalyDetection`, `tryRegression`, `trySimilaritySearch`, `tryArchitectures`, `maxTrials`, `maxTimeMinutes`, `validationSplit`, `backend`, `verbose`, `saveModelTo`, `randomSeed`, `progressReporter`, `cancellationToken` |
| **binaryClassification** | Classify items into two categories | `trainWith`, `architecture`, `learningRate`, `maxEpochs`, `convergenceThreshold`, `backend`, `shots`, `verbose`, `saveModelTo`, `note`, `progressReporter`, `cancellationToken` |
| **circuit** | Build quantum circuits with gates and loops | `qubits`, `H`, `X`, `Y`, `Z`, `S`, `SDG`, `T`, `TDG`, `P`, `RX`, `RY`, `RZ`, `CNOT`, `CZ`, `CP`, `SWAP`, `CCX` |
| **coloredNode** | Define a node in graph coloring problem | `nodeId`, `conflictsWith`, `fixedColor`, `priority`, `avoidColors`, `property` |
| **constraintSolver<'T>** | Define constraint satisfaction problems (CSP) | `searchSpace`, `domain`, `satisfies`, `backend`, `maxIterations`, `shots` |
| **drugDiscovery** | Virtual screening for drug discovery | `load_candidates_from_file`, `load_candidates_from_provider`, `load_candidates_from_provider_async`, `target_protein_from_pdb`, `use_method`, `use_feature_map`, `set_batch_size`, `shots`, `backend`, `vqc_layers`, `vqc_max_epochs`, `selection_budget`, `diversity_weight` |
| **graphColoring** | Define graph coloring optimization problems | `node`, `nodes`, `colors`, `maxColors`, `objective`, `conflictPenalty` |
| **patternMatcher<'T>** | Define quantum pattern matching problems | `searchSpace`, `searchSpaceSize`, `matchPattern`, `findTop`, `backend`, `maxIterations`, `shots` |
| **periodFinder** | Define period finding (Shor's algorithm) | `number`, `chosenBase`, `precision`, `maxAttempts`, `backend`, `shots` |
| **phaseEstimator** | Define quantum phase estimation (QPE) | `unitary`, `precision`, `targetQubits`, `eigenstate`, `backend`, `shots` |
| **predictiveModel** | Predict continuous values or categories | `trainWith`, `problemType`, `architecture`, `learningRate`, `maxEpochs`, `convergenceThreshold`, `backend`, `shots`, `verbose`, `saveModelTo`, `note`, `progressReporter`, `cancellationToken` |
| **quantumArithmetic** | Define quantum arithmetic operations | `operands`, `operandA`, `operandB`, `operation`, `modulus`, `qubits`, `exponent`, `backend`, `shots` |
| **quantumTreeSearch<'T>** | Define quantum tree search (game AI, decision trees) | `initialState`, `maxDepth`, `branchingFactor`, `evaluateWith`, `generateMovesWith`, `topPercentile`, `backend`, `shots`, `solutionThreshold`, `successThreshold`, `maxPaths`, `limitSearchSpace`, `maxIterations` |
| **resource<'T>** | Define scheduling resources | `resourceId`, `capacity`, `costPerUnit`, `availableWindow` |
| **scheduledTask<'T>** | Define tasks for scheduling | `taskId`, `duration`, `after`, `afterMultiple`, `requires`, `priority`, `deadline`, `earliestStart` |
| **schedulingProblem<'T>** | Define complete scheduling problems | `tasks`, `resources`, `objective`, `timeHorizon` |
| **similaritySearch** | Find similar items using quantum kernels | `indexItems`, `similarityMetric`, `threshold`, `backend`, `shots`, `verbose`, `saveIndexTo`, `note`, `progressReporter`, `cancellationToken` |
| **optionPricing** | Price financial options using quantum Monte Carlo | `spotPrice`, `strikePrice`, `riskFreeRate`, `volatility`, `expiry`, `optionType`, `qubits`, `iterations`, `shots`, `backend`, `cancellation_token` |
| **topological** | Topological quantum computing with anyons | `let!`/`do!` syntax with `initialize`, `braid`, `measure`, `braidSequence`, `getState`, `getResults`, `getLog` |
| **quantumChemistry** | Quantum chemistry ground state calculations | `molecule`, `basis`, `ansatz`, `optimizer`, `maxIterations`, `initialParameters`, `molecule_from_xyz`, `molecule_from_fcidump`, `molecule_from_provider`, `molecule_from_name` |

---

## Detailed Documentation

### 1. circuit

**Module**: `FSharp.Azure.Quantum.CircuitBuilder`

**Purpose**: Declaratively construct quantum circuits with automatic validation

**Example**:
```fsharp
let bellState = circuit {
    qubits 2
    H 0
    CNOT (0, 1)
}
```

**Custom Operations**:
- `qubits` - Set number of qubits (required first)
- **Single-Qubit Gates**: `H`, `X`, `Y`, `Z`, `S`, `SDG`, `T`, `TDG`, `P`
- **Rotation Gates**: `RX`, `RY`, `RZ`
- **Two-Qubit Gates**: `CNOT`, `CZ`, `CP`, `SWAP`
- **Three-Qubit Gates**: `CCX` (Toffoli)

**Features**:
- ✅ Supports `for` loops for applying gates to multiple qubits
- ✅ Automatic circuit validation on construction
- ✅ Composable subcircuits with `Combine`

---

### 2. coloredNode

**Module**: `FSharp.Azure.Quantum.GraphColoring`

**Purpose**: Define individual nodes in a graph coloring problem

**Example**:
```fsharp
let node1 = coloredNode {
    nodeId "R1"
    conflictsWith ["R2"; "R3"]
    priority 1.0
}
```

**Custom Operations**:
- `nodeId` - Unique identifier for the node
- `conflictsWith` - List of node IDs that conflict (cannot have same color)
- `fixedColor` - Pre-assign a specific color
- `priority` - Priority for tie-breaking (higher = assign first)
- `avoidColors` - Colors to avoid if possible (soft constraint)
- `property` - Add metadata key-value pair

---

### 3. constraintSolver<'T>

**Module**: `FSharp.Azure.Quantum.QuantumConstraintSolver`

**Purpose**: Solve constraint satisfaction problems (CSP) using Grover's algorithm

**Example**:
```fsharp
let sudokuRow = constraintSolver<int> {
    searchSpace 9
    domain [1..9]
    satisfies (fun vars -> allDifferent vars)
    shots 1000
}
```

**Custom Operations**:
- `searchSpace` - Number of variables (or search space size in bits)
- `domain` - Domain of values for each variable
- `satisfies` - Add constraint predicate (all must be satisfied)
- `backend` - Quantum backend to use (None = LocalBackend)
- `maxIterations` - Maximum Grover iterations for amplitude amplification
- `shots` - Number of measurement shots (None = auto-scale: 1000 for Local, 2000 for Cloud)

**Features**:
- ✅ Supports `for` loops to add multiple constraints
- ✅ Uses Grover's algorithm for O(√N) speedup
- ✅ Generic over variable domain type

---

### 4. graphColoring

**Module**: `FSharp.Azure.Quantum.GraphColoring`

**Purpose**: Define complete graph coloring optimization problems

**Example**:
```fsharp
let problem = graphColoring {
    node (coloredNode { nodeId "R1"; conflictsWith ["R2"] })
    node (coloredNode { nodeId "R2"; conflictsWith ["R1"; "R3"] })
    colors ["EAX"; "EBX"; "ECX"]
    objective MinimizeColors
}
```

**Custom Operations**:
- `node` - Add a single node
- `nodes` - Add multiple nodes at once
- `colors` - Available colors to assign
- `maxColors` - Maximum colors to use (for chromatic number constraint)
- `objective` - Optimization objective (MinimizeColors | MinimizeConflicts | BalanceColors)
- `conflictPenalty` - Penalty weight for conflicts

---

### 5. patternMatcher<'T>

**Module**: `FSharp.Azure.Quantum.QuantumPatternMatcher`

**Purpose**: Search for items matching complex patterns using Grover's algorithm

**Example**:
```fsharp
let search = patternMatcher<Config> {
    searchSpace allConfigs
    matchPattern (fun cfg -> cfg.Performance > 0.8 && cfg.Cost < 100.0)
    findTop 5
    shots 500
}
```

**Custom Operations**:
- `searchSpace` - List of items to search through
- `searchSpaceSize` - Alternative: specify search space size as integer
- `matchPattern` - Pattern predicate (returns true if item matches)
- `findTop` - Number of top matches to return
- `backend` - Quantum backend to use (None = LocalBackend)
- `maxIterations` - Maximum Grover iterations
- `shots` - Number of measurement shots (None = auto-scale)

**Features**:
- ✅ Supports `for` loops to combine multiple patterns with AND logic
- ✅ Uses Grover's algorithm for O(√N) speedup
- ✅ Generic over item type

---

### 6. periodFinder

**Module**: `FSharp.Azure.Quantum.QuantumPeriodFinder`

**Purpose**: Find periods in modular exponentiation (Shor's factorization algorithm)

**Example**:
```fsharp
let problem = periodFinder {
    number 15
    precision 12
    maxAttempts 10
    shots 2048
}
```

**Custom Operations**:
- `number` - Number to factor (N > 3, composite)
- `chosenBase` - Random base a < N (coprime to N). If not specified, auto-selects
- `precision` - QPE precision qubits (recommended: 2*log₂(N) + 3)
- `maxAttempts` - Maximum retry attempts if period finding fails
- `backend` - Quantum backend to use (None = LocalBackend)
- `shots` - Number of QPE measurement shots (None = auto-scale: 1024 for Local, 2048 for Cloud)

**Notes**:
- Uses Quantum Phase Estimation (QPE) internally
- Higher `precision` = better success rate but more qubits required
- Higher `shots` = better phase estimate accuracy

---

### 7. phaseEstimator

**Module**: `FSharp.Azure.Quantum.QuantumPhaseEstimator`

**Purpose**: Estimate eigenvalues of unitary operators using Quantum Phase Estimation (QPE)

**Example**:
```fsharp
let problem = phaseEstimator {
    unitary TGate
    precision 8
    targetQubits 1
    shots 1024
}
```

**Custom Operations**:
- `unitary` - Unitary operator U to estimate phase of
- `precision` - Number of counting qubits (n bits precision for φ)
- `targetQubits` - Number of target qubits for eigenvector |ψ⟩ (default: 1)
- `eigenstate` - Initial eigenvector |ψ⟩ (None = use |0⟩)
- `backend` - Quantum backend to use (None = LocalBackend)
- `shots` - Number of measurement shots (None = auto-scale: 1024 for Local, 2048 for Cloud)

**Notes**:
- Higher `precision` = more accurate phase estimate
- Higher `shots` = better statistical accuracy
- Used internally by period finding and many quantum algorithms

---

### 8. quantumArithmetic

**Module**: `FSharp.Azure.Quantum.QuantumArithmeticOps`

**Purpose**: Perform quantum arithmetic operations (addition, multiplication, exponentiation)

**Example**:
```fsharp
let operation = quantumArithmetic {
    operands 42 17
    operation Add
    qubits 8
    shots 100
}
```

**Custom Operations**:
- `operands` - Set both operands (a, b)
- `operandA` - Set first operand
- `operandB` - Set second operand (or exponent for exponentiation)
- `operation` - Operation type (Add | Multiply | ModularAdd | ModularMultiply | ModularExponentiate)
- `modulus` - Modulus for modular operations (required for modular ops)
- `qubits` - Number of qubits for computation
- `exponent` - Alias for operandB in exponentiation context
- `backend` - Quantum backend to use (None = LocalBackend)
- `shots` - Number of measurement shots (None = auto-scale: 100 for Local, 500 for Cloud)

**Notes**:
- Arithmetic operations are deterministic
- `shots` used for statistical verification on noisy hardware

---

### 9. quantumTreeSearch<'T>

**Module**: `FSharp.Azure.Quantum.QuantumTreeSearch`

**Purpose**: Search game trees and decision trees using Grover's algorithm

**Example**:
```fsharp
let search = quantumTreeSearch<Board> {
    initialState gameBoard
    maxDepth 3
    branchingFactor 9
    evaluateWith (fun board -> evaluatePosition board)
    generateMovesWith (fun board -> getLegalMoves board)
    topPercentile 0.2
    shots 100
    maxIterations 5
}
```

**Custom Operations**:
- `initialState` - Starting game/decision state
- `maxDepth` - Maximum depth to explore in tree
- `branchingFactor` - Expected branching factor (moves per position)
- `evaluateWith` - Heuristic evaluation function (higher score = better)
- `generateMovesWith` - Move generation function (returns list of next states)
- `topPercentile` - Fraction of best moves to amplify (0.0 < x ≤ 1.0)
- `backend` - Quantum backend to use (None = LocalBackend)
- `shots` - Number of measurements (None = auto-scale: 100 for Local, 500 for Cloud)
- `solutionThreshold` - Min fraction of shots to consider as solution (None = auto-scale: 1% for Local, 2% for Cloud)
- `successThreshold` - Min total probability for search success (None = auto-scale: 10% for Local, 20% for Cloud)
- `maxPaths` - Maximum paths to search (None = use full tree)
- `limitSearchSpace` - Auto-recommend maxPaths limit based on tree size
- `maxIterations` - Maximum Grover iterations (None = auto-calculate optimal)

**Features**:
- ✅ Generic over state type
- ✅ O(√N) quantum speedup over classical minimax
- ✅ Auto-scaling for different backends

---

### 10. resource<'T>

**Module**: `FSharp.Azure.Quantum.TaskScheduling.Builders`

**Purpose**: Define resources for task scheduling problems

**Example**:
```fsharp
let cpu = resource<string> {
    resourceId "CPU"
    capacity 4.0
    costPerUnit 10.0
    availableWindow (0.0, 100.0)
}
```

**Custom Operations**:
- `resourceId` - Unique identifier for resource
- `capacity` - Maximum capacity available
- `costPerUnit` - Cost per unit of resource usage
- `availableWindow` - Time window when resource is available (start, end)

---

### 11. scheduledTask<'T>

**Module**: `FSharp.Azure.Quantum.TaskScheduling.Builders`

**Purpose**: Define tasks for scheduling optimization

**Example**:
```fsharp
let task1 = scheduledTask<string> {
    taskId "Task1"
    duration (Duration 5.0)
    requires "CPU" 2.0
    priority 1.0
    deadline 50.0
}
```

**Custom Operations**:
- `taskId` - Unique identifier for task
- `duration` - Task duration (use `Duration` wrapper)
- `after` - Single dependency (task must start after specified task)
- `afterMultiple` - Multiple dependencies
- `requires` - Resource requirement (resourceId, quantity)
- `priority` - Priority for tie-breaking (higher = schedule first)
- `deadline` - Latest completion time
- `earliestStart` - Earliest start time

---

### 12. schedulingProblem<'T>

**Module**: `FSharp.Azure.Quantum.TaskScheduling.Builders`

**Purpose**: Define complete task scheduling optimization problems

**Example**:
```fsharp
let problem = schedulingProblem<string> {
    tasks [task1; task2; task3]
    resources [cpu; memory]
    objective MinimizeMakespan
    timeHorizon 100.0
}
```

**Custom Operations**:
- `tasks` - List of tasks to schedule
- `resources` - Available resources
- `objective` - Optimization objective (MinimizeMakespan | MinimizeCost | BalanceLoad)
- `timeHorizon` - Total time available for scheduling

---

### 13. anomalyDetection

**Module**: `FSharp.Azure.Quantum.Business`

**Purpose**: Detect outliers and anomalies in data using quantum one-class classification

**Example**:
```fsharp
let detector = anomalyDetection {
    trainOnNormalData normalTransactions  // Only normal examples needed
    sensitivity High                       // High sensitivity to detect subtle anomalies
    contaminationRate 0.1                  // Expect ~10% anomalies
    shots 1000
}

match detector with
| Ok model ->
    let result = AnomalyDetector.detect suspiciousTransaction model
    if result.IsAnomaly then
        printfn "⚠️ Anomaly detected! Score: %.2f" result.AnomalyScore
| Error e -> printfn "Training failed: %s" e
```

**Custom Operations**:
- `trainOnNormalData` - Training data (normal examples only)
- `sensitivity` - Detection sensitivity (Low | Medium | High)
- `contaminationRate` - Expected percentage of anomalies (default: 0.1)
- `backend` - Quantum backend to use (None = LocalBackend)
- `shots` - Number of measurement shots (default: 1000)
- `verbose` - Enable verbose logging (default: false)
- `saveModelTo` - Path to save trained model
- `note` - Optional note about the model
- `progressReporter` - Progress reporter for real-time training updates (default: None)
- `cancellationToken` - Cancellation token for early termination (default: None)

**Use Cases**:
- Security threat detection
- Fraud detection
- Quality control
- System monitoring
- Network intrusion detection

---

### 14. autoML

**Module**: `FSharp.Azure.Quantum.Business`

**Purpose**: Automated machine learning - tries multiple approaches and returns the best model automatically

**Example**:
```fsharp
let result = autoML {
    trainWith features labels
    
    // Optional: Control search space
    tryBinaryClassification true
    tryMultiClass 4
    tryRegression true
    tryAnomalyDetection false
    
    // Architectures to test
    tryArchitectures [Quantum; Hybrid; Classical]
    
    // Search budget
    maxTrials 20
    maxTimeMinutes 10
    
    verbose true
}

match result with
| Ok model ->
    printfn "Best model: %s (%.2f%%)" model.BestModelType (model.Score * 100.0)
    let prediction = AutoML.predict newSample model
| Error e -> printfn "AutoML failed: %s" e
```

**Custom Operations**:
- `trainWith` - Training features and labels
- `tryBinaryClassification` - Enable binary classification trials (default: true)
- `tryMultiClass` - Enable multi-class with N classes (default: auto-detect)
- `tryAnomalyDetection` - Enable anomaly detection trials (default: true)
- `tryRegression` - Enable regression trials (default: true)
- `trySimilaritySearch` - Enable similarity search trials (default: false)
- `tryArchitectures` - List of architectures to test (default: [Quantum; Hybrid; Classical])
- `maxTrials` - Maximum number of trials (default: 20)
- `maxTimeMinutes` - Maximum search time in minutes (default: None)
- `validationSplit` - Train/validation split ratio (default: 0.2)
- `backend` - Quantum backend to use (None = LocalBackend)
- `verbose` - Enable verbose logging (default: false)
- `saveModelTo` - Path to save best model
- `randomSeed` - Random seed for reproducibility (default: None)
- `progressReporter` - Progress reporter for real-time trial updates (default: None)
- `cancellationToken` - Cancellation token to stop search early (default: None)

**Use Cases**:
- Quick prototyping
- Model selection
- Baseline comparison
- Non-expert users

---

### 15. binaryClassification

**Module**: `FSharp.Azure.Quantum.Business`

**Purpose**: Classify items into two categories (yes/no, fraud/legitimate, spam/ham)

**Example**:
```fsharp
let classifier = binaryClassification {
    trainWith trainFeatures trainLabels
    architecture Quantum
    learningRate 0.01
    maxEpochs 100
    shots 1000
}

match classifier with
| Ok model ->
    let prediction = BinaryClassifier.predict newTransaction model
    if prediction.IsFraud then
        blockTransaction()
| Error e -> printfn "Training failed: %s" e
```

**Custom Operations**:
- `trainWith` - Training features (float[][]) and labels (int[]: 0 or 1)
- `architecture` - Architecture choice (Quantum | Hybrid | Classical)
- `learningRate` - Learning rate for training (default: 0.01)
- `maxEpochs` - Maximum training epochs (default: 100)
- `convergenceThreshold` - Convergence threshold (default: 0.001)
- `backend` - Quantum backend to use (None = LocalBackend)
- `shots` - Number of measurement shots (default: 1000)
- `verbose` - Enable verbose logging (default: false)
- `saveModelTo` - Path to save trained model
- `note` - Optional note about the model
- `progressReporter` - Progress reporter for real-time training updates (default: None)
- `cancellationToken` - Cancellation token for early termination (default: None)

**Use Cases**:
- Fraud detection
- Spam filtering
- Churn prediction (yes/no)
- Credit risk (approve/reject)
- Quality control (pass/fail)
- Medical diagnosis (disease/healthy)

---

### 16. predictiveModel

**Module**: `FSharp.Azure.Quantum.Business`

**Purpose**: Predict continuous values (regression) or categories (multi-class classification)

**Example**:
```fsharp
// Regression: Predict revenue
let revenueModel = predictiveModel {
    trainWith customerFeatures revenueTargets
    problemType Regression
    learningRate 0.01
    maxEpochs 100
}

// Multi-class: Predict churn timing
let churnModel = predictiveModel {
    trainWith customerFeatures churnLabels  // 0=Stay, 1=Churn30, 2=Churn60, 3=Churn90
    problemType (MultiClass 4)
    architecture Quantum
    shots 1000
}

match churnModel with
| Ok model ->
    let prediction = PredictiveModel.predictCategory customer model
    match prediction.Category with
    | 0 -> printfn "Customer will stay"
    | 1 -> printfn "⚠️ Churn risk in 30 days!"
    | _ -> ()
| Error e -> printfn "Training failed: %s" e
```

**Custom Operations**:
- `trainWith` - Training features (float[][]) and targets (float[])
- `problemType` - Problem type (Regression | MultiClass n)
- `architecture` - Architecture choice (Quantum | Hybrid | Classical)
- `learningRate` - Learning rate for training (default: 0.01)
- `maxEpochs` - Maximum training epochs (default: 100)
- `convergenceThreshold` - Convergence threshold (default: 0.001)
- `backend` - Quantum backend to use (None = LocalBackend)
- `shots` - Number of measurement shots (default: 1000)
- `verbose` - Enable verbose logging (default: false)
- `saveModelTo` - Path to save trained model
- `note` - Optional note about the model
- `progressReporter` - Progress reporter for real-time training updates (default: None)
- `cancellationToken` - Cancellation token for early termination (default: None)

**Use Cases**:
- **Regression**: Revenue forecasting, demand prediction, customer LTV, risk scoring
- **Multi-Class**: Churn timing prediction, customer segmentation, risk levels, lead scoring

---

### 17. similaritySearch

**Module**: `FSharp.Azure.Quantum.Business`

**Purpose**: Find similar items using quantum kernels

**Example**:
```fsharp
let finder = similaritySearch<Product> {
    indexItems productCatalog  // Array of (item, features)
    similarityMetric CosineSimilarity
    threshold 0.7              // Minimum similarity threshold
    shots 1000
}

match finder with
| Ok index ->
    let similar = SimilarityFinder.findSimilar queryProduct 5 index
    printfn "Top 5 similar products:"
    similar |> Array.iter (fun (item, score) ->
        printfn "  %A: %.2f%% similar" item (score * 100.0)
    )
| Error e -> printfn "Indexing failed: %s" e
```

**Custom Operations**:
- `indexItems` - Items to index (('T * float array)[])
- `similarityMetric` - Metric to use (CosineSimilarity | EuclideanDistance | QuantumKernel)
- `threshold` - Minimum similarity threshold (default: 0.0)
- `backend` - Quantum backend to use (None = LocalBackend)
- `shots` - Number of measurement shots (default: 1000)
- `verbose` - Enable verbose logging (default: false)
- `saveIndexTo` - Path to save search index
- `note` - Optional note about the index
- `progressReporter` - Progress reporter for real-time indexing updates (default: None)
- `cancellationToken` - Cancellation token for early termination (default: None)

**Use Cases**:
- Product recommendations
- Duplicate detection
- Content similarity
- Clustering
- Image similarity
- Document matching

---

### 18. optionPricing

**Module**: `FSharp.Azure.Quantum.Business.OptionPricing`

**Purpose**: Price financial options (European, Asian) using quantum Monte Carlo with quadratic speedup

**Example**:
```fsharp
let result = optionPricing {
    spotPrice 100.0
    strikePrice 105.0
    riskFreeRate 0.05
    volatility 0.2
    expiry 1.0
    optionType EuropeanCall
    qubits 6
    iterations 5
    shots 1000
    backend (LocalBackend())
}

match result |> Async.RunSynchronously with
| Ok price -> printfn "Option price: $%.4f (±%.4f)" price.Price price.ConfidenceInterval
| Error e -> printfn "Error: %A" e
```

**Custom Operations**:
- `spotPrice` - Current spot price of underlying asset (S₀)
- `strikePrice` - Strike price of the option (K)
- `riskFreeRate` - Risk-free interest rate (annualized, r)
- `volatility` - Volatility of underlying asset (annualized, σ)
- `expiry` - Time to expiry in years (T)
- `optionType` - Option type: `EuropeanCall`, `EuropeanPut`, `AsianCall n`, `AsianPut n`
- `qubits` - Number of qubits for price discretization (2-10)
- `iterations` - Number of Grover iterations for amplitude estimation
- `shots` - Number of measurement shots
- `backend` - Quantum backend (REQUIRED - no default)
- `cancellation_token` - Optional cancellation token for async operations

**Option Types**:
- `EuropeanCall` - max(S_T - K, 0)
- `EuropeanPut` - max(K - S_T, 0)
- `AsianCall n` - max(Avg(S_t) - K, 0) with n time steps
- `AsianPut n` - max(K - Avg(S_t), 0) with n time steps

**Quantum Advantage**:
- Uses Möttönen state preparation for exact amplitude encoding
- Achieves O(1/ε) complexity vs classical O(1/ε²)
- Quadratic speedup: ~100x for 1% accuracy target

**Greeks Calculation**:
```fsharp
// Calculate option sensitivities (Delta, Gamma, Vega, Theta, Rho)
let! greeks = OptionPricing.greeksEuropeanCall 100.0 105.0 0.05 0.2 1.0 backend
```

---

### 19. topological

**Module**: `FSharp.Azure.Quantum.Topological.TopologicalBuilder`

**Purpose**: Compose topological quantum programs with anyon braiding and fusion

**Example**:
```fsharp
let program = topological backend {
    let! ctx = TopologicalBuilder.initialize Ising 4
    let! ctx = TopologicalBuilder.braid 0 ctx
    let! ctx = TopologicalBuilder.braid 2 ctx
    let! (outcome, ctx) = TopologicalBuilder.measure 0 ctx
    return outcome
}

// Execute the program
let! result = TopologicalBuilder.execute backend program
match result with
| Ok particle -> printfn "Measured: %A" particle
| Error e -> printfn "Error: %A" e
```

**Builder Functions** (used with `let!` and `do!`):
- `TopologicalBuilder.initialize anyonType count` - Initialize anyons of given type and count
- `TopologicalBuilder.braid index context` - Braid anyons at given index
- `TopologicalBuilder.measure index context` - Measure fusion outcome at given index
- `TopologicalBuilder.braidSequence indices context` - Apply sequence of braiding operations
- `TopologicalBuilder.getState context` - Get current quantum state
- `TopologicalBuilder.getResults context` - Get accumulated measurement results
- `TopologicalBuilder.getLog context` - Get execution log for debugging
- `TopologicalBuilder.getContext context` - Get full context for visualization

**Anyon Types**:
- `Ising` - Ising anyons (Majorana fermions)
- `Fibonacci` - Fibonacci anyons (universal quantum computation)

**Execution Functions**:
- `TopologicalBuilder.execute backend program` - Execute and return just the result
- `TopologicalBuilder.executeWithContext backend program` - Execute and return result with full context

**Features**:
- Natural F# syntax with `let!` and `do!`
- Automatic state threading through context
- Backend-agnostic (works with any IQuantumBackend)
- Composable operations with error handling
- Execution log for debugging/visualization

---

### 20. quantumChemistry

**Module**: `FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder`

**Purpose**: Quantum chemistry ground state calculations with VQE (Variational Quantum Eigensolver)

**Example**:
```fsharp
open FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder

// Simple H2 molecule
let problem = quantumChemistry {
    molecule (h2 0.74)     // H2 at 0.74 Angstrom bond length
    basis "sto-3g"         // Minimal basis set
    ansatz UCCSD           // Most accurate ansatz
}

let! result = solve problem
printfn "Ground state energy: %.6f Ha" result.GroundStateEnergy
```

**Custom Operations**:
- `molecule mol` - Set molecule for calculation (direct instance)
- `molecule_from_xyz path` - Load molecule from XYZ file
- `molecule_from_fcidump path` - Load molecule from FCIDump file
- `molecule_from_provider provider name` - Load molecule from dataset provider
- `molecule_from_name name` - Load molecule by name from default library
- `basis basisSet` - Set basis set (e.g., "sto-3g", "6-31g", "cc-pvdz")
- `ansatz ansatzType` - Set ansatz type: `UCCSD`, `HEA`, `ADAPT`
- `optimizer name` - Set optimizer method (e.g., "COBYLA", "SLSQP", "Powell")
- `maxIterations n` - Set maximum VQE iterations (default: 100)
- `initialParameters params` - Set initial parameters for warm start

**Pre-built Molecules**:
```fsharp
// Hydrogen molecule
let hydrogen = h2 0.74          // H2 at bond length 0.74 Å

// Water molecule
let water = h2o 0.96 104.5      // H2O with O-H 0.96 Å, angle 104.5°

// Lithium hydride
let lithiumHydride = lih 1.6    // LiH at bond length 1.6 Å
```

**Ansatz Types**:
- `UCCSD` - Unitary Coupled Cluster Singles Doubles (most accurate, most expensive)
- `HEA` - Hardware-Efficient Ansatz (faster, less accurate)
- `ADAPT` - Adaptive ansatz (dynamic construction based on gradients)

**Basis Sets**:
- `"sto-3g"` - Minimal basis (fast, less accurate)
- `"6-31g"` - Split-valence basis (balanced)
- `"cc-pvdz"` - Correlation-consistent polarized double-zeta (accurate)

**Loading from Files**:
```fsharp
// From XYZ file (geometry format)
let problem1 = quantumChemistry {
    molecule_from_xyz "caffeine.xyz"
    basis "sto-3g"
    ansatz UCCSD
}

// From FCIDump file (molecular integrals)
let problem2 = quantumChemistry {
    molecule_from_fcidump "h2o.fcidump"
    basis "6-31g"
    ansatz HEA
}

// From molecule library
let problem3 = quantumChemistry {
    molecule_from_name "benzene"
    basis "sto-3g"
    ansatz UCCSD
}
```

**Result Fields**:
- `GroundStateEnergy` - Ground state energy in Hartrees
- `OptimalParameters` - Optimal VQE parameters found
- `Iterations` - Number of VQE iterations performed
- `Convergence` - Whether VQE converged within tolerance
- `BondLengths` - Map of bond lengths (e.g., "H-H" -> 0.74)
- `DipoleMoment` - Dipole moment if computed

---

## Common Patterns

### For Loops in CEs

Many CEs support `for` loops for adding multiple similar items:

```fsharp
// Circuit: Apply Hadamard to all qubits
let superposition = circuit {
    qubits 5
    for q in [0..4] do
        H q
}

// Constraint Solver: Add row constraints for Sudoku
let sudoku = constraintSolver<int> {
    searchSpace 81
    domain [1..9]
    for row in [0..8] do
        satisfies (checkRow row)
}

// Pattern Matcher: Combine multiple patterns with AND logic
let search = patternMatcher<Config> {
    searchSpace allConfigs
    for pattern in [perfPattern; costPattern; securityPattern] do
        matchPattern pattern  // All must match
}
```

### Backend Configuration

Most quantum CEs support backend specification:

```fsharp
// Default: LocalBackend (simulation)
let problem1 = periodFinder {
    number 15
    precision 12
}

// Explicit backend
let ionqBackend = // Cloud backend requires Azure Quantum workspace
// createIonQBackend(...)
let problem2 = periodFinder {
    number 15
    precision 12
    backend ionqBackend
}
```

### Auto-Scaling Parameters

Many CEs have auto-scaling parameters that adapt to backend type:

| Parameter | LocalBackend | Cloud Backend |
|-----------|--------------|---------------|
| `shots` | 100-1000 | 500-2048 |
| `maxIterations` | Auto-calculate | Auto-calculate |
| `solutionThreshold` | 1% | 2% |
| `successThreshold` | 10% | 20% |

Use `None` for auto-scaling (recommended), or specify explicit values for fine-tuning.

### Progress Reporting and Cancellation

All ML builders (`autoML`, `binaryClassification`, `predictiveModel`, `anomalyDetection`, `similaritySearch`) support progress reporting and cancellation for long-running operations:

```fsharp
open System.Threading
open FSharp.Azure.Quantum.Core.Progress

// Example 1: Console progress reporter
let consoleReporter = createConsoleReporter(verbose = true)

let result1 = autoML {
    trainWith features labels
    maxTrials 20
    progressReporter consoleReporter  // Real-time console updates
}

// Example 2: Event-based progress with cancellation
let cts = new CancellationTokenSource()
let reporter = createEventReporter()
reporter.SetCancellationToken(cts.Token)

// Subscribe to progress events
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
ctsTimeout.CancelAfter(TimeSpan.FromMinutes(5.0))

let result3 = binaryClassification {
    trainWith features labels
    maxEpochs 1000
    cancellationToken ctsTimeout.Token  // Auto-cancel after 5 minutes
}
```

**Progress Event Types**:
- `TrialStarted(trialId, totalTrials, modelType)` - AutoML trial starting
- `TrialCompleted(trialId, score, elapsedSeconds)` - AutoML trial completed successfully
- `TrialFailed(trialId, error)` - AutoML trial failed
- `ProgressUpdate(percentComplete, message)` - General progress update
- `PhaseChanged(phaseName, message)` - Algorithm phase changed
- `IterationUpdate(current, total, currentBest)` - Iteration progress

**Built-in Reporters**:
- `createConsoleReporter()` - Console output with formatting
- `createEventReporter()` - Event-based for UI integration
- `createNullReporter()` - No-op reporter
- `createAggregatingReporter(reporters)` - Combine multiple reporters

**Use Cases**:
- **Long-running searches**: Monitor AutoML with 20+ trials
- **UI integration**: Real-time progress bars in WPF/Blazor/Avalonia
- **Production monitoring**: Log progress to monitoring systems
- **Resource constraints**: Timeout protection with automatic cancellation
- **Early exit**: Cancel when good-enough results found

---

## IntelliSense Tips

### If IntelliSense Doesn't Show Operations

1. **Type the CE name explicitly**:
   ```fsharp
   let problem = periodFinder {
       // Now press Ctrl+Space to see operations
   }
   ```

2. **Check you're using the correct CE instance name** (not the builder type):
   - ✅ `periodFinder { }` - Correct
   - ❌ `PeriodFinderBuilder { }` - Wrong

3. **For generic CEs, specify the type parameter**:
   ```fsharp
   let search = patternMatcher<Config> {
       // Type parameter helps IntelliSense
   }
   ```

4. **Use this reference table** when IntelliSense fails

---

## See Also

- [Getting Started Guide](getting-started.md)
- [API Reference](api-reference.md)
- [Architecture Overview](architecture-overview.md)
