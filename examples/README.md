# Topological Quantum Computing Examples

This directory contains example scripts demonstrating the FSharp.Azure.Quantum.Topological library features.

## Running Examples

```bash
# From the examples/ directory
dotnet fsi ModularDataExample.fsx
dotnet fsi ToricCodeExample.fsx
```

## Available Examples

### 1. ModularDataExample.fsx
**Demonstrates**: Modular S and T matrices - fundamental topological invariants

**Topics covered**:
- Computing S-matrix (unlinking matrix)
- Computing T-matrix (twist matrix)
- Verifying consistency relations (unitarity, (ST)³ = S²)
- Quantum dimensions and total quantum dimension D
- Ground state degeneracies on different genus surfaces
- Comparing Ising, Fibonacci, and SU(2)₃ theories

**Output**: Shows S and T matrices, verification checks, quantum dimensions, and topological properties

**Key concepts**:
- Modular tensor categories (MTCs)
- Topological spins θₐ = exp(2πi hₐ)
- Ground state degeneracy: dim(g) = Σₐ S₀ₐ^(2-2g)

---

### 2. ToricCodeExample.fsx
**Demonstrates**: Topological error correction with toric code

**Topics covered**:
- Creating toric code lattice with periodic boundary
- Initializing ground state (all stabilizers +1)
- Code parameters (distance, encoding rate)
- Injecting X errors (bit flips) → e-particle pairs
- Injecting Z errors (phase flips) → m-particle pairs
- Measuring syndromes (stabilizer violations)
- Detecting anyon positions
- Toric distance calculations

**Output**: Shows lattice creation, error injection, syndrome measurement, and anyon detection

**Key concepts**:
- Stabilizer operators (vertex A_v, plaquette B_p)
- Z₂ × Z₂ anyon theory {1, e, m, ε}
- Topological protection
- Error correction capability

---

## Prerequisites

All examples require the compiled library:

```bash
# Build the library first
cd ../src/FSharp.Azure.Quantum.Topological
dotnet build
```

## Expected Output

Each example script:
- ✅ Runs without errors
- ✅ Shows step-by-step computations
- ✅ Verifies mathematical properties
- ✅ Displays results in readable format
- ✅ Includes educational commentary

## Educational Value

These examples serve as:
1. **Learning materials** - Understand topological quantum concepts
2. **API documentation** - See how to use library functions
3. **Verification** - Confirm library works correctly
4. **Starting points** - Template for your own experiments

## Additional Resources

- **Library documentation**: See XML doc comments in source files
- **Test files**: `tests/FSharp.Azure.Quantum.Topological.Tests/`
- **Academic reference**: Steven H. Simon, "Topological Quantum" (2023)

## Next Steps

Try modifying the examples:
- Change lattice sizes in ToricCode
- Inject different error patterns
- Compute modular data for SU(2)₄ or higher levels
- Calculate ground state degeneracies for high-genus surfaces

Happy experimenting! 🚀
