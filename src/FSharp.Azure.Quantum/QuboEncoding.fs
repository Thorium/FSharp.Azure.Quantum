namespace FSharp.Azure.Quantum

// Types for variable encoding (module-level, publicly accessible)
type VariableType =
    | BinaryVar
    | IntegerVar of min: int * max: int
    | CategoricalVar of categories: string list

type Variable = {
    Name: string
    VarType: VariableType
}

/// Variable encoding strategies for QUBO optimization.
/// 
/// Selection guide:
/// - Binary: True binary decisions (include/exclude)
/// - OneHot: Unordered categories (route selection)
/// - DomainWall: Ordered levels (priorities) - 25% fewer qubits
/// - BoundedInteger: Integer ranges (quantities) - logarithmic efficiency
[<Struct>]
type VariableEncoding =
    | Binary
    | OneHot of options: int
    | DomainWall of levels: int
    | BoundedInteger of min: int * max: int

/// Constraint type classification for penalty calculation.
[<Struct>]
type ConstraintType =
    | Hard
    | Soft of preferenceWeight: float

/// Constraint violation description
type ConstraintViolation = {
    EncodingType: string
    Message: string
    ExpectedCount: int voption
    ActualCount: int voption
}

/// Problem-specific encoding strategies for QUBO transformations
[<Struct>]
type EncodingStrategy =
    | NodeBased
    | EdgeBased
    | CorrelationBased
    | Custom of transformer: (int -> int)

/// Operations for variable encoding strategies
module VariableEncoding =
    
    /// Calculate number of qubits needed for an encoding strategy.
    let qubitCount (encoding: VariableEncoding) : int =
        match encoding with
        | Binary -> 1
        | OneHot n -> n
        | DomainWall n -> n - 1
        | BoundedInteger(min, max) ->
            let range = max - min + 1
            if range <= 1 then 1
            else int (System.Math.Ceiling(System.Math.Log(float range, 2.0)))
    
    /// Encode a value to qubit assignments.
    let encode (encoding: VariableEncoding) (value: int) : int list =
        match encoding with
        | Binary ->
            [value]
        
        | OneHot n ->
            // One-hot: Single bit set at position 'value'
            List.init n (fun i -> if i = value then 1 else 0)
        
        | DomainWall levels ->
            // Domain-wall: Wall of 1s followed by 0s
            let numQubits = levels - 1
            List.init numQubits (fun i -> if i < value - 1 then 1 else 0)
        
        | BoundedInteger(min, _) ->
            // Binary encoding (LSB first)
            let normalizedValue = value - min
            let numQubits = qubitCount encoding
            List.init numQubits (fun i ->
                if (normalizedValue &&& (1 <<< i)) <> 0 then 1 else 0)
    
    /// Decode qubit assignments back to original value.
    let decode (encoding: VariableEncoding) (bits: int list) : int =
        match encoding with
        | Binary ->
            bits.[0]
        
        | OneHot _ ->
            // Find which bit is set
            bits
            |> List.tryFindIndex ((=) 1)
            |> Option.defaultValue 0
        
        | DomainWall _ ->
            // Count number of 1s and add 1
            bits
            |> List.sumBy id
            |> (+) 1
        
        | BoundedInteger(min, _) ->
            // Convert binary (LSB first) to integer
            bits
            |> List.indexed
            |> List.sumBy (fun (i, bit) -> bit * (1 <<< i))
            |> (+) min
    
    /// Roundtrip encoding validation (encode then decode).
    let roundtrip encoding value =
        value |> encode encoding |> decode encoding
    
    /// Generate QUBO penalty matrix for encoding constraints.
    /// Weight parameter controls penalty strength (higher = stricter constraint).
    let constraintPenalty (encoding: VariableEncoding) (weight: float) : float[,] =
        let n = qubitCount encoding
        let penalty = Array2D.zeroCreate<float> n n
        
        match encoding with
        | Binary ->
            // No constraints needed for binary variables
            penalty
        
        | OneHot _ ->
            // Constraint: Exactly one qubit must be active
            // Penalty: (Σxi - 1)^2 = Σxi^2 - 2Σxi + ΣΣ(2xixj) + 1
            // For binary: xi^2 = xi
            // QUBO form: -Σxi + ΣΣ(2xixj)
            
            // Diagonal terms: -weight per qubit
            for i in 0 .. n - 1 do
                penalty.[i, i] <- -weight
            
            // Off-diagonal terms: 2 * weight per interaction
            for i in 0 .. n - 1 do
                for j in i + 1 .. n - 1 do
                    penalty.[i, j] <- 2.0 * weight
                    penalty.[j, i] <- 2.0 * weight
            
            penalty
        
        | DomainWall _ ->
            // Domain-wall naturally enforces ordering through bit pattern
            // No additional constraints needed
            penalty
        
        | BoundedInteger(min, max) ->
            // Constraint: Prevent values outside [min, max] range
            // Penalty higher-order bits to discourage overflow
            let range = max - min + 1
            
            // Apply quadratic penalty to bits that would exceed range
            for i in 0 .. n - 1 do
                let bitValue = 1 <<< i
                // If this bit alone exceeds range, penalize it
                if bitValue > range then
                    penalty.[i, i] <- weight * float bitValue
            
            penalty

