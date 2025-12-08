---
layout: default
title: Local Quantum Simulation Guide
---

# Local Quantum Simulation Guide

**Test and develop quantum algorithms offline** - No Azure credentials required!

The local quantum simulation module enables rapid development, unit testing, and educational exploration of quantum algorithms without cloud connectivity or costs.

## Overview

FSharp.Azure.Quantum includes a lightweight, pure F# quantum simulator that supports:

- **State vector simulation** up to 20 qubits (65536-dimensional state space)
- **QAOA circuits** with mixer and cost Hamiltonians
- **Single-qubit gates**: X, Y, Z, H, Rx, Ry, Rz
- **Two-qubit gates**: CNOT, CZ
- **Measurement** with shot sampling
- **Zero external dependencies** - uses only System.Numerics.Complex from BCL

## Quick Start

```fsharp
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver
open FSharp.Azure.Quantum.Core.BackendAbstraction

// Create distance matrix for a simple 3-city TSP
let distances = array2D [
    [ 0.0; 1.0; 2.0 ]
    [ 1.0; 0.0; 1.5 ]
    [ 2.0; 1.5; 0.0 ]
]

// Create local backend (supports up to 20 qubits)
let backend = createLocalBackend()

// Solve with default configuration (QAOA with parameter optimization)
match solve backend distances defaultConfig with
| Ok solution ->
    printfn "Backend: %s" solution.BackendName
    printfn "Time: %.2f ms" solution.ElapsedMs
    printfn "Best tour: %A" solution.Tour
    printfn "Tour length: %.2f" solution.TourLength
    printfn "Optimized parameters (γ, β): %A" solution.OptimizedParameters
    printfn "Optimization converged: %b" (solution.OptimizationConverged |> Option.defaultValue false)
| Error err ->
    eprintfn "Simulation failed: %s" err.Message
```

**Output:**
```
Backend: Local QAOA Simulator
Time: 125.45 ms
Best tour: [|0; 1; 2|]
Tour length: 4.50
Optimized parameters (γ, β): (1.23, 0.87)
Optimization converged: true
```

## When to Use Local Simulation

### ✅ Use Local Simulation For:

- **Unit testing** - Fast, deterministic tests without network I/O
- **Algorithm development** - Rapid iteration during development
- **Educational purposes** - Learning quantum concepts interactively
- **Small problems** - Up to 16 qubits (2^16 = 65536 state dimensions)
- **Offline work** - No internet connection required
- **Cost-free exploration** - Zero cloud execution costs

### ⚠️ Use Azure Quantum For:

- **Large problems** - More than 16 qubits
- **Production workloads** - Scalable cloud execution
- **Hardware access** - Real quantum hardware (IonQ, Rigetti, etc.)
- **Performance** - Parallel execution across multiple circuits

## Unified Backend API (Recommended)

The `BackendAbstraction` module provides a **single consistent API** for local simulation, IonQ, and Rigetti backends. This is the recommended approach for quantum algorithm development.

### Creating Backends

```fsharp
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver

// Option 1: Local backend (no configuration needed)
let localBackend = createLocalBackend()

// Option 2: IonQ backend (requires Azure Quantum workspace)
let httpClient = new System.Net.Http.HttpClient()
let workspaceUrl = "https://management.azure.com/subscriptions/.../Workspaces/..."
let ionqBackend = createIonQBackend httpClient workspaceUrl "ionq.simulator"

// Option 3: Rigetti backend
let rigettiBackend = createRigettiBackend httpClient workspaceUrl "rigetti.sim.qvm"
```

### Backend Switching

**The beauty of the unified API:** Use the same solver code with any backend!

