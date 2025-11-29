// Test to verify the makeMove player switching bug
#load "Board.fs"
#load "AI/ThreatDetection.fs"

open FSharp.Azure.Quantum.Examples.Gomoku
open FSharp.Azure.Quantum.Examples.Gomoku.AI

// Create the pattern: OXXXX_ at row 8
let config = { Size = 15; WinLength = 5 }
let emptyBoard = Board.create config

let cells = Array2D.copy emptyBoard.Cells
cells.[8, 5] <- White  // O
cells.[8, 6] <- Black  // X
cells.[8, 7] <- Black  // X
cells.[8, 8] <- Black  // X
cells.[8, 9] <- Black  // X

// Current player is Black (would normally be alternating, but we're testing)
let testBoard = { emptyBoard with Cells = cells; CurrentPlayer = Black }

printfn "Board state:"
printfn "%s" (Board.toString testBoard)
printfn "Current player: %A" testBoard.CurrentPlayer

// Test position (8,10) - should complete Black's 5-in-a-row
printfn "\nTesting isWinningPosition for Black at (8,10)..."

// Simulate what isWinningPosition does
match Board.makeMove testBoard { Row = 8; Col = 10 } with
| None -> printfn "  ERROR: Move failed!"
| Some newBoard ->
    printfn "  After makeMove:"
    printfn "    Piece at (8,10): %A" (Board.getCell newBoard { Row = 8; Col = 10 })
    printfn "    newBoard.CurrentPlayer: %A" newBoard.CurrentPlayer
    printfn "    Checking if BLACK wins at (8,10)..."
    let blackWins = Board.checkWinAtPosition newBoard { Row = 8; Col = 10 } Black
    printfn "    Result: %b" blackWins

// Now test via ThreatDetection
printfn "\nTesting via ThreatDetection.getImmediateThreat..."
let threat = ThreatDetection.getImmediateThreat testBoard
match threat with
| Some pos -> printfn "  Threat detected at: (%d,%d)" pos.Row pos.Col
| None -> printfn "  NO THREAT DETECTED!"
