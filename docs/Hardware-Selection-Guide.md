# Hardware Selection Guide

**Choosing the Right Quantum Backend for Your Application**

This guide helps you select the appropriate quantum backend (LocalBackend, IonQ, Rigetti, D-Wave) for your specific use case in FSharp.Azure.Quantum. The library also has cloud backends for Quantinuum, Atom Computing and IQM (see [Backend Switching](backend-switching.md)); they follow the same pattern as IonQ and Rigetti below.

> **About the hardware figures:** qubit counts, fidelities and prices below are approximate and describe the device generations named in each profile (IonQ Harmony, Rigetti Aspen-M). Providers retire and replace devices often, and current systems are larger; check the [Azure Quantum target list](https://learn.microsoft.com/azure/quantum/qc-target-list) before choosing. The library's own pre-flight checks (`CircuitValidator.KnownTargets`) use similar figures: 11 qubits for IonQ hardware and 79 for Rigetti Aspen-M-3.

---

## Quick Decision Tree

```
START: What are you trying to do?
│
├─ Learning / Development / Testing
│  └─→ LocalBackend (memory-derived width, ≤30; solvers run up to 20 by default)
│
├─ Small problem (≤11 qubits) + Need HIGH accuracy
│  └─→ IonQ (trapped ion, high gate fidelity)
│
├─ Medium problem (12-80 qubits) + Can tolerate some noise
│  └─→ Rigetti (superconducting, fast gates)
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
| **Qubit Count** | ≤20 practical, ≤30 in memory | 11 | ~80 | 5000+ |
| **Connectivity** | Full | All-to-all | Limited (grid) | Chimera/Pegasus graph |
| **Gate Fidelity** | Perfect | 99.5%+ | 97-99% | N/A (annealing) |
| **Coherence Time** | Infinite | ~1 second | ~50 μs | N/A |
| **Gate Time** | N/A (simulated) | ~200 μs | ~50 ns | N/A |
| **Circuit Depth** | Unlimited | ~100 gates | ~50 gates | N/A (fixed schedule) |
| **Cost** | Free | $$$ per shot | $$ per shot | $$ per second |
| **Best For** | Development | High-precision | Medium-scale NISQ | Large-scale opt. |
| **Algorithms** | All | Gate-based | Gate-based | QUBO/Ising optimization only |

---

## Detailed Backend Profiles

### 1. LocalBackend (Simulation)

**Technology:** Classical simulation of quantum state vector

**Specifications:**
- **Qubits:** derived from available memory at startup, not a fixed constant.
  A state vector holds 2ⁿ amplitudes × 16 bytes, and applying a gate holds two
  of them, so the library picks the widest n whose working set fits half of
  available memory — reported as `StateVector.maxQubits` and through
  `LocalBackend`'s `MaxQubits`. It never reports fewer than 20. Override with the
  `FSAQ_MAX_QUBITS` environment variable.
  - 10 qubits: 16 KB
  - 20 qubits: 16 MB
  - 26 qubits: 1 GB (needs ~4 GB machine)
  - 28 qubits: 4 GB (needs ~16 GB machine)
  - 30 qubits: 16 GB (needs ~64 GB machine)
- **Hard ceiling: 30 qubits.** The amplitudes live in one flat array and .NET
  caps a single array at `Array.MaxLength` (2,147,483,591 elements), so 2³¹
  amplitudes cannot be allocated at any memory size. Going wider needs a
  chunked state representation, not more RAM.
- **Runnable width:** time doubles with every qubit too, so solvers refuse problems
  wider than `StateVector.practicalCircuitQubits` (20 by default; override with
  `FSAQ_MAX_CIRCUIT_QUBITS`).
- **Fidelity:** Perfect (no noise). `NoisyLocalBackend` adds a depolarizing noise
  model for circuits of up to 8 qubits.
- **Speed:** Instant for small circuits, exponentially slower with qubits

**✅ Best For:**
- **Development and debugging** quantum algorithms
- **Unit testing** without cloud costs
- **Educational purposes** and learning
- **Small problems** (within the simulator width) where perfect accuracy is needed
- **Algorithm prototyping** before cloud submission

**❌ NOT Good For:**
- **Large problems** (beyond the simulator width) - exponentially slow
- **Noise studies** - only the simple depolarizing model of `NoisyLocalBackend`
- **Performance benchmarking** - simulation doesn't reflect real hardware

**Code Example:**
```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.GraphColoring

// No configuration needed - always available
let problem = graphColoring {
    node "A" ["B"; "C"]
    node "B" ["A"]
    node "C" ["A"]
    colors ["Red"; "Blue"]
}

// Backend None = the local simulator (3 nodes × 2 colors = 6 qubits)
match GraphColoring.solve problem 2 None with
| Ok solution -> printfn "Solution: %A" solution.Assignments
| Error err -> printfn "Error: %s" err.Message
```

**Cost:** Free ✅

---

### 2. IonQ (Trapped Ion)

**Technology:** Individual trapped ytterbium ions manipulated by lasers

**Specifications** (IonQ Harmony):
- **Qubits:** 11 physical qubits (newer IonQ systems have more)
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
- **Large problems** (more qubits than the device has)
- **Very deep circuits** (>100 gates) - accumulates errors
- **Cost-sensitive applications** - most expensive per shot

**Code Example:**
```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends.CloudBackends

// Authenticate (DefaultAzureCredential: `az login`, environment variables or managed identity)
let workspaceUrl =
    "https://<location>.quantum.azure.com/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Quantum/workspaces/<ws>"

let credential = Authentication.CredentialProviders.createDefaultCredential ()
let httpClient = Authentication.createAuthenticatedClient credential

// "ionq.simulator" for the free simulator, or a QPU target such as "ionq.qpu.aria-1"
let ionqBackend = CloudBackendFactory.createIonQ httpClient workspaceUrl "ionq.simulator" 1000

// Run a 3-qubit GHZ circuit on IonQ
let ghz =
    CircuitBuilder.empty 3
    |> CircuitBuilder.addGate (CircuitBuilder.H 0)
    |> CircuitBuilder.addGate (CircuitBuilder.CNOT(0, 1))
    |> CircuitBuilder.addGate (CircuitBuilder.CNOT(1, 2))

match Primitives.sample ionqBackend ghz 1000 with
| Ok counts -> printfn "Counts: %A" counts  // mostly "000" and "111"
| Error err -> printfn "Error: %s" err.Message
```

Chemistry solvers take the same backend through `SolverConfig.Backend`. Note that with `Method = GroundStateMethod.VQE`, `GroundStateEnergy.estimateEnergy` currently returns tabulated reference energies for molecules it recognises as H₂, H₂O or LiH without running circuits, so use a different molecule (or your own Hamiltonian, see [Bring Your Own Hamiltonian](bring-your-own-hamiltonian.md)) when the goal is to exercise the hardware.

> **Async alternative:** Cloud backends support `task { }` with `CancellationToken`. Use `backend.ExecuteToStateAsync circuit ct` or `Primitives.sampleAsync` for non-blocking execution. See [Backend Switching](backend-switching.md).

**Cost:** ~$0.30 per circuit execution (varies by shot count)

**When to Choose IonQ:** You need the **highest quality** results and your problem fits on the device.

---

### 3. Rigetti (Superconducting)

**Technology:** Superconducting transmon qubits at ~15 mK temperature

> *Physics: Superconducting qubits exploit Josephson junctions—two superconductors separated by a thin insulator. Quantum tunneling of Cooper pairs creates discrete energy levels that encode |0⟩ and |1⟩. First described by Josephson (1962), this earned the Nobel Prize and enabled Google, IBM, and Rigetti hardware.*

**Specifications** (Rigetti Aspen-M):
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
- **Optimization problems** (MaxCut, Graph Coloring with 20-80 variables)
- **Fast execution** required (gates are 1000x faster than IonQ)
- **Variational algorithms** that are noise-resilient

**❌ NOT Good For:**
- **High-precision calculations** - fidelity lower than IonQ
- **Deep circuits** (>50 gates) - decoherence destroys state
- **Algorithms requiring all-to-all connectivity** - SWAP overhead
- **Small problems** (<12 qubits) - use IonQ instead for better quality

**Code Example:**
```fsharp
// "rigetti.sim.qvm" for the simulator, or a QPU target such as "rigetti.qpu.ankaa-3"
let rigettiBackend = CloudBackendFactory.createRigetti httpClient workspaceUrl "rigetti.sim.qvm" 1000

// Run QAOA MaxCut on Rigetti
let vertices = ["A"; "B"; "C"; "D"; "E"; "F"]  // 6 vertices = 6 qubits
let edges = [
    ("A", "B", 1.0); ("B", "C", 1.0)
    ("C", "D", 1.0); ("D", "E", 1.0)
    ("E", "F", 1.0); ("F", "A", 1.0)
]

let maxCutProblem = MaxCut.createProblem vertices edges

match MaxCut.solve maxCutProblem (Some rigettiBackend) with
| Ok solution ->
    printfn "Max cut value: %.2f" solution.CutValue
    printfn "Partition S: %A" solution.PartitionS
| Error err ->
    printfn "Error: %s" err.Message
```

For QPUs with limited connectivity, `CloudBackendFactory.createRigettiRouted` takes the device coupling map and inserts the SWAP gates for you.

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
  - Portfolio optimization
  - Job shop scheduling
  - MaxCut on large graphs
  - Small routing problems (TSP needs N² binary variables, so even 50 cities is 2,500 variables before embedding)
- **QUBO problems** (Quadratic Unconstrained Binary Optimization)
- **Ising model simulations**
- **Combinatorial optimization** at scale
- **Fast sampling** once the problem is embedded

**❌ NOT Good For:**
- **General quantum algorithms** (Shor's, Grover, QFT, QPE) - annealer can't run these
- **Quantum chemistry** (VQE) - requires gate model
- **Quantum machine learning** (VQC, QSVM) - requires gate model
- **Problems not expressible as QUBO/Ising** - fundamental limitation

**Code Example:**

The D-Wave backend implements `IQuantumBackend`, so QUBO-based solvers such as `MaxCut.solve` accept it: the solver's QAOA circuit is converted back to a QUBO and annealed.

```fsharp
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

// Reads DWAVE_API_TOKEN (required), DWAVE_ENDPOINT and DWAVE_SOLVER (default "Advantage_system6.1")
match RealDWaveBackend.createFromEnv () with
| Error err -> printfn "D-Wave not configured: %s" err.Message
| Ok dwave ->
    use dwave = dwave
    let ring =
        MaxCut.createProblem
            [ for i in 1 .. 20 -> $"N{i}" ]
            [ for i in 1 .. 20 -> ($"N{i}", $"N{i % 20 + 1}", 1.0) ]

    match MaxCut.solve ring (Some(dwave :> IQuantumBackend)) with
    | Ok solution -> printfn "Cut value: %.1f" solution.CutValue
    | Error err -> printfn "Error: %s" err.Message

// Offline testing: a mock annealer with the same interface
let mockDWave = DWaveBackend.createDefaultMockBackend () :> IQuantumBackend
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
| **VQE (chemistry)** | IonQ (<4 atoms), Rigetti (4-8 atoms) | 4-20 | Shallow circuits, noise-resilient; the library's chemistry path caps molecules at 20 qubits |
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
| **TSP** (N² qubits) | ≤4 cities | LocalBackend | Proof of concept; 3 cities also fit an 11-qubit device |
| | 5-8 cities | Rigetti | 25-64 qubits |
| | 9+ cities | D-Wave or classical | 81+ variables; annealer or HybridSolver |
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
- Problem >20 qubits (the simulator's default runnable width)
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
| 50 | Not possible (>30) | N/A | ~2 sec | ~50 ms |
| 100 | Not possible | N/A | N/A (>80) | ~50 ms |
| 1000 | Not possible | N/A | N/A | ~100 ms |

Cloud times exclude queueing, which usually dominates. **Key Takeaway:** once a QUBO problem is embedded, D-Wave's annealing time hardly depends on problem size (for QUBO problems only).

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

**Example:** a 100-variable dense QUBO might use 500-1000 physical qubits after embedding

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

`ErrorMitigationStrategy.selectStrategy` recommends techniques from the circuit size, backend, budget and accuracy target:

```fsharp
let rigettiCriteria : ErrorMitigationStrategy.SelectionCriteria =
    { CircuitDepth = 30
      QubitCount = 6
      Backend = { Id = "rigetti.sim.qvm"; Provider = "Rigetti"; Name = "Rigetti QVM"; Status = "Available" }
      MaxCostUSD = Some 50.0
      RequiredAccuracy = None
      Calibration = None } // supply a ReadoutErrorMitigation calibration to enable REM correction

let mitigationStrategy = ErrorMitigationStrategy.selectStrategy rigettiCriteria
printfn "%s" mitigationStrategy.Reasoning  // medium circuit with budget: ZNE + readout
```

Mitigation is applied to results, not to a backend: ZNE and PEC run your circuit through an executor (`ZeroNoiseExtrapolation.mitigate`, `ProbabilisticErrorCancellation.mitigate`), and REM corrects measured histograms (`ReadoutErrorMitigation.correctReadoutErrors`). The chemistry solvers accept a strategy in `SolverConfig.ErrorMitigation` and apply its readout part to their measurement counts. See [Error Mitigation](error-mitigation.md) for complete examples.

---

## Summary: Quick Selection Table

**I want to...**

| Goal | Backend Choice |
|------|----------------|
| Learn quantum computing | LocalBackend |
| Develop/debug algorithm | LocalBackend |
| Test small problem (≤20 qubits) | LocalBackend (free) |
| Solve high-precision chemistry (H2, LiH) | IonQ |
| Solve medium NISQ problem (20-80 qubits) | Rigetti |
| Solve large optimization (100-5000 vars) | D-Wave Advantage |
| Minimize cost | LocalBackend → Rigetti → IonQ |
| Maximize accuracy | IonQ → Rigetti → D-Wave |
| Maximize scale | D-Wave (QUBO only) |
| Get fastest results | D-Wave (if QUBO) → Rigetti → IonQ |

---

## Further Reading

- [Azure Quantum Documentation](https://docs.microsoft.com/en-us/azure/quantum/)
- [IonQ Hardware Specifications](https://ionq.com/quantum-systems)
- [Rigetti Systems](https://www.rigetti.com/systems)
- [D-Wave Advantage System](https://www.dwavesys.com/solutions-and-products/systems/)
- [Quantum Computing: An Applied Approach (Hidary, Ch 5)](https://link.springer.com/chapter/10.1007/978-3-030-83274-2_5) - Building Quantum Computers

---

**Happy quantum computing! Choose wisely! 🚀**
