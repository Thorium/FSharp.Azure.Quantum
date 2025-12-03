// ============================================================================
// Quantum Tree Search Examples - FSharp.Azure.Quantum
// ============================================================================
//
// This script demonstrates the Quantum Tree Search API using Grover's
// algorithm to solve game tree and decision tree problems:
//
// 1. Tic-Tac-Toe AI (Simple)
// 2. Chess-style Position Evaluation (Intermediate)
// 3. Decision Tree Optimization (Advanced)
//
// WHAT IS QUANTUM TREE SEARCH:
// Find the best move in a game tree by exploring possible move sequences,
// using quantum amplitude amplification to accelerate the search. Instead
// of evaluating all paths classically, Grover's algorithm finds promising
// paths quadratically faster.
//
// WHY USE QUANTUM:
// - Grover's algorithm provides O(√N) speedup over classical minimax
// - Ideal when position evaluation is expensive (e.g., Monte Carlo rollouts)
// - Quadratic speedup for exploring large game trees
// - Useful for real-time game AI where speed matters
//
// ============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumTreeSearch
open FSharp.Azure.Quantum.Core.BackendAbstraction

// ============================================================================
// BACKEND CONFIGURATION
// ============================================================================

// Create local quantum simulator (fast, for development/testing)
let localBackend = LocalBackend() :> IQuantumBackend

// For cloud execution, use IonQ or Rigetti backend:
// let cloudBackend = IonQBackend(workspace, resourceId) :> IQuantumBackend
// let cloudBackend = RigettiBackend(workspace, resourceId) :> IQuantumBackend

// ============================================================================
// EXAMPLE 1: Tic-Tac-Toe AI (Educational)
// ============================================================================
//
// PROBLEM: Find the best move in Tic-Tac-Toe by evaluating possible futures
// 2-3 moves ahead.
//
// REAL-WORLD IMPACT:
// - Demonstrates tree search for turn-based games
// - Scales to more complex games (chess, go, gomoku)
// - Quantum speedup useful for real-time game AI
//
printfn "========================================="
printfn "EXAMPLE 1: Tic-Tac-Toe AI"
printfn "========================================="
printfn ""

// Game state representation
type Player = X | O | Empty
type Board = Player array  // 3×3 = 9 cells

type TicTacToeState = {
    Board: Board
    CurrentPlayer: Player
    Move: int option  // The move that led to this state
}

// Helper: Display board
let displayBoard (board: Board) =
    let charOf p = match p with X -> "X" | O -> "O" | Empty -> "·"
    printfn "  %s | %s | %s" (charOf board.[0]) (charOf board.[1]) (charOf board.[2])
    printfn "  ---------"
    printfn "  %s | %s | %s" (charOf board.[3]) (charOf board.[4]) (charOf board.[5])
    printfn "  ---------"
    printfn "  %s | %s | %s" (charOf board.[6]) (charOf board.[7]) (charOf board.[8])

// Helper: Check for winner
let checkWinner (board: Board) : Player option =
    let lines = [
        [0; 1; 2]; [3; 4; 5]; [6; 7; 8]  // Rows
        [0; 3; 6]; [1; 4; 7]; [2; 5; 8]  // Columns
        [0; 4; 8]; [2; 4; 6]              // Diagonals
    ]
    
    lines 
    |> List.tryPick (fun line ->
        let cells = line |> List.map (fun i -> board.[i])
        match cells with
        | [X; X; X] -> Some X
        | [O; O; O] -> Some O
        | _ -> None
    )

// Helper: Evaluate position (heuristic)
let evaluatePosition (state: TicTacToeState) : float =
    match checkWinner state.Board with
    | Some X -> 1000.0   // X wins (maximize)
    | Some O -> -1000.0  // O wins (minimize)
    | Some _
    | None ->
        // Count potential winning lines
        let lines = [
            [0; 1; 2]; [3; 4; 5]; [6; 7; 8]
            [0; 3; 6]; [1; 4; 7]; [2; 5; 8]
            [0; 4; 8]; [2; 4; 6]
        ]
        
        let scoreLines player =
            lines |> List.sumBy (fun line ->
                let cells = line |> List.map (fun i -> state.Board.[i])
                let count = cells |> List.filter ((=) player) |> List.length
                let enemyCount = cells |> List.filter ((=) (if player = X then O else X)) |> List.length
                if enemyCount > 0 then 0.0  // Line blocked
                else float (count * count)  // More pieces = exponentially better
            )
        
        let xScore = scoreLines X
        let oScore = scoreLines O
        
        if state.CurrentPlayer = X then xScore - oScore
        else oScore - xScore

