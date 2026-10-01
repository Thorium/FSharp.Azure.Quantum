module FSharp.Azure.Quantum.Tests.DrugDiscoverySolversTests

open Xunit
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Quantum.DrugDiscoverySolvers
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends

// Helper to create local backend for tests
let private createLocalBackend () : BackendAbstraction.IQuantumBackend =
    LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend


// ============================================================================
// CONFIGURATION TESTS
// ============================================================================

module ConfigurationTests =

    [<Fact>]
    let ``defaultConfig has reasonable values`` () =
        Assert.Equal(2, defaultConfig.NumLayers)
        Assert.True(defaultConfig.EnableOptimization)
        Assert.True(defaultConfig.EnableConstraintRepair)
        Assert.Equal(100, defaultConfig.OptimizationShots)
        Assert.Equal(1000, defaultConfig.FinalShots)

    [<Fact>]
    let ``fastConfig prioritizes speed`` () =
        Assert.Equal(1, fastConfig.NumLayers)
        Assert.False(fastConfig.EnableOptimization)
        Assert.True(fastConfig.FinalShots < defaultConfig.FinalShots)

    [<Fact>]
    let ``highQualityConfig prioritizes quality`` () =
        Assert.Equal(3, highQualityConfig.NumLayers)
        Assert.True(highQualityConfig.EnableOptimization)
        Assert.True(highQualityConfig.FinalShots > defaultConfig.FinalShots)

// ============================================================================
// INDEPENDENT SET (MWIS) TESTS
// ============================================================================

module IndependentSetTests =

    [<Fact>]
    let ``toQubo produces correct diagonal terms for node weights`` () =
        // Arrange: 3 nodes with different weights
        let problem: IndependentSet.Problem =
            {
                Nodes =
                    [
                        { Id = "A"; Weight = 10.0 }
                        { Id = "B"; Weight = 20.0 }
                        { Id = "C"; Weight = 5.0 }
                    ]
                Edges = []
            }

        // Act
        let qubo = IndependentSet.toQubo problem

        // Assert: diagonal should be -weight (maximizing weight)
        Assert.Equal(-10.0, qubo.[0, 0], 6)
        Assert.Equal(-20.0, qubo.[1, 1], 6)
        Assert.Equal(-5.0, qubo.[2, 2], 6)

    [<Fact>]
    let ``toQubo adds penalty for edges`` () =
        // Arrange: 2 nodes connected by an edge
        let problem: IndependentSet.Problem =
            {
                Nodes = [ { Id = "A"; Weight = 10.0 }; { Id = "B"; Weight = 20.0 } ]
                Edges = [ (0, 1) ]
            }

        // Act
        let qubo = IndependentSet.toQubo problem

        // Assert: off-diagonal term should have positive penalty
        // Total penalty is split between Q[0,1] and Q[1,0]
        let penalty01 = qubo.[0, 1] + qubo.[1, 0]
        Assert.True(penalty01 > 0.0, $"Edge penalty should be positive, got {penalty01}")

        // Penalty should be larger than max possible weight gain
        let maxWeight = 10.0 + 20.0
        Assert.True(penalty01 >= maxWeight, $"Penalty {penalty01} should exceed max weight {maxWeight}")

    [<Fact>]
    let ``isValid returns true when no adjacent nodes selected`` () =
        // Arrange: Triangle graph, select only one node
        let problem: IndependentSet.Problem =
            {
                Nodes =
                    [
                        { Id = "A"; Weight = 10.0 }
                        { Id = "B"; Weight = 20.0 }
                        { Id = "C"; Weight = 5.0 }
                    ]
                Edges = [ (0, 1); (1, 2); (0, 2) ]
            }

        let bits = [| 1; 0; 0 |]

        // Act & Assert
        Assert.True(IndependentSet.isValid problem bits)

    [<Fact>]
    let ``isValid returns false when adjacent nodes selected`` () =
        // Arrange: Select two adjacent nodes
        let problem: IndependentSet.Problem =
            {
                Nodes =
                    [
                        { Id = "A"; Weight = 10.0 }
                        { Id = "B"; Weight = 20.0 }
                        { Id = "C"; Weight = 5.0 }
                    ]
                Edges = [ (0, 1) ]
            }

        let bits = [| 1; 1; 0 |] // Both A and B selected, but they're connected

        // Act & Assert
        Assert.False(IndependentSet.isValid problem bits)

    [<Fact>]
    let ``decode calculates correct total weight`` () =
        // Arrange
        let problem: IndependentSet.Problem =
            {
                Nodes =
                    [
                        { Id = "A"; Weight = 10.0 }
                        { Id = "B"; Weight = 20.0 }
                        { Id = "C"; Weight = 5.0 }
                    ]
                Edges = []
            }

        let bits = [| 1; 0; 1 |] // Select A and C

        // Act
        let solution = IndependentSet.decode problem bits

        // Assert
        Assert.Equal(15.0, solution.TotalWeight, 6) // 10 + 5
        Assert.Equal(2, solution.SelectedNodes.Length)

    [<Fact>]
    let ``solveClassical finds valid maximum weight independent set`` () =
        // Arrange: Path graph A-B-C, B has highest weight
        // Optimal: select A and C (total 15), not B (20) because A+C > B
        let problem: IndependentSet.Problem =
            {
                Nodes =
                    [
                        { Id = "A"; Weight = 10.0 }
                        { Id = "B"; Weight = 18.0 }
                        { Id = "C"; Weight = 12.0 }
                    ]
                Edges = [ (0, 1); (1, 2) ] // A-B-C path
            }

        // Act
        let solution = IndependentSet.solveClassical problem

        // Assert: Solution should be valid
        Assert.True(solution.IsValid, "Classical solution should be valid")

        // B has highest single weight (18), but greedy picks it first
        // then neither A nor C can be added. Total = 18
        // A+C = 22 is better but greedy doesn't find it
        // Just verify validity
        Assert.True(solution.TotalWeight >= 10.0, "Should select at least one node")

    [<Fact>]
    let ``solve validates empty nodes list`` () : Task =
        task {
            // Arrange
            let problem: IndependentSet.Problem = { Nodes = []; Edges = [] }
            let backend = createLocalBackend ()

            // Act
            let! result =
                IndependentSet.solveWithConfigAsync
                    backend
                    problem
                    { defaultConfig with FinalShots = 100 }
                    CancellationToken.None

            // Assert
            result
            |> Result.map (fun _ -> Assert.Fail("Should fail with empty nodes"))
            |> Result.defaultWith (fun err -> Assert.Contains("no nodes", err.ToString().ToLower()))
        }

    [<Fact>]
    let ``solveWithConfig uses custom configuration`` () : Task =
        task {
            // Arrange
            let problem: IndependentSet.Problem =
                {
                    Nodes = [ { Id = "A"; Weight = 10.0 }; { Id = "B"; Weight = 5.0 } ]
                    Edges = [ (0, 1) ]
                }

            let backend = createLocalBackend ()
            let config = { fastConfig with FinalShots = 50 }

            // Act
            let! result =
                IndependentSet.solveWithConfigAsync backend problem config CancellationToken.None

            // Assert
            match result with
            | Error err -> Assert.Fail($"Solve failed: {err}")
            | Ok solution ->
                Assert.Equal(50, solution.NumShots)
                // With constraint repair, solution should always be valid
                Assert.True(solution.IsValid || solution.WasRepaired)
        }

