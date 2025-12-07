# Quantum Tree Search Examples

Find optimal moves in game trees and decision trees using Grover's algorithm for O(√N) speedup.

## Examples

### 1. Tic-Tac-Toe AI (`GameAI.fsx`)

Evaluate Tic-Tac-Toe positions 3 moves ahead to find best move.

**Run:**
```bash
dotnet fsi GameAI.fsx
```

**Use Cases:**
- Turn-based game AI (Tic-Tac-Toe)
- Simple game tree search
- Educational quantum AI demonstration

**Quantum Advantage:**
- Classical: 9³ = 729 evaluations
- Quantum: √729 = 27 evaluations
- **27× speedup!**

### 2. Chess-Style Position Analysis

Analyze chess positions 4 moves ahead with expensive neural network evaluation.

**Use Cases:**
- Chess engines with ML position evaluation
- Go AI with Monte Carlo rollouts
- Strategy games with complex heuristics

**Quantum Advantage:**
- Classical: 16⁴ = 65,536 evaluations × 100ms = 1.8 hours
- Quantum: √65,536 = 256 evaluations × 100ms = 25 seconds
- **256× speedup makes real-time play viable!**

### 3. Business Decision Trees

Optimize 3-stage business decisions (marketing → pricing → launch strategy).

**Use Cases:**
- Product launch strategy planning
- Multi-stage business optimization
- Sequential decision making with expensive market simulation

**Quantum Advantage:**
- Classical: 36 simulations × 7.5 min = 4.5 hours
- Quantum: √36 = 6 simulations × 7.5 min = 45 minutes
- **Saves 3 hours 45 minutes!**

## When to Use

✅ **Good for:**
- Game AI (chess, go, gomoku, strategy games)
- Decision trees with expensive evaluation
- Monte Carlo Tree Search acceleration
- Path planning with complex heuristics
- **Branching factor: 8-64 moves per position**
- **Search depth: 2-5 moves ahead**
- **Expensive evaluation (100ms+ per position)**

❌ **Not suitable for:**
- Simple games (solved classically)
- Very deep search (>6 moves ahead on NISQ)
- Fast evaluation (<1ms per position)
- Problems with strong alpha-beta pruning

## Quantum Advantage

**Grover's algorithm:** O(√N) vs O(N) minimax

**Best for:** Expensive evaluation + medium depth

**Example:** Chess with neural network evaluation
- Depth 4, branching 16: 16⁴ = 65,536 positions
- Classical: 65,536 × 100ms = 1.8 hours
- Quantum: √65,536 = 256 × 100ms = 25 seconds
- **256× speedup!**

## API Usage

```fsharp
open FSharp.Azure.Quantum.QuantumTreeSearch
open FSharp.Azure.Quantum.Core.BackendAbstraction

let localBackend = LocalBackend() :> IQuantumBackend

let problem = quantumTreeSearch {
    initialState myGameBoard
    maxDepth 3
    branchingFactor 16
    evaluateWith (fun board -> evaluatePosition board)
    generateMovesWith (fun board -> getLegalMoves board)
    topPercentile 0.2
    backend localBackend
    shots 1000
}

match solve problem with
| Ok result -> 
    printfn "Best move: %A" result.BestMove
    printfn "Score: %.2f" result.Score
| Error err -> printfn "Error: %s" err.Message
```

## Real-World Example

**Gomoku (Five-in-a-Row) AI:**

See the complete Gomoku implementation in `../Gomoku/` for a production-ready example using `QuantumTreeSearchBuilder` with:
- 15×15 board game tree
- Threat detection heuristics
- Monte Carlo-style position evaluation
- Interactive console gameplay

## Related Examples

- **Constraint Solving:** Use `QuantumConstraintSolverBuilder` for CSP problems
- **Pattern Matching:** Use `QuantumPatternMatcherBuilder` for configuration optimization
- **Graph Problems:** Use `GraphColoringBuilder` for scheduling problems
