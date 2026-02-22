// ==============================================================================
// Exotic Options Pricing with Quantum Monte Carlo
// ==============================================================================
// Path-dependent exotic options using quantum amplitude estimation.
// Compares barrier options and lookback options across configurable specs,
// computing prices and Greeks via quantum Monte Carlo.
//
// Usage:
//   dotnet fsi ExoticOptions.fsx                                  (defaults)
//   dotnet fsi ExoticOptions.fsx -- --help                        (show options)
//   dotnet fsi ExoticOptions.fsx -- --options up-out-call,lookback-float-call
//   dotnet fsi ExoticOptions.fsx -- --spot 110 --barrier 130 --volatility 0.3
//   dotnet fsi ExoticOptions.fsx -- --input custom-options.csv
//   dotnet fsi ExoticOptions.fsx -- --quiet --output results.json --csv out.csv
//
// References:
//   [1] Hull, "Options, Futures, and Other Derivatives" (2021) Ch. 26-27
//   [2] Rebentrost et al., "Quantum computational finance" arXiv:1805.00109
//   [3] https://en.wikipedia.org/wiki/Barrier_option
//   [4] https://en.wikipedia.org/wiki/Lookback_option
// ==============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
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
    [ { Cli.OptionSpec.Name = "options";           Description = "Comma-separated option keys to include";  Default = None }
      { Cli.OptionSpec.Name = "input";             Description = "CSV file with custom option definitions";  Default = None }
      { Cli.OptionSpec.Name = "spot";              Description = "Spot price";                               Default = Some "100" }
      { Cli.OptionSpec.Name = "strike";            Description = "Strike price";                             Default = Some "100" }
      { Cli.OptionSpec.Name = "rate";              Description = "Risk-free rate";                           Default = Some "0.05" }
      { Cli.OptionSpec.Name = "volatility";        Description = "Volatility (annualized)";                  Default = Some "0.2" }
      { Cli.OptionSpec.Name = "expiry";            Description = "Time to expiry in years";                  Default = Some "1.0" }
      { Cli.OptionSpec.Name = "barrier";           Description = "Barrier level for barrier options";        Default = Some "120" }
      { Cli.OptionSpec.Name = "qubits";            Description = "Qubits for amplitude estimation";          Default = Some "4" }
      { Cli.OptionSpec.Name = "shots";             Description = "Quantum circuit shots";                    Default = Some "1000" }
      { Cli.OptionSpec.Name = "output";            Description = "Write results to JSON file";               Default = None }
      { Cli.OptionSpec.Name = "csv";               Description = "Write results to CSV file";                Default = None }
      { Cli.OptionSpec.Name = "quiet";             Description = "Suppress informational output";            Default = None } ]
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
let timeToExpiry = Cli.getFloatOr "expiry" 1.0 args
let barrierLevel = Cli.getFloatOr "barrier" 120.0 args
let numQubits = Cli.getIntOr "qubits" 4 args
let shots = Cli.getIntOr "shots" 1000 args

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

type BarrierType = UpAndOut | UpAndIn | DownAndOut | DownAndIn

type LookbackType = FloatingStrike | FixedStrike

type OptionDirection = Call | Put

type MarketParams = {
    Spot: float
    Strike: float
    RiskFreeRate: float
    Volatility: float
    TimeToExpiry: float
    DividendYield: float
}

type BarrierOption = {
    Direction: OptionDirection
    BarrierType: BarrierType
    BarrierLevel: float
    Rebate: float
    MonitoringPoints: int
}

type LookbackOption = {
    Direction: OptionDirection
    LookbackType: LookbackType
    ObservationPoints: int
}

type ExoticPriceResult = {
    Price: float
    StandardError: float
    PathsSimulated: int
    Method: string
}

/// Unified option specification for data-driven processing
type ExoticOptionSpec =
    | BarrierSpec of key: string * name: string * BarrierOption
    | LookbackSpec of key: string * name: string * LookbackOption

