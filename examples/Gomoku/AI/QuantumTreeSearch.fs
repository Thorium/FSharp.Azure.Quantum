namespace FSharp.Azure.Quantum.Examples.Gomoku.AI

open FSharp.Azure.Quantum.Examples.Gomoku
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum

/// Quantum Tree Search AI for Gomoku using Grover's algorithm
/// 
/// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
/// ⚡ ARCHITECTURE RECOMMENDATION
/// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
/// 
/// **RECOMMENDED FOR PRODUCTION**: Use the high-level `FSharp.Azure.Quantum.QuantumTreeSearch`
/// builder API instead of calling low-level `GroverSearch.TreeSearch` functions directly.
/// 
/// **Builder API Benefits**:
/// ✅ Clean computation expression syntax
/// ✅ Automatic validation (qubits, search space, parameters)
/// ✅ Backend abstraction (LocalBackend or cloud quantum hardware)
/// ✅ Resource estimation (qubits, iterations, feasibility)
/// ✅ Idiomatic F# design following F# Component Design Guidelines
/// 
/// **Source**: `src/FSharp.Azure.Quantum/Solvers/Quantum/QuantumTreeSearchBuilder.fs`
/// 
/// **This Example**: Demonstrates the RECOMMENDED pattern for quantum game AI.
/// Uses `QuantumTreeSearch.forGameAI` helper to build problems cleanly.
/// 
/// **Migration Guide**: See README.md for comparison between old (low-level API)
/// and new (builder API) approaches.
/// 
/// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
module QuantumTreeSearch =
    
    // ========================================================================
    // BOARD STATE EVALUATION
    // ========================================================================
    
    /// Evaluate Gomoku board position using threat detection
    /// 
    /// Higher score = better for current player
    /// Weights:
    /// - Own threats: +2 points each (offensive value)
    /// - Opponent threats: -1 point each (defensive value)
    let evaluateGomokuPosition (board: Board) : float =
        let currentPlayer = board.CurrentPlayer
        let opponent = if currentPlayer = Black then White else Black
        
        // Count threats for current player (offensive)
        let myThreats = 
            ThreatDetection.getAllThreats board 
            |> List.length
        
        // Count threats for opponent (defensive)
        let opponentThreats = 
            let oppBoard = { board with CurrentPlayer = opponent }
            ThreatDetection.getAllThreats oppBoard 
            |> List.length
        
        // Prioritize offense over defense (2:1 ratio)
        float (myThreats * 2 - opponentThreats)
    
    // ========================================================================
    // MOVE GENERATION
    // ========================================================================
    
    /// Generate legal moves from current board state
    /// 
    /// Returns list of resulting board states after each legal move.
    /// Filters to keep only nearby occupied cells (reduces branching factor).
    let generateMoves (board: Board) : Board list =
        // Get all valid moves
        let allValidMoves = Board.getValidMoves board
        
        // Heuristic: Focus on moves near existing pieces (reduces search space)
        let nearbyMoves =
            if List.isEmpty allValidMoves then
                []
            else
                allValidMoves
                |> List.filter (fun pos ->
                    // Check if position is within 2 squares of any occupied cell
                    let hasNearbyPiece =
                        [-2 .. 2] |> List.exists (fun dr ->
                            [-2 .. 2] |> List.exists (fun dc ->
                                if dr = 0 && dc = 0 then false
                                else
                                    let nearPos = { Row = pos.Row + dr; Col = pos.Col + dc }
                                    Board.isValidPosition board nearPos && 
                                    not (Board.isEmpty board nearPos)
                            )
                        )
                    hasNearbyPiece
                )
        
        // If no nearby moves, use all valid moves (e.g., start of game)
        let movesToConsider = 
            if List.isEmpty nearbyMoves then allValidMoves 
            else nearbyMoves
        
        // Generate resulting boards
        movesToConsider
        |> List.choose (fun pos ->
            match Board.makeMove board pos with
            | Ok newBoard -> Some newBoard
            | Error _ -> None
        )
    
    // ========================================================================
    // QUANTUM AI PLAYER
    // ========================================================================
    
    /// Select best move using quantum tree search
    /// 
    /// **NEW IMPLEMENTATION**: Uses the high-level `QuantumTreeSearch` builder API
    /// instead of calling `GroverSearch.TreeSearch.searchGameTree` directly.
    /// 
    /// Parameters:
    /// - board: Current game board
    /// - backend: IQuantumBackend (LocalBackend for testing, IonQ/Rigetti for cloud)
    /// - searchDepth: Tree depth to explore (1-3 recommended for NISQ devices)
    /// - topPercentile: Fraction of best moves to consider (default: 0.2 = top 20%)
    /// 
    /// Returns: Best position to play, or Error if search fails
    let selectMove 
        (board: Board) 
        (backend: IQuantumBackend) 
        (searchDepth: int) 
        (topPercentile: float option) 
        : Result<Position, string> =
        
        let percentile = topPercentile |> Option.defaultValue 0.2
        
        // Get available moves
        let validMoves = Board.getValidMoves board
        
        if List.isEmpty validMoves then
            Error "No valid moves available"
        elif searchDepth < 1 then
            Error "Search depth must be at least 1"
        elif searchDepth > 3 then
            Error "Search depth > 3 not recommended for NISQ devices (too many qubits required)"
        else
            try
                // Estimate qubits needed
                let estimatedBranching = min (List.length validMoves) 15  // Cap at 15
                let qubitsNeeded = GroverSearch.TreeSearch.estimateQubitsNeeded searchDepth estimatedBranching
                
                if qubitsNeeded > backend.MaxQubits then
                    Error $"Tree search requires {qubitsNeeded} qubits but backend '{backend.Name}' supports max {backend.MaxQubits}. Reduce search depth."
                else
                    // **RECOMMENDED APPROACH**: Use QuantumTreeSearch builder
                    // Note: We build the problem step by step due to F# computation expression limitations
                    let baseProblem = QuantumTreeSearch.forGameAI 
                                        board 
                                        searchDepth 
                                        estimatedBranching 
                                        evaluateGomokuPosition 
                                        generateMoves
                    
                    // Override with custom settings
                    let problem = { baseProblem with 
                                      Backend = Some backend
                                      TopPercentile = percentile }
                    
                    // Solve using high-level API
                    match QuantumTreeSearch.solve problem with
                    | Error msg -> Error $"Quantum search failed: {msg}"
                    | Ok solution ->
                        // Map move index to actual position
                        if solution.BestMove < List.length validMoves then
                            let bestPosition = List.item solution.BestMove validMoves
                            Ok bestPosition
                        else
                            Error $"Invalid move index {solution.BestMove} returned (only {List.length validMoves} moves available)"
            
            with ex ->
                Error $"Quantum tree search exception: {ex.Message}"
    
    /// Select best move with default parameters
    /// 
    /// Uses:
    /// - Search depth: 2 (practical for near-term quantum devices)
    /// - Top percentile: 20% (marks top 20% of moves as solutions)
    let selectMoveDefault (board: Board) (backend: IQuantumBackend) : Result<Position, string> =
        selectMove board backend 2 None
    
    // ========================================================================
    // FALLBACK STRATEGY
    // ========================================================================
    
    /// Select move with fallback to classical AI
    /// 
    /// Tries quantum search first, falls back to classical if quantum fails.
    /// Useful for production systems where reliability is critical.
    let selectMoveWithFallback
        (board: Board)
        (backend: IQuantumBackend)
        (searchDepth: int)
        : Result<Position, string> =
        
        match selectMove board backend searchDepth None with
        | Ok position -> Ok position
        | Error _ ->
            // Fallback to classical AI
            match Classical.selectBestMove board with
            | Some pos -> Ok pos
            | None -> Error "No valid moves available"
    
    // ========================================================================
    // DIAGNOSTICS
    // ========================================================================
    
    /// Estimate computational resources for quantum search
    type ResourceEstimate = {
        SearchDepth: int
        BranchingFactor: int
        QubitsNeeded: int
        SearchSpaceSize: int
        OptimalIterations: int option
        Feasible: bool
        Reason: string option
    }
    
    /// Estimate resources needed for quantum tree search on current board
    let estimateResources (board: Board) (searchDepth: int) (backend: IQuantumBackend) : ResourceEstimate =
        let validMoves = Board.getValidMoves board
        let branching = min (List.length validMoves) 15
        let qubits = GroverSearch.TreeSearch.estimateQubitsNeeded searchDepth branching
        let searchSpace = GroverSearch.TreeSearch.estimateSearchSpaceSize searchDepth branching
        
        let optimalIter =
            let numSolutions = searchSpace / 5  // Assume top 20%
            match FSharp.Azure.Quantum.GroverSearch.GroverIteration.optimalIterations searchSpace numSolutions with
            | Ok k -> Some k
            | Error _ -> None
        
        let (feasible, reason) =
            if qubits > backend.MaxQubits then
                (false, Some $"Requires {qubits} qubits but backend supports max {backend.MaxQubits}")
            elif qubits > 16 then
                (false, Some $"Requires {qubits} qubits (oracle enumeration too expensive)")
            elif searchSpace > 100000 then
                (false, Some $"Search space too large ({searchSpace} states)")
            else
                (true, None)
        
        {
            SearchDepth = searchDepth
            BranchingFactor = branching
            QubitsNeeded = qubits
            SearchSpaceSize = searchSpace
            OptimalIterations = optimalIter
            Feasible = feasible
            Reason = reason
        }
    
    // ========================================================================
    // EXAMPLES - Using QuantumTreeSearch Builder API
    // ========================================================================
    
    module Examples =
        
        /// Example: Play Gomoku move using local quantum simulator with builder API
        let playWithLocalBackend () =
            let board = Board.init 15 15
            let backend = createLocalBackend()
            
            // **RECOMMENDED**: Use the forGameAI helper for clean, declarative code
            let problem = QuantumTreeSearch.forGameAI 
                            board 
                            2  // maxDepth
                            15  // branchingFactor
                            evaluateGomokuPosition 
                            generateMoves
            
            // Override backend if needed
            let problemWithBackend = { problem with Backend = Some backend }
            
            match QuantumTreeSearch.solve problemWithBackend with
            | Ok solution -> 
                printfn "Quantum AI selected move: %d" solution.BestMove
                printfn "Score: %.4f" solution.Score
                printfn "Paths explored: %d" solution.PathsExplored
                printfn "Quantum advantage: %b" solution.QuantumAdvantage
                
                // Convert move index to position
                let validMoves = Board.getValidMoves board
                if solution.BestMove < List.length validMoves then
                    let position = List.item solution.BestMove validMoves
                    printfn "Position: Row %d, Col %d" position.Row position.Col
                    Ok position
                else
                    Error "Invalid move index"
            | Error msg ->
                printfn "Quantum search failed: %s" msg
                Error msg
        
        /// Example: Estimate resources for different search depths
        let estimateForAllDepths (board: Board) (backend: IQuantumBackend) =
            [1 .. 3]
            |> List.map (fun depth ->
                let estimate = estimateResources board depth backend
                printfn "Depth %d: %d qubits, %d states, feasible=%b" 
                    depth estimate.QubitsNeeded estimate.SearchSpaceSize estimate.Feasible
                estimate
            )
        
        /// Example: Compare quantum vs classical move selection time
        let compareQuantumVsClassical (board: Board) (backend: IQuantumBackend) =
            // Time quantum search
            let startQuantum = System.Diagnostics.Stopwatch.StartNew()
            let quantumMove = selectMove board backend 2 None
            startQuantum.Stop()
            
            // Time classical search
            let startClassical = System.Diagnostics.Stopwatch.StartNew()
            let classicalMove = Classical.selectBestMove board
            startClassical.Stop()
            
            printfn "Quantum time: %d ms" startQuantum.ElapsedMilliseconds
            printfn "Classical time: %d ms" startClassical.ElapsedMilliseconds
            
            (quantumMove, classicalMove, startQuantum.ElapsedMilliseconds, startClassical.ElapsedMilliseconds)
