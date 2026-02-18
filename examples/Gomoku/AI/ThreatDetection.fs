namespace FSharp.Azure.Quantum.Examples.Gomoku.AI

open FSharp.Azure.Quantum.Examples.Gomoku

/// Fast threat detection using pattern matching
/// Inspired by the efficient recursive check in the original Thomoku C# implementation
module ThreatDetection =
    
    /// Count pieces in a direction with gap tolerance
    let private countInDirectionWithGaps 
        (board: Board) 
        (pos: Position) 
        (player: Cell) 
        (dir: Direction)
        (maxGaps: int) : int * int =  // (total pieces, total gaps)
        
        let (rowDelta, colDelta) = 
            match dir with
            | Horizontal -> (0, 1)
            | Vertical -> (1, 0)
            | DiagonalDown -> (1, 1)
            | DiagonalUp -> (1, -1)
        
        let rec countDir offset gapsSoFar piecesSoFar =
            if gapsSoFar > maxGaps then (piecesSoFar, gapsSoFar)
            else
                let checkPos = { 
                    Row = pos.Row + rowDelta * offset
                    Col = pos.Col + colDelta * offset 
                }
                
                if not (Board.isValidPosition board checkPos) then
                    (piecesSoFar, gapsSoFar)
                else
                    match Board.getCell board checkPos with
                    | c when c = player -> 
                        countDir (offset + 1) gapsSoFar (piecesSoFar + 1)
                    | Cell.Empty when gapsSoFar < maxGaps -> 
                        countDir (offset + 1) (gapsSoFar + 1) piecesSoFar
                    | _ -> (piecesSoFar, gapsSoFar)
        
        // Count both directions
        let (fwdPieces, fwdGaps) = countDir 1 0 0
        let (bwdPieces, bwdGaps) = countDir -1 0 0
        
        (fwdPieces + bwdPieces, fwdGaps + bwdGaps)
    
    /// Check if position creates winning threat (5+ consecutive pieces, NO gaps)
    let private isWinningPosition (board: Board) (pos: Position) (player: Cell) : bool =
        // To check if a specific player would win at this position,
        // we need to temporarily set them as the current player
        let boardWithPlayer = { board with CurrentPlayer = player }
        
        // Simulate placing the piece
        match Board.makeMove boardWithPlayer pos with
        | Error _ -> false
        | Ok newBoard ->
            // Use Board's built-in win detection!
            Board.checkWinAtPosition newBoard pos player
    
    /// Check if position blocks opponent from winning
    let private isBlockingPosition (board: Board) (pos: Position) (opponent: Cell) : bool =
        // Check if opponent would win by playing here
        isWinningPosition board pos opponent
    
    /// Check if a position in a direction has an open end (empty cell beyond the line)
    let private hasOpenEnd (board: Board) (pos: Position) (dir: Direction) (offset: int) : bool =
        let (rowDelta, colDelta) = 
            match dir with
            | Horizontal -> (0, 1)
            | Vertical -> (1, 0)
            | DiagonalDown -> (1, 1)
            | DiagonalUp -> (1, -1)
        let endPos = { Row = pos.Row + rowDelta * offset; Col = pos.Col + colDelta * offset }
        Board.isValidPosition board endPos && Board.getCell board endPos = Cell.Empty

    /// Check if position creates open-ended four (will win next move)
    /// A true open four has 4 in a row with BOTH ends open — opponent cannot block both
    let private isOpenFour (board: Board) (pos: Position) (player: Cell) : bool =
        // Set the player as current player before simulating the move
        let boardWithPlayer = { board with CurrentPlayer = player }
        
        match Board.makeMove boardWithPlayer pos with
        | Error _ -> false
        | Ok newBoard ->
            let directions = [Horizontal; Vertical; DiagonalDown; DiagonalUp]
            directions
            |> List.exists (fun dir ->
                let (pieces, _) = countInDirectionWithGaps newBoard pos player dir 0
                let total = pieces + 1  // include the piece at pos
                if total >= 4 then
                    // Count how far forward/backward the line extends from pos
                    let (fwd, _) = countInDirectionWithGaps newBoard pos player dir 0
                    // fwd is forward count, we need to find the ends
                    let (rowDelta, colDelta) = 
                        match dir with
                        | Horizontal -> (0, 1)
                        | Vertical -> (1, 0)
                        | DiagonalDown -> (1, 1)
                        | DiagonalUp -> (1, -1)
                    // Scan forward to find end of line
                    let rec findEnd p d =
                        let next = { Row = p.Row + fst d; Col = p.Col + snd d }
                        if Board.isValidPosition newBoard next && Board.getCell newBoard next = player then
                            findEnd next d
                        else next
                    let fwdEnd = findEnd pos (rowDelta, colDelta)
                    let bwdEnd = findEnd pos (-rowDelta, -colDelta)
                    // Both ends must be empty (open)
                    let fwdOpen = Board.isValidPosition newBoard fwdEnd && Board.getCell newBoard fwdEnd = Cell.Empty
                    let bwdOpen = Board.isValidPosition newBoard bwdEnd && Board.getCell newBoard bwdEnd = Cell.Empty
                    fwdOpen && bwdOpen
                else false)
    
    /// Check if position creates dangerous three (can become open four next move)
    /// A dangerous three has 3 in a row with enough open space to reach 5
    let private isDangerousThree (board: Board) (pos: Position) (player: Cell) : bool =
        // Set the player as current player before simulating the move
        let boardWithPlayer = { board with CurrentPlayer = player }
        
        match Board.makeMove boardWithPlayer pos with
        | Error _ -> false
        | Ok newBoard ->
            let directions = [Horizontal; Vertical; DiagonalDown; DiagonalUp]
            directions
            |> List.exists (fun dir ->
                let (pieces, gaps) = countInDirectionWithGaps newBoard pos player dir 1
                let total = pieces + 1  // include the piece at pos
                if total >= 3 && gaps <= 1 then
                    // Verify there's enough room to extend to 5
                    let (rowDelta, colDelta) = 
                        match dir with
                        | Horizontal -> (0, 1)
                        | Vertical -> (1, 0)
                        | DiagonalDown -> (1, 1)
                        | DiagonalUp -> (1, -1)
                    let rec findEnd p d =
                        let next = { Row = p.Row + fst d; Col = p.Col + snd d }
                        if Board.isValidPosition newBoard next && Board.getCell newBoard next = player then
                            findEnd next d
                        else next
                    let fwdEnd = findEnd pos (rowDelta, colDelta)
                    let bwdEnd = findEnd pos (-rowDelta, -colDelta)
                    // At least one end must be open for the three to be dangerous
                    let fwdOpen = Board.isValidPosition newBoard fwdEnd && Board.getCell newBoard fwdEnd = Cell.Empty
                    let bwdOpen = Board.isValidPosition newBoard bwdEnd && Board.getCell newBoard bwdEnd = Cell.Empty
                    fwdOpen || bwdOpen
                else false)
    
    /// Get immediate threat that must be addressed
    /// Only handles truly urgent situations: winning moves and open fours
    /// Threes and lower threats are handled by the scoring function for strategic balance
    let getImmediateThreat (board: Board) : Position option =
        let currentPlayer = board.CurrentPlayer
        let opponent = currentPlayer.Opposite()
        let legalMoves = Board.getLegalMoves board
        
        // Filter moves near existing pieces for efficiency (unless early game)
        let relevantMoves =
            if legalMoves.Length > 100 && board.MoveHistory.Length > 4 then
                let occupied = Board.getOccupiedPositions board |> List.map fst
                legalMoves
                |> List.filter (fun pos ->
                    occupied |> List.exists (fun occ ->
                        abs (pos.Row - occ.Row) <= 2 && abs (pos.Col - occ.Col) <= 2))
            else
                legalMoves
        
        // Priority 1: Take our own winning move (if we can win, win immediately!)
        let ourWins = 
            relevantMoves 
            |> List.filter (fun pos -> isWinningPosition board pos currentPlayer)
        
        match ourWins with
        | winPos :: _ -> Some winPos
        | [] ->
            // Priority 2: Block opponent's immediate winning move
            let opponentWins = 
                relevantMoves 
                |> List.filter (fun pos -> isWinningPosition board pos opponent)
            
            match opponentWins with
            | winPos :: _ -> Some winPos
            | [] ->
                // Priority 3: Create our own open four (forces opponent to respond)
                let ourFours = 
                    relevantMoves 
                    |> List.filter (fun pos -> isOpenFour board pos currentPlayer)
                
                match ourFours with
                | fourPos :: _ -> Some fourPos
                | [] ->
                    // Priority 4: Block opponent's open four (will win next turn)
                    let opponentFours = 
                        relevantMoves 
                        |> List.filter (fun pos -> isOpenFour board pos opponent)
                    
                    match opponentFours with
                    | fourPos :: _ -> Some fourPos
                    | [] ->
                        // Threes and lower: handled by evaluateMoves scoring
                        // This allows the AI to play strategically instead of always reacting
                        None
    
    /// Get all threat positions sorted by urgency
    let getAllThreats (board: Board) : (Position * float) list =
        let currentPlayer = board.CurrentPlayer
        let opponent = currentPlayer.Opposite()
        let legalMoves = Board.getLegalMoves board
        
        legalMoves
        |> List.choose (fun pos ->
            // Score based on threat level
            let score =
                if isWinningPosition board pos currentPlayer then Some (pos, 100000.0)  // WIN!
                elif isWinningPosition board pos opponent then Some (pos, 90000.0)  // MUST BLOCK!
                elif isOpenFour board pos currentPlayer then Some (pos, 50000.0)  // Create four
                elif isOpenFour board pos opponent then Some (pos, 40000.0)  // Block four
                elif isDangerousThree board pos opponent then Some (pos, 10000.0)  // Block three
                elif isDangerousThree board pos currentPlayer then Some (pos, 8000.0)  // Create three
                else None
            score)
        |> List.sortByDescending snd