/// Per-option result including Greeks
type OptionResult = {
    Key: string
    Name: string
    OptionType: string
    Price: float
    StdError: float
    Delta: float
    Vega: float
    Theta: float
    HasQuantumFailure: bool
}

// ==============================================================================
// BUILT-IN OPTION PRESETS
// ==============================================================================

let private presetUpOutCall =
    BarrierSpec(
        "up-out-call", "Up-and-Out Barrier Call",
        { Direction = Call; BarrierType = UpAndOut; BarrierLevel = barrierLevel
          Rebate = 0.0; MonitoringPoints = 52 })

let private presetDownInPut =
    BarrierSpec(
        "down-in-put", "Down-and-In Barrier Put",
        { Direction = Put; BarrierType = DownAndIn; BarrierLevel = max 1.0 (spotPrice * 0.8)
          Rebate = 0.0; MonitoringPoints = 252 })

let private presetLookbackFloatCall =
    LookbackSpec(
        "lookback-float-call", "Floating Strike Lookback Call",
        { Direction = Call; LookbackType = FloatingStrike; ObservationPoints = 252 })

let private presetLookbackFixedPut =
    LookbackSpec(
        "lookback-fixed-put", "Fixed Strike Lookback Put",
        { Direction = Put; LookbackType = FixedStrike; ObservationPoints = 52 })

let private specKey = function
    | BarrierSpec(k, _, _) -> k
    | LookbackSpec(k, _, _) -> k

let private builtInOptions =
    [ presetUpOutCall; presetDownInPut; presetLookbackFloatCall; presetLookbackFixedPut ]
    |> List.map (fun s -> (specKey s).ToLowerInvariant(), s)
    |> Map.ofList

// ==============================================================================
// CSV LOADING
// ==============================================================================

let private parseDirection (s: string) =
    match s.Trim().ToLowerInvariant() with
    | "put" -> Put
    | _ -> Call

let private parseBarrierType (s: string) =
    match s.Trim().ToLowerInvariant() with
    | "upandout" | "up-and-out" | "up_out" -> UpAndOut
    | "upandin" | "up-and-in" | "up_in" -> UpAndIn
    | "downandout" | "down-and-out" | "down_out" -> DownAndOut
    | "downandin" | "down-and-in" | "down_in" -> DownAndIn
    | _ -> UpAndOut

let private parseLookbackType (s: string) =
    match s.Trim().ToLowerInvariant() with
    | "fixed" | "fixedstrike" | "fixed_strike" -> FixedStrike
    | _ -> FloatingStrike

let private loadOptionsFromCsv (filePath: string) : ExoticOptionSpec list =
    let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ filePath
    let rows, errors = Data.readCsvWithHeaderWithErrors resolved
    if not (List.isEmpty errors) then
        eprintfn "WARNING: CSV parse errors in %s:" filePath
        errors |> List.iter (eprintfn "  %s")
    if rows.IsEmpty then failwithf "No valid rows in CSV %s" filePath
    rows |> List.mapi (fun i row ->
        let get key = row.Values |> Map.tryFind key |> Option.defaultValue ""
        match get "preset" with
        | p when not (String.IsNullOrWhiteSpace p) ->
            match builtInOptions |> Map.tryFind (p.Trim().ToLowerInvariant()) with
            | Some s -> s
            | None -> failwithf "Unknown preset '%s' in CSV row %d" p (i + 1)
        | _ ->
            let key = let k = get "key" in if k = "" then sprintf "custom-%d" (i + 1) else k
            let name = let n = get "name" in if n = "" then key else n
            let optType = get "option_type"
            match optType.Trim().ToLowerInvariant() with
            | "lookback" ->
                LookbackSpec(key, name,
                    { Direction = get "direction" |> parseDirection
                      LookbackType = get "lookback_type" |> parseLookbackType
                      ObservationPoints = get "observation_points" |> fun s -> match Int32.TryParse s with true, v -> v | _ -> 252 })
            | _ ->
                BarrierSpec(key, name,
                    { Direction = get "direction" |> parseDirection
                      BarrierType = get "barrier_type" |> parseBarrierType
                      BarrierLevel = get "barrier_level" |> fun s -> match Double.TryParse s with true, v -> v | _ -> barrierLevel
                      Rebate = get "rebate" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 0.0
                      MonitoringPoints = get "monitoring_points" |> fun s -> match Int32.TryParse s with true, v -> v | _ -> 52 }))

