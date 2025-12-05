# Mathematical Foundations for Quantum Computing

**A Quick Reference Guide for FSharp.Azure.Quantum Developers**

This guide provides the essential mathematical concepts needed to understand and work with quantum computing algorithms in this library. For deeper coverage, see [Quantum Computing: An Applied Approach (Hidary, 2021), Chapters 11-14](https://link.springer.com/book/10.1007/978-3-030-83274-2).

---

## Table of Contents

1. [Complex Numbers](#complex-numbers)
2. [Vectors and Vector Spaces](#vectors-and-vector-spaces)
3. [Dirac Notation](#dirac-notation)
4. [Matrices and Linear Transformations](#matrices-and-linear-transformations)
5. [Inner Products and Orthogonality](#inner-products-and-orthogonality)
6. [Tensor Products](#tensor-products)
7. [Eigenvalues and Eigenvectors](#eigenvalues-and-eigenvectors)
8. [Unitary and Hermitian Operators](#unitary-and-hermitian-operators)
9. [Hilbert Spaces](#hilbert-spaces)
10. [Quick Reference: Key Formulas](#quick-reference-key-formulas)

---

## Complex Numbers

### Definition

A complex number has the form: **z = a + bi**

Where:
- **a** = real part (Re(z))
- **b** = imaginary part (Im(z))
- **i** = √(-1) (imaginary unit)

### Key Properties

**Conjugate:** z* = a - bi

**Modulus:** |z| = √(a² + b²)

**Polar Form:** z = r·e^(iθ) where r = |z| and θ = arg(z)

### Euler's Formula

**e^(iθ) = cos(θ) + i·sin(θ)**

Special cases:
- e^(iπ) = -1 (Euler's identity)
- e^(i·π/2) = i
- e^(i·2π) = 1

### Why Complex Numbers in Quantum Computing?

- **Amplitudes** are complex numbers (quantum state coefficients)
- **Phases** encode quantum interference (constructive/destructive)
- **Probabilities** = |amplitude|² (Born rule)

**Example in Code:**
```fsharp
open System.Numerics

// Create complex amplitude
let alpha = Complex(1.0 / sqrt(2.0), 0.0)  // 1/√2
let beta = Complex(0.0, 1.0 / sqrt(2.0))   // i/√2

// Probability = |amplitude|²
let probability = alpha.Magnitude * alpha.Magnitude  // 0.5
```

---

## Vectors and Vector Spaces

### Definition

A **vector** is an ordered list of numbers (real or complex).

**Example:** v = [1, 2, 3] or in column form:
```
v = | 1 |
    | 2 |
    | 3 |
```

### Vector Operations

**Addition:** u + v = [u₁+v₁, u₂+v₂, ...]

**Scalar Multiplication:** c·v = [c·v₁, c·v₂, ...]

**Zero Vector:** 0 = [0, 0, ...]

### Quantum State as Vector

A **qubit state** is a 2D complex vector:

```
|ψ⟩ = α|0⟩ + β|1⟩ where |0⟩ = [1]  and  |1⟩ = [0]
                              [0]            [1]
```

**Normalization:** |α|² + |β|² = 1 (Born rule)

**Example in Code:**
```fsharp
// |+⟩ state = (|0⟩ + |1⟩)/√2
let plusState = 
    [| Complex(1.0 / sqrt(2.0), 0.0)
       Complex(1.0 / sqrt(2.0), 0.0) |]

// Verify normalization
let norm = plusState |> Array.sumBy (fun c -> c.Magnitude ** 2.0)
// norm = 1.0 ✓
```

---

## Dirac Notation

### Ket: |ψ⟩ (Column Vector)

Represents a quantum state:

```
|ψ⟩ = α|0⟩ + β|1⟩ = [α]
                     [β]
```

### Bra: ⟨ψ| (Row Vector)

The conjugate transpose of a ket:

```
⟨ψ| = (|ψ⟩)† = [α* β*]
```

### Computational Basis States

**Single Qubit:**
```
|0⟩ = [1]    |1⟩ = [0]
      [0]          [1]
```

**Two Qubits:**
```
|00⟩ = [1]   |01⟩ = [0]   |10⟩ = [0]   |11⟩ = [0]
       [0]          [1]          [0]          [0]
       [0]          [0]          [1]          [0]
       [0]          [0]          [0]          [1]
```

### Common Superposition States

**|+⟩ = (|0⟩ + |1⟩)/√2** (Hadamard of |0⟩)

**|−⟩ = (|0⟩ − |1⟩)/√2** (Hadamard of |1⟩)

**|i⟩ = (|0⟩ + i|1⟩)/√2** (Y-basis state)

### Example in Code:
```fsharp
// Create |+⟩ state using Dirac notation semantics
let ket0 = [| Complex.One; Complex.Zero |]
let ket1 = [| Complex.Zero; Complex.One |]

let ketPlus = 
    Array.map2 (fun a b -> (a + b) / Complex(sqrt(2.0), 0.0)) ket0 ket1
```

---

## Matrices and Linear Transformations

### Definition

A **matrix** is a rectangular array of numbers. An **m × n** matrix has m rows and n columns.

### Matrix Multiplication

**(AB)ᵢⱼ = Σₖ Aᵢₖ·Bₖⱼ**

**Note:** AB ≠ BA in general (not commutative)

### Identity Matrix

**I** leaves vectors unchanged:

```
I₂ = [1 0]    I₃ = [1 0 0]
     [0 1]         [0 1 0]
                   [0 0 1]
```

### Quantum Gates as Matrices

Quantum gates are **unitary matrices** that transform qubit states.

**Pauli X (NOT gate):**
```
X = [0 1]    X|0⟩ = |1⟩
    [1 0]    X|1⟩ = |0⟩
```

**Hadamard:**
```
H = 1/√2 [1  1]    H|0⟩ = |+⟩ = (|0⟩ + |1⟩)/√2
         [1 -1]    H|1⟩ = |−⟩ = (|0⟩ − |1⟩)/√2
```

**Pauli Z (Phase Flip):**
```
Z = [1  0]    Z|0⟩ = |0⟩
    [0 -1]    Z|1⟩ = −|1⟩
```

### Example in Code:
```fsharp
// Apply Hadamard gate
open FSharp.Azure.Quantum.LocalSimulator

let state = StateVector.init 1  // |0⟩
let afterH = Gates.applyH 0 state  // H|0⟩ = |+⟩
```

---

## Inner Products and Orthogonality

### Inner Product (Dot Product)

For complex vectors u and v:

**⟨u|v⟩ = Σᵢ uᵢ* · vᵢ**

**Properties:**
- ⟨u|v⟩* = ⟨v|u⟩ (conjugate symmetry)
- ⟨u|u⟩ ≥ 0 (positive definite)
- ⟨u|u⟩ = 0 iff u = 0

### Orthogonality

Two vectors are **orthogonal** if: **⟨u|v⟩ = 0**

**Computational basis states are orthogonal:**
```
⟨0|0⟩ = 1    ⟨0|1⟩ = 0
⟨1|0⟩ = 0    ⟨1|1⟩ = 1
```

### Norm (Length)

**||v|| = √⟨v|v⟩ = √(Σᵢ |vᵢ|²)**

A vector is **normalized** if ||v|| = 1.

### Born Rule

The probability of measuring state |ψ⟩ and obtaining outcome |i⟩:

**P(i) = |⟨i|ψ⟩|²**

**Example:**
```
|ψ⟩ = (|0⟩ + |1⟩)/√2

P(0) = |⟨0|ψ⟩|² = |1/√2|² = 1/2
P(1) = |⟨1|ψ⟩|² = |1/√2|² = 1/2
```

---

## Tensor Products

### Definition

The **tensor product** (⊗) combines two vector spaces into a larger space.

For vectors u = [u₁, u₂] and v = [v₁, v₂]:

```
u ⊗ v = [u₁·v₁]
        [u₁·v₂]
        [u₂·v₁]
        [u₂·v₂]
```

### Multi-Qubit States

Two qubits: **|ψ⟩ ⊗ |φ⟩ = |ψφ⟩**

**Example:**
```
|0⟩ ⊗ |1⟩ = |01⟩ = [1] ⊗ [0] = [0]
                     [0]   [1]   [1]
                                 [0]
                                 [0]
```

### Tensor Product of Matrices

For matrices A (m×n) and B (p×q), A ⊗ B is (mp × nq):

```
A ⊗ B = [a₁₁·B  a₁₂·B  ...]
        [a₂₁·B  a₂₂·B  ...]
        [  ...    ...    ]
```

**Example:** CNOT gate
```
CNOT = |0⟩⟨0| ⊗ I + |1⟩⟨1| ⊗ X = [1 0 0 0]
                                   [0 1 0 0]
                                   [0 0 0 1]
                                   [0 0 1 0]
```

### Entangled States

**Bell state:** |Φ⁺⟩ = (|00⟩ + |11⟩)/√2

**Cannot** be written as |ψ⟩ ⊗ |φ⟩ for any single-qubit states → **entangled**

**Example in Code:**
```fsharp
// Create Bell state: (|00⟩ + |11⟩)/√2
let state = StateVector.init 2  // |00⟩
let afterH = Gates.applyH 0 state  // (|00⟩ + |10⟩)/√2
let bellState = Gates.applyCNOT 0 1 afterH  // (|00⟩ + |11⟩)/√2
```

---

## Eigenvalues and Eigenvectors

### Definition

For matrix A, vector v is an **eigenvector** with **eigenvalue** λ if:

**A·v = λ·v**

The eigenvector's direction is unchanged by A, only scaled by λ.

### Spectral Decomposition

Any Hermitian matrix H can be written as:

**H = Σᵢ λᵢ |vᵢ⟩⟨vᵢ|**

Where λᵢ are eigenvalues and |vᵢ⟩ are eigenvectors.

### Measurement in Quantum Mechanics

Measuring observable O (Hermitian operator):
- Possible outcomes: eigenvalues of O
- Probability of outcome λᵢ: |⟨vᵢ|ψ⟩|²
- State after measurement: |vᵢ⟩ (collapsed)

**Example: Measuring in Z-basis**
```
Z = [1  0]   eigenvalues: λ₀ = +1, λ₁ = -1
    [0 -1]   eigenvectors: |0⟩, |1⟩

Measure |ψ⟩ = (|0⟩ + |1⟩)/√2:
P(+1) = |⟨0|ψ⟩|² = 1/2 → outcome +1, state collapses to |0⟩
P(-1) = |⟨1|ψ⟩|² = 1/2 → outcome -1, state collapses to |1⟩
```

---

## Unitary and Hermitian Operators

### Unitary Operators

An operator U is **unitary** if:

**U†·U = U·U† = I**

Where U† is the conjugate transpose (adjoint).

**Properties:**
- Preserves inner products: ⟨U·u|U·v⟩ = ⟨u|v⟩
- Preserves norms: ||U·v|| = ||v||
- Reversible: U⁻¹ = U†

**All quantum gates are unitary** (except measurement).

**Example:**
```
Hadamard: H† = H and H·H = I → H is unitary (and self-inverse)
```

### Hermitian Operators

An operator H is **Hermitian** (self-adjoint) if:

**H† = H**

**Properties:**
- Eigenvalues are real
- Eigenvectors are orthogonal
- Used for observables (measurable quantities)

**Example:**
```
Pauli matrices X, Y, Z are Hermitian:
X† = X, Y† = Y, Z† = Z
```

### Example in Code:
```fsharp
// All gates in this library are unitary
// Applying gate then its inverse returns to original state
let state = StateVector.init 1
let afterX = Gates.applyX 0 state  // Apply X
let back = Gates.applyX 0 afterX   // Apply X again (X⁻¹ = X)
// back = state ✓
```

---

## Hilbert Spaces

### Definition

A **Hilbert space** is a complete vector space with an inner product.

**For quantum computing:**
- Single qubit: ℂ² (2-dimensional complex Hilbert space)
- n qubits: ℂ^(2ⁿ) (2ⁿ-dimensional space)

### Basis

A **basis** is a set of linearly independent vectors that span the space.

**Computational basis** for n qubits:
```
{|0...00⟩, |0...01⟩, |0...10⟩, ..., |1...11⟩}
```

**Any state** can be written as:
```
|ψ⟩ = Σᵢ cᵢ|i⟩
```
where |i⟩ are basis states and Σᵢ|cᵢ|² = 1.

### State Space Growth

| Qubits | Dimensions | Classical Bits Equivalent |
|--------|-----------|---------------------------|
| 1      | 2         | 1 bit                     |
| 2      | 4         | 2 bits                    |
| 3      | 8         | 3 bits                    |
| 10     | 1,024     | 10 bits                   |
| 20     | 1,048,576 | 20 bits                   |
| 50     | 1.13×10¹⁵ | 50 bits                   |
| 100    | 1.27×10³⁰ | 100 bits                  |

**Exponential growth** is why quantum computers are powerful (and hard to simulate).

---

## Quick Reference: Key Formulas

### Quantum States

```
Single qubit:           |ψ⟩ = α|0⟩ + β|1⟩, |α|² + |β|² = 1
Two qubits:             |ψ⟩ = Σᵢⱼ cᵢⱼ|ij⟩, Σᵢⱼ|cᵢⱼ|² = 1
n qubits:               |ψ⟩ ∈ ℂ^(2ⁿ)
```

### Measurement (Born Rule)

```
Probability:            P(i) = |⟨i|ψ⟩|²
Expectation value:      ⟨O⟩ = ⟨ψ|O|ψ⟩ = Σᵢ λᵢ|⟨vᵢ|ψ⟩|²
```

### Gate Operations

```
Single-qubit gate:      U|ψ⟩ → |ψ'⟩
Two-qubit gate:         U(|ψ⟩ ⊗ |φ⟩) → |ψφ'⟩
Unitary condition:      U†U = I
```

### Common Gates

```
Pauli X (NOT):          X = [0 1]
                            [1 0]

Pauli Z (Phase):        Z = [1  0]
                            [0 -1]

Hadamard:               H = 1/√2 [1  1]
                                 [1 -1]

Phase shift:            Rφ = [1    0  ]
                             [0  e^(iφ)]

CNOT:                   CNOT = [1 0 0 0]
                               [0 1 0 0]
                               [0 0 0 1]
                               [0 0 1 0]
```

### Useful Identities

```
Euler's formula:        e^(iθ) = cos(θ) + i·sin(θ)
Euler's identity:       e^(iπ) = -1
Complex conjugate:      (a + bi)* = a - bi
Modulus squared:        |a + bi|² = a² + b²
Hadamard identity:      H·X·H = Z, H·Z·H = X
Pauli products:         X·Y = iZ, Y·Z = iX, Z·X = iY
```

---

## Further Reading

### Textbooks

1. **Hidary, J. D.** (2021). *Quantum Computing: An Applied Approach* (2nd ed.). Springer.
   - **Ch 11-12:** Linear algebra, complex numbers, inner products
   - **Ch 13:** Logarithms, exponentials, Euler's formula
   - **Ch 14:** Dirac notation comprehensive guide

2. **Nielsen, M. A., & Chuang, I. L.** (2010). *Quantum Computation and Quantum Information*. Cambridge.
   - **Ch 2:** Linear algebra for quantum computing

3. **Axler, S.** (2015). *Linear Algebra Done Right* (3rd ed.). Springer.
   - Rigorous treatment of vector spaces and linear transformations

### Online Resources

- [Quantum Computing Playground](https://quantum-computing.ibm.com/composer/docs/iqx/)
- [Microsoft Quantum Documentation](https://docs.microsoft.com/en-us/azure/quantum/)
- [Qiskit Textbook - Linear Algebra](https://qiskit.org/textbook/ch-appendix/linear_algebra.html)

---

## Using This Guide with FSharp.Azure.Quantum

This library implements all these mathematical concepts in F# code:

```fsharp
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.LocalSimulator
open System.Numerics

// Complex numbers (amplitudes)
let alpha = Complex(1.0 / sqrt(2.0), 0.0)

// Vectors (quantum states)
let state = StateVector.init 2  // 2-qubit state |00⟩

// Matrices (quantum gates)
let afterH = Gates.applyH 0 state  // Apply Hadamard

// Inner products (measurement probabilities)
let prob0 = Measurement.getProbability state 0 false  // P(qubit 0 = |0⟩)

// Tensor products (multi-qubit operations)
let entangled = Gates.applyCNOT 0 1 afterH  // Create entanglement

// Unitary operators (all quantum gates)
// Gates are automatically unitary in this library

// Hermitian operators (observables)
// Use QaoaCircuit.ProblemHamiltonian for energy measurements
```

**Happy quantum coding! 🚀**
