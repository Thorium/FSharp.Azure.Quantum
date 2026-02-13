// ==============================================================================
// Quantum Tree Search Examples - FSharp.Azure.Quantum
// ==============================================================================
// Demonstrates the Quantum Tree Search API using Grover's algorithm to solve
// game tree and decision tree problems:
//
// 1. Tic-Tac-Toe AI (educational)
// 2. Chess-style Position Evaluation (intermediate)
// 3. Business Decision Tree Optimization (advanced)
//
// Usage:
//   dotnet fsi GameAI.fsx
//   dotnet fsi GameAI.fsx -- --example tictactoe
//   dotnet fsi GameAI.fsx -- --quiet --output results.json --csv results.csv
//   dotnet fsi GameAI.fsx -- --help
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
open FSharp.Azure.Quantum.Examples.Common

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumTreeSearch
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv
Cli.exitIfHelp "GameAI.fsx" "Quantum tree search for game AI and decision optimization" [
    { Name = "example"; Description = "Which example: all, tictactoe, chess, business"; Default = Some "all" }
    { Name = "shots"; Description = "Measurement shots per search"; Default = Some "50" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress printed output"; Default = None }
] args

let exampleChoice = Cli.getOr "example" "all" args
let cliShots = Cli.getIntOr "shots" 50 args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

// ==============================================================================
// Backend (Rule 1: explicit IQuantumBackend)
// ==============================================================================

let quantumBackend = LocalBackend() :> IQuantumBackend

// ==============================================================================
// Domain Types (all at top level - F# requires type declarations at module scope)
// ==============================================================================

// --- Tic-Tac-Toe ---

type Player = X | O | Empty
type Board = Player array

type TicTacToeState = {
    Board: Board
    CurrentPlayer: Player
    Move: int option
}

// --- Chess (simplified) ---

type Piece = Pawn | Knight | Bishop | Rook | Queen | King
type ChessColor = White | Black
type Square = (Piece * ChessColor) option

type ChessState = {
    Pieces: Square array
    ToMove: ChessColor
    Ply: int
}

// --- Business Decision Tree ---

type MarketingDecision = SocialMedia | TV | Radio | Email
type PricingDecision = BudgetPrice | StandardPrice | PremiumPrice
type LaunchDecision = SoftLaunch | Regional | Global

type BusinessState = {
    Marketing: MarketingDecision option
    Pricing: PricingDecision option
    Launch: LaunchDecision option
    Stage: int
}

// ==============================================================================
// Tic-Tac-Toe Functions
// ==============================================================================

let displayBoard (board: Board) =
    let charOf p = match p with X -> "X" | O -> "O" | Empty -> "."
    pr "  %s | %s | %s" (charOf board.[0]) (charOf board.[1]) (charOf board.[2])
    pr "  ---------"
    pr "  %s | %s | %s" (charOf board.[3]) (charOf board.[4]) (charOf board.[5])
    pr "  ---------"
    pr "  %s | %s | %s" (charOf board.[6]) (charOf board.[7]) (charOf board.[8])

let checkWinner (board: Board) : Player option =
    let lines = [
        [0; 1; 2]; [3; 4; 5]; [6; 7; 8]
        [0; 3; 6]; [1; 4; 7]; [2; 5; 8]
        [0; 4; 8]; [2; 4; 6]
    ]
    lines
    |> List.tryPick (fun line ->
        let cells = line |> List.map (fun i -> board.[i])
        match cells with
        | [X; X; X] -> Some X
        | [O; O; O] -> Some O
        | _ -> None
    )

let evaluatePosition (state: TicTacToeState) : float =
    match checkWinner state.Board with
    | Some X -> 1000.0
    | Some O -> -1000.0
    | Some _
    | None ->
        let lines = [
            [0; 1; 2]; [3; 4; 5]; [6; 7; 8]
            [0; 3; 6]; [1; 4; 7]; [2; 5; 8]
            [0; 4; 8]; [2; 4; 6]
        ]
        let scoreLines player =
            lines |> List.sumBy (fun line ->
                let cells = line |> List.map (fun i -> state.Board.[i])
                let count = cells |> List.filter ((=) player) |> List.length
                let enemyCount = cells |> List.filter ((=) (if player = X then O else X)) |> List.length
                if enemyCount > 0 then 0.0
                else float (count * count)
            )
        let xScore = scoreLines X
        let oScore = scoreLines O
        if state.CurrentPlayer = X then xScore - oScore
        else oScore - xScore

let generateMoves (state: TicTacToeState) : TicTacToeState list =
    if checkWinner state.Board |> Option.isSome then []
    else
        [0..8]
        |> List.filter (fun i -> state.Board.[i] = Empty)
        |> List.map (fun move ->
            let newBoard = Array.copy state.Board
            newBoard.[move] <- state.CurrentPlayer
            { Board = newBoard
              CurrentPlayer = if state.CurrentPlayer = X then O else X
              Move = Some move })

// ==============================================================================
// Chess Functions
// ==============================================================================

let evaluateChessPosition (state: ChessState) : float =
    let materialValue = function
        | Pawn -> 1.0 | Knight -> 3.0 | Bishop -> 3.0
        | Rook -> 5.0 | Queen -> 9.0 | King -> 0.0
    let material =
        state.Pieces
        |> Array.choose id
        |> Array.sumBy (fun (piece, color) ->
            let value = materialValue piece
            if color = White then value else -value
        )
    let positional = float state.Ply * 0.1
    material + positional

let generateChessMoves (state: ChessState) : ChessState list =
    if state.Ply >= 8 then []
    else
        [1..16]
        |> List.map (fun _ ->
            { Pieces = state.Pieces
              ToMove = if state.ToMove = White then Black else White
              Ply = state.Ply + 1 })

// ==============================================================================
// Business Decision Functions
// ==============================================================================

let simulateMarketImpact (state: BusinessState) : float =
    let marketingScore =
        match state.Marketing with
        | Some SocialMedia -> 80.0 | Some TV -> 100.0
        | Some Radio -> 60.0 | Some Email -> 70.0
        | None -> 0.0
    let pricingScore =
        match state.Pricing with
        | Some PremiumPrice -> 100.0 | Some StandardPrice -> 85.0
        | Some BudgetPrice -> 60.0 | None -> 0.0
    let launchScore =
        match state.Launch with
        | Some Global -> 100.0 | Some Regional -> 80.0
        | Some SoftLaunch -> 60.0 | None -> 0.0
    (marketingScore + pricingScore + launchScore) / 3.0

let generateBusinessDecisions (state: BusinessState) : BusinessState list =
    match state.Stage with
    | 0 ->
        [SocialMedia; TV; Radio; Email]
        |> List.map (fun m -> { state with Marketing = Some m; Stage = 1 })
    | 1 ->
        [BudgetPrice; StandardPrice; PremiumPrice]
        |> List.map (fun p -> { state with Pricing = Some p; Stage = 2 })
    | 2 ->
        [SoftLaunch; Regional; Global]
        |> List.map (fun l -> { state with Launch = Some l; Stage = 3 })
    | _ -> []

// ==============================================================================
// Main Execution
// ==============================================================================

pr "=== Quantum Tree Search: Game AI ==="
pr "Backend: %s" quantumBackend.Name
pr ""

// Accumulators for output
let mutable jsonResults: (string * obj) list = []
let mutable csvRows: string list list = []

// --- Example 1: Tic-Tac-Toe ---

if exampleChoice = "all" || exampleChoice = "tictactoe" then
    pr "--- Example 1: Tic-Tac-Toe AI ---"
    pr ""

    let startState = {
        Board = [| Empty; Empty; Empty; Empty; X; Empty; Empty; Empty; O |]
        CurrentPlayer = X
        Move = None
    }

    pr "Position (X to move):"
    displayBoard startState.Board
    pr ""
    pr "Searching 2 moves ahead..."
    pr ""

    let tttProblem = quantumTreeSearch {
        initialState startState
        maxDepth 2
        branchingFactor 9
        evaluateWith evaluatePosition
        generateMovesWith generateMoves
        topPercentile 0.2
        backend quantumBackend
        shots cliShots
        solutionThreshold 0.05
        successThreshold 0.5
    }

    match solve tttProblem with
    | Ok result ->
        pr "Best Move: position %d" result.BestMove
        pr "  Score: %.4f" result.Score
        pr "  Paths Explored: %d" result.PathsExplored
        pr "  Qubits: %d" result.QubitsRequired
        pr "  Quantum Advantage: %b" result.QuantumAdvantage
        pr ""
        jsonResults <- ("tictactoe", box {| bestMove = result.BestMove; score = result.Score; pathsExplored = result.PathsExplored; qubits = result.QubitsRequired; quantumAdvantage = result.QuantumAdvantage |}) :: jsonResults
        csvRows <- ["tictactoe"; sprintf "%d" result.BestMove; sprintf "%.4f" result.Score; sprintf "%d" result.PathsExplored; sprintf "%d" result.QubitsRequired; sprintf "%b" result.QuantumAdvantage] :: csvRows
    | Error err ->
        pr "Error: %s" err.Message
        csvRows <- ["tictactoe"; "error"; err.Message; ""; ""; ""] :: csvRows

// --- Example 2: Chess ---

if exampleChoice = "all" || exampleChoice = "chess" then
    pr "--- Example 2: Chess Position Analysis ---"
    pr "Simplified position, depth 2, branching factor 16"
    pr ""

    let chessInitial = {
        Pieces = Array.create 64 None
        ToMove = White
        Ply = 0
    }

    let chessProblem = quantumTreeSearch {
        initialState chessInitial
        maxDepth 2
        branchingFactor 16
        evaluateWith evaluateChessPosition
        generateMovesWith generateChessMoves
        topPercentile 0.15
        backend quantumBackend
        shots cliShots
        solutionThreshold 0.05
        successThreshold 0.5
    }

    match solve chessProblem with
    | Ok result ->
        pr "Best Move: %d" result.BestMove
        pr "  Score: %.4f" result.Score
        pr "  Paths Explored: %d" result.PathsExplored
        pr "  Qubits: %d" result.QubitsRequired
        pr "  Quantum Advantage: %b" result.QuantumAdvantage
        pr ""
        jsonResults <- ("chess", box {| bestMove = result.BestMove; score = result.Score; pathsExplored = result.PathsExplored; qubits = result.QubitsRequired; quantumAdvantage = result.QuantumAdvantage |}) :: jsonResults
        csvRows <- ["chess"; sprintf "%d" result.BestMove; sprintf "%.4f" result.Score; sprintf "%d" result.PathsExplored; sprintf "%d" result.QubitsRequired; sprintf "%b" result.QuantumAdvantage] :: csvRows
    | Error err ->
        pr "Error: %s" err.Message
        csvRows <- ["chess"; "error"; err.Message; ""; ""; ""] :: csvRows

// --- Example 3: Business Decision Tree ---

if exampleChoice = "all" || exampleChoice = "business" then
    pr "--- Example 3: Business Decision Tree ---"
    pr "3-stage decisions: Marketing x Pricing x Launch"
    pr "Total paths: 4 x 3 x 3 = 36 decision sequences"
    pr ""

    let businessInitial = {
        Marketing = None; Pricing = None; Launch = None; Stage = 0
    }

    let businessProblem = quantumTreeSearch {
        initialState businessInitial
        maxDepth 3
        branchingFactor 4
        evaluateWith simulateMarketImpact
        generateMovesWith generateBusinessDecisions
        backend quantumBackend
        shots cliShots
        solutionThreshold 0.05
        successThreshold 0.5
    }

    match solve businessProblem with
    | Ok result ->
        pr "Best First Move: %d" result.BestMove
        pr "  Expected ROI Score: %.4f" result.Score
        pr "  Paths Explored: %d" result.PathsExplored
        pr "  Qubits: %d" result.QubitsRequired
        pr "  Quantum Advantage: %b" result.QuantumAdvantage
        pr ""
        jsonResults <- ("business", box {| bestMove = result.BestMove; score = result.Score; pathsExplored = result.PathsExplored; qubits = result.QubitsRequired; quantumAdvantage = result.QuantumAdvantage |}) :: jsonResults
        csvRows <- ["business"; sprintf "%d" result.BestMove; sprintf "%.4f" result.Score; sprintf "%d" result.PathsExplored; sprintf "%d" result.QubitsRequired; sprintf "%b" result.QuantumAdvantage] :: csvRows
    | Error err ->
        pr "Error: %s" err.Message
        csvRows <- ["business"; "error"; err.Message; ""; ""; ""] :: csvRows

// ==============================================================================
// Output
// ==============================================================================

outputPath |> Option.iter (fun path ->
    let payload =
        {| backend = quantumBackend.Name
           shotsPerSearch = cliShots
           examples = jsonResults |> List.rev |> List.map (fun (name, data) -> {| example = name; results = data |}) |}
    Reporting.writeJson path payload
    pr "JSON written to %s" path
)

csvPath |> Option.iter (fun path ->
    let header = ["Example"; "BestMove"; "Score"; "PathsExplored"; "Qubits"; "QuantumAdvantage"]
    Reporting.writeCsv path header (csvRows |> List.rev)
    pr "CSV written to %s" path
)

if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr "---"
    pr "Tip: Use --example tictactoe|chess|business to run a single example."
    pr "     Use --shots N to change measurement count (default 50)."
    pr "     Use --output results.json or --csv results.csv to export."
    pr "     Use --help for all options."