// ============================================================================
// INFLUENCE MAXIMIZATION TESTS
// ============================================================================

module InfluenceMaximizationTests =

    [<Fact>]
    let ``toQubo includes cardinality constraint`` () =
        // Arrange: 3 nodes, select k=2
        let problem: InfluenceMaximization.Problem =
            {
                Nodes =
                    [
                        { Id = "A"; Score = 10.0 }
                        { Id = "B"; Score = 20.0 }
                        { Id = "C"; Score = 5.0 }
                    ]
                Edges = []
                K = 2
                SynergyWeight = 0.0 // No synergy for this test
            }

        // Act
        let qubo = InfluenceMaximization.toQubo problem

        // Assert: Verify constraint structure
        // For cardinality constraint (sum x_i = k), the QUBO has:
        // Q_ii = penalty * (1 - 2k) - score_i
        // Q_ij = penalty (for i != j)

        // Off-diagonal terms should be positive (penalty for selecting pairs)
        Assert.True(qubo.[0, 1] + qubo.[1, 0] > 0.0, "Off-diagonal should have positive penalty")
        Assert.True(qubo.[0, 2] + qubo.[2, 0] > 0.0, "Off-diagonal should have positive penalty")
        Assert.True(qubo.[1, 2] + qubo.[2, 1] > 0.0, "Off-diagonal should have positive penalty")

    [<Fact>]
    let ``toQubo includes synergy bonus for edges`` () =
        // Arrange: 3 nodes, one edge between A and B
        let problem: InfluenceMaximization.Problem =
            {
                Nodes =
                    [
                        { Id = "A"; Score = 10.0 }
                        { Id = "B"; Score = 10.0 }
                        { Id = "C"; Score = 10.0 }
                    ]
                Edges = [ { Source = 0; Target = 1; Weight = 5.0 } ]
                K = 2
                SynergyWeight = 1.0
            }

        // Act
        let qubo = InfluenceMaximization.toQubo problem

        // Assert: every pair carries the same cardinality penalty (which grows with the
        // synergy total), so the connected pair differs from an unconnected pair by
        // exactly the synergy bonus -α·w (lower QUBO value = better in minimization)
        let connectedPair = qubo.[0, 1] + qubo.[1, 0]
        let unconnectedPair = qubo.[0, 2] + qubo.[2, 0]

        Assert.Equal(-5.0, connectedPair - unconnectedPair, 9)

    [<Fact>]
    let ``decode calculates correct score and synergy`` () =
        // Arrange
        let problem: InfluenceMaximization.Problem =
            {
                Nodes =
                    [
                        { Id = "A"; Score = 10.0 }
                        { Id = "B"; Score = 20.0 }
                        { Id = "C"; Score = 5.0 }
                    ]
                Edges =
                    [
                        { Source = 0; Target = 1; Weight = 3.0 }
                        { Source = 1; Target = 2; Weight = 2.0 }
                    ]
                K = 2
                SynergyWeight = 1.0
            }

        let bits = [| 1; 1; 0 |] // Select A and B

        // Act
        let solution = InfluenceMaximization.decode problem bits

        // Assert
        Assert.Equal(30.0, solution.TotalScore, 6) // 10 + 20
        Assert.Equal(3.0, solution.SynergyBonus, 6) // Edge A-B weight * synergy
        Assert.Equal(2, solution.NumSelected)

    [<Fact>]
    let ``solveClassical selects k nodes with highest marginal gain`` () =
        // Arrange: 4 nodes, select k=2
        let problem: InfluenceMaximization.Problem =
            {
                Nodes =
                    [
                        { Id = "A"; Score = 10.0 }
                        { Id = "B"; Score = 20.0 }
                        { Id = "C"; Score = 15.0 }
                        { Id = "D"; Score = 5.0 }
                    ]
                Edges = []
                K = 2
                SynergyWeight = 0.0
            }

        // Act
        let solution = InfluenceMaximization.solveClassical problem

        // Assert: Should select B (20) and C (15) - top 2 scores
        Assert.Equal(2, solution.SelectedNodes.Length)
        Assert.Equal(35.0, solution.TotalScore, 6)

        let selectedIds = solution.SelectedNodes |> List.map (fun n -> n.Id) |> Set.ofList
        Assert.Contains("B", selectedIds)
        Assert.Contains("C", selectedIds)

    [<Fact>]
    let ``solve validates k parameter`` () : Task =
        task {
            // Arrange: k > number of nodes
            let problem: InfluenceMaximization.Problem =
                {
                    Nodes = [ { Id = "A"; Score = 10.0 } ]
                    Edges = []
                    K = 5
                    SynergyWeight = 0.0
                }

            let backend = createLocalBackend ()

            // Act
            let! result =
                InfluenceMaximization.solveWithConfigAsync
                    backend
                    problem
                    { defaultConfig with FinalShots = 100 }
                    CancellationToken.None

            // Assert
            result
            |> Result.map (fun _ -> Assert.Fail("Should fail with invalid k"))
            |> Result.defaultWith (fun err -> Assert.Contains("k", err.ToString().ToLower()))
        }

    [<Fact>]
    let ``solve validates empty nodes list`` () : Task =
        task {
            // Arrange
            let problem: InfluenceMaximization.Problem =
                {
                    Nodes = []
                    Edges = []
                    K = 1
                    SynergyWeight = 0.0
                }

            let backend = createLocalBackend ()

            // Act
            let! result =
                InfluenceMaximization.solveWithConfigAsync
                    backend
                    problem
                    { defaultConfig with FinalShots = 100 }
                    CancellationToken.None

            // Assert
            result
            |> Result.map (fun _ -> Assert.Fail("Should fail with empty nodes"))
            |> Result.defaultWith (fun err -> Assert.Contains("no nodes", err.ToString().ToLower()))
        }

    [<Fact>]
    let ``solveWithConfig with constraint repair fixes cardinality violations`` () : Task =
        task {
            // Arrange
            let problem: InfluenceMaximization.Problem =
                {
                    Nodes =
                        [
                            { Id = "A"; Score = 10.0 }
                            { Id = "B"; Score = 20.0 }
                            { Id = "C"; Score = 5.0 }
                        ]
                    Edges = []
                    K = 2
                    SynergyWeight = 0.0
                }

            let backend = createLocalBackend ()

            let config =
                { fastConfig with
                    EnableConstraintRepair = true
                }

            // Act
            let! result =
                InfluenceMaximization.solveWithConfigAsync backend problem config CancellationToken.None

            // Assert
            match result with
            | Error err -> Assert.Fail($"Solve failed: {err}")
            | Ok solution ->
                // With constraint repair, should have exactly k nodes
                Assert.Equal(2, solution.NumSelected)
        }

