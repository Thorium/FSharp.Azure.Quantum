# FSharp.Azure.Quantum Architecture Analysis

## Current Architecture (AS-IS)

### Layer 1: Circuit Representations (Multiple Incompatible Types)

```
CircuitBuilder.Circuit          ← General purpose gates (H, X, CNOT, RX, etc.)
    ↓ NOT compatible with ↓
QaoaCircuit.QaoaCircuit         ← QAOA-specific (Hamiltonian, layers, RZZ)
    ↓ NOT compatible with ↓
IonQBackend.IonQCircuit         ← IonQ format (JSON serialization)
    ↓ NOT compatible with ↓
RigettiBackend.QuilProgram      ← Rigetti Quil format
```

**Problem**: Each backend requires its own circuit format!

### Layer 2: Backend Implementations (Disconnected)

```
Types.Backend                   ← Metadata only (Id, Provider, Name, Status)
    ↑ used by ↑
Client.QuantumClient            ← Azure Quantum REST API client
    ↑ used by ↑
IonQBackend.submitAndWaitForResultsAsync    ← Takes IonQCircuit → Map<string, int>
RigettiBackend.submitAndWaitForResultsAsync ← Takes QuilProgram → Map<string, int>

HybridSolver.QuantumBackend     ← Backend selector DU (IonQ | Rigetti)
    ↓ NEVER ACTUALLY USED ↓
HybridSolver.executeQuantumTsp  ← Has TODOs, uses classical fallback!
```

**Problem**: Backends exist but are never connected to solvers!

### Layer 3: Business Logic (Solvers)

```
TspSolver.solve                 ← Classical only (2-opt, nearest neighbor)
    ↑ called by ↑
HybridSolver.solveTsp           ← Tries to route to quantum, but...
    ↓ calls ↓
HybridSolver.executeQuantumTsp  ← Just returns TspSolver.solve (classical!)

PortfolioSolver.solve           ← Classical only (greedy, mean-variance)
    ↑ called by ↑  
HybridSolver.solvePortfolio     ← Same pattern - quantum is stubbed
```

**Problem**: "Hybrid" solver only uses classical!

---

## Current Data Flow (What Actually Happens)

### Scenario: User calls HybridSolver.solveTsp

```
User
  ↓ calls solveTsp(distances, quantumConfig=Some(...))
HybridSolver.solveTsp
  ↓ calls QuantumAdvisor.getRecommendation
QuantumAdvisor
  ↓ returns "StronglyRecommendQuantum"
HybridSolver.solveTsp
  ↓ calls executeQuantumTsp (line 165)
executeQuantumTsp
  ↓ IGNORES quantumConfig!
  ↓ calls TspSolver.solveWithDistances (classical!)
TspSolver
  ↓ returns classical solution
  ↓ pretends it's quantum! (line 168 says Method = Quantum)
```

**This is a lie!** The code says "Quantum" but uses classical solver.

---

## Issues Summary

### 1. **Circuit Format Tower of Babel**
- 4 different circuit types that don't convert to each other
- QaoaCircuit can't be sent to IonQ/Rigetti directly
- Each solver would need 4 implementations!

### 2. **Backend Isolation**
- IonQBackend and RigettiBackend work (382+ lines, full tests)
- But NO solver actually uses them
- HybridSolver has the infrastructure but doesn't connect

### 3. **Misleading Abstraction**
- HybridSolver.QuantumBackend looks like it selects backends
- But executeQuantumTsp ignores it completely
- Returns `Method = Quantum` for classical execution

### 4. **No Conversion Pipeline**
```
Need: TSP Problem → QUBO → QaoaCircuit → IonQCircuit → IonQ Results → TSP Solution
Have: TSP Problem → ❌ (gaps everywhere) → TSP Solution
```

---

## Desired Architecture (SHOULD-BE)

### Layer 1: Unified Circuit Abstraction

```fsharp
// Core circuit interface - all backends implement this
type ICircuit =
    abstract member NumQubits: int
    abstract member Gates: Gate list  // Generic gate representation

// Backend-specific adapters
module CircuitAdapter =
    let toIonQ: ICircuit -> IonQCircuit
    let toRigetti: ICircuit -> QuilProgram  
    let toQaoaCircuit: ICircuit -> QaoaCircuit
```

### Layer 2: Unified Backend Interface

```fsharp
// All backends implement this
type IQuantumBackend =
    abstract member Execute: ICircuit -> int -> Async<Result<MeasurementCounts, QuantumError>>
    abstract member Name: string
    abstract member MaxQubits: int option

// Concrete implementations
type LocalBackend() =
    interface IQuantumBackend with
        member _.Execute circuit shots = 
            // QaoaSimulator.simulate
            
type IonQBackend(client: QuantumClient, target: string) =
    interface IQuantumBackend with
        member _.Execute circuit shots =
            let ionqCircuit = CircuitAdapter.toIonQ circuit
            IonQBackend.submitAndWaitForResultsAsync client target ionqCircuit shots

type RigettiBackend(client: QuantumClient, target: string) =
    interface IQuantumBackend with
        member _.Execute circuit shots =
            let quilProgram = CircuitAdapter.toRigetti circuit
            RigettiBackend.submitAndWaitForResultsAsync client target quilProgram shots
```

### Layer 3: Solver Integration

