# Computation Expression Composition Patterns in FSharp.Azure.Quantum

## Overview

This page explains how the library's builders support composition: loops, `yield!` of existing values, and sequencing of custom operations. It shows how to use these patterns with the `circuit` builder, and which builder methods a builder needs to support them.

## The Challenge

A custom operation cannot use a variable bound by a `for` loop. Custom operations such as `CNOT` are applied to the builder's state; F# does not bring the loop variable into scope for them.

### Example of the Problem

<!-- fragment -->
```fsharp
// ❌ THIS DOES NOT COMPILE
let ghzState = circuit {
    qubits 5
    H 0
    for i in [0..3] do
        CNOT (i, i+1)  // error FS0039: The value or constructor 'i' is not defined
}
```

`CNOT` is a custom operation (`[<CustomOperation("CNOT")>]`). Custom operations work at the top level of the computation expression; inside a `for` loop the compiler rejects the use of `i`.

## The Solution: Composition with `yield!`

The loop body produces a value of the builder's state type and adds it with `yield!`. `CircuitBuilder` provides `singleGate` and `multiGate` for this:

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder

// ✅ THIS WORKS
let ghzState = circuit {
    qubits 5
    H 0
    for i in [0..3] do
        yield! singleGate (Gate.CNOT (i, i+1))
}
```

## Required Builder Methods for Composition

For a builder to support `for` loops and `yield!` next to custom operations, it implements the methods below. The snippets are the ones in `CircuitBuilder` (`src/FSharp.Azure.Quantum/Builders/CircuitBuilder.fs`); they are shown on their own, outside the builder type.

### 1. **Zero** - Empty state
```fsharp
member _.Zero() : Circuit =
    { QubitCount = 0; Gates = [] }
```

### 2. **Yield** - Initial state
```fsharp
member _.Yield(_) : Circuit =
    { QubitCount = 0; Gates = [] }
```

### 3. **YieldFrom** - Use an existing state (enables `yield!`)
```fsharp
member _.YieldFrom(circuit: Circuit) : Circuit =
    circuit
```

### 4. **Combine** - Merge two states
```fsharp
member _.Combine(circuit1: Circuit, circuit2: Circuit) : Circuit =
    // Gates are stored in reverse order, so the gates of circuit2 (which come later)
    // go in front of those of circuit1
    let qubitCount = max circuit1.QubitCount circuit2.QubitCount
    {
        QubitCount = qubitCount
        Gates = circuit2.Gates @ circuit1.Gates
    }
```

### 5. **Delay** - Run the body immediately
```fsharp
member inline _.Delay([<InlineIfLambda>] f: unit -> Circuit) : Circuit = f()
```

### 6. **For** - Loop support (two overloads)

#### Overload 1: A state followed by a delayed body
```fsharp
member inline this.For(circuit: Circuit, [<InlineIfLambda>] f: unit -> Circuit) : Circuit =
    this.Combine(circuit, f())
```

#### Overload 2: A loop over a sequence
```fsharp
member this.For(sequence: seq<'T>, body: 'T -> Circuit) : Circuit =
    let mutable state = this.Zero()
    for item in sequence do
        let itemCircuit = body item
        state <- this.Combine(state, itemCircuit)
    state
```

### 7. **Run** - Finalize and validate
```fsharp
member _.Run(circuit: Circuit) : Circuit =
    match validate circuit with
    | result when result.IsValid -> circuit
    | result -> failwithf "Invalid circuit: %s" (System.String.Join("; ", result.Messages))
```

## Helper Functions for Loop Bodies

Loop bodies need functions that build a one-operation value of the state type. `CircuitBuilder` defines these two:

```fsharp
/// Creates a circuit with a single gate (for use in for loops)
let singleGate (gate: Gate) : Circuit =
    { QubitCount = 0; Gates = [gate] }

/// Creates a circuit with multiple gates (for use in for loops)
let multiGate (gates: Gate list) : Circuit =
    { QubitCount = 0; Gates = gates }
```

`CircuitBuilder` also has lowercase functions for the gate union cases, such as:

```fsharp
/// Creates a CNOT gate - for use in for loops
let cnot control target = Gate.CNOT (control, target)

/// Creates an H (Hadamard) gate - for use in for loops
let h q = Gate.H q
```

## Usage Patterns

### Pattern 1: Simple Linear Composition
```fsharp
let bellState = circuit {
    qubits 2
    H 0          // Custom operation
    CNOT (0, 1)  // Custom operation
}
```

### Pattern 2: Composition with yield!
```fsharp
let part1 = circuit {
    qubits 3
    H 0
    H 1
}

