# Quantum Chemistry Examples

This directory contains examples demonstrating how to use the `FSharp.Azure.Quantum.QuantumChemistry` library for molecular simulations.

## Getting Started

If you are new to this library, start here:

*   **[H2Molecule.fsx](H2Molecule.fsx)**: The master example. It demonstrates the two primary ways to use the library:
    1.  **Direct API**: Low-level control for custom experiments.
    2.  **Builder API**: High-level declarative syntax (`quantumChemistry { ... }`) for quick setups.
    *   *Includes examples for H2, H2O, and LiH.*

## Advanced Topics & Deep Dives

These examples focus on specific components of the quantum chemistry pipeline:

*   **[H2_UCCSD_VQE_Example.fsx](H2_UCCSD_VQE_Example.fsx)**: A detailed walkthrough of the Variational Quantum Eigensolver (VQE) algorithm for the Hydrogen molecule. It explicitly shows the Pauli terms, optimization steps, and energy convergence.
*   **[UCCSDExample.fsx](UCCSDExample.fsx)**: Focuses specifically on the **Unitary Coupled Cluster Singles and Doubles (UCCSD)** ansatz. It demonstrates how fermionic operators are generated and mapped to qubit operators using the Jordan-Wigner transformation.
*   **[HartreeFockInitialStateExample.fsx](HartreeFockInitialStateExample.fsx)**: Demonstrates how to prepare the **Hartree-Fock** initial state, which is the standard starting point for many quantum chemistry algorithms to accelerate convergence.

## Applications

*   **[HamiltonianTimeEvolution.fsx](HamiltonianTimeEvolution.fsx)**: Demonstrates how to simulate the time evolution of a molecular Hamiltonian ($e^{-iHt}$) using **Trotter-Suzuki decomposition**, which is useful for studying dynamics and reaction pathways.

## Theoretical Background

For a deeper understanding of the quantum chemistry algorithms used in this library, we recommend:

### Recommended Reading

*   **"Learn Quantum Computing with Python and Q#"** by Sarah Kaiser and Christopher Granade (Manning, 2021)
    - **Chapter 10: Solving chemistry problems with quantum computers** - Excellent introduction to:
      - Hamiltonians and energy eigenvalues
      - Trotter-Suzuki decomposition (with intuitive "walking through Phoenix" analogy)
      - Phase estimation for energy calculation
      - Complete H₂ molecule simulation walkthrough
    - ISBN: 978-1-61729-613-0
    - https://www.manning.com/books/learn-quantum-computing-with-python-and-q-sharp

### VQE vs Phase Estimation

This library uses **VQE (Variational Quantum Eigensolver)** as its primary algorithm, while the Kaiser/Granade book demonstrates **Phase Estimation**. Both approaches find ground state energies:

| This Library (VQE) | Book Chapter 10 (Phase Estimation) |
|-------------------|-----------------------------------|
| Hybrid quantum-classical | Pure quantum algorithm |
| Shallow circuits (NISQ-friendly) | Deep circuits (fault-tolerant) |
| Works on today's hardware | Requires future hardware |

See `../DrugDiscovery/_data/PHARMA_GLOSSARY.md` → "VQE vs Phase Estimation" for detailed comparison.

## Biologically Relevant Examples

*   **[ElectronTransportChain.fsx](ElectronTransportChain.fsx)** 🧬 NEW: Simulates **electron transfer in cytochromes** using VQE for Fe2+/Fe3+ redox chemistry. Demonstrates:
    - Marcus theory for electron transfer rates
    - Quantum tunneling distance dependence in proteins
    - Respiratory chain redox potential ladder (NADH → O2)
    - Drug discovery implications (mitochondrial targets, antimicrobials)
    - **Biochemistry Reference:** Harper's Ch.12-13 (Biologic Oxidation, Respiratory Chain)
    - **Quantum Advantage:** ✅ True quantum advantage for d-orbital correlation and spin-state energetics
