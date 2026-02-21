namespace FSharp.Azure.Quantum.Core

open FSharp.Azure.Quantum

/// Generic problem decomposition orchestrator for QAOA solvers.
///
/// Provides backend-aware decomposition: when a problem requires more qubits
/// than the backend supports (IQubitLimitedBackend.MaxQubits), the problem is
/// automatically split into sub-problems, solved independently, and recombined.
///
/// Design:
/// - Fully generic over problem/solution types (no solver-specific knowledge).
/// - Solvers supply decompose/recombine/estimateQubits/solve functions.
/// - Graph helpers (connected components) provided for graph-based solvers.
///
/// Usage from a solver's solveWithConfig:
///   ProblemDecomposition.solveWithDecomposition
///       backend problem config estimateQubits decompose recombine solveOne
module ProblemDecomposition =

    // ========================================================================
    // DECOMPOSITION STRATEGY
    // ========================================================================

    /// Strategy for decomposing a problem when it exceeds backend capacity.
    type DecompositionStrategy =
        /// Run problem as-is (no decomposition).
        | NoDecomposition
        /// Decompose into fixed-size partitions of at most N qubits each.
        | FixedPartition of maxQubitsPerPartition: int
        /// Automatically decompose based on backend's MaxQubits capacity.
        | AdaptiveToBackend

    /// Result of the decomposition planning step.
    type DecompositionPlan<'Problem> =
        /// Problem fits within backend capacity — run directly.
        | RunDirect of 'Problem
        /// Problem exceeds capacity — run decomposed sub-problems.
        | RunDecomposed of 'Problem list

    // ========================================================================
    // PLANNING
    // ========================================================================

    /// Plan whether to decompose a problem based on strategy and backend capacity.
    ///
    /// Parameters:
    ///   strategy       - decomposition strategy to use
    ///   backend        - quantum backend (checked for IQubitLimitedBackend)
    ///   estimateQubits - function to estimate qubits for a problem
    ///   decomposeFn    - function to split a problem into sub-problems
    ///   problem        - the problem to plan for
    ///
    /// Returns a DecompositionPlan indicating whether to run directly or decomposed.
    let plan
        (strategy: DecompositionStrategy)
        (backend: BackendAbstraction.IQuantumBackend)
        (estimateQubits: 'Problem -> int)
        (decomposeFn: 'Problem -> 'Problem list)
        (problem: 'Problem)
        : DecompositionPlan<'Problem> =

        match strategy with
        | NoDecomposition ->
            RunDirect problem

        | FixedPartition maxQubits ->
            let qubitsNeeded = estimateQubits problem
            if qubitsNeeded <= maxQubits then
                RunDirect problem
            else
                let subProblems = decomposeFn problem
                // If decomposition returned a single sub-problem identical to the
                // original (solver doesn't support decomposition yet), run directly.
                match subProblems with
                | [ _ ] -> RunDirect problem
                | subs -> RunDecomposed subs

        | AdaptiveToBackend ->
            let maxQubits = BackendAbstraction.UnifiedBackend.getMaxQubits backend
            match maxQubits with
            | None ->
                // Backend doesn't report capacity — run directly.
                RunDirect problem
            | Some limit ->
                let qubitsNeeded = estimateQubits problem
                if qubitsNeeded <= limit then
                    RunDirect problem
                else
                    let subProblems = decomposeFn problem
                    match subProblems with
                    | [ _ ] -> RunDirect problem
                    | subs -> RunDecomposed subs

    // ========================================================================
    // EXECUTION
    // ========================================================================

    /// Execute a decomposition plan by solving sub-problems and recombining.
    ///
    /// Parameters:
    ///   solveFn     - function to solve a single (sub-)problem
    ///   recombineFn - function to merge sub-solutions into one
    ///   plan        - the decomposition plan (RunDirect or RunDecomposed)
    ///
    /// Returns Ok solution or Error if any sub-problem fails.
    let execute
        (solveFn: 'Problem -> Result<'Solution, QuantumError>)
        (recombineFn: 'Solution list -> 'Solution)
        (plan: DecompositionPlan<'Problem>)
        : Result<'Solution, QuantumError> =

        match plan with
        | RunDirect problem ->
            solveFn problem

        | RunDecomposed subProblems ->
            // Solve each sub-problem, collecting results.
            // Short-circuit on first error.
            let results =
                subProblems
                |> List.fold (fun acc subProblem ->
                    match acc with
                    | Error _ -> acc
                    | Ok solutions ->
                        match solveFn subProblem with
                        | Error err -> Error err
                        | Ok solution -> Ok (solution :: solutions)
                ) (Ok [])

            results
            |> Result.map (fun solutions ->
                solutions
                |> List.rev  // Restore original order
                |> recombineFn)

    // ========================================================================
    // COMBINED PLAN + EXECUTE CONVENIENCE
    // ========================================================================

    /// Plan and execute decomposition in one call.
    ///
    /// This is the primary entry point for solvers. It checks backend capacity,
    /// decomposes if necessary, solves sub-problems, and recombines results.
    ///
    /// Parameters:
    ///   backend        - quantum backend
    ///   problem        - the problem to solve
    ///   estimateQubits - qubit estimation function
    ///   decomposeFn    - problem decomposition function
    ///   recombineFn    - solution recombination function
    ///   solveFn        - single-problem solver function
    ///
    /// Returns Ok solution or Error.
    let solveWithDecomposition
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: 'Problem)
        (estimateQubits: 'Problem -> int)
        (decomposeFn: 'Problem -> 'Problem list)
        (recombineFn: 'Solution list -> 'Solution)
        (solveFn: 'Problem -> Result<'Solution, QuantumError>)
        : Result<'Solution, QuantumError> =

        let decompositionPlan =
            plan AdaptiveToBackend backend estimateQubits decomposeFn problem

        execute solveFn recombineFn decompositionPlan

    // ========================================================================
    // GRAPH DECOMPOSITION HELPERS
    // ========================================================================

    /// Find connected components of an undirected graph using union-find.
    ///
    /// Parameters:
    ///   numVertices - total number of vertices (0-indexed)
    ///   edges       - undirected edges as (int * int) pairs
    ///
    /// Returns list of vertex groups (each group is a list of vertex indices).
    /// Singleton components (isolated vertices) are included.
    let connectedComponents (numVertices: int) (edges: (int * int) list) : int list list =
        if numVertices = 0 then []
        else
            // Union-Find with path compression
            let parent = Array.init numVertices id
            let rank = Array.create numVertices 0

            let rec find x =
                if parent.[x] <> x then
                    parent.[x] <- find parent.[x]  // Path compression
                parent.[x]

            let union a b =
                let ra = find a
                let rb = find b
                if ra <> rb then
                    if rank.[ra] < rank.[rb] then parent.[ra] <- rb
                    elif rank.[ra] > rank.[rb] then parent.[rb] <- ra
                    else parent.[rb] <- ra; rank.[ra] <- rank.[ra] + 1

            // Process all edges
            edges |> List.iter (fun (i, j) ->
                if i >= 0 && i < numVertices && j >= 0 && j < numVertices then
                    union i j)

            // Group vertices by their root
            [0 .. numVertices - 1]
            |> List.groupBy find
            |> List.map snd

    /// Partition a graph problem (vertices + edges) into sub-problems by connected
    /// components. Returns sub-problems with local vertex indices (0-based) and
    /// a mapping from global to (component-index, local-index).
    ///
    /// Parameters:
    ///   numVertices - total vertex count
    ///   edges       - undirected edges (global indices)
    ///
    /// Returns:
    ///   List of (localVertices: int list, localEdges: (int*int) list) per component.
    ///   localVertices contains the original global indices.
    ///   localEdges use 0-based indices within the component.
    let partitionByComponents
        (numVertices: int)
        (edges: (int * int) list)
        : (int list * (int * int) list) list =

        let components = connectedComponents numVertices edges

        components
        |> List.map (fun componentVertices ->
            // Build global-to-local index mapping for this component
            let globalToLocal =
                componentVertices
                |> List.mapi (fun localIdx globalIdx -> (globalIdx, localIdx))
                |> Map.ofList

            // Filter and re-index edges for this component
            let localEdges =
                edges
                |> List.choose (fun (i, j) ->
                    match Map.tryFind i globalToLocal, Map.tryFind j globalToLocal with
                    | Some li, Some lj -> Some (li, lj)
                    | _ -> None)

            (componentVertices, localEdges))

    /// Check whether decomposing a graph by connected components would produce
    /// sub-problems that each fit within a qubit limit.
    ///
    /// Parameters:
    ///   numVertices       - total vertex count
    ///   edges             - undirected edges
    ///   maxQubitsPerPart  - maximum qubits per sub-problem
    ///   qubitsPerVertex   - number of qubits per vertex (typically 1)
    ///
    /// Returns true if all components fit within the limit.
    let canDecomposeWithinLimit
        (numVertices: int)
        (edges: (int * int) list)
        (maxQubitsPerPart: int)
        (qubitsPerVertex: int)
        : bool =

        let components = connectedComponents numVertices edges
        components
        |> List.forall (fun comp -> comp.Length * qubitsPerVertex <= maxQubitsPerPart)
