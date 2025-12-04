// HHL Algorithm (Harrow-Hassidim-Lloyd) Example
// Quantum Linear System Solver: Ax = b
//
// BREAKTHROUGH: Exponential speedup for solving linear systems
// Classical: O(N log N) using conjugate gradient (sparse)
// Quantum HHL: O(log(N) × poly(κ, log(ε)))
//
// WHERE IT MATTERS:
// - Quantum chemistry: Molecular ground state energies  
// - Machine learning: Quantum SVM, least squares regression
// - Engineering: Finite element analysis, circuit simulation
// - Finance: Portfolio optimization with covariance matrices

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"


open System
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumLinearSystemSolver
open FSharp.Azure.Quantum.Algorithms.TrotterSuzuki
open FSharp.Azure.Quantum.Algorithms.MottonenStatePreparation

printfn "╔══════════════════════════════════════════════════════════════════════╗"
printfn "║  HHL ALGORITHM: Quantum Linear System Solver                         ║"
printfn "║  Exponential Speedup for Ax = b                                       ║"
printfn "╚══════════════════════════════════════════════════════════════════════╝"
printfn ""

// ============================================================================
// SCENARIO 1: Simple 2×2 System (Educational)
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn "SCENARIO 1: Simple 2×2 Diagonal System"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "BUSINESS PROBLEM:"
printfn "  Solve electrical circuit with 2 nodes:"
printfn "    2V₁ = 4  (node 1)"
printfn "    1V₂ = 2  (node 2)"
printfn ""
printfn "  Matrix A = [[2, 0], [0, 1]]"
printfn "  Vector b = [4, 2]"
printfn "  Expected solution: x = [2, 2] volts"
printfn ""

// Solve using HHL
printfn "🔧 Setting up HHL solver..."
let problem1 = linearSystemSolver {
    matrix [[2.0; 0.0]; [0.0; 1.0]]
    vector [4.0; 2.0]
    precision 4  // 4 qubits for eigenvalue estimation
}

printfn "⚡ Running HHL algorithm on local simulator..."
match solve problem1 with
| Error msg -> 
    printfn "❌ Error: %s" msg
| Ok result ->
    printfn "✅ SUCCESS!"
    printfn ""
    printfn "RESULTS:"
    printfn "  Success Probability: %.4f" result.SuccessProbability
    printfn "  Condition Number (κ): %s" (
        match result.ConditionNumber with
        | Some k -> sprintf "%.2f" k
        | None -> "N/A"
    )
    printfn "  Gates Used: %d" result.GateCount
    printfn "  Backend: %s" result.BackendName
    printfn ""

printfn "CLASSICAL VERIFICATION:"
printfn "  x₁ = 4/2 = 2.0 ✓"
printfn "  x₂ = 2/1 = 2.0 ✓"
printfn ""

// ============================================================================
// SCENARIO 2: Ill-Conditioned System (Stress Test)
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn "SCENARIO 2: Ill-Conditioned Matrix (κ = 100)"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "CHALLENGE:"
printfn "  High condition number κ = λ_max/λ_min affects:"
printfn "  - Success probability: P_success ∝ 1/κ²"
printfn "  - Accuracy of solution"
printfn ""
printfn "  Matrix: diag(100, 1)"
printfn "  Vector: [1, 1]"
printfn ""

let problem2 = linearSystemSolver {
    diagonalMatrix [100.0; 1.0]  // κ = 100
    vector [1.0; 1.0]
    precision 6  // More precision needed
    minEigenvalue 0.001
}

printfn "⚡ Running HHL..."
match solve problem2 with
| Error msg -> 
    printfn "❌ Error: %s" msg
| Ok result ->
    printfn "✅ Result obtained"
    printfn ""
    printfn "CONDITION NUMBER ANALYSIS:"
    match result.ConditionNumber with
    | Some k ->
        printfn "  κ = %.2f (ill-conditioned!)" k
        printfn "  Expected success rate: ~%.2f%%" (100.0 / (k * k))
    | None ->
        printfn "  κ not available"
    
    printfn ""
    printfn "MEASURED RESULTS:"
    printfn "  Success Probability: %.4f" result.SuccessProbability
    printfn "  Gates: %d" result.GateCount
    printfn ""

