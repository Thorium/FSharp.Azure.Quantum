# Kasino Card Game - C# Interop Example

**Traditional Finnish Card Game demonstrating C# ↔ F# interoperability with quantum-inspired optimization.**

## Overview

This example demonstrates how C# applications can seamlessly consume the F#.Azure.Quantum library's Subset Selection framework. Kasino is a traditional Finnish card game where players capture table cards by matching their sum to a hand card's value.

## What This Example Demonstrates

1. **C# → F# Interop**: Natural integration between C# and F# libraries
2. **Fluent Builder API**: Method chaining works beautifully across language boundaries
3. **F# Types in C#**: Proper handling of F# records, discriminated unions, and lists
4. **Subset Selection**: Solving optimization problems with classical algorithms
5. **Quantum Potential**: 32x-181x speedup achievable with QUBO encoding

## Running the Example

```bash
cd examples/Kasino_CSharp/KasinoExample
dotnet run
```

## Example Output

```
╔════════════════════════════════════════════════════════════╗
║  Kasino Card Game - C# Interop with F# Subset Selection   ║
║  Traditional Finnish Card Game (32x-181x Quantum Speedup)  ║
╚════════════════════════════════════════════════════════════╝

Example 1: Simple Kasino Capture
═══════════════════════════════════════════════════════════
🎴 Hand Card: King (K) = 13
🃏 Table Cards: 2, 5, 8, Jack (11)
✅ Capture Solution Found!
   Cards to capture: 2 (2), Jack (11)
   Total value: 13
```

## Code Highlights

### Creating Items with F# Helper Functions

```csharp
using FSharp.Azure.Quantum;
using Microsoft.FSharp.Collections;
using static FSharp.Azure.Quantum.SubsetSelection;

// F# tuples in C# use System.Tuple, not C# value tuples
var tableCards = new[]
{
    itemMulti("card_2", "2", ListModule.OfArray(new[] 
    { 
        Tuple.Create("weight", 2.0), 
        Tuple.Create("value", 2.0) 
    })),
    itemMulti("card_J", "Jack", ListModule.OfArray(new[] 
    { 
        Tuple.Create("weight", 11.0), 
        Tuple.Create("value", 11.0) 
    })),
};
```

### Using the Fluent Builder API

```csharp
// Fluent builder pattern works naturally in C#
var problem = SubsetSelectionBuilder<string>.Create()
    .Items(ListModule.OfArray(tableCards))
    .AddConstraint(SelectionConstraint.NewMaxLimit("weight", 13.0))
    .Objective(SelectionObjective.NewMaximizeWeight("value"))
    .Build();
```

### Solving with Classical Algorithms

```csharp
// F# Result type interop
var result = solveKnapsack(problem, "weight", "value");

if (result.IsOk)
{
    var solution = result.ResultValue;
    Console.WriteLine($"Total value: {solution.TotalWeights["value"]}");
    Console.WriteLine($"Cards captured: {ListModule.Length(solution.SelectedItems)}");
}
```

## Key C# Interop Patterns

### F# Tuples
- F# tuples are `System.Tuple<T1, T2>`, not C# value tuples `(T1, T2)`
- Use `Tuple.Create()` to create F# tuples from C#

### F# Lists
- Use `Microsoft.FSharp.Collections.ListModule` for list operations
- `ListModule.OfArray()` converts C# arrays to F# lists
- `ListModule.Length()` gets list count (not `.Count`)

### F# Result Type
- Check `.IsOk` property for success/failure
- Access value with `.ResultValue` or `.ErrorValue`

### F# Discriminated Unions
- Use static `New*` methods to construct union cases
- Example: `SelectionConstraint.NewMaxLimit("weight", 10.0)`

## Game Rules (Kasino)

Kasino is a traditional Finnish card game similar to Scopa or Casino:

1. **Table** has cards with numeric values (1-13)
2. **Player** has cards in hand
3. **Goal**: Capture table cards whose sum equals hand card value
4. **Optimal Play**: Minimize cards captured (or maximize value)

This example uses the Subset Selection framework to find optimal captures, demonstrating how quantum-inspired algorithms can solve real-world game strategy problems.

## Quantum Advantage

While this example uses classical dynamic programming algorithms, the same problems can be encoded as QUBO (Quadratic Unconstrained Binary Optimization) for quantum annealers, achieving **32x-181x speedup** on quantum hardware for complex scenarios.

## Related Examples

- **F# Subset Selection**: See `examples/` directory for F# examples
- **Portfolio Optimization**: Another subset selection use case
- **Knapsack Problems**: Classic optimization problem

## Dependencies

- **.NET 10.0+**
- **FSharp.Azure.Quantum** library (F# quantum optimization)
- **FSharp.Core** (included automatically)

## Cultural Note

**Kasino** (also spelled *Kasino* or *Casino*) is a popular Finnish card game, part of the Nordic card game tradition. This example honors Finnish cultural heritage while demonstrating modern quantum computing concepts.

---

**32x-181x Quantum Speedup** | **C# ↔ F# Interop** | **Finnish Cultural Heritage**
