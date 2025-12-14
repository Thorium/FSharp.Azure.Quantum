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
//   dotnet fsi QuantumOptionPricing.fsx
// ============================================================================

#r "nuget: FSharp.Azure.Quantum"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

printfn "╔═══════════════════════════════════════════════════════════════╗"
printfn "║   Quantum Monte Carlo Option Pricing                         ║"
printfn "║   Using FSharp.Azure.Quantum                                  ║"
printfn "╚═══════════════════════════════════════════════════════════════╝"
printfn ""

// ============================================================================
// EXAMPLE 1: Price European Call Option
// ============================================================================

printfn "═══ Example 1: European Call Option ═══"
printfn ""

// Market parameters
let spotPrice = 100.0      // Current stock price
let strikePrice = 105.0    // Strike price (slightly out-of-the-money)
let riskFreeRate = 0.05    // 5% risk-free rate
let volatility = 0.2       // 20% volatility
let timeToExpiry = 1.0     // 1 year

printfn "Market Parameters:"
printfn "  Spot Price (S₀):    $%.2f" spotPrice
printfn "  Strike Price (K):   $%.2f" strikePrice
printfn "  Risk-free Rate (r): %.1f%%" (riskFreeRate * 100.0)
printfn "  Volatility (σ):     %.1f%%" (volatility * 100.0)
printfn "  Time to Expiry (T): %.1f year" timeToExpiry
printfn ""

// Use local quantum simulator
let backend = LocalBackend.LocalBackend() :> IQuantumBackend

printfn "Using LocalBackend (quantum simulator)..."
printfn "Running quantum Monte Carlo..."
printfn ""

// Price the option
let result = 
    OptionPricing.priceEuropeanCall 
        spotPrice 
        strikePrice 
        riskFreeRate 
        volatility 
        timeToExpiry 
        backend
    |> Async.RunSynchronously

match result with
| Ok price ->
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

| Error err ->
    printfn "✗ Error: %A" err
    printfn ""

// ============================================================================
// EXAMPLE 2: Compare Call vs Put Options
// ============================================================================

printfn "═══ Example 2: Put-Call Comparison ═══"
printfn ""

let priceBothOptions spot strike =
    async {
        let! callResult = OptionPricing.priceEuropeanCall spot strike riskFreeRate volatility timeToExpiry backend
        let! putResult = OptionPricing.priceEuropeanPut spot strike riskFreeRate volatility timeToExpiry backend
        
        return (callResult, putResult)
    }

let (callPrice, putPrice) = 
    priceBothOptions spotPrice strikePrice 
    |> Async.RunSynchronously

printfn "Comparing European Call vs Put (Same strike):"
printfn ""

match callPrice, putPrice with
| Ok call, Ok put ->
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

| Error err, _ ->
    printfn "  Call pricing error: %A" err
| _, Error err ->
    printfn "  Put pricing error: %A" err

// ============================================================================
// EXAMPLE 3: Different Strike Prices (Moneyness)
// ============================================================================

printfn "═══ Example 3: Option Moneyness Analysis ═══"
printfn ""

let strikes = [
    (90.0, "Deep In-the-Money")
    (100.0, "At-the-Money")
    (110.0, "Out-of-the-Money")
]

printfn "European Call Options at Different Strikes:"
printfn "  (Spot = $%.2f)\n" spotPrice

for (strike, description) in strikes do
    let result = 
        OptionPricing.priceEuropeanCall spotPrice strike riskFreeRate volatility timeToExpiry backend
        |> Async.RunSynchronously
    
    match result with
    | Ok price ->
        printfn "  Strike $%.2f (%s):" strike description
        printfn "    Price: $%.4f ± $%.4f" price.Price price.ConfidenceInterval
    | Error err ->
        printfn "  Strike $%.2f: Error %A" strike err

printfn ""

// ============================================================================
// EXAMPLE 4: Volatility Smile
// ============================================================================

printfn "═══ Example 4: Volatility Impact ═══"
printfn ""

let volatilities = [ 0.1; 0.2; 0.3; 0.4 ]

printfn "Impact of Volatility on ATM Call Option:"
printfn "  (Spot = Strike = $%.2f)\n" spotPrice

for vol in volatilities do
    let result = 
        OptionPricing.priceEuropeanCall spotPrice spotPrice riskFreeRate vol timeToExpiry backend
        |> Async.RunSynchronously
    
    match result with
    | Ok price ->
        printfn "  Volatility %2.0f%%: $%.4f" (vol * 100.0) price.Price
    | Error err ->
        printfn "  Volatility %2.0f%%: Error" (vol * 100.0)

printfn ""
printfn "(Higher volatility → Higher option value)"
printfn ""

// ============================================================================
// EXAMPLE 5: Advanced - Custom Parameters with Validation
// ============================================================================

printfn "═══ Example 5: Input Validation ═══"
printfn ""

// Try invalid parameters to demonstrate validation
let invalidParams = [
    (-100.0, 105.0, "Negative spot price", 0.0)
    (100.0, -105.0, "Negative strike price", 0.0)
    (100.0, 105.0, "Zero volatility", 0.0)
]

printfn "Testing input validation:"
printfn ""

// Test negative spot
let invalidResult = 
    OptionPricing.priceEuropeanCall (-100.0) 105.0 riskFreeRate volatility timeToExpiry backend
    |> Async.RunSynchronously

match invalidResult with
| Error (QuantumError.ValidationError (param, msg)) ->
    printfn "  ✓ Correctly rejected negative spot price"
    printfn "    Parameter: %s" param
    printfn "    Message: %s" msg
| _ ->
    printfn "  ✗ Should have rejected negative spot"

printfn ""

// ============================================================================
// EXAMPLE 6: Asian Options (Path-Dependent)
// ============================================================================

printfn "═══ Example 6: Asian Options ═══"
printfn ""

let timeSteps = 12 // Monthly averaging

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
        backend
    |> Async.RunSynchronously

match asianResult with
| Ok price ->
    printfn "  Price:    $%.4f ± $%.4f" price.Price price.ConfidenceInterval
    printfn "  Method:   %s" price.Method
    printfn "  Qubits:   %d" price.QubitsUsed
| Error err ->
    printfn "  Error: %A" err

printfn ""

// ============================================================================
// SUMMARY
// ============================================================================

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
printfn "NEXT STEPS:"
printfn "  • Deploy to Azure Quantum (IonQ, Rigetti hardware)"
printfn "  • Implement exact comparison oracle"
printfn "  • Add more exotic option types"
printfn "  • Full Quantum Amplitude Estimation (QAE)"
printfn ""
printfn "For more information, see:"
printfn "  • FSharp.Azure.Quantum Documentation"
printfn "  • Rebentrost et al., Phys. Rev. A 98, 022321 (2018)"
printfn ""
printfn "✓ Example completed successfully!"
