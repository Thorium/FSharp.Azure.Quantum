namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Algorithms.QuboToIsing
open FSharp.Azure.Quantum.Backends.DWaveTypes

/// Unit tests for QUBO ↔ Ising conversion
///
/// Tests cover:
/// - QUBO → Ising conversion (quboToIsing)
/// - Ising → QUBO solution conversion (isingToQubo)
/// - QUBO → Ising solution conversion (quboToIsingSolution)
/// - Energy calculations (quboEnergy, isingEnergy)
/// - Validation functions (validateSpins, validateBinary)
/// - Conversion verification (verifyConversion)
module QuboToIsingTests =
    
    // ========================================================================
    // QUBO → ISING CONVERSION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``QUBO with single diagonal term converts correctly`` () =
        // QUBO: E = 2*x0  (single variable)
        let qubo = Map.ofList [((0, 0), 2.0)]
        
        let ising = quboToIsing qubo
        
        // Expected Ising: h_0 = 2/2 = 1.0, offset = 2/2 = 1.0
        // (x = (1+s)/2, so 2*x = 2*(1+s)/2 = 1 + s = 1 + 1*s)
        Assert.Equal(1.0, ising.LinearCoeffs.[0], precision = 10)
        Assert.Empty(ising.QuadraticCoeffs)
        Assert.Equal(1.0, ising.Offset, precision = 10)
    
    [<Fact>]
    let ``QUBO with single off-diagonal term converts correctly`` () =
        // QUBO: E = -5*x0*x1  (two-variable interaction)
        let qubo = Map.ofList [((0, 1), -5.0)]
        
        let ising = quboToIsing qubo
        
        // Expected Ising:
        // h_0 = -5/4 = -1.25
        // h_1 = -5/4 = -1.25
        // J_01 = -5/4 = -1.25
        // offset = -5/4 = -1.25
        Assert.Equal(-1.25, ising.LinearCoeffs.[0], precision = 10)
        Assert.Equal(-1.25, ising.LinearCoeffs.[1], precision = 10)
        Assert.Equal(-1.25, ising.QuadraticCoeffs.[(0, 1)], precision = 10)
        Assert.Equal(-1.25, ising.Offset, precision = 10)
    
    [<Fact>]
    let ``QUBO with multiple terms converts correctly`` () =
        // QUBO: E = -5*x0*x1 - 3*x1*x2 + 2*x0 + 4*x1 + x2
        let qubo = Map.ofList [
            ((0, 1), -5.0)
            ((1, 2), -3.0)
            ((0, 0), 2.0)
            ((1, 1), 4.0)
            ((2, 2), 1.0)
        ]
        
        let ising = quboToIsing qubo
        
        // Verify linear coefficients exist
        Assert.True(ising.LinearCoeffs.ContainsKey(0))
        Assert.True(ising.LinearCoeffs.ContainsKey(1))
        Assert.True(ising.LinearCoeffs.ContainsKey(2))
        
        // Verify quadratic coefficients
        Assert.Equal(-1.25, ising.QuadraticCoeffs.[(0, 1)], precision = 10)
        Assert.Equal(-0.75, ising.QuadraticCoeffs.[(1, 2)], precision = 10)
        
        // Verify offset is non-zero
        Assert.NotEqual(0.0, ising.Offset)
    
    [<Fact>]
    let ``QUBO with reversed indices (j < i) normalizes to upper triangle`` () =
        // QUBO: E = -5*x1*x0  (reversed indices)
        let qubo = Map.ofList [((1, 0), -5.0)]
        
        let ising = quboToIsing qubo
        
        // Should normalize to (0, 1) not (1, 0)
        Assert.True(ising.QuadraticCoeffs.ContainsKey((0, 1)))
        Assert.False(ising.QuadraticCoeffs.ContainsKey((1, 0)))
        Assert.Equal(-1.25, ising.QuadraticCoeffs.[(0, 1)], precision = 10)
    
    [<Fact>]
    let ``Empty QUBO converts to empty Ising`` () =
        let qubo = Map.empty
        
        let ising = quboToIsing qubo
        
        Assert.Empty(ising.LinearCoeffs)
        Assert.Empty(ising.QuadraticCoeffs)
        Assert.Equal(0.0, ising.Offset)
    
    // ========================================================================
    // SOLUTION CONVERSION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Ising spins convert to QUBO binary correctly`` () =
        // Ising: s0=1, s1=-1, s2=1  (spins)
        let spins = Map.ofList [(0, 1); (1, -1); (2, 1)]
        
        let binary = isingToQubo spins
        
        // Expected QUBO: x0=1, x1=0, x2=1  (x = (1+s)/2)
        Assert.Equal(1, binary.[0])
        Assert.Equal(0, binary.[1])
        Assert.Equal(1, binary.[2])
    
    [<Fact>]
    let ``QUBO binary converts to Ising spins correctly`` () =
        // QUBO: x0=1, x1=0, x2=1  (binary)
        let binary = Map.ofList [(0, 1); (1, 0); (2, 1)]
        
        let spins = quboToIsingSolution binary
        
        // Expected Ising: s0=1, s1=-1, s2=1  (s = 2x - 1)
        Assert.Equal(1, spins.[0])
        Assert.Equal(-1, spins.[1])
        Assert.Equal(1, spins.[2])
    
    [<Fact>]
    let ``Ising-to-QUBO-to-Ising roundtrip preserves spins`` () =
        let originalSpins = Map.ofList [(0, 1); (1, -1); (2, 1); (3, -1)]
        
        let binary = isingToQubo originalSpins
        let roundtripSpins = quboToIsingSolution binary
        
        // Compare maps element by element
        for KeyValue(k, v) in originalSpins do
            Assert.Equal(v, roundtripSpins.[k])
    
    [<Fact>]
    let ``QUBO-to-Ising-to-QUBO roundtrip preserves binary`` () =
        let originalBinary = Map.ofList [(0, 1); (1, 0); (2, 1); (3, 0)]
        
        let spins = quboToIsingSolution originalBinary
        let roundtripBinary = isingToQubo spins
        
        // Compare maps element by element
        for KeyValue(k, v) in originalBinary do
            Assert.Equal(v, roundtripBinary.[k])
    
    // ========================================================================
    // ENERGY CALCULATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Ising energy calculation for simple problem`` () =
        // Problem: h_0 = 1.0, h_1 = -0.5, J_01 = -2.0, offset = 0.5
        let problem = {
            LinearCoeffs = Map.ofList [(0, 1.0); (1, -0.5)]
            QuadraticCoeffs = Map.ofList [((0, 1), -2.0)]
            Offset = 0.5
        }
        
        // Solution: s_0 = 1, s_1 = -1
        let spins = Map.ofList [(0, 1); (1, -1)]
        
        let energy = isingEnergy problem spins
        
        // Expected: 1.0*1 + (-0.5)*(-1) + (-2.0)*1*(-1) + 0.5
        //         = 1.0 + 0.5 + 2.0 + 0.5 = 4.0
        Assert.Equal(4.0, energy, precision = 10)
    
    [<Fact>]
    let ``QUBO energy calculation for simple problem`` () =
        // QUBO: E = 2*x0 - 5*x0*x1 + 4*x1
        let qubo = Map.ofList [
            ((0, 0), 2.0)
            ((0, 1), -5.0)
            ((1, 1), 4.0)
        ]
        
        // Solution: x0=1, x1=1
        let binary = Map.ofList [(0, 1); (1, 1)]
        
        let energy = quboEnergy qubo binary
        
        // Expected: 2*1 + (-5)*1*1 + 4*1 = 2 - 5 + 4 = 1.0
        Assert.Equal(1.0, energy, precision = 10)
    
    [<Fact>]
    let ``QUBO and Ising energies match for equivalent problems`` () =
        // QUBO: E = -5*x0*x1 + 2*x0 + 4*x1
        let qubo = Map.ofList [
            ((0, 1), -5.0)
            ((0, 0), 2.0)
            ((1, 1), 4.0)
        ]
        
        // QUBO solution: x0=1, x1=1
        let binary = Map.ofList [(0, 1); (1, 1)]
        
        // Calculate QUBO energy
        let quboEnergyVal = quboEnergy qubo binary
        
        // Convert to Ising
        let ising = quboToIsing qubo
        let spins = quboToIsingSolution binary
        
        // Calculate Ising energy
        let isingEnergyVal = isingEnergy ising spins
        
        // Energies should be equal
        Assert.Equal(quboEnergyVal, isingEnergyVal, precision = 10)
    
    [<Fact>]
    let ``Energy calculation with missing qubits defaults to 0`` () =
        let problem = {
            LinearCoeffs = Map.ofList [(0, 1.0); (1, -0.5)]
            QuadraticCoeffs = Map.ofList [((0, 1), -2.0)]
            Offset = 0.0
        }
        
        // Solution missing qubit 1
        let spins = Map.ofList [(0, 1)]
        
        let energy = isingEnergy problem spins
        
        // Expected: 1.0*1 + (-0.5)*0 + (-2.0)*1*0 = 1.0
        Assert.Equal(1.0, energy, precision = 10)
    
    // ========================================================================
    // VALIDATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``validateSpins accepts valid spins`` () =
        let spins = Map.ofList [(0, 1); (1, -1); (2, 1); (3, -1)]
        
        let result = validateSpins spins
        
        Assert.True(result.IsOk)
    
    [<Fact>]
    let ``validateSpins rejects invalid spin values`` () =
        let spins = Map.ofList [(0, 1); (1, 0); (2, -1)]  // 0 is invalid
        
        let result = validateSpins spins
        
        Assert.True(result.IsError)
        match result with
        | Error msg -> Assert.Contains("Invalid spin", msg.Message)
        | Ok _ -> Assert.True(false, "Should have failed validation")
    
    [<Fact>]
    let ``validateBinary accepts valid binary values`` () =
        let binary = Map.ofList [(0, 1); (1, 0); (2, 1); (3, 0)]
        
        let result = validateBinary binary
        
        Assert.True(result.IsOk)
    
    [<Fact>]
    let ``validateBinary rejects invalid binary values`` () =
        let binary = Map.ofList [(0, 1); (1, 2); (2, 0)]  // 2 is invalid
        
        let result = validateBinary binary
        
        Assert.True(result.IsError)
        match result with
        | Error msg -> Assert.Contains("Invalid binary", msg.Message)
        | Ok _ -> Assert.True(false, "Should have failed validation")
    
    // ========================================================================
    // CONVERSION VERIFICATION TESTS
    // ========================================================================
    
    [<Fact>]
    let ``verifyConversion confirms correct QUBO-Ising equivalence`` () =
        // QUBO: E = -5*x0*x1 + 2*x0 + 4*x1
        let qubo = Map.ofList [
            ((0, 1), -5.0)
            ((0, 0), 2.0)
            ((1, 1), 4.0)
        ]
        
        // Solution: x0=1, x1=1
        let solution = Map.ofList [(0, 1); (1, 1)]
        
        let result = verifyConversion qubo solution
        
        match result with
        | Ok diff ->
            // Energy difference should be negligible
            Assert.True(diff < 1e-10, $"Energy difference too large: {diff}")
        | Error e ->
            Assert.True(false, $"Verification failed: {e}")
    
    [<Fact>]
    let ``verifyConversion detects invalid binary values`` () =
        let qubo = Map.ofList [((0, 1), -5.0)]
        let invalidSolution = Map.ofList [(0, 2); (1, 1)]  // 2 is invalid
        
        let result = verifyConversion qubo invalidSolution
        
        Assert.True(result.IsError)
    
    [<Fact>]
    let ``verifyConversion works for multiple solutions`` () =
        // QUBO: E = -5*x0*x1
        let qubo = Map.ofList [((0, 1), -5.0)]
        
        // Test multiple solutions
        let solutions = [
            Map.ofList [(0, 0); (1, 0)]  // Both off
            Map.ofList [(0, 1); (1, 0)]  // One on
            Map.ofList [(0, 0); (1, 1)]  // Other on
            Map.ofList [(0, 1); (1, 1)]  // Both on (optimal)
        ]
        
        for solution in solutions do
            let result = verifyConversion qubo solution
            match result with
            | Ok diff -> Assert.True(diff < 1e-10, $"Conversion error for {solution}")
            | Error e -> Assert.True(false, $"Failed for {solution}: {e}")
    
    // ========================================================================
    // EDGE CASES AND SPECIAL SCENARIOS
    // ========================================================================
    
    [<Fact>]
    let ``Large QUBO coefficients convert correctly`` () =
        // QUBO with large coefficients
        let qubo = Map.ofList [
            ((0, 1), -1000.0)
            ((1, 2), 500.0)
        ]
        
        let ising = quboToIsing qubo
        
        // Verify conversion completes without overflow
        Assert.NotEmpty(ising.LinearCoeffs)
        Assert.NotEmpty(ising.QuadraticCoeffs)
    
    [<Fact>]
    let ``Small QUBO coefficients preserve precision`` () =
        // QUBO with very small coefficients
        let qubo = Map.ofList [
            ((0, 1), -1e-10)
            ((1, 2), 5e-11)
        ]
        
        let ising = quboToIsing qubo
        
        // Verify conversion preserves small values
        Assert.True(abs ising.QuadraticCoeffs.[(0, 1)] > 0.0)
        Assert.True(abs ising.QuadraticCoeffs.[(1, 2)] > 0.0)
    
    [<Fact>]
    let ``All-zeros QUBO converts to all-zeros Ising`` () =
        let qubo = Map.ofList [
            ((0, 0), 0.0)
            ((1, 1), 0.0)
            ((0, 1), 0.0)
        ]
        
        let ising = quboToIsing qubo
        
        // All coefficients should be zero
        ising.LinearCoeffs |> Map.iter (fun _ v -> Assert.Equal(0.0, v))
        ising.QuadraticCoeffs |> Map.iter (fun _ v -> Assert.Equal(0.0, v))
        Assert.Equal(0.0, ising.Offset)
    
    [<Fact>]
    let ``MaxCut-style QUBO converts correctly`` () =
        // MaxCut QUBO: Maximize cut = Minimize -cut
        // Edge (0,1) weight 5: QUBO term -5*x0*x1 + 5*x0 + 5*x1
        let qubo = Map.ofList [
            ((0, 1), -5.0)
            ((0, 0), 5.0)
            ((1, 1), 5.0)
        ]
        
        let ising = quboToIsing qubo
        
        // Verify quadratic term is negative (attractive coupling)
        Assert.True(ising.QuadraticCoeffs.[(0, 1)] < 0.0)
    
    [<Fact>]
    let ``QUBO with 10 variables converts without error`` () =
        // Generate 10-variable QUBO
        let qubo = 
            [0 .. 9]
            |> List.collect (fun i ->
                [i .. 9] |> List.map (fun j -> ((i, j), float (i + j)))
            )
            |> Map.ofList
        
        let ising = quboToIsing qubo
        
        // Verify all variables present
        for i in 0 .. 9 do
            Assert.True(ising.LinearCoeffs.ContainsKey(i), $"Missing linear coeff for qubit {i}")
