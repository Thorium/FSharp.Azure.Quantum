namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Core

/// High-level Quantum Tree Search Builder - Quantum-First API
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for users who want to solve game tree search and decision problems
/// without understanding Grover's algorithm internals (oracles, qubits, amplitude amplification).
/// 
/// QUANTUM-FIRST:
/// - Uses Grover's algorithm via quantum backends by default (LocalBackend for simulation)
/// - Optional backend parameter for cloud quantum hardware (IonQ, Rigetti)
/// - For algorithm-level control, use GroverSearch.TreeSearch module directly
/// 
/// WHAT IS QUANTUM TREE SEARCH:
/// Find the best move in a game tree by exploring possible move sequences (paths),
/// using quantum amplitude amplification to accelerate the search. Instead of evaluating
/// all paths classically, Grover's algorithm finds promising paths quadratically faster.
/// 
/// USE CASES:
/// - Game AI: Chess, Gomoku, Tic-Tac-Toe move selection
/// - Decision Trees: Multi-step optimization with branching choices
/// - Path Planning: Robot navigation, logistics optimization
/// - Monte Carlo Tree Search acceleration
/// 
/// EXAMPLE USAGE:
///   // Simple: Define Gomoku AI search
///   let problem = quantumTreeSearch {
///       initialState myGameBoard
///       maxDepth 3
///       branchingFactor 16
///       evaluateWith (fun board -> evaluatePosition board)
///       generateMovesWith (fun board -> getLegalMoves board)
///   }
///   
///   // Advanced: Custom backend and top percentile
///   let problem = quantumTreeSearch {
///       initialState myGameBoard
///       maxDepth 4
///       branchingFactor 8
///       evaluateWith evalFunc
///       generateMovesWith moveGen
///       topPercentile 0.15
///       backend ionqBackend
///   }
///   
///   // Solve the problem
///   match QuantumTreeSearch.solve problem with
///   | Ok solution -> printfn "Best move: %d" solution.BestMove
///   | Error msg -> printfn "Error: %s" msg
module QuantumTreeSearch =
    
    // ============================================================================
    // CORE TYPES - Tree Search Domain Model
    // ============================================================================
    
    /// <summary>
    /// Complete quantum tree search problem specification.
    /// </summary>
    type TreeSearchProblem<'T> = {
        /// Initial game/decision state to search from (None if not yet set)
        InitialState: 'T option
        /// Maximum depth to explore in the tree
        MaxDepth: int
        /// Expected branching factor (moves per position)
        BranchingFactor: int
        /// Heuristic evaluation function (higher score = better position)
        EvaluationFunction: 'T -> float
        /// Move generation function (returns list of next states)
        MoveGenerator: 'T -> 'T list
        /// Fraction of best moves to amplify (0.0 < x <= 1.0)
        TopPercentile: float
        /// Quantum backend to use (None = LocalBackend)
        Backend: BackendAbstraction.IQuantumBackend option
        /// Number of measurements to perform (None = auto-scale based on search space)
        Shots: int option
        /// Solution threshold: min fraction of shots to consider a state as solution (None = auto-scale, typical: 0.01-0.05)
        SolutionThreshold: float option
        /// Success threshold: min total probability for search success (None = auto-scale, typical: 0.05-0.15)
        SuccessThreshold: float option
        /// Maximum paths to search (None = use full tree, Some(n) = limit to n paths to prevent explosion)
        MaxPaths: int option
        /// Maximum Grover iterations for amplitude amplification (None = auto-calculate optimal iterations)
        /// Higher iterations = stronger amplification but risk of over-rotation
        MaxIterations: int option
        /// Optional progress reporter for search iterations
        ProgressReporter: Progress.IProgressReporter option
    }
    
    /// <summary>
    /// Solution to a quantum tree search problem.
    /// </summary>
    type TreeSearchSolution = {
        /// Best move to play (index into legal moves from initial state)
        BestMove: int
        /// Success probability / score of best move
        Score: float
        /// Total number of paths explored
        PathsExplored: int
        /// Whether quantum advantage was achieved
        QuantumAdvantage: bool
        /// Backend used for execution
        BackendName: string
        /// Qubits required for this search
        QubitsRequired: int
        /// All solution paths found (for debugging)
        AllSolutions: int list
    }
    
    // ============================================================================
    // VALIDATION HELPERS
    // ============================================================================
    
    /// <summary>
    /// Validates a quantum tree search problem specification.
    /// </summary>
    let validate (problem: TreeSearchProblem<'T>) : Result<unit, QuantumError> =
        if Option.isNone problem.InitialState then
            Error (QuantumError.ValidationError ("InitialState", "must be provided via 'initialState' in the builder"))
        elif problem.MaxDepth < 1 then
            Error (QuantumError.ValidationError ("MaxDepth", "must be at least 1"))
        elif problem.MaxDepth > 8 then
            Error (QuantumError.ValidationError ("MaxDepth", "exceeds 8 (would require too many qubits for NISQ devices)"))
        elif problem.BranchingFactor < 2 then
            Error (QuantumError.ValidationError ("BranchingFactor", "must be at least 2"))
        elif problem.BranchingFactor > 256 then
            Error (QuantumError.ValidationError ("BranchingFactor", "exceeds 256 (would require too many qubits)"))
        elif problem.TopPercentile <= 0.0 || problem.TopPercentile > 1.0 then
            Error (QuantumError.ValidationError ("TopPercentile", $"must be in range (0.0, 1.0], got {problem.TopPercentile}"))
        else
            let qubitsNeeded = GroverSearch.TreeSearch.estimateQubitsNeeded problem.MaxDepth problem.BranchingFactor
            if qubitsNeeded > 16 then
                Error (QuantumError.ValidationError ("TreeSearchSize", $"requires {qubitsNeeded} qubits (depth={problem.MaxDepth}, branching={problem.BranchingFactor}). Max: 16"))
            else
                Ok ()
    
    // ============================================================================
    // COMPUTATION EXPRESSION BUILDER - Tree Search Problem Builder
    // ============================================================================
    
    /// <summary>
    /// Computation expression builder for defining quantum tree search problems.
    /// </summary>
    type QuantumTreeSearchBuilder<'T>() =
        
        member _.Yield(_) : TreeSearchProblem<'T> =
            {
                InitialState = None
                MaxDepth = 3
                BranchingFactor = 16
                EvaluationFunction = fun _ -> 0.0
                MoveGenerator = fun _ -> []
                TopPercentile = 0.2
                Backend = None
                Shots = None  // Auto-scale: 50 (Local) or 250 (Cloud) - reduced after bug fixes
                SolutionThreshold = None  // Auto-scale: 5% (both Local/Cloud) - increased after bug fixes
                SuccessThreshold = None  // Auto-scale: 50% (Local) or 60% (Cloud) - increased after bug fixes
                MaxPaths = None  // Auto-recommend based on tree size
                MaxIterations = None  // Auto-calculate optimal Grover iterations
                ProgressReporter = None
            }
        
        member _.Delay(f: unit -> TreeSearchProblem<'T>) : unit -> TreeSearchProblem<'T> = f
        
        member _.Run(f: unit -> TreeSearchProblem<'T>) : TreeSearchProblem<'T> =
            let problem = f()
            match validate problem with
            | Error err -> failwith err.Message
            | Ok () -> problem
        
        member _.For(sequence: seq<'U>, body: 'U -> TreeSearchProblem<'T>) : TreeSearchProblem<'T> =
            // Idiomatic F#: Use Seq.fold for functional accumulation
            // Returns the last problem state (typical for configuration-style CEs)
            let zero = {
                InitialState = None
                MaxDepth = 3
                BranchingFactor = 16
                EvaluationFunction = fun _ -> 0.0
                MoveGenerator = fun _ -> []
                TopPercentile = 0.2
                Backend = None
                Shots = None
                SolutionThreshold = None
                SuccessThreshold = None
                MaxPaths = None
                MaxIterations = None
                ProgressReporter = None
            }
            
            sequence
            |> Seq.map body
            |> Seq.fold (fun _ current -> current) zero  // Keep last state
        
        member _.Combine(problem1: TreeSearchProblem<'T>, problem2: TreeSearchProblem<'T>) : TreeSearchProblem<'T> =
            // When combining, use the second problem but preserve any non-default values from first
            problem2
        
        /// <summary>Set the initial state for tree search.</summary>
        /// <param name="state">Initial state</param>
        [<CustomOperation("initialState")>]
        member _.InitialState(problem: TreeSearchProblem<'T>, state: 'T) : TreeSearchProblem<'T> =
            { problem with InitialState = Some state }
        
        /// <summary>Set the maximum depth for tree search.</summary>
        /// <param name="depth">Maximum search depth</param>
        [<CustomOperation("maxDepth")>]
        member _.MaxDepth(problem: TreeSearchProblem<'T>, depth: int) : TreeSearchProblem<'T> =
            { problem with MaxDepth = depth }
        
        /// <summary>Set the branching factor for tree expansion.</summary>
        /// <param name="factor">Branching factor</param>
        [<CustomOperation("branchingFactor")>]
        member _.BranchingFactor(problem: TreeSearchProblem<'T>, factor: int) : TreeSearchProblem<'T> =
            { problem with BranchingFactor = factor }
        
        /// <summary>Set the evaluation function for state scoring.</summary>
        /// <param name="evalFunc">Function to evaluate state quality</param>
        [<CustomOperation("evaluateWith")>]
        member _.EvaluateWith(problem: TreeSearchProblem<'T>, evalFunc: 'T -> float) : TreeSearchProblem<'T> =
            { problem with EvaluationFunction = evalFunc }
        
        /// <summary>Set the move generator function.</summary>
        /// <param name="moveGen">Function to generate possible moves from a state</param>
        [<CustomOperation("generateMovesWith")>]
        member _.GenerateMovesWith(problem: TreeSearchProblem<'T>, moveGen: 'T -> 'T list) : TreeSearchProblem<'T> =
            { problem with MoveGenerator = moveGen }
        
        /// <summary>Set the top percentile threshold for path selection.</summary>
        /// <param name="percentile">Top percentile (0.0 to 1.0)</param>
        [<CustomOperation("topPercentile")>]
        member _.TopPercentile(problem: TreeSearchProblem<'T>, percentile: float) : TreeSearchProblem<'T> =
            { problem with TopPercentile = percentile }
        
        /// <summary>Set the quantum backend for execution.</summary>
        /// <param name="backend">Quantum backend instance</param>
        [<CustomOperation("backend")>]
        member _.Backend(problem: TreeSearchProblem<'T>, backend: BackendAbstraction.IQuantumBackend) : TreeSearchProblem<'T> =
            { problem with Backend = Some backend }
        
        /// <summary>Set the number of measurement shots.</summary>
        /// <param name="numShots">Number of shots</param>
        [<CustomOperation("shots")>]
        member _.Shots(problem: TreeSearchProblem<'T>, numShots: int) : TreeSearchProblem<'T> =
            { problem with Shots = Some numShots }
        
        /// <summary>Set the solution quality threshold.</summary>
        /// <param name="threshold">Solution threshold value</param>
        [<CustomOperation("solutionThreshold")>]
        member _.SolutionThreshold(problem: TreeSearchProblem<'T>, threshold: float) : TreeSearchProblem<'T> =
            { problem with SolutionThreshold = Some threshold }
        
        /// <summary>Set the success probability threshold.</summary>
        /// <param name="threshold">Success threshold (0.0 to 1.0)</param>
        [<CustomOperation("successThreshold")>]
        member _.SuccessThreshold(problem: TreeSearchProblem<'T>, threshold: float) : TreeSearchProblem<'T> =
            { problem with SuccessThreshold = Some threshold }
        
        /// <summary>Set the maximum number of paths to explore.</summary>
        /// <param name="limit">Maximum path count</param>
        [<CustomOperation("maxPaths")>]
        member _.MaxPaths(problem: TreeSearchProblem<'T>, limit: int) : TreeSearchProblem<'T> =
            { problem with MaxPaths = Some limit }
        
        /// <summary>Enable automatic search space limiting.</summary>
        /// <param name="enable">True to limit search space automatically</param>
        [<CustomOperation("limitSearchSpace")>]
        member _.LimitSearchSpace(problem: TreeSearchProblem<'T>, enable: bool) : TreeSearchProblem<'T> =
            // Auto-recommend maxPaths limit based on tree size
            if enable then
                let recommendedLimit = GroverSearch.TreeSearch.recommendMaxPaths problem.MaxDepth problem.BranchingFactor
                { problem with MaxPaths = recommendedLimit }
            else
                { problem with MaxPaths = None }
        
        /// <summary>
        /// Set the maximum number of Grover iterations for amplitude amplification.
        /// Controls the strength of quantum search amplification.
        /// </summary>
        /// <param name="iterations">Number of Grover iterations (typical: 1-10)</param>
        /// <remarks>
        /// If not specified, automatically calculates optimal iterations based on search space size.
        /// Too few iterations = weak amplification, too many = over-rotation past optimal state.
        /// Optimal iterations ≈ π/4 * √(N/M) where N = search space, M = solutions.
        /// </remarks>
        [<CustomOperation("maxIterations")>]
        member _.MaxIterations(problem: TreeSearchProblem<'T>, iterations: int) : TreeSearchProblem<'T> =
            { problem with MaxIterations = Some iterations }
        
        /// <summary>Set progress reporter for search iterations.</summary>
        /// <param name="reporter">Progress reporter</param>
        [<CustomOperation("onProgress")>]
        member _.OnProgress(problem: TreeSearchProblem<'T>, reporter: Progress.IProgressReporter) : TreeSearchProblem<'T> =
            { problem with ProgressReporter = Some reporter }
    
    /// Global instance of quantumTreeSearch builder
    let quantumTreeSearch<'T> = QuantumTreeSearchBuilder<'T>()
    
    // ============================================================================
    // MAIN SOLVER - QUANTUM-FIRST
    // ============================================================================
    
    /// Solve tree search problem using Grover's algorithm
    /// 
    /// QUANTUM-FIRST API:
    /// - Uses quantum backend by default (LocalBackend for simulation)
    /// - Specify custom backend for cloud quantum hardware (IonQ, Rigetti)
    /// - Returns business-domain Solution result
    /// 
    /// PARAMETERS:
    ///   problem - Tree search problem with initial state and search config
    /// 
    /// EXAMPLES:
    ///   // Simple: Automatic quantum simulation
    ///   let solution = QuantumTreeSearch.solve problem
    ///   
    ///   // Cloud execution: Problem with IonQ backend
    ///   let problem = quantumTreeSearch {
    ///       initialState gameBoard
    ///       backend ionqBackend
    ///       ...
    ///   }
    ///   let solution = QuantumTreeSearch.solve problem
    let solve (problem: TreeSearchProblem<'T>) : Result<TreeSearchSolution, QuantumError> =
        
        try
            // Validate problem first
            match validate problem with
            | Error err -> Error err
            | Ok () ->
                
                // Unwrap InitialState (validated as Some above)
                let initialState =
                    match problem.InitialState with
                    | Some s -> s
                    | None -> failwith "InitialState must be set (should have been caught by validation)"
                
                // Use provided backend or create LocalBackend for simulation
                let actualBackend = 
                    problem.Backend 
                    |> Option.defaultValue (Backends.LocalBackend.LocalBackend() :> Core.BackendAbstraction.IQuantumBackend)
                
                // Report search start
                problem.ProgressReporter
                |> Option.iter (fun r -> 
                    let searchSpace = GroverSearch.TreeSearch.estimateSearchSpaceSize problem.MaxDepth problem.BranchingFactor
                    r.Report(Progress.PhaseChanged("Quantum Tree Search", Some $"Searching {searchSpace} paths (depth={problem.MaxDepth})...")))
                
                // Create TreeSearchConfig for the underlying algorithm
                let config : GroverSearch.TreeSearch.TreeSearchConfig<'T> = {
                    MaxDepth = problem.MaxDepth
                    BranchingFactor = problem.BranchingFactor
                    EvaluationFunction = problem.EvaluationFunction
                    MoveGenerator = problem.MoveGenerator
                }
                
                // Call quantum tree search algorithm with user-provided parameters (including maxPaths)
                match GroverSearch.TreeSearch.searchGameTree 
                        initialState 
                        config 
                        actualBackend 
                        problem.TopPercentile 
                        problem.Shots
                        problem.SolutionThreshold
                        problem.SuccessThreshold
                        problem.MaxPaths with
                | Error err -> Error (QuantumError.OperationError ("QuantumTreeSearch", $"Quantum tree search failed: {err.Message}"))
                | Ok treeResult ->
                    
                    // Report completion
                    problem.ProgressReporter
                    |> Option.iter (fun r -> 
                        r.Report(Progress.PhaseChanged("Search Complete", Some $"Found best move with score {treeResult.Score:F3}")))
                    
                    let qubitsNeeded = GroverSearch.TreeSearch.estimateQubitsNeeded problem.MaxDepth problem.BranchingFactor
                    let backendName = 
                        match problem.Backend with
                        | Some backend -> backend.GetType().Name
                        | None -> "LocalBackend (Simulation)"
                    
                    Ok {
                        BestMove = treeResult.BestMove
                        Score = treeResult.Score
                        PathsExplored = treeResult.NodesExplored
                        QuantumAdvantage = treeResult.QuantumAdvantage
                        BackendName = backendName
                        QubitsRequired = qubitsNeeded
                        AllSolutions = treeResult.AllSolutions
                    }
        with
        | ex -> Error (QuantumError.OperationError ("TreeSearchSolver", $"Tree search solve failed: {ex.Message}"))
    
    // ============================================================================
    // CONVENIENCE FUNCTIONS
    // ============================================================================
    
    /// Quick helper to create a simple tree search with common defaults
    let simple (initialState: 'T) (evalFunc: 'T -> float) (moveGen: 'T -> 'T list) : TreeSearchProblem<'T> =
        {
            InitialState = Some initialState
            MaxDepth = 3
            BranchingFactor = 16
            EvaluationFunction = evalFunc
            MoveGenerator = moveGen
            TopPercentile = 0.2
            Backend = None
            Shots = None  // Auto-scale
            SolutionThreshold = None  // Auto-scale
            SuccessThreshold = None  // Auto-scale
            MaxPaths = None  // Auto-recommend
            MaxIterations = None  // Auto-calculate optimal iterations
            ProgressReporter = None
        }
    
    /// Estimate resource requirements without executing
    let estimateResources (maxDepth: int) (branchingFactor: int) : string =
        let qubits = GroverSearch.TreeSearch.estimateQubitsNeeded maxDepth branchingFactor
        let searchSpace = GroverSearch.TreeSearch.estimateSearchSpaceSize maxDepth branchingFactor
        
        sprintf """Tree Search Resource Estimate:
  Max Depth: %d
  Branching Factor: %d
  Qubits Required: %d
  Search Space Size: %d paths
  Feasibility: %s"""
            maxDepth
            branchingFactor
            qubits
            searchSpace
            (if qubits <= 16 then "✓ Feasible on NISQ devices" else "✗ Requires fault-tolerant quantum computer")
    
    /// Export solution to human-readable string
    let describeSolution (solution: TreeSearchSolution) : string =
        let quantumAdvantageText = if solution.QuantumAdvantage then "✓ Yes" else "✗ No"
        let solutionsText =
            if List.isEmpty solution.AllSolutions then
                ""
            else
                let displayCount = min 10 (List.length solution.AllSolutions)
                let solutions = 
                    solution.AllSolutions 
                    |> List.take displayCount
                    |> List.map (sprintf "  Path encoding: %d")
                    |> String.concat "\n"
                
                let remainder =
                    if List.length solution.AllSolutions > 10 then
                        sprintf "\n  ... and %d more" (List.length solution.AllSolutions - 10)
                    else
                        ""
                
                sprintf "\n\nAll Solutions Found:\n%s%s" solutions remainder
        
        sprintf """=== Quantum Tree Search Solution ===
Best Move: %d
Score: %.4f
Paths Explored: %d
Quantum Advantage: %s
Backend: %s
Qubits Required: %d%s"""
            solution.BestMove
            solution.Score
            solution.PathsExplored
            quantumAdvantageText
            solution.BackendName
            solution.QubitsRequired
            solutionsText
    
    // ============================================================================
    // DOMAIN-SPECIFIC HELPERS
    // ============================================================================
    
    /// Create a tree search for game AI (common game AI pattern)
    let forGameAI 
        (board: 'T) 
        (depth: int) 
        (branching: int) 
        (evaluator: 'T -> float) 
        (legalMoves: 'T -> 'T list) 
        : TreeSearchProblem<'T> =
        {
            InitialState = Some board
            MaxDepth = depth
            BranchingFactor = branching
            EvaluationFunction = evaluator
            MoveGenerator = legalMoves
            TopPercentile = 0.2
            Backend = None
            Shots = None  // Auto-scale
            SolutionThreshold = None  // Auto-scale
            SuccessThreshold = None  // Auto-scale
            MaxPaths = GroverSearch.TreeSearch.recommendMaxPaths depth branching
            MaxIterations = None  // Auto-calculate optimal iterations
            ProgressReporter = None
        }
    
    /// Create a tree search for decision problems (multi-step optimization)
    let forDecisionProblem 
        (initialDecision: 'T) 
        (steps: int) 
        (optionsPerStep: int) 
        (scorer: 'T -> float) 
        (nextOptions: 'T -> 'T list) 
        : TreeSearchProblem<'T> =
        {
            InitialState = Some initialDecision
            MaxDepth = steps
            BranchingFactor = optionsPerStep
            EvaluationFunction = scorer
            MoveGenerator = nextOptions
            TopPercentile = 0.15  // More selective for decision problems
            Backend = None
            Shots = None  // Auto-scale
            SolutionThreshold = None  // Auto-scale
            SuccessThreshold = None  // Auto-scale
            MaxPaths = GroverSearch.TreeSearch.recommendMaxPaths steps optionsPerStep
            MaxIterations = None  // Auto-calculate optimal iterations
            ProgressReporter = None
        }