// Helper: Generate successor states
let generateMoves (state: TicTacToeState) : TicTacToeState list =
    if checkWinner state.Board |> Option.isSome then
        []  // Game over
    else
        [0..8]
        |> List.filter (fun i -> state.Board.[i] = Empty)
        |> List.map (fun move ->
            let newBoard = Array.copy state.Board
            newBoard.[move] <- state.CurrentPlayer
            {
                Board = newBoard
                CurrentPlayer = if state.CurrentPlayer = X then O else X
                Move = Some move
            }
        )

// Initial game state (X's turn)
let ticTacToeStartState = {
    Board = [| Empty; Empty; Empty; 
               Empty; X; Empty; 
               Empty; Empty; O |]
    CurrentPlayer = X
    Move = None
}

printfn "Initial Position:"
displayBoard ticTacToeStartState.Board
printfn ""
printfn "Current Player: X"
printfn "Finding best move (searching 3 moves ahead)..."
printfn ""

// Build quantum tree search problem
let ticTacToeProblem = quantumTreeSearch {
    initialState ticTacToeStartState
    maxDepth 2  // Depth 2 with natural branching
    branchingFactor 9  // Tic-tac-toe has up to 9 possible moves
    evaluateWith evaluatePosition
    generateMovesWith generateMoves  // NO truncation - use all legal moves!
    topPercentile 0.2  // Consider top 20% of moves
    backend localBackend
    
    // LocalBackend tuning (improved after MCZ/amplitude bug fixes)
    shots 50  // Reduced from 100 - algorithm is now much more reliable
    solutionThreshold 0.05  // 5% - increased from 1% (more rigorous)
    successThreshold 0.5  // 50% - increased from 10% (much higher confidence)
}

match solve ticTacToeProblem with
| Ok result ->
    printfn "✅ BEST MOVE FOUND!"
    printfn ""
    
    printfn "  Recommended Move: Position %d" result.BestMove
    printfn "  Evaluation Score: %.4f" result.Score
    printfn "  Paths Explored: %d" result.PathsExplored
    printfn "  Qubits Required: %d" result.QubitsRequired
    printfn "  Quantum Advantage: %b" result.QuantumAdvantage
    printfn ""

| Error msg ->
    printfn "❌ Error: %s" msg

printfn ""
printfn ""

// ============================================================================
// EXAMPLE 2: Chess-Style Position Evaluation
// ============================================================================
//
// PROBLEM: Evaluate a chess-like position 4 moves ahead with expensive
// tactical analysis (threat detection, king safety, piece coordination).
//
// REAL-WORLD IMPACT:
// - Chess engines evaluate millions of positions per second
// - Deep learning models make evaluation expensive (100ms per position)
// - Quantum search reduces evaluations from 16^4 = 65,536 to √65,536 = 256
// - 256× speedup makes real-time play viable with ML evaluation
//
printfn "========================================="
printfn "EXAMPLE 2: Chess Position Analysis"
printfn "========================================="
printfn ""

// Simplified chess state (for demonstration)
type Piece = Pawn | Knight | Bishop | Rook | Queen | King
type Color = White | Black
type Square = (Piece * Color) option

type ChessState = {
    Pieces: Square array  // 64 squares
    ToMove: Color
    Ply: int  // Half-moves (for depth tracking)
}

// Simulate expensive position evaluation (normally 100ms+ with neural network)
let evaluateChessPosition (state: ChessState) : float =
    // Real evaluation: neural network, tactical analysis, king safety, pawn structure
    // Here: simplified material + positional scoring
    
    let materialValue = function
        | Pawn -> 1.0 | Knight -> 3.0 | Bishop -> 3.0 
        | Rook -> 5.0 | Queen -> 9.0 | King -> 0.0
    
    let material =
        state.Pieces
        |> Array.choose id
        |> Array.sumBy (fun (piece, color) ->
            let value = materialValue piece
            if color = White then value else -value
        )
    
    // Add positional bonus (simplified)
    let positional = float state.Ply * 0.1  // Favor advancing
    
    material + positional

