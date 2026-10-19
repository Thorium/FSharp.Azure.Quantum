namespace FSharp.Azure.Quantum.Examples.Gomoku

open FSharp.Azure.Quantum.Examples.Gomoku.AI
open FSharp.Azure.Quantum.Examples.Gomoku.UI
open Spectre.Console

/// Main program module
module Program =

    /// AI player type
    type AIPlayer =
        | ClassicalAI of Evaluation.Style
        /// Classical exhaustive two-move look-ahead: the reference for the quantum search
        | ClassicalDeepAI
        | LocalQuantumAI
        | LocalHybridAI
        | TopologicalQuantumAI

    [<RequireQualifiedAccess>]
    type Source =
        | Threat
        | Grover
        | Classical
        | ClassicalDeep
        | HybridClassical
        | HybridQuantum

        override this.ToString() =
            match this with
            | Source.Threat -> "threat"
            | Source.Grover -> "grover"
            | Source.Classical -> "classical"
            | Source.ClassicalDeep -> "classical-deep"
            | Source.HybridClassical -> "hybrid-classical"
            | Source.HybridQuantum -> "hybrid-quantum"

    /// How an AI chose its move, and what a quantum search cost
    type MoveReport =
        {
            Source: Source
            Quantum: QuantumSearch.Metrics option
        }

    /// Game result
    type GameResult =
        {
            Winner: Cell option
            TotalMoves: int
            Reports: MoveReport list
            FinalBoard: Board // Added to preserve final board state
        }

    // Each backend is created once and kept for every move: setting one up is the costly
    // part to repeat, while a quantum state cannot be kept between searches.
    let private localBackend =
        lazy (FSharp.Azure.Quantum.Backends.LocalBackendFactory.createUnified ())

    // Ising encoding, 20 anyons → 9 logical qubits, so shallower forcing searches
    let private topologicalBackend =
        lazy (FSharp.Azure.Quantum.Topological.TopologicalUnifiedBackendFactory.createIsing 20)

    /// Play one turn (either player or AI)
    /// Returns (new board option, quit flag, how an AI chose its move)
    let playTurn
        (board: Board)
        (isAI: bool)
        (aiPlayer: AIPlayer option)
        (debug: bool)
        : (Board option * bool * MoveReport option) =
        if isAI then
            let quantumMove label backend =
                let move, metrics = QuantumSearch.selectMove board backend

                if debug then
                    ConsoleRenderer.displayInfo label

                    metrics |> Option.iter (QuantumSearch.describe >> ConsoleRenderer.displayInfo)

                (move,
                 Some
                     {
                         Source = Source.Grover
                         Quantum = metrics
                     })

            let pos, report =
                match aiPlayer with
                | Some(ClassicalAI style) ->
                    let source =
                        match Classical.forcedMove board with
                        | Some _ -> Source.Threat
                        | None -> Source.Classical

                    if debug then
                        ConsoleRenderer.displayInfo $"Classical pattern scan ({style}, {source})"

                    (Classical.selectBestMoveWith style board, Some { Source = source; Quantum = None })

                | Some ClassicalDeepAI ->
                    // The same forcing sequences the quantum AI searches on the local simulator
                    let move, evaluations =
                        Classical.selectForcingMove
                            QuantumSearch.ForcingCandidateLimit
                            (QuantumSearch.depthFor QuantumSearch.MaxSequenceQubits)
                            board

                    if debug then
                        ConsoleRenderer.displayInfo $"Classical forcing-sequence search: {evaluations} evaluations"

                    (move,
                     Some
                         {
                             Source = Source.ClassicalDeep
                             Quantum = None
                         })

                | Some LocalQuantumAI -> quantumMove "Local quantum simulator" localBackend.Value

                | Some TopologicalQuantumAI -> quantumMove "Topological quantum (Ising anyons)" topologicalBackend.Value

                | Some LocalHybridAI ->
                    let metrics = LocalHybrid.selectBestMove board localBackend.Value

                    if debug then
                        ConsoleRenderer.displayInfo (LocalHybrid.explainStrategy metrics)

                    let source =
                        match metrics.Strategy with
                        | LocalHybrid.Classical _ -> Source.HybridClassical
                        | LocalHybrid.Quantum _ -> Source.HybridQuantum

                    (metrics.Move,
                     Some
                         {
                             Source = source
                             Quantum = metrics.Quantum
                         })

                | None -> (None, None)

            match pos with
            | Some p ->
                (Board.makeMove board p)
                |> Result.map (fun newBoard -> Some newBoard, false, report)
                |> Result.defaultWith (fun _ -> None, false, None)
            | None -> (None, false, None)
        else
            // Human player
            // InputHandler finishes the frame (endFrame) once it has drawn the controls below
            let renderBoardWithCursor cursorPos =
                ConsoleRenderer.beginFrame board.Config.Size
                ConsoleRenderer.displayTitle ()
                ConsoleRenderer.renderBoard board cursorPos
                AnsiConsole.WriteLine()
                ConsoleRenderer.displayTurn board.CurrentPlayer

            match InputHandler.getValidPlayerMove board renderBoardWithCursor with
            | InputHandler.Move pos ->
                (Board.makeMove board pos)
                |> Result.map (fun newBoard -> Some newBoard, false, None)
                |> Result.defaultWith (fun _ -> None, false, None)
            | InputHandler.Quit -> (None, true, None) // Signal quit

    /// Game loop for player vs AI
    [<TailCall>]
    let rec gameLoop (board: Board) (aiPlayer: AIPlayer) (playerIsBlack: bool) : GameResult option =
        // Check game status FIRST
        match Board.getGameStatus board with
        | Board.Won winner ->
            Some
                {
                    Winner = Some winner
                    TotalMoves = board.MoveHistory.Length
                    Reports = []
                    FinalBoard = board
                }

        | Board.Draw ->
            Some
                {
                    Winner = None
                    TotalMoves = board.MoveHistory.Length
                    Reports = []
                    FinalBoard = board
                }

        | Board.InProgress ->
            // Display current state for AI turns
            let isAITurn =
                (board.CurrentPlayer = Black && not playerIsBlack)
                || (board.CurrentPlayer = White && playerIsBlack)

            if isAITurn then
                ConsoleRenderer.beginFrame board.Config.Size
                ConsoleRenderer.displayTitle ()
                ConsoleRenderer.renderBoard board None
                AnsiConsole.WriteLine()
                ConsoleRenderer.displayTurn board.CurrentPlayer
                ConsoleRenderer.endFrame ()

            let turn () =
                playTurn board isAITurn (Some aiPlayer) false

            // A spinner under the board while the AI searches, where Esc quits; the human's own
            // turn has the cursor controls instead
            let result =
                if isAITurn then
                    InputHandler.thinkUnlessQuit turn |> Option.defaultValue (None, true, None)
                else
                    turn ()

            match result with
            | (Some newBoard, false, _) -> gameLoop newBoard aiPlayer playerIsBlack
            | (None, true, _) ->
                // Player quit
                ConsoleRenderer.displayInfo "Game quit by player."
                None
            | (None, false, _) ->
                ConsoleRenderer.displayError "Invalid move!"
                InputHandler.waitForKey ()
                gameLoop board aiPlayer playerIsBlack
            | (Some _, true, _) ->
                // Shouldn't happen but handle it
                None

    /// Move log entry for debugging
    type MoveLogEntry =
        {
            MoveNumber: int
            Player: Cell
            Position: Position
            Source: Source
        }

    /// AI vs AI game for benchmarking
    [<TailCall>]
    let rec aiVsAiLoop
        (board: Board)
        (ai1: AIPlayer)
        (ai2: AIPlayer)
        (reports: MoveReport list)
        (moveLog: MoveLogEntry list)
        (debug: bool)
        : GameResult * MoveLogEntry list =
        let finished winner =
            ({
                Winner = winner
                TotalMoves = board.MoveHistory.Length
                Reports = List.rev reports
                FinalBoard = board
             },
             List.rev moveLog)

        // Check game status FIRST (before printing next move)
        match Board.getGameStatus board with
        | Board.Won winner -> finished (Some winner)
        | Board.Draw -> finished None
        | Board.InProgress ->
            let moveNum = board.MoveHistory.Length + 1
            let player = board.CurrentPlayer

            // Display current move only after confirming game is still in progress
            if debug then
                AnsiConsole.MarkupLine($"[grey]Move {moveNum}... {player}[/]")

            let currentAI = if player = Black then ai1 else ai2

            match playTurn board true (Some currentAI) debug with
            | (Some newBoard, _, Some report) ->
                let entry =
                    {
                        MoveNumber = moveNum
                        Player = player
                        Position = newBoard.MoveHistory.Head
                        Source = report.Source
                    }

                aiVsAiLoop newBoard ai1 ai2 (report :: reports) (entry :: moveLog) debug
            | _ -> finished None

    /// What the quantum searches of a game cost, against the classical search at the same depth
    let private quantumSummary (reports: MoveReport list) : (string * string) list =
        match reports |> List.choose (fun r -> r.Quantum) with
        | [] -> []
        | searches ->
            let total f = searches |> List.sumBy f
            let queries = total (fun m -> m.OracleQueries)
            let classical = total (fun m -> m.ClassicalEvaluations)

            [
                "Quantum searches", $"{searches.Length}"
                "Avg candidates", $"{searches |> List.averageBy (fun m -> float m.Candidates):F1}"
                "Max qubits", $"{searches |> List.map (fun m -> m.Qubits) |> List.max}"
                "Grover circuits", $"{total (fun m -> m.GroverRuns)}"
                "Oracle queries", $"{queries}"
                "Exhaustive enumeration, same search", $"{classical}"
                "Queries / enumeration", $"{float queries / float classical:F2}"
                "Simulator time", $"{(searches |> List.sumBy (fun m -> m.ElapsedMs)) / 1000.0:F1} s"
            ]

    /// Run player vs AI game
    let runPlayerVsAI (aiPlayer: AIPlayer) : unit =
        ConsoleRenderer.clear ()
        ConsoleRenderer.displayTitle ()
        ConsoleRenderer.displayRules ()

        let config = InputHandler.getBoardSize ()
        let board = Board.create config

        let playerColor =
            AnsiConsole.Confirm("[cyan]Do you want to play as Black (first player)?[/]", true)

        AnsiConsole.WriteLine()
        ConsoleRenderer.displayInfo "Starting game..."
        InputHandler.waitForKey ()

        match gameLoop board aiPlayer playerColor with
        | Some result ->
            // Display final result with the actual final board state
            ConsoleRenderer.clear ()
            ConsoleRenderer.displayTitle ()
            ConsoleRenderer.renderBoard result.FinalBoard None
            AnsiConsole.WriteLine()

            match result.Winner with
            | Some winner ->
                ConsoleRenderer.displayGameStatus (Board.Won winner)
                AnsiConsole.WriteLine()

                let isPlayerWin =
                    (winner = Black && playerColor) || (winner = White && not playerColor)

                if isPlayerWin then
                    ConsoleRenderer.displaySuccess "Congratulations! You won!"
                else
                    ConsoleRenderer.displayInfo "AI wins! Better luck next time."

            | None ->
                ConsoleRenderer.displayGameStatus Board.Draw
                AnsiConsole.WriteLine()
                ConsoleRenderer.displayInfo "Game ended in a draw!"

            AnsiConsole.WriteLine()
            AnsiConsole.MarkupLine($"[cyan]Total moves:[/] {result.TotalMoves}")
            AnsiConsole.WriteLine()
            InputHandler.waitForKey () // Wait for user to see result

        | None ->
            // Player quit
            ConsoleRenderer.displayInfo "Game was quit."

    /// The AIs to pick from in the menu, with their --ai-vs-ai names
    let private watchablePlayers =
        [
            "Classical (pattern scan)", "classical", ClassicalAI Evaluation.defaultStyle
            "Classical (forcing-sequence search)", "classical2", ClassicalDeepAI
            "Quantum (Grover, local simulator)", "quantum", LocalQuantumAI
            "Hybrid (classical opening, then quantum)", "hybrid", LocalHybridAI
            "Topological quantum (Ising anyons)", "topological", TopologicalQuantumAI
        ]

    /// Watch two AIs play each other on the board. The numbers behind each move are in the
    /// command-line mode (--ai-vs-ai), which this mode names at the end.
    let runWatchAiVsAi () : unit =
        ConsoleRenderer.clear ()
        ConsoleRenderer.displayTitle ()

        let pick (side: string) =
            let label =
                AnsiConsole.Prompt(
                    SelectionPrompt<string>()
                        .Title($"[cyan]Who plays {side}?[/]")
                        .AddChoices(watchablePlayers |> List.map (fun (label, _, _) -> label))
                )

            watchablePlayers |> List.find (fun (l, _, _) -> l = label)

        let blackLabel, blackName, blackAI = pick "Black (X, moves first)"
        let whiteLabel, whiteName, whiteAI = pick "White (O)"
        let board = Board.create (InputHandler.getBoardSize ())

        let render (board: Board) =
            ConsoleRenderer.beginFrame board.Config.Size
            ConsoleRenderer.displayTitle ()

            AnsiConsole.MarkupLine(
                $"[mediumpurple1]X[/] {Markup.Escape blackLabel}  vs  [lime]O[/] {Markup.Escape whiteLabel}"
            )

            ConsoleRenderer.renderBoard board None
            AnsiConsole.WriteLine()

        // Each move stays on screen at least this long, so fast AIs can be followed
        let minimumMoveMs = 400

        let rec watch (board: Board) =
            match Board.getGameStatus board with
            | Board.InProgress ->
                render board
                ConsoleRenderer.displayTurn board.CurrentPlayer
                ConsoleRenderer.endFrame ()

                let ai = if board.CurrentPlayer = Black then blackAI else whiteAI
                let stopwatch = System.Diagnostics.Stopwatch.StartNew()

                match InputHandler.thinkUnlessQuit (fun () -> playTurn board true (Some ai) false) with
                | None -> (board, Error "Game quit.")
                | Some(next, _, _) ->
                    let wait = minimumMoveMs - int stopwatch.ElapsedMilliseconds

                    if wait > 0 then
                        System.Threading.Thread.Sleep wait

                    match next with
                    | Some next -> watch next
                    | None -> (board, Error "The AI found no move.")
            | finished -> (board, Ok finished)

        let final, outcome = watch board
        render final
        ConsoleRenderer.endFrame ()

        match outcome with
        | Ok status -> ConsoleRenderer.displayGameStatus status
        | Error message -> ConsoleRenderer.displayInfo message

        AnsiConsole.MarkupLine($"[cyan]Total moves:[/] {final.MoveHistory.Length}")
        AnsiConsole.WriteLine()

        AnsiConsole.MarkupLine(
            $"[grey]Search costs and the move log for this pairing: Gomoku --ai-vs-ai {blackName} {whiteName}[/]"
        )

        InputHandler.waitForKey ()

    /// Main entry point
    [<EntryPoint>]
    let main args =
        try
            // Check for command-line arguments for automated AI vs AI
            match args with
            | [| "--ai-vs-ai"; ai1Name; ai2Name |]
            | [| "--ai-vs-ai"; ai1Name; ai2Name; "--debug" |] ->
                let debug = Array.contains "--debug" args

                // Parse AI player types
                let parseAI (name: string) =
                    match name.ToLower() with
                    | "classical" -> Some(ClassicalAI Evaluation.defaultStyle)
                    | "attacker" -> Some(ClassicalAI Evaluation.Attacker)
                    | "defender" -> Some(ClassicalAI Evaluation.Defender)
                    | "pressure" -> Some(ClassicalAI Evaluation.Pressure)
                    | "builder" -> Some(ClassicalAI Evaluation.Builder)
                    | "classical2"
                    | "deep" -> Some ClassicalDeepAI
                    | "quantum"
                    | "localquantum" -> Some LocalQuantumAI
                    | "hybrid"
                    | "localhybrid" -> Some LocalHybridAI
                    | "topological"
                    | "topo" -> Some TopologicalQuantumAI
                    | _ -> None

                match parseAI ai1Name, parseAI ai2Name with
                | Some ai1, Some ai2 ->
                    if not debug then
                        // Quiet mode - just print progress indicator
                        AnsiConsole.MarkupLine($"[cyan]Running: {ai1Name} vs {ai2Name}...[/]")
                    else
                        // Debug mode - full verbose output
                        ConsoleRenderer.clear ()
                        ConsoleRenderer.displayTitle ()

                        AnsiConsole.MarkupLine(
                            $"[bold yellow]Automated Match: {ai1Name} (Black) vs {ai2Name} (White)[/]"
                        )

                        AnsiConsole.WriteLine()

                    let config = BoardConfig.standard15x15
                    let board = Board.create config

                    let startTime = System.Diagnostics.Stopwatch.StartNew()
                    let (result, moveLog) = aiVsAiLoop board ai1 ai2 [] [] debug
                    startTime.Stop()

                    // Display final board and results
                    if debug then
                        ConsoleRenderer.clear ()
                        ConsoleRenderer.displayTitle ()
                        ConsoleRenderer.renderBoard result.FinalBoard None
                        AnsiConsole.WriteLine()

                    match result.Winner with
                    | Some winner ->
                        if debug then
                            ConsoleRenderer.displayGameStatus (Board.Won winner)
                            AnsiConsole.WriteLine()

                        let winnerName = if winner = Black then ai1Name else ai2Name
                        AnsiConsole.MarkupLine($"[bold green]{winnerName} wins![/]")
                    | None ->
                        if debug then
                            ConsoleRenderer.displayGameStatus Board.Draw
                            AnsiConsole.WriteLine()

                        AnsiConsole.MarkupLine("[yellow]Draw[/]")

                    AnsiConsole.MarkupLine($"[cyan]Total moves:[/] {result.TotalMoves}")

                    if debug then
                        AnsiConsole.MarkupLine($"[cyan]Game time:[/] {startTime.Elapsed.TotalSeconds:F2} seconds")

                        AnsiConsole.MarkupLine(
                            $"[cyan]Avg move time:[/] {startTime.Elapsed.TotalMilliseconds / float result.TotalMoves:F0} ms"
                        )

                    for (name, value) in quantumSummary result.Reports do
                        AnsiConsole.MarkupLine($"[cyan]{name}:[/] {value}")

                    // Always print move coordinate log
                    AnsiConsole.WriteLine()
                    AnsiConsole.MarkupLine("[bold cyan]Move Log (row,col):[/]")

                    for entry in moveLog do
                        let playerChar = if entry.Player = Black then "X" else "O"

                        let sourceTag =
                            match entry.Source with
                            | Source.Threat -> " [red]<THREAT>[/]"
                            | Source.Grover -> " [magenta]<GROVER>[/]"
                            | Source.Classical -> ""
                            | s -> $" [grey]<{s}>[/]"

                        AnsiConsole.MarkupLine(
                            $"  {entry.MoveNumber, 3}. {playerChar} ({entry.Position.Row},{entry.Position.Col}){sourceTag}"
                        )

                    0 // Success

                | _ ->
                    ConsoleRenderer.displayError
                        "Invalid AI player names. Use: classical, classical2, attacker, defender, pressure, builder, quantum, hybrid, or topological"

                    AnsiConsole.WriteLine()
                    AnsiConsole.MarkupLine("[cyan]Usage:[/] Gomoku --ai-vs-ai <ai1> <ai2> [--debug]")
                    AnsiConsole.MarkupLine("[cyan]Example:[/] Gomoku --ai-vs-ai classical quantum")
                    1 // Error

            | [| "--help" |]
            | [| "-h" |] ->
                ConsoleRenderer.displayTitle ()
                AnsiConsole.MarkupLine("[bold cyan]Usage:[/]")
                AnsiConsole.WriteLine()
                AnsiConsole.MarkupLine("  [yellow]Gomoku[/]                                 - Start interactive game")

                AnsiConsole.MarkupLine(
                    "  [yellow]Gomoku --ai-vs-ai <ai1> <ai2>[/]         - Run automated AI vs AI match (quiet)"
                )

                AnsiConsole.MarkupLine(
                    "  [yellow]Gomoku --ai-vs-ai <ai1> <ai2> --debug[/] - Run with verbose debug output"
                )

                AnsiConsole.MarkupLine("  [yellow]Gomoku --help[/]                          - Show this help")
                AnsiConsole.WriteLine()
                AnsiConsole.MarkupLine("[bold cyan]AI Players:[/]")
                AnsiConsole.MarkupLine("  [green]classical[/]     - Classical pattern-table AI (builder style)")

                AnsiConsole.MarkupLine(
                    "  [green]attacker[/], [green]defender[/], [green]pressure[/], [green]builder[/] - Classical AI in a given style"
                )

                AnsiConsole.MarkupLine(
                    "  [green]classical2[/]    - Classical forcing-sequence search (the quantum AI's steps, searched classically)"
                )

                AnsiConsole.MarkupLine(
                    "  [green]quantum[/]       - Quantum forcing-sequence search by Grover (local simulator, up to 16 qubits)"
                )

                AnsiConsole.MarkupLine("  [green]hybrid[/]        - Classical for the opening, then the quantum search")

                AnsiConsole.MarkupLine(
                    "  [green]topological[/]   - Quantum search by Grover via Ising anyon braiding (9 qubits)"
                )

                AnsiConsole.WriteLine()
                AnsiConsole.MarkupLine("[bold cyan]Examples:[/]")
                AnsiConsole.MarkupLine("  Gomoku --ai-vs-ai classical quantum          # Quiet mode (default)")
                AnsiConsole.MarkupLine("  Gomoku --ai-vs-ai quantum topological --debug # Gate-based vs topological")
                AnsiConsole.MarkupLine("  Gomoku --ai-vs-ai hybrid hybrid")
                0

            | _ ->
                // Interactive mode (original behavior)
                ConsoleRenderer.clear ()
                ConsoleRenderer.displayTitle ()

                let mutable running = true

                while running do
                    let mode = InputHandler.getValidGameMode ()

                    match mode with
                    | 1 -> runPlayerVsAI (ClassicalAI Evaluation.defaultStyle)
                    | 2 -> runPlayerVsAI LocalQuantumAI
                    | 3 -> runPlayerVsAI LocalHybridAI
                    | 4 -> runWatchAiVsAi ()
                    | 5 ->
                        running <- false
                        ConsoleRenderer.displaySuccess "Thanks for playing!"
                    | _ -> ConsoleRenderer.displayError "Invalid choice!"

                    if running then
                        AnsiConsole.WriteLine()

                        if not (ConsoleRenderer.confirm "Play another game?") then
                            running <- false
                            ConsoleRenderer.displaySuccess "Thanks for playing!"

                0 // Success exit code

        with ex ->
            ConsoleRenderer.displayError $"Fatal error: {ex.Message}"
            ConsoleRenderer.displayError $"Stack trace: {ex.StackTrace}"
            1 // Error exit code
