namespace FSharp.Azure.Quantum.GroverSearch

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// Tree Search Module for Grover's Algorithm - Unified Backend Implementation
///
/// Enables quantum tree search for game AIs and decision problems.
/// Encodes game tree paths as basis states and uses Grover's algorithm
/// to find optimal moves.
///
/// Key features:
/// - Generic game state representation
/// - Lazy path evaluation (no classical enumeration)
/// - Percentile-based oracle construction
/// - Depth-limited search for NISQ devices
/// - Uses unified Grover.search API (Rule 1 compliant)
///
/// Algorithm Overview:
/// 1. Encode game tree paths as basis states
/// 2. Create lazy oracle that evaluates paths on-the-fly
/// 3. Use Grover's algorithm to amplify best moves
/// 4. Decode quantum results to game moves
///
/// Example:
/// ```fsharp
/// open FSharp.Azure.Quantum.GroverSearch.TreeSearch
/// open FSharp.Azure.Quantum.Backends.LocalBackend
///
/// let backend = LocalBackend() :> IQuantumBackend
///
/// let config = {
///     MaxDepth = 3
///     BranchingFactor = 8
///     EvaluationFunction = fun state -> evaluatePosition state
///     MoveGenerator = fun state -> getLegalMoves state
/// }
///
/// match searchGameTree myGameState config backend 0.2 None None None None with
/// | Ok result -> printfn "Best move: %d" result.BestMove
/// | Error err -> printfn "Error: %A" err
/// ```
module TreeSearch =

    open FSharp.Azure.Quantum.GroverSearch.Oracle
    open FSharp.Azure.Quantum.GroverSearch.Grover

    // ========================================================================
    // TYPES - Tree search configuration and results
    // ========================================================================

    /// Generic game state representation
    type GameState<'T> =
        {
            Position: 'T
            Depth: int
            ParentMove: int option
        }

    /// Tree search configuration
    type TreeSearchConfig<'T> =
        {
            /// Maximum tree depth to explore
            MaxDepth: int

            /// Expected branching factor (for qubit calculation)
            BranchingFactor: int

            /// Heuristic evaluation function (higher = better)
            EvaluationFunction: 'T -> float

            /// Generate legal moves from a position
            MoveGenerator: 'T -> 'T list
        }

    /// Tree search result
    type TreeSearchResult =
        {
            /// Best move to play (index into legal moves list)
            BestMove: int

            /// Score/probability of best move
            Score: float

            /// Number of nodes explored
            NodesExplored: int

            /// Did quantum speedup occur?
            QuantumAdvantage: bool

            /// All solutions found (for debugging)
            AllSolutions: int list

            /// Grover iterations the search ran
            GroverIterations: int
        }

    // ========================================================================
    // STATE ENCODING - Map paths to basis states
    // ========================================================================

    /// Calculate bits needed to represent a value
    let private bitsNeeded (value: int) : int =
        if value <= 0 then
            0
        else
            int (Math.Ceiling(Math.Log(float value, 2.0)))

    /// Encode game tree path to basis state integer
    ///
    /// Encoding scheme:
    /// - Each level uses log2(branching_factor) bits
    /// - Path [m1, m2, m3] encoded as: m1 + (m2 << bits) + (m3 << (2*bits))
    ///
    /// Example: branchingFactor=16 (4 bits), path=[5,12,3]
    ///   Encoded = 5 + (12<<4) + (3<<8) = 5 + 192 + 768 = 965
    let encodeTreePosition (path: int list) (branchingFactor: int) : int =
        let bitsPerLevel = bitsNeeded branchingFactor

        path
        |> List.indexed
        |> List.fold (fun acc (depth, move) -> acc + (move <<< (depth * bitsPerLevel))) 0

    /// Decode basis state integer to game tree path
    let decodeTreePosition (encoded: int) (branchingFactor: int) (maxDepth: int) : int list =
        let bitsPerLevel = bitsNeeded branchingFactor
        let mask = (1 <<< bitsPerLevel) - 1

        List.init (max 0 maxDepth) (fun depth -> (encoded >>> (depth * bitsPerLevel)) &&& mask)

    // ========================================================================
    // PATH EVALUATION
    // ========================================================================

    /// Follow a path from root state
    let private followPath<'T> (rootState: 'T) (path: int list) (moveGenerator: 'T -> 'T list) : 'T option =
        // An out-of-range move index means the path does not exist in this tree: None, no exception.
        path
        |> List.fold
            (fun state moveIdx ->
                match state with
                | None -> None
                | Some s ->
                    let moves = moveGenerator s

                    if moveIdx >= 0 && moveIdx < List.length moves then
                        Some(List.item moveIdx moves)
                    else
                        None)
            (Some rootState)

    // ========================================================================
    // THRESHOLD CALCULATION
    // ========================================================================

    /// Calculate score threshold from top percentile via sampling
    ///
    /// Samples a subset of paths to estimate the score distribution,
    /// then returns the threshold score for the top percentile.
    ///
    /// Parameters:
    /// - rootState: Initial game position
    /// - config: Tree search configuration
    /// - topPercentile: Fraction of best moves (0.0-1.0)
    /// - sampleSize: Number of paths to sample for threshold estimation (default: 100)
    let calculateScoreThreshold<'T>
        (rootState: 'T)
        (config: TreeSearchConfig<'T>)
        (topPercentile: float)
        (sampleSize: int)
        : float =

        try
            // Sample random paths
            let random = Random()
            let searchSpaceSize = 1 <<< (config.MaxDepth * bitsNeeded config.BranchingFactor)
            let samplesToTake = min sampleSize searchSpaceSize

            let scores =
                [ 1..samplesToTake ]
                |> List.choose (fun _ ->
                    let encoded = random.Next searchSpaceSize
                    let path = decodeTreePosition encoded config.BranchingFactor config.MaxDepth

                    match followPath rootState path config.MoveGenerator with
                    | Some leafState -> Some(config.EvaluationFunction leafState)
                    | None -> None)
                |> List.sort

            if List.isEmpty scores then
                0.0 // No valid paths found in sample
            else
                // Return the score at top percentile
                let index = int (float (List.length scores) * (1.0 - topPercentile))
                let clampedIndex = max 0 (min (List.length scores - 1) index)
                scores.[clampedIndex]

        with _ ->
            0.0 // Fallback to 0 threshold

    // ========================================================================
    // TREE SEARCH ORACLE CONSTRUCTION
    // ========================================================================

    /// Create oracle with lazy path evaluation (no classical pre-enumeration)
    ///
    /// NEW STRATEGY:
    /// 1. Create a predicate that decodes basis states to paths on-the-fly
    /// 2. Follow each path from root state (lazy evaluation)
    /// 3. Evaluate leaf state and compare to threshold
    /// 4. Let Grover find the best paths via quantum amplitude amplification
    ///
    /// This provides TRUE quantum speedup - no classical O(N) enumeration!
    ///
    /// Parameters:
    /// - rootState: Initial game position
    /// - config: Tree search configuration
    /// - scoreThreshold: Minimum score for a path to be marked as solution
    /// - maxPaths: Optional limit on search space (None = use full tree up to 2^16)
    let private createTreeSearchOracleLazy<'T>
        (rootState: 'T)
        (config: TreeSearchConfig<'T>)
        (scoreThreshold: float)
        (maxPaths: int option)
        : Result<CompiledOracle, QuantumError> =

        result {
            // Calculate search space
            let numQubits = config.MaxDepth * bitsNeeded config.BranchingFactor

            if numQubits > 16 then
                return!
                    Error(
                        QuantumError.ValidationError(
                            "TreeSearch",
                            $"requires {numQubits} qubits (depth={config.MaxDepth}, branching={config.BranchingFactor}). Max: 16"
                        )
                    )
            else
                let searchSpaceSize = 1 <<< numQubits

                // Apply user-specified search space limit if provided
                let actualSearchSpace =
                    match maxPaths with
                    | Some limit when limit < searchSpaceSize -> limit
                    | _ -> searchSpaceSize

                // Create lazy evaluation predicate
                let predicate (encoded: int) : bool =
                    // Only evaluate within the limited search space
                    if encoded >= actualSearchSpace then
                        false
                    else
                        // Decode to path
                        let path = decodeTreePosition encoded config.BranchingFactor config.MaxDepth

                        // Follow path from root (lazy evaluation - happens during quantum search!)
                        match followPath rootState path config.MoveGenerator with
                        | Some leafState ->
                            // Evaluate this specific path
                            let score = config.EvaluationFunction leafState
                            score >= scoreThreshold
                        | None ->
                            // Invalid path (illegal moves)
                            false

                return! Oracle.fromPredicate predicate numQubits
        }

    // ========================================================================
    // QUANTUM TREE SEARCH EXECUTION
    // ========================================================================

    /// Largest explicit Grover iteration count accepted. The optimum is π/4·√(N/M), about 200
    /// for the widest (16-qubit) search.
    [<Literal>]
    let MaxGroverIterations = 1000

    /// Shots per Grover run when the caller does not set them, on every backend.
    [<Literal>]
    let DefaultShots = 100

    /// Solution threshold (fraction of shots) when the caller does not set one. Grover.search
    /// lowers it to 1/N, since every candidate is verified against the oracle.
    [<Literal>]
    let DefaultSolutionThreshold = 0.05

    /// Execute tree search using Grover's algorithm with LAZY evaluation
    ///
    /// Finds best move in game tree using quantum amplitude amplification.
    /// Uses lazy path evaluation - NO classical pre-enumeration!
    ///
    /// Parameters:
    /// - rootState: Initial game position
    /// - config: Tree search configuration
    /// - backend: IQuantumBackend instance
    /// - topPercentile: Fraction of best moves to mark (default: 0.2 = top 20%)
    /// - numShots: Optional number of measurement shots (None = DefaultShots)
    /// - solutionThreshold: Optional threshold for solution detection (None = DefaultSolutionThreshold)
    /// - successThreshold: Optional threshold for success probability (None = Grover.defaultConfig)
    /// - maxPaths: Optional limit on search space size (None = use full tree up to 2^16)
    /// - groverIterations: Grover iterations to run (None = Grover's unknown-M schedule, from 1);
    ///   Some k outside 1..MaxGroverIterations is an Error
    ///
    /// Returns: TreeSearchResult with best move
    let searchGameTreeWithIterations<'T>
        (rootState: 'T)
        (config: TreeSearchConfig<'T>)
        (backend: IQuantumBackend)
        (topPercentile: float)
        (numShots: int option)
        (solutionThreshold: float option)
        (successThreshold: float option)
        (maxPaths: int option)
        (groverIterations: int option)
        : Result<TreeSearchResult, QuantumError> =

        result {
            do!
                match groverIterations with
                | Some k when k < 1 ->
                    Error(QuantumError.ValidationError("MaxIterations", $"must be at least 1, got {k}"))
                | Some k when k > MaxGroverIterations ->
                    Error(
                        QuantumError.ValidationError(
                            "MaxIterations",
                            $"must be at most {MaxGroverIterations}, got {k} (the optimum for a 16-qubit search is about 200)"
                        )
                    )
                | _ -> Ok()

            // Step 1: Calculate score threshold from top percentile
            let sampledThreshold = calculateScoreThreshold rootState config topPercentile 100

            // Mark exactly the sampled top percentile. Lowering the bar below it marks most of
            // the tree, and once more than half the search space is marked a Grover iteration
            // lowers the probability of a marked path below uniform sampling.
            let scoreThreshold =
                if sampledThreshold = 0.0 then
                    // Fallback: use negative infinity to accept any valid path
                    Double.NegativeInfinity
                else
                    sampledThreshold

            // Step 2: Create LAZY tree search oracle (no classical enumeration!)
            let! oracle = createTreeSearchOracleLazy rootState config scoreThreshold maxPaths

            // Step 3: Calculate search space size (respecting maxPaths limit)
            let fullSearchSpace = 1 <<< oracle.NumQubits

            let searchSpaceSize =
                match maxPaths with
                | Some limit -> min limit fullSearchSpace
                | None -> fullSearchSpace

            // Step 4: Execute Grover search. The number of marked paths is not known: the
            // threshold comes from a sample, ties at it are all marked, and illegal paths are
            // not. So unless the caller fixes the count, Grover's unknown-M schedule chooses
            // it, starting at one iteration and stopping at the first verified solution.
            let groverConfig =
                { Grover.defaultConfig with
                    Iterations = groverIterations
                    Shots = numShots |> Option.defaultValue DefaultShots
                    SolutionThreshold = solutionThreshold |> Option.defaultValue DefaultSolutionThreshold
                    SuccessThreshold = successThreshold |> Option.defaultValue Grover.defaultConfig.SuccessThreshold
                }

            let! searchResult = Grover.search oracle backend groverConfig

            // Step 5: Decode best move from result
            let! bestEncoded =
                match List.tryHead searchResult.Solutions with
                | None -> Error(QuantumError.OperationError("TreeSearch", "no solution found"))
                | Some value -> Ok value

            let decodedPath =
                decodeTreePosition bestEncoded config.BranchingFactor config.MaxDepth

            // Extract first move (root level)
            let firstMove = List.tryHead decodedPath |> Option.defaultValue 0

            // Calculate quantum advantage
            let classicalComplexity = searchSpaceSize
            let quantumComplexity = searchResult.Iterations
            let quantumAdvantage = quantumComplexity < classicalComplexity

            return
                {
                    BestMove = firstMove
                    Score = searchResult.SuccessProbability
                    NodesExplored = searchSpaceSize
                    QuantumAdvantage = quantumAdvantage
                    AllSolutions = searchResult.Solutions
                    GroverIterations = searchResult.Iterations
                }
        }

    /// Execute tree search using Grover's algorithm with the optimal iteration count.
    /// Same as searchGameTreeWithIterations with groverIterations = None.
    let searchGameTree<'T>
        (rootState: 'T)
        (config: TreeSearchConfig<'T>)
        (backend: IQuantumBackend)
        (topPercentile: float)
        (numShots: int option)
        (solutionThreshold: float option)
        (successThreshold: float option)
        (maxPaths: int option)
        : Result<TreeSearchResult, QuantumError> =
        searchGameTreeWithIterations
            rootState
            config
            backend
            topPercentile
            numShots
            solutionThreshold
            successThreshold
            maxPaths
            None

    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================

    /// Search with default top percentile (20%) and default shots and thresholds
    let searchGameTreeDefault<'T>
        (rootState: 'T)
        (config: TreeSearchConfig<'T>)
        (backend: IQuantumBackend)
        : Result<TreeSearchResult, QuantumError> =
        searchGameTree rootState config backend 0.2 None None None None

    /// Estimate qubits needed for tree search
    let estimateQubitsNeeded (maxDepth: int) (branchingFactor: int) : int = maxDepth * bitsNeeded branchingFactor

    /// Estimate search space size
    let estimateSearchSpaceSize (maxDepth: int) (branchingFactor: int) : int =
        let qubits = estimateQubitsNeeded maxDepth branchingFactor
        1 <<< qubits

    /// Helper: Calculate maximum paths to limit search space explosion
    ///
    /// Recommends a reasonable maxPaths limit based on depth and branching.
    /// Use this to prevent exponential blowup for large trees.
    ///
    /// Strategy:
    /// - Small trees (≤64 paths): No limit needed
    /// - Medium trees (≤1024 paths): Use full space
    /// - Large trees (>1024 paths): Limit to 1024-4096 based on qubits
    let recommendMaxPaths (maxDepth: int) (branchingFactor: int) : int option =
        let fullSpace = estimateSearchSpaceSize maxDepth branchingFactor

        if fullSpace <= 64 then None // Small enough, no limit needed
        elif fullSpace <= 1024 then None // Medium, manageable
        elif fullSpace <= 4096 then Some 1024 // Large: limit to 1024 paths (10 qubits)
        elif fullSpace <= 16384 then Some 2048 // Very large: limit to 2048 paths (11 qubits)
        else Some 4096 // Huge: limit to 4096 paths (12 qubits)
