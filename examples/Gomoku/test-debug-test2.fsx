// Debug Test 2 specifically
#load "Board.fs"
#load "AI/ThreatDetection.fs"

open FSharp.Azure.Quantum.Examples.Gomoku
open FSharp.Azure.Quantum.Examples.Gomoku.AI

let config = { Size = 15; WinLength = 5 }
let testBoard = Board.create config
let cells = Array2D.copy testBoard.Cells
cells.[5, 4] <- White  // O at col 4
cells.[5, 5] <- White  // O at col 5
cells.[5, 6] <- White  // O at col 6
cells.[5, 7] <- White  // O at col 7
// Empty at (5,3) and (5,8) - BOTH are winning positions for White!

let gameBoard = { testBoard with Cells = cells; CurrentPlayer = Black }

printfn "Board state (row 5: _OOOO_):"
printfn "Row 5: "
for col in 3 .. 8 do
    let cell = Board.getCell gameBoard { Row = 5; Col = col }
    printf "%A " cell
printfn ""

printfn "\nChecking both potential threat positions:"

// Check (5,3)
printfn "\nPosition (5,3):"
let boardWithWhite3 = { gameBoard with CurrentPlayer = White }
match Board.makeMove boardWithWhite3 { Row = 5; Col = 3 } with
| Some newBoard ->
    let wins = Board.checkWinAtPosition newBoard { Row = 5; Col = 3 } White
    printfn "  Would White win at (5,3)? %b" wins
| None -> printfn "  Invalid move"

// Check (5,8)
printfn "\nPosition (5,8):"
let boardWithWhite8 = { gameBoard with CurrentPlayer = White }
match Board.makeMove boardWithWhite8 { Row = 5; Col = 8 } with
| Some newBoard ->
    let wins = Board.checkWinAtPosition newBoard { Row = 5; Col = 8 } White
    printfn "  Would White win at (5,8)? %b" wins
| None -> printfn "  Invalid move"

printfn "\nUsing ThreatDetection:"
match ThreatDetection.getImmediateThreat gameBoard with
| Some pos -> printfn "  Detected: (%d,%d)" pos.Row pos.Col
| None -> printfn "  No threat detected"

printfn "\nBOTH positions are valid threats! Either should work."
