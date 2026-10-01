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
        let linearTerms = varIndices |> List.map (fun i -> ((i, i), -penalty))

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

    /// Convert sparse QUBO map to dense 2D array.
    /// Consolidates the private quboMapToArray copies duplicated across 6 solver files.
    /// Used at the toQubo boundary: buildQuboMap >> toDenseArray >> ProblemHamiltonian.fromQubo
    let toDenseArray (n: int) (qubo: Map<int * int, float>) : float[,] =
        let dense = Array2D.zeroCreate n n

        for KeyValue((i, j), value) in qubo do
            dense.[i, j] <- value

        dense

    /// At-most-one constraint: at most one variable in a set equals 1.
    /// Penalizes any pair being simultaneously 1.
    /// Formula: λ * Σ_{i<j} x_i * x_j
    let atMostOneConstraint (varIndices: int list) (penalty: float) : Map<(int * int), float> =
        varIndices
        |> List.collect (fun i ->
            varIndices
            |> List.filter (fun j -> j > i)
            |> List.map (fun j -> ((i, j), penalty)))
        |> List.fold (fun acc (key, value) -> combineTerms key value acc) Map.empty

    // ------------------------------------------------------------------------
    // Linear constraints: λ·(Σ cᵢxᵢ + k)² and integer slack
    // ------------------------------------------------------------------------

    /// Terms of weight·(Σ cᵢ·xᵢ + constant)² over binary xᵢ, without the constant weight·constant².
    /// Diagonal (i, i) gets weight·cᵢ·(cᵢ + 2·constant) (xᵢ² = xᵢ); each pair (i, j), i < j, gets
    /// weight·2·cᵢ·cⱼ. A variable listed twice counts with the sum of its coefficients.
    let squaredLinearPenalty (weight: float) (terms: (int * float) list) (constant: float) : Map<int * int, float> =
        let coefficients =
            terms
            |> List.groupBy fst
            |> List.map (fun (index, group) -> index, group |> List.sumBy snd)
            |> List.sortBy fst

        let diagonal =
            coefficients
            |> List.map (fun (i, c) -> ((i, i), weight * c * (c + 2.0 * constant)))

        let pairs =
            [
                for (i, ci) in coefficients do
                    for (j, cj) in coefficients do
                        if i < j then
                            yield ((i, j), weight * 2.0 * ci * cj)
            ]

        (diagonal @ pairs)
        |> List.fold (fun acc (key, value) -> combineTerms key value acc) Map.empty

    /// Weights of binary slack bits whose subset sums are exactly the integers 0..range:
    /// powers of two, with the last weight cut so that the total is range (5 → [1; 2; 2]).
    /// A slack that can exceed range would let the equality Σ + slack = bound hold for an
    /// infeasible Σ when coefficients may be negative. Empty for range ≤ 0.
    let boundedSlackWeights (range: int) : int list =
        let rec build (remaining: int) (power: int) (acc: int list) =
            if remaining <= 0 then
                List.rev acc
            elif power <= remaining then
                build (remaining - power) (power * 2) (power :: acc)
            else
                List.rev (remaining :: acc)

        build range 1 []

    /// Rescale values to integers: the smallest power of ten (at most 10⁶) that makes every
    /// value integral, divided by the common divisor. Returns the integers and the factor f
    /// with integerᵢ = valueᵢ·f. None when no such power exists, a value is not finite, or an
    /// integer would exceed 10⁹ in magnitude. All-zero input gives factor 1.
    let tryScaleToIntegers (values: float list) : (int list * float) option =
        let isIntegral (v: float) =
            abs (v - round v) <= 1e-9 * max 1.0 (abs v)

        let rec gcd (a: int64) (b: int64) = if b = 0L then abs a else gcd b (a % b)

        if
            values
            |> List.exists (fun v -> System.Double.IsNaN v || System.Double.IsInfinity v)
        then
            None
        else
            [ 0..6 ]
            |> List.tryPick (fun digits ->
                let power = pown 10.0 digits
                let scaled = values |> List.map (fun v -> v * power)

                if
                    scaled |> List.forall isIntegral
                    && scaled |> List.forall (fun v -> abs v <= 1e9)
                then
                    let integers = scaled |> List.map (round >> int64)
                    let divisor = integers |> List.fold gcd 0L |> max 1L
                    Some(integers |> List.map (fun v -> int (v / divisor)), power / float divisor)
                else
                    None)