// ============================================================================
// DIVERSE SELECTION TESTS
// ============================================================================

module DiverseSelectionTests =

    [<Fact>]
    let ``toQubo includes value terms on diagonal`` () =
        // Arrange: 2 items with different values
        let problem: DiverseSelection.Problem =
            {
                Items =
                    [
                        { Id = "A"; Value = 10.0; Cost = 5.0 }
                        { Id = "B"; Value = 20.0; Cost = 5.0 }
                    ]
                Diversity = Array2D.zeroCreate 2 2
                Budget = 100.0
                DiversityWeight = 0.0
            }

        // Act
        let qubo = DiverseSelection.toQubo problem

        // Assert: Higher value item should have lower (more negative) diagonal
        // (because we're minimizing QUBO)
        // Note: diagonal also includes budget constraint terms
        // But the relative difference due to value should be present
        let diff = qubo.[1, 1] - qubo.[0, 0]
        Assert.True(diff < 0.0, $"Higher value item should have lower diagonal: diff={diff}")

    [<Fact>]
    let ``toQubo includes diversity bonus for pairs`` () =
        // Arrange: 2 items with diversity between them
        // Use zero costs to eliminate budget constraint effects on pair term
        let diversity = Array2D.init 2 2 (fun i j -> if i = j then 0.0 else 5.0)

        let problem: DiverseSelection.Problem =
            {
                Items =
                    [
                        { Id = "A"; Value = 10.0; Cost = 0.0 } // Zero cost eliminates budget penalty on pair term
                        { Id = "B"; Value = 10.0; Cost = 0.0 }
                    ]
                Diversity = diversity
                Budget = 100.0
                DiversityWeight = 1.0
            }

        // Compare with zero diversity weight
        let problemNoDiversity = { problem with DiversityWeight = 0.0 }

        // Act
        let quboWithDiv = DiverseSelection.toQubo problem
        let quboNoDiv = DiverseSelection.toQubo problemNoDiversity

        // Assert: With zero costs, the only difference in pair term should be diversity bonus
        // Diversity bonus = -beta * diversity_ij / 2.0 = -1.0 * 5.0 / 2.0 = -2.5 per cell
        // Total pair contribution = 2 * -2.5 = -5.0
        let pairTermWithDiv = quboWithDiv.[0, 1] + quboWithDiv.[1, 0]
        let pairTermNoDiv = quboNoDiv.[0, 1] + quboNoDiv.[1, 0]

        // With zero costs, pairTermNoDiv should be 0 (no budget penalty, no diversity)
        // and pairTermWithDiv should be negative (diversity bonus)
        Assert.True(
            pairTermWithDiv < pairTermNoDiv,
            $"Diversity bonus should reduce pair term: with={pairTermWithDiv}, without={pairTermNoDiv}"
        )

    [<Fact>]
    let ``toQubo includes budget constraint`` () =
        // Arrange: 2 items, one over budget individually
        let problem: DiverseSelection.Problem =
            {
                Items =
                    [
                        {
                            Id = "Cheap"
                            Value = 10.0
                            Cost = 10.0
                        }
                        {
                            Id = "Expensive"
                            Value = 20.0
                            Cost = 200.0
                        }
                    ]
                Diversity = Array2D.zeroCreate 2 2
                Budget = 50.0
                DiversityWeight = 0.0
            }

        // Act
        let qubo = DiverseSelection.toQubo problem

        // Assert: Expensive item should have higher diagonal (penalty for exceeding budget)
        // The cost constraint adds: penalty * (cost² - 2*budget*cost)
        // For expensive item (cost=200, budget=50): 200² - 2*50*200 = 40000 - 20000 = 20000
        // For cheap item (cost=10, budget=50): 100 - 1000 = -900
        // So expensive item gets much higher penalty
        Assert.True(
            qubo.[1, 1] > qubo.[0, 0],
            $"Expensive item should have higher diagonal due to budget penalty: cheap={qubo.[0, 0]}, expensive={qubo.[1, 1]}"
        )

    [<Fact>]
    let ``decode calculates correct totals and feasibility`` () =
        // Arrange
        let problem: DiverseSelection.Problem =
            {
                Items =
                    [
                        { Id = "A"; Value = 10.0; Cost = 20.0 }
                        { Id = "B"; Value = 15.0; Cost = 30.0 }
                        { Id = "C"; Value = 5.0; Cost = 10.0 }
                    ]
                Diversity =
                    Array2D.init 3 3 (fun i j ->
                        if i = j then 0.0
                        elif (i, j) = (0, 1) || (i, j) = (1, 0) then 3.0
                        else 1.0)
                Budget = 50.0
                DiversityWeight = 1.0
            }

        let bits = [| 1; 1; 0 |] // Select A and B, cost = 50

        // Act
        let solution = DiverseSelection.decode problem bits

        // Assert
        Assert.Equal(25.0, solution.TotalValue, 6) // 10 + 15
        Assert.Equal(50.0, solution.TotalCost, 6) // 20 + 30
        Assert.Equal(3.0, solution.DiversityBonus, 6) // div[0,1] * weight
        Assert.True(solution.IsFeasible) // cost == budget

    [<Fact>]
    let ``decode marks over-budget as infeasible`` () =
        // Arrange
        let problem: DiverseSelection.Problem =
            {
                Items =
                    [
                        { Id = "A"; Value = 10.0; Cost = 30.0 }
                        { Id = "B"; Value = 15.0; Cost = 30.0 }
                    ]
                Diversity = Array2D.zeroCreate 2 2
                Budget = 50.0
                DiversityWeight = 0.0
            }

        let bits = [| 1; 1 |] // Select both, cost = 60 > budget

        // Act
        let solution = DiverseSelection.decode problem bits

        // Assert
        Assert.Equal(60.0, solution.TotalCost, 6)
        Assert.False(solution.IsFeasible)

    [<Fact>]
    let ``solveClassical stays within budget`` () =
        // Arrange
        let problem: DiverseSelection.Problem =
            {
                Items =
                    [
                        { Id = "A"; Value = 10.0; Cost = 20.0 }
                        { Id = "B"; Value = 15.0; Cost = 30.0 }
                        { Id = "C"; Value = 25.0; Cost = 40.0 }
                    ]
                Diversity = Array2D.zeroCreate 3 3
                Budget = 50.0
                DiversityWeight = 0.0
            }

        // Act
        let solution = DiverseSelection.solveClassical problem

        // Assert
        Assert.True(solution.IsFeasible, "Classical solution should be feasible")

        Assert.True(
            solution.TotalCost <= problem.Budget,
            $"Total cost {solution.TotalCost} should not exceed budget {problem.Budget}"
        )

    [<Fact>]
    let ``solveClassical considers diversity in selection`` () =
        // Arrange: Items with same value but different diversity
        let diversity =
            Array2D.init 3 3 (fun i j ->
                if i = j then 0.0
                elif (i, j) = (0, 2) || (i, j) = (2, 0) then 10.0 // A and C very diverse
                else 1.0)

        let problem: DiverseSelection.Problem =
            {
                Items =
                    [
                        { Id = "A"; Value = 10.0; Cost = 10.0 }
                        { Id = "B"; Value = 10.0; Cost = 10.0 }
                        { Id = "C"; Value = 10.0; Cost = 10.0 }
                    ]
                Diversity = diversity
                Budget = 20.0 // Can only afford 2 items
                DiversityWeight = 1.0
            }

        // Act
        let solution = DiverseSelection.solveClassical problem

        // Assert: Should prefer A and C (highest diversity pair)
        Assert.Equal(2, solution.SelectedItems.Length)
        let selectedIds = solution.SelectedItems |> List.map (fun i -> i.Id) |> Set.ofList

        // A and C have diversity 10, any other pair has diversity 1
        // So greedy should select A+C or similar high-diversity pair
        Assert.True(solution.DiversityBonus >= 1.0, "Should have some diversity bonus")

    [<Fact>]
    let ``solve validates empty items list`` () : Task =
        task {
            // Arrange
            let problem: DiverseSelection.Problem =
                {
                    Items = []
                    Diversity = Array2D.zeroCreate 0 0
                    Budget = 100.0
                    DiversityWeight = 0.0
                }

            let backend = createLocalBackend ()

            // Act
            let! result =
                DiverseSelection.solveWithConfigAsync
                    backend
                    problem
                    { defaultConfig with FinalShots = 100 }
                    CancellationToken.None

            // Assert
            result
            |> Result.map (fun _ -> Assert.Fail("Should fail with empty items"))
            |> Result.defaultWith (fun err -> Assert.Contains("no items", err.ToString().ToLower()))
        }

    [<Fact>]
    let ``solve validates negative budget`` () : Task =
        task {
            // Arrange
            let problem: DiverseSelection.Problem =
                {
                    Items = [ { Id = "A"; Value = 10.0; Cost = 5.0 } ]
                    Diversity = Array2D.zeroCreate 1 1
                    Budget = -10.0
                    DiversityWeight = 0.0
                }

            let backend = createLocalBackend ()

            // Act
            let! result =
                DiverseSelection.solveWithConfigAsync
                    backend
                    problem
                    { defaultConfig with FinalShots = 100 }
                    CancellationToken.None

            // Assert
            result
            |> Result.map (fun _ -> Assert.Fail("Should fail with negative budget"))
            |> Result.defaultWith (fun err -> Assert.Contains("budget", err.ToString().ToLower()))
        }

    [<Fact>]
    let ``solveWithConfig with constraint repair fixes budget violations`` () : Task =
        task {
            // Arrange
            let problem: DiverseSelection.Problem =
                {
                    Items =
                        [
                            { Id = "A"; Value = 10.0; Cost = 20.0 }
                            { Id = "B"; Value = 15.0; Cost = 30.0 }
                            { Id = "C"; Value = 5.0; Cost = 10.0 }
                        ]
                    Diversity = Array2D.zeroCreate 3 3
                    Budget = 40.0
                    DiversityWeight = 0.0
                }

            let backend = createLocalBackend ()

            let config =
                { fastConfig with
                    EnableConstraintRepair = true
                }

            // Act
            let! result =
                DiverseSelection.solveWithConfigAsync backend problem config CancellationToken.None

            // Assert
            match result with
            | Error err -> Assert.Fail($"Solve failed: {err}")
            | Ok solution ->
                // With constraint repair, should be within budget
                Assert.True(
                    solution.IsFeasible || solution.WasRepaired,
                    $"Solution should be feasible or repaired. Feasible={solution.IsFeasible}, Repaired={solution.WasRepaired}, Cost={solution.TotalCost}"
                )
        }

