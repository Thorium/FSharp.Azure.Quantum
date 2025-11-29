// Recreate the EXACT user scenario
// Computer is White (O), Human is Black (X)
// Current player: White (computer's turn)
// Pattern on row 8: OXXXX_ (positions 5-10)
// Computer should block at (8,10) to prevent Black from winning

#load "Board.fs"
#load "AI/ThreatDetection.fs"

open FSharp.Azure.Quantum.Examples.Gomoku
open FSharp.Azure.Quantum.Examples.Gomoku.AI

let config = { Size = 15; WinLength = 5 }
let emptyBoard = Board.create config

let cells = Array2D.copy emptyBoard.Cells
cells.[8, 5] <- White  // O (computer)
cells.[8, 6] <- Black  // X (human)
cells.[8, 7] <- Black  // X
cells.[8, 8] <- Black  // X
cells.[8, 9] <- Black  // X
// (8,10) is empty

// CRITICAL: Current player is WHITE (computer's turn)
let gameBoard = { emptyBoard with Cells = cells; CurrentPlayer = White }

printfn "=== EXACT USER SCENARIO ==="
printfn "Current player: %A (Computer)" gameBoard.CurrentPlayer
printfn "Opponent: %A (Human)" (gameBoard.CurrentPlayer.Opposite())
printfn "\nBoard state:"
printfn "%s" (Board.toString gameBoard)

printfn "\n=== THREAT DETECTION TEST ==="
printfn "Computer (White) needs to find threats..."
printfn "Checking if Black would win at (8,10)..."

// What the computer should detect
let opponent = gameBoard.CurrentPlayer.Opposite()  // Black
printfn "Opponent is: %A" opponent

// Test if position (8,10) is a winning threat for Black
match Board.makeMove gameBoard { Row = 8; Col = 10 } with
| None -> printfn "ERROR: Move failed"
| Some newBoard ->
    printfn "After simulating White move at (8,10):"
    printfn "  Piece placed: %A" (Board.getCell newBoard { Row = 8; Col = 10 })
    printfn "  newBoard.CurrentPlayer: %A" newBoard.CurrentPlayer
    
    // Check if BLACK wins (not White!)
    let blackWins = Board.checkWinAtPosition newBoard { Row = 8; Col = 10 } White
    printfn "  Does WHITE win at (8,10)? %b" blackWins
    
    let blackWins2 = Board.checkWinAtPosition newBoard { Row = 8; Col = 10 } Black
    printfn "  Does BLACK win at (8,10)? %b" blackWins2

printfn "\n=== USING THREAT DETECTION ==="
let threat = ThreatDetection.getImmediateThreat gameBoard
match threat with
| Some pos -> 
    printfn "✅ Threat detected! Computer should play: (%d,%d)" pos.Row pos.Col
    printfn "   Expected: (8,10)"
| None -> 
    printfn "❌ NO THREAT DETECTED - BUG CONFIRMED!"
