namespace FSharp.Azure.Quantum.Examples.Gomoku.UI

open FSharp.Azure.Quantum.Examples.Gomoku
open Spectre.Console
open System

/// Console renderer for Gomoku board with compact layout
module ConsoleRenderer =
    
    /// Render the game board with compact spacing using direct console drawing
    let renderBoard (board: Board) (cursorPos: Position option) : unit =
        AnsiConsole.WriteLine()
        
        // Column headers
        Console.Write("   ")
        for col in 0 .. board.Config.Size - 1 do
            AnsiConsole.Markup($"[cyan]{col % 10}[/]")  // Show only last digit for compactness
        AnsiConsole.WriteLine()
        
        // Top border
        Console.Write("  ╔")
        for col in 0 .. board.Config.Size - 1 do
            Console.Write(if col = board.Config.Size - 1 then "═╗" else "═")
        AnsiConsole.WriteLine()
        
        // Board rows
        for row in 0 .. board.Config.Size - 1 do
            // Row number
            if row < 10 then
                AnsiConsole.Markup($"[cyan] {row}[/]║")
            else
                AnsiConsole.Markup($"[cyan]{row}[/]║")
            
            // Board cells
            for col in 0 .. board.Config.Size - 1 do
                let pos = { Row = row; Col = col }
                let cell = Board.getCell board pos
                
                // Check if this is cursor position
                let isCursor = 
                    match cursorPos with
                    | Some cp when cp = pos -> true
                    | _ -> false
                
                // Check if this is last move
                let isLastMove =
                    match board.MoveHistory with
                    | lastMove :: _ when lastMove = pos -> true
                    | _ -> false
                
                // Render cell with appropriate styling
                match cell, isCursor, isLastMove with
                | Empty, true, _ -> AnsiConsole.Markup("[black on yellow]·[/]")  // Cursor on empty (1 char wide)
                | Black, true, _ -> AnsiConsole.Markup("[blue on yellow]X[/]")   // Cursor on Black
                | White, true, _ -> AnsiConsole.Markup("[red on yellow]O[/]")    // Cursor on White
                | Black, _, true -> AnsiConsole.Markup("[blue on grey]X[/]")     // Last move (Black)
                | White, _, true -> AnsiConsole.Markup("[red on grey]O[/]")      // Last move (White)
                | Black, _, _ -> AnsiConsole.Markup("[blue]X[/]")                // Black piece
                | White, _, _ -> AnsiConsole.Markup("[red]O[/]")                 // White piece
                | Empty, _, _ -> AnsiConsole.Markup("[grey]·[/]")                // Empty intersection (1 char)
            
            AnsiConsole.WriteLine("║")
        
        // Bottom border
        Console.Write("  ╚")
        for col in 0 .. board.Config.Size - 1 do
            Console.Write(if col = board.Config.Size - 1 then "═╝" else "═")
        AnsiConsole.WriteLine()
        AnsiConsole.WriteLine()
    
    /// Display game title and header
    let displayTitle() : unit =
        let rule = Rule("[bold yellow]Gomoku (Five-in-a-Row) - Local Quantum AI Example[/]")
        rule.Style <- Style.Parse("yellow")
        AnsiConsole.Write(rule)
        AnsiConsole.WriteLine()
    
    /// Display current player turn
    let displayTurn (player: Cell) : unit =
        let color = if player = Black then "blue" else "red"
        let symbol = if player = Black then "●" else "○"
        AnsiConsole.MarkupLine($"[{color}]Current Player: {symbol} {player}[/]")
        AnsiConsole.WriteLine()
    
    /// Display game status panel
    let displayGameStatus (status: Board.GameStatus) : unit =
        let panel = 
            match status with
            | Board.InProgress ->
                let p = Panel("Game in progress...")
                p.Header <- PanelHeader("Status")
                p.Border <- BoxBorder.Rounded
                p.BorderStyle <- Style(foreground = Color.Green)
                p
            | Board.Won winner ->
                let color = if winner = Black then "blue" else "red"
                let symbol = if winner = Black then "●" else "○"
                let p = Panel($"[{color}]{symbol} {winner} wins![/]")
                p.Header <- PanelHeader("Game Over!")
                p.Border <- BoxBorder.Double
                p.BorderStyle <- Style(foreground = Color.Yellow)
                p
            | Board.Draw ->
                let p = Panel("It's a draw!")
                p.Header <- PanelHeader("Game Over!")
                p.Border <- BoxBorder.Double
                p.BorderStyle <- Style(foreground = Color.Grey)
                p
        
        AnsiConsole.Write(panel)
        AnsiConsole.WriteLine()
    
    /// Display AI thinking status
    let displayAIThinking (mode: string) (candidateCount: int) (depth: int option) : unit =
        let depthStr = 
            match depth with
            | Some d -> $"Depth: {d}"
            | None -> ""
        
        let panel = Panel($"[cyan]Mode:[/] {mode}\n[cyan]Candidates:[/] {candidateCount} positions\n[cyan]{depthStr}[/]")
        panel.Header <- PanelHeader("AI Thinking...")
        panel.Border <- BoxBorder.Rounded
        panel.BorderStyle <- Style(foreground = Color.Cyan1)
        AnsiConsole.Write(panel)
    
    /// Show progress bar for long computations
    let showProgress (task: string) (action: unit -> 'T) : 'T =
        let mutable result = Unchecked.defaultof<'T>
        
        AnsiConsole.Progress()
            .Start(fun ctx ->
                let progressTask = ctx.AddTask($"[cyan]{task}[/]")
                progressTask.IsIndeterminate <- true
                
                result <- action()
                
                progressTask.StopTask()
            )
        
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
            
            let table = Table()
            table.Border <- TableBorder.Rounded
            table.BorderStyle <- Style(foreground = Color.Grey)
            table.AddColumn("[bold]Move[/]") |> ignore
            table.AddColumn("[bold]Player[/]") |> ignore
            table.AddColumn("[bold]Position[/]") |> ignore
            
            movesToShow
            |> List.iteri (fun i pos ->
                let moveNum = i + 1
                let player = if moveNum % 2 = 1 then "Black ●" else "White ○"
                let color = if moveNum % 2 = 1 then "blue" else "red"
                table.AddRow($"{moveNum}", $"[{color}]{player}[/]", $"({pos.Row}, {pos.Col})") |> ignore
            )
            
            AnsiConsole.Write(table)
            AnsiConsole.WriteLine()
    
    /// Display performance metrics
    let displayMetrics (classicalTime: float option) (quantumTime: float option) (evaluatedPositions: int) : unit =
        let table = Table()
        table.Border <- TableBorder.Rounded
        table.BorderStyle <- Style(foreground = Color.Green)
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
        
        let panel = Panel(table)
        panel.Header <- PanelHeader("Performance Metrics")
        panel.Border <- BoxBorder.Rounded
        AnsiConsole.Write(panel)
        AnsiConsole.WriteLine()
    
    /// Clear the console
    let clear() : unit =
        AnsiConsole.Clear()
    
    /// Display a message with color
    let displayMessage (message: string) (color: string) : unit =
        AnsiConsole.MarkupLine($"[{color}]{message}[/]")
        AnsiConsole.WriteLine()
    
    /// Display error message
    let displayError (message: string) : unit =
        displayMessage $"❌ Error: {message}" "red"
    
    /// Display success message
    let displaySuccess (message: string) : unit =
        displayMessage $"✅ {message}" "green"
    
    /// Display info message
    let displayInfo (message: string) : unit =
        displayMessage $"ℹ️  {message}" "cyan"
    
    /// Ask for confirmation
    let confirm (prompt: string) : bool =
        AnsiConsole.Confirm(prompt)
    
    /// Display the main menu
    let displayMenu() : unit =
        let panel = Panel("[cyan]1.[/] Player vs Classical AI\n[cyan]2.[/] Player vs Local Quantum Grover AI\n[cyan]3.[/] Player vs Local Hybrid Grover AI (Recommended)\n[cyan]4.[/] AI vs AI (Benchmark)\n[cyan]5.[/] Exit")
        panel.Header <- PanelHeader("Game Modes")
        panel.Border <- BoxBorder.Rounded
        panel.BorderStyle <- Style(foreground = Color.Cyan1)
        AnsiConsole.Write(panel)
        AnsiConsole.WriteLine()
    
    /// Display game rules
    let displayRules() : unit =
        let panel = Panel("[bold yellow]Gomoku Rules:[/]\n\n• Two players: Black (●) and White (○)\n• Black moves first\n• Players alternate placing stones on the board\n• First to get [bold]5 in a row[/] (horizontal, vertical, or diagonal) wins\n• If the board fills up, the game is a draw\n\n[bold cyan]Board Coordinates:[/]\n• Rows and columns are numbered 0-14\n• Enter your move as: row, column (e.g., \"7, 7\" for center)\n\n[bold green]Local Quantum AI Features:[/]\n• Uses real Grover's algorithm with local quantum simulator\n• Demonstrates √N speedup over classical search\n• Switches between classical and quantum based on complexity")
        panel.Header <- PanelHeader("How to Play")
        panel.Border <- BoxBorder.Double
        panel.BorderStyle <- Style(foreground = Color.Yellow)
        AnsiConsole.Write(panel)
        AnsiConsole.WriteLine()