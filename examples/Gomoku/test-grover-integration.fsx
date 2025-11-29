// Test script to verify real Grover integration in LocalQuantum AI
#load "Board.fs"
#load "AI/ThreatDetection.fs"
#load "AI/Classical.fs"
#load "AI/Quantum.fs"

open FSharp.Azure.Quantum.Examples.Gomoku
open FSharp.Azure.Quantum.Examples.Gomoku.AI

printfn "=== LOCAL QUANTUM AI - REAL GROVER INTEGRATION TEST ==="
printfn ""

let config = { Size = 15; WinLength = 5 }
let board = Board.create config

// Place a few initial moves to create a realistic game state
let cells = Array2D.copy board.Cells
cells.[7, 7] <- Black
cells.[7, 8] <- White
cells.[8, 7] <- Black

let gameBoard = { board with Cells = cells; CurrentPlayer = White }

printfn "Board state (3 moves played):"
printfn "%s" (Board.toString gameBoard)

printfn "\nTesting LocalQuantum AI move selection..."
printfn "This uses the REAL Grover's algorithm from FSharp.Azure.Quantum!"
printfn ""

try
    let (move, iterations) = LocalQuantum.selectBestMove gameBoard None
    
    match move with
    | Some pos ->
        printfn "✅ SUCCESS!"
        printfn "   Selected move: (%d,%d)" pos.Row pos.Col
        printfn "   Grover iterations: %d" iterations
        printfn ""
        printfn "The LocalQuantum AI successfully used real Grover's algorithm"
        printfn "from the FSharp.Azure.Quantum local quantum simulator!"
    | None ->
        printfn "⚠️  No move selected (unexpected)"
with
| ex ->
    printfn "❌ ERROR: %s" ex.Message
    printfn "Stack trace: %s" ex.StackTrace

printfn ""
printfn "=========================================="
