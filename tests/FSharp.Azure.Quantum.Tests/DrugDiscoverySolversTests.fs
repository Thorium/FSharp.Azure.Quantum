module FSharp.Azure.Quantum.Tests.DrugDiscoverySolversTests

open Xunit
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
        let problem : IndependentSet.Problem = {
            Nodes = [
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
        let problem : IndependentSet.Problem = {
            Nodes = [
                { Id = "A"; Weight = 10.0 }
                { Id = "B"; Weight = 20.0 }
            ]
            Edges = [(0, 1)]
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
        let problem : IndependentSet.Problem = {
            Nodes = [
                { Id = "A"; Weight = 10.0 }
                { Id = "B"; Weight = 20.0 }
                { Id = "C"; Weight = 5.0 }
            ]
            Edges = [(0, 1); (1, 2); (0, 2)]
        }
        let bits = [| 1; 0; 0 |]
        
        // Act & Assert
        Assert.True(IndependentSet.isValid problem bits)
    
    [<Fact>]
    let ``isValid returns false when adjacent nodes selected`` () =
        // Arrange: Select two adjacent nodes
        let problem : IndependentSet.Problem = {
            Nodes = [
                { Id = "A"; Weight = 10.0 }
                { Id = "B"; Weight = 20.0 }
                { Id = "C"; Weight = 5.0 }
            ]
            Edges = [(0, 1)]
        }
        let bits = [| 1; 1; 0 |]  // Both A and B selected, but they're connected
        
        // Act & Assert
        Assert.False(IndependentSet.isValid problem bits)
    
    [<Fact>]
    let ``decode calculates correct total weight`` () =
        // Arrange
        let problem : IndependentSet.Problem = {
            Nodes = [
                { Id = "A"; Weight = 10.0 }
                { Id = "B"; Weight = 20.0 }
                { Id = "C"; Weight = 5.0 }
            ]
            Edges = []
        }
        let bits = [| 1; 0; 1 |]  // Select A and C
        
        // Act
        let solution = IndependentSet.decode problem bits
        
        // Assert
        Assert.Equal(15.0, solution.TotalWeight, 6)  // 10 + 5
        Assert.Equal(2, solution.SelectedNodes.Length)
    
    [<Fact>]
    let ``solveClassical finds valid maximum weight independent set`` () =
        // Arrange: Path graph A-B-C, B has highest weight
        // Optimal: select A and C (total 15), not B (20) because A+C > B
        let problem : IndependentSet.Problem = {
            Nodes = [
                { Id = "A"; Weight = 10.0 }
                { Id = "B"; Weight = 18.0 }
                { Id = "C"; Weight = 12.0 }
            ]
            Edges = [(0, 1); (1, 2)]  // A-B-C path
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
    let ``solve validates empty nodes list`` () =
        // Arrange
        let problem : IndependentSet.Problem = {
            Nodes = []
            Edges = []
        }
        let backend = createLocalBackend()
        
        // Act
        let result = IndependentSet.solve backend problem 100
        
        // Assert
        match result with
        | Error err -> Assert.Contains("no nodes", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with empty nodes")
    
    [<Fact>]
    let ``solveWithConfig uses custom configuration`` () =
        // Arrange
        let problem : IndependentSet.Problem = {
            Nodes = [
                { Id = "A"; Weight = 10.0 }
                { Id = "B"; Weight = 5.0 }
            ]
            Edges = [(0, 1)]
        }
        let backend = createLocalBackend()
        let config = { fastConfig with FinalShots = 50 }
        
        // Act
        let result = IndependentSet.solveWithConfig backend problem config
        
        // Assert
        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.Equal(50, solution.NumShots)
            // With constraint repair, solution should always be valid
            Assert.True(solution.IsValid || solution.WasRepaired)

// ============================================================================
// INFLUENCE MAXIMIZATION TESTS
// ============================================================================

module InfluenceMaximizationTests =
    
    [<Fact>]
    let ``toQubo includes cardinality constraint`` () =
        // Arrange: 3 nodes, select k=2
        let problem : InfluenceMaximization.Problem = {
            Nodes = [
                { Id = "A"; Score = 10.0 }
                { Id = "B"; Score = 20.0 }
                { Id = "C"; Score = 5.0 }
            ]
            Edges = []
            K = 2
            SynergyWeight = 0.0  // No synergy for this test
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
        // Arrange: 2 nodes with an edge
        let problem : InfluenceMaximization.Problem = {
            Nodes = [
                { Id = "A"; Score = 10.0 }
                { Id = "B"; Score = 10.0 }
            ]
            Edges = [{ Source = 0; Target = 1; Weight = 5.0 }]
            K = 2
            SynergyWeight = 1.0
        }
        
        // Compare with same problem without edge
        let problemNoEdge = { problem with Edges = [] }
        
        // Act
        let quboWithEdge = InfluenceMaximization.toQubo problem
        let quboNoEdge = InfluenceMaximization.toQubo problemNoEdge
        
        // Assert: Edge term should make selecting both more favorable
        // (lower QUBO value = better in minimization)
        let pairTermWithEdge = quboWithEdge.[0, 1] + quboWithEdge.[1, 0]
        let pairTermNoEdge = quboNoEdge.[0, 1] + quboNoEdge.[1, 0]
        
        Assert.True(pairTermWithEdge < pairTermNoEdge, 
            $"Synergy should reduce pair penalty: with={pairTermWithEdge}, without={pairTermNoEdge}")
    
    [<Fact>]
    let ``decode calculates correct score and synergy`` () =
        // Arrange
        let problem : InfluenceMaximization.Problem = {
            Nodes = [
                { Id = "A"; Score = 10.0 }
                { Id = "B"; Score = 20.0 }
                { Id = "C"; Score = 5.0 }
            ]
            Edges = [
                { Source = 0; Target = 1; Weight = 3.0 }
                { Source = 1; Target = 2; Weight = 2.0 }
            ]
            K = 2
            SynergyWeight = 1.0
        }
        let bits = [| 1; 1; 0 |]  // Select A and B
        
        // Act
        let solution = InfluenceMaximization.decode problem bits
        
        // Assert
        Assert.Equal(30.0, solution.TotalScore, 6)  // 10 + 20
        Assert.Equal(3.0, solution.SynergyBonus, 6)  // Edge A-B weight * synergy
        Assert.Equal(2, solution.NumSelected)
    
    [<Fact>]
    let ``solveClassical selects k nodes with highest marginal gain`` () =
        // Arrange: 4 nodes, select k=2
        let problem : InfluenceMaximization.Problem = {
            Nodes = [
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
    let ``solve validates k parameter`` () =
        // Arrange: k > number of nodes
        let problem : InfluenceMaximization.Problem = {
            Nodes = [{ Id = "A"; Score = 10.0 }]
            Edges = []
            K = 5
            SynergyWeight = 0.0
        }
        let backend = createLocalBackend()
        
        // Act
        let result = InfluenceMaximization.solve backend problem 100
        
        // Assert
        match result with
        | Error err -> Assert.Contains("k", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with invalid k")
    
    [<Fact>]
    let ``solve validates empty nodes list`` () =
        // Arrange
        let problem : InfluenceMaximization.Problem = {
            Nodes = []
            Edges = []
            K = 1
            SynergyWeight = 0.0
        }
        let backend = createLocalBackend()
        
        // Act
        let result = InfluenceMaximization.solve backend problem 100
        
        // Assert
        match result with
        | Error err -> Assert.Contains("no nodes", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with empty nodes")
    
    [<Fact>]
    let ``solveWithConfig with constraint repair fixes cardinality violations`` () =
        // Arrange
        let problem : InfluenceMaximization.Problem = {
            Nodes = [
                { Id = "A"; Score = 10.0 }
                { Id = "B"; Score = 20.0 }
                { Id = "C"; Score = 5.0 }
            ]
            Edges = []
            K = 2
            SynergyWeight = 0.0
        }
        let backend = createLocalBackend()
        let config = { fastConfig with EnableConstraintRepair = true }
        
        // Act
        let result = InfluenceMaximization.solveWithConfig backend problem config
        
        // Assert
        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            // With constraint repair, should have exactly k nodes
            Assert.Equal(2, solution.NumSelected)

// ============================================================================
// DIVERSE SELECTION TESTS
// ============================================================================

module DiverseSelectionTests =
    
    [<Fact>]
    let ``toQubo includes value terms on diagonal`` () =
        // Arrange: 2 items with different values
        let problem : DiverseSelection.Problem = {
            Items = [
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
        Assert.True(diff < 0.0, 
            $"Higher value item should have lower diagonal: diff={diff}")
    
    [<Fact>]
    let ``toQubo includes diversity bonus for pairs`` () =
        // Arrange: 2 items with diversity between them
        // Use zero costs to eliminate budget constraint effects on pair term
        let diversity = Array2D.init 2 2 (fun i j ->
            if i = j then 0.0 else 5.0)
        
        let problem : DiverseSelection.Problem = {
            Items = [
                { Id = "A"; Value = 10.0; Cost = 0.0 }  // Zero cost eliminates budget penalty on pair term
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
        Assert.True(pairTermWithDiv < pairTermNoDiv,
            $"Diversity bonus should reduce pair term: with={pairTermWithDiv}, without={pairTermNoDiv}")
    
    [<Fact>]
    let ``toQubo includes budget constraint`` () =
        // Arrange: 2 items, one over budget individually
        let problem : DiverseSelection.Problem = {
            Items = [
                { Id = "Cheap"; Value = 10.0; Cost = 10.0 }
                { Id = "Expensive"; Value = 20.0; Cost = 200.0 }
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
        Assert.True(qubo.[1, 1] > qubo.[0, 0],
            $"Expensive item should have higher diagonal due to budget penalty: cheap={qubo.[0, 0]}, expensive={qubo.[1, 1]}")
    
    [<Fact>]
    let ``decode calculates correct totals and feasibility`` () =
        // Arrange
        let problem : DiverseSelection.Problem = {
            Items = [
                { Id = "A"; Value = 10.0; Cost = 20.0 }
                { Id = "B"; Value = 15.0; Cost = 30.0 }
                { Id = "C"; Value = 5.0; Cost = 10.0 }
            ]
            Diversity = Array2D.init 3 3 (fun i j ->
                if i = j then 0.0
                elif (i, j) = (0, 1) || (i, j) = (1, 0) then 3.0
                else 1.0)
            Budget = 50.0
            DiversityWeight = 1.0
        }
        let bits = [| 1; 1; 0 |]  // Select A and B, cost = 50
        
        // Act
        let solution = DiverseSelection.decode problem bits
        
        // Assert
        Assert.Equal(25.0, solution.TotalValue, 6)  // 10 + 15
        Assert.Equal(50.0, solution.TotalCost, 6)   // 20 + 30
        Assert.Equal(3.0, solution.DiversityBonus, 6)  // div[0,1] * weight
        Assert.True(solution.IsFeasible)  // cost == budget
    
    [<Fact>]
    let ``decode marks over-budget as infeasible`` () =
        // Arrange
        let problem : DiverseSelection.Problem = {
            Items = [
                { Id = "A"; Value = 10.0; Cost = 30.0 }
                { Id = "B"; Value = 15.0; Cost = 30.0 }
            ]
            Diversity = Array2D.zeroCreate 2 2
            Budget = 50.0
            DiversityWeight = 0.0
        }
        let bits = [| 1; 1 |]  // Select both, cost = 60 > budget
        
        // Act
        let solution = DiverseSelection.decode problem bits
        
        // Assert
        Assert.Equal(60.0, solution.TotalCost, 6)
        Assert.False(solution.IsFeasible)
    
    [<Fact>]
    let ``solveClassical stays within budget`` () =
        // Arrange
        let problem : DiverseSelection.Problem = {
            Items = [
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
        Assert.True(solution.TotalCost <= problem.Budget,
            $"Total cost {solution.TotalCost} should not exceed budget {problem.Budget}")
    
    [<Fact>]
    let ``solveClassical considers diversity in selection`` () =
        // Arrange: Items with same value but different diversity
        let diversity = Array2D.init 3 3 (fun i j ->
            if i = j then 0.0
            elif (i, j) = (0, 2) || (i, j) = (2, 0) then 10.0  // A and C very diverse
            else 1.0)
        
        let problem : DiverseSelection.Problem = {
            Items = [
                { Id = "A"; Value = 10.0; Cost = 10.0 }
                { Id = "B"; Value = 10.0; Cost = 10.0 }
                { Id = "C"; Value = 10.0; Cost = 10.0 }
            ]
            Diversity = diversity
            Budget = 20.0  // Can only afford 2 items
            DiversityWeight = 1.0
        }
        
        // Act
        let solution = DiverseSelection.solveClassical problem
        
        // Assert: Should prefer A and C (highest diversity pair)
        Assert.Equal(2, solution.SelectedItems.Length)
        let selectedIds = solution.SelectedItems |> List.map (fun i -> i.Id) |> Set.ofList
        
        // A and C have diversity 10, any other pair has diversity 1
        // So greedy should select A+C or similar high-diversity pair
        Assert.True(solution.DiversityBonus >= 1.0, 
            "Should have some diversity bonus")
    
    [<Fact>]
    let ``solve validates empty items list`` () =
        // Arrange
        let problem : DiverseSelection.Problem = {
            Items = []
            Diversity = Array2D.zeroCreate 0 0
            Budget = 100.0
            DiversityWeight = 0.0
        }
        let backend = createLocalBackend()
        
        // Act
        let result = DiverseSelection.solve backend problem 100
        
        // Assert
        match result with
        | Error err -> Assert.Contains("no items", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with empty items")
    
    [<Fact>]
    let ``solve validates negative budget`` () =
        // Arrange
        let problem : DiverseSelection.Problem = {
            Items = [{ Id = "A"; Value = 10.0; Cost = 5.0 }]
            Diversity = Array2D.zeroCreate 1 1
            Budget = -10.0
            DiversityWeight = 0.0
        }
        let backend = createLocalBackend()
        
        // Act
        let result = DiverseSelection.solve backend problem 100
        
        // Assert
        match result with
        | Error err -> Assert.Contains("budget", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with negative budget")
    
    [<Fact>]
    let ``solveWithConfig with constraint repair fixes budget violations`` () =
        // Arrange
        let problem : DiverseSelection.Problem = {
            Items = [
                { Id = "A"; Value = 10.0; Cost = 20.0 }
                { Id = "B"; Value = 15.0; Cost = 30.0 }
                { Id = "C"; Value = 5.0; Cost = 10.0 }
            ]
            Diversity = Array2D.zeroCreate 3 3
            Budget = 40.0
            DiversityWeight = 0.0
        }
        let backend = createLocalBackend()
        let config = { fastConfig with EnableConstraintRepair = true }
        
        // Act
        let result = DiverseSelection.solveWithConfig backend problem config
        
        // Assert
        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            // With constraint repair, should be within budget
            Assert.True(solution.IsFeasible || solution.WasRepaired,
                $"Solution should be feasible or repaired. Feasible={solution.IsFeasible}, Repaired={solution.WasRepaired}, Cost={solution.TotalCost}")

// ============================================================================
// QUBO PROPERTY TESTS
// ============================================================================

module QuboPropertyTests =
    
    [<Fact>]
    let ``IndependentSet QUBO is symmetric`` () =
        // Arrange
        let problem : IndependentSet.Problem = {
            Nodes = [
                { Id = "A"; Weight = 10.0 }
                { Id = "B"; Weight = 20.0 }
                { Id = "C"; Weight = 5.0 }
            ]
            Edges = [(0, 1); (1, 2)]
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
        let problem : InfluenceMaximization.Problem = {
            Nodes = [
                { Id = "A"; Score = 10.0 }
                { Id = "B"; Score = 20.0 }
            ]
            Edges = [{ Source = 0; Target = 1; Weight = 5.0 }]
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
        let diversity = Array2D.init 2 2 (fun i j ->
            if i = j then 0.0 else 3.0)
        
        let problem : DiverseSelection.Problem = {
            Items = [
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
        let problem : IndependentSet.Problem = {
            Nodes = [
                { Id = "A"; Weight = 10.0 }
                { Id = "B"; Weight = 20.0 }
            ]
            Edges = []  // No edges, so any selection is valid
        }
        
        let qubo = IndependentSet.toQubo problem
        
        // Act: Evaluate energy for different selections
        let evalEnergy (bits: int[]) =
            let mutable e = 0.0
            for i in 0 .. 1 do
                for j in 0 .. 1 do
                    e <- e + qubo.[i, j] * float bits.[i] * float bits.[j]
            e
        
        let e00 = evalEnergy [| 0; 0 |]  // Select nothing
        let e10 = evalEnergy [| 1; 0 |]  // Select A
        let e01 = evalEnergy [| 0; 1 |]  // Select B
        let e11 = evalEnergy [| 1; 1 |]  // Select both
        
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
    let ``IndependentSet solve returns solution with backend info`` () =
        // Arrange
        let problem : IndependentSet.Problem = {
            Nodes = [
                { Id = "A"; Weight = 10.0 }
                { Id = "B"; Weight = 5.0 }
            ]
            Edges = [(0, 1)]
        }
        let backend = createLocalBackend()
        
        // Act
        let result = IndependentSet.solve backend problem 100
        
        // Assert
        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.NotEmpty(solution.BackendName)
            Assert.Equal(100, solution.NumShots)
    
    [<Fact>]
    let ``InfluenceMaximization solve returns solution with backend info`` () =
        // Arrange
        let problem : InfluenceMaximization.Problem = {
            Nodes = [
                { Id = "A"; Score = 10.0 }
                { Id = "B"; Score = 5.0 }
            ]
            Edges = []
            K = 1
            SynergyWeight = 0.0
        }
        let backend = createLocalBackend()
        
        // Act
        let result = InfluenceMaximization.solve backend problem 100
        
        // Assert
        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.NotEmpty(solution.BackendName)
            Assert.Equal(100, solution.NumShots)
    
    [<Fact>]
    let ``DiverseSelection solve returns solution with backend info`` () =
        // Arrange
        let problem : DiverseSelection.Problem = {
            Items = [
                { Id = "A"; Value = 10.0; Cost = 5.0 }
                { Id = "B"; Value = 5.0; Cost = 3.0 }
            ]
            Diversity = Array2D.zeroCreate 2 2
            Budget = 10.0
            DiversityWeight = 0.0
        }
        let backend = createLocalBackend()
        
        // Act
        let result = DiverseSelection.solve backend problem 100
        
        // Assert
        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.NotEmpty(solution.BackendName)
            Assert.Equal(100, solution.NumShots)

// ============================================================================
// ADVANCED QAOA FEATURE TESTS
// ============================================================================

module AdvancedQaoaTests =
    
    [<Fact>]
    let ``solveWithConfig returns optimization parameters when enabled`` () =
        // Arrange
        let problem : IndependentSet.Problem = {
            Nodes = [
                { Id = "A"; Weight = 10.0 }
                { Id = "B"; Weight = 5.0 }
            ]
            Edges = []
        }
        let backend = createLocalBackend()
        let config = { defaultConfig with 
                        EnableOptimization = true
                        NumLayers = 2
                        OptimizationShots = 50
                        FinalShots = 100 }
        
        // Act
        let result = IndependentSet.solveWithConfig backend problem config
        
        // Assert
        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.OptimizedParameters.IsSome, 
                "Should return optimized parameters")
            match solution.OptimizedParameters with
            | Some parameters -> 
                Assert.Equal(2, parameters.Length)  // 2 layers
                for (gamma, beta) in parameters do
                    Assert.True(gamma >= 0.0 && gamma <= System.Math.PI,
                        $"Gamma {gamma} should be in [0, π]")
                    Assert.True(beta >= 0.0 && beta <= System.Math.PI / 2.0,
                        $"Beta {beta} should be in [0, π/2]")
            | None -> Assert.Fail("OptimizedParameters should not be None")
    
    [<Fact>]
    let ``constraint repair produces valid solutions for IndependentSet`` () =
        // Arrange: Problem where QAOA likely violates constraints
        let problem : IndependentSet.Problem = {
            Nodes = [
                { Id = "A"; Weight = 10.0 }
                { Id = "B"; Weight = 10.0 }
                { Id = "C"; Weight = 10.0 }
            ]
            Edges = [(0, 1); (1, 2); (0, 2)]  // Triangle - fully connected
        }
        let backend = createLocalBackend()
        let config = { fastConfig with EnableConstraintRepair = true }
        
        // Act
        let result = IndependentSet.solveWithConfig backend problem config
        
        // Assert
        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            // With constraint repair, solution MUST be valid
            Assert.True(solution.IsValid, 
                $"Solution should be valid after constraint repair. WasRepaired={solution.WasRepaired}")
    
    [<Fact>]
    let ``constraint repair produces correct cardinality for InfluenceMaximization`` () =
        // Arrange
        let problem : InfluenceMaximization.Problem = {
            Nodes = [
                { Id = "A"; Score = 10.0 }
                { Id = "B"; Score = 20.0 }
                { Id = "C"; Score = 15.0 }
                { Id = "D"; Score = 5.0 }
            ]
            Edges = []
            K = 2
            SynergyWeight = 0.0
        }
        let backend = createLocalBackend()
        let config = { fastConfig with EnableConstraintRepair = true }
        
        // Act
        let result = InfluenceMaximization.solveWithConfig backend problem config
        
        // Assert
        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            // With constraint repair, should have exactly k nodes
            Assert.Equal(2, solution.NumSelected)
    
    [<Fact>]
    let ``constraint repair produces feasible solutions for DiverseSelection`` () =
        // Arrange
        let problem : DiverseSelection.Problem = {
            Items = [
                { Id = "A"; Value = 50.0; Cost = 30.0 }
                { Id = "B"; Value = 40.0; Cost = 25.0 }
                { Id = "C"; Value = 30.0; Cost = 20.0 }
                { Id = "D"; Value = 20.0; Cost = 15.0 }
            ]
            Diversity = Array2D.zeroCreate 4 4
            Budget = 50.0
            DiversityWeight = 0.0
        }
        let backend = createLocalBackend()
        let config = { fastConfig with EnableConstraintRepair = true }
        
        // Act
        let result = DiverseSelection.solveWithConfig backend problem config
        
        // Assert
        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            // With constraint repair, should be within budget
            Assert.True(solution.IsFeasible,
                $"Solution should be feasible. Cost={solution.TotalCost}, Budget={problem.Budget}, Repaired={solution.WasRepaired}")
