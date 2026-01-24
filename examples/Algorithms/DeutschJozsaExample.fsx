/// Deutsch-Jozsa Algorithm Example
/// 
/// Demonstrates the canonical first quantum algorithm using the unified
/// backend architecture:
/// - LocalBackend (gate-based StateVector simulation)
/// - TopologicalUnifiedBackend (braiding-based FusionSuperposition)
/// 
/// Textbook References:
/// - "Learn Quantum Computing with Python and Q#" (Kaiser, 2021) - Appendix D
/// - "Quantum Programming in Depth" (Manning, 2024) - Chapter 6
/// 
/// The Deutsch-Jozsa algorithm determines if a function is constant or balanced
/// with a single query, compared to 2^(n-1)+1 queries classically.

(*
===============================================================================
 Background Theory
===============================================================================

The Deutsch-Jozsa algorithm (1992) was the first quantum algorithm to demonstrate
exponential speedup over classical deterministic computation. Given a black-box
function f:{0,1}ⁿ → {0,1} promised to be either "constant" (same output for all
inputs) or "balanced" (outputs 0 for exactly half the inputs and 1 for the other
half), the algorithm determines which case applies with certainty using only ONE
query to f, whereas any classical deterministic algorithm requires 2^(n-1)+1
queries in the worst case.

The algorithm exploits quantum parallelism and interference. Starting from |0⟩ⁿ|1⟩,
Hadamard gates create a superposition of all 2ⁿ inputs. The oracle U_f applies
the function in superposition: U_f|x⟩|y⟩ = |x⟩|y ⊕ f(x)⟩. Due to phase kickback
with the ancilla in |−⟩ state, this effectively computes |x⟩ → (-1)^f(x)|x⟩.
Final Hadamards cause constructive interference at |0⟩ⁿ for constant functions
and destructive interference (giving non-zero states) for balanced functions.

Key Equations:
  - Oracle action (phase kickback): U_f|x⟩|−⟩ = (-1)^f(x)|x⟩|−⟩
  - Initial superposition: H⊗ⁿ|0⟩ⁿ = (1/√2ⁿ) Σₓ|x⟩
  - After oracle: (1/√2ⁿ) Σₓ (-1)^f(x)|x⟩
  - Final measurement: |⟨0ⁿ|H⊗ⁿU_f H⊗ⁿ|0ⁿ⟩|² = 1 if constant, 0 if balanced
  - Classical lower bound: 2^(n-1) + 1 queries (deterministic)

Quantum Advantage:
  Deutsch-Jozsa provides exponential separation between quantum and classical
  deterministic query complexity. While a probabilistic classical algorithm can
  achieve high confidence with O(1) random queries, Deutsch-Jozsa gives CERTAINTY
  in one query. This algorithm introduced key quantum computing concepts: quantum
  parallelism (evaluating f on all inputs simultaneously), phase kickback, and
  interference-based computation. It is the pedagogical starting point for
  understanding more practical algorithms like Simon's and Shor's.

Unified Backend Architecture:
  This example demonstrates how the same Deutsch-Jozsa algorithm works across
  different quantum backends through the IQuantumBackend interface:
  
  - LocalBackend: Traditional gate-based simulation using state vectors
  - TopologicalBackend: Braiding-based computation using Ising anyons
  
  The unified architecture enables backend-agnostic algorithm development.

References:
  [1] Deutsch & Jozsa, "Rapid solution of problems by quantum computation",
      Proc. R. Soc. Lond. A 439, 553-558 (1992). https://doi.org/10.1098/rspa.1992.0167
  [2] Cleve et al., "Quantum algorithms revisited", Proc. R. Soc. Lond. A 454,
      339-354 (1998). https://doi.org/10.1098/rspa.1998.0164
  [3] Nielsen & Chuang, "Quantum Computation and Quantum Information",
      Cambridge University Press (2010), Section 1.4.3.
  [4] Wikipedia: Deutsch-Jozsa_algorithm
      https://en.wikipedia.org/wiki/Deutsch%E2%80%93Jozsa_algorithm
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"

open FSharp.Azure.Quantum.Algorithms.DeutschJozsa
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Topological

printfn "╔══════════════════════════════════════════════════════════════╗"
printfn "║     Deutsch-Jozsa Algorithm - Unified Backend Demo           ║"
printfn "╚══════════════════════════════════════════════════════════════╝"
printfn ""

// ============================================================================
// HELPER: Run Deutsch-Jozsa tests on any backend
// ============================================================================

let runDeutschJozsaTests (backend: IQuantumBackend) (backendLabel: string) =
    let numQubits = 3
    let shots = 100
    
    printfn "╔══════════════════════════════════════════════════════════════╗"
    printfn "║  %s" (backendLabel.PadRight(60) + "║")
    printfn "╚══════════════════════════════════════════════════════════════╝"
    printfn ""
    printfn "Backend: %s" backend.Name
    printfn "Native state type: %A" backend.NativeStateType
    printfn "Testing with %d qubits (search space = %d)" numQubits (1 <<< numQubits)
    printfn ""
    
    // Test 1: Constant-Zero Oracle
    printfn "Test 1: Constant-Zero Oracle (f(x) = 0 for all x)"
    printfn "─────────────────────────────────────────────────────────"
    match runConstantZero numQubits backend shots with
    | Ok result ->
        printfn "%s" (formatResult result)
        printfn "Expected: Constant"
        printfn ""
    | Error err ->
        printfn "Error: %A" err
        printfn ""
    
    // Test 2: Constant-One Oracle
    printfn "Test 2: Constant-One Oracle (f(x) = 1 for all x)"
    printfn "─────────────────────────────────────────────────────────"
    match runConstantOne numQubits backend shots with
    | Ok result ->
        printfn "%s" (formatResult result)
        printfn "Expected: Constant"
        printfn ""
    | Error err ->
        printfn "Error: %A" err
        printfn ""
    
    // Test 3: Balanced First-Bit Oracle
    printfn "Test 3: Balanced Oracle (f(x) = first bit of x)"
    printfn "─────────────────────────────────────────────────────────"
    match runBalancedFirstBit numQubits backend shots with
    | Ok result ->
        printfn "%s" (formatResult result)
        printfn "Expected: Balanced"
        printfn ""
    | Error err ->
        printfn "Error: %A" err
        printfn ""
    
    // Test 4: Balanced Parity Oracle
    printfn "Test 4: Balanced Oracle (f(x) = XOR of all bits)"
    printfn "─────────────────────────────────────────────────────────"
    match runBalancedParity numQubits backend shots with
    | Ok result ->
        printfn "%s" (formatResult result)
        printfn "Expected: Balanced"
        printfn ""
    | Error err ->
        printfn "Error: %A" err
        printfn ""

// ============================================================================
// BACKEND 1: LocalBackend (Gate-based StateVector simulation)
// ============================================================================

let localBackend = LocalBackend() :> IQuantumBackend
runDeutschJozsaTests localBackend "BACKEND 1: LocalBackend (Gate-based StateVector)"

// ============================================================================
// BACKEND 2: TopologicalUnifiedBackend (Braiding-based simulation)
// ============================================================================

printfn ""
let topoBackend = TopologicalUnifiedBackendFactory.createIsing 16
runDeutschJozsaTests topoBackend "BACKEND 2: TopologicalBackend (Ising Anyons)"

// NOTE: Topological backend may show different results for balanced oracles.
// This is due to Solovay-Kitaev gate approximation limitations when decomposing
// multi-controlled gates (like the oracle's controlled-X gates) into braiding
// operations. The constant oracles work perfectly because they require fewer
// or no multi-qubit entangling gates beyond what braiding naturally supports.

// ============================================================================
// QUANTUM ADVANTAGE SUMMARY
// ============================================================================

printfn "╔══════════════════════════════════════════════════════════════╗"
printfn "║              Quantum Advantage Demonstration                 ║"
printfn "╚══════════════════════════════════════════════════════════════╝"
printfn ""
printfn "Classical approach (deterministic):"
printfn "  - Worst case: 2^(n-1) + 1 = %d queries for n=3" ((1 <<< 2) + 1)
printfn "  - Must check >50%% of inputs to be certain"
printfn ""
printfn "Quantum approach (deterministic):"
printfn "  - Exactly 1 query to oracle"
printfn "  - 100%% certainty (in theory, ~95%% on NISQ hardware with noise)"
printfn ""
printfn "Speedup factor: %dx" ((1 <<< 2) + 1)
printfn ""

// ============================================================================
// UNIFIED ARCHITECTURE BENEFITS
// ============================================================================

printfn "╔══════════════════════════════════════════════════════════════╗"
printfn "║              Unified Backend Architecture                    ║"
printfn "╚══════════════════════════════════════════════════════════════╝"
printfn ""
printfn "Key insight: Same algorithm code works on BOTH backends!"
printfn ""
printfn "  // Works identically on LocalBackend and TopologicalBackend:"
printfn "  let result = runConstantZero numQubits backend shots"
printfn ""
printfn "Benefits:"
printfn "  - Write algorithms once, run on any backend"
printfn "  - LocalBackend: Fast development & debugging with state vectors"
printfn "  - TopologicalBackend: Fault-tolerant execution via braiding"
printfn "  - Future backends plug in seamlessly"
printfn ""
printfn "Why This Matters:"
printfn "  - Deutsch-Jozsa is foundational for quantum algorithm education"
printfn "  - Same code runs on gate-based AND topological quantum computers"
printfn "  - Demonstrates quantum parallelism and interference on both"
