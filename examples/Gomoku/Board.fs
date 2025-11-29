namespace FSharp.Azure.Quantum.Examples.Gomoku

/// Represents a cell on the Gomoku board
type Cell =
    | Empty
    | Black
    | White
    
    member this.ToChar() =
        match this with
        | Empty -> '·'
        | Black -> '●'
        | White -> '○'
    
    member this.Opposite() =
        match this with
        | Black -> White
        | White -> Black
        | Empty -> Empty

/// Represents a position on the board
type Position = { Row: int; Col: int }

/// Represents a direction for checking win conditions
type Direction =
    | Horizontal
    | Vertical
    | DiagonalUp      // Bottom-left to top-right
    | DiagonalDown    // Top-left to bottom-right

/// Configuration for the Gomoku game
type BoardConfig = {
    Size: int          // Board size (15 for standard Gomoku, 19 for Go board)
    WinLength: int     // Number of consecutive pieces needed to win (default 5)
}

/// Static default configuration
module BoardConfig =
    let standard15x15 = { Size = 15; WinLength = 5 }
    let pro19x19 = { Size = 19; WinLength = 5 }

/// Represents the game board state
type Board = {
    Config: BoardConfig
    Cells: Cell[,]
    MoveHistory: Position list
    CurrentPlayer: Cell
}

/// Board module with functions for board operations
module Board =
    
    /// Create a new empty board
    let create (config: BoardConfig) : Board =
        {
            Config = config
            Cells = Array2D.create config.Size config.Size Empty
            MoveHistory = []
            CurrentPlayer = Black  // Black moves first
        }
    
    /// Check if a position is valid (within board bounds)
    let isValidPosition (board: Board) (pos: Position) : bool =
        pos.Row >= 0 && pos.Row < board.Config.Size &&
        pos.Col >= 0 && pos.Col < board.Config.Size
    
    /// Get the cell at a specific position
    let getCell (board: Board) (pos: Position) : Cell =
        if isValidPosition board pos then
            board.Cells.[pos.Row, pos.Col]
        else
            Empty
    
    /// Check if a position is empty
    let isEmpty (board: Board) (pos: Position) : bool =
        getCell board pos = Empty
    
    /// Get all legal moves (empty positions)
    let getLegalMoves (board: Board) : Position list =
        [ for row in 0 .. board.Config.Size - 1 do
            for col in 0 .. board.Config.Size - 1 do
                let pos = { Row = row; Col = col }
                if isEmpty board pos then yield pos ]
    
    /// Place a piece on the board
    let makeMove (board: Board) (pos: Position) : Board option =
        if not (isValidPosition board pos) then
            None
        elif not (isEmpty board pos) then
            None
        else
            // Create new cells array with the move
            let newCells = Array2D.copy board.Cells
            newCells.[pos.Row, pos.Col] <- board.CurrentPlayer
            
            Some {
                Config = board.Config
                Cells = newCells
                MoveHistory = pos :: board.MoveHistory
                CurrentPlayer = board.CurrentPlayer.Opposite()
            }
    
    /// Get direction offset for checking consecutive pieces
    let private getDirectionOffset (dir: Direction) : int * int =
        match dir with
        | Horizontal -> (0, 1)
        | Vertical -> (1, 0)
        | DiagonalDown -> (1, 1)
        | DiagonalUp -> (1, -1)
    
    /// Count consecutive pieces in a given direction from a starting position
    let private countInDirection (board: Board) (pos: Position) (player: Cell) (dir: Direction) : int =
        let (rowDelta, colDelta) = getDirectionOffset dir
        
        // Count in one direction (not including starting position)
        let rec countDir currentPos deltaRow deltaCol =
            if not (isValidPosition board currentPos) then
                0
            elif getCell board currentPos <> player then
                0
            else
                let nextPos = { Row = currentPos.Row + deltaRow; Col = currentPos.Col + deltaCol }
                1 + countDir nextPos deltaRow deltaCol
        
        // Count forward from position
        let forwardPos = { Row = pos.Row + rowDelta; Col = pos.Col + colDelta }
        let forward = countDir forwardPos rowDelta colDelta
        
        // Count backward from position
        let backwardPos = { Row = pos.Row - rowDelta; Col = pos.Col - colDelta }
        let backward = countDir backwardPos (-rowDelta) (-colDelta)
        
        // Total: forward + backward + 1 (for the position itself)
        forward + backward + 1
    
    /// Check if there's a win at a specific position for a given player
    let checkWinAtPosition (board: Board) (pos: Position) (player: Cell) : bool =
        if getCell board pos <> player then
            false
        else
            let directions = [Horizontal; Vertical; DiagonalDown; DiagonalUp]
            directions
            |> List.exists (fun dir -> 
                countInDirection board pos player dir >= board.Config.WinLength)
    
    /// Check if the game has been won
    let checkWin (board: Board) : Cell option =
        match board.MoveHistory with
        | [] -> None  // No moves yet
        | lastMove :: _ ->
            let lastPlayer = board.CurrentPlayer.Opposite()  // Previous player who just moved
            if checkWinAtPosition board lastMove lastPlayer then
                Some lastPlayer
            else
                None
    
    /// Check if the board is full (draw)
    let isFull (board: Board) : bool =
        board.MoveHistory.Length = board.Config.Size * board.Config.Size
    
    /// Get game status
    type GameStatus =
        | InProgress
        | Won of Cell
        | Draw
    
    let getGameStatus (board: Board) : GameStatus =
        match checkWin board with
        | Some winner -> Won winner
        | None when isFull board -> Draw
        | None -> InProgress
    
    /// Get all positions around a given position (for pattern matching)
    let getNeighborhood (board: Board) (pos: Position) (radius: int) : Position list =
        [ for dr in -radius .. radius do
            for dc in -radius .. radius do
                if dr <> 0 || dc <> 0 then
                    let neighborPos = { Row = pos.Row + dr; Col = pos.Col + dc }
                    if isValidPosition board neighborPos then
                        yield neighborPos ]
    
    /// Get all occupied positions on the board
    let getOccupiedPositions (board: Board) : (Position * Cell) list =
        [ for row in 0 .. board.Config.Size - 1 do
            for col in 0 .. board.Config.Size - 1 do
                let pos = { Row = row; Col = col }
                let cell = getCell board pos
                if cell <> Empty then yield (pos, cell) ]
    
    /// Convert board to string representation (for debugging)
    let toString (board: Board) : string =
        let sb = System.Text.StringBuilder()
        
        // Header row
        sb.Append("   ") |> ignore
        for col in 0 .. board.Config.Size - 1 do
            sb.Append(sprintf "%2d " col) |> ignore
        sb.AppendLine() |> ignore
        
        // Board rows
        for row in 0 .. board.Config.Size - 1 do
            sb.Append(sprintf "%2d " row) |> ignore
            for col in 0 .. board.Config.Size - 1 do
                let cell = board.Cells.[row, col]
                sb.Append(sprintf " %c " (cell.ToChar())) |> ignore
            sb.AppendLine() |> ignore
        
        sb.ToString()