let part2 = circuit {
    qubits 3
    CNOT (0, 1)
    CNOT (1, 2)
}

let twoPartCircuit = circuit {
    qubits 3
    yield! part1                  // Compose an existing circuit
    yield! singleGate (Gate.H 2)  // After a yield!, add gates with yield! too
    yield! part2                  // Compose another existing circuit
}
```

**Ordering rule:** custom operations come first. Once the expression contains a `yield!`, a `for` loop or an `if`, every later step must also be a `yield!` (or another loop or `if`). A custom operation after them is rejected with error FS3086 ("A custom operation may not be used in conjunction with ... 'if/then/else' ..."). See Anti-Pattern 4 below.

### Pattern 3: For Loops with Helper Functions
```fsharp
let multiQubitCircuit = circuit {
    qubits 5
    H 0

    // Use yield! with a helper function in loops
    for i in [0..3] do
        yield! singleGate (Gate.CNOT (i, i+1))
}
```

### Pattern 4: For Loops with Multiple Gates
```fsharp
let complexCircuit = circuit {
    qubits 10

    for i in [0..9] do
        yield! multiGate [
            Gate.H i
            Gate.RZ (i, float i * 0.1)
        ]
}
```

### Pattern 5: Conditional Composition
```fsharp
let conditionalCircuit qubitCount addPhaseFlips = circuit {
    qubits qubitCount
    H 0
    CNOT (0, 1)  // Custom operations before the if

    if addPhaseFlips then
        for i in [0..qubitCount-1] do
            yield! singleGate (Gate.Z i)
}
```

## Other Builders in the Library

Not every builder is a gate-list builder like `circuit`:

- **Graph coloring** follows the same pattern: `singleNode` wraps a node for `yield!` in a loop.

```fsharp
open FSharp.Azure.Quantum.GraphColoring

let fromData = graphColoring {
    colors ["Red"; "Green"; "Blue"]
    for (id, conflicts) in [ "A", ["B"]; "B", ["A"; "C"]; "C", ["B"] ] do
        yield! singleNode (coloredNode { nodeId id; conflictsWith conflicts })
}
```

- **Topological programs** use a monadic builder (`Bind`, `Return`, `Combine`, `For`, `While`, `TryWith`) with no custom operations, so a loop body can use `do!` directly:

```fsharp
open FSharp.Azure.Quantum.Topological

let isingBackend = TopologicalUnifiedBackendFactory.createIsing 10

let braidingProgram = topological isingBackend {
    do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
    for index in [0; 2; 0] do
        do! TopologicalBuilder.braid index
    let! outcome = TopologicalBuilder.measure 0
    return outcome
}
```

## Anti-Patterns to Avoid

### ❌ Anti-Pattern 1: Custom Operations in Loops

<!-- fragment -->
```fsharp
// DOES NOT COMPILE
let bad = circuit {
    qubits 5
    for i in [0..4] do
        H i  // error FS0039: 'i' is not defined
}
```

### ❌ Anti-Pattern 2: Missing Combine Method

If a builder has no `Combine` method, two operations in a row do not compile. `circuit` has `Combine`; this snippet shows a hypothetical builder without it:

<!-- fragment -->
```fsharp
// Hypothetical builder without: member _.Combine(state1, state2) = ...
let bad = myBuilderWithoutCombine {
    H 0
    H 1  // ERROR: sequencing needs Combine
}
```

### ❌ Anti-Pattern 3: Forgetting yield! in Loops

This compiles, but only with warning FS0020 ("The result of this expression has type 'Circuit' and is implicitly ignored"), and the gates are silently dropped:

```fsharp
let dropsGates = circuit {
    qubits 5
    for i in [0..4] do
        singleGate (Gate.H i)  // Missing yield!: no gate is added
}
```

### ❌ Anti-Pattern 4: Custom Operations After yield!, for or if

<!-- fragment -->
```fsharp
// DOES NOT COMPILE
let bad = circuit {
    qubits 3
    for i in [0..2] do
        yield! singleGate (Gate.H i)
    X 2  // error FS3086: a custom operation may not follow the loop
}

// Works: custom operations first, or yield! after the loop
let good = circuit {
    qubits 3
    X 2
    for i in [0..2] do
        yield! singleGate (Gate.H i)
}
```

### ❌ Anti-Pattern 5: Only One For Overload
```fsharp
// INCOMPLETE - handles loops over sequences only
member this.For(sequence: seq<'T>, body: 'T -> Circuit) : Circuit =
    // ... implementation ...

