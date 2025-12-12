namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
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
            let! result = OptionPricing.priceEuropeanCall 100.0 105.0 0.05 0.2 1.0 backend
            
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
    let ``price should reject numQubits less than 2`` () =
        let test = async {
            let backend = LocalBackend.LocalBackend() :> IQuantumBackend
            let params' = createMarketParams 100.0 105.0 0.05 0.2 1.0
            let! result = OptionPricing.price OptionPricing.EuropeanCall params' 1 5 backend
            
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
            let! result = OptionPricing.price OptionPricing.EuropeanCall params' 6 5 backend
            
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
            let! result = OptionPricing.priceEuropeanPut 100.0 105.0 0.05 0.2 1.0 backend
            
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
            let! result = OptionPricing.priceEuropeanCall 50.0 100.0 0.05 0.2 1.0 backend
            
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
            
            let! result4 = OptionPricing.price OptionPricing.EuropeanCall params' 4 3 backend
            let! result8 = OptionPricing.price OptionPricing.EuropeanCall params' 8 3 backend
            
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