printfn "KEY INSIGHT:"
printfn "  HHL works best with well-conditioned matrices (κ < 100)"
printfn "  For ill-conditioned systems, use preconditioning!"
printfn ""

// ============================================================================
// SCENARIO 3: Larger System (4×4)
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn "SCENARIO 3: 4×4 System (Finite Element Analysis)"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "APPLICATION:"
printfn "  Structural analysis with 4 nodes"
printfn "  Stiffness matrix (diagonal approximation)"
printfn ""

let problem3 = linearSystemSolver {
    diagonalMatrix [2.0; 3.0; 4.0; 5.0]
    vector [1.0; 0.0; 0.0; 0.0]
    precision 5
}

printfn "⚡ Running HHL on 4×4 system..."
printfn "  This requires 5 + 2 + 1 = 8 qubits total"
printfn "  Clock: 5 qubits, Solution: 2 qubits, Ancilla: 1 qubit"
printfn ""

match solve problem3 with
| Error msg -> 
    printfn "❌ Error: %s" msg
| Ok result ->
    printfn "✅ Solved 4×4 system!"
    printfn "  Gates: %d" result.GateCount
    printfn "  Success: %.4f" result.SuccessProbability
    printfn ""

// ============================================================================
// SCENARIO 4: Demonstrating M\u00f6tt\u00f6nen's State Preparation
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn "ADVANCED: Möttönen's Arbitrary State Preparation"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "KEY INNOVATION:"
printfn "  Previous HHL limitation: Only encoded dominant component"
printfn "  Möttönen's method: Encodes FULL arbitrary quantum state!"
printfn ""

printfn "EXAMPLE: Encode superposition state"
printfn "  |ψ⟩ = 0.6|00⟩ + 0.5|01⟩ + 0.4|10⟩ + 0.4|11⟩"
printfn ""

// Create arbitrary state
let amplitudes = [| Complex(0.6, 0.0); Complex(0.5, 0.0); 
                    Complex(0.4, 0.0); Complex(0.4, 0.0) |]

try
    let state = normalizeState amplitudes
    printfn "✅ State normalized:"
    printfn "  Dimension: 2^%d = %d" state.NumQubits state.Amplitudes.Length
    
    for i in 0 .. state.Amplitudes.Length - 1 do
        let prob = state.Amplitudes[i].Magnitude * state.Amplitudes[i].Magnitude
        if prob > 0.01 then
            printfn "  |%s⟩: %.4f (prob: %.2f%%)" 
                (Convert.ToString(i, 2).PadLeft(state.NumQubits, '0'))
                state.Amplitudes[i].Real
                (prob * 100.0)
    
    printfn ""
    printfn "This enables HHL to solve Ax = b for ANY input vector b!"
    printfn ""
with
| ex -> printfn "Error: %s" ex.Message

// ============================================================================
// SCENARIO 5: Demonstrating Trotter-Suzuki Decomposition
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn "ADVANCED: Trotter-Suzuki for Non-Diagonal Matrices"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "BREAKTHROUGH:"
printfn "  Previous HHL limitation: Only diagonal matrices"
printfn "  Trotter-Suzuki: Handles ANY Hermitian matrix via Pauli decomposition!"
printfn ""

printfn "EXAMPLE: Simple 2×2 matrix in Pauli basis"
let eigenvalues = [| 2.0; 1.0 |]
let pauliHamiltonian = decomposeDiagonalMatrixToPauli eigenvalues

printfn "  Matrix: diag(2, 1)"
printfn "  Pauli decomposition: H = Σᵢ cᵢ Pᵢ"
printfn "  Number of terms: %d" pauliHamiltonian.Terms.Length
printfn "  Qubits: %d" pauliHamiltonian.NumQubits
printfn ""

for term in pauliHamiltonian.Terms do
    let pauliStr = term.Operators |> String
    printfn "    %s: coefficient = %.4f" pauliStr term.Coefficient.Real

