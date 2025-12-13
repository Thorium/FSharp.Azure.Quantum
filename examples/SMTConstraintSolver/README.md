# SMT-Style Declarative Constraint Solver

This example demonstrates the new **SMT-style declarative constraint API** inspired by Z3Fs, which provides a high-level, declarative way to express constraint satisfaction problems.

## Features

- **Z3Fs-inspired syntax** for declarative constraints
- **Theory support**: Integers, BitVectors, Arrays, Real Numbers
- **Quantum search** via Grover's algorithm for exponential speedup
- **F# computation expressions** for idiomatic DSL
- **Composability** with `for` loops and `Combine`

## Performance

- **Classical CSP**: O(N) exhaustive search
- **Quantum CSP**: O(√N) using Grover's algorithm
- **Speedup**: Quadratic for large search spaces

## Basic Usage

```fsharp
open FSharp.Azure.Quantum.QuantumConstraintSolver.SMT

// Declare variables
let x = intVar "x"
let y = intVar "y"

// Build SMT problem using computation expression
let problem = smt<int> {
    variables [x; y]
    smtConstraint (equals (Add (var x, var y)) (constant 10))
    smtConstraint (lessThan (var x) (constant 5))
    domain [1..9]
    backend myBackend
    shots 1000
}

// Solve
let solution = solveSmt problem backend
match solution with
| Ok assignments ->
    printfn "x = %A, y = %A" assignments.["x"] assignments.["y"]
| Error err ->
    printfn "Error: %A" err
```

## Example 1: Integer Arithmetic

Solve: **x + y = 10** where **x < 5** and **x, y ∈ {1..9}**

```fsharp
let x = intVar "x"
let y = intVar "y"

let problem = smt<int> {
    variables [x; y]
    smtConstraint (equals (Add (var x, var y)) (constant 10))
    smtConstraint (lessThan (var x) (constant 5))
    domain [1..9]
}

// Solution: x = 1, y = 9 (or x = 2, y = 8, etc.)
```

## Example 2: Range Constraints

Solve: **x × y = 24** where **2 ≤ x ≤ 6** and **y > 3**

```fsharp
let a = intVar "a"
let b = intVar "b"

let problem = smt<int> {
    variables [a; b]
    smtConstraint (equals (Multiply (var a, var b)) (constant 24))
    smtConstraint (inRange (var a) 2 6)
    smtConstraint (greaterThan (var b) (constant 3))
    domain [1..12]
}

// Solution: a = 4, b = 6 (or a = 3, b = 8, etc.)
```

## Example 3: Logical Combinations (AND/OR)

Solve: **(x = 5 OR x = 7) AND x + y < 15**

```fsharp
let p = intVar "p"
let q = intVar "q"

let problem = smt<int> {
    variables [p; q]
    smtConstraint (andAll [
        orAny [
            equals (var p) (constant 5)
            equals (var p) (constant 7)
        ]
        lessThan (Add (var p, var q)) (constant 15)
    ])
    domain [1..10]
}

// Solution: p = 5, q = 9 (or p = 7, q = 7, etc.)
```

## Example 4: Multiple Constraints (Dynamic Generation)

Generate multiple constraints dynamically outside the CE:

```fsharp
let r = intVar "r"

// Generate constraints outside the CE using list comprehension
let excludeConstraints = 
    [3; 5; 7]
    |> List.map (fun v -> notEquals (var r) (constant v))
    |> andAll

let problem = smt<int> {
    variables [r]
    smtConstraint excludeConstraints
    domain [1..10]
}

// Solution: r ∈ {1,2,4,6,8,9,10} (any value except 3, 5, 7)
```

**Note**: for-loops don't work inside CustomOperation-based CEs. Generate constraints outside instead.

## Example 5: Sudoku Row Constraint

Ensure all cells in a row are different:

```fsharp
let c1 = intVar "c1"
let c2 = intVar "c2"
let c3 = intVar "c3"
let c4 = intVar "c4"
let row = [c1; c2; c3; c4]

// All cells must be different
let allDifferent =
    [
        for i in 0 .. row.Length - 1 do
            for j in i + 1 .. row.Length - 1 do
                notEquals (var row.[i]) (var row.[j])
    ]
    |> andAll

// All cells must be in domain
let inDomain =
    [
        for cell in row do
            inRange (var cell) 1 4
    ]
    |> andAll

let problem = smt<int> {
    variables row
    smtConstraint (andAll [allDifferent; inDomain])
    domain [1 .. 4]
}

// Solution: [1, 2, 3, 4] (or any permutation)
```

