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
        | None -> false
        | Some newBoard ->
            // Use Board's built-in win detection!
            Board.checkWinAtPosition newBoard pos player
    
    /// Check if position blocks opponent from winning
    let private isBlockingPosition (board: Board) (pos: Position) (opponent: Cell) : bool =
        // Check if opponent would win by playing here
        isWinningPosition board pos opponent
    
    /// Check if position creates open-ended four (will win next move)
    let private isOpenFour (board: Board) (pos: Position) (player: Cell) : bool =
        // Set the player as current player before simulating the move
        let boardWithPlayer = { board with CurrentPlayer = player }
        
        match Board.makeMove boardWithPlayer pos with
        | None -> false
        | Some newBoard ->
            let directions = [Horizontal; Vertical; DiagonalDown; DiagonalUp]
            directions
            |> List.exists (fun dir ->
                let (pieces, _) = countInDirectionWithGaps newBoard pos player dir 0
                pieces + 1 >= 4)  // 4 in a row with no gaps = open four
    
    /// Check if position creates dangerous three (can become four next move)
    let private isDangerousThree (board: Board) (pos: Position) (player: Cell) : bool =
        // Set the player as current player before simulating the move
        let boardWithPlayer = { board with CurrentPlayer = player }
        
        match Board.makeMove boardWithPlayer pos with
        | None -> false
        | Some newBoard ->
            let directions = [Horizontal; Vertical; DiagonalDown; DiagonalUp]
            directions
            |> List.exists (fun dir ->
                let (pieces, gaps) = countInDirectionWithGaps newBoard pos player dir 1
                pieces + 1 >= 3 && gaps <= 1)
    
    /// Get immediate threat that must be addressed
    /// Priority: Block opponent win > Take our win > Block opponent four > Create our four
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
        
        // Priority 1: Block opponent's immediate winning move (CRITICAL!)
        let opponentWins = 
            relevantMoves 
            |> List.filter (fun pos -> isWinningPosition board pos opponent)
        
        match opponentWins with
        | winPos :: _ -> Some winPos
        | [] ->
            // Priority 2: Take our own winning move
            let ourWins = 
                relevantMoves 
                |> List.filter (fun pos -> isWinningPosition board pos currentPlayer)
            
            match ourWins with
            | winPos :: _ -> Some winPos
            | [] ->
                // Priority 3: Block opponent's open four (will win next turn)
                let opponentFours = 
                    relevantMoves 
                    |> List.filter (fun pos -> isOpenFour board pos opponent)
                
                match opponentFours with
                | fourPos :: _ -> Some fourPos
                | [] ->
                    // Priority 4: Create our own open four
                    let ourFours = 
                        relevantMoves 
                        |> List.filter (fun pos -> isOpenFour board pos currentPlayer)
                    
                    match ourFours with
                    | fourPos :: _ -> Some fourPos
                    | [] ->
                        // Priority 5: Block opponent's dangerous three
                        let opponentThrees = 
                            relevantMoves 
                            |> List.filter (fun pos -> isDangerousThree board pos opponent)
                        
                        match opponentThrees with
                        | threePos :: _ -> Some threePos
                        | [] -> None
    
    /// Get all threat positions sorted by urgency
    let getAllThreats (board: Board) : (Position * float) list =
        let currentPlayer = board.CurrentPlayer
        let opponent = currentPlayer.Opposite()
        let legalMoves = Board.getLegalMoves board
        
        legalMoves
        |> List.choose (fun pos ->
            // Score based on threat level
            let score =
                if isWinningPosition board pos opponent then Some (pos, 100000.0)  // MUST BLOCK!
                elif isWinningPosition board pos currentPlayer then Some (pos, 90000.0)  // WIN!
                elif isOpenFour board pos opponent then Some (pos, 50000.0)  // Block four
                elif isOpenFour board pos currentPlayer then Some (pos, 40000.0)  // Create four
                elif isDangerousThree board pos opponent then Some (pos, 10000.0)  // Block three
                elif isDangerousThree board pos currentPlayer then Some (pos, 8000.0)  // Create three
                else None
            score)
        |> List.sortByDescending snd
