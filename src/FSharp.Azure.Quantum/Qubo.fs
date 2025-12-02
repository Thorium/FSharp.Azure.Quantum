namespace FSharp.Azure.Quantum

/// Common QUBO (Quadratic Unconstrained Binary Optimization) encoding patterns
/// Shared across multiple optimization solvers (GraphColoring, TSP, TaskScheduling, NetworkFlow, Knapsack)
module Qubo =
    
    /// Combine QUBO terms functionally - adds value to existing key or creates new entry
    /// Used for accumulating linear and quadratic QUBO coefficients
    let combineTerms (key: int * int) (value: float) (qubo: Map<int * int, float>) : Map<int * int, float> =
        let newValue =
            match Map.tryFind key qubo with
            | Some existing -> existing + value
            | None -> value
        Map.add key newValue qubo
    
    /// Encode one-hot constraint: exactly one variable in a set equals 1
    /// Formula: λ * (1 - Σx_i)² = -λΣx_i + 2λΣΣx_i*x_j (for i < j)
    /// Returns linear terms (diagonal) and quadratic terms (off-diagonal)
    let oneHotConstraint (varIndices: int list) (penalty: float) : Map<int * int, float> =
        // Linear terms: -λ * x_i (diagonal of QUBO matrix)
        let linearTerms =
            varIndices
            |> List.map (fun i -> ((i, i), -penalty))
        
        // Quadratic terms: 2λ * x_i * x_j (off-diagonal of QUBO matrix)
        let quadraticTerms =
            varIndices
            |> List.collect (fun i ->
                varIndices
                |> List.filter (fun j -> j > i)
                |> List.map (fun j -> ((i, j), 2.0 * penalty)))
        
        // Combine all terms into single map
        (linearTerms @ quadraticTerms)
        |> List.fold (fun acc (key, value) -> combineTerms key value acc) Map.empty
    
    /// Compute penalty weights using Lucas Rule: penalties >> objective magnitude
    /// Formula: penalty = numOptions * objectiveMagnitude * 10.0
    /// Ensures constraint violations dominate the objective in QUBO energy
    let computeLucasPenalties (objectiveMagnitude: float) (numOptions: int) : float =
        float numOptions * objectiveMagnitude * 10.0