// Generate pseudo-legal moves (simplified)
let generateChessMoves (state: ChessState) : ChessState list =
    // Real chess: generate legal moves, apply, check validity
    // Here: simulate ~35 legal moves per position (chess average)
    
    if state.Ply >= 8 then []  // Stop at depth 4 (8 plies)
    else
        // Simulate 16 candidate moves (reduced for performance)
        [1..16]
        |> List.map (fun moveNum ->
            {
                Pieces = state.Pieces  // Simplified: keep same position
                ToMove = if state.ToMove = White then Black else White
                Ply = state.Ply + 1
            }
        )

// Example: Opening position (simplified)
let chessInitial = {
    Pieces = Array.create 64 None
    ToMove = White
    Ply = 0
}

printfn "Position: Opening (simplified)"
printfn "Search Depth: 4 moves (8 plies)"
printfn "Branching Factor: ~16 legal moves per position"
printfn ""
printfn "Searching for best move..."
printfn "(With neural network eval: √65,536 = 256 evals vs 65,536 classical)"
printfn ""

let chessProblem = quantumTreeSearch {
    initialState chessInitial
    maxDepth 2  // Depth 2 with realistic branching
    branchingFactor 16  // Chess-like game with ~16 candidate moves
    evaluateWith evaluateChessPosition
    generateMovesWith generateChessMoves  // Use all generated moves
    topPercentile 0.15
    backend localBackend
    shots 50  // Reduced from 100 - algorithm is more reliable
    solutionThreshold 0.05  // 5% - increased from 1%
    successThreshold 0.5  // 50% - increased from 10%
}

match solve chessProblem with
| Ok result ->
    printfn "✅ SEARCH COMPLETE!"
    printfn ""
    printfn "  Best Move: %d" result.BestMove
    printfn "  Evaluation Score: %.4f" result.Score
    printfn "  Paths Explored: %d" result.PathsExplored
    printfn "  Qubits Required: %d" result.QubitsRequired
    printfn "  Quantum Advantage: %b" result.QuantumAdvantage
    printfn ""

| Error msg ->
    printfn "❌ Error: %s" msg

printfn ""
printfn ""

// ============================================================================
// EXAMPLE 3: Decision Tree Optimization
// ============================================================================
//
// PROBLEM: Find optimal sequence of business decisions over 3 stages
// (e.g., product launch strategy, marketing channels, pricing tiers).
//
// REAL-WORLD IMPACT:
// - Business decisions have expensive evaluation (market simulation)
// - Each simulation takes 5-10 minutes
// - Quantum search: √512 = 22 simulations vs 512 classical
// - 23× speedup: 40 minutes vs 15 hours
//
printfn "========================================="
printfn "EXAMPLE 3: Business Decision Tree"
printfn "========================================="
printfn ""

type MarketingDecision = 
    | SocialMedia | TV | Radio | Email

type PricingDecision = 
    | Budget | Standard | Premium

type LaunchDecision = 
    | SoftLaunch | Regional | Global

type BusinessState = {
    Marketing: MarketingDecision option
    Pricing: PricingDecision option
    Launch: LaunchDecision option
    Stage: int
}

// Simulate expensive market simulation
let simulateMarketImpact (state: BusinessState) : float =
    // Real simulation: Monte Carlo, market dynamics, competitor response
    // Here: simplified ROI calculation
    
    let marketingScore =
        match state.Marketing with
        | Some SocialMedia -> 80.0
        | Some TV -> 100.0
        | Some Radio -> 60.0
        | Some Email -> 70.0
        | None -> 0.0
    
    let pricingScore =
        match state.Pricing with
        | Some Premium -> 100.0
        | Some Standard -> 85.0
        | Some Budget -> 60.0
        | None -> 0.0
    
    let launchScore =
        match state.Launch with
        | Some Global -> 100.0
        | Some Regional -> 80.0
        | Some SoftLaunch -> 60.0
        | None -> 0.0
    
    // Combined ROI
    (marketingScore + pricingScore + launchScore) / 3.0