// ==============================================================================
// OPTION SELECTION
// ==============================================================================

let selectedOptions =
    let base' =
        match Cli.tryGet "input" args with
        | Some csvFile -> loadOptionsFromCsv csvFile
        | None -> builtInOptions |> Map.toList |> List.map snd

    match Cli.getCommaSeparated "options" args with
    | [] -> base'
    | filter ->
        let filterSet = filter |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        base' |> List.filter (fun s -> filterSet.Contains((specKey s).ToLowerInvariant()))

if selectedOptions.IsEmpty then
    eprintfn "ERROR: No options selected. Check --options filter or --input CSV."
    exit 1

// ==============================================================================
// MARKET PARAMETERS
// ==============================================================================

let market = {
    Spot = spotPrice
    Strike = strikePrice
    RiskFreeRate = riskFreeRate
    Volatility = volParam
    TimeToExpiry = timeToExpiry
    DividendYield = 0.02
}

if not quiet then
    printfn "Exotic Options Pricing (Quantum Monte Carlo)"
    printfn "Options: %d  Spot: $%.2f  Strike: $%.2f  Vol: %.1f%%  Expiry: %.1fy"
        selectedOptions.Length market.Spot market.Strike (market.Volatility * 100.0) market.TimeToExpiry
    printfn ""

// ==============================================================================
// PATH SIMULATION
// ==============================================================================

let private generatePricePaths (market: MarketParams) (numPaths: int) (numSteps: int) (seed: int) : float[][] =
    let rng = Random(seed)
    let dt = market.TimeToExpiry / float numSteps
    let drift = (market.RiskFreeRate - market.DividendYield - 0.5 * market.Volatility ** 2.0) * dt
    let diffusion = market.Volatility * sqrt dt

    [| for _ in 1 .. numPaths ->
        let prices = Array.zeroCreate (numSteps + 1)
        prices.[0] <- market.Spot
        for t in 1 .. numSteps do
            let u1 = rng.NextDouble()
            let u2 = rng.NextDouble()
            let z = sqrt(-2.0 * log u1) * cos(2.0 * Math.PI * u2)
            prices.[t] <- prices.[t-1] * exp(drift + diffusion * z)
        prices
    |]

// ==============================================================================
// PAYOFF FUNCTIONS
// ==============================================================================

let private barrierBreached (barrierType: BarrierType) (barrier: float) (path: float[]) : bool =
    match barrierType with
    | UpAndOut | UpAndIn -> path |> Array.exists (fun p -> p >= barrier)
    | DownAndOut | DownAndIn -> path |> Array.exists (fun p -> p <= barrier)

let private barrierPayoff (option: BarrierOption) (strike: float) (path: float[]) : float =
    let finalPrice = path.[path.Length - 1]
    let breached = barrierBreached option.BarrierType option.BarrierLevel path
    let intrinsicValue =
        match option.Direction with
        | Call -> max 0.0 (finalPrice - strike)
        | Put -> max 0.0 (strike - finalPrice)
    match option.BarrierType with
    | UpAndOut | DownAndOut -> if breached then option.Rebate else intrinsicValue
    | UpAndIn | DownAndIn -> if breached then intrinsicValue else option.Rebate

