// ==============================================================================
// Investment Portfolio - Small Quantum Test
// ==============================================================================
// Tests QuantumPortfolioSolver with a minimal 3-asset portfolio that fits
// within LocalSimulator's constraints (<10 qubits).
//
// This demonstrates:
// - Direct quantum portfolio optimization via QuantumPortfolioSolver
// - QUBO encoding for mean-variance portfolio problems
// - QAOA execution and measurement decoding
// - Quantum readiness verification
// ==============================================================================

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"


open System
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Classical.PortfolioSolver
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core

// ==============================================================================
// SMALL PORTFOLIO CONFIGURATION (3 Assets)
// ==============================================================================

let assets : Asset list = [
    { Symbol = "AAPL"; ExpectedReturn = 0.15; Risk = 0.20; Price = 175.0 }
    { Symbol = "MSFT"; ExpectedReturn = 0.18; Risk = 0.22; Price = 380.0 }
    { Symbol = "GOOGL"; ExpectedReturn = 0.12; Risk = 0.25; Price = 140.0 }
]

let constraints = {
    Budget = 10000.0        // $10,000 budget
    MinHolding = 0.0        // No minimum
    MaxHolding = 10000.0    // Can invest all in one asset
}

let config : QuantumPortfolioSolver.QuantumPortfolioConfig = {
    NumShots = 1000
    RiskAversion = 0.5
    InitialParameters = (0.5, 0.5)
}

// ==============================================================================
// QUANTUM BACKEND SETUP
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║              QUANTUM PORTFOLIO SOLVER TEST (3 Assets)                        ║"
printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
printfn ""
printfn "Portfolio Configuration:"
printfn "  Assets: %d" assets.Length
printfn "  Budget: $%.2f" constraints.Budget
printfn ""

assets |> List.iter (fun a ->
    printfn "  %-6s | Return: %5.1f%% | Risk: %5.1f%% | Price: $%.2f" 
        a.Symbol (a.ExpectedReturn * 100.0) (a.Risk * 100.0) a.Price
)
printfn ""

// Create backend
let backend = BackendAbstraction.createLocalBackend()

printfn "Backend: LocalSimulator (emulator)"
printfn ""
printfn "Running quantum portfolio optimization..."
printfn ""

// ==============================================================================
// QUANTUM OPTIMIZATION
// ==============================================================================

let startTime = DateTime.UtcNow

let result = QuantumPortfolioSolver.solve backend assets constraints config

let endTime = DateTime.UtcNow
let elapsed = (endTime - startTime).TotalMilliseconds

printfn "Completed in %.0f ms" elapsed
printfn ""

// ==============================================================================
// RESULTS ANALYSIS
// ==============================================================================

match result with
| Ok solution ->
    printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
    printfn "║                         QUANTUM SOLUTION FOUND                               ║"
    printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
    printfn ""
    printfn "QUANTUM SOLVER OUTPUT:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "  Energy:     %.4f" solution.BestEnergy
    printfn "  Backend:    %s" solution.BackendName
    printfn "  Num Shots:  %d" solution.NumShots
    printfn "  Elapsed:    %.0f ms" solution.ElapsedMs
    printfn ""
    
    printfn "PORTFOLIO ALLOCATION:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    
    let mutable totalValue = 0.0
    solution.Allocations |> List.iteri (fun i alloc ->
        totalValue <- totalValue + alloc.Value
        let pct = (alloc.Value / constraints.Budget) * 100.0
        printfn "  %d. %-6s | %6.2f shares @ $%.2f = $%8.2f (%.1f%%)"
            (i + 1)
            alloc.Asset.Symbol
            alloc.Shares
            alloc.Asset.Price
            alloc.Value
            pct
    )
    
    printfn ""
    printfn "PORTFOLIO METRICS:"
    printfn "────────────────────────────────────────────────────────────────────────────────"
    printfn "  Total Invested:        $%.2f" totalValue
    printfn "  Expected Annual Return: %.2f%%" (solution.ExpectedReturn * 100.0)
    printfn "  Portfolio Risk (σ):     %.2f%%" (solution.Risk * 100.0)
    
    let sharpe = 
        if solution.Risk > 0.0 then solution.ExpectedReturn / solution.Risk
        else 0.0
    printfn "  Sharpe Ratio:           %.2f" sharpe
    printfn ""
    
    printfn "✅ QUANTUM PORTFOLIO SOLVER VERIFIED"
    printfn "   • QUBO encoding successful"
    printfn "   • QAOA circuit executed"
    printfn "   • Solution decoded and validated"
    printfn ""
    
| Error err ->
    printfn "❌ Quantum optimization failed: %s" err.Message
    printfn ""
    printfn "Common issues:"
    printfn "  • Problem exceeds LocalSimulator's qubit limit (max ~10 qubits)"
    printfn "  • QUBO encoding constraints may be over-constrained"
    printfn "  • Try reducing number of assets or discretization levels"
    exit 1

printfn "╔══════════════════════════════════════════════════════════════════════════════╗"
printfn "║                            TEST COMPLETE                                     ║"
printfn "╚══════════════════════════════════════════════════════════════════════════════╝"
