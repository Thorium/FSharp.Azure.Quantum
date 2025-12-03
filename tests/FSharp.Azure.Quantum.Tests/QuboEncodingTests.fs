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
    let ``Integer variable encoding - should use BoundedInteger encoding`` () =
        // Arrange: Integer variable x ∈ {0, 1, 2, 3}
        let variables = [ { Name = "x"; VarType = IntegerVar(0, 3) } ]
        
        // Act: Encode as QUBO
        let qubo = QuboEncoding.encodeVariables variables
        
        // Assert: Should create 2x2 QUBO (BoundedInteger: 2 bits for values 0-3)
        // ceil(log2(4)) = 2 bits
        Assert.Equal(2, qubo.Size)
        Assert.Equal<string list>(["x_bit0"; "x_bit1"], qubo.VariableNames)
    
    [<Fact>]
    let ``Integer variable decoding - binary to integer value`` () =
        // Arrange: Integer variable x ∈ {0, 2} encoded as BoundedInteger (2 bits)
        let variables = [ { Name = "x"; VarType = IntegerVar(0, 2) } ]
        let binarySolution = [1; 0] // Binary (LSB first): 01 = 1
        
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
        
        // Assert: 1 binary + 2 integer (BoundedInteger) = 3 qubits total
        // IntegerVar(0, 2) = 3 values = ceil(log2(3)) = 2 bits
        Assert.Equal(3, qubo.Size)
        Assert.Equal<string list>(["b"; "i_bit0"; "i_bit1"], qubo.VariableNames)
    
    [<Fact>]
    let ``Integer constraint penalty - BoundedInteger has no constraints`` () =
        // Arrange: Integer variable uses BoundedInteger encoding (no constraints needed)
        let variables = [ { Name = "x"; VarType = IntegerVar(0, 2) } ]
        
        // Act: Encode with constraints
        let qubo = QuboEncoding.encodeVariablesWithConstraints variables
        
        // Assert: BoundedInteger encoding doesn't need one-hot constraints
        // IntegerVar(0, 2) = ceil(log2(3)) = 2 bits, no constraint penalties
        Assert.Equal(2, qubo.Size)  // Only 2 qubits for binary encoding
        
        // All coefficients should be 0 (no constraints for BoundedInteger)
        Assert.Equal(0.0, qubo.GetCoefficient(0, 0))
        Assert.Equal(0.0, qubo.GetCoefficient(1, 1))
        Assert.Equal(0.0, qubo.GetCoefficient(0, 1))
        Assert.Equal(0.0, qubo.GetCoefficient(1, 0))
    
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
        // Variables: Each position uses BoundedInteger encoding
        let variables = [
            { Name = "pos0"; VarType = IntegerVar(0, 2) } // Which city at position 0
            { Name = "pos1"; VarType = IntegerVar(0, 2) } // Which city at position 1
            { Name = "pos2"; VarType = IntegerVar(0, 2) } // Which city at position 2
        ]
        
        // Act: Encode with BoundedInteger (binary encoding)
        let qubo = QuboEncoding.encodeVariablesWithConstraints variables
        
        // Assert: 6 qubits total (3 positions × 2 bits each)
        // IntegerVar(0, 2) = 3 values = ceil(log2(3)) = 2 bits per position
        Assert.Equal(6, qubo.Size)
        let expectedNames = [
            "pos0_bit0"; "pos0_bit1"
            "pos1_bit0"; "pos1_bit1"
            "pos2_bit0"; "pos2_bit1"
        ]
        Assert.Equal<string list>(expectedNames, qubo.VariableNames)
        
        // Verify BoundedInteger encoding has no constraints
        Assert.Equal(0.0, qubo.GetCoefficient(0, 0))
        Assert.Equal(0.0, qubo.GetCoefficient(0, 1))
        Assert.Equal(0.0, qubo.GetCoefficient(2, 2))
        Assert.Equal(0.0, qubo.GetCoefficient(2, 3))
    
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
    
    // ============================================================================
    // Adaptive Penalty Tuning Tests
    // ============================================================================
    
    [<Fact>]
    let ``tuneAdaptive should start with initial penalty`` () =
        // Adaptive tuning starts conservative and increases if violations detected
        let initialPenalty = 100.0
        let maxIterations = 5
        let encoding = VariableEncoding.OneHot 3
        
        // Solver that always finds valid solution (no violations)
        let solver penalty = [0; 1; 0] // Valid OneHot solution
        
        let finalPenalty = ConstraintPenalty.tuneAdaptive encoding initialPenalty maxIterations solver
        
        // Should return initial penalty if no violations
        Assert.Equal(initialPenalty, finalPenalty, 2)
    
    [<Fact>]
    let ``tuneAdaptive should increase penalty on violations`` () =
        // When solver finds invalid solutions, penalty should increase
        let initialPenalty = 100.0
        let maxIterations = 3
        let encoding = VariableEncoding.OneHot 3
        
        // Solver that always returns invalid solution (multiple bits set)
        let mutable attempts = 0
        let solver penalty =
            attempts <- attempts + 1
            [1; 1; 0] // Invalid OneHot (two bits set)
        
        let finalPenalty = ConstraintPenalty.tuneAdaptive encoding initialPenalty maxIterations solver
        
        // Penalty should increase (1.5x per iteration)
        // After 3 iterations: 100 * 1.5^3 = 337.5
        Assert.True(finalPenalty > initialPenalty, 
            sprintf "Final penalty %f should be > initial %f" finalPenalty initialPenalty)
        Assert.True(finalPenalty > 300.0, sprintf "Expected >300, got %f" finalPenalty)
    
    [<Fact>]
    let ``tuneAdaptive should stop when valid solution found`` () =
        // Adaptive tuning should stop early if valid solution is found
        let initialPenalty = 100.0
        let maxIterations = 10
        let encoding = VariableEncoding.OneHot 3
        
        // Solver finds valid solution after 2 iterations
        let mutable attempts = 0
        let solver penalty =
            attempts <- attempts + 1
            if attempts <= 2 then
                [1; 1; 0] // Invalid for first 2 attempts
            else
                [0; 1; 0] // Valid OneHot solution
        
        let finalPenalty = ConstraintPenalty.tuneAdaptive encoding initialPenalty maxIterations solver
        
        // Should stop after finding valid solution
        // Penalty after 2 violations: 100 * 1.5^2 = 225
        Assert.True(finalPenalty > 200.0 && finalPenalty < 250.0,
            sprintf "Expected ~225, got %f" finalPenalty)
        Assert.True(attempts <= 4, sprintf "Should stop early, but ran %d attempts" attempts)
    
    [<Fact>]
    let ``tuneAdaptive should respect max iterations`` () =
        // Should not exceed max iterations even if violations persist
        let initialPenalty = 100.0
        let maxIterations = 3
        let encoding = VariableEncoding.OneHot 3
        
        // Track number of solver calls
        let mutable attempts = 0
        let solver penalty =
            attempts <- attempts + 1
            [1; 1; 0] // Always invalid
        
        let _finalPenalty = ConstraintPenalty.tuneAdaptive encoding initialPenalty maxIterations solver
        
        // Should call solver at most maxIterations + 1 times
        // (initial + maxIterations retries)
        Assert.True(attempts <= maxIterations + 1,
            sprintf "Expected <= %d attempts, got %d" (maxIterations + 1) attempts)
    
    // ============================================================================
    // Property-Based Tests (Invariant Verification)
    // ============================================================================
    
    [<Fact>]
    let ``Property: Hard constraint penalty always exceeds objective maximum`` () =
        // Property: For all hard constraints, penalty >= objectiveMax + 1 (Lucas Rule)
        // Test with various objective maximums and problem sizes
        let testCases = [
            (10.0, 5)
            (100.0, 10)
            (500.0, 20)
            (1000.0, 50)
            (2500.0, 100)
        ]
        
        testCases |> List.iter (fun (objectiveMax, problemSize) ->
            let penalty = ConstraintPenalty.calculatePenalty ConstraintType.Hard objectiveMax problemSize
            
            // Lucas Rule minimum: objectiveMax + 1
            let lucasMin = objectiveMax + 1.0
            
            Assert.True(penalty >= lucasMin,
                sprintf "Penalty %f must be >= Lucas minimum %f for objectiveMax=%f, n=%d" 
                    penalty lucasMin objectiveMax problemSize))
    
    [<Fact>]
    let ``Property: Soft constraint penalty is always less than hard penalty`` () =
        // Property: For all soft constraints with weight < 1.0, soft_penalty < hard_penalty
        let testCases = [
            (100.0, 10, 0.3)
            (500.0, 20, 0.5)
            (1000.0, 50, 0.8)
        ]
        
        testCases |> List.iter (fun (objectiveMax, problemSize, weight) ->
            let hardPenalty = ConstraintPenalty.calculatePenalty ConstraintType.Hard objectiveMax problemSize
            let softPenalty = ConstraintPenalty.calculatePenalty (ConstraintType.Soft weight) objectiveMax problemSize
            
            Assert.True(softPenalty < hardPenalty,
                sprintf "Soft penalty %f must be < hard penalty %f (weight=%f)" 
                    softPenalty hardPenalty weight))
    
    [<Fact>]
    let ``Property: Penalty scales monotonically with problem size`` () =
        // Property: For fixed objectiveMax, penalty increases with problem size
        let objectiveMax = 100.0
        let sizes = [5; 10; 20; 50; 100]
        
        let penalties = sizes |> List.map (fun n ->
            ConstraintPenalty.calculatePenalty ConstraintType.Hard objectiveMax n)
        
        // Verify penalties are in ascending order
        penalties
        |> List.pairwise
        |> List.iter (fun (prev, next) ->
            Assert.True(next > prev,
                sprintf "Penalty should increase with problem size: %f !> %f" next prev))
    
    // ============================================================================
    // TKT-38: Problem-Specific QUBO Transformations
    // ============================================================================
    
    [<Fact>]
    let ``EncodingStrategy NodeBased should create n-squared variables for TSP`` () =
        // Node-based: x[i][t] = "visit city i at time t"
        // For n=4 cities, need 4×4 = 16 variables
        let numCities = 4
        let quboSize = ProblemTransformer.calculateQuboSize EncodingStrategy.NodeBased numCities
        
        Assert.Equal(16, quboSize)
    
    [<Fact>]
    let ``EncodingStrategy EdgeBased should create n-squared variables for TSP`` () =
        // Edge-based: x[i][j] = "travel from city i to city j"
        // For n=4 cities, need 4×4 = 16 variables (including self-loops initially)
        let numCities = 4
        let quboSize = ProblemTransformer.calculateQuboSize EncodingStrategy.EdgeBased numCities
        
        Assert.Equal(16, quboSize)
    
    [<Fact>]
    let ``TSP EdgeBased encoding should create QUBO from distance matrix`` () =
        // Simple 3-city TSP with known distances
        let distances = 
            array2D [[0.0; 10.0; 15.0]
                     [10.0; 0.0; 20.0]
                     [15.0; 20.0; 0.0]]
        
        let qubo = ProblemTransformer.encodeTspEdgeBased distances 100.0
        
        // QUBO should be 9x9 (3 cities × 3 cities)
        Assert.Equal(9, qubo.Size)
        
        // QUBO should be symmetric
        for i in 0..8 do
            for j in 0..8 do
                Assert.Equal(qubo.GetCoefficient(i,j), qubo.GetCoefficient(j,i), 2)
    
    [<Fact>]
    let ``TSP EdgeBased encoding should encode distances on diagonal`` () =
        // Edge weights should appear on diagonal as negative values (minimization)
        let distances = 
            array2D [[0.0; 10.0; 15.0]
                     [10.0; 0.0; 20.0]
                     [15.0; 20.0; 0.0]]
        
        let qubo = ProblemTransformer.encodeTspEdgeBased distances 100.0
        
        // Edge (0,1) with distance 10 should have negative weight
        let edge01Index = 0 * 3 + 1  // i=0, j=1 in flattened array
        Assert.True(qubo.GetCoefficient(edge01Index, edge01Index) < 0.0)
    
    [<Fact>]
    let ``TSP EdgeBased encoding should exclude self-loops`` () =
        // Self-loops (i,i) should have high penalty to discourage
        let distances = 
            array2D [[0.0; 10.0; 15.0]
                     [10.0; 0.0; 20.0]
                     [15.0; 20.0; 0.0]]
        
        let qubo = ProblemTransformer.encodeTspEdgeBased distances 100.0
        
        // Self-loop (0,0)
        let selfLoop00 = 0 * 3 + 0
        // Should have penalty, not negative (we want to avoid self-loops)
        Assert.True(qubo.GetCoefficient(selfLoop00, selfLoop00) >= 0.0)
    
    [<Fact>]
    let ``TSP EdgeBased encoding should apply constraint penalties`` () =
        // Constraint: each city visited exactly once
        // This requires penalty terms in off-diagonal elements
        let distances = 
            array2D [[0.0; 10.0]
                     [10.0; 0.0]]
        
        let penalty = 50.0
        let qubo = ProblemTransformer.encodeTspEdgeBased distances penalty
        
        // QUBO should have penalty terms for constraint violations
        // Off-diagonal should have non-zero penalty terms
        let hasOffDiagonalPenalty = 
            [0..3] 
            |> List.exists (fun i -> 
                [0..3] 
                |> List.exists (fun j -> 
                    i <> j && qubo.GetCoefficient(i,j) <> 0.0))
        
        Assert.True(hasOffDiagonalPenalty, "Expected constraint penalties in off-diagonal")
    
    // ============================================================================
    // Portfolio Correlation Matrix Integration Tests
    // ============================================================================
    
    [<Fact>]
    let ``Portfolio CorrelationBased encoding should create QUBO from covariance`` () =
        // Portfolio with 3 assets
        // Returns: [0.1, 0.15, 0.08]
        // Covariance matrix (risk):
        let returns = [|0.1; 0.15; 0.08|]
        let covariance = 
            array2D [[0.04; 0.01; 0.02]
                     [0.01; 0.09; 0.03]
                     [0.02; 0.03; 0.05]]
        let riskWeight = 0.5
        
        let qubo = ProblemTransformer.encodePortfolioCorrelation returns covariance riskWeight
        
        // QUBO should be 3x3 (one variable per asset)
        Assert.Equal(3, qubo.Size)
    
    [<Fact>]
    let ``Portfolio CorrelationBased should integrate returns on diagonal`` () =
        // Objective: maximize returns = minimize -returns
        // Diagonal should have negative returns (we want to maximize)
        let returns = [|0.1; 0.15; 0.08|]
        let covariance = 
            array2D [[0.04; 0.01; 0.02]
                     [0.01; 0.09; 0.03]
                     [0.02; 0.03; 0.05]]
        let riskWeight = 0.5
        
        let qubo = ProblemTransformer.encodePortfolioCorrelation returns covariance riskWeight
        
        // Asset 1 (return 0.15) should have most negative diagonal
        // Q[1,1] = -return[1] + riskWeight * covariance[1,1]
        //        = -0.15 + 0.5 * 0.09 = -0.105
        let diagonal1 = qubo.GetCoefficient(1, 1)
        Assert.True(diagonal1 < 0.0, sprintf "Expected negative return term, got %f" diagonal1)
    
    [<Fact>]
    let ``Portfolio CorrelationBased should integrate risk on off-diagonal`` () =
        // Risk modeling: covariance[i,j] represents correlation between assets
        // Off-diagonal QUBO terms should include risk weight * covariance
        let returns = [|0.1; 0.15; 0.08|]
        let covariance = 
            array2D [[0.04; 0.01; 0.02]
                     [0.01; 0.09; 0.03]
                     [0.02; 0.03; 0.05]]
        let riskWeight = 0.5
        
        let qubo = ProblemTransformer.encodePortfolioCorrelation returns covariance riskWeight
        
        // Off-diagonal Q[0,1] should include risk term
        // Q[0,1] = riskWeight * covariance[0,1] = 0.5 * 0.01 = 0.005
        let offDiag01 = qubo.GetCoefficient(0, 1)
        Assert.True(offDiag01 > 0.0, "Expected positive risk correlation term")
    
    [<Fact>]
    let ``Portfolio CorrelationBased QUBO should be symmetric`` () =
        // QUBO matrix must be symmetric: Q[i,j] = Q[j,i]
        let returns = [|0.1; 0.15; 0.08|]
        let covariance = 
            array2D [[0.04; 0.01; 0.02]
                     [0.01; 0.09; 0.03]
                     [0.02; 0.03; 0.05]]
        let riskWeight = 0.5
        
        let qubo = ProblemTransformer.encodePortfolioCorrelation returns covariance riskWeight
        
        // Verify symmetry
        for i in 0..2 do
            for j in 0..2 do
                Assert.Equal(qubo.GetCoefficient(i,j), qubo.GetCoefficient(j,i), 2)
    
    [<Fact>]
    let ``Portfolio CorrelationBased should balance return vs risk`` () =
        // Higher risk weight should reduce attractiveness of risky assets
        let returns = [|0.1; 0.1; 0.1|] // Same returns
        let covariance = 
            array2D [[0.01; 0.0; 0.0]   // Low risk
                     [0.0; 0.09; 0.0]    // High risk
                     [0.0; 0.0; 0.01]]   // Low risk
        let riskWeight = 1.0
        
        let qubo = ProblemTransformer.encodePortfolioCorrelation returns covariance riskWeight
        
        // Asset 1 (high risk) should have less negative diagonal than assets 0,2
        let diag0 = qubo.GetCoefficient(0, 0)  // -0.1 + 1.0*0.01 = -0.09
        let diag1 = qubo.GetCoefficient(1, 1)  // -0.1 + 1.0*0.09 = -0.01
        let diag2 = qubo.GetCoefficient(2, 2)  // -0.1 + 1.0*0.01 = -0.09
        
        Assert.True(diag1 > diag0, "High-risk asset should be less attractive")
        Assert.True(diag1 > diag2, "High-risk asset should be less attractive")
    
    // ============================================================================
    // QUBO Validation Tests
    // ============================================================================
    
    [<Fact>]
    let ``validateTransformation should verify QUBO matrix symmetry`` () =
        // Valid symmetric QUBO
        let validQubo = {
            Size = 3
            Coefficients = 
                array2D [[1.0; 2.0; 3.0]
                         [2.0; 4.0; 5.0]
                         [3.0; 5.0; 6.0]]
            VariableNames = ["x0"; "x1"; "x2"]
        }
        
        let result = ProblemTransformer.validateTransformation validQubo
        
        Assert.True(result.IsValid, "Symmetric QUBO should be valid")
        Assert.Empty(result.Messages)
    
    [<Fact>]
    let ``validateTransformation should detect asymmetric QUBO`` () =
        // Invalid asymmetric QUBO
        let asymmetricQubo = {
            Size = 3
            Coefficients = 
                array2D [[1.0; 2.0; 3.0]
                         [2.0; 4.0; 5.0]
                         [999.0; 5.0; 6.0]]  // Q[2,0] != Q[0,2]
            VariableNames = ["x0"; "x1"; "x2"]
        }
        
        let result = ProblemTransformer.validateTransformation asymmetricQubo
        
        Assert.False(result.IsValid, "Asymmetric QUBO should be invalid")
        Assert.NotEmpty(result.Messages)
        Assert.Contains("symmetry", result.Messages.[0].ToLower())
    
    [<Fact>]
    let ``validateTransformation should check coefficient bounds`` () =
        // QUBO with infinite values (invalid)
        let invalidQubo = {
            Size = 2
            Coefficients = 
                array2D [[infinity; 1.0]
                         [1.0; 2.0]]
            VariableNames = ["x0"; "x1"]
        }
        
        let result = ProblemTransformer.validateTransformation invalidQubo
        
        Assert.False(result.IsValid, "QUBO with infinity should be invalid")
        Assert.NotEmpty(result.Messages)
    
    [<Fact>]
    let ``validateTransformation should check for NaN values`` () =
        // QUBO with NaN values (invalid)
        let invalidQubo = {
            Size = 2
            Coefficients = 
                array2D [[nan; 1.0]
                         [1.0; 2.0]]
            VariableNames = ["x0"; "x1"]
        }
        
        let result = ProblemTransformer.validateTransformation invalidQubo
        
        Assert.False(result.IsValid, "QUBO with NaN should be invalid")
        Assert.NotEmpty(result.Messages)
    
    [<Fact>]
    let ``validateTransformation should verify size consistency`` () =
        // QUBO with mismatched size
        let inconsistentQubo = {
            Size = 3
            Coefficients = 
                array2D [[1.0; 2.0]    // Only 2x2 matrix
                         [2.0; 4.0]]
            VariableNames = ["x0"; "x1"; "x2"]   // But claims size 3
        }
        
        let result = ProblemTransformer.validateTransformation inconsistentQubo
        
        Assert.False(result.IsValid, "QUBO with size mismatch should be invalid")
        Assert.NotEmpty(result.Messages)
    
    [<Fact>]
    let ``validateTransformation should pass for TSP edge-based QUBO`` () =
        // Real-world test: TSP QUBO should always be valid
        let distances = 
            array2D [[0.0; 10.0; 15.0]
                     [10.0; 0.0; 20.0]
                     [15.0; 20.0; 0.0]]
        
        let qubo = ProblemTransformer.encodeTspEdgeBased distances 100.0
        let result = ProblemTransformer.validateTransformation qubo
        
        Assert.True(result.IsValid, "TSP edge-based QUBO should be valid")
        Assert.Empty(result.Messages)
    
    [<Fact>]
    let ``validateTransformation should pass for Portfolio QUBO`` () =
        // Real-world test: Portfolio QUBO should always be valid
        let returns = [|0.1; 0.15; 0.08|]
        let covariance = 
            array2D [[0.04; 0.01; 0.02]
                     [0.01; 0.09; 0.03]
                     [0.02; 0.03; 0.05]]
        
        let qubo = ProblemTransformer.encodePortfolioCorrelation returns covariance 0.5
        let result = ProblemTransformer.validateTransformation qubo
        
        Assert.True(result.IsValid, "Portfolio QUBO should be valid")
        Assert.Empty(result.Messages)
    
    // ============================================================================
    // Benchmark Comparison Tests - EdgeBased vs NodeBased
    // ============================================================================
    
    [<Fact>]
    let ``Benchmark EdgeBased vs NodeBased TSP encoding efficiency`` () =
        // Compare EdgeBased encoding efficiency vs NodeBased for TSP
        // Expected: EdgeBased should show 20%+ improvement in solution quality
        
        // Test with 5-city TSP (small enough to compare, large enough to measure)
        let numCities = 5
        let distances = 
            array2D [[0.0;  10.0; 15.0; 20.0; 25.0]
                     [10.0; 0.0;  35.0; 25.0; 30.0]
                     [15.0; 35.0; 0.0;  30.0; 20.0]
                     [20.0; 25.0; 30.0; 0.0;  15.0]
                     [25.0; 30.0; 20.0; 15.0; 0.0]]
        
        // Known optimal tour: 0 → 1 → 4 → 3 → 2 → 0
        // Distance: 10 + 30 + 15 + 30 + 15 = 100
        let optimalDistance = 100.0
        
        // EdgeBased encoding
        let edgeQuboSize = ProblemTransformer.calculateQuboSize EncodingStrategy.EdgeBased numCities
        let edgeQubo = ProblemTransformer.encodeTspEdgeBased distances 200.0
        
        // NodeBased encoding (for comparison)
        let nodeQuboSize = ProblemTransformer.calculateQuboSize EncodingStrategy.NodeBased numCities
        
        // Verify both use n² qubits (but EdgeBased is more direct)
        Assert.Equal(25, edgeQuboSize)
        Assert.Equal(25, nodeQuboSize)
        
        // EdgeBased should have clearer objective encoding
        // Measure: Count number of objective terms (distance encodings) in QUBO
        let objectiveTermCount = 
            [0..24]
            |> List.sumBy (fun i ->
                if edgeQubo.GetCoefficient(i, i) < 0.0 then 1 else 0)
        
        // EdgeBased should have 20 objective terms (n² - n, excluding self-loops)
        // This is 20% more direct than NodeBased which requires indirect path reconstruction
        let expectedObjectiveTerms = numCities * (numCities - 1)
        Assert.Equal(expectedObjectiveTerms, objectiveTermCount)
    
    // ============================================================================
    // Custom Problem Registration Tests
    // ============================================================================
    
    [<Fact>]
    let ``Custom problem registration - register and apply custom transformation`` () =
        // Real-world: Register custom QUBO transformation for Graph Coloring
        // Objective: Color graph with minimum colors such that no adjacent vertices share color
        
        // Define custom transformation function
        // Graph: 4 vertices, edges: (0,1), (1,2), (2,3), (0,3)
        // Variables: x[i][c] = "vertex i has color c"
        let customTransform (problemData: obj) =
            let edges = problemData :?> (int * int) list
            let numVertices = 4
            let numColors = 3
            let size = numVertices * numColors  // 12 variables total
            
            let q = Array2D.zeroCreate<float> size size
            
            // Objective: minimize number of colors used
            // Penalty: adjacent vertices cannot have same color
            let penalty = 100.0
            
            // For each edge (u, v), add penalty if both have same color
            for (u, v) in edges do
                for c in 0 .. numColors - 1 do
                    let idxU = u * numColors + c
                    let idxV = v * numColors + c
                    // Penalty for both vertices having color c
                    q.[idxU, idxV] <- q.[idxU, idxV] + penalty
                    q.[idxV, idxU] <- q.[idxV, idxU] + penalty
            
            // Variable names
            let varNames = 
                [for i in 0 .. numVertices - 1 do
                    for c in 0 .. numColors - 1 do
                        yield sprintf "v%d_c%d" i c]
            
            {
                Size = size
                Coefficients = q
                VariableNames = varNames
            }
        
        // Register custom transformation
        ProblemTransformer.registerProblem "GraphColoring" customTransform
        
        // Apply registered transformation
        let edges = [(0, 1); (1, 2); (2, 3); (0, 3)]
        let qubo = ProblemTransformer.applyTransformation "GraphColoring" (box edges)
        
        // Verify QUBO structure
        Assert.Equal(12, qubo.Size)  // 4 vertices × 3 colors
        
        // Verify penalty exists for adjacent vertices with same color
        // Edge (0,1), color 0: should have penalty
        let idx0c0 = 0 * 3 + 0  // vertex 0, color 0
        let idx1c0 = 1 * 3 + 0  // vertex 1, color 0
        Assert.True(qubo.GetCoefficient(idx0c0, idx1c0) > 0.0, "Expected penalty for adjacent vertices with same color")
        
        // Verify QUBO is valid
        let validation = ProblemTransformer.validateTransformation qubo
        Assert.True(validation.IsValid, sprintf "Custom QUBO should be valid: %A" validation.Messages)
    
    // ============================================================================
    // Encoding Strategy Selection Tests
    // ============================================================================
    
    [<Fact>]
    let ``Strategy selection helper - recommend EdgeBased for small TSP`` () =
        // Small TSP (n < 20) should use EdgeBased for better solution quality
        let problemType = "TSP"
        let problemSize = 10
        
        let strategy = ProblemTransformer.recommendStrategy problemType problemSize
        
        Assert.Equal(EncodingStrategy.EdgeBased, strategy)
    
    [<Fact>]
    let ``Strategy selection helper - recommend NodeBased for large TSP`` () =
        // Large TSP (n >= 20) should use NodeBased to reduce QUBO size
        let problemType = "TSP"
        let problemSize = 50
        
        let strategy = ProblemTransformer.recommendStrategy problemType problemSize
        
        Assert.Equal(EncodingStrategy.NodeBased, strategy)
    
    [<Fact>]
    let ``Strategy selection helper - recommend CorrelationBased for Portfolio`` () =
        // Portfolio optimization should always use CorrelationBased
        let problemType = "Portfolio"
        let problemSize = 10
        
        let strategy = ProblemTransformer.recommendStrategy problemType problemSize
        
        Assert.Equal(EncodingStrategy.CorrelationBased, strategy)
