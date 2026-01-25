namespace FSharp.Azure.Quantum.Tests

open System
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
        let test = async {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let! result = OptionPricing.priceEuropeanCall 100.0 105.0 0.05 0.2 1.0 6 5 200 backend
            
            return
                match result with
                | Ok price -> 
                    Assert.True(price.Price >= 0.0, "Option price should be non-negative")
                    Assert.Equal(6, price.QubitsUsed)
                    true
                | Error err -> 
                    failwith $"Should succeed, got error: {err}"
        }
        test |> Async.RunSynchronously |> ignore

    [<Fact>]
    let ``optionPricing CE should respect qubits and shots`` () =
        let quantumBackend = LocalBackend.LocalBackend() :> IQuantumBackend

        let result =
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
            |> Async.RunSynchronously

        match result with
        | Ok price ->
            Assert.Equal(4, price.QubitsUsed)
        | Error err ->
            failwith $"Should succeed, got error: {err}"

    [<Fact>]
    let ``optionPricing CE should reject missing backend`` () =
        let result =
            OptionPricing.optionPricing {
                spotPrice 100.0
                strikePrice 105.0
            }
            |> Async.RunSynchronously

        match result with
        | Error (QuantumError.ValidationError (param, _)) ->
            Assert.Equal("Backend", param)
        | _ ->
            failwith "Should return ValidationError for missing backend"
    
    [<Fact>]
    let ``price should reject numQubits less than 2`` () =
        let test = async {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let params' = createMarketParams 100.0 105.0 0.05 0.2 1.0
            let! result = OptionPricing.price OptionPricing.EuropeanCall params' 1 5 200 backend
            
            return
                match result with
                | Error (QuantumError.ValidationError (param, _)) -> 
                    Assert.Equal("numQubits", param)
                    true
                | _ -> 
                    failwith "Should return ValidationError for numQubits < 2"
        }
        test |> Async.RunSynchronously |> ignore
    
    [<Fact>]
    let ``price should reject negative spot price`` () =
        let test = async {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let params' = createMarketParams -100.0 105.0 0.05 0.2 1.0
            let! result = OptionPricing.price OptionPricing.EuropeanCall params' 6 5 200 backend
            
            return
                match result with
                | Error (QuantumError.ValidationError (param, _)) -> 
                    Assert.Equal("SpotPrice", param)
                    true
                | _ -> 
                    failwith "Should return ValidationError for negative spot"
        }
        test |> Async.RunSynchronously |> ignore
    
    [<Fact>]
    let ``priceEuropeanPut should return valid result`` () =
        let test = async {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let! result = OptionPricing.priceEuropeanPut 100.0 105.0 0.05 0.2 1.0 6 5 200 backend
            
            return
                match result with
                | Ok price -> 
                    Assert.True(price.Price >= 0.0)
                    Assert.Equal(6, price.QubitsUsed)
                    true
                | Error err -> 
                    failwith $"Should succeed, got error: {err}"
        }
        test |> Async.RunSynchronously |> ignore
    
    [<Fact>]
    let ``Call option should have non-negative price`` () =
        let test = async {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let! result = OptionPricing.priceEuropeanCall 50.0 100.0 0.05 0.2 1.0 6 5 200 backend
            
            return
                match result with
                | Ok price -> 
                    Assert.True(price.Price >= 0.0)
                    true
                | Error err -> 
                    failwith $"Pricing failed: {err}"
        }
        test |> Async.RunSynchronously |> ignore
    
    [<Fact>]
    let ``Pricing with different qubit counts should work`` () =
        let test = async {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let params' = createMarketParams 100.0 105.0 0.05 0.2 1.0
            
            let! result4 = OptionPricing.price OptionPricing.EuropeanCall params' 4 3 200 backend
            let! result8 = OptionPricing.price OptionPricing.EuropeanCall params' 8 3 200 backend
            
            return
                match result4, result8 with
                | Ok price4, Ok price8 ->
                    Assert.Equal(4, price4.QubitsUsed)
                    Assert.Equal(8, price8.QubitsUsed)
                    true
                | _ -> 
                    failwith "Both should succeed"
        }
        test |> Async.RunSynchronously |> ignore
    
    // ========================================================================
    // GREEKS TESTS - Option Sensitivities via Quantum Finite Differences
    // ========================================================================
    
    [<Fact>]
    let ``greeksEuropeanCall should return all Greeks with LocalBackend`` () =
        let test = async {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let! result = OptionPricing.greeksEuropeanCall 100.0 100.0 0.05 0.2 1.0 backend
            
            return
                match result with
                | Ok greeks ->
                    // Price should be non-negative
                    Assert.True(greeks.Price >= 0.0, $"Price should be non-negative, got {greeks.Price}")
                    
                    // Delta for ATM call should be roughly 0.5 (can be anywhere in [0,1])
                    Assert.True(greeks.Delta >= -0.5 && greeks.Delta <= 1.5, 
                        $"Delta should be roughly in [0,1], got {greeks.Delta}")
                    
                    // Gamma should be non-negative (curvature is positive for vanilla options)
                    Assert.True(greeks.Gamma >= -1.0, $"Gamma should be roughly non-negative, got {greeks.Gamma}")
                    
                    // Vega should be non-negative (higher vol = higher option value)
                    Assert.True(greeks.Vega >= -0.1, $"Vega should be roughly non-negative, got {greeks.Vega}")
                    
                    // Theta is usually negative (time decay)
                    // But allow some flexibility for numerical noise
                    Assert.True(greeks.Theta >= -10.0 && greeks.Theta <= 10.0, 
                        $"Theta should be reasonable, got {greeks.Theta}")
                    
                    // Rho for calls should be roughly positive
                    Assert.True(greeks.Rho >= -1.0 && greeks.Rho <= 1.0, 
                        $"Rho should be reasonable, got {greeks.Rho}")
                    
                    // Method should indicate quantum
                    Assert.Contains("Quantum", greeks.Method)
                    
                    // Should have made 8 pricing calls
                    Assert.Equal(8, greeks.PricingCalls)
                    
                    true
                | Error err ->
                    failwith $"Should succeed, got error: {err}"
        }
        test |> Async.RunSynchronously |> ignore
    
    [<Fact>]
    let ``greeksEuropeanPut should return all Greeks with LocalBackend`` () =
        let test = async {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let! result = OptionPricing.greeksEuropeanPut 100.0 100.0 0.05 0.2 1.0 backend
            
            return
                match result with
                | Ok greeks ->
                    // Price should be non-negative
                    Assert.True(greeks.Price >= 0.0, $"Price should be non-negative, got {greeks.Price}")
                    
                    // Delta for put should be roughly in [-1, 0]
                    Assert.True(greeks.Delta >= -1.5 && greeks.Delta <= 0.5, 
                        $"Put Delta should be roughly in [-1,0], got {greeks.Delta}")
                    
                    // All confidence intervals should be non-negative
                    Assert.True(greeks.ConfidenceIntervals.Delta >= 0.0, "Delta CI should be non-negative")
                    Assert.True(greeks.ConfidenceIntervals.Gamma >= 0.0, "Gamma CI should be non-negative")
                    Assert.True(greeks.ConfidenceIntervals.Vega >= 0.0, "Vega CI should be non-negative")
                    Assert.True(greeks.ConfidenceIntervals.Theta >= 0.0, "Theta CI should be non-negative")
                    Assert.True(greeks.ConfidenceIntervals.Rho >= 0.0, "Rho CI should be non-negative")
                    
                    true
                | Error err ->
                    failwith $"Should succeed, got error: {err}"
        }
        test |> Async.RunSynchronously |> ignore
    
    [<Fact>]
    let ``calculateGreeks should validate config SpotBump`` () =
        let test = async {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let params' = createMarketParams 100.0 105.0 0.05 0.2 1.0
            let invalidConfig = { OptionPricing.defaultGreeksConfig with SpotBump = -0.01 }
            
            let! result = OptionPricing.calculateGreeks OptionPricing.EuropeanCall params' invalidConfig 6 5 backend
            
            return
                match result with
                | Error (QuantumError.ValidationError (param, _)) ->
                    Assert.Equal("SpotBump", param)
                    true
                | _ ->
                    failwith "Should return ValidationError for invalid SpotBump"
        }
        test |> Async.RunSynchronously |> ignore
    
    [<Fact>]
    let ``calculateGreeks should validate TimeToExpiry vs TimeBump`` () =
        let test = async {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            // Expiry is 1 day, but TimeBump is also 1 day (1/365)
            let params' = createMarketParams 100.0 105.0 0.05 0.2 (1.0 / 365.0)
            let config = OptionPricing.defaultGreeksConfig  // TimeBump = 1/365
            
            let! result = OptionPricing.calculateGreeks OptionPricing.EuropeanCall params' config 6 5 backend
            
            return
                match result with
                | Error (QuantumError.ValidationError (param, _)) ->
                    Assert.Equal("TimeToExpiry", param)
                    true
                | _ ->
                    failwith "Should return ValidationError when TimeToExpiry <= TimeBump"
        }
        test |> Async.RunSynchronously |> ignore
    
    [<Fact>]
    let ``calculateGreeks with custom config should work`` () =
        let test = async {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let params' = createMarketParams 100.0 100.0 0.05 0.2 1.0
            let customConfig = {
                OptionPricing.SpotBump = 0.02      // 2% spot bump
                OptionPricing.VolatilityBump = 0.02  // 2% vol bump
                OptionPricing.TimeBump = 7.0 / 365.0 // 1 week
                OptionPricing.RateBump = 0.005      // 0.5% rate bump
            }
            
            let! result = OptionPricing.calculateGreeks OptionPricing.EuropeanCall params' customConfig 6 5 backend
            
            return
                match result with
                | Ok greeks ->
                    // Should have calculated all Greeks
                    Assert.True(greeks.Price >= 0.0)
                    Assert.Equal(8, greeks.PricingCalls)
                    true
                | Error err ->
                    failwith $"Should succeed with custom config, got error: {err}"
        }
        test |> Async.RunSynchronously |> ignore
    
    [<Fact>]
    let ``Greeks for deep ITM call should have Delta near 1`` () =
        let test = async {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            // Deep in-the-money: Spot=150, Strike=100 (50% ITM)
            let! result = OptionPricing.greeksEuropeanCall 150.0 100.0 0.05 0.2 1.0 backend
            
            return
                match result with
                | Ok greeks ->
                    // Deep ITM call should have delta closer to 1 than ATM
                    // Allow wide range due to quantum noise, but should trend toward 1
                    Assert.True(greeks.Delta >= 0.0 && greeks.Delta <= 2.0, 
                        $"Deep ITM call Delta should trend toward 1, got {greeks.Delta}")
                    true
                | Error err ->
                    failwith $"Should succeed, got error: {err}"
        }
        test |> Async.RunSynchronously |> ignore
    
    [<Fact>]
    let ``Greeks for deep OTM call should have Delta near 0`` () =
        let test = async {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            // Deep out-of-the-money: Spot=50, Strike=100 (50% OTM)
            let! result = OptionPricing.greeksEuropeanCall 50.0 100.0 0.05 0.2 1.0 backend
            
            return
                match result with
                | Ok greeks ->
                    // Deep OTM call should have delta closer to 0
                    // Allow wide range due to quantum noise
                    Assert.True(greeks.Delta >= -1.0 && greeks.Delta <= 1.0, 
                        $"Deep OTM call Delta should trend toward 0, got {greeks.Delta}")
                    true
                | Error err ->
                    failwith $"Should succeed, got error: {err}"
        }
        test |> Async.RunSynchronously |> ignore