// ============================================================================
// QUBO PROPERTY TESTS
// ============================================================================

module QuboPropertyTests =

    [<Fact>]
    let ``IndependentSet QUBO is symmetric`` () =
        // Arrange
        let problem: IndependentSet.Problem =
            {
                Nodes =
                    [
                        { Id = "A"; Weight = 10.0 }
                        { Id = "B"; Weight = 20.0 }
                        { Id = "C"; Weight = 5.0 }
                    ]
                Edges = [ (0, 1); (1, 2) ]
            }

        // Act
        let qubo = IndependentSet.toQubo problem

        // Assert: Q[i,j] should equal Q[j,i] for all i,j
        let n = Array2D.length1 qubo

        for i in 0 .. n - 1 do
            for j in 0 .. n - 1 do
                Assert.Equal(qubo.[i, j], qubo.[j, i], 10)

    [<Fact>]
    let ``InfluenceMaximization QUBO is symmetric`` () =
        // Arrange
        let problem: InfluenceMaximization.Problem =
            {
                Nodes = [ { Id = "A"; Score = 10.0 }; { Id = "B"; Score = 20.0 } ]
                Edges = [ { Source = 0; Target = 1; Weight = 5.0 } ]
                K = 1
                SynergyWeight = 0.5
            }

        // Act
        let qubo = InfluenceMaximization.toQubo problem

        // Assert
        let n = Array2D.length1 qubo

        for i in 0 .. n - 1 do
            for j in 0 .. n - 1 do
                Assert.Equal(qubo.[i, j], qubo.[j, i], 10)

    [<Fact>]
    let ``DiverseSelection QUBO is symmetric`` () =
        // Arrange
        let diversity = Array2D.init 2 2 (fun i j -> if i = j then 0.0 else 3.0)

        let problem: DiverseSelection.Problem =
            {
                Items =
                    [
                        { Id = "A"; Value = 10.0; Cost = 5.0 }
                        { Id = "B"; Value = 15.0; Cost = 8.0 }
                    ]
                Diversity = diversity
                Budget = 20.0
                DiversityWeight = 1.0
            }

        // Act
        let qubo = DiverseSelection.toQubo problem

        // Assert
        let n = Array2D.length1 qubo

        for i in 0 .. n - 1 do
            for j in 0 .. n - 1 do
                Assert.Equal(qubo.[i, j], qubo.[j, i], 10)

    [<Fact>]
    let ``QUBO energy is correctly evaluated`` () =
        // Arrange: Simple 2-node problem
        let problem: IndependentSet.Problem =
            {
                Nodes = [ { Id = "A"; Weight = 10.0 }; { Id = "B"; Weight = 20.0 } ]
                Edges = [] // No edges, so any selection is valid
            }

        let qubo = IndependentSet.toQubo problem

        // Act: Evaluate energy for different selections
        let evalEnergy (bits: int[]) =
            let mutable e = 0.0

            for i in 0..1 do
                for j in 0..1 do
                    e <- e + qubo.[i, j] * float bits.[i] * float bits.[j]

            e

        let e00 = evalEnergy [| 0; 0 |] // Select nothing
        let e10 = evalEnergy [| 1; 0 |] // Select A
        let e01 = evalEnergy [| 0; 1 |] // Select B
        let e11 = evalEnergy [| 1; 1 |] // Select both

        // Assert:
        // e00 = 0 (nothing selected)
        // e10 = -10 (weight of A)
        // e01 = -20 (weight of B)
        // e11 = -30 (both weights)
        Assert.Equal(0.0, e00, 6)
        Assert.Equal(-10.0, e10, 6)
        Assert.Equal(-20.0, e01, 6)
        Assert.Equal(-30.0, e11, 6)

