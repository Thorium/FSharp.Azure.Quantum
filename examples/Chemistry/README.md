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

*   **[HamiltonianTimeEvolution.fsx](HamiltonianTimeEvolution.fsx)**: Demonstrates how to simulate the time evolution of a molecular Hamiltonian ($e^{-iHt}$), which is useful for studying dynamics and reaction pathways.
