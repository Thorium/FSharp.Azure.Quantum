# Computation Expression Composition Patterns in FSharp.Azure.Quantum

## Overview

This document explains the proper patterns for building composable computation expressions (CEs) in FSharp.Azure.Quantum, with particular focus on handling loops and ensuring proper composition of builder operations.

## The Challenge

Computation expressions in F# have a fundamental limitation: **custom operations do not work inside `for` loops**. This is by design in the F# compiler and affects all computation expression builders.

### Example of the Problem

```fsharp
// ❌ THIS DOES NOT WORK
let ghzState = circuit {
    qubits 5
    H 0
    for i in [0..3] do
        CNOT (i, i+1)  // ERROR: CNOT is a custom operation, not available here
}
```

The issue is that `CNOT` is a custom operation defined with `[<CustomOperation("CNOT")]>`, which only works at the top level of the computation expression, not inside control flow like `for` loops.

## The Solution: Proper Composition with `yield!`

The correct pattern uses `yield!` with helper functions that return the builder's state type:

```fsharp
// ✅ THIS WORKS
let ghzState = circuit {
    qubits 5
    H 0
    for i in [0..3] do
        yield! singleGate (Gate.CNOT (i, i+1))
}
```

## Required Builder Methods for Proper Composition

For a computation expression builder to support composition with `for` loops, it must implement these core methods:

### 1. **Zero** - Empty state
```fsharp
member _.Zero() : Circuit =
    { QubitCount = 0; Gates = [] }
```

### 2. **Yield** - Initialize from unit
```fsharp
member _.Yield(_) : Circuit =
    { QubitCount = 0; Gates = [] }
```

### 3. **YieldFrom** - Compose existing state (enables `yield!`)
```fsharp
member _.YieldFrom(circuit: Circuit) : Circuit =
    circuit
```

### 4. **Combine** - Merge two states
```fsharp
member _.Combine(circuit1: Circuit, circuit2: Circuit) : Circuit =
    let qubitCount = max circuit1.QubitCount circuit2.QubitCount
    {
        QubitCount = qubitCount
        Gates = circuit1.Gates @ circuit2.Gates
    }
```

### 5. **Delay** - Deferred execution
```fsharp
member inline _.Delay([<InlineIfLambda>] f: unit -> Circuit) : Circuit = f()
```

### 6. **For** - Loop support (TWO OVERLOADS REQUIRED)

#### Overload 1: For delayed execution patterns
```fsharp
member inline this.For(circuit: Circuit, [<InlineIfLambda>] f: unit -> Circuit) : Circuit =
    this.Combine(circuit, f())
```

#### Overload 2: For actual sequences
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
    // Validate or transform the final result
    validate circuit
    circuit
```

## Helper Functions for Loop Bodies

To make `for` loops ergonomic, provide helper functions that construct single-operation instances of your state type:

```fsharp
/// Creates a circuit with a single gate (for use in for loops)
let singleGate (gate: Gate) : Circuit =
    { QubitCount = 0; Gates = [gate] }

/// Creates a circuit with multiple gates (for use in for loops)
let multiGate (gates: Gate list) : Circuit =
    { QubitCount = 0; Gates = gates }
```

Additionally, provide lowercase function versions of union case constructors:

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
let twoPartCircuit = circuit {
    qubits 3
    yield! part1  // Compose an existing circuit
    H 2
    yield! part2  // Compose another existing circuit
}
```

### Pattern 3: For Loops with Helper Functions
```fsharp
let multiQubitCircuit = circuit {
    qubits 5
    H 0
    
    // Use yield! with helper function in loops
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
let conditionalCircuit qubitCount useBarrier = circuit {
    qubits qubitCount
    H 0
    
    if useBarrier then
        for i in [0..qubitCount-1] do
            yield! singleGate (Gate.Z i)
    
    CNOT (0, 1)
}
```

## Comparison with Other F# Builders

### FsCDK Pattern (AWS CDK)
FsCDK's `StackBuilder` follows the same pattern:

```fsharp
stack "MyStack" {
    lambda myFunction
    bucket myBucket
    
    for i in [1..5] do
        yield! createQueue $"queue-{i}"
}
```

### Farmer Pattern (Azure ARM Templates)
Farmer's `arm` builder also uses this pattern:

```fsharp
arm {
    location Location.WestUS
    
    for i in [1..3] do
        yield! storageAccount { name $"storage{i}" }
}
```

## Anti-Patterns to Avoid

