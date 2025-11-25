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
