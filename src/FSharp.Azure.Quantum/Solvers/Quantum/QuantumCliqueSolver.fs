namespace FSharp.Azure.Quantum.Quantum

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers

/// Quantum Maximum-Weight Clique Solver
///
/// Problem: Given graph G=(V,E) with a weight per vertex, find the complete subgraph
/// (a subset S of vertices such that every pair in S is connected) of the largest
/// total weight. With every weight 1.0 this is the largest clique; with other weights
/// a small heavy clique beats a large light one.
///
/// QUBO Formulation:
///   Variables: x_i in {0,1} per vertex (1 = in clique)
///   Maximize:  Sum_i w_i * x_i  (maximize clique weight)
///     => Minimize: -Sum_i w_i * x_i
///   Constraint: For each NON-edge (i,j) where (i,j) not in E and i<>j,
///     x_i and x_j cannot both be 1.
///     Penalty: lambda * x_i * x_j  for each non-edge
///
/// This is equivalent to Maximum Weight Independent Set on the complement graph.
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
    type Vertex =
        {
            Id: string
            /// Weight the vertex adds to a clique; the solver maximises the total.
            /// 1.0 on every vertex makes the heaviest clique the largest one.
            Weight: float
        }

    /// Maximum-weight clique problem definition
    type Problem =
        {
            Vertices: Vertex list
            /// Edges as (source index, target index) pairs.
            /// Represents the ACTUAL edges of the graph.
            Edges: (int * int) list
        }

    /// Maximum-weight clique solution
    type Solution =
        {
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
            /// Standing of this solution among the final samples; None when no single sampling run produced it
            Sampling: QaoaExecutionHelpers.SampleStatistics option
            /// How the problem was split into circuits that fit the backend; None when it ran as one circuit
            Split: QaoaExecutionHelpers.SplitReport option
        }

    // ========================================================================
    // CONFIGURATION (type alias for unified config)
    // ========================================================================

    type Config = QaoaSolverConfig

    let defaultConfig: Config = QaoaExecutionHelpers.defaultConfig
    let fastConfig: Config = QaoaExecutionHelpers.fastConfig
    let highQualityConfig: Config = QaoaExecutionHelpers.highQualityConfig

    // ========================================================================
    // QUBIT ESTIMATION (Decision 11)
    // ========================================================================

    /// Estimate the number of qubits required for a clique problem.
    /// One qubit per vertex.
    let estimateQubits (problem: Problem) : int = problem.Vertices.Length

    // ========================================================================
    // EDGE NORMALIZATION
    // ========================================================================

    /// Normalize edges to canonical form: (min(i,j), max(i,j)), deduplicated.
    /// Handles bidirectional edges and duplicates.
    let private normalizeEdges (edges: (int * int) list) : (int * int) list =
        edges |> List.map (fun (i, j) -> (min i j, max i j)) |> List.distinct

    // ========================================================================
    // INTERNAL HELPERS
    // ========================================================================

    /// Build the set of non-edges (complement graph edges).
    /// A non-edge (i,j) exists when vertices i and j are NOT connected in G.
    /// Uses normalized edges for correct duplicate/bidirectional handling.
    let private buildNonEdges (problem: Problem) : (int * int) list =
        let n = problem.Vertices.Length
        let edgeSet = normalizeEdges problem.Edges |> Set.ofList

        [
            for i in 0 .. n - 2 do
                for j in i + 1 .. n - 1 do
                    if not (Set.contains (i, j) edgeSet) then
                        yield (i, j)
        ]

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
    /// Objective: maximize Sum_i w_i * x_i  =>  minimize -Sum_i w_i * x_i
    ///   Diagonal Q[i,i] = -w_i
    ///
    /// Constraint: for each non-edge (i,j), x_i*x_j must be 0
    ///   Penalty: lambda * x_i * x_j  for each non-edge
    ///   Off-diagonal Q[i,j] += lambda / 2  (symmetric split)
    ///
    /// The minimum is a clique of the largest total weight (the largest clique when
    /// every weight is 1.0).
    let private buildQuboMap (problem: Problem) : Map<int * int, float> =
        let n = problem.Vertices.Length
        let nonEdges = buildNonEdges problem

        // Penalty must dominate the objective, whose magnitude is at most the total weight
        // (n when every vertex weighs 1).
        let totalWeight = problem.Vertices |> List.sumBy (fun v -> abs v.Weight)
        let penalty = max (float n) totalWeight + 1.0

        // Linear terms: -w_i (maximize clique weight)
        let linearTerms =
            problem.Vertices |> List.indexed |> List.map (fun (i, v) -> ((i, i), -v.Weight))

        // Quadratic penalty for non-edges (symmetric split)
        let quadraticTerms =
            nonEdges
            |> List.collect (fun (i, j) -> [ ((i, j), penalty / 2.0); ((j, i), penalty / 2.0) ])

        (linearTerms @ quadraticTerms)
        |> List.fold (fun acc (key, value) -> Qubo.combineTerms key value acc) Map.empty

    /// Convert problem to dense QUBO matrix.
    /// Returns Result to follow the canonical pattern (validates inputs).
    let toQubo (problem: Problem) : Result<float[,], QuantumError> =
        if problem.Vertices.IsEmpty then
            Error(QuantumError.ValidationError("vertices", "Problem has no vertices"))
        else
            let n = problem.Vertices.Length
            let quboMap = buildQuboMap problem
            Ok(Qubo.toDenseArray n quboMap)

    // ========================================================================
    // SOLUTION DECODING & VALIDATION
    // ========================================================================

    /// Check whether a bitstring represents a valid clique:
    /// every pair of selected vertices must be connected by an edge.
    /// Also validates bitstring length matches vertex count.
    let isValid (problem: Problem) (bits: int[]) : bool =
        bits.Length = problem.Vertices.Length
        && (let adjacency = buildAdjacencySet problem

            let selected =
                bits
                |> Array.indexed
                |> Array.choose (fun (i, b) -> if b = 1 then Some i else None)

            // Every pair of selected vertices must have an edge
            selected
            |> Array.forall (fun i -> selected |> Array.forall (fun j -> i = j || Set.contains (i, j) adjacency)))

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
            Sampling = None
            Split = None
        }

    // ========================================================================
    // CONSTRAINT REPAIR (recursive, idiomatic F#)
    // ========================================================================

    /// Repair an infeasible solution by removing vertices that violate the clique property.
    /// Strategy: iteratively remove the vertex involved in the most non-edge conflicts
    /// (among selected vertices), breaking ties by lowest weight, until valid.
    let private repairConstraints (problem: Problem) (bits: int[]) : int[] =
        let adjacency = buildAdjacencySet problem
        let vertices = problem.Vertices |> List.toArray

        let rec fix (current: int[]) =
            let selected =
                current
                |> Array.indexed
                |> Array.choose (fun (i, b) -> if b = 1 then Some i else None)

            // Find all non-edge conflicts among selected vertices
            let conflicts =
                [
                    for si in 0 .. selected.Length - 2 do
                        for sj in si + 1 .. selected.Length - 1 do
                            let i = selected.[si]
                            let j = selected.[sj]

                            if not (Set.contains (i, j) adjacency) then
                                yield (i, j)
                ]

            if List.isEmpty conflicts then
                current // Valid clique
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
                        (count, -vertices.[i].Weight))
                    |> Array.tryHead

                match worstVertex with
                | None -> current // No conflicting vertices to remove (shouldn't happen)
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

        if n <= 1 then
            [ problem ]
        else
            let parts = ProblemDecomposition.partitionByComponents n problem.Edges

            match parts with
            | [ _ ] -> [ problem ]
            | components ->
                let vertices = problem.Vertices |> List.toArray

                components
                |> List.map (fun (globalIndices, localEdges) ->
                    let localVertices = globalIndices |> List.map (fun gi -> vertices.[gi])

                    {
                        Vertices = localVertices
                        Edges = localEdges
                    })

    /// Recombine the per-component solutions into one: the clique of the largest total
    /// weight, the measure the QUBO maximises. An empty list gives the empty clique.
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
                Sampling = None
                Split = None
            }
        | [ single ] -> single
        // A clique is fully connected, so it cannot span disconnected components:
        // the maximum-weight clique lives entirely within one component. Picking the
        // heaviest per-component clique is therefore correct (do NOT union here).
        // Split counts the runs of every component, not only the winner's.
        | _ ->
            { (solutions |> List.maxBy (fun s -> s.CliqueWeight)) with
                Split = QaoaExecutionHelpers.combineSplitReports (solutions |> List.map (fun s -> s.Split))
            }

    // ========================================================================
    // QUANTUM SOLVERS (Rule 1: IQuantumBackend required)
    // ========================================================================

    /// Shared implementation of solveWithConfigAsync.
    let private solveWithConfigCore
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (config: Config)
        (cancellationToken: CancellationToken)
        : Task<Result<Solution, QuantumError>> =

        let numVertices = problem.Vertices.Length

        if numVertices = 0 then
            Task.FromResult(Error(QuantumError.ValidationError("vertices", "Problem has no vertices")))
        elif
            problem.Edges
            |> List.exists (fun (i, j) -> i < 0 || j < 0 || i >= numVertices || j >= numVertices || i = j)
        then
            Task.FromResult(Error(QuantumError.ValidationError("edges", "Edge index out of range or self-loop")))
        else
            let solveSingle (subProblem: Problem) : Task<Result<Solution, QuantumError>> =
                task {
                    match toQubo subProblem with
                    | Error err -> return Error err
                    | Ok qubo ->
                        match! QuboSplitting.runQaoaAsync backend qubo config cancellationToken with
                        | Error err -> return Error err
                        | Ok run ->
                            let bits = run.Best
                            let optParams = run.Direct |> Option.map (fun direct -> direct.Parameters)
                            let converged = run.Direct |> Option.bind (fun direct -> direct.Converged)

                            let finalBits, wasRepaired =
                                if config.EnableConstraintRepair && not (isValid subProblem bits) then
                                    (repairConstraints subProblem bits, true)
                                else
                                    (bits, false)

                            let solution = decodeSolution subProblem finalBits

                            // A split run has no single sample set to take statistics from
                            let sampling =
                                run.Direct
                                |> Option.map (fun direct ->
                                    sampleStatistics
                                        bits.Length
                                        (isValid subProblem)
                                        (fun sample -> sample = finalBits)
                                        direct.Samples)

                            return
                                Ok
                                    { solution with
                                        BackendName = backend.Name
                                        NumShots = config.FinalShots
                                        WasRepaired = wasRepaired
                                        OptimizedParameters = optParams
                                        OptimizationConverged = converged
                                        Sampling = sampling
                                        Split = run.Split
                                    }
                }

            ProblemDecomposition.solveWithDecompositionAsync
                backend
                problem
                estimateQubits
                decompose
                recombine
                solveSingle

    /// Solve maximum-weight clique using QAOA with full configuration control (async).
    /// Automatically decomposes into connected components when the problem
    /// exceeds backend qubit capacity.
    let solveWithConfigAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (config: Config)
        (cancellationToken: CancellationToken)
        : Task<Result<Solution, QuantumError>> =
        task {
            cancellationToken.ThrowIfCancellationRequested()
            return! solveWithConfigCore backend problem config cancellationToken
        }

    // ========================================================================
    // CLASSICAL SOLVER (Rule 1: private — not exposed without backend)
    // ========================================================================

    /// Classical greedy clique finder for comparison.
    /// Strategy: start with highest-weight vertex, greedily add vertices
    /// that are connected to all current clique members (prefer highest weight).
    let private solveClassical (problem: Problem) : Solution =
        if problem.Vertices.IsEmpty then
            decodeSolution problem (Array.zeroCreate 0)
            |> fun s ->
                { s with
                    BackendName = "Classical Greedy"
                }
        else
            let vertices = problem.Vertices |> List.toArray
            let n = vertices.Length
            let adjacency = buildAdjacencySet problem

            // Start with the highest-weight vertex
            let startVertex =
                problem.Vertices |> List.indexed |> List.maxBy (fun (_, v) -> v.Weight) |> fst

            // Greedily add vertices connected to all current clique members
            let candidates =
                [ 0 .. n - 1 ]
                |> List.filter (fun i -> i <> startVertex)
                |> List.sortByDescending (fun i -> vertices.[i].Weight)

            let clique =
                candidates
                |> List.fold
                    (fun (acc: Set<int>) candidate ->
                        let connectedToAll =
                            acc |> Set.forall (fun member' -> Set.contains (candidate, member') adjacency)

                        if connectedToAll then acc |> Set.add candidate else acc)
                    (Set.singleton startVertex)

            let bits = Array.init n (fun i -> if clique |> Set.contains i then 1 else 0)

            decodeSolution problem bits
            |> fun s ->
                { s with
                    BackendName = "Classical Greedy"
                }