type QuboMatrix = {
    Size: int
    Coefficients: float[,]
    VariableNames: string list
} with
    member this.GetCoefficient(i: int, j: int) : float =
        this.Coefficients.[i, j]

type VariableAssignment = {
    Name: string
    Value: int
}

type Solution = {
    Assignments: VariableAssignment list
}

// Constraint types for custom QUBO encoding
type Constraint =
    | EqualityConstraint of variableIndices: int list * targetValue: float
    | InequalityConstraint of variableIndices: int list * maxValue: float

/// QUBO (Quadratic Unconstrained Binary Optimization) Encoding Module
/// Converts optimization problems into quantum-compatible QUBO format
module QuboEncoding =
    
    // Helper: Calculate number of qubits needed for a variable
    let private qubitCountFor (varType: VariableType) : int =
        match varType with
        | BinaryVar -> 1
        | IntegerVar(min, max) -> (max - min + 1) // One-hot encoding
        | CategoricalVar(categories) -> categories.Length
    
    // Helper: Generate qubit names for a variable
    let private qubitNamesFor (varName: string) (varType: VariableType) : string list =
        match varType with
        | BinaryVar -> [varName]
        | IntegerVar(min, max) ->
            [min .. max] |> List.map (fun i -> sprintf "%s_%d" varName i)
        | CategoricalVar(categories) ->
            categories |> List.map (fun cat -> sprintf "%s_%s" varName cat)
    
    // Encoding functions
    let encodeVariables (variables: Variable list) : QuboMatrix =
        // Calculate total qubits needed
        let totalQubits = variables |> List.sumBy (fun v -> qubitCountFor v.VarType)
        
        // Generate all qubit names
        let allQubitNames =
            variables
            |> List.collect (fun v -> qubitNamesFor v.Name v.VarType)
        
        // Initialize QUBO matrix with zeros (no bias or interactions by default)
        let coefficients = Array2D.zeroCreate<float> totalQubits totalQubits
        
        {
            Size = totalQubits
            Coefficients = coefficients
            VariableNames = allQubitNames
        }
    
    let encodeVariablesWithConstraints (variables: Variable list) : QuboMatrix =
        // Start with basic encoding
        let qubo = encodeVariables variables
        
        // Add one-hot constraints for integer/categorical variables
        // Constraint: exactly one bit must be set
        // Penalty: (sum(bits) - 1)^2 = sum(bits^2) - 2*sum(bits) + sum(cross_terms)
        // For binary: bits^2 = bits, so: -sum(bits) + sum(cross_terms)
        
        // Helper to apply one-hot constraint penalties to a qubit range
        let applyOneHotPenalty startIdx numBits =
            // Diagonal penalties: -1 * bit (encourages selection)
            [0 .. numBits - 1]
            |> List.iter (fun i ->
                qubo.Coefficients.[startIdx + i, startIdx + i] <- -1.0)
            
            // Off-diagonal penalties: +2 * bit_i * bit_j (discourages multiple)
            [0 .. numBits - 1]
            |> List.iter (fun i ->
                [i + 1 .. numBits - 1]
                |> List.iter (fun j ->
                    qubo.Coefficients.[startIdx + i, startIdx + j] <- 2.0
                    qubo.Coefficients.[startIdx + j, startIdx + i] <- 2.0))
        
        // Apply constraints using fold to track offset functionally
        variables
        |> List.fold (fun offset var ->
            match var.VarType with
            | BinaryVar ->
                offset + 1
            | IntegerVar(min, max) ->
                let numBits = max - min + 1
                applyOneHotPenalty offset numBits
                offset + numBits
            | CategoricalVar(categories) ->
                let numBits = categories.Length
                applyOneHotPenalty offset numBits
                offset + numBits
        ) 0
        |> ignore
        
        qubo
    
    let encodeVariablesWithCustomConstraints (variables: Variable list) (constraints: Constraint list) (penaltyWeight: float) : QuboMatrix =
        // Start with basic encoding
        let qubo = encodeVariables variables
        
        // Helper to apply equality constraint penalties
        let applyEqualityConstraint indices target =
            // Penalty: (sum(x_i) - target)^2
            // Diagonal terms: (1 - 2*target) * weight
            indices
            |> List.iter (fun i ->
                qubo.Coefficients.[i, i] <- qubo.Coefficients.[i, i] + penaltyWeight * (1.0 - 2.0 * target))
            
            // Off-diagonal terms: 2 * weight
            indices
            |> List.iter (fun i ->
                indices
                |> List.filter (fun j -> i < j)
                |> List.iter (fun j ->
                    qubo.Coefficients.[i, j] <- qubo.Coefficients.[i, j] + penaltyWeight * 2.0
                    qubo.Coefficients.[j, i] <- qubo.Coefficients.[j, i] + penaltyWeight * 2.0))
        
        // Helper to apply inequality constraint penalties
        let applyInequalityConstraint indices maxVal =
            indices
            |> List.filter (fun i -> float i > maxVal)
            |> List.iter (fun i ->
                qubo.Coefficients.[i, i] <- qubo.Coefficients.[i, i] + penaltyWeight)
        
        // Apply each constraint functionally
        constraints
        |> List.iter (fun constr ->
            match constr with
            | EqualityConstraint(indices, target) ->
                applyEqualityConstraint indices target
            | InequalityConstraint(indices, maxVal) ->
                applyInequalityConstraint indices maxVal)
        
        qubo
    
    let decodeSolution (variables: Variable list) (binarySolution: int list) : Solution =
        // Decode binary solution back to variable assignments using fold to track offset
        let (assignments, _) =
            variables
            |> List.fold (fun (accAssignments, offset) var ->
                match var.VarType with
                | BinaryVar ->
                    let value = binarySolution.[offset]
                    let assignment = { Name = var.Name; Value = value }
                    (assignment :: accAssignments, offset + 1)
                
                | IntegerVar(min, max) ->
                    let numBits = max - min + 1
                    let bits = binarySolution.[offset .. offset + numBits - 1]
                    
                    // Find which bit is set (one-hot decoding)
                    let value =
                        bits
                        |> List.indexed
                        |> List.tryFind (fun (_, bit) -> bit = 1)
                        |> Option.map (fun (idx, _) -> min + idx)
                        |> Option.defaultValue min
                    
                    let assignment = { Name = var.Name; Value = value }
                    (assignment :: accAssignments, offset + numBits)
                
                | CategoricalVar(categories) ->
                    let numBits = categories.Length
                    let bits = binarySolution.[offset .. offset + numBits - 1]
                    
                    // Find which category is selected
                    let categoryIndex =
                        bits
                        |> List.indexed
                        |> List.tryFind (fun (_, bit) -> bit = 1)
                        |> Option.map fst
                        |> Option.defaultValue 0
                    
                    let assignment = { Name = var.Name; Value = categoryIndex }
                    (assignment :: accAssignments, offset + numBits)
            ) ([], 0)
        
        { Assignments = List.rev assignments }

