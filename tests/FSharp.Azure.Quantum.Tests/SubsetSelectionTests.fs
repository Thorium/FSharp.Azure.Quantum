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
