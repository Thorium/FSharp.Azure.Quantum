namespace FSharp.Azure.Quantum.Quantum

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GraphOptimization

/// Quantum Graph Coloring Solver using QAOA and Backend Abstraction
///
/// ALGORITHM-LEVEL API (for advanced users):
/// This module provides direct access to quantum K-coloring solving via QAOA.
/// Graph coloring assigns colors to vertices such that no adjacent vertices
/// share the same color, minimizing the total number of colors used.
///
/// RULE 1 COMPLIANCE:
/// ✅ Requires IQuantumBackend parameter (explicit quantum execution)
///
/// TECHNICAL DETAILS:
/// - Execution: Quantum hardware/simulator via backend
/// - Algorithm: QAOA (Quantum Approximate Optimization Algorithm)
/// - Speed: Seconds to minutes (includes job queue wait for cloud backends)
/// - Cost: ~$10-100 per run on real quantum hardware (IonQ, Rigetti)
/// - LocalBackend: Free simulation (limited by qubit count)
///
/// QUANTUM PIPELINE:
/// 1. Graph + K colors → K-coloring QUBO Matrix (one-hot encoding)
/// 2. QUBO → QAOA Circuit (Hamiltonians + Layers)
/// 3. Execute on Quantum Backend (IonQ/Rigetti/Local)
/// 4. Decode Measurements → Color Assignments
/// 5. Return Best Valid Coloring
///
/// K-Coloring Problem:
///   Given graph G = (V, E) and K colors,
///   assign color c_i ∈ {0..K-1} to each vertex i such that:
///
///   ∀ (i,j) ∈ E: c_i ≠ c_j  (adjacent vertices have different colors)
///
///   Minimize: Number of colors used (chromatic number)
///
/// Example:
///   let backend = LocalBackend.LocalBackend() :> IQuantumBackend
///   let config = { NumShots = 1000; NumColors = 3; InitialParameters = (0.5, 0.5) }
///   match! QuantumGraphColoringSolver.solveAsync backend problem config CancellationToken.None with
///   | Ok solution -> printfn "Used %d colors" solution.ColorsUsed
///   | Error msg -> printfn "Error: %s" msg
module QuantumGraphColoringSolver =

    // ================================================================================
    // PROBLEM DEFINITION
    // ================================================================================

    /// Graph coloring problem specification
    type GraphColoringProblem =
        {
            /// Graph vertices (nodes)
            Vertices: string list

            /// Graph edges (conflicts - adjacent vertices cannot have same color)
            Edges: Edge<unit> list

            /// Number of available colors
            NumColors: int

            /// Optional fixed color assignments (pre-colored vertices)
            FixedColors: Map<string, int>
        }

    /// Graph coloring solution result
    type GraphColoringSolution =
        {
            /// Color assignment for each vertex (color index 0..K-1)
            ColorAssignments: Map<string, int>

            /// Number of distinct colors used
            ColorsUsed: int

            /// Number of conflicts (adjacent vertices with same color)
            ConflictCount: int

            /// Whether solution is valid (no conflicts)
            IsValid: bool

            /// Backend used for execution
            BackendName: string

            /// Number of measurement shots
            NumShots: int

            /// Execution time in milliseconds
            ElapsedMs: float

            /// QUBO objective value (energy)
            BestEnergy: float
        }

    /// Soft goal of a coloring. It shapes the QUBO and the ranking of measured samples.
    [<RequireQualifiedAccess; Struct>]
    type ColoringGoal =
        /// Prefer fewer distinct colors: a cost that grows with the color index, and
        /// among valid samples the one using the fewest colors wins.
        | MinimizeColors
        /// Prefer the fewest conflicting edges, with no color-count preference:
        /// samples are ranked by conflict count, then by QUBO energy.
        | MinimizeConflicts
        /// Prefer equal color class sizes: penalty Σ_c (Σ_i x_{i,c})², which is smallest
        /// for equal class sizes because the one-hot constraint fixes Σ_c Σ_i x_{i,c} = n;
        /// among valid samples the one with the smallest Σ_c n_c² wins.
        | BalanceColors

    /// Soft preferences layered on the hard K-coloring constraints.
    ///
    /// Scaling (P = penalty weight, n = vertices): a broken one-hot constraint costs at
    /// least P, a broken fixed color at least 10 × P, and a conflicting edge
    /// ConflictWeight × P. The soft terms are non-negative and bounded in TOTAL over the
    /// whole graph: color index at most 0.2 × P (0.2 × P × c / (n × (K - 1)) per vertex),
    /// balance at most 0.2 × P (λ = 0.2 × P / n²), avoided colors at most 0.3 × P
    /// (0.3 × P / n per vertex on an avoided color). A valid coloring therefore costs at
    /// most 0.5 × P of soft terms, and any assignment that breaks a constraint costs at
    /// least min(1, ConflictWeight) × P more than its soft terms. So with ConflictWeight
    /// ≥ 1 (the default), and generally above 0.5, the QUBO minimum is a valid coloring
    /// whenever the K colors admit one. With ConflictWeight ≤ 0.5 a coloring with
    /// conflicts can have lower energy than every valid coloring.
    type ColoringPreferences =
        {
            /// Multiplier on the penalty weight for an edge whose endpoints share a color.
            /// Must be positive.
            ConflictWeight: float

            /// Soft goal of the coloring
            Goal: ColoringGoal

            /// Color indices each vertex should avoid if possible (soft penalty per color).
            /// Indices outside 0..NumColors-1 are ignored.
            AvoidColors: Map<string, int list>

            /// Tie-break priority per vertex (higher = assigned first, missing = 0.0).
            /// Among samples that rank equal on the goal and on QUBO energy, the one that
            /// gives higher-priority vertices lower color indices wins; the greedy coloring
            /// (edgeless graphs, classical solver) visits vertices in descending priority.
            /// Has no effect when all vertices share one priority.
            Priorities: Map<string, float>
        }

    /// Preferences that reproduce the plain K-coloring QUBO: conflict weight 1,
    /// fewer colors preferred, nothing avoided, no priorities.
    let defaultPreferences: ColoringPreferences =
        {
            ConflictWeight = 1.0
            Goal = ColoringGoal.MinimizeColors
            AvoidColors = Map.empty
            Priorities = Map.empty
        }

    /// Fraction of the penalty weight bounding the total color-index cost (MinimizeColors).
    [<Literal>]
    let private ColorIndexBudget = 0.2

    /// Fraction of the penalty weight bounding the total avoided-color cost.
    [<Literal>]
    let private AvoidColorBudget = 0.3

    /// Fraction of the penalty weight bounding the total balance cost (BalanceColors).
    [<Literal>]
    let private BalanceBudget = 0.2

    /// Color-index cost of one vertex on color c: the n vertices together stay within
    /// ColorIndexBudget × P.
    let private colorIndexCost (penaltyWeight: float) (numVertices: int) (numColors: int) (c: int) : float =
        if numColors > 1 then
            penaltyWeight * ColorIndexBudget * float c
            / float (numVertices * (numColors - 1))
        else
            0.0

    /// Cost of one vertex on an avoided color: the n vertices together stay within
    /// AvoidColorBudget × P.
    let private avoidColorCost (penaltyWeight: float) (numVertices: int) : float =
        penaltyWeight * AvoidColorBudget / float numVertices

    /// λ of the balance term λ × Σ_c (Σ_i x_{i,c})²; Σ_c n_c² ≤ n² keeps it within
    /// BalanceBudget × P.
    let private balanceWeight (penaltyWeight: float) (numVertices: int) : float =
        penaltyWeight * BalanceBudget / float (numVertices * numVertices)

    /// Rejects inputs the encoding cannot represent.
    let private validateEncoding
        (problem: GraphColoringProblem)
        (preferences: ColoringPreferences)
        : Result<unit, QuantumError> =
        if problem.Vertices.IsEmpty then
            Error(QuantumError.ValidationError("numVertices", "Graph coloring problem has no vertices"))
        elif problem.NumColors < 1 then
            Error(QuantumError.ValidationError("numColors", "Graph coloring problem must have at least 1 color"))
        elif
            not (preferences.ConflictWeight > 0.0)
            || Double.IsInfinity preferences.ConflictWeight
        then
            Error(
                QuantumError.ValidationError(
                    "ConflictWeight",
                    $"Conflict weight must be a positive finite number, got %g{preferences.ConflictWeight}"
                )
            )
        else
            Ok()

    /// Backend name reported when a graph has no edges and no circuit runs.
    [<Literal>]
    let NoCircuitBackendName = "None (graph has no edges; no circuit executed)"

    // ================================================================================
    // QUBO ENCODING FOR K-COLORING
    // ================================================================================

    /// Encode K-coloring problem as QUBO using one-hot encoding
    ///
    /// ONE-HOT ENCODING:
    /// For n vertices and K colors, we use n*K binary variables:
    ///   x_{i,c} = 1 if vertex i is assigned color c, else 0
    ///
    /// CONSTRAINTS (as penalty terms):
    ///
    /// 1. Each vertex gets exactly one color (one-hot constraint):
    ///    Penalty: Σ_i (1 - Σ_c x_{i,c})²
    ///           = Σ_i (1 - 2*Σ_c x_{i,c} + (Σ_c x_{i,c})²)
    ///
    /// 2. Adjacent vertices have different colors (weight ConflictWeight × P):
    ///    Penalty: Σ_{(i,j) ∈ E} Σ_c x_{i,c} * x_{j,c}
    ///
    /// SOFT TERMS (see ColoringPreferences for their scaling):
    ///
    /// 3. MinimizeColors: 0.2 × P × c / (n × (K - 1)) on x_{i,c} (higher color indices cost more)
    ///    BalanceColors: λ × Σ_c (Σ_i x_{i,c})² with λ = 0.2 × P / n²
    ///    MinimizeConflicts: no color-count term
    ///
    /// 4. Avoided colors: 0.3 × P / n on x_{i,c} for each color c that vertex i avoids
    ///
    /// The soft terms total at most 0.5 × P on any valid coloring, so they never outweigh
    /// a hard constraint while ConflictWeight > 0.5 (see ColoringPreferences).
    /// An edgeless graph encodes to the one-hot and soft terms only.
    let toQuboWithPreferences
        (problem: GraphColoringProblem)
        (penaltyWeight: float)
        (preferences: ColoringPreferences)
        : Result<QuboMatrix * Map<int, string * int>, QuantumError> =
        try
            // Create variable mapping: (vertex_index, color_index) → qubo_variable_index
            let vertexIndexMap =
                problem.Vertices |> List.mapi (fun i vertex -> vertex, i) |> Map.ofList

            let vertices = problem.Vertices |> List.toArray
            let numVertices = vertices.Length
            let numColors = problem.NumColors
            let numVars = numVertices * numColors

            match validateEncoding problem preferences with
            | Error err -> Error err
            | Ok() ->
                // Create reverse mapping: qubo_variable_index → (vertex, color)
                let reverseMap =
                    seq {
                        for v in 0 .. numVertices - 1 do
                            for c in 0 .. numColors - 1 do
                                let quboVar = v * numColors + c
                                yield quboVar, (vertices.[v], c)
                    }
                    |> Map.ofSeq

                // Helper: Get QUBO variable index for (vertex_index, color)
                let getVarIndex vertexIdx color = vertexIdx * numColors + color

                // Helper: add value to existing map entry
                let addTerm key value (qubo: Map<(int * int), float>) =
                    let existing = qubo |> Map.tryFind key |> Option.defaultValue 0.0
                    qubo |> Map.add key (existing + value)

                // Build QUBO terms as Map<(int * int), float>

                // CONSTRAINT 1: One-hot constraint (each vertex gets exactly one color)
                // Penalty: Σ_i (1 - Σ_c x_{i,c})²
                //        = Σ_i (1 - 2*Σ_c x_{i,c} + (Σ_c x_{i,c})²)
                //        = Σ_i (1 - 2*Σ_c x_{i,c} + Σ_c x_{i,c} + Σ_{c≠d} x_{i,c}*x_{i,d})

                let quboTerms =
                    [ 0 .. numVertices - 1 ]
                    |> List.fold
                        (fun qubo v ->
                            // Check if vertex has fixed color
                            let vertexName = vertices.[v]

                            match Map.tryFind vertexName problem.FixedColors with
                            | Some fixedColor ->
                                // For fixed color: force x_{v,fixedColor} = 1, others = 0.
                                // Penalise the other colour bits AND reward the fixed bit
                                // with a strong negative diagonal term. Penalising the
                                // others alone leaves nothing forcing the fixed bit to 1 —
                                // the colour-count objective then actively pushes it to 0
                                // and the pre-assignment is silently dropped at decode.
                                [ 0 .. numColors - 1 ]
                                |> List.fold
                                    (fun q c ->
                                        let varIdx = getVarIndex v c

                                        if c <> fixedColor then
                                            q |> addTerm (varIdx, varIdx) (penaltyWeight * 10.0)
                                        else
                                            q |> addTerm (varIdx, varIdx) (-(penaltyWeight * 10.0)))
                                    qubo
                            | None ->
                                // Normal one-hot constraint using shared helper
                                let varIndices = [ for c in 0 .. numColors - 1 -> getVarIndex v c ]
                                let oneHotTerms = Qubo.oneHotConstraint varIndices penaltyWeight

                                // Merge one-hot terms into quboTerms
                                oneHotTerms |> Map.fold (fun acc key value -> acc |> addTerm key value) qubo)
                        Map.empty

                // CONSTRAINT 2: Adjacent vertices have different colors
                // Penalty: Σ_{(i,j) ∈ E} Σ_c x_{i,c} * x_{j,c}
                let conflictWeight = penaltyWeight * preferences.ConflictWeight

                let quboTerms =
                    problem.Edges
                    |> Seq.fold
                        (fun qubo edge ->
                            let i = vertexIndexMap.[edge.Source]
                            let j = vertexIndexMap.[edge.Target]

                            [ 0 .. numColors - 1 ]
                            |> List.fold
                                (fun q c ->
                                    let varIdx1 = getVarIndex i c
                                    let varIdx2 = getVarIndex j c

                                    if varIdx1 = varIdx2 then
                                        // Self-loop (should not happen in valid graph)
                                        q |> addTerm (varIdx1, varIdx1) conflictWeight
                                    else
                                        let (row, col) = (min varIdx1 varIdx2, max varIdx1 varIdx2)
                                        q |> addTerm (row, col) conflictWeight)
                                qubo)
                        quboTerms

                // SOFT TERM 3: color-count goal
                let quboTerms =
                    match preferences.Goal with
                    | ColoringGoal.MinimizeColors when numColors > 1 ->
                        // Linear cost proportional to the color index, in total at most ColorIndexBudget × P
                        seq {
                            for v in 0 .. numVertices - 1 do
                                for c in 1 .. numColors - 1 do
                                    yield getVarIndex v c, colorIndexCost penaltyWeight numVertices numColors c
                        }
                        |> Seq.fold
                            (fun qubo (varIdx, colorPenalty) -> qubo |> addTerm (varIdx, varIdx) colorPenalty)
                            quboTerms
                    | ColoringGoal.BalanceColors ->
                        // λ × Σ_c (Σ_i x_{i,c})² = λ × Σ_c (Σ_i x_{i,c} + 2 × Σ_{i<j} x_{i,c} x_{j,c}),
                        // at most λ × n² = BalanceBudget × P on a one-hot assignment
                        let lambda = balanceWeight penaltyWeight numVertices

                        seq {
                            for c in 0 .. numColors - 1 do
                                for v in 0 .. numVertices - 1 do
                                    let varIdx = getVarIndex v c
                                    yield (varIdx, varIdx), lambda

                                    for w in v + 1 .. numVertices - 1 do
                                        yield (varIdx, getVarIndex w c), 2.0 * lambda
                        }
                        |> Seq.fold (fun qubo (key, value) -> qubo |> addTerm key value) quboTerms
                    | ColoringGoal.MinimizeColors
                    | ColoringGoal.MinimizeConflicts -> quboTerms

                // SOFT TERM 4: avoided colors (fixed vertices keep their fixed color),
                // in total at most AvoidColorBudget × P
                let avoidWeight = avoidColorCost penaltyWeight numVertices

                let quboTerms =
                    seq {
                        for v in 0 .. numVertices - 1 do
                            let vertexName = vertices.[v]

                            if not (problem.FixedColors.ContainsKey vertexName) then
                                match Map.tryFind vertexName preferences.AvoidColors with
                                | Some avoided ->
                                    for c in List.distinct avoided do
                                        if c >= 0 && c < numColors then
                                            yield getVarIndex v c
                                | None -> ()
                    }
                    |> Seq.fold (fun qubo varIdx -> qubo |> addTerm (varIdx, varIdx) avoidWeight) quboTerms

                Ok(
                    {
                        Q = quboTerms
                        NumVariables = numVars
                    },
                    reverseMap
                )
        with ex ->
            Error(QuantumError.OperationError("QuboEncoding", $"Graph coloring QUBO encoding failed: %s{ex.Message}"))

    /// Encode K-coloring problem as QUBO with the default preferences
    /// (conflict weight 1, fewer colors preferred); see toQuboWithPreferences.
    let toQubo
        (problem: GraphColoringProblem)
        (penaltyWeight: float)
        : Result<QuboMatrix * Map<int, string * int>, QuantumError> =
        toQuboWithPreferences problem penaltyWeight defaultPreferences

    /// QUBO energy of a color assignment: its one-hot bitstring evaluated against the QUBO.
    let internal assignmentEnergy
        (problem: GraphColoringProblem)
        (qubo: QuboMatrix)
        (assignments: Map<string, int>)
        : float =
        let bits = Array.zeroCreate qubo.NumVariables

        problem.Vertices
        |> List.iteri (fun v vertex ->
            match Map.tryFind vertex assignments with
            | Some c when c >= 0 && c < problem.NumColors -> bits.[v * problem.NumColors + c] <- 1
            | _ -> ())

        QaoaExecutionHelpers.evaluateQuboSparse qubo.Q bits

    /// QUBO energy of a color assignment computed from the terms directly, equal to
    /// assignmentEnergy on the matrix toQuboWithPreferences builds, without building it.
    let internal coloringEnergy
        (problem: GraphColoringProblem)
        (penaltyWeight: float)
        (preferences: ColoringPreferences)
        (assignments: Map<string, int>)
        : float =
        let numVertices = problem.Vertices.Length
        let numColors = problem.NumColors

        let colorOf vertex =
            match Map.tryFind vertex assignments with
            | Some c when c >= 0 && c < numColors -> Some c
            | _ -> None

        let vertexTerms =
            problem.Vertices
            |> List.sumBy (fun vertex ->
                match colorOf vertex with
                | None -> 0.0
                | Some c ->
                    let constraintTerm, avoidTerm =
                        match Map.tryFind vertex problem.FixedColors with
                        | Some fixedColor -> (if c = fixedColor then -10.0 else 10.0) * penaltyWeight, 0.0
                        | None ->
                            let avoided =
                                Map.tryFind vertex preferences.AvoidColors
                                |> Option.defaultValue []
                                |> List.contains c

                            -penaltyWeight,
                            (if avoided then
                                 avoidColorCost penaltyWeight numVertices
                             else
                                 0.0)

                    let indexTerm =
                        match preferences.Goal with
                        | ColoringGoal.MinimizeColors -> colorIndexCost penaltyWeight numVertices numColors c
                        | ColoringGoal.BalanceColors
                        | ColoringGoal.MinimizeConflicts -> 0.0

                    constraintTerm + avoidTerm + indexTerm)

        let balanceTerm =
            match preferences.Goal with
            | ColoringGoal.BalanceColors ->
                let classSizes = problem.Vertices |> List.choose colorOf |> List.countBy id

                balanceWeight penaltyWeight numVertices
                * float (classSizes |> List.sumBy (fun (_, size) -> size * size))
            | ColoringGoal.MinimizeColors
            | ColoringGoal.MinimizeConflicts -> 0.0

        let conflictTerm =
            problem.Edges
            |> List.sumBy (fun edge ->
                match colorOf edge.Source, colorOf edge.Target with
                | Some a, Some b when a = b -> penaltyWeight * preferences.ConflictWeight
                | _ -> 0.0)

        vertexTerms + balanceTerm + conflictTerm

    // ================================================================================
    // SOLUTION DECODING
    // ================================================================================

    /// Solution record for a color assignment: colors used and conflicting edges.
    let private summarizeAssignments
        (problem: GraphColoringProblem)
        (colorAssignments: Map<string, int>)
        : GraphColoringSolution =
        // Count distinct colors used
        let colorsUsed =
            colorAssignments |> Map.toList |> List.map snd |> List.distinct |> List.length

        // Count conflicts (adjacent vertices with same color)
        let conflictCount =
            problem.Edges
            |> List.filter (fun edge ->
                let sourceColor = Map.find edge.Source colorAssignments
                let targetColor = Map.find edge.Target colorAssignments
                sourceColor = targetColor)
            |> List.length

        {
            ColorAssignments = colorAssignments
            ColorsUsed = colorsUsed
            ConflictCount = conflictCount
            IsValid = conflictCount = 0
            BackendName = ""
            NumShots = 0
            ElapsedMs = 0.0
            BestEnergy = 0.0
        }

    /// Decode binary solution to color assignments
    let private decodeSolution
        (problem: GraphColoringProblem)
        (bitstring: int[])
        (reverseMap: Map<int, string * int>)
        : GraphColoringSolution =

        let numColors = problem.NumColors

        // Decode color assignments (handle one-hot encoding)
        let colorAssignments =
            problem.Vertices
            |> List.map (fun vertex ->
                // A user-fixed vertex ALWAYS gets its fixed colour, regardless of
                // what the measured bits say: defaulting a bit-less fixed vertex to
                // colour 0 would silently violate the user's pre-assignment, and
                // conflict counting below must use the actual fixed colours.
                match Map.tryFind vertex problem.FixedColors with
                | Some fixedColor -> vertex, fixedColor
                | None ->

                    // Find which color variable is set to 1 for this vertex
                    let vertexIdx = problem.Vertices |> List.findIndex ((=) vertex)

                    let assignedColors =
                        [
                            for c in 0 .. numColors - 1 do
                                let varIdx = vertexIdx * numColors + c

                                if varIdx < bitstring.Length && bitstring.[varIdx] = 1 then
                                    yield c
                        ]

                    // Take first assigned color (or 0 if none/multiple)
                    let color =
                        match assignedColors with
                        | [] -> 0 // No color assigned, default to color 0
                        | c :: _ -> c // Take first color

                    vertex, color)
            |> Map.ofList

        summarizeAssignments problem colorAssignments

    /// Vertices in descending priority (stable); empty when all vertices share one priority.
    let private verticesByPriority (problem: GraphColoringProblem) (preferences: ColoringPreferences) : string list =
        let priorityOf vertex =
            Map.tryFind vertex preferences.Priorities |> Option.defaultValue 0.0

        match problem.Vertices |> List.map priorityOf |> List.distinct with
        | []
        | [ _ ] -> []
        | _ -> problem.Vertices |> List.sortByDescending priorityOf

    /// Pick the best decoded sample for the goal.
    ///
    /// Ranking: MinimizeColors — valid first, then fewest colors (invalid: fewest conflicts);
    /// BalanceColors — valid first, then smallest Σ_c n_c² (invalid: fewest conflicts);
    /// MinimizeConflicts — fewest conflicts. Then lowest QUBO energy (BestEnergy), then the
    /// sample giving higher-priority vertices lower color indices.
    let internal selectBest
        (problem: GraphColoringProblem)
        (preferences: ColoringPreferences)
        (solutions: GraphColoringSolution[])
        : GraphColoringSolution =
        let priorityOrder = verticesByPriority problem preferences

        let classSizeSquares (sol: GraphColoringSolution) =
            sol.ColorAssignments
            |> Map.toList
            |> List.countBy snd
            |> List.sumBy (fun (_, n) -> n * n)

        let goalKey (sol: GraphColoringSolution) =
            match preferences.Goal with
            | ColoringGoal.MinimizeColors ->
                if sol.IsValid then
                    (0, sol.ColorsUsed, sol.ConflictCount)
                else
                    (1, sol.ConflictCount, sol.ColorsUsed)
            | ColoringGoal.BalanceColors ->
                if sol.IsValid then
                    (0, classSizeSquares sol, 0)
                else
                    (1, sol.ConflictCount, classSizeSquares sol)
            | ColoringGoal.MinimizeConflicts -> (sol.ConflictCount, 0, 0)

        let priorityKey (sol: GraphColoringSolution) =
            priorityOrder |> List.map (fun vertex -> Map.find vertex sol.ColorAssignments)

        solutions
        |> Array.sortBy (fun sol -> goalKey sol, sol.BestEnergy, priorityKey sol)
        |> Array.head

    /// Greedy coloring. Fixed vertices keep their color and are placed first; the others are
    /// visited in descending priority (vertex order when all priorities are equal) and take,
    /// among the colors no already-colored neighbor uses, the one with the lowest
    /// (avoided?, goal, index) cost. The goal is, for MinimizeColors, 0 for a color some
    /// vertex already has and 1 for a new one (reuse before opening a color); for
    /// BalanceColors, the class size so far; for MinimizeConflicts, 0. Returns Error with
    /// the first vertex whose neighbors already use every color (never without edges).
    let internal greedyColoring
        (problem: GraphColoringProblem)
        (preferences: ColoringPreferences)
        : Result<Map<string, int>, string> =
        let order =
            match verticesByPriority problem preferences with
            | [] -> problem.Vertices
            | ordered -> ordered

        let neighbors =
            problem.Edges
            |> List.collect (fun e -> [ e.Source, e.Target; e.Target, e.Source ])
            |> List.groupBy fst
            |> List.map (fun (vertex, pairs) -> vertex, pairs |> List.map snd)
            |> Map.ofList

        let fixedAssignments =
            problem.Vertices
            |> List.choose (fun vertex ->
                Map.tryFind vertex problem.FixedColors
                |> Option.map (fun color -> vertex, color))
            |> Map.ofList

        order
        |> List.fold
            (fun (state: Result<Map<string, int>, string>) vertex ->
                match state with
                | Ok assignments when not (assignments.ContainsKey vertex) ->
                    let avoided =
                        Map.tryFind vertex preferences.AvoidColors
                        |> Option.defaultValue []
                        |> Set.ofList

                    let neighborColors =
                        Map.tryFind vertex neighbors
                        |> Option.defaultValue []
                        |> List.choose (fun neighbor -> Map.tryFind neighbor assignments)
                        |> Set.ofList

                    let classSize color =
                        assignments |> Map.filter (fun _ c -> c = color) |> Map.count

                    let freeColors =
                        [ 0 .. problem.NumColors - 1 ]
                        |> List.filter (fun c -> not (neighborColors.Contains c))

                    match freeColors with
                    | [] -> Error vertex
                    | _ ->
                        let color =
                            freeColors
                            |> List.minBy (fun c ->
                                let goalCost =
                                    match preferences.Goal with
                                    | ColoringGoal.BalanceColors -> classSize c
                                    | ColoringGoal.MinimizeColors -> if classSize c > 0 then 0 else 1
                                    | ColoringGoal.MinimizeConflicts -> 0

                                (avoided.Contains c, goalCost, c))

                        Ok(assignments |> Map.add vertex color)
                | _ -> state)
            (Ok fixedAssignments)

    // ================================================================================
    // QAOA CONFIGURATION
    // ================================================================================

    /// QAOA configuration parameters for graph coloring
    type QaoaConfig =
        {
            /// Number of measurement shots
            NumShots: int

            /// Number of colors to use
            NumColors: int

            /// QAOA angles (gamma, beta) of the single layer, in units of the normalised cost
            /// Hamiltonian (minimisation convention, see Core.QaoaCircuit)
            InitialParameters: float * float

            /// Penalty weight for constraint violations (default: 10.0)
            PenaltyWeight: float
        }

    /// Default QAOA configuration for graph coloring
    let defaultConfig (numColors: int) : QaoaConfig =
        {
            NumShots = 1000
            NumColors = numColors
            InitialParameters = (0.5, 0.5)
            PenaltyWeight = 10.0
        }

    /// Colors a graph without edges, where no edge can conflict: the greedy coloring never
    /// fails, and its energy comes from the terms directly instead of a built QUBO. A function
    /// of its own rather than inline in solveWithPreferencesAsync: there this branch keeps the
    /// task from compiling to a static state machine (FS3511 in Release builds).
    let private colorWithoutCircuit
        (problem: GraphColoringProblem)
        (preferences: ColoringPreferences)
        (penaltyWeight: float)
        (stopwatch: Stopwatch)
        : Result<GraphColoringSolution, QuantumError> =
        match validateEncoding problem preferences with
        | Error err -> Error err
        | Ok() ->
            let assignments =
                greedyColoring problem preferences |> Result.defaultValue Map.empty

            Ok
                { summarizeAssignments problem assignments with
                    BackendName = NoCircuitBackendName
                    NumShots = 0
                    ElapsedMs = stopwatch.Elapsed.TotalMilliseconds
                    BestEnergy = coloringEnergy problem penaltyWeight preferences assignments
                }

    // ================================================================================
    // MAIN SOLVER
    // ================================================================================

    /// Solve graph coloring problem using quantum QAOA with soft preferences (async version)
    ///
    /// Parameters:
    ///   - backend: Quantum backend (LocalBackend, IonQ, Rigetti)
    ///   - problem: Graph coloring problem (vertices, edges, colors)
    ///   - preferences: Conflict weight, soft goal, avoided colors and priorities
    ///   - config: QAOA configuration (shots, colors, parameters)
    ///
    /// The QUBO comes from toQuboWithPreferences and the measured samples are ranked by
    /// selectBest. A graph without edges runs no circuit: it is colored directly by
    /// greedyColoring and reported with BackendName = NoCircuitBackendName and NumShots = 0.
    let solveWithPreferencesAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: GraphColoringProblem)
        (preferences: ColoringPreferences)
        (config: QaoaConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<GraphColoringSolution, QuantumError>> =
        task {

            let stopwatch = Stopwatch.StartNew()

            try
                // Step 1: Validate problem inputs
                if problem.Vertices.Length = 0 then
                    return Error(QuantumError.ValidationError("numVertices", "Graph coloring problem has no vertices"))
                elif config.NumColors < 1 then
                    return
                        Error(
                            QuantumError.ValidationError(
                                "numColors",
                                "Graph coloring problem must have at least 1 color"
                            )
                        )
                elif problem.Edges.IsEmpty then
                    return colorWithoutCircuit problem preferences config.PenaltyWeight stopwatch
                else
                    // Step 2: Encode graph coloring as QUBO
                    match toQuboWithPreferences problem config.PenaltyWeight preferences with
                    | Error err -> return Error err
                    | Ok(quboMatrix, reverseMap) ->

                        // Step 3: Convert QUBO to dense array and execute QAOA pipeline
                        let quboArray = Qubo.toDenseArray quboMatrix.NumVariables quboMatrix.Q
                        // One (gamma, beta) layer. No tuple pattern here: a `let (a, b) = ...`
                        // before the `match!` keeps the task from compiling to a static state machine.
                        let parameters = [| config.InitialParameters |]

                        match!
                            QaoaExecutionHelpers.executeFromQuboAsync
                                backend
                                quboArray
                                parameters
                                config.NumShots
                                cancellationToken
                        with
                        | Error err -> return Error err
                        | Ok measurements ->

                            // Step 9: Decode measurements to color assignments with their QUBO energy
                            let solutions =
                                measurements
                                |> Array.map (fun bitstring ->
                                    let decoded = decodeSolution problem bitstring reverseMap

                                    { decoded with
                                        BestEnergy = assignmentEnergy problem quboMatrix decoded.ColorAssignments
                                    })

                            // Step 10: Pick the best sample for the goal
                            let bestSolution = selectBest problem preferences solutions

                            let elapsedMs = stopwatch.Elapsed.TotalMilliseconds

                            return
                                Ok
                                    { bestSolution with
                                        BackendName = backend.Name
                                        NumShots = config.NumShots
                                        ElapsedMs = elapsedMs
                                    }

            with ex when not (ex :? OperationCanceledException) ->
                return
                    Error(
                        QuantumError.OperationError(
                            "QuantumGraphColoringSolver",
                            $"Quantum graph coloring solve failed: %s{ex.Message}"
                        )
                    )
        }

    /// Solve graph coloring problem using quantum QAOA (async version)
    ///
    /// Uses defaultPreferences; see solveWithPreferencesAsync.
    ///
    /// Parameters:
    ///   - backend: Quantum backend (LocalBackend, IonQ, Rigetti)
    ///   - problem: Graph coloring problem (vertices, edges, colors)
    ///   - config: QAOA configuration (shots, colors, parameters)
    ///
    /// Returns: Task<Result<GraphColoringSolution, QuantumError>> - Task with result or error
    ///
    /// Example:
    ///   let backend = LocalBackend.LocalBackend() :> IQuantumBackend
    ///   let problem = { Vertices = ["A"; "B"; "C"]; Edges = [...]; NumColors = 3; FixedColors = Map.empty }
    ///   let config = defaultConfig 3
    ///   async {
    ///       match! solveAsync backend problem config CancellationToken.None with
    ///       | Ok solution -> printfn "Colors used: %d" solution.ColorsUsed
    ///       | Error msg -> printfn "Error: %s" msg
    ///   }
    let solveAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: GraphColoringProblem)
        (config: QaoaConfig)
        (cancellationToken: CancellationToken)
        : Task<Result<GraphColoringSolution, QuantumError>> =
        solveWithPreferencesAsync backend problem defaultPreferences config cancellationToken

    // ================================================================================
    // CLASSICAL GREEDY SOLVER (for comparison)
    // ================================================================================

    /// Solve graph coloring using the greedy coloring algorithm (classical) with preferences
    ///
    /// This provides a classical baseline for comparison with quantum QAOA. It runs
    /// greedyColoring: fixed vertices keep their color, the others are visited in
    /// descending priority and avoid their AvoidColors when another free color exists.
    /// It never creates a conflict, so ConflictWeight has no effect here.
    ///
    /// Typical performance: Near-optimal for many graph types
    /// Returns Error when some vertex has all NumColors colors already used by its
    /// neighbors, i.e. the graph is not colorable with NumColors colors by this
    /// greedy heuristic.
    let internal solveClassicalWithPreferences
        (problem: GraphColoringProblem)
        (preferences: ColoringPreferences)
        : Result<GraphColoringSolution, QuantumError> =
        greedyColoring problem preferences
        |> Result.mapError (fun vertex ->
            QuantumError.OperationError(
                "Classical graph coloring",
                $"Graph is not colorable with {problem.NumColors} colors by the greedy heuristic: all colors are already used by neighbors of vertex '{vertex}'. Try increasing the number of colors."
            ))
        |> Result.map (fun colorAssignments ->
            { summarizeAssignments problem colorAssignments with
                BackendName = "Classical Greedy"
            })

    /// Solve graph coloring using the greedy coloring algorithm (classical) with the
    /// default preferences: vertex order, a color no neighbor uses, preferring one already in use.
    let internal solveClassical (problem: GraphColoringProblem) : Result<GraphColoringSolution, QuantumError> =
        solveClassicalWithPreferences problem defaultPreferences
