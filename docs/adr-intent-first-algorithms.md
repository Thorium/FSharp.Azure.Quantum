# ADR: Intent-First Algorithms (Intent/Plan/Execute), Not Gate-Lingua-Franca

**Status**: Accepted

**Date**: 2025-12-17

## What This Means For Library Users

If you are just *using* FSharp.Azure.Quantum (F# or C#), this ADR is mainly about **making algorithms run correctly across very different backends** (state-vector simulators, Azure Quantum providers, and future topological/Majorana-style backends).

Practical impact:

- **Same public API, more portability**: You keep calling the same algorithm entry points (`Grover`, `AmplitudeAmplification`, `QFT`, etc.). Internally, the library plans execution based on backend capabilities.
- **Backend-dependent execution strategy**: The same algorithm may run via different strategies, for example:
  - a backend-native semantic primitive (preferred), or
  - an explicit lowering into supported operations (gates/braids), when valid.
- **Clearer failures instead of silent mismatches**: If a backend cannot execute the required intent and there is no valid lowering, the library should return a clear error rather than silently running an incorrect “gate-shaped” approximation.
- **Performance may change** (usually improve) depending on backend: intent-first execution can avoid unnecessary gate decompositions on backends that are not gate-native.

If you need to reason about backend support, use the existing capability probes (`IQuantumBackend.SupportsOperation`, `NativeStateType`), and prefer algorithm APIs that expose explicit configuration for exactness/approximation where available.

---

## Context

This repository already provides a unified execution surface via `IQuantumBackend` (`ExecuteToState`, `ApplyOperation`, `InitializeState`, `NativeStateType`, `SupportsOperation`). This enables writing algorithms once and running them on multiple backends.

However, several “generic” algorithms and solvers still encode algorithm intent primarily as **gate sequences** (`QuantumOperation.Gate ...`). In practice, this makes the **gate model the lingua franca**, and forces non-gate-native backends to translate gates into their native model.

This is risky because:

- Some backends are not naturally gate-native. Their correct semantics are better expressed as native operations or state transformations.
- Some backends (notably **annealers** like D-Wave / Azure Quantum Optimization) are not even *unitary-circuit* machines; they are best expressed as **sampling** problems (QUBO/Ising) rather than gate sequences.
- Compilation paths (e.g., Gate → Braid) are not always exact, especially around basis changes / superposition semantics.
- Algorithms like Grover, QFT, and Shor are better defined by **semantic primitives** (prepare, oracle, diffusion, QFT, modular arithmetic, phase estimation) than by any particular gate decomposition.

We expect Microsoft’s future **Majorana** platform to be a topological quantum computer, and Azure Quantum includes **annealing** providers. A robust unified model must allow running workloads across these paradigms without requiring “gate-first” translation as the definition of correctness.

## Decision

**Algorithms and solvers must be expressed as “intent” first, using Algebraic Data Types (F# discriminated unions and records), and only lowered to gates/braids as an explicit execution strategy.**

- “Gate circuits” are treated as **one valid lowering strategy**, not the canonical representation of algorithm intent.
- Backends may execute intent either:
  - natively in their preferred model (gate-based, topological, sparse, etc.), or
  - via explicit lowering to a supported operation set with an explicit correctness contract.

## Principles

1. **Intent is canonical**
   - Algorithm meaning is captured by DUs/records.
   - A gate-level decomposition is a strategy artifact.

2. **Execution is planned**
   - Execution uses a planning step: *intent → plan → execution*.
   - Plans are explicit about which strategy is chosen.

3. **No silent fallback to gates**
   - If an algorithm cannot be executed natively and a lowering is not valid/available, it must fail with a clear error (not silently “do something gate-shaped”).

4. **Correctness is explicit**
   - Plans/results must be explicit about exactness vs approximation.

5. **Interfaces are optional and justified**
   - Prefer DUs and pattern matching.
   - Use interfaces only as an “open-world plugin point” (e.g., external provider packages) when we cannot depend on concrete backend types.

## Type Model (Recommended Pattern)

Each algorithm module (e.g., `Grover`, `QFT`, later `Shor`) should define:

### 1) Intent DU

A DU expressing what the algorithm step means.

Example (illustrative):

```fsharp
type QftIntent =
    { Qubits: int list
      Inverse: bool
      ApplySwaps: bool }

type QftStep =
    | ApplyQft of QftIntent
    | ApplyControlledPhase of control:int * target:int * angle:float
    | ApplySwap of a:int * b:int
```

### 2) Plan DU

A DU expressing how we will execute the intent on a specific backend.

```fsharp
type Exactness =
    | Exact
    | Approximate of epsilon: float

type QftPlan =
    | ExecuteNatively of exactness: Exactness
    | ExecuteViaOps of ops: BackendAbstraction.QuantumOperation list * exactness: Exactness
```

### 3) Planner + Executor

- `plan : IQuantumBackend -> QftStep list -> Result<QftPlan, QuantumError>`
- `executePlan : IQuantumBackend -> QuantumState -> QftPlan -> Result<QuantumState, QuantumError>`

The algorithm entrypoints (`execute`, `executeOnState`, etc.) should:

1. Build intent (`QftStep list`) from configuration.
2. Plan against the backend.
3. Execute the chosen plan.

## Relating Intent to `IQuantumBackend`

`IQuantumBackend` remains the unified execution surface. Intent planning should use these signals:

- `backend.NativeStateType` (gate-based vs topological vs sparse vs mixed)
- `backend.SupportsOperation` (capability probes)
- Algorithm-specific invariants (e.g., “requires exact QFT” vs “approximate allowed”).

Lowering outputs must target existing `QuantumOperation` where possible:

- Gate-based lowering: `QuantumOperation.Gate ...`
- Topological lowering: `QuantumOperation.Braid ...` / `QuantumOperation.FMove ...`
- Annealing lowering: prefer **semantic intent** (typically `QuantumOperation.Extension ...`) instead of pretending a QUBO/Ising sampler is a gate backend
- Mixed: `QuantumOperation.Sequence ...` when beneficial

## When Interfaces Are Acceptable

By default, intent execution should use DUs and pattern matching on:

- `backend.NativeStateType`
- backend types within this repo (e.g., topological backend)

Interfaces may be used when we need **open-world extensibility**, e.g.:

- A Majorana/Azure provider backend shipped as a separate package can provide a native intent executor without the algorithm module referencing that package.

If interfaces are used, they should be:

- narrow and semantic (e.g., “execute QFT intent”), not gate-shaped
- optional: algorithm still works via other strategies if valid

## Testing & Correctness Contract

- Simulator backends should generally provide `Exact` execution plans.
- Hardware backends may return `Approximate epsilon`.

Tests should be written to reflect this:

- “Exactness-required” tests should reject approximate plans.
- Probabilistic/hardware-like tests should allow thresholds/tolerances.

## Current Status (Implemented In Repo)

The intent-first pattern described here is already implemented in several core algorithms.

- **Grover** (`src/FSharp.Azure.Quantum/Algorithms/Grover.fs`)
  - Implements explicit **intent  plan  execute** (`GroverSearchIntent`, `GroverPlan`).
  - Chooses between native semantic ops (`QuantumOperation.Algorithm (Grover*)`) and explicit lowering.

- **Amplitude Amplification** (`src/FSharp.Azure.Quantum/Algorithms/AmplitudeAmplification.fs`)
  - Implements explicit intent/plan/execute (`AmplitudeAmplificationIntent`, `AmplitudeAmplificationPlan`).
  - Can reuse Grover semantic ops where applicable, otherwise lowers to `QuantumOperation` sequences.

- **QFT** (`src/FSharp.Azure.Quantum/Algorithms/QFT.fs`)
  - Implements explicit intent/plan/execute (`QftExecutionIntent`, `QftPlan`).
  - Plans native execution via `AlgorithmOperation.QFT` when supported; otherwise uses explicit gate-sequence lowering.

- **QPE** (`src/FSharp.Azure.Quantum/Algorithms/QPE.fs`)
  - Implements explicit intent/plan/execute (`QpeExecutionIntent`, `QpePlan`).
  - Plans native execution via `AlgorithmOperation.QPE` when supported; otherwise lowers to supported operations.

- **Shor** (`src/FSharp.Azure.Quantum/Algorithms/Shor.fs`)
  - Uses planning as part of period finding and factorization.
  - Delegates to QPE planning so exactness/constraints are made explicit in the selected plan.

- **Annealing intent as an extension operation** (`src/FSharp.Azure.Quantum/Backends/DWaveBackend.fs`)
  - Demonstrates how non-unitary backends can expose **semantic intents** via `QuantumOperation.Extension` (e.g., sampling an Ising problem) without pretending to be a gate backend.

- **HHL / Quantum Regression** (`src/FSharp.Azure.Quantum/Algorithms/HHL.fs`)
  - Implements explicit intent/plan/execute (`HhlExecutionIntent`, `HhlPlan`).
  - General Hermitian matrices are supported via explicit gate-level lowering (controlled Trotter-Suzuki Hamiltonian evolution) and backend-aware gate transpilation during planning.

## Follow-Ups (Optional Improvements)

- **Standardize plan visibility**: expose planned strategy in public results where it helps diagnostics.
- **Unify exactness vocabulary**: keep `Exact` vs `Approximate epsilon` consistent across algorithm modules.
- **Consider generic annealing intents**: model a provider-agnostic "sample Ising/QUBO" intent, with provider-specific payloads behind extensions or adapters.
- **Native plugin points**: add narrow intent-specific interfaces only where open-world provider extensibility is required (e.g., external Majorana provider package).

## Consequences

**Pros**
- Algorithms become truly backend-agnostic.
- Topological/Majorana backends can execute core primitives without pretending to be gate-based.
- Planning makes limitations and approximations explicit.
- F# pattern matching enforces completeness.

**Cons**
- Requires refactoring existing algorithms that currently “are gates.”
- Introduces planning/execution layers (more types), but they remain local and explicit.

## Notes

- This ADR does not require removing `QuantumOperation.Gate`. It changes its role: from “algorithm definition” to “lowering strategy.”
- `IQuantumBackend` remains the unifying interface. The “unification” moves to the *intent layer* rather than the gate layer.