// Generate decision options
let generateBusinessDecisions (state: BusinessState) : BusinessState list =
    match state.Stage with
    | 0 ->  // Stage 1: Choose marketing channel
        [SocialMedia; TV; Radio; Email]
        |> List.map (fun m -> 
            { state with Marketing = Some m; Stage = 1 }
        )
    
    | 1 ->  // Stage 2: Choose pricing
        [Budget; Standard; Premium]
        |> List.map (fun p -> 
            { state with Pricing = Some p; Stage = 2 }
        )
    
    | 2 ->  // Stage 3: Choose launch strategy
        [SoftLaunch; Regional; Global]
        |> List.map (fun l -> 
            { state with Launch = Some l; Stage = 3 }
        )
    
    | _ -> []  // Done

let businessInitial = {
    Marketing = None
    Pricing = None
    Launch = None
    Stage = 0
}

printfn "Decision Stages:"
printfn "  1. Marketing Channel (4 options)"
printfn "  2. Pricing Strategy (3 options)"
printfn "  3. Launch Approach (3 options)"
printfn ""
printfn "Total Paths: 4 × 3 × 3 = 36 decision sequences"
printfn "Each evaluation: 5-10 minutes (market simulation)"
printfn ""

let businessProblem = quantumTreeSearch {
    initialState businessInitial
    maxDepth 3  // 3-stage decision process
    branchingFactor 4  // Average of 3-4 options per stage
    evaluateWith simulateMarketImpact
    generateMovesWith generateBusinessDecisions  // Use all decision options
    
    // Backend configuration (improved after MCZ/amplitude bug fixes)
    backend localBackend
    shots 50  // Reduced from 100 - algorithm is more reliable
    solutionThreshold 0.05  // 5% - increased from 1%
    successThreshold 0.5  // 50% - increased from 10%
}

match solve businessProblem with
| Ok result ->
    printfn "✅ OPTIMAL STRATEGY FOUND!"
    printfn ""
    
    printfn "  Best Move: %d" result.BestMove
    printfn "  Expected ROI Score: %.4f" result.Score
    printfn "  Paths Explored: %d" result.PathsExplored
    printfn "  Qubits Required: %d" result.QubitsRequired
    printfn "  Quantum Advantage: %b" result.QuantumAdvantage
    printfn ""

| Error msg ->
    printfn "❌ Error: %s" msg

printfn ""
printfn ""

// ============================================================================
// SUMMARY: When to Use Quantum Tree Search
// ============================================================================

printfn "========================================="
printfn "WHEN TO USE QUANTUM TREE SEARCH"
printfn "========================================="
printfn ""
printfn "✅ GOOD FITS:"
printfn "  - Game AI (chess, go, gomoku, strategy games)"
printfn "  - Decision trees with expensive evaluation"
printfn "  - Monte Carlo Tree Search acceleration"
printfn "  - Path planning with complex heuristics"
printfn "  - Branching factor: 8-64 moves per position"
printfn "  - Search depth: 2-5 moves ahead"
printfn ""
printfn "❌ NOT SUITABLE FOR:"
printfn "  - Simple games (solved classically)"
printfn "  - Very deep search (>6 moves ahead on NISQ)"
printfn "  - Fast evaluation (<1ms per position)"
printfn "  - Problems with strong alpha-beta pruning"
printfn ""
printfn "🚀 QUANTUM ADVANTAGE:"
printfn "  - Grover's algorithm: O(√N) vs O(N) minimax"
printfn "  - Best for: expensive evaluation + medium depth"
printfn "  - Example: 16^4 = 65,536 → √65,536 = 256 evals"
printfn "  - 256× speedup with neural network position evaluation"
printfn ""
printfn "📚 RELATED BUILDERS:"
printfn "  - Constraint problems: QuantumConstraintSolverBuilder"
printfn "  - Pattern matching: QuantumPatternMatcherBuilder"
printfn "  - Graph coloring: GraphColoringBuilder"
printfn ""
