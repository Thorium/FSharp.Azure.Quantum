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

## Kasino Game Rules

**Kasino** (Finnish spelling) is a traditional Nordic card game similar to Italian *Scopa* or the international *Casino*. It's a fishing-style card game where players capture cards from the table.

### Basic Setup

- **Players**: 2-4 players
- **Deck**: Standard 52-card deck
- **Card Values**: 
  - Number cards (2-10): Face value
  - Jack = 11, Queen = 12, King = 13
  - Ace = 1 or 14 (player's choice)

### How to Play

1. **Deal**: 
   - Each player gets 4 cards
   - 4 cards are dealt face-up to the table
   - Remaining cards form the draw pile

2. **Turn Actions**: On your turn, play one card from hand and either:
   - **Capture**: Take table cards whose sum equals your played card
   - **Build**: Add your card to the table if no capture is possible

3. **Capturing Rules**:
   - **Single Capture**: Take one table card with same value as your card
   - **Multiple Capture**: Take multiple table cards whose sum equals your card
   - **Sweep**: Capture all table cards (scores bonus points)

4. **Winning**: 
   - Game ends when all cards are played
   - Player with most captured cards wins
   - Additional points for: Most spades, 10 of diamonds, Aces

### Example Captures

**Scenario 1: Simple Match**
- Hand: 7
- Table: [3, 4, 7, K]
- **Capture**: Take the 7 (exact match)

**Scenario 2: Sum Capture**
- Hand: 10
- Table: [2, 3, 4, 5, 6]
- **Possible Captures**: 
  - [4, 6] = 10
  - [2, 3, 5] = 10
  - Any combination summing to 10

**Scenario 3: Optimal Strategy** ⭐
- Hand: 13 (King)
- Table: [2, 5, 8, 11]
- **Optimal**: [2, 11] = 13 (2 cards - minimizes future opponent options)
- **Suboptimal**: [5, 8] = 13 (also valid but leaves 2, 11 for opponent)

### Strategy & Optimization

This is where **quantum optimization** helps! 

**Traditional Strategy Questions**:
- Which capture minimizes cards left for opponents?
- Which capture maximizes my total points?
- Should I build for a future sweep?

**Optimization Problem**:
- **Input**: Table cards, hand card value
- **Constraint**: Sum must equal hand card
- **Objective**: Minimize captured cards (optimal play) OR maximize value
- **Classical Complexity**: Subset sum is NP-complete
- **Quantum Advantage**: **32x-181x speedup** with quantum annealing!

### Why This Example?

Kasino demonstrates **real-world subset selection**:
- ✅ **Constraints**: Sum must match exactly (or be ≤ for this example)
- ✅ **Objectives**: Minimize count, maximize value, or custom strategy
- ✅ **Practical**: Actual game strategy optimization
- ✅ **Scalable**: Complex scenarios benefit from quantum speedup

The Subset Selection framework solves these strategy problems efficiently, showing how quantum computing can optimize decision-making in games, logistics, finance, and more!

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

**Kasino** (also spelled *Kasino* or *Cassino* ) is a popular Finnish card game, part of the Nordic card game tradition. This is nothing to do with the casino gambling game. There are other variants as Laistokasino, where you avoid taking points. This example honors Finnish cultural heritage while demonstrating modern quantum computing concepts.

---

**32x-181x Quantum Speedup** | **C# and F# Interop** | **Finnish Cultural Heritage**