let private lookbackPayoff (option: LookbackOption) (strike: float) (path: float[]) : float =
    let finalPrice = path.[path.Length - 1]
    let maxPrice = path |> Array.max
    let minPrice = path |> Array.min
    match option.LookbackType, option.Direction with
    | FloatingStrike, Call -> max 0.0 (finalPrice - minPrice)
    | FloatingStrike, Put -> max 0.0 (maxPrice - finalPrice)
    | FixedStrike, Call -> max 0.0 (maxPrice - strike)
    | FixedStrike, Put -> max 0.0 (strike - minPrice)

// ==============================================================================
// QUANTUM PRICING
// ==============================================================================

let backend = LocalBackend.LocalBackend() :> IQuantumBackend

let private buildPathStatePreparation (numQubits: int) (payoffs: float[]) : CircuitBuilder.Circuit =
    let numStates = 1 <<< numQubits
    let totalPayoff = payoffs |> Array.sum
    let amplitudes =
        if totalPayoff > 0.0 then
            payoffs |> Array.map (fun p -> sqrt (max 0.0 p / totalPayoff))
        else
            Array.create numStates (1.0 / sqrt (float numStates))
    let paddedAmplitudes =
        if amplitudes.Length >= numStates then amplitudes.[0 .. numStates - 1]
        else Array.append amplitudes (Array.create (numStates - amplitudes.Length) 0.0)
    let norm = paddedAmplitudes |> Array.sumBy (fun a -> a * a) |> sqrt
    let normalizedAmplitudes =
        if norm > 0.0 then paddedAmplitudes |> Array.map (fun a -> a / norm)
        else Array.create numStates (1.0 / sqrt (float numStates))

    let circuit = CircuitBuilder.empty numQubits
    let withHadamards =
        [0 .. numQubits - 1]
        |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.H q)) circuit
    let avgAmplitude = normalizedAmplitudes |> Array.average
    let theta = 2.0 * asin avgAmplitude
    withHadamards |> CircuitBuilder.addGate (CircuitBuilder.RY(0, theta))

let private buildPayoffOracle (numQubits: int) : CircuitBuilder.Circuit =
    let circuit = CircuitBuilder.empty numQubits
    let msb = numQubits - 1
    circuit |> CircuitBuilder.addGate (CircuitBuilder.Z msb)

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
            { Price = price; StandardError = stdError
              PathsSimulated = payoffs.Length
              Method = "Quantum Monte Carlo (Amplitude Estimation)" })
    }

// ==============================================================================
// PRICING FUNCTIONS (barrier / lookback)
// ==============================================================================

let private priceBarrierOption (market: MarketParams) (option: BarrierOption) (backend: IQuantumBackend)
    : Async<Result<ExoticPriceResult, QuantumError>> =
    async {
        if market.Spot <= 0.0 then return Error (QuantumError.ValidationError("Spot", "Must be positive"))
        elif market.Strike <= 0.0 then return Error (QuantumError.ValidationError("Strike", "Must be positive"))
        elif market.Volatility <= 0.0 then return Error (QuantumError.ValidationError("Volatility", "Must be positive"))
        elif market.TimeToExpiry <= 0.0 then return Error (QuantumError.ValidationError("TimeToExpiry", "Must be positive"))
        elif option.BarrierLevel <= 0.0 then return Error (QuantumError.ValidationError("BarrierLevel", "Must be positive"))
        else
            let paths = generatePricePaths market 256 option.MonitoringPoints 42
            let payoffs = paths |> Array.map (barrierPayoff option market.Strike)
            let discountFactor = exp(-market.RiskFreeRate * market.TimeToExpiry)
            return! priceWithQuantumMC payoffs discountFactor numQubits 3 backend
    }

