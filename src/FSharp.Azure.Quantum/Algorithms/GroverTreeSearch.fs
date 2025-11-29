namespace FSharp.Azure.Quantum.GroverSearch

open System

/// Tree Search Module for Grover's Algorithm
/// 
/// Enables quantum tree search for game AIs and decision problems.
/// Encodes game tree paths as basis states and uses Grover's algorithm
/// to find optimal moves.
/// 
/// Key features:
/// - Generic game state representation
/// - Path encoding to basis states
/// - Percentile-based oracle construction
/// - Depth-limited search for NISQ devices
module TreeSearch =
    
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    open FSharp.Azure.Quantum.GroverSearch.Oracle
    open FSharp.Azure.Quantum.GroverSearch.BackendAdapter
    open FSharp.Azure.Quantum.GroverSearch.GroverIteration
    
    // ========================================================================
    // TYPES - Tree search configuration and results
    // ========================================================================
    
    /// Generic game state representation
    type GameState<'T> = {
        Position: 'T
        Depth: int
        ParentMove: int option
    }
    
    /// Tree search configuration
    type TreeSearchConfig<'T> = {
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
    type TreeSearchResult = {
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
    }
    
    // ========================================================================
    // STATE ENCODING - Map paths to basis states
    // ========================================================================
    
    /// Calculate bits needed to represent a value
    let private bitsNeeded (value: int) : int =
        if value <= 0 then 0
        else int (Math.Ceiling(Math.Log(float value, 2.0)))
    
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
        |> List.fold (fun acc (depth, move) ->
            acc + (move <<< (depth * bitsPerLevel))
        ) 0
    
    /// Decode basis state integer to game tree path
    let decodeTreePosition (encoded: int) (branchingFactor: int) (maxDepth: int) : int list =
        let bitsPerLevel = bitsNeeded branchingFactor
        let mask = (1 <<< bitsPerLevel) - 1
        
        [0 .. maxDepth - 1]
        |> List.map (fun depth ->
            (encoded >>> (depth * bitsPerLevel)) &&& mask
        )
    
    // ========================================================================
    // TREE ENUMERATION - Classical path generation
    // ========================================================================
    
    /// Enumerate all paths in game tree up to max depth
    /// 
    /// Returns list of (path, leaf_state) pairs
    let rec private enumeratePaths<'T> 
        (state: 'T) 
        (config: TreeSearchConfig<'T>) 
        (currentDepth: int) 
        (currentPath: int list) 
        : (int list * 'T) list =
        
        if currentDepth >= config.MaxDepth then
            // Reached max depth - return current path
            [(currentPath, state)]
        else
            // Generate legal moves
            let moves = config.MoveGenerator state
            
            if List.isEmpty moves then
                // No legal moves - terminal state
                [(currentPath, state)]
            else
                // Recursively explore each move
                moves
                |> List.indexed
                |> List.collect (fun (moveIdx, nextState) ->
                    enumeratePaths nextState config (currentDepth + 1) (currentPath @ [moveIdx])
                )
    
    /// Follow a path from root state
    let private followPath<'T> 
        (rootState: 'T) 
        (path: int list) 
        (moveGenerator: 'T -> 'T list) 
        : 'T option =
        
        try
            let finalState =
                path
                |> List.fold (fun state moveIdx ->
                    let moves = moveGenerator state
                    if moveIdx < List.length moves then
                        List.item moveIdx moves
                    else
                        failwith "Invalid move index in path"
                ) rootState
            
            Some finalState
        with _ ->
            None
    
    // ========================================================================
    // TREE SEARCH ORACLE CONSTRUCTION
    // ========================================================================
    
    /// Create oracle that marks promising moves in game tree
    /// 
    /// Strategy:
    /// 1. Enumerate all paths to max depth
    /// 2. Evaluate leaf nodes with heuristic function
    /// 3. Mark top percentile of paths as solutions
    /// 
    /// Parameters:
    /// - rootState: Initial game position
    /// - config: Tree search configuration
    /// - topPercentile: Fraction of best moves to mark (0.0-1.0)
    let createTreeSearchOracle<'T> 
        (rootState: 'T) 
        (config: TreeSearchConfig<'T>) 
        (topPercentile: float) 
        : Result<CompiledOracle, string> =
        
        try
            if topPercentile <= 0.0 || topPercentile > 1.0 then
                Error $"Top percentile must be in range (0.0, 1.0], got {topPercentile}"
            else
                // Step 1: Enumerate all paths
                let allPaths = enumeratePaths rootState config 0 []
                
                if List.isEmpty allPaths then
                    Error "No paths found in game tree"
                else
                    // Step 2: Evaluate all leaf nodes
                    let evaluatedPaths =
                        allPaths
                        |> List.map (fun (path, leafState) ->
                            let score = config.EvaluationFunction leafState
                            (path, score)
                        )
                        |> List.sortByDescending snd
                    
                    // Step 3: Select top percentile as solutions
                    let numSolutions = max 1 (int (topPercentile * float (List.length evaluatedPaths)))
                    let solutions =
                        evaluatedPaths
                        |> List.take numSolutions
                        |> List.map (fun (path, _) -> encodeTreePosition path config.BranchingFactor)
                    
                    // Step 4: Create oracle
                    let numQubits = config.MaxDepth * bitsNeeded config.BranchingFactor
                    
                    if numQubits > 16 then
                        Error $"Tree search requires {numQubits} qubits (depth={config.MaxDepth}, branching={config.BranchingFactor}). Max supported: 16. Reduce depth or branching factor."
                    else
                        Oracle.forValues solutions numQubits
        
        with ex ->
            Error $"Tree search oracle creation failed: {ex.Message}"
    
    // ========================================================================
    // QUANTUM TREE SEARCH EXECUTION
    // ========================================================================
    
    /// Execute tree search using Grover's algorithm
    /// 
    /// Finds best move in game tree using quantum amplitude amplification.
    /// 
    /// Parameters:
    /// - rootState: Initial game position
    /// - config: Tree search configuration
    /// - backend: IQuantumBackend instance
    /// - topPercentile: Fraction of best moves to mark (default: 0.2 = top 20%)
    /// 
    /// Returns: TreeSearchResult with best move
    let searchGameTree<'T>
        (rootState: 'T)
        (config: TreeSearchConfig<'T>)
        (backend: IQuantumBackend)
        (topPercentile: float)
        : Result<TreeSearchResult, string> =
        
        try
            // Step 1: Create tree search oracle
            match createTreeSearchOracle rootState config topPercentile with
            | Error msg -> Error msg
            | Ok oracle ->
                // Step 2: Calculate optimal iterations
                let searchSpaceSize = 1 <<< oracle.NumQubits
                let numSolutions = max 1 (int (topPercentile * float searchSpaceSize))
                
                match GroverIteration.optimalIterations searchSpaceSize numSolutions with
                | Error msg -> Error msg
                | Ok numIterations ->
                    // Step 3: Execute Grover search
                    match BackendAdapter.executeGroverWithBackend oracle backend numIterations 1000 with
                    | Error msg -> Error msg
                    | Ok searchResult ->
                        // Step 4: Decode best move from result
                        if List.isEmpty searchResult.Solutions then
                            Error "No solution found by quantum search"
                        else
                            let bestEncoded = List.head searchResult.Solutions
                            let decodedPath = decodeTreePosition bestEncoded config.BranchingFactor config.MaxDepth
                            
                            // Extract first move (root level)
                            let firstMove = List.tryHead decodedPath |> Option.defaultValue 0
                            
                            // Calculate quantum advantage
                            let classicalComplexity = searchSpaceSize
                            let quantumComplexity = numIterations * searchSpaceSize  // Approximate
                            let quantumAdvantage = quantumComplexity < classicalComplexity
                            
                            Ok {
                                BestMove = firstMove
                                Score = searchResult.SuccessProbability
                                NodesExplored = searchSpaceSize
                                QuantumAdvantage = quantumAdvantage
                                AllSolutions = searchResult.Solutions
                            }
        
        with ex ->
            Error $"Quantum tree search failed: {ex.Message}"
    
    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================
    
    /// Search with default top percentile (20%)
    let searchGameTreeDefault<'T>
        (rootState: 'T)
        (config: TreeSearchConfig<'T>)
        (backend: IQuantumBackend)
        : Result<TreeSearchResult, string> =
        searchGameTree rootState config backend 0.2
    
    /// Estimate qubits needed for tree search
    let estimateQubitsNeeded (maxDepth: int) (branchingFactor: int) : int =
        maxDepth * bitsNeeded branchingFactor
    
    /// Estimate search space size
    let estimateSearchSpaceSize (maxDepth: int) (branchingFactor: int) : int =
        let qubits = estimateQubitsNeeded maxDepth branchingFactor
        1 <<< qubits
    
    // ========================================================================
    // EXAMPLES
    // ========================================================================
    
    module Examples =
        
        /// Example: Simple arithmetic game
        /// Choose operations to maximize result
        let arithmeticGame () : Result<TreeSearchResult, string> =
            let rootState = 1
            
            let config = {
                MaxDepth = 3
                BranchingFactor = 4
                EvaluationFunction = float
                MoveGenerator = fun x -> [x + 1; x * 2; x - 1; x / 2]
            }
            
            let backend = FSharp.Azure.Quantum.Core.BackendAbstraction.createLocalBackend()
            
            searchGameTree rootState config backend 0.3
        
        /// Example: Path finding
        /// Find shortest path in grid
        let pathFinding () : Result<TreeSearchResult, string> =
            // Helper type for grid positions
            let encodePos (x: int) (y: int) = x * 10 + y
            let decodePos (encoded: int) = (encoded / 10, encoded % 10)
            
            let rootState = encodePos 0 0
            let goal = encodePos 3 3
            
            let config = {
                MaxDepth = 2
                BranchingFactor = 4  // Up, Down, Left, Right
                EvaluationFunction = fun encoded ->
                    let (x, y) = decodePos encoded
                    let (gx, gy) = decodePos goal
                    let dx = gx - x
                    let dy = gy - y
                    -sqrt(float (dx*dx + dy*dy))  // Negative distance (closer = better)
                MoveGenerator = fun encoded ->
                    let (x, y) = decodePos encoded
                    [
                        encodePos (x + 1) y
                        encodePos (x - 1) y
                        encodePos x (y + 1)
                        encodePos x (y - 1)
                    ]
            }
            
            let backend = FSharp.Azure.Quantum.Core.BackendAbstraction.createLocalBackend()
            
            searchGameTree rootState config backend 0.25
