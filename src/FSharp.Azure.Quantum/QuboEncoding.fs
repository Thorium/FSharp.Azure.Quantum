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

/// QUBO (Quadratic Unconstrained Binary Optimization) Encoding Module
/// Converts optimization problems into quantum-compatible QUBO format
module QuboEncoding =
    
    // Encoding functions
    let encodeVariables (variables: Variable list) : QuboMatrix =
        // For binary variables, each variable maps to one qubit
        // QUBO matrix initialized with zeros (no bias or interactions by default)
        let size = variables.Length
        let coefficients = Array2D.zeroCreate<float> size size
        let variableNames = variables |> List.map (fun v -> v.Name)
        
        {
            Size = size
            Coefficients = coefficients
            VariableNames = variableNames
        }
    
    let decodeSolution (variables: Variable list) (binarySolution: int list) : Solution =
        // Decode binary solution back to variable assignments
        let assignments =
            List.zip variables binarySolution
            |> List.map (fun (var, value) ->
                { Name = var.Name; Value = value })
        
        { Assignments = assignments }
