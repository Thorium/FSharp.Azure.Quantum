# QuantumResult Computation Expression Builder

## Overview

Most library functions that can fail return `QuantumResult<'T>`, which is `Result<'T, QuantumError>`. The `quantumResult` computation expression removes the nested `match` expressions you otherwise write to pass errors along: each `let!` continues with the `Ok` value and stops at the first `Error`.

`quantumResult` is defined in `FSharp.Azure.Quantum.Core` and is available after `open FSharp.Azure.Quantum.Core`. The same namespace also has a general `result` builder for `Result<'T, 'E>` with any error type, and `quantumResultTask`, the asynchronous twin of `quantumResult` for steps that return `Task<QuantumResult<'T>>` (see [Asynchronous Steps: quantumResultTask](#asynchronous-steps-quantumresulttask)).

## Setup for the Examples

The examples on this page use a few small steps built on the library: validate some rotation angles, build a circuit, run it on a backend, and read a probability from the measurements. Each step returns a `QuantumResult`.

```fsharp
open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Backends

let localBackend = LocalBackend.LocalBackend() :> IQuantumBackend

/// Angles must be present, and one qubit per angle must stay small.
let validateInput (angles: float array) : QuantumResult<float array> =
    if angles.Length = 0 then
        Error(QuantumError.ValidationError("angles", "must not be empty"))
    elif angles.Length > 8 then
        Error(QuantumError.ValidationError("angles", "at most 8 angles (one qubit each)"))
    else
        Ok angles

/// One RY rotation per qubit.
let buildCircuit (angles: float array) : QuantumResult<ICircuit> =
    let circuit =
        angles
        |> Array.indexed
        |> Array.fold
            (fun c (qubit, angle) -> c |> CircuitBuilder.addGate (CircuitBuilder.RY(qubit, angle)))
            (CircuitBuilder.empty angles.Length)

    Ok(CircuitWrapper(circuit) :> ICircuit)

/// Run the circuit and return the final state.
let execute (backend: IQuantumBackend) (circuit: ICircuit) : QuantumResult<QuantumState> =
    backend.ExecuteToState circuit

/// Fraction of the shots in which qubit 0 was measured as 1.
let probabilityOfOne (shots: int) (state: QuantumState) : QuantumResult<float> =
    let samples = QuantumState.measure state shots
    Ok(samples |> Array.averageBy (fun bits -> float bits.[0]))
```

## Before: Nested Match Expressions

```fsharp
let processNested (angles: float array) (backend: IQuantumBackend) : QuantumResult<float> =
    match validateInput angles with
    | Error err -> Error err
    | Ok validAngles ->
        match buildCircuit validAngles with
        | Error err -> Error err
        | Ok circuit ->
            match execute backend circuit with
            | Error err -> Error err
            | Ok state ->
                match probabilityOfOne 1000 state with
                | Error err -> Error err
                | Ok probability -> Ok probability
```

**Problems:**
- One level of nesting per step
- The same `| Error err -> Error err` line repeated for every step
- Hard to read, and easy to get wrong when steps are added or reordered

## After: Computation Expression

```fsharp
let processWorkflow (angles: float array) (backend: IQuantumBackend) : QuantumResult<float> =
    quantumResult {
        let! validAngles = validateInput angles
        let! circuit = buildCircuit validAngles
        let! state = execute backend circuit
        let! probability = probabilityOfOne 1000 state
        return probability
    }
```

**Benefits:**
- Flat, linear structure
- Errors propagate automatically: the first `Error` ends the computation and is the result
- The steps read in the order they run

## Examples with Library Builders

### Example 1: Period Finding

`periodFinder` returns `Result<PeriodFinderProblem, QuantumError>` and `QuantumPeriodFinder.solve` returns a `QuantumResult`, so both can be bound with `let!`:

```fsharp
open FSharp.Azure.Quantum.QuantumPeriodFinder

let factor (n: int) : QuantumResult<int * int> =
    quantumResult {
        let! problem = periodFinder {
            number n
            precision 8
        }

        let! result = QuantumPeriodFinder.solve problem

        match result.Factors with
        | Some factors -> return factors
        | None -> return! Error(QuantumError.OperationError("PeriodFinder", $"no factors found for {n}"))
    }
```

### Example 2: Training and Prediction

The ML builders train when they are evaluated and yield a `Task<QuantumResult<_>>`, as do the prediction functions, so the steps go in `quantumResultTask { }`:

```fsharp
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Business.BinaryClassifier

let trainAndPredict
    (features: float[][])
    (labels: int[])
    (sample: float[])
    (ct: CancellationToken)
    : Task<QuantumResult<int>> =
    quantumResultTask {
        // Inside the builder, `cancellationToken` names its custom operation.
        let! model = binaryClassification {
            trainWith features labels
            maxEpochs 50
            cancellationToken ct
        }

        let! prediction = BinaryClassifier.predictAsync sample model ct
        return prediction.Label
    }
```

### Example 3: Intermediate Values

Plain `let` bindings can sit between the `let!` steps, and every earlier value stays in scope:

```fsharp
let compareRotations (angle: float) (backend: IQuantumBackend) : QuantumResult<float> =
    quantumResult {
        let! single = validateInput [| angle |]
        let doubled = single |> Array.map (fun a -> 2.0 * a)

        let! circuitA = buildCircuit single
        let! circuitB = buildCircuit doubled

        let! stateA = execute backend circuitA
        let! stateB = execute backend circuitB

        let! pA = probabilityOfOne 1000 stateA
        let! pB = probabilityOfOne 1000 stateB
        return pB - pA
    }
```

## Advanced Features

### Exception Handling with try-with

`try ... with` inside the builder turns an exception into an `Error`:

```fsharp
let safeExecute (backend: IQuantumBackend) (circuit: ICircuit) : QuantumResult<QuantumState> =
    quantumResult {
        try
            let! state = backend.ExecuteToState circuit
            return state
        with
        | :? TimeoutException as ex ->
            return! Error(QuantumError.OperationError("Execution", $"Timeout: {ex.Message}"))
        | ex ->
            return! Error(QuantumError.OperationError("Execution", $"Failed: {ex.Message}"))
    }
```

> **Async alternative:** `backend.ExecuteToStateAsync circuit cancellationToken` returns `Task<QuantumResult<QuantumState>>`. Use it inside `quantumResultTask { }` (below) to execute without blocking and to cancel through the token.

### Asynchronous Steps: quantumResultTask

`quantumResultTask { }` is the asynchronous twin of `quantumResult { }`: the block is a `Task<QuantumResult<'T>>` that starts when it is evaluated, like `task { }`. Its `let!`, `do!` and `return!` accept:

- `Task<QuantumResult<'T>>` (the library's `...Async` functions and the business builders)
- `Async<QuantumResult<'T>>`
- a plain `QuantumResult<'T>` (the synchronous steps above)
- `Task<'T>` or `Async<'T>`, which cannot fail with a `QuantumError`, so their value is bound as `Ok`

The first `Error` short-circuits the rest of the block, as in `quantumResult`. An exception faults the task unless a `try ... with` inside the block catches it; since `return` wraps its argument in `Ok`, a handler that turns the exception into an `Error` writes `return! Error ...`.

```fsharp
open System.Threading
open System.Threading.Tasks

let processWorkflowAsync
    (angles: float array)
    (backend: IQuantumBackend)
    (cancellationToken: CancellationToken)
    : Task<QuantumResult<float>> =
    quantumResultTask {
        let! validAngles = validateInput angles // QuantumResult
        let! circuit = buildCircuit validAngles
        let! state = backend.ExecuteToStateAsync circuit cancellationToken // Task<QuantumResult<_>>
        return! probabilityOfOne 1000 state
    }

let safeExecuteAsync
    (backend: IQuantumBackend)
    (circuit: ICircuit)
    (cancellationToken: CancellationToken)
    : Task<QuantumResult<QuantumState>> =
    quantumResultTask {
        try
            return! backend.ExecuteToStateAsync circuit cancellationToken
        with ex ->
            return! Error(QuantumError.OperationError("Execution", $"Failed: {ex.Message}"))
    }
```

From ordinary task code, bind the result with `let!` and match on it:

```fsharp
task {
    let! result = processWorkflowAsync [| 0.5; 1.0 |] localBackend CancellationToken.None

    match result with
    | Ok probability -> printfn "P(1) = %.3f" probability
    | Error err -> printfn "Failed: %s" err.Message
}
```

`for`, `while`, `use` and `try ... finally` work in `quantumResultTask` as they do in `quantumResult`.

### Loops and Iteration

A `for` loop runs its body for each item and stops at the first `Error`:

```fsharp
let validateAll (inputs: float array list) : QuantumResult<unit> =
    quantumResult {
        for input in inputs do
            let! _ = validateInput input
            ()
    }
```

### Collecting Results

```fsharp
let probabilities (inputs: float array list) (backend: IQuantumBackend) : QuantumResult<float list> =
    quantumResult {
        let results = ResizeArray<float>()

        for input in inputs do
            let! validAngles = validateInput input
            let! circuit = buildCircuit validAngles
            let! state = execute backend circuit
            let! probability = probabilityOfOne 1000 state
            results.Add probability

        return List.ofSeq results
    }
```

## Migration Guide

### Step 1: Identify Nested Matches

Look for code where every step is followed by `| Error err -> Error err`, as in [Before: Nested Match Expressions](#before-nested-match-expressions).

### Step 2: Convert to a Computation Expression

Wrap the steps in `quantumResult { }`, replace each `match step with | Error err -> Error err | Ok x ->` by `let! x = step`, and finish with `return` (see [After: Computation Expression](#after-computation-expression)).

### Step 3: Handle Special Cases

**Early return on error:** an `if` without `else` can end the computation with `return! Error ...`; when the condition is false, the computation continues.

```fsharp
let checkedProbability (angles: float array) (backend: IQuantumBackend) : QuantumResult<float> =
    quantumResult {
        let! validAngles = validateInput angles

        if validAngles |> Array.exists Double.IsNaN then
            return! Error(QuantumError.ValidationError("angles", "must not contain NaN"))

        let! circuit = buildCircuit validAngles
        let! state = execute backend circuit
        return! probabilityOfOne 1000 state
    }
```

**Conditional logic:** both branches of an `if` must have the same `QuantumResult` type.

```fsharp
let probabilityOnBestBackend (angles: float array) (cloudBackend: IQuantumBackend option) : QuantumResult<float> =
    quantumResult {
        let! validAngles = validateInput angles
        let! circuit = buildCircuit validAngles

        let! state =
            match cloudBackend with
            | Some backend when validAngles.Length > 4 -> execute backend circuit
            | _ -> execute localBackend circuit

        return! probabilityOfOne 1000 state
    }
```

## Best Practices

### DO

**Use it for sequential operations that can fail:**

```fsharp
let pipeline (angles: float array) =
    quantumResult {
        let! validAngles = validateInput angles
        let! circuit = buildCircuit validAngles
        let! state = execute localBackend circuit
        return state
    }
```

**Mix in plain `let` bindings for steps that cannot fail:**

```fsharp
let scaledPipeline (angles: float array) =
    quantumResult {
        let! validAngles = validateInput angles
        let scaled = validAngles |> Array.map (fun a -> a / 2.0) // cannot fail
        let! circuit = buildCircuit scaled
        return circuit
    }
```

**Use `return!` to return a `QuantumResult` directly:**

```fsharp
let lastStep (state: QuantumState) =
    quantumResult {
        let shots = 2000
        return! probabilityOfOne shots state
    }
```

### DON'T

**Wrap a single call:**

```fsharp
// Unnecessary
let wrapped (angles: float array) =
    quantumResult {
        return! validateInput angles
    }

// Better: call it directly
let direct (angles: float array) = validateInput angles
```

**Nest computation expressions without need:**

```fsharp
// Harder to read
let nested (angles: float array) =
    quantumResult {
        let! circuit =
            quantumResult {
                let! validAngles = validateInput angles
                return! buildCircuit validAngles
            }

        return circuit
    }

// Flatter
let flat (angles: float array) =
    quantumResult {
        let! validAngles = validateInput angles
        let! circuit = buildCircuit validAngles
        return circuit
    }
```

## Comparison with Other Patterns

### vs Result.bind

**Result.bind chain:**

```fsharp
let viaBind (angles: float array) (backend: IQuantumBackend) : QuantumResult<float> =
    validateInput angles
    |> Result.bind buildCircuit
    |> Result.bind (execute backend)
    |> Result.bind (probabilityOfOne 1000)
```

**Computation expression:**

```fsharp
let viaBuilder (angles: float array) (backend: IQuantumBackend) : QuantumResult<float> =
    quantumResult {
        let! validAngles = validateInput angles
        let! circuit = buildCircuit validAngles
        let! state = execute backend circuit
        return! probabilityOfOne 1000 state
    }
```

**When to use each:**
- `Result.bind` suits a short linear chain where each step only needs the previous value
- `quantumResult` suits code that needs earlier values later on, branching, loops or `try ... with`

### vs Railway-Oriented Programming

The `quantumResult` builder is railway-oriented programming written with computation expression syntax:

```
 validate ──→ build ──→ execute ──→ measure ──→ Ok
    │           │          │           │
    ↓ Error     ↓ Error    ↓ Error     ↓ Error
```

The builder switches to the error track at the first `Error`.

## Summary

The `quantumResult` computation expression:

- Removes nested `match` expressions
- Propagates the first error automatically
- Keeps the steps in the order they run
- Keeps full type safety: every step returns `QuantumResult<'T>`
- Supports `for` and `while` loops, `try ... with`, `try ... finally` and `use`

Use it whenever two or more operations that return `QuantumResult<'T>` run in sequence, and `quantumResultTask` when any of them returns a `Task<QuantumResult<'T>>`.
