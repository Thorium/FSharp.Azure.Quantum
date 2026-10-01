namespace FSharp.Azure.Quantum.Examples.Gomoku.UI

open FSharp.Azure.Quantum.Examples.Gomoku
open Spectre.Console
open System

/// Console renderer for the Gomoku board: a grid of lines with the stones on the intersections
module ConsoleRenderer =

    /// Colour of the grid lines
    [<Literal>]
    let private GridColor = "teal"

    /// Colour and letter of each player's stones: Black plays X, White plays O
    let private stone (cell: Cell) : string * string =
        match cell with
        | Black -> "mediumpurple1", "X"
        | White -> "lime", "O"
        | Empty -> GridColor, " "

    /// Line glyph of an empty intersection; the outer rows and columns close the frame
    let private intersection (size: int) (row: int) (col: int) : string =
        let last = size - 1

        match row, col with
        | 0, 0 -> "┌"
        | 0, c when c = last -> "┐"
        | 0, _ -> "┬"
        | r, 0 when r = last -> "└"
        | r, c when r = last && c = last -> "┘"
        | r, _ when r = last -> "┴"
        | _, 0 -> "├"
        | _, c when c = last -> "┤"
        | _ -> "┼"

    /// Render the game board. An intersection is one character and a line segment joins it
    /// to the next, so a cell is about as wide as it is tall. The cursor is shown on a
    /// yellow background and the last move is underlined.
    let renderBoard (board: Board) (cursorPos: Position option) : unit =
        let size = board.Config.Size
        let lastMove = List.tryHead board.MoveHistory

        let cellMarkup (pos: Position) : string =
            let cell = Board.getCell board pos
            let isCursor = cursorPos = Some pos
            let isLastMove = lastMove = Some pos

            match cell with
            | Empty ->
                let glyph = intersection size pos.Row pos.Col

                if isCursor then
                    $"[black on yellow]{glyph}[/]"
                else
                    $"[{GridColor}]{glyph}[/]"
            | Black
            | White ->
                let color, letter = stone cell

                if isCursor then $"[bold black on yellow]{letter}[/]"
                elif isLastMove then $"[bold underline {color}]{letter}[/]"
                else $"[bold {color}]{letter}[/]"

        AnsiConsole.WriteLine()

        // Column numbers (last digit) above their intersections
        let header = [ for col in 0 .. size - 1 -> string (col % 10) ] |> String.concat " "

        AnsiConsole.MarkupLine($"   [grey]{header}[/]")

        for row in 0 .. size - 1 do
            let cells =
                [ for col in 0 .. size - 1 -> cellMarkup { Row = row; Col = col } ]
                |> String.concat $"[{GridColor}]─[/]"

            AnsiConsole.MarkupLine($"[grey]{row, 2}[/] {cells}")

        AnsiConsole.WriteLine()

    /// Display game title and header
    let displayTitle () : unit =
        let rule =
            Rule("[bold yellow]Gomoku (Five-in-a-Row) - Local Quantum AI Example[/]", Style = (Style.Parse "yellow"))

        AnsiConsole.Write(rule)
        AnsiConsole.WriteLine()

    /// Display current player turn
    let displayTurn (player: Cell) : unit =
        let color, symbol = stone player
        AnsiConsole.MarkupLine($"[{color}]Current Player: {symbol} {player}[/]")
        AnsiConsole.WriteLine()

    /// Display game status panel
    let displayGameStatus (status: Board.GameStatus) : unit =
        let panel =
            match status with
            | Board.InProgress ->
                let p =
                    Panel(
                        "Game in progress...",
                        Header = (PanelHeader "Status"),
                        Border = BoxBorder.Rounded,
                        BorderStyle = (Style(foreground = Color.Green))
                    )

                p
            | Board.Won winner ->
                let color, symbol = stone winner

                let p =
                    Panel(
                        $"[{color}]{symbol} {winner} wins![/]",
                        Header = (PanelHeader "Game Over!"),
                        Border = BoxBorder.Double,
                        BorderStyle = (Style(foreground = Color.Yellow))
                    )

                p
            | Board.Draw ->
                let p =
                    Panel(
                        "It's a draw!",
                        Header = (PanelHeader "Game Over!"),
                        Border = BoxBorder.Double,
                        BorderStyle = (Style(foreground = Color.Grey))
                    )

                p

        AnsiConsole.Write(panel)
        AnsiConsole.WriteLine()

    /// Display AI thinking status
    let displayAIThinking (mode: string) (candidateCount: int) (depth: int option) : unit =
        let depthStr =
            match depth with
            | Some d -> $"Depth: {d}"
            | None -> ""

        let panel =
            Panel(
                $"[cyan]Mode:[/] {mode}\n[cyan]Candidates:[/] {candidateCount} positions\n[cyan]{depthStr}[/]",
                Header = (PanelHeader "AI Thinking..."),
                Border = BoxBorder.Rounded,
                BorderStyle = (Style(foreground = Color.Cyan1))
            )

        AnsiConsole.Write(panel)

    /// Show progress bar for long computations
    let showProgress (task: string) (action: unit -> 'T) : 'T =
        let mutable result = Unchecked.defaultof<'T>

        AnsiConsole
            .Progress()
            .Start(fun ctx ->
                let progressTask = ctx.AddTask $"[cyan]{task}[/]"
                progressTask.IsIndeterminate <- true

                result <- action ()

                progressTask.StopTask())

        result

    /// Display move history
    let displayMoveHistory (board: Board) (maxMoves: int) : unit =
        if board.MoveHistory.IsEmpty then
            ()
        else
            let movesToShow =
                board.MoveHistory
                |> List.rev
                |> List.take (min maxMoves board.MoveHistory.Length)

            let table =
                Table(Border = TableBorder.Rounded, BorderStyle = (Style(foreground = Color.Grey)))

            table.AddColumn("[bold]Move[/]") |> ignore
            table.AddColumn("[bold]Player[/]") |> ignore
            table.AddColumn("[bold]Position[/]") |> ignore

            movesToShow
            |> List.iteri (fun i pos ->
                let moveNum = i + 1
                let player = if moveNum % 2 = 1 then "Black X" else "White O"
                let color = fst (stone (if moveNum % 2 = 1 then Black else White))

                table.AddRow($"{moveNum}", $"[{color}]{player}[/]", $"({pos.Row}, {pos.Col})")
                |> ignore)

            AnsiConsole.Write(table)
            AnsiConsole.WriteLine()

    /// Display performance metrics
    let displayMetrics (classicalTime: float option) (quantumTime: float option) (evaluatedPositions: int) : unit =
        let table =
            Table(Border = TableBorder.Rounded, BorderStyle = (Style(foreground = Color.Green)))

        table.AddColumn("[bold]Metric[/]") |> ignore
        table.AddColumn("[bold]Value[/]") |> ignore

        match classicalTime with
        | Some t -> table.AddRow("Classical Time", $"{t:F2} ms") |> ignore
        | None -> ()

        match quantumTime with
        | Some t -> table.AddRow("Quantum Time", $"{t:F2} ms") |> ignore
        | None -> ()

        table.AddRow("Positions Evaluated", $"{evaluatedPositions}") |> ignore

        match classicalTime, quantumTime with
        | Some ct, Some qt when qt > 0.0 ->
            let speedup = ct / qt
            let color = if speedup > 1.0 then "green" else "red"
            table.AddRow("Speedup", $"[{color}]{speedup:F2}x[/]") |> ignore
        | _ -> ()

        let panel =
            Panel(table, Header = (PanelHeader "Performance Metrics"), Border = BoxBorder.Rounded)

        AnsiConsole.Write(panel)
        AnsiConsole.WriteLine()

    /// Clear the console
    let clear () : unit = AnsiConsole.Clear()

    /// Display a message with color
    let displayMessage (message: string) (color: string) : unit =
        AnsiConsole.MarkupLine($"[{color}]{message}[/]")
        AnsiConsole.WriteLine()

    /// Display error message
    let displayError (message: string) : unit =
        displayMessage $"❌ Error: {message}" "red"

    /// Display success message
    let displaySuccess (message: string) : unit = displayMessage $"✅ {message}" "green"

    /// Display info message
    let displayInfo (message: string) : unit = displayMessage $"ℹ️  {message}" "cyan"

    /// Ask for confirmation
    let confirm (prompt: string) : bool = AnsiConsole.Confirm(prompt)

    /// Display the main menu
    let displayMenu () : unit =
        let panel =
            Panel(
                "[cyan]1.[/] Player vs Classical AI\n[cyan]2.[/] Player vs Local Quantum Grover AI\n[cyan]3.[/] Player vs Local Hybrid Grover AI (Recommended)\n[cyan]4.[/] AI vs AI (Benchmark)\n[cyan]5.[/] Exit",
                Header = (PanelHeader "Game Modes"),
                Border = BoxBorder.Rounded,
                BorderStyle = (Style(foreground = Color.Cyan1))
            )

        AnsiConsole.Write(panel)
        AnsiConsole.WriteLine()

    /// Display game rules
    let displayRules () : unit =
        let panel =
            Panel(
                "[bold yellow]Gomoku Rules:[/]\n\n• Two players: Black (X) and White (O)\n• Black moves first\n• Players alternate placing stones on the intersections of the grid\n• First to get [bold]5 in a row[/] (horizontal, vertical, or diagonal) wins\n• If the board fills up, the game is a draw\n\n[bold cyan]Board Coordinates:[/]\n• Rows and columns are numbered 0-14\n• Enter your move as: row, column (e.g., \"7, 7\" for center)\n\n[bold green]Local Quantum AI Features:[/]\n• Uses real Grover's algorithm with local quantum simulator\n• Demonstrates √N speedup over classical search\n• Switches between classical and quantum based on complexity",
                Header = (PanelHeader "How to Play"),
                Border = BoxBorder.Double,
                BorderStyle = (Style(foreground = Color.Yellow))
            )

        AnsiConsole.Write(panel)
        AnsiConsole.WriteLine()
