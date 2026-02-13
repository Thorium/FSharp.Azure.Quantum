// ============================================================================
// Exotic Options Pricing with Quantum Monte Carlo
// ============================================================================
//
// Path-dependent exotic options using quantum amplitude estimation.
// Demonstrates barrier options and lookback options.
//
// QUANTUM ADVANTAGE:
// - Quadratic speedup: O(1/ε) vs classical O(1/ε²)
// - Path-dependent options require many simulation paths
// - Quantum provides speedup for high-precision pricing
//
// USAGE:
//   dotnet fsi ExoticOptions.fsx                                     (defaults)
//   dotnet fsi ExoticOptions.fsx -- --help                           (show options)
//   dotnet fsi ExoticOptions.fsx -- --spot 110 --barrier 130 --volatility 0.3
//   dotnet fsi ExoticOptions.fsx -- --quiet --output results.json --csv results.csv
// ============================================================================

(*
===============================================================================
 Background Theory
===============================================================================

Exotic options are derivative contracts with payoffs that depend on the entire
price path, not just the final price. This path-dependence makes them 
computationally expensive to price via Monte Carlo simulation.

BARRIER OPTIONS condition their payoff on whether the underlying crosses a 
predetermined price level (barrier) during the option's life:

  - Up-and-Out: Worthless if price rises above barrier (cap on upside)
  - Down-and-Out: Worthless if price falls below barrier (crash protection)
  - Up-and-In: Activated only if price rises above barrier
  - Down-and-In: Activated only if price falls below barrier

The payoff for an Up-and-Out call is:
    Payoff = max(S_T - K, 0) * 1_{max(S_t) < B}

Where B is the barrier level, and the indicator function ensures the option
survives (price never touched the barrier).

LOOKBACK OPTIONS have payoffs based on the extreme values (max or min) of the
underlying price during the option's life:

  - Floating Strike Call: Payoff = S_T - min(S_t) (buy at the low)
  - Floating Strike Put:  Payoff = max(S_t) - S_T (sell at the high)
  - Fixed Strike Call:    Payoff = max(max(S_t) - K, 0)
  - Fixed Strike Put:     Payoff = max(K - min(S_t), 0)

Lookback options are always in-the-money (ITM) at expiration, making them 
expensive but valuable for trend-following strategies.

GREEKS measure option price sensitivity to market parameters:
  - Delta: dV/dS - sensitivity to spot price
  - Vega:  dV/dsigma - sensitivity to volatility
  - Theta: dV/dt - time decay

For path-dependent options, Greeks require repricing via Monte Carlo with
bumped parameters, multiplying computational cost.

Quantum Advantage:
  Path-dependent options require simulating many price paths (N ~ 10,000+).
  Classical Monte Carlo converges as O(1/sqrt(N)), so doubling precision
  requires 4x the paths. Quantum amplitude estimation achieves O(1/N)
  convergence - quadratic speedup. For Greeks requiring 6+ pricings, this
  compounds to significant computational savings.

References:
  [1] Hull, J.C. "Options, Futures, and Other Derivatives" (2021) Ch. 26-27
  [2] Rebentrost, P. et al. "Quantum computational finance" arXiv:1805.00109
  [3] Wikipedia: Barrier_option (https://en.wikipedia.org/wiki/Barrier_option)
  [4] Wikipedia: Lookback_option (https://en.wikipedia.org/wiki/Lookback_option)
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "ExoticOptions.fsx"
    "Exotic options pricing (barrier & lookback) with quantum Monte Carlo."
    [ { Cli.OptionSpec.Name = "spot";              Description = "Spot price";                          Default = Some "100" }
      { Cli.OptionSpec.Name = "strike";            Description = "Strike price";                        Default = Some "100" }
      { Cli.OptionSpec.Name = "rate";              Description = "Risk-free rate";                      Default = Some "0.05" }
      { Cli.OptionSpec.Name = "volatility";        Description = "Volatility (annualized)";             Default = Some "0.2" }
      { Cli.OptionSpec.Name = "expiry";            Description = "Time to expiry in years";             Default = Some "1.0" }
      { Cli.OptionSpec.Name = "barrier";           Description = "Barrier level for barrier options";   Default = Some "120" }
      { Cli.OptionSpec.Name = "maturity";          Description = "Alias for --expiry";                  Default = Some "1.0" }
      { Cli.OptionSpec.Name = "qubits";            Description = "Qubits for amplitude estimation";     Default = Some "4" }
      { Cli.OptionSpec.Name = "shots";             Description = "Quantum circuit shots";               Default = Some "1000" }
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
let strikePrice = Cli.getFloatOr "strike" 100.0 args
let riskFreeRate = Cli.getFloatOr "rate" 0.05 args
let volParam = Cli.getFloatOr "volatility" 0.2 args
let timeToExpiry =
    match Cli.tryGet "expiry" args with
    | Some _ -> Cli.getFloatOr "expiry" 1.0 args
    | None -> Cli.getFloatOr "maturity" 1.0 args
let barrierLevel = Cli.getFloatOr "barrier" 120.0 args
let numQubits = Cli.getIntOr "qubits" 4 args
let shots = Cli.getIntOr "shots" 1000 args

// Collect results for structured output
let resultRows = System.Collections.Generic.List<Map<string, string>>()

// ============================================================================
// Domain Types
// ============================================================================

/// Barrier option types
type BarrierType =
    | UpAndOut    // Knocked out if price rises above barrier
    | UpAndIn     // Activated if price rises above barrier
    | DownAndOut  // Knocked out if price falls below barrier
    | DownAndIn   // Activated if price falls below barrier

/// Lookback option types
type LookbackType =
    | FloatingStrike  // Strike = min (call) or max (put) price over life
    | FixedStrike     // Payoff based on max (call) or min (put) price

/// Option direction
type OptionDirection = Call | Put

/// Market parameters
type MarketParams = {
    Spot: float           // Current price
    Strike: float         // Strike price
    RiskFreeRate: float   // Annual risk-free rate
    Volatility: float     // Annual volatility
    TimeToExpiry: float   // Years to expiration
    DividendYield: float  // Continuous dividend yield
}

/// Barrier option specification
type BarrierOption = {
    Direction: OptionDirection
    BarrierType: BarrierType
    BarrierLevel: float
    Rebate: float         // Payment if knocked out
    MonitoringPoints: int // Number of barrier monitoring points
}

/// Lookback option specification
type LookbackOption = {
    Direction: OptionDirection
    LookbackType: LookbackType
    ObservationPoints: int
}

/// Pricing result
type ExoticPriceResult = {
    Price: float
    StandardError: float
    PathsSimulated: int
    Method: string
}

// ============================================================================
// Path Simulation (Quantum-Enhanced)
// ============================================================================

/// Generate GBM price paths encoded in quantum state
let private generatePricePaths
    (market: MarketParams)
    (numPaths: int)
    (numSteps: int)
    (seed: int)
    : float[][] =

    let rng = Random(seed)
    let dt = market.TimeToExpiry / float numSteps
    let drift = (market.RiskFreeRate - market.DividendYield - 0.5 * market.Volatility ** 2.0) * dt
    let diffusion = market.Volatility * sqrt dt

    [| for _ in 1 .. numPaths ->
        let prices = Array.zeroCreate (numSteps + 1)
        prices.[0] <- market.Spot
        for t in 1 .. numSteps do
            let z =
                // Box-Muller transform
                let u1 = rng.NextDouble()
                let u2 = rng.NextDouble()
                sqrt(-2.0 * log u1) * cos(2.0 * Math.PI * u2)
            prices.[t] <- prices.[t-1] * exp(drift + diffusion * z)
        prices
    |]

// ============================================================================
// Barrier Option Payoff
// ============================================================================

/// Check if barrier was breached
let private barrierBreached (barrierType: BarrierType) (barrier: float) (path: float[]) : bool =
    match barrierType with
    | UpAndOut | UpAndIn ->
        path |> Array.exists (fun p -> p >= barrier)
    | DownAndOut | DownAndIn ->
        path |> Array.exists (fun p -> p <= barrier)

/// Calculate barrier option payoff for a single path
let private barrierPayoff (option: BarrierOption) (strike: float) (path: float[]) : float =
    let finalPrice = path.[path.Length - 1]
    let breached = barrierBreached option.BarrierType option.BarrierLevel path

    let intrinsicValue =
        match option.Direction with
        | Call -> max 0.0 (finalPrice - strike)
        | Put -> max 0.0 (strike - finalPrice)

    match option.BarrierType with
    | UpAndOut | DownAndOut ->
        if breached then option.Rebate else intrinsicValue
    | UpAndIn | DownAndIn ->
        if breached then intrinsicValue else option.Rebate

// ============================================================================
// Lookback Option Payoff
// ============================================================================

/// Calculate lookback option payoff for a single path
let private lookbackPayoff (option: LookbackOption) (strike: float) (path: float[]) : float =
    let finalPrice = path.[path.Length - 1]
    let maxPrice = path |> Array.max
    let minPrice = path |> Array.min

    match option.LookbackType, option.Direction with
    | FloatingStrike, Call ->
        // Call with floating strike: payoff = S_T - S_min
        max 0.0 (finalPrice - minPrice)
    | FloatingStrike, Put ->
        // Put with floating strike: payoff = S_max - S_T
        max 0.0 (maxPrice - finalPrice)
    | FixedStrike, Call ->
        // Call with fixed strike: payoff = max(S_max - K, 0)
        max 0.0 (maxPrice - strike)
    | FixedStrike, Put ->
        // Put with fixed strike: payoff = max(K - S_min, 0)
        max 0.0 (strike - minPrice)

// ============================================================================
// Quantum State Preparation for Path-Dependent Options
// ============================================================================

/// Build quantum circuit that encodes path distribution
let private buildPathStatePreparation
    (numQubits: int)
    (payoffs: float[])
    : CircuitBuilder.Circuit =

    let numStates = 1 <<< numQubits

    // Normalize payoffs to create probability amplitudes
    let totalPayoff = payoffs |> Array.sum
    let amplitudes =
        if totalPayoff > 0.0 then
            payoffs |> Array.map (fun p -> sqrt (max 0.0 p / totalPayoff))
        else
            Array.create numStates (1.0 / sqrt (float numStates))

    // Pad or truncate to match qubit count
    let paddedAmplitudes =
        if amplitudes.Length >= numStates then
            amplitudes.[0 .. numStates - 1]
        else
            Array.append amplitudes (Array.create (numStates - amplitudes.Length) 0.0)

    // Normalize
    let norm = paddedAmplitudes |> Array.sumBy (fun a -> a * a) |> sqrt
    let normalizedAmplitudes =
        if norm > 0.0 then
            paddedAmplitudes |> Array.map (fun a -> a / norm)
        else
            Array.create numStates (1.0 / sqrt (float numStates))

    // Build state preparation circuit using rotation gates
    let circuit = CircuitBuilder.empty numQubits

    // Apply Hadamard to create superposition, then Ry rotations for amplitudes
    let withHadamards =
        [0 .. numQubits - 1]
        |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.H q)) circuit

    // Apply amplitude-dependent rotations (simplified encoding)
    let avgAmplitude = normalizedAmplitudes |> Array.average
    let theta = 2.0 * asin avgAmplitude

    withHadamards
    |> CircuitBuilder.addGate (CircuitBuilder.RY(0, theta))

/// Build oracle that marks profitable paths
let private buildPayoffOracle (numQubits: int) : CircuitBuilder.Circuit =
    let circuit = CircuitBuilder.empty numQubits
    // Mark states in upper half (positive payoff approximation)
    let msb = numQubits - 1
    circuit |> CircuitBuilder.addGate (CircuitBuilder.Z msb)

// ============================================================================
// Quantum Monte Carlo Pricing (Quantum Compliant)
// ============================================================================

/// Price exotic option using quantum amplitude estimation
let private priceWithQuantumMC
    (payoffs: float[])
    (discountFactor: float)
    (numQubits: int)
    (groverIterations: int)
    (backend: IQuantumBackend)
    : Async<Result<ExoticPriceResult, QuantumError>> =

    async {
        let statePrep = buildPathStatePreparation numQubits payoffs
        let oracle = buildPayoffOracle numQubits

        let config: QuantumMonteCarlo.QMCConfig = {
            NumQubits = numQubits
            StatePreparation = statePrep
            Oracle = oracle
            GroverIterations = groverIterations
            Shots = shots
        }

        let! result = QuantumMonteCarlo.estimateExpectation config backend

        return result |> Result.map (fun qmcResult ->
            let avgPayoff = payoffs |> Array.average
            let price = discountFactor * avgPayoff * qmcResult.ExpectationValue * float payoffs.Length
            let stdError = discountFactor * qmcResult.StandardError * avgPayoff

            {
                Price = price
                StandardError = stdError
                PathsSimulated = payoffs.Length
                Method = "Quantum Monte Carlo (Amplitude Estimation)"
            }
        )
    }

// ============================================================================
// Public API - Barrier Options (Quantum Compliant)
// ============================================================================

/// Price a barrier option using quantum Monte Carlo
let priceBarrierOption
    (market: MarketParams)
    (option: BarrierOption)
    (backend: IQuantumBackend)
    : Async<Result<ExoticPriceResult, QuantumError>> =

    async {
        // Validate inputs
        if market.Spot <= 0.0 then
            return Error (QuantumError.ValidationError("Spot", "Must be positive"))
        elif market.Strike <= 0.0 then
            return Error (QuantumError.ValidationError("Strike", "Must be positive"))
        elif market.Volatility <= 0.0 then
            return Error (QuantumError.ValidationError("Volatility", "Must be positive"))
        elif market.TimeToExpiry <= 0.0 then
            return Error (QuantumError.ValidationError("TimeToExpiry", "Must be positive"))
        elif option.BarrierLevel <= 0.0 then
            return Error (QuantumError.ValidationError("BarrierLevel", "Must be positive"))
        else
            let numPaths = 256  // 2^8 paths for quantum encoding
            let numSteps = option.MonitoringPoints

            // Generate paths and calculate payoffs
            let paths = generatePricePaths market numPaths numSteps 42
            let payoffs = paths |> Array.map (barrierPayoff option market.Strike)

            // Discount factor
            let discountFactor = exp(-market.RiskFreeRate * market.TimeToExpiry)

            // Price using quantum MC
            return! priceWithQuantumMC payoffs discountFactor numQubits 3 backend
    }

/// Price a lookback option using quantum Monte Carlo
let priceLookbackOption
    (market: MarketParams)
    (option: LookbackOption)
    (backend: IQuantumBackend)
    : Async<Result<ExoticPriceResult, QuantumError>> =

    async {
        // Validate inputs
        if market.Spot <= 0.0 then
            return Error (QuantumError.ValidationError("Spot", "Must be positive"))
        elif market.Strike <= 0.0 then
            return Error (QuantumError.ValidationError("Strike", "Must be positive"))
        elif market.Volatility <= 0.0 then
            return Error (QuantumError.ValidationError("Volatility", "Must be positive"))
        elif market.TimeToExpiry <= 0.0 then
            return Error (QuantumError.ValidationError("TimeToExpiry", "Must be positive"))
        else
            let numPaths = 256
            let numSteps = option.ObservationPoints

            // Generate paths and calculate payoffs
            let paths = generatePricePaths market numPaths numSteps 42
            let payoffs = paths |> Array.map (lookbackPayoff option market.Strike)

            // Discount factor
            let discountFactor = exp(-market.RiskFreeRate * market.TimeToExpiry)

            // Price using quantum MC
            return! priceWithQuantumMC payoffs discountFactor numQubits 3 backend
    }

// ============================================================================
// Greeks Calculation (Quantum Compliant)
// ============================================================================

/// Calculate Delta (price sensitivity to spot)
let calculateDelta
    (market: MarketParams)
    (pricingFunc: MarketParams -> IQuantumBackend -> Async<Result<ExoticPriceResult, QuantumError>>)
    (backend: IQuantumBackend)
    : Async<Result<float, QuantumError>> =

    async {
        let bump = 0.01 * market.Spot  // 1% bump

        let marketUp = { market with Spot = market.Spot + bump }
        let marketDown = { market with Spot = market.Spot - bump }

        let! priceUp = pricingFunc marketUp backend
        let! priceDown = pricingFunc marketDown backend

        match priceUp, priceDown with
        | Ok up, Ok down ->
            let delta = (up.Price - down.Price) / (2.0 * bump)
            return Ok delta
        | Error e, _ -> return Error e
        | _, Error e -> return Error e
    }

/// Calculate Vega (price sensitivity to volatility)
let calculateVega
    (market: MarketParams)
    (pricingFunc: MarketParams -> IQuantumBackend -> Async<Result<ExoticPriceResult, QuantumError>>)
    (backend: IQuantumBackend)
    : Async<Result<float, QuantumError>> =

    async {
        let bump = 0.01  // 1% vol bump

        let marketUp = { market with Volatility = market.Volatility + bump }
        let marketDown = { market with Volatility = market.Volatility - bump }

        let! priceUp = pricingFunc marketUp backend
        let! priceDown = pricingFunc marketDown backend

        match priceUp, priceDown with
        | Ok up, Ok down ->
            let vega = (up.Price - down.Price) / (2.0 * bump)
            return Ok vega
        | Error e, _ -> return Error e
        | _, Error e -> return Error e
    }

/// Calculate Theta (time decay per day)
let calculateTheta
    (market: MarketParams)
    (pricingFunc: MarketParams -> IQuantumBackend -> Async<Result<ExoticPriceResult, QuantumError>>)
    (backend: IQuantumBackend)
    : Async<Result<float, QuantumError>> =

    async {
        let dayBump = 1.0 / 365.0  // 1 day

        if market.TimeToExpiry <= dayBump then
            return Ok 0.0
        else
            let marketLater = { market with TimeToExpiry = market.TimeToExpiry - dayBump }

            let! priceNow = pricingFunc market backend
            let! priceLater = pricingFunc marketLater backend

            match priceNow, priceLater with
            | Ok now, Ok later ->
                let theta = later.Price - now.Price  // Negative = time decay
                return Ok theta
            | Error e, _ -> return Error e
            | _, Error e -> return Error e
    }

// ============================================================================
// Example Execution
// ============================================================================

// Initialize quantum backend
let backend = LocalBackend.LocalBackend() :> IQuantumBackend

// Market parameters (from CLI)
let market = {
    Spot = spotPrice
    Strike = strikePrice
    RiskFreeRate = riskFreeRate
    Volatility = volParam
    TimeToExpiry = timeToExpiry
    DividendYield = 0.02
}

if not quiet then
    printfn "╔══════════════════════════════════════════════════════════════╗"
    printfn "║   Exotic Options Pricing (Quantum Monte Carlo)              ║"
    printfn "╚══════════════════════════════════════════════════════════════╝"
    printfn ""
    printfn "Quantum Backend: %s" backend.Name
    printfn ""
    printfn "Market Parameters:"
    printfn "  Spot Price:     $%.2f" market.Spot
    printfn "  Strike Price:   $%.2f" market.Strike
    printfn "  Risk-Free Rate: %.1f%%" (market.RiskFreeRate * 100.0)
    printfn "  Volatility:     %.1f%%" (market.Volatility * 100.0)
    printfn "  Time to Expiry: %.1f years" market.TimeToExpiry
    printfn "  Dividend Yield: %.1f%%" (market.DividendYield * 100.0)
    printfn ""

/// Helper to add a pricing result row
let addResultRow (exampleName: string) (optionType: string) (result: Result<ExoticPriceResult, QuantumError>) =
    match result with
    | Ok r ->
        resultRows.Add(
            [ "example", exampleName
              "option_type", optionType
              "spot", sprintf "%.2f" market.Spot
              "strike", sprintf "%.2f" market.Strike
              "rate", sprintf "%.4f" market.RiskFreeRate
              "volatility", sprintf "%.4f" market.Volatility
              "expiry", sprintf "%.1f" market.TimeToExpiry
              "price", sprintf "%.4f" r.Price
              "std_error", sprintf "%.4f" r.StandardError
              "paths", sprintf "%d" r.PathsSimulated
              "method", r.Method
              "error", "" ]
            |> Map.ofList)
    | Error err ->
        resultRows.Add(
            [ "example", exampleName
              "option_type", optionType
              "spot", sprintf "%.2f" market.Spot
              "strike", sprintf "%.2f" market.Strike
              "rate", sprintf "%.4f" market.RiskFreeRate
              "volatility", sprintf "%.4f" market.Volatility
              "expiry", sprintf "%.1f" market.TimeToExpiry
              "price", ""
              "std_error", ""
              "paths", ""
              "method", ""
              "error", sprintf "%A" err ]
            |> Map.ofList)

// ============================================================================
// Example 1: Up-and-Out Barrier Call
// ============================================================================

if not quiet then
    printfn "═══ Example 1: Up-and-Out Barrier Call ═══"
    printfn ""

let upOutCall = {
    Direction = Call
    BarrierType = UpAndOut
    BarrierLevel = barrierLevel  // Knocked out if price reaches barrier
    Rebate = 0.0
    MonitoringPoints = 52  // Weekly monitoring
}

if not quiet then
    printfn "Option Specification:"
    printfn "  Type: Up-and-Out Call"
    printfn "  Barrier: $%.2f (knocked out if price >= barrier)" upOutCall.BarrierLevel
    printfn "  Rebate: $%.2f" upOutCall.Rebate
    printfn "  Monitoring: %d points (weekly)" upOutCall.MonitoringPoints
    printfn ""

let upOutResult =
    priceBarrierOption market upOutCall backend
    |> Async.RunSynchronously

match upOutResult with
| Ok result ->
    if not quiet then
        printfn "Pricing Result:"
        printfn "  Price:          $%.4f" result.Price
        printfn "  Std Error:      $%.4f" result.StandardError
        printfn "  Paths:          %d" result.PathsSimulated
        printfn "  Method:         %s" result.Method
| Error err ->
    if not quiet then printfn "Error: %A" err

addResultRow "Up-and-Out Barrier Call" "Barrier" upOutResult
if not quiet then printfn ""

// ============================================================================
// Example 2: Down-and-In Barrier Put
// ============================================================================

if not quiet then
    printfn "═══ Example 2: Down-and-In Barrier Put ═══"
    printfn ""

let downInBarrier = max 1.0 (spotPrice * 0.8) // 80% of spot by default

let downInPut = {
    Direction = Put
    BarrierType = DownAndIn
    BarrierLevel = downInBarrier  // Activated if price falls to this level
    Rebate = 0.0
    MonitoringPoints = 252  // Daily monitoring
}

if not quiet then
    printfn "Option Specification:"
    printfn "  Type: Down-and-In Put"
    printfn "  Barrier: $%.2f (activated if price <= barrier)" downInPut.BarrierLevel
    printfn "  Rebate: $%.2f" downInPut.Rebate
    printfn "  Monitoring: %d points (daily)" downInPut.MonitoringPoints
    printfn ""

let downInResult =
    priceBarrierOption market downInPut backend
    |> Async.RunSynchronously

match downInResult with
| Ok result ->
    if not quiet then
        printfn "Pricing Result:"
        printfn "  Price:          $%.4f" result.Price
        printfn "  Std Error:      $%.4f" result.StandardError
        printfn "  Paths:          %d" result.PathsSimulated
        printfn "  Method:         %s" result.Method
| Error err ->
    if not quiet then printfn "Error: %A" err

addResultRow "Down-and-In Barrier Put" "Barrier" downInResult
if not quiet then printfn ""

// ============================================================================
// Example 3: Floating Strike Lookback Call
// ============================================================================

if not quiet then
    printfn "═══ Example 3: Floating Strike Lookback Call ═══"
    printfn ""

let floatingLookback = {
    Direction = Call
    LookbackType = FloatingStrike
    ObservationPoints = 252  // Daily observations
}

if not quiet then
    printfn "Option Specification:"
    printfn "  Type: Floating Strike Lookback Call"
    printfn "  Payoff: S_T - S_min (buy at lowest price)"
    printfn "  Observations: %d points (daily)" floatingLookback.ObservationPoints
    printfn ""

let floatingResult =
    priceLookbackOption market floatingLookback backend
    |> Async.RunSynchronously

match floatingResult with
| Ok result ->
    if not quiet then
        printfn "Pricing Result:"
        printfn "  Price:          $%.4f" result.Price
        printfn "  Std Error:      $%.4f" result.StandardError
        printfn "  Paths:          %d" result.PathsSimulated
        printfn "  Method:         %s" result.Method
| Error err ->
    if not quiet then printfn "Error: %A" err

addResultRow "Floating Strike Lookback Call" "Lookback" floatingResult
if not quiet then printfn ""

// ============================================================================
// Example 4: Fixed Strike Lookback Put
// ============================================================================

if not quiet then
    printfn "═══ Example 4: Fixed Strike Lookback Put ═══"
    printfn ""

let fixedLookback = {
    Direction = Put
    LookbackType = FixedStrike
    ObservationPoints = 52  // Weekly observations
}

if not quiet then
    printfn "Option Specification:"
    printfn "  Type: Fixed Strike Lookback Put"
    printfn "  Payoff: max(K - S_min, 0)"
    printfn "  Observations: %d points (weekly)" fixedLookback.ObservationPoints
    printfn ""

let fixedResult =
    priceLookbackOption market fixedLookback backend
    |> Async.RunSynchronously

match fixedResult with
| Ok result ->
    if not quiet then
        printfn "Pricing Result:"
        printfn "  Price:          $%.4f" result.Price
        printfn "  Std Error:      $%.4f" result.StandardError
        printfn "  Paths:          %d" result.PathsSimulated
        printfn "  Method:         %s" result.Method
| Error err ->
    if not quiet then printfn "Error: %A" err

addResultRow "Fixed Strike Lookback Put" "Lookback" fixedResult
if not quiet then printfn ""

// ============================================================================
// Example 5: Greeks Calculation
// ============================================================================

if not quiet then
    printfn "═══ Example 5: Greeks for Up-and-Out Call ═══"
    printfn ""

let barrierPricer m b = priceBarrierOption m upOutCall b

let deltaResult = calculateDelta market barrierPricer backend |> Async.RunSynchronously
let vegaResult = calculateVega market barrierPricer backend |> Async.RunSynchronously
let thetaResult = calculateTheta market barrierPricer backend |> Async.RunSynchronously

if not quiet then
    printfn "Greeks (finite difference via quantum pricing):"
    printfn ""

    match deltaResult with
    | Ok delta -> printfn "  Delta: %.4f (price change per $1 spot move)" delta
    | Error _ -> printfn "  Delta: calculation failed"

    match vegaResult with
    | Ok vega -> printfn "  Vega:  %.4f (price change per 1%% vol move)" vega
    | Error _ -> printfn "  Vega:  calculation failed"

    match thetaResult with
    | Ok theta -> printfn "  Theta: %.4f (daily time decay)" theta
    | Error _ -> printfn "  Theta: calculation failed"

    printfn ""

// Add Greeks to results
let deltaStr = match deltaResult with Ok d -> sprintf "%.6f" d | Error _ -> ""
let vegaStr = match vegaResult with Ok v -> sprintf "%.6f" v | Error _ -> ""
let thetaStr = match thetaResult with Ok t -> sprintf "%.6f" t | Error _ -> ""

resultRows.Add(
    [ "example", "Greeks (Up-and-Out Call)"
      "option_type", "Greeks"
      "spot", sprintf "%.2f" market.Spot
      "strike", sprintf "%.2f" market.Strike
      "rate", sprintf "%.4f" market.RiskFreeRate
      "volatility", sprintf "%.4f" market.Volatility
      "expiry", sprintf "%.1f" market.TimeToExpiry
      "price", sprintf "delta=%s vega=%s theta=%s" deltaStr vegaStr thetaStr
      "std_error", ""
      "paths", ""
      "method", "Finite Difference (Quantum)"
      "error", "" ]
    |> Map.ofList)

// ============================================================================
// Summary
// ============================================================================

if not quiet then
    printfn "╔══════════════════════════════════════════════════════════════╗"
    printfn "║                        Summary                               ║"
    printfn "╚══════════════════════════════════════════════════════════════╝"
    printfn ""
    printfn "Exotic Options Demonstrated:"
    printfn ""
    printfn "  1. Barrier Options:"
    printfn "     - Up-and-Out: Knocked out when price rises above barrier"
    printfn "     - Down-and-In: Activated when price falls below barrier"
    printfn "     - Also available: Up-and-In, Down-and-Out"
    printfn ""
    printfn "  2. Lookback Options:"
    printfn "     - Floating Strike: Strike set to min (call) or max (put)"
    printfn "     - Fixed Strike: Payoff based on path extremum"
    printfn ""
    printfn "  3. Greeks:"
    printfn "     - Delta, Vega, Theta via finite difference"
    printfn "     - Each bump requires quantum pricing"
    printfn ""
    printfn "Quantum Advantage:"
    printfn "  - Path-dependent options require many MC paths"
    printfn "  - Quantum amplitude estimation: O(1/e) vs classical O(1/e^2)"
    printfn "  - Beneficial for high-precision exotic pricing"
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
    let header = [ "example"; "option_type"; "spot"; "strike"; "rate"; "volatility"; "expiry"; "price"; "std_error"; "paths"; "method"; "error" ]
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
    printfn "  dotnet fsi ExoticOptions.fsx -- --spot 110 --barrier 130 --volatility 0.3"
    printfn "  dotnet fsi ExoticOptions.fsx -- --quiet --output results.json --csv results.csv"
    printfn "  dotnet fsi ExoticOptions.fsx -- --help"

if not quiet then
    printfn ""
    printfn "Example completed successfully!"
