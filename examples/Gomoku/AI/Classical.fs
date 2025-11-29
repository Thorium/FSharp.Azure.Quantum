namespace FSharp.Azure.Quantum.Examples.Gomoku.AI

open FSharp.Azure.Quantum.Examples.Gomoku

/// Simple classical AI for Gomoku using basic heuristics
/// This is intentionally simplified - the quantum solver will provide the sophistication
module Classical =
    
    /// Simple threat level based on consecutive pieces
    type ThreatLevel =
        | FiveInRow = 100000      // Immediate win
        | OpenFour = 10000        // Four in a row with both ends open (next move wins)
        | Four = 5000             // Four in a row (one end blocked)
        | OpenThree = 1000        // Three in a row with both ends open
        | Three = 500             // Three in a row (one end blocked)
        | OpenTwo = 100           // Two in a row with both ends open
        | Two = 50                // Two in a row (one end blocked)
        | One = 10                // Single piece
        | None = 0
    
    /// Count consecutive pieces in a line from a position
    let private countConsecutive (board: Board) (pos: Position) (player: Cell) (dir: Direction) : int =
        let (rowDelta, colDelta) = 
            match dir with
            | Horizontal -> (0, 1)
            | Vertical -> (1, 0)
            | DiagonalDown -> (1, 1)
            | DiagonalUp -> (1, -1)
        
        let rec countDirection pos delta =
            let nextPos = { Row = pos.Row + fst delta; Col = pos.Col + snd delta }
            if Board.isValidPosition board nextPos && Board.getCell board nextPos = player then
                1 + countDirection nextPos delta
            else
                0
        
        let forward = countDirection pos (rowDelta, colDelta)
        let backward = countDirection pos (-rowDelta, -colDelta)
        forward + backward + 1  // +1 for the position itself
    
    /// Check if both ends of a line are open
    let private isOpenEnded (board: Board) (pos: Position) (dir: Direction) (length: int) : bool =
        let (rowDelta, colDelta) = 
            match dir with
            | Horizontal -> (0, 1)
            | Vertical -> (1, 0)
            | DiagonalDown -> (1, 1)
            | DiagonalUp -> (1, -1)
        
        let frontPos = { Row = pos.Row + rowDelta * length; Col = pos.Col + colDelta * length }
        let backPos = { Row = pos.Row - rowDelta; Col = pos.Col - colDelta }
        
        let frontOpen = Board.isValidPosition board frontPos && Board.isEmpty board frontPos
        let backOpen = Board.isValidPosition board backPos && Board.isEmpty board backPos
        
        frontOpen && backOpen
    
    /// Evaluate a single position for a player
    let evaluatePosition (board: Board) (pos: Position) (player: Cell) : float =
        if not (Board.isEmpty board pos) then
            0.0  // Position already occupied
        else
            // Simulate placing the piece
            match Board.makeMove board pos with
            | None -> 0.0
            | Some newBoard ->
                let mutable score = 0.0
                
                // Check all directions for threats
                let directions = [Horizontal; Vertical; DiagonalDown; DiagonalUp]
                
                for dir in directions do
                    let count = countConsecutive newBoard pos player dir
                    let isOpen = isOpenEnded newBoard pos dir count
                    
                    let threat =
                        match count with
                        | n when n >= 5 -> ThreatLevel.FiveInRow
                        | 4 when isOpen -> ThreatLevel.OpenFour
                        | 4 -> ThreatLevel.Four
                        | 3 when isOpen -> ThreatLevel.OpenThree
                        | 3 -> ThreatLevel.Three
                        | 2 when isOpen -> ThreatLevel.OpenTwo
                        | 2 -> ThreatLevel.Two
                        | 1 -> ThreatLevel.One
                        | _ -> ThreatLevel.None
                    
                    score <- score + float (int threat)
                
                // Add positional bonus for center control
                let centerRow = board.Config.Size / 2
                let centerCol = board.Config.Size / 2
                let distFromCenter = 
                    abs (pos.Row - centerRow) + abs (pos.Col - centerCol)
                let centerBonus = float (board.Config.Size - distFromCenter) * 2.0
                
                score + centerBonus
    
    /// Evaluate all legal moves and return scored list
    let evaluateMoves (board: Board) (player: Cell) : (Position * float) list =
        let legalMoves = Board.getLegalMoves board
        
        // For efficiency, if there are too many moves, only consider moves near existing pieces
        let relevantMoves =
            if legalMoves.Length > 50 && not (List.isEmpty board.MoveHistory) then
                // Only consider moves within 2 spaces of existing pieces
                let occupied = Board.getOccupiedPositions board |> List.map fst
                legalMoves
                |> List.filter (fun pos ->
                    occupied |> List.exists (fun occ ->
                        abs (pos.Row - occ.Row) <= 2 && abs (pos.Col - occ.Col) <= 2
                    ))
            else
                legalMoves
        
        relevantMoves
        |> List.map (fun pos ->
            let offensiveScore = evaluatePosition board pos player
            let defensiveScore = evaluatePosition board pos (player.Opposite())
            
            // Weigh defensive and offensive play
            // INCREASE defensive weight significantly to prevent quick losses
            let totalScore = offensiveScore * 1.0 + defensiveScore * 2.0  // Was 1.1, now 2.0!
            
            (pos, totalScore))
        |> List.sortByDescending snd
    
    /// Select best move using classical heuristics
    let selectBestMove (board: Board) : Position option =
        let player = board.CurrentPlayer
        
        // FAST PRE-CHECK: Immediate threats take absolute priority
        match ThreatDetection.getImmediateThreat board with
        | Some threatPos -> Some threatPos
        | None ->
            // No immediate threat - proceed with normal evaluation
            let moves = evaluateMoves board player
            
            match moves with
            | [] -> None
            | (bestMove, score) :: _ ->
                // If we have an immediate win (or must block), take it
                if score >= float (int ThreatLevel.Four) then
                    Some bestMove
                else
                    // Return best scoring move
                    Some bestMove
    
    /// Get top N candidate moves for further analysis
    let getTopCandidates (board: Board) (n: int) : Position list =
        let player = board.CurrentPlayer
        let scored = evaluateMoves board player
        
        scored
        |> List.truncate n
        |> List.map fst
    
    /// Simple evaluation function for position scoring (used by quantum oracle)
    let simpleEvaluate (board: Board) (pos: Position) : float =
        let player = board.CurrentPlayer
        evaluatePosition board pos player