```fsharp
let distances_backend_demo = array2D [
    [ 0.0; 1.0; 2.0 ]
    [ 1.0; 0.0; 1.5 ]
    [ 2.0; 1.5; 0.0 ]
]

// Same code, different backends - just pass different backend instance
let runWithBackend (backend: IQuantumBackend) =
    match solve backend distances_backend_demo defaultConfig with
    | Ok solution ->
        printfn "%s: Tour length = %.2f (%.2f ms)" 
            solution.BackendName solution.TourLength solution.ElapsedMs
    | Error err -> printfn "Error: %s" err.Message

// Execute on local simulator
runWithBackend localBackend

// Execute on IonQ (when configured)
// runWithBackend ionqBackend

// Execute on Rigetti (when configured)
// runWithBackend rigettiBackend
```

**No algorithm changes needed** - same `solve` function, same distance matrix input!

### Using Backend Interface

For dependency injection or testing, use the `IQuantumBackend` interface:

```fsharp
// Mock circuit for demonstration purposes
let quboMatrix = array2D [[1.0; -1.0]; [-1.0; 1.0]]
let problemHam = ProblemHamiltonian.fromQubo quboMatrix
let mixerHam = MixerHamiltonian.create 2
let qaoaCircuit = QaoaCircuit.build problemHam mixerHam [|(0.5, 0.3)|]
let circuit = wrapQaoaCircuit qaoaCircuit

let executeWithBackend (backend: IQuantumBackend) circuit shots =
    match backend.Execute circuit shots with
        | Ok result ->
            printfn "Backend: %s, Shots: %d" result.BackendName result.NumShots
            result.Measurements
        | Error err ->
            eprintfn "Execution failed: %s" err.Message
            [||]

// Use local backend
let localBackend2 = createLocalBackend()
let measurements_demo = executeWithBackend localBackend2 circuit 1000

// Easy to swap for testing or different backends
let testBackend = createMockBackend()  // Your test implementation
let testMeasurements = runWithBackend testBackend circuit 100
```

### Execution Result Format

All backends return the same `ExecutionResult` type:

```fsharp
type ExecutionResult = {
    /// Measurement counts (bitstring -> frequency)
    Counts: Map<string, int>
    
    /// Number of shots executed
    Shots: int
    
    /// Backend identifier ("Local", "Azure", etc.)
    Backend: string
    
    /// Execution time in milliseconds
    ExecutionTimeMs: float
    
    /// Job ID (Azure only, None for local)
    JobId: string option
}
```

This uniform format makes it easy to:
- Compare results between backends
- Log execution metrics consistently
- Build visualizations that work with any backend

## Advanced: Low-Level Modules

**Note:** The following low-level modules are available for advanced use cases, but most users should use the unified `QuantumBackend` API shown above.

These modules provide direct access to quantum operations for:
- Educational purposes (learning how quantum simulation works)
- Custom circuit types beyond QAOA
- Performance optimization for specific use cases

### 1. StateVector - Quantum State Representation

The `StateVector` module manages quantum state as a complex-valued vector.

```fsharp
open FSharp.Azure.Quantum.LocalSimulator.StateVector
open FSharp.Azure.Quantum.LocalSimulator.Gates

// Initialize 3 qubits to |000⟩ state
let state = StateVector.init 3

// Get state properties
let dimension = StateVector.dimension state        // 8 (2^3)
let amplitudes = 
    [| 0 .. dimension - 1 |]
    |> Array.map (fun i -> StateVector.getAmplitude i state)  // Get each amplitude

// Check normalization (should be 1.0)
let norm = StateVector.norm state  // 1.0

// Create uniform superposition |+⟩^⊗n (all basis states equally likely)
// Apply Hadamard to all qubits
let superposition = 
    let s = StateVector.init 2  // Start with |00⟩
    s |> Gates.applyH 0 |> Gates.applyH 1  // Apply H to each qubit
```

**Key Concepts:**

- **Basis States**: For n qubits, basis states are |000⟩, |001⟩, ..., |111⟩
- **Amplitudes**: Complex numbers α_i in the quantum state superposition
- **Quantum State Formula:**

