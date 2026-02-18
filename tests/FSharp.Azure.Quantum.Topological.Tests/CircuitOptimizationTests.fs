namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Topological.SolovayKitaev
open FSharp.Azure.Quantum.Topological.CircuitOptimization

module CircuitOptimizationTests =

    // ========================================================================
    // GATE COMMUTATION TESTS
    // ========================================================================

    [<Fact>]
    let ``Z-axis gates commute with each other`` () =
        // All Z-axis gates: T, TDagger, S, SDagger, Z
        let zGates = [ T; TDagger; S; SDagger; Z ]
        for g1 in zGates do
            for g2 in zGates do
                Assert.True(commutes g1 g2, $"{g1} and {g2} should commute (both Z-axis)")

    [<Fact>]
    let ``Identity commutes with everything`` () =
        let allGates = [ T; TDagger; S; SDagger; H; X; Y; Z; I ]
        for g in allGates do
            Assert.True(commutes I g, $"I should commute with {g}")
            Assert.True(commutes g I, $"{g} should commute with I")

    [<Fact>]
    let ``X and Y do not commute with Z-axis gates`` () =
        let zGates = [ T; TDagger; S; SDagger; Z ]
        for zg in zGates do
            Assert.False(commutes X zg, $"X should not commute with {zg}")
            Assert.False(commutes Y zg, $"Y should not commute with {zg}")
            Assert.False(commutes zg X, $"{zg} should not commute with X")
            Assert.False(commutes zg Y, $"{zg} should not commute with Y")

    [<Fact>]
    let ``H does not commute with Z-axis gates`` () =
        let zGates = [ T; TDagger; S; SDagger; Z ]
        for zg in zGates do
            Assert.False(commutes H zg, $"H should not commute with {zg}")
            Assert.False(commutes zg H, $"{zg} should not commute with H")

    [<Fact>]
    let ``X commutes with X and Y commutes with Y`` () =
        Assert.True(commutes X X)
        Assert.True(commutes Y Y)

    [<Fact>]
    let ``X does not commute with Y`` () =
        Assert.False(commutes X Y)
        Assert.False(commutes Y X)

    [<Fact>]
    let ``H does not commute with H`` () =
        // H is not a Z-axis gate; default case returns false
        Assert.False(commutes H H)

    // ========================================================================
    // CLIFFORD AND T-GATE CLASSIFICATION TESTS
    // ========================================================================

    [<Fact>]
    let ``Clifford gates are correctly identified`` () =
        let cliffords = [ H; S; SDagger; X; Y; Z; I ]
        for g in cliffords do
            Assert.True(isClifford g, $"{g} should be Clifford")

    [<Fact>]
    let ``T and TDagger are not Clifford`` () =
        Assert.False(isClifford T)
        Assert.False(isClifford TDagger)

    [<Fact>]
    let ``T gates are correctly identified`` () =
        Assert.True(isTGate T)
        Assert.True(isTGate TDagger)

    [<Fact>]
    let ``Non-T gates are not T gates`` () =
        let nonT = [ H; S; SDagger; X; Y; Z; I ]
        for g in nonT do
            Assert.False(isTGate g, $"{g} should not be a T gate")

    // ========================================================================
    // GATE CANCELLATION TESTS
    // ========================================================================

    [<Fact>]
    let ``T followed by TDagger cancels`` () =
        let result = cancelInverses [ T; TDagger ]
        Assert.Empty(result)

    [<Fact>]
    let ``TDagger followed by T cancels`` () =
        let result = cancelInverses [ TDagger; T ]
        Assert.Empty(result)

    [<Fact>]
    let ``S followed by SDagger cancels`` () =
        let result = cancelInverses [ S; SDagger ]
        Assert.Empty(result)

    [<Fact>]
    let ``SDagger followed by S cancels`` () =
        let result = cancelInverses [ SDagger; S ]
        Assert.Empty(result)

    [<Fact>]
    let ``H followed by H cancels`` () =
        let result = cancelInverses [ H; H ]
        Assert.Empty(result)

    [<Fact>]
    let ``X followed by X cancels`` () =
        let result = cancelInverses [ X; X ]
        Assert.Empty(result)

    [<Fact>]
    let ``Y followed by Y cancels`` () =
        let result = cancelInverses [ Y; Y ]
        Assert.Empty(result)

    [<Fact>]
    let ``Z followed by Z cancels`` () =
        let result = cancelInverses [ Z; Z ]
        Assert.Empty(result)

    [<Fact>]
    let ``Identity gates are removed`` () =
        let result = cancelInverses [ T; I; S; I; H ]
        Assert.Equal<BasicGate list>([ T; S; H ], result)

    [<Fact>]
    let ``Non-inverse adjacent gates are preserved`` () =
        let result = cancelInverses [ T; S; H ]
        Assert.Equal<BasicGate list>([ T; S; H ], result)

    [<Fact>]
    let ``Cancel inverses on empty list`` () =
        let result = cancelInverses []
        Assert.Empty(result)

    [<Fact>]
    let ``Cancel inverses on single gate`` () =
        let result = cancelInverses [ T ]
        Assert.Equal<BasicGate list>([ T ], result)

    [<Fact>]
    let ``Nested cancellations cascade`` () =
        // T TDagger S SDagger -> after cancelling T/TDagger, rest is S SDagger which also cancels
        let result = cancelInverses [ T; TDagger; S; SDagger ]
        Assert.Empty(result)

    // ========================================================================
    // GATE MERGING TESTS
    // ========================================================================

    [<Fact>]
    let ``T T merges to S`` () =
        let result = mergeAdjacentGates [ T; T ]
        Assert.Equal<BasicGate list>([ S ], result)

    [<Fact>]
    let ``TDagger TDagger merges to SDagger`` () =
        let result = mergeAdjacentGates [ TDagger; TDagger ]
        Assert.Equal<BasicGate list>([ SDagger ], result)

    [<Fact>]
    let ``Z Z merges to nothing (identity)`` () =
        let result = mergeAdjacentGates [ Z; Z ]
        Assert.Empty(result)

    [<Fact>]
    let ``Merge preserves non-mergeable gates`` () =
        let result = mergeAdjacentGates [ H; X; Y ]
        Assert.Equal<BasicGate list>([ H; X; Y ], result)

    [<Fact>]
    let ``Merge on empty list`` () =
        let result = mergeAdjacentGates []
        Assert.Empty(result)

    [<Fact>]
    let ``Four T gates merge to two S gates`` () =
        // T T T T -> S T T -> S S
        let result = mergeAdjacentGates [ T; T; T; T ]
        Assert.Equal<BasicGate list>([ S; S ], result)

    // ========================================================================
    // COMMUTATION-BASED OPTIMIZATION TESTS
    // ========================================================================

    [<Fact>]
    let ``Commute Cliffords left swaps non-Clifford with commuting Clifford`` () =
        // T commutes with Z (both Z-axis), and Z is Clifford while T is not
        // So T :: Z should become Z :: T
        let (result, changed) = commuteCliffordsLeft [ T; Z ]
        Assert.True(changed)
        Assert.Equal<BasicGate list>([ Z; T ], result)

    [<Fact>]
    let ``Commute Cliffords left does not swap non-commuting gates`` () =
        // T does not commute with H
        let (result, changed) = commuteCliffordsLeft [ T; H ]
        Assert.False(changed)
        Assert.Equal<BasicGate list>([ T; H ], result)

    [<Fact>]
    let ``Commute Cliffords left on empty list`` () =
        let (result, changed) = commuteCliffordsLeft []
        Assert.False(changed)
        Assert.Empty(result)

    [<Fact>]
    let ``Commute Cliffords left on single gate`` () =
        let (result, changed) = commuteCliffordsLeft [ T ]
        Assert.False(changed)
        Assert.Equal<BasicGate list>([ T ], result)

    [<Fact>]
    let ``CommuteCliffordsUntilStable moves all commuting Cliffords left`` () =
        // T Z S -> should move Z and S left of T since all Z-axis gates commute
        let result = commuteCliffordsUntilStable [ T; Z; S ]
        // Z and S should be before T
        let tIndex = result |> List.findIndex (fun g -> g = T)
        let zIndex = result |> List.findIndex (fun g -> g = Z)
        let sIndex = result |> List.findIndex (fun g -> g = S)
        Assert.True(zIndex < tIndex, "Z should be before T")
        Assert.True(sIndex < tIndex, "S should be before T")

    [<Fact>]
    let ``CommuteCliffordsUntilStable preserves sequence when no commutation possible`` () =
        let result = commuteCliffordsUntilStable [ H; X; Y ]
        Assert.Equal<BasicGate list>([ H; X; Y ], result)

    // ========================================================================
    // TEMPLATE MATCHING TESTS
    // ========================================================================

    [<Fact>]
    let ``Template match T^7 reduces to TDagger`` () =
        let gates = List.replicate 7 T
        let (result, changed) = templateMatch gates
        Assert.True(changed)
        Assert.Equal<BasicGate list>([ TDagger ], result)

    [<Fact>]
    let ``Template match T^3 reduces to S T`` () =
        let gates = List.replicate 3 T
        let (result, changed) = templateMatch gates
        Assert.True(changed)
        Assert.Equal<BasicGate list>([ S; T ], result)

    [<Fact>]
    let ``Template match S^3 reduces to SDagger`` () =
        let gates = [ S; S; S ]
        let (result, changed) = templateMatch gates
        Assert.True(changed)
        Assert.Equal<BasicGate list>([ SDagger ], result)

    [<Fact>]
    let ``Template match does not change short sequences`` () =
        let (result, changed) = templateMatch [ T; T ]
        Assert.False(changed)
        Assert.Equal<BasicGate list>([ T; T ], result)

    [<Fact>]
    let ``Template match does not change non-matching sequences`` () =
        let (result, changed) = templateMatch [ H; X; Y ]
        Assert.False(changed)
        Assert.Equal<BasicGate list>([ H; X; Y ], result)

    [<Fact>]
    let ``Template match empty list`` () =
        let (result, changed) = templateMatch []
        Assert.False(changed)
        Assert.Empty(result)

    [<Fact>]
    let ``Template match single gate`` () =
        let (result, changed) = templateMatch [ T ]
        Assert.False(changed)
        Assert.Equal<BasicGate list>([ T ], result)

    [<Fact>]
    let ``TemplateMatchUntilStable fully reduces T^7`` () =
        let result = templateMatchUntilStable (List.replicate 7 T)
        Assert.Equal<BasicGate list>([ TDagger ], result)

    [<Fact>]
    let ``TemplateMatchUntilStable reduces T^8 to TDagger T`` () =
        // T^8 -> T^7 T -> (TDagger) T -> then TDagger T is NOT cancelled by templateMatch
        // templateMatch only matches T^7, T^5, T^3, S^3 patterns
        // T^8 = T^7 :: T -> TDagger :: T
        let result = templateMatchUntilStable (List.replicate 8 T)
        Assert.Equal<BasicGate list>([ TDagger; T ], result)

    // ========================================================================
    // COUNTING AND DEPTH TESTS
    // ========================================================================

    [<Fact>]
    let ``countTGates counts T and TDagger`` () =
        let gates = [ T; S; TDagger; H; T; Z; TDagger ]
        Assert.Equal(4, countTGates gates)

    [<Fact>]
    let ``countTGates on empty list is zero`` () =
        Assert.Equal(0, countTGates [])

    [<Fact>]
    let ``countTGates on Clifford-only is zero`` () =
        Assert.Equal(0, countTGates [ H; S; X; Z; I ])

    [<Fact>]
    let ``calculateDepth equals gate count`` () =
        let gates = [ T; S; H; X ]
        Assert.Equal(4, calculateDepth gates)

    [<Fact>]
    let ``calculateDepth of empty is zero`` () =
        Assert.Equal(0, calculateDepth [])

    // ========================================================================
    // OPTIMIZATION PIPELINE TESTS
    // ========================================================================

    [<Fact>]
    let ``optimizeBasic cancels T TDagger`` () =
        let result = optimizeBasic [ T; TDagger ]
        Assert.Empty(result)

    [<Fact>]
    let ``optimizeBasic merges T T to S`` () =
        let result = optimizeBasic [ T; T ]
        Assert.Equal<BasicGate list>([ S ], result)

    [<Fact>]
    let ``optimizeBasic cancels after merge`` () =
        // T T SDagger -> S SDagger -> cancel -> empty
        let result = optimizeBasic [ T; T; SDagger ]
        Assert.Empty(result)

    [<Fact>]
    let ``optimizeAggressive applies all techniques`` () =
        // Create a sequence that benefits from commutation + template matching
        let gates = [ T; T; T; T; T; T; T ]  // T^7
        let result = optimizeAggressive gates
        // Should reduce T-count significantly
        Assert.True(countTGates result < countTGates gates,
            $"T-count should decrease: original={countTGates gates}, optimized={countTGates result}")

    [<Fact>]
    let ``optimizeAggressive handles empty list`` () =
        let result = optimizeAggressive []
        Assert.Empty(result)

    // ========================================================================
    // OPTIMIZE WITH STATS TESTS
    // ========================================================================

    [<Fact>]
    let ``optimize level 0 returns unchanged gates`` () =
        let gates = [ T; TDagger; S; H ]
        let (result, stats) = optimize gates 0
        Assert.Equal<BasicGate list>(gates, result)
        Assert.Equal(4, stats.OriginalGateCount)
        Assert.Equal(4, stats.OptimizedGateCount)
        Assert.Empty(stats.OptimizationsApplied)

    [<Fact>]
    let ``optimize level 1 applies basic optimizations`` () =
        let gates = [ T; TDagger; H ]
        let (result, stats) = optimize gates 1
        // T TDagger should cancel
        Assert.Equal<BasicGate list>([ H ], result)
        Assert.Equal(3, stats.OriginalGateCount)
        Assert.Equal(1, stats.OptimizedGateCount)
        Assert.Equal(2, stats.OriginalTCount)
        Assert.Equal(0, stats.OptimizedTCount)
        Assert.Contains("Gate cancellation", stats.OptimizationsApplied)

    [<Fact>]
    let ``optimize level 2 applies aggressive optimizations`` () =
        let gates = List.replicate 7 T @ [ H ]
        let (result, stats) = optimize gates 2
        Assert.True(stats.OptimizedTCount < stats.OriginalTCount,
            $"T-count should decrease: {stats.OriginalTCount} -> {stats.OptimizedTCount}")
        Assert.Contains("Template matching", stats.OptimizationsApplied)
        Assert.Contains("Commutation-based reordering", stats.OptimizationsApplied)

    [<Fact>]
    let ``optimize stats record gate counts correctly`` () =
        let gates = [ T; T; T; S; H ]
        let (_, stats) = optimize gates 1
        Assert.Equal(5, stats.OriginalGateCount)
        Assert.Equal(3, stats.OriginalTCount)
        Assert.Equal(5, stats.OriginalDepth)

    // ========================================================================
    // DISPLAY STATS TESTS
    // ========================================================================

    [<Fact>]
    let ``displayStats handles zero original counts without division by zero`` () =
        let stats = {
            OriginalGateCount = 0
            OptimizedGateCount = 0
            OriginalTCount = 0
            OptimizedTCount = 0
            OriginalDepth = 0
            OptimizedDepth = 0
            OptimizationsApplied = []
        }
        let output = displayStats stats
        Assert.Contains("0.0%", output)

    [<Fact>]
    let ``displayStats shows reduction percentages`` () =
        let stats = {
            OriginalGateCount = 10
            OptimizedGateCount = 5
            OriginalTCount = 6
            OptimizedTCount = 2
            OriginalDepth = 10
            OptimizedDepth = 5
            OptimizationsApplied = [ "Gate cancellation"; "Template matching" ]
        }
        let output = displayStats stats
        Assert.Contains("50.0%", output)
        Assert.Contains("Gate cancellation", output)
        Assert.Contains("Template matching", output)

    [<Fact>]
    let ``displayStats contains section headers`` () =
        let stats = {
            OriginalGateCount = 5
            OptimizedGateCount = 3
            OriginalTCount = 2
            OptimizedTCount = 1
            OriginalDepth = 5
            OptimizedDepth = 3
            OptimizationsApplied = [ "Gate cancellation" ]
        }
        let output = displayStats stats
        Assert.Contains("Circuit Optimization Results", output)
        Assert.Contains("Reductions:", output)
        Assert.Contains("Optimizations applied:", output)

    // ========================================================================
    // INTEGRATION / END-TO-END TESTS
    // ========================================================================

    [<Fact>]
    let ``Full pipeline reduces realistic circuit`` () =
        // Simulate a circuit that came from Solovay-Kitaev decomposition
        // Contains many T gates, some inverses, and identity gates
        let gates = [ T; T; T; I; T; TDagger; H; T; T; I; S; SDagger; T ]
        let (result, stats) = optimize gates 2
        // Should remove identities, cancel inverses, and template-match
        Assert.True(result.Length < gates.Length,
            $"Gate count should decrease: {gates.Length} -> {result.Length}")
        Assert.True(stats.OptimizedTCount <= stats.OriginalTCount)

    [<Fact>]
    let ``Optimization preserves Clifford-only circuits`` () =
        let gates = [ H; S; Z; X ]
        let (result, stats) = optimize gates 2
        Assert.Equal(0, stats.OriginalTCount)
        Assert.Equal(0, stats.OptimizedTCount)
