// ==============================================================================
// Quantum VaR Stress Test
// ==============================================================================
// Demonstrates the use of the QuantumRiskEngine DSL for financial stress testing.
//
// Business Context:
// Banks and asset managers must calculate Value at Risk (VaR) and Conditional 
// VaR (Expected Shortfall) to quantify potential losses in extreme market 
// scenarios.
//
// This example uses the high-level 'quantumRiskEngine' builder to:
// 1. Configure the risk simulation parameters.
// 2. Select quantum acceleration (Amplitude Estimation).
// 3. Calculate key risk metrics (VaR, CVaR).
//
// Quantum Advantage:
// Quantum Amplitude Estimation provides a quadratic speedup over classical 
// Monte Carlo simulation, allowing for faster convergence or higher precision 
// estimates of tail risks.
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Business

printfn "╔══════════════════════════════════════════════════════════════╗"
printfn "║            Quantum VaR Stress Test Engine                    ║"
printfn "╚══════════════════════════════════════════════════════════════╝"
printfn ""

// Define the stress test scenario using the DSL
printfn "Configuring Risk Engine..."
let riskReport = 
    quantumRiskEngine {
        // 1. Data Ingestion (Mock file for this example)
        load_market_data "market_data_2023_2024.csv"
        
        // 2. Configuration
        // 99% Confidence Level (standard for Basel III)
        set_confidence_level 0.99
        
        // Number of paths for simulation
        // In a real quantum backend, this maps to circuit depth/shots
        set_simulation_paths 1_000_000
        
        // 3. Quantum Acceleration
        // Use Amplitude Estimation algorithm for O(1/epsilon) convergence
        use_amplitude_estimation true
        use_error_mitigation true
        
        // 4. Goals
        calculate_metric RiskMetric.ValueAtRisk
        calculate_metric RiskMetric.ConditionalVaR
        calculate_metric RiskMetric.ExpectedShortfall
    }
    |> RiskEngine.execute

// Report Results
printfn ""
printfn "📊 Risk Analysis Report"
printfn "───────────────────────"
printfn "Method:           %s" riskReport.Method
printfn "Confidence Level: %.1f%%" (riskReport.ConfidenceLevel * 100.0)
printfn "Execution Time:   %.2f ms" riskReport.ExecutionTimeMs
printfn ""
printfn "Risk Metrics:"
printfn "-------------"

let formatMetric name value =
    match value with
    | Some v -> printfn "  %-20s: %.2f%%" name (v * 100.0)
    | None   -> printfn "  %-20s: N/A" name

formatMetric "Value at Risk (VaR)" riskReport.VaR
formatMetric "Conditional VaR" riskReport.CVaR
formatMetric "Expected Shortfall" riskReport.ExpectedShortfall

printfn ""
printfn "Interpretation:"
printfn "  At %.1f%% confidence, the maximum expected loss is %.2f%% of portfolio value." 
    (riskReport.ConfidenceLevel * 100.0) 
    (riskReport.VaR |> Option.defaultValue 0.0 |> fun v -> v * 100.0)
printfn "  If that threshold is breached, the average loss (CVaR) is expected to be %.2f%%."
    (riskReport.CVaR |> Option.defaultValue 0.0 |> fun v -> v * 100.0)
printfn ""