<div style="display:block; margin-left: 1em;">
  <span style="color:#009966; font-weight:bold">|ψ⟩</span> = <span style="color:#CC6600; font-weight:bold">Σ<sub>i</sub></span> <span style="color:#0066CC; font-weight:bold">αᵢ</span><span style="color:#CC0066; font-weight:bold">|i⟩</span>
</div>
  
**Where:**
- <span style="color:#009966; font-weight:bold">|ψ⟩</span> = Quantum state vector
- <span style="color:#0066CC; font-weight:bold">αᵢ</span> = Complex amplitude for basis state |i⟩
- <span style="color:#CC6600; font-weight:bold">Σ<sub>i</sub></span> = Sum over all 2ⁿ basis states
- <span style="color:#CC0066; font-weight:bold">|i⟩</span> = Computational basis state (e.g., |000⟩, |001⟩, etc.)

- **Normalization**: Σ|α_i|² = 1 (probability conservation)
- **Qubit Indexing**: Qubit i corresponds to bit i in basis state index
  - Example: |10⟩ (basis 2) = qubit_0=0, qubit_1=1

### 2. Gates - Quantum Operations

Single-qubit and two-qubit gate operations.

#### Single-Qubit Gates

```fsharp
open FSharp.Azure.Quantum.LocalSimulator.StateVector
open FSharp.Azure.Quantum.LocalSimulator.Gates

let state = StateVector.init 2

// Pauli gates
let stateX = Gates.applyX 0 state  // Bit flip on qubit 0
let stateY = Gates.applyY 1 state  // Pauli-Y on qubit 1
let stateZ = Gates.applyZ 0 state  // Phase flip on qubit 0

// Hadamard gate (creates superposition)
let stateH = Gates.applyH 0 state  // |0⟩ → (|0⟩+|1⟩)/√2

// Rotation gates (parameterized)
let angle = System.Math.PI / 4.0
let stateRx = Gates.applyRx 0 angle state  // Rotate around X-axis
let stateRy = Gates.applyRy 1 angle state  // Rotate around Y-axis
let stateRz = Gates.applyRz 0 angle state  // Rotate around Z-axis
```

**Gate Definitions:**

| Gate | Matrix | Description |
|------|--------|-------------|
| **X** | `[[0,1],[1,0]]` | Bit flip: \|0⟩↔\|1⟩ |
| **Y** | `[[0,-i],[i,0]]` | Bit+phase flip |
| **Z** | `[[1,0],[0,-1]]` | Phase flip: \|1⟩→-\|1⟩ |
| **H** | `[[1,1],[1,-1]]/√2` | Hadamard: creates superposition |

**Rotation Gates:**

**Rx(θ) - Rotation around X-axis:**

<div style="display:block; margin-left: 1em;">
  Rx(<span style="color:#CC0066; font-weight:bold">θ</span>) = cos(<span style="color:#CC0066; font-weight:bold">θ</span>/2)<span style="color:#0066CC; font-weight:bold">I</span> - i·sin(<span style="color:#CC0066; font-weight:bold">θ</span>/2)<span style="color:#009966; font-weight:bold">X</span>
</div>

**Where:** <span style="color:#CC0066; font-weight:bold">θ</span> = rotation angle, <span style="color:#0066CC; font-weight:bold">I</span> = identity matrix, <span style="color:#009966; font-weight:bold">X</span> = Pauli-X matrix

**Ry(θ) - Rotation around Y-axis:**

<div style="display:block; margin-left: 1em;">
  Ry(<span style="color:#CC0066; font-weight:bold">θ</span>) = cos(<span style="color:#CC0066; font-weight:bold">θ</span>/2)<span style="color:#0066CC; font-weight:bold">I</span> - i·sin(<span style="color:#CC0066; font-weight:bold">θ</span>/2)<span style="color:#CC6600; font-weight:bold">Y</span>
</div>

**Where:** <span style="color:#CC0066; font-weight:bold">θ</span> = rotation angle, <span style="color:#CC6600; font-weight:bold">Y</span> = Pauli-Y matrix

**Rz(θ) - Rotation around Z-axis:**