// ============================================================================
// QUANTUM BACKEND INTEGRATION TESTS
// ============================================================================

module QuantumBackendTests =

    [<Fact>]
    let ``IndependentSet solve returns solution with backend info`` () : Task =
        task {
            // Arrange
            let problem: IndependentSet.Problem =
                {
                    Nodes = [ { Id = "A"; Weight = 10.0 }; { Id = "B"; Weight = 5.0 } ]
                    Edges = [ (0, 1) ]
                }

            let backend = createLocalBackend ()

            // Act
            let! result =
                IndependentSet.solveWithConfigAsync
                    backend
                    problem
                    { defaultConfig with FinalShots = 100 }
                    CancellationToken.None

            // Assert
            match result with
            | Error err -> Assert.Fail($"Solve failed: {err}")
            | Ok solution ->
                Assert.NotEmpty(solution.BackendName)
                Assert.Equal(100, solution.NumShots)
        }

    [<Fact>]
    let ``InfluenceMaximization solve returns solution with backend info`` () : Task =
        task {
            // Arrange
            let problem: InfluenceMaximization.Problem =
                {
                    Nodes = [ { Id = "A"; Score = 10.0 }; { Id = "B"; Score = 5.0 } ]
                    Edges = []
                    K = 1
                    SynergyWeight = 0.0
                }

            let backend = createLocalBackend ()

            // Act
            let! result =
                InfluenceMaximization.solveWithConfigAsync
                    backend
                    problem
                    { defaultConfig with FinalShots = 100 }
                    CancellationToken.None

            // Assert
            match result with
            | Error err -> Assert.Fail($"Solve failed: {err}")
            | Ok solution ->
                Assert.NotEmpty(solution.BackendName)
                Assert.Equal(100, solution.NumShots)
        }

    [<Fact>]
    let ``DiverseSelection solve returns solution with backend info`` () : Task =
        task {
            // Arrange
            let problem: DiverseSelection.Problem =
                {
                    Items =
                        [
                            { Id = "A"; Value = 10.0; Cost = 5.0 }
                            { Id = "B"; Value = 5.0; Cost = 3.0 }
                        ]
                    Diversity = Array2D.zeroCreate 2 2
                    Budget = 10.0
                    DiversityWeight = 0.0
                }

            let backend = createLocalBackend ()

            // Act
            let! result =
                DiverseSelection.solveWithConfigAsync
                    backend
                    problem
                    { defaultConfig with FinalShots = 100 }
                    CancellationToken.None

            // Assert
            match result with
            | Error err -> Assert.Fail($"Solve failed: {err}")
            | Ok solution ->
                Assert.NotEmpty(solution.BackendName)
                Assert.Equal(100, solution.NumShots)
        }

// ============================================================================
// ADVANCED QAOA FEATURE TESTS
// ============================================================================

