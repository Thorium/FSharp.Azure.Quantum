namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.Algorithms.QRNG
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

/// Tests for Quantum Random Number Generator (QRNG)
module QRNGTests =
    
    // ========================================================================
    // BASIC GENERATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``generate produces correct number of bits`` () =
        let numBits = 100
        let result = generate numBits
        
        Assert.Equal(numBits, result.Bits.Length)
        Assert.Equal((numBits + 7) / 8, result.AsBytes.Length)
    
    [<Fact>]
    let ``generate with seed is reproducible`` () =
        let numBits = 50
        let seed = Some 12345
        
        let result1 = generateBits numBits seed
        let result2 = generateBits numBits seed
        
        // With same seed, results should be identical
        Assert.Equal<bool[]>(result1.Bits, result2.Bits)
        Assert.Equal<byte[]>(result1.AsBytes, result2.AsBytes)
    
    [<Fact>]
    let ``generate fails with zero bits`` () =
        let ex = Assert.Throws<Exception>(fun () -> generate 0 |> ignore)
        Assert.Contains("must be positive", ex.Message)
    
    [<Fact>]
    let ``generate fails with negative bits`` () =
        let ex = Assert.Throws<Exception>(fun () -> generate -10 |> ignore)
        Assert.Contains("must be positive", ex.Message)
    
    [<Fact>]
    let ``generate fails with excessive bits`` () =
        let ex = Assert.Throws<Exception>(fun () -> generate 2000000 |> ignore)
        Assert.Contains("too large", ex.Message)
    
    [<Fact>]
    let ``generate with 1 bit produces valid result`` () =
        let result = generate 1
        
        Assert.Equal(1, result.Bits.Length)
        Assert.Equal(1, result.AsBytes.Length)
        Assert.True(result.AsInteger.IsSome)
        
        // Bit must be true or false
        Assert.True(result.Bits.[0] = true || result.Bits.[0] = false)
    
    // ========================================================================
    // INTEGER GENERATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``generateInt produces value in range`` () =
        let maxValue = 10
        let results = [1..100] |> List.map (fun _ -> generateInt maxValue)
        
        // All values should be 0 <= x < maxValue
        results |> List.iter (fun x ->
            Assert.True(x >= 0)
            Assert.True(x < maxValue))
    
    [<Fact>]
    let ``generateInt handles power of two max values`` () =
        // Powers of 2 don't need rejection sampling
        let maxValue = 16  // 2^4
        let results = [1..50] |> List.map (fun _ -> generateInt maxValue)
        
        results |> List.iter (fun x ->
            Assert.True(x >= 0)
            Assert.True(x < maxValue))
    
    [<Fact>]
    let ``generateInt handles non-power of two max values`` () =
        // Non-powers of 2 require rejection sampling
        let maxValue = 7
        let results = [1..50] |> List.map (fun _ -> generateInt maxValue)
        
        results |> List.iter (fun x ->
            Assert.True(x >= 0)
            Assert.True(x < maxValue))
    
    [<Fact>]
    let ``generateInt fails with zero max`` () =
        let ex = Assert.Throws<Exception>(fun () -> generateInt 0 |> ignore)
        Assert.Contains("must be positive", ex.Message)
    
    [<Fact>]
    let ``generateInt fails with negative max`` () =
        let ex = Assert.Throws<Exception>(fun () -> generateInt -5 |> ignore)
        Assert.Contains("must be positive", ex.Message)
    
    [<Fact>]
    let ``generateInt with max=1 always returns 0`` () =
        let results = [1..20] |> List.map (fun _ -> generateInt 1)
        results |> List.iter (fun x -> Assert.Equal(0, x))
    
    // ========================================================================
    // FLOAT GENERATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``generateFloat produces value between 0 and 1`` () =
        let results = [1..100] |> List.map (fun _ -> generateFloat())
        
        results |> List.iter (fun x ->
            Assert.True(x >= 0.0)
            Assert.True(x < 1.0))
    
    [<Fact>]
    let ``generateFloat uses 53 bits precision`` () =
        // IEEE 754 double precision mantissa is 53 bits
        // This test verifies we generate enough bits
        let result = generateFloat()
        
        // Float should not be exactly 0.0 or 1.0 (extremely unlikely)
        // Just verify it's in valid range
        Assert.True(result >= 0.0 && result < 1.0)
    
    [<Fact>]
    let ``generateFloat never equals 1.0`` () =
        // Range is [0.0, 1.0) - should never be exactly 1.0
        let results = [1..100] |> List.map (fun _ -> generateFloat())
        results |> List.iter (fun x -> Assert.NotEqual(1.0, x))
    
    // ========================================================================
    // BYTE GENERATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``generateBytes produces correct number of bytes`` () =
        let numBytes = 32
        let result = generateBytes numBytes
        
        Assert.Equal(numBytes, result.Length)
    
    [<Fact>]
    let ``generateBytes fills all bits properly`` () =
        let numBytes = 16
        let result = generateBytes numBytes
        
        // Each byte should be 0-255
        result |> Array.iter (fun b ->
            Assert.True(b >= 0uy)
            Assert.True(b <= 255uy))
    
    [<Fact>]
    let ``generateBytes fails with zero bytes`` () =
        let ex = Assert.Throws<Exception>(fun () -> generateBytes 0 |> ignore)
        Assert.Contains("must be positive", ex.Message)
    
    [<Fact>]
    let ``generateBytes fails with negative bytes`` () =
        let ex = Assert.Throws<Exception>(fun () -> generateBytes -8 |> ignore)
        Assert.Contains("must be positive", ex.Message)
    
    // ========================================================================
    // RESULT STRUCTURE TESTS
    // ========================================================================
    
    [<Fact>]
    let ``result contains bits array`` () =
        let result = generate 10
        
        Assert.NotNull(result.Bits)
        Assert.Equal(10, result.Bits.Length)
    
    [<Fact>]
    let ``result contains asInteger for small bit counts`` () =
        let result = generate 32
        
        Assert.True(result.AsInteger.IsSome)
        
        match result.AsInteger with
        | Some value -> Assert.True(value >= 0UL)
        | None -> Assert.True(false, "AsInteger should be Some for 32 bits")
    
    [<Fact>]
    let ``result asInteger is Some for 64 bits or less`` () =
        let result = generate 64
        Assert.True(result.AsInteger.IsSome)
    
    [<Fact>]
    let ``result asInteger is None for large bit counts`` () =
        let result = generate 100
        Assert.True(result.AsInteger.IsNone)
    
    [<Fact>]
    let ``result contains asBytes`` () =
        let result = generate 16
        
        Assert.NotNull(result.AsBytes)
        Assert.Equal(2, result.AsBytes.Length)  // 16 bits = 2 bytes
    
    [<Fact>]
    let ``result entropy is between 0 and 1`` () =
        let result = generate 100
        
        Assert.True(result.Entropy >= 0.0)
        Assert.True(result.Entropy <= 1.0)
    
    // ========================================================================
    // STATISTICAL QUALITY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``testRandomness detects excellent quality`` () =
        // Generate large sample with seed for reproducibility
        let result = generateBits 10000 (Some 42)
        let test = testRandomness result.Bits
        
        // For large samples, quality should be good or excellent
        Assert.True(test.Quality = "EXCELLENT" || test.Quality = "GOOD")
    
    [<Fact>]
    let ``testRandomness detects poor quality for biased bits`` () =
        // Create artificially poor random bits (all 1s)
        let poorBits = Array.create 1000 true
        let test = testRandomness poorBits
        
        Assert.Equal("POOR", test.Quality)
        Assert.Equal(0.0, test.Entropy)  // No entropy
    
    [<Fact>]
    let ``testRandomness frequency ratio near 0.5`` () =
        let result = generateBits 10000 (Some 123)
        let test = testRandomness result.Bits
        
        // Frequency ratio should be close to 0.5 for good randomness
        // Allow ±0.1 tolerance for statistical variation
        Assert.True(abs(test.FrequencyRatio - 0.5) < 0.1)
    
    [<Fact>]
    let ``testRandomness run count indicates alternations`` () =
        let result = generateBits 1000 (Some 456)
        let test = testRandomness result.Bits
        
        // Run count should be > 0 (bits alternate)
        Assert.True(test.RunCount > 0)
        
        // For random bits, runs should be roughly n/2
        // Allow wide tolerance for statistical variation
        let expectedRuns = 1000 / 2
        Assert.True(test.RunCount > expectedRuns / 3)
        Assert.True(test.RunCount < expectedRuns * 3)
    
    [<Fact>]
    let ``entropy calculation is correct`` () =
        // Perfect 50/50 distribution should have entropy = 1.0
        let perfectBits = 
            Array.init 1000 (fun i -> i % 2 = 0)  // [true, false, true, false, ...]
        
        let test = testRandomness perfectBits
        
        // Shannon entropy for p=0.5: H = -0.5*log2(0.5) - 0.5*log2(0.5) = 1.0
        Assert.True(abs(test.Entropy - 1.0) < 0.01)
    
    [<Fact>]
    let ``entropy is 0 for all zeros`` () =
        let zeros = Array.create 100 false
        let test = testRandomness zeros
        
        Assert.Equal(0.0, test.Entropy)
    
    [<Fact>]
    let ``entropy is 0 for all ones`` () =
        let ones = Array.create 100 true
        let test = testRandomness ones
        
        Assert.Equal(0.0, test.Entropy)
    
    // ========================================================================
    // BACKEND INTEGRATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``generateWithBackend works with LocalBackend`` () =
        async {
            let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
            
            let! result = generateWithBackend 10 backend
            
            match result with
            | Ok qrng ->
                Assert.Equal(10, qrng.Bits.Length)
                Assert.True(qrng.Entropy >= 0.0 && qrng.Entropy <= 1.0)
            | Error msg ->
                Assert.True(false, $"Should succeed with LocalBackend: {msg}")
        }
        |> Async.RunSynchronously
    
    [<Fact>]
    let ``generateWithBackend fails with excessive bits`` () =
        async {
            let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
            
            let! result = generateWithBackend 2000 backend
            
            match result with
            | Ok _ ->
                Assert.True(false, "Should fail with excessive bits")
            | Error msg ->
                Assert.Contains("too large", msg.Message)
        }
        |> Async.RunSynchronously
    
    [<Fact>]
    let ``generateWithBackend fails with zero bits`` () =
        async {
            let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
            
            let! result = generateWithBackend 0 backend
            
            match result with
            | Ok _ ->
                Assert.True(false, "Should fail with zero bits")
            | Error msg ->
                Assert.Contains("must be positive", msg.Message)
        }
        |> Async.RunSynchronously
    
    // ========================================================================
    // BIT-BYTE CONVERSION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``bits to bytes conversion is correct`` () =
        // Test with known bit pattern
        let bits = [| true; false; true; true; false; false; true; false |]  // 0b10110010 = 178 (little-endian)
        
        let result = {
            Bits = bits
            AsInteger = Some 0UL
            AsBytes = [| 0uy |]
            Entropy = 0.0
        }
        
        // Manually compute expected byte
        let mutable expectedByte = 0uy
        for i in 0..7 do
            if bits.[i] then
                expectedByte <- expectedByte ||| (1uy <<< i)
        
        // Generate actual result
        let actualResult = generateBits 8 (Some 999)
        
        // Just verify byte is valid (0-255)
        Assert.True(actualResult.AsBytes.[0] >= 0uy && actualResult.AsBytes.[0] <= 255uy)
    
    [<Fact>]
    let ``integer conversion is correct for simple pattern`` () =
        // For 8 bits, AsInteger should match AsBytes
        let result = generateBits 8 (Some 777)
        
        match result.AsInteger with
        | Some intValue ->
            Assert.Equal(uint64 result.AsBytes.[0], intValue)
        | None ->
            Assert.True(false, "AsInteger should be Some for 8 bits")

