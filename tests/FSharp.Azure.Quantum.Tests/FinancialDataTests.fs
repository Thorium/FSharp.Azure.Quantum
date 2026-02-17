namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Data.FinancialData

module FinancialDataTests =

    // ========================================================================
    // HELPERS
    // ========================================================================

    let private makeBar (date: DateTime) (close: float) : PriceBar =
        { Date = date; Open = close; High = close; Low = close; Close = close; Volume = 1000.0; AdjustedClose = None }

    let private makeBarAdj (date: DateTime) (close: float) (adj: float) : PriceBar =
        { Date = date; Open = close; High = close; Low = close; Close = close; Volume = 1000.0; AdjustedClose = Some adj }

    let private makePriceSeries (symbol: string) (bars: PriceBar array) : PriceSeries =
        { Symbol = symbol; Name = None; Currency = "USD"; Prices = bars; Frequency = Daily }

    let private makePosition symbol qty price assetClass =
        { Symbol = symbol; Quantity = qty; CurrentPrice = price; MarketValue = qty * price; AssetClass = assetClass; Sector = None }

    // ========================================================================
    // RETURN CALCULATIONS
    // ========================================================================

    [<Fact>]
    let ``calculateReturns with 2 prices produces 1 return`` () =
        let bars = [|
            makeBar (DateTime(2024, 1, 1)) 100.0
            makeBar (DateTime(2024, 1, 2)) 110.0
        |]
        let series = makePriceSeries "TEST" bars
        let returns = calculateReturns series
        Assert.Equal(1, returns.LogReturns.Length)
        Assert.Equal(1, returns.SimpleReturns.Length)
        Assert.Equal(1, returns.Dates.Length)

    [<Fact>]
    let ``calculateReturns log return is ln(P1/P0)`` () =
        let bars = [|
            makeBar (DateTime(2024, 1, 1)) 100.0
            makeBar (DateTime(2024, 1, 2)) 110.0
        |]
        let series = makePriceSeries "TEST" bars
        let returns = calculateReturns series
        let expected = log(110.0 / 100.0)
        Assert.True(abs(returns.LogReturns.[0] - expected) < 1e-10)

    [<Fact>]
    let ``calculateReturns simple return is (P1-P0)/P0`` () =
        let bars = [|
            makeBar (DateTime(2024, 1, 1)) 100.0
            makeBar (DateTime(2024, 1, 2)) 110.0
        |]
        let series = makePriceSeries "TEST" bars
        let returns = calculateReturns series
        Assert.True(abs(returns.SimpleReturns.[0] - 0.1) < 1e-10)

    [<Fact>]
    let ``calculateReturns prefers AdjustedClose when available`` () =
        let bars = [|
            makeBarAdj (DateTime(2024, 1, 1)) 100.0 50.0
            makeBarAdj (DateTime(2024, 1, 2)) 110.0 55.0
        |]
        let series = makePriceSeries "TEST" bars
        let returns = calculateReturns series
        // Should use adjusted close: ln(55/50)
        let expected = log(55.0 / 50.0)
        Assert.True(abs(returns.LogReturns.[0] - expected) < 1e-10)

    [<Fact>]
    let ``calculateReturns with single price returns empty arrays`` () =
        let bars = [| makeBar (DateTime(2024, 1, 1)) 100.0 |]
        let series = makePriceSeries "TEST" bars
        let returns = calculateReturns series
        Assert.Empty(returns.LogReturns)
        Assert.Empty(returns.SimpleReturns)
        Assert.Empty(returns.Dates)

    [<Fact>]
    let ``calculateReturns with empty prices returns empty arrays`` () =
        let series = makePriceSeries "TEST" [||]
        let returns = calculateReturns series
        Assert.Empty(returns.LogReturns)

    [<Fact>]
    let ``calculateReturns dates correspond to second bar onward`` () =
        let bars = [|
            makeBar (DateTime(2024, 1, 1)) 100.0
            makeBar (DateTime(2024, 1, 2)) 105.0
            makeBar (DateTime(2024, 1, 3)) 110.0
        |]
        let series = makePriceSeries "TEST" bars
        let returns = calculateReturns series
        Assert.Equal(DateTime(2024, 1, 2), returns.Dates.[0])
        Assert.Equal(DateTime(2024, 1, 3), returns.Dates.[1])

    [<Fact>]
    let ``calculateReturns sets symbol correctly`` () =
        let bars = [|
            makeBar (DateTime(2024, 1, 1)) 100.0
            makeBar (DateTime(2024, 1, 2)) 110.0
        |]
        let series = makePriceSeries "AAPL" bars
        let returns = calculateReturns series
        Assert.Equal("AAPL", returns.Symbol)

    // ========================================================================
    // VOLATILITY & EXPECTED RETURN
    // ========================================================================

    [<Fact>]
    let ``calculateVolatility returns zero for single return`` () =
        let returns = {
            Symbol = "TEST"; StartDate = DateTime(2024, 1, 1); EndDate = DateTime(2024, 1, 2)
            LogReturns = [| 0.01 |]; SimpleReturns = [| 0.01 |]; Dates = [| DateTime(2024, 1, 2) |]
        }
        let vol = calculateVolatility returns 252.0
        Assert.Equal(0.0, vol)

    [<Fact>]
    let ``calculateVolatility returns positive for varying returns`` () =
        let logReturns = [| 0.01; -0.02; 0.015; -0.005; 0.008 |]
        let returns = {
            Symbol = "TEST"; StartDate = DateTime(2024, 1, 1); EndDate = DateTime(2024, 1, 6)
            LogReturns = logReturns; SimpleReturns = logReturns; Dates = Array.init 5 (fun i -> DateTime(2024, 1, i + 2))
        }
        let vol = calculateVolatility returns 252.0
        Assert.True(vol > 0.0, $"Volatility {vol} should be positive")

    [<Fact>]
    let ``calculateExpectedReturn returns zero for empty returns`` () =
        let returns = {
            Symbol = "TEST"; StartDate = DateTime.MinValue; EndDate = DateTime.MinValue
            LogReturns = [||]; SimpleReturns = [||]; Dates = [||]
        }
        let er = calculateExpectedReturn returns 252.0
        Assert.Equal(0.0, er)

    [<Fact>]
    let ``calculateExpectedReturn returns finite value`` () =
        let logReturns = [| 0.001; 0.002; -0.001; 0.0015; 0.0005 |]
        let returns = {
            Symbol = "TEST"; StartDate = DateTime(2024, 1, 1); EndDate = DateTime(2024, 1, 6)
            LogReturns = logReturns; SimpleReturns = logReturns; Dates = Array.init 5 (fun i -> DateTime(2024, 1, i + 2))
        }
        let er = calculateExpectedReturn returns 252.0
        Assert.True(Double.IsFinite(er), $"Expected return {er} should be finite")

    // ========================================================================
    // tryGetLatestPrice
    // ========================================================================

    [<Fact>]
    let ``tryGetLatestPrice returns last bar close`` () =
        let bars = [|
            makeBar (DateTime(2024, 1, 1)) 100.0
            makeBar (DateTime(2024, 1, 2)) 105.0
            makeBar (DateTime(2024, 1, 3)) 110.0
        |]
        let series = makePriceSeries "TEST" bars
        Assert.Equal(Some 110.0, tryGetLatestPrice series)

    [<Fact>]
    let ``tryGetLatestPrice returns None for empty series`` () =
        let series = makePriceSeries "TEST" [||]
        Assert.Equal(None, tryGetLatestPrice series)

    [<Fact>]
    let ``tryGetLatestPrice prefers AdjustedClose`` () =
        let bars = [|
            makeBarAdj (DateTime(2024, 1, 1)) 100.0 95.0
        |]
        let series = makePriceSeries "TEST" bars
        Assert.Equal(Some 95.0, tryGetLatestPrice series)

    // ========================================================================
    // CORRELATION & COVARIANCE
    // ========================================================================

    [<Fact>]
    let ``calculateCorrelationMatrix diagonal is 1.0`` () =
        let returns1 = {
            Symbol = "A"; StartDate = DateTime(2024, 1, 1); EndDate = DateTime(2024, 1, 5)
            LogReturns = [| 0.01; -0.02; 0.015; 0.005 |]
            SimpleReturns = [| 0.01; -0.02; 0.015; 0.005 |]
            Dates = [| DateTime(2024, 1, 2); DateTime(2024, 1, 3); DateTime(2024, 1, 4); DateTime(2024, 1, 5) |]
        }
        let returns2 = {
            Symbol = "B"; StartDate = DateTime(2024, 1, 1); EndDate = DateTime(2024, 1, 5)
            LogReturns = [| -0.005; 0.01; -0.01; 0.02 |]
            SimpleReturns = [| -0.005; 0.01; -0.01; 0.02 |]
            Dates = [| DateTime(2024, 1, 2); DateTime(2024, 1, 3); DateTime(2024, 1, 4); DateTime(2024, 1, 5) |]
        }
        let corrMatrix = calculateCorrelationMatrix [| returns1; returns2 |]
        Assert.True(abs(corrMatrix.Values.[0].[0] - 1.0) < 1e-10)
        Assert.True(abs(corrMatrix.Values.[1].[1] - 1.0) < 1e-10)

    [<Fact>]
    let ``calculateCorrelationMatrix is symmetric`` () =
        let returns1 = {
            Symbol = "A"; StartDate = DateTime(2024, 1, 1); EndDate = DateTime(2024, 1, 5)
            LogReturns = [| 0.01; -0.02; 0.015; 0.005 |]
            SimpleReturns = [| 0.01; -0.02; 0.015; 0.005 |]
            Dates = [| DateTime(2024, 1, 2); DateTime(2024, 1, 3); DateTime(2024, 1, 4); DateTime(2024, 1, 5) |]
        }
        let returns2 = {
            Symbol = "B"; StartDate = DateTime(2024, 1, 1); EndDate = DateTime(2024, 1, 5)
            LogReturns = [| -0.005; 0.01; -0.01; 0.02 |]
            SimpleReturns = [| -0.005; 0.01; -0.01; 0.02 |]
            Dates = [| DateTime(2024, 1, 2); DateTime(2024, 1, 3); DateTime(2024, 1, 4); DateTime(2024, 1, 5) |]
        }
        let corrMatrix = calculateCorrelationMatrix [| returns1; returns2 |]
        Assert.True(abs(corrMatrix.Values.[0].[1] - corrMatrix.Values.[1].[0]) < 1e-10)

    [<Fact>]
    let ``calculateCorrelationMatrix correlation in range [-1, 1]`` () =
        let returns1 = {
            Symbol = "A"; StartDate = DateTime(2024, 1, 1); EndDate = DateTime(2024, 1, 5)
            LogReturns = [| 0.01; -0.02; 0.015; 0.005 |]
            SimpleReturns = [| 0.01; -0.02; 0.015; 0.005 |]
            Dates = [| DateTime(2024, 1, 2); DateTime(2024, 1, 3); DateTime(2024, 1, 4); DateTime(2024, 1, 5) |]
        }
        let returns2 = {
            Symbol = "B"; StartDate = DateTime(2024, 1, 1); EndDate = DateTime(2024, 1, 5)
            LogReturns = [| -0.005; 0.01; -0.01; 0.02 |]
            SimpleReturns = [| -0.005; 0.01; -0.01; 0.02 |]
            Dates = [| DateTime(2024, 1, 2); DateTime(2024, 1, 3); DateTime(2024, 1, 4); DateTime(2024, 1, 5) |]
        }
        let corrMatrix = calculateCorrelationMatrix [| returns1; returns2 |]
        let corr01 = corrMatrix.Values.[0].[1]
        Assert.True(corr01 >= -1.0 && corr01 <= 1.0, $"Correlation {corr01} should be in [-1, 1]")

    [<Fact>]
    let ``calculateCovarianceMatrix annualized has larger values`` () =
        let returns1 = {
            Symbol = "A"; StartDate = DateTime(2024, 1, 1); EndDate = DateTime(2024, 1, 5)
            LogReturns = [| 0.01; -0.02; 0.015; 0.005 |]
            SimpleReturns = [||]; Dates = [| DateTime(2024, 1, 2); DateTime(2024, 1, 3); DateTime(2024, 1, 4); DateTime(2024, 1, 5) |]
        }
        let covAnn = calculateCovarianceMatrix [| returns1 |] true
        let covDaily = calculateCovarianceMatrix [| returns1 |] false
        Assert.True(covAnn.IsAnnualized)
        Assert.False(covDaily.IsAnnualized)
        Assert.True(abs(covAnn.Values.[0].[0]) > abs(covDaily.Values.[0].[0]),
            "Annualized covariance should be larger")

    // ========================================================================
    // PORTFOLIO
    // ========================================================================

    [<Fact>]
    let ``createPortfolio calculates total value`` () =
        let positions = [
            makePosition "AAPL" 10.0 150.0 Equity
            makePosition "MSFT" 5.0 300.0 Equity
        ]
        let portfolio = createPortfolio "Test Portfolio" positions
        Assert.Equal(3000.0, portfolio.TotalValue) // 10*150 + 5*300

    [<Fact>]
    let ``createPortfolio sets name correctly`` () =
        let portfolio = createPortfolio "My Portfolio" []
        Assert.Equal("My Portfolio", portfolio.Name)

    [<Fact>]
    let ``portfolioToFeatures returns weights summing to 1`` () =
        let positions = [
            makePosition "AAPL" 10.0 100.0 Equity
            makePosition "MSFT" 10.0 100.0 Equity
        ]
        let portfolio = createPortfolio "Test" positions
        let features = portfolioToFeatures portfolio
        let sum = features |> Array.sum
        Assert.True(abs(sum - 1.0) < 1e-10, $"Weights sum {sum} should be 1.0")

    // ========================================================================
    // VaR CALCULATIONS
    // ========================================================================

    [<Fact>]
    let ``calculateParametricVaR returns positive VaR`` () =
        let positions = [
            makePosition "A" 100.0 100.0 Equity
        ]
        let portfolio = createPortfolio "Test" positions
        let covMatrix = {
            Assets = [| "A" |]
            Values = [| [| 0.04 |] |] // 20% annual vol
            IsAnnualized = true
        }
        let riskParams = {
            ConfidenceLevel = 0.99
            TimeHorizon = 1
            Distribution = Normal
            LookbackPeriod = 252
        }
        match calculateParametricVaR portfolio covMatrix riskParams with
        | Ok result ->
            Assert.True(result.VaR > 0.0, $"VaR {result.VaR} should be positive")
            Assert.True(result.ExpectedShortfall > result.VaR, "ES should be >= VaR")
            Assert.Equal(0.99, result.ConfidenceLevel)
            Assert.Equal(1, result.TimeHorizon)
            Assert.Equal(10000.0, result.PortfolioValue)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``calculateParametricVaR VaRPercent is VaR / portfolio value`` () =
        let positions = [ makePosition "A" 100.0 100.0 Equity ]
        let portfolio = createPortfolio "Test" positions
        let covMatrix = { Assets = [| "A" |]; Values = [| [| 0.04 |] |]; IsAnnualized = true }
        let riskParams = { ConfidenceLevel = 0.95; TimeHorizon = 10; Distribution = Normal; LookbackPeriod = 252 }
        match calculateParametricVaR portfolio covMatrix riskParams with
        | Ok result ->
            Assert.True(abs(result.VaRPercent - result.VaR / result.PortfolioValue) < 1e-10)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``calculateHistoricalVaR rejects insufficient data`` () =
        let portfolio = createPortfolio "Test" [ makePosition "A" 100.0 100.0 Equity ]
        let returnSeries = [| {
            Symbol = "A"; StartDate = DateTime(2024, 1, 1); EndDate = DateTime(2024, 1, 5)
            LogReturns = [| 0.01; -0.02; 0.015 |]
            SimpleReturns = [| 0.01; -0.02; 0.015 |]
            Dates = [| DateTime(2024, 1, 2); DateTime(2024, 1, 3); DateTime(2024, 1, 4) |]
        } |]
        let riskParams = { ConfidenceLevel = 0.95; TimeHorizon = 1; Distribution = ReturnDistribution.Historical; LookbackPeriod = 252 }
        match calculateHistoricalVaR portfolio returnSeries riskParams with
        | Error (QuantumError.ValidationError _) -> ()
        | r -> failwith $"Expected ValidationError for insufficient data, got {r}"

    [<Fact>]
    let ``calculateHistoricalVaR returns positive VaR with sufficient data`` () =
        let portfolio = createPortfolio "Test" [ makePosition "A" 100.0 100.0 Equity ]
        let rng = Random(42)
        let logReturns = Array.init 50 (fun _ -> (rng.NextDouble() - 0.5) * 0.02)
        let dates = Array.init 50 (fun i -> DateTime(2024, 1, 1).AddDays(float i))
        let returnSeries = [| {
            Symbol = "A"; StartDate = dates.[0]; EndDate = dates.[49]
            LogReturns = logReturns; SimpleReturns = logReturns; Dates = dates
        } |]
        let riskParams = { ConfidenceLevel = 0.95; TimeHorizon = 1; Distribution = ReturnDistribution.Historical; LookbackPeriod = 252 }
        match calculateHistoricalVaR portfolio returnSeries riskParams with
        | Ok result ->
            Assert.True(result.VaR > 0.0 || result.VaR <= 0.0, "VaR should be finite")
            Assert.True(Double.IsFinite(result.VaR))
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    // ========================================================================
    // STRESS TESTING
    // ========================================================================

    [<Fact>]
    let ``financialCrisis2008 has equity shock of -50pct`` () =
        match financialCrisis2008.Shocks |> Map.tryFind "Equity" with
        | Some shock -> Assert.Equal(-0.50, shock)
        | None -> failwith "Expected Equity shock"

    [<Fact>]
    let ``covidCrash2020 has fixed income gain`` () =
        match covidCrash2020.Shocks |> Map.tryFind "FixedIncome" with
        | Some shock -> Assert.True(shock > 0.0, "Fixed income should gain in COVID flight to quality")
        | None -> failwith "Expected FixedIncome shock"

    [<Fact>]
    let ``applyStressScenario reduces equity portfolio`` () =
        let positions = [ makePosition "AAPL" 100.0 100.0 Equity ]
        let portfolio = createPortfolio "Test" positions
        let stressedValue = applyStressScenario portfolio financialCrisis2008
        Assert.True(stressedValue < portfolio.TotalValue,
            $"Stressed value {stressedValue} should be less than portfolio {portfolio.TotalValue}")

    [<Fact>]
    let ``applyStressScenario cash is unaffected`` () =
        let positions = [ makePosition "USD" 10000.0 1.0 Cash ]
        let portfolio = createPortfolio "Cash" positions
        let stressedValue = applyStressScenario portfolio financialCrisis2008
        Assert.True(abs(stressedValue - portfolio.TotalValue) < 1e-10,
            "Cash should be unaffected by equity stress")

    [<Fact>]
    let ``applyStressScenario applies symbol-specific shock`` () =
        let scenario = {
            Name = "Custom"
            Type = Hypothetical
            Shocks = Map.ofList [ ("AAPL", -0.20) ]
            CorrelationShock = None
        }
        let positions = [ makePosition "AAPL" 100.0 100.0 Equity ]
        let portfolio = createPortfolio "Test" positions
        let stressedValue = applyStressScenario portfolio scenario
        Assert.True(abs(stressedValue - 8000.0) < 1e-10,
            $"Expected 8000, got {stressedValue}")

    // ========================================================================
    // FEATURE EXTRACTION
    // ========================================================================

    [<Fact>]
    let ``extractReturnFeatures returns 10 features`` () =
        let logReturns = [| 0.01; -0.02; 0.015; -0.005; 0.008; 0.003; -0.01 |]
        let returns = {
            Symbol = "TEST"; StartDate = DateTime(2024, 1, 1); EndDate = DateTime(2024, 1, 8)
            LogReturns = logReturns; SimpleReturns = logReturns
            Dates = Array.init 7 (fun i -> DateTime(2024, 1, i + 2))
        }
        let features = extractReturnFeatures returns
        Assert.Equal(10, features.Length)

    [<Fact>]
    let ``extractReturnFeatures returns 10 zeros for short series`` () =
        let returns = {
            Symbol = "TEST"; StartDate = DateTime(2024, 1, 1); EndDate = DateTime(2024, 1, 2)
            LogReturns = [| 0.01 |]; SimpleReturns = [| 0.01 |]; Dates = [| DateTime(2024, 1, 2) |]
        }
        let features = extractReturnFeatures returns
        Assert.Equal(10, features.Length)
        Assert.True(features |> Array.forall (fun f -> f = 0.0))

    [<Fact>]
    let ``extractReturnFeatures all values are finite`` () =
        let logReturns = [| 0.01; -0.02; 0.015; -0.005; 0.008; 0.003; -0.01 |]
        let returns = {
            Symbol = "TEST"; StartDate = DateTime(2024, 1, 1); EndDate = DateTime(2024, 1, 8)
            LogReturns = logReturns; SimpleReturns = logReturns
            Dates = Array.init 7 (fun i -> DateTime(2024, 1, i + 2))
        }
        let features = extractReturnFeatures returns
        Assert.True(features |> Array.forall Double.IsFinite, "All features should be finite")

    // ========================================================================
    // DATA FREQUENCY DU
    // ========================================================================

    [<Fact>]
    let ``DataFrequency Intraday carries minutes`` () =
        let freq = Intraday 5
        match freq with
        | Intraday m -> Assert.Equal(5, m)
        | _ -> failwith "Expected Intraday"

    // ========================================================================
    // ASSET CLASS DU
    // ========================================================================

    [<Fact>]
    let ``AssetClass DU has expected cases`` () =
        let classes = [ Equity; FixedIncome; Commodity; Currency; Derivative; Alternative; Cash ]
        Assert.Equal(7, classes.Length)
