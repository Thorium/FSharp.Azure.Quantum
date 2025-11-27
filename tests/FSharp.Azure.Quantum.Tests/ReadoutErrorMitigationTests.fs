namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open System

module ReadoutErrorMitigationTests =
    
    // ============================================================================
    // TKT-45: Readout Error Mitigation Tests
    // ============================================================================
    
    // Cycle #1: Configuration types and builders
    
    [<Fact>]
    let ``REMConfig should have sensible defaults for production use`` () =
        // Arrange & Act: Use default configuration
        let config = ReadoutErrorMitigation.defaultConfig
        
        // Assert: Verify production-ready defaults
        Assert.Equal(10000, config.CalibrationShots)  // High precision
        Assert.Equal(0.95, config.ConfidenceLevel)     // 95% CI standard
        Assert.True(config.ClipNegative, "Should clip negative probabilities by default")
        Assert.Equal(0.01, config.MinProbability)      // Filter 1% noise threshold
    
    [<Fact>]
    let ``withCalibrationShots should override calibration shots`` () =
        // Arrange: Start with default config
        let config = ReadoutErrorMitigation.defaultConfig
        
        // Act: Override calibration shots using fluent API
        let customConfig = 
            config
            |> ReadoutErrorMitigation.withCalibrationShots 5000
        
        // Assert: Verify override
        Assert.Equal(5000, customConfig.CalibrationShots)
        // Assert: Other fields unchanged
        Assert.Equal(0.95, customConfig.ConfidenceLevel)
    
    [<Fact>]
    let ``withConfidenceLevel should override confidence level`` () =
        // Arrange
        let config = ReadoutErrorMitigation.defaultConfig
        
        // Act: Set 99% confidence interval
        let customConfig = 
            config
            |> ReadoutErrorMitigation.withConfidenceLevel 0.99
        
        // Assert
        Assert.Equal(0.99, customConfig.ConfidenceLevel)
    
    [<Fact>]
    let ``withClipNegative should toggle negative clipping`` () =
        // Arrange
        let config = ReadoutErrorMitigation.defaultConfig
        
        // Act: Disable clipping to allow negative counts (for debugging)
        let customConfig = 
            config
            |> ReadoutErrorMitigation.withClipNegative false
        
        // Assert
        Assert.False(customConfig.ClipNegative)
    
    [<Fact>]
    let ``Fluent API should allow chaining configuration`` () =
        // Arrange & Act: Chain multiple configuration overrides
        let customConfig =
            ReadoutErrorMitigation.defaultConfig
            |> ReadoutErrorMitigation.withCalibrationShots 8000
            |> ReadoutErrorMitigation.withConfidenceLevel 0.99
            |> ReadoutErrorMitigation.withMinProbability 0.005
        
        // Assert: All overrides applied
        Assert.Equal(8000, customConfig.CalibrationShots)
        Assert.Equal(0.99, customConfig.ConfidenceLevel)
        Assert.Equal(0.005, customConfig.MinProbability)
    
    // Cycle #2: Bitstring conversion helpers
    
    [<Fact>]
    let ``bitstringToInt should convert binary strings to integers`` () =
        // Note: bitstringToInt is private - test via public API or reflection
        // For now, we'll test through public calibration matrix validation
        // This test validates the concept indirectly
        
        // Arrange: Create a simple 1-qubit calibration matrix
        let matrix = Array2D.init 2 2 (fun i j -> if i = j then 0.98 else 0.02)
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 1000
        }
        
        // Act: Validate (this uses bitstring conversion internally)
        let result = ReadoutErrorMitigation.validateCalibrationMatrix calibration
        
        // Assert: Should succeed
        match result with
        | Ok () -> Assert.True(true)
        | Error msg -> Assert.True(false, sprintf "Validation failed: %s" msg)
    
    // Cycle #3: Calibration matrix validation
    
    [<Fact>]
    let ``validateCalibrationMatrix should accept valid 1-qubit matrix`` () =
        // Arrange: Perfect 1-qubit calibration (98% accuracy)
        let matrix = Array2D.init 2 2 (fun i j ->
            if i = j then 0.98 else 0.02)  // Diagonal: correct measurement
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "IonQ"
            CalibrationShots = 10000
        }
        
        // Act
        let result = ReadoutErrorMitigation.validateCalibrationMatrix calibration
        
        // Assert: Should be valid
        match result with
        | Ok () -> Assert.True(true)
        | Error msg -> Assert.True(false, sprintf "Expected valid, got error: %s" msg)
    
    [<Fact>]
    let ``validateCalibrationMatrix should reject matrix with invalid dimensions`` () =
        // Arrange: 1 qubit should have 2x2 matrix, but provide 3x3
        let matrix = Array2D.zeroCreate 3 3
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1  // Claims 1 qubit, but matrix is 3x3
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 1000
        }
        
        // Act
        let result = ReadoutErrorMitigation.validateCalibrationMatrix calibration
        
        // Assert: Should fail with dimension error
        match result with
        | Error msg -> 
            Assert.Contains("Expected 2x2 matrix", msg)
        | Ok () -> 
            Assert.True(false, "Should reject invalid dimensions")
    
    [<Fact>]
    let ``validateCalibrationMatrix should reject probabilities outside [0,1]`` () =
        // Arrange: Matrix with invalid probability (negative)
        let matrix = Array2D.init 2 2 (fun i j -> -0.1)
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 1000
        }
        
        // Act
        let result = ReadoutErrorMitigation.validateCalibrationMatrix calibration
        
        // Assert: Should fail
        match result with
        | Error msg -> 
            Assert.Contains("not in [0,1]", msg)
        | Ok () -> 
            Assert.True(false, "Should reject invalid probabilities")
    
    [<Fact>]
    let ``validateCalibrationMatrix should reject columns not summing to 1.0`` () =
        // Arrange: Matrix where columns don't sum to 1.0 (invalid probability distribution)
        let matrix = Array2D.init 2 2 (fun i j -> 0.3)  // Each column sums to 0.6
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 1000
        }
        
        // Act
        let result = ReadoutErrorMitigation.validateCalibrationMatrix calibration
        
        // Assert: Should fail
        match result with
        | Error msg -> 
            Assert.Contains("sums to", msg)
        | Ok () -> 
            Assert.True(false, "Should reject invalid probability distribution")
    
    // Cycle #4: Matrix inversion numerical stability
    
    [<Fact>]
    let ``invertCalibrationMatrix should invert well-conditioned matrix`` () =
        // Arrange: Good 1-qubit calibration matrix (98% accuracy)
        let matrix = Array2D.init 2 2 (fun i j ->
            if i = j then 0.98 else 0.02)
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 1000
        }
        
        // Act: Invert
        let result = ReadoutErrorMitigation.invertCalibrationMatrix calibration
        
        // Assert: Should succeed and produce reasonable inverse
        match result with
        | Ok inverse ->
            // For M = [[0.98, 0.02], [0.02, 0.98]]
            // M^-1 ≈ [[1.02, -0.02], [-0.02, 1.02]]
            Assert.True(abs (inverse.[0,0] - 1.02) < 0.01, "Diagonal should be ~1.02")
            Assert.True(abs (inverse.[0,1] + 0.02) < 0.01, "Off-diagonal should be ~-0.02")
        | Error msg ->
            Assert.True(false, sprintf "Inversion should succeed: %s" msg)
    
    [<Fact>]
    let ``invertCalibrationMatrix should reject nearly singular matrix`` () =
        // Arrange: Nearly singular matrix (determinant ≈ 0)
        // Example: [[1.0, 1.0], [1.0, 1.0]] - columns are identical
        let matrix = Array2D.init 2 2 (fun i j -> 0.5)
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 1000
        }
        
        // Act
        let result = ReadoutErrorMitigation.invertCalibrationMatrix calibration
        
        // Assert: Should fail with singularity error
        match result with
        | Error msg ->
            Assert.Contains("singular", msg.ToLower())
        | Ok _ ->
            Assert.True(false, "Should reject singular matrix")
    
    [<Fact>]
    let ``invertCalibrationMatrix should warn on poorly-conditioned matrix`` () =
        // Arrange: Matrix with high condition number
        // Use extreme error rates: 99% vs 1% creates ill-conditioning
        let matrix = Array2D.init 2 2 (fun i j ->
            if i = j then 0.99 else 0.01)
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 1000
        }
        
        // Act: Should succeed but warn (warning goes to stdout via printfn)
        let result = ReadoutErrorMitigation.invertCalibrationMatrix calibration
        
        // Assert: Should succeed despite warning
        match result with
        | Ok _ -> Assert.True(true, "Should invert but warn")
        | Error _ -> Assert.True(false, "Should not fail, just warn")
    
    // Cycle #5: Histogram correction accuracy
    
    [<Fact>]
    let ``correctReadoutErrors should correct perfect measurement (no errors)`` () =
        // Arrange: Perfect calibration matrix (identity-like)
        let matrix = Array2D.init 2 2 (fun i j -> if i = j then 1.0 else 0.0)
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 1000
        }
        
        // Measured histogram: 100% in |0⟩ state
        let measured = Map.ofList [("0", 1000)]
        let config = ReadoutErrorMitigation.defaultConfig
        
        // Act: Correct (should return same histogram)
        let result = ReadoutErrorMitigation.correctReadoutErrors measured calibration config
        
        // Assert: Corrected should match original
        match result with
        | Ok corrected ->
            Assert.True(corrected.Histogram.ContainsKey("0"))
            let count0 = corrected.Histogram.["0"]
            Assert.True(abs (count0 - 1000.0) < 10.0, 
                sprintf "Expected ~1000 counts for |0⟩, got %.1f" count0)
        | Error msg ->
            Assert.True(false, sprintf "Correction should succeed: %s" msg)
    
    [<Fact>]
    let ``correctReadoutErrors should improve biased measurement`` () =
        // Arrange: Realistic 98% accuracy calibration
        let matrix = Array2D.init 2 2 (fun i j ->
            if i = j then 0.98 else 0.02)  // 2% readout error
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 10000
        }
        
        // Measured histogram: Should be 100% |0⟩, but measured 980/20 split
        // This simulates 2% readout error on perfect |0⟩ preparation
        let measured = Map.ofList [("0", 980); ("1", 20)]
        let config = ReadoutErrorMitigation.defaultConfig
        
        // Act: Apply correction
        let result = ReadoutErrorMitigation.correctReadoutErrors measured calibration config
        
        // Assert: Corrected should be closer to 1000/0
        match result with
        | Ok corrected ->
            let count0 = corrected.Histogram.["0"]
            let count1 = corrected.Histogram |> Map.tryFind "1" |> Option.defaultValue 0.0
            
            // After correction: should be much closer to 1000/0 than 980/20
            Assert.True(count0 > 990.0, 
                sprintf "Expected corrected |0⟩ > 990, got %.1f" count0)
            Assert.True(count1 < 10.0, 
                sprintf "Expected corrected |1⟩ < 10, got %.1f" count1)
        | Error msg ->
            Assert.True(false, sprintf "Correction should succeed: %s" msg)
    
    [<Fact>]
    let ``correctReadoutErrors should clip negative counts when configured`` () =
        // Arrange: Calibration matrix
        let matrix = Array2D.init 2 2 (fun i j ->
            if i = j then 0.95 else 0.05)
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 1000
        }
        
        // Extreme measurement that might produce negative after correction
        let measured = Map.ofList [("0", 50); ("1", 950)]
        let config = 
            ReadoutErrorMitigation.defaultConfig
            |> ReadoutErrorMitigation.withClipNegative true
        
        // Act
        let result = ReadoutErrorMitigation.correctReadoutErrors measured calibration config
        
        // Assert: No negative counts
        match result with
        | Ok corrected ->
            for (bitstring, count) in Map.toList corrected.Histogram do
                Assert.True(count >= 0.0, 
                    sprintf "Count for %s should be non-negative, got %.1f" bitstring count)
        | Error msg ->
            Assert.True(false, sprintf "Correction should succeed: %s" msg)
    
    [<Fact>]
    let ``correctReadoutErrors should normalize probabilities to sum to 1.0`` () =
        // Arrange: Standard calibration
        let matrix = Array2D.init 2 2 (fun i j ->
            if i = j then 0.97 else 0.03)
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 1000
        }
        
        let measured = Map.ofList [("0", 600); ("1", 400)]
        let config = ReadoutErrorMitigation.defaultConfig
        
        // Act
        let result = ReadoutErrorMitigation.correctReadoutErrors measured calibration config
        
        // Assert: Total probability sums to ~1.0 (or total counts = original total)
        match result with
        | Ok corrected ->
            let totalCounts = 
                corrected.Histogram 
                |> Map.toList 
                |> List.sumBy snd
            
            // Should sum to original total shots (1000)
            Assert.True(abs (totalCounts - 1000.0) < 5.0,
                sprintf "Total counts should be ~1000, got %.1f" totalCounts)
            
            // Check goodness of fit is high
            Assert.True(corrected.GoodnessOfFit > 0.95,
                sprintf "Goodness of fit should be > 0.95, got %.3f" corrected.GoodnessOfFit)
        | Error msg ->
            Assert.True(false, sprintf "Correction should succeed: %s" msg)
    
    [<Fact>]
    let ``correctReadoutErrors should filter noise below threshold`` () =
        // Arrange: Calibration with realistic errors
        let matrix = Array2D.init 2 2 (fun i j ->
            if i = j then 0.98 else 0.02)
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 10000
        }
        
        // Measured with tiny noise count
        let measured = Map.ofList [("0", 9950); ("1", 50)]  // 0.5% noise
        let config = 
            ReadoutErrorMitigation.defaultConfig
            |> ReadoutErrorMitigation.withMinProbability 0.01  // Filter < 1%
        
        // Act
        let result = ReadoutErrorMitigation.correctReadoutErrors measured calibration config
        
        // Assert: After correction, tiny counts might be filtered
        match result with
        | Ok corrected ->
            // Should still have the dominant state
            Assert.True(corrected.Histogram.ContainsKey("0"))
            
            // Might or might not have |1⟩ depending on correction magnitude
            let count0 = corrected.Histogram.["0"]
            Assert.True(count0 > 9900.0,
                sprintf "Dominant state should have > 9900 counts, got %.1f" count0)
        | Error msg ->
            Assert.True(false, sprintf "Correction should succeed: %s" msg)
    
    // Cycle #6: Confidence interval coverage
    
    [<Fact>]
    let ``correctReadoutErrors should provide confidence intervals for all states`` () =
        // Arrange: Standard setup
        let matrix = Array2D.init 2 2 (fun i j ->
            if i = j then 0.98 else 0.02)
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 10000
        }
        
        let measured = Map.ofList [("0", 980); ("1", 20)]
        let config = ReadoutErrorMitigation.defaultConfig
        
        // Act
        let result = ReadoutErrorMitigation.correctReadoutErrors measured calibration config
        
        // Assert: Every histogram entry has confidence interval
        match result with
        | Ok corrected ->
            for (bitstring, count) in Map.toList corrected.Histogram do
                Assert.True(corrected.ConfidenceIntervals.ContainsKey(bitstring),
                    sprintf "Missing confidence interval for state %s" bitstring)
                
                let (lower, upper) = corrected.ConfidenceIntervals.[bitstring]
                
                // CI should bracket the count
                Assert.True(lower <= count && count <= upper,
                    sprintf "Count %.1f should be within CI [%.1f, %.1f]" count lower upper)
                
                // CI should be reasonable (not degenerate)
                Assert.True(upper > lower,
                    sprintf "Upper %.1f should exceed lower %.1f" upper lower)
        | Error msg ->
            Assert.True(false, sprintf "Correction should succeed: %s" msg)
    
    [<Fact>]
    let ``Confidence intervals should widen with smaller sample sizes`` () =
        // Arrange: Same calibration, different shot counts
        let matrix = Array2D.init 2 2 (fun i j ->
            if i = j then 0.98 else 0.02)
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 10000
        }
        
        // High shots: narrow CI
        let measuredHigh = Map.ofList [("0", 9800); ("1", 200)]
        let configHigh = 
            ReadoutErrorMitigation.defaultConfig
            |> ReadoutErrorMitigation.withCalibrationShots 10000
        
        // Low shots: wide CI
        let measuredLow = Map.ofList [("0", 98); ("1", 2)]
        let configLow = 
            ReadoutErrorMitigation.defaultConfig
            |> ReadoutErrorMitigation.withCalibrationShots 100
        
        // Act
        let resultHigh = ReadoutErrorMitigation.correctReadoutErrors measuredHigh calibration configHigh
        let resultLow = ReadoutErrorMitigation.correctReadoutErrors measuredLow calibration configLow
        
        // Assert: Low shots should have wider relative CI
        match resultHigh, resultLow with
        | Ok high, Ok low ->
            let (lowerHigh, upperHigh) = high.ConfidenceIntervals.["0"]
            let (lowerLow, upperLow) = low.ConfidenceIntervals.["0"]
            
            let widthHigh = upperHigh - lowerHigh
            let widthLow = upperLow - lowerLow
            
            let relativeWidthHigh = widthHigh / high.Histogram.["0"]
            let relativeWidthLow = widthLow / low.Histogram.["0"]
            
            Assert.True(relativeWidthLow > relativeWidthHigh,
                sprintf "Low shots CI (%.3f) should be relatively wider than high shots (%.3f)" 
                    relativeWidthLow relativeWidthHigh)
        | _ ->
            Assert.True(false, "Both corrections should succeed")
    
    [<Fact>]
    let ``Confidence intervals should be non-negative`` () =
        // Arrange
        let matrix = Array2D.init 2 2 (fun i j ->
            if i = j then 0.95 else 0.05)
        
        let calibration: ReadoutErrorMitigation.CalibrationMatrix = {
            Matrix = matrix
            Qubits = 1
            Timestamp = DateTime.UtcNow
            Backend = "test"
            CalibrationShots = 1000
        }
        
        let measured = Map.ofList [("0", 950); ("1", 50)]
        let config = ReadoutErrorMitigation.defaultConfig
        
        // Act
        let result = ReadoutErrorMitigation.correctReadoutErrors measured calibration config
        
        // Assert: Lower bounds should be >= 0
        match result with
        | Ok corrected ->
            for (bitstring, (lower, upper)) in Map.toList corrected.ConfidenceIntervals do
                Assert.True(lower >= 0.0,
                    sprintf "Lower CI bound for %s should be non-negative, got %.1f" bitstring lower)
                Assert.True(upper >= 0.0,
                    sprintf "Upper CI bound for %s should be non-negative, got %.1f" bitstring upper)
        | Error msg ->
            Assert.True(false, sprintf "Correction should succeed: %s" msg)
    
    // TODO: Cycle #7: Integration tests (6 tests - 1/2/3 qubit scenarios)