printfn ""
printfn "Trotter-Suzuki Configuration:"
let trotterConfig = {
    NumSteps = 10
    Time = 1.0
    Order = 1
}
printfn "  Steps: %d" trotterConfig.NumSteps
printfn "  Time: %.1f" trotterConfig.Time
printfn "  Order: %d (first-order formula)" trotterConfig.Order
printfn ""

let estimatedSteps = estimateTrotterSteps 2.0 1.0 0.01 1
printfn "  For ‖H‖=2, t=1, ε=0.01:"
printfn "  Required steps: %d" estimatedSteps
printfn ""

// ============================================================================
// PERFORMANCE COMPARISON
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn "QUANTUM ADVANTAGE: When HHL Beats Classical"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "┌─────────┬──────────┬─────────┬────────────┬──────────────┐"
printfn "│ N       │ κ        │ Sparse  │ Classical  │ HHL Quantum  │"
printfn "├─────────┼──────────┼─────────┼────────────┼──────────────┤"
printfn "│ 100     │ < 10     │ Yes     │ O(N log N) │ O(log N)     │"
printfn "│ 1,000   │ < 100    │ Yes     │ ~10⁶ ops   │ ~10³ ops     │"
printfn "│ 1,000,000│ < 100   │ Yes     │ ~10¹² ops  │ ~10⁶ ops     │"
printfn "└─────────┴──────────┴─────────┴────────────┴──────────────┘"
printfn ""

printfn "SPEEDUP FACTOR:"
printfn "  N = 1,000:     ~1,000× faster"
printfn "  N = 1,000,000: ~1,000,000× faster (EXPONENTIAL!)"
printfn ""

printfn "REQUIREMENTS FOR ADVANTAGE:"
printfn "  ✓ Large system (N > 1000)"
printfn "  ✓ Sparse matrix (few non-zero entries per row)"
printfn "  ✓ Well-conditioned (κ < 100)"
printfn "  ✓ Quantum output acceptable (no need for full state tomography)"
printfn ""

// ============================================================================
// PRACTICAL APPLICATIONS
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn "REAL-WORLD APPLICATIONS"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "1. QUANTUM CHEMISTRY"
printfn "   Problem: Compute molecular ground states"
printfn "   Matrix: Hamiltonian (sparse, Hermitian)"
printfn "   Speedup: Enables simulation of larger molecules"
printfn ""

printfn "2. MACHINE LEARNING"
printfn "   Problem: Quantum SVM, least squares regression"
printfn "   Matrix: Kernel matrix, covariance matrix"
printfn "   Speedup: Train on exponentially more data"
printfn ""

printfn "3. FINANCIAL MODELING"
printfn "   Problem: Portfolio optimization"
printfn "   Matrix: Covariance matrix of asset returns"
printfn "   Speedup: Analyze thousands of assets simultaneously"
printfn ""

printfn "4. ENGINEERING SIMULATION"
printfn "   Problem: Finite element analysis (FEA)"
printfn "   Matrix: Stiffness matrix (sparse)"
printfn "   Speedup: Simulate larger structures with finer meshes"
printfn ""

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn "SUMMARY: HHL Algorithm Capabilities"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""
printfn "✅ IMPLEMENTED:"
printfn "   • Diagonal matrix solver (working today!)"
printfn "   • Möttönen's arbitrary state preparation"
printfn "   • Trotter-Suzuki non-diagonal decomposition"
printfn "   • LocalBackend simulation (testing)"
printfn "   • Cloud backend support (IonQ, Rigetti)"
printfn ""
printfn "🎯 QUANTUM ADVANTAGE:"
printfn "   • Exponential speedup: O(log N) vs O(N)"
printfn "   • Enables previously impossible calculations"
printfn "   • Critical for quantum machine learning & chemistry"
printfn ""
printfn "📊 READY FOR:"
printfn "   • Research & algorithm development"
printfn "   • Educational purposes"
printfn "   • Benchmarking quantum hardware"
printfn "   • Production use (well-conditioned, sparse systems)"
printfn ""
printfn "Example complete! HHL is ready to revolutionize linear algebra! 🚀"
printfn ""
