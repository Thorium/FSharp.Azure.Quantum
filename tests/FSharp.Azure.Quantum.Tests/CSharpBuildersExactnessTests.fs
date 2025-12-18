module CSharpBuildersExactnessTests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Algorithms.QPE

    [<Fact>]
    let ``CSharpBuilders QpeExactness factories create expected values`` () =
        Assert.Equal(Exactness.Exact, CSharpBuilders.QpeExactnessExact())

        match CSharpBuilders.QpeExactnessApproximate 0.001 with
        | Exactness.Approximate epsilon -> Assert.Equal(0.001, epsilon, 3)
        | Exactness.Exact -> Assert.True(false, "Expected Approximate")

    [<Fact>]
    let ``CSharpBuilders EstimateTGate overload sets Exactness`` () =
        let result = CSharpBuilders.EstimateTGate(8, Exactness.Approximate 0.01)

        match result with
        | Error err -> Assert.True(false, err.Message)
        | Ok problem ->
            match problem.Exactness with
            | Exactness.Approximate epsilon -> Assert.Equal(0.01, epsilon, 3)
            | Exactness.Exact -> Assert.True(false, "Expected Approximate")

    [<Fact>]
    let ``CSharpBuilders FactorInteger overload sets Exactness`` () =
        let result = CSharpBuilders.FactorInteger(15, 8, Exactness.Approximate 0.02)

        match result with
        | Error err -> Assert.True(false, err.Message)
        | Ok problem ->
            match problem.Exactness with
            | Exactness.Approximate epsilon -> Assert.Equal(0.02, epsilon, 3)
            | Exactness.Exact -> Assert.True(false, "Expected Approximate")
