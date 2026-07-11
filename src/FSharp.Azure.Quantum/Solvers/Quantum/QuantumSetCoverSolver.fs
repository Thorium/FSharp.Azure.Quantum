namespace FSharp.Azure.Quantum.Quantum

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers

/// Quantum Minimum Set Cover Solver
///
/// Problem: Given universe U = {1,...,m} and collection S = {S_1,...,S_n}
/// of subsets with costs, find minimum-cost subsets whose union equals U.
///
/// QUBO Formulation (inequality encoding with binary slack bits):
///   Variables: x_j in {0,1} per subset (1 = selected), plus slack bits z_{e,t}
///   Objective: Minimize Sum_j c_j * x_j
///   Constraint: Each element e must be covered by AT LEAST one subset.
///     For each element e, let T_e = {j : e in S_j} with m = |T_e|.
///     The inequality Sum_{j in T_e} x_j >= 1 is rewritten as the equality
///       Sum_{j in T_e} x_j - 1 = s_e,   s_e in [0, m-1]
///     with s_e = Sum_t 2^t * z_{e,t} over ceil(log2 m) binary slack bits
///     (the same slack-bit pattern as QuantumBinaryILPSolver), and penalised as
///       lambda * (Sum_{j in T_e} x_j - Sum_t 2^t * z_{e,t} - 1)^2
///     This is 0 for ANY coverage count in 1..m — unlike the exact-one penalty
///     lambda*(1 - Sum x_j)^2, which punished double coverage like non-coverage
///     and made overlapping covers lose to infeasible selections.
///     For m = 1 no slack bit is needed (the constraint degenerates to x_j = 1).
///
/// Qubits: n (one per subset) + Sum_e ceil(log2 |T_e|) slack bits
///
/// RULE 1 COMPLIANCE:
/// All public solve functions require IQuantumBackend parameter.
/// Classical solver is private.
module QuantumSetCoverSolver =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// A subset in the collection
    type Subset = {
        Id: string
        /// Elements contained in this subset (indices into the universe)
        Elements: int list
        /// Cost of selecting this subset; default 1.0
        Cost: float
    }

    /// Set cover problem definition
    type Problem = {
        /// Number of elements in the universe (U = {0, 1, ..., UniverseSize-1})
        UniverseSize: int
        /// Collection of subsets
        Subsets: Subset list
    }

    /// Set cover solution
    type Solution = {
        /// Subsets selected in the cover
        SelectedSubsets: Subset list
        /// Total cost of the selected subsets
        TotalCost: float
        /// Number of subsets in the cover
        CoverSize: int
        /// Whether every element in U is covered
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
    // COVERAGE MAP & SLACK LAYOUT
    // ========================================================================

    /// Build a map from element -> list of subset indices that contain it.
    let private buildCoverageMap (problem: Problem) : Map<int, int list> =
        problem.Subsets
        |> List.indexed
        |> List.collect (fun (j, subset) ->
            subset.Elements
            |> List.map (fun e -> (e, j)))
        |> List.groupBy fst
        |> List.map (fun (e, pairs) -> (e, pairs |> List.map snd))
        |> Map.ofList

    /// Number of binary slack bits for an element covered by m candidate subsets.
    /// The coverage inequality Sum_{j in T_e} x_j >= 1 becomes the equality
    /// Sum x_j - 1 = s_e with slack s_e in [0, m-1], needing ceil(log2 m) bits
    /// (0 bits when m <= 1: with a single candidate the constraint is x_j = 1).
    /// Integer bit counting mirrors QuantumBinaryILPSolver.slackBitsForBound.
    let private slackBitsForCoverage (m: int) : int =
        if m <= 1 then 0
        else
            let bound = m - 1
            let rec countBits value bits =
                if value <= 0 then bits
                else countBits (value >>> 1) (bits + 1)
            countBits bound 0

    /// Covered elements in deterministic (ascending) order with their covering
    /// subset indices — the layout order for slack variables after the n
    /// subset-selection variables.
    let private coverageEntries (problem: Problem) : (int * int list) list =
        let coverageMap = buildCoverageMap problem
        [ 0 .. problem.UniverseSize - 1 ]
        |> List.choose (fun e ->
            coverageMap
            |> Map.tryFind e
            |> Option.map (fun subsetIndices -> (e, subsetIndices)))

    // ========================================================================
    // QUBIT ESTIMATION (Decision 11)
    // ========================================================================

    /// Estimate the number of qubits required for a set cover problem.
    /// One qubit per subset plus ceil(log2 |T_e|) coverage slack bits per element.
    let estimateQubits (problem: Problem) : int =
        problem.Subsets.Length
        + (coverageEntries problem
           |> List.sumBy (fun (_, subsetIndices) -> slackBitsForCoverage subsetIndices.Length))

    // ========================================================================
    // QUBO CONSTRUCTION (Decision 9: sparse internally, Decision 5: dense output)
    // ========================================================================

    /// Build the QUBO as a sparse map.
    ///
    /// Objective: minimize Sum_j c_j * x_j
    ///   Diagonal Q[j,j] += c_j
    ///
    /// Coverage constraint per element e (see module header):
    ///   Penalty: lambda * (Sum_{j in T_e} x_j - Sum_t 2^t * z_{e,t} - 1)^2
    ///   With coefficient a_v (+1 for x_j, -2^t for z_{e,t}) and constant -1,
    ///   the expansion (using v^2 = v for binary v) gives, dropping the constant:
    ///     Diagonal:     Q[v,v] += lambda * (a_v^2 - 2*a_v)
    ///     Off-diagonal: Q[u,v] += lambda * a_u * a_v  (symmetric split, both orders)
    ///   For x_j this reproduces the previous -lambda diagonal and +lambda
    ///   symmetric pair terms; the slack terms make any coverage count in
    ///   1..|T_e| penalty-free instead of only exactly-one.
    let private buildQuboMap (problem: Problem) : Map<int * int, float> =
        let n = problem.Subsets.Length
        let entries = coverageEntries problem

        // Penalty must dominate objective. Max objective = sum of all costs.
        let totalCost = problem.Subsets |> List.sumBy (fun s -> abs s.Cost)
        let penalty = totalCost + 1.0

        // Objective terms: c_j on diagonal
        let objectiveTerms =
            problem.Subsets
            |> List.indexed
            |> List.map (fun (j, subset) -> ((j, j), subset.Cost))

        // Coverage constraint terms per element, laying out each element's slack
        // bits consecutively after the n decision variables.
        let (_, constraintTerms) =
            ((n, []), entries)
            ||> List.fold (fun (slackStart, acc) (_, subsetIndices) ->
                let numSlackBits = slackBitsForCoverage subsetIndices.Length

                // Unified coefficient vector for (Sum x_j - Sum 2^t z_t - 1)^2
                let coeffs =
                    (subsetIndices |> List.map (fun j -> (j, 1.0)))
                    @ [ for t in 0 .. numSlackBits - 1 -> (slackStart + t, -(pown 2.0 t)) ]

                // Diagonal: lambda * (a^2 - 2*a)  (the -2a comes from the -1 constant)
                let diagonal =
                    coeffs
                    |> List.map (fun (v, a) -> ((v, v), penalty * (a * a - 2.0 * a)))

                // Off-diagonal: 2 * lambda * a_u * a_v, symmetric split across (u,v) and (v,u)
                let offDiagonal =
                    [ for (u, au) in coeffs do
                        for (v, av) in coeffs do
                            if u < v then
                                yield ((u, v), penalty * au * av)
                                yield ((v, u), penalty * au * av) ]

                (slackStart + numSlackBits, acc @ diagonal @ offDiagonal))

        (objectiveTerms @ constraintTerms)
        |> List.fold (fun acc (key, value) -> Qubo.combineTerms key value acc) Map.empty

    /// Convert problem to dense QUBO matrix (decision variables + coverage slack bits).
    /// Returns Result to follow the canonical pattern (validates inputs).
    let toQubo (problem: Problem) : Result<float[,], QuantumError> =
        if problem.Subsets.IsEmpty then
            Error (QuantumError.ValidationError ("subsets", "Problem has no subsets"))
        elif problem.UniverseSize <= 0 then
            Error (QuantumError.ValidationError ("universeSize", "Universe size must be positive"))
        else
            let numVars = estimateQubits problem
            let quboMap = buildQuboMap problem
            Ok (Qubo.toDenseArray numVars quboMap)

    // ========================================================================
    // SOLUTION DECODING & VALIDATION
    // ========================================================================

    /// Compute the set of elements covered by a bitstring selection.
    let private coveredElements (problem: Problem) (bits: int[]) : Set<int> =
        problem.Subsets
        |> List.indexed
        |> List.collect (fun (j, subset) ->
            if bits.[j] = 1 then subset.Elements else [])
        |> Set.ofList

    /// Check whether a bitstring represents a valid set cover:
    /// every element in the universe must be covered by at least one selected subset.
    /// Also validates bitstring length matches subset count.
    let isValid (problem: Problem) (bits: int[]) : bool =
        bits.Length = problem.Subsets.Length
        && (
            let covered = coveredElements problem bits
            [ 0 .. problem.UniverseSize - 1 ]
            |> List.forall (fun e -> covered |> Set.contains e))

    /// Decode a bitstring into a Solution.
    let private decodeSolution (problem: Problem) (bits: int[]) : Solution =
        let selected =
            problem.Subsets
            |> List.indexed
            |> List.choose (fun (j, subset) -> if bits.[j] = 1 then Some subset else None)

        {
            SelectedSubsets = selected
            TotalCost = selected |> List.sumBy (fun s -> s.Cost)
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

    /// Repair an infeasible solution by greedily adding cheapest subsets
    /// that cover uncovered elements, then removing redundant subsets.
    let private repairConstraints (problem: Problem) (bits: int[]) : int[] =
        let universe = Set.ofList [ 0 .. problem.UniverseSize - 1 ]

        // Phase 1: greedily add cheapest subset covering uncovered elements
        let rec addCoverage (current: int[]) =
            let covered = coveredElements problem current
            let uncovered = Set.difference universe covered
            if Set.isEmpty uncovered then
                current
            else
                // Find the unselected subset that covers the most uncovered
                // elements per unit cost (best cost-effectiveness)
                let bestSubset =
                    problem.Subsets
                    |> List.indexed
                    |> List.filter (fun (j, _) -> current.[j] = 0)
                    |> List.choose (fun (j, subset) ->
                        let newlyCovered =
                            subset.Elements
                            |> List.filter (fun e -> uncovered |> Set.contains e)
                            |> List.length
                        if newlyCovered > 0 then
                            let effectiveness =
                                if subset.Cost <= 0.0 then infinity
                                else float newlyCovered / subset.Cost
                            Some (j, effectiveness)
                        else
                            None)
                    |> List.sortByDescending snd

                match bestSubset with
                | [] -> current  // No subset can cover remaining (impossible if well-formed)
                | (j, _) :: _ ->
                    let updated = Array.copy current
                    updated.[j] <- 1
                    addCoverage updated

        // Phase 2: recursively remove redundant subsets (costliest first)
        let rec tryRemove (current: int[]) (candidates: (int * Subset) list) =
            match candidates with
            | [] -> current
            | (j, _) :: rest ->
                let tentative = Array.copy current
                tentative.[j] <- 0
                if isValid problem tentative then
                    tryRemove tentative rest
                else
                    tryRemove current rest

        let afterAdd = addCoverage (Array.copy bits)

        let sortedSelected =
            problem.Subsets
            |> List.indexed
            |> List.filter (fun (j, _) -> afterAdd.[j] = 1)
            |> List.sortByDescending (fun (_, s) -> s.Cost)

        tryRemove afterAdd sortedSelected

    // ========================================================================
    // DECOMPOSE / RECOMBINE HOOKS (Decision 10: identity stubs)
    // ========================================================================

    /// Decompose a set cover problem into sub-problems.
    /// Currently identity — set cover lacks natural graph structure for splitting.
    /// Future: partition by independent element groups (non-overlapping subsets).
    let decompose (problem: Problem) : Problem list = [ problem ]

    /// Recombine sub-solutions into a single solution. Currently identity (single solution).
    /// Handles empty list gracefully.
    let recombine (solutions: Solution list) : Solution =
        match solutions with
        | [] ->
            {
                SelectedSubsets = []
                TotalCost = 0.0
                CoverSize = 0
                IsValid = false
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
            }
        | [ single ] -> single
        | _ -> solutions |> List.minBy (fun s -> s.TotalCost)

    // ========================================================================
    // QUANTUM SOLVERS (Rule 1: IQuantumBackend required)
    // ========================================================================

    /// Solve set cover using QAOA with full configuration control.
    /// Supports automatic decomposition when problem exceeds backend capacity.
    [<Obsolete("Use solveWithConfigAsync for non-blocking execution against cloud backends")>]
    let solveWithConfig
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (config: Config)
        : Result<Solution, QuantumError> =

        if problem.Subsets.IsEmpty then
            Error (QuantumError.ValidationError ("subsets", "Problem has no subsets"))
        elif problem.UniverseSize <= 0 then
            Error (QuantumError.ValidationError ("universeSize", "Universe size must be positive"))
        elif problem.Subsets |> List.exists (fun s ->
                s.Elements |> List.exists (fun e -> e < 0 || e >= problem.UniverseSize)) then
            Error (QuantumError.ValidationError ("elements", "Element index out of range"))
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
                        // Keep only the subset-selection bits: the trailing coverage
                        // slack bits encode the >=1 inequality inside the QUBO and
                        // carry no solution content.
                        let numSubsets = subProblem.Subsets.Length
                        let decisionBits =
                            if bits.Length > numSubsets then bits.[0 .. numSubsets - 1] else bits

                        let finalBits, wasRepaired =
                            if config.EnableConstraintRepair && not (isValid subProblem decisionBits) then
                                (repairConstraints subProblem decisionBits, true)
                            else
                                (decisionBits, false)

                        let solution = decodeSolution subProblem finalBits
                        Ok { solution with
                                BackendName = backend.Name
                                NumShots = config.FinalShots
                                WasRepaired = wasRepaired
                                OptimizedParameters = optParams
                                OptimizationConverged = converged }

            ProblemDecomposition.solveWithDecomposition
                backend problem estimateQubits decompose recombine solveSingle

    /// Solve set cover using QAOA with full configuration control (async).
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

    /// Solve set cover using QAOA with default configuration.
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

    /// Classical greedy set cover for comparison.
    /// Strategy: repeatedly select the subset with best cost-effectiveness
    /// (most uncovered elements per unit cost) until all elements are covered.
    /// This is a ln(n)-approximation.
    let private solveClassical (problem: Problem) : Solution =
        if problem.Subsets.IsEmpty || problem.UniverseSize <= 0 then
            decodeSolution problem (Array.zeroCreate (max 0 problem.Subsets.Length))
            |> fun s -> { s with BackendName = "Classical Greedy" }
        else
            let universe = Set.ofList [ 0 .. problem.UniverseSize - 1 ]
            let n = problem.Subsets.Length

            let rec greedyCover (selected: Set<int>) (covered: Set<int>) =
                let uncovered = Set.difference universe covered
                if Set.isEmpty uncovered then
                    selected
                else
                    let bestCandidate =
                        problem.Subsets
                        |> List.indexed
                        |> List.filter (fun (j, _) -> not (selected |> Set.contains j))
                        |> List.choose (fun (j, subset) ->
                            let newlyCovered =
                                subset.Elements
                                |> List.filter (fun e -> uncovered |> Set.contains e)
                                |> List.length
                            if newlyCovered > 0 then
                                let effectiveness =
                                    if subset.Cost <= 0.0 then infinity
                                    else float newlyCovered / subset.Cost
                                Some (j, subset, effectiveness)
                            else
                                None)
                        |> List.sortByDescending (fun (_, _, effectiveness) -> effectiveness)

                    match bestCandidate with
                    | [] -> selected  // Cannot cover remaining elements
                    | (j, subset, _) :: _ ->
                        let newCovered =
                            subset.Elements
                            |> List.fold (fun acc e -> acc |> Set.add e) covered
                        greedyCover (selected |> Set.add j) newCovered

            let selectedSet = greedyCover Set.empty Set.empty
            let bits = Array.init n (fun j -> if selectedSet |> Set.contains j then 1 else 0)
            decodeSolution problem bits
            |> fun s -> { s with BackendName = "Classical Greedy" }
