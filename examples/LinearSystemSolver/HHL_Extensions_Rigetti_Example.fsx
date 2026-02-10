// HHL Algorithm Extensions - Production Rigetti Example
// Demonstrates new features:
// 1. Automatic condition number estimation
// 2. Comprehensive error bounds calculation
// 3. Adaptive eigenvalue inversion method selection
//
// NEW in this version: Smart configuration for quantum hardware

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Algorithms.HHL
open FSharp.Azure.Quantum.Algorithms.HHLTypes
open FSharp.Azure.Quantum.Backends.LocalBackend

printfn "╔══════════════════════════════════════════════════════════════════════╗"
printfn "║  HHL EXTENSIONS: Production Quantum Features                        ║"
printfn "║  Auto-Optimization for Rigetti/IonQ/Quantinuum                       ║"
printfn "╚══════════════════════════════════════════════════════════════════════╝"
printfn ""

// ============================================================================
// FEATURE 1: Automatic Condition Number Estimation
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn "FEATURE 1: Automatic Condition Number Estimation"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "Creating a 2x2 diagonal matrix..."
let matrix1 = createDiagonalMatrix [|2.0; 5.0|]

match matrix1 with
| Error err ->
    printfn "❌ Error: %A" err
| Ok mat ->
    printfn "Matrix created: 2x2 diagonal"
    printfn "  Eigenvalues: 2.0, 5.0"
    printfn ""
    
    // Calculate condition number
    printfn "Calculating condition number..."
    let matWithKappa = calculateConditionNumber mat
    
    match matWithKappa.ConditionNumber with
    | Some kappa ->
        printfn "✅ Condition number κ = %.2f" kappa
        printfn "   (κ = λ_max / λ_min = 5.0 / 2.0 = 2.5)"
        printfn ""
        printfn "   Interpretation:"
        if kappa <= 10.0 then
            printfn "   ✅ Well-conditioned! HHL will work great."
        elif kappa <= 100.0 then
            printfn "   ⚠️  Moderately conditioned. HHL should work."
        else
            printfn "   ❌ Poorly conditioned. Consider preconditioning."
        printfn ""
    | None ->
        printfn "❌ Could not calculate condition number"

printfn ""

// ============================================================================
// FEATURE 2: Comprehensive Error Bounds
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn "FEATURE 2: Error Bounds for Quantum Hardware"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

match matrix1 with
| Error _ -> ()
| Ok mat ->
    let vector1 = createQuantumVector [|Complex(1.0, 0.0); Complex(1.0, 0.0)|]
    
    match vector1 with
    | Error err ->
        printfn "❌ Vector error: %A" err
    | Ok vec ->
        printfn "Setting up HHL configuration..."
        match defaultConfig mat vec with
        | Error err ->
            printfn "❌ Config error: %A" err
        | Ok config ->
        printfn "  QPE precision: %d qubits" config.EigenvalueQubits
        printfn "  Inversion method: %A" config.InversionMethod
        printfn ""
        
        // Calculate error bounds for different backends
        printfn "Calculating error bounds for different backends..."
        printfn ""
        
        // Rigetti (superconducting qubits)
        printfn "📊 Rigetti (Aspen-M-3, superconducting qubits):"
        let rigettiErrors = calculateErrorBounds config (Some 0.998) None
        printfn "   Gate fidelity: 99.8%%"
        printfn "   QPE precision error: %.6f" rigettiErrors.QPEPrecisionError
        printfn "   Gate fidelity error: %.6f" rigettiErrors.GateFidelityError
        printfn "   Inversion error: %.6f" rigettiErrors.InversionError
        printfn "   Total error: %.6f" rigettiErrors.TotalError
        printfn "   Success probability: %.4f (%.1f%%)" 
            rigettiErrors.EstimatedSuccessProbability
            (rigettiErrors.EstimatedSuccessProbability * 100.0)
        printfn ""
        
        // IonQ (trapped ion)
        printfn "📊 IonQ Aria (trapped ion qubits):"
        let ionqErrors = calculateErrorBounds config (Some 0.9999) None
        printfn "   Gate fidelity: 99.99%%"
        printfn "   QPE precision error: %.6f" ionqErrors.QPEPrecisionError
        printfn "   Gate fidelity error: %.6f" ionqErrors.GateFidelityError
        printfn "   Inversion error: %.6f" ionqErrors.InversionError
        printfn "   Total error: %.6f" ionqErrors.TotalError
        printfn "   Success probability: %.4f (%.1f%%)" 
            ionqErrors.EstimatedSuccessProbability
            (ionqErrors.EstimatedSuccessProbability * 100.0)
        printfn ""
        
        printfn "💡 Analysis:"
        printfn "   IonQ has %.2fx better total error than Rigetti" 
            (rigettiErrors.TotalError / ionqErrors.TotalError)
        printfn "   (Due to higher gate fidelity: 99.99%% vs 99.8%%)"
        printfn ""

