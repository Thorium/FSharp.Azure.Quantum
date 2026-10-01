// ==============================================================================
// Investment Portfolio Market Data
// ==============================================================================
// Shared by the InvestmentPortfolio examples and data/generate-market-data.fsx.
//
// Every figure comes from Yahoo Finance daily adjusted closes (split- and
// dividend-adjusted) fetched with FinancialData.fetchYahooHistoryAsync for the
// dates the figures need (StartDate/EndDate: 10 days before the window to the
// end of the hold-out year):
//   - daily simple return r_t = A_t / A_(t-1) - 1, dated inside the window
//   - annual return     = arithmetic mean of r_t * 252
//   - annual volatility = sample standard deviation of r_t * sqrt 252
//   - correlation       = Pearson correlation of r_t on the dates all symbols share
//   - buy price         = adjusted close on the last trading day of the window
//   - hold-out return   = adjusted close at the end of the following year / buy price - 1
//
// The bundled file data/market-stats-2019-2023.json holds these derived figures
// only (no daily prices). `computeFromYahooAsync` recomputes them for any symbols
// and dates.
//
// Usage:
//   #load "_marketData.fsx"
//   open _marketData
// ==============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
// The library comes from NuGet; `dotnet fsi --define:LOCAL_BUILD <script>` uses the repo's Debug build.
#if LOCAL_BUILD
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#else
#r "nuget: FSharp.Azure.Quantum"
#endif

open System
open System.Globalization
open System.IO
open System.Net.Http
open System.Text.Encodings.Web
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Data

