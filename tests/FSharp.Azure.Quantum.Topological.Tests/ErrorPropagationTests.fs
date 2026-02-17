namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Topological.SolovayKitaev
open FSharp.Azure.Quantum.Topological.ErrorPropagation

module ErrorPropagationTests =

    // ========================================================================
    // ERROR MODEL CALCULATION TESTS
    // ========================================================================

    [<Fact>]
    let ``Additive model sums errors`` () =
        let errors = [ 0.001; 0.002; 0.003 ]
        let total = calculateTotalError errors Additive
        Assert.Equal(0.006, total, 10)

    [<Fact>]
    let ``Quadratic model computes sqrt of sum of squares`` () =
        let errors = [ 3.0; 4.0 ]
        let total = calculateTotalError errors Quadratic
        // sqrt(9 + 16) = sqrt(25) = 5
        Assert.Equal(5.0, total, 10)

    [<Fact>]
    let ``DiamondNorm model same as additive`` () =
        let errors = [ 0.001; 0.002; 0.003 ]
        let totalDiamond = calculateTotalError errors DiamondNorm
        let totalAdditive = calculateTotalError errors Additive
        Assert.Equal(totalAdditive, totalDiamond, 10)

    [<Fact>]
    let ``All error models return zero for empty list`` () =
        Assert.Equal(0.0, calculateTotalError [] Additive, 10)
        Assert.Equal(0.0, calculateTotalError [] Quadratic, 10)
        Assert.Equal(0.0, calculateTotalError [] DiamondNorm, 10)

    [<Fact>]
    let ``All error models return zero for all-zero errors`` () =
        let zeros = [ 0.0; 0.0; 0.0 ]
        Assert.Equal(0.0, calculateTotalError zeros Additive, 10)
        Assert.Equal(0.0, calculateTotalError zeros Quadratic, 10)
        Assert.Equal(0.0, calculateTotalError zeros DiamondNorm, 10)

    [<Fact>]
    let ``Quadratic model is always less than or equal to additive for multiple errors`` () =
        let errors = [ 0.001; 0.002; 0.003; 0.004 ]
        let additive = calculateTotalError errors Additive
        let quadratic = calculateTotalError errors Quadratic
        Assert.True(quadratic <= additive,
            $"Quadratic ({quadratic}) should be <= Additive ({additive})")

    // ========================================================================
    // GATE ERROR TESTS
    // ========================================================================

    [<Fact>]
    let ``Exact gates have zero error`` () =
        let exactGates = [ T; TDagger; S; SDagger; Z; I ]
        for g in exactGates do
            let (error, source) = getGateError g
            Assert.Equal(0.0, error)
            Assert.Contains("Exact", source)

    [<Fact>]
    let ``Approximate gates have non-zero error`` () =
        let approxGates = [ H; X; Y ]
        for g in approxGates do
            let (error, source) = getGateError g
            Assert.Equal(1e-5, error)
            Assert.Contains("Solovay-Kitaev", source)

    // ========================================================================
    // ERROR TRACKING TESTS
    // ========================================================================

    [<Fact>]
    let ``trackErrors records position-indexed gate errors`` () =
        let gates = [ T; H; S; X ]
        let acc = trackErrors gates Additive
        Assert.Equal(4, acc.GateErrors.Length)
        Assert.Equal(0, acc.GateErrors.[0].Position)
        Assert.Equal(1, acc.GateErrors.[1].Position)
        Assert.Equal(2, acc.GateErrors.[2].Position)
        Assert.Equal(3, acc.GateErrors.[3].Position)

    [<Fact>]
    let ``trackErrors counts exact and approximate gates`` () =
        let gates = [ T; H; S; X; Z; Y ]
        let acc = trackErrors gates Additive
        Assert.Equal(3, acc.ExactGateCount)       // T, S, Z
        Assert.Equal(3, acc.ApproximateGateCount)  // H, X, Y

    [<Fact>]
    let ``trackErrors computes total error with specified model`` () =
        let gates = [ H; X ]  // Both have error 1e-5
        let accAdditive = trackErrors gates Additive
        Assert.Equal(2e-5, accAdditive.TotalError, 10)

        let accQuadratic = trackErrors gates Quadratic
        // sqrt(1e-10 + 1e-10) = sqrt(2e-10) = 1e-5 * sqrt(2)
        Assert.Equal(1e-5 * sqrt 2.0, accQuadratic.TotalError, 10)

    [<Fact>]
    let ``trackErrors records max single error`` () =
        let gates = [ T; H; S ]  // T=0, H=1e-5, S=0
        let acc = trackErrors gates Additive
        Assert.Equal(1e-5, acc.MaxSingleError, 10)

    [<Fact>]
    let ``trackErrors on exact-only circuit has zero total error`` () =
        let gates = [ T; S; Z; I; TDagger; SDagger ]
        let acc = trackErrors gates Additive
        Assert.Equal(0.0, acc.TotalError, 10)
        Assert.Equal(0.0, acc.MaxSingleError, 10)
        Assert.Equal(6, acc.ExactGateCount)
        Assert.Equal(0, acc.ApproximateGateCount)

    [<Fact>]
    let ``trackErrors on empty gate list`` () =
        let acc = trackErrors [] Additive
        Assert.Equal(0.0, acc.TotalError, 10)
        Assert.Equal(0.0, acc.MaxSingleError, 10)
        Assert.Equal(0, acc.ExactGateCount)
        Assert.Equal(0, acc.ApproximateGateCount)
        Assert.Empty(acc.GateErrors)

    [<Fact>]
    let ``trackErrors records correct model type`` () =
        let acc = trackErrors [ T ] Quadratic
        Assert.Equal(Quadratic, acc.Model)

    // ========================================================================
    // ERROR BUDGET PRESET TESTS
    // ========================================================================

    [<Fact>]
    let ``defaultBudget has correct values`` () =
        Assert.Equal(1e-3, defaultBudget.MaxTotalError, 10)
        Assert.Equal(1e-5, defaultBudget.MaxSingleGateError, 10)
        Assert.Equal(Quadratic, defaultBudget.Model)

    [<Fact>]
    let ``strictBudget has correct values`` () =
        Assert.Equal(1e-6, strictBudget.MaxTotalError, 10)
        Assert.Equal(1e-8, strictBudget.MaxSingleGateError, 10)
        Assert.Equal(DiamondNorm, strictBudget.Model)

    [<Fact>]
    let ``relaxedBudget has correct values`` () =
        Assert.Equal(1e-2, relaxedBudget.MaxTotalError, 10)
        Assert.Equal(1e-4, relaxedBudget.MaxSingleGateError, 10)
        Assert.Equal(Additive, relaxedBudget.Model)

    // ========================================================================
    // QUALITY ASSESSMENT TESTS
    // ========================================================================

    [<Fact>]
    let ``assessQuality passes when within budget`` () =
        let acc = trackErrors [ T; S; Z ] defaultBudget.Model
        let assessment = assessQuality acc defaultBudget
        Assert.True(assessment.MeetsBudget)
        Assert.True(assessment.ErrorMargin > 0.0)

    [<Fact>]
    let ``assessQuality fails when single gate exceeds budget`` () =
        // H has error 1e-5, strictBudget allows max 1e-8 per gate
        let acc = trackErrors [ H ] strictBudget.Model
        let assessment = assessQuality acc strictBudget
        Assert.False(assessment.MeetsBudget)

    [<Fact>]
    let ``assessQuality grades A+ for low utilization`` () =
        // All exact gates = 0 error, 0% utilization
        let acc = trackErrors [ T; S; Z ] defaultBudget.Model
        let assessment = assessQuality acc defaultBudget
        Assert.Equal("A+", assessment.Grade)
        Assert.True(assessment.BudgetUtilization < 10.0)

    [<Fact>]
    let ``assessQuality grades F for over budget`` () =
        // Many approximate gates with strict budget
        let gates = List.replicate 1000 H  // 1000 * 1e-5 = 1e-2 total (additive)
        let acc = trackErrors gates strictBudget.Model
        let assessment = assessQuality acc strictBudget
        Assert.Equal("F", assessment.Grade)
        Assert.True(assessment.BudgetUtilization > 100.0)

    [<Fact>]
    let ``assessQuality error margin is positive when under budget`` () =
        let acc = trackErrors [ T ] defaultBudget.Model
        let assessment = assessQuality acc defaultBudget
        Assert.True(assessment.ErrorMargin > 0.0)
        Assert.Equal(defaultBudget.MaxTotalError - acc.TotalError, assessment.ErrorMargin, 10)

    [<Fact>]
    let ``assessQuality error margin is negative when over budget`` () =
        let gates = List.replicate 200 H
        let acc = trackErrors gates strictBudget.Model
        let assessment = assessQuality acc strictBudget
        Assert.True(assessment.ErrorMargin < 0.0)

    [<Fact>]
    let ``assessQuality handles zero budget gracefully`` () =
        let zeroBudget = { MaxTotalError = 0.0; MaxSingleGateError = 0.0; Model = Additive }
        let acc = trackErrors [ T ] zeroBudget.Model
        let assessment = assessQuality acc zeroBudget
        Assert.Equal(0.0, assessment.BudgetUtilization, 10)

    // ========================================================================
    // OPTIMIZATION SUGGESTIONS TESTS
    // ========================================================================

    [<Fact>]
    let ``suggestOptimizations for circuit within budget`` () =
        let acc = trackErrors [ T; S; Z ] defaultBudget.Model
        let suggestions = suggestOptimizations acc defaultBudget
        Assert.Contains("Circuit meets error budget with room to spare", suggestions)

    [<Fact>]
    let ``suggestOptimizations warns when over budget`` () =
        let gates = List.replicate 200 H
        let acc = trackErrors gates strictBudget.Model
        let suggestions = suggestOptimizations acc strictBudget
        Assert.True(suggestions |> List.exists (fun s -> s.Contains("exceeds error budget")))

    [<Fact>]
    let ``suggestOptimizations suggests SK precision when approximate gates present and over budget`` () =
        let gates = List.replicate 200 H
        let acc = trackErrors gates strictBudget.Model
        let suggestions = suggestOptimizations acc strictBudget
        Assert.True(suggestions |> List.exists (fun s -> s.Contains("Solovay-Kitaev")))

    [<Fact>]
    let ``suggestOptimizations warns about single gate violations`` () =
        // H has error 1e-5, strictBudget maxSingleGate = 1e-8
        let acc = trackErrors [ H ] strictBudget.Model
        let suggestions = suggestOptimizations acc strictBudget
        Assert.True(suggestions |> List.exists (fun s -> s.Contains("single-gate error budget")))

    [<Fact>]
    let ``suggestOptimizations suggests circuit optimization for many approximate gates`` () =
        let gates = List.replicate 15 H
        let acc = trackErrors gates strictBudget.Model
        let suggestions = suggestOptimizations acc strictBudget
        Assert.True(suggestions |> List.exists (fun s -> s.Contains("circuit optimization")))

    [<Fact>]
    let ``suggestOptimizations provides diamond norm info`` () =
        let diamondBudget = { MaxTotalError = 1.0; MaxSingleGateError = 1.0; Model = DiamondNorm }
        let acc = trackErrors [ T ] diamondBudget.Model
        let suggestions = suggestOptimizations acc diamondBudget
        Assert.True(suggestions |> List.exists (fun s -> s.Contains("diamond norm")))

    // ========================================================================
    // DISPLAY UTILITY TESTS
    // ========================================================================

    [<Fact>]
    let ``displayAccumulation contains model name`` () =
        let acc = trackErrors [ T; H ] Additive
        let output = displayAccumulation acc
        Assert.Contains("Additive", output)

    [<Fact>]
    let ``displayAccumulation shows quadratic model name`` () =
        let acc = trackErrors [ T ] Quadratic
        let output = displayAccumulation acc
        Assert.Contains("Quadratic", output)

    [<Fact>]
    let ``displayAccumulation shows diamond norm model name`` () =
        let acc = trackErrors [ T ] DiamondNorm
        let output = displayAccumulation acc
        Assert.Contains("Diamond Norm", output)

    [<Fact>]
    let ``displayAccumulation shows all-exact message when no approximate gates`` () =
        let acc = trackErrors [ T; S; Z ] Additive
        let output = displayAccumulation acc
        Assert.Contains("all exact", output)

    [<Fact>]
    let ``displayAccumulation shows top error contributors`` () =
        let acc = trackErrors [ T; H; X ] Additive
        let output = displayAccumulation acc
        Assert.Contains("Solovay-Kitaev", output)

    [<Fact>]
    let ``displayQualityAssessment shows PASS for meeting budget`` () =
        let acc = trackErrors [ T ] defaultBudget.Model
        let assessment = assessQuality acc defaultBudget
        let output = displayQualityAssessment assessment
        Assert.Contains("PASS", output)

    [<Fact>]
    let ``displayQualityAssessment shows FAIL for exceeding budget`` () =
        let gates = List.replicate 200 H
        let acc = trackErrors gates strictBudget.Model
        let assessment = assessQuality acc strictBudget
        let output = displayQualityAssessment assessment
        Assert.Contains("FAIL", output)

    [<Fact>]
    let ``displayQualityAssessment shows grade`` () =
        let acc = trackErrors [ T ] defaultBudget.Model
        let assessment = assessQuality acc defaultBudget
        let output = displayQualityAssessment assessment
        Assert.Contains("A+", output)

    [<Fact>]
    let ``generateReport produces complete report`` () =
        let gates = [ T; H; S; X; Z ]
        let report = generateReport gates defaultBudget
        Assert.Contains("Error Propagation Analysis", report)
        Assert.Contains("Circuit Quality Assessment", report)
        Assert.Contains("Recommendations:", report)