let private priceLookbackOption (market: MarketParams) (option: LookbackOption) (backend: IQuantumBackend)
    : Async<Result<ExoticPriceResult, QuantumError>> =
    async {
        if market.Spot <= 0.0 then return Error (QuantumError.ValidationError("Spot", "Must be positive"))
        elif market.Strike <= 0.0 then return Error (QuantumError.ValidationError("Strike", "Must be positive"))
        elif market.Volatility <= 0.0 then return Error (QuantumError.ValidationError("Volatility", "Must be positive"))
        elif market.TimeToExpiry <= 0.0 then return Error (QuantumError.ValidationError("TimeToExpiry", "Must be positive"))
        else
            let paths = generatePricePaths market 256 option.ObservationPoints 42
            let payoffs = paths |> Array.map (lookbackPayoff option market.Strike)
            let discountFactor = exp(-market.RiskFreeRate * market.TimeToExpiry)
            return! priceWithQuantumMC payoffs discountFactor numQubits 3 backend
    }

/// Unified pricing dispatch
let private priceOption (spec: ExoticOptionSpec) (m: MarketParams) (b: IQuantumBackend) =
    match spec with
    | BarrierSpec(_, _, opt) -> priceBarrierOption m opt b
    | LookbackSpec(_, _, opt) -> priceLookbackOption m opt b

// ==============================================================================
// GREEKS (finite difference via quantum pricing)
// ==============================================================================

let private calculateDelta (market: MarketParams) (spec: ExoticOptionSpec) (backend: IQuantumBackend) : Async<Result<float, QuantumError>> =
    async {
        let bump = 0.01 * market.Spot
        let! priceUp = priceOption spec { market with Spot = market.Spot + bump } backend
        let! priceDown = priceOption spec { market with Spot = market.Spot - bump } backend
        match priceUp, priceDown with
        | Ok up, Ok down -> return Ok ((up.Price - down.Price) / (2.0 * bump))
        | Error e, _ | _, Error e -> return Error e
    }

let private calculateVega (market: MarketParams) (spec: ExoticOptionSpec) (backend: IQuantumBackend) : Async<Result<float, QuantumError>> =
    async {
        let bump = 0.01
        let! priceUp = priceOption spec { market with Volatility = market.Volatility + bump } backend
        let! priceDown = priceOption spec { market with Volatility = market.Volatility - bump } backend
        match priceUp, priceDown with
        | Ok up, Ok down -> return Ok ((up.Price - down.Price) / (2.0 * bump))
        | Error e, _ | _, Error e -> return Error e
    }

let private calculateTheta (market: MarketParams) (spec: ExoticOptionSpec) (backend: IQuantumBackend) : Async<Result<float, QuantumError>> =
    async {
        let dayBump = 1.0 / 365.0
        if market.TimeToExpiry <= dayBump then return Ok 0.0
        else
            let! priceNow = priceOption spec market backend
            let! priceLater = priceOption spec { market with TimeToExpiry = market.TimeToExpiry - dayBump } backend
            match priceNow, priceLater with
            | Ok now, Ok later -> return Ok (later.Price - now.Price)
            | Error e, _ | _, Error e -> return Error e
    }

// ==============================================================================
// PER-OPTION PROCESSING
// ==============================================================================

if not quiet then printfn "Pricing %d exotic options with quantum Monte Carlo..." selectedOptions.Length

let mutable anyQuantumFailure = false

let optionResults =
    selectedOptions
    |> List.map (fun spec ->
        let key = specKey spec
        let name = match spec with BarrierSpec(_, n, _) -> n | LookbackSpec(_, n, _) -> n
        let optType = match spec with BarrierSpec _ -> "Barrier" | LookbackSpec _ -> "Lookback"

        // Price
        let priceResult = priceOption spec market backend |> Async.RunSynchronously

        match priceResult with
        | Ok pr ->
            // Greeks
            let deltaR = calculateDelta market spec backend |> Async.RunSynchronously
            let vegaR = calculateVega market spec backend |> Async.RunSynchronously
            let thetaR = calculateTheta market spec backend |> Async.RunSynchronously
            let delta = match deltaR with Ok d -> d | Error _ -> nan
            let vega = match vegaR with Ok v -> v | Error _ -> nan
            let theta = match thetaR with Ok t -> t | Error _ -> nan
            if not quiet then
                printfn "  [OK]   %-35s  Price: $%8.4f  Delta: %7.4f  Vega: %7.4f  Theta: %7.4f"
                    name pr.Price delta vega theta
            { Key = key; Name = name; OptionType = optType
              Price = pr.Price; StdError = pr.StandardError
              Delta = delta; Vega = vega; Theta = theta
              HasQuantumFailure = false }
        | Error err ->
            anyQuantumFailure <- true
            if not quiet then printfn "  [FAIL] %-35s  Error: %A" name err
            { Key = key; Name = name; OptionType = optType
              Price = nan; StdError = nan
              Delta = nan; Vega = nan; Theta = nan
              HasQuantumFailure = true })

