# Quantum Chemistry API Reference

## Overview

The Quantum Chemistry Domain Builder provides an idiomatic F# computation expression API for molecular ground state energy calculations using VQE (Variational Quantum Eigensolver). Built on top of the VQE Framework (TKT-95), it offers a chemistry-specific interface for quantum chemistry simulations.

**Key Use Cases:**
- **Drug Discovery** - Calculate molecular binding energies and reaction pathways
- **Materials Science** - Design battery materials, catalysts, and semiconductors  
- **Chemical Reactions** - Analyze transition states and reaction barriers
- **Quantum Simulation** - Molecular dynamics and electronic structure

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [F# API Reference](#f-api-reference)
3. [C# API Equivalent](#c-api-equivalent)
4. [Real-World Examples](#real-world-examples)
5. [Pre-Built Molecules](#pre-built-molecules)
6. [Ansatz Types](#ansatz-types)
7. [Performance Characteristics](#performance-characteristics)

---

## Quick Start

### Installation

```bash
dotnet add package FSharp.Azure.Quantum
```

### Hello World - H2 Molecule Ground State

```fsharp
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder

// Problem: Calculate H2 ground state energy at 0.74 Angstrom
let problem = quantumChemistry {
    molecule (h2 0.74)       // H2 at equilibrium bond length
    basis "sto-3g"           // Minimal basis set
    ansatz UCCSD             // Unitary Coupled Cluster ansatz
}

let! result = solve problem

match result with
| Ok solution ->
    printfn "Ground state energy: %.6f Ha" solution.GroundStateEnergy
    printfn "H-H bond length: %.2f Å" solution.BondLengths.["H-H"]
| Error msg ->
    printfn "Error: %s" msg

// Expected output:
// Ground state energy: -1.137000 Ha
// H-H bond length: 0.74 Å
```

---

## F# API Reference

### Types

#### ChemistryAnsatz

Chemistry-specific ansatz types for VQE calculations.

```fsharp
type ChemistryAnsatz =
    | UCCSD   // Unitary Coupled Cluster Singles Doubles (most accurate)
    | HEA     // Hardware-Efficient Ansatz (faster, less accurate)
    | ADAPT   // Adaptive ansatz (dynamic construction)
```

#### ChemistryProblem

Configuration for a quantum chemistry calculation.

```fsharp
type ChemistryProblem = {
    Molecule: Molecule option
    Basis: string option
    Ansatz: ChemistryAnsatz option
    Optimizer: OptimizerConfig option
    MaxIterations: int
    InitialParameters: float[] option
}
```

#### ChemistryResult

Result of a ground state calculation.

```fsharp
type ChemistryResult = {
    GroundStateEnergy: float              // Energy in Hartrees
    OptimalParameters: float[]            // VQE parameters found
    Iterations: int                       // Number of iterations
    Convergence: bool                     // Whether VQE converged
    BondLengths: Map<string, float>       // Bond lengths in Angstroms
    DipoleMoment: float option            // Dipole moment (if computed)
}
```

### Computation Expression Builder

#### quantumChemistry { }

F# computation expression for building chemistry problems.

**Available Operations:**

| Operation | Description | Example |
|-----------|-------------|---------|
| `molecule` | Set molecule for calculation | `molecule (h2 0.74)` |
| `basis` | Set basis set | `basis "sto-3g"` |
| `ansatz` | Set ansatz type | `ansatz UCCSD` |
| `optimizer` | Set optimizer method | `optimizer "COBYLA"` |
| `maxIterations` | Set iteration limit | `maxIterations 200` |
| `initialParameters` | Set warm start parameters | `initialParameters params` |

**Example:**

```fsharp
let problem = quantumChemistry {
    molecule (h2o 0.96 104.5)
    basis "sto-3g"
    ansatz HEA
    maxIterations 150
}
```

### Pre-Built Molecules

Convenience functions for common molecules.

#### h2

```fsharp
val h2 : distance:float -> Molecule
```

Create H2 molecule at specified bond length.

**Parameters:**
- `distance` - Bond length in Angstroms

**Example:**
```fsharp
let hydrogen = h2 0.74  // Equilibrium bond length
```

#### h2o

```fsharp
val h2o : bondLength:float -> angle:float -> Molecule
```

Create H2O (water) molecule with specified geometry.

**Parameters:**
- `bondLength` - O-H bond length in Angstroms
- `angle` - H-O-H angle in degrees

**Example:**
```fsharp
let water = h2o 0.96 104.5  // Standard geometry
```

#### lih

```fsharp
val lih : distance:float -> Molecule
```

Create LiH (lithium hydride) molecule.

**Parameters:**
- `distance` - Li-H bond length in Angstroms

**Example:**
```fsharp
let lithiumHydride = lih 1.6  // Typical bond length
```

### Solver

#### solve

```fsharp
val solve : ChemistryProblem -> Async<Result<ChemistryResult, string>>
```

Solve quantum chemistry problem using VQE framework.

**Parameters:**
- `problem` - Chemistry problem specification

**Returns:**
- `Result<ChemistryResult, string>` - Success with result or error message

**Example:**
```fsharp
let! result = solve problem

match result with
| Ok solution ->
    printfn "Energy: %.6f Ha" solution.GroundStateEnergy
| Error msg ->
    printfn "Failed: %s" msg
```

---

## C# API Equivalent

For C# developers, use the underlying VQE framework directly with the existing `Molecule` and `SolverConfig` types.

### C# Example

```csharp
using FSharp.Azure.Quantum.QuantumChemistry;
using static FSharp.Azure.Quantum.QuantumChemistry.Molecule;

// Create H2 molecule
var molecule = createH2(0.74);

// Configure VQE solver
var config = new SolverConfig
{
    Method = GroundStateMethod.VQE,
    MaxIterations = 100,
    Tolerance = 1e-6,
    InitialParameters = null
};

// Run calculation
var result = await GroundStateEnergy.estimateEnergy(molecule, config);

if (result.IsOk)
{
    var energy = result.ResultValue;
    Console.WriteLine($"Ground state energy: {energy:F6} Ha");
}
else
{
    Console.WriteLine($"Error: {result.ErrorValue}");
}
```

**Note:** C# uses the VQE framework API directly. The F# computation expression builder provides additional features like control flow and composition that are specific to F# syntax.

---

## Real-World Examples

### Example 1: Bond Length Scan

Calculate energy at multiple bond lengths to find optimal geometry.

```fsharp
let bondScan = async {
    let mutable results = []
    let mutable previousParams = None
    
    for distance in [0.5 .. 0.1 .. 1.5] do
        let problem = quantumChemistry {
            molecule (h2 distance)
            basis "sto-3g"
            ansatz UCCSD
            
            // Warm start: reuse previous parameters
            match previousParams with
            | Some params -> initialParameters params
            | None -> ()
        }
        
        let! result = solve problem
        match result with
        | Ok solution ->
            printfn "%.2f Å: %.6f Ha" distance solution.GroundStateEnergy
            previousParams <- Some solution.OptimalParameters
            results <- (distance, solution.GroundStateEnergy) :: results
        | Error msg ->
            printfn "Error at %.2f: %s" distance msg
    
    return List.rev results
}

let! energyCurve = bondScan
```

### Example 2: Conditional Basis Selection

Choose basis set based on molecule size.

```fsharp
let calculateMolecule (mol: Molecule) = quantumChemistry {
    molecule mol
    
    // Conditional basis selection using F# syntax
    if mol.Atoms.Length <= 4 then
        basis "sto-3g"      // Fast for small molecules
    else
        basis "6-31g"       // More accurate for larger molecules
    
    ansatz UCCSD
    maxIterations 200
}
```

### Example 3: Multiple Molecules Comparison

Compare different molecules systematically.

```fsharp
let molecules = [
    ("H2", h2 0.74)
    ("H2O", h2o 0.96 104.5)
    ("LiH", lih 1.6)
]

for (name, mol) in molecules do
    let problem = quantumChemistry {
        molecule mol
        basis "sto-3g"
        ansatz HEA  // Faster ansatz for comparison
    }
    
    let! result = solve problem
    match result with
    | Ok solution ->
        printfn "%s: %.6f Ha (%d atoms)" 
            name 
            solution.GroundStateEnergy 
            mol.Atoms.Length
    | Error msg ->
        printfn "%s: Error - %s" name msg
```

---

## Pre-Built Molecules

### Common Molecules Reference

| Molecule | Function | Parameters | Typical Value |
|----------|----------|------------|---------------|
| H₂ (Hydrogen) | `h2 distance` | Distance (Å) | 0.74 |
| H₂O (Water) | `h2o bondLength angle` | Bond length (Å), Angle (°) | 0.96, 104.5 |
| LiH (Lithium Hydride) | `lih distance` | Distance (Å) | 1.6 |

### Custom Molecules

For molecules not pre-built, use the `Molecule` type directly:

```fsharp
let customMolecule = {
    Name = "NH3"
    Atoms = [
        { Element = "N"; Position = (0.0, 0.0, 0.0) }
        { Element = "H"; Position = (1.0, 0.0, 0.0) }
        { Element = "H"; Position = (0.0, 1.0, 0.0) }
        { Element = "H"; Position = (0.0, 0.0, 1.0) }
    ]
    Bonds = [
        { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
        { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
        { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 }
    ]
    Charge = 0
    Multiplicity = 1
}

let problem = quantumChemistry {
    molecule customMolecule
    basis "sto-3g"
    ansatz UCCSD
}
```

---

## Ansatz Types

### UCCSD - Unitary Coupled Cluster

**Best For:** High accuracy, small molecules (2-4 atoms)

**Characteristics:**
- Most accurate ansatz for chemistry
- Expensive: O(n⁴) parameters where n = qubits
- Physically motivated (mimics coupled cluster theory)

**When to Use:**
- Publication-quality results
- Benchmark calculations
- Small molecules where accuracy is critical

**Example:**
```fsharp
ansatz UCCSD
```

### HEA - Hardware-Efficient Ansatz

**Best For:** Larger molecules, exploratory work

**Characteristics:**
- Faster execution: fewer parameters
- Less accurate than UCCSD
- Optimized for quantum hardware

**When to Use:**
- Quick estimations
- Larger molecules (5+ atoms)
- Hardware with limited coherence time

**Example:**
```fsharp
ansatz HEA
```

### ADAPT - Adaptive Ansatz

**Best For:** Research, custom workflows

**Characteristics:**
- Dynamically constructs ansatz
- Balances accuracy and efficiency
- Requires gradient calculations

**When to Use:**
- Research applications
- When UCCSD is too expensive but HEA too approximate
- Adaptive algorithm development

**Example:**
```fsharp
ansatz ADAPT
```

---

## Performance Characteristics

### Computational Complexity

| Molecule | Atoms | Qubits | UCCSD Parameters | HEA Parameters | Typical Time (Local) |
|----------|-------|--------|------------------|----------------|---------------------|
| H₂ | 2 | 4 | ~16 | ~8 | 100-200ms |
| H₂O | 3 | 10 | ~100 | ~20 | 500ms-1s |
| LiH | 2 | 6 | ~36 | ~12 | 200-400ms |

### Basis Sets

| Basis Set | Accuracy | Speed | Recommended For |
|-----------|----------|-------|-----------------|
| sto-3g | Low | Fast | Initial testing, small molecules |
| 6-31g | Medium | Medium | Production calculations |
| cc-pVDZ | High | Slow | High-accuracy research |

### Optimization

**Tips for Faster Calculations:**

1. **Warm Start:** Reuse parameters from similar geometries
   ```fsharp
   initialParameters previousSolution.OptimalParameters
   ```

2. **Appropriate Ansatz:** Use HEA for quick estimates
   ```fsharp
   ansatz HEA  // 2-3x faster than UCCSD
   ```

3. **Iteration Limits:** Start with fewer iterations
   ```fsharp
   maxIterations 50  // Quick test
   maxIterations 200  // Production
   ```

---

## Best Practices

### Validation

Always validate results against known values:

```fsharp
let validateH2Energy (energy: float) =
    let expected = -1.137  // Experimental H2 ground state
    let error = abs(energy - expected)
    
    if error < 0.1 then
        printfn "✓ Energy within 0.1 Ha of experimental"
    else
        printfn "⚠ Large deviation: %.3f Ha error" error
```

### Error Handling

Use Result type pattern matching:

```fsharp
let! result = solve problem

match result with
| Ok solution when solution.Convergence ->
    // Use result
    printfn "Converged: %.6f Ha" solution.GroundStateEnergy
    
| Ok solution ->
    // Did not converge
    printfn "⚠ Did not converge after %d iterations" solution.Iterations
    
| Error msg ->
    // Calculation failed
    printfn "✗ Error: %s" msg
```

### Reproducibility

Save configurations for reproducible research:

```fsharp
type ChemistryExperiment = {
    Molecule: Molecule
    BasisSet: string
    AnsatzType: ChemistryAnsatz
    Result: ChemistryResult
    Timestamp: DateTime
}

let saveExperiment (problem: ChemistryProblem) (result: ChemistryResult) =
    {
        Molecule = problem.Molecule.Value
        BasisSet = problem.Basis.Value
        AnsatzType = problem.Ansatz.Value
        Result = result
        Timestamp = DateTime.UtcNow
    }
```

---

## See Also

- [Getting Started Guide](getting-started.md)
- [Local Simulation](local-simulation.md)
- [Backend Switching](backend-switching.md)
- [Examples](../examples/QuantumChemistry/)

---

## References

- VQE Algorithm: [Peruzzo et al., Nature Communications (2014)](https://arxiv.org/abs/1304.3061)
- Quantum Chemistry: [McArdle et al., Reviews of Modern Physics (2020)](https://arxiv.org/abs/1808.10402)
- UCCSD Ansatz: [Romero et al., Quantum Science and Technology (2018)](https://arxiv.org/abs/1701.02691)