/// Constraint penalty optimization for QUBO problems
module ConstraintPenalty =
    
    /// Calculate penalty weight using Lucas Rule with problem-size scaling.
    /// 
    /// Lucas Rule: λ ≥ max(H_objective) + 1
    /// Size Scaling: λ * sqrt(problemSize)
    let rec calculatePenalty (constraintType: ConstraintType) (objectiveMax: float) (problemSize: int) : float =
        match constraintType with
        | Hard ->
            // Lucas Rule + square root size scaling
            let lucasPenalty = objectiveMax + 1.0
            let sizeFactor = sqrt (float problemSize)
            lucasPenalty * sizeFactor
        
        | Soft preferenceWeight ->
            // Soft constraints: fraction of hard constraint penalty
            let hardPenalty = calculatePenalty Hard objectiveMax problemSize
            hardPenalty * preferenceWeight
    
    /// Validate that a solution satisfies encoding constraints.
    let validateConstraints (encoding: VariableEncoding) (solution: int list) : ConstraintViolation list =
        match encoding with
        | Binary ->
            // Binary has no constraints (0 or 1 both valid)
            []
        
        | OneHot n ->
            // Constraint: Exactly one bit must be set
            let onesCount = solution |> List.sumBy id
            if onesCount <> 1 then
                [{
                    EncodingType = "OneHot"
                    Message = sprintf "OneHot encoding requires exactly 1 active bit, found %d" onesCount
                    ExpectedCount = ValueSome 1
                    ActualCount = ValueSome onesCount
                }]
            else
                []
        
        | DomainWall _ ->
            // Domain-wall naturally enforces ordering through bit pattern
            // Valid patterns: 0*, 1+0*, 1* (wall of 1s followed by 0s)
            // No explicit constraint violation possible
            []
        
        | BoundedInteger(min, max) ->
            // Constraint: Decoded value must be within [min, max]
            let decodedValue = VariableEncoding.decode encoding solution
            if decodedValue < min || decodedValue > max then
                [{
                    EncodingType = "BoundedInteger"
                    Message = sprintf "Value %d is outside bounds [%d, %d]" decodedValue min max
                    ExpectedCount = ValueNone
                    ActualCount = ValueNone
                }]
            else
                []
    
    /// Adaptive penalty tuning: start low, increase if violations detected.
    /// 
    /// Solver function takes penalty weight and returns a solution.
    /// If solution violates constraints, penalty is increased by 1.5x and solver is retried.
    let rec tuneAdaptive 
        (encoding: VariableEncoding) 
        (penalty: float) 
        (maxIterations: int) 
        (solver: float -> int list) : float =
        
        if maxIterations <= 0 then
            penalty
        else
            // Get solution with current penalty
            let solution = solver penalty
            
            // Check for constraint violations
            let violations = validateConstraints encoding solution
            
            if List.isEmpty violations then
                // Valid solution found - return current penalty
                penalty
            else
                // Violations detected - increase penalty and retry
                let newPenalty = penalty * 1.5
                tuneAdaptive encoding newPenalty (maxIterations - 1) solver

