namespace FSharp.Azure.Quantum.Quantum

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers

/// Quantum Weighted Maximum Matching Solver (QAOA-based)
///
/// Problem: Given graph G=(V,E) with edge weights, find a set of edges M
/// with no shared vertices (a matching) that maximizes total weight.
///
/// QUBO Formulation:
///   Variables: x_e in {0,1} for each edge e (1 = selected in matching)
///   Objective: Maximize sum(w_e * x_e) → minimize -sum(w_e * x_e)
///     qubo[e,e] -= w_e
///   Constraint: Each vertex has at most one incident selected edge.
///     For each vertex v, let I_v = {e : v is endpoint of e}
///     Penalty: lambda * sum_{e1 < e2 in I_v} x_{e1} * x_{e2}
///     (at-most-one constraint per vertex)
///
/// Qubits: |E| (one per edge)
///
/// Note: More qubits than vertex-based problems (|E| vs |V|).
/// Practical for sparse graphs only.
///
/// RULE 1 COMPLIANCE:
/// All public solve functions require IQuantumBackend parameter.
/// Classical solver is private.
module QuantumMatchingSolver =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// A weighted edge in the graph
    type Edge = {
        /// Source vertex index
        Source: int
        /// Target vertex index
        Target: int
        /// Edge weight (positive = desirable to include)
        Weight: float
    }

    /// Maximum matching problem definition
    type Problem = {
        /// Number of vertices in the graph
        NumVertices: int
        /// Weighted edges
        Edges: Edge list
    }

    /// Maximum matching solution
    type Solution = {
        /// Edges selected in the matching
        SelectedEdges: Edge list
        /// Total weight of the matching
        TotalWeight: float
        /// Number of edges in the matching
        MatchingSize: int
        /// Whether the matching is valid (no shared vertices)
        IsValid: bool
        /// Whether constraint repair was applied
        WasRepaired: bool
        /// Name of the quantum backend used
        BackendName: string
        /// Number of measurement shots
        NumShots: int
        /// Optimized QAOA (gamma, beta) parameters per layer
        OptimizedParameters: (float * float)[] option
        /// Whether Nelder-Mead converged
        OptimizationConverged: bool option
    }

    // ========================================================================
    // CONFIGURATION (type alias for unified config)
    // ========================================================================

    type Config = QaoaSolverConfig

    let defaultConfig : Config = QaoaExecutionHelpers.defaultConfig
    let fastConfig : Config = QaoaExecutionHelpers.fastConfig
    let highQualityConfig : Config = QaoaExecutionHelpers.highQualityConfig

    // ========================================================================
    // EDGE NORMALIZATION
    // ========================================================================

    /// Normalize edges to canonical form (min, max) and deduplicate.
    /// Also removes self-loops.
    let private normalizeEdges (edges: Edge list) : Edge list =
        edges
        |> List.choose (fun e ->
            if e.Source = e.Target then None  // Remove self-loops
            else
                let s, t = min e.Source e.Target, max e.Source e.Target
                Some { e with Source = s; Target = t })
        |> List.distinctBy (fun e -> (e.Source, e.Target))

    // ========================================================================
    // QUBIT ESTIMATION (Decision 11)
    // ========================================================================

    /// Estimate the number of qubits required.
    /// One qubit per edge (after normalization removes self-loops and duplicates).
    let estimateQubits (problem: Problem) : int =
        let edges = normalizeEdges problem.Edges
        edges.Length

    // ========================================================================
    // QUBO CONSTRUCTION (Decision 9: sparse internally, Decision 5: dense output)
    // ========================================================================

    /// Build a map from vertex -> list of edge indices incident to it.
    let private buildIncidenceMap (edges: Edge list) : Map<int, int list> =
        edges
        |> List.indexed
        |> List.collect (fun (edgeIdx, edge) ->
            [ (edge.Source, edgeIdx); (edge.Target, edgeIdx) ])
        |> List.groupBy fst
        |> List.map (fun (vertex, pairs) -> (vertex, pairs |> List.map snd))
        |> Map.ofList

    /// Build the QUBO as a sparse map.
    ///
    /// Objective: maximize sum(w_e * x_e) → minimize -sum(w_e * x_e)
    ///   qubo[e,e] -= w_e
    ///
    /// Matching constraint per vertex v (at-most-one incident edge selected):
    ///   For each pair of edges e1 < e2 incident to v:
    ///     qubo[e1,e2] += lambda/2, qubo[e2,e1] += lambda/2  (symmetric split)
    let private buildQuboMap (edges: Edge list) (numVertices: int) : Map<int * int, float> =
        let incidenceMap = buildIncidenceMap edges

        // Penalty must dominate objective. Max objective = sum of all weights.
        let totalWeight = edges |> List.sumBy (fun e -> abs e.Weight)
        let penalty = totalWeight + 1.0

        // Objective: minimize -w_e * x_e (diagonal)
        let objectiveTerms =
            edges
            |> List.indexed
            |> List.map (fun (eIdx, edge) -> ((eIdx, eIdx), -edge.Weight))

        // Matching constraints: at-most-one per vertex (symmetric split)
        let constraintTerms =
            [ 0 .. numVertices - 1 ]
            |> List.collect (fun v ->
                match incidenceMap |> Map.tryFind v with
                | None | Some [] | Some [ _ ] -> []
                | Some edgeIndices ->
                    edgeIndices
                    |> List.collect (fun e1 ->
                        edgeIndices
                        |> List.filter (fun e2 -> e2 > e1)
                        |> List.collect (fun e2 ->
                            [ ((e1, e2), penalty / 2.0)
                              ((e2, e1), penalty / 2.0) ])))

        (objectiveTerms @ constraintTerms)
        |> List.fold (fun acc (key, value) -> Qubo.combineTerms key value acc) Map.empty

    /// Validate a matching problem, returning Error if invalid.
    let private validateProblem (problem: Problem) : Result<unit, QuantumError> =
        if problem.Edges.IsEmpty then
            Error (QuantumError.ValidationError ("edges", "Problem has no edges"))
        elif problem.NumVertices <= 0 then
            Error (QuantumError.ValidationError ("numVertices", "Number of vertices must be positive"))
        elif problem.Edges |> List.exists (fun e ->
                e.Source < 0 || e.Source >= problem.NumVertices
                || e.Target < 0 || e.Target >= problem.NumVertices) then
            Error (QuantumError.ValidationError ("edge", "Edge endpoint out of range"))
        elif problem.Edges |> List.exists (fun e -> e.Source = e.Target) then
            Error (QuantumError.ValidationError ("edge", "Self-loops are not allowed"))
        else
            Ok ()

    /// Convert problem to dense QUBO matrix.
    /// Returns Result to follow the canonical pattern (validates inputs).
    let toQubo (problem: Problem) : Result<float[,], QuantumError> =
        match validateProblem problem with
        | Error err -> Error err
        | Ok () ->
            let edges = normalizeEdges problem.Edges
            let n = edges.Length
            let quboMap = buildQuboMap edges problem.NumVertices
            Ok (Qubo.toDenseArray n quboMap)

    // ========================================================================
    // SOLUTION DECODING & VALIDATION
    // ========================================================================

    /// Check whether a bitstring represents a valid matching:
    /// no two selected edges share a vertex.
    /// Also validates bitstring length matches edge count.
    let isValid (problem: Problem) (bits: int[]) : bool =
        let edges = normalizeEdges problem.Edges
        bits.Length = edges.Length
        && (
            let selectedVertices =
                edges
                |> List.indexed
                |> List.filter (fun (eIdx, _) -> bits.[eIdx] = 1)
                |> List.collect (fun (_, edge) -> [ edge.Source; edge.Target ])
            let distinct = selectedVertices |> List.distinct
            distinct.Length = selectedVertices.Length)

    /// Decode a bitstring into a Solution.
    let private decodeSolution (edges: Edge list) (bits: int[]) : Solution =
        let selected =
            edges
            |> List.indexed
            |> List.choose (fun (eIdx, edge) ->
                if eIdx < bits.Length && bits.[eIdx] = 1 then Some edge
                else None)

        let usedVertices =
            selected |> List.collect (fun e -> [ e.Source; e.Target ])

        let isValidMatching =
            let distinct = usedVertices |> List.distinct
            distinct.Length = usedVertices.Length

        {
            SelectedEdges = selected
            TotalWeight = selected |> List.sumBy (fun e -> e.Weight)
            MatchingSize = selected.Length
            IsValid = isValidMatching
            WasRepaired = false
            BackendName = ""
            NumShots = 0
            OptimizedParameters = None
            OptimizationConverged = None
        }

    // ========================================================================
    // CONSTRAINT REPAIR (recursive, idiomatic F#)
    // ========================================================================

    /// Repair a matching by removing conflicting edges.
    /// Strategy: sort selected edges by weight descending, greedily keep edges
    /// whose vertices haven't been used yet.
    let private repairConstraints (edges: Edge list) (bits: int[]) : int[] =
        let selected =
            edges
            |> List.indexed
            |> List.choose (fun (eIdx, edge) ->
                if eIdx < bits.Length && bits.[eIdx] = 1 then Some (eIdx, edge)
                else None)
            |> List.sortByDescending (fun (_, edge) -> edge.Weight)

        let rec greedyKeep
            (remaining: (int * Edge) list)
            (usedVertices: Set<int>)
            (kept: Set<int>) =
            match remaining with
            | [] -> kept
            | (eIdx, edge) :: rest ->
                if usedVertices |> Set.contains edge.Source
                   || usedVertices |> Set.contains edge.Target then
                    greedyKeep rest usedVertices kept
                else
                    let newUsed =
                        usedVertices
                        |> Set.add edge.Source
                        |> Set.add edge.Target
                    greedyKeep rest newUsed (kept |> Set.add eIdx)

        let keptEdges = greedyKeep selected Set.empty Set.empty

        Array.init edges.Length (fun eIdx ->
            if keptEdges |> Set.contains eIdx then 1 else 0)

    // ========================================================================
    // DECOMPOSE / RECOMBINE HOOKS (Decision 10: identity stubs)
    // ========================================================================

    /// Decompose a problem into sub-problems. Currently identity (single problem).
    let decompose (problem: Problem) : Problem list = [ problem ]

    /// Recombine sub-solutions into a single solution. Currently identity.
    /// Handles empty list gracefully.
    let recombine (solutions: Solution list) : Solution =
        match solutions with
        | [] ->
            {
                SelectedEdges = []
                TotalWeight = 0.0
                MatchingSize = 0
                IsValid = false
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
            }
        | [ single ] -> single
        | _ -> solutions |> List.maxBy (fun s -> s.TotalWeight)

    // ========================================================================
    // QUANTUM SOLVERS (Rule 1: IQuantumBackend required)
    // ========================================================================

    /// Solve maximum matching using QAOA with full configuration control.
    let solveWithConfig
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (config: Config)
        : Result<Solution, QuantumError> =

        match validateProblem problem with
        | Error err -> Error err
        | Ok () ->
            let edges = normalizeEdges problem.Edges
            match toQubo problem with
            | Error err -> Error err
            | Ok qubo ->
                let result =
                    if config.EnableOptimization then
                        executeQaoaWithOptimization backend qubo config
                        |> Result.map (fun (bits, optParams, converged) ->
                            (bits, Some optParams, Some converged))
                    else
                        executeQaoaWithGridSearch backend qubo config
                        |> Result.map (fun (bits, optParams) ->
                            (bits, Some optParams, None))

                match result with
                | Error err -> Error err
                | Ok (bits, optParams, converged) ->
                    let needsRepair =
                        let decoded = decodeSolution edges bits
                        not decoded.IsValid

                    let finalBits, wasRepaired =
                        if config.EnableConstraintRepair && needsRepair then
                            (repairConstraints edges bits, true)
                        else
                            (bits, false)

                    let solution = decodeSolution edges finalBits
                    Ok { solution with
                            BackendName = backend.Name
                            NumShots = config.FinalShots
                            WasRepaired = wasRepaired
                            OptimizedParameters = optParams
                            OptimizationConverged = converged }

    /// Solve maximum matching using QAOA with default configuration.
    let solve
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (shots: int)
        : Result<Solution, QuantumError> =

        let config = { defaultConfig with FinalShots = shots }
        solveWithConfig backend problem config

    // ========================================================================
    // CLASSICAL SOLVER (Rule 1: private — not exposed without backend)
    // ========================================================================

    /// Classical greedy maximum matching for comparison.
    /// Strategy: sort edges by weight descending, greedily select edges
    /// whose vertices haven't been used yet.
    let private solveClassical (problem: Problem) : Solution =
        if problem.Edges.IsEmpty || problem.NumVertices <= 0 then
            {
                SelectedEdges = []
                TotalWeight = 0.0
                MatchingSize = 0
                IsValid = true
                WasRepaired = false
                BackendName = "Classical Greedy"
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
            }
        else
            let edges = normalizeEdges problem.Edges

            let rec greedyMatch
                (remaining: Edge list)
                (usedVertices: Set<int>)
                (selected: Edge list) =
                match remaining with
                | [] -> selected |> List.rev
                | edge :: rest ->
                    if usedVertices |> Set.contains edge.Source
                       || usedVertices |> Set.contains edge.Target then
                        greedyMatch rest usedVertices selected
                    else
                        let newUsed =
                            usedVertices
                            |> Set.add edge.Source
                            |> Set.add edge.Target
                        greedyMatch rest newUsed (edge :: selected)

            let sorted = edges |> List.sortByDescending (fun e -> e.Weight)
            let matched = greedyMatch sorted Set.empty []

            {
                SelectedEdges = matched
                TotalWeight = matched |> List.sumBy (fun e -> e.Weight)
                MatchingSize = matched.Length
                IsValid = true
                WasRepaired = false
                BackendName = "Classical Greedy"
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
            }
