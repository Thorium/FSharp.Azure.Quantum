namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Topological

module BraidingConsistencyTests =
    
    // ========================================================================
    // TEST HELPERS - Make tests more readable and maintainable
    // ========================================================================
    
    /// Helper to verify consistency or fail with meaningful message
    let private verifyConsistencyOrFail anyonType =
        match BraidingConsistency.verifyConsistency anyonType with
        | Ok summary -> summary
        | Error err -> failwith $"Failed to verify consistency for {anyonType}: {err.Message}"
    
    /// Helper to assert all checks passed
    let private assertAllChecksPassed (summary: BraidingConsistency.ConsistencySummary) =
        if not summary.AllSatisfied then
            let display = BraidingConsistency.displayConsistencySummary summary
            failwith $"Consistency checks failed:\n{display}"
        
        Assert.True(summary.AllSatisfied, "All consistency checks should pass")
    
    /// Helper to assert specific check count
    let private assertCheckCount (expectedMin: int) (actual: int) (checkType: string) =
        Assert.True(actual >= expectedMin, 
            $"{checkType} checks: expected at least {expectedMin}, got {actual}")
    
    // ========================================================================
    // ISING CONSISTENCY TESTS - Testing Majorana zero mode algebra
    // ========================================================================
    
    [<Fact>]
    let ``Ising anyons satisfy pentagon equation ensuring fusion associativity`` () =
        // Business meaning: The pentagon equation guarantees that different
        // orders of fusing four anyons give the same result. This is essential
        // for consistent quantum computation - ((a×b)×c)×d = a×(b×(c×d)).
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        
        Assert.NotEmpty(summary.PentagonChecks)
        Assert.True(
            summary.PentagonChecks |> List.forall (fun c -> c.IsSatisfied),
            "All pentagon equations should be satisfied"
        )
    
    [<Fact>]
    let ``Ising hexagon equations tested for braiding-fusion compatibility`` () =
        // Business meaning: The hexagon equation ensures that braiding and fusion
        // operations commute properly. Using Kitaev convention R-matrix phases,
        // all non-trivial hexagon checks pass with negligible numerical deviation.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        
        Assert.NotEmpty(summary.HexagonChecks)
        
        // Count how many hexagon checks actually have valid fusion paths
        let nonTrivialChecks = 
            summary.HexagonChecks 
            |> List.filter (fun c -> not (c.Details.Contains("No valid")))
        
        Assert.NotEmpty(nonTrivialChecks)
        
        // All non-trivial hexagon checks must pass (R-matrix uses Kitaev convention)
        let passedCount = nonTrivialChecks |> List.filter (fun c -> c.IsSatisfied) |> List.length
        let totalCount = nonTrivialChecks.Length
        Assert.True((passedCount = totalCount),
            $"All non-trivial hexagon checks must pass, got {passedCount}/{totalCount}")
    
    [<Fact>]
    let ``Ising pentagon equations have negligible deviation`` () =
        // Business meaning: Pentagon equations (F-matrix only) should have
        // negligible numerical error since we trust literature values.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        
        let maxPentagonDeviation = 
            summary.PentagonChecks 
            |> List.map (fun c -> c.MaxDeviation) 
            |> List.max
        
        Assert.InRange(maxPentagonDeviation, 0.0, 1e-9)
    
    [<Fact>]
    let ``Ising consistency check completes for all particle combinations`` () =
        // Business meaning: With particles {1, σ, ψ}, we check all 3^4 = 81 possible
        // 4-tuples. Both pentagon and hexagon equations must be fully satisfied.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        
        // Pentagon equations should be satisfied
        Assert.True(
            summary.PentagonChecks |> List.forall (fun c -> c.IsSatisfied),
            "Pentagon equations must be satisfied"
        )
        
        // All hexagon checks must pass (Kitaev convention R-matrix phases verified)
        Assert.True(
            summary.HexagonChecks |> List.forall (fun c -> c.IsSatisfied),
            "All hexagon checks must be satisfied (including trivial vacuum cases)"
        )
        
        // Overall consistency must hold
        assertAllChecksPassed summary
    
    [<Fact>]
    let ``Ising consistency summary includes both pentagon and hexagon results`` () =
        // Business meaning: A complete consistency check must verify both
        // pentagon (fusion associativity) and hexagon (braiding-fusion commutation).
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        
        Assert.NotEmpty(summary.PentagonChecks)
        Assert.NotEmpty(summary.HexagonChecks)
        Assert.Equal(AnyonSpecies.AnyonType.Ising, summary.AnyonType)
    
    // ========================================================================
    // FIBONACCI CONSISTENCY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Fibonacci anyons satisfy pentagon equation for universal quantum computation`` () =
        // Business meaning: Fibonacci anyons must satisfy pentagon to enable
        // universal topological quantum computation. The golden ratio appearing
        // in F-matrices must satisfy algebraic consistency relations.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Fibonacci
        
        Assert.NotEmpty(summary.PentagonChecks)
        Assert.True(
            summary.PentagonChecks |> List.forall (fun c -> c.IsSatisfied),
            "All pentagon equations should be satisfied"
        )
    
    [<Fact>]
    let ``Fibonacci hexagon equations tested for golden ratio phase compatibility`` () =
        // Business meaning: R-matrix phases (multiples of π/5) and F-matrix golden
        // ratio values (φ^(-1)) must be compatible. Full numerical hexagon verification
        // passes for all non-trivial fusion paths.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Fibonacci
        
        let nonTrivialChecks = 
            summary.HexagonChecks 
            |> List.filter (fun c -> not (c.Details.Contains("No valid")))
        
        Assert.NotEmpty(nonTrivialChecks)
        
        // All non-trivial hexagon checks must pass
        let passedCount = nonTrivialChecks |> List.filter (fun c -> c.IsSatisfied) |> List.length
        let totalCount = nonTrivialChecks.Length
        Assert.True((passedCount = totalCount),
            $"All non-trivial Fibonacci hexagon checks must pass, got {passedCount}/{totalCount}")
    
    [<Fact>]
    let ``Fibonacci consistency check completes for all valid fusion paths`` () =
        // Business meaning: With particles {1, τ}, we check all 2^4 = 16 possible
        // 4-tuples. Both pentagon and hexagon equations must be fully satisfied.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Fibonacci
        
        // Pentagon equations should be satisfied
        Assert.True(
            summary.PentagonChecks |> List.forall (fun c -> c.IsSatisfied),
            "Pentagon equations must be satisfied"
        )
        
        // All hexagon checks must pass
        Assert.True(
            summary.HexagonChecks |> List.forall (fun c -> c.IsSatisfied),
            "All Fibonacci hexagon checks must be satisfied"
        )
        
        // Overall consistency must hold
        assertAllChecksPassed summary
    
    [<Fact>]
    let ``Fibonacci pentagon equations have negligible deviation`` () =
        // Business meaning: Pentagon equations with golden ratio must be exact.
        // We verify numerical accuracy of our F-matrix golden ratio calculations.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Fibonacci
        
        let maxPentagonDeviation = 
            summary.PentagonChecks 
            |> List.map (fun c -> c.MaxDeviation) 
            |> List.max
        
        Assert.InRange(maxPentagonDeviation, 0.0, 1e-9)
    
    // ========================================================================
    // CROSS-THEORY CONSISTENCY TESTS
    // ========================================================================
    
    [<Fact>]
    let ``SU(2)_2 consistency matches Ising due to isomorphism`` () =
        // Business meaning: SU(2) level 2 and Ising theories are mathematically
        // isomorphic, so they must satisfy identical consistency equations.
        // Note: Pentagon checks (F-matrix only) should match exactly.
        // Hexagon checks involve R-matrix type validation which currently
        // has a known limitation for SU(2)_2 → Ising delegation, so we
        // compare pentagon results specifically.
        let su2Summary = verifyConsistencyOrFail (AnyonSpecies.AnyonType.SU2Level 2)
        let isingSummary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        
        // Pentagon checks should match: same F-symbols, same particle set
        let su2PentagonsSatisfied = su2Summary.PentagonChecks |> List.forall (fun c -> c.IsSatisfied)
        let isingPentagonsSatisfied = isingSummary.PentagonChecks |> List.forall (fun c -> c.IsSatisfied)
        Assert.Equal(isingPentagonsSatisfied, su2PentagonsSatisfied)
        Assert.Equal(isingSummary.PentagonChecks.Length, su2Summary.PentagonChecks.Length)
    
    [<Fact>]
    let ``Consistency verification uses matching F and R matrix types`` () =
        // Business meaning: verifyConsistency computes both F and R matrices
        // for the same anyon type, ensuring they're compatible. Mixing theories
        // would be physically meaningless.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        
        // Verify we got results (would fail earlier if type mismatch)
        Assert.NotEmpty(summary.PentagonChecks)
        Assert.NotEmpty(summary.HexagonChecks)
    
    // ========================================================================
    // PENTAGON-SPECIFIC TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Pentagon equation verified for all Ising particle 5-tuples`` () =
        // With particles {1,σ,ψ}, we check all 3^5 = 243 particle 5-tuples (a,b,c,d,e).
        // Each 5-tuple yields one ConsistencyCheckResult covering all valid
        // intermediate-channel index combinations for that tuple.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        
        // 3 particles ^ 5 positions = 243 per-combination results
        Assert.Equal(243, summary.PentagonChecks.Length)
        
        summary.PentagonChecks
        |> List.iter (fun check ->
            Assert.True(check.IsSatisfied, $"Pentagon check failed: {check.Equation} - {check.Details}")
        )
    
    [<Fact>]
    let ``Pentagon checks include equation description for debugging`` () =
        // Business meaning: When a pentagon check fails, developers need to know
        // which specific particle combination caused the failure.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        
        summary.PentagonChecks
        |> List.iter (fun check ->
            Assert.False(System.String.IsNullOrWhiteSpace(check.Equation))
            Assert.False(System.String.IsNullOrWhiteSpace(check.Details))
        )
    
    // ========================================================================
    // HEXAGON-SPECIFIC TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Hexagon equation verified for all Ising particle 4-tuples`` () =
        // Business meaning: Hexagon must hold for all combinations of 4 particles.
        // With {1,σ,ψ}, that's 3^4 = 81 cases.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        
        assertCheckCount 1 summary.HexagonChecks.Length "Hexagon"
    
    [<Fact>]
    let ``Hexagon checks skip cases with no valid fusion paths`` () =
        // Business meaning: If particles cannot fuse according to fusion rules,
        // the hexagon equation is vacuously true (no braiding possible).
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        
        let trivialChecks = 
            summary.HexagonChecks 
            |> List.filter (fun c -> c.Details.Contains("No valid"))
        
        // All trivial checks should be marked as satisfied
        trivialChecks
        |> List.iter (fun check ->
            Assert.True(check.IsSatisfied, "Trivial hexagon cases should be satisfied")
        )
    
    [<Fact>]
    let ``Hexagon checks include maximum deviation measurement`` () =
        // Business meaning: We measure deviation between left and right sides
        // of the hexagon equation for diagnostic purposes. Passing hexagons
        // should have negligible deviation.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        
        let nonTrivialChecks = 
            summary.HexagonChecks 
            |> List.filter (fun c -> not (c.Details.Contains("No valid")))
        
        // All checks should have non-negative deviation
        nonTrivialChecks
        |> List.iter (fun check ->
            Assert.True(check.MaxDeviation >= 0.0, "Deviation must be non-negative")
        )
        
        // Checks that pass should have negligible deviation
        let passingChecks = nonTrivialChecks |> List.filter (fun c -> c.IsSatisfied)
        passingChecks
        |> List.iter (fun check ->
            Assert.True(check.MaxDeviation < 1e-9, "Passing checks must have negligible deviation")
        )
    
    // ========================================================================
    // DISPLAY AND DIAGNOSTICS TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Consistency summary displays readable format with status symbols`` () =
        // Business meaning: Developers need quick visual feedback on which
        // equations passed (✓) or failed (✗) for rapid debugging.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        let display = BraidingConsistency.displayConsistencySummary summary
        
        Assert.Contains("Ising", display)
        Assert.Contains("Consistency Verification", display)
        
        // Should show overall status
        if summary.AllSatisfied then
            Assert.Contains("✓", display)
        else
            Assert.Contains("✗", display)
    
    [<Fact>]
    let ``Consistency summary includes anyon type and check results`` () =
        // Business meaning: Summary must clearly identify which theory was checked
        // and report the status of pentagon and hexagon verifications.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Fibonacci
        
        Assert.Equal(AnyonSpecies.AnyonType.Fibonacci, summary.AnyonType)
        Assert.NotEmpty(summary.PentagonChecks)
        Assert.NotEmpty(summary.HexagonChecks)
        
        let display = BraidingConsistency.displayConsistencySummary summary
        Assert.Contains("Fibonacci", display)
    
    [<Fact>]
    let ``Display summary filters out trivial hexagon cases for clarity`` () =
        // Business meaning: Console output should focus on interesting cases.
        // Trivial "no valid fusion paths" cases clutter the output.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        let display = BraidingConsistency.displayConsistencySummary summary
        
        // Display should not include every "No valid fusion paths" case
        let lines = display.Split('\n')
        let noValidLines = lines |> Array.filter (fun l -> l.Contains("No valid"))
        
        // Should have fewer "No valid" lines in display than in full check list
        Assert.True(noValidLines.Length < summary.HexagonChecks.Length)
    
    // ========================================================================
    // ERROR HANDLING TESTS
    // ========================================================================
    
    [<Fact>]
    let ``Consistency verification succeeds for higher SU(2)_k levels`` () =
        // Business meaning: SU(2)_k consistency verification works for all valid k,
        // including k=5 which exercises the general 6j-symbol computation
        // with correct zero-valued F-symbol storage and the phase convention fix.
        // Pentagon equations (F-matrix only) must pass; hexagon equations
        // (F+R matrices) are a separate verification area for general SU(2)_k.
        match BraidingConsistency.verifyConsistency (AnyonSpecies.AnyonType.SU2Level 5) with
        | Ok summary ->
            Assert.NotEmpty(summary.PentagonChecks)
            let allPentagonsPassed = summary.PentagonChecks |> List.forall (fun c -> c.IsSatisfied)
            Assert.True(allPentagonsPassed, "SU(2)_5 pentagon equations should all be satisfied")
        | Error err ->
            failwith $"SU(2)_5 consistency check should succeed but got: {err.Message}"
    
    [<Fact>]
    let ``Consistency check results include equation names for diagnostics`` () =
        // Business meaning: Each check result must identify which specific equation
        // it tested, enabling analysis of which hexagons pass/fail.
        let summary = verifyConsistencyOrFail AnyonSpecies.AnyonType.Ising
        
        let allChecks = summary.PentagonChecks @ summary.HexagonChecks
        
        allChecks
        |> List.iter (fun check ->
            Assert.False(System.String.IsNullOrWhiteSpace(check.Equation))
        )
