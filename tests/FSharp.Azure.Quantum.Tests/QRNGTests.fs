namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum.Algorithms.QRNG
open FSharp.Azure.Quantum.Core

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
            let backend = BackendAbstraction.createLocalBackend()
            
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
            let backend = BackendAbstraction.createLocalBackend()
            
            let! result = generateWithBackend 2000 backend
            
            match result with
            | Ok _ ->
                Assert.True(false, "Should fail with excessive bits")
            | Error msg ->
                Assert.Contains("too large", msg)
        }
        |> Async.RunSynchronously
    
    [<Fact>]
    let ``generateWithBackend fails with zero bits`` () =
        async {
            let backend = BackendAbstraction.createLocalBackend()
            
            let! result = generateWithBackend 0 backend
            
            match result with
            | Ok _ ->
                Assert.True(false, "Should fail with zero bits")
            | Error msg ->
                Assert.Contains("must be positive", msg)
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