// ALSO NEEDED for custom operations before a loop:
// member inline this.For(circuit: Circuit, [<InlineIfLambda>] f: unit -> Circuit) : Circuit =
//     this.Combine(circuit, f())
```

## Testing Your Builder

To check that a builder composes, test these four shapes. With `circuit` they look like this:

### Test 1: Simple Sequencing
```fsharp
let test1 = circuit {
    qubits 2
    H 0
    X 1
}
```

### Test 2: yield! Composition
```fsharp
let existingState = circuit {
    qubits 2
    CNOT (0, 1)
}

let test2 = circuit {
    qubits 2
    H 0
    yield! existingState
    yield! singleGate (Gate.X 1)
}
```

### Test 3: For Loops
```fsharp
let test3 = circuit {
    qubits 6
    for i in [0..5] do
        yield! singleGate (Gate.H i)
}
```

### Test 4: Mixed Composition
```fsharp
let test4 = circuit {
    qubits 3
    H 0

    for i in [0..2] do
        yield! singleGate (Gate.RY (i, 0.5))

    yield! existingState
    yield! singleGate (Gate.X 2)
}
```

## Current Status of Builders in FSharp.Azure.Quantum

### Support `for` loops with `yield!`
- **circuit** (`CircuitBuilderCE`) - both `For` overloads, `YieldFrom`, and the `singleGate`/`multiGate` helpers
- **graphColoring** - both `For` overloads, `YieldFrom`, and the `singleNode` helper
- **TaskScheduling builders** (`scheduledTask`, `resource`, `scheduling`) - both `For` overloads and `YieldFrom`

### Monadic builders
- **topological** - `Bind`/`Return` style; loops use `do!` in the body
- **quantumResult** - see the [QuantumResult Builder Guide](quantumresult-builder-guide.md)

### No loop composition
- **Solver builders** `constraintSolver`, `patternMatcher`, `quantumTreeSearch` and `quantumChemistry` define a `For` over sequences but no `YieldFrom`, so a loop body cannot add to the problem. `quantumRiskEngine` and `drugDiscovery` define only a `For` that runs its body once.
- **Business builders** (`autoML`, `binaryClassification`, `anomalyDetection`, `predictiveModel`, `similaritySearch`, `coverageOptimizer`, `resourcePairing`, `packingOptimizer`, `constraintScheduler`, `socialNetwork`, `optionPricing`) and `periodFinder`, `phaseEstimator`, `quantumArithmetic`, `linearSystemSolver` have no `For`.

These builders mostly configure a single item, and their list-valued operations (`nodes`, `tasks`, `participants`, `people`, `indexItems`, ...) cover the cases where you would otherwise loop.

## Recommendations

### When to Add For Support
Add `For` methods to your builder if:
1. Users are likely to want to add multiple items in a loop
2. The builder represents a collection or sequence of operations

### When For Support is Optional
For support may be optional if:
1. Your builder typically configures a single item (e.g., ML model training)
2. Loop usage would be unusual in your domain
3. You prefer users to build collections outside the CE and pass them in

### Implementation Checklist
- [ ] Implement `Zero()`
- [ ] Implement `Yield(_)`
- [ ] Implement `YieldFrom(state)` for `yield!`
- [ ] Implement `Combine(state1, state2)` to merge states
- [ ] Implement `Delay(f)`
- [ ] Implement both `For` overloads (state + delayed body, and sequence)
- [ ] Implement `Run(state)` for validation or finalization
- [ ] Provide helper functions such as `singleGate` for loop bodies
- [ ] Add examples of the composition patterns
- [ ] Test all four composition shapes

`CircuitBuilderCE` in `src/FSharp.Azure.Quantum/Builders/CircuitBuilder.fs` implements every item.

## Further Reading

- [Computation Expressions (F# language reference)](https://learn.microsoft.com/dotnet/fsharp/language-reference/computation-expressions)
- [Understanding Computation Expressions](https://fsharpforfunandprofit.com/series/computation-expressions/)
- [Computation Expressions Reference](computation-expressions-reference.md) - all builders in this library and their operations

## Summary

Composition in computation expressions requires:

1. **The builder methods**: Zero, Yield, YieldFrom, Combine, Delay, For (two overloads), Run
2. **Helper functions** that return the state type, for use in `for` loop bodies
3. **Documentation** that shows the `yield!` pattern for loops, and that custom operations must come before any `yield!`, `for` or `if`
4. **Tests** of sequencing, `yield!`, loops, and their mix

`CircuitBuilder` is the reference implementation of these patterns in FSharp.Azure.Quantum.