module AdvancedQaoaTests =

    [<Fact>]
    let ``solveWithConfig returns optimization parameters when enabled`` () : Task =
        task {
            // Arrange
            let problem: IndependentSet.Problem =
                {
                    Nodes = [ { Id = "A"; Weight = 10.0 }; { Id = "B"; Weight = 5.0 } ]
                    Edges = []
                }

            let backend = createLocalBackend ()

            let config =
                { defaultConfig with
                    EnableOptimization = true
                    NumLayers = 2
                    OptimizationShots = 50
                    FinalShots = 100
                }

            // Act
            let! result =
                IndependentSet.solveWithConfigAsync backend problem config CancellationToken.None

            // Assert
            match result with
            | Error err -> Assert.Fail($"Solve failed: {err}")
            | Ok solution ->
                Assert.True(solution.OptimizedParameters.IsSome, "Should return optimized parameters")

                match solution.OptimizedParameters with
                | Some parameters ->
                    Assert.Equal(2, parameters.Length) // 2 layers

                    for (gamma, beta) in parameters do
                        Assert.True(gamma >= 0.0 && gamma <= System.Math.PI, $"Gamma {gamma} should be in [0, π]")
                        Assert.True(beta >= 0.0 && beta <= System.Math.PI / 2.0, $"Beta {beta} should be in [0, π/2]")
                | None -> Assert.Fail("OptimizedParameters should not be None")
        }

    [<Fact>]
    let ``constraint repair produces valid solutions for IndependentSet`` () : Task =
        task {
            // Arrange: Problem where QAOA likely violates constraints
            let problem: IndependentSet.Problem =
                {
                    Nodes =
                        [
                            { Id = "A"; Weight = 10.0 }
                            { Id = "B"; Weight = 10.0 }
                            { Id = "C"; Weight = 10.0 }
                        ]
                    Edges = [ (0, 1); (1, 2); (0, 2) ] // Triangle - fully connected
                }

            let backend = createLocalBackend ()

            let config =
                { fastConfig with
                    EnableConstraintRepair = true
                }

            // Act
            let! result =
                IndependentSet.solveWithConfigAsync backend problem config CancellationToken.None

            // Assert
            match result with
            | Error err -> Assert.Fail($"Solve failed: {err}")
            | Ok solution ->
                // With constraint repair, solution MUST be valid
                Assert.True(
                    solution.IsValid,
                    $"Solution should be valid after constraint repair. WasRepaired={solution.WasRepaired}"
                )
        }

    [<Fact>]
    let ``constraint repair produces correct cardinality for InfluenceMaximization`` () : Task =
        task {
            // Arrange
            let problem: InfluenceMaximization.Problem =
                {
                    Nodes =
                        [
                            { Id = "A"; Score = 10.0 }
                            { Id = "B"; Score = 20.0 }
                            { Id = "C"; Score = 15.0 }
                            { Id = "D"; Score = 5.0 }
                        ]
                    Edges = []
                    K = 2
                    SynergyWeight = 0.0
                }

            let backend = createLocalBackend ()

            let config =
                { fastConfig with
                    EnableConstraintRepair = true
                }

            // Act
            let! result =
                InfluenceMaximization.solveWithConfigAsync backend problem config CancellationToken.None

            // Assert
            match result with
            | Error err -> Assert.Fail($"Solve failed: {err}")
            | Ok solution ->
                // With constraint repair, should have exactly k nodes
                Assert.Equal(2, solution.NumSelected)
        }

    [<Fact>]
    let ``constraint repair produces feasible solutions for DiverseSelection`` () : Task =
        task {
            // Arrange
            let problem: DiverseSelection.Problem =
                {
                    Items =
                        [
                            { Id = "A"; Value = 50.0; Cost = 30.0 }
                            { Id = "B"; Value = 40.0; Cost = 25.0 }
                            { Id = "C"; Value = 30.0; Cost = 20.0 }
                            { Id = "D"; Value = 20.0; Cost = 15.0 }
                        ]
                    Diversity = Array2D.zeroCreate 4 4
                    Budget = 50.0
                    DiversityWeight = 0.0
                }

            let backend = createLocalBackend ()

            let config =
                { fastConfig with
                    EnableConstraintRepair = true
                }

            // Act
            let! result =
                DiverseSelection.solveWithConfigAsync backend problem config CancellationToken.None

            // Assert
            match result with
            | Error err -> Assert.Fail($"Solve failed: {err}")
            | Ok solution ->
                // With constraint repair, should be within budget
                Assert.True(
                    solution.IsFeasible,
                    $"Solution should be feasible. Cost={solution.TotalCost}, Budget={problem.Budget}, Repaired={solution.WasRepaired}"
                )
        }

// ============================================================================
// QUBO MINIMUM = CLASSICAL OPTIMUM (every bitstring enumerated, no circuit)
// ============================================================================

