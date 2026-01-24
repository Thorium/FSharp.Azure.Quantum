# Drone Swarm Choreography

**Quantum-Enhanced Formation Optimization for Drone Light Shows**

This example demonstrates how to use FSharp.Azure.Quantum's QAOA implementation to solve the Quadratic Assignment Problem (QAP) for optimal drone formation transitions.

## Overview

The example simulates an 8-drone aerial light show with the following sequence:

```
Ground (Line) → Diamond → Square → Heart → Ground (Return)
```

Each transition requires assigning drones to target positions in a way that minimizes total flight distance - a classic optimization problem that maps perfectly to quantum computing.

## The Problem: Quadratic Assignment Problem (QAP)

### What is QAP?

The Quadratic Assignment Problem asks: *How do we assign n facilities to n locations to minimize total cost?*

In our drone context:
- **Facilities** = Drones (8 drones)
- **Locations** = Target positions in the formation (8 positions)
- **Cost** = Flight distance

### Why is QAP Hard?

According to [Wikipedia](https://en.wikipedia.org/wiki/Quadratic_assignment_problem):

> "The QAP is NP-hard and has no known polynomial-time algorithm. Even finding a constant-factor approximation is NP-hard unless P=NP."

- **Brute force**: O(n!) complexity = 40,320 permutations for just 8 drones
- **Classical heuristics**: Greedy algorithms find good (but not optimal) solutions
- **Quantum approach**: QAOA can explore the solution space more efficiently

### Real-World Applications

QAP appears in many domains:
- **Drone light shows** (this example)
- **Facility layout planning** - minimize material handling costs
- **PCB component placement** - minimize wire length
- **Keyboard design** - minimize finger travel
- **Hospital department planning** - minimize patient/staff travel

## QUBO Formulation

### Variables

For n drones and n positions, we create n² binary variables:

```
x[i,j] = 1 if drone i is assigned to position j
x[i,j] = 0 otherwise
```

For 8 drones: 64 binary variables = 64 qubits required

### Objective Function

Minimize total flight distance:

```
minimize: Σᵢⱼ distance[i,j] × x[i,j]
```

### Constraints

1. **Each drone assigned exactly once**:
   ```
   Σⱼ x[i,j] = 1  for all drones i
   ```

2. **Each position filled exactly once**:
   ```
   Σᵢ x[i,j] = 1  for all positions j
   ```

### Penalty Encoding

Constraints are converted to penalties using the Lucas rule:

```
λ > max(objective value)
```

This ensures that any constraint violation incurs a cost greater than the best valid solution.

## Formations

### Ground (Starting Position)
8 drones in a horizontal line at ground level, 4m apart:
```
0   1   2   3   4   5   6   7
```

### Diamond
Vertical diamond shape at altitude (5-30m):
```
        0         <- Top (30m)
      1   2       <- Upper (20m)
    3       4     <- Middle (15m)
      5   6       <- Lower (10m)
        7         <- Bottom (5m)
```

### Square
2×4 grid in vertical plane:
```
0   1   2   3     <- Top row (25m)
4   5   6   7     <- Bottom row (10m)
```

### Heart
8-point heart approximation:
```
      5   6   7       <- Upper lobes (27m, 23m)
    3       4         <- Curve peaks (20m)
      1   2           <- Lower curves (12m)
        0             <- Bottom point (5m)
```

## Usage

```bash
# Build the example
cd examples/Drone/SwarmChoreography
dotnet build

# Run with default settings (compares both methods)
dotnet run

# Run with specific method
dotnet run -- --method classical   # Greedy heuristic only
dotnet run -- --method quantum     # QAOA solver (falls back if too many qubits)
dotnet run -- --method both        # Compare both methods

# Specify output directory
dotnet run -- --out ./my-results

# Show help
dotnet run -- --help
```

## Output

The program generates:
1. **Console output** - ASCII visualizations of formations and transition details
2. **metrics.json** - Machine-readable performance data
3. **run-report.md** - Human-readable summary report

Example output:
```
╔════════════════════════════════════════════════════════════╗
║  FORMATION TRANSITION                                      ║
╠════════════════════════════════════════════════════════════╣
║  From: Ground (Line)                                       ║
║  To:   Diamond                                             ║
║  Method: Classical (Greedy)                                ║
║  Total Distance:   142.35 meters                           ║
╠════════════════════════════════════════════════════════════╣
║  ASSIGNMENTS:                                              ║
║    Drone 0 → Position 3                                    ║
║    Drone 1 → Position 1                                    ║
║    ...                                                     ║
╚════════════════════════════════════════════════════════════╝
```

## Quantum Computing Notes

### Current Limitations

- **8 drones × 8 positions = 64 qubits** required for QUBO encoding
- **LocalBackend limit** is 20 qubits (memory grows as 2^n)
- Quantum solver automatically falls back to classical when qubit limit exceeded

### Scaling Options

1. **Reduce problem size**: 4 drones = 16 qubits (fits LocalBackend)
2. **Use Azure Quantum**: Real quantum hardware or simulators with higher qubit counts
3. **Hybrid approaches**: Decompose large problems into smaller sub-problems

### RULE 1 Compliance

This example follows the library's RULE 1 pattern:

```fsharp
// Quantum solver requires IQuantumBackend parameter
let solve 
    (backend: IQuantumBackend)   // Required - no default
    (shots: int)                 // Required - explicit control
    (distanceMatrix: float[,]) 
    : Result<Assignment[], string>

// Classical solver marked obsolete with migration guidance
[<Obsolete("Use Solver.solve(backend, shots, distanceMatrix)")>]
let solveClassical (distanceMatrix: float[,]) : Assignment[]
```

## References

1. **Quadratic Assignment Problem** - [Wikipedia](https://en.wikipedia.org/wiki/Quadratic_assignment_problem)
2. **Drone display** - [Wikipedia](https://en.wikipedia.org/wiki/Drone_display)
3. **QAOA** - Farhi, E., Goldstone, J., & Gutmann, S. (2014). A Quantum Approximate Optimization Algorithm. arXiv:1411.4028

## See Also

- [FleetRouting Example](../FleetRouting/README.md) - TSP for drone delivery routes
- [DroneDelivery Example](../DroneDelivery/README.md) - Job shop scheduling for deliveries