### ❌ Anti-Pattern 1: Custom Operations in Loops
```fsharp
// DOES NOT WORK
let bad = circuit {
    qubits 5
    for i in [0..4] do
        H i  // ERROR: Custom operations don't work in loops
}
```

### ❌ Anti-Pattern 2: Missing Combine Method
If your builder doesn't implement `Combine`, you'll get cryptic errors:

```fsharp
// Missing: member _.Combine(state1, state2) = ...
let bad = circuit {
    H 0
    H 1  // ERROR: Needs Combine to sequence operations
}
```

### ❌ Anti-Pattern 3: Forgetting yield! in Loops
```fsharp
// DOES NOT WORK
let bad = circuit {
    qubits 5
    for i in [0..4] do
        singleGate (Gate.H i)  // Missing yield!
}
```

### ❌ Anti-Pattern 4: Wrong For Signature
```fsharp
// INCOMPLETE - Only handles sequences, not Delay/Run interactions
member this.For(sequence: seq<'T>, body: 'T -> Circuit) : Circuit =
    // ... implementation ...
    
// MISSING THIS OVERLOAD:
// member inline this.For(circuit: Circuit, [<InlineIfLambda>] f: unit -> Circuit) : Circuit =
//     this.Combine(circuit, f())
```

## Testing Your Builder

To verify your builder supports proper composition, test these scenarios:

### Test 1: Simple Sequencing
```fsharp
let test1 = builder {
    operation1
    operation2
}
```

### Test 2: yield! Composition
```fsharp
let test2 = builder {
    operation1
    yield! existingState
    operation2
}
```

### Test 3: For Loops
```fsharp
let test3 = builder {
    for i in [0..5] do
        yield! singleOp i
}
```

### Test 4: Mixed Composition
```fsharp
let test4 = builder {
    operation1
    
    for i in [0..2] do
        yield! singleOp i
    
    yield! existingState
    operation2
}
```

## Current Status of Builders in FSharp.Azure.Quantum

Based on review of the codebase:

### ✅ Properly Implemented
- **CircuitBuilder** - Full composition support with For loops

### ⚠️ Missing For Support (but may not need it)
- **TaskScheduling Builders** (ScheduledTaskBuilder, ResourceBuilder, SchedulingBuilder)
- **Business Builders** (AutoML, BinaryClassification, AnomalyDetection, etc.)
- **Solver Builders** (LinearSystemSolver, etc.)

Most of these builders work fine for their intended use cases but would fail if users tried to use custom operations inside `for` loops.

## Recommendations

### When to Add For Support
Add `For` methods to your builder if:
1. Users are likely to want to add multiple items in a loop
2. The builder represents a collection or sequence of operations
3. You want your builder to be as composable as FsCDK or Farmer

### When For Support is Optional
For support may be optional if:
1. Your builder typically configures a single item (e.g., ML model training)
2. Loop usage would be unusual in your domain
3. You prefer users to build collections outside the CE and pass them in

### Implementation Checklist
- [x] Implement `Zero()` method
- [x] Implement `Yield(_)` method
- [x] Implement `YieldFrom(state)` method for `yield!` support (via `ReturnFrom`)
- [x] Implement `Combine(state1, state2)` to merge states
- [x] Implement `Delay(f)` for deferred execution
- [x] Implement both `For` overloads (delayed + sequence)
- [x] Implement `Run(state)` for validation/finalization
- [x] Provide helper functions like `singleGate` for ergonomic loop bodies (see `braid`, `measure`, etc.)
- [x] Add examples demonstrating composition patterns
- [x] Test all composition scenarios

> **Note**: All items implemented in `TopologicalBuilder.fs` with tests in `TopologicalBuilderTests.fs`

## Further Reading

- [F# Computation Expressions Spec](https://fsharp.org/specs/language-spec/4.1/FSharpSpec-4.1-latest.pdf) (Section 6.3.10)
- [Understanding Computation Expressions](https://fsharpforfunandprofit.com/series/computation-expressions/)
- [FsCDK Implementation](https://github.com/chadunit/FsCDK) - Excellent example of composable builders
- [Farmer Implementation](https://compositionalit.github.io/farmer/) - Another excellent example

## Summary

Proper composition in computation expressions requires:

1. **Full method implementation**: Zero, Yield, YieldFrom, Combine, Delay, For (2 overloads), Run
2. **Helper functions**: Functions that return your state type for use in `for` loop bodies
3. **User education**: Documentation showing `yield!` pattern for loops
4. **Testing**: Verify all composition patterns work correctly

The CircuitBuilder in FSharp.Azure.Quantum now serves as a reference implementation of these patterns.
