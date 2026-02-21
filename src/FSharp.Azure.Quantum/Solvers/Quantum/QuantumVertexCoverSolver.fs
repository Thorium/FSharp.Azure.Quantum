namespace FSharp.Azure.Quantum.Quantum

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers

/// Quantum Minimum Vertex Cover Solver
///
/// Problem: Given graph G=(V,E), find minimum-cardinality vertex set S
/// such that every edge has at least one endpoint in S.
///
/// QUBO Formulation:
///   Variables: x_i in {0,1} per vertex (1 = in cover)
///   Minimize:  Sum_i x_i  (minimize cover size)
///   Constraint: For each edge (i,j), x_i + x_j >= 1
///     Encoded as penalty: lambda * (1 - x_i)(1 - x_j)
///                       = lambda * (1 - x_i - x_j + x_i*x_j)
///
/// Qubits: |V|
///
/// RULE 1 COMPLIANCE:
/// All public solve functions require IQuantumBackend parameter.
/// Classical solver is private.
module QuantumVertexCoverSolver =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// A vertex in the graph
    type Vertex = {
        Id: string
        /// Optional weight (for weighted vertex cover); default 1.0
        Weight: float
    }

    /// Vertex cover problem definition
    type Problem = {
        Vertices: Vertex list
        /// Edges as (source index, target index) pairs
        Edges: (int * int) list
    }

    /// Vertex cover solution
    type Solution = {
        /// Vertices included in the cover
        CoverVertices: Vertex list
        /// Total weight of the cover (sum of weights of selected vertices)
        CoverWeight: float
        /// Number of vertices in the cover
        CoverSize: int
        /// Whether every edge has at least one endpoint in the cover
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
    // QUBIT ESTIMATION (Decision 11)
    // ========================================================================

    /// Estimate the number of qubits required for a vertex cover problem.
    /// One qubit per vertex.
    let estimateQubits (problem: Problem) : int =
        problem.Vertices.Length

    // ========================================================================
    // EDGE NORMALIZATION
    // ========================================================================

    /// Normalize edges to canonical form: (min(i,j), max(i,j)), deduplicated.
    /// Handles bidirectional edges and duplicates.
    let private normalizeEdges (edges: (int * int) list) : (int * int) list =
        edges
        |> List.map (fun (i, j) -> (min i j, max i j))
        |> List.distinct

    // ========================================================================
    // QUBO CONSTRUCTION (Decision 9: sparse internally, Decision 5: dense output)
    // ========================================================================

    /// Build the QUBO as a sparse map.
    ///
    /// Objective: minimize Sum_i w_i * x_i
    /// Constraint per edge (i,j): x_i + x_j >= 1
    ///   Penalty: lambda * (1 - x_i - x_j + x_i*x_j)
    ///          = lambda * (1) + lambda * (-1)*x_i + lambda * (-1)*x_j + lambda * x_i*x_j
    ///
    /// Diagonal Q[i,i] = w_i + sum_{edges touching i} (-lambda)
    /// Off-diagonal Q[i,j] = lambda  for each edge (i,j)
    /// (Constant term lambda * |E| is ignored since it doesn't affect argmin.)
    let private buildQuboMap (problem: Problem) : Map<int * int, float> =
        let normalizedEdges = normalizeEdges problem.Edges

        // Penalty must dominate the objective.
        // Max possible objective = sum of all weights.
        let totalWeight = problem.Vertices |> List.sumBy (fun v -> abs v.Weight)
        let penalty = totalWeight + 1.0

        // Linear terms: w_i (objective) + (-penalty) per incident edge
        let edgeCounts =
            normalizedEdges
            |> List.collect (fun (i, j) -> [ i; j ])
            |> List.countBy id
            |> Map.ofList

        let linearTerms =
            problem.Vertices
            |> List.indexed
            |> List.map (fun (i, v) ->
                let incidentCount = edgeCounts |> Map.tryFind i |> Option.defaultValue 0
                ((i, i), v.Weight - penalty * float incidentCount))

        // Quadratic terms: +penalty for each edge (symmetric split)
        let quadraticTerms =
            normalizedEdges
            |> List.collect (fun (i, j) ->
                [ ((i, j), penalty / 2.0)
                  ((j, i), penalty / 2.0) ])

        (linearTerms @ quadraticTerms)
        |> List.fold (fun acc (key, value) -> Qubo.combineTerms key value acc) Map.empty

    /// Convert problem to dense QUBO matrix.
    /// Returns Result to follow the canonical pattern (validates inputs).
    let toQubo (problem: Problem) : Result<float[,], QuantumError> =
        if problem.Vertices.IsEmpty then
            Error (QuantumError.ValidationError ("vertices", "Problem has no vertices"))
        else
            let n = problem.Vertices.Length
            let quboMap = buildQuboMap problem
            Ok (Qubo.toDenseArray n quboMap)

    // ========================================================================
    // SOLUTION DECODING & VALIDATION
    // ========================================================================

    /// Check whether a bitstring represents a valid vertex cover:
    /// every edge (i,j) must have at least one endpoint selected.
    /// Also validates bitstring length matches vertex count.
    let isValid (problem: Problem) (bits: int[]) : bool =
        bits.Length = problem.Vertices.Length
        && problem.Edges
           |> List.forall (fun (i, j) ->
               let ci = min i j |> max 0
               let cj = max i j |> min (bits.Length - 1)
               bits.[ci] = 1 || bits.[cj] = 1)

    /// Decode a bitstring into a Solution.
    let private decodeSolution (problem: Problem) (bits: int[]) : Solution =
        let selected =
            problem.Vertices
            |> List.indexed
            |> List.choose (fun (i, v) -> if bits.[i] = 1 then Some v else None)

        {
            CoverVertices = selected
            CoverWeight = selected |> List.sumBy (fun v -> v.Weight)
            CoverSize = selected.Length
            IsValid = isValid problem bits
            WasRepaired = false
            BackendName = ""
            NumShots = 0
            OptimizedParameters = None
            OptimizationConverged = None
        }

    // ========================================================================
    // CONSTRAINT REPAIR (recursive, idiomatic F#)
    // ========================================================================

    /// Repair an infeasible solution by adding vertices to cover uncovered edges.
    /// Strategy: for each uncovered edge (i,j), add the lighter-weight endpoint.
    /// Then recursively remove redundant vertices (heaviest first) whose removal
    /// doesn't uncover any edge.
    let private repairConstraints (problem: Problem) (bits: int[]) : int[] =
        let normalizedEdges = normalizeEdges problem.Edges

        // Phase 1: ensure every edge is covered
        let afterCover =
            normalizedEdges
            |> List.fold (fun (acc: int[]) (i, j) ->
                if acc.[i] = 0 && acc.[j] = 0 then
                    let updated = Array.copy acc
                    if problem.Vertices.[i].Weight <= problem.Vertices.[j].Weight then
                        updated.[i] <- 1
                    else
                        updated.[j] <- 1
                    updated
                else
                    acc
            ) (Array.copy bits)

        // Phase 2: recursively remove redundant vertices (heaviest first)
        let sortedSelected =
            problem.Vertices
            |> List.indexed
            |> List.filter (fun (i, _) -> afterCover.[i] = 1)
            |> List.sortByDescending (fun (_, v) -> v.Weight)

        let rec tryRemove (current: int[]) (candidates: (int * Vertex) list) =
            match candidates with
            | [] -> current
            | (idx, _) :: rest ->
                let tentative = Array.copy current
                tentative.[idx] <- 0
                if isValid problem tentative then
                    tryRemove tentative rest
                else
                    tryRemove current rest

        tryRemove afterCover sortedSelected

    // ========================================================================
    // DECOMPOSE / RECOMBINE HOOKS (Decision 10: identity stubs)
    // ========================================================================

    /// Decompose a problem into sub-problems. Currently identity (single problem).
    let decompose (problem: Problem) : Problem list = [ problem ]

    /// Recombine sub-solutions into a single solution. Currently identity (single solution).
    /// Handles empty list gracefully.
    let recombine (solutions: Solution list) : Solution =
        match solutions with
        | [] ->
            {
                CoverVertices = []
                CoverWeight = 0.0
                CoverSize = 0
                IsValid = true
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
            }
        | [ single ] -> single
        | _ -> solutions |> List.minBy (fun s -> s.CoverWeight)

    // ========================================================================
    // QUANTUM SOLVERS (Rule 1: IQuantumBackend required)
    // ========================================================================

    /// Solve vertex cover using QAOA with full configuration control.
    let solveWithConfig
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (config: Config)
        : Result<Solution, QuantumError> =

        if problem.Vertices.IsEmpty then
            Error (QuantumError.ValidationError ("vertices", "Problem has no vertices"))
        elif problem.Edges |> List.exists (fun (i, j) ->
                i < 0 || j < 0 || i >= problem.Vertices.Length || j >= problem.Vertices.Length || i = j) then
            Error (QuantumError.ValidationError ("edges", "Edge index out of range or self-loop"))
        else
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
                    let finalBits, wasRepaired =
                        if config.EnableConstraintRepair && not (isValid problem bits) then
                            (repairConstraints problem bits, true)
                        else
                            (bits, false)

                    let solution = decodeSolution problem finalBits
                    Ok { solution with
                            BackendName = backend.Name
                            NumShots = config.FinalShots
                            WasRepaired = wasRepaired
                            OptimizedParameters = optParams
                            OptimizationConverged = converged }

    /// Solve vertex cover using QAOA with default configuration.
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

    /// Classical greedy vertex cover for comparison.
    /// Strategy: repeatedly pick the edge with lowest-weight uncovered endpoint.
    /// This is a 2-approximation for unweighted vertex cover.
    let private solveClassical (problem: Problem) : Solution =
        if problem.Vertices.IsEmpty then
            decodeSolution problem (Array.zeroCreate 0)
            |> fun s -> { s with BackendName = "Classical Greedy" }
        else
            let n = problem.Vertices.Length
            let normalizedEdges = normalizeEdges problem.Edges

            // Sort edges by the minimum-weight endpoint (greedy heuristic)
            let sortedEdges =
                normalizedEdges
                |> List.sortBy (fun (i, j) ->
                    min problem.Vertices.[i].Weight problem.Vertices.[j].Weight)

            // Greedily cover edges using fold with immutable Set
            let selected =
                sortedEdges
                |> List.fold (fun (sel: Set<int>, covered: Set<int * int>) (i, j) ->
                    if covered |> Set.contains (i, j) then
                        (sel, covered)
                    else
                        // Pick the lighter endpoint (skip if already selected)
                        let pick =
                            if sel |> Set.contains i then None
                            elif sel |> Set.contains j then None
                            elif problem.Vertices.[i].Weight <= problem.Vertices.[j].Weight then Some i
                            else Some j

                        let sel' =
                            match pick with
                            | Some v -> sel |> Set.add v
                            | None -> sel

                        // Mark all edges incident to selected vertices as covered
                        let covered' =
                            normalizedEdges
                            |> List.fold (fun acc (ei, ej) ->
                                if sel' |> Set.contains ei || sel' |> Set.contains ej then
                                    acc |> Set.add (ei, ej)
                                else
                                    acc
                            ) covered

                        (sel', covered')
                ) (Set.empty, Set.empty)
                |> fst

            let bits = Array.init n (fun i -> if selected |> Set.contains i then 1 else 0)
            decodeSolution problem bits
            |> fun s -> { s with BackendName = "Classical Greedy" }
