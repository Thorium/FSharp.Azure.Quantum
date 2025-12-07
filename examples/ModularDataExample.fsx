#r "../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"

open FSharp.Azure.Quantum.Topological
open System.Numerics

/// Modular Data Example - Fundamental Topological Invariants
/// 
/// Demonstrates:
/// - Computing S and T matrices (modular data)
/// - Verifying consistency relations
/// - Calculating quantum dimensions
/// - Ground state degeneracies
/// - Comparing different anyon theories

printfn "========================================="
printfn "MODULAR DATA - TOPOLOGICAL INVARIANTS"
printfn "========================================="
printfn ""

// ============================================================================
// Helper: Print complex matrix
// ============================================================================

let printMatrix (name: string) (matrix: Complex[,]) =
    let rows = Array2D.length1 matrix
    let cols = Array2D.length2 matrix
    
    printfn "%s (%dx%d):" name rows cols
    for i in 0 .. rows - 1 do
        printf "   "
        for j in 0 .. cols - 1 do
            let c = matrix.[i, j]
            if abs c.Imaginary < 1e-10 then
                printf "%7.4f  " c.Real
            else
                printf "%6.3f+%6.3fi  " c.Real c.Imaginary
        printfn ""
    printfn ""

// ============================================================================
// STEP 1: Ising Anyon Modular Data
// ============================================================================

printfn "STEP 1: Ising Anyons (Majorana Zero Modes)"
printfn "-------------------------------------------"

match ModularData.computeModularData AnyonSpecies.Ising with
| Error err ->
    printfn "❌ Failed: %s" err.Message
| Ok isingData ->
    printfn "✅ Computed modular data for Ising theory"
    printfn "   Particles: [1, σ, ψ]"
    printfn "   Central charge: c = %.2f" isingData.CentralCharge
    printfn ""
    
    // Print S-matrix
    printMatrix "S-matrix (unlinking)" isingData.SMatrix
    
    // Print T-matrix
    printMatrix "T-matrix (twist)" isingData.TMatrix
    
    // Verify consistency
    printfn "🔬 Consistency Checks:"
    let sUnitary = ModularData.verifySMatrixUnitary isingData.SMatrix
    let tDiagonal = ModularData.verifyTMatrixDiagonal isingData.TMatrix
    let stRelation = ModularData.verifyModularSTRelation isingData.SMatrix isingData.TMatrix
    
    printfn "   S is unitary (S S† = I):     %s" (if sUnitary then "✅" else "❌")
    printfn "   T is diagonal & unitary:     %s" (if tDiagonal then "✅" else "❌")
    printfn "   (ST)³ = e^(iφ) S² relation:  %s" (if stRelation then "✅" else "❌")
    printfn ""
    
    // Quantum dimensions
    match ModularData.totalQuantumDimension AnyonSpecies.Ising with
    | Ok D ->
        printfn "📊 Quantum Dimensions:"
        printfn "   d₁ (vacuum) = %.4f" (AnyonSpecies.quantumDimension AnyonSpecies.Vacuum)
        printfn "   d_σ (sigma) = %.4f (= √2)" (AnyonSpecies.quantumDimension AnyonSpecies.Sigma)
        printfn "   d_ψ (psi)   = %.4f" (AnyonSpecies.quantumDimension AnyonSpecies.Psi)
        printfn "   Total D = √(Σ dₐ²) = %.4f" D
        printfn ""
    | Error _ -> ()
    
    // Ground state degeneracies
    printfn "🌐 Ground State Degeneracies (genus g):"
    for g in 0 .. 3 do
        let dim = ModularData.groundStateDegeneracy isingData g
        printfn "   g=%d: dim = %d" g dim
    printfn ""

// ============================================================================
// STEP 2: Fibonacci Anyon Modular Data
// ============================================================================

printfn "STEP 2: Fibonacci Anyons (Universal Topological QC)"
printfn "--------------------------------------------------"

match ModularData.computeModularData AnyonSpecies.Fibonacci with
| Error err ->
    printfn "❌ Failed: %s" err.Message
| Ok fibData ->
    printfn "✅ Computed modular data for Fibonacci theory"
    printfn "   Particles: [1, τ]"
    printfn "   Central charge: c = %.4f" fibData.CentralCharge
    printfn ""
    
    // Print S-matrix
    printMatrix "S-matrix" fibData.SMatrix
    
    // Print T-matrix
    printMatrix "T-matrix" fibData.TMatrix
    
    // Verify consistency
    printfn "🔬 Consistency Checks:"
    let sUnitary = ModularData.verifySMatrixUnitary fibData.SMatrix
    let tDiagonal = ModularData.verifyTMatrixDiagonal fibData.TMatrix
    let stRelation = ModularData.verifyModularSTRelation fibData.SMatrix fibData.TMatrix
    
    printfn "   S is unitary:       %s" (if sUnitary then "✅" else "❌")
    printfn "   T is diagonal:      %s" (if tDiagonal then "✅" else "❌")
    printfn "   (ST)³ = e^(iφ) S²:  %s" (if stRelation then "✅" else "❌")
    printfn ""
    
    // Quantum dimensions with golden ratio
    let phi = (1.0 + sqrt 5.0) / 2.0
    printfn "📊 Quantum Dimensions:"
    printfn "   d₁ (vacuum) = %.4f" (AnyonSpecies.quantumDimension AnyonSpecies.Vacuum)
    printfn "   d_τ (tau)   = %.4f (= φ = golden ratio)" (AnyonSpecies.quantumDimension AnyonSpecies.Tau)
    printfn "   φ = (1+√5)/2 ≈ %.6f" phi
    
    match ModularData.totalQuantumDimension AnyonSpecies.Fibonacci with
    | Ok D ->
        printfn "   Total D = √(1 + φ²) = %.4f" D
        printfn ""
    | Error _ -> ()
    
    // Ground state degeneracies
    printfn "🌐 Ground State Degeneracies:"
    for g in 0 .. 3 do
        let dim = ModularData.groundStateDegeneracy fibData g
        printfn "   g=%d: dim = %d" g dim
    printfn ""

