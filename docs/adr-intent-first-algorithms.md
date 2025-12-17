# ADR: Intent-First Algorithms (DU-Based), Not Gate-Lingua-Franca

**Status**: Accepted (proposed)

**Date**: 2025-12-17

## Context

This repository already provides a unified execution surface via `IQuantumBackend` (`ExecuteToState`, `ApplyOperation`, `InitializeState`, `NativeStateType`, `SupportsOperation`). This enables writing algorithms once and running them on multiple backends.

However, several “generic” algorithms and solvers still encode algorithm intent primarily as **gate sequences** (`QuantumOperation.Gate ...`). In practice, this makes the **gate model the lingua franca**, and forces non-gate-native backends (e.g., topological/anyonic systems) to translate gates into their native model.

This is risky because:

- Some backends are not naturally gate-native. Their correct semantics are better expressed as native operations or state transformations.
- Compilation paths (e.g., Gate → Braid) are not always exact, especially around basis changes / superposition semantics.
- Algorithms like Grover, QFT, and Shor are better defined by **semantic primitives** (prepare, oracle, diffusion, QFT, modular arithmetic, phase estimation) than by any particular gate decomposition.

We expect Microsoft’s future **Majorana** platform to be a topological quantum computer. A robust unified model must allow running generic algorithms without requiring “gate-first” translation as the definition of correctness.

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

## Migration Plan

1. **Grover**
   - Keep the previously introduced native path (but evolve toward DU-first intent planning over time).

2. **QFT (next)**
   - Refactor QFT to build an intent DU and choose between:
     - native execution (topological / Majorana)
     - explicit lowering to operations (`QuantumOperation` list) as a strategy

3. **Shor**
   - Split into intent components:
     - `ModularArithmeticIntent`
     - `PhaseEstimationIntent`
     - `QftIntent`
   - Allow each component to be planned independently per backend.

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