printfn ""

// ============================================================================
// FEATURE 3: Adaptive Method Selection
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn "FEATURE 3: Adaptive Eigenvalue Inversion"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "Testing adaptive method selection for different condition numbers..."
printfn ""

let testConditionNumbers = [2.0; 15.0; 150.0; 5000.0]

for kappa in testConditionNumbers do
    let method = selectInversionMethod kappa None
    printfn "κ = %.0f → %A" kappa method
    
printfn ""
printfn "💡 Insights:"
printfn "   κ ≤ 10:    ExactRotation (best for well-conditioned)"
printfn "   10 < κ ≤ 100: LinearApproximation (moderate)"  
printfn "   κ > 100:   PiecewiseLinear (handles wide eigenvalue range)"
printfn ""

printfn ""

// ============================================================================
// FEATURE 4: One-Function Optimized Configuration
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn "FEATURE 4: Auto-Optimized Configuration"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

match matrix1 with
| Error _ -> ()
| Ok mat ->
    let vector1 = createQuantumVector [|Complex(1.0, 0.0); Complex(1.0, 0.0)|]
    
    match vector1 with
    | Error _ -> ()
    | Ok vec ->
        printfn "Using optimizedConfig for automatic setup..."
        printfn ""
        
        // Target 1% accuracy
        match optimizedConfig mat vec (Some 0.01) with
        | Error err ->
            printfn "❌ Config error: %A" err
        | Ok config ->
        
        printfn "✅ Configuration automatically optimized:"
        printfn "   Matrix condition number: %.2f" 
            (config.Matrix.ConditionNumber |> Option.defaultValue 0.0)
        printfn "   QPE precision: %d qubits (for 1%% accuracy)" config.EigenvalueQubits
        printfn "   Inversion method: %A" config.InversionMethod
        printfn "   Min eigenvalue threshold: %.6f" config.MinEigenvalue
        printfn "   Post-selection: %b" config.UsePostSelection
        printfn ""
        
        // Calculate recommended QPE precision for different accuracies
        printfn "📏 Recommended QPE precision for different targets:"
        let accuracies = [0.1; 0.01; 0.001; 0.0001]
        for acc in accuracies do
            let precision = recommendQPEPrecision acc
            printfn "   %.2f%% accuracy → %d qubits" (acc * 100.0) precision
        printfn ""

printfn ""

// ============================================================================
// COMPLETE EXAMPLE: HHL with Rigetti Backend (Simulated)
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn "COMPLETE EXAMPLE: Solve 2×2 System with Auto-Optimization"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "PROBLEM: Solve [[2, 0], [0, 3]] · x = [1, 1]"
printfn "Expected solution: x ≈ [0.5, 0.333...]"
printfn ""

// Create matrix and vector
let matrixResult = createDiagonalMatrix [|2.0; 3.0|]
let vectorResult = createQuantumVector [|Complex(1.0, 0.0); Complex(1.0, 0.0)|]