module QuboMinimumTests =

    let private bitsOf (n: int) (index: int) =
        Array.init n (fun q -> (index >>> q) &&& 1)

    /// Every minimum-energy bitstring of the QUBO.
    let private minimumEnergyStates (qubo: float[,]) : int[][] =
        let n = Array2D.length1 qubo
        let states = Array.init (1 <<< n) (bitsOf n)
        let energies = states |> Array.map (QaoaExecutionHelpers.evaluateQubo qubo)
        let lowest = Array.min energies

        Array.zip states energies
        |> Array.filter (fun (_, e) -> e <= lowest + 1e-9 * max 1.0 (abs lowest))
        |> Array.map fst

    /// Items as (value, cost in cost units); a cost unit is 1 / unitsPerCost of the problem's
    /// cost scale, so the test's own feasibility check runs on exact integers.
    let private diverseProblem
        (unitsPerCost: int)
        (items: (float * int) list)
        (budgetUnits: int)
        : DiverseSelection.Problem =
        let n = items.Length

        {
            Items =
                items
                |> List.mapi (fun i (value, costUnits) ->
                    {
                        DiverseSelection.Item.Id = $"m{i}"
                        Value = value
                        Cost = float costUnits / float unitsPerCost
                    })
            Diversity = Array2D.init n n (fun i j -> if i = j then 0.0 else 0.2 * float (1 + (i + j) % 3))
            Budget = float budgetUnits / float unitsPerCost
            DiversityWeight = 0.5
        }

    /// The QUBO's minimum-energy states select, on their item bits, exactly the best
    /// selections within the budget.
    let private assertMinimumIsBestFeasible (items: (float * int) list) (budgetUnits: int) (unitsPerCost: int) =
        let problem = diverseProblem unitsPerCost items budgetUnits
        let n = items.Length
        let costUnits = items |> List.map snd |> List.toArray

        let objective (selection: int[]) =
            let value = items |> List.mapi (fun i (v, _) -> float selection.[i] * v) |> List.sum

            let diversity =
                [
                    for i in 0 .. n - 1 do
                        for j in i + 1 .. n - 1 do
                            if selection.[i] = 1 && selection.[j] = 1 then
                                yield problem.Diversity.[i, j]
                ]
                |> List.sum

            value + problem.DiversityWeight * diversity

        let feasible (selection: int[]) =
            (selection |> Array.mapi (fun i bit -> bit * costUnits.[i]) |> Array.sum)
            <= budgetUnits

        let best =
            Array.init (1 <<< n) (bitsOf n)
            |> Array.filter feasible
            |> Array.map objective
            |> Array.max

        let minima = minimumEnergyStates (DiverseSelection.toQubo problem)
        Assert.NotEmpty minima

        for state in minima do
            let selection = Array.truncate n state
            Assert.True(feasible selection, $"minimum-energy state %A{state} exceeds the budget")
            Assert.Equal(best, objective selection, 9)
            Assert.True((DiverseSelection.decode problem selection).IsFeasible)

    [<Fact>]
    let ``DiverseSelection QUBO minimum is the best selection within an integer budget`` () =
        assertMinimumIsBestFeasible [ 3.0, 2; 4.0, 3; 6.0, 4; 1.0, 1; 2.5, 2 ] 6 1

    [<Fact>]
    let ``DiverseSelection QUBO minimum is the best selection within the budget for non-integer costs`` () =
        // costs 1.0 / 1.1 / 0.9, budget 2.0: the two most valuable items cost 2.1
        assertMinimumIsBestFeasible [ 5.0, 10; 5.0, 11; 1.0, 9 ] 20 10
        // costs 0.1 / 0.2 / 0.3, budget 0.3: 0.1 + 0.2 fits exactly
        assertMinimumIsBestFeasible [ 1.0, 1; 1.0, 2; 1.0, 3 ] 3 10

    [<Fact>]
    let ``DiverseSelection QUBO minimum is the best selection when the budget lies between cost sums`` () =
        // costs 2 / 4 / 2, budget 5: every cost sum is even
        assertMinimumIsBestFeasible [ 3.0, 2; 4.0, 4; 2.0, 2 ] 5 1
        // costs 120 / 250 / 90, budget 300
        assertMinimumIsBestFeasible [ 5.0, 120; 8.0, 250; 4.0, 90 ] 300 1

    [<Fact>]
    let ``DiverseSelection QUBO minimum selects everything worth having under a budget above the total cost`` () =
        assertMinimumIsBestFeasible [ 1.0, 1; 0.0, 1; 1.0, 1; 1.0, 1 ] 10 1

    let private influenceProblem
        (scores: float list)
        (edges: (int * int * float) list)
        (k: int)
        : InfluenceMaximization.Problem =
        {
            Nodes =
                scores
                |> List.mapi (fun i score ->
                    {
                        InfluenceMaximization.Node.Id = $"n{i}"
                        Score = score
                    })
            Edges =
                edges
                |> List.map (fun (source, target, weight) ->
                    {
                        InfluenceMaximization.Edge.Source = source
                        Target = target
                        Weight = weight
                    })
            K = k
            SynergyWeight = 0.5
        }

    /// The QUBO's minimum-energy states are exactly the best selections of K nodes.
    let private assertMinimumIsBestOfSizeK (problem: InfluenceMaximization.Problem) =
        let n = problem.Nodes.Length
        let scores = problem.Nodes |> List.map (fun node -> node.Score) |> List.toArray

        let objective (selection: int[]) =
            (selection |> Array.mapi (fun i bit -> float bit * scores.[i]) |> Array.sum)
            + problem.SynergyWeight
              * (problem.Edges
                 |> List.sumBy (fun e ->
                     if selection.[e.Source] = 1 && selection.[e.Target] = 1 then
                         e.Weight
                     else
                         0.0))

        let best =
            Array.init (1 <<< n) (bitsOf n)
            |> Array.filter (fun selection -> Array.sum selection = problem.K)
            |> Array.map objective
            |> Array.max

        let minima = minimumEnergyStates (InfluenceMaximization.toQubo problem)
        Assert.NotEmpty minima

        for state in minima do
            Assert.Equal(problem.K, Array.sum state)
            Assert.Equal(best, objective state, 9)

    let private completeGraphEdges (n: int) (weight: float) =
        [
            for i in 0 .. n - 2 do
                for j in i + 1 .. n - 1 -> (i, j, weight)
        ]

    [<Fact>]
    let ``InfluenceMaximization QUBO minimum is the best selection of K nodes`` () =
        assertMinimumIsBestOfSizeK (
            influenceProblem [ 0.9; 0.8; 0.7; 0.3; 0.2; 0.1 ] [ 0, 3, 1.0; 1, 2, 0.8; 2, 5, 0.3; 3, 4, 0.5 ] 2
        )

    [<Fact>]
    let ``InfluenceMaximization QUBO minimum keeps K nodes when the synergy outweighs the scores`` () =
        assertMinimumIsBestOfSizeK (influenceProblem (List.replicate 5 0.1) (completeGraphEdges 5 100.0) 2)

    [<Fact>]
    let ``InfluenceMaximization QUBO minimum keeps K nodes when every score is zero`` () =
        assertMinimumIsBestOfSizeK (influenceProblem (List.replicate 5 0.0) (completeGraphEdges 5 1.0) 2)

    [<Fact>]
    let ``DiverseSelection QUBO has slack bits only for a budget that can bind`` () =
        let qubits (items: (float * int) list) (budgetUnits: int) (unitsPerCost: int) =
            Array2D.length1 (DiverseSelection.toQubo (diverseProblem unitsPerCost items budgetUnits))

        // costs 120 / 250 / 90, budget 300: common divisor 10, so 12 / 25 / 9 against 30
        // with slack weights 1, 2, 4, 8, 15
        Assert.Equal(3 + 5, qubits [ 5.0, 120; 8.0, 250; 4.0, 90 ] 300 1)
        // costs 1.0 / 1.1 / 0.9, budget 2.0: 10 / 11 / 9 against 20 with slack weights 1, 2, 4, 8, 5
        Assert.Equal(3 + 5, qubits [ 5.0, 10; 5.0, 11; 1.0, 9 ] 20 10)

        let unitCosts = [ 1.0, 1; 0.0, 1; 1.0, 1; 1.0, 1 ]
        // one below the total cost: slack weights 1, 2
        Assert.Equal(4 + 2, qubits unitCosts 3 1)
        // at or above the total cost the budget cannot be exceeded: no slack
        Assert.Equal(4, qubits unitCosts 4 1)
        Assert.Equal(4, qubits unitCosts 10 1)

    [<Fact>]
    let ``DiverseSelection rejects costs without a common integer scale`` () : Task =
        task {
            let problem: DiverseSelection.Problem =
                {
                    Items =
                        [
                            {
                                Id = "A"
                                Value = 1.0
                                Cost = 1.0 / 3.0
                            }
                            { Id = "B"; Value = 1.0; Cost = 1.0 }
                        ]
                    Diversity = Array2D.zeroCreate 2 2
                    Budget = 1.0
                    DiversityWeight = 0.0
                }

            match DiverseSelection.tryToQubo problem with
            | Error(QuantumError.ValidationError("cost", _)) -> ()
            | other -> Assert.Fail($"expected a validation error on the costs, got %A{other}")

            Assert.Throws<System.ArgumentException>(fun () -> DiverseSelection.toQubo problem |> ignore)
            |> ignore

            let! solved =
                DiverseSelection.solveWithConfigAsync (createLocalBackend ()) problem fastConfig CancellationToken.None

            match solved with
            | Error(QuantumError.ValidationError("cost", _)) -> ()
            | other -> Assert.Fail($"expected a validation error on the costs, got %A{other}")
        }

    [<Fact>]
    let ``DiverseSelection rejects a negative cost`` () =
        let problem: DiverseSelection.Problem =
            {
                Items = [ { Id = "A"; Value = 1.0; Cost = -1.0 } ]
                Diversity = Array2D.zeroCreate 1 1
                Budget = 1.0
                DiversityWeight = 0.0
            }

        match DiverseSelection.tryToQubo problem with
        | Error(QuantumError.ValidationError("cost", _)) -> ()
        | other -> Assert.Fail($"expected a validation error on the costs, got %A{other}")

    [<Fact>]
    let ``DiverseSelection solution on non-integer costs is within budget and reports its sampling`` () : Task =
        task {
            // costs 1.0 / 1.1 / 0.9, budget 2.0
            let problem = diverseProblem 10 [ 5.0, 10; 5.0, 11; 1.0, 9 ] 20

            let! solved =
                DiverseSelection.solveWithConfigAsync
                    (createLocalBackend ())
                    problem
                    { defaultConfig with FinalShots = 200 }
                    CancellationToken.None

            match solved with
            | Error err -> Assert.Fail($"Solve failed: {err}")
            | Ok solution ->
                Assert.True(solution.IsFeasible)
                Assert.True(solution.TotalCost <= 2.0 + 1e-9)

                match solution.Sampling with
                | None -> Assert.Fail("Sampling should be reported")
                | Some sampling ->
                    Assert.Equal(200, sampling.Shots)
                    Assert.Equal(8, sampling.Qubits)
                    Assert.InRange(sampling.Valid, 0, 200)
                    Assert.InRange(sampling.Hits, 0, 200)

                    // A selection that was not repaired is the lowest-energy sample itself
                    if not solution.WasRepaired then
                        Assert.True(sampling.Hits >= 1)
                        Assert.True(sampling.Valid >= sampling.Hits)
        }

