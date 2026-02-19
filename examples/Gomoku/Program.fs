namespace FSharp.Azure.Quantum.Examples.Gomoku

open FSharp.Azure.Quantum.Examples.Gomoku.AI
open FSharp.Azure.Quantum.Examples.Gomoku.UI
open Spectre.Console

/// Main program module
module Program =
    
    /// AI player type
    type AIPlayer =
        | ClassicalAI
        | LocalQuantumAI
        | LocalHybridAI
        | TopologicalQuantumAI
    
    /// Game result
    type GameResult = {
        Winner: Cell option
        TotalMoves: int
        AIMetrics: LocalHybrid.MoveMetrics list
        FinalBoard: Board  // Added to preserve final board state
    }
    
    /// Calculate adaptive candidate count for quantum AIs based on game phase
    let private getQuantumCandidateCount (board: Board) : int =
        let moveNumber = board.MoveHistory.Length
        let boardSize = board.Config.Size
        let totalCells = boardSize * boardSize
        let occupancy = float moveNumber / float totalCells
        
        // Scale candidates: start small (15), grow in midgame (up to 50), shrink in endgame
        if moveNumber < 6 then 15            // Early game: few relevant positions
        elif occupancy < 0.15 then 25         // Early-mid: standard
        elif occupancy < 0.30 then 40         // Midgame: more nuance needed
        elif occupancy < 0.50 then 50         // Late-mid: peak complexity
        else 35                               // Endgame: fewer open positions

    /// Play one turn (either player or AI)
    /// Returns (new board option, quit flag, optional hybrid metrics)
    let playTurn (board: Board) (isAI: bool) (aiPlayer: AIPlayer option) (debug: bool) : (Board option * bool * LocalHybrid.MoveMetrics option) =
        if isAI then
            let pos, hybridMetrics =
                match aiPlayer with
                | Some ClassicalAI ->
                    // Delegate fully to Classical.selectBestMove (includes threat check + randomness)
                    let move = Classical.selectBestMove board
                    if debug then
                        let label =
                            match ThreatDetection.getImmediateThreat board with
                            | Some _ -> "Classical Heuristic (threat)"
                            | None -> "Classical Heuristic"
                        ConsoleRenderer.displayAIThinking label 1 None
                        AnsiConsole.WriteLine()
                    (move, None)
                
                | Some LocalQuantumAI ->
                    // Create local backend for quantum simulation
                    let backend = FSharp.Azure.Quantum.Backends.LocalBackendFactory.createUnified()
                    let candidateCount = getQuantumCandidateCount board
                    let candidates = Classical.getTopCandidates board candidateCount
                    let (move, iterations) = LocalQuantum.selectBestMove board backend (Some candidates)
                    if debug then
                        let usedThreat = iterations = 0 && move.IsSome
                        let count = if usedThreat then 1 else candidates.Length
                        let label = if usedThreat then "Local Quantum (threat)" else "Local Quantum (Grover's Search)"
                        ConsoleRenderer.displayAIThinking label count None
                        ConsoleRenderer.displayInfo $"Grover iterations: {iterations}"
                        AnsiConsole.WriteLine()
                    (move, None)
                
                | Some LocalHybridAI ->
                    let metrics = LocalHybrid.selectBestMove board
                    
                    if debug then
                        let strategyName = 
                            match metrics.Strategy with
                            | LocalHybrid.Classical _ -> "Classical"
                            | LocalHybrid.Quantum _ -> "Quantum"
                        
                        ConsoleRenderer.displayAIThinking strategyName metrics.CandidatesEvaluated None
                        AnsiConsole.WriteLine()
                        
                        // Show strategy explanation
                        AnsiConsole.MarkupLine("[grey]{0}[/]", LocalHybrid.explainStrategy metrics)
                        AnsiConsole.WriteLine()
                    
                    (metrics.Move, Some metrics)
                
                | Some TopologicalQuantumAI ->
                    // Create topological backend (Ising encoding, 20 anyons → 9 logical qubits)
                    let backend = FSharp.Azure.Quantum.Topological.TopologicalUnifiedBackendFactory.createIsing 20
                    let candidateCount = getQuantumCandidateCount board
                    let candidates = Classical.getTopCandidates board candidateCount
                    let (move, iterations) = LocalQuantum.selectBestMove board backend (Some candidates)
                    if debug then
                        let usedThreat = iterations = 0 && move.IsSome
                        let count = if usedThreat then 1 else candidates.Length
                        let label = if usedThreat then "Topological Quantum (threat)" else "Topological Quantum (Grover's Search)"
                        ConsoleRenderer.displayAIThinking label count None
                        ConsoleRenderer.displayInfo $"Grover iterations (topological): {iterations}"
                        AnsiConsole.WriteLine()
                    (move, None)
                
                | None -> (None, None)
            
            match pos with
            | Some p -> 
                match Board.makeMove board p with
                | Ok newBoard -> (Some newBoard, false, hybridMetrics)
                | Error _ -> (None, false, None)
            | None -> (None, false, None)
        else
            // Human player
            let renderBoardWithCursor cursorPos = 
                ConsoleRenderer.clear()
                ConsoleRenderer.displayTitle()
                ConsoleRenderer.renderBoard board cursorPos
                AnsiConsole.WriteLine()
                ConsoleRenderer.displayTurn board.CurrentPlayer
            
            match InputHandler.getValidPlayerMove board renderBoardWithCursor with
            | InputHandler.Move pos -> 
                match Board.makeMove board pos with
                | Ok newBoard -> (Some newBoard, false, None)
                | Error _ -> (None, false, None)
            | InputHandler.Quit -> (None, true, None)  // Signal quit
    
    /// Game loop for player vs AI
    let rec gameLoop (board: Board) (aiPlayer: AIPlayer) (playerIsBlack: bool) : GameResult option =
        // Check game status FIRST
        match Board.getGameStatus board with
        | Board.Won winner ->
            Some { Winner = Some winner; TotalMoves = board.MoveHistory.Length; AIMetrics = []; FinalBoard = board }
        
        | Board.Draw ->
            Some { Winner = None; TotalMoves = board.MoveHistory.Length; AIMetrics = []; FinalBoard = board }
        
        | Board.InProgress ->
            // Display current state for AI turns
            let isAITurn = 
                (board.CurrentPlayer = Black && not playerIsBlack) ||
                (board.CurrentPlayer = White && playerIsBlack)
            
            if isAITurn then
                ConsoleRenderer.clear()
                ConsoleRenderer.displayTitle()
                ConsoleRenderer.renderBoard board None
                AnsiConsole.WriteLine()
                ConsoleRenderer.displayTurn board.CurrentPlayer
            
            match playTurn board isAITurn (Some aiPlayer) false with
            | (Some newBoard, false, _) -> gameLoop newBoard aiPlayer playerIsBlack
            | (None, true, _) -> 
                // Player quit
                ConsoleRenderer.displayInfo "Game quit by player."
                None
            | (None, false, _) ->
                ConsoleRenderer.displayError "Invalid move!"
                InputHandler.waitForKey()
                gameLoop board aiPlayer playerIsBlack
            | (Some _, true, _) -> 
                // Shouldn't happen but handle it
                None
    
    /// Move log entry for debugging
    type MoveLogEntry = {
        MoveNumber: int
        Player: Cell
        Position: Position
        Source: string  // "threat", "grover", "classical", "hybrid-classical", "hybrid-quantum"
    }

    /// AI vs AI game for benchmarking
    let rec aiVsAiLoop (board: Board) (ai1: AIPlayer) (ai2: AIPlayer) (metrics: LocalHybrid.MoveMetrics list) (moveLog: MoveLogEntry list) (debug: bool) : GameResult * MoveLogEntry list =
        // Check game status FIRST (before printing next move)
        match Board.getGameStatus board with
        | Board.Won winner ->
            ({ Winner = Some winner; TotalMoves = board.MoveHistory.Length; AIMetrics = metrics; FinalBoard = board }, List.rev moveLog)
        
        | Board.Draw ->
            ({ Winner = None; TotalMoves = board.MoveHistory.Length; AIMetrics = metrics; FinalBoard = board }, List.rev moveLog)
        
        | Board.InProgress ->
            let moveNum = board.MoveHistory.Length + 1
            let player = board.CurrentPlayer
            
            // Display current move only after confirming game is still in progress
            if debug then
                AnsiConsole.MarkupLine($"[grey]Move {moveNum}... {player}[/]")
            
            let currentAI = if player = Black then ai1 else ai2
            
            // Check if threat detection will fire (to log move source)
            let threatMove = ThreatDetection.getImmediateThreat board
            
            match playTurn board true (Some currentAI) debug with
            | (Some newBoard, _, hybridMetrics) -> 
                // Determine the move that was played (head of new board's history)
                let playedPos = newBoard.MoveHistory.Head
                
                // Determine move source
                let source =
                    match threatMove with
                    | Some _ -> "threat"
                    | None ->
                        match currentAI with
                        | ClassicalAI -> "classical"
                        | LocalQuantumAI -> "grover"
                        | TopologicalQuantumAI -> "grover"
                        | LocalHybridAI ->
                            match hybridMetrics with
                            | Some m ->
                                match m.Strategy with
                                | LocalHybrid.Classical _ -> "hybrid-classical"
                                | LocalHybrid.Quantum _ -> "hybrid-quantum"
                            | None -> "hybrid"
                
                let entry = {
                    MoveNumber = moveNum
                    Player = player
                    Position = playedPos
                    Source = source
                }
                
                // Collect metrics from the actual move (no redundant re-evaluation)
                let newMetrics =
                    match hybridMetrics with
                    | Some m -> m :: metrics
                    | None -> metrics
                
                aiVsAiLoop newBoard ai1 ai2 newMetrics (entry :: moveLog) debug
            | (None, _, _) ->
                ({ Winner = None; TotalMoves = board.MoveHistory.Length; AIMetrics = metrics; FinalBoard = board }, List.rev moveLog)
    
    /// Run player vs AI game
    let runPlayerVsAI (aiPlayer: AIPlayer) : unit =
        ConsoleRenderer.clear()
        ConsoleRenderer.displayTitle()
        ConsoleRenderer.displayRules()
        
        let config = InputHandler.getBoardSize()
        let board = Board.create config
        
        let playerColor = 
            AnsiConsole.Confirm("[cyan]Do you want to play as Black (first player)?[/]", true)
        
        AnsiConsole.WriteLine()
        ConsoleRenderer.displayInfo "Starting game..."
        InputHandler.waitForKey()
        
        match gameLoop board aiPlayer playerColor with
        | Some result ->
            // Display final result with the actual final board state
            ConsoleRenderer.clear()
            ConsoleRenderer.displayTitle()
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
            InputHandler.waitForKey()  // Wait for user to see result
        
        | None ->
            // Player quit
            ConsoleRenderer.displayInfo "Game was quit."
    
    /// Run AI vs AI benchmark
    let runBenchmark() : unit =
        ConsoleRenderer.clear()
        ConsoleRenderer.displayTitle()
        AnsiConsole.MarkupLine("[bold yellow]Benchmark Mode: Classical vs Local Quantum AI[/]")
        AnsiConsole.WriteLine()
        
        let config = BoardConfig.standard15x15
        let board = Board.create config
        
        AnsiConsole.MarkupLine("[cyan]Running benchmark game...[/]")
        AnsiConsole.WriteLine()
        
        let startTime = System.Diagnostics.Stopwatch.StartNew()
        let (result, _moveLog) = aiVsAiLoop board ClassicalAI LocalQuantumAI [] [] true  // true = debug mode for interactive
        startTime.Stop()
        
        // Display results
        AnsiConsole.WriteLine()
        AnsiConsole.MarkupLine("[bold green]Benchmark Complete![/]")
        AnsiConsole.WriteLine()
        
        let table = Spectre.Console.Table()
        table.Border <- TableBorder.Rounded
        table.AddColumn("[bold]Metric[/]") |> ignore
        table.AddColumn("[bold]Value[/]") |> ignore
        
        table.AddRow("Total Moves", $"{result.TotalMoves}") |> ignore
        table.AddRow("Game Time", $"{startTime.Elapsed.TotalSeconds:F2} seconds") |> ignore
        table.AddRow("Winner", 
            match result.Winner with 
            | Some w -> $"{w}" 
            | None -> "Draw") |> ignore
        
        // Calculate average metrics from hybrid moves
        if not result.AIMetrics.IsEmpty then
            let quantumMoves = 
                result.AIMetrics 
                |> List.filter (fun m -> match m.Strategy with LocalHybrid.Quantum _ -> true | _ -> false)
            
            let avgCandidates = 
                result.AIMetrics 
                |> List.averageBy (fun m -> float m.CandidatesEvaluated)
            
            table.AddRow("Avg Candidates per Move", $"{avgCandidates:F1}") |> ignore
            table.AddRow("Quantum Strategy Used", $"{quantumMoves.Length} / {result.AIMetrics.Length} moves") |> ignore
        
        AnsiConsole.Write(table)
        AnsiConsole.WriteLine()
        
        // Show quantum advantage explanation
        let avgSearchSpace = if result.AIMetrics.IsEmpty then 20 else int (result.AIMetrics |> List.averageBy (fun m -> float m.CandidatesEvaluated))
        AnsiConsole.WriteLine(LocalQuantum.explainQuantumAdvantage avgSearchSpace (avgSearchSpace / 3))
    
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
                    | "classical" -> Some ClassicalAI
                    | "quantum" | "localquantum" -> Some LocalQuantumAI
                    | "hybrid" | "localhybrid" -> Some LocalHybridAI
                    | "topological" | "topo" -> Some TopologicalQuantumAI
                    | _ -> None
                
                match parseAI ai1Name, parseAI ai2Name with
                | Some ai1, Some ai2 ->
                    if not debug then
                        // Quiet mode - just print progress indicator
                        AnsiConsole.MarkupLine($"[cyan]Running: {ai1Name} vs {ai2Name}...[/]")
                    else
                        // Debug mode - full verbose output
                        ConsoleRenderer.clear()
                        ConsoleRenderer.displayTitle()
                        AnsiConsole.MarkupLine($"[bold yellow]Automated Match: {ai1Name} (Black) vs {ai2Name} (White)[/]")
                        AnsiConsole.WriteLine()
                    
                    let config = BoardConfig.standard15x15
                    let board = Board.create config
                    
                    let startTime = System.Diagnostics.Stopwatch.StartNew()
                    let (result, moveLog) = aiVsAiLoop board ai1 ai2 [] [] debug
                    startTime.Stop()
                    
                    // Display final board and results
                    if debug then
                        ConsoleRenderer.clear()
                        ConsoleRenderer.displayTitle()
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
                        AnsiConsole.MarkupLine($"[cyan]Avg move time:[/] {startTime.Elapsed.TotalMilliseconds / float result.TotalMoves:F0} ms")
                    
                    // Always print move coordinate log
                    AnsiConsole.WriteLine()
                    AnsiConsole.MarkupLine("[bold cyan]Move Log (row,col):[/]")
                    for entry in moveLog do
                        let playerChar = if entry.Player = Black then "X" else "O"
                        let sourceTag = 
                            match entry.Source with
                            | "threat" -> " [red]<THREAT>[/]"
                            | "grover" -> " [magenta]<GROVER>[/]"
                            | "classical" -> ""
                            | s -> $" [grey]<{s}>[/]"
                        AnsiConsole.MarkupLine($"  {entry.MoveNumber,3}. {playerChar} ({entry.Position.Row},{entry.Position.Col}){sourceTag}")
                    
                    0  // Success
                
                | _ ->
                    ConsoleRenderer.displayError $"Invalid AI player names. Use: classical, quantum, hybrid, or topological"
                    AnsiConsole.WriteLine()
                    AnsiConsole.MarkupLine("[cyan]Usage:[/] Gomoku --ai-vs-ai <ai1> <ai2> [--debug]")
                    AnsiConsole.MarkupLine("[cyan]Example:[/] Gomoku --ai-vs-ai classical quantum")
                    1  // Error
            
            | [| "--help" |] | [| "-h" |] ->
                ConsoleRenderer.displayTitle()
                AnsiConsole.MarkupLine("[bold cyan]Usage:[/]")
                AnsiConsole.WriteLine()
                AnsiConsole.MarkupLine("  [yellow]Gomoku[/]                                 - Start interactive game")
                AnsiConsole.MarkupLine("  [yellow]Gomoku --ai-vs-ai <ai1> <ai2>[/]         - Run automated AI vs AI match (quiet)")
                AnsiConsole.MarkupLine("  [yellow]Gomoku --ai-vs-ai <ai1> <ai2> --debug[/] - Run with verbose debug output")
                AnsiConsole.MarkupLine("  [yellow]Gomoku --help[/]                          - Show this help")
                AnsiConsole.WriteLine()
                AnsiConsole.MarkupLine("[bold cyan]AI Players:[/]")
                AnsiConsole.MarkupLine("  [green]classical[/]     - Classical heuristic AI")
                AnsiConsole.MarkupLine("  [green]quantum[/]       - Local Quantum AI (Grover's algorithm with gate-based simulator)")
                AnsiConsole.MarkupLine("  [green]hybrid[/]        - Local Hybrid AI (switches between classical and quantum)")
                AnsiConsole.MarkupLine("  [green]topological[/]   - Topological Quantum AI (Grover's via Ising anyon braiding)")
                AnsiConsole.WriteLine()
                AnsiConsole.MarkupLine("[bold cyan]Examples:[/]")
                AnsiConsole.MarkupLine("  Gomoku --ai-vs-ai classical quantum          # Quiet mode (default)")
                AnsiConsole.MarkupLine("  Gomoku --ai-vs-ai quantum topological --debug # Gate-based vs topological")
                AnsiConsole.MarkupLine("  Gomoku --ai-vs-ai hybrid hybrid")
                0
            
            | _ ->
                // Interactive mode (original behavior)
                ConsoleRenderer.clear()
                ConsoleRenderer.displayTitle()
                
                let mutable running = true
                
                while running do
                    let mode = InputHandler.getValidGameMode()
                    
                    match mode with
                    | 1 -> runPlayerVsAI ClassicalAI
                    | 2 -> runPlayerVsAI LocalQuantumAI
                    | 3 -> runPlayerVsAI LocalHybridAI
                    | 4 -> runBenchmark()
                    | 5 -> 
                        running <- false
                        ConsoleRenderer.displaySuccess "Thanks for playing!"
                    | _ -> ConsoleRenderer.displayError "Invalid choice!"
                    
                    if running then
                        AnsiConsole.WriteLine()
                        if not (ConsoleRenderer.confirm "Play another game?") then
                            running <- false
                            ConsoleRenderer.displaySuccess "Thanks for playing!"
                
                0  // Success exit code
        
        with
        | ex ->
            ConsoleRenderer.displayError $"Fatal error: {ex.Message}"
            ConsoleRenderer.displayError $"Stack trace: {ex.StackTrace}"
            1  // Error exit code
