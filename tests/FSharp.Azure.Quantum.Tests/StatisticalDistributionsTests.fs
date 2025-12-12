namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.Algorithms.StatisticalDistributions

module StatisticalDistributionsTests =
    
    // ========================================================================
    // Error Function Tests
    // ========================================================================
    
    [<Fact>]
    let ``erf(0) should be 0`` () =
        let result = erf 0.0
        Assert.Equal(0.0, result, 8) // 8 decimal places (numerical approximation)
    
    [<Fact>]
    let ``erf(-x) should equal -erf(x)`` () =
        let x = 1.5
        let result = erf (-x)
        Assert.Equal(-(erf x), result, 10)
    
    [<Fact>]
    let ``erf approaches 1 for large positive x`` () =
        let result = erf 5.0
        Assert.True(result > 0.9999, "erf(5) should be very close to 1")
    
    [<Fact>]
    let ``erfc(x) + erf(x) should equal 1`` () =
        let x = 2.0
        let result = erfc x + erf x
        Assert.Equal(1.0, result, 10)
    
    // ========================================================================
    // Normal Distribution PDF Tests
    // ========================================================================
    
    [<Fact>]
    let ``normalPDF(0) should be 1/sqrt(2π)`` () =
        let expected = 1.0 / sqrt(2.0 * Math.PI)
        let result = normalPDF 0.0
        Assert.Equal(expected, result, 10)
    
    [<Fact>]
    let ``normalPDF should be symmetric around 0`` () =
        let x = 1.5
        Assert.Equal(normalPDF x, normalPDF (-x), 10)
    
    [<Fact>]
    let ``normalPDF should integrate to approximately 1`` () =
        // Approximate integration using trapezoidal rule
        let dx = 0.1
        let xs = [| -5.0 .. dx .. 5.0 |]
        let integral = 
            xs 
            |> Array.map normalPDF
            |> Array.sum
            |> (*) dx
        
        Assert.InRange(integral, 0.99, 1.01) // Should be close to 1
    
    // ========================================================================
    // Normal Distribution CDF Tests
    // ========================================================================
    
    [<Fact>]
    let ``normalCDF(0) should be 0.5`` () =
        let result = normalCDF 0.0
        Assert.Equal(0.5, result, 6)
    
    [<Fact>]
    let ``normalCDF should be monotonically increasing`` () =
        let x1 = -1.0
        let x2 = 1.0
        Assert.True(normalCDF x1 < normalCDF x2)
    
    [<Fact>]
    let ``normalCDF approaches 1 for large x`` () =
        let result = normalCDF 5.0
        Assert.True(result > 0.9999)
    
    [<Fact>]
    let ``normalCDF approaches 0 for large negative x`` () =
        let result = normalCDF (-5.0)
        Assert.True(result < 0.0001)
    
    // ========================================================================
    // Normal Quantile (Inverse CDF) Tests
    // ========================================================================
    
    [<Fact>]
    let ``normalQuantile(0.5) should be 0`` () =
        let result = normalQuantile 0.5
        Assert.Equal(0.0, result, 6)
    
    [<Fact>]
    let ``normalQuantile should invert normalCDF`` () =
        let x = 1.5
        let p = normalCDF x
        let recovered = normalQuantile p
        Assert.Equal(x, recovered, 6)
    
    [<Fact>]
    let ``normalQuantile(p) should equal -normalQuantile(1-p)`` () =
        let p = 0.25
        let q1 = normalQuantile p
        let q2 = normalQuantile (1.0 - p)
        Assert.Equal(-q1, q2, 6)
    
    [<Fact>]
    let ``normalQuantile should fail for p <= 0`` () =
        Assert.Throws<Exception>(fun () -> normalQuantile 0.0 |> ignore)
    
    [<Fact>]
    let ``normalQuantile should fail for p >= 1`` () =
        Assert.Throws<Exception>(fun () -> normalQuantile 1.0 |> ignore)
    
    // ========================================================================
    // Log-Normal Distribution Tests
    // ========================================================================
    
    [<Fact>]
    let ``logNormalPDF should be 0 for x <= 0`` () =
        Assert.Equal(0.0, logNormalPDF 0.0 1.0 0.0)
        Assert.Equal(0.0, logNormalPDF 0.0 1.0 (-1.0))
    
    [<Fact>]
    let ``logNormalCDF should be 0 for x <= 0`` () =
        Assert.Equal(0.0, logNormalCDF 0.0 1.0 0.0)
        Assert.Equal(0.0, logNormalCDF 0.0 1.0 (-1.0))
    
    [<Fact>]
    let ``logNormalQuantile should invert logNormalCDF`` () =
        let mu = 0.0
        let sigma = 0.5
        let x = 2.0
        let p = logNormalCDF mu sigma x
        let recovered = logNormalQuantile mu sigma p
        Assert.Equal(x, recovered, 4)
    
    [<Fact>]
    let ``logNormalQuantile(0.5) should equal exp(μ) when σ is small`` () =
        let mu = 1.0
        let sigma = 0.01 // Very small sigma
        let median = logNormalQuantile mu sigma 0.5
        Assert.Equal(exp mu, median, 2)
    
    // ========================================================================
    // Discretization Tests
    // ========================================================================
    
    [<Fact>]
    let ``discretizeNormal should return correct number of bins`` () =
        let n = 8
        let bins = discretizeNormal n
        Assert.Equal(n, bins.Length)
    
    [<Fact>]
    let ``discretizeNormal probabilities should sum to 1`` () =
        let bins = discretizeNormal 16
        let totalProb = bins |> Array.sumBy snd
        Assert.Equal(1.0, totalProb, 10)
    
    [<Fact>]
    let ``discretizeNormal z-scores should be sorted`` () =
        let bins = discretizeNormal 32
        let zScores = bins |> Array.map fst
        
        // Check monotonically increasing
        for i in 0 .. zScores.Length - 2 do
            Assert.True(zScores[i] < zScores[i+1])
    
    [<Fact>]
    let ``discretizeLogNormal should return correct number of bins`` () =
        let n = 8
        let bins = discretizeLogNormal 0.0 1.0 n
        Assert.Equal(n, bins.Length)
    
    [<Fact>]
    let ``discretizeLogNormal probabilities should sum to 1`` () =
        let bins = discretizeLogNormal 0.0 0.5 16
        let totalProb = bins |> Array.sumBy snd
        Assert.Equal(1.0, totalProb, 10)
    
    [<Fact>]
    let ``discretizeLogNormal prices should be positive`` () =
        let bins = discretizeLogNormal 0.0 1.0 32
        bins |> Array.iter (fun (price, _) -> 
            Assert.True(price > 0.0, $"Price {price} should be positive"))
    
    [<Fact>]
    let ``discretizeLogNormal prices should be sorted`` () =
        let bins = discretizeLogNormal 0.0 1.0 32
        let prices = bins |> Array.map fst
        
        // Check monotonically increasing
        for i in 0 .. prices.Length - 2 do
            Assert.True(prices[i] < prices[i+1])
    
    // ========================================================================
    // Numerical Accuracy Tests (Known Values)
    // ========================================================================
    
    [<Fact>]
    let ``normalCDF at standard deviations should match known values`` () =
        // P(Z <= 1) ≈ 0.8413
        Assert.Equal(0.8413, normalCDF 1.0, 3)
        
        // P(Z <= 2) ≈ 0.9772
        Assert.Equal(0.9772, normalCDF 2.0, 3)
        
        // P(Z <= -1) ≈ 0.1587
        Assert.Equal(0.1587, normalCDF (-1.0), 3)
    
    [<Fact>]
    let ``normalQuantile at common percentiles should match known values`` () =
        // 25th percentile ≈ -0.6745
        Assert.Equal(-0.6745, normalQuantile 0.25, 3)
        
        // 75th percentile ≈ 0.6745
        Assert.Equal(0.6745, normalQuantile 0.75, 3)
        
        // 95th percentile ≈ 1.645
        Assert.Equal(1.645, normalQuantile 0.95, 2)
        
        // 99th percentile ≈ 2.326
        Assert.Equal(2.326, normalQuantile 0.99, 2)
    
    // ========================================================================
    // Edge Cases and Error Handling
    // ========================================================================
    
    [<Fact>]
    let ``discretizeNormal should fail for n < 2`` () =
        Assert.Throws<Exception>(fun () -> discretizeNormal 1 |> ignore)
    
    [<Fact>]
    let ``discretizeLogNormal should fail for n < 2`` () =
        Assert.Throws<Exception>(fun () -> discretizeLogNormal 0.0 1.0 1 |> ignore)
    
    [<Fact>]
    let ``normalPDFGeneral should handle different parameters`` () =
        let mean = 5.0
        let stdDev = 2.0
        let pdf = normalPDFGeneral mean stdDev mean
        
        // PDF at mean should be maximum
        let pdfLeft = normalPDFGeneral mean stdDev (mean - 1.0)
        let pdfRight = normalPDFGeneral mean stdDev (mean + 1.0)
        
        Assert.True(pdf > pdfLeft)
        Assert.True(pdf > pdfRight)
    
    [<Fact>]
    let ``normalCDFGeneral should match standard CDF when μ=0, σ=1`` () =
        let x = 1.5
        let standard = normalCDF x
        let general = normalCDFGeneral 0.0 1.0 x
        
        Assert.Equal(standard, general, 10)
    
    [<Fact>]
    let ``normalQuantileGeneral should invert normalCDFGeneral`` () =
        let mean = 100.0
        let stdDev = 15.0
        let x = 115.0
        
        let p = normalCDFGeneral mean stdDev x
        let recovered = normalQuantileGeneral mean stdDev p
        
        Assert.Equal(x, recovered, 5) // 5 decimal places due to double precision limits
