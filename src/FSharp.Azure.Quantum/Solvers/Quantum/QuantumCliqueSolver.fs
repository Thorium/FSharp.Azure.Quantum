namespace FSharp.Azure.Quantum.Quantum

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers

/// Quantum Maximum Clique Solver
///
/// Problem: Given graph G=(V,E), find the largest complete subgraph
/// (i.e., the largest subset S of vertices such that every pair in S is connected).
///
/// QUBO Formulation:
///   Variables: x_i in {0,1} per vertex (1 = in clique)
///   Maximize:  Sum_i x_i  (maximize clique size)
///     => Minimize: -Sum_i x_i
///   Constraint: For each NON-edge (i,j) where (i,j) not in E and i<>j,
///     x_i and x_j cannot both be 1.
///     Penalty: lambda * x_i * x_j  for each non-edge
///
/// This is equivalent to Maximum Independent Set on the complement graph.
///
/// Qubits: |V|
///
/// RULE 1 COMPLIANCE:
/// All public solve functions require IQuantumBackend parameter.
/// Classical solver is private.
module QuantumCliqueSolver =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// A vertex in the graph
    type Vertex = {
        Id: string
        /// Optional priority weight for tie-breaking; default 1.0
        Weight: float
    }

    /// Maximum clique problem definition
    type Problem = {
        Vertices: Vertex list
        /// Edges as (source index, target index) pairs.
        /// Represents the ACTUAL edges of the graph.
        Edges: (int * int) list
    }

    /// Maximum clique solution
    type Solution = {
        /// Vertices in the found clique
        CliqueVertices: Vertex list
        /// Size of the clique
        CliqueSize: int
        /// Sum of vertex weights in the clique
        CliqueWeight: float
        /// Whether all pairs of selected vertices are connected
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

    /// Estimate the number of qubits required for a clique problem.
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
    // INTERNAL HELPERS
    // ========================================================================

    /// Build the set of non-edges (complement graph edges).
    /// A non-edge (i,j) exists when vertices i and j are NOT connected in G.
    /// Uses normalized edges for correct duplicate/bidirectional handling.
    let private buildNonEdges (problem: Problem) : (int * int) list =
        let n = problem.Vertices.Length
        let edgeSet =
            normalizeEdges problem.Edges
            |> Set.ofList

        [ for i in 0 .. n - 2 do
            for j in i + 1 .. n - 1 do
                if not (Set.contains (i, j) edgeSet) then
                    yield (i, j) ]

    /// Build adjacency set for fast neighbor lookup.
    /// Uses normalized edges, stored in both directions for O(1) lookup.
    let private buildAdjacencySet (problem: Problem) : Set<int * int> =
        normalizeEdges problem.Edges
        |> List.collect (fun (i, j) -> [ (i, j); (j, i) ])
        |> Set.ofList

    // ========================================================================
    // QUBO CONSTRUCTION (Decision 9: sparse internally, Decision 5: dense output)
    // ========================================================================

    /// Build the QUBO as a sparse map.
    ///
    /// Objective: maximize Sum_i x_i  =>  minimize -Sum_i x_i
    ///   Diagonal Q[i,i] = -1 (or -w_i for weighted variant)
    ///
    /// Constraint: for each non-edge (i,j), x_i*x_j must be 0
    ///   Penalty: lambda * x_i * x_j  for each non-edge
    ///   Off-diagonal Q[i,j] += lambda / 2  (symmetric split)
    let private buildQuboMap (problem: Problem) : Map<int * int, float> =
        let n = problem.Vertices.Length
        let nonEdges = buildNonEdges problem

        // Penalty must dominate the objective.
        // Max objective magnitude = n (all vertices selected with weight 1 each).
        let totalWeight = problem.Vertices |> List.sumBy (fun v -> abs v.Weight)
        let penalty = max (float n) totalWeight + 1.0

        // Linear terms: -w_i (maximize clique size/weight)
        let linearTerms =
            problem.Vertices
            |> List.indexed
            |> List.map (fun (i, v) -> ((i, i), -v.Weight))

        // Quadratic penalty for non-edges (symmetric split)
        let quadraticTerms =
            nonEdges
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

    /// Check whether a bitstring represents a valid clique:
    /// every pair of selected vertices must be connected by an edge.
    /// Also validates bitstring length matches vertex count.
    let isValid (problem: Problem) (bits: int[]) : bool =
        bits.Length = problem.Vertices.Length
        && (
            let adjacency = buildAdjacencySet problem
            let selected = bits |> Array.indexed |> Array.choose (fun (i, b) -> if b = 1 then Some i else None)

            // Every pair of selected vertices must have an edge
            selected
            |> Array.forall (fun i ->
                selected
                |> Array.forall (fun j ->
                    i = j || Set.contains (i, j) adjacency))
        )

    /// Decode a bitstring into a Solution.
    let private decodeSolution (problem: Problem) (bits: int[]) : Solution =
        let selected =
            problem.Vertices
            |> List.indexed
            |> List.choose (fun (i, v) -> if bits.[i] = 1 then Some v else None)

        {
            CliqueVertices = selected
            CliqueSize = selected.Length
            CliqueWeight = selected |> List.sumBy (fun v -> v.Weight)
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

    /// Repair an infeasible solution by removing vertices that violate the clique property.
    /// Strategy: iteratively remove the vertex involved in the most non-edge conflicts
    /// (among selected vertices), breaking ties by lowest weight, until valid.
    let private repairConstraints (problem: Problem) (bits: int[]) : int[] =
        let adjacency = buildAdjacencySet problem

        let rec fix (current: int[]) =
            let selected =
                current
                |> Array.indexed
                |> Array.choose (fun (i, b) -> if b = 1 then Some i else None)

            // Find all non-edge conflicts among selected vertices
            let conflicts =
                [ for si in 0 .. selected.Length - 2 do
                    for sj in si + 1 .. selected.Length - 1 do
                        let i = selected.[si]
                        let j = selected.[sj]
                        if not (Set.contains (i, j) adjacency) then
                            yield (i, j) ]

            if List.isEmpty conflicts then
                current  // Valid clique
            else
                // Count conflicts per vertex
                let conflictCounts =
                    conflicts
                    |> List.collect (fun (i, j) -> [ i; j ])
                    |> List.countBy id
                    |> Map.ofList

                // Remove the vertex with most conflicts; break ties by lowest weight
                let worstVertex =
                    selected
                    |> Array.filter (fun i -> conflictCounts |> Map.containsKey i)
                    |> Array.sortByDescending (fun i ->
                        let count = conflictCounts |> Map.tryFind i |> Option.defaultValue 0
                        (count, -problem.Vertices.[i].Weight))
                    |> Array.tryHead

                match worstVertex with
                | None -> current  // No conflicting vertices to remove (shouldn't happen)
                | Some idx ->
                    let updated = Array.copy current
                    updated.[idx] <- 0
                    fix updated

        fix (Array.copy bits)

    // ========================================================================
    // DECOMPOSE / RECOMBINE HOOKS (Decision 10: identity stubs)
    // ========================================================================

    /// Decompose a clique problem into independent sub-problems by connected
    /// components. Cliques exist entirely within a single component, so each
    /// component can be solved independently.
    let decompose (problem: Problem) : Problem list =
        let n = problem.Vertices.Length
        if n <= 1 then [ problem ]
        else
            let parts = ProblemDecomposition.partitionByComponents n problem.Edges
            match parts with
            | [ _ ] -> [ problem ]
            | components ->
                components
                |> List.map (fun (globalIndices, localEdges) ->
                    let localVertices =
                        globalIndices
                        |> List.map (fun gi -> problem.Vertices.[gi])
                    { Vertices = localVertices; Edges = localEdges })

    /// Recombine sub-solutions into a single solution. Currently identity (single solution).
    /// Handles empty list gracefully.
    let recombine (solutions: Solution list) : Solution =
        match solutions with
        | [] ->
            {
                CliqueVertices = []
                CliqueSize = 0
                CliqueWeight = 0.0
                IsValid = true
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
            }
        | [ single ] -> single
        | _ -> solutions |> List.maxBy (fun s -> s.CliqueSize)

    // ========================================================================
    // QUANTUM SOLVERS (Rule 1: IQuantumBackend required)
    // ========================================================================

    /// Solve maximum clique using QAOA with full configuration control.
    /// Automatically decomposes into connected components when the problem
    /// exceeds backend qubit capacity.
    [<Obsolete("Use solveWithConfigAsync for non-blocking execution against cloud backends")>]
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
            let solveSingle (subProblem: Problem) =
                match toQubo subProblem with
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
                            if config.EnableConstraintRepair && not (isValid subProblem bits) then
                                (repairConstraints subProblem bits, true)
                            else
                                (bits, false)

                        let solution = decodeSolution subProblem finalBits
                        Ok { solution with
                                BackendName = backend.Name
                                NumShots = config.FinalShots
                                WasRepaired = wasRepaired
                                OptimizedParameters = optParams
                                OptimizationConverged = converged }

            ProblemDecomposition.solveWithDecomposition
                backend problem estimateQubits decompose recombine solveSingle

    /// Solve maximum clique using QAOA with full configuration control (async).
    /// Wraps the synchronous solveWithConfig in a task; will become truly async
    /// once ProblemDecomposition supports async solve functions.
    let solveWithConfigAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (config: Config)
        (cancellationToken: CancellationToken)
        : Task<Result<Solution, QuantumError>> = task {
        cancellationToken.ThrowIfCancellationRequested()
        return solveWithConfig backend problem config
    }

    /// Solve maximum clique using QAOA with default configuration.
    [<Obsolete("Use solveWithConfigAsync for non-blocking execution against cloud backends")>]
    let solve
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (shots: int)
        : Result<Solution, QuantumError> =

        let config = { defaultConfig with FinalShots = shots }
        solveWithConfigAsync backend problem config CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    // ========================================================================
    // CLASSICAL SOLVER (Rule 1: private — not exposed without backend)
    // ========================================================================

    /// Classical greedy clique finder for comparison.
    /// Strategy: start with highest-weight vertex, greedily add vertices
    /// that are connected to all current clique members (prefer highest weight).
    let private solveClassical (problem: Problem) : Solution =
        if problem.Vertices.IsEmpty then
            decodeSolution problem (Array.zeroCreate 0)
            |> fun s -> { s with BackendName = "Classical Greedy" }
        else
            let n = problem.Vertices.Length
            let adjacency = buildAdjacencySet problem

            // Start with the highest-weight vertex
            let startVertex =
                problem.Vertices
                |> List.indexed
                |> List.maxBy (fun (_, v) -> v.Weight)
                |> fst

            // Greedily add vertices connected to all current clique members
            let candidates =
                [ 0 .. n - 1 ]
                |> List.filter (fun i -> i <> startVertex)
                |> List.sortByDescending (fun i -> problem.Vertices.[i].Weight)

            let clique =
                candidates
                |> List.fold (fun (acc: Set<int>) candidate ->
                    let connectedToAll =
                        acc |> Set.forall (fun member' ->
                            Set.contains (candidate, member') adjacency)
                    if connectedToAll then acc |> Set.add candidate
                    else acc
                ) (Set.singleton startVertex)

            let bits = Array.init n (fun i -> if clique |> Set.contains i then 1 else 0)
            decodeSolution problem bits
            |> fun s -> { s with BackendName = "Classical Greedy" }
