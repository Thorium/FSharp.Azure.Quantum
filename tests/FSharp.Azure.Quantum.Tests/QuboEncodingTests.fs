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
    
    // Integer variable encoding tests
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
        
        // Assert: Diagonal should have -1.0 (encourages selection)
        // Penalty: (x_0 + x_1 + x_2 - 1)^2 = x_0^2 + x_1^2 + x_2^2 - 2*x_0 - 2*x_1 - 2*x_2 + 2*x_0*x_1 + 2*x_0*x_2 + 2*x_1*x_2 + 1
        // For binary: x^2 = x, so diagonal: -x_i (coefficient -1)
        Assert.Equal(-1.0, qubo.GetCoefficient(0, 0))
        Assert.Equal(-1.0, qubo.GetCoefficient(1, 1))
        Assert.Equal(-1.0, qubo.GetCoefficient(2, 2))
        
        // Off-diagonal should have +2.0 (discourages multiple selections)
        Assert.Equal(2.0, qubo.GetCoefficient(0, 1))
        Assert.Equal(2.0, qubo.GetCoefficient(0, 2))
        Assert.Equal(2.0, qubo.GetCoefficient(1, 2))
    
    // Categorical variables and constraint penalties
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
        // For binary: x^2 = x, so: x + y + 2xy - 4x - 4y + 4 = -3x - 3y + 2xy + 4
        // Implementation adds: (1 - 2*target)*x = (1 - 4)*x = -3x per diagonal
        // Implementation adds: 2*weight per off-diagonal
        Assert.Equal(-30.0, qubo.GetCoefficient(0, 0)) // (1 - 2*2) * 10 = -3 * 10 = -30
        Assert.Equal(-30.0, qubo.GetCoefficient(1, 1)) // (1 - 2*2) * 10 = -3 * 10 = -30
        Assert.Equal(20.0, qubo.GetCoefficient(0, 1)) // 2 * 10 = 20
        Assert.Equal(20.0, qubo.GetCoefficient(1, 0)) // Symmetric
    
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
        let expectedNames = [
            "pos0_0"; "pos0_1"; "pos0_2"
            "pos1_0"; "pos1_1"; "pos1_2"
            "pos2_0"; "pos2_1"; "pos2_2"
        ]
        Assert.Equal<string list>(expectedNames, qubo.VariableNames)
        
        // Verify one-hot constraints applied to each position variable
        // pos0: qubits 0,1,2 should have -1 diagonal, +2 off-diagonal
        Assert.Equal(-1.0, qubo.GetCoefficient(0, 0))
        Assert.Equal(2.0, qubo.GetCoefficient(0, 1))
        
        // pos1: qubits 3,4,5 should have same pattern
        Assert.Equal(-1.0, qubo.GetCoefficient(3, 3))
        Assert.Equal(2.0, qubo.GetCoefficient(3, 4))
    
    // ============================================================================
    // TKT-36: Advanced Variable Encoding Tests
    // ============================================================================
    
    [<Fact>]
    let ``VariableEncoding Binary should calculate 1 qubit`` () =
        let encoding = VariableEncoding.Binary
        let count = VariableEncoding.qubitCount encoding
        Assert.Equal(1, count)
    
    [<Fact>]
    let ``VariableEncoding OneHot should calculate N qubits for N options`` () =
        let encoding = VariableEncoding.OneHot 4
        let count = VariableEncoding.qubitCount encoding
        Assert.Equal(4, count)
    
    [<Fact>]
    let ``VariableEncoding DomainWall should calculate N-1 qubits for N levels`` () =
        let encoding = VariableEncoding.DomainWall 4
        let count = VariableEncoding.qubitCount encoding
        Assert.Equal(3, count)
    
    [<Fact>]
    let ``VariableEncoding BoundedInteger should calculate log2 qubits`` () =
        let encoding = VariableEncoding.BoundedInteger(0, 10)
        let count = VariableEncoding.qubitCount encoding
        // Range 0-10 = 11 values, needs ceil(log2(11)) = 4 qubits
        Assert.Equal(4, count)
    
    // ============================================================================
    // Binary Encoding Tests - True binary decisions (include/exclude)
    // ============================================================================
    
    [<Fact>]
    let ``Binary encoding - Portfolio asset inclusion decision`` () =
        // Real-world: Should we include MSFT stock in portfolio?
        // Binary decision: 0 = exclude, 1 = include
        let includeAsset = VariableEncoding.Binary
        
        // Exclude decision (0)
        let excludeBits = VariableEncoding.encode includeAsset 0
        Assert.Equal<int list>([0], excludeBits)
        Assert.Equal(0, VariableEncoding.decode includeAsset excludeBits)
        
        // Include decision (1)
        let includeBits = VariableEncoding.encode includeAsset 1
        Assert.Equal<int list>([1], includeBits)
        Assert.Equal(1, VariableEncoding.decode includeAsset includeBits)
    
    // ============================================================================
    // One-Hot Encoding Tests - Choose one option from N (unordered categories)
    // ============================================================================
    
    [<Fact>]
    let ``OneHot encoding - Delivery route selection (3 routes)`` () =
        // Real-world: Choose delivery route from {North, South, East}
        // One-hot: Exactly one route selected
        let routeEncoding = VariableEncoding.OneHot 3
        
        // Verify qubit efficiency
        Assert.Equal(3, VariableEncoding.qubitCount routeEncoding)
        
        // Route 0 (North): [1, 0, 0]
        let northBits = VariableEncoding.encode routeEncoding 0
        Assert.Equal<int list>([1; 0; 0], northBits)
        Assert.Equal(0, VariableEncoding.decode routeEncoding northBits)
        
        // Route 2 (East): [0, 0, 1]
        let eastBits = VariableEncoding.encode routeEncoding 2
        Assert.Equal<int list>([0; 0; 1], eastBits)
        Assert.Equal(2, VariableEncoding.decode routeEncoding eastBits)
    
    [<Fact>]
    let ``OneHot encoding - Color choice (unordered categories)`` () =
        // Real-world: Choose website theme color from {Red, Blue, Green, Yellow}
        let colorEncoding = VariableEncoding.OneHot 4
        
        // Blue (index 1): [0, 1, 0, 0]
        let blueBits = VariableEncoding.encode colorEncoding 1
        Assert.Equal<int list>([0; 1; 0; 0], blueBits)
        Assert.Equal(1, VariableEncoding.decode colorEncoding blueBits)
        
        // Roundtrip all colors
        [0..3] |> List.iter (fun color ->
            let bits = VariableEncoding.encode colorEncoding color
            let decoded = VariableEncoding.decode colorEncoding bits
            Assert.Equal(color, decoded))
    
    // ============================================================================
    // Domain-Wall Encoding Tests - Ordered categories (priority levels)
    // ============================================================================
    
    [<Fact>]
    let ``DomainWall encoding - Priority levels (Low < Medium < High < Critical)`` () =
        // Real-world: Asset priority levels where order matters
        // Domain-wall: 25% fewer qubits than one-hot (3 qubits vs 4)
        let priorityEncoding = VariableEncoding.DomainWall 4
        
        // Verify qubit efficiency: N-1 qubits for N levels
        Assert.Equal(3, VariableEncoding.qubitCount priorityEncoding)
        
        // Low priority (level 1): [0, 0, 0] - no wall
        let lowBits = VariableEncoding.encode priorityEncoding 1
        Assert.Equal<int list>([0; 0; 0], lowBits)
        Assert.Equal(1, VariableEncoding.decode priorityEncoding lowBits)
        
        // Medium priority (level 2): [1, 0, 0] - wall at position 1
        let mediumBits = VariableEncoding.encode priorityEncoding 2
        Assert.Equal<int list>([1; 0; 0], mediumBits)
        Assert.Equal(2, VariableEncoding.decode priorityEncoding mediumBits)
        
        // High priority (level 3): [1, 1, 0] - wall extends
        let highBits = VariableEncoding.encode priorityEncoding 3
        Assert.Equal<int list>([1; 1; 0], highBits)
        Assert.Equal(3, VariableEncoding.decode priorityEncoding highBits)
        
        // Critical priority (level 4): [1, 1, 1] - full wall
        let criticalBits = VariableEncoding.encode priorityEncoding 4
        Assert.Equal<int list>([1; 1; 1], criticalBits)
        Assert.Equal(4, VariableEncoding.decode priorityEncoding criticalBits)
    
    [<Fact>]
    let ``DomainWall encoding - Qubit efficiency vs OneHot`` () =
        // Demonstrate 25% qubit reduction for ordered variables
        let levels = 8
        let domainWall = VariableEncoding.DomainWall levels
        let oneHot = VariableEncoding.OneHot levels
        
        let domainWallQubits = VariableEncoding.qubitCount domainWall
        let oneHotQubits = VariableEncoding.qubitCount oneHot
        
        // Domain-wall: 7 qubits, One-hot: 8 qubits
        Assert.Equal(7, domainWallQubits)
        Assert.Equal(8, oneHotQubits)
        
        // Verify reduction
        let reduction = float (oneHotQubits - domainWallQubits) / float oneHotQubits
        Assert.True(reduction >= 0.12) // At least 12.5% reduction
    
    // ============================================================================
    // Bounded Integer Encoding Tests - Integer ranges (quantities)
    // ============================================================================
    
    [<Fact>]
    let ``BoundedInteger encoding - Order quantity (0-10 units)`` () =
        // Real-world: How many units to order? Range 0-10
        // Binary encoding: Most efficient for large ranges
        let quantityEncoding = VariableEncoding.BoundedInteger(0, 10)
        
        // Verify qubit efficiency: log2(range) qubits
        Assert.Equal(4, VariableEncoding.qubitCount quantityEncoding) // ceil(log2(11)) = 4
        
        // Quantity 0: [0, 0, 0, 0]
        let qty0 = VariableEncoding.encode quantityEncoding 0
        Assert.Equal<int list>([0; 0; 0; 0], qty0)
        
        // Quantity 5: [1, 0, 1, 0] (binary 0101 LSB first)
        let qty5 = VariableEncoding.encode quantityEncoding 5
        Assert.Equal<int list>([1; 0; 1; 0], qty5)
        Assert.Equal(5, VariableEncoding.decode quantityEncoding qty5)
        
        // Quantity 10: [0, 1, 0, 1] (binary 1010 LSB first)
        let qty10 = VariableEncoding.encode quantityEncoding 10
        Assert.Equal<int list>([0; 1; 0; 1], qty10)
        Assert.Equal(10, VariableEncoding.decode quantityEncoding qty10)
    
    [<Fact>]
    let ``BoundedInteger encoding - Large range efficiency`` () =
        // Real-world: Inventory quantity 0-100 items
        let inventoryEncoding = VariableEncoding.BoundedInteger(0, 100)
        
        // Verify logarithmic efficiency: 7 qubits for 101 values
        let qubits = VariableEncoding.qubitCount inventoryEncoding
        Assert.Equal(7, qubits) // ceil(log2(101)) = 7
        
        // Compare to one-hot: would need 101 qubits!
        let oneHotAlternative = VariableEncoding.OneHot 101
        Assert.Equal(101, VariableEncoding.qubitCount oneHotAlternative)
        
        // BoundedInteger is 93% more efficient
        let efficiency = 1.0 - (float qubits / float 101)
        Assert.True(efficiency > 0.93)
    
    [<Fact>]
    let ``BoundedInteger roundtrip - All values in range`` () =
        // Verify roundtrip for realistic order quantities
        let quantityEncoding = VariableEncoding.BoundedInteger(0, 10)
        
        [0..10] |> List.iter (fun qty ->
            let bits = VariableEncoding.encode quantityEncoding qty
            let decoded = VariableEncoding.decode quantityEncoding bits
            Assert.Equal(qty, decoded))
    
    // ============================================================================
    // Real-World Portfolio Optimization Example
    // ============================================================================
    
    [<Fact>]
    let ``Portfolio optimization - 5 assets with priority levels`` () =
        // Real-world: Select priority for 5 assets (Low/Medium/High/Critical)
        // Compare encoding strategies
        
        // Option 1: One-Hot encoding (unordered)
        // 5 assets × 4 levels = 20 qubits
        let oneHotStrategy = VariableEncoding.OneHot 4
        let oneHotTotalQubits = 5 * VariableEncoding.qubitCount oneHotStrategy
        Assert.Equal(20, oneHotTotalQubits)
        
        // Option 2: Domain-Wall encoding (ordered priorities)
        // 5 assets × 3 qubits = 15 qubits (25% reduction!)
        let domainWallStrategy = VariableEncoding.DomainWall 4
        let domainWallTotalQubits = 5 * VariableEncoding.qubitCount domainWallStrategy
        Assert.Equal(15, domainWallTotalQubits)
        
        // Verify 25% qubit reduction
        let reduction = float (oneHotTotalQubits - domainWallTotalQubits) / float oneHotTotalQubits
        Assert.Equal(0.25, reduction, 2) // 25% reduction
        
        // Encode asset priorities using domain-wall
        let asset1Priority = VariableEncoding.encode domainWallStrategy 3 // High
        let asset2Priority = VariableEncoding.encode domainWallStrategy 1 // Low
        
        Assert.Equal<int list>([1; 1; 0], asset1Priority)
        Assert.Equal<int list>([0; 0; 0], asset2Priority)
    
    // ============================================================================
    // Constraint Penalty Tests - QUBO penalty matrix generation
    // ============================================================================
    
    [<Fact>]
    let ``OneHot constraint penalty enforces exactly-one-active rule`` () =
        // Real-world: Delivery route selection - exactly one route must be chosen
        // Penalty: (x0 + x1 + x2 - 1)^2 ensures exactly one qubit is active
        let routeEncoding = VariableEncoding.OneHot 3
        let penalty = VariableEncoding.constraintPenalty routeEncoding 10.0
        
        // Verify penalty matrix structure:
        // Diagonal: -1 * weight (encourages selection)
        // Off-diagonal: +2 * weight (discourages multiple selections)
        
        // Diagonal elements should be -10.0
        Assert.Equal(-10.0, penalty.[0, 0])
        Assert.Equal(-10.0, penalty.[1, 1])
        Assert.Equal(-10.0, penalty.[2, 2])
        
        // Off-diagonal elements should be +20.0
        Assert.Equal(20.0, penalty.[0, 1])
        Assert.Equal(20.0, penalty.[0, 2])
        Assert.Equal(20.0, penalty.[1, 2])
        Assert.Equal(20.0, penalty.[1, 0]) // Symmetric
        Assert.Equal(20.0, penalty.[2, 0])
        Assert.Equal(20.0, penalty.[2, 1])
    
    [<Fact>]
    let ``OneHot constraint penalty - Verify QUBO math`` () =
        // Mathematical verification: (x0 + x1 + x2 - 1)^2
        // Expansion: x0^2 + x1^2 + x2^2 + 2x0x1 + 2x0x2 + 2x1x2 - 2x0 - 2x1 - 2x2 + 1
        // For binary variables: x^2 = x
        // Simplified: x0 + x1 + x2 + 2x0x1 + 2x0x2 + 2x1x2 - 2x0 - 2x1 - 2x2 + 1
        // Final: -x0 - x1 - x2 + 2x0x1 + 2x0x2 + 2x1x2 + 1
        // QUBO form (ignoring constant): -x0 - x1 - x2 + 2x0x1 + 2x0x2 + 2x1x2
        
        let encoding = VariableEncoding.OneHot 3
        let weight = 1.0
        let penalty = VariableEncoding.constraintPenalty encoding weight
        
        // Diagonal: -weight
        Assert.Equal(-1.0, penalty.[0, 0])
        // Off-diagonal: 2 * weight
        Assert.Equal(2.0, penalty.[0, 1])
    
    [<Fact>]
    let ``BoundedInteger constraint penalty enforces value bounds`` () =
        // Real-world: Order quantity 0-10 units, penalize out-of-range values
        // For binary encoding, we need to ensure decoded value stays within [min, max]
        let quantityEncoding = VariableEncoding.BoundedInteger(0, 10)
        let penalty = VariableEncoding.constraintPenalty quantityEncoding 5.0
        
        // Penalty matrix should be 4x4 (4 qubits for range 0-10)
        let size = VariableEncoding.qubitCount quantityEncoding
        Assert.Equal(4, size)
        Assert.Equal(size, penalty.GetLength(0))
        Assert.Equal(size, penalty.GetLength(1))
        
        // Verify penalty structure exists (specific values depend on implementation)
        // Higher-order bits should have larger penalties to prevent overflow
        let highBitPenalty = penalty.[3, 3] // MSB
        let lowBitPenalty = penalty.[0, 0]  // LSB
        Assert.True(abs highBitPenalty >= abs lowBitPenalty)
    
    [<Fact>]
    let ``Binary and DomainWall encodings have no constraint penalties`` () =
        // Real-world: Binary decisions and domain-wall naturally satisfy constraints
        // No additional QUBO penalties needed
        
        let binaryEncoding = VariableEncoding.Binary
        let binaryPenalty = VariableEncoding.constraintPenalty binaryEncoding 10.0
        
        // Should return zero matrix (no constraints needed)
        Assert.Equal(0.0, binaryPenalty.[0, 0])
        
        let domainWallEncoding = VariableEncoding.DomainWall 4
        let dwPenalty = VariableEncoding.constraintPenalty domainWallEncoding 10.0
        
        // Domain-wall naturally enforces ordering, no penalties needed
        for i in 0..2 do
            for j in 0..2 do
                Assert.Equal(0.0, dwPenalty.[i, j])
    
    [<Fact>]
    let ``Constraint penalty weight scaling`` () =
        // Real-world: Adjust penalty weight to balance problem objectives
        let encoding = VariableEncoding.OneHot 3
        
        // Small weight for soft constraints
        let softPenalty = VariableEncoding.constraintPenalty encoding 1.0
        Assert.Equal(-1.0, softPenalty.[0, 0])
        Assert.Equal(2.0, softPenalty.[0, 1])
        
        // Large weight for hard constraints  
        let hardPenalty = VariableEncoding.constraintPenalty encoding 100.0
        Assert.Equal(-100.0, hardPenalty.[0, 0])
        Assert.Equal(200.0, hardPenalty.[0, 1])
        
        // Verify linear scaling
        let ratio = hardPenalty.[0, 0] / softPenalty.[0, 0]
        Assert.Equal(100.0, ratio)
    
    // ============================================================================
    // TKT-37: Constraint Penalty Optimization Tests
    // ============================================================================
    
    [<Fact>]
    let ``ConstraintPenalty Hard constraint should use Lucas Rule`` () =
        // Lucas Rule: λ ≥ max(|coefficient in H_objective|) + 1
        // Example: TSP with max distance 100 → penalty = 101
        let objectiveMax = 100.0
        let problemSize = 10
        
        let penalty = ConstraintPenalty.calculatePenalty ConstraintType.Hard objectiveMax problemSize
        
        // Hard constraint should use Lucas Rule: objectiveMax + 1
        // With size scaling: (100 + 1) * sqrt(10) ≈ 101 * 3.16 ≈ 319
        let expectedMinPenalty = objectiveMax + 1.0
        Assert.True(penalty >= expectedMinPenalty, 
            sprintf "Penalty %f should be >= %f (Lucas Rule)" penalty expectedMinPenalty)
    
    [<Fact>]
    let ``ConstraintPenalty Soft constraint should use preference weight`` () =
        // Soft constraint: fraction of hard constraint penalty
        // Example: Prefer short segments = 30% of hard constraint
        let objectiveMax = 500.0
        let problemSize = 20
        let preferenceWeight = 0.3
        
        let softPenalty = ConstraintPenalty.calculatePenalty (ConstraintType.Soft preferenceWeight) objectiveMax problemSize
        let hardPenalty = ConstraintPenalty.calculatePenalty ConstraintType.Hard objectiveMax problemSize
        
        // Soft penalty should be 30% of hard penalty
        let expected = hardPenalty * preferenceWeight
        Assert.Equal(expected, softPenalty, 2)
    
    [<Fact>]
    let ``ConstraintPenalty should scale with problem size`` () =
        // Larger problems need larger penalties
        // Size scaling: sqrt(problemSize)
        let objectiveMax = 100.0
        
        let smallPenalty = ConstraintPenalty.calculatePenalty ConstraintType.Hard objectiveMax 5
        let largePenalty = ConstraintPenalty.calculatePenalty ConstraintType.Hard objectiveMax 20
        
        // sqrt(20) / sqrt(5) = 2.0, so large penalty should be 2x small penalty
        let ratio = largePenalty / smallPenalty
        Assert.Equal(2.0, ratio, 1)
    
    [<Fact>]
    let ``ConstraintPenalty Lucas Rule ensures constraint dominates objective`` () =
        // Lucas Rule: λ ≥ max(H_objective) + 1
        // Ensures constraint violation costs more than any objective improvement
        let objectiveMax = 250.0
        let problemSize = 1
        
        let penalty = ConstraintPenalty.calculatePenalty ConstraintType.Hard objectiveMax problemSize
        
        // For problem size 1, no size scaling (sqrt(1) = 1)
        // Penalty should be exactly objectiveMax + 1
        Assert.Equal(251.0, penalty, 2)
    
    [<Fact>]
    let ``ConstraintPenalty TSP example - 20 cities with max distance 500km`` () =
        // Real-world: TSP with 20 cities, max distance 500km
        let objectiveMax = 500.0
        let problemSize = 20
        
        let visitOncePenalty = ConstraintPenalty.calculatePenalty ConstraintType.Hard objectiveMax problemSize
        
        // Expected: (500 + 1) * sqrt(20) ≈ 501 * 4.47 ≈ 2240
        Assert.True(visitOncePenalty > 2200.0 && visitOncePenalty < 2250.0,
            sprintf "Expected ~2240, got %f" visitOncePenalty)
    
    [<Fact>]
    let ``ConstraintPenalty Portfolio example - soft constraint at 30 percent`` () =
        // Real-world: Portfolio with soft preference for diversification
        let objectiveMax = 500.0
        let problemSize = 20
        
        let softPenalty = ConstraintPenalty.calculatePenalty (ConstraintType.Soft 0.3) objectiveMax problemSize
        
        // Expected: (500 + 1) * sqrt(20) * 0.3 ≈ 2240 * 0.3 ≈ 672
        Assert.True(softPenalty > 660.0 && softPenalty < 680.0,
            sprintf "Expected ~672, got %f" softPenalty)
    
    [<Fact>]
    let ``ConstraintPenalty should handle small problems`` () =
        // Small problem: n=5 cities
        let objectiveMax = 100.0
        let problemSize = 5
        
        let penalty = ConstraintPenalty.calculatePenalty ConstraintType.Hard objectiveMax problemSize
        
        // (100 + 1) * sqrt(5) ≈ 101 * 2.24 ≈ 226
        Assert.True(penalty > 220.0 && penalty < 230.0)
    
    [<Fact>]
    let ``ConstraintPenalty should handle large problems`` () =
        // Large problem: n=100 cities
        let objectiveMax = 1000.0
        let problemSize = 100
        
        let penalty = ConstraintPenalty.calculatePenalty ConstraintType.Hard objectiveMax problemSize
        
        // (1000 + 1) * sqrt(100) = 1001 * 10 = 10010
        Assert.Equal(10010.0, penalty, 2)
    
    [<Fact>]
    let ``ConstraintPenalty Hard vs Soft comparison`` () =
        // Compare hard and soft constraints
        let objectiveMax = 100.0
        let problemSize = 10
        
        let hardPenalty = ConstraintPenalty.calculatePenalty ConstraintType.Hard objectiveMax problemSize
        let softPenalty = ConstraintPenalty.calculatePenalty (ConstraintType.Soft 0.5) objectiveMax problemSize
        
        // Soft penalty should be exactly half of hard penalty
        Assert.Equal(hardPenalty * 0.5, softPenalty, 2)
        Assert.True(hardPenalty > softPenalty)
    
    [<Fact>]
    let ``ConstraintPenalty should handle zero objective max`` () =
        // Edge case: objective maximum is zero
        let objectiveMax = 0.0
        let problemSize = 10
        
        let penalty = ConstraintPenalty.calculatePenalty ConstraintType.Hard objectiveMax problemSize
        
        // Lucas Rule: 0 + 1 = 1, with sqrt(10) scaling
        // Expected: 1 * sqrt(10) ≈ 3.16
        Assert.True(penalty > 3.1 && penalty < 3.2)
    
    [<Fact>]
    let ``ConstraintPenalty multiple soft weights`` () =
        // Different soft constraint strengths
        let objectiveMax = 100.0
        let problemSize = 10
        
        let veryWeak = ConstraintPenalty.calculatePenalty (ConstraintType.Soft 0.1) objectiveMax problemSize
        let weak = ConstraintPenalty.calculatePenalty (ConstraintType.Soft 0.3) objectiveMax problemSize
        let moderate = ConstraintPenalty.calculatePenalty (ConstraintType.Soft 0.5) objectiveMax problemSize
        let strong = ConstraintPenalty.calculatePenalty (ConstraintType.Soft 0.8) objectiveMax problemSize
        
        // Verify ordering: very weak < weak < moderate < strong
        Assert.True(veryWeak < weak)
        Assert.True(weak < moderate)
        Assert.True(moderate < strong)
    
    // ============================================================================
    // Constraint Validation Tests
    // ============================================================================
    
    [<Fact>]
    let ``validateConstraints should detect OneHot violation - no bits set`` () =
        // OneHot constraint: exactly one bit must be active
        // Violation: [0, 0, 0] - no bits set
        let encoding = VariableEncoding.OneHot 3
        let solution = [0; 0; 0]
        
        let violations = ConstraintPenalty.validateConstraints encoding solution
        
        Assert.False(List.isEmpty violations)
        Assert.Equal(1, violations.Length)
    
    [<Fact>]
    let ``validateConstraints should detect OneHot violation - multiple bits set`` () =
        // OneHot constraint: exactly one bit must be active
        // Violation: [1, 1, 0] - two bits set
        let encoding = VariableEncoding.OneHot 3
        let solution = [1; 1; 0]
        
        let violations = ConstraintPenalty.validateConstraints encoding solution
        
        Assert.False(List.isEmpty violations)
    
    [<Fact>]
    let ``validateConstraints should accept valid OneHot solution`` () =
        // Valid OneHot: exactly one bit set
        // Solution: [0, 1, 0] - one bit at position 1
        let encoding = VariableEncoding.OneHot 3
        let solution = [0; 1; 0]
        
        let violations = ConstraintPenalty.validateConstraints encoding solution
        
        Assert.True(List.isEmpty violations)
    
    [<Fact>]
    let ``validateConstraints should accept valid Binary solution`` () =
        // Binary variables have no constraints
        let encoding = VariableEncoding.Binary
        let solution0 = [0]
        let solution1 = [1]
        
        Assert.True(List.isEmpty (ConstraintPenalty.validateConstraints encoding solution0))
        Assert.True(List.isEmpty (ConstraintPenalty.validateConstraints encoding solution1))
    
    [<Fact>]
    let ``validateConstraints should detect BoundedInteger out of range`` () =
        // BoundedInteger: value must be within [min, max]
        // Range [0, 10], solution decodes to 15 (out of range)
        let encoding = VariableEncoding.BoundedInteger(0, 10)
        let solution = [1; 1; 1; 1] // Binary 1111 = 15 > 10
        
        let violations = ConstraintPenalty.validateConstraints encoding solution
        
        Assert.False(List.isEmpty violations)
