// ==============================================================================
// Trotter–Suzuki decomposition — simulating time evolution e^(-iHt)
// ==============================================================================
// Trotter–Suzuki approximates the time-evolution operator e^(-iHt) of a Hamiltonian
// H = Σ_k h_k (a sum of Pauli strings) by a product of easy-to-implement single-Pauli
// rotations, split into n steps:  e^(-iHt) ≈ (Π_k e^(-i h_k t/n))^n.
//   • 1st order error  ~ O(t²/n);  2nd order (symmetrized) ~ O(t³/n²).
// It is the workhorse for Hamiltonian simulation and the controlled-evolution inside
// quantum phase estimation.
//
// Run:  dotnet fsi examples/Algorithms/TrotterSuzukiExample.fsx
// ==============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System.Numerics
open FSharp.Azure.Quantum                       // CircuitBuilder
open FSharp.Azure.Quantum.Algorithms.TrotterSuzuki

// --- 1. Build a Pauli Hamiltonian from a diagonal (eigenvalues on Z-basis) ---
let hamiltonian = decomposeDiagonalMatrixToPauli [| 0.5; -0.5 |]
printfn "Hamiltonian: %d Pauli term(s), %d qubit(s)" hamiltonian.Terms.Length hamiltonian.NumQubits

// --- 2. Synthesize the time-evolution circuit e^(-iHt) ---
// Use 2nd-order Trotter with 5 steps over t = 1.0.
let config = { defaultConfig with NumSteps = 5; Time = 1.0; Order = 2 }
let qubits = [| 0 .. hamiltonian.NumQubits - 1 |]
let circuit =
    synthesizeHamiltonianEvolution hamiltonian config qubits (CircuitBuilder.empty hamiltonian.NumQubits)
printfn "Evolution circuit (%d steps, order %d): %d gates" config.NumSteps config.Order circuit.Gates.Length

// --- 3. Pick a Trotter step count for a target accuracy ---
let steps = estimateTrotterSteps 2.0 1.0 1e-3 2   // ‖H‖≈2, t=1, tol=1e-3, 2nd order
printfn "Recommended Trotter steps (‖H‖=2, t=1, tol=1e-3, order 2): %d" steps

// --- 4. Decompose an arbitrary Hermitian matrix into Pauli terms ---
// Here the Pauli-Z matrix diag(1, -1).
let m = array2D [ [ Complex(1.0, 0.0); Complex(0.0, 0.0) ]
                  [ Complex(0.0, 0.0); Complex(-1.0, 0.0) ] ]
match decomposeMatrixToPauli m with
| Ok h -> printfn "Decomposed 2x2 Hermitian matrix into %d Pauli term(s)" h.Terms.Length
| Error e -> printfn "Decomposition error: %A" e
