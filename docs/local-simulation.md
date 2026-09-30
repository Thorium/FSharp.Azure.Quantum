---
layout: default
title: Local Quantum Simulation Guide
---

# Local Quantum Simulation Guide

**Test and develop quantum algorithms offline** - No Azure credentials required!

The local quantum simulation module enables rapid development, unit testing, and educational exploration of quantum algorithms without cloud connectivity or costs.

## Overview

FSharp.Azure.Quantum includes a pure F# quantum simulator that supports:

- **State vector simulation** up to `StateVector.maxQubits`, derived from available memory (2^n amplitudes x 16 bytes; hard ceiling 30)
- **Any `CircuitBuilder` circuit** through `LocalBackend`, including QAOA circuits
- **Single-qubit gates**: X, Y, Z, H, S, S†, T, T†, P, Rx, Ry, Rz, U
- **Multi-qubit gates**: CNOT, CZ, SWAP, controlled phase and rotations (CP, CRX, CRY, CRZ), Rxx, Ryy, Rzz, CCX, multi-controlled Z
- **Measurement** with shot sampling
- **A noisy density-matrix variant** (`NoisyLocalBackend`) for small circuits
- **No native dependencies** - the simulator core uses only `System.Numerics.Complex` from the BCL

## Quick Start

```fsharp
open System.Threading
open FSharp.Azure.Quantum.Quantum.QuantumTspSolver
open FSharp.Azure.Quantum.Backends

// Create distance matrix for a simple 3-city TSP
let distances = array2D [
    [ 0.0; 1.0; 2.0 ]
    [ 1.0; 0.0; 1.5 ]
    [ 2.0; 1.5; 0.0 ]
]

// Create local backend (width derived from available memory)
let backend = LocalBackendFactory.createUnified()

// Solve with default configuration (QAOA with parameter optimization)
let result =
    solveAsync backend distances defaultConfig CancellationToken.None
    |> Async.AwaitTask
    |> Async.RunSynchronously

match result with
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

**Example output** (timings and parameters vary from run to run):
```
Backend: Local Simulator
Time: 125.45 ms
Best tour: [|0; 1; 2|]
Tour length: 4.50
Optimized parameters (γ, β): Some (1.23, 0.87)
Optimization converged: true
```

A 3-city TSP uses 9 qubits (N² for N cities). The solver refuses problems wider than the backend can run in reasonable time, so on the local simulator TSP is practical up to 4 cities (16 qubits).

## When to Use Local Simulation

### ✅ Use Local Simulation For:

- **Unit testing** - Fast tests without network I/O
- **Algorithm development** - Rapid iteration during development
- **Educational purposes** - Learning quantum concepts interactively
- **Small problems** - Up to about 20 qubits comfortably (2^20 ≈ 1M amplitudes, 16 MB)
- **Offline work** - No internet connection required
- **Cost-free exploration** - Zero cloud execution costs

### ⚠️ Use Azure Quantum For:

- **Large problems** - Wider than the simulator can hold (`StateVector.maxQubits`) or finish in reasonable time
- **Production workloads** - Scalable cloud execution
- **Hardware access** - Real quantum hardware (IonQ, Rigetti, Quantinuum, etc.)
- **Real noise** - Results that reflect a specific device

## Unified Backend API (Recommended)

The `IQuantumBackend` interface (in `FSharp.Azure.Quantum.Core.BackendAbstraction`) is the **single consistent API** for local simulation and cloud backends (IonQ, Rigetti, Quantinuum, Atom Computing, IQM). Solvers and algorithms take an `IQuantumBackend`, so the same code runs on any of them.

### Creating Backends

```fsharp
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

// Local backend (no configuration needed)
let localBackend = LocalBackendFactory.createUnified()

// Cloud backends need an authenticated HttpClient and an Azure Quantum workspace URL;
// they are created with CloudBackends.CloudBackendFactory (see Backend Switching).
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
    let result =
        solveAsync backend distances_backend_demo defaultConfig CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    match result with
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

**No algorithm changes needed** - same `solveAsync` function, same distance matrix input!

