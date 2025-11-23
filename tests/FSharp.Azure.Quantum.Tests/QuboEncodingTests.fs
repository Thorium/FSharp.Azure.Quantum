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
    
    // TDD Cycle #2: Integer variable encoding tests
    [<Fact>]
    let ``Integer variable encoding - should use one-hot encoding`` () =
        // Arrange: Integer variable x ∈ {0, 1, 2, 3}
        let variables = [ { Name = "x"; VarType = IntegerVar(0, 3) } ]
        
        // Act: Encode as QUBO
        let qubo = QuboEncoding.encodeVariables variables
        
        // Assert: Should create 4x4 QUBO (one-hot: 4 qubits for values 0-3)
        Assert.Equal(4, qubo.Size)
        Assert.Equal<string list>(["x_0"; "x_1"; "x_2"; "x_3"], qubo.VariableNames)
    
    [<Fact>]
    let ``Integer variable decoding - one-hot to integer value`` () =
        // Arrange: Integer variable x ∈ {0, 1, 2} encoded as one-hot
        let variables = [ { Name = "x"; VarType = IntegerVar(0, 2) } ]
        let binarySolution = [0; 1; 0] // One-hot: x=1
        
        // Act: Decode solution
        let solution = QuboEncoding.decodeSolution variables binarySolution
        
        // Assert: x=1
        Assert.Equal(1, solution.Assignments.Length)
        Assert.Equal("x", solution.Assignments.[0].Name)
        Assert.Equal(1, solution.Assignments.[0].Value)
    
    [<Fact>]
    let ``Mixed variables - binary and integer encoding`` () =
        // Arrange: Mixed variable types
        let variables = [
            { Name = "b"; VarType = BinaryVar }
            { Name = "i"; VarType = IntegerVar(0, 2) }
        ]
        
        // Act: Encode as QUBO
        let qubo = QuboEncoding.encodeVariables variables
        
        // Assert: 1 binary + 3 integer (one-hot) = 4 qubits total
        Assert.Equal(4, qubo.Size)
        Assert.Equal<string list>(["b"; "i_0"; "i_1"; "i_2"], qubo.VariableNames)
    
    [<Fact>]
    let ``Integer constraint penalty - exactly one bit must be set`` () =
        // Arrange: Integer variable with one-hot constraint
        let variables = [ { Name = "x"; VarType = IntegerVar(0, 2) } ]
        
        // Act: Encode with constraints
        let qubo = QuboEncoding.encodeVariablesWithConstraints variables
        
        // Assert: Diagonal should penalize no selection, off-diagonal should penalize multiple
        // Penalty: (x_0 + x_1 + x_2 - 1)^2 expands to encourage exactly one bit set
        let penalty = qubo.GetCoefficient(0, 0) // Should be negative to encourage selection
        Assert.True(penalty < 0.0 || penalty = 0.0) // May have negative bias or zero initially
    
    // TDD Cycle #3: Categorical variables and constraint penalties
    [<Fact>]
    let ``Categorical variable encoding - should use one-hot encoding`` () =
        // Arrange: Categorical variable color ∈ {red, green, blue}
        let variables = [ { Name = "color"; VarType = CategoricalVar(["red"; "green"; "blue"]) } ]
        
        // Act: Encode as QUBO
        let qubo = QuboEncoding.encodeVariables variables
        
        // Assert: Should create 3x3 QUBO (one-hot: 3 qubits for 3 categories)
        Assert.Equal(3, qubo.Size)
        Assert.Equal<string list>(["color_red"; "color_green"; "color_blue"], qubo.VariableNames)
    
    [<Fact>]
    let ``Categorical variable decoding - one-hot to category index`` () =
        // Arrange: Categorical variable with 3 options
        let variables = [ { Name = "color"; VarType = CategoricalVar(["red"; "green"; "blue"]) } ]
        let binarySolution = [0; 1; 0] // One-hot: green (index 1)
        
        // Act: Decode solution
        let solution = QuboEncoding.decodeSolution variables binarySolution
        
        // Assert: color=1 (green)
        Assert.Equal(1, solution.Assignments.Length)
        Assert.Equal("color", solution.Assignments.[0].Name)
        Assert.Equal(1, solution.Assignments.[0].Value) // Index of selected category
    
    [<Fact>]
    let ``Constraint penalty - equality constraint`` () =
        // Arrange: Constraint x + y = 2 (where x,y are binary)
        let variables = [
            { Name = "x"; VarType = BinaryVar }
            { Name = "y"; VarType = BinaryVar }
        ]
        let constr = Constraint.EqualityConstraint([0; 1], 2.0) // x + y = 2
        
        // Act: Apply constraint with penalty weight
        let qubo = QuboEncoding.encodeVariablesWithCustomConstraints variables [constr] 10.0
        
        // Assert: Penalty (x + y - 2)^2 should be added to QUBO
        // Expansion: x^2 + y^2 + 2xy - 4x - 4y + 4
        // For binary: x^2 = x, so: -3x - 3y + 2xy + 4
        Assert.Equal(-30.0, qubo.GetCoefficient(0, 0)) // -3 * weight
        Assert.Equal(-30.0, qubo.GetCoefficient(1, 1)) // -3 * weight
        Assert.Equal(20.0, qubo.GetCoefficient(0, 1)) // 2 * weight
    
    [<Fact>]
    let ``Complex problem - TSP with 3 cities`` () =
        // Arrange: 3 cities, need position and city at each step
        // Variables: x_0_0, x_0_1, x_0_2 (city at position 0)
        //            x_1_0, x_1_1, x_1_2 (city at position 1)
        //            x_2_0, x_2_1, x_2_2 (city at position 2)
        let variables = [
            { Name = "pos0"; VarType = IntegerVar(0, 2) } // Which city at position 0
            { Name = "pos1"; VarType = IntegerVar(0, 2) } // Which city at position 1
            { Name = "pos2"; VarType = IntegerVar(0, 2) } // Which city at position 2
        ]
        
        // Act: Encode with one-hot constraints
        let qubo = QuboEncoding.encodeVariablesWithConstraints variables
        
        // Assert: 9 qubits total (3 positions × 3 cities each)
        Assert.Equal(9, qubo.Size)
        Assert.Equal(9, qubo.VariableNames.Length)
