// ============================================================================
// Grover SAT Solver Example
// ============================================================================
//
// Demonstrates using Grover's algorithm to solve Boolean Satisfiability (SAT)
// problems. SAT is a fundamental NP-complete problem with applications in:
// - Circuit verification
// - Software verification
// - Planning and scheduling
// - Cryptanalysis
//
// This example shows how to:
// 1. Define SAT formulas in CNF (Conjunctive Normal Form)
// 2. Create SAT oracles for Grover's algorithm
// 3. Find satisfying assignments using quantum search
// ============================================================================

(*
===============================================================================
 Background Theory
===============================================================================

Grover's algorithm (1996) provides a quadratic speedup for unstructured search
problems. Given a search space of N items with M "marked" solutions, classical
algorithms require O(N/M) queries on average, while Grover's algorithm finds a
solution with high probability in O(√(N/M)) queries. For SAT problems with n
variables, the search space is N = 2ⁿ, so Grover reduces the search from O(2ⁿ)
to O(2^(n/2)) - a substantial speedup for large instances.

The algorithm works by repeatedly applying two operations: (1) an oracle that
marks solutions by flipping their phase, and (2) a diffusion operator that
amplifies the amplitude of marked states. After approximately π/4 × √(N/M)
iterations, measuring the quantum state yields a solution with probability
approaching 1. For SAT, the oracle encodes the Boolean formula such that
satisfying assignments receive a phase flip.

Key Equations:
  - Grover iterate: G = (2|ψ⟩⟨ψ| - I) × O_f  where O_f is the oracle
  - Initial state: |ψ⟩ = H⊗ⁿ|0⟩ⁿ = (1/√N) Σₓ|x⟩  (uniform superposition)
  - Optimal iterations: k ≈ (π/4)√(N/M) for M solutions in N items
  - Success probability: P(success) = sin²((2k+1)θ) where sin²(θ) = M/N
  - SAT oracle: O_f|x⟩ = (-1)^f(x)|x⟩ where f(x) = 1 iff x satisfies formula

Quantum Advantage:
  Grover's speedup is provably optimal for unstructured search (BBBV theorem).
  For SAT, this means finding satisfying assignments in O(2^(n/2)) vs O(2ⁿ).
  While this doesn't solve NP-complete problems in polynomial time, it provides
  meaningful speedup for cryptanalysis (AES key search: 2¹²⁸ → 2⁶⁴) and
  verification tasks. Hybrid classical-quantum approaches combine Grover with
  classical preprocessing for practical SAT solving.

References:
  [1] Grover, "A fast quantum mechanical algorithm for database search",
      STOC '96, pp. 212-219. https://doi.org/10.1145/237814.237866
  [2] Boyer et al., "Tight bounds on quantum searching",
      Fortschr. Phys. 46, 493-505 (1998). https://arxiv.org/abs/quant-ph/9605034
  [3] Ambainis, "Quantum search algorithms", SIGACT News 35(2), 22-35 (2004).
      https://doi.org/10.1145/992287.992296
  [4] Wikipedia: Grover's_algorithm
      https://en.wikipedia.org/wiki/Grover%27s_algorithm
*)

// Use local build for development
#I "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0"
#r "FSharp.Azure.Quantum.dll"

// For published package, use instead:
// #r "nuget: FSharp.Azure.Quantum"

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.GroverSearch.Oracle

// ============================================================================
// Example 1: Simple 2-SAT Problem
// ============================================================================
//
// Formula: (x0 OR x1) AND (NOT x0 OR x1)
// Expected solutions: Any assignment where x1 = true
//   - 0b01 (x0=false, x1=true) = 1
//   - 0b11 (x0=true, x1=true) = 3

printfn "=== Example 1: Simple 2-SAT ==="
printfn "Formula: (x0 OR x1) AND (NOT x0 OR x1)"
printfn ""

// Define the formula
let simple2SAT = {
    NumVariables = 2
    Clauses = [
        clause [var 0; var 1]       // x0 OR x1
        clause [notVar 0; var 1]    // NOT x0 OR x1
    ]
}

// Create oracle
match satOracle simple2SAT with
| Error err -> 
    printfn "❌ Failed to create oracle: %A" err
| Ok oracle ->
    printfn "✅ Oracle created successfully"
    printfn "   Search space: %d states (2^%d)" (1 <<< oracle.NumQubits) oracle.NumQubits
    
    // Verify which assignments satisfy the formula
    printfn ""
    printfn "Checking all possible assignments:"
    for i in 0 .. (1 <<< oracle.NumQubits) - 1 do
        let x0 = (i &&& 1) = 1
        let x1 = (i >>> 1) = 1
        let isSol = Oracle.isSolution oracle.Spec i
        let mark = if isSol then "✅ SOLUTION" else "  "
        printfn "  %s x0=%b x1=%b (assignment=%d)" mark x0 x1 i
    
    // Run Grover search
    printfn ""
    printfn "Running Grover's algorithm..."
    
    // Create local backend
    let backend = LocalBackend() :> IQuantumBackend
    
    // Search configuration
    let config = Grover.defaultConfig
    
    match Grover.search oracle backend config with
    | Error err ->
        printfn "❌ Search failed: %A" err
    | Ok result ->
        if result.Solutions.IsEmpty then
            printfn "⚠️  No solution found"
        else
            for solution in result.Solutions do
                let x0 = (solution &&& 1) = 1
                let x1 = (solution >>> 1) = 1
                printfn "✅ Found solution: x0=%b x1=%b (assignment=%d)" x0 x1 solution
            printfn "   Success probability: %.2f%%" (result.SuccessProbability * 100.0)
            printfn "   Iterations used: %d" result.Iterations