<div style="display:block; margin-left: 1em;">
  Rz(<span style="color:#CC0066; font-weight:bold">θ</span>) = e^(-i<span style="color:#CC0066; font-weight:bold">θ</span>/2)|0⟩⟨0| + e^(i<span style="color:#CC0066; font-weight:bold">θ</span>/2)|1⟩⟨1|
</div>

**Where:** <span style="color:#CC0066; font-weight:bold">θ</span> = rotation angle, adds phase based on qubit state

#### Two-Qubit Gates

```fsharp
// CNOT (Controlled-NOT) - flips target if control is |1⟩
let stateCNOT = Gates.applyCNOT 0 1 state  // Control=0, Target=1

// CZ (Controlled-Z) - adds phase if both qubits are |1⟩
let stateCZ = Gates.applyCZ 0 1 state  // Qubit 0 and 1
```

**Gate Behavior:**

- **CNOT(control, target)**: Flips target qubit if control is |1⟩
  - |00⟩ → |00⟩, |01⟩ → |01⟩, |10⟩ → |11⟩, |11⟩ → |10⟩
- **CZ(qubit1, qubit2)**: Adds -1 phase if both qubits are |1⟩
  - |11⟩ → -|11⟩, all other states unchanged

### 3. QaoaSimulator - QAOA Circuit Execution

**Note:** For application development, use `QuantumBackend.Local.simulate` instead (see Unified Backend API section above). This low-level module is for educational purposes.

The `QaoaSimulator` module provides direct QAOA simulation operations:

```fsharp
open FSharp.Azure.Quantum.LocalSimulator.QaoaSimulator

// Initialize uniform superposition manually
let state = QaoaSimulator.initializeUniformSuperposition 3

// Apply cost interaction (ZZ term)
let stateAfterCost = QaoaSimulator.applyCostInteraction 0.5 0 1 -1.0 state

// Apply mixer layer (RX gates on all qubits)
let stateAfterMixer = QaoaSimulator.applyMixerLayer 0.3 stateAfterCost
```

**QAOA Circuit Structure:**

For depth p, QAOA applies p layers of:
1. **Cost Hamiltonian**: Encodes problem structure
   - Applies Rz rotations based on edge weights
   - Applies CZ gates between connected nodes
2. **Mixer Hamiltonian**: Enables exploration
   - Applies Rx rotations to all qubits

**QAOA Circuit Formula:**

<div style="display:block; margin-left: 1em;">
  <span style="color:#666666; font-weight:bold">|0⟩^⊗n</span> → [<span style="color:#CC0066; font-weight:bold">Cost(γ₁)</span> → <span style="color:#0066CC; font-weight:bold">Mix(β₁)</span>] → ... → [<span style="color:#CC0066; font-weight:bold">Cost(γₚ)</span> → <span style="color:#0066CC; font-weight:bold">Mix(βₚ)</span>] → Measure
</div>

**Where:**
- <span style="color:#666666; font-weight:bold">|0⟩^⊗n</span> = Initial state (n qubits, all in |0⟩)
- <span style="color:#CC0066; font-weight:bold">Cost(γₖ)</span> = Cost Hamiltonian layer with parameter γₖ
- <span style="color:#0066CC; font-weight:bold">Mix(βₖ)</span> = Mixer Hamiltonian layer with parameter βₖ
- <span style="color:#009966; font-weight:bold">p</span> = Circuit depth (number of layer repetitions)
- <span style="color:#CC0066; font-weight:bold">γₖ</span>, <span style="color:#0066CC; font-weight:bold">βₖ</span> = Variational parameters (optimized classically)

### 4. Measurement - Observation and Sampling

Measure quantum states and sample outcomes.

