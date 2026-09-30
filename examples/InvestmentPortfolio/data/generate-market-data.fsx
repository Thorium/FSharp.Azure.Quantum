// ==============================================================================
// Generate the bundled market statistics
// ==============================================================================
// Fetches daily adjusted closes from Yahoo Finance through the library
// (FinancialData.fetchYahooHistoryAsync with StartDate/EndDate bounds: 10 days
// before --from to the end of the hold-out year) and writes only derived figures:
// annual return and volatility, buy price, hold-out return, correlations and
// the regime model. Daily prices are not written. The method is described in
// ../_marketData.fsx and README.md.
//
// Usage (from the repository root):
//   dotnet fsi --define:LOCAL_BUILD examples/InvestmentPortfolio/data/generate-market-data.fsx
//   dotnet fsi examples/InvestmentPortfolio/data/generate-market-data.fsx -- --symbols AAPL,MSFT,SPY --from 2015-01-01 --to 2019-12-31
//
// Options:
//   --symbols A,B,C   symbols to include (default: every symbol the examples use)
//   --from yyyy-MM-dd window start (default 2019-01-01)
//   --to yyyy-MM-dd   window end (default 2023-12-31); the hold-out is the year after
//   --proxy SYMBOL    market proxy for the regime model (default SPY; "none" to skip)
//   --out PATH        output file (default data/market-stats-<from year>-<to year>.json)
// ==============================================================================

#load "../_marketData.fsx"
#load "../../_common/Cli.fs"

open System
open System.IO
open _marketData
open FSharp.Azure.Quantum.Examples.Common

let args = Cli.parse (fsi.CommandLineArgs |> Array.skip 1)

let exampleSymbols =
    [
        // InvestmentPortfolio.fsx, InvestmentPortfolio-Small.fsx, CSharp/PortfolioExample
        "AAPL"
        "MSFT"
        "GOOGL"
        "AMZN"
        "NVDA"
        "META"
        "TSLA"
        "AMD"
        // RegimeAwarePortfolio.fsx
        "TQQQ"
        "JNJ"
        "XLP"
        "GLD"
        "TLT"
        "SH"
    ]

let dateOption name fallback =
    match Cli.tryGet name args with
    | None -> fallback
    | Some s ->
        match MarketData.tryParseIsoDate s with
        | Some d -> d
        | None ->
            eprintfn "ERROR: --%s must be yyyy-MM-dd, got '%s'" name s
            exit 2

let fromDate = dateOption "from" MarketData.defaultWindowFrom
let toDate = dateOption "to" MarketData.defaultWindowTo

let symbols =
    match Cli.getCommaSeparated "symbols" args with
    | [] -> exampleSymbols
    | xs -> xs |> List.map (fun s -> s.ToUpperInvariant())

let proxy =
    match Cli.getOr "proxy" MarketData.defaultRegimeProxy args with
    | p when p.Equals("none", StringComparison.OrdinalIgnoreCase) -> None
    | p -> Some(p.ToUpperInvariant())

let outPath =
    match Cli.tryGet "out" args with
    | Some p -> Path.GetFullPath p
    | None -> Path.Combine(__SOURCE_DIRECTORY__, $"market-stats-%d{fromDate.Year}-%d{toDate.Year}.json")

match MarketData.computeFromYahoo (printfn "%s") symbols fromDate toDate proxy with
| Error e ->
    eprintfn "ERROR: %s" e
    exit 1
| Ok(stats, dropped) ->
    for (s, reason) in dropped do
        printfn "  Dropped %s: %s" s reason

    File.WriteAllText(outPath, MarketData.toJson stats + "\n")
    printfn "Wrote %s" outPath
    printfn ""
    printfn "  Window %s..%s, hold-out %s..%s" stats.WindowFrom stats.WindowTo stats.HoldoutFrom stats.HoldoutTo
    printfn "  %-6s %8s %8s %6s %11s %10s %9s" "Symbol" "Return" "Vol" "Obs" "Buy date" "Buy price" "Hold-out"

    for a in stats.Assets do
        printfn
            "  %-6s %7.1f%% %7.1f%% %6d %11s %10.2f %9s"
            a.Symbol
            (a.AnnualReturn * 100.0)
            (a.AnnualVolatility * 100.0)
            a.Observations
            a.BuyDate
            a.BuyPrice
            (match a.HoldoutReturn with
             | Some r -> $"%+.1f{r * 100.0}%%"
             | None -> "n/a")

    match stats.RegimeModel with
    | Some m ->
        printfn ""

        printfn
            "  Regime (%s, %d iterations): Bull mean %.3f%%/day sd %.3f%%, Bear mean %.3f%%/day sd %.3f%%, P(bull->bear) %.3f, P(bear->bull) %.3f"
            m.MarketProxy
            m.Iterations
            (m.BullMean * 100.0)
            (m.BullStd * 100.0)
            (m.BearMean * 100.0)
            (m.BearStd * 100.0)
            m.PBullToBear
            m.PBearToBull

        let lastDay =
            MarketData.tryAsset stats m.MarketProxy
            |> Option.map (fun a -> a.BuyDate)
            |> Option.defaultValue stats.WindowTo

        printfn "  Bull days %d, Bear days %d, regime on %s: %s" m.BullDays m.BearDays lastDay m.RegimeAtWindowEnd
    | None -> ()
