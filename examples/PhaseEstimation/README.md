# Quantum Phase Estimation Examples

**Business Use Case:** Molecular energy calculation, drug discovery, materials science, quantum chemistry simulations

---

## Overview

The `QuantumPhaseEstimatorBuilder` implements Quantum Phase Estimation (QPE) to extract eigenvalues from quantum systems. This is the foundation for:

1. **Drug Discovery** - Calculate ground state energies to predict molecular binding
2. **Materials Science** - Predict electronic properties of new materials
3. **Quantum Chemistry** - Simulate molecular dynamics exponentially faster than classical DFT
4. **Algorithm Research** - Core subroutine for Shor's algorithm, HHL, and VQE

---

## Examples

### 1. Molecular Energy Calculation (Pharmaceutical)

**File:** `MolecularEnergy.fsx`

**Scenario:** A pharmaceutical company calculates ground state energies of drug molecules to predict binding affinity with target proteins.

**What It Demonstrates:**
- Eigenvalue extraction from molecular Hamiltonians
- How QPE provides exponential speedup over classical methods
- Quantum resource requirements for chemistry simulations
- Applications in drug design and materials science

**Run:**
```bash
dotnet fsi MolecularEnergy.fsx
```

**Expected Output:**
```
=== Molecular Energy Calculation with QPE ===

Molecular Rotation Hamiltonian
  Rotation angle θ:     1.0472 radians (60°)

✅ SUCCESS: Molecular Energy Extracted!

MOLECULAR PROPERTIES:
  Estimated Phase (φ):  0.166667
  Ground State Energy:  1.047198 a.u.
  
PHARMACEUTICAL APPLICATION:
  • Lower energy = More stable molecular configuration
  • Predicts drug-protein binding affinity
  • Quantum advantage: Exponentially faster than classical DFT
```

---

## API Reference

### F# Computation Expression

```fsharp
open FSharp.Azure.Quantum.QuantumPhaseEstimator

// Estimate phase of rotation gate (molecular simulation)
let problem = phaseEstimator {
    unitary (RotationZ (Math.PI / 3.0))  // 60° rotation
    precision 12                          // 12-bit precision
    targetQubits 1
}

match estimate problem with
| Ok result ->
    printfn "Phase: %.6f" result.Phase
    printfn "Eigenvalue: %A" result.Eigenvalue
| Error msg ->
    printfn "Error: %s" msg
```

### F# Convenience Functions

```fsharp
// Common quantum gates
let tGate = estimateTGate 10        // T gate: e^(iπ/4)
let sGate = estimateSGate 10        // S gate: e^(iπ/2)

// Custom phase gates
let phaseGate = estimatePhaseGate (Math.PI / 4.0) 12
let rzGate = estimateRotationZ (Math.PI / 3.0) 12
```

### C# API

```csharp
using static FSharp.Azure.Quantum.CSharpBuilders;

// Estimate T gate eigenvalue
var problem = EstimateTGate(precision: 10);
var result = ExecutePhaseEstimator(problem);

if (result.IsOk) {
    var phase = result.ResultValue.Phase;
    var eigenvalue = result.ResultValue.Eigenvalue;
    Console.WriteLine($"Phase: {phase:F6}");
    Console.WriteLine($"Eigenvalue: {eigenvalue}");
}
```

---

## Quantum Phase Estimation (QPE) Algorithm

### What It Does

Given a unitary operator `U` and its eigenstate `|ψ⟩`, QPE estimates the phase `φ` where:

```
U|ψ⟩ = e^(2πiφ)|ψ⟩
```

The eigenvalue `λ = e^(2πiφ)` is extracted to n-bit precision using n counting qubits.

### How It Works

1. **Initialize counting register** to superposition state
2. **Apply controlled-U^(2^k)** operations
3. **Inverse Quantum Fourier Transform** on counting register
4. **Measure** counting register to get binary representation of φ

### Quantum Advantage

| Task | Classical | Quantum (QPE) | Speedup |
|------|-----------|---------------|---------|
| **Eigenvalue of 100×100 matrix** | O(100³) ≈ 1M ops | O((log 100)³) ≈ 343 ops | **3000x** |
| **Molecular simulation (50 atoms)** | Days (DFT) | Minutes (QPE) | **Exponential** |
| **Precision doubling** | 4x time | 2x qubits | **Polynomial** |

---

## Business Applications

### 1. Drug Discovery (Pharmaceutical Industry)

**Problem:** Calculate ground state energy of drug-protein complexes

**Why QPE Helps:**
- Predict binding affinity before synthesis
- Screen millions of molecules virtually
- Identify optimal drug candidates faster

