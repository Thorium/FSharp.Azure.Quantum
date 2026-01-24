# Hardware Selection Guide

**Choosing the Right Quantum Backend for Your Application**

This guide helps you select the appropriate quantum backend (LocalBackend, IonQ, Rigetti, D-Wave) for your specific use case in FSharp.Azure.Quantum.

---

## Quick Decision Tree

```
START: What are you trying to do?
│
├─ Learning / Development / Testing
│  └─→ LocalBackend (≤20 qubits)
│
├─ Small problem (≤11 qubits) + Need HIGH accuracy
│  └─→ IonQ Harmony (trapped ion, 99.5% fidelity)
│
├─ Medium problem (12-80 qubits) + Can tolerate some noise
│  └─→ Rigetti Aspen-M (superconducting, fast gates)
│
├─ Large optimization problem (100-5000 variables)
│  └─→ D-Wave Advantage (quantum annealer, QUBO/Ising only)
│
└─ Very large problem (>80 qubits gate-based)
   └─→ Wait for future hardware OR use HybridSolver (classical fallback)
```

---

## Backend Comparison Matrix

| Feature | LocalBackend | IonQ Harmony | Rigetti Aspen-M | D-Wave Advantage |
|---------|--------------|--------------|-----------------|------------------|
| **Type** | Simulator | Trapped Ion | Superconducting | Quantum Annealer |
| **Qubit Count** | ≤20 practical | 11 | ~80 | 5000+ |
| **Connectivity** | Full | All-to-all | Limited (grid) | Chimera/Pegasus graph |
| **Gate Fidelity** | Perfect | 99.5%+ | 97-99% | N/A (annealing) |
| **Coherence Time** | Infinite | ~1 second | ~50 μs | N/A |
| **Gate Time** | Instant | ~200 μs | ~50 ns | N/A |
| **Circuit Depth** | Unlimited | ~100 gates | ~50 gates | N/A (fixed schedule) |
| **Cost** | Free | $$$ per shot | $$ per shot | $$ per second |
| **Best For** | Development | High-precision | Medium-scale NISQ | Large-scale opt. |
| **Algorithms** | All | Gate-based | Gate-based | QAOA, VQE, Opt. only |

---

## Detailed Backend Profiles

### 1. LocalBackend (Simulation)

**Technology:** Classical simulation of quantum state vector

**Specifications:**
- **Qubits:** Technically unlimited, practically ≤20
  - 10 qubits: ~1 KB memory
  - 20 qubits: ~8 MB memory
  - 30 qubits: ~8 GB memory (impractical)
- **Fidelity:** Perfect (no noise, unless added deliberately)
- **Speed:** Instant for small circuits, exponentially slower with qubits

**✅ Best For:**
- **Development and debugging** quantum algorithms
- **Unit testing** without cloud costs
- **Educational purposes** and learning
- **Small problems** (≤20 qubits) where perfect accuracy is needed
- **Algorithm prototyping** before cloud submission

**❌ NOT Good For:**
- **Large problems** (>20 qubits) - exponentially slow
- **Noise studies** - too perfect unless noise model added
- **Performance benchmarking** - simulation doesn't reflect real hardware

**Code Example:**
```fsharp
open FSharp.Azure.Quantum

// No configuration needed - always available
let problem = graphColoring {
    node "A" ["B"; "C"]
    node "B" ["A"]
    node "C" ["A"]
    colors ["Red"; "Blue"]
}

// Automatically uses LocalBackend for ≤20 qubits
match GraphColoring.solve problem 2 None with
| Ok solution -> printfn "Solution: %A" solution
| Error err -> printfn "Error: %s" err.Message
```

**Cost:** Free ✅

---

### 2. IonQ Harmony (Trapped Ion)

**Technology:** Individual trapped ytterbium ions manipulated by lasers

**Specifications:**
- **Qubits:** 11 physical qubits
- **Connectivity:** All-to-all (any qubit can interact with any other)
- **Gate Fidelity:**
  - Single-qubit gates: 99.7%
  - Two-qubit gates: 99.5%
  - Measurement: 99.8%
- **Coherence Time:** ~1 second (1000x longer than superconducting)
- **Gate Time:** ~200 microseconds (slower than superconducting)

