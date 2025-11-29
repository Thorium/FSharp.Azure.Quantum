// Test script - directly set board state without using makeMove
#load "Board.fs"

open FSharp.Azure.Quantum.Examples.Gomoku

// Create a 15x15 board
let config = { Size = 15; WinLength = 5 }
let emptyBoard = Board.create config

// Directly create the pattern: OXXXX_ at row 8, cols 5-10
// O at (8,5), X at (8,6-9), empty at (8,10)
let cells = Array2D.copy emptyBoard.Cells
cells.[8, 5] <- White  // O
cells.[8, 6] <- Black  // X
cells.[8, 7] <- Black  // X
cells.[8, 8] <- Black  // X
cells.[8, 9] <- Black  // X
// (8,10) remains Empty

let testBoard = { emptyBoard with Cells = cells }

printfn "Board state (row 8 should show: OXXXX_):"
printfn "%s" (Board.toString testBoard)

// Now simulate placing Black at (8,10) to complete OXXXXX
let cells2 = Array2D.copy cells
cells2.[8, 10] <- Black
let boardWithMove = { testBoard with Cells = cells2 }

printfn "\nAfter placing Black at (8,10):"
printfn "%s" (Board.toString boardWithMove)

// Check if Black wins at position (8,10)
let blackWins = Board.checkWinAtPosition boardWithMove { Row = 8; Col = 10 } Black

printfn "\nDoes Black win at (8,10)? %b" blackWins
printfn "Expected: true (Black has 5 in a row at positions 6,7,8,9,10)"

// Manual verification
printfn "\nManual check of row 8:"
for col in 5 .. 10 do
    let cell = Board.getCell boardWithMove { Row = 8; Col = col }
    printfn "  Position (8,%d): %A" col cell
