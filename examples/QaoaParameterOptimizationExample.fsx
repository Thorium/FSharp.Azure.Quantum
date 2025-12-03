/// QAOA Parameter Optimization Example
///
/// This example demonstrates:
/// 1. Building a MaxCut QAOA problem
/// 2. Automatic parameter optimization with multiple strategies
/// 3. Comparing optimization results
/// 4. Visualizing convergence
///
/// Run with: dotnet fsi examples/QaoaParameterOptimizationExample.fsx

#r "nuget: FSharp.Azure.Quantum"

open System
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Core.QaoaParameterOptimizer
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Backends.DWaveBackend
open FSharp.Azure.Quantum.Backends.DWaveTypes

printfn "╔══════════════════════════════════════════════════════════════╗"
printfn "║  QAOA Parameter Optimization Demo                            ║"
printfn "╚══════════════════════════════════════════════════════════════╝"
printfn ""

// ============================================================================
// STEP 1: Define the Problem
// ============================================================================

printfn "📊 Step 1: Define MaxCut Problem"
printfn "──────────────────────────────────"

// Small graph for quick optimization
// Triangle: (0-1-2) with equal weights
let edges = [(0, 1, 1.0); (1, 2, 1.0); (0, 2, 1.0)]

// Build problem Hamiltonian
let buildMaxCutHamiltonian (numVertices: int) (edges: (int * int * float) list) : ProblemHamiltonian =
    let diagonalTerms =
        [0 .. numVertices - 1]
        |> List.map (fun v ->
            let weight = 
                edges 
                |> List.filter (fun (u, w, _) -> u = v || w = v)
                |> List.sumBy (fun (_, _, w) -> w)
            { Coefficient = weight / 2.0; QubitsIndices = [| v |]; PauliOperators = [| PauliZ |] }
        )
    
    let offDiagonalTerms =
        edges
        |> List.map (fun (u, v, w) ->
            { Coefficient = -w / 4.0; QubitsIndices = [| u; v |]; PauliOperators = [| PauliZ; PauliZ |] }
        )
    
    {
        NumQubits = numVertices
        Terms = List.append diagonalTerms offDiagonalTerms |> List.toArray
    }

let numVertices = 3
let problemHam = buildMaxCutHamiltonian numVertices edges

printfn $"Graph: {edges.Length} edges, {numVertices} vertices"
printfn $"Problem Hamiltonian: {problemHam.Terms.Length} terms"
printfn ""

// ============================================================================
// STEP 2: Create Backend
// ============================================================================

printfn "🔧 Step 2: Create Quantum Backend"
printfn "───────────────────────────────────"

// Use D-Wave backend with fixed seed for reproducibility
let backend = createMockDWaveBackend Advantage_System6_1 (Some 42)
printfn $"Backend: {backend.Name}"
printfn $"Max Qubits: {backend.MaxQubits}"
printfn ""

// ============================================================================
// STEP 3: Optimization Strategy Comparison
// ============================================================================

printfn "⚡ Step 3: Compare Optimization Strategies"
printfn "────────────────────────────────────────────"
printfn ""

let p = 1  // Single QAOA layer for speed

// Strategy 1: Single Run with Standard Initialization
printfn "┌─ Strategy 1: Single Run (Standard Init) ─────────────┐"
let config1 = {
    defaultConfig with
        OptStrategy = SingleRun
        InitStrategy = StandardQAOA
        NumShots = 500  // Fewer shots for speed
        MaxIterations = 50
        RandomSeed = Some 42
}

let result1 = optimizeQaoaParameters problemHam p backend config1
printfn ""

// Strategy 2: Multi-Start (3 starts)
printfn "┌─ Strategy 2: Multi-Start (3 starts) ─────────────────┐"
let config2 = {
    defaultConfig with
        OptStrategy = MultiStart 3
        InitStrategy = RandomUniform
        NumShots = 500
        MaxIterations = 50
        RandomSeed = Some 42
}

let result2 = optimizeQaoaParameters problemHam p backend config2
printfn ""

// Strategy 3: Two-Local Pattern Initialization
printfn "┌─ Strategy 3: Two-Local Pattern Init ─────────────────┐"
let config3 = {
    defaultConfig with
        OptStrategy = SingleRun
        InitStrategy = TwoLocalPattern
        NumShots = 500
        MaxIterations = 50
        RandomSeed = Some 42
}

let result3 = optimizeQaoaParameters problemHam p backend config3
printfn ""

// ============================================================================
// STEP 4: Compare Results
// ============================================================================