// ============================================================================
// STEP 3: SU(2)₃ Modular Data
// ============================================================================

printfn "STEP 3: SU(2)₃ Anyons"
printfn "---------------------"

match ModularData.computeModularData (AnyonSpecies.SU2Level 3) with
| Error err ->
    printfn "❌ Failed: %s" err.Message
| Ok su2Data ->
    printfn "✅ Computed modular data for SU(2)₃"
    printfn "   Particles: j ∈ {0, 1/2, 1, 3/2}"
    printfn "   Central charge: c = %.4f" su2Data.CentralCharge
    printfn ""
    
    // Print S-matrix
    printMatrix "S-matrix" su2Data.SMatrix
    
    // Print T-matrix (showing phases)
    printfn "T-matrix (diagonal):"
    for i in 0 .. 3 do
        let t = su2Data.TMatrix.[i, i]
        let phase = atan2 t.Imaginary t.Real
        printfn "   T[%d,%d] = e^(i·%.4f) = %.4f + %.4fi" i i phase t.Real t.Imaginary
    printfn ""
    
    // Verify consistency
    printfn "🔬 Consistency Checks:"
    let sUnitary = ModularData.verifySMatrixUnitary su2Data.SMatrix
    let tDiagonal = ModularData.verifyTMatrixDiagonal su2Data.TMatrix
    let stRelation = ModularData.verifyModularSTRelation su2Data.SMatrix su2Data.TMatrix
    
    printfn "   S is unitary:       %s" (if sUnitary then "✅" else "❌")
    printfn "   T is diagonal:      %s" (if tDiagonal then "✅" else "❌")
    printfn "   (ST)³ = e^(iφ) S²:  %s" (if stRelation then "✅" else "❌")
    printfn ""
    
    // Quantum dimensions
    printfn "📊 Quantum Dimensions:"
    match AnyonSpecies.particles (AnyonSpecies.SU2Level 3) with
    | Ok particles ->
        particles |> List.iteri (fun i p ->
            let d = AnyonSpecies.quantumDimension p
            printfn "   d[j=%d/2] = %.4f" i d)
    | Error _ -> ()
    
    match ModularData.totalQuantumDimension (AnyonSpecies.SU2Level 3) with
    | Ok D ->
        printfn "   Total D = %.4f" D
        printfn ""
    | Error _ -> ()
    
    // Ground state degeneracies
    printfn "🌐 Ground State Degeneracies:"
    for g in 0 .. 2 do
        let dim = ModularData.groundStateDegeneracy su2Data g
        printfn "   g=%d: dim = %d" g dim
    printfn ""

// ============================================================================
// STEP 4: Comparing Theories
// ============================================================================

printfn "STEP 4: Comparing Anyon Theories"
printfn "---------------------------------"
printfn ""

printfn "| Theory      | Particles | Central Charge | Total D  | Universal? |"
printfn "|-------------|-----------|----------------|----------|------------|"

let theories = [
    ("Ising", AnyonSpecies.Ising, "[1,σ,ψ]", "No (Clifford)")
    ("Fibonacci", AnyonSpecies.Fibonacci, "[1,τ]", "Yes")
    ("SU(2)₃", AnyonSpecies.SU2Level 3, "[0,½,1,³⁄₂]", "No")
]

for (name, theory, particles, universal) in theories do
    match ModularData.computeModularData theory, 
          ModularData.totalQuantumDimension theory with
    | Ok data, Ok D ->
        printfn "| %-11s | %-9s | %14.4f | %8.4f | %-10s |" 
            name particles data.CentralCharge D universal
    | _ -> ()

printfn ""

// ============================================================================
// STEP 5: Key Properties Summary
// ============================================================================

printfn "STEP 5: Key Properties of Modular Data"
printfn "---------------------------------------"
printfn ""
printfn "🔬 Mathematical Structure:"
printfn "   - S-matrix: Symmetric, unitary (S S† = I, S = Sᵀ)"
printfn "   - T-matrix: Diagonal, unitary (|θₐ| = 1)"
printfn "   - Consistency: S² = C (charge conjugation)"
printfn "   - Consistency: (ST)³ = e^(iφ)·S² (modular group PSL(2,ℤ))"
printfn ""
printfn "📊 Physical Interpretation:"
printfn "   - S₀ₐ = dₐ/D: Quantum dimension normalization"
printfn "   - θₐ = e^(2πihₐ): Topological spin (twist phase)"
printfn "   - D² = Σdₐ²: Total quantum dimension"
printfn ""
printfn "🌐 Topological Properties:"
printfn "   - Ground state degeneracy: dim(g) = Σₐ S₀ₐ^(2-2g)"
printfn "   - Encodes fusion rules: S diagonalizes fusion matrices"
printfn "   - Determines braiding statistics"
printfn "   - Classifies modular tensor categories (MTCs)"
printfn ""
printfn "⚛️ Computational Power:"
printfn "   - Ising: Clifford gates only (needs magic states)"
printfn "   - Fibonacci: Universal via braiding alone!"
printfn "   - SU(2)ₖ: Rich structure, k≥3 needed for universality"
printfn ""
printfn "========================================="
printfn "✅ MODULAR DATA EXAMPLE COMPLETE"
printfn "========================================="

