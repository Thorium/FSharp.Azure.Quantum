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

// TKT-36: Advanced Variable Encoding Strategies
[<Struct>]
type VariableEncoding =
    | Binary
    | OneHot of options: int
    | DomainWall of levels: int
    | BoundedInteger of min: int * max: int
    
    /// Calculate number of qubits needed for this encoding
    static member qubitCount (encoding: VariableEncoding) : int =
        match encoding with
        | Binary -> 1
        | OneHot n -> n
        | DomainWall n -> n - 1
        | BoundedInteger(min, max) ->
            let range = max - min + 1
            // ceil(log2(range))
            if range <= 1 then 1
            else int (System.Math.Ceiling(System.Math.Log(float range, 2.0)))

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
