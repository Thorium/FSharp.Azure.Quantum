# Integer Variables Example

## Overview

This example demonstrates **native integer variable support** in QAOA quantum optimization, allowing you to work directly with integer decision variables instead of manual binary encoding.

## Use Cases

Integer variables are essential for real-world optimization:
- **Production Planning**: How many units to produce?
- **Resource Allocation**: Assign N resources to M tasks
- **Scheduling**: Time slot selection, priority assignment
- **Configuration**: Parameter tuning with discrete values

## Encoding Strategies

### 1. **BoundedInteger** - Logarithmic Scaling

**Best for:** Large integer ranges, quantities, counts

```fsharp
let encoding = VariableEncoding.BoundedInteger(0, 100)
// Range [0, 100] requires only 7 qubits (log₂ 101 ≈ 6.66)
```

**Efficiency:** O(log₂ range) qubits

**Example:** Production quantities from 0 to 1000 units

### 2. **DomainWall** - Ordered Levels

**Best for:** Naturally ordered categories (priorities, quality levels)

```fsharp
let encoding = VariableEncoding.DomainWall 5
// Priority levels 1-5 require only 4 qubits (saves 25%)
```

**Pattern:** Wall of 1s followed by 0s
- Priority 1: `0000`
- Priority 2: `1000`
- Priority 3: `1100`
- Priority 4: `1110`
- Priority 5: `1111`

**Efficiency:** 25% fewer qubits than OneHot

### 3. **OneHot** - Unordered Categories

**Best for:** Mutually exclusive choices without natural ordering

```fsharp
let encoding = VariableEncoding.OneHot 4
// 4 route choices require 4 qubits (one per option)
```

**Pattern:** Exactly one bit set to 1
- Route A: `1 0 0 0`
- Route B: `0 1 0 0`
- Route C: `0 0 1 0`
- Route D: `0 0 0 1`

**Constraint:** Enforced via QUBO penalty matrix

### 4. **Binary** - Boolean Decisions

**Best for:** Yes/no, on/off, include/exclude decisions

```fsharp
let encoding = VariableEncoding.Binary
// Single qubit: 0 or 1
```

**Efficiency:** Most efficient (1 qubit)

## Quick Start

```fsharp
#r "FSharp.Azure.Quantum.dll"
open FSharp.Azure.Quantum

// Define integer variables
let variables = [
    { Name = "quantity_A"; VarType = IntegerVar(0, 50) }
    { Name = "quantity_B"; VarType = IntegerVar(0, 50) }
    { Name = "priority"; VarType = IntegerVar(1, 5) }
]

// Choose encoding strategy
let encoding = VariableEncoding.BoundedInteger(0, 50)

// Get required qubits
let qubits = VariableEncoding.qubitCount encoding
printfn "Qubits needed: %d" qubits  // Only 6 qubits for range [0, 50]

// Encode a value
let bits = VariableEncoding.encode encoding 25
printfn "25 encoded as: %A" bits  // [1; 0; 0; 1; 1; 0] (binary: 011001)

// Decode back to integer
let value = VariableEncoding.decode encoding bits
printfn "Decoded: %d" value  // 25
```

## Running the Example

```bash
cd examples/IntegerVariables
dotnet fsi IntegerVariablesExample.fsx
```

## Examples Covered

1. **Encoding Strategy Comparison** - Compare qubit efficiency
2. **Production Planning** - Resource-constrained manufacturing
3. **Task Scheduling** - Priority-based with DomainWall encoding
4. **Route Selection** - Mutually exclusive choices with OneHot
5. **Mixed Integer Programming** - Multiple variable types in one problem

## Output

The example demonstrates:
- ✅ Qubit efficiency comparison across encodings
- ✅ Automatic encoding/decoding roundtrips
- ✅ Constraint penalty matrices
- ✅ Integration with QAOA quantum circuits
- ✅ Mixed integer variable problems

## Performance

| Range | BoundedInteger | OneHot | Savings |
|-------|---------------|---------|---------|
| [0, 7] | 3 qubits | 8 qubits | **62%** |
| [0, 15] | 4 qubits | 16 qubits | **75%** |
| [0, 31] | 5 qubits | 32 qubits | **84%** |
| [0, 100] | 7 qubits | 101 qubits | **93%** |

**Logarithmic scaling makes BoundedInteger ideal for large ranges!**

## Integration with QAOA

Integer variables work seamlessly with QAOA quantum optimization:

```fsharp
// Define problem with integer variables
let problem = {
    Variables = [
        { Name = "x"; VarType = IntegerVar(0, 10) }
        { Name = "y"; VarType = IntegerVar(0, 10) }
    ]
    Objective = fun x y -> -1.0 * (x * x + y * y)  // Maximize x² + y²
    Constraints = [ x + y <= 15 ]
}

// Solve on quantum backend
let solution = QAOA.solve problem backend

// Solutions are automatically decoded to integers
printfn "x = %d, y = %d" solution.x solution.y
```

## Testing

**75+ tests** cover all encoding strategies:
- Roundtrip encoding/decoding
- Qubit count calculations
- Constraint penalty generation
- Edge cases (min/max values)
- Mixed variable types

```bash
dotnet test --filter "FullyQualifiedName~QuboEncoding"
# Result: Passed! - 75/75 tests ✅
```

## Key Features

✅ **Multiple Encodings**: Binary, OneHot, DomainWall, BoundedInteger  
✅ **Automatic Qubit Allocation**: Optimal qubit usage  
✅ **Constraint Enforcement**: QUBO penalty matrices  
✅ **Transparent Encoding**: Automatic encode/decode  
✅ **QAOA Integration**: Works with all quantum backends  
✅ **Production Ready**: 75+ passing tests  

## When to Use Each Encoding

| Problem Type | Best Encoding | Why |
|-------------|---------------|-----|
| Binary decisions | `Binary` | Most efficient (1 qubit) |
| Large integer ranges | `BoundedInteger` | Logarithmic scaling |
| Ordered levels/priorities | `DomainWall` | 25% qubit savings |
| Unordered categories | `OneHot` | Natural constraint enforcement |

## Related Examples

- **Knapsack**: Integer quantities with constraints
- **JobScheduling**: Task scheduling with integer time slots
- **SupplyChain**: Resource allocation with bounded integers
- **InvestmentPortfolio**: Portfolio weights as integers

## References

- [QUBO Encoding Documentation](../../docs/QuboEncoding.md)
- [Variable Encoding Strategies](../../src/FSharp.Azure.Quantum/Builders/QuboEncoding.fs)
- [Test Suite](../../tests/FSharp.Azure.Quantum.Tests/QuboEncodingTests.fs)
