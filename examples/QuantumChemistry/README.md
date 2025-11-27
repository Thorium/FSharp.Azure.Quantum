# Quantum Chemistry Examples

Examples demonstrating quantum chemistry capabilities for drug discovery and materials science applications.

## Overview

This directory contains F# script examples showing how to use the Quantum Chemistry VQE (Variational Quantum Eigensolver) framework for molecular ground state energy calculations and Hamiltonian simulations.

## Examples

### 1. H2Molecule.fsx - Hydrogen Molecule Ground State Energy

Demonstrates VQE calculation for the simplest molecule (H₂).

**Features:**
- Create H2 molecule at equilibrium bond length (0.74 Å)
- Run VQE to estimate ground state energy (~-1.174 Hartree)
- Compare VQE vs Classical DFT methods
- Automatic method selection based on molecule size
- Energy unit conversions (Hartree → eV)

**Usage:**
```bash
dotnet fsi H2Molecule.fsx
```

**Expected Output:**
```
=== H2 Molecule Ground State Energy ===
Molecule: H2
Atoms: 2
Bonds: 1
Electrons: 2

Running VQE calculation...
✓ Ground state energy: -1.168000 Hartree
  Expected (experimental): -1.174 Hartree
  Error: 0.006000 Hartree
```

### 2. H2OWater.fsx - Water Molecule Example

Demonstrates quantum chemistry for a polyatomic molecule (H₂O).

**Features:**
- Create H2O molecule at equilibrium geometry
- Calculate bond lengths and angles
- Ground state energy calculation
- Automatic method selection for larger molecules
- Molecular geometry analysis

**Usage:**
```bash
dotnet fsi H2OWater.fsx
```

**Expected Output:**
```
=== H2O (Water) Molecule Ground State Energy ===
Molecule: H2O
Atoms: 3 (O, H, H)
Bonds: 2
Electrons: 10

Bond lengths:
  O-H (bond 1): 0.957 Angstroms
  O-H (bond 2): 0.957 Angstroms

✓ Ground state energy: -75.996000 Hartree
  Expected (experimental): -76.0 Hartree
```

### 3. HamiltonianTimeEvolution.fsx - Time Evolution Simulation

Demonstrates Hamiltonian simulation using Trotter-Suzuki decomposition.

**Features:**
- Build molecular Hamiltonian from molecule structure
- Time evolution: exp(-iHt)|ψ₀⟩
- Trotter-Suzuki decomposition (1st and 2nd order)
- Unitary evolution verification (norm preservation)
- State probability analysis
- Comparison of different Trotter orders

**Usage:**
```bash
dotnet fsi HamiltonianTimeEvolution.fsx
```

**Expected Output:**
```
=== Hamiltonian Time Evolution Simulation ===
✓ Molecular Hamiltonian constructed
  Qubits: 4
  Terms: 10

Initial state: |00...0⟩
  Norm: 1.000000

Running time evolution: exp(-iHt)|ψ₀⟩
✓ Simulation complete
  Final norm: 1.000000 (unitary evolution preserved)
```

## Prerequisites

- .NET SDK 8.0 or later
- FSharp.Azure.Quantum NuGet package

## Concepts Demonstrated

### Quantum Chemistry
- **Molecule Representation**: Atoms, bonds, 3D coordinates
- **Ground State Energy**: Lowest energy configuration of electrons
- **VQE (Variational Quantum Eigensolver)**: Hybrid quantum-classical algorithm
- **Hamiltonian**: Energy operator for quantum systems
- **Trotter Decomposition**: Approximating time evolution with product of gates

### Energy Units
- **Hartree**: Atomic unit of energy (1 Ha ≈ 27.2114 eV)
- **Electron Volt (eV)**: Common unit in chemistry and physics
- Negative energies indicate bound, stable molecules

### Applications
- **Drug Discovery**: Molecular binding energies, reaction pathways
- **Materials Science**: Battery materials, catalysts, semiconductors
- **Chemical Reactions**: Transition states, reaction barriers
- **Quantum Simulation**: Molecular dynamics, electronic structure

## Architecture

All examples use:
- **Backend-Agnostic Design**: Works with Local, IonQ, Rigetti, or Azure quantum backends
- **No External Dependencies**: Pure F# implementation, no Microsoft.Quantum.Chemistry required
- **Existing Infrastructure**: Reuses StateVector, Gates, and QaoaCircuit modules

## Performance

- **H2 calculation**: ~100-200ms (local simulation)
- **H2O calculation**: ~200-500ms (local simulation, uses ClassicalDFT fallback)
- **Time evolution**: ~50-100ms per Trotter step

## Further Reading

- **Molecular Representation**: See `Molecule.fs` for types and operations
- **Ground State Energy**: See `GroundStateEnergy.fs` for VQE implementation
- **Hamiltonian Simulation**: See `HamiltonianSimulation.fs` for Trotter decomposition
- **File Formats**: See `MolecularInput.fs` for XYZ and FCIDump parsers

## Contributing

To add more examples:
1. Create a new `.fsx` file in this directory
2. Follow the naming convention: `{MoleculeName}{Feature}.fsx`
3. Include clear comments and expected output
4. Add entry to this README

## References

- VQE Algorithm: [arXiv:1304.3061](https://arxiv.org/abs/1304.3061)
- Trotter-Suzuki Decomposition: [Wikipedia](https://en.wikipedia.org/wiki/Lie_product_formula)
- Quantum Chemistry: "Quantum Computing in the NISQ era and beyond" (Preskill, 2018)
