namespace FSharp.Azure.Quantum.Examples.Gomoku.AI

open FSharp.Azure.Quantum.Examples.Gomoku
open System

/// What a Gomoku move is worth: the pattern table of LASKIN.C (THO-MOKU 4.0B by
/// Tuomas Hietanen). This is the game knowledge both AIs share; they differ only in how
/// they search with it (Classical.fs scans, Quantum.fs uses Grover).
///
/// An empty cell is scored by matching a table of known line shapes through it in all four
/// directions and adding up their scores. The table holds both the side to move's shapes
/// (attack) and the opponent's (defence), so one number covers both.
///
/// Fixes over the original C:
/// - Every shape is matched in both directions along a line. The C table listed most
///   mirror images by hand but missed some, so it played differently left-to-right.
/// - Cells off the board are walls. The C padded the board with empty cells, so lines
///   running into the edge looked open.
/// - Completing five and blocking five are scored far above everything else, so no sum
///   of lesser shapes can outrank them.
/// - The scan no longer reads one entry past the end of the table, pattern 33 (a mistyped
///   mirror of 32) is gone, and the Defender style's mismatched 59/60 scores agree.
module Evaluation =

    /// Weightings from LASKIN.C. They differ in how much they value building their own
    /// shapes against blocking the opponent's.
    type Style =
        | Attacker
        | Defender
        | Pressure
        | Builder

    /// Builder won most often in round-robin AI-vs-AI matches of the four styles.
    let defaultStyle = Builder

    /// Score for completing five, and for blocking the opponent's five.
    [<Literal>]
    let Five = 100_000_000

    [<Literal>]
    let BlockFive = 1_000_000

    /// Line shapes, read along a line through the scored cell:
    ///   X  stone of the side to move       O  opponent's stone
    ///   _  empty, and the cell being scored (every _ in a shape is scored)
    ///   .  empty, not scored
    ///   x  own stone or the board edge     o  opponent's stone or the board edge
    /// Each shape is also matched mirrored, so only one of a mirror pair is listed.
    /// Scores are (Attacker, Defender, Pressure, Builder).
    let private table =
        [
            // Five
            "XX_XX", (Five, Five, Five, Five)
            "XXX_X", (Five, Five, Five, Five)
            "XXXX_", (Five, Five, Five, Five)
            "OO_OO", (BlockFive, BlockFive, BlockFive, BlockFive)
            "O_OOO", (BlockFive, BlockFive, BlockFive, BlockFive)
            "OOOO_", (BlockFive, BlockFive, BlockFive, BlockFive)
            // Own four
            ".XX_X", (5000, 2500, 2700, 5000)
            ".X_XX", (5000, 2500, 2700, 5000)
            "XXX_.", (5000, 2500, 2700, 4950)
            "oXXX._", (5000, 2500, 2700, 5000)
            "XX.X_", (3000, 2400, 3000, 3000)
            "_XX.X", (3000, 2400, 3000, 3000)
            "X__XX", (2900, 2000, 2900, 2900)
            "X_X_X", (2900, 2000, 2900, 2900)
            // Opponent's open three, and the gapped double four
            "O_O_O_O", (14000, 14000, 14000, 14000)
            "__OOO_x", (2000, 2900, 2000, 2000)
            "._OOO_.", (2000, 2900, 2000, 2000)
            "_O_OO_", (2000, 2900, 2000, 2000)
            "..O_OO..", (30, 50, 30, 20)
            "._OOO.x", (30, 50, 30, 20)
            // Own open three
            "._XX.", (500, 400, 500, 360)
            ".X_X.", (500, 400, 500, 370)
            ".X__X.", (490, 390, 490, 490)
            "._X.X.", (490, 390, 490, 490)
            ".XX._.", (490, 390, 490, 490)
            // Opponent's closed three
            "xOOO__", (350, 350, 420, 350)
            "xOO_O_", (350, 350, 400, 350)
            "xO_OO_", (350, 350, 400, 350)
            "x_OOO_", (350, 350, 400, 350)
            // Opponent's open two
            "._OO.", (200, 400, 200, 200)
            ".O_O.", (200, 400, 200, 200)
            ".O__O.", (190, 380, 220, 110)
            "._O.O.", (190, 380, 220, 140)
            ".OO._.", (80, 90, 90, 80)
            // Loose shapes
            "._..XX.", (80, 90, 90, 90)
            ".X_.", (60, 30, 60, 50)
            ".X._.", (50, 15, 50, 35)
            ".O_.", (40, 45, 40, 40)
            ".O._.", (25, 25, 25, 25)
            ".X._.X.", (15, 15, 15, 20)
            ".X.._X.", (10, 10, 10, 15)
            ".X.._.", (10, 10, 10, 15)
            "._..OO.", (10, 10, 10, 15)
            ".O._.O.", (5, 5, 5, 7)
            ".O.._O.", (2, 2, 2, 3)
        ]

    /// A shape with the offsets of its scored cells.
    type private Shape =
        {
            Cells: char[]
            Targets: int[]
            Scores: int * int * int * int
        }

    /// The table plus every mirror image that differs from its original.
    let private shapes =
        table
        |> List.collect (fun (shape, scores) ->
            [ shape; String(Array.rev (shape.ToCharArray())) ]
            |> List.distinct
            |> List.map (fun s ->
                {
                    Cells = s.ToCharArray()
                    Targets =
                        [|
                            for i in 0 .. s.Length - 1 do
                                if s.[i] = '_' then
                                    yield i
                        |]
                    Scores = scores
                }))
        |> Array.ofList

    let private scoreFor style (a, d, p, b) =
        match style with
        | Attacker -> a
        | Defender -> d
        | Pressure -> p
        | Builder -> b

    let private directions = [| (0, 1); (1, 0); (1, 1); (1, -1) |]

    /// Score an empty cell for the side to move; occupied cells score 0.
    let scoreCellWith (style: Style) (board: Board) (pos: Position) : int =
        if not (Board.isEmpty board pos) then
            0
        else
            let size = board.Config.Size
            let me = board.CurrentPlayer
            let opponent = me.Opposite()

            let matches row col ch =
                let onBoard = row >= 0 && row < size && col >= 0 && col < size
                let cell = if onBoard then board.Cells.[row, col] else Empty

                match ch with
                | 'X' -> onBoard && cell = me
                | 'O' -> onBoard && cell = opponent
                | 'x' -> not onBoard || cell = me
                | 'o' -> not onBoard || cell = opponent
                | _ -> onBoard && cell = Empty

            let mutable total = 0

            for (dr, dc) in directions do
                for shape in shapes do
                    for target in shape.Targets do
                        // Line the shape up so its scored cell sits on pos
                        let row0 = pos.Row - target * dr
                        let col0 = pos.Col - target * dc

                        let rec fits i =
                            i = shape.Cells.Length
                            || (matches (row0 + i * dr) (col0 + i * dc) shape.Cells.[i] && fits (i + 1))

                        if fits 0 then
                            total <- total + scoreFor style shape.Scores

            total

    let scoreCell (board: Board) (pos: Position) : int = scoreCellWith defaultStyle board pos

    /// Empty cells worth considering: those within two cells of a stone, the ones next to a
    /// stone first, and among each the ones nearest the last move first; at most `limit`.
    /// Every cell that completes or blocks five lies within two cells of a stone. On an
    /// empty board this is the centre.
    let candidateMoves (limit: int) (board: Board) : Position list =
        let size = board.Config.Size

        match board.MoveHistory with
        | [] -> [ { Row = size / 2; Col = size / 2 } ]
        | lastMove :: _ ->
            let distanceToStone (pos: Position) =
                seq {
                    for dr in -2 .. 2 do
                        for dc in -2 .. 2 do
                            let near =
                                {
                                    Row = pos.Row + dr
                                    Col = pos.Col + dc
                                }

                            if Board.isValidPosition board near && not (Board.isEmpty board near) then
                                yield max (abs dr) (abs dc)
                }
                |> Seq.fold min 3

            let distanceToLast (pos: Position) =
                max (abs (pos.Row - lastMove.Row)) (abs (pos.Col - lastMove.Col))

            Board.getLegalMoves board
            |> List.map (fun pos -> (pos, distanceToStone pos))
            |> List.filter (fun (_, d) -> d <= 2)
            |> List.sortBy (fun (pos, d) -> (d, distanceToLast pos))
            |> List.truncate limit
            |> List.map fst

    // ------------------------------------------------------------------------
    // Forcing sequences ("victory by continuous fours"): the player makes a four with every
    // move, so each opponent reply is forced, until the opponent cannot block any more.
    // ------------------------------------------------------------------------

    let private onBoard (board: Board) row col =
        row >= 0 && row < board.Config.Size && col >= 0 && col < board.Config.Size

    /// Would `player` complete five by playing the empty cell `pos`?
    let completesFive (board: Board) (player: Cell) (pos: Position) : bool =
        let run dr dc =
            let rec go i =
                let row = pos.Row + i * dr
                let col = pos.Col + i * dc

                if onBoard board row col && board.Cells.[row, col] = player then
                    go (i + 1)
                else
                    i - 1

            go 1

        directions
        |> Array.exists (fun (dr, dc) -> 1 + run dr dc + run -dr -dc >= board.Config.WinLength)

    /// Empty cells where `player` completes five along a line through `stone`.
    let private fiveCellsThrough (board: Board) (player: Cell) (stone: Position) : Position list =
        let reach = board.Config.WinLength - 1

        [
            for (dr, dc) in directions do
                for offset in -reach .. reach do
                    let pos =
                        {
                            Row = stone.Row + offset * dr
                            Col = stone.Col + offset * dc
                        }

                    // Board.isEmpty is true off the board too, so check the bounds first
                    if
                        offset <> 0
                        && Board.isValidPosition board pos
                        && Board.isEmpty board pos
                        && completesFive board player pos
                    then
                        yield pos
        ]
        |> List.distinct

    let private place (board: Board) (player: Cell) (pos: Position) : Board =
        Board.makeMove { board with CurrentPlayer = player } pos
        |> Result.defaultWith failwith

    /// Does `player` already have a four, a cell where they would complete five?
    let hasFour (board: Board) (player: Cell) : bool =
        Board.getOccupiedPositions board
        |> List.exists (fun (stone, cell) -> cell = player && not (fiveCellsThrough board player stone).IsEmpty)

    /// One move of a forcing sequence.
    type ForcingStep =
        /// The move wins: it completes five, or makes two fours the opponent cannot both block
        | Wins
        /// The move makes one four; the opponent has blocked it on this board
        | Blocked of Board
        /// The move is taken, makes no four, or the forced block gives the opponent a four
        | Fails

    let forcingStep (board: Board) (player: Cell) (move: Position) : ForcingStep =
        if not (Board.isEmpty board move) then
            Fails
        elif completesFive board player move then
            Wins
        else
            let afterMove = place board player move

            match fiveCellsThrough afterMove player move with
            | [] -> Fails
            | [ block ] ->
                let opponent = player.Opposite()
                let afterBlock = place afterMove opponent block

                if (fiveCellsThrough afterBlock opponent block).IsEmpty then
                    Blocked afterBlock
                else
                    Fails
            | _ -> Wins

    /// If these moves, played in turn by `player` with the opponent's forced blocks between
    /// them, win: every cell the line uses, moves and blocks in the order played. Moves after
    /// the winning one are ignored. Assumes the opponent has no four to begin with (they
    /// would complete it instead of blocking).
    let forcingLine (board: Board) (player: Cell) (moves: Position list) : Position list option =
        let rec go board moves cells =
            match moves with
            | [] -> None
            | move :: rest ->
                match forcingStep board player move with
                | Wins -> Some(List.rev (move :: cells))
                | Blocked next -> go next rest (next.MoveHistory.Head :: move :: cells)
                | Fails -> None

        go board moves []

    /// The steps both AIs take to choose a move, given their own ways to search:
    /// - `bestCell`: the highest-scoring candidate cell and its score
    /// - `forcingWin board player`: the moves of a forcing win for `player`, if one is found
    /// 1. If the best cell wins now or blocks the opponent's five, play it.
    /// 2. Play the first move of a forcing win of its own.
    /// 3. If the opponent has a forcing win, play a cell after which they have none left: a
    ///    cell of their winning line, or else a cell of a line they still have after one of
    ///    those (or the line's first cell if nothing stops them).
    /// 4. Otherwise play the best cell.
    /// Returns the move and which step chose it.
    let decide
        (bestCell: unit -> Position * int)
        (forcingWin: Board -> Cell -> Position list option)
        (board: Board)
        : Position * string =
        let me = board.CurrentPlayer
        let opponent = me.Opposite()
        let best, score = bestCell ()

        if score >= BlockFive then
            (best, "wins now or blocks five")
        else
            match forcingWin board me with
            | Some(first :: _) -> (first, "forcing win")
            | _ ->
                match forcingWin board opponent with
                | Some moves ->
                    let lineOf board moves =
                        forcingLine board opponent moves |> Option.defaultValue moves

                    let line = lineOf board moves

                    // Each cell of the line, and the opponent's forcing win, if any, after it
                    let tries =
                        line
                        |> List.map (fun cell ->
                            let after = place board me cell
                            (cell, after, forcingWin after opponent))

                    match tries |> List.tryFind (fun (_, _, left) -> left.IsNone) with
                    | Some(cell, _, _) -> (cell, "stops the opponent's forcing win")
                    | None ->
                        // Every cell leaves them another line; a cell of one of those may stop
                        // both (a block on the first line and one on the second at once)
                        let secondLines =
                            tries
                            |> List.collect (fun (_, after, left) -> lineOf after (Option.defaultValue [] left))
                            |> List.distinct
                            |> List.filter (fun cell -> not (List.contains cell line))

                        match
                            secondLines
                            |> List.tryFind (fun cell -> (forcingWin (place board me cell) opponent).IsNone)
                        with
                        | Some cell -> (cell, "stops the opponent's forcing win")
                        | None -> (List.head line, "cannot stop the opponent's forcing win")
                | None -> (best, "best cell")

    /// Cells a forcing sequence of `player` can use: empty cells in a line of five free of the
    /// opponent's stones that holds at least two of `player`'s, the fullest lines first; at
    /// most `limit`.
    let forcingCandidates (limit: int) (board: Board) (player: Cell) : Position list =
        let length = board.Config.WinLength
        let opponent = player.Opposite()

        let fullestLine (pos: Position) =
            seq {
                for (dr, dc) in directions do
                    for start in -(length - 1) .. 0 do
                        let cells =
                            [ for i in start .. start + length - 1 -> (pos.Row + i * dr, pos.Col + i * dc) ]

                        if
                            cells
                            |> List.forall (fun (r, c) -> onBoard board r c && board.Cells.[r, c] <> opponent)
                        then
                            yield cells |> List.filter (fun (r, c) -> board.Cells.[r, c] = player) |> List.length
            }
            |> Seq.fold max 0

        Board.getLegalMoves board
        |> List.map (fun pos -> (pos, fullestLine pos))
        |> List.filter (fun (_, stones) -> stones >= 2)
        |> List.sortByDescending snd
        |> List.truncate limit
        |> List.map fst