printfn "📊 Step 4: Compare Results"
printfn "───────────────────────────"
printfn ""

let results = [
    ("Single Run (Standard)", result1)
    ("Multi-Start (3x)", result2)
    ("Two-Local Pattern", result3)
]

printfn "  Strategy              | Final Energy | Converged | Evaluations"
printfn "  ──────────────────────|──────────────|───────────|────────────"
for (name, result) in results do
    printfn $"  {name,-22}| {result.FinalEnergy,12:F6} | {result.Converged,9} | {result.TotalEvaluations,10}"

printfn ""

// Find best result
let (bestName, bestResult) = 
    results 
    |> List.minBy (fun (_, res) -> res.FinalEnergy)

printfn $"✨ Best Strategy: {bestName}"
printfn $"   Energy: {bestResult.FinalEnergy:F6}"
printfn $"   Converged: {bestResult.Converged}"
printfn ""

// ============================================================================
// STEP 5: Verify with Final Circuit
// ============================================================================

printfn "🔍 Step 5: Verify Optimized Solution"
printfn "──────────────────────────────────────"

let (optGamma, optBeta) = bestResult.OptimizedParameters.[0]
printfn $"Optimized Parameters: γ = {optGamma:F4}, β = {optBeta:F4}"
printfn ""

// Build circuit with optimized parameters
let mixerHam = MixerHamiltonian.create numVertices
let optimalCircuit = QaoaCircuit.build problemHam mixerHam bestResult.OptimizedParameters
let circuitWrapper = QaoaCircuitWrapper(optimalCircuit) :> ICircuit

// Execute with more shots for better statistics
match backend.Execute circuitWrapper 2000 with
| Error e -> printfn $"Error: {e}"
| Ok execResult ->
    // Analyze solution distribution
    let counts = 
        execResult.Measurements
        |> Array.countBy id
        |> Array.sortByDescending snd
    
    printfn "Top 3 solutions:"
    printfn "  Bitstring | Count  | Probability | Cut Value"
    printfn "  ──────────|────────|─────────────|──────────"
    
    for i in 0 .. min 2 (counts.Length - 1) do
        let (bitstring, count) = counts.[i]
        let prob = float count / float execResult.NumShots
        
        // Calculate cut value
        let cutValue =
            edges
            |> List.filter (fun (u, v, _) -> bitstring.[u] <> bitstring.[v])
            |> List.sumBy (fun (_, _, w) -> w)
        
        let bitstringStr = String.Join("", bitstring)
        printfn $"  {bitstringStr}       | {count,6} | {prob,11:P2} | {cutValue,9:F1}"
    
    printfn ""
    
    // Find maximum cut
    let maxCutSolution =
        counts
        |> Array.map (fun (bitstring, count) ->
            let cutValue =
                edges
                |> List.filter (fun (u, v, _) -> bitstring.[u] <> bitstring.[v])
                |> List.sumBy (fun (_, _, w) -> w)
            (bitstring, count, cutValue)
        )
        |> Array.maxBy (fun (_, _, cutValue) -> cutValue)
    
    let (maxBitstring, maxCount, maxCut) = maxCutSolution
    
    printfn "╔══════════════════════════════════════════════════════════╗"
    printfn "║  Maximum Cut Solution                                    ║"
    printfn "╚══════════════════════════════════════════════════════════╝"
    printfn ""
    printfn $"  Partition: {String.Join("", maxBitstring)}"
    printfn $"  Cut Value: {maxCut:F1} / {float edges.Length:F1}"
    printfn $"  Percentage: {maxCut / float edges.Length:P1}"
    printfn $"  Found in: {float maxCount / float execResult.NumShots:P1} of shots"
    printfn ""

// ============================================================================
// Summary
// ============================================================================

printfn "╔══════════════════════════════════════════════════════════╗"
printfn "║  Key Takeaways                                           ║"
printfn "╚══════════════════════════════════════════════════════════╝"
printfn ""
printfn "1. Multi-start optimization helps escape local minima"
printfn "2. Different initialization strategies affect convergence"
printfn "3. More shots → better energy estimates but slower"
printfn "4. Nelder-Mead is derivative-free (works with noisy quantum)"
printfn "5. QAOA finds good approximate solutions quickly"
printfn ""

printfn "💡 Next Steps:"
printfn "   - Try deeper circuits (p=2, p=3)"
printfn "   - Experiment with larger graphs"
printfn "   - Compare with classical algorithms"
printfn "   - Use real D-Wave hardware"
printfn ""