**Example Molecules:**
- Small molecules (< 20 atoms): Feasible on NISQ
- Proteins (100+ atoms): Requires fault-tolerant quantum computers

**ROI:**
- Reduce drug development time from 10 years to 5 years
- Save $100M+ per drug in failed trials
- Enable precision medicine

### 2. Materials Science (Manufacturing)

**Problem:** Predict electronic properties of new materials

**Why QPE Helps:**
- Calculate band structure (conductivity)
- Optimize solar cell efficiency
- Discover new superconductors
- Design better battery materials

**Example Applications:**
- **Semiconductors**: Optimize band gaps for transistors
- **Catalysts**: Find materials for green hydrogen production
- **Batteries**: Predict lithium-ion conductivity

### 3. Quantum Chemistry (Academic Research)

**Problem:** Simulate molecular dynamics

**Why QPE Helps:**
- Exponentially faster than Density Functional Theory (DFT)
- Handle larger molecules (100+ atoms)
- Higher accuracy for excited states

**Example Systems:**
- Photosynthesis light-harvesting complexes
- Nitrogen fixation catalysts (fertilizer production)
- Atmospheric chemistry (climate modeling)

---

## Precision vs. Qubit Tradeoff

| Precision Qubits | Phase Accuracy | Energy Accuracy (kcal/mol) | Use Case |
|------------------|----------------|----------------------------|----------|
| 4 qubits | ±1/16 = 6.25% | ±2.0 | Rough screening |
| 8 qubits | ±1/256 = 0.4% | ±0.1 | Initial design |
| 12 qubits | ±1/4096 = 0.02% | ±0.006 | **Drug discovery** |
| 16 qubits | ±1/65536 = 0.002% | ±0.0004 | **Chemical accuracy** |
| 20 qubits | ±1/1M = 0.0001% | ±0.000025 | High-precision spectroscopy |

**Note:** Chemical accuracy requires ~1 kcal/mol (~0.0016 a.u.) precision

---

## Current Hardware Limitations

### NISQ Era (2024-2025)

- **Qubit Count**: 50-100 qubits (IBM, Google, IonQ)
- **Gate Fidelity**: 99-99.9% (1-0.1% error per gate)
- **Coherence Time**: 100 µs - 1 ms
- **Feasible Systems**: Small molecules (< 10 atoms)

### Fault-Tolerant Era (2030+)

- **Qubit Count**: 1000-10,000 qubits
- **Error Correction**: Logical qubits with 10^-15 error rates
- **Feasible Systems**: Proteins, polymers, materials (100+ atoms)
- **Impact**: Revolutionize drug discovery and materials design

---

## Important Notes

### ⚠️ Accuracy Considerations

- QPE accuracy is `1/2^n` where n = precision qubits
- Higher precision requires more qubits and deeper circuits
- NISQ hardware noise limits practical precision to ~8-10 qubits
- For production chemistry, need fault-tolerant quantum computers

### 🎓 Educational Value

This builder is ideal for:
- Teaching QPE algorithm and its applications
- Understanding quantum chemistry simulations
- Exploring quantum advantage in scientific computing
- Prototyping algorithms for future quantum computers

**Not for:**
- Production drug discovery (requires fault-tolerant systems)
- Real-world molecular simulations (NISQ precision insufficient)
- Replacing classical DFT today (quantum advantage not yet realized)

### 📊 When to Use Quantum vs. Classical

| System Size | Method | Status |
|-------------|--------|--------|
| < 10 atoms | Classical DFT | ✅ Use today |
| 10-50 atoms | Classical DFT | ✅ Use today (slow but works) |
| 50-100 atoms | QPE (fault-tolerant) | ⏳ Wait for better hardware |
| 100+ atoms | QPE (fault-tolerant) | ⏳ Quantum advantage clear, hardware pending |

---

## Next Steps

- Try `../QuantumArithmetic/` - Modular arithmetic for cryptography
- Try `../CryptographicAnalysis/` - Shor's algorithm uses QPE internally
- Explore VQE (Variational Quantum Eigensolver) for NISQ-era chemistry

---

## Resources

- [Quantum Phase Estimation (Wikipedia)](https://en.wikipedia.org/wiki/Quantum_phase_estimation_algorithm)
- [Quantum Chemistry on Quantum Computers](https://arxiv.org/abs/2010.16046)
- [VQE for Molecular Energies](https://www.nature.com/articles/ncomms5213)
- [Quantum Computing for Drug Discovery](https://www.nature.com/articles/s41570-021-00278-1)