```fsharp
open FSharp.Azure.Quantum.LocalSimulator.Measurement
open FSharp.Azure.Quantum.LocalSimulator.StateVector
open FSharp.Azure.Quantum.LocalSimulator.Gates
open System

// Create a superposition state
let state = 
    StateVector.init 2
    |> Gates.applyH 0  // |0⟩ → (|0⟩+|1⟩)/√2 on qubit 0

// Get probability distribution
let probabilities = Measurement.getProbabilityDistribution state
// probabilities = [| 0.5; 0.0; 0.5; 0.0 |]
//                    |00⟩  |01⟩  |10⟩  |11⟩

// Verify Born rule: P(|ψ⟩) = |⟨ψ|α⟩|²
let prob00 = Measurement.getBasisStateProbability 0 state  // 0.5
let prob10 = Measurement.getBasisStateProbability 2 state  // 0.5

// Sample outcomes with shots
let rng = Random()
let samples = Measurement.sampleAndCount rng 1000 state  // 1000 measurements
// Returns: Map<int, int> of basis_index → count
// Example: Map [(0, 503); (2, 497)]

// Perform single measurement (collapses state)
let outcome = Measurement.measureComputationalBasis rng state
let collapsedState = Measurement.collapseAfterMeasurement 0 outcome state
printfn "Measured basis state: %d" outcome  // 0 or 2 (50% chance each)

// Sample bitstrings (convert int outcomes to binary strings)
let rawSamples = Measurement.sampleMeasurements rng 100 state
let bitstrings = 
    rawSamples 
    |> Array.groupBy id 
    |> Array.map (fun (outcome, arr) -> 
        (Convert.ToString(outcome, 2).PadLeft(2, '0'), arr.Length))
    |> Map.ofArray
// Returns: Map<string, int> of "00" → 52, "10" → 48

// Get expectation value (using computeExpectedValue)
let pauliZ qubitIdx basisState =
    let bitMask = 1 <<< qubitIdx
    if (basisState &&& bitMask) <> 0 then -1.0 else 1.0

let expectation = Measurement.computeExpectedValue (pauliZ 0) state
// For |+⟩ state on qubit 0: expectation ≈ 0.0
// For |0⟩ state: expectation = +1.0
// For |1⟩ state: expectation = -1.0
```

**Measurement Concepts:**

- **Born Rule - Measurement Probability:**

<div style="display:block; margin-left: 1em;">
  <span style="color:#CC0066; font-weight:bold">P(i)</span> = <span style="color:#009966; font-weight:bold">|αᵢ|²</span>
</div>
  
**Where:**
- <span style="color:#CC0066; font-weight:bold">P(i)</span> = Probability of measuring basis state |i⟩
- <span style="color:#0066CC; font-weight:bold">αᵢ</span> = Complex amplitude for state |i⟩
- <span style="color:#009966; font-weight:bold">|αᵢ|²</span> = Squared magnitude (amplitude × conjugate)

- **Collapse**: After measurement, state becomes the measured basis state
- **Shots**: Multiple measurements to estimate probability distribution
- **Bitstrings**: Classical outcome representation (e.g., "101" for |101⟩)
- **Expectation Value:**

<div style="display:block; margin-left: 1em;">
  <span style="color:#CC6600; font-weight:bold">⟨Z⟩</span> = <span style="color:#666666; font-weight:bold">Σ<sub>i</sub></span> <span style="color:#CC0066; font-weight:bold">P(i)</span>·<span style="color:#9933CC; font-weight:bold">zᵢ</span>
</div>
  
**Where:**
- <span style="color:#CC6600; font-weight:bold">⟨Z⟩</span> = Average value of observable Z
- <span style="color:#CC0066; font-weight:bold">P(i)</span> = Probability of state i
- <span style="color:#9933CC; font-weight:bold">zᵢ</span> = Eigenvalue of Z for state i

**Statistical Analysis Example:**

