// Test script to debug countInDirection function
#load "Board.fs"

open FSharp.Azure.Quantum.Examples.Gomoku

// Create a simple 15x15 board
let config = { Size = 15; WinLength = 5 }
let emptyBoard = Board.create config

// Set up the exact pattern: row 8 has OXXXX_ at positions 5-10
// O at (8,5), X at (8,6), X at (8,7), X at (8,8), X at (8,9), empty at (8,10)
let board1 = Board.makeMove emptyBoard { Row = 8; Col = 5 } |> Option.get  // White
let board2 = Board.makeMove board1 { Row = 8; Col = 6 } |> Option.get      // Black
let board3 = Board.makeMove board2 { Row = 8; Col = 7 } |> Option.get      // White
let board4 = Board.makeMove board3 { Row = 8; Col = 8 } |> Option.get      // Black
let board5 = Board.makeMove board4 { Row = 8; Col = 9 } |> Option.get      // White (now current is Black)

printfn "Board state:"
printfn "%s" (Board.toString board5)

// Now test: if we place Black at (8,10), does it create 5-in-a-row?
let testBoard = Board.makeMove board5 { Row = 8; Col = 10 } |> Option.get

printfn "\nAfter placing Black at (8,10):"
printfn "%s" (Board.toString testBoard)

// Check if Black wins at position (8,10)
let blackWins = Board.checkWinAtPosition testBoard { Row = 8; Col = 10 } Black

printfn "\nDoes Black win at (8,10)? %b" blackWins
printfn "Expected: true (Black has 5 in a row: positions 6,7,8,9,10)"

// Let's manually count to see what's happening
printfn "\nManual verification:"
for col in 6 .. 10 do
    let cell = Board.getCell testBoard { Row = 8; Col = col }
    printfn "  Position (8,%d): %A" col cell
