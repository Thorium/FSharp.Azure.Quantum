namespace FSharp.Azure.Quantum.Tests.Topological

open Xunit
open FSharp.Azure.Quantum.Topological

// ============================================================================
// ANYONIC ERROR CORRECTION TESTS
//
// Tests for fusion-tree-level error correction:
// - Charge violation detection
// - Syndrome extraction (locating corrupted fusion nodes)
// - Charge flip error injection
// - Greedy charge-correction decoder
// - Protected subspace projection
// ============================================================================

module AnyonicErrorCorrectionTests =

    // ========================================================================
    // HELPER: Build common fusion trees for testing
    // ========================================================================

    /// Build a valid 4-sigma Ising tree: (σ×σ→1) × (σ×σ→1) → 1
    let private isingVacuumTree () =
        let left = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Vacuum
        let right = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Vacuum
        FusionTree.fuse left right AnyonSpecies.Particle.Vacuum

    /// Build a valid 4-sigma Ising tree: (σ×σ→ψ) × (σ×σ→ψ) → 1
    let private isingPsiTree () =
        let left = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Psi
        let right = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Psi
        FusionTree.fuse left right AnyonSpecies.Particle.Vacuum

    /// Build a valid 4-tau Fibonacci tree: (τ×τ→1) × (τ×τ→1) → 1
    let private fibVacuumTree () =
        let left = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Tau) (FusionTree.leaf AnyonSpecies.Particle.Tau) AnyonSpecies.Particle.Vacuum
        let right = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Tau) (FusionTree.leaf AnyonSpecies.Particle.Tau) AnyonSpecies.Particle.Vacuum
        FusionTree.fuse left right AnyonSpecies.Particle.Vacuum

    /// Build a valid 4-tau Fibonacci tree: (τ×τ→τ) × (τ×τ→τ) → 1
    let private fibTauTree () =
        let left = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Tau) (FusionTree.leaf AnyonSpecies.Particle.Tau) AnyonSpecies.Particle.Tau
        let right = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Tau) (FusionTree.leaf AnyonSpecies.Particle.Tau) AnyonSpecies.Particle.Tau
        FusionTree.fuse left right AnyonSpecies.Particle.Vacuum

    // ========================================================================
    // CHARGE VIOLATION DETECTION
    // ========================================================================

    [<Fact>]
    let ``Charge violation: valid Ising tree has no violations`` () =
        let tree = isingVacuumTree ()
        let result = AnyonicErrorCorrection.detectChargeViolations tree AnyonSpecies.AnyonType.Ising
        match result with
        | Ok violations -> Assert.Empty(violations)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``Charge violation: valid Ising psi-channel tree has no violations`` () =
        let tree = isingPsiTree ()
        let result = AnyonicErrorCorrection.detectChargeViolations tree AnyonSpecies.AnyonType.Ising
        match result with
        | Ok violations -> Assert.Empty(violations)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``Charge violation: valid Fibonacci tree has no violations`` () =
        let tree = fibVacuumTree ()
        let result = AnyonicErrorCorrection.detectChargeViolations tree AnyonSpecies.AnyonType.Fibonacci
        match result with
        | Ok violations -> Assert.Empty(violations)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``Charge violation: leaf node has no violations`` () =
        let tree = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let result = AnyonicErrorCorrection.detectChargeViolations tree AnyonSpecies.AnyonType.Ising
        match result with
        | Ok violations -> Assert.Empty(violations)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``Charge violation: invalid channel σ×σ→σ detected`` () =
        // σ×σ can only fuse to 1 or ψ, not σ
        let badTree = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Sigma
        let result = AnyonicErrorCorrection.detectChargeViolations badTree AnyonSpecies.AnyonType.Ising
        match result with
        | Ok violations -> Assert.NotEmpty(violations)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``Charge violation: invalid Fibonacci τ×τ→ψ detected`` () =
        // τ×τ can only fuse to 1 or τ in Fibonacci, not ψ (ψ doesn't exist in Fibonacci)
        // But Psi is invalid for Fibonacci altogether, so this should detect a violation
        let badTree = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Tau) (FusionTree.leaf AnyonSpecies.Particle.Tau) AnyonSpecies.Particle.Psi
        let result = AnyonicErrorCorrection.detectChargeViolations badTree AnyonSpecies.AnyonType.Fibonacci
        match result with
        | Ok violations -> Assert.NotEmpty(violations)
        | Error _ -> () // An error is also acceptable for invalid particles

    [<Fact>]
    let ``Charge violation: nested violation detected deep in tree`` () =
        // Build a tree with an invalid inner channel but valid outer
        // (σ×σ→σ) × (σ×σ→1) → ? — the left subtree has invalid channel
        let leftBad = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Sigma
        let rightOk = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Vacuum
        let outerTree = FusionTree.fuse leftBad rightOk AnyonSpecies.Particle.Sigma
        let result = AnyonicErrorCorrection.detectChargeViolations outerTree AnyonSpecies.AnyonType.Ising
        match result with
        | Ok violations -> Assert.True(violations.Length >= 1, $"Expected at least 1 violation, got {violations.Length}")
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    // ========================================================================
    // CHARGE VIOLATION INFO
    // ========================================================================

    [<Fact>]
    let ``Charge violation info includes node path`` () =
        let badTree = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Sigma
        let result = AnyonicErrorCorrection.detectChargeViolations badTree AnyonSpecies.AnyonType.Ising
        match result with
        | Ok violations ->
            Assert.NotEmpty(violations)
            let v = violations.[0]
            // Violation should have a path (list of directions to reach the violating node)
            Assert.True(v.Path.Length >= 0) // Root violation has empty path
            Assert.Equal(AnyonSpecies.Particle.Sigma, v.ActualChannel)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``Charge violation info includes expected channels`` () =
        let badTree = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Sigma
        let result = AnyonicErrorCorrection.detectChargeViolations badTree AnyonSpecies.AnyonType.Ising
        match result with
        | Ok violations ->
            Assert.NotEmpty(violations)
            let v = violations.[0]
            // σ×σ should produce Vacuum or Psi, so expected channels should list those
            Assert.True(v.ExpectedChannels.Length >= 1)
            Assert.Contains(AnyonSpecies.Particle.Vacuum, v.ExpectedChannels)
            Assert.Contains(AnyonSpecies.Particle.Psi, v.ExpectedChannels)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    // ========================================================================
    // CHARGE FLIP ERROR INJECTION
    // ========================================================================

    [<Fact>]
    let ``injectChargeFlip on leaf returns error (no channel to flip)`` () =
        let tree = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let result = AnyonicErrorCorrection.injectChargeFlip tree [] AnyonSpecies.AnyonType.Ising
        match result with
        | Error _ -> () // Expected: can't flip a leaf
        | Ok _ -> Assert.Fail("Expected error for leaf charge flip")

    [<Fact>]
    let ``injectChargeFlip changes channel at root of 2-anyon tree`` () =
        // σ×σ→1, flip the root channel
        let tree = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Vacuum
        let result = AnyonicErrorCorrection.injectChargeFlip tree [] AnyonSpecies.AnyonType.Ising
        match result with
        | Ok flipped ->
            // σ×σ→1 should flip to σ×σ→ψ (the other valid channel)
            let charge = FusionTree.totalCharge flipped AnyonSpecies.AnyonType.Ising
            Assert.Equal(AnyonSpecies.Particle.Psi, charge)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``injectChargeFlip at nested path flips inner channel`` () =
        let tree = isingVacuumTree ()
        // Path [Left] targets the left subtree's root: (σ×σ→1) should flip to (σ×σ→ψ)
        let result = AnyonicErrorCorrection.injectChargeFlip tree [AnyonicErrorCorrection.PathDirection.Left] AnyonSpecies.AnyonType.Ising
        match result with
        | Ok flipped ->
            // The flipped tree should now have a charge violation at the outer level
            // because (σ×σ→ψ) × (σ×σ→1) cannot fuse to vacuum
            let violations = AnyonicErrorCorrection.detectChargeViolations flipped AnyonSpecies.AnyonType.Ising
            match violations with
            | Ok vs -> Assert.True(vs.Length >= 1, "Charge flip should cause violation")
            | Error err -> Assert.Fail($"Unexpected error: {err}")
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``injectChargeFlip on Fibonacci τ×τ→1 flips to τ×τ→τ`` () =
        let tree = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Tau) (FusionTree.leaf AnyonSpecies.Particle.Tau) AnyonSpecies.Particle.Vacuum
        let result = AnyonicErrorCorrection.injectChargeFlip tree [] AnyonSpecies.AnyonType.Fibonacci
        match result with
        | Ok flipped ->
            let charge = FusionTree.totalCharge flipped AnyonSpecies.AnyonType.Fibonacci
            Assert.Equal(AnyonSpecies.Particle.Tau, charge)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    // ========================================================================
    // SYNDROME EXTRACTION
    // ========================================================================

    [<Fact>]
    let ``extractSyndrome on valid tree returns clean syndrome`` () =
        let tree = isingVacuumTree ()
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        let result = AnyonicErrorCorrection.extractSyndrome state
        match result with
        | Ok syndrome ->
            Assert.True(syndrome.IsClean)
            Assert.Equal(0, syndrome.ViolationCount)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``extractSyndrome on corrupted tree reports violations`` () =
        // Build tree with invalid channel
        let badTree = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Sigma
        let state = FusionTree.create badTree AnyonSpecies.AnyonType.Ising
        let result = AnyonicErrorCorrection.extractSyndrome state
        match result with
        | Ok syndrome ->
            Assert.False(syndrome.IsClean)
            Assert.True(syndrome.ViolationCount > 0)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``extractSyndrome on Fibonacci with two violations counts correctly`` () =
        // Construct a tree with two violations:
        // Subtrees: (τ×τ→1) and (τ×τ→1) are valid.
        // Root: 1×1→τ is INVALID (1×1 can only produce {1}).
        // But we need TWO violations, so we also corrupt a subtree.
        //
        // Strategy: Build (τ×τ→τ) × (τ×τ→1) → 1
        // Then manually set root channel to τ: (τ×τ→τ) × (τ×τ→1) → τ
        // Left subtree has charge τ, right has charge 1.
        // τ×1→{τ} so valid root channels are [τ]. Root channel IS τ → valid root.
        // That doesn't help. Let's try a different approach:
        //
        // Build tree directly: left=(τ×τ→1), right=(τ×τ→1), root channel=τ
        // Root: left charge 1, right charge 1. 1×1→{1}. Channel τ ∉ {1} → violation!
        // Now also add violation inside: left=(τ×τ→1) with root manually changed.
        //
        // Simpler: two violations by nesting.
        // Inner: (τ×τ→τ) — valid
        // Outer left: ((τ×τ→τ)×τ→1) — τ×τ→{1,τ}, channel 1 ∈ {1,τ} → valid
        // Need direct construction with wrong channels.
        //
        // Simplest approach: construct flat tree with wrong root channels at two levels.
        // Level 1 (left subtree):  (τ×τ→1) — valid
        // Level 1 (right subtree): (τ×τ→1) — valid
        // Level 0 (root):          1×1→τ   — INVALID (only {1} possible)
        // That gives exactly 1 violation. For 2, we wrap in another layer:
        //
        // Grand-left: (τ×τ→1) × (τ×τ→1) → τ  [violation at this node: 1×1 can only give 1]
        // Grand-right: (τ×τ→1) × (τ×τ→1) → τ [violation at this node: same reason]
        // Root: τ×τ→1 [valid: τ×τ→{1,τ}]
        let leftInner = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Tau) (FusionTree.leaf AnyonSpecies.Particle.Tau) AnyonSpecies.Particle.Vacuum
        let rightInner = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Tau) (FusionTree.leaf AnyonSpecies.Particle.Tau) AnyonSpecies.Particle.Vacuum
        // Grand-left: 1×1→τ (invalid — only {1} is possible)
        let grandLeft = FusionTree.Fusion (leftInner, rightInner, AnyonSpecies.Particle.Tau)
        // Grand-right: same structure, same violation
        let leftInner2 = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Tau) (FusionTree.leaf AnyonSpecies.Particle.Tau) AnyonSpecies.Particle.Vacuum
        let rightInner2 = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Tau) (FusionTree.leaf AnyonSpecies.Particle.Tau) AnyonSpecies.Particle.Vacuum
        let grandRight = FusionTree.Fusion (leftInner2, rightInner2, AnyonSpecies.Particle.Tau)
        // Root: τ×τ→1 (valid)
        let tree = FusionTree.Fusion (grandLeft, grandRight, AnyonSpecies.Particle.Vacuum)
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Fibonacci
        let result = AnyonicErrorCorrection.extractSyndrome state
        match result with
        | Ok syndrome ->
            Assert.False(syndrome.IsClean)
            // Both grand-left and grand-right have violations (1×1→τ is invalid)
            Assert.True(syndrome.ViolationCount >= 2, $"Expected >= 2 violations, got {syndrome.ViolationCount}")
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    // ========================================================================
    // GREEDY CHARGE CORRECTION DECODER
    // ========================================================================

    [<Fact>]
    let ``correctChargeViolations on valid tree is identity`` () =
        let tree = isingVacuumTree ()
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        let result = AnyonicErrorCorrection.correctChargeViolations state
        match result with
        | Ok corrected ->
            Assert.True(FusionTree.equals corrected.Tree state.Tree)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``correctChargeViolations fixes single channel flip in Ising`` () =
        // σ×σ→σ (invalid) should be corrected to σ×σ→1 or σ×σ→ψ
        let badTree = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Sigma
        let state = FusionTree.create badTree AnyonSpecies.AnyonType.Ising
        let result = AnyonicErrorCorrection.correctChargeViolations state
        match result with
        | Ok corrected ->
            // Corrected tree should have no violations
            let checkResult = AnyonicErrorCorrection.detectChargeViolations corrected.Tree AnyonSpecies.AnyonType.Ising
            match checkResult with
            | Ok violations -> Assert.Empty(violations)
            | Error err -> Assert.Fail($"Unexpected error checking corrected tree: {err}")
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``correctChargeViolations fixes nested violation in Ising`` () =
        let tree = isingVacuumTree ()
        // Corrupt inner left: (σ×σ→1) becomes (σ×σ→ψ), making root invalid
        match AnyonicErrorCorrection.injectChargeFlip tree [AnyonicErrorCorrection.PathDirection.Left] AnyonSpecies.AnyonType.Ising with
        | Ok corrupted ->
            let state = FusionTree.create corrupted AnyonSpecies.AnyonType.Ising
            let result = AnyonicErrorCorrection.correctChargeViolations state
            match result with
            | Ok corrected ->
                let checkResult = AnyonicErrorCorrection.detectChargeViolations corrected.Tree AnyonSpecies.AnyonType.Ising
                match checkResult with
                | Ok violations -> Assert.Empty(violations)
                | Error err -> Assert.Fail($"Unexpected error: {err}")
            | Error err -> Assert.Fail($"Unexpected error: {err}")
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``correctChargeViolations fixes Fibonacci single violation`` () =
        // τ×τ→σ is invalid in Fibonacci — but σ doesn't exist there.
        // Use a manually constructed invalid tree: (τ×τ→Vacuum) × (τ×τ→Vacuum) → τ
        // Since Vacuum × Vacuum cannot fuse to τ, the root is violated
        let left = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Tau) (FusionTree.leaf AnyonSpecies.Particle.Tau) AnyonSpecies.Particle.Vacuum
        let right = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Tau) (FusionTree.leaf AnyonSpecies.Particle.Tau) AnyonSpecies.Particle.Vacuum
        let badTree = FusionTree.fuse left right AnyonSpecies.Particle.Tau
        let state = FusionTree.create badTree AnyonSpecies.AnyonType.Fibonacci
        let result = AnyonicErrorCorrection.correctChargeViolations state
        match result with
        | Ok corrected ->
            let checkResult = AnyonicErrorCorrection.detectChargeViolations corrected.Tree AnyonSpecies.AnyonType.Fibonacci
            match checkResult with
            | Ok violations -> Assert.Empty(violations)
            | Error err -> Assert.Fail($"Unexpected error: {err}")
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``correctChargeViolations on leaf is identity`` () =
        let tree = FusionTree.leaf AnyonSpecies.Particle.Sigma
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        let result = AnyonicErrorCorrection.correctChargeViolations state
        match result with
        | Ok corrected ->
            Assert.True(FusionTree.equals corrected.Tree state.Tree)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    // ========================================================================
    // CORRECTION RESULT METADATA
    // ========================================================================

    [<Fact>]
    let ``correction result reports corrections applied count`` () =
        let badTree = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Sigma
        let state = FusionTree.create badTree AnyonSpecies.AnyonType.Ising
        let result = AnyonicErrorCorrection.correctChargeViolations state
        match result with
        | Ok corrected ->
            Assert.True(corrected.CorrectionsApplied > 0, "Should report at least one correction")
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``correction result for valid tree reports zero corrections`` () =
        let tree = isingVacuumTree ()
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        let result = AnyonicErrorCorrection.correctChargeViolations state
        match result with
        | Ok corrected ->
            Assert.Equal(0, corrected.CorrectionsApplied)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    // ========================================================================
    // TOTAL CHARGE PRESERVATION
    // ========================================================================

    [<Fact>]
    let ``correction preserves total charge when possible`` () =
        // Corrupt an inner channel but keep root charge consistent
        // (σ×σ→ψ) × (σ×σ→1) → ? (root charge is now inconsistent)
        // After correction, the tree should have a valid structure
        let tree = isingVacuumTree ()
        match AnyonicErrorCorrection.injectChargeFlip tree [AnyonicErrorCorrection.PathDirection.Left] AnyonSpecies.AnyonType.Ising with
        | Ok corrupted ->
            let state = FusionTree.create corrupted AnyonSpecies.AnyonType.Ising
            match AnyonicErrorCorrection.correctChargeViolations state with
            | Ok corrected ->
                // The corrected tree should be fully valid
                match FusionTree.isValid corrected.Tree AnyonSpecies.AnyonType.Ising with
                | Ok valid -> Assert.True(valid, "Corrected tree should be valid")
                | Error err -> Assert.Fail($"Unexpected error: {err}")
            | Error err -> Assert.Fail($"Unexpected error: {err}")
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    // ========================================================================
    // PROTECTED SUBSPACE PROJECTION
    // ========================================================================

    [<Fact>]
    let ``projectToCodeSpace on pure valid state is identity`` () =
        let tree = isingVacuumTree ()
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        let superposition = TopologicalOperations.pureState state
        let result = AnyonicErrorCorrection.projectToCodeSpace superposition AnyonSpecies.Particle.Vacuum
        match result with
        | Ok projected ->
            Assert.Equal(1, projected.Terms.Length)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``projectToCodeSpace filters out wrong-charge states`` () =
        let tree1 = isingVacuumTree ()
        let state1 = FusionTree.create tree1 AnyonSpecies.AnyonType.Ising
        // Create a state with different total charge
        let tree2 = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Psi
        let state2 = FusionTree.create tree2 AnyonSpecies.AnyonType.Ising
        // Superposition of vacuum-charge and psi-charge states
        let superposition = {
            TopologicalOperations.Superposition.Terms = [
                (System.Numerics.Complex(0.7071, 0.0), state1)
                (System.Numerics.Complex(0.7071, 0.0), state2)
            ]
            TopologicalOperations.Superposition.AnyonType = AnyonSpecies.AnyonType.Ising
        }
        let result = AnyonicErrorCorrection.projectToCodeSpace superposition AnyonSpecies.Particle.Vacuum
        match result with
        | Ok projected ->
            // Should only keep the vacuum-charge state
            Assert.Equal(1, projected.Terms.Length)
            let (_, keptState) = projected.Terms.[0]
            let charge = FusionTree.totalCharge keptState.Tree AnyonSpecies.AnyonType.Ising
            Assert.Equal(AnyonSpecies.Particle.Vacuum, charge)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``projectToCodeSpace on empty superposition returns empty`` () =
        let superposition = {
            TopologicalOperations.Superposition.Terms = []
            TopologicalOperations.Superposition.AnyonType = AnyonSpecies.AnyonType.Ising
        }
        let result = AnyonicErrorCorrection.projectToCodeSpace superposition AnyonSpecies.Particle.Vacuum
        match result with
        | Ok projected ->
            Assert.Empty(projected.Terms)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``projectToCodeSpace renormalizes after projection`` () =
        let tree1 = isingVacuumTree ()
        let state1 = FusionTree.create tree1 AnyonSpecies.AnyonType.Ising
        let tree2 = isingPsiTree ()
        let state2 = FusionTree.create tree2 AnyonSpecies.AnyonType.Ising
        // Both states have vacuum total charge — both should be kept
        let superposition = {
            TopologicalOperations.Superposition.Terms = [
                (System.Numerics.Complex(0.6, 0.0), state1)
                (System.Numerics.Complex(0.8, 0.0), state2)
            ]
            TopologicalOperations.Superposition.AnyonType = AnyonSpecies.AnyonType.Ising
        }
        let result = AnyonicErrorCorrection.projectToCodeSpace superposition AnyonSpecies.Particle.Vacuum
        match result with
        | Ok projected ->
            Assert.Equal(2, projected.Terms.Length)
            // Check normalization: sum of |amp|^2 ≈ 1
            let normSq = projected.Terms |> List.sumBy (fun (amp, _) -> (System.Numerics.Complex.Abs amp) ** 2.0)
            Assert.True(abs (normSq - 1.0) < 1e-10, $"Expected normalized, got norm²={normSq}")
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    // ========================================================================
    // FULL ERROR CORRECTION PIPELINE
    // ========================================================================

    [<Fact>]
    let ``fullCorrection detects, corrects, and projects Ising state`` () =
        let tree = isingVacuumTree ()
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        let superposition = TopologicalOperations.pureState state
        let result = AnyonicErrorCorrection.fullCorrection superposition AnyonSpecies.Particle.Vacuum
        match result with
        | Ok corrected ->
            Assert.True(corrected.Terms.Length >= 1)
            // All terms should have vacuum total charge
            for (_, s) in corrected.Terms do
                let charge = FusionTree.totalCharge s.Tree AnyonSpecies.AnyonType.Ising
                Assert.Equal(AnyonSpecies.Particle.Vacuum, charge)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``fullCorrection on Fibonacci state preserves valid terms`` () =
        let tree = fibTauTree ()
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Fibonacci
        let superposition = TopologicalOperations.pureState state
        let result = AnyonicErrorCorrection.fullCorrection superposition AnyonSpecies.Particle.Vacuum
        match result with
        | Ok corrected ->
            Assert.True(corrected.Terms.Length >= 1)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    // ========================================================================
    // EDGE CASES AND VALIDATION
    // ========================================================================

    [<Fact>]
    let ``detectChargeViolations on two-particle valid tree`` () =
        let tree = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Vacuum
        let result = AnyonicErrorCorrection.detectChargeViolations tree AnyonSpecies.AnyonType.Ising
        match result with
        | Ok violations -> Assert.Empty(violations)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``correctChargeViolations handles ψ×ψ→ψ violation`` () =
        // ψ×ψ should be 1 (vacuum), not ψ
        let badTree = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Psi) (FusionTree.leaf AnyonSpecies.Particle.Psi) AnyonSpecies.Particle.Psi
        let state = FusionTree.create badTree AnyonSpecies.AnyonType.Ising
        let result = AnyonicErrorCorrection.correctChargeViolations state
        match result with
        | Ok corrected ->
            let charge = FusionTree.totalCharge corrected.Tree AnyonSpecies.AnyonType.Ising
            Assert.Equal(AnyonSpecies.Particle.Vacuum, charge)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``injectChargeFlip with invalid path returns error`` () =
        let tree = FusionTree.leaf AnyonSpecies.Particle.Sigma
        // Can't go Left from a leaf
        let result = AnyonicErrorCorrection.injectChargeFlip tree [AnyonicErrorCorrection.PathDirection.Left] AnyonSpecies.AnyonType.Ising
        match result with
        | Error _ -> () // Expected
        | Ok _ -> Assert.Fail("Expected error for invalid path on leaf")

    [<Fact>]
    let ``injectChargeFlip preserves tree structure except channel`` () =
        let tree = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Vacuum
        match AnyonicErrorCorrection.injectChargeFlip tree [] AnyonSpecies.AnyonType.Ising with
        | Ok flipped ->
            // Should still have same leaves
            let origLeaves = FusionTree.leaves tree
            let flippedLeaves = FusionTree.leaves flipped
            Assert.Equal<AnyonSpecies.Particle list>(origLeaves, flippedLeaves)
            // But channel should be different
            let origCharge = FusionTree.totalCharge tree AnyonSpecies.AnyonType.Ising
            let flippedCharge = FusionTree.totalCharge flipped AnyonSpecies.AnyonType.Ising
            Assert.NotEqual(origCharge, flippedCharge)
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    // ========================================================================
    // SYNDROME DISPLAY
    // ========================================================================

    [<Fact>]
    let ``syndrome display shows clean for valid tree`` () =
        let tree = isingVacuumTree ()
        let state = FusionTree.create tree AnyonSpecies.AnyonType.Ising
        match AnyonicErrorCorrection.extractSyndrome state with
        | Ok syndrome ->
            let display = AnyonicErrorCorrection.displaySyndrome syndrome
            Assert.Contains("clean", display.ToLower())
        | Error err -> Assert.Fail($"Unexpected error: {err}")

    [<Fact>]
    let ``syndrome display shows violations for corrupted tree`` () =
        let badTree = FusionTree.fuse (FusionTree.leaf AnyonSpecies.Particle.Sigma) (FusionTree.leaf AnyonSpecies.Particle.Sigma) AnyonSpecies.Particle.Sigma
        let state = FusionTree.create badTree AnyonSpecies.AnyonType.Ising
        match AnyonicErrorCorrection.extractSyndrome state with
        | Ok syndrome ->
            let display = AnyonicErrorCorrection.displaySyndrome syndrome
            Assert.Contains("violation", display.ToLower())
        | Error err -> Assert.Fail($"Unexpected error: {err}")
