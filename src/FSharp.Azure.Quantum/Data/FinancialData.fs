namespace FSharp.Azure.Quantum.Data

/// Financial Data Infrastructure for Risk Management Applications
///
/// Provides market data loading, portfolio management, correlation calculation,
/// and risk parameter utilities for quantum risk management applications.
///
/// Supports standard financial data formats (OHLCV CSV, portfolio JSON)
/// and regulatory risk calculations (VaR, CVaR, stress testing).

open System
open System.Text.RegularExpressions
open System.Net.Http
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core

module FinancialData =
    
    // ========================================================================
    // CORE TYPES
    // ========================================================================
    
    /// Single price observation (OHLCV)
    type PriceBar = {
        /// Date/time of the observation
        Date: DateTime
        
        /// Opening price
        Open: float
        
        /// Highest price
        High: float
        
        /// Lowest price
        Low: float
        
        /// Closing price
        Close: float
        
        /// Trading volume
        Volume: float
        
        /// Adjusted close (for dividends/splits)
        AdjustedClose: float option
    }
    
    /// Time series of price data for a single asset
    type PriceSeries = {
        /// Asset symbol/ticker
        Symbol: string
        
        /// Asset name (optional)
        Name: string option
        
        /// Currency of prices
        Currency: string
        
        /// Price observations (sorted by date ascending)
        Prices: PriceBar array
        
        /// Data frequency
        Frequency: DataFrequency
    }
    
    and DataFrequency =
        | Daily
        | Weekly
        | Monthly
        | Intraday of minutes: int
    
    /// Return series for an asset
    type ReturnSeries = {
        /// Asset symbol
        Symbol: string
        
        /// Date range
        StartDate: DateTime
        EndDate: DateTime
        
        /// Log returns: ln(P_t / P_{t-1})
        LogReturns: float array
        
        /// Simple returns: (P_t - P_{t-1}) / P_{t-1}
        SimpleReturns: float array
        
        /// Corresponding dates (length = LogReturns.Length)
        Dates: DateTime array
    }
    
    /// Portfolio position
    type Position = {
        /// Asset symbol
        Symbol: string
        
        /// Number of shares/units held
        Quantity: float
        
        /// Current market price per unit
        CurrentPrice: float
        
        /// Position market value (Quantity * CurrentPrice)
        MarketValue: float
        
        /// Asset class for risk aggregation
        AssetClass: AssetClass
        
        /// Optional sector/industry
        Sector: string option
    }
    
    and AssetClass =
        | Equity
        | FixedIncome
        | Commodity
        | Currency
        | Derivative
        | Alternative
        | Cash
    
    /// Complete portfolio definition
    type Portfolio = {
        /// Portfolio identifier
        Id: string
        
        /// Portfolio name
        Name: string
        
        /// Base currency for NAV calculation
        BaseCurrency: string
        
        /// Portfolio positions
        Positions: Position array
        
        /// Total market value
        TotalValue: float
        
        /// Valuation date
        ValuationDate: DateTime
    }
    
    /// Correlation matrix between assets
    type CorrelationMatrix = {
        /// Asset symbols (row/column labels)
        Assets: string array
        
        /// Correlation values (symmetric matrix)
        Values: float array array
        
        /// Calculation period
        StartDate: DateTime
        EndDate: DateTime
        
        /// Number of observations used
        ObservationCount: int
    }
    
    /// Covariance matrix
    type CovarianceMatrix = {
        /// Asset symbols
        Assets: string array
        
        /// Covariance values
        Values: float array array
        
        /// Annualized (252 trading days)
        IsAnnualized: bool
    }
    
    /// Risk parameters for VaR calculation
    type RiskParameters = {
        /// Confidence level (e.g., 0.95, 0.99)
        ConfidenceLevel: float
        
        /// Time horizon in days (e.g., 1, 10)
        TimeHorizon: int
        
        /// Return distribution assumption
        Distribution: ReturnDistribution
        
        /// Historical lookback period in days
        LookbackPeriod: int
    }
    
    and ReturnDistribution =
        | Normal
        | StudentT of degreesOfFreedom: float
        | Historical
        | LogNormal
    
    /// Stress scenario definition
    type StressScenario = {
        /// Scenario name
        Name: string
        
        /// Scenario type
        Type: ScenarioType
        
        /// Shock magnitudes by asset class or specific asset
        Shocks: Map<string, float>
        
        /// Correlation stress (optional multiplier)
        CorrelationShock: float option
    }
    
    and ScenarioType =
        /// Historical scenario (replay actual market moves)
        | Historical of startDate: DateTime * endDate: DateTime
        
        /// Hypothetical scenario (user-defined shocks)
        | Hypothetical
        
        /// Regulatory scenario (Basel, CCAR, etc.)
        | Regulatory of standard: string
    
    /// VaR calculation result
    type VaRResult = {
        /// Value at Risk amount
        VaR: float
        
        /// Expected Shortfall (CVaR)
        ExpectedShortfall: float
        
        /// Confidence level used
        ConfidenceLevel: float
        
        /// Time horizon (days)
        TimeHorizon: int
        
        /// Method used
        Method: string
        
        /// Portfolio value
        PortfolioValue: float
        
        /// VaR as percentage of portfolio
        VaRPercent: float
    }
    
    // ========================================================================
    // PRICE DATA LOADING
    // ========================================================================
    
    /// Parse date from various formats
    let private parseDate (dateStr: string) : DateTime option =
        let formats = [|
            "yyyy-MM-dd"
            "MM/dd/yyyy"
            "dd/MM/yyyy"
            "yyyy/MM/dd"
            "yyyyMMdd"
            "yyyy-MM-dd HH:mm:ss"
        |]
        
        match DateTime.TryParseExact(
                dateStr.Trim(), 
                formats, 
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None) with
        | true, dt -> Some dt
        | false, _ -> 
            match DateTime.TryParse(dateStr.Trim()) with
            | true, dt -> Some dt
            | false, _ -> None
    
    /// Parse float with fallback
    let private parseFloat (s: string) : float option =
        match Double.TryParse(s.Trim(), System.Globalization.NumberStyles.Any, 
                             System.Globalization.CultureInfo.InvariantCulture) with
        | true, v -> Some v
        | false, _ -> None
    
    /// Load OHLCV data from CSV file
    ///
    /// Supports common formats: Yahoo Finance, Alpha Vantage, generic OHLCV
    let loadPricesFromCsv 
        (filePath: string) 
        (symbol: string)
        (dateColumn: string)
        (closeColumn: string)
        : QuantumResult<PriceSeries> =
        
        try
            let lines = System.IO.File.ReadAllLines(filePath)
            if lines.Length < 2 then
                Error (QuantumError.ValidationError ("file", "CSV must have header and at least one data row"))
            else
                let headers = 
                    lines.[0].Split(',') 
                    |> Array.map (fun s -> s.Trim().Trim('"').ToLower())
                
                let dateIdx = headers |> Array.tryFindIndex (fun h -> h = dateColumn.ToLower())
                let closeIdx = headers |> Array.tryFindIndex (fun h -> h = closeColumn.ToLower())
                
                // Try to find optional columns
                let openIdx = headers |> Array.tryFindIndex (fun h -> h = "open")
                let highIdx = headers |> Array.tryFindIndex (fun h -> h = "high")
                let lowIdx = headers |> Array.tryFindIndex (fun h -> h = "low")
                let volumeIdx = headers |> Array.tryFindIndex (fun h -> h = "volume")
                let adjCloseIdx = headers |> Array.tryFindIndex (fun h -> 
                    h = "adj close" || h = "adjusted_close" || h = "adjclose")
                
                match dateIdx, closeIdx with
                | None, _ -> Error (QuantumError.ValidationError ("dateColumn", sprintf "Column '%s' not found" dateColumn))
                | _, None -> Error (QuantumError.ValidationError ("closeColumn", sprintf "Column '%s' not found" closeColumn))
                | Some dIdx, Some cIdx ->
                    let dataLines = lines.[1..]
                    
                    let prices =
                        dataLines
                        |> Array.choose (fun line ->
                            let fields = line.Split(',') |> Array.map (fun s -> s.Trim().Trim('"'))
                            match parseDate fields.[dIdx], parseFloat fields.[cIdx] with
                            | Some date, Some close ->
                                let openPrice = openIdx |> Option.bind (fun idx -> parseFloat fields.[idx]) |> Option.defaultValue close
                                let highPrice = highIdx |> Option.bind (fun idx -> parseFloat fields.[idx]) |> Option.defaultValue close
                                let lowPrice = lowIdx |> Option.bind (fun idx -> parseFloat fields.[idx]) |> Option.defaultValue close
                                let volume = volumeIdx |> Option.bind (fun idx -> parseFloat fields.[idx]) |> Option.defaultValue 0.0
                                let adjClose = adjCloseIdx |> Option.bind (fun idx -> parseFloat fields.[idx])
                                
                                Some {
                                    Date = date
                                    Open = openPrice
                                    High = highPrice
                                    Low = lowPrice
                                    Close = close
                                    Volume = volume
                                    AdjustedClose = adjClose
                                }
                            | _ -> None)
                    
                    if prices.Length = 0 then
                        Error (QuantumError.ValidationError (
                            "csv",
                            sprintf "All %d data rows failed to parse (no valid date/close pairs found)" dataLines.Length))
                    else
                    
                    let sortedPrices = prices |> Array.sortBy (fun p -> p.Date)
                    
                    Ok {
                        Symbol = symbol
                        Name = None
                        Currency = "USD"
                        Prices = sortedPrices
                        Frequency = Daily
                    }
        with ex ->
            Error (QuantumError.Other (sprintf "Failed to read CSV: %s" ex.Message))
    
    /// Load prices for Yahoo Finance CSV format
    let loadYahooFinanceCsv (filePath: string) (symbol: string) : QuantumResult<PriceSeries> =
        loadPricesFromCsv filePath symbol "Date" "Close"
    
    /// Load OHLCV data from CSV file asynchronously
    let loadPricesFromCsvAsync
        (filePath: string)
        (symbol: string)
        (dateColumn: string)
        (closeColumn: string)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<PriceSeries>> =
        task {
            try
                let! allText = File.ReadAllTextAsync(filePath, cancellationToken)
                let lines = allText.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
                if lines.Length < 2 then
                    return Error (QuantumError.ValidationError ("file", "CSV must have header and at least one data row"))
                else
                    let headers = 
                        lines.[0].Split(',') 
                        |> Array.map (fun s -> s.Trim().Trim('"').ToLower())
                    
                    let dateIdx = headers |> Array.tryFindIndex (fun h -> h = dateColumn.ToLower())
                    let closeIdx = headers |> Array.tryFindIndex (fun h -> h = closeColumn.ToLower())
                    
                    let openIdx = headers |> Array.tryFindIndex (fun h -> h = "open")
                    let highIdx = headers |> Array.tryFindIndex (fun h -> h = "high")
                    let lowIdx = headers |> Array.tryFindIndex (fun h -> h = "low")
                    let volumeIdx = headers |> Array.tryFindIndex (fun h -> h = "volume")
                    let adjCloseIdx = headers |> Array.tryFindIndex (fun h -> 
                        h = "adj close" || h = "adjusted_close" || h = "adjclose")
                    
                    match dateIdx, closeIdx with
                    | None, _ -> return Error (QuantumError.ValidationError ("dateColumn", sprintf "Column '%s' not found" dateColumn))
                    | _, None -> return Error (QuantumError.ValidationError ("closeColumn", sprintf "Column '%s' not found" closeColumn))
                    | Some dIdx, Some cIdx ->
                        let dataLines = lines.[1..]
                        
                        let prices =
                            dataLines
                            |> Array.choose (fun line ->
                                let fields = line.Split(',') |> Array.map (fun s -> s.Trim().Trim('"'))
                                match parseDate fields.[dIdx], parseFloat fields.[cIdx] with
                                | Some date, Some close ->
                                    let openPrice = openIdx |> Option.bind (fun idx -> parseFloat fields.[idx]) |> Option.defaultValue close
                                    let highPrice = highIdx |> Option.bind (fun idx -> parseFloat fields.[idx]) |> Option.defaultValue close
                                    let lowPrice = lowIdx |> Option.bind (fun idx -> parseFloat fields.[idx]) |> Option.defaultValue close
                                    let volume = volumeIdx |> Option.bind (fun idx -> parseFloat fields.[idx]) |> Option.defaultValue 0.0
                                    let adjClose = adjCloseIdx |> Option.bind (fun idx -> parseFloat fields.[idx])
                                    
                                    Some {
                                        Date = date
                                        Open = openPrice
                                        High = highPrice
                                        Low = lowPrice
                                        Close = close
                                        Volume = volume
                                        AdjustedClose = adjClose
                                    }
                                | _ -> None)
                        
                        if prices.Length = 0 then
                            return Error (QuantumError.ValidationError (
                                "csv",
                                sprintf "All %d data rows failed to parse (no valid date/close pairs found)" dataLines.Length))
                        else
                        
                        let sortedPrices = prices |> Array.sortBy (fun p -> p.Date)
                        
                        return Ok {
                            Symbol = symbol
                            Name = None
                            Currency = "USD"
                            Prices = sortedPrices
                            Frequency = Daily
                        }
            with ex ->
                return Error (QuantumError.Other (sprintf "Failed to read CSV: %s" ex.Message))
        }

    // ========================================================================
    // YAHOO FINANCE - LIVE FETCHING
    // ========================================================================

    type YahooHistoryInterval =
        | OneDay
        | OneWeek
        | OneMonth

        member this.ToQueryString() =
            match this with
            | OneDay -> "1d"
            | OneWeek -> "1wk"
            | OneMonth -> "1mo"

    type YahooHistoryRange =
        | OneMonth
        | ThreeMonths
        | SixMonths
        | OneYear
        | TwoYears
        | FiveYears
        | TenYears
        | Max

        member this.ToQueryString() =
            match this with
            | OneMonth -> "1mo"
            | ThreeMonths -> "3mo"
            | SixMonths -> "6mo"
            | OneYear -> "1y"
            | TwoYears -> "2y"
            | FiveYears -> "5y"
            | TenYears -> "10y"
            | Max -> "max"

    type YahooHistoryRequest = {
        Symbol: string
        Range: YahooHistoryRange
        Interval: YahooHistoryInterval
        IncludeAdjustedClose: bool
        CacheDirectory: string option
        CacheTtl: TimeSpan
    }

    let private defaultYahooHistoryRequest symbol =
        {
            Symbol = symbol
            Range = OneYear
            Interval = OneDay
            IncludeAdjustedClose = true
            CacheDirectory = None
            CacheTtl = TimeSpan.FromHours 6.0
        }

    let private sha256Hex (text: string) =
        use sha = SHA256.Create()
        let bytes = Encoding.UTF8.GetBytes(text)
        let hash = sha.ComputeHash(bytes)
        hash |> Array.map (fun b -> b.ToString("x2")) |> String.Concat

    let private tryReadFreshCache (cachePath: string) (ttl: TimeSpan) : string option =
        try
            if File.Exists(cachePath) then
                let age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath)
                if age <= ttl then
                    Some (File.ReadAllText(cachePath))
                else
                    None
            else
                None
        with _ -> None

    let private tryReadFreshCacheAsync (cachePath: string) (ttl: TimeSpan) (cancellationToken: CancellationToken) : Task<string option> =
        task {
            try
                if File.Exists(cachePath) then
                    let age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath)
                    if age <= ttl then
                        let! content = File.ReadAllTextAsync(cachePath, cancellationToken)
                        return Some content
                    else
                        return None
                else
                    return None
            with _ -> return None
        }

    let private tryWriteCache (cachePath: string) (content: string) : unit =
        try
            let directory = Path.GetDirectoryName(cachePath)
            if not (String.IsNullOrWhiteSpace directory) then
                Directory.CreateDirectory(directory) |> ignore
            File.WriteAllText(cachePath, content)
        with _ -> ()

    let private tryWriteCacheAsync (cachePath: string) (content: string) (cancellationToken: CancellationToken) : Task<unit> =
        task {
            try
                let directory = Path.GetDirectoryName(cachePath)
                if not (String.IsNullOrWhiteSpace directory) then
                    Directory.CreateDirectory(directory) |> ignore
                do! File.WriteAllTextAsync(cachePath, content, cancellationToken)
            with _ -> ()
        }

    let private parseYahooChartJson (symbol: string) (json: string) : QuantumResult<PriceSeries> =
        try
            use doc = System.Text.Json.JsonDocument.Parse(json)
            let root = doc.RootElement

            let chart = root.GetProperty("chart")

            let errorEl = chart.GetProperty("error")
            if errorEl.ValueKind <> System.Text.Json.JsonValueKind.Null then
                let message =
                    match errorEl.TryGetProperty("description") with
                    | true, v -> (v.GetString() |> Option.ofObj) |> Option.defaultValue (errorEl.ToString())
                    | _ -> errorEl.ToString()

                Error (QuantumError.BackendError ("YahooFinance", message))
            else
                let resultArr = chart.GetProperty("result")
                if resultArr.GetArrayLength() = 0 then
                    Error (QuantumError.BackendError ("YahooFinance", "Empty result"))
                else
                    let result0 = resultArr.[0]

                    let timestamps = result0.GetProperty("timestamp").EnumerateArray() |> Seq.toArray
                    let indicators = result0.GetProperty("indicators")
                    let quote0 = indicators.GetProperty("quote").[0]

                    let closes = quote0.GetProperty("close").EnumerateArray() |> Seq.toArray
                    let opens = quote0.GetProperty("open").EnumerateArray() |> Seq.toArray
                    let highs = quote0.GetProperty("high").EnumerateArray() |> Seq.toArray
                    let lows = quote0.GetProperty("low").EnumerateArray() |> Seq.toArray

                    let volumes =
                        match quote0.TryGetProperty("volume") with
                        | true, v -> v.EnumerateArray() |> Seq.toArray
                        | _ -> Array.empty

                    let adjCloses =
                        match indicators.TryGetProperty("adjclose") with
                        | true, adjArr ->
                            let adj0 = adjArr.[0]
                            match adj0.TryGetProperty("adjclose") with
                            | true, v -> v.EnumerateArray() |> Seq.toArray
                            | _ -> Array.empty
                        | _ -> Array.empty

                    let currency =
                        match result0.TryGetProperty("meta") with
                        | true, meta ->
                            match meta.TryGetProperty("currency") with
                            | true, v -> (v.GetString() |> Option.ofObj) |> Option.defaultValue "USD"
                            | _ -> "USD"
                        | _ -> "USD"

                    let inline tryGetFloat (el: System.Text.Json.JsonElement) : float option =
                        if el.ValueKind = System.Text.Json.JsonValueKind.Number then
                            el.GetDouble() |> Some
                        else
                            None

                    let toDate (unixSeconds: int64) =
                        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime.Date

                    let count = timestamps.Length

                    let bars =
                        [|
                            for i in 0 .. count - 1 do
                                let closeOpt = tryGetFloat closes.[i]
                                if closeOpt.IsSome then
                                    let ts = timestamps.[i].GetInt64()
                                    let openP = tryGetFloat opens.[i] |> Option.defaultValue closeOpt.Value
                                    let highP = tryGetFloat highs.[i] |> Option.defaultValue closeOpt.Value
                                    let lowP = tryGetFloat lows.[i] |> Option.defaultValue closeOpt.Value

                                    let volume =
                                        if i < volumes.Length then
                                            tryGetFloat volumes.[i] |> Option.defaultValue 0.0
                                        else
                                            0.0

                                    let adjClose =
                                        if i < adjCloses.Length then
                                            tryGetFloat adjCloses.[i]
                                        else
                                            None

                                    yield {
                                        Date = toDate (timestamps.[i].GetInt64())
                                        Open = openP
                                        High = highP
                                        Low = lowP
                                        Close = closeOpt.Value
                                        Volume = volume
                                        AdjustedClose = adjClose
                                    }
                        |]
                        |> Array.sortBy (fun b -> b.Date)

                    Ok {
                        Symbol = symbol
                        Name = None
                        Currency = currency
                        Prices = bars
                        Frequency = Daily
                    }
        with ex ->
            Error (QuantumError.OperationError ("YahooFinance.Parse", ex.Message))

    /// Download historical prices from Yahoo Finance's chart API (task-based).
    ///
    /// Note: Yahoo Finance does not provide an official public API; this uses the JSON
    /// endpoint used by their website.
    let fetchYahooHistoryAsync
        (httpClient: HttpClient)
        (request: YahooHistoryRequest)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<PriceSeries>> =
        task {
            let symbol = request.Symbol.Trim().ToUpperInvariant()
            if String.IsNullOrWhiteSpace symbol then
                return Error (QuantumError.ValidationError ("symbol", "Symbol must be non-empty"))
            else
                let range = request.Range.ToQueryString()
                let interval = request.Interval.ToQueryString()

                let url =
                    sprintf "https://query1.finance.yahoo.com/v8/finance/chart/%s?range=%s&interval=%s&includePrePost=false&events=div%%7Csplits" (Uri.EscapeDataString symbol) range interval
                    + (if request.IncludeAdjustedClose then "&includeAdjustedClose=true" else "")

                let cacheKey = sha256Hex url
                let cachePathOpt =
                    request.CacheDirectory
                    |> Option.map (fun dir -> Path.Combine(dir, sprintf "yahoo_chart_%s.json" cacheKey))

                // Try reading from cache asynchronously
                let! cachedJsonOpt =
                    match cachePathOpt with
                    | Some p -> tryReadFreshCacheAsync p request.CacheTtl cancellationToken
                    | None -> Task.FromResult None

                match cachedJsonOpt with
                | Some cachedJson ->
                    return parseYahooChartJson symbol cachedJson
                | None ->
                    try
                        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FSharp.Azure.Quantum/InvestmentPortfolio")
                    with _ -> ()

                    try
                        use req = new HttpRequestMessage(HttpMethod.Get, url)
                        let! resp = httpClient.SendAsync(req, cancellationToken)
                        let! body = resp.Content.ReadAsStringAsync(cancellationToken)

                        if not resp.IsSuccessStatusCode then
                            return Error (QuantumError.BackendError ("YahooFinance", $"HTTP {(int resp.StatusCode)}: {body}"))
                        else
                            // Write to cache asynchronously
                            match cachePathOpt with
                            | Some p -> do! tryWriteCacheAsync p body cancellationToken
                            | None -> ()
                            return parseYahooChartJson symbol body
                    with ex ->
                        return Error (QuantumError.BackendError ("YahooFinance", ex.Message))
        }

    /// Synchronous wrapper for fetchYahooHistoryAsync.
    [<Obsolete("Use fetchYahooHistoryAsync with CancellationToken instead.")>]
    let fetchYahooHistory
        (httpClient: HttpClient)
        (request: YahooHistoryRequest)
        : QuantumResult<PriceSeries> =
        fetchYahooHistoryAsync httpClient request CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    /// Convenience overload with defaults.
    [<Obsolete("Use fetchYahooHistoryAsync with CancellationToken instead.")>]
    let fetchYahooHistoryDefault (httpClient: HttpClient) (symbol: string) : QuantumResult<PriceSeries> =
        fetchYahooHistory httpClient (defaultYahooHistoryRequest symbol)

    // ========================================================================
    // RETURN CALCULATIONS
    // ========================================================================
    
    /// Calculate returns from price series
    let calculateReturns (priceSeries: PriceSeries) : ReturnSeries =
        let prices = priceSeries.Prices
        let n = prices.Length
        
        if n < 2 then
            {
                Symbol = priceSeries.Symbol
                StartDate = if n > 0 then prices.[0].Date else DateTime.MinValue
                EndDate = if n > 0 then prices.[n-1].Date else DateTime.MinValue
                LogReturns = [||]
                SimpleReturns = [||]
                Dates = [||]
            }
        else
            let logReturns = Array.init (n - 1) (fun i ->
                let p0 = prices.[i].AdjustedClose |> Option.defaultValue prices.[i].Close
                let p1 = prices.[i+1].AdjustedClose |> Option.defaultValue prices.[i+1].Close
                if p0 > 0.0 then log(p1 / p0) else 0.0)
            
            let simpleReturns = Array.init (n - 1) (fun i ->
                let p0 = prices.[i].AdjustedClose |> Option.defaultValue prices.[i].Close
                let p1 = prices.[i+1].AdjustedClose |> Option.defaultValue prices.[i+1].Close
                if p0 > 0.0 then (p1 - p0) / p0 else 0.0)
            
            let dates = Array.init (n - 1) (fun i -> prices.[i+1].Date)
            
            {
                Symbol = priceSeries.Symbol
                StartDate = prices.[0].Date
                EndDate = prices.[n-1].Date
                LogReturns = logReturns
                SimpleReturns = simpleReturns
                Dates = dates
            }
    
    /// Calculate annualized volatility from returns
    let calculateVolatility (returns: ReturnSeries) (annualizationFactor: float) : float =
        let n = returns.LogReturns.Length
        if n < 2 then 0.0
        else
            let mean = returns.LogReturns |> Array.average
            let variance = 
                returns.LogReturns 
                |> Array.map (fun r -> (r - mean) ** 2.0)
                |> Array.average
            sqrt(variance * annualizationFactor)

    /// Calculate annualized expected return from log returns.
    ///
    /// Typical usage: annualizationFactor = 252.0 for daily returns.
    let calculateExpectedReturn (returns: ReturnSeries) (annualizationFactor: float) : float =
        let n = returns.LogReturns.Length
        if n < 1 then 0.0
        else
            let meanLog = returns.LogReturns |> Array.average
            // Convert expected log return to expected simple return
            exp(meanLog * annualizationFactor) - 1.0

    /// Extract latest close from a PriceSeries.
    let tryGetLatestPrice (series: PriceSeries) : float option =
        series.Prices
        |> Array.sortBy (fun p -> p.Date)
        |> Array.tryLast
        |> Option.map (fun p -> p.AdjustedClose |> Option.defaultValue p.Close)

    // ========================================================================
    // CORRELATION & COVARIANCE
    // ========================================================================
    
    /// Align multiple return series by date
    let private alignReturns (returnSeries: ReturnSeries array) : (DateTime array * float array array) =
        // Find common dates
        let allDates = 
            returnSeries 
            |> Array.collect (fun rs -> rs.Dates)
            |> Array.distinct
            |> Array.sort
        
        // Create date lookup for each series
        let dateLookups =
            returnSeries
            |> Array.map (fun rs ->
                rs.Dates
                |> Array.mapi (fun i dt -> (dt, i))
                |> Map.ofArray)
        
        // Find dates present in all series
        let commonDates =
            allDates
            |> Array.filter (fun dt ->
                dateLookups |> Array.forall (fun lookup -> lookup.ContainsKey(dt)))
        
        // Extract aligned returns
        let alignedReturns =
            returnSeries
            |> Array.mapi (fun seriesIdx rs ->
                commonDates
                |> Array.map (fun dt ->
                    match dateLookups.[seriesIdx].TryFind(dt) with
                    | Some idx -> rs.LogReturns.[idx]
                    | None -> 0.0))
        
        (commonDates, alignedReturns)
    
    /// Calculate correlation matrix from multiple return series
    let calculateCorrelationMatrix (returnSeries: ReturnSeries array) : CorrelationMatrix =
        let n = returnSeries.Length
        let symbols = returnSeries |> Array.map (fun rs -> rs.Symbol)
        
        let (dates, alignedReturns) = alignReturns returnSeries
        let nObs = dates.Length
        
        // Calculate means
        let means = alignedReturns |> Array.map Array.average
        
        // Calculate standard deviations
        let stds = 
            alignedReturns
            |> Array.mapi (fun i returns ->
                let mean = means.[i]
                let variance = returns |> Array.map (fun r -> (r - mean) ** 2.0) |> Array.average
                sqrt variance)
        
        // Calculate correlation matrix
        let correlations =
            Array.init n (fun i ->
                Array.init n (fun j ->
                    if i = j then 1.0
                    elif stds.[i] = 0.0 || stds.[j] = 0.0 then 0.0
                    else
                        let cov = 
                            Array.zip alignedReturns.[i] alignedReturns.[j]
                            |> Array.map (fun (ri, rj) -> (ri - means.[i]) * (rj - means.[j]))
                            |> Array.average
                        cov / (stds.[i] * stds.[j])))
        
        {
            Assets = symbols
            Values = correlations
            StartDate = if dates.Length > 0 then dates.[0] else DateTime.MinValue
            EndDate = if dates.Length > 0 then dates.[dates.Length - 1] else DateTime.MinValue
            ObservationCount = nObs
        }
    
    /// Calculate covariance matrix from return series
    let calculateCovarianceMatrix 
        (returnSeries: ReturnSeries array) 
        (annualize: bool) 
        : CovarianceMatrix =
        
        let n = returnSeries.Length
        let symbols = returnSeries |> Array.map (fun rs -> rs.Symbol)
        
        let (_, alignedReturns) = alignReturns returnSeries
        
        // Calculate means
        let means = alignedReturns |> Array.map Array.average
        
        // Annualization factor (252 trading days)
        let annFactor = if annualize then 252.0 else 1.0
        
        // Calculate covariance matrix
        let covariances =
            Array.init n (fun i ->
                Array.init n (fun j ->
                    let cov = 
                        Array.zip alignedReturns.[i] alignedReturns.[j]
                        |> Array.map (fun (ri, rj) -> (ri - means.[i]) * (rj - means.[j]))
                        |> Array.average
                    cov * annFactor))
        
        {
            Assets = symbols
            Values = covariances
            IsAnnualized = annualize
        }
    
    // ========================================================================
    // PORTFOLIO LOADING
    // ========================================================================
    
    /// Load portfolio from CSV
    let loadPortfolioFromCsv (filePath: string) (portfolioName: string) : QuantumResult<Portfolio> =
        try
            let lines = System.IO.File.ReadAllLines(filePath)
            if lines.Length < 2 then
                Error (QuantumError.ValidationError ("file", "CSV must have header and at least one position"))
            else
                let headers = lines.[0].Split(',') |> Array.map (fun s -> s.Trim().Trim('"').ToLower())
                
                let symbolIdx = headers |> Array.tryFindIndex (fun h -> h = "symbol" || h = "ticker")
                let quantityIdx = headers |> Array.tryFindIndex (fun h -> h = "quantity" || h = "shares")
                let priceIdx = headers |> Array.tryFindIndex (fun h -> h = "price" || h = "current_price")
                let assetClassIdx = headers |> Array.tryFindIndex (fun h -> h = "asset_class" || h = "type")
                let sectorIdx = headers |> Array.tryFindIndex (fun h -> h = "sector")
                
                match symbolIdx, quantityIdx, priceIdx with
                | Some sIdx, Some qIdx, Some pIdx ->
                    let posArray =
                        lines.[1..]
                        |> Array.map (fun line ->
                            let fields = line.Split(',') |> Array.map (fun s -> s.Trim().Trim('"'))
                            
                            let symbol = fields.[sIdx]
                            let quantity = parseFloat fields.[qIdx] |> Option.defaultValue 0.0
                            let price = parseFloat fields.[pIdx] |> Option.defaultValue 0.0
                            
                            let assetClass =
                                assetClassIdx
                                |> Option.map (fun idx -> 
                                    match fields.[idx].ToLower() with
                                    | "equity" | "stock" -> Equity
                                    | "fixed_income" | "bond" -> FixedIncome
                                    | "commodity" -> Commodity
                                    | "currency" | "fx" -> Currency
                                    | "derivative" -> Derivative
                                    | "alternative" -> Alternative
                                    | "cash" -> Cash
                                    | _ -> Equity)
                                |> Option.defaultValue Equity
                            
                            let sector = sectorIdx |> Option.map (fun idx -> fields.[idx])
                            
                            {
                                Symbol = symbol
                                Quantity = quantity
                                CurrentPrice = price
                                MarketValue = quantity * price
                                AssetClass = assetClass
                                Sector = sector
                            })
                    let totalValue = posArray |> Array.sumBy (fun p -> p.MarketValue)
                    
                    Ok {
                        Id = Guid.NewGuid().ToString()
                        Name = portfolioName
                        BaseCurrency = "USD"
                        Positions = posArray
                        TotalValue = totalValue
                        ValuationDate = DateTime.UtcNow
                    }
                    
                | _ ->
                    Error (QuantumError.ValidationError ("columns", "CSV must have symbol, quantity, and price columns"))
        with ex ->
            Error (QuantumError.Other (sprintf "Failed to read portfolio CSV: %s" ex.Message))
    
    /// Load portfolio from CSV asynchronously
    let loadPortfolioFromCsvAsync (filePath: string) (portfolioName: string) (cancellationToken: CancellationToken) : Task<QuantumResult<Portfolio>> =
        task {
            try
                let! allText = File.ReadAllTextAsync(filePath, cancellationToken)
                let lines = allText.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
                if lines.Length < 2 then
                    return Error (QuantumError.ValidationError ("file", "CSV must have header and at least one position"))
                else
                    let headers = lines.[0].Split(',') |> Array.map (fun s -> s.Trim().Trim('"').ToLower())
                    
                    let symbolIdx = headers |> Array.tryFindIndex (fun h -> h = "symbol" || h = "ticker")
                    let quantityIdx = headers |> Array.tryFindIndex (fun h -> h = "quantity" || h = "shares")
                    let priceIdx = headers |> Array.tryFindIndex (fun h -> h = "price" || h = "current_price")
                    let assetClassIdx = headers |> Array.tryFindIndex (fun h -> h = "asset_class" || h = "type")
                    let sectorIdx = headers |> Array.tryFindIndex (fun h -> h = "sector")
                    
                    match symbolIdx, quantityIdx, priceIdx with
                    | Some sIdx, Some qIdx, Some pIdx ->
                        let posArray =
                            lines.[1..]
                            |> Array.map (fun line ->
                                let fields = line.Split(',') |> Array.map (fun s -> s.Trim().Trim('"'))
                                
                                let symbol = fields.[sIdx]
                                let quantity = parseFloat fields.[qIdx] |> Option.defaultValue 0.0
                                let price = parseFloat fields.[pIdx] |> Option.defaultValue 0.0
                                
                                let assetClass =
                                    assetClassIdx
                                    |> Option.map (fun idx -> 
                                        match fields.[idx].ToLower() with
                                        | "equity" | "stock" -> Equity
                                        | "fixed_income" | "bond" -> FixedIncome
                                        | "commodity" -> Commodity
                                        | "currency" | "fx" -> Currency
                                        | "derivative" -> Derivative
                                        | "alternative" -> Alternative
                                        | "cash" -> Cash
                                        | _ -> Equity)
                                    |> Option.defaultValue Equity
                                
                                let sector = sectorIdx |> Option.map (fun idx -> fields.[idx])
                                
                                {
                                    Symbol = symbol
                                    Quantity = quantity
                                    CurrentPrice = price
                                    MarketValue = quantity * price
                                    AssetClass = assetClass
                                    Sector = sector
                                })
                        let totalValue = posArray |> Array.sumBy (fun p -> p.MarketValue)
                        
                        return Ok {
                            Id = Guid.NewGuid().ToString()
                            Name = portfolioName
                            BaseCurrency = "USD"
                            Positions = posArray
                            TotalValue = totalValue
                            ValuationDate = DateTime.UtcNow
                        }
                        
                    | _ ->
                        return Error (QuantumError.ValidationError ("columns", "CSV must have symbol, quantity, and price columns"))
            with ex ->
                return Error (QuantumError.Other (sprintf "Failed to read portfolio CSV: %s" ex.Message))
        }
    
    /// Create portfolio from position list
    let createPortfolio (name: string) (positions: Position list) : Portfolio =
        let posArray = positions |> List.toArray
        {
            Id = Guid.NewGuid().ToString()
            Name = name
            BaseCurrency = "USD"
            Positions = posArray
            TotalValue = posArray |> Array.sumBy (fun p -> p.MarketValue)
            ValuationDate = DateTime.UtcNow
        }
    
    // ========================================================================
    // VAR CALCULATIONS (Classical baseline)
    // ========================================================================
    
    /// Calculate parametric VaR (normal distribution assumption)
    let calculateParametricVaR 
        (portfolio: Portfolio) 
        (covMatrix: CovarianceMatrix) 
        (riskParams: RiskParameters) 
        : QuantumResult<VaRResult> =
        
        // Find portfolio weights
        let weights =
            covMatrix.Assets
            |> Array.map (fun symbol ->
                match portfolio.Positions |> Array.tryFind (fun p -> p.Symbol = symbol) with
                | Some pos -> pos.MarketValue / portfolio.TotalValue
                | None -> 0.0)
        
        // Calculate portfolio variance: w' * Σ * w
        let portfolioVariance =
            let n = weights.Length
            seq {
                for i in 0 .. n - 1 do
                    for j in 0 .. n - 1 do
                        yield weights.[i] * weights.[j] * covMatrix.Values.[i].[j]
            }
            |> Seq.sum
        
        let portfolioStd = sqrt portfolioVariance
        
        // Scale by time horizon (sqrt of time for variance)
        let timeScaledStd = portfolioStd * sqrt(float riskParams.TimeHorizon / 252.0)
        
        // Z-score for confidence level
        let zScore =
            match riskParams.Distribution with
            | Normal -> 
                // Approximate normal quantile
                let p = riskParams.ConfidenceLevel
                // Beasley-Springer-Moro approximation
                let a = [| 2.50662823884; -18.61500062529; 41.39119773534; -25.44106049637 |]
                let b = [| -8.47351093090; 23.08336743743; -21.06224101826; 3.13082909833 |]
                let c = [| 0.3374754822726147; 0.9761690190917186; 0.1607979714918209;
                           0.0276438810333863; 0.0038405729373609; 0.0003951896511919;
                           0.0000321767881768; 0.0000002888167364; 0.0000003960315187 |]
                
                let y = p - 0.5
                if abs y < 0.42 then
                    let r = y * y
                    y * (((a.[3] * r + a.[2]) * r + a.[1]) * r + a.[0]) /
                        ((((b.[3] * r + b.[2]) * r + b.[1]) * r + b.[0]) * r + 1.0)
                else
                    let r = if y < 0.0 then p else 1.0 - p
                    let s = log(-log(r))
                    let sign = if y < 0.0 then -1.0 else 1.0
                    sign * (c.[0] + s * (c.[1] + s * (c.[2] + s * (c.[3] + s * (c.[4] + 
                           s * (c.[5] + s * (c.[6] + s * (c.[7] + s * c.[8]))))))))
            | StudentT df -> 
                // Approximate Student-t quantile (use normal as approximation for large df)
                2.326  // 99% for df > 30
            | ReturnDistribution.Historical -> 2.326
            | LogNormal -> 2.326
        
        let var = portfolio.TotalValue * timeScaledStd * zScore
        
        // Expected Shortfall (approximate: ES ≈ VaR * (φ(z) / (1-p)))
        // where φ is standard normal PDF
        let normalPdf z = exp(-z * z / 2.0) / sqrt(2.0 * Math.PI)
        let es = var * (normalPdf zScore) / (1.0 - riskParams.ConfidenceLevel)
        
        Ok {
            VaR = var
            ExpectedShortfall = es
            ConfidenceLevel = riskParams.ConfidenceLevel
            TimeHorizon = riskParams.TimeHorizon
            Method = "Parametric (Normal)"
            PortfolioValue = portfolio.TotalValue
            VaRPercent = var / portfolio.TotalValue
        }
    
    /// Calculate historical VaR (non-parametric)
    let calculateHistoricalVaR
        (portfolio: Portfolio)
        (returnSeries: ReturnSeries array)
        (riskParams: RiskParameters)
        : QuantumResult<VaRResult> =
        
        // Align returns
        let (_, alignedReturns) = alignReturns returnSeries
        
        if alignedReturns.Length = 0 || alignedReturns.[0].Length < 10 then
            Error (QuantumError.ValidationError ("data", "Insufficient historical data for VaR calculation"))
        else
            // Calculate portfolio returns
            let weights =
                returnSeries
                |> Array.map (fun rs ->
                    match portfolio.Positions |> Array.tryFind (fun p -> p.Symbol = rs.Symbol) with
                    | Some pos -> pos.MarketValue / portfolio.TotalValue
                    | None -> 0.0)
            
            let nObs = alignedReturns.[0].Length
            let portfolioReturns = 
                Array.init nObs (fun t ->
                    Array.zip weights alignedReturns
                    |> Array.sumBy (fun (w, returns) -> w * returns.[t]))
            
            // Scale returns by time horizon
            let scaledReturns = 
                portfolioReturns 
                |> Array.map (fun r -> r * sqrt(float riskParams.TimeHorizon))
            
            // Sort returns (ascending = worst first)
            let sortedReturns = scaledReturns |> Array.sort
            
            // Find VaR percentile
            let percentileIndex = int (float nObs * (1.0 - riskParams.ConfidenceLevel))
            let varReturn = -sortedReturns.[max 0 percentileIndex]
            let var = portfolio.TotalValue * varReturn
            
            // Expected Shortfall = average of returns worse than VaR
            let tailReturns = sortedReturns.[0 .. percentileIndex]
            let esReturn = -(tailReturns |> Array.average)
            let es = portfolio.TotalValue * esReturn
            
            Ok {
                VaR = var
                ExpectedShortfall = es
                ConfidenceLevel = riskParams.ConfidenceLevel
                TimeHorizon = riskParams.TimeHorizon
                Method = "Historical Simulation"
                PortfolioValue = portfolio.TotalValue
                VaRPercent = var / portfolio.TotalValue
            }
    
    // ========================================================================
    // STRESS TESTING
    // ========================================================================
    
    /// Define common stress scenarios
    let financialCrisis2008 : StressScenario = {
        Name = "2008 Financial Crisis"
        Type = Historical (DateTime(2008, 9, 15), DateTime(2009, 3, 9))
        Shocks = Map.ofList [
            ("Equity", -0.50)        // 50% equity decline
            ("FixedIncome", -0.10)   // 10% bond decline (credit stress)
            ("Commodity", -0.40)     // 40% commodity decline
        ]
        CorrelationShock = Some 1.5  // Correlations increase in crisis
    }
    
    let covidCrash2020 : StressScenario = {
        Name = "COVID-19 March 2020"
        Type = Historical (DateTime(2020, 2, 19), DateTime(2020, 3, 23))
        Shocks = Map.ofList [
            ("Equity", -0.34)        // 34% equity decline
            ("FixedIncome", 0.05)    // 5% bond gain (flight to quality)
            ("Commodity", -0.30)     // 30% commodity decline
        ]
        CorrelationShock = Some 1.3
    }
    
    let interestRateShock : StressScenario = {
        Name = "Interest Rate Shock (+300bp)"
        Type = Hypothetical
        Shocks = Map.ofList [
            ("Equity", -0.15)        // 15% equity decline
            ("FixedIncome", -0.20)   // 20% bond decline (duration effect)
        ]
        CorrelationShock = None
    }
    
    /// Apply stress scenario to portfolio
    let applyStressScenario 
        (portfolio: Portfolio) 
        (scenario: StressScenario) 
        : float =
        
        // Calculate stressed portfolio value
        let stressedPositions =
            portfolio.Positions
            |> Array.map (fun pos ->
                let assetClassKey = 
                    match pos.AssetClass with
                    | Equity -> "Equity"
                    | FixedIncome -> "FixedIncome"
                    | Commodity -> "Commodity"
                    | Currency -> "Currency"
                    | Derivative -> "Derivative"
                    | Alternative -> "Alternative"
                    | Cash -> "Cash"
                
                // Look up shock by asset class or symbol
                let shock =
                    scenario.Shocks 
                    |> Map.tryFind pos.Symbol
                    |> Option.orElse (scenario.Shocks |> Map.tryFind assetClassKey)
                    |> Option.defaultValue 0.0
                
                pos.MarketValue * (1.0 + shock))
        
        stressedPositions |> Array.sum
    
    // ========================================================================
    // FEATURE EXTRACTION (for quantum ML)
    // ========================================================================
    
    /// Extract features from return series for ML
    let extractReturnFeatures (returns: ReturnSeries) : float array =
        let logRet = returns.LogReturns
        let n = logRet.Length
        
        if n < 2 then
            Array.create 10 0.0
        else
            let mean = logRet |> Array.average
            let variance = logRet |> Array.map (fun r -> (r - mean) ** 2.0) |> Array.average
            let std = sqrt variance
            
            // Skewness
            let skewness =
                if std = 0.0 then 0.0
                else
                    let m3 = logRet |> Array.map (fun r -> ((r - mean) / std) ** 3.0) |> Array.average
                    m3
            
            // Kurtosis
            let kurtosis =
                if std = 0.0 then 0.0
                else
                    let m4 = logRet |> Array.map (fun r -> ((r - mean) / std) ** 4.0) |> Array.average
                    m4 - 3.0  // Excess kurtosis
            
            // Max drawdown
            let cumReturns = 
                logRet 
                |> Array.scan (fun acc r -> acc + r) 0.0
                |> Array.tail
            let peaks = 
                cumReturns 
                |> Array.scan max (cumReturns.[0])
                |> Array.tail
            let drawdowns = Array.zip peaks cumReturns |> Array.map (fun (p, c) -> c - p)
            let maxDrawdown = drawdowns |> Array.min |> abs
            
            // VaR 95%
            let sortedReturns = logRet |> Array.sort
            let var95Idx = int (0.05 * float n)
            let var95 = -sortedReturns.[max 0 var95Idx]
            
            [|
                mean * 252.0           // Annualized mean return
                std * sqrt(252.0)      // Annualized volatility
                skewness
                kurtosis
                maxDrawdown
                var95
                float n                // Sample size
                mean / (std + 0.001)   // Sharpe ratio (simplified)
                logRet.[n-1]           // Most recent return
                (if n > 5 then logRet.[n-5..n-1] |> Array.average else 0.0)  // 5-day MA
            |]
    
    /// Convert portfolio weights to feature array
    let portfolioToFeatures (portfolio: Portfolio) : float array =
        portfolio.Positions
        |> Array.map (fun p -> p.MarketValue / portfolio.TotalValue)
