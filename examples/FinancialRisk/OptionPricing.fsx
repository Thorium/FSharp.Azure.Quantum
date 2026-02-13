// ============================================================================
// Quantum Option Pricing with FSharp.Azure.Quantum
// ============================================================================
//
// This example demonstrates quantum Monte Carlo option pricing using
// FSharp.Azure.Quantum library.
//
// FEATURES:
// - Möttönen state preparation for GBM distribution encoding
// - Grover-based amplitude estimation
// - Quadratic speedup: O(1/ε) vs classical O(1/ε²)
// - Production-ready validation and error handling
//
// REQUIREMENTS:
// - .NET SDK
// - FSharp.Azure.Quantum library
//
// USAGE:
//   dotnet fsi OptionPricing.fsx                                    (defaults)
//   dotnet fsi OptionPricing.fsx -- --help                          (show options)
//   dotnet fsi OptionPricing.fsx -- --spot 110 --strike 100 --volatility 0.3
//   dotnet fsi OptionPricing.fsx -- --quiet --output results.json --csv results.csv
// ============================================================================

(*
===============================================================================
 Background Theory
===============================================================================

OPTION PRICING is a cornerstone of quantitative finance. A European call option
gives the holder the right (but not obligation) to buy an asset at a strike 
price K at expiration time T. The famous BLACK-SCHOLES formula (1973) provides
closed-form pricing for European options under geometric Brownian motion:

    C = S₀ * N(d₁) - K * e^(-rT) * N(d₂)

Where:
    d₁ = [ln(S₀/K) + (r + σ²/2)T] / (σ√T)
    d₂ = d₁ - σ√T
    N(x) = standard normal CDF

For complex options (path-dependent, American, multi-asset), closed-form 
solutions don't exist, requiring MONTE CARLO SIMULATION:

    Price = e^(-rT) * E[max(S_T - K, 0)]
          ≈ e^(-rT) * (1/N) * Σᵢ max(Sᵢ - K, 0)

Classical Monte Carlo achieves precision ε with O(1/ε²) samples due to the
Central Limit Theorem convergence rate of 1/√N.

QUANTUM AMPLITUDE ESTIMATION provides quadratic speedup. The algorithm:
1. Encode the price distribution into quantum amplitudes: |ψ⟩ = Σₓ √p(x)|x⟩
2. Apply an oracle marking "profitable" states (S > K)
3. Use Grover-like iterations to amplify the probability
4. Measure to estimate E[payoff] with O(1/ε) queries

The quantum speedup is particularly valuable for:
- High-precision pricing (regulatory capital calculations)
- Many-asset basket options (exponential state space)
- Real-time risk calculations during market stress
- Greeks computation (multiple pricings per Greek)

Key Equations:
  - Black-Scholes: C = S₀*N(d₁) - K*e^(-rT)*N(d₂)
  - GBM dynamics: dS = μS dt + σS dW
  - Classical MC error: O(1/√N)
  - Quantum AE error: O(1/N) - quadratic speedup

Quantum Advantage:
  For precision ε = 0.01 (1 cent on a $1 option):
  - Classical: ~10,000 samples
  - Quantum: ~100 queries
  100x speedup per option, compounding for portfolios.

References:
  [1] Black, F. & Scholes, M. "The Pricing of Options" J. Political Economy (1973)
  [2] Rebentrost, P. et al. "Quantum computational finance" arXiv:1805.00109 (2018)
  [3] Stamatopoulos, N. et al. "Option Pricing using Quantum Computers" Quantum (2020)
  [4] Wikipedia: Black-Scholes_model (https://en.wikipedia.org/wiki/Black-Scholes_model)
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "OptionPricing.fsx"
    "Quantum Monte Carlo option pricing with amplitude estimation."
    [ { Cli.OptionSpec.Name = "spot";              Description = "Spot price";                          Default = Some "100" }
      { Cli.OptionSpec.Name = "strike";            Description = "Strike price";                        Default = Some "105" }
      { Cli.OptionSpec.Name = "rate";              Description = "Risk-free rate";                      Default = Some "0.05" }
      { Cli.OptionSpec.Name = "volatility";        Description = "Volatility (annualized)";             Default = Some "0.2" }
      { Cli.OptionSpec.Name = "expiry";            Description = "Time to expiry in years";             Default = Some "1.0" }
      { Cli.OptionSpec.Name = "qubits";            Description = "Qubits for amplitude estimation";     Default = Some "6" }
      { Cli.OptionSpec.Name = "shots";             Description = "Quantum circuit shots";               Default = Some "500" }
      { Cli.OptionSpec.Name = "grover-iterations"; Description = "Grover iterations for amplification"; Default = Some "2" }
      { Cli.OptionSpec.Name = "output";            Description = "Write results to JSON file";          Default = None }
      { Cli.OptionSpec.Name = "csv";               Description = "Write results to CSV file";           Default = None }
      { Cli.OptionSpec.Name = "quiet";             Description = "Suppress informational output";       Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ==============================================================================
// CONFIGURATION
// ==============================================================================

let spotPrice = Cli.getFloatOr "spot" 100.0 args
let strikePrice = Cli.getFloatOr "strike" 105.0 args
let riskFreeRate = Cli.getFloatOr "rate" 0.05 args
let volatility = Cli.getFloatOr "volatility" 0.2 args
let timeToExpiry = Cli.getFloatOr "expiry" 1.0 args
let numQubits = Cli.getIntOr "qubits" 6 args
let groverIterations = Cli.getIntOr "grover-iterations" 2 args
let shots = Cli.getIntOr "shots" 500 args

// Use local quantum simulator
let backend = LocalBackend.LocalBackend() :> IQuantumBackend

// Collect results for structured output
let resultRows = System.Collections.Generic.List<Map<string, string>>()

if not quiet then
    printfn "╔═══════════════════════════════════════════════════════════════╗"
    printfn "║   Quantum Monte Carlo Option Pricing                         ║"
    printfn "║   Using FSharp.Azure.Quantum                                  ║"
    printfn "╚═══════════════════════════════════════════════════════════════╝"
    printfn ""

// ============================================================================
// EXAMPLE 1: Price European Call Option
// ============================================================================

if not quiet then
    printfn "═══ Example 1: European Call Option ═══"
    printfn ""
    printfn "Market Parameters:"
    printfn "  Spot Price (S₀):    $%.2f" spotPrice
    printfn "  Strike Price (K):   $%.2f" strikePrice
    printfn "  Risk-free Rate (r): %.1f%%" (riskFreeRate * 100.0)
    printfn "  Volatility (σ):     %.1f%%" (volatility * 100.0)
    printfn "  Time to Expiry (T): %.1f year" timeToExpiry
    printfn ""
    printfn "Using LocalBackend (quantum simulator)..."
    printfn "Running quantum Monte Carlo..."
    printfn ""

let result =
    OptionPricing.priceEuropeanCall
        spotPrice
        strikePrice
        riskFreeRate
        volatility
        timeToExpiry
        numQubits
        groverIterations
        shots
        backend
    |> Async.RunSynchronously

match result with
| Ok price ->
    if not quiet then
        printfn "✓ Success!"
        printfn ""
        printfn "RESULTS:"
        printfn "  Option Price:          $%.4f" price.Price
        printfn "  Confidence Interval:   ±$%.4f" price.ConfidenceInterval
        printfn "  Price Range:           $%.4f - $%.4f"
            (price.Price - price.ConfidenceInterval)
            (price.Price + price.ConfidenceInterval)
        printfn "  Qubits Used:           %d (2^%d = %d price levels)"
            price.QubitsUsed
            price.QubitsUsed
            (1 <<< price.QubitsUsed)
        printfn "  Method:                %s" price.Method
        printfn "  Quantum Speedup:       %.1fx" price.Speedup
        printfn ""

    resultRows.Add(
        [ "example", "European Call"
          "spot", sprintf "%.2f" spotPrice
          "strike", sprintf "%.2f" strikePrice
          "rate", sprintf "%.4f" riskFreeRate
          "volatility", sprintf "%.4f" volatility
          "expiry", sprintf "%.1f" timeToExpiry
          "price", sprintf "%.4f" price.Price
          "confidence_interval", sprintf "%.4f" price.ConfidenceInterval
          "qubits", sprintf "%d" price.QubitsUsed
          "method", price.Method
          "speedup", sprintf "%.1f" price.Speedup
          "error", "" ]
        |> Map.ofList)

| Error err ->
    if not quiet then
        printfn "✗ Error: %A" err
        printfn ""

    resultRows.Add(
        [ "example", "European Call"
          "spot", sprintf "%.2f" spotPrice
          "strike", sprintf "%.2f" strikePrice
          "rate", sprintf "%.4f" riskFreeRate
          "volatility", sprintf "%.4f" volatility
          "expiry", sprintf "%.1f" timeToExpiry
          "price", ""
          "confidence_interval", ""
          "qubits", ""
          "method", ""
          "speedup", ""
          "error", sprintf "%A" err ]
        |> Map.ofList)

// ============================================================================
// EXAMPLE 2: Compare Call vs Put Options
// ============================================================================

if not quiet then
    printfn "═══ Example 2: Put-Call Comparison ═══"
    printfn ""

let priceBothOptions spot strike =
    async {
        let! callResult =
            OptionPricing.priceEuropeanCall spot strike riskFreeRate volatility timeToExpiry numQubits groverIterations shots backend
        let! putResult =
            OptionPricing.priceEuropeanPut spot strike riskFreeRate volatility timeToExpiry numQubits groverIterations shots backend

        return (callResult, putResult)
    }

let (callPrice, putPrice) =
    priceBothOptions spotPrice strikePrice
    |> Async.RunSynchronously

if not quiet then
    printfn "Comparing European Call vs Put (Same strike):"
    printfn ""

match callPrice, putPrice with
| Ok call, Ok put ->
    if not quiet then
        printfn "  Call Option Price:  $%.4f" call.Price
        printfn "  Put Option Price:   $%.4f" put.Price
        printfn ""
        printfn "  Put-Call Difference: $%.4f" (abs (call.Price - put.Price))

        // Put-Call Parity check (approximate due to quantum approximation)
        // C - P ≈ S - K*e^(-rT)
        let parity = call.Price - put.Price
        let expected = spotPrice - strikePrice * exp(-riskFreeRate * timeToExpiry)
        printfn "  Put-Call Parity Check:"
        printfn "    Observed (C - P):   $%.4f" parity
        printfn "    Expected (S - Ke⁻ʳᵀ): $%.4f" expected
        printfn "    Difference:         $%.4f" (abs (parity - expected))
        printfn ""

    resultRows.Add(
        [ "example", "European Call (Put-Call)"
          "spot", sprintf "%.2f" spotPrice
          "strike", sprintf "%.2f" strikePrice
          "rate", sprintf "%.4f" riskFreeRate
          "volatility", sprintf "%.4f" volatility
          "expiry", sprintf "%.1f" timeToExpiry
          "price", sprintf "%.4f" call.Price
          "confidence_interval", sprintf "%.4f" call.ConfidenceInterval
          "qubits", sprintf "%d" call.QubitsUsed
          "method", call.Method
          "speedup", sprintf "%.1f" call.Speedup
          "error", "" ]
        |> Map.ofList)

    resultRows.Add(
        [ "example", "European Put (Put-Call)"
          "spot", sprintf "%.2f" spotPrice
          "strike", sprintf "%.2f" strikePrice
          "rate", sprintf "%.4f" riskFreeRate
          "volatility", sprintf "%.4f" volatility
          "expiry", sprintf "%.1f" timeToExpiry
          "price", sprintf "%.4f" put.Price
          "confidence_interval", sprintf "%.4f" put.ConfidenceInterval
          "qubits", sprintf "%d" put.QubitsUsed
          "method", put.Method
          "speedup", sprintf "%.1f" put.Speedup
          "error", "" ]
        |> Map.ofList)

| Error err, _ ->
    if not quiet then printfn "  Call pricing error: %A" err
| _, Error err ->
    if not quiet then printfn "  Put pricing error: %A" err

// ============================================================================
// EXAMPLE 3: Different Strike Prices (Moneyness)
// ============================================================================

if not quiet then
    printfn "═══ Example 3: Option Moneyness Analysis ═══"
    printfn ""

let strikes = [
    (90.0, "Deep In-the-Money")
    (100.0, "At-the-Money")
    (110.0, "Out-of-the-Money")
]

if not quiet then
    printfn "European Call Options at Different Strikes:"
    printfn "  (Spot = $%.2f)\n" spotPrice

for (strike, description) in strikes do
    let strikeResult =
        OptionPricing.priceEuropeanCall spotPrice strike riskFreeRate volatility timeToExpiry numQubits groverIterations shots backend
        |> Async.RunSynchronously

    match strikeResult with
    | Ok price ->
        if not quiet then
            printfn "  Strike $%.2f (%s):" strike description
            printfn "    Price: $%.4f ± $%.4f" price.Price price.ConfidenceInterval

        resultRows.Add(
            [ "example", sprintf "Moneyness %s" description
              "spot", sprintf "%.2f" spotPrice
              "strike", sprintf "%.2f" strike
              "rate", sprintf "%.4f" riskFreeRate
              "volatility", sprintf "%.4f" volatility
              "expiry", sprintf "%.1f" timeToExpiry
              "price", sprintf "%.4f" price.Price
              "confidence_interval", sprintf "%.4f" price.ConfidenceInterval
              "qubits", sprintf "%d" price.QubitsUsed
              "method", price.Method
              "speedup", sprintf "%.1f" price.Speedup
              "error", "" ]
            |> Map.ofList)
    | Error err ->
        if not quiet then printfn "  Strike $%.2f: Error %A" strike err

        resultRows.Add(
            [ "example", sprintf "Moneyness %s" description
              "spot", sprintf "%.2f" spotPrice
              "strike", sprintf "%.2f" strike
              "rate", sprintf "%.4f" riskFreeRate
              "volatility", sprintf "%.4f" volatility
              "expiry", sprintf "%.1f" timeToExpiry
              "price", ""
              "confidence_interval", ""
              "qubits", ""
              "method", ""
              "speedup", ""
              "error", sprintf "%A" err ]
            |> Map.ofList)

if not quiet then printfn ""

// ============================================================================
// EXAMPLE 4: Volatility Smile
// ============================================================================

if not quiet then
    printfn "═══ Example 4: Volatility Impact ═══"
    printfn ""

let volatilities = [ 0.1; 0.2; 0.3; 0.4 ]

if not quiet then
    printfn "Impact of Volatility on ATM Call Option:"
    printfn "  (Spot = Strike = $%.2f)\n" spotPrice

for vol in volatilities do
    let volResult =
        OptionPricing.priceEuropeanCall spotPrice spotPrice riskFreeRate vol timeToExpiry numQubits groverIterations shots backend
        |> Async.RunSynchronously

    match volResult with
    | Ok price ->
        if not quiet then
            printfn "  Volatility %2.0f%%: $%.4f" (vol * 100.0) price.Price

        resultRows.Add(
            [ "example", sprintf "Volatility %.0f%%" (vol * 100.0)
              "spot", sprintf "%.2f" spotPrice
              "strike", sprintf "%.2f" spotPrice
              "rate", sprintf "%.4f" riskFreeRate
              "volatility", sprintf "%.4f" vol
              "expiry", sprintf "%.1f" timeToExpiry
              "price", sprintf "%.4f" price.Price
              "confidence_interval", sprintf "%.4f" price.ConfidenceInterval
              "qubits", sprintf "%d" price.QubitsUsed
              "method", price.Method
              "speedup", sprintf "%.1f" price.Speedup
              "error", "" ]
            |> Map.ofList)
    | Error err ->
        if not quiet then printfn "  Volatility %2.0f%%: Error" (vol * 100.0)

if not quiet then
    printfn ""
    printfn "(Higher volatility → Higher option value)"
    printfn ""

// ============================================================================
// EXAMPLE 5: Advanced - Custom Parameters with Validation
// ============================================================================

if not quiet then
    printfn "═══ Example 5: Input Validation ═══"
    printfn ""

// Try invalid parameters to demonstrate validation
if not quiet then
    printfn "Testing input validation:"
    printfn ""

// Test negative spot
let invalidResult =
    OptionPricing.priceEuropeanCall (-100.0) 105.0 riskFreeRate volatility timeToExpiry numQubits groverIterations shots backend
    |> Async.RunSynchronously

match invalidResult with
| Error (QuantumError.ValidationError (param, msg)) ->
    if not quiet then
        printfn "  ✓ Correctly rejected negative spot price"
        printfn "    Parameter: %s" param
        printfn "    Message: %s" msg

    resultRows.Add(
        [ "example", "Validation (negative spot)"
          "spot", "-100.00"
          "strike", "105.00"
          "rate", sprintf "%.4f" riskFreeRate
          "volatility", sprintf "%.4f" volatility
          "expiry", sprintf "%.1f" timeToExpiry
          "price", ""
          "confidence_interval", ""
          "qubits", ""
          "method", ""
          "speedup", ""
          "error", sprintf "ValidationError(%s, %s)" param msg ]
        |> Map.ofList)
| _ ->
    if not quiet then printfn "  ✗ Should have rejected negative spot"

if not quiet then printfn ""

// ============================================================================
// EXAMPLE 6: Asian Options (Path-Dependent)
// ============================================================================

if not quiet then
    printfn "═══ Example 6: Asian Options ═══"
    printfn ""

let timeSteps = 12 // Monthly averaging

if not quiet then
    printfn "Asian Call Option (12 monthly observations):"
    printfn ""

let asianResult =
    OptionPricing.priceAsianCall
        spotPrice
        strikePrice
        riskFreeRate
        volatility
        timeToExpiry
        timeSteps
        numQubits
        groverIterations
        shots
        backend
    |> Async.RunSynchronously

match asianResult with
| Ok price ->
    if not quiet then
        printfn "  Price:    $%.4f ± $%.4f" price.Price price.ConfidenceInterval
        printfn "  Method:   %s" price.Method
        printfn "  Qubits:   %d" price.QubitsUsed

    resultRows.Add(
        [ "example", "Asian Call"
          "spot", sprintf "%.2f" spotPrice
          "strike", sprintf "%.2f" strikePrice
          "rate", sprintf "%.4f" riskFreeRate
          "volatility", sprintf "%.4f" volatility
          "expiry", sprintf "%.1f" timeToExpiry
          "price", sprintf "%.4f" price.Price
          "confidence_interval", sprintf "%.4f" price.ConfidenceInterval
          "qubits", sprintf "%d" price.QubitsUsed
          "method", price.Method
          "speedup", sprintf "%.1f" price.Speedup
          "error", "" ]
        |> Map.ofList)
| Error err ->
    if not quiet then printfn "  Error: %A" err

    resultRows.Add(
        [ "example", "Asian Call"
          "spot", sprintf "%.2f" spotPrice
          "strike", sprintf "%.2f" strikePrice
          "rate", sprintf "%.4f" riskFreeRate
          "volatility", sprintf "%.4f" volatility
          "expiry", sprintf "%.1f" timeToExpiry
          "price", ""
          "confidence_interval", ""
          "qubits", ""
          "method", ""
          "speedup", ""
          "error", sprintf "%A" err ]
        |> Map.ofList)

if not quiet then printfn ""

// ============================================================================
// SUMMARY
// ============================================================================

if not quiet then
    printfn "╔═══════════════════════════════════════════════════════════════╗"
    printfn "║   Summary                                                     ║"
    printfn "╚═══════════════════════════════════════════════════════════════╝"
    printfn ""
    printfn "QUANTUM ADVANTAGES:"
    printfn "  • Quadratic Speedup: O(1/ε) vs Classical O(1/ε²)"
    printfn "  • 100x faster for 1%% accuracy"
    printfn "  • Scales to complex multi-dimensional problems"
    printfn ""
    printfn "IMPLEMENTATION:"
    printfn "  • Möttönen state preparation (exact GBM encoding)"
    printfn "  • Grover-based amplitude estimation"
    printfn "  • Production-ready validation & error handling"
    printfn ""
    printfn "LIMITATIONS:"
    printfn "  • Payoff oracle uses MSB approximation (not exact)"
    printfn "  • Best for strikes near median price"
    printfn "  • 2-10 qubits (4-1024 price levels)"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let results = resultRows |> Seq.toList

match outputPath with
| Some path ->
    Reporting.writeJson path results
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header = [ "example"; "spot"; "strike"; "rate"; "volatility"; "expiry"; "price"; "confidence_interval"; "qubits"; "method"; "speedup"; "error" ]
    let rows =
        results |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if not quiet && outputPath.IsNone && csvPath.IsNone && argv.Length = 0 then
    printfn ""
    printfn "Hint: Customize this run with CLI options:"
    printfn "  dotnet fsi OptionPricing.fsx -- --spot 110 --strike 100 --volatility 0.3"
    printfn "  dotnet fsi OptionPricing.fsx -- --quiet --output results.json --csv results.csv"
    printfn "  dotnet fsi OptionPricing.fsx -- --help"

if not quiet then
    printfn ""
    printfn "✓ Example completed successfully!"
