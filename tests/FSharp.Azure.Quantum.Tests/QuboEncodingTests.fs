namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum

module QuboEncodingTests =
    
    [<Fact>]
    let ``Binary variable encoding - single variable should create identity QUBO`` () =
        // Arrange: Single binary variable x ∈ {0, 1}
        let variables = [ { Name = "x"; VarType = BinaryVar } ]
        
        // Act: Encode as QUBO
        let qubo = QuboEncoding.encodeVariables variables
        
        // Assert: Should create 1x1 QUBO matrix [0.0] (no bias by default)
        Assert.Equal(1, qubo.Size)
        Assert.Equal(0.0, qubo.GetCoefficient(0, 0))
        Assert.Equal<string list>(["x"], qubo.VariableNames)
    
    [<Fact>]
    let ``Binary variable encoding - two variables should create 2x2 QUBO`` () =
        // Arrange: Two binary variables x, y ∈ {0, 1}
        let variables = [
            { Name = "x"; VarType = BinaryVar }
            { Name = "y"; VarType = BinaryVar }
        ]
        
        // Act: Encode as QUBO
        let qubo = QuboEncoding.encodeVariables variables
        
        // Assert: Should create 2x2 QUBO matrix
        Assert.Equal(2, qubo.Size)
        Assert.Equal<string list>(["x"; "y"], qubo.VariableNames)
    
    [<Fact>]
    let ``Solution decoding - binary string to variable assignments`` () =
        // Arrange: Binary solution "10" for variables [x, y]
        let variables = [
            { Name = "x"; VarType = BinaryVar }
            { Name = "y"; VarType = BinaryVar }
        ]
        let binarySolution = [1; 0]
        
        // Act: Decode solution
        let solution = QuboEncoding.decodeSolution variables binarySolution
        
        // Assert: x=1, y=0
        Assert.Equal(2, solution.Assignments.Length)
        Assert.Equal("x", solution.Assignments.[0].Name)
        Assert.Equal(1, solution.Assignments.[0].Value)
        Assert.Equal("y", solution.Assignments.[1].Name)
        Assert.Equal(0, solution.Assignments.[1].Value)
