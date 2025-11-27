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