module MarketData =

    [<Literal>]
    let tradingDaysPerYear = 252.0

    /// Daily simple-return statistics of one asset on the market proxy's Bull and Bear days.
    [<Struct>]
    type RegimeFigures =
        {
            BullDailyMean: float
            BullDailyStd: float
            BearDailyMean: float
            BearDailyStd: float
        }

    type AssetFigures =
        {
            Symbol: string
            Name: string
            /// Arithmetic mean of daily simple returns in the window, times 252.
            AnnualReturn: float
            /// Sample standard deviation of daily simple returns in the window, times sqrt 252.
            AnnualVolatility: float
            /// Number of daily returns in the window.
            Observations: int
            /// Last trading day of the window (yyyy-MM-dd).
            BuyDate: string
            /// Adjusted close on BuyDate.
            BuyPrice: float
            /// Adjusted close on the last trading day of the hold-out / BuyPrice - 1; None when the data ends earlier.
            HoldoutReturn: float option
            /// Mean of the last 30 daily simple returns of the window.
            Recent30DailyMean: float
            /// Sample standard deviation of the last 30 daily simple returns of the window.
            Recent30DailyStd: float
            Regime: RegimeFigures option
        }

    type CorrelationFigures =
        {
            Symbols: string array
            Matrix: float array array
            /// Number of dates shared by all symbols.
            Observations: int
        }

    /// Two-state Gaussian HMM of the market proxy's daily simple returns, fitted by Viterbi training.
    type RegimeModel =
        {
            MarketProxy: string
            BullMean: float
            BullStd: float
            BearMean: float
            BearStd: float
            PBullToBear: float
            PBearToBull: float
            BullDays: int
            BearDays: int
            /// "Bull" or "Bear": the Viterbi state on the last day of the window.
            RegimeAtWindowEnd: string
            Iterations: int
        }

    type MarketStats =
        {
            Source: string
            Method: string
            GeneratedOn: string
            WindowFrom: string
            WindowTo: string
            HoldoutFrom: string
            HoldoutTo: string
            Assets: AssetFigures array
            Correlation: CorrelationFigures
            RegimeModel: RegimeModel option
        }

    let defaultWindowFrom = DateTime(2019, 1, 1)
    let defaultWindowTo = DateTime(2023, 12, 31)

    [<Literal>]
    let defaultRegimeProxy = "SPY"

    let bundledPath =
        Path.Combine(__SOURCE_DIRECTORY__, "data", "market-stats-2019-2023.json")

    let defaultCacheDirectory =
        Path.Combine(Path.GetTempPath(), "FSharp.Azure.Quantum", "yahoo-cache")

    [<Literal>]
    let sourceDescription =
        "Yahoo Finance daily adjusted closes (split- and dividend-adjusted), fetched with FinancialData.fetchYahooHistoryAsync"

    let methodDescription =
        "Daily simple returns r_t = A_t/A_(t-1) - 1 of adjusted closes dated inside the window; "
        + "annual return = mean(r_t) * 252; annual volatility = sample std(r_t) * sqrt(252); "
        + "correlation = Pearson on dates shared by all symbols; buy price = adjusted close on the last trading day of the window; "
        + "hold-out return = adjusted close on the last trading day of the following year / buy price - 1; "
        + "regime = 2-state Gaussian HMM on the market proxy's daily returns, fitted by Viterbi training"

    let knownNames =
        [
            "AAPL", "Apple Inc."
            "MSFT", "Microsoft Corp."
            "GOOGL", "Alphabet Inc. Class A"
            "AMZN", "Amazon.com Inc."
            "NVDA", "NVIDIA Corp."
            "META", "Meta Platforms Inc."
            "TSLA", "Tesla Inc."
            "AMD", "Advanced Micro Devices"
            "TQQQ", "ProShares UltraPro QQQ (3x Nasdaq-100)"
            "JNJ", "Johnson & Johnson"
            "XLP", "Consumer Staples Select Sector SPDR"
            "GLD", "SPDR Gold Shares"
            "TLT", "iShares 20+ Year Treasury Bond ETF"
            "SH", "ProShares Short S&P500 (-1x)"
            "SPY", "SPDR S&P 500 ETF"
        ]
        |> Map.ofList

    let isoDate (d: DateTime) =
        d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    let tryParseIsoDate (s: string) =
        match DateTime.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None) with
        | true, d -> Some d
        | _ -> None

    let private jsonOptions =
        JsonSerializerOptions(
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        )

    let loadBundled () : MarketStats =
        JsonSerializer.Deserialize<MarketStats>(File.ReadAllText bundledPath, jsonOptions)

    let private toJsonRegex2 = Regex @",\s*"

    let private toJsonRegex =
        Regex @"\[\s*(-?[0-9][0-9.eE+-]*(?:,\s*-?[0-9][0-9.eE+-]*)*)\s*\]"

    /// JSON with numeric arrays kept on one line.
    let toJson (stats: MarketStats) : string =
        let json = JsonSerializer.Serialize(stats, jsonOptions).Replace("\r\n", "\n")

        toJsonRegex.Replace(json, fun m -> "[" + toJsonRegex2.Replace(m.Groups.[1].Value, ", ") + "]")

    // --------------------------------------------------------------------------
    // Statistics
    // --------------------------------------------------------------------------

    let private mean (xs: float array) = Array.average xs

    let private sampleStd (xs: float array) =
        let m = mean xs
        sqrt ((xs |> Array.sumBy (fun x -> (x - m) * (x - m))) / float (xs.Length - 1))

    let private pearson (a: float array) (b: float array) =
        let ma = mean a
        let mb = mean b
        let cov = Array.fold2 (fun acc x y -> acc + (x - ma) * (y - mb)) 0.0 a b
        let va = a |> Array.sumBy (fun x -> (x - ma) * (x - ma))
        let vb = b |> Array.sumBy (fun y -> (y - mb) * (y - mb))
        if va = 0.0 || vb = 0.0 then 0.0 else cov / sqrt (va * vb)

    /// Daily simple returns dated inside [fromDate, toDate]; the first one uses the last close before fromDate.
    let private returnsIn (fromDate: DateTime) (toDate: DateTime) (bars: (DateTime * float) array) =
        bars
        |> Array.pairwise
        |> Array.choose (fun ((_, p0), (d1, p1)) ->
            if d1 >= fromDate && d1 <= toDate then
                Some(d1, p1 / p0 - 1.0)
            else
                None)

    /// True when the bars reach from before the window start (7-day tolerance) to its end (7-day tolerance).
    let coversWindow (fromDate: DateTime) (toDate: DateTime) (bars: (DateTime * float) array) =
        bars.Length > 1
        && fst bars.[0] <= fromDate.AddDays 7.0
        && (bars |> Array.exists (fun (d, _) -> d >= toDate.AddDays -7.0 && d <= toDate))

    // --------------------------------------------------------------------------
    // Regime HMM (2 states, Gaussian emissions; state 0 = Bull, state 1 = Bear)
    // --------------------------------------------------------------------------

    type HmmParams =
        {
            Means: float array
            Stds: float array
            Trans: float array2d
            Start: float array
        }

    let private logNormalPdf x mu sigma =
        -0.5 * log (2.0 * Math.PI) - log sigma - 0.5 * ((x - mu) / sigma) ** 2.0

    /// Most likely state sequence (Viterbi).
    let viterbiPath (p: HmmParams) (obs: float array) : int array =
        let n = obs.Length
        let v = Array2D.create n 2 Double.NegativeInfinity
        let back = Array2D.zeroCreate<int> n 2

        for s in 0..1 do
            v.[0, s] <- log p.Start.[s] + logNormalPdf obs.[0] p.Means.[s] p.Stds.[s]

        for t in 1 .. n - 1 do
            for s in 0..1 do
                let fromBull = v.[t - 1, 0] + log p.Trans.[0, s]
                let fromBear = v.[t - 1, 1] + log p.Trans.[1, s]
                let best, prev = if fromBull >= fromBear then fromBull, 0 else fromBear, 1
                v.[t, s] <- best + logNormalPdf obs.[t] p.Means.[s] p.Stds.[s]
                back.[t, s] <- prev

        let path = Array.zeroCreate n
        path.[n - 1] <- if v.[n - 1, 0] >= v.[n - 1, 1] then 0 else 1

        for t in n - 2 .. -1 .. 0 do
            path.[t] <- back.[t + 1, path.[t + 1]]

        path

    let hmmParamsOf (m: RegimeModel) : HmmParams =
        {
            Means = [| m.BullMean; m.BearMean |]
            Stds = [| m.BullStd; m.BearStd |]
            Trans =
                array2D
                    [
                        [ 1.0 - m.PBullToBear; m.PBullToBear ]
                        [ m.PBearToBull; 1.0 - m.PBearToBull ]
                    ]
            Start = [| 0.5; 0.5 |]
        }

    /// Viterbi training: label days with the current parameters, re-estimate from the labels, repeat until
    /// the labels stop changing. Starting values: calm up-market vs. volatile down-market; the fit replaces them.
    let fitRegimeHmm (obs: float array) : HmmParams * int array * int =
        let initial =
            {
                Means = [| 0.0005; -0.002 |]
                Stds = [| 0.01; 0.03 |]
                Trans = array2D [ [ 0.95; 0.05 ]; [ 0.10; 0.90 ] ]
                Start = [| 0.5; 0.5 |]
            }

        let reestimate (path: int array) =
            let inState s =
                Array.zip obs path |> Array.filter (fun (_, q) -> q = s) |> Array.map fst

            let groups = [| inState 0; inState 1 |]

            if groups |> Array.exists (fun g -> g.Length < 2) then
                None
            else
                // Add-one smoothing keeps both transitions possible.
                let counts = Array2D.create 2 2 1.0

                for t in 1 .. path.Length - 1 do
                    counts.[path.[t - 1], path.[t]] <- counts.[path.[t - 1], path.[t]] + 1.0

                Some
                    {
                        Means = groups |> Array.map mean
                        Stds = groups |> Array.map sampleStd
                        Trans = Array2D.init 2 2 (fun i j -> counts.[i, j] / (counts.[i, 0] + counts.[i, 1]))
                        Start = [| 0.5; 0.5 |]
                    }

        let rec loop p path iteration =
            if iteration >= 200 then
                p, path, iteration
            else
                match reestimate path with
                | None -> p, path, iteration
                | Some next ->
                    let nextPath = viterbiPath next obs

                    if nextPath = path then
                        next, nextPath, iteration + 1
                    else
                        loop next nextPath (iteration + 1)

        let fitted, path, iterations = loop initial (viterbiPath initial obs) 0

        // State 0 is the one with the higher mean return.
        if fitted.Means.[0] >= fitted.Means.[1] then
            fitted, path, iterations
        else
            let swapped =
                {
                    Means = Array.rev fitted.Means
                    Stds = Array.rev fitted.Stds
                    Trans = Array2D.init 2 2 (fun i j -> fitted.Trans.[1 - i, 1 - j])
                    Start = fitted.Start
                }

            swapped, path |> Array.map (fun s -> 1 - s), iterations

    // --------------------------------------------------------------------------
    // Figures from daily adjusted closes
    // --------------------------------------------------------------------------

    let private round (digits: int) (x: float) = Math.Round(x, digits)

    /// Statistics for series that already cover the window (see coversWindow).
    let computeStats
        (series: (string * (DateTime * float) array) list)
        (fromDate: DateTime)
        (toDate: DateTime)
        (regimeProxy: string option)
        : MarketStats =
        let holdoutTo = toDate.AddYears 1

        let returns =
            series
            |> List.map (fun (s, bars) -> s, returnsIn fromDate toDate bars)
            |> Map.ofList

        let regime =
            regimeProxy
            |> Option.bind (fun proxy -> returns |> Map.tryFind proxy |> Option.map (fun r -> proxy, r))
            |> Option.map (fun (proxy, proxyReturns) ->
                let obs = proxyReturns |> Array.map snd
                let p, path, iterations = fitRegimeHmm obs

                let bullDates =
                    Array.zip proxyReturns path
                    |> Array.choose (fun ((d, _), s) -> if s = 0 then Some d else None)
                    |> Set.ofArray

                let model =
                    {
                        MarketProxy = proxy
                        BullMean = round 6 p.Means.[0]
                        BullStd = round 6 p.Stds.[0]
                        BearMean = round 6 p.Means.[1]
                        BearStd = round 6 p.Stds.[1]
                        PBullToBear = round 6 p.Trans.[0, 1]
                        PBearToBull = round 6 p.Trans.[1, 0]
                        BullDays = path |> Array.filter ((=) 0) |> Array.length
                        BearDays = path |> Array.filter ((=) 1) |> Array.length
                        RegimeAtWindowEnd = if path.[path.Length - 1] = 0 then "Bull" else "Bear"
                        Iterations = iterations
                    }

                model, bullDates, proxyReturns |> Array.map fst |> Set.ofArray)

        let assets =
            series
            |> List.map (fun (symbol, bars) ->
                let r = returns.[symbol]
                let values = r |> Array.map snd

                let buyDate, buyPrice =
                    bars |> Array.filter (fun (d, _) -> d <= toDate) |> Array.last

                let holdoutReturn =
                    match
                        bars
                        |> Array.filter (fun (d, _) -> d > toDate && d <= holdoutTo)
                        |> Array.tryLast
                    with
                    | Some(d, p) when d >= holdoutTo.AddDays -7.0 -> Some(round 6 (p / buyPrice - 1.0))
                    | _ -> None

                let recent = values |> Array.skip (max 0 (values.Length - 30))

                let regimeFigures =
                    regime
                    |> Option.bind (fun (_, bullDates, proxyDates) ->
                        let bull = r |> Array.filter (fun (d, _) -> bullDates.Contains d) |> Array.map snd

                        let bear =
                            r
                            |> Array.filter (fun (d, _) -> proxyDates.Contains d && not (bullDates.Contains d))
                            |> Array.map snd

                        if bull.Length < 2 || bear.Length < 2 then
                            None
                        else
                            Some
                                {
                                    BullDailyMean = round 6 (mean bull)
                                    BullDailyStd = round 6 (sampleStd bull)
                                    BearDailyMean = round 6 (mean bear)
                                    BearDailyStd = round 6 (sampleStd bear)
                                })

                {
                    Symbol = symbol
                    Name = knownNames |> Map.tryFind symbol |> Option.defaultValue symbol
                    AnnualReturn = round 6 (mean values * tradingDaysPerYear)
                    AnnualVolatility = round 6 (sampleStd values * sqrt tradingDaysPerYear)
                    Observations = values.Length
                    BuyDate = isoDate buyDate
                    BuyPrice = round 4 buyPrice
                    HoldoutReturn = holdoutReturn
                    Recent30DailyMean = round 6 (mean recent)
                    Recent30DailyStd = round 6 (sampleStd recent)
                    Regime = regimeFigures
                })
            |> List.toArray

        let symbols = series |> List.map fst |> List.toArray

        let commonDates =
            symbols
            |> Array.map (fun s -> returns.[s] |> Array.map fst |> Set.ofArray)
            |> Set.intersectMany
            |> Set.toArray

        let columns =
            symbols
            |> Array.map (fun s ->
                let byDate = returns.[s] |> Map.ofArray
                commonDates |> Array.map (fun d -> byDate.[d]))

        let matrix =
            Array.init symbols.Length (fun i ->
                Array.init symbols.Length (fun j ->
                    if i = j then
                        1.0
                    else
                        round 4 (pearson columns.[i] columns.[j])))

        {
            Source = sourceDescription
            Method = methodDescription
            GeneratedOn = isoDate DateTime.UtcNow
            WindowFrom = isoDate fromDate
            WindowTo = isoDate toDate
            HoldoutFrom = isoDate (toDate.AddDays 1.0)
            HoldoutTo = isoDate holdoutTo
            Assets = assets
            Correlation =
                {
                    Symbols = symbols
                    Matrix = matrix
                    Observations = commonDates.Length
                }
            RegimeModel = regime |> Option.map (fun (m, _, _) -> m)
        }

    // --------------------------------------------------------------------------
    // Yahoo Finance
    // --------------------------------------------------------------------------

    /// Daily adjusted closes from 10 days before fromDate (the close the first window return
    /// starts from) to the end of the hold-out year after toDate, ascending. Bars without an
    /// adjusted close are left out.
    let fetchAdjustedClosesAsync
        (http: HttpClient)
        (cacheDir: string)
        (fromDate: DateTime)
        (toDate: DateTime)
        (symbol: string)
        : Task<Result<(DateTime * float) array, string>> =
        task {
            let request: FinancialData.YahooHistoryRequest =
                {
                    Symbol = symbol
                    Range = FinancialData.YahooHistoryRange.Max
                    Interval = FinancialData.YahooHistoryInterval.OneDay
                    IncludeAdjustedClose = true
                    CacheDirectory = Some cacheDir
                    CacheTtl = TimeSpan.FromHours 6.0
                    StartDate = Some(fromDate.AddDays -10.0)
                    EndDate = Some(toDate.AddYears 1)
                }

            match! FinancialData.fetchYahooHistoryAsync http request CancellationToken.None with
            | Error e -> return Error e.Message
            | Ok series ->
                let bars =
                    series.Prices
                    |> Array.choose (fun b ->
                        b.AdjustedClose
                        |> Option.filter (fun p -> p > 0.0)
                        |> Option.map (fun p -> b.Date, p))
                    |> Array.distinctBy fst
                    |> Array.sortBy fst

                return
                    if bars.Length = 0 then
                        Error "the response has no adjusted closes"
                    else
                        Ok bars
        }

    /// Fetches every symbol (plus the regime proxy) and computes the figures. Symbols that fail to
    /// download or do not cover the whole window are dropped and returned with the reason.
    let computeFromYahooAsync
        (log: string -> unit)
        (symbols: string list)
        (fromDate: DateTime)
        (toDate: DateTime)
        (regimeProxy: string option)
        : Task<Result<MarketStats * (string * string) list, string>> =
        task {
            Directory.CreateDirectory defaultCacheDirectory |> ignore
            use http = new HttpClient(Timeout = TimeSpan.FromSeconds 60.0)

            let wanted =
                symbols @ Option.toList regimeProxy
                |> List.map (fun s -> s.Trim().ToUpperInvariant())
                |> List.filter (fun s -> s <> "")
                |> List.distinct

            log $"Fetching %d{wanted.Length} symbols from Yahoo Finance (cache: %s{defaultCacheDirectory}, 6 h)..."

            let fetchedSymbols = ResizeArray()

            for s in wanted do
                match! fetchAdjustedClosesAsync http defaultCacheDirectory fromDate toDate s with
                | Error e -> fetchedSymbols.Add(s, Error e)
                | Ok bars when not (coversWindow fromDate toDate bars) ->
                    fetchedSymbols.Add(
                        s,
                        Error
                            $"no data for the whole window (history %s{isoDate (fst bars.[0])}..%s{isoDate (fst bars.[bars.Length - 1])})"
                    )
                | Ok bars -> fetchedSymbols.Add(s, Ok bars)

            let fetched = List.ofSeq fetchedSymbols

            let dropped =
                fetched
                |> List.choose (fun (s, r) ->
                    r |> Result.map (fun _ -> None) |> Result.defaultWith (fun e -> Some(s, e)))

            let kept =
                fetched
                |> List.choose (fun (s, r) -> r |> Result.map (fun bars -> Some(s, bars)) |> Result.defaultValue None)

            return
                if kept.IsEmpty then
                    Error(dropped |> List.map (fun (s, e) -> $"%s{s}: %s{e}") |> String.concat "; ")
                else
                    Ok(computeStats kept fromDate toDate regimeProxy, dropped)
        }

    // --------------------------------------------------------------------------
    // Portfolio evaluation
    // --------------------------------------------------------------------------

    let tryAsset (stats: MarketStats) (symbol: string) =
        stats.Assets
        |> Array.tryFind (fun a -> a.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))

    let private normalise (weights: (string * float) list) =
        let total = weights |> List.sumBy snd

        if total <= 0.0 then
            []
        else
            weights
            |> List.filter (fun (_, w) -> w > 0.0)
            |> List.map (fun (s, w) -> s, w / total)

    let private allFound (xs: 'a option list) =
        if xs |> List.forall Option.isSome then
            Some(xs |> List.map Option.get)
        else
            None

    /// Weighted annual return from the window figures.
    let expectedReturn (stats: MarketStats) (weights: (string * float) list) : float option =
        normalise weights
        |> List.map (fun (s, w) -> tryAsset stats s |> Option.map (fun a -> w * a.AnnualReturn))
        |> allFound
        |> Option.map List.sum

    /// Covariance S_ij = rho_ij * sigma_i * sigma_j of the symbols, in the given order, with rho the
    /// window correlations and sigma the given volatilities (PortfolioTypes.covarianceFromCorrelation).
    /// None when a symbol has no correlation row.
    let covariance (stats: MarketStats) (symbolsAndVolatilities: (string * float) list) : float[,] option =
        symbolsAndVolatilities
        |> List.map (fun (s, _) ->
            stats.Correlation.Symbols
            |> Array.tryFindIndex (fun c -> c.Equals(s, StringComparison.OrdinalIgnoreCase)))
        |> allFound
        |> Option.bind (fun indices ->
            let idx = List.toArray indices

            let rho =
                Array2D.init idx.Length idx.Length (fun a b -> stats.Correlation.Matrix.[idx.[a]].[idx.[b]])

            let volatilities = symbolsAndVolatilities |> List.map snd |> List.toArray

            match PortfolioTypes.covarianceFromCorrelation volatilities rho with
            | Ok sigma -> Some sigma
            | Error _ -> None)

    /// Annual volatility sqrt(w' S w) from the window volatilities and correlations.
    let volatilityWithCorrelations (stats: MarketStats) (weights: (string * float) list) : float option =
        let w = normalise weights

        w
        |> List.map (fun (s, _) -> tryAsset stats s |> Option.map (fun a -> s, a.AnnualVolatility))
        |> allFound
        |> Option.bind (covariance stats)
        |> Option.map (fun sigma ->
            PortfolioTypes.portfolioVariance (w |> List.map snd |> List.toArray) sigma
            |> max 0.0
            |> sqrt)

    /// Buy-and-hold return over the hold-out year for the given weights.
    let holdoutReturn (stats: MarketStats) (weights: (string * float) list) : float option =
        normalise weights
        |> List.map (fun (s, w) ->
            tryAsset stats s
            |> Option.bind (fun a -> a.HoldoutReturn)
            |> Option.map (fun r -> w * r))
        |> allFound
        |> Option.map List.sum

    let private pct (x: float option) =
        match x with
        | Some v -> $"%+.1f{v * 100.0}%%"
        | None -> "n/a"

    let private pctPlain (x: float option) =
        match x with
        | Some v -> $"%.1f{v * 100.0}%%"
        | None -> "n/a"

    let describeSource (stats: MarketStats) (live: bool) =
        let origin =
            if live then
                "computed at run time"
            else
                $"bundled, generated %s{stats.GeneratedOn}"

        $"Figures: Yahoo Finance adjusted closes, daily returns %s{stats.WindowFrom}..%s{stats.WindowTo} (%s{origin})"

    /// Prints the chosen portfolio next to an equal-weight portfolio of the whole universe:
    /// expected return and correlation-aware risk over the window, and the realised hold-out return.
    /// chosenRisk is the solver's risk when it used the window covariance; None computes it here.
    let printComparison
        (stats: MarketStats)
        (chosen: (string * float) list)
        (universe: string list)
        (chosenRisk: float option)
        =
        let equal = universe |> List.map (fun s -> s, 1.0)

        let row label w (risk: float option) =
            printfn
                "  %-28s %9s %13s %10s"
                label
                (pctPlain (expectedReturn stats w))
                (pctPlain (risk |> Option.orElse (volatilityWithCorrelations stats w)))
                (pct (holdoutReturn stats w))

        let buyDate =
            chosen
            |> List.tryPick (fun (s, _) -> tryAsset stats s |> Option.map (fun a -> a.BuyDate))
            |> Option.defaultValue stats.WindowTo

        let holdoutLabel =
            if
                stats.HoldoutFrom.EndsWith "-01-01"
                && stats.HoldoutTo.EndsWith "-12-31"
                && stats.HoldoutFrom.[..3] = stats.HoldoutTo.[..3]
            then
                stats.HoldoutFrom.[..3]
            else
                "1 year"

        printfn ""
        printfn "  %-28s %9s %13s %10s" "" "Expected" "Risk" "Realised"
        printfn "  %-28s %9s %13s %10s" "" "(window)" "(with corr.)" $"(%s{holdoutLabel})"
        row "Optimised portfolio" chosen chosenRisk
        row $"Equal weight, %d{universe.Length} symbols" equal None
        printfn ""

        printfn
            "  Hold-out %s..%s: bought at the %s adjusted close, held without rebalancing."
            stats.HoldoutFrom
            stats.HoldoutTo
            buyDate

        printfn "  Risk is sqrt(w' S w) with the %s..%s covariance." stats.WindowFrom stats.WindowTo
        printfn "  Past performance over one fixed window; not investment advice."