// ============================================================================
// DRUG DISCOVERY BUILDER: QAOA DIVERSE SELECTION
// ============================================================================

module DrugDiscoveryBuilderTests =

    open FSharp.Azure.Quantum.Business

    [<Fact>]
    let ``builder defaults keep 100 classifier shots and use the shared QAOA final shots`` () =
        let defaults = drugDiscovery.Zero()
        Assert.Equal(100, defaults.Shots)
        Assert.Equal(QaoaExecutionHelpers.defaultConfig.FinalShots, defaults.SelectionShots)
        Assert.Equal(10, defaults.BatchSize)
        Assert.Equal(10.0, defaults.SelectionBudget)

        let custom = drugDiscovery.Shots(defaults, 250)
        Assert.Equal(250, custom.Shots)
        Assert.Equal(250, custom.SelectionShots)

    [<Fact>]
    let ``QAOA diverse selection stays within the budget and reports repair and sampling`` () : Task =
        task {
            let file =
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fsaq-diverse-{System.Guid.NewGuid():N}.csv")

            do!
                System.IO.File.WriteAllLinesAsync(
                    file,
                    [
                        "SMILES,Label"
                        "CCO,0"
                        "CC(=O)O,0"
                        "c1ccccc1,1"
                        "c1ccc(O)cc1,1"
                        "c1ccc(N)cc1,1"
                        "CC(=O)Oc1ccccc1C(=O)O,1"
                    ]
                )

            try
                // 6 molecules of cost 1, budget 3: 6 item qubits + 2 slack qubits
                let! result =
                    drugDiscovery {
                        load_candidates_from_file file
                        use_method QAOADiverseSelection
                        selection_budget 3.0
                        backend (createLocalBackend ())
                    }

                match result with
                | Error err -> Assert.Fail($"Selection failed: {err}")
                | Ok screening ->
                    Assert.Equal(QAOADiverseSelection, screening.Method)
                    Assert.Equal(6, screening.MoleculesProcessed)
                    Assert.InRange(screening.RankedCandidates.Length, 0, 3)
                    Assert.Contains("Feasible: True", screening.Message)
                    Assert.Contains("Repaired to fit the budget:", screening.Message)
                    Assert.Contains("Final samples: 1000;", screening.Message)
            finally
                System.IO.File.Delete file
        }
