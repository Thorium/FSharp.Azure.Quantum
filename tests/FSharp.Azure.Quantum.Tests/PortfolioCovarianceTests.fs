namespace FSharp.Azure.Quantum.Tests

open System
open System.Threading
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Mean-variance portfolio support: covariance validation, risk sqrt(wᵀΣw), the covariance
/// QUBO (checked against brute force) and covariance pass-through in the solvers.
module PortfolioCovarianceTests =

    let private asset symbol expectedReturn risk price : PortfolioTypes.Asset =
        {
            Symbol = symbol
            ExpectedReturn = expectedReturn
            Risk = risk
            Price = price
        }

    let private isValidationError (field: string) (result: Result<'T, QuantumError>) =
        match result with
        | Error(QuantumError.ValidationError(f, _)) -> f = field
        | _ -> false

    /// Σ = ρ σ σᵀ for two assets.
    let private twoAssetCovariance (s1: float) (s2: float) (rho: float) =
        array2D [ [ s1 * s1; rho * s1 * s2 ]; [ rho * s1 * s2; s2 * s2 ] ]

    // ========================================================================
    // RISK FORMULA
    // ========================================================================

    [<Fact>]
    let ``portfolioRisk with covariance is sqrt of w'Sw`` () =
        // w = (0.5, 0.5), σ = (0.2, 0.3), ρ = 0.5:
        // wᵀΣw = 0.25·0.04 + 0.25·0.09 + 2·0.25·0.5·0.2·0.3 = 0.0475
        let assets = [ asset "A" 0.1 0.2 1.0; asset "B" 0.1 0.3 1.0 ]
        let covariance = twoAssetCovariance 0.2 0.3 0.5

        let correlated =
            PortfolioTypes.portfolioRisk assets [| 0.5; 0.5 |] (Some covariance)

        let independent = PortfolioTypes.portfolioRisk assets [| 0.5; 0.5 |] None

        Assert.Equal(sqrt 0.0475, correlated, 12)
        // Independent: 0.25·0.04 + 0.25·0.09 = 0.0325
        Assert.Equal(sqrt 0.0325, independent, 12)

    [<Fact>]
    let ``portfolioRisk with a diagonal covariance equals the independent formula`` () =
        let assets =
            [ asset "A" 0.1 0.2 1.0; asset "B" 0.1 0.3 1.0; asset "C" 0.1 0.25 1.0 ]

        let diagonal =
            array2D [ [ 0.04; 0.0; 0.0 ]; [ 0.0; 0.09; 0.0 ]; [ 0.0; 0.0; 0.0625 ] ]

        let weights = [| 0.2; 0.3; 0.5 |]

        Assert.Equal(
            PortfolioTypes.portfolioRisk assets weights None,
            PortfolioTypes.portfolioRisk assets weights (Some diagonal),
            12
        )

    [<Fact>]
    let ``covarianceFromCorrelation scales by both volatilities`` () =
        let correlation = array2D [ [ 1.0; 0.5 ]; [ 0.5; 1.0 ] ]

        match PortfolioTypes.covarianceFromCorrelation [| 0.2; 0.3 |] correlation with
        | Ok sigma ->
            Assert.Equal(0.04, sigma.[0, 0], 12)
            Assert.Equal(0.09, sigma.[1, 1], 12)
            Assert.Equal(0.03, sigma.[0, 1], 12)
            Assert.Equal(0.03, sigma.[1, 0], 12)
        | Error err -> Assert.Fail(err.Message)

    [<Fact>]
    let ``covarianceFromCorrelation rejects a shape mismatch`` () =
        let correlation = array2D [ [ 1.0; 0.5 ]; [ 0.5; 1.0 ] ]

        Assert.True(
            PortfolioTypes.covarianceFromCorrelation [| 0.2; 0.3; 0.4 |] correlation
            |> isValidationError "correlation"
        )

    [<Fact>]
    let ``classical greedy reports correlated risk from the covariance`` () =
        // Same selection as the independent case: HIGH gets 150 (60%), MED 100 (40%).
        let assets =
            [
                asset "HIGH" 0.20 0.10 100.0
                asset "MED" 0.15 0.15 100.0
                asset "LOW" 0.10 0.20 100.0
            ]

        let constraints: PortfolioSolver.Constraints =
            {
                Budget = 250.0
                MinHolding = 0.0
                MaxHolding = 150.0
            }

        // ρ(HIGH, MED) = 0.8, LOW uncorrelated
        let covariance =
            array2D
                [
                    [ 0.01; 0.8 * 0.10 * 0.15; 0.0 ]
                    [ 0.8 * 0.10 * 0.15; 0.0225; 0.0 ]
                    [ 0.0; 0.0; 0.04 ]
                ]

        let independent =
            PortfolioSolver.solveGreedyByRatio assets constraints PortfolioSolver.defaultConfig

        let correlated =
            PortfolioSolver.solveGreedyByRatioWithCovariance
                assets
                (Some covariance)
                constraints
                PortfolioSolver.defaultConfig

        let symbols (s: PortfolioSolver.PortfolioSolution) =
            s.Allocations |> List.map (fun a -> a.Asset.Symbol, a.Value)

        Assert.Equal<(string * float) list>(symbols independent, symbols correlated)
        // Independent: 0.36·0.01 + 0.16·0.0225 = 0.0072
        Assert.Equal(sqrt 0.0072, independent.Risk, 10)
        // Correlated: 0.0072 + 2·0.6·0.4·0.012 = 0.01296
        Assert.Equal(sqrt 0.01296, correlated.Risk, 10)
        Assert.Equal(correlated.ExpectedReturn / correlated.Risk, correlated.SharpeRatio, 10)

    // ========================================================================
    // COVARIANCE VALIDATION
    // ========================================================================

    [<Fact>]
    let ``validateCovariance accepts a positive semidefinite matrix`` () =
        Assert.True(Result.isOk (PortfolioTypes.validateCovariance 2 (twoAssetCovariance 0.2 0.3 0.5)))

    [<Fact>]
    let ``validateCovariance accepts a singular matrix of perfectly correlated assets`` () =
        Assert.True(Result.isOk (PortfolioTypes.validateCovariance 2 (twoAssetCovariance 0.2 0.3 1.0)))

    [<Fact>]
    let ``validateCovariance accepts a zero-risk asset`` () =
        let sigma = array2D [ [ 0.04; 0.0 ]; [ 0.0; 0.0 ] ]
        Assert.True(Result.isOk (PortfolioTypes.validateCovariance 2 sigma))

    [<Fact>]
    let ``validateCovariance rejects a non-square matrix`` () =
        Assert.True(
            PortfolioTypes.validateCovariance 2 (Array2D.zeroCreate 2 3)
            |> isValidationError "covariance"
        )

    [<Fact>]
    let ``validateCovariance rejects a size that does not match the asset count`` () =
        Assert.True(
            PortfolioTypes.validateCovariance 3 (twoAssetCovariance 0.2 0.3 0.5)
            |> isValidationError "covariance"
        )

    [<Fact>]
    let ``validateCovariance rejects an asymmetric matrix`` () =
        let sigma = array2D [ [ 0.04; 0.01 ]; [ 0.02; 0.09 ] ]
        Assert.True(PortfolioTypes.validateCovariance 2 sigma |> isValidationError "covariance")

    [<Fact>]
    let ``validateCovariance rejects a matrix with a negative eigenvalue`` () =
        // |ρ| > 1: eigenvalues of [[1, 2], [2, 1]] are 3 and -1
        let sigma = array2D [ [ 1.0; 2.0 ]; [ 2.0; 1.0 ] ]
        Assert.True(PortfolioTypes.validateCovariance 2 sigma |> isValidationError "covariance")

    [<Fact>]
    let ``validateCovariance rejects a three-asset matrix with valid pairs but no joint PSD`` () =
        // Each pairwise ρ is in [-1, 1], yet ρ12 = ρ13 = 0.9, ρ23 = -0.9 is not a correlation matrix.
        let sigma = array2D [ [ 1.0; 0.9; 0.9 ]; [ 0.9; 1.0; -0.9 ]; [ 0.9; -0.9; 1.0 ] ]

        Assert.True(PortfolioTypes.validateCovariance 3 sigma |> isValidationError "covariance")

    [<Fact>]
    let ``validateCovariance rejects non-finite and negative-variance entries`` () =
        let withNaN = array2D [ [ 0.04; nan ]; [ nan; 0.09 ] ]
        let negativeVariance = array2D [ [ -0.04; 0.0 ]; [ 0.0; 0.09 ] ]

        Assert.True(PortfolioTypes.validateCovariance 2 withNaN |> isValidationError "covariance")

        Assert.True(
            PortfolioTypes.validateCovariance 2 negativeVariance
            |> isValidationError "covariance"
        )

    // ========================================================================
    // QUBO: BRUTE FORCE AGAINST THE DISCRETISED MEAN-VARIANCE PROBLEM
    // ========================================================================

    let private quboEnergy (qubo: GraphOptimization.QuboMatrix) (bits: int array) =
        qubo.Q
        |> Map.fold (fun acc (i, j) q -> acc + q * float bits.[i] * float bits.[j]) 0.0

    let private bitsOf (n: int) (k: int) = Array.init n (fun i -> (k >>> i) &&& 1)

    /// -(μᵀw - λ wᵀΣw) with w = s·x, computed directly from the problem data.
    let private directObjective (mu: float array) (sigma: float[,]) (lambda: float) (s: float) (bits: int array) =
        let w = bits |> Array.map (fun b -> s * float b)
        let ret = Array.fold2 (fun acc wi mi -> acc + wi * mi) 0.0 w mu
        let mutable variance = 0.0

        for i in 0 .. w.Length - 1 do
            for j in 0 .. w.Length - 1 do
                variance <- variance + w.[i] * w.[j] * sigma.[i, j]

        -(ret - lambda * variance)

    let private problemOf assets constraints lambda covariance : QuantumPortfolioSolver.PortfolioProblem =
        {
            Assets = assets
            Constraints = constraints
            RiskAversion = lambda
            Covariance = covariance
        }

    [<Fact>]
    let ``QUBO minimum is the mean-variance optimum of the discretised 12-asset problem`` () =
        let n = 12
        let rng = Random(20260930)

        // Σ = A Aᵀ / n is positive semidefinite with non-zero off-diagonal terms.
        let a = Array2D.init n n (fun _ _ -> rng.NextDouble() * 0.6 - 0.15)

        let sigma =
            Array2D.init n n (fun i j -> (Seq.init n (fun k -> a.[i, k] * a.[j, k]) |> Seq.sum) / float n)

        let mu = Array.init n (fun _ -> rng.NextDouble() * 0.4 - 0.05)

        let assets =
            List.init n (fun i -> asset $"S{i}" mu.[i] (sqrt sigma.[i, i]) (10.0 + float i))

        let constraints: PortfolioSolver.Constraints =
            {
                Budget = 12000.0
                MinHolding = 0.0
                MaxHolding = 12000.0
            }

        let lambda = 8.0
        let problem = problemOf assets constraints lambda (Some sigma)

        match QuantumPortfolioSolver.toQubo problem with
        | Error err -> Assert.Fail(err.Message)
        | Ok qubo ->
            Assert.Equal(n, qubo.NumVariables)
            Assert.True(qubo.Q |> Map.exists (fun (i, j) v -> i <> j && v <> 0.0), "QUBO has covariance couplings")

            let s = QuantumPortfolioSolver.lotWeight problem
            Assert.Equal(1.0 / float n, s, 12)

            let all = Array.init (FSharp.Core.Operators.max 0 (1 <<< n)) (bitsOf n)

            // xᵀQx equals the mean-variance objective on every one of the 4096 selections ...
            for bits in all do
                let expected = directObjective mu sigma lambda s bits
                Assert.Equal(expected, quboEnergy qubo bits, 10)
                Assert.Equal(expected, QuantumPortfolioSolver.meanVarianceEnergy problem bits, 10)

            // ... so the QUBO minimum is the discretised optimum.
            let bestByQubo = all |> Array.minBy (quboEnergy qubo)
            let bestDirect = all |> Array.minBy (directObjective mu sigma lambda s)
            Assert.Equal<int array>(bestDirect, bestByQubo)

            // The optimum depends on the correlations: the independent problem picks differently.
            let independentBest =
                all
                |> Array.minBy (
                    directObjective mu (Array2D.init n n (fun i j -> if i = j then sigma.[i, i] else 0.0)) lambda s
                )

            Assert.NotEqual<int array>(independentBest, bestByQubo)

    /// Two highly correlated high-return assets (ρ = 0.95) and one uncorrelated asset with a
    /// lower return, all with σ = 0.3, lot weight 1/3, λ = 5:
    ///   {A, B}: -0.2 + 5·0.039 = -0.005     {A, C} / {B, C}: -1/6 + 5·0.02 = -0.0667
    ///   {A, B, C}: -0.2667 + 5·0.049 = -0.0217
    /// Without the correlation {A, B, C} is best (-0.1167).
    let private correlatedPairCase () =
        let assets =
            [ asset "A" 0.30 0.30 50.0; asset "B" 0.30 0.30 50.0; asset "C" 0.20 0.30 50.0 ]

        let rho = 0.95

        let sigma =
            array2D [ [ 0.09; rho * 0.09; 0.0 ]; [ rho * 0.09; 0.09; 0.0 ]; [ 0.0; 0.0; 0.09 ] ]

        let constraints: PortfolioSolver.Constraints =
            {
                Budget = 3000.0
                MinHolding = 0.0
                MaxHolding = 3000.0
            }

        assets, sigma, constraints

    [<Fact>]
    let ``QUBO optimum diversifies into the uncorrelated asset`` () =
        let assets, sigma, constraints = correlatedPairCase ()
        let all = Array.init 8 (bitsOf 3)

        let best covariance =
            match QuantumPortfolioSolver.toQubo (problemOf assets constraints 5.0 covariance) with
            | Ok qubo -> all |> Array.minBy (quboEnergy qubo)
            | Error err -> failwith err.Message

        let correlatedBest = best (Some sigma)
        Assert.Equal(1, correlatedBest.[2])
        Assert.Equal(1, correlatedBest.[0] + correlatedBest.[1])

        Assert.Equal<int array>([| 1; 1; 1 |], best None)

    [<Fact>]
    let ``quantum solver with covariance diversifies into the uncorrelated asset`` () =
        let assets, sigma, constraints = correlatedPairCase ()
        let backend = Backends.LocalBackend.LocalBackend() :> IQuantumBackend

        let config =
            { QuantumPortfolioSolver.defaultConfig with
                RiskAversion = 5.0
            }

        let result =
            QuantumPortfolioSolver.solveWithCovarianceAsync
                backend
                assets
                sigma
                constraints
                config
                CancellationToken.None
            |> fun t -> t.GetAwaiter().GetResult()

        match result with
        | Error err -> Assert.Fail(err.Message)
        | Ok solution ->
            let held = solution.Allocations |> List.map (fun a -> a.Asset.Symbol) |> Set.ofList
            Assert.Equal(2, held.Count)
            Assert.Contains("C", held)

            // Each held asset gets one lot of Budget / 3; the rest stays uninvested.
            for a in solution.Allocations do
                Assert.Equal(1000.0, a.Value, 6)

            // Risk is sqrt(wᵀΣw) over the invested weights (0.5, 0.5 on one of A/B and C).
            Assert.Equal(sqrt (0.25 * 0.09 + 0.25 * 0.09), solution.Risk, 10)
            Assert.Equal(-(1.0 / 6.0) + 5.0 * 0.02, solution.BestEnergy, 10)

    [<Fact>]
    let ``toQubo rejects an invalid covariance`` () =
        let assets, _, constraints = correlatedPairCase ()
        let notPsd = array2D [ [ 1.0; 2.0; 0.0 ]; [ 2.0; 1.0; 0.0 ]; [ 0.0; 0.0; 1.0 ] ]

        Assert.True(
            QuantumPortfolioSolver.toQubo (problemOf assets constraints 0.5 (Some notPsd))
            |> isValidationError "covariance"
        )

        Assert.True(
            QuantumPortfolioSolver.toQubo (problemOf assets constraints 0.5 (Some(twoAssetCovariance 0.2 0.3 0.5)))
            |> isValidationError "covariance"
        )

    [<Fact>]
    let ``toQubo without covariance uses the variances only`` () =
        let assets, _, constraints = correlatedPairCase ()

        match QuantumPortfolioSolver.toQubo (problemOf assets constraints 0.5 None) with
        | Error err -> Assert.Fail(err.Message)
        | Ok qubo ->
            Assert.True(qubo.Q |> Map.forall (fun (i, j) _ -> i = j))
            // Q[0,0] = -s μ + λ s² σ² with s = 1/3
            Assert.Equal(-0.30 / 3.0 + 0.5 * 0.09 / 9.0, qubo.Q.[(0, 0)], 12)

    // ========================================================================
    // HYBRID SOLVER AND PORTFOLIO BUILDER PASS-THROUGH
    // ========================================================================

    [<Fact>]
    let ``solvePortfolioWithCovariance forced classical reports sqrt(w'Sw)`` () =
        let assets = [ asset "A" 0.10 0.20 1.0; asset "B" 0.05 0.10 1.0 ]

        let constraints: PortfolioSolver.Constraints =
            {
                Budget = 10.0
                MinHolding = 0.0
                MaxHolding = 6.0
            }

        // Greedy: both ratios are 0.5; the first gets MaxHolding 6, the other the remaining 4
        let covariance = twoAssetCovariance 0.2 0.1 (-0.5)

        match
            HybridSolver.solvePortfolioWithCovariance
                assets
                covariance
                constraints
                None
                None
                (Some HybridSolver.SolverMethod.Classical)
                None
        with
        | Error err -> Assert.Fail(err.Message)
        | Ok solution ->
            Assert.Equal(HybridSolver.SolverMethod.Classical, solution.Method)

            let weights =
                assets
                |> List.map (fun a ->
                    solution.Result.Allocations
                    |> List.tryFind (fun x -> x.Asset.Symbol = a.Symbol)
                    |> Option.map (fun x -> x.Percentage)
                    |> Option.defaultValue 0.0)
                |> List.toArray

            Assert.Equal(1.0, Array.sum weights, 10)
            Assert.True(weights |> Array.forall (fun w -> w > 0.0), "Both assets are held")

            let expected =
                sqrt (
                    weights.[0] * weights.[0] * 0.04
                    + weights.[1] * weights.[1] * 0.01
                    + 2.0 * weights.[0] * weights.[1] * (-0.5 * 0.2 * 0.1)
                )

            Assert.Equal(expected, solution.Result.Risk, 10)
            Assert.True(solution.Result.Risk < PortfolioTypes.portfolioRisk assets weights None)

    [<Fact>]
    let ``solvePortfolioWithCovariance forced quantum reports sqrt(w'Sw)`` () =
        let assets, sigma, constraints = correlatedPairCase ()

        match
            HybridSolver.solvePortfolioWithCovariance
                assets
                sigma
                constraints
                None
                None
                (Some HybridSolver.SolverMethod.Quantum)
                None
        with
        | Error err -> Assert.Fail(err.Message)
        | Ok solution ->
            Assert.Equal(HybridSolver.SolverMethod.Quantum, solution.Method)

            let weights =
                assets
                |> List.map (fun a ->
                    solution.Result.Allocations
                    |> List.tryFind (fun x -> x.Asset.Symbol = a.Symbol)
                    |> Option.map (fun x -> x.Percentage)
                    |> Option.defaultValue 0.0)
                |> List.toArray

            Assert.Equal(PortfolioTypes.portfolioRisk assets weights (Some sigma), solution.Result.Risk, 10)

    [<Fact>]
    let ``solvePortfolioWithCovariance rejects an invalid covariance on every path`` () =
        let assets = [ asset "A" 0.10 0.20 10.0; asset "B" 0.05 0.10 5.0 ]

        let constraints: PortfolioSolver.Constraints =
            {
                Budget = 10.0
                MinHolding = 0.0
                MaxHolding = 10.0
            }

        let asymmetric = array2D [ [ 0.04; 0.01 ]; [ 0.0; 0.01 ] ]

        for method in
            [
                Some HybridSolver.SolverMethod.Classical
                Some HybridSolver.SolverMethod.Quantum
                None
            ] do
            Assert.True(
                HybridSolver.solvePortfolioWithCovariance assets asymmetric constraints None None method None
                |> isValidationError "covariance"
            )

    [<Fact>]
    let ``Portfolio.solve with covariance reports correlated risk`` () =
        let assets =
            [ ("A", 0.30, 0.30, 50.0); ("B", 0.30, 0.30, 50.0); ("C", 0.20, 0.30, 50.0) ]

        let _, sigma, _ = correlatedPairCase ()
        let problem = Portfolio.createProblemWithCovariance assets 3000.0 sigma

        match Portfolio.solve problem None with
        | Error err -> Assert.Fail(err.Message)
        | Ok allocation ->
            Assert.True(allocation.IsValid)

            let weights =
                problem.Assets
                |> Array.map (fun a ->
                    allocation.Allocations
                    |> List.tryFind (fun (s, _, _) -> s = a.Symbol)
                    |> Option.map (fun (_, _, v) -> v / allocation.TotalValue)
                    |> Option.defaultValue 0.0)

            Assert.Equal(
                PortfolioTypes.portfolioRisk (List.ofArray problem.Assets) weights (Some sigma),
                allocation.Risk,
                10
            )

    [<Fact>]
    let ``Portfolio.solve rejects a covariance of the wrong size`` () =
        let assets =
            [ ("A", 0.30, 0.30, 50.0); ("B", 0.30, 0.30, 50.0); ("C", 0.20, 0.30, 50.0) ]

        let problem =
            Portfolio.createProblemWithCovariance assets 3000.0 (twoAssetCovariance 0.3 0.3 0.5)

        Assert.True(Portfolio.solve problem None |> isValidationError "covariance")

    [<Fact>]
    let ``Portfolio.createProblemWithCorrelation builds the covariance from the asset risks`` () =
        let assets = [ ("A", 0.10, 0.20, 50.0); ("B", 0.12, 0.30, 50.0) ]
        let correlation = array2D [ [ 1.0; 0.5 ]; [ 0.5; 1.0 ] ]

        match Portfolio.createProblemWithCorrelation assets 1000.0 correlation with
        | Error err -> Assert.Fail(err.Message)
        | Ok problem ->
            match problem.Covariance with
            | None -> Assert.Fail "Expected a covariance"
            | Some sigma -> Assert.Equal(0.5 * 0.2 * 0.3, sigma.[0, 1], 12)

        Assert.True(
            Portfolio.createProblemWithCorrelation assets 1000.0 (array2D [ [ 1.0 ] ])
            |> isValidationError "correlation"
        )

    // ========================================================================
    // LOT SIZING, FEASIBILITY, PRICES AND QAOA ANGLES
    // ========================================================================

    let private fiveAssets =
        [
            asset "A" 0.12 0.2 100.0
            asset "B" 0.10 0.15 50.0
            asset "C" 0.08 0.1 20.0
            asset "D" 0.15 0.3 200.0
            asset "E" 0.05 0.05 10.0
        ]

    let private localBackend () =
        Backends.LocalBackend.LocalBackend() :> IQuantumBackend

    [<Fact>]
    let ``lot follows MinHolding above Budget over n and caps the number of holdings`` () =
        // 1/n = 0.2 is below MinHolding / Budget = 0.3: each lot is 3000 and at most 3 fit.
        task {
            let constraints: PortfolioSolver.Constraints =
                {
                    Budget = 10000.0
                    MinHolding = 3000.0
                    MaxHolding = 5000.0
                }

            let problem = problemOf fiveAssets constraints 0.5 None
            Assert.Equal(0.3, QuantumPortfolioSolver.lotWeight problem, 12)
            Assert.Equal(3, QuantumPortfolioSolver.maxHoldings problem)

            let feasibleBest =
                Array.init 32 (bitsOf 5)
                |> Array.filter (fun bits ->
                    Array.sum bits > 0 && QuantumPortfolioSolver.isFeasibleSelection problem bits)
                |> Array.map (QuantumPortfolioSolver.meanVarianceEnergy problem)
                |> Array.min

            let! result =
                (QuantumPortfolioSolver.solveAsync
                    (localBackend ())
                    fiveAssets
                    constraints
                    QuantumPortfolioSolver.defaultConfig
                    CancellationToken.None)

            match result with
            | Error err -> Assert.Fail(err.Message)
            | Ok solution ->
                Assert.InRange(solution.Allocations.Length, 1, 3)

                for a in solution.Allocations do
                    Assert.Equal(3000.0, a.Value, 6)

                Assert.True(solution.TotalValue <= constraints.Budget)
                // 32 selections and thousands of samples: the best feasible one is always seen.
                Assert.Equal(feasibleBest, solution.BestEnergy, 12)
        }
        :> System.Threading.Tasks.Task

    [<Fact>]
    let ``holding limits that cannot be met are validation errors`` () =
        let minAboveMax: PortfolioSolver.Constraints =
            {
                Budget = 10000.0
                MinHolding = 6000.0
                MaxHolding = 5000.0
            }

        Assert.True(
            QuantumPortfolioSolver.toQubo (problemOf fiveAssets minAboveMax 0.5 None)
            |> isValidationError "MinHolding"
        )

    [<Fact>]
    let ``an asset priced above one lot is never bought`` () =
        task {
            let pricey =
                [
                    asset "BRKA" 0.20 0.15 600000.0
                    asset "B" 0.05 0.15 50.0
                    asset "C" 0.04 0.1 20.0
                ]

            let constraints: PortfolioSolver.Constraints =
                {
                    Budget = 10000.0
                    MinHolding = 0.0
                    MaxHolding = 10000.0
                }

            let problem = problemOf pricey constraints 0.5 None
            Assert.False(QuantumPortfolioSolver.isAffordable problem pricey.[0])

            // The QUBO minimum leaves it out although it has the best return.
            match QuantumPortfolioSolver.toQubo problem with
            | Error err -> Assert.Fail(err.Message)
            | Ok qubo ->
                let best = Array.init 8 (bitsOf 3) |> Array.minBy (quboEnergy qubo)
                Assert.Equal(0, best.[0])

            let! result =
                (QuantumPortfolioSolver.solveAsync
                    (localBackend ())
                    pricey
                    constraints
                    QuantumPortfolioSolver.defaultConfig
                    CancellationToken.None)

            match result with
            | Error err -> Assert.Fail(err.Message)
            | Ok solution ->
                Assert.DoesNotContain("BRKA", solution.Allocations |> List.map (fun a -> a.Asset.Symbol))
                Assert.False(solution.SelectedAssets.["BRKA"])

                for a in solution.Allocations do
                    Assert.True(a.Shares >= 1.0, $"{a.Asset.Symbol}: {a.Shares} shares")

            // The classical greedy also needs at least one share's worth.
            match
                HybridSolver.solvePortfolio pricey constraints None None (Some HybridSolver.SolverMethod.Classical)
            with
            | Error err -> Assert.Fail(err.Message)
            | Ok solution ->
                Assert.DoesNotContain("BRKA", solution.Result.Allocations |> List.map (fun a -> a.Asset.Symbol))
        }
        :> System.Threading.Tasks.Task

    [<Fact>]
    let ``no affordable asset is a validation error`` () =
        task {
            let constraints: PortfolioSolver.Constraints =
                {
                    Budget = 1000.0
                    MinHolding = 0.0
                    MaxHolding = 1000.0
                }

            let! result =
                (QuantumPortfolioSolver.solveAsync
                    (localBackend ())
                    [ asset "X" 0.1 0.2 5000.0; asset "Y" 0.1 0.2 9000.0 ]
                    constraints
                    QuantumPortfolioSolver.defaultConfig
                    CancellationToken.None)

            Assert.True(result |> isValidationError "Price")
        }
        :> System.Threading.Tasks.Task

    [<Fact>]
    let ``validateCovariance accepts an all-zero covariance`` () =
        Assert.True(Result.isOk (PortfolioTypes.validateCovariance 2 (Array2D.zeroCreate 2 2)))
        // A tiny but clearly indefinite matrix is still rejected.
        let tiny = array2D [ [ 1e-20; 2e-20 ]; [ 2e-20; 1e-20 ] ]
        Assert.True(PortfolioTypes.validateCovariance 2 tiny |> isValidationError "covariance")

    [<Fact>]
    let ``transaction costs are in budget fractions`` () =
        let assets = [ asset "AAPL" 0.12 0.20 150.0; asset "MSFT" 0.10 0.18 350.0 ]

        let constraints: PortfolioSolver.Constraints =
            {
                Budget = 5000.0
                MinHolding = 0.0
                MaxHolding = 2500.0
            }

        let costs: QuantumPortfolioSolver.TransactionCosts =
            {
                BuyCostRate = 0.05
                SellCostRate = 0.0
                FixedCostPerTrade = 10.0
            }

        let withCosts =
            QuantumPortfolioSolver.createProblemWithCosts assets constraints 0.5 Map.empty costs

        match
            QuantumPortfolioSolver.toQubo withCosts.BaseProblem,
            QuantumPortfolioSolver.toQuboWithTransactionCosts withCosts
        with
        | Ok baseQubo, Ok costQubo ->
            // Lot 2500: buying costs 0.05 × 2500 + 10 = 135 dollars = 0.027 of the budget.
            Assert.Equal(0.027, costQubo.Q.[(0, 0)] - baseQubo.Q.[(0, 0)], 12)
        | a, b -> Assert.Fail($"%A{a} %A{b}")

    [<Fact>]
    let ``QAOA angle grid concentrates probability on the best selections of a 12-asset problem`` () =
        let n = 12
        let rng = Random(2)
        let a = Array2D.init n n (fun _ _ -> rng.NextDouble() * 0.6 - 0.15)

        let sigma =
            Array2D.init n n (fun i j -> (Seq.init n (fun k -> a.[i, k] * a.[j, k]) |> Seq.sum) / float n)

        let mu = Array.init n (fun _ -> rng.NextDouble() * 0.4 - 0.05)
        let assets = List.init n (fun i -> asset $"S{i}" mu.[i] (sqrt sigma.[i, i]) 10.0)

        let constraints: PortfolioSolver.Constraints =
            {
                Budget = 12000.0
                MinHolding = 0.0
                MaxHolding = 12000.0
            }

        let problem = problemOf assets constraints 8.0 (Some sigma)
        let backend = localBackend ()

        match QuantumPortfolioSolver.toQubo problem with
        | Error err -> Assert.Fail(err.Message)
        | Ok qubo ->
            let circuitMatrix, kept = QuantumPortfolioSolver.circuitQubo problem qubo
            Assert.Equal(n, kept.Length)

            let sampled =
                (QuantumPortfolioSolver.sampleWithAngleGridAsync
                    backend
                    circuitMatrix
                    QuantumPortfolioSolver.defaultConfig.InitialParameters
                    100
                    1000
                    CancellationToken.None)
                    .GetAwaiter()
                    .GetResult()

            match sampled with
            | Error err -> Assert.Fail(err.Message)
            | Ok(_, angles) ->
                // Exact state at the chosen angles: probability of the best 1% of all 4096
                // selections (uniform sampling gives 0.01). Solver angles are in units of
                // the normalised Hamiltonian.
                let hamiltonian =
                    QaoaCircuit.ProblemHamiltonian.fromQubo circuitMatrix
                    |> QaoaCircuit.ProblemHamiltonian.normalize

                let mixer = QaoaCircuit.MixerHamiltonian.create n
                let circuit = QaoaCircuit.QaoaCircuit.build hamiltonian mixer [| angles |]

                match backend.ExecuteToState(CircuitAbstraction.QaoaCircuitWrapper(circuit)) with
                | Error err -> Assert.Fail(err.Message)
                | Ok state ->
                    let best =
                        Array.init (FSharp.Core.Operators.max 0 (1 <<< n)) (bitsOf n)
                        |> Array.sortBy (QuantumPortfolioSolver.meanVarianceEnergy problem)
                        |> Array.take ((1 <<< n) / 100)

                    let mass = best |> Array.sumBy (fun bits -> QuantumState.probability bits state)
                    Assert.True(mass > 0.1, $"P(best 1%%) = {mass} at {angles}; uniform is 0.01")
