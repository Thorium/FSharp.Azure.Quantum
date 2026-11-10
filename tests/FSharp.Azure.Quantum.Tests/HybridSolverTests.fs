namespace FSharp.Azure.Quantum.Tests

open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction

module HybridSolverTests =

    let private twoAssets: PortfolioSolver.Asset list =
        [
            {
                PortfolioTypes.Asset.Symbol = "A"
                ExpectedReturn = 0.10
                Risk = 0.20
                Price = 10.0
            }
            {
                PortfolioTypes.Asset.Symbol = "B"
                ExpectedReturn = 0.05
                Risk = 0.10
                Price = 5.0
            }
        ]

    let private twoAssetConstraints: PortfolioSolver.Constraints =
        {
            Budget = 10.0
            MinHolding = 0.0
            MaxHolding = 10.0
        }

    // Slow: a 3-city TSP is 3² = 9 qubits under the one-hot time encoding, and a
    // topological backend carries that as a fusion-tree state of 2⁹ explicit terms
    // through every gate — one circuit execution takes minutes. That is the floor
    // for this problem on this backend, independent of the QAOA configuration.
    [<Fact; Trait("Category", "Slow")>]
    let ``solveTspWithBackendAndConfigAsync forced quantum accepts topological backend`` () =
        task {
            // Arrange: 3-city symmetric TSP instance
            let distances = array2D [ [ 0.0; 1.0; 2.0 ]; [ 1.0; 0.0; 3.0 ]; [ 2.0; 3.0; 0.0 ] ]

            // Use a topological backend to validate HybridSolver supports non-gate backends.
            let backend =
                FSharp.Azure.Quantum.Topological.TopologicalUnifiedBackendFactory.createIsing 50

            // The default config's variational loop runs up to 1000 Nelder-Mead
            // iterations, each a full circuit execution — hours on this backend. What
            // this test checks is that an injected non-gate backend is accepted and
            // routed to the quantum path, so skip the loop entirely.
            let quantumConfig =
                { QuantumTspSolver.fastConfig with
                    FinalShots = 200
                }

            // Act
            let! result =
                HybridSolver.solveTspWithBackendAndConfigAsync
                    distances
                    None
                    None
                    (Some HybridSolver.SolverMethod.Quantum)
                    (Some backend)
                    quantumConfig
                    CancellationToken.None

            // Assert
            // The injected backend is accepted and the quantum path runs. Whether one of the
            // 200 samples is a valid tour (a permutation matrix) depends on the sampling: the
            // result is the measured tour, or the solver's error, never a classical tour.
            match result with
            | Ok solution ->
                Assert.Equal(HybridSolver.SolverMethod.Quantum, solution.Method)
                Assert.Equal<int[]>([| 0; 1; 2 |], solution.Result.Tour)
                Assert.Equal(6.0, solution.Result.TourLength, 9)
            | Error(FSharp.Azure.Quantum.Core.QuantumError.OperationError(op, context)) ->
                Assert.Equal("Quantum TSP solver", op)
                Assert.Contains("No valid tour in 200 shots", context)
            | Error err -> Assert.Fail(err.Message)
        }
        :> Task

    [<Fact>]
    let ``solveTspWithBackendAndConfigAsync forced quantum returns the measured tour or the solver's error`` () =
        task {
            // 3 cities = 9 qubits on the local simulator, one circuit
            let distances = array2D [ [ 0.0; 1.0; 2.0 ]; [ 1.0; 0.0; 3.0 ]; [ 2.0; 3.0; 0.0 ] ]

            let! result =
                HybridSolver.solveTspWithBackendAndConfigAsync
                    distances
                    None
                    None
                    (Some HybridSolver.SolverMethod.Quantum)
                    None
                    { QuantumTspSolver.fastConfig with
                        FinalShots = 200
                    }
                    CancellationToken.None

            // A forced quantum run never falls back to the classical solver: without a valid
            // tour among the samples it is an error.
            match result with
            | Ok solution ->
                Assert.Equal(HybridSolver.SolverMethod.Quantum, solution.Method)
                Assert.Equal<int[]>([| 0; 1; 2 |], solution.Result.Tour)
                Assert.Equal(6.0, solution.Result.TourLength, 9)
            | Error(FSharp.Azure.Quantum.Core.QuantumError.OperationError(op, context)) ->
                Assert.Equal("Quantum TSP solver", op)
                Assert.Contains("No valid tour in 200 shots", context)
            | Error err -> Assert.Fail(err.Message)
        }
        :> Task

    [<Fact>]
    let ``solveTspAsync forced classical returns Classical`` () =
        task {
            let distances =
                array2D
                    [
                        [ 0.0; 1.0; 2.0; 3.0 ]
                        [ 1.0; 0.0; 4.0; 5.0 ]
                        [ 2.0; 4.0; 0.0; 6.0 ]
                        [ 3.0; 5.0; 6.0; 0.0 ]
                    ]

            let! result =
                HybridSolver.solveTspAsync
                    distances
                    None
                    None
                    (Some HybridSolver.SolverMethod.Classical)
                    CancellationToken.None

            match result with
            | Ok solution ->
                Assert.Equal(HybridSolver.SolverMethod.Classical, solution.Method)
                Assert.Equal(4, solution.Result.Tour.Length)
                Assert.Contains("forced by user override", solution.Reasoning)
            | Error err -> Assert.Fail(err.Message)
        }
        :> Task

    [<Fact>]
    let ``solvePortfolioWithBackendAsync forced classical returns Classical`` () =
        task {
            // Act
            let! result =
                HybridSolver.solvePortfolioWithBackendAsync
                    twoAssets
                    twoAssetConstraints
                    None
                    None
                    (Some HybridSolver.SolverMethod.Classical)
                    None
                    CancellationToken.None

            // Assert
            match result with
            | Ok solution -> Assert.Equal(HybridSolver.SolverMethod.Classical, solution.Method)
            | Error err -> Assert.Fail(err.Message)
        }
        :> Task

    // ========================================================================
    // COST ESTIMATION (budget guard inputs)
    // ========================================================================

    [<Fact>]
    let ``estimateBackendCostUSD treats local simulators as free`` () =
        let backend =
            FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend() :> IQuantumBackend

        let plan = QuantumTspSolver.jobPlan backend QuantumTspSolver.defaultConfig
        Assert.Equal(0.0, HybridSolver.estimateBackendCostUSD backend 25 2 plan)

    /// A cloud backend class for `target`; it is only asked for its name and shot count.
    let private cloudBackend (target: string) (shots: int) : IQuantumBackend =
        let http = new System.Net.Http.HttpClient()
        let url = "https://example.invalid"

        if target.StartsWith "ionq" then
            Backends.CloudBackends.IonQCloudBackend(http, url, target, shots) :> IQuantumBackend
        elif target.StartsWith "rigetti" then
            Backends.CloudBackends.RigettiCloudBackend(http, url, target, shots) :> IQuantumBackend
        elif target.StartsWith "quantinuum" then
            Backends.CloudBackends.QuantinuumCloudBackend(http, url, target, shots) :> IQuantumBackend
        elif target.StartsWith "iqm" then
            Backends.CloudBackends.IqmCloudBackend(http, url, target, shots) :> IQuantumBackend
        else
            Backends.CloudBackends.AtomComputingCloudBackend(http, url, target, shots) :> IQuantumBackend

    [<Fact>]
    let ``the TSP estimate prices every job of the configured solve, not one circuit`` () =
        let backend = cloudBackend "ionq.qpu.aria-1" 100
        let config = QuantumTspSolver.defaultConfig
        let plan = QuantumTspSolver.jobPlan backend config

        // Up to 1000 optimizer iterations, each at least one job, and the final sampling.
        Assert.True(plan.OptimizationJobs >= config.MaxOptimizationIterations, $"%A{plan}")
        Assert.Equal(1, plan.FinalJobs)
        // A sampling backend takes its own shots per job, whatever the config asks for.
        Assert.Equal(100, plan.OptimizationShots)
        Assert.Equal(100, plan.FinalShots)

        let oneJob = Core.QaoaExecutionHelpers.singleRunPlan backend config.FinalShots
        let whole = HybridSolver.estimateBackendCostUSD backend 9 config.NumLayers plan
        let single = HybridSolver.estimateBackendCostUSD backend 9 config.NumLayers oneJob

        Assert.True(single > 0.0)
        Assert.Equal(float plan.Jobs * single, whole, 6)

    [<Fact>]
    let ``the TSP estimate follows the configuration passed down`` () =
        let backend = cloudBackend "rigetti.qpu.ankaa-3" 200

        let cost (config: QuantumTspSolver.QuantumTspConfig) =
            HybridSolver.estimateBackendCostUSD backend 9 config.NumLayers (QuantumTspSolver.jobPlan backend config)

        let full = cost QuantumTspSolver.defaultConfig

        let short =
            cost
                { QuantumTspSolver.defaultConfig with
                    MaxOptimizationIterations = 20
                }

        // fastConfig runs one circuit at fixed angles.
        let fixedAngles = cost QuantumTspSolver.fastConfig

        Assert.Equal(1, (QuantumTspSolver.jobPlan backend QuantumTspSolver.fastConfig).Jobs)
        Assert.True(full > short && short > fixedAngles && fixedAngles > 0.0, $"{full} > {short} > {fixedAngles}")

    [<Fact>]
    let ``a billed backend without a price model estimates to infinity`` () =
        // Quantinuum is charged against a subscription's HQC quota: no per-job USD price.
        for target in
            [
                "iqm.qpu.garnet"
                "atom-computing.qpu.phoenix"
                "quantinuum.qpu.h1-1"
            ] do
            let backend = cloudBackend target 100
            let plan = Core.QaoaExecutionHelpers.singleRunPlan backend 1000
            let cost = HybridSolver.estimateBackendCostUSD backend 10 1 plan
            Assert.True(System.Double.IsPositiveInfinity cost, $"{target}: {cost}")
            // No finite budget covers it.
            Assert.True(cost > System.Double.MaxValue)

    [<Fact>]
    let ``a provider's free simulator target estimates to zero`` () =
        for target in [ "ionq.simulator"; "rigetti.sim.qvm"; "iqm.sim"; "atom-computing.sim"; "quantinuum.sim.h1-1sc" ] do
            let backend = cloudBackend target 100
            let plan = QuantumTspSolver.jobPlan backend QuantumTspSolver.defaultConfig
            Assert.Equal(0.0, HybridSolver.estimateBackendCostUSD backend 9 2 plan)

    [<Fact>]
    let ``a Quantinuum emulator is billed, not a free simulator`` () =
        let backend = cloudBackend "quantinuum.sim.h1-1e" 100
        let plan = Core.QaoaExecutionHelpers.singleRunPlan backend 100
        Assert.True(System.Double.IsPositiveInfinity(HybridSolver.estimateBackendCostUSD backend 9 1 plan))

    [<Fact>]
    let ``the D-Wave mock is free and the real annealer has no price model`` () =
        let mock =
            Backends.DWaveBackend.createMockDWaveBackend Backends.DWaveTypes.Advantage_System6_1 (Some 1)
            :> IQuantumBackend

        // An annealer is submitted once, for the final shots.
        let mockPlan = QuantumTspSolver.jobPlan mock QuantumTspSolver.defaultConfig
        Assert.Equal(1, mockPlan.Jobs)
        Assert.Equal(QuantumTspSolver.defaultConfig.FinalShots, mockPlan.FinalShots)
        Assert.Equal(0.0, HybridSolver.estimateBackendCostUSD mock 9 2 mockPlan)

        use real =
            Backends.RealDWaveBackend.create
                {
                    Backends.RealDWaveBackend.ApiToken = "not-a-token"
                    Endpoint = "https://example.invalid/sapi/v2/"
                    Solver = "Advantage_system6.1"
                    TimeoutMs = None
                }

        let realPlan = QuantumTspSolver.jobPlan real QuantumTspSolver.defaultConfig
        Assert.Equal(1, realPlan.Jobs)
        Assert.True(System.Double.IsPositiveInfinity(HybridSolver.estimateBackendCostUSD real 9 2 realPlan))

    [<Fact>]
    let ``the portfolio estimate counts the angle grid and the final sampling`` () =
        let backend = cloudBackend "ionq.qpu.aria-1" 100
        let plan = QuantumPortfolioSolver.jobPlan backend QuantumPortfolioSolver.defaultConfig

        Assert.Equal(
            1
            + QuantumPortfolioSolver.angleGridGammas.Length
              * QuantumPortfolioSolver.angleGridBetas.Length,
            plan.OptimizationJobs
        )

        Assert.Equal(1, plan.FinalJobs)

    [<Fact>]
    let ``estimateQuantumConfigCostUSD scales with problem size`` () =
        let backend = HybridSolver.QuantumBackend.IonQ "ionq.qpu"
        let small = HybridSolver.estimateQuantumConfigCostUSD backend 9
        let large = HybridSolver.estimateQuantumConfigCostUSD backend 100

        Assert.True(small > 0.0, $"Expected nonzero cost for cloud backend, got {small}")
        Assert.True(large > small, $"Expected cost to grow with problem size: {small} -> {large}")

    [<Fact>]
    let ``estimateQuantumConfigCostUSD differs between providers`` () =
        let ionq =
            HybridSolver.estimateQuantumConfigCostUSD (HybridSolver.QuantumBackend.IonQ "ionq.qpu") 25

        let rigetti =
            HybridSolver.estimateQuantumConfigCostUSD (HybridSolver.QuantumBackend.Rigetti "rigetti.qpu") 25

        Assert.True(ionq > 0.0)
        Assert.True(rigetti > 0.0)