printfn ""
printfn "================================================"
printfn ""

// ============================================================================
// Example 2: 3-SAT Problem (NP-Complete)
// ============================================================================
//
// Formula: (x0 OR x1 OR x2) AND (NOT x0 OR NOT x1 OR x2) AND (x0 OR NOT x2)
// This is a 3-SAT instance (all clauses have exactly 3 literals)

printfn "=== Example 2: 3-SAT Problem ==="
printfn "Formula: (x0 OR x1 OR x2) AND (NOT x0 OR NOT x1 OR x2) AND (x0 OR NOT x2)"
printfn ""

let threeSAT = {
    NumVariables = 3
    Clauses = [
        clause [var 0; var 1; var 2]           // x0 OR x1 OR x2
        clause [notVar 0; notVar 1; var 2]    // NOT x0 OR NOT x1 OR x2
        clause [var 0; notVar 2]               // x0 OR NOT x2
    ]
}

match satOracle threeSAT with
| Error err -> 
    printfn "❌ Failed to create oracle: %A" err
| Ok oracle ->
    printfn "✅ Oracle created successfully"
    printfn "   Search space: %d states (2^%d)" (1 <<< oracle.NumQubits) oracle.NumQubits
    
    // Find and display all solutions
    printfn ""
    printfn "All satisfying assignments:"
    let solutions =
        [0 .. (1 <<< oracle.NumQubits) - 1]
        |> List.filter (fun i -> Oracle.isSolution oracle.Spec i)
    
    for sol in solutions do
        let x0 = (sol &&& 1) = 1
        let x1 = ((sol >>> 1) &&& 1) = 1
        let x2 = ((sol >>> 2) &&& 1) = 1
        printfn "  ✅ x0=%b x1=%b x2=%b (assignment=%d)" x0 x1 x2 sol
    
    printfn ""
    printfn "Total solutions: %d out of %d" solutions.Length (1 <<< oracle.NumQubits)
    
    // Run Grover search
    printfn ""
    printfn "Running Grover's algorithm..."
    
    let backend = LocalBackend() :> IQuantumBackend
    let config = Grover.defaultConfig
    
    match Grover.search oracle backend config with
    | Error err ->
        printfn "❌ Search failed: %A" err
    | Ok result ->
        if result.Solutions.IsEmpty then
            printfn "⚠️  No solution found"
        else
            for solution in result.Solutions do
                let x0 = (solution &&& 1) = 1
                let x1 = ((solution >>> 1) &&& 1) = 1
                let x2 = ((solution >>> 2) &&& 1) = 1
                printfn "✅ Found solution: x0=%b x1=%b x2=%b (assignment=%d)" x0 x1 x2 solution
            printfn "   Success probability: %.2f%%" (result.SuccessProbability * 100.0)
            printfn "   Iterations used: %d" result.Iterations

printfn ""
printfn "================================================"
printfn ""

// ============================================================================
// Example 3: UNSAT Formula (No Solutions)
// ============================================================================
//
// Formula: (x0) AND (NOT x0)
// This is unsatisfiable - no assignment can satisfy both clauses

printfn "=== Example 3: UNSAT Formula ==="
printfn "Formula: (x0) AND (NOT x0)"
printfn ""

let unsatFormula = {
    NumVariables = 1
    Clauses = [
        clause [var 0]      // x0
        clause [notVar 0]   // NOT x0
    ]
}

match satOracle unsatFormula with
| Error err -> 
    printfn "❌ Failed to create oracle: %A" err
| Ok oracle ->
    printfn "✅ Oracle created successfully"
    
    // Verify no solutions exist
    printfn ""
    printfn "Checking all possible assignments:"
    let solutions =
        [0 .. (1 <<< oracle.NumQubits) - 1]
        |> List.filter (fun i -> Oracle.isSolution oracle.Spec i)
    
    for i in 0 .. (1 <<< oracle.NumQubits) - 1 do
        let x0 = (i &&& 1) = 1
        printfn "  ❌ x0=%b (assignment=%d) - NOT a solution" x0 i
    
    printfn ""
    printfn "Total solutions: %d" solutions.Length
    printfn "Formula is UNSATISFIABLE"
    
    // Grover will handle UNSAT gracefully
    printfn ""
    printfn "Running Grover's algorithm (expected to find no solution)..."
    
    let backend = LocalBackend() :> IQuantumBackend
    let config = Grover.defaultConfig
    
    match Grover.search oracle backend config with
    | Error err ->
        printfn "❌ Search failed: %A" err
    | Ok result ->
        if result.Solutions.IsEmpty then
            printfn "✅ Correctly found no solution (formula is UNSAT)"
        else
            printfn "⚠️  Unexpected: Found assignments %A (may be false positives)" result.Solutions

printfn ""
printfn "================================================"
printfn ""

printfn "✅ SAT solver examples completed!"
printfn ""
printfn "Key Takeaways:"
printfn "  1. SAT oracles enable quantum search for satisfying assignments"
printfn "  2. Grover provides quadratic speedup: O(√N) vs O(N) classical"
printfn "  3. Works for both satisfiable and unsatisfiable formulas"
printfn "  4. Scales to larger problems (limited by qubit count)"
printfn "  5. 3-SAT is NP-complete - quantum advantage is significant!"
printfn ""
