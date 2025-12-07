# HHL Algorithm: Quantum Linear System Solver

Solve linear systems **Ax = b** with exponential quantum speedup!

## What is HHL?

The **Harrow-Hassidim-Lloyd (HHL) algorithm** is a groundbreaking quantum algorithm that solves linear systems exponentially faster than classical methods:

- **Classical**: O(N log N) using conjugate gradient (for sparse matrices)
- **Quantum HHL**: O(log(N) × poly(κ, log(ε)))

where:
- N = matrix dimension
- κ = condition number (λ_max / λ_min)
- ε = desired accuracy

## Running the Example

```bash
cd examples/LinearSystemSolver
dotnet fsi HHLAlgorithm.fsx
```

## What This Example Demonstrates

### 1. **Simple 2×2 System** (Educational)
Solves a basic electrical circuit problem:
```
2V₁ = 4  →  V₁ = 2 volts
1V₂ = 2  →  V₂ = 2 volts
```

### 2. **Ill-Conditioned Matrix** (Stress Test)
Shows how condition number affects success probability:
- Matrix: diag(100, 1)
- κ = 100 (ill-conditioned!)
- Success probability ∝ 1/κ²

### 3. **Larger System** (4×4)
Demonstrates scalability:
- 8 qubits total (5 clock + 2 solution + 1 ancilla)
- Finite element analysis use case

### 4. **Möttönen's State Preparation**
NEW! Arbitrary quantum state encoding:
- Previous limitation: Only dominant component
- Now: Full amplitude encoding
- Enables solving Ax = b for ANY vector b

### 5. **Trotter-Suzuki Decomposition**
NEW! Non-diagonal matrix support:
- Previous limitation: Only diagonal matrices
- Now: Any Hermitian matrix via Pauli decomposition
- Configurable accuracy vs. circuit depth tradeoff

## Key Innovations in This Implementation

### ✅ Production-Ready Features:

1. **Möttönen's Method** (307 lines)
   - Arbitrary quantum state preparation
   - Gray code optimization: O(2ⁿ) gates
   - Numerically stable

2. **Trotter-Suzuki** (361 lines)
   - 1st and 2nd order formulas
   - Pauli decomposition
   - Configurable Trotter steps

3. **Full HHL Pipeline**
   - Quantum Phase Estimation (QPE)
   - Eigenvalue inversion
   - Post-selection
   - LocalBackend + Cloud support

## When Does HHL Provide Quantum Advantage?

| N | κ | Sparse | Classical | Quantum | Speedup |
|---|---|--------|-----------|---------|---------|
| 100 | <10 | Yes | O(N log N) | O(log N) | ~10× |
| 1,000 | <100 | Yes | ~10⁶ ops | ~10³ ops | ~1,000× |
| 1,000,000 | <100 | Yes | ~10¹² ops | ~10⁶ ops | **~1,000,000×** |

### Requirements:
✓ Large system (N > 1,000)  
✓ Sparse matrix  
✓ Well-conditioned (κ < 100)  
✓ Quantum output acceptable (no full tomography)

## Real-World Applications

### 1. Quantum Chemistry
- **Problem**: Molecular ground state energies
- **Matrix**: Hamiltonian (sparse, Hermitian)
- **Benefit**: Simulate larger molecules

### 2. Machine Learning
- **Problem**: Quantum SVM, regression
- **Matrix**: Kernel/covariance matrices
- **Benefit**: Train on exponentially more data

### 3. Financial Modeling
- **Problem**: Portfolio optimization
- **Matrix**: Asset covariance matrices
- **Benefit**: Analyze thousands of assets

### 4. Engineering Simulation
- **Problem**: Finite element analysis
- **Matrix**: Stiffness matrices (sparse)
- **Benefit**: Simulate larger structures

## Example Output

```
╔══════════════════════════════════════════════════════════════════════╗
║  HHL ALGORITHM: Quantum Linear System Solver                         ║
║  Exponential Speedup for Ax = b                                       ║
╚══════════════════════════════════════════════════════════════════════╝

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
SCENARIO 1: Simple 2×2 Diagonal System
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

BUSINESS PROBLEM:
  Solve electrical circuit with 2 nodes:
    2V₁ = 4  (node 1)
    1V₂ = 2  (node 2)

✅ SUCCESS!
  Success Probability: 0.8542
  Condition Number (κ): 2.00
  Gates Used: 127
  Backend: LocalBackend
```

## API Usage

### Basic Solver
```fsharp
open FSharp.Azure.Quantum.QuantumLinearSystemSolver

let problem = linearSystemSolver {
    matrix [[2.0; 0.0]; [0.0; 1.0]]
    vector [4.0; 2.0]
    precision 4
}

match solve problem with
| Ok result -> printfn "Success rate: %.2f" result.SuccessProbability
| Error err -> printfn "Error: %s" err.Message
```

### Diagonal Systems (Fast Path)
```fsharp
let result = solveDiagonal [2.0; 4.0; 8.0; 16.0] [1.0; 1.0; 1.0; 1.0]
```

### Advanced Configuration
```fsharp
let problem = linearSystemSolver {
    matrix [[2.0; 0.0]; [0.0; 1.0]]
    vector [1.0; 0.0]
    precision 6
    eigenvalueQubits 8
    minEigenvalue 0.001
    inversionMethod (LinearApproximation 1.0)
    postSelection true
}
```

## Technical Details

### Circuit Structure
```
Qubits:
  [0 .. n_clock-1]:        Clock register (QPE)
  [n_clock .. n_clock+n_b-1]: Solution register |b⟩
  [n_clock+n_b]:           Ancilla (success indicator)

Phases:
  1. State prep: |0⟩ → |b⟩ using Möttönen's method
  2. QPE forward: Extract eigenvalues into clock register
  3. Inversion: Controlled rotations ∝ 1/λ on ancilla
  4. QPE backward: Uncompute clock register
  5. Measurement: ancilla=|1⟩ indicates success
```

### Algorithms Used

**Möttönen's State Preparation:**
- Amplitude preparation: controlled-RY gates
- Phase preparation: controlled-RZ gates
- Gray code traversal for efficiency
- Circuit depth: O(2ⁿ) gates

**Trotter-Suzuki Decomposition:**
- 1st order: [e^(-iH₁Δt) ... e^(-iHₖΔt)]^n
- 2nd order: Symmetrized, better accuracy
- Error: O(t²/n) for 1st order
- Pauli basis decomposition

## References

- [HHL Paper](https://arxiv.org/abs/0811.3171) - Harrow, Hassidim, Lloyd (2009)
- [Möttönen & Vartiainen](https://arxiv.org/abs/quant-ph/0407010) - State preparation (2004)
- [Trotter-Suzuki](https://arxiv.org/abs/1901.00564) - Hamiltonian simulation review

## Next Steps

1. **Try different matrices**: Experiment with various condition numbers
2. **Adjust precision**: Balance accuracy vs. circuit depth
3. **Cloud execution**: Run on IonQ or Rigetti hardware
4. **Compare classical**: Benchmark against NumPy/SciPy solvers
5. **Real applications**: Apply to quantum chemistry or ML problems

## Performance Tips

- **For well-conditioned systems (κ < 10)**: Use default settings
- **For ill-conditioned (κ > 100)**: Increase precision, use preconditioning
- **For large systems**: Use Trotter-Suzuki with adaptive step count
- **For cloud backends**: Minimize shots, use error mitigation

---

**Ready to revolutionize linear algebra with quantum computing? Run the example now!** 🚀
