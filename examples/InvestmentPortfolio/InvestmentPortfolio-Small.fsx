// ==============================================================================
// Investment Portfolio - Small Quantum Test
// ==============================================================================
// Tests QuantumPortfolioSolver with a minimal 3-asset portfolio that fits
// within LocalSimulator's constraints (<10 qubits).
//
// Demonstrates:
// - Direct quantum portfolio optimization via QuantumPortfolioSolver
// - QUBO encoding for mean-variance portfolio problems
// - QAOA execution and measurement decoding
//
// Usage:
//   dotnet fsi InvestmentPortfolio-Small.fsx
//   dotnet fsi InvestmentPortfolio-Small.fsx -- --budget 20000 --risk-aversion 0.7
//   dotnet fsi InvestmentPortfolio-Small.fsx -- --quiet --output results.json --csv results.csv
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
open FSharp.Azure.Quantum.Examples.Common

open System
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Classical.PortfolioSolver
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv
Cli.exitIfHelp "InvestmentPortfolio-Small.fsx" "Quantum portfolio optimization with 3 assets" [
    { Name = "shots"; Description = "Number of measurement shots"; Default = Some "1000" }
    { Name = "budget"; Description = "Investment budget in dollars"; Default = Some "10000" }
    { Name = "risk-aversion"; Description = "Risk aversion factor (0.0-1.0)"; Default = Some "0.5" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress printed output"; Default = None }
] args

let cliShots = Cli.getIntOr "shots" 1000 args
let cliBudget = Cli.getFloatOr "budget" 10000.0 args
let cliRiskAversion = Cli.getFloatOr "risk-aversion" 0.5 args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

// --- Portfolio Configuration ---
let assets : Asset list = [
    { Symbol = "AAPL"; ExpectedReturn = 0.15; Risk = 0.20; Price = 175.0 }
    { Symbol = "MSFT"; ExpectedReturn = 0.18; Risk = 0.22; Price = 380.0 }
    { Symbol = "GOOGL"; ExpectedReturn = 0.12; Risk = 0.25; Price = 140.0 }
]

let constraints = {
    Budget = cliBudget
    MinHolding = 0.0
    MaxHolding = cliBudget
}

let config : QuantumPortfolioSolver.QuantumPortfolioConfig = {
    NumShots = cliShots
    RiskAversion = cliRiskAversion
    InitialParameters = (0.5, 0.5)
}

// --- Display Setup ---
pr "=== Quantum Portfolio Solver Test (3 Assets) ==="
pr ""
pr "Portfolio Configuration:"
pr "  Assets: %d" assets.Length
pr "  Budget: $%.2f" constraints.Budget
pr "  Risk Aversion: %.2f" cliRiskAversion
pr ""

assets |> List.iter (fun a ->
    pr "  %-6s | Return: %5.1f%% | Risk: %5.1f%% | Price: $%.2f"
        a.Symbol (a.ExpectedReturn * 100.0) (a.Risk * 100.0) a.Price
)
pr ""

// --- Quantum Execution ---
let quantumBackend = LocalBackend() :> IQuantumBackend

pr "Backend: LocalSimulator (emulator)"
pr "Running quantum portfolio optimization..."
pr ""

let startTime = DateTime.UtcNow
let result = QuantumPortfolioSolver.solve quantumBackend assets constraints config
let elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds

pr "Completed in %.0f ms" elapsed
pr ""

// --- Results ---
match result with
| Ok solution ->
    let totalValue =
        solution.Allocations |> List.sumBy (fun alloc -> alloc.Value)

    pr "=== QUANTUM SOLUTION FOUND ==="
    pr ""
    pr "Quantum Solver Output:"
    pr "-------------------------------------------"
    pr "  Energy:    %.4f" solution.BestEnergy
    pr "  Backend:   %s" solution.BackendName
    pr "  Shots:     %d" solution.NumShots
    pr "  Elapsed:   %.0f ms" solution.ElapsedMs
    pr ""
    pr "Portfolio Allocation:"
    pr "-------------------------------------------"

    solution.Allocations |> List.iteri (fun i alloc ->
        let pct = (alloc.Value / constraints.Budget) * 100.0
        pr "  %d. %-6s | %6.2f shares @ $%.2f = $%8.2f (%.1f%%)"
            (i + 1)
            alloc.Asset.Symbol
            alloc.Shares
            alloc.Asset.Price
            alloc.Value
            pct
    )

    pr ""
    pr "Portfolio Metrics:"
    pr "-------------------------------------------"
    pr "  Total Invested:         $%.2f" totalValue
    pr "  Expected Annual Return: %.2f%%" (solution.ExpectedReturn * 100.0)
    pr "  Portfolio Risk:         %.2f%%" (solution.Risk * 100.0)

    let sharpe =
        if solution.Risk > 0.0 then solution.ExpectedReturn / solution.Risk
        else 0.0
    pr "  Sharpe Ratio:           %.2f" sharpe
    pr ""

    // --- JSON output ---
    outputPath |> Option.iter (fun path ->
        let payload =
            {| energy = solution.BestEnergy
               backendName = solution.BackendName
               shots = solution.NumShots
               elapsedMs = solution.ElapsedMs
               totalInvested = totalValue
               expectedReturn = solution.ExpectedReturn
               risk = solution.Risk
               sharpeRatio = sharpe
               allocations = solution.Allocations |> List.map (fun a ->
                   {| symbol = a.Asset.Symbol
                      shares = a.Shares
                      price = a.Asset.Price
                      value = a.Value |}) |}
        Reporting.writeJson path payload
        pr "JSON written to %s" path
    )

    // --- CSV output ---
    csvPath |> Option.iter (fun path ->
        let header = ["Symbol"; "Shares"; "Price"; "Value"; "Pct"]
        let rows =
            solution.Allocations |> List.map (fun a ->
                let pct = (a.Value / constraints.Budget) * 100.0
                [a.Asset.Symbol; sprintf "%.2f" a.Shares; sprintf "%.2f" a.Asset.Price;
                 sprintf "%.2f" a.Value; sprintf "%.1f" pct])
        Reporting.writeCsv path header rows
        pr "CSV written to %s" path
    )

| Error err ->
    pr "Quantum optimization failed: %s" err.Message
    pr ""
    pr "Common issues:"
    pr "  - Problem exceeds LocalSimulator qubit limit (max ~10 qubits)"
    pr "  - QUBO encoding constraints may be over-constrained"
    pr "  - Try reducing number of assets or discretization levels"

// --- Usage hints ---
if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr "-------------------------------------------"
    pr "Tip: Use --output results.json or --csv results.csv to export data."
    pr "     Use --budget N to set investment budget (default 10000)."
    pr "     Use --risk-aversion F to adjust risk preference (default 0.5)."
    pr "     Use --help for all options."
