namespace FSharp.Azure.Quantum.Examples.Gomoku.AI

open FSharp.Azure.Quantum.Examples.Gomoku
open System

/// Classical Gomoku AI: scores every empty cell with the pattern table (Evaluation.fs) and
/// plays the best one. One evaluation per cell, no look-ahead.
module Classical =

    type Style = Evaluation.Style

    /// Empty cells by score, best first. On an empty board only the centre is offered.
    let rankMovesWith (style: Style) (board: Board) : (Position * int) list =
        if List.isEmpty board.MoveHistory then
            let centre = board.Config.Size / 2
            [ ({ Row = centre; Col = centre }, 0) ]
        else
            Board.getLegalMoves board
            |> List.map (fun pos -> (pos, Evaluation.scoreCellWith style board pos))
            |> List.sortByDescending snd

    let rankMoves (board: Board) =
        rankMovesWith Evaluation.defaultStyle board

    /// A move that wins or stops the opponent winning, if there is one.
    let forcedMove (board: Board) : Position option =
        match rankMoves board with
        | (pos, score) :: _ when score >= Evaluation.BlockFive -> Some pos
        | _ -> None

    let private rng = Random()

    let private pickAmongBest (scored: (Position * int) list) : Position option =
        match scored with
        | [] -> None
        | _ ->
            let best = scored |> List.map snd |> List.max
            let tied = scored |> List.filter (fun (_, score) -> score = best)
            Some(fst tied.[rng.Next tied.Length])

    /// Play the best-scoring cell, picking at random between equally good ones.
    let selectBestMoveWith (style: Style) (board: Board) : Position option =
        pickAmongBest (rankMovesWith style board)

    let selectBestMove (board: Board) : Position option =
        selectBestMoveWith Evaluation.defaultStyle board

    /// The moves of a forcing win for `player` of at most `depth` moves drawn from
    /// `candidates`, by depth-first search that drops a line at its first non-forcing move.
    /// `steps` counts the forcing steps tried.
    let findForcingWin
        (candidates: Position list)
        (depth: int)
        (steps: int ref)
        (board: Board)
        (player: Cell)
        : Position list option =
        let rec search board depth =
            candidates
            |> List.tryPick (fun move ->
                steps.Value <- steps.Value + 1

                match Evaluation.forcingStep board player move with
                | Evaluation.Wins -> Some [ move ]
                | Evaluation.Blocked next when depth > 1 ->
                    search next (depth - 1) |> Option.map (fun rest -> move :: rest)
                | _ -> None)

        search board depth

    /// The pattern scan with a forcing-sequence look-ahead (Evaluation.decide), searched
    /// classically: the reference for the quantum AI, which takes the same steps by Grover
    /// search. Forcing sequences use at most `candidateLimit` cells and `depthFor` (candidate
    /// count) moves; pass the quantum AI's, so both search the same sequences. Also returns
    /// the evaluations made: cell scores plus forcing steps.
    let selectForcingMove (candidateLimit: int) (depthFor: int -> int) (board: Board) : Position option * int =
        let ranked = rankMoves board
        let steps = ref ranked.Length

        let bestCell () =
            let best = (pickAmongBest ranked).Value
            (best, ranked |> List.find (fst >> (=) best) |> snd)

        let forcingWin board player =
            let candidates = Evaluation.forcingCandidates candidateLimit board player
            findForcingWin candidates (depthFor candidates.Length) steps board player

        match ranked with
        | [] -> (None, 0)
        | _ ->
            let move, _ = Evaluation.decide bestCell forcingWin board
            (Some move, steps.Value)