## Available Constraint Types

### Comparison Constraints
- `equals (x, y)` - x = y
- `notEquals (x, y)` - x ≠ y
- `lessThan (x, y)` - x < y
- `lessOrEqual (x, y)` - x ≤ y
- `greaterThan (x, y)` - x > y
- `greaterOrEqual (x, y)` - x ≥ y
- `inRange (x, min, max)` - min ≤ x ≤ max

### Logical Constraints
- `andAll [c1; c2; ...]` - c1 ∧ c2 ∧ ...
- `orAny [c1; c2; ...]` - c1 ∨ c2 ∨ ...
- `notConstraint c` - ¬c

### Expression Builders
- `Add (x, y)` - x + y
- `Subtract (x, y)` - x - y
- `Multiply (x, y)` - x × y
- `Divide (x, y)` - x / y
- `Modulo (x, y)` - x mod y

## Variable Types

- `intVar "name"` - Integer variable
- `bvVar "name" width` - Bit-vector variable (fixed-width)
- `realVar "name"` - Real number variable (floating point)

## Composability Features

The SMT builder supports full F# computation expression composability:

### For-comprehension
```fsharp
smt<int> {
    for constraint in generateConstraints() do
        smtConstraint constraint
}
```

### Combine multiple problem specs
```fsharp
let base = smt<int> {
    variables [x; y]
    domain [1..10]
}

let withConstraints = smt<int> {
    yield! base
    smtConstraint (equals (var x) (var y))
}
```

### Zero (empty problem)
```fsharp
let empty = smt<int> { () }  // Creates empty problem
```

## C# Usage

```csharp
using FSharp.Azure.Quantum.QuantumConstraintSolver.SMT;
using static FSharp.Azure.Quantum.BuildersCSharpExtensions;

// Create variables
var x = SMT.intVar("x");
var y = SMT.intVar("y");

// Build problem
var problem = new SmtProblem<int>
{
    Variables = new[] { x, y },
    Constraints = new[] 
    { 
        SMT.equals(SMT.Add(SMT.var(x), SMT.var(y)), SMT.constant(10)),
        SMT.lessThan(SMT.var(x), SMT.constant(5))
    },
    Domain = Enumerable.Range(1, 9).ToList(),
    Backend = backend,
    Shots = 1000
};

// Or use fluent API
var problem2 = new SmtProblem<int>()
    .WithVariablesFromArray(new[] { x, y })
    .WithConstraint(SMT.equals(/* ... */))
    .WithDomainFromEnumerable(Enumerable.Range(1, 9));

// Solve
var solution = await SolveSmtTask(problem, backend);
if (solution.IsOk())
{
    var assignments = solution.GetOkValue();
    Console.WriteLine($"x = {assignments["x"]}, y = {assignments["y"]}");
}
```

## Comparison with Existing ConstraintSolver

### Before (Predicate-based):
```fsharp
let problem = constraintSolver {
    searchSpace 2
    domain [1..9]
    satisfies (fun assignment -> 
        let x = assignment.[0]
        let y = assignment.[1]
        x + y = 10 && x < 5
    )
}
```

### Now (Declarative SMT):
```fsharp
let problem = smt<int> {
    variables [x; y]
    smtConstraint (equals (Add (var x, var y)) (constant 10))
    smtConstraint (lessThan (var x) (constant 5))
    domain [1..9]
}
```

**Benefits**:
- ✅ Named variables instead of array indices
- ✅ Composable constraints
- ✅ Type-safe expressions
- ✅ Z3-like declarative syntax
- ✅ Easier to debug and maintain

## Algorithm Details

The SMT solver compiles declarative constraints to Grover oracle predicates:

1. **Compilation**: SMT constraints → boolean predicate
2. **Quantum Search**: Grover's algorithm finds satisfying assignment
3. **Post-processing**: Map solution indices back to variable names

**Time Complexity**: O(√N) for N possible assignments (quadratic speedup vs classical O(N))

## Related Examples

- `../ConstraintSolver/SudokuSolver.fsx` - Predicate-based constraint solving
- `../GraphColoring/GraphColoring.fsx` - Graph coloring with QUBO
- `../JobScheduling/JobScheduling.fsx` - Resource allocation constraints