```fsharp
// Run many shots and analyze statistics
let numShots = 10000
let rng = Random()
let samples = Measurement.sampleAndCount rng numShots state

let statistics = 
    samples
    |> Map.toList
    |> List.map (fun (basisIndex, count) ->
        let bitstring = Convert.ToString(basisIndex, 2).PadLeft(2, '0')
        let probability = float count / float numShots
        let expectedProb = Measurement.getBasisStateProbability basisIndex state
        let error = abs (probability - expectedProb)
        (bitstring, count, probability, expectedProb, error)
    )

printfn "Measurement Statistics:"
printfn "State | Count | Measured | Expected | Error"
statistics
|> List.iter (fun (bs, cnt, meas, exp, err) ->
    printfn "  %s  | %5d | %6.3f   | %6.3f   | %.4f" bs cnt meas exp err
)
```

## Performance Characteristics

### Time Complexity

| Operation | Complexity | Example (5 qubits) |
|-----------|------------|-------------------|
| State init | O(2^n) | 32 elements |
| Single-qubit gate | O(2^n) | 32 operations |
| Two-qubit gate | O(2^n) | 32 operations |
| QAOA layer | O(E·2^n) | E edges × 32 |
| Measurement | O(2^n) | 32 probability calcs |

### Memory Usage

| Qubits | State Vector Size | Memory |
|--------|------------------|--------|
| 5 | 32 complex numbers | 512 bytes |
| 8 | 256 complex numbers | 4 KB |
| 10 | 1024 complex numbers | 16 KB |

**Note:** Each complex number uses 16 bytes (2 × 8-byte doubles)

### Practical Limits

```fsharp
// ✅ Fast: 5 qubits, 100 shots
QaoaSimulator.simulate circuit5 100  // ~10ms

// ✅ Reasonable: 8 qubits, 1000 shots
QaoaSimulator.simulate circuit8 1000  // ~100ms

// ⚠️ Slow: 16 qubits, 10000 shots
QaoaSimulator.simulate circuit16 10000  // ~several seconds

// ❌ Too large: 17+ qubits
QaoaSimulator.simulate circuit17 1000  // Error: exceeds 16-qubit limit
```

## Complete Example: MaxCut Problem

Let's solve a MaxCut problem using local simulation:

```fsharp
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Quantum
open System

// Define a 4-node graph MaxCut problem
//     0 --- 1
//     |  X  |
//     3 --- 2
// Goal: Partition nodes into two sets to maximize cut edges

let buildMaxCutCircuit numQubits edges beta gamma =
    {
        NumQubits = numQubits
        Parameters = [| beta; gamma |]
        CostTerms = 
            edges 
            |> List.map (fun (i, j) -> (i, j, -1.0))  // Weight -1 for MaxCut
            |> Array.ofList
        Depth = 1
    }

let evaluateMaxCut edges bitstring =
    let isSet i = bitstring.[i] = '1'
    edges
    |> List.filter (fun (i, j) -> isSet i <> isSet j)  // Count cut edges
    |> List.length

let edges = [(0, 1); (1, 2); (2, 3); (3, 0); (0, 2)]  // 5 edges

// Grid search over QAOA parameters
let betaRange = [0.0 .. 0.2 .. 1.0]
let gammaRange = [0.0 .. 0.2 .. 1.0]

let bestResult =
    [ for beta in betaRange do
        for gamma in gammaRange do
            let circuit = buildMaxCutCircuit 4 edges beta gamma
            match QaoaSimulator.simulate circuit 1000 with
            | Ok result ->
                // Find best bitstring from this simulation
                let best = 
                    result.Counts
                    |> Map.toList
                    |> List.map (fun (bs, count) -> 
                        (bs, count, evaluateMaxCut edges bs))
                    |> List.maxBy (fun (_, _, cut) -> cut)
                Some (beta, gamma, best)
            | Error _ -> None
    ]
    |> List.choose id
    |> List.maxBy (fun (_, _, (_, _, cut)) -> cut)

let (optBeta, optGamma, (optBitstring, optCount, optCut)) = bestResult

printfn "Best QAOA Parameters:"
printfn "  β = %.2f" optBeta
printfn "  γ = %.2f" optGamma
printfn ""
printfn "Best Solution:"
printfn "  Partition: %s" optBitstring
printfn "  Cut edges: %d / %d" optCut edges.Length
printfn "  Frequency: %d / 1000 shots" optCount

// Verify solution
let partition0 = [for i in 0..3 do if optBitstring.[i] = '0' then yield i]
let partition1 = [for i in 0..3 do if optBitstring.[i] = '1' then yield i]
printfn ""
printfn "Partitions:"
printfn "  Set 0: %A" partition0
printfn "  Set 1: %A" partition1
```