### Running Your Own Circuits

For your own circuits, `Primitives` runs a `CircuitBuilder` circuit on any `IQuantumBackend`. This also makes testing easy: pass a different backend instance.

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder

// Bell state circuit
let bell =
    circuit {
        qubits 2
        H 0
        CNOT 0 1
    }

let executeWithBackend (backend: IQuantumBackend) (c: Circuit) (shots: int) =
    match Primitives.sample backend c shots with
    | Ok counts ->
        printfn "Backend: %s, Shots: %d" backend.Name shots
        counts
    | Error err ->
        eprintfn "Execution failed: %s" err.Message
        Map.empty

// Use local backend
let measurements_demo = executeWithBackend localBackend bell 1000
// e.g. map [("00", 507); ("11", 493)]
```

### Execution Results

There is no single result record shared by every call; each level returns what it naturally produces:

| Call | Returns |
|------|---------|
| `backend.ExecuteToState (CircuitAbstraction.wrapCircuit c)` | `Result<QuantumState, QuantumError>` - the final state (amplitudes on the local simulator) |
| `Primitives.getState backend c` | the same, for a `CircuitBuilder.Circuit` |
| `Primitives.sample backend c shots` | `Result<Map<string, int>, QuantumError>` - bitstring → count |
| `Primitives.run backend c shots` | `Result<int[][], QuantumError>` - one bit array per shot |
| `Primitives.observe backend c hamiltonian` | `Result<float, QuantumError>` - expectation value of a Pauli Hamiltonian |
| Solvers (TSP, MaxCut, ...) | their own solution records, including `BackendName` |

Bitstrings from `Primitives.sample` list **qubit 0 first**: `"10"` means qubit 0 measured 1 and qubit 1 measured 0. `QaoaSimulator.simulate` (below) writes the **highest qubit first**, like `Convert.ToString(index, 2)`.

On the local simulator `shots` can be anything. On a cloud backend `sample` and `run` return the job's own measured shots, so `shots` must equal the count the backend was created with (anything else is an `Error`), and `observe` is estimated from shots, one job per commuting group of Pauli terms.

Because every backend returns the same shapes, it is easy to:
- Compare results between backends
- Log execution metrics consistently
- Build visualizations that work with any backend

## Advanced: Low-Level Modules

**Note:** The following low-level modules are available for advanced use cases, but most users should use `IQuantumBackend` and the solvers shown above.

These modules provide direct access to quantum operations for:
- Educational purposes (learning how quantum simulation works)
- Custom simulation code
- Performance optimization for specific use cases

### 1. StateVector - Quantum State Representation

The `StateVector` module manages quantum state as a complex-valued vector.

```fsharp
open FSharp.Azure.Quantum.LocalSimulator

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
open FSharp.Azure.Quantum.LocalSimulator

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
  - Written as |control target⟩: |00⟩ → |00⟩, |01⟩ → |01⟩, |10⟩ → |11⟩, |11⟩ → |10⟩
- **CZ(qubit1, qubit2)**: Adds -1 phase if both qubits are |1⟩
  - |11⟩ → -|11⟩, all other states unchanged

### 3. QaoaSimulator - QAOA Circuit Execution

