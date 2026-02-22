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
/// QUBO Formulation:
///   Variables: x_j in {0,1} per subset (1 = selected)
///   Objective: Minimize Sum_j c_j * x_j
///   Constraint: Each element e must be covered by at least one subset.
///     For each element e, let T_e = {j : e in S_j}
///     Penalty: lambda_e * (1 - Sum_{j in T_e} x_j)^2
///            = lambda_e * (1 - 2*Sum x_j + (Sum x_j)^2)
///     Expands to:
///       qubo[j,j] -= lambda_e  for each j in T_e
///       qubo[j,k] += 2*lambda_e  for each pair j < k in T_e
///     (Constant term lambda_e is ignored.)
///
/// Qubits: n (one per subset, NOT per element)
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
    // QUBIT ESTIMATION (Decision 11)
    // ========================================================================

    /// Estimate the number of qubits required for a set cover problem.
    /// One qubit per subset.
    let estimateQubits (problem: Problem) : int =
        problem.Subsets.Length

    // ========================================================================
    // QUBO CONSTRUCTION (Decision 9: sparse internally, Decision 5: dense output)
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

    /// Build the QUBO as a sparse map.
    ///
    /// Objective: minimize Sum_j c_j * x_j
    ///   Diagonal Q[j,j] += c_j
    ///
    /// Coverage constraint per element e:
    ///   Penalty: lambda * (1 - Sum_{j in T_e} x_j)^2
    ///   Expands to (dropping constant):
    ///     Q[j,j] -= lambda  for each j in T_e (linear from -2*Sum x_j)
    ///     Q[j,k] += 2*lambda  for each pair j<k in T_e (from (Sum x_j)^2)
    ///   Symmetric split: Q[j,k] += lambda, Q[k,j] += lambda
    let private buildQuboMap (problem: Problem) : Map<int * int, float> =
        let coverageMap = buildCoverageMap problem

        // Penalty must dominate objective. Max objective = sum of all costs.
        let totalCost = problem.Subsets |> List.sumBy (fun s -> abs s.Cost)
        let penalty = totalCost + 1.0

        // Objective terms: c_j on diagonal
        let objectiveTerms =
            problem.Subsets
            |> List.indexed
            |> List.map (fun (j, subset) -> ((j, j), subset.Cost))

        // Coverage constraint terms per element
        let constraintTerms =
            [ 0 .. problem.UniverseSize - 1 ]
            |> List.collect (fun e ->
                match coverageMap |> Map.tryFind e with
                | None -> []  // Element not in any subset (will be uncoverable)
                | Some subsetIndices ->
                    // Linear: Q[j,j] -= lambda for each j in T_e
                    let linear =
                        subsetIndices
                        |> List.map (fun j -> ((j, j), -penalty))

                    // Quadratic: Q[j,k] += lambda (symmetric) for each pair j<k in T_e
                    let quadratic =
                        subsetIndices
                        |> List.collect (fun j ->
                            subsetIndices
                            |> List.filter (fun k -> k > j)
                            |> List.collect (fun k ->
                                [ ((j, k), penalty)
                                  ((k, j), penalty) ]))

                    linear @ quadratic)

        (objectiveTerms @ constraintTerms)
        |> List.fold (fun acc (key, value) -> Qubo.combineTerms key value acc) Map.empty

    /// Convert problem to dense QUBO matrix.
    /// Returns Result to follow the canonical pattern (validates inputs).
    let toQubo (problem: Problem) : Result<float[,], QuantumError> =
        if problem.Subsets.IsEmpty then
            Error (QuantumError.ValidationError ("subsets", "Problem has no subsets"))
        elif problem.UniverseSize <= 0 then
            Error (QuantumError.ValidationError ("universeSize", "Universe size must be positive"))
        else
            let n = problem.Subsets.Length
            let quboMap = buildQuboMap problem
            Ok (Qubo.toDenseArray n quboMap)

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