**Output:**
```
Best QAOA Parameters:
  β = 0.40
  γ = 0.60

Best Solution:
  Partition: 0110
  Cut edges: 4 / 5
  Frequency: 387 / 1000 shots

Partitions:
  Set 0: [0; 3]
  Set 1: [1; 2]
```

## Integration with Existing Code

The local simulator uses the same `QaoaCircuit` type as the Azure Quantum integration:

```fsharp
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.LocalSimulator

let circuit = {
    NumQubits = 5
    Parameters = [| 0.5; 0.3 |]
    CostTerms = [| (0, 1, -1.0); (1, 2, -1.0) |]
    Depth = 1
}

// Option 1: Local simulation (fast, free)
let localResult = QaoaSimulator.simulate circuit 1000

// Option 2: Azure Quantum (scalable, requires credentials)
// let azureResult = AzureQuantum.execute circuit workspace

// Same circuit type, different backends!
```

**Hybrid Development Workflow:**

```fsharp
// Mock variables for demonstration
let numQubits = 4
let edges = [(0, 1); (1, 2); (2, 3)]  // Example graph edges
let circuit_demo = circuit  // Re-use circuit defined earlier

// Mock helper functions for demonstration
let generateCircuits numQubits edges = [circuit_demo]  // Mock: generate test circuits
let validateResult result = ()  // Mock: validate simulation result
let parameterGrid = [(1.0, 0.5); (1.5, 0.7); (2.0, 1.0)]  // Mock: parameter combinations to test
let buildCircuit params = circuit_demo  // Mock: build circuit with given parameters
let evaluateQuality result = match result with | Ok _ -> 0.85 | Error _ -> 0.0  // Mock: evaluate solution quality

// 1. Develop and test locally
let testCircuits = generateCircuits numQubits edges
for circuit in testCircuits do
    match QaoaSimulator.simulate circuit 100 with
    | Ok result -> validateResult result
    | Error err -> eprintfn "Test failed: %s" err.Message

// 2. Optimize parameters locally
let optimizedParams = 
    parameterGrid
    |> List.map (fun params ->
        let circuit = buildCircuit params
        let result = QaoaSimulator.simulate circuit 1000
        (params, evaluateQuality result))
    |> List.maxBy snd
    |> fst

// 3. Deploy to Azure for production scale
let productionCircuit = buildCircuit optimizedParams
// let azureResult = AzureQuantum.execute productionCircuit workspace
```

## Unit Testing with Local Simulation

The local simulator is ideal for unit testing quantum algorithms:

```fsharp
module QaoaTests =
    open NUnit.Framework
    open FSharp.Azure.Quantum.LocalSimulator
    
    [<Test>]
    let ``QAOA creates superposition`` () =
        // Setup: 2-qubit circuit with no cost terms
        let circuit = {
            NumQubits = 2
            Parameters = [| 0.5; 0.0 |]  // Only mixer, no cost
            CostTerms = [||]
            Depth = 1
        }
        
        // Act: Simulate
        let result = QaoaSimulator.simulate circuit 1000
        
        // Assert: Should see multiple outcomes (superposition)
        match result with
        | Ok r ->
            Assert.Greater(r.Counts.Count, 1, "Should have multiple outcomes")
            Assert.AreEqual(1000, r.Shots, "All shots recorded")
        | Error err ->
            Assert.Fail($"Simulation failed: {msg}")
    
    [<Test>]
    let ``Single-qubit gates preserve normalization`` () =
        // Setup: Create initial state
        let state = StateVector.init 3
        
        // Act: Apply various gates
        let state' = 
            state
            |> Gates.applyH 0
            |> Gates.applyX 1
            |> Gates.applyRz 2 (Math.PI / 4.0)
        
        // Assert: State should remain normalized
        let norm = StateVector.norm state'
        Assert.AreEqual(1.0, norm, 1e-10, "State must remain normalized")
    
    [<Test>]
    let ``Measurement probabilities sum to 1`` () =
        // Setup: Create superposition
        let state = 
            StateVector.init 2
            |> Gates.applyH 0
            |> Gates.applyH 1
        
        // Act: Get probabilities
        let probs = getProbabilityDistribution state
        
        // Assert: Born rule - probabilities sum to 1
        let total = Array.sum probs
        Assert.AreEqual(1.0, total, 1e-10, "Probabilities must sum to 1")
```