**✅ Best For:**
- **High-precision algorithms** (VQE, QPE, Shor's algorithm)
- **Small molecules** quantum chemistry (H2, H2O, LiH)
- **Algorithms requiring all-to-all connectivity** (no SWAP overhead)
- **Deep circuits** up to ~100 gates
- **Research requiring high fidelity** results

**❌ NOT Good For:**
- **Large problems** (>11 qubits) - qubit count limit
- **Very deep circuits** (>100 gates) - accumulates errors
- **Cost-sensitive applications** - most expensive per shot

**Code Example:**
```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core

// Configure IonQ backend
let workspace = AzureQuantumWorkspace.create 
    "your-subscription-id"
    "your-resource-group"
    "your-workspace-name"
    "eastus"

let ionqBackend = IonQBackend.create workspace "ionq.simulator"  // or "ionq.qpu"

// Run VQE on IonQ
let h2 = Molecule.createH2 0.74  // H2 molecule
let config = {
    Method = GroundStateMethod.VQE
    MaxIterations = 100
    Tolerance = 1e-6
    InitialParameters = None
}

async {
    let! result = QuantumChemistry.estimateGroundState h2 config (Some ionqBackend)
    match result with
    | Ok vqeResult -> 
        printfn "Ground state energy: %.6f Hartree" vqeResult.Energy
    | Error err -> 
        printfn "Error: %s" err.Message
}
|> Async.RunSynchronously
```

**Cost:** ~$0.30 per circuit execution (varies by shot count)

**When to Choose IonQ:** You need the **highest quality** results and your problem fits in 11 qubits.

---

### 3. Rigetti Aspen-M (Superconducting)

**Technology:** Superconducting transmon qubits at ~15 mK temperature

> *Physics: Superconducting qubits exploit Josephson junctions—two superconductors separated by a thin insulator. Quantum tunneling of Cooper pairs creates discrete energy levels that encode |0⟩ and |1⟩. First described by Josephson (1962), this earned the Nobel Prize and enabled Google, IBM, and Rigetti hardware.*

**Specifications:**
- **Qubits:** ~80 qubits (varies by generation)
- **Connectivity:** Limited (grid/lattice topology)
  - Nearest-neighbor interactions only
  - SWAP gates needed for distant qubits
- **Gate Fidelity:**
  - Single-qubit gates: 99.5%
  - Two-qubit gates: 97-99%
  - Measurement: 95-97%
- **Coherence Time:** ~50 microseconds
- **Gate Time:** ~50 nanoseconds (1000x faster than ion trap)
- **Circuit Depth:** ~50 gates practical (limited by coherence)

**✅ Best For:**
- **Medium-scale NISQ algorithms** (QAOA, VQE with 20-80 qubits)
- **Optimization problems** (MaxCut, Graph Coloring, TSP with 20-80 variables)
- **Fast execution** required (gates are 1000x faster than IonQ)
- **Larger molecular systems** (if you can tolerate lower fidelity)
- **Variational algorithms** that are noise-resilient

**❌ NOT Good For:**
- **High-precision calculations** - fidelity lower than IonQ
- **Deep circuits** (>50 gates) - decoherence destroys state
- **Algorithms requiring all-to-all connectivity** - SWAP overhead
- **Small problems** (<12 qubits) - use IonQ instead for better quality

**Code Example:**
```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core

let workspace = AzureQuantumWorkspace.create 
    "your-subscription-id"
    "your-resource-group"
    "your-workspace-name"
    "eastus"

let rigettiBackend = RigettiBackend.create workspace "rigetti.sim.qvm"  // or "rigetti.qpu.aspen-m-3"

// Run QAOA MaxCut on Rigetti
let vertices = ["A"; "B"; "C"; "D"; "E"; "F"]  // 6 vertices = 6 qubits
let edges = [
    ("A", "B", 1.0); ("B", "C", 1.0)
    ("C", "D", 1.0); ("D", "E", 1.0)
    ("E", "F", 1.0); ("F", "A", 1.0)
]

let problem = MaxCut.createProblem vertices edges

match MaxCut.solve problem (Some rigettiBackend) with
| Ok solution ->
    printfn "Max cut value: %.2f" solution.CutValue
    printfn "Partition S: %A" solution.PartitionS
| Error err ->
    printfn "Error: %s" err.Message
```

**Cost:** ~$0.10-0.20 per circuit execution (cheaper than IonQ)

**When to Choose Rigetti:** You need **more qubits** (12-80) and can tolerate some noise for speed/scale.

---

### 4. D-Wave Advantage (Quantum Annealer)

**Technology:** Quantum annealing with superconducting flux qubits

**Specifications:**
- **Qubits:** 5000+ physical qubits
- **Connectivity:** Pegasus graph topology (15 connections per qubit)
- **Type:** Quantum annealer (NOT gate-based)
  - Solves QUBO/Ising problems only
  - Cannot run arbitrary quantum circuits
- **Annealing Time:** ~20 microseconds
- **Programming Time:** ~5 microseconds per read
- **Coherence:** Not applicable (adiabatic evolution, not gates)

**✅ Best For:**
- **Large-scale optimization** (100-5000 variables)
  - Traveling Salesperson Problem (TSP)
  - Portfolio optimization
  - Vehicle routing
  - Job shop scheduling
  - MaxCut on large graphs
- **QUBO problems** (Quadratic Unconstrained Binary Optimization)
- **Ising model simulations**
- **Combinatorial optimization** at scale
- **When you need results NOW** (milliseconds vs minutes for gate-based)

**❌ NOT Good For:**
- **General quantum algorithms** (Shor's, Grover, QFT, QPE) - annealer can't run these
- **Quantum chemistry** (VQE) - requires gate model
- **Quantum machine learning** (VQC, QSVM) - requires gate model
- **Problems not expressible as QUBO/Ising** - fundamental limitation

**Code Example:**
```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.DWave

// Configure D-Wave backend
let dwaveConfig = {
    Solver = "Advantage_system6.4"  // Latest D-Wave hardware
    Chain Strength = 1.0
    NumReads = 1000
    AnnealingTime = 20  // microseconds
}

let dwaveBackend = RealDWaveBackend.create 
    "your-dwave-api-token"
    "your-dwave-endpoint"
    dwaveConfig

// Solve large TSP on D-Wave (100 cities)
let cities = [1..100] |> List.map (fun i -> sprintf "City%d" i)
let distances = // ... 100x100 distance matrix

let problem = tsp {
    for city in cities do
        addCity city
    for i in 0..99 do
        for j in i+1..99 do
            addDistance cities.[i] cities.[j] distances.[i].[j]
}

// D-Wave can handle 100 cities (gate-based limited to ~10)
match Tsp.solve problem (Some dwaveBackend) with
| Ok solution ->
    printfn "Tour length: %.2f" solution.TotalDistance
    printfn "Route: %A" solution.Tour
| Error err ->
    printfn "Error: %s" err.Message
```

**Cost:** ~$2 per minute of QPU time (cost-effective for large problems)

**When to Choose D-Wave:** You have a **large optimization problem** (>100 variables) expressible as QUBO.

---

## Decision Guide by Problem Type

### Quantum Algorithm Implementations

| Algorithm | Recommended Backend | Qubit Need | Notes |
|-----------|-------------------|------------|-------|
| **Grover's Search** | IonQ (small), Rigetti (medium) | 5-50 | High fidelity helps accuracy |
| **Shor's Factoring** | IonQ | 5-11 | QPE requires high precision |
| **QFT** | IonQ | 3-11 | Deep circuit, needs fidelity |
| **QPE** | IonQ | 5-11 | High precision critical |
| **VQE (chemistry)** | IonQ (<4 atoms), Rigetti (4-8 atoms) | 4-20 | Shallow circuits, noise-resilient |
| **QAOA** | Rigetti (medium), D-Wave (large) | 10-5000 | Optimization-focused |

### Optimization Problems

| Problem Type | Variables | Recommended Backend | Why |
|--------------|-----------|-------------------|-----|
| **Graph Coloring** | <10 | LocalBackend or IonQ | Small, test locally first |
| | 10-50 | Rigetti | Medium scale |
| | 50+ | D-Wave | Annealer excels here |
| **MaxCut** | <10 | LocalBackend or IonQ | Small, test locally |
| | 10-80 | Rigetti | Gate-based QAOA |
| | 80+ | D-Wave | Annealer optimal |
| **TSP** | <8 cities | LocalBackend/IonQ | Proof of concept |
| | 8-20 cities | Rigetti | Medium scale |
| | 20+ cities | D-Wave | Only option at scale |
| **Portfolio Opt.** | <10 assets | LocalBackend | Test first |
| | 10-50 assets | Rigetti | Medium portfolios |
| | 50+ assets | D-Wave | Large institutional |

### Quantum Machine Learning

| Task | Dataset Size | Recommended Backend | Notes |
|------|-------------|-------------------|-------|
| **Binary Classification (VQC)** | Small (<100 samples) | IonQ | High precision |
| | Medium (100-1000) | Rigetti | Acceptable noise |
| **Quantum Kernel SVM** | Any | IonQ or Rigetti | Depends on feature dimension |
| **Quantum Regression (HHL)** | Small systems | IonQ | Requires QPE (high precision) |

---

## Cost Considerations

### Development Phase

**Use LocalBackend** exclusively:
- Zero cost
- Instant results
- Perfect for debugging

**Only move to cloud when:**
- Problem >20 qubits
- Need real hardware noise characteristics
- Ready for production testing

### Production Phase

**Cost per 1000 runs** (approximate):

| Backend | Cost | When Worth It |
|---------|------|---------------|
| LocalBackend | $0 | Always test here first |
| D-Wave | ~$20-50 | Large optimization (>100 vars) |
| Rigetti | ~$100-200 | Medium NISQ (20-80 qubits) |
| IonQ | ~$300-500 | High precision required |

**Cost Optimization Tips:**
1. **Develop on LocalBackend** - test all logic before cloud
2. **Use simulators first** - `ionq.simulator`, `rigetti.sim.qvm` (cheaper)
3. **Batch jobs** - submit multiple problems in one QPU session
4. **Start small** - validate with 5-10 qubits before scaling
5. **Monitor spending** - Azure Cost Management alerts

---

## Performance Benchmarks

### Circuit Execution Time (Approximate)

| Qubits | LocalBackend | IonQ | Rigetti | D-Wave |
|--------|--------------|------|---------|--------|
| 5 | <1 ms | ~500 ms | ~100 ms | ~50 ms |
| 10 | ~10 ms | ~1 sec | ~200 ms | ~50 ms |
| 20 | ~1 sec | N/A (>11) | ~500 ms | ~50 ms |
| 50 | Hours | N/A | ~2 sec | ~50 ms |
| 100 | Impossible | N/A | N/A (>80) | ~50 ms |
| 1000 | Impossible | N/A | N/A | ~100 ms |

**Key Takeaway:** D-Wave annealer is consistently fast regardless of problem size (for QUBO problems only).

---

## Connectivity Matters

### IonQ: All-to-All Connectivity ✅

```
Every qubit can directly interact with every other qubit
No SWAP gates needed → Shorter circuits → Higher fidelity
```

**Example:** 5-qubit fully connected problem = 5 qubits on IonQ

### Rigetti: Limited Connectivity ⚠️

```
Grid topology: qubit i can only interact with neighbors
SWAP gates needed for distant qubits → Longer circuits → Lower fidelity
```

**Example:** 5-qubit fully connected problem might need 8-10 physical qubits + SWAPs

### D-Wave: Graph Connectivity 📊

```
Pegasus graph: Each qubit connected to ~15 neighbors (fixed topology)
Problem must map to Pegasus graph → May need "minor embedding"
Embedding efficiency varies by problem structure
```

**Example:** 100-variable TSP might use 500-1000 physical qubits after embedding

---

## Error Mitigation Recommendations

Different backends benefit from different error mitigation strategies:

| Backend | Recommended Mitigation | Why |
|---------|----------------------|-----|
| **LocalBackend** | None | Perfect simulation |
| **IonQ** | ZNE (Zero Noise Extrapolation) | High fidelity, ZNE works well |
| **Rigetti** | REM (Readout Error Mitigation) | Measurement errors dominant |
| **D-Wave** | Majority voting, spin-reversal | Annealer-specific techniques |

**Code Example:**
```fsharp
open FSharp.Azure.Quantum.ErrorMitigation

// Configure error mitigation for Rigetti
let mitigationStrategy = {
    ZNE = None  // Not as effective on noisy Rigetti
    PEC = None  // Too expensive for Rigetti noise level
    REM = Some { CalibrationShots = 1000 }  // ✅ Best for readout errors
}

// Apply mitigation to backend
let mitigatedBackend = ErrorMitigationStrategy.apply mitigationStrategy rigettiBackend
```

---

## Summary: Quick Selection Table

**I want to...**

| Goal | Backend Choice |
|------|----------------|
| Learn quantum computing | LocalBackend |
| Develop/debug algorithm | LocalBackend |
| Test small problem (<20 qubits) | LocalBackend (free) |
| Solve high-precision chemistry (H2, LiH) | IonQ Harmony |
| Solve medium NISQ problem (20-80 qubits) | Rigetti Aspen-M |
| Solve large optimization (100-5000 vars) | D-Wave Advantage |
| Minimize cost | LocalBackend → Rigetti → IonQ |
| Maximize accuracy | IonQ → Rigetti → D-Wave |
| Maximize scale | D-Wave (QUBO only) |
| Get fastest results | D-Wave (if QUBO) → Rigetti → IonQ |

---

## Further Reading

- [Azure Quantum Documentation](https://docs.microsoft.com/en-us/azure/quantum/)
- [IonQ Hardware Specifications](https://ionq.com/quantum-systems)
- [Rigetti Aspen-M Specifications](https://www.rigetti.com/systems)
- [D-Wave Advantage System](https://www.dwavesys.com/solutions-and-products/systems/)
- [Quantum Computing: An Applied Approach (Hidary, Ch 5)](https://link.springer.com/chapter/10.1007/978-3-030-83274-2_5) - Building Quantum Computers

---

**Happy quantum computing! Choose wisely! 🚀**
