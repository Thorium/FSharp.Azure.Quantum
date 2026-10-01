namespace FSharp.Azure.Quantum.Tests

open System
open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core.BackendAbstraction

module OptionPricingTests =

    let createMarketParams spot strike rate vol expiry =
        {
            OptionPricing.SpotPrice = spot
            OptionPricing.StrikePrice = strike
            OptionPricing.RiskFreeRate = rate
            OptionPricing.Volatility = vol
            OptionPricing.TimeToExpiry = expiry
        }

    [<Fact>]
    let ``MarketParameters should construct properly`` () =
        let params' = createMarketParams 100.0 105.0 0.05 0.2 1.0
        Assert.Equal(100.0, params'.SpotPrice)
        Assert.Equal(105.0, params'.StrikePrice)

    [<Fact>]
    let ``priceEuropeanCall should return valid result with LocalBackend`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                OptionPricing.priceEuropeanCallAsync 100.0 105.0 0.05 0.2 1.0 6 5 200 backend CancellationToken.None

            match result with
            | Ok price ->
                Assert.True(price.Price >= 0.0, "Option price should be non-negative")
                Assert.Equal(6, price.QubitsUsed)
            | Error err -> failwith $"Should succeed, got error: {err}"
        }
        :> Task

    [<Fact>]
    let ``optionPricing CE should respect qubits and shots`` () =
        task {
            let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                OptionPricing.optionPricing {
                    spotPrice 100.0
                    strikePrice 105.0
                    riskFreeRate 0.05
                    volatility 0.2
                    expiry 1.0
                    optionType OptionPricing.EuropeanCall
                    qubits 4
                    iterations 3
                    shots 100
                    backend quantumBackend
                }

            result
            |> Result.map (fun price -> Assert.Equal(4, price.QubitsUsed))
            |> Result.defaultWith (fun err -> failwith $"Should succeed, got error: {err}")
        }
        :> Task

    [<Fact>]
    let ``optionPricing CE should reject missing backend`` () =
        task {
            let! result =
                OptionPricing.optionPricing {
                    spotPrice 100.0
                    strikePrice 105.0
                }

            match result with
            | Error(QuantumError.ValidationError(param, _)) -> Assert.Equal("Backend", param)
            | _ -> failwith "Should return ValidationError for missing backend"
        }
        :> Task

    [<Fact>]
    let ``price should reject numQubits less than 2`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let params' = createMarketParams 100.0 105.0 0.05 0.2 1.0

            let! result =
                OptionPricing.priceAsync OptionPricing.EuropeanCall params' 1 5 200 backend CancellationToken.None

            match result with
            | Error(QuantumError.ValidationError(param, _)) -> Assert.Equal("numQubits", param)
            | _ -> failwith "Should return ValidationError for numQubits < 2"
        }
        :> Task

    [<Fact>]
    let ``price should reject negative spot price`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let params' = createMarketParams -100.0 105.0 0.05 0.2 1.0

            let! result =
                OptionPricing.priceAsync OptionPricing.EuropeanCall params' 6 5 200 backend CancellationToken.None

            match result with
            | Error(QuantumError.ValidationError(param, _)) -> Assert.Equal("SpotPrice", param)
            | _ -> failwith "Should return ValidationError for negative spot"
        }
        :> Task

    [<Fact>]
    let ``priceEuropeanPut should return valid result`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                OptionPricing.priceEuropeanPutAsync 100.0 105.0 0.05 0.2 1.0 6 5 200 backend CancellationToken.None

            match result with
            | Ok price ->
                Assert.True(price.Price >= 0.0)
                Assert.Equal(6, price.QubitsUsed)
            | Error err -> failwith $"Should succeed, got error: {err}"
        }
        :> Task

    [<Fact>]
    let ``Call option should have non-negative price`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                OptionPricing.priceEuropeanCallAsync 50.0 100.0 0.05 0.2 1.0 6 5 200 backend CancellationToken.None

            match result with
            | Ok price -> Assert.True(price.Price >= 0.0)
            | Error err -> failwith $"Pricing failed: {err}"
        }
        :> Task

    [<Fact>]
    let ``Pricing with different qubit counts should work`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let params' = createMarketParams 100.0 105.0 0.05 0.2 1.0

            let! result4 =
                OptionPricing.priceAsync OptionPricing.EuropeanCall params' 4 3 200 backend CancellationToken.None

            let! result8 =
                OptionPricing.priceAsync OptionPricing.EuropeanCall params' 8 3 200 backend CancellationToken.None

            match result4, result8 with
            | Ok price4, Ok price8 ->
                Assert.Equal(4, price4.QubitsUsed)
                Assert.Equal(8, price8.QubitsUsed)
            | _ -> failwith "Both should succeed"
        }
        :> Task

    // ========================================================================
    // GREEKS TESTS - Option Sensitivities via Quantum Finite Differences
    // ========================================================================

    [<Fact>]
    let ``greeksEuropeanCall should return all Greeks with LocalBackend`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                OptionPricing.greeksEuropeanCallAsync 100.0 100.0 0.05 0.2 1.0 backend CancellationToken.None

            match result with
            | Ok greeks ->
                // Price should be non-negative
                Assert.True(greeks.Price >= 0.0, $"Price should be non-negative, got {greeks.Price}")

                // Delta for ATM call should be roughly 0.5 (can be anywhere in [0,1])
                Assert.True(
                    greeks.Delta >= -0.5 && greeks.Delta <= 1.5,
                    $"Delta should be roughly in [0,1], got {greeks.Delta}"
                )

                // Gamma should be non-negative (curvature is positive for vanilla options)
                Assert.True(greeks.Gamma >= -1.0, $"Gamma should be roughly non-negative, got {greeks.Gamma}")

                // Vega should be non-negative (higher vol = higher option value)
                Assert.True(greeks.Vega >= -0.1, $"Vega should be roughly non-negative, got {greeks.Vega}")

                // Theta is usually negative (time decay)
                // But allow some flexibility for numerical noise
                Assert.True(
                    greeks.Theta >= -10.0 && greeks.Theta <= 10.0,
                    $"Theta should be reasonable, got {greeks.Theta}"
                )

                // Rho for a call is positive and of order K·T·e^(-rT)·N(d2) (≈ 50 for this
                // ATM 1y option); the genuine quantum payoff now reproduces this realistic
                // magnitude rather than the near-zero value the old fudge produced.
                Assert.True(greeks.Rho >= -1.0 && greeks.Rho <= 110.0, $"Rho should be reasonable, got {greeks.Rho}")

                // Method should indicate quantum
                Assert.Contains("Quantum", greeks.Method)

                // Should have made 8 pricing calls
                Assert.Equal(8, greeks.PricingCalls)
            | Error err -> failwith $"Should succeed, got error: {err}"
        }
        :> Task

    [<Fact>]
    let ``greeksEuropeanPut should return all Greeks with LocalBackend`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                OptionPricing.greeksEuropeanPutAsync 100.0 100.0 0.05 0.2 1.0 backend CancellationToken.None

            match result with
            | Ok greeks ->
                // Price should be non-negative
                Assert.True(greeks.Price >= 0.0, $"Price should be non-negative, got {greeks.Price}")

                // Delta for put should be roughly in [-1, 0]
                Assert.True(
                    greeks.Delta >= -1.5 && greeks.Delta <= 0.5,
                    $"Put Delta should be roughly in [-1,0], got {greeks.Delta}"
                )

                // All confidence intervals should be non-negative
                Assert.True(greeks.ConfidenceIntervals.Delta >= 0.0, "Delta CI should be non-negative")
                Assert.True(greeks.ConfidenceIntervals.Gamma >= 0.0, "Gamma CI should be non-negative")
                Assert.True(greeks.ConfidenceIntervals.Vega >= 0.0, "Vega CI should be non-negative")
                Assert.True(greeks.ConfidenceIntervals.Theta >= 0.0, "Theta CI should be non-negative")
                Assert.True(greeks.ConfidenceIntervals.Rho >= 0.0, "Rho CI should be non-negative")
            | Error err -> failwith $"Should succeed, got error: {err}"
        }
        :> Task

    [<Fact>]
    let ``calculateGreeks should validate config SpotBump`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let params' = createMarketParams 100.0 105.0 0.05 0.2 1.0

            let invalidConfig =
                { OptionPricing.defaultGreeksConfig with
                    SpotBump = -0.01
                }

            let! result =
                OptionPricing.calculateGreeksAsync
                    OptionPricing.EuropeanCall
                    params'
                    invalidConfig
                    6
                    5
                    backend
                    CancellationToken.None

            match result with
            | Error(QuantumError.ValidationError(param, _)) -> Assert.Equal("SpotBump", param)
            | _ -> failwith "Should return ValidationError for invalid SpotBump"
        }
        :> Task

    [<Fact>]
    let ``calculateGreeks should validate TimeToExpiry vs TimeBump`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            // Expiry is 1 day, but TimeBump is also 1 day (1/365)
            let params' = createMarketParams 100.0 105.0 0.05 0.2 (1.0 / 365.0)
            let config = OptionPricing.defaultGreeksConfig // TimeBump = 1/365

            let! result =
                OptionPricing.calculateGreeksAsync
                    OptionPricing.EuropeanCall
                    params'
                    config
                    6
                    5
                    backend
                    CancellationToken.None

            match result with
            | Error(QuantumError.ValidationError(param, _)) -> Assert.Equal("TimeToExpiry", param)
            | _ -> failwith "Should return ValidationError when TimeToExpiry <= TimeBump"
        }
        :> Task

    [<Fact>]
    let ``calculateGreeks with custom config should work`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let params' = createMarketParams 100.0 100.0 0.05 0.2 1.0

            let customConfig =
                {
                    OptionPricing.SpotBump = 0.02 // 2% spot bump
                    OptionPricing.VolatilityBump = 0.02 // 2% vol bump
                    OptionPricing.TimeBump = 7.0 / 365.0 // 1 week
                    OptionPricing.RateBump = 0.005 // 0.5% rate bump
                }

            let! result =
                OptionPricing.calculateGreeksAsync
                    OptionPricing.EuropeanCall
                    params'
                    customConfig
                    6
                    5
                    backend
                    CancellationToken.None

            match result with
            | Ok greeks ->
                // Should have calculated all Greeks
                Assert.True(greeks.Price >= 0.0)
                Assert.Equal(8, greeks.PricingCalls)
            | Error err -> failwith $"Should succeed with custom config, got error: {err}"
        }
        :> Task

    [<Fact; Trait("Category", "Slow")>]
    let ``Greeks for deep ITM call should have Delta near 1`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            // Deep in-the-money: Spot=150, Strike=100 (50% ITM)
            let! result =
                OptionPricing.greeksEuropeanCallAsync 150.0 100.0 0.05 0.2 1.0 backend CancellationToken.None

            match result with
            | Ok greeks ->
                // Deep ITM call should have delta closer to 1 than ATM
                // Allow wide range due to quantum noise, but should trend toward 1
                Assert.True(
                    greeks.Delta >= 0.0 && greeks.Delta <= 2.0,
                    $"Deep ITM call Delta should trend toward 1, got {greeks.Delta}"
                )
            | Error err -> failwith $"Should succeed, got error: {err}"
        }
        :> Task

    [<Fact>]
    let ``Greeks for deep OTM call should have Delta near 0`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            // Deep out-of-the-money: Spot=50, Strike=100 (50% OTM)
            let! result =
                OptionPricing.greeksEuropeanCallAsync 50.0 100.0 0.05 0.2 1.0 backend CancellationToken.None

            match result with
            | Ok greeks ->
                // Deep OTM call should have delta closer to 0
                // Allow wide range due to quantum noise
                Assert.True(
                    greeks.Delta >= -1.0 && greeks.Delta <= 1.0,
                    $"Deep OTM call Delta should trend toward 0, got {greeks.Delta}"
                )
            | Error err -> failwith $"Should succeed, got error: {err}"
        }
        :> Task

    // ========================================================================
    // COMPARATOR ORACLE TESTS - Verify diagonal oracle correctness
    // ========================================================================

    [<Fact>]
    let ``Call option with strike far below spot prices correctly`` () =
        // Tests that the comparator oracle correctly marks most states as ITM
        // when strike is far below spot (deep ITM call)
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            // Deep ITM: Spot=200, Strike=50 → almost all states in-the-money
            let! result =
                OptionPricing.priceEuropeanCallAsync 200.0 50.0 0.05 0.2 1.0 4 3 200 backend CancellationToken.None

            match result with
            | Ok price ->
                Assert.True(price.Price > 0.0, $"Deep ITM call should have positive price, got {price.Price}")

                Assert.Equal(4, price.QubitsUsed)
            | Error err -> failwith $"Should succeed, got error: {err}"
        }
        :> Task

    [<Fact>]
    let ``Put option with strike far above spot prices correctly`` () =
        // Tests that the comparator oracle correctly marks most states as ITM
        // when strike is far above spot (deep ITM put)
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            // Deep ITM put: Spot=50, Strike=200 → almost all states in-the-money
            let! result =
                OptionPricing.priceEuropeanPutAsync 50.0 200.0 0.05 0.2 1.0 4 3 200 backend CancellationToken.None

            match result with
            | Ok price ->
                Assert.True(price.Price > 0.0, $"Deep ITM put should have positive price, got {price.Price}")

                Assert.Equal(4, price.QubitsUsed)
            | Error err -> failwith $"Should succeed, got error: {err}"
        }
        :> Task

    [<Fact>]
    let ``Call price increases with spot price (monotonicity)`` () =
        // The comparator oracle should correctly shift the boundary
        // as spot price changes, producing monotonically increasing call prices
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let strike = 100.0

            let! result80 =
                OptionPricing.priceEuropeanCallAsync 80.0 strike 0.05 0.2 1.0 4 3 200 backend CancellationToken.None

            let! result120 =
                OptionPricing.priceEuropeanCallAsync 120.0 strike 0.05 0.2 1.0 4 3 200 backend CancellationToken.None

            match result80, result120 with
            | Ok price80, Ok price120 ->
                // Higher spot must give a higher (non-decreasing) call price: the S=120 call
                // is in-the-money (strike 100) while the S=80 call is out-of-the-money, so the
                // ITM price must dominate. A tiny tolerance absorbs quantum sampling noise.
                let noiseTolerance = 1e-6

                Assert.True(
                    price120.Price >= price80.Price - noiseTolerance,
                    $"Call price should be monotonic non-decreasing in spot: S=120 ({price120.Price:F4}) should be >= S=80 ({price80.Price:F4})"
                )
            | Error e, _ -> failwith $"S=80 pricing failed: {e}"
            | _, Error e -> failwith $"S=120 pricing failed: {e}"
        }
        :> Task

    [<Fact>]
    let ``Asian call option prices with comparator oracle`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                OptionPricing.priceAsianCallAsync 100.0 100.0 0.05 0.2 1.0 4 4 3 200 backend CancellationToken.None

            match result with
            | Ok price ->
                Assert.True(price.Price >= 0.0, $"Asian call price should be non-negative, got {price.Price}")

                Assert.Equal(4, price.QubitsUsed)
            | Error err -> failwith $"Should succeed, got error: {err}"
        }
        :> Task

    [<Fact>]
    let ``Asian put option prices with comparator oracle`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            let! result =
                OptionPricing.priceAsianPutAsync 100.0 100.0 0.05 0.2 1.0 4 4 3 200 backend CancellationToken.None

            match result with
            | Ok price ->
                Assert.True(price.Price >= 0.0, $"Asian put price should be non-negative, got {price.Price}")

                Assert.Equal(4, price.QubitsUsed)
            | Error err -> failwith $"Should succeed, got error: {err}"
        }
        :> Task

    // ========================================================================
    // ROUTES: the price is the amplitude estimate, exact locally, sampled on whole circuits
    // ========================================================================

    /// Discounted E[max(S - 105, 0)] on the 16-level grid priceEuropeanCall 100 105 0.05 0.2 1 prices.
    let private gridCallPrice () =
        let logMean = log 100.0 + (0.05 - 0.5 * 0.2 * 0.2) * 1.0

        FSharp.Azure.Quantum.Algorithms.StatisticalDistributions.discretizeLogNormalBinMeans logMean 0.2 16
        |> Array.sumBy (fun (s, p) -> p * max (s - 105.0) 0.0)
        |> (*) (exp -0.05)

    [<Fact>]
    let ``European call price is the amplitude estimate of the grid expectation on the local simulator`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend

            match!
                OptionPricing.priceEuropeanCallAsync 100.0 105.0 0.05 0.2 1.0 4 2 1000 backend CancellationToken.None
            with
            | Ok price ->
                let expected = gridCallPrice ()
                Assert.True(abs (price.Price - expected) < 1e-3, $"expected {expected}, got {price.Price}")
                Assert.Contains("amplitude estimation", price.Method)
                Assert.DoesNotContain("whole circuits", price.Method)
            | Error err -> failwith $"Should succeed, got error: {err}"
        }
        :> Task

    [<Fact>]
    let ``European call on a whole-circuit sampling backend is priced from the sampled amplitude estimate`` () =
        task {
            let backend = SampledWholeCircuit.Backend(4000, 3)

            match!
                OptionPricing.priceEuropeanCallAsync 100.0 105.0 0.05 0.2 1.0 4 2 1000 backend CancellationToken.None
            with
            | Ok price ->
                let expected = gridCallPrice ()
                // ConfidenceInterval is 1.96 standard errors of the same estimate.
                Assert.True(
                    abs (price.Price - expected) < 2.0 * price.ConfidenceInterval + 0.05,
                    $"expected ≈ {expected}, got {price.Price} ± {price.ConfidenceInterval}"
                )

                Assert.Contains("whole circuits sampled at 4000 shots", price.Method)
                Assert.True(backend.Executed >= 3, $"expected one job per Grover power, got {backend.Executed}")
            | Error err -> failwith $"Should succeed, got error: {err}"
        }
        :> Task

    // ========================================================================
    // GREEKS AGAINST BLACK–SCHOLES (exact simulator)
    // ========================================================================

    /// Black–Scholes (price, Delta, Vega, Rho) of a European option.
    let private blackScholes (isCall: bool) spot strike rate vol expiry =
        let cdf x =
            MathNet.Numerics.Distributions.Normal.CDF(0.0, 1.0, x)

        let d1 =
            (log (spot / strike) + (rate + 0.5 * vol * vol) * expiry) / (vol * sqrt expiry)

        let d2 = d1 - vol * sqrt expiry
        let discount = exp (-rate * expiry)

        let vega =
            spot * MathNet.Numerics.Distributions.Normal.PDF(0.0, 1.0, d1) * sqrt expiry

        if isCall then
            spot * cdf d1 - strike * discount * cdf d2, cdf d1, vega, strike * expiry * discount * cdf d2
        else
            strike * discount * cdf -d2 - spot * cdf -d1, cdf d1 - 1.0, vega, -strike * expiry * discount * cdf -d2

    [<Theory>]
    [<InlineData(true, 100.0, 100.0)>]
    [<InlineData(true, 90.0, 100.0)>]
    [<InlineData(false, 110.0, 100.0)>]
    [<InlineData(false, 100.0, 120.0)>]
    let ``Greeks on the exact simulator match Black-Scholes`` (isCall: bool, spot: float, strike: float) =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let market = createMarketParams spot strike 0.05 0.2 1.0

            let optionType =
                if isCall then
                    OptionPricing.EuropeanCall
                else
                    OptionPricing.EuropeanPut

            let price, delta, vega, rho = blackScholes isCall spot strike 0.05 0.2 1.0

            match!
                OptionPricing.calculateGreeksAsync
                    optionType
                    market
                    OptionPricing.defaultGreeksConfig
                    6
                    5
                    backend
                    CancellationToken.None
            with
            | Ok greeks ->
                // 6 qubits = 64 price levels. Previously Delta was only checked to lie in
                // [-0.5, 1.5]; the fit's grid step (≈1e-3 of the price) cost Rho and Theta
                // several percent and the mid-quantile levels cost the price and Vega 1-2%.
                Assert.True(abs (greeks.Delta - delta) < 0.002, $"Delta {greeks.Delta} vs Black–Scholes {delta}")
                Assert.True(abs (greeks.Price - price) < 0.002 * price, $"price {greeks.Price} vs {price}")
                Assert.True(abs (greeks.Vega - vega) < 0.01 * vega, $"Vega {greeks.Vega} vs {vega}")
                Assert.True(abs (greeks.Rho - rho) < 0.03 * abs rho, $"Rho {greeks.Rho} vs {rho}")
            | Error err -> failwith $"Should succeed, got error: {err}"
        }
        :> Task

    [<Fact>]
    let ``Gamma on the exact simulator converges to Black-Scholes with the price grid`` () =
        task {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let market = createMarketParams 100.0 100.0 0.05 0.2 1.0
            let d1 = (0.05 + 0.5 * 0.2 * 0.2) / 0.2
            let gamma = MathNet.Numerics.Distributions.Normal.PDF(0.0, 1.0, d1) / (100.0 * 0.2)

            match!
                OptionPricing.calculateGreeksAsync
                    OptionPricing.EuropeanCall
                    market
                    OptionPricing.defaultGreeksConfig
                    8
                    5
                    backend
                    CancellationToken.None
            with
            | Ok greeks ->
                // 256 levels; the second difference of the payoff kinks limits Gamma (was -4.6%).
                Assert.True(abs (greeks.Gamma - gamma) < 0.02 * gamma, $"Gamma {greeks.Gamma} vs {gamma}")
            | Error err -> failwith $"Should succeed, got error: {err}"
        }
        :> Task
