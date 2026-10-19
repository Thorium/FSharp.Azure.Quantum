namespace FSharp.Azure.Quantum.Examples.Gomoku.UI

open FSharp.Azure.Quantum.Examples.Gomoku
open Spectre.Console
open System

/// Input handler for player moves
module InputHandler =

    /// Result of player input (move or quit)
    type PlayerInput =
        | Move of Position
        | Quit

    /// Get player move using cursor navigation (arrow keys + Enter) or typing coordinates
    /// Returns cursor position updates as we navigate for live board rendering
    let getPlayerMoveWithCursor (board: Board) (renderBoard: Position option -> unit) : PlayerInput option =
        try
            // The cursor starts on the last move played (the opponent's), or on the centre of
            // an empty board.
            let start =
                match board.MoveHistory with
                | lastMove :: _ -> lastMove
                | [] ->
                    {
                        Row = board.Config.Size / 2
                        Col = board.Config.Size / 2
                    }

            let mutable cursorRow = start.Row
            let mutable cursorCol = start.Col
            let mutable quit = false
            let mutable confirmed = false
            let mutable useTyping = false

            // One whole frame: the board with the cursor, the controls and the cursor position.
            // The position line is padded so a shorter one covers a longer one drawn before it.
            let redraw () =
                renderBoard (Some { Row = cursorRow; Col = cursorCol })

                AnsiConsole.MarkupLine(
                    "[cyan]Controls:[/] ↑↓←→ Move | [green]Enter[/] Place | [green]T[/] Type coords | [red]Esc/Q[/] Quit"
                )

                AnsiConsole.MarkupLine($"[yellow]→[/] Row {cursorRow}, Col {cursorCol}    ")
                ConsoleRenderer.endFrame ()

            redraw ()

            while not ((quit || confirmed) || useTyping) do
                let key = Console.ReadKey(true)

                match key.Key with
                | ConsoleKey.UpArrow ->
                    cursorRow <- max 0 (cursorRow - 1)
                    redraw ()
                | ConsoleKey.DownArrow ->
                    cursorRow <- min (board.Config.Size - 1) (cursorRow + 1)
                    redraw ()
                | ConsoleKey.LeftArrow ->
                    cursorCol <- max 0 (cursorCol - 1)
                    redraw ()
                | ConsoleKey.RightArrow ->
                    cursorCol <- min (board.Config.Size - 1) (cursorCol + 1)
                    redraw ()
                | ConsoleKey.Enter -> confirmed <- true
                | ConsoleKey.Escape
                | ConsoleKey.Q -> quit <- true
                | ConsoleKey.T -> useTyping <- true
                | _ -> ()

            if quit then
                Some Quit
            elif useTyping then
                // Fall back to typing coordinates
                AnsiConsole.WriteLine()
                let row = AnsiConsole.Ask<int>($"[cyan]Enter row (0-{board.Config.Size - 1}):[/] ")

                let col =
                    AnsiConsole.Ask<int>($"[cyan]Enter column (0-{board.Config.Size - 1}):[/] ")

                let pos = { Row = row; Col = col }

                if not (Board.isValidPosition board pos) then
                    ConsoleRenderer.displayError "Position is outside the board!"
                    None
                elif not (Board.isEmpty board pos) then
                    ConsoleRenderer.displayError "Position is already occupied!"
                    None
                else
                    Some(Move pos)
            else
                let pos = { Row = cursorRow; Col = cursorCol }

                if not (Board.isEmpty board pos) then
                    ConsoleRenderer.displayError "Position is already occupied!"
                    None
                else
                    Some(Move pos)

        with
        | :? FormatException ->
            ConsoleRenderer.displayError "Invalid input! Please enter numbers only."
            None
        | ex ->
            ConsoleRenderer.displayError $"Error: {ex.Message}"
            None

    /// Get player move with retry logic - wrapper for live cursor rendering
    [<TailCall>]
    let rec getValidPlayerMove (board: Board) (renderBoard: Position option -> unit) : PlayerInput =
        match getPlayerMoveWithCursor board renderBoard with
        | Some input -> input
        | None ->
            AnsiConsole.WriteLine()
            getValidPlayerMove board renderBoard

    /// Ask player to choose game mode
    let getGameMode () : int option =
        try
            ConsoleRenderer.displayMenu ()
            let choice = AnsiConsole.Ask<int>("[cyan]Select game mode (1-5):[/] ")

            if choice >= 1 && choice <= 5 then
                Some choice
            else
                ConsoleRenderer.displayError "Invalid choice! Please select 1-5."
                None
        with
        | :? FormatException ->
            ConsoleRenderer.displayError "Invalid input! Please enter a number."
            None
        | ex ->
            ConsoleRenderer.displayError $"Error: {ex.Message}"
            None

    /// Get valid game mode with retry
    [<TailCall>]
    let rec getValidGameMode () : int =
        match getGameMode () with
        | Some mode -> mode
        | None ->
            AnsiConsole.WriteLine()
            getValidGameMode ()

    /// Ask player for board size preference
    let getBoardSize () : BoardConfig =
        AnsiConsole.WriteLine()

        let choice =
            AnsiConsole.Prompt(
                SelectionPrompt<string>().Title("[cyan]Select board size:[/]").AddChoices
                    [ "15x15 (Standard Gomoku)"; "19x19 (Go board)"; "Custom size" ]
            )

        match choice with
        | "15x15 (Standard Gomoku)" -> BoardConfig.standard15x15
        | "19x19 (Go board)" -> BoardConfig.pro19x19
        | "Custom size" ->
            try
                let size = AnsiConsole.Ask<int>("[cyan]Enter board size (5-25):[/] ")

                if size >= 5 && size <= 25 then
                    { Size = size; WinLength = 5 }
                else
                    ConsoleRenderer.displayError "Invalid size! Using standard 15x15."
                    BoardConfig.standard15x15
            with _ ->
                ConsoleRenderer.displayError "Invalid input! Using standard 15x15."
                BoardConfig.standard15x15
        | _ -> BoardConfig.standard15x15

    /// Run `think` (an AI choosing its move) behind a "Thinking..." spinner, letting Esc quit
    /// the game after a confirmation. Some result, or None when the player quits.
    /// On a quit the search is not abandoned midway but left to finish first (it takes about
    /// a second at most), so two searches never share the AIs' random number generators.
    let thinkUnlessQuit (think: unit -> 'T) : 'T option =
        let search = Threading.Tasks.Task.Run(fun () -> think ())

        // Console.KeyAvailable throws when input is redirected; then there is no Esc to see
        let keyAvailable () =
            try
                Console.KeyAvailable
            with :? InvalidOperationException ->
                false

        // Spin until the search finishes (false) or Esc is pressed (true); other keys are
        // dropped so they do not carry over into the player's next move
        let spin label =
            AnsiConsole
                .Status()
                .Spinner(Spinner.Known.Dots)
                .Start(
                    label,
                    fun _ ->
                        let mutable escape = false

                        while not search.IsCompleted && not escape do
                            if keyAvailable () then
                                escape <- Console.ReadKey(true).Key = ConsoleKey.Escape
                            else
                                Threading.Thread.Sleep 30

                        escape
                )

        let rec wait () =
            if spin "Thinking... [grey](Esc to quit)[/]" then
                if AnsiConsole.Confirm("[yellow]Quit this game?[/]", false) then
                    spin "Stopping..." |> ignore
                    search.Wait()
                    None
                else
                    wait ()
            else
                // GetResult rethrows a search failure as itself, not wrapped
                Some(search.GetAwaiter().GetResult())

        wait ()

    /// Wait for player to press any key
    let waitForKey () : unit =
        AnsiConsole.WriteLine()
        AnsiConsole.Markup("[grey]Press any key to continue...[/]")
        Console.ReadKey(true) |> ignore
        AnsiConsole.WriteLine()