match matrixResult, vectorResult with
| Ok matrix, Ok vector ->
    printfn "Step 1: Auto-optimize configuration..."
    match optimizedConfig matrix vector (Some 0.01) with  // 1% target accuracy
    | Error err ->
        printfn "  ❌ Config error: %A" err
    | Ok config ->
    printfn "  ✅ κ = %.2f (well-conditioned)" 
        (config.Matrix.ConditionNumber |> Option.defaultValue 0.0)
    printfn "  ✅ Method: %A" config.InversionMethod
    printfn ""
    
    printfn "Step 2: Calculate error budget (for Rigetti)..."
    let errorBudget = calculateErrorBounds config (Some 0.998) None
    printfn "  ✅ Total error: %.4f" errorBudget.TotalError
    printfn "  ✅ Success probability: %.2f%%" 
        (errorBudget.EstimatedSuccessProbability * 100.0)
    printfn ""
    
    printfn "Step 3: Execute HHL on local simulator..."
    let backend = LocalBackend() :> IQuantumBackend
    
    match solve2x2Diagonal (2.0, 3.0) (Complex(1.0, 0.0), Complex(1.0, 0.0)) backend with
    | Error err ->
        printfn "  ❌ Error: %A" err
    | Ok result ->
        printfn "  ✅ SUCCESS!"
        printfn ""
        printfn "  Solution vector:"
        for i in 0 .. result.Solution.Length - 1 do
            printfn "    x[%d] = %.6f" i result.Solution[i].Real
        printfn ""
        printfn "  Actual success probability: %.4f" result.SuccessProbability
        printfn "  Gates used: %d" result.GateCount
        printfn ""
        
        printfn "  Classical verification:"
        printfn "    Expected: x[0] = 0.5, x[1] = 0.333..."
        printfn "    Quantum:  x[0] = %.3f, x[1] = %.3f" 
            result.Solution[0].Real result.Solution[1].Real
        printfn ""
        
| Error err, _ ->
    printfn "❌ Matrix error: %A" err
| _, Error err ->
    printfn "❌ Vector error: %A" err

printfn ""

// ============================================================================
// PRODUCTION WORKFLOW GUIDE
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn "PRODUCTION WORKFLOW for Rigetti/IonQ/Quantinuum"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "1️⃣  Create your matrix and vector (domain-specific problem)"
printfn "   let matrix = createDiagonalMatrix [|λ₁; λ₂; ...|]"
printfn "   let vector = createQuantumVector [|b₁; b₂; ...|]"
printfn ""

printfn "2️⃣  Use optimizedConfig for automatic setup"
printfn "   let config = optimizedConfig matrix vector (Some 0.01)"
printfn "   // Automatically:"
printfn "   //   - Calculates condition number"
printfn "   //   - Selects best inversion method"
printfn "   //   - Sets QPE precision for target accuracy"
printfn ""

printfn "3️⃣  Check error budget BEFORE running on expensive hardware"
printfn "   let errors = calculateErrorBounds config (Some 0.998) None"
printfn "   if errors.TotalError > 0.1 then"
printfn "       printfn \"Warning: High error expected!\""
printfn ""

printfn "4️⃣  Execute on quantum backend"
printfn "   // For local testing:"
printfn "   let backend = LocalBackend() :> IQuantumBackend"
printfn ""
printfn "   // For Rigetti (requires Azure Quantum workspace):"
printfn "   // let backend = RigettiBackend.create workspace \"Aspen-M-3\" :> IQuantumBackend"
printfn ""
printfn "   // For IonQ:"
printfn "   // let backend = IonQBackend.create workspace \"ionq.qpu.aria-1\" :> IQuantumBackend"
printfn ""
printfn "   match HHL.execute config backend with"
printfn "   | Ok result -> printfn \"Success: %A\" result.Solution"
printfn "   | Error err -> printfn \"Error: %A\" err"
printfn ""

printfn "5️⃣  Analyze results and error margins"
printfn "   // Use fidelity estimate if classical solution known"
printfn "   // Compare predicted vs actual success probability"
printfn ""

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "✅ HHL Extensions Demo Complete!"
printfn ""
printfn "Key Takeaways:"
printfn "  • Condition number predicts HHL success (κ < 100 is good)"
printfn "  • Error bounds help choose hardware (IonQ vs Rigetti vs Quantinuum)"
printfn "  • Adaptive methods optimize for matrix properties"
printfn "  • optimizedConfig() does everything automatically"
printfn ""
printfn "Next Steps:"
printfn "  1. Test with your domain-specific matrices"
printfn "  2. Run on local simulator first"
printfn "  3. Use error bounds to estimate hardware costs"
printfn "  4. Deploy to production Rigetti/IonQ when error budget acceptable"
printfn ""