## Error Handling

The simulator provides detailed error messages for common mistakes:

```fsharp
// ❌ Too many qubits (example - incomplete record syntax)
// let hugeCircuit = { NumQubits = 15; ... }
// match QaoaSimulator.simulate hugeCircuit 1000 with
// | Error err -> 
//     // "Number of qubits (15) must be at most 16"
//     ()

// ❌ Invalid qubit index
// let state = StateVector.init 3
// let invalid = Gates.applyX 5 state  // Exception: qubit 5 out of range [0..2]

// ❌ Mismatched parameters (example - incomplete record syntax)
// let badCircuit = { NumQubits = 4; Parameters = [|0.5|]; Depth = 2; ... }
// match QaoaSimulator.simulate badCircuit 1000 with
// | Error err ->
//     // "Expected 4 parameters for depth 2, got 1"
//     ()

// ❌ Invalid edge indices (example - incomplete record syntax)
// let invalidCircuit = { 
//     NumQubits = 3
//     CostTerms = [| (0, 5, -1.0) |]  // Qubit 5 doesn't exist!
//     ...
// }
// match QaoaSimulator.simulate invalidCircuit 1000 with
// | Error err ->
//     // "Cost term edge (0,5) references qubit 5, but only 3 qubits available"
//     ()
```

## Next Steps

- **[API Reference](api-reference.md)** - Complete API documentation
- **[QAOA Algorithm Guide](examples/qaoa-example.md)** - Deep dive into QAOA
- **[MaxCut Example](examples/maxcut-example.md)** - Complete MaxCut tutorial
- **[Testing Guide](testing-guide.md)** - Writing tests with the simulator

## FAQ

**Q: Why is simulation limited to 16 qubits?**  
A: State vector simulation requires 2^n complex numbers. For 16 qubits, that's 65536 complex numbers (1 MB). For 20 qubits, it would be 16 MB, and for 30 qubits, 16 GB. The 16-qubit limit balances functionality with practical memory and performance constraints.

**Q: How accurate is the simulator?**  
A: The simulator implements exact state vector evolution with floating-point arithmetic. Expect ~1e-14 numerical precision (double precision). This is sufficient for algorithm development and unit testing.

**Q: Can I simulate noise?**  
A: Not yet. The current implementation is a noiseless (ideal) simulator. Noise models may be added in future versions.

**Q: How do I compare local vs Azure results?**  
A: Both return shot counts (bitstring → frequency). The formats are compatible:
```fsharp
// Local simulator
let localCounts: Map<string, int> = result.Counts

// Azure Quantum (hypothetical)
// let azureCounts: Map<string, int> = azureResult.Counts

// Can directly compare distributions
```

**Q: Can I use this for algorithms other than QAOA?**  
A: Currently, the high-level API is QAOA-specific. However, the `StateVector`, `Gates`, and `Measurement` modules are general-purpose and can be used to build arbitrary quantum circuits. Support for other algorithms may be added based on demand.

---

**Last Updated**: 2025-11-24  
**Module Version**: v0.1.0-alpha
