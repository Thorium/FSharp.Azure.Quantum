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
        
        let mutable offset = 0
        for var in variables do
            match var.VarType with
            | BinaryVar ->
                offset <- offset + 1
            | IntegerVar(min, max) ->
                let numBits = max - min + 1
                
                // Add diagonal penalties: -1 * bit (encourages selection)
                for i in 0 .. numBits - 1 do
                    qubo.Coefficients.[offset + i, offset + i] <- -1.0
                
                // Add off-diagonal penalties: +2 * bit_i * bit_j (discourages multiple)
                for i in 0 .. numBits - 1 do
                    for j in i + 1 .. numBits - 1 do
                        qubo.Coefficients.[offset + i, offset + j] <- 2.0
                        qubo.Coefficients.[offset + j, offset + i] <- 2.0
                
                offset <- offset + numBits
            | CategoricalVar(categories) ->
                let numBits = categories.Length
                
                // Add diagonal penalties: -1 * bit (encourages selection)
                for i in 0 .. numBits - 1 do
                    qubo.Coefficients.[offset + i, offset + i] <- -1.0
                
                // Add off-diagonal penalties: +2 * bit_i * bit_j (discourages multiple)
                for i in 0 .. numBits - 1 do
                    for j in i + 1 .. numBits - 1 do
                        qubo.Coefficients.[offset + i, offset + j] <- 2.0
                        qubo.Coefficients.[offset + j, offset + i] <- 2.0
                
                offset <- offset + numBits
        
        qubo
    
    let encodeVariablesWithCustomConstraints (variables: Variable list) (constraints: Constraint list) (penaltyWeight: float) : QuboMatrix =
        // Start with basic encoding
        let qubo = encodeVariables variables
        
        // Apply each constraint
        for constr in constraints do
            match constr with
            | EqualityConstraint(indices, target) ->
                // Penalty: (sum(x_i) - target)^2
                // Expansion: sum(x_i^2) + 2*sum(x_i*x_j) - 2*target*sum(x_i) + target^2
                // For binary: x^2 = x, so: sum(x_i) + 2*sum(x_i*x_j) - 2*target*sum(x_i) + target^2
                //                        = sum((1 - 2*target)*x_i) + 2*sum(x_i*x_j) + target^2
                
                // Diagonal terms: (1 - 2*target) * weight
                for i in indices do
                    qubo.Coefficients.[i, i] <- qubo.Coefficients.[i, i] + penaltyWeight * (1.0 - 2.0 * target)
                
                // Off-diagonal terms: 2 * weight
                for i in indices do
                    for j in indices do
                        if i < j then
                            qubo.Coefficients.[i, j] <- qubo.Coefficients.[i, j] + penaltyWeight * 2.0
                            qubo.Coefficients.[j, i] <- qubo.Coefficients.[j, i] + penaltyWeight * 2.0
            
            | InequalityConstraint(indices, maxVal) ->
                // For inequality x <= maxVal, use slack variables or penalty
                // Simple penalty: max(0, x - maxVal)^2
                // For now, just add a soft penalty discouraging values > maxVal
                for i in indices do
                    if float i > maxVal then
                        qubo.Coefficients.[i, i] <- qubo.Coefficients.[i, i] + penaltyWeight
        
        qubo
    
    let decodeSolution (variables: Variable list) (binarySolution: int list) : Solution =
        // Decode binary solution back to variable assignments
        let mutable offset = 0
        let assignments =
            variables |> List.map (fun var ->
                match var.VarType with
                | BinaryVar ->
                    let value = binarySolution.[offset]
                    offset <- offset + 1
                    { Name = var.Name; Value = value }
                
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
                    
                    offset <- offset + numBits
                    { Name = var.Name; Value = value }
                
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
                    
                    offset <- offset + numBits
                    { Name = var.Name; Value = categoryIndex }
            )
        
        { Assignments = assignments }