/// Problem-specific QUBO transformations for optimization problems
module ProblemTransformer =
    
    /// Calculate QUBO matrix size based on encoding strategy and problem size.
    let calculateQuboSize (strategy: EncodingStrategy) (problemSize: int) : int =
        match strategy with
        | NodeBased ->
            // Node-based: x[i][t] = "visit city i at time t"
            // For n cities: n × n variables
            problemSize * problemSize
        
        | EdgeBased ->
            // Edge-based: x[i][j] = "travel from city i to city j"
            // For n cities: n × n variables (including self-loops)
            problemSize * problemSize
        
        | CorrelationBased ->
            // Correlation matrix: one variable per asset
            problemSize
        
        | Custom transformer ->
            // Use custom size calculator
            transformer problemSize
    
    /// Encode TSP using edge-based representation.
    /// 
    /// Edge-based: Variable x[i][j] represents "travel from city i to city j"
    /// Objective: minimize total distance = minimize Σ distance[i,j] * x[i,j]
    /// 
    /// QUBO form: minimize x^T Q x
    /// - Diagonal Q[k,k] = -distance[i,j] for edge (i,j) at index k
    /// - Off-diagonal contains constraint penalties
    let encodeTspEdgeBased (distances: float[,]) (constraintPenalty: float) : QuboMatrix =
        let n = distances.GetLength(0)
        let size = n * n
        let q = Array2D.zeroCreate<float> size size
        
        // Helper: Convert (i,j) edge to flat index
        let edgeIndex i j = i * n + j
        
        // Step 1: Encode objective (minimize total distance)
        for i in 0 .. n - 1 do
            for j in 0 .. n - 1 do
                let idx = edgeIndex i j
                if i = j then
                    // Self-loops: penalize heavily (we don't want city to itself)
                    q.[idx, idx] <- constraintPenalty
                else
                    // Edge weight: negative because we want to minimize distance
                    // But we also want to SELECT edges, so use negative distance
                    q.[idx, idx] <- -distances.[i, j]
        
        // Step 2: Add constraint penalties
        // Constraint 1: Each city must be entered exactly once
        for j in 0 .. n - 1 do
            for i1 in 0 .. n - 1 do
                for i2 in i1 + 1 .. n - 1 do
                    let idx1 = edgeIndex i1 j
                    let idx2 = edgeIndex i2 j
                    // Penalty for multiple entries to city j
                    q.[idx1, idx2] <- q.[idx1, idx2] + constraintPenalty
                    q.[idx2, idx1] <- q.[idx2, idx1] + constraintPenalty
        
        // Constraint 2: Each city must be exited exactly once
        for i in 0 .. n - 1 do
            for j1 in 0 .. n - 1 do
                for j2 in j1 + 1 .. n - 1 do
                    let idx1 = edgeIndex i j1
                    let idx2 = edgeIndex i j2
                    // Penalty for multiple exits from city i
                    q.[idx1, idx2] <- q.[idx1, idx2] + constraintPenalty
                    q.[idx2, idx1] <- q.[idx2, idx1] + constraintPenalty
        
        // Generate variable names
        let varNames = 
            [for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    yield sprintf "edge_%d_%d" i j]
        
        {
            Size = size
            Coefficients = q
            VariableNames = varNames
        }
    
    /// Encode Portfolio optimization using correlation matrix.
    /// 
    /// Objective: maximize return - λ * risk
    /// where risk = x^T Σ x (Σ is covariance matrix)
    /// 
    /// QUBO form: minimize x^T Q x
    /// Q = -returns + λ * Σ
    /// - Diagonal Q[i,i] = -return[i] + λ * covariance[i,i]
    /// - Off-diagonal Q[i,j] = λ * covariance[i,j]
    let encodePortfolioCorrelation (returns: float[]) (covariance: float[,]) (riskWeight: float) : QuboMatrix =
        let n = returns.Length
        let q = Array2D.zeroCreate<float> n n
        
        // Encode objective: maximize return - λ * risk
        // This becomes: minimize -return + λ * risk
        for i in 0 .. n - 1 do
            for j in 0 .. n - 1 do
                if i = j then
                    // Diagonal: -return[i] + λ * variance[i]
                    q.[i, j] <- -returns.[i] + riskWeight * covariance.[i, j]
                else
                    // Off-diagonal: λ * covariance[i,j]
                    // Note: Covariance matrix is symmetric, so Q will be symmetric
                    q.[i, j] <- riskWeight * covariance.[i, j]
        
        // Generate variable names
        let varNames = 
            [for i in 0 .. n - 1 do
                yield sprintf "asset_%d" i]
        
        {
            Size = n
            Coefficients = q
            VariableNames = varNames
        }
