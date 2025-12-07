# Quantum Constraint Solver Examples

Demonstrates solving constraint satisfaction problems (CSPs) using Grover's algorithm for O(√N) speedup.

## Examples

### 1. Sudoku Solver (`SudokuSolver.fsx`)

Solve 4×4 and 9×9 Sudoku puzzles using quantum constraint satisfaction.

**Run:**
```bash
dotnet fsi SudokuSolver.fsx
```

**Use Cases:**
- Sudoku puzzles (any size)
- Logic puzzles with constraints
- Configuration problems with rules

### 2. N-Queens Puzzle

Place N queens on N×N chessboard with no attacks (same row/column/diagonal).

**Use Cases:**
- Classic CSP benchmarks
- Resource placement problems
- Non-attacking placement optimization

### 3. Job Scheduling

Assign workers to shifts respecting skills, availability, and no overlaps.

**Use Cases:**
- Healthcare: nurse scheduling with certifications
- Manufacturing: operator assignment with qualifications
- Retail: employee scheduling with preferences

## When to Use

✅ **Good for:**
- Constraint satisfaction problems (find ANY valid solution)
- Small-to-medium search spaces (10³-10⁶ states)
- Expensive constraint checking
- Problems with no structure to exploit

❌ **Not suitable for:**
- Optimization problems (use QAOA/VQE instead)
- Very large search spaces (>10⁶ states)
- Problems with efficient classical algorithms

## Quantum Advantage

**Grover's algorithm:** O(√N) vs O(N) classical backtracking

**Example:** 4096 possible assignments
- Classical: 4096 constraint checks
- Quantum: √4096 = 64 constraint checks
- **64× speedup!**

## API Usage

```fsharp
open FSharp.Azure.Quantum.QuantumConstraintSolver
open FSharp.Azure.Quantum.Core.BackendAbstraction

let localBackend = LocalBackend() :> IQuantumBackend

let problem = constraintSolver {
    searchSpace 3  // 3 workers to assign
    domain [0..9]  // Shift numbers 0-9
    satisfies (fun assignment -> 
        checkSkillMatch assignment && 
        checkAvailability assignment
    )
    backend localBackend
    shots 1000
}

match solve problem with
| Ok solution -> 
    printfn "Worker assignments: %A" solution.Assignment
    printfn "Constraints satisfied: %b" solution.AllConstraintsSatisfied
| Error err -> printfn "Error: %s" err.Message
```

## Related Examples

- **Tree Search:** Use `QuantumTreeSearchBuilder` for game AI
- **Pattern Matching:** Use `QuantumPatternMatcherBuilder` for configuration optimization
- **Graph Problems:** Use `GraphColoringBuilder` for register allocation, scheduling