if not quiet then printfn ""

// Sort by price descending (most expensive first)
let sortedResults =
    optionResults |> List.sortByDescending (fun r -> if Double.IsNaN r.Price then Double.MinValue else r.Price)

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let printTable () =
    let divider = String('-', 110)
    printfn ""
    printfn "  Exotic Option Pricing Comparison (sorted by price)"
    printfn "  %s" divider
    printfn "  %-32s %8s %10s %8s %8s %8s %8s %8s" "Option" "Type" "Price" "StdErr" "Delta" "Vega" "Theta" "Status"
    printfn "  %s" divider
    for r in sortedResults do
        let status = if r.HasQuantumFailure then "FAIL" else "OK"
        let fmt v = if Double.IsNaN v then "â€”" else sprintf "%8.4f" v
        let priceFmt = if Double.IsNaN r.Price then "       â€”" else sprintf "$%8.4f" r.Price
        let errFmt = if Double.IsNaN r.StdError then "       â€”" else sprintf "$%7.4f" r.StdError
        printfn "  %-32s %8s %10s %8s %8s %8s %8s %8s"
            (if r.Name.Length > 32 then r.Name.[..31] else r.Name)
            r.OptionType
            priceFmt
            errFmt
            (fmt r.Delta)
            (fmt r.Vega)
            (fmt r.Theta)
            status
    printfn "  %s" divider
    printfn ""
    printfn "  Market: Spot=$%.2f  Strike=$%.2f  Rate=%.1f%%  Vol=%.1f%%  Expiry=%.1fy"
        market.Spot market.Strike (market.RiskFreeRate * 100.0) (market.Volatility * 100.0) market.TimeToExpiry
    printfn ""

printTable ()

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let resultMaps : Map<string, string> list =
    sortedResults
    |> List.map (fun r ->
        [ "key",                  r.Key
          "name",                 r.Name
          "option_type",          r.OptionType
          "spot",                 sprintf "%.2f" market.Spot
          "strike",               sprintf "%.2f" market.Strike
          "rate",                 sprintf "%.4f" market.RiskFreeRate
          "volatility",           sprintf "%.4f" market.Volatility
          "expiry",               sprintf "%.2f" market.TimeToExpiry
          "price",                if Double.IsNaN r.Price then "" else sprintf "%.4f" r.Price
          "std_error",            if Double.IsNaN r.StdError then "" else sprintf "%.4f" r.StdError
          "delta",                if Double.IsNaN r.Delta then "" else sprintf "%.6f" r.Delta
          "vega",                 if Double.IsNaN r.Vega then "" else sprintf "%.6f" r.Vega
          "theta",                if Double.IsNaN r.Theta then "" else sprintf "%.6f" r.Theta
          "has_quantum_failure",  sprintf "%b" r.HasQuantumFailure ]
        |> Map.ofList)

match outputPath with
| Some path ->
    Reporting.writeJson path resultMaps
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "key"; "name"; "option_type"; "spot"; "strike"; "rate"; "volatility"; "expiry"
          "price"; "std_error"; "delta"; "vega"; "theta"; "has_quantum_failure" ]
    let rows =
        resultMaps |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()
