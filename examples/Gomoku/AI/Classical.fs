namespace FSharp.Azure.Quantum.Examples.Gomoku.AI

open FSharp.Azure.Quantum.Examples.Gomoku
open System

/// Simple classical AI for Gomoku using basic heuristics
/// This is intentionally simplified - the quantum solver will provide the sophistication
module Classical =
    
    /// Shared RNG for non-deterministic move selection
    let private rng = Random()
    
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
    
    /// Count consecutive pieces in a line from a position, returning (forward, backward)
    let private countConsecutiveDetail (board: Board) (pos: Position) (player: Cell) (dir: Direction) : int * int =
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
        (forward, backward)
    
    /// Count consecutive pieces total (forward + backward + 1 for pos itself)
    let private countConsecutive (board: Board) (pos: Position) (player: Cell) (dir: Direction) : int =
        let (fwd, bwd) = countConsecutiveDetail board pos player dir
        fwd + bwd + 1
    
    /// Check if both ends of a line are open
    let private isOpenEnded (board: Board) (pos: Position) (dir: Direction) (player: Cell) : bool =
        let (rowDelta, colDelta) = 
            match dir with
            | Horizontal -> (0, 1)
            | Vertical -> (1, 0)
            | DiagonalDown -> (1, 1)
            | DiagonalUp -> (1, -1)
        
        let (forward, backward) = countConsecutiveDetail board pos player dir
        
        // Front end: one step past the last piece in forward direction
        let frontPos = { Row = pos.Row + rowDelta * (forward + 1); Col = pos.Col + colDelta * (forward + 1) }
        // Back end: one step past the last piece in backward direction
        let backPos = { Row = pos.Row - rowDelta * (backward + 1); Col = pos.Col - colDelta * (backward + 1) }
        
        let frontOpen = Board.isValidPosition board frontPos && Board.isEmpty board frontPos
        let backOpen = Board.isValidPosition board backPos && Board.isEmpty board backPos
        
        frontOpen && backOpen
    
    /// Evaluate a single position for a player
    let evaluatePosition (board: Board) (pos: Position) (player: Cell) : float =
        if not (Board.isEmpty board pos) then
            0.0  // Position already occupied
        else
            // Simulate placing the piece AS the specified player
            let boardAsPlayer = { board with CurrentPlayer = player }
            match Board.makeMove boardAsPlayer pos with
            | Error _ -> 0.0
            | Ok newBoard ->
                // Check all directions for threats using functional fold
                let directions = [Horizontal; Vertical; DiagonalDown; DiagonalUp]
                
                // Collect per-direction threat info
                let directionThreats =
                    directions
                    |> List.map (fun dir ->
                        let count = countConsecutive newBoard pos player dir
                        let isOpen = isOpenEnded newBoard pos dir player
                        
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
                        
                        (dir, threat, count, isOpen))
                
                let threatScore = 
                    directionThreats 
                    |> List.sumBy (fun (_, threat, _, _) -> float (int threat))
                
                // Double-threat bonus: creating threats in multiple directions
                // is disproportionately strong because opponent can only block one
                let significantThreats =
                    directionThreats
                    |> List.filter (fun (_, threat, _, _) ->
                        threat >= ThreatLevel.OpenTwo)  // OpenTwo or better
                    |> List.length
                
                let openThreeOrBetter =
                    directionThreats
                    |> List.filter (fun (_, threat, _, _) ->
                        threat >= ThreatLevel.OpenThree)  // OpenThree or better
                    |> List.length
                
                let doubleThreatBonus =
                    if openThreeOrBetter >= 2 then
                        // Two open threes or better = virtually unblockable fork → forced win
                        75000.0
                    elif significantThreats >= 3 then
                        // Three directions with open-two or better = strong strategic position
                        8000.0
                    elif significantThreats >= 2 then
                        // Two directions with decent threats = good multi-threat position
                        2000.0
                    else
                        0.0
                
                // Center bonus: applied ONLY on the very first move (empty board)
                // to steer the opening to center without polluting later scoring.
                let centerBonus =
                    if List.isEmpty board.MoveHistory then
                        let centerRow = board.Config.Size / 2
                        let centerCol = board.Config.Size / 2
                        let distFromCenter = 
                            abs (pos.Row - centerRow) + abs (pos.Col - centerCol)
                        let maxDist = board.Config.Size
                        let normalizedProximity = float (maxDist - distFromCenter) / float maxDist
                        normalizedProximity * normalizedProximity * 50.0  // 0..50, quadratic
                    else
                        0.0
                
                threatScore + doubleThreatBonus + centerBonus
    
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
        
        let moveNumber = board.MoveHistory.Length + 1
        
        relevantMoves
        |> List.map (fun pos ->
            let offensiveScore = evaluatePosition board pos player
            let defensiveScore = evaluatePosition board pos (player.Opposite())
            
            // Temporal asymmetry: White plays more defensively in the opening
            // (first 50 moves), then matches Black's aggression. This simulates
            // Gomoku's natural first-player advantage — Black builds structural
            // pressure early while White focuses on blocking, then White ramps
            // up once the positional advantage is established. Breaks defensive
            // stalemates that cause 225-move draws.
            let offenseMultiplier =
                if player = White && moveNumber <= 50 then 1.1
                else 1.5
            let totalScore = offensiveScore * offenseMultiplier + defensiveScore * 1.0
            
            (pos, totalScore))
        |> List.sortByDescending snd
    
    /// Select best move using classical heuristics with symmetry-aware tiebreaking.
    /// Only randomizes between moves with effectively identical scores (within epsilon).
    /// This means symmetric positions like _XX_ get random tiebreaking, but a move
    /// that is clearly better always wins. Keeps games deterministic and easy to debug.
    let selectBestMove (board: Board) : Position option =
        let player = board.CurrentPlayer
        
        // FAST PRE-CHECK: Immediate threats take absolute priority
        match ThreatDetection.getImmediateThreat board with
        | Some threatPos -> Some threatPos
        | None ->
            // No immediate threat - proceed with normal evaluation
            let scored = evaluateMoves board player
            match scored with
            | [] -> None
            | [(pos, _)] -> Some pos
            | topMoves ->
                let bestScore = topMoves |> List.head |> snd
                
                // Symmetry-aware tiebreaking: only randomize when scores are
                // effectively identical (epsilon = 0.01). This captures true
                // symmetric positions (e.g. _XX_ endpoints) while ensuring
                // any meaningful score difference is always respected.
                let epsilon = 0.01
                let candidates =
                    topMoves
                    |> List.filter (fun (_, score) -> abs (score - bestScore) < epsilon)
                
                match candidates with
                | [] -> Some (fst topMoves.[0])
                | [single] -> Some (fst single)
                | tied ->
                    let idx = rng.Next(tied.Length)
                    Some (fst tied.[idx])
    
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