```fsharp
// Solvers receive backends as dependency injection
module TspSolver =
    let solveQuantum (backend: IQuantumBackend) (distances: float[,]) : Async<Result<TspSolution, string>> =
        async {
            // 1. Convert TSP to QUBO
            let qubo = GraphOptimization.Tsp.toQubo distances
            
            // 2. Convert QUBO to circuit
            let circuit = QaoaCircuit.fromQubo qubo p=1
            
            // 3. Execute on backend
            let! result = backend.Execute circuit 1000
            
            // 4. Decode measurements to tour
            match result with
            | Ok counts ->
                let tour = GraphOptimization.Tsp.decodeTour counts distances.GetLength(0)
                return Ok { Tour = tour; Distance = calculateTourDistance tour distances }
            | Error e -> return Error (string e)
        }

// HybridSolver becomes a router
module HybridSolver =
    let solveTsp (backend: IQuantumBackend option) (distances: float[,]) =
        match backend with
        | Some qBackend ->
            // Use quantum
            match Async.RunSynchronously (TspSolver.solveQuantum qBackend distances) with
            | Ok solution -> { Method = Quantum; Result = solution; ... }
            | Error _ -> 
                // Fallback to classical
                let solution = TspSolver.solveClassical distances
                { Method = Classical; Result = solution; ... }
        | None ->
            // Classical only
            let solution = TspSolver.solveClassical distances
            { Method = Classical; Result = solution; ... }
```

---

## Migration Path

### Phase 1: Circuit Unification (Foundation)
1. Define `ICircuit` interface
2. Implement `CircuitAdapter.toIonQ` and `CircuitAdapter.toRigetti`
3. Add tests for circuit conversion

### Phase 2: Backend Abstraction (Infrastructure)
1. Define `IQuantumBackend` interface
2. Wrap existing IonQBackend/RigettiBackend
3. Implement LocalBackend wrapper around QaoaSimulator
4. Remove `HybridSolver.QuantumBackend` DU (use IQuantumBackend instead)

### Phase 3: Solver Integration (Business Logic)
1. Add `TspSolver.solveQuantum(backend, distances)`
2. Implement QUBO → Circuit → Execute → Decode pipeline
3. Update `HybridSolver.solveTsp` to actually call quantum backends
4. Add integration tests

### Phase 4: Cleanup (Polish)
1. Remove misleading "Method = Quantum" for classical execution
2. Consolidate `Types.Backend` (metadata) vs `IQuantumBackend` (execution)
3. Update documentation

---

## Benefits of New Architecture

### ✅ Single Execution Path
```
Any Solver → ICircuit → Any Backend → Results
```

### ✅ Easy Backend Switching
```fsharp
let ionq = IonQBackend(client, "ionq.simulator")
let rigetti = RigettiBackend(client, "rigetti.sim")
let local = LocalBackend()

// Same solver, different backend
TspSolver.solveQuantum ionq distances
TspSolver.solveQuantum rigetti distances  
TspSolver.solveQuantum local distances
```

### ✅ Testable
```fsharp
type MockBackend() =
    interface IQuantumBackend with
        member _.Execute circuit shots = 
            async { return Ok (Map ["000", 500; "111", 500]) }

// Test solver logic without real backend
TspSolver.solveQuantum (MockBackend()) distances
```

### ✅ Honest Abstractions
- If backend is None → use classical (clearly stated)
- If backend is Some(ionq) → actually use IonQ!
- No more lying about which method was used

---

## Key Design Decisions

### 1. Interface vs. DU for Backends?

**Current**: DU (`HybridSolver.QuantumBackend = IonQ | Rigetti`)  
**Proposed**: Interface (`IQuantumBackend`)

**Rationale**:
- Interfaces allow adding new backends without changing core code
- Easier to mock for testing
- Follows OCP (Open-Closed Principle)

### 2. Circuit Format Strategy?

**Option A**: Single universal format (e.g., OpenQASM)  
**Option B**: Adapter pattern (ICircuit → backend-specific)

**Proposed**: Option B (Adapter)

**Rationale**:
- Don't force all circuits through OpenQASM serialization
- Keep type safety (QaoaCircuit has structure, not just text)
- Adapters can optimize for each backend

### 3. Where Should Conversion Happen?

**Option A**: In backend implementations  
**Option B**: In solver layer  
**Option C**: Separate adapter layer

**Proposed**: Option C (Adapter Layer)

**Rationale**:
- Separation of concerns
- Testable in isolation
- Reusable across solvers

---

## Questions for Decision

1. **Should we keep QaoaCircuit as-is or make it implement ICircuit?**
   - Pro: Backward compatible
   - Con: Special case instead of general

2. **Should LocalBackend use QaoaSimulator directly or have its own circuit type?**
   - Pro (direct): Simpler
   - Con (direct): Tight coupling

3. **Do we need a separate `QuantumExecutionConfig` or fold it into backend constructor?**
   - Current: Config is separate from backend
   - Proposed: Backend knows its own config

4. **Should HybridSolver keep QuantumAdvisor integration?**
   - Yes: Smart routing based on problem size
   - No: Let user decide (simpler)

---

## Compatibility Impact

### Breaking Changes
- `HybridSolver.QuantumBackend` DU → removed (use `IQuantumBackend`)
- `HybridSolver.solveTsp` signature change (backend parameter)
- `TspSolver` gains new `solveQuantum` function

### Non-Breaking Additions
- `ICircuit` interface
- `IQuantumBackend` interface
- `CircuitAdapter` module
- Backend wrapper classes

### Safe to Remove
- Deleted `QuantumBackend.fs` (already done ✅)
- `HybridSolver.executeQuantumTsp` (stub that lies)