**Note:** For application development, use a solver or run a QAOA circuit on an `IQuantumBackend` (see [Integration with Backends](#integration-with-backends)). This low-level module is for educational purposes.

The `QaoaSimulator` module provides direct QAOA simulation operations:

```fsharp
open FSharp.Azure.Quantum.LocalSimulator

// Initialize uniform superposition manually
let state = QaoaSimulator.initializeUniformSuperposition 3

// Apply cost interaction (ZZ term): gamma, qubit1, qubit2, coefficient
let stateAfterCost = QaoaSimulator.applyCostInteraction 0.5 0 1 (-1.0) state

// Apply mixer layer (RX gates on all qubits)
let stateAfterMixer = QaoaSimulator.applyMixerLayer 0.3 stateAfterCost
```

**QAOA Circuit Structure:**

The circuit starts from the uniform superposition (H on every qubit). For depth p, QAOA then applies p layers of:
1. **Cost Hamiltonian**: Encodes problem structure
   - Rz rotations for single-qubit (diagonal) terms
   - ZZ rotations (CNOT - Rz - CNOT) for two-qubit terms, with angle 2·γ·weight
2. **Mixer Hamiltonian**: Enables exploration
   - Rx(-2β) on all qubits: e^(-iβH_M) with H_M = -Σ Xᵢ, whose ground state is the initial |+⟩^⊗n, so positive (γ, β) lower the cost (minimisation)

**QAOA Circuit Formula:**

<div style="display:block; margin-left: 1em;">
  <span style="color:#666666; font-weight:bold">H<sup>⊗n</sup>|0⟩^⊗n</span> → [<span style="color:#CC0066; font-weight:bold">Cost(γ₁)</span> → <span style="color:#0066CC; font-weight:bold">Mix(β₁)</span>] → ... → [<span style="color:#CC0066; font-weight:bold">Cost(γₚ)</span> → <span style="color:#0066CC; font-weight:bold">Mix(βₚ)</span>] → Measure
</div>

**Where:**
- <span style="color:#666666; font-weight:bold">H<sup>⊗n</sup>|0⟩^⊗n</span> = Initial uniform superposition (n qubits)
- <span style="color:#CC0066; font-weight:bold">Cost(γₖ)</span> = Cost Hamiltonian layer with parameter γₖ
- <span style="color:#0066CC; font-weight:bold">Mix(βₖ)</span> = Mixer Hamiltonian layer with parameter βₖ
- <span style="color:#009966; font-weight:bold">p</span> = Circuit depth (number of layer repetitions)
- <span style="color:#CC0066; font-weight:bold">γₖ</span>, <span style="color:#0066CC; font-weight:bold">βₖ</span> = Variational parameters (optimized classically)

### 4. Measurement - Observation and Sampling

Measure quantum states and sample outcomes.

```fsharp
open System
open FSharp.Azure.Quantum.LocalSimulator

// Create a superposition state
let state =
    StateVector.init 2
    |> Gates.applyH 0  // |0⟩ → (|0⟩+|1⟩)/√2 on qubit 0

// Get probability distribution (index i = basis state i, qubit 0 = bit 0)
let probabilities = Measurement.getProbabilityDistribution state
// probabilities = [| 0.5; 0.5; 0.0; 0.0 |]
//              index   0     1     2     3

// Born rule: P(i) = |αᵢ|²
let prob0 = Measurement.getBasisStateProbability 0 state  // 0.5
let prob1 = Measurement.getBasisStateProbability 1 state  // 0.5 (qubit 0 = 1)

// Sample outcomes with shots (non-destructive: each shot samples the same state)
let rng = Random()
let samples = Measurement.sampleAndCount rng 1000 state  // 1000 measurements
// Returns: Map<int, int> of basis_index → count
// Example: Map [(0, 503); (1, 497)]

// Measure one qubit, then collapse the state to match the outcome
let outcome = Measurement.measureSingleQubit rng 0 state  // 0 or 1 (50% chance each)
let collapsedState = Measurement.collapseAfterMeasurement 0 outcome state
printfn "Qubit 0 measured: %d" outcome

// Sample bitstrings (convert int outcomes to binary strings, highest qubit first)
let rawSamples = Measurement.sampleMeasurements rng 100 state
let bitstrings =
    rawSamples
    |> Array.countBy id
    |> Array.map (fun (outcome, count) ->
        (Convert.ToString(outcome, 2).PadLeft(2, '0'), count))
    |> Map.ofArray
// Returns: Map<string, int>, e.g. "00" → 52, "01" → 48

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

- **Collapse**: After measurement, state becomes consistent with the measured outcome (`collapseAfterMeasurement`)
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
let statsRng = Random()
let counts = Measurement.sampleAndCount statsRng numShots state

let statistics =
    counts
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
| 20 | about 1 million complex numbers | 16 MB |
| 30 | about 1 billion complex numbers | 16 GB |

**Note:** Each complex number uses 16 bytes (2 × 8-byte doubles). Applying a gate keeps two state vectors alive (source and result), and the simulator allows itself half of available memory, so an n-qubit simulation needs about 2^n × 64 bytes of total memory.

### Practical Limits

The simulator has three separate limits:

| Limit | Value | What it controls |
|-------|-------|------------------|
| `StateVector.maxQubits` | Derived from available memory, at least 20 and at most 30; override with `FSAQ_MAX_QUBITS` | Widest state `LocalBackend` can hold (reported as its `MaxQubits`) |
| `StateVector.practicalCircuitQubits` | 20 by default; override with `FSAQ_MAX_CIRCUIT_QUBITS` (clamped to `maxQubits`) | Widest circuit worth running; solvers refuse wider problems via `UnifiedBackend.getRunnableQubits` |
| `QaoaSimulator.simulate` | 16 qubits | The standalone QAOA simulator returns an `Error` above 16 qubits |

Each extra qubit doubles both memory and time per gate. Circuits of a few qubits run in milliseconds; around 16-20 qubits, iterative algorithms that run a circuit many times (VQE, QAOA optimization) become slow.

## Complete Example: MaxCut Problem

Let's solve a MaxCut problem using local simulation:

```fsharp
open FSharp.Azure.Quantum.LocalSimulator

// Define a 4-node graph MaxCut problem
//     0 --- 1
//     |  \  |
//     3 --- 2
// Goal: Partition nodes into two sets to maximize cut edges

let buildMaxCutCircuit numQubits edges gamma beta : QaoaSimulator.QaoaCircuit =
    {
        NumQubits = numQubits
        Parameters = [| gamma; beta |]  // [γ₁; β₁] for depth 1
        CostTerms =
            edges
            |> List.map (fun (i, j) -> (i, j, -1.0))  // Weight -1 for MaxCut
            |> Array.ofList
        Depth = 1
    }

// Bitstrings from QaoaSimulator list the highest qubit first,
// so qubit i is character (length - 1 - i)
let evaluateMaxCut edges (bitstring: string) =
    let isSet i = bitstring.[bitstring.Length - 1 - i] = '1'
    edges
    |> List.filter (fun (i, j) -> isSet i <> isSet j)  // Count cut edges
    |> List.length

let edges = [(0, 1); (1, 2); (2, 3); (3, 0); (0, 2)]  // 5 edges

// Grid search over QAOA parameters
let gammaRange = [0.0 .. 0.2 .. 1.0]
let betaRange = [0.0 .. 0.2 .. 1.0]

let bestResult =
    [ for gamma in gammaRange do
        for beta in betaRange do
            let circuit = buildMaxCutCircuit 4 edges gamma beta
            match QaoaSimulator.simulate circuit 1000 with
            | Ok result ->
                // Find best bitstring from this simulation
                let best =
                    result.Counts
                    |> Map.toList
                    |> List.map (fun (bs, count) ->
                        (bs, count, evaluateMaxCut edges bs))
                    |> List.maxBy (fun (_, _, cut) -> cut)
                Some (gamma, beta, best)
            | Error _ -> None
    ]
    |> List.choose id
    |> List.maxBy (fun (_, _, (_, _, cut)) -> cut)

let (optGamma, optBeta, (optBitstring, optCount, optCut)) = bestResult

printfn "Best QAOA Parameters:"
printfn "  γ = %.2f" optGamma
printfn "  β = %.2f" optBeta
printfn ""
printfn "Best Solution:"
printfn "  Partition: %s" optBitstring
printfn "  Cut edges: %d / %d" optCut edges.Length
printfn "  Frequency: %d / 1000 shots" optCount

// Verify solution
let inSet1 i = optBitstring.[optBitstring.Length - 1 - i] = '1'
let partition0 = [for i in 0..3 do if not (inSet1 i) then yield i]
let partition1 = [for i in 0..3 do if inSet1 i then yield i]
printfn ""
printfn "Partitions:"
printfn "  Set 0: %A" partition0
printfn "  Set 1: %A" partition1
```

**Example output** (parameters and frequencies vary from run to run; the maximum cut of this graph is 4):
```
Best QAOA Parameters:
  γ = 0.00
  β = 0.00

Best Solution:
  Partition: 0101
  Cut edges: 4 / 5
  Frequency: 61 / 1000 shots

Partitions:
  Set 0: [1; 3]
  Set 1: [0; 2]
```

The grid search keeps the best bitstring seen in any run, so with 16 possible bitstrings and 1000 shots it finds the optimum even at γ = β = 0 (a uniform superposition). To judge the parameters themselves, compare the average cut over all shots instead.

## Integration with Backends

`QaoaSimulator` is standalone: its `QaoaSimulator.QaoaCircuit` record is only understood by `QaoaSimulator.simulate`. To run QAOA on any `IQuantumBackend` (local or cloud), build a `Core.QaoaCircuit` from a QUBO and wrap it:

```fsharp
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Core.CircuitAbstraction

let qubo = array2D [ [ 1.0; -1.0 ]; [ -1.0; 1.0 ] ]
let problemHam = ProblemHamiltonian.fromQubo qubo
let mixerHam = MixerHamiltonian.create problemHam.NumQubits
let qaoa = QaoaCircuit.build problemHam mixerHam [| (0.5, 0.3) |]  // one (γ, β) layer

// Option 1: Local simulation (fast, free)
match localBackend.ExecuteToState (wrapQaoaCircuit qaoa) with
| Ok state ->
    let shots = UnifiedBackend.measureState state 1000  // one bit array per shot
    printfn "First shot: %A" shots.[0]
| Error err -> eprintfn "Execution failed: %s" err.Message

// Option 2: Azure Quantum - pass a cloud IQuantumBackend instead of localBackend
```

**Hybrid Development Workflow:**

1. Develop and test locally with the simulator (fast, free).
2. Choose parameters locally, for example by comparing the average cut of a few candidates:

```fsharp
let candidates = [ (0.2, 0.4); (0.6, 0.4); (1.0, 0.2) ]

let averageCut (gamma, beta) =
    match QaoaSimulator.simulate (buildMaxCutCircuit 4 edges gamma beta) 1000 with
    | Ok result ->
        let total = result.Counts |> Map.fold (fun acc bs count -> acc + count * evaluateMaxCut edges bs) 0
        float total / float result.Shots
    | Error _ -> 0.0

let (bestGamma, bestBeta) = candidates |> List.maxBy averageCut
```

3. Run the production problem on a cloud backend through the same solver or `IQuantumBackend` code, starting from the chosen parameters.

## Unit Testing with Local Simulation

The local simulator is ideal for unit testing quantum algorithms. The library's own tests use xUnit; add the `xunit` package to your test project:

<!-- fragment -->
```fsharp
module QaoaTests =
    open System
    open Xunit
    open FSharp.Azure.Quantum.LocalSimulator

    [<Fact>]
    let ``QAOA creates superposition`` () =
        // Setup: 2-qubit circuit with no cost terms (γ = 0, β = 0.5)
        let circuit : QaoaSimulator.QaoaCircuit = {
            NumQubits = 2
            Parameters = [| 0.0; 0.5 |]
            CostTerms = [||]
            Depth = 1
        }

        // Act: Simulate
        let result = QaoaSimulator.simulate circuit 1000

        // Assert: Should see multiple outcomes (superposition)
        match result with
        | Ok r ->
            Assert.True(r.Counts.Count > 1, "Should have multiple outcomes")
            Assert.Equal(1000, r.Shots)
        | Error msg ->
            Assert.Fail($"Simulation failed: {msg}")

    [<Fact>]
    let ``Single-qubit gates preserve normalization`` () =
        // Setup: Create initial state
        let state = StateVector.init 3

        // Act: Apply various gates
        let state' =
            state
            |> Gates.applyH 0
            |> Gates.applyX 1
            |> Gates.applyRz 2 (Math.PI / 4.0)

        // Assert: State should remain normalized (10 decimal places)
        let norm = StateVector.norm state'
        Assert.Equal(1.0, norm, 10)

    [<Fact>]
    let ``Measurement probabilities sum to 1`` () =
        // Setup: Create superposition
        let state =
            StateVector.init 2
            |> Gates.applyH 0
            |> Gates.applyH 1

        // Act: Get probabilities
        let probs = Measurement.getProbabilityDistribution state

        // Assert: Born rule - probabilities sum to 1
        let total = Array.sum probs
        Assert.Equal(1.0, total, 10)
```

## Error Handling

`QaoaSimulator.simulate` returns an `Error` for invalid input instead of throwing:

```fsharp
let baseCircuit : QaoaSimulator.QaoaCircuit =
    { NumQubits = 3; Parameters = [| 0.5; 0.3 |]; CostTerms = [| (0, 1, -1.0) |]; Depth = 1 }

let report (circuit: QaoaSimulator.QaoaCircuit) =
    match QaoaSimulator.simulate circuit 1000 with
    | Ok _ -> printfn "OK"
    | Error msg -> printfn "Error: %s" msg

// ❌ Too many qubits
report { baseCircuit with NumQubits = 17 }
// "Number of qubits must be between 1 and 16, got 17"

// ❌ Mismatched parameters (depth 2 needs 4 parameters)
report { baseCircuit with Depth = 2 }
// "Parameters array length (2) must equal Depth * 2 (4)"

// ❌ Invalid edge indices (qubit 5 doesn't exist)
report { baseCircuit with CostTerms = [| (0, 5, -1.0) |] }
// "QAOA simulation failed: Qubit indices (0, 5) out of range for 3-qubit state"
```

The low-level `Gates` functions throw instead: `Gates.applyX 5 (StateVector.init 3)` raises "Qubit index 5 out of range for 3-qubit state".

## Next Steps

- **[API Reference](api-reference.md)** - Complete API documentation
- **[Getting Started Guide](getting-started.md)** - Installation and first steps
- **[Error Mitigation](error-mitigation.md)** - Uses the noisy density-matrix simulator

## FAQ

**Q: How many qubits can I simulate?**  
A: A state vector holds 2^n complex numbers of 16 bytes: 16 MB at 20 qubits, 16 GB at 30. `StateVector.maxQubits` picks the widest n that fits half of available memory (for example 24 qubits with 1 GB, 28 with 16 GB, 30 with 64 GB or more), never less than 20 and never more than 30, because .NET cannot allocate a single array of 2^31 amplitudes. Separately, `StateVector.practicalCircuitQubits` (20 by default) limits how wide a circuit solvers will run, because time also doubles with every qubit.

**Q: How accurate is the simulator?**  
A: The simulator implements exact state vector evolution with floating-point arithmetic. Expect ~1e-14 numerical precision (double precision). This is sufficient for algorithm development and unit testing.

**Q: Can I simulate noise?**  
A: Yes, for small circuits. `Backends.DensityMatrixSimulator.NoisyLocalBackend` evolves a density matrix with a depolarizing channel after each gate and implements `IQuantumBackend`, so `Primitives.sample` and `Primitives.observe` work with it. It is limited to 8 qubits. `LocalBackend` itself is noiseless.

**Q: How do I compare local vs Azure results?**  
A: Run the same circuit with `Primitives.sample` on both backends; both return `Map<string, int>` (bitstring → count):
```fsharp
// Local simulator
let localCounts: Map<string, int> = measurements_demo

// Azure Quantum (with a configured cloud backend)
// let azureCounts = Primitives.sample azureBackend bell 1000

// Can directly compare distributions
```

**Q: Can I use this for algorithms other than QAOA?**  
A: Yes. `LocalBackend` runs any `CircuitBuilder` circuit, and the library's algorithms (Grover, QFT, QPE, VQE and others) take an `IQuantumBackend`. The `StateVector`, `Gates` and `Measurement` modules are also general-purpose.

---

**Last Updated**: 2026-09-29
