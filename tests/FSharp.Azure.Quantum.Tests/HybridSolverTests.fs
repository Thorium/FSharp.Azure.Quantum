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
    // through every gate — one circuit execution is ~2 minutes. That is the floor
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
            // Don't require a successful decode (QAOA is probabilistic and depends on backend capabilities);
            // instead, verify the injected backend is accepted and quantum path is attempted.
            match result with
            | Ok solution ->
                Assert.Equal(HybridSolver.SolverMethod.Quantum, solution.Method)
                Assert.Equal(3, solution.Result.Tour.Length)
            | Error(FSharp.Azure.Quantum.Core.QuantumError.OperationError(op, _)) ->
                Assert.Equal("Quantum TSP solver", op)
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

        Assert.Equal(0.0, HybridSolver.estimateBackendCostUSD backend 25)

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
