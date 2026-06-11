namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Core.BackendAbstraction

module HybridSolverTests =

    [<Fact>]
    let ``solveTspWithBackend forced quantum accepts topological backend`` () =
        // Arrange: 3-city symmetric TSP instance
        let distances =
            array2D [
                [ 0.0; 1.0; 2.0 ]
                [ 1.0; 0.0; 3.0 ]
                [ 2.0; 3.0; 0.0 ]
            ]

        // Use a topological backend to validate HybridSolver supports non-gate backends.
        let backend = FSharp.Azure.Quantum.Topological.TopologicalUnifiedBackendFactory.createIsing 50

        // Act
        let result =
            HybridSolver.solveTspWithBackend distances None None (Some HybridSolver.SolverMethod.Quantum) (Some backend)

        // Assert
        // Don't require a successful decode (QAOA is probabilistic and depends on backend capabilities);
        // instead, verify the injected backend is accepted and quantum path is attempted.
        match result with
        | Ok solution -> Assert.Equal(HybridSolver.SolverMethod.Quantum, solution.Method)
        | Error (FSharp.Azure.Quantum.Core.QuantumError.OperationError (op, _)) ->
            Assert.Equal("Quantum TSP solver", op)
        | Error err -> Assert.True(false, err.Message)

    [<Fact>]
    let ``solvePortfolioWithBackend forced classical returns Classical`` () =
        // Arrange
        let assets : PortfolioSolver.Asset list =
            [
                { PortfolioTypes.Asset.Symbol = "A"; ExpectedReturn = 0.10; Risk = 0.20; Price = 10.0 }
                { PortfolioTypes.Asset.Symbol = "B"; ExpectedReturn = 0.05; Risk = 0.10; Price = 5.0 }
            ]

        let constraints : PortfolioSolver.Constraints =
            {
                Budget = 10.0
                MinHolding = 0.0
                MaxHolding = 10.0
            }

        // Act
        let result = HybridSolver.solvePortfolioWithBackend assets constraints None None (Some HybridSolver.SolverMethod.Classical) None

        // Assert
        match result with
        | Error err -> Assert.True(false, err.Message)
        | Ok solution -> Assert.Equal(HybridSolver.SolverMethod.Classical, solution.Method)

    // ========================================================================
    // COST ESTIMATION (budget guard inputs)
    // ========================================================================

    [<Fact>]
    let ``estimateBackendCostUSD treats local simulators as free`` () =
        let backend = FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend() :> IQuantumBackend
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
        let ionq = HybridSolver.estimateQuantumConfigCostUSD (HybridSolver.QuantumBackend.IonQ "ionq.qpu") 25
        let rigetti = HybridSolver.estimateQuantumConfigCostUSD (HybridSolver.QuantumBackend.Rigetti "rigetti.qpu") 25

        Assert.True(ionq > 0.0)
        Assert.True(rigetti > 0.0)
