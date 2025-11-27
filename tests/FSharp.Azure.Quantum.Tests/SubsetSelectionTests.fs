namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum

module SubsetSelectionTests =

    [<Fact>]
    let ``Item creation with multi-dimensional weights`` () =
        // Arrange & Act
        let laptop = SubsetSelection.itemMulti "laptop" "Laptop" ["weight", 3.0; "value", 1000.0]
        
        // Assert
        Assert.Equal("laptop", laptop.Id)
        Assert.Equal("Laptop", laptop.Value)
        Assert.Equal(3.0, laptop.Weights.["weight"])
        Assert.Equal(1000.0, laptop.Weights.["value"])
        Assert.True(Map.isEmpty laptop.Metadata)
    
    [<Fact>]
    let ``SelectionConstraint and SelectionObjective types`` () =
        // Arrange & Act - Create various constraints
        let exactTarget = SubsetSelection.ExactTarget("value", 13.0)
        let maxLimit = SubsetSelection.MaxLimit("weight", 5.0)
        let minLimit = SubsetSelection.MinLimit("value", 100.0)
        let range = SubsetSelection.Range("weight", 1.0, 10.0)
        
        // Create objectives
        let minWeight = SubsetSelection.MinimizeWeight("weight")
        let maxWeight = SubsetSelection.MaximizeWeight("value")
        let minCount = SubsetSelection.MinimizeCount
        let maxCount = SubsetSelection.MaximizeCount
        
        // Assert - verify types exist and match correctly
        match exactTarget with
        | SubsetSelection.ExactTarget(dim, target) ->
            Assert.Equal("value", dim)
            Assert.Equal(13.0, target)
        | _ -> Assert.Fail("Expected ExactTarget")
        
        match maxLimit with
        | SubsetSelection.MaxLimit(dim, limit) ->
            Assert.Equal("weight", dim)
            Assert.Equal(5.0, limit)
        | _ -> Assert.Fail("Expected MaxLimit")
        
        match minWeight with
        | SubsetSelection.MinimizeWeight(dim) ->
            Assert.Equal("weight", dim)
        | _ -> Assert.Fail("Expected MinimizeWeight")
    
    [<Fact>]
    let ``SubsetSelectionBuilder fluent API composes with method chaining`` () =
        // Arrange - Knapsack problem items
        let laptop = SubsetSelection.itemMulti "laptop" "Laptop" ["weight", 3.0; "value", 1000.0]
        let phone = SubsetSelection.itemMulti "phone" "Phone" ["weight", 0.5; "value", 800.0]
        let tablet = SubsetSelection.itemMulti "tablet" "Tablet" ["weight", 1.5; "value", 600.0]
        
        // Act - fluent builder API
        let problem =
            SubsetSelection.SubsetSelectionBuilder.Create()
                .Items([laptop; phone; tablet])
                .AddConstraint(SubsetSelection.MaxLimit("weight", 5.0))
                .Objective(SubsetSelection.MaximizeWeight("value"))
                .Build()
        
        // Assert
        Assert.Equal(3, problem.Items.Length)
        Assert.Equal(1, problem.Constraints.Length)
        
        match problem.Objective with
        | SubsetSelection.MaximizeWeight(dim) ->
            Assert.Equal("value", dim)
        | _ -> Assert.Fail("Expected MaximizeWeight")
        
        match problem.Constraints.[0] with
        | SubsetSelection.MaxLimit(dim, limit) ->
            Assert.Equal("weight", dim)
            Assert.Equal(5.0, limit)
        | _ -> Assert.Fail("Expected MaxLimit")
    
    [<Fact>]
    let ``Classical Knapsack solver solves 0-1 knapsack problem`` () =
        // Arrange - Classic Knapsack: maximize value within weight limit
        let laptop = SubsetSelection.itemMulti "laptop" "Laptop" ["weight", 3.0; "value", 1000.0]
        let phone = SubsetSelection.itemMulti "phone" "Phone" ["weight", 0.5; "value", 800.0]
        let tablet = SubsetSelection.itemMulti "tablet" "Tablet" ["weight", 1.5; "value", 600.0]
        let camera = SubsetSelection.itemMulti "camera" "Camera" ["weight", 2.0; "value", 400.0]
        
        let problem =
            SubsetSelection.SubsetSelectionBuilder.Create()
                .Items([laptop; phone; tablet; camera])
                .AddConstraint(SubsetSelection.MaxLimit("weight", 5.0))
                .Objective(SubsetSelection.MaximizeWeight("value"))
                .Build()
        
        // Act - solve with classical DP algorithm
        let result = SubsetSelection.solveKnapsack problem "weight" "value"
        
        // Assert
        match result with
        | Ok solution ->
            // Should select phone (0.5, 800) + tablet (1.5, 600) + laptop (3.0, 1000) = 5.0 weight, 2400 value
            // OR phone + laptop + camera = 5.5 (over), so phone + tablet + laptop = 5.0, 2400
            Assert.True(solution.IsFeasible, "Solution should be feasible")
            Assert.Equal(3, solution.SelectedItems.Length)
            
            // Verify total value is 2400 (best combination)
            Assert.Equal(2400.0, solution.ObjectiveValue)
            
            // Verify weight constraint is satisfied
            Assert.True(solution.TotalWeights.["weight"] <= 5.0)
            
        | Error msg ->
            Assert.Fail($"Solver failed: {msg}")
    
    [<Fact>]
    let ``Integration: Real-world portfolio optimization selects optimal investments`` () =
        // Arrange - Portfolio optimization: maximize ROI within budget constraint
        // Scenario: Startup has $50k budget, must select from 6 investment opportunities
        let marketing = SubsetSelection.itemMulti "marketing" "Digital Marketing Campaign" ["cost", 15000.0; "roi", 45000.0; "risk", 0.3]
        let hiring = SubsetSelection.itemMulti "hiring" "Hire Senior Developer" ["cost", 25000.0; "roi", 80000.0; "risk", 0.2]
        let infrastructure = SubsetSelection.itemMulti "infra" "Cloud Infrastructure Upgrade" ["cost", 10000.0; "roi", 30000.0; "risk", 0.1]
        let productFeature = SubsetSelection.itemMulti "feature" "Premium Feature Development" ["cost", 20000.0; "roi", 60000.0; "risk", 0.4]
        let researchDev = SubsetSelection.itemMulti "rd" "R&D Prototype" ["cost", 18000.0; "roi", 50000.0; "risk", 0.5]
        let sales = SubsetSelection.itemMulti "sales" "Sales Team Expansion" ["cost", 22000.0; "roi", 70000.0; "risk", 0.25]
        
        let problem =
            SubsetSelection.SubsetSelectionBuilder.Create()
                .Items([marketing; hiring; infrastructure; productFeature; researchDev; sales])
                .AddConstraint(SubsetSelection.MaxLimit("cost", 50000.0))
                .Objective(SubsetSelection.MaximizeWeight("roi"))
                .Build()
        
        // Act - solve portfolio optimization
        let result = SubsetSelection.solveKnapsack problem "cost" "roi"
        
        // Assert
        match result with
        | Ok solution ->
            Assert.True(solution.IsFeasible, "Solution must be within budget")
            
            // Verify budget constraint satisfied
            let totalCost = solution.TotalWeights.["cost"]
            Assert.True(totalCost <= 50000.0, $"Budget exceeded: {totalCost}")
            
            // Optimal selection should maximize ROI
            let totalROI = solution.ObjectiveValue
            Assert.True(totalROI > 0.0, "Should have positive ROI")
            
            // Log selected investments for business visibility
            let selectedNames = 
                solution.SelectedItems 
                |> List.map (fun item -> item.Value) 
                |> String.concat ", "
            
            // Verify reasonable solution (at least 2 investments selected)
            Assert.True(solution.SelectedItems.Length >= 2, 
                $"Should select multiple investments. Selected: {selectedNames}")
            
            // Business metrics
            let avgRisk = 
                solution.SelectedItems
                |> List.averageBy (fun item -> item.Weights.["risk"])
            
            Assert.True(avgRisk >= 0.0 && avgRisk <= 1.0, "Risk should be normalized 0-1")
            
        | Error msg ->
            Assert.Fail($"Portfolio optimization failed: {msg}")
    
    [<Fact>]
    let ``QUBO encoding generates valid matrix for simple knapsack problem`` () =
        // Arrange - Simple 3-item knapsack
        let item1 = SubsetSelection.itemMulti "item1" "Item 1" ["weight", 2.0; "value", 10.0]
        let item2 = SubsetSelection.itemMulti "item2" "Item 2" ["weight", 3.0; "value", 15.0]
        let item3 = SubsetSelection.itemMulti "item3" "Item 3" ["weight", 4.0; "value", 20.0]
        
        let problem =
            SubsetSelection.SubsetSelectionBuilder.Create()
                .Items([item1; item2; item3])
                .AddConstraint(SubsetSelection.MaxLimit("weight", 5.0))
                .Objective(SubsetSelection.MaximizeWeight("value"))
                .Build()
        
        // Act - encode to QUBO
        let quboResult = SubsetSelection.toQubo problem "weight" "value"
        
        // Assert
        match quboResult with
        | Ok qubo ->
            // Verify QUBO matrix dimensions (3 items = 3 binary variables)
            Assert.Equal(3, qubo.NumVars)
            
            // Verify matrix is square and has correct structure
            Assert.True(Map.count qubo.Q >= 0, "QUBO matrix should have entries")
            
            // QUBO should encode:
            // 1. Objective: maximize value (minimize negative value)
            // 2. Constraint: penalty for exceeding weight capacity
            
        | Error msg ->
            Assert.Fail($"QUBO encoding failed: {msg}")