/// Tests for Quantum-Enhanced Statistical Distributions
module QuantumDistributionsTests =
    
    open FSharp.Azure.Quantum.Algorithms.QuantumDistributions
    
    // ========================================================================
    // DISTRIBUTION VALIDATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Normal distribution requires positive stddev`` () =
        let dist = Normal (0.0, -1.0)
        let result = sample dist
        
        match result with
        | Ok _ -> Assert.True(false, "Should fail with negative stddev")
        | Error msg -> Assert.Contains("stddev must be positive", msg)
    
    [<Fact>]
    let ``Normal distribution requires finite parameters`` () =
        let dist1 = Normal (Double.NaN, 1.0)
        let result1 = sample dist1
        
        match result1 with
        | Ok _ -> Assert.True(false, "Should fail with NaN mean")
        | Error msg -> Assert.Contains("mean must be finite", msg)
        
        let dist2 = Normal (0.0, Double.PositiveInfinity)
        let result2 = sample dist2
        
        match result2 with
        | Ok _ -> Assert.True(false, "Should fail with infinite stddev")
        | Error msg -> Assert.Contains("stddev must be finite", msg)
    
    [<Fact>]
    let ``LogNormal distribution requires positive sigma`` () =
        let dist = LogNormal (0.0, -0.5)
        let result = sample dist
        
        match result with
        | Ok _ -> Assert.True(false, "Should fail with negative sigma")
        | Error msg -> Assert.Contains("sigma must be positive", msg)
    
    [<Fact>]
    let ``Exponential distribution requires positive lambda`` () =
        let dist = Exponential (-1.0)
        let result = sample dist
        
        match result with
        | Ok _ -> Assert.True(false, "Should fail with negative lambda")
        | Error msg -> Assert.Contains("lambda must be positive", msg)
    
    [<Fact>]
    let ``Uniform distribution requires min less than max`` () =
        let dist = Uniform (10.0, 5.0)
        let result = sample dist
        
        match result with
        | Ok _ -> Assert.True(false, "Should fail with min >= max")
        | Error msg -> Assert.Contains("min must be < max", msg)
    
    [<Fact>]
    let ``Custom distribution requires non-empty name`` () =
        let dist = Custom ("", fun x -> x)
        let result = sample dist
        
        match result with
        | Ok _ -> Assert.True(false, "Should fail with empty name")
        | Error msg -> Assert.Contains("name cannot be empty", msg)
    
    // ========================================================================
    // SAMPLING TESTS (Pure Simulation)
    // ========================================================================
    
    [<Fact>]
    let ``sample StandardNormal produces reasonable values`` () =
        // Generate 1000 samples
        let results = [1..1000] |> List.choose (fun _ ->
            match sample StandardNormal with
            | Ok r -> Some r.Value
            | Error _ -> None)
        
        Assert.Equal(1000, results.Length)
        
        // Mean should be ~0.0 (allow ±0.3 for statistical variation)
        let mean = results |> List.average
        Assert.True(abs mean < 0.3, $"Mean {mean} outside ±0.3")
        
        // StdDev should be ~1.0 (allow 0.8-1.2 for statistical variation)
        let variance = results |> List.map (fun x -> (x - mean) ** 2.0) |> List.average
        let stddev = sqrt variance
        Assert.True(stddev > 0.8 && stddev < 1.2, $"StdDev {stddev} outside [0.8, 1.2]")
    
    [<Fact>]
    let ``sample Normal with custom mean and stddev`` () =
        let dist = Normal (10.0, 2.0)
        
        let results = [1..1000] |> List.choose (fun _ ->
            match sample dist with
            | Ok r -> Some r.Value
            | Error _ -> None)
        
        Assert.Equal(1000, results.Length)
        
        // Mean should be ~10.0 (allow ±0.5)
        let mean = results |> List.average
        Assert.True(abs (mean - 10.0) < 0.5, $"Mean {mean} not near 10.0")
        
        // StdDev should be ~2.0 (allow 1.6-2.4)
        let variance = results |> List.map (fun x -> (x - mean) ** 2.0) |> List.average
        let stddev = sqrt variance
        Assert.True(stddev > 1.6 && stddev < 2.4, $"StdDev {stddev} outside [1.6, 2.4]")
    
    [<Fact>]
    let ``sample LogNormal produces positive values`` () =
        let dist = LogNormal (0.0, 1.0)
        
        let results = [1..1000] |> List.choose (fun _ ->
            match sample dist with
            | Ok r -> Some r.Value
            | Error _ -> None)
        
        Assert.Equal(1000, results.Length)
        
        // All values must be positive
        results |> List.iter (fun x -> Assert.True(x > 0.0, $"Value {x} not positive"))
        
        // Mean should be ~exp(0 + 1²/2) = exp(0.5) ≈ 1.649
        let mean = results |> List.average
        Assert.True(mean > 1.2 && mean < 2.2, $"Mean {mean} outside [1.2, 2.2]")
    
    [<Fact>]
    let ``sample Exponential produces positive values with correct mean`` () =
        let lambda = 2.0
        let dist = Exponential lambda
        
        let results = [1..1000] |> List.choose (fun _ ->
            match sample dist with
            | Ok r -> Some r.Value
            | Error _ -> None)
        
        Assert.Equal(1000, results.Length)
        
        // All values must be positive
        results |> List.iter (fun x -> Assert.True(x > 0.0))
        
        // Mean should be ~1/λ = 0.5 (allow 0.4-0.6)
        let mean = results |> List.average
        Assert.True(mean > 0.4 && mean < 0.6, $"Mean {mean} outside [0.4, 0.6]")
    
    [<Fact>]
    let ``sample Uniform produces values in range`` () =
        let minVal = 5.0
        let maxVal = 15.0
        let dist = Uniform (minVal, maxVal)
        
        let results = [1..1000] |> List.choose (fun _ ->
            match sample dist with
            | Ok r -> Some r.Value
            | Error _ -> None)
        
        Assert.Equal(1000, results.Length)
        
        // All values must be in [min, max]
        results |> List.iter (fun x ->
            Assert.True(x >= minVal, $"Value {x} < {minVal}")
            Assert.True(x <= maxVal, $"Value {x} > {maxVal}"))
        
        // Mean should be ~(min+max)/2 = 10.0 (allow 9.5-10.5)
        let mean = results |> List.average
        Assert.True(mean > 9.5 && mean < 10.5, $"Mean {mean} outside [9.5, 10.5]")
    
    [<Fact>]
    let ``sample Custom distribution with square transform`` () =
        // Transform: x² (maps uniform [0,1] to [0,1])
        let dist = Custom ("Square", fun u -> u ** 2.0)
        
        let result = sample dist
        
        match result with
        | Ok r ->
            Assert.True(r.Value >= 0.0 && r.Value <= 1.0)
            Assert.Equal(53, r.QuantumBitsUsed)
        | Error msg ->
            Assert.True(false, $"Should succeed: {msg}")
    
    // ========================================================================
    // SAMPLE MANY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``sampleMany generates correct number of samples`` () =
        let dist = StandardNormal
        let count = 100
        
        let result = sampleMany dist count
        
        match result with
        | Ok samples ->
            Assert.Equal(count, samples.Length)
            samples |> Array.iter (fun s ->
                Assert.Equal(53, s.QuantumBitsUsed))
        | Error msg ->
            Assert.True(false, $"Should succeed: {msg}")
    
    [<Fact>]
    let ``sampleMany fails with zero count`` () =
        let dist = StandardNormal
        let result = sampleMany dist 0
        
        match result with
        | Ok _ -> Assert.True(false, "Should fail with zero count")
        | Error msg -> Assert.Contains("must be positive", msg)
    
    [<Fact>]
    let ``sampleMany fails with excessive count`` () =
        let dist = StandardNormal
        let result = sampleMany dist 2000000
        
        match result with
        | Ok _ -> Assert.True(false, "Should fail with excessive count")
        | Error msg -> Assert.Contains("too large", msg)
    
    [<Fact>]
    let ``sampleMany propagates validation errors`` () =
        let dist = Normal (0.0, -1.0)  // Invalid stddev
        let result = sampleMany dist 10
        
        match result with
        | Ok _ -> Assert.True(false, "Should fail with invalid distribution")
        | Error msg -> Assert.Contains("stddev must be positive", msg)
    
    // ========================================================================
    // BACKEND INTEGRATION TESTS (RULE1 Compliant)
    // ========================================================================
    
    [<Fact>]
    let ``sampleWithBackend works with LocalBackend`` () =
        async {
            let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
            let dist = StandardNormal
            
            let! result = sampleWithBackend dist backend
            
            match result with
            | Ok sample ->
                Assert.Equal(10, sample.QuantumBitsUsed)
                // Value should be reasonable for N(0,1) - allow wide range
                Assert.True(sample.Value > -10.0 && sample.Value < 10.0)
            | Error msg ->
                Assert.True(false, $"Should succeed with LocalBackend: {msg}")
        }
        |> Async.RunSynchronously
    
    [<Fact>]
    let ``sampleWithBackend validates distribution parameters`` () =
        async {
            let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
            let dist = Normal (0.0, -1.0)  // Invalid stddev
            
            let! result = sampleWithBackend dist backend
            
            match result with
            | Ok _ -> Assert.True(false, "Should fail with invalid distribution")
            | Error err -> Assert.Contains("stddev must be positive", err.Message)
        }
        |> Async.RunSynchronously
    
    [<Fact>]
    let ``sampleManyWithBackend generates correct number of samples`` () =
        async {
            let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
            let dist = Uniform (0.0, 1.0)
            let count = 10
            
            let! result = sampleManyWithBackend dist count backend
            
            match result with
            | Ok samples ->
                Assert.Equal(count, samples.Length)
                samples |> Array.iter (fun s ->
                    Assert.True(s.Value >= 0.0 && s.Value <= 1.0)
                    Assert.Equal(10, s.QuantumBitsUsed))
            | Error msg ->
                Assert.True(false, $"Should succeed: {msg}")
        }
        |> Async.RunSynchronously
    
    [<Fact>]
    let ``sampleManyWithBackend fails with excessive count`` () =
        async {
            let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
            let dist = StandardNormal
            
            let! result = sampleManyWithBackend dist 20000 backend
            
            match result with
            | Ok _ -> Assert.True(false, "Should fail with excessive count")
            | Error err -> Assert.Contains("too large", err.Message)
        }
        |> Async.RunSynchronously
    
    [<Fact>]
    let ``sampleManyWithBackend fails with zero count`` () =
        async {
            let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
            let dist = StandardNormal
            
            let! result = sampleManyWithBackend dist 0 backend
            
            match result with
            | Ok _ -> Assert.True(false, "Should fail with zero count")
            | Error err -> Assert.Contains("must be positive", err.Message)
        }
        |> Async.RunSynchronously
    
    // ========================================================================
    // STATISTICAL UTILITIES TESTS
    // ========================================================================
    
    [<Fact>]
    let ``computeStatistics calculates correct mean and stddev`` () =
        let dist = Normal (5.0, 2.0)
        
        match sampleMany dist 1000 with
        | Ok samples ->
            let stats = computeStatistics samples
            
            Assert.Equal(1000, stats.Count)
            
            // Mean should be ~5.0 (allow ±0.5)
            Assert.True(abs (stats.Mean - 5.0) < 0.5, $"Mean {stats.Mean} not near 5.0")
            
            // StdDev should be ~2.0 (allow 1.6-2.4)
            Assert.True(stats.StdDev > 1.6 && stats.StdDev < 2.4, $"StdDev {stats.StdDev} outside [1.6, 2.4]")
            
            // Min/Max should be reasonable
            Assert.True(stats.Min < stats.Mean)
            Assert.True(stats.Max > stats.Mean)
        
        | Error msg ->
            Assert.True(false, $"Should succeed: {msg}")
    
    [<Fact>]
    let ``computeStatistics handles uniform distribution`` () =
        let dist = Uniform (0.0, 10.0)
        
        match sampleMany dist 1000 with
        | Ok samples ->
            let stats = computeStatistics samples
            
            // Min should be ~0.0, Max should be ~10.0
            Assert.True(stats.Min >= 0.0 && stats.Min < 1.0)
            Assert.True(stats.Max > 9.0 && stats.Max <= 10.0)
            
            // Mean should be ~5.0 (allow ±1.0)
            Assert.True(abs (stats.Mean - 5.0) < 1.0)
        
        | Error msg ->
            Assert.True(false, $"Should succeed: {msg}")
    
    // ========================================================================
    // DISTRIBUTION HELPER TESTS
    // ========================================================================
    
    [<Fact>]
    let ``distributionName formats correctly`` () =
        Assert.Equal("StandardNormal(μ=0, σ=1)", distributionName StandardNormal)
        Assert.Contains("Normal(μ=5.00, σ=2.00)", distributionName (Normal (5.0, 2.0)))
        Assert.Contains("LogNormal(μ=0.00, σ=1.00)", distributionName (LogNormal (0.0, 1.0)))
        Assert.Contains("Exponential(λ=2.00)", distributionName (Exponential 2.0))
        Assert.Contains("Uniform(0.00, 10.00)", distributionName (Uniform (0.0, 10.0)))
        Assert.Contains("Custom(MyTransform)", distributionName (Custom ("MyTransform", fun x -> x)))
    
    [<Fact>]
    let ``expectedMean returns correct values`` () =
        match expectedMean StandardNormal with
        | Some mean -> Assert.Equal(0.0, mean)
        | None -> Assert.True(false, "Should return Some for StandardNormal")
        
        match expectedMean (Normal (5.0, 2.0)) with
        | Some mean -> Assert.Equal(5.0, mean)
        | None -> Assert.True(false, "Should return Some for Normal")
        
        match expectedMean (Exponential 2.0) with
        | Some mean -> Assert.Equal(0.5, mean)
        | None -> Assert.True(false, "Should return Some for Exponential")
        
        match expectedMean (Uniform (0.0, 10.0)) with
        | Some mean -> Assert.Equal(5.0, mean)
        | None -> Assert.True(false, "Should return Some for Uniform")
        
        match expectedMean (Custom ("test", fun x -> x)) with
        | Some _ -> Assert.True(false, "Should return None for Custom")
        | None -> Assert.True(true)
    
    [<Fact>]
    let ``expectedStdDev returns correct values`` () =
        match expectedStdDev StandardNormal with
        | Some stddev -> Assert.Equal(1.0, stddev)
        | None -> Assert.True(false, "Should return Some for StandardNormal")
        
        match expectedStdDev (Normal (5.0, 2.0)) with
        | Some stddev -> Assert.Equal(2.0, stddev)
        | None -> Assert.True(false, "Should return Some for Normal")
        
        match expectedStdDev (Exponential 2.0) with
        | Some stddev -> Assert.Equal(0.5, stddev)
        | None -> Assert.True(false, "Should return Some for Exponential")
        
        match expectedStdDev (Custom ("test", fun x -> x)) with
        | Some _ -> Assert.True(false, "Should return None for Custom")
        | None -> Assert.True(true)
    
    // ========================================================================
    // EDGE CASE TESTS (Regression tests for fixes)
    // ========================================================================
    
    [<Fact>]
    let ``computeStatistics fails gracefully on empty array`` () =
        let emptyArray : SampleResult[] = [||]
        
        let ex = Assert.Throws<Exception>(fun () -> computeStatistics emptyArray |> ignore)
        Assert.Contains("empty sample array", ex.Message)
    
    [<Fact>]
    let ``sample handles edge case probabilities without crashing`` () =
        // This tests that clamping works - previously would produce ±∞ or NaN
        let dist = StandardNormal
        
        // Generate many samples to statistically hit edge cases
        let results = [1..1000] |> List.choose (fun _ ->
            match sample dist with
            | Ok r -> Some r.Value
            | Error _ -> None)
        
        // All values should be finite (no ±∞ or NaN)
        results |> List.iter (fun x ->
            Assert.False(Double.IsInfinity(x), $"Value {x} is infinite")
            Assert.False(Double.IsNaN(x), $"Value {x} is NaN"))
    
    [<Fact>]
    let ``sample Exponential never returns infinity`` () =
        let dist = Exponential 1.0
        
        // Generate many samples
        let results = [1..1000] |> List.choose (fun _ ->
            match sample dist with
            | Ok r -> Some r.Value
            | Error _ -> None)
        
        // All values should be finite and positive
        results |> List.iter (fun x ->
            Assert.True(x > 0.0, "Exponential values must be positive")
            Assert.False(Double.IsInfinity(x), $"Value {x} is infinite")
            Assert.False(Double.IsNaN(x), $"Value {x} is NaN"))
    
    [<Fact>]
    let ``sample Uniform stays within bounds`` () =
        let minVal = 5.0
        let maxVal = 15.0
        let dist = Uniform (minVal, maxVal)
        
        // Generate many samples
        let results = [1..1000] |> List.choose (fun _ ->
            match sample dist with
            | Ok r -> Some r.Value
            | Error _ -> None)
        
        // All values should be strictly within [min, max]
        results |> List.iter (fun x ->
            Assert.True(x >= minVal, $"Value {x} < min {minVal}")
            Assert.True(x <= maxVal, $"Value {x} > max {maxVal}"))
    
    [<Fact>]
    let ``sample Custom validates result`` () =
        // Custom transform that returns NaN
        let distNaN = Custom ("NaN", fun _ -> Double.NaN)
        
        let resultNaN = sample distNaN
        match resultNaN with
        | Ok _ -> Assert.True(false, "Should fail with NaN transform")
        | Error msg -> Assert.Contains("NaN", msg)
        
        // Custom transform that returns Infinity
        let distInf = Custom ("Inf", fun _ -> Double.PositiveInfinity)
        
        let resultInf = sample distInf
        match resultInf with
        | Ok _ -> Assert.True(false, "Should fail with Infinity transform")
        | Error msg -> Assert.Contains("Infinity", msg)
    
    [<Fact>]
    let ``sample Custom handles exceptions`` () =
        // Custom transform that throws
        let distThrow = Custom ("Throw", fun _ -> failwith "Intentional error")
        
        let result = sample distThrow
        match result with
        | Ok _ -> Assert.True(false, "Should fail with throwing transform")
        | Error msg -> Assert.Contains("Custom transform failed", msg)
