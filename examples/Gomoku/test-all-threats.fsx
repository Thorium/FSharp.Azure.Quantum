// Debug ALL threats
#r "bin/Debug/net10.0/Gomoku.dll"

open FSharp.Azure.Quantum.Examples.Gomoku
open FSharp.Azure.Quantum.Examples.Gomoku.AI

let board = Board.create BoardConfig.standard15x15

// Pattern: OXXXX_ at row 8
let moves = [
    (7, 7)   // Black
    (8, 5)   // White - O
    (8, 6)   // Black - X
    (6, 6)   // White - O
    (8, 7)   // Black - X
    (6, 7)   // White - O
    (8, 8)   // Black - X  
    (6, 8)   // White - O
    (8, 9)   // Black - X (now OXXXX_ at row 8, and OOO at row 6!)
]

let rec makeMoves b moveList =
    match moveList with
    | [] -> b
    | (r, c) :: rest ->
        match Board.makeMove b { Row = r; Col = c } with
        | Some newB -> makeMoves newB rest
        | None -> failwithf "Invalid move at (%d,%d)" r c

let testBoard = makeMoves board moves

printfn "Board state:"
printfn "Row 6: ····OOO······· (White at cols 6,7,8)"
printfn "Row 7: ·····XOX······ (Black at col 5, White at col 6, Black at col 7)"
printfn "Row 8: ·····OXXXX···· (White at col 5, Black at cols 6-9)"
printfn ""
printfn "Current player: %A" testBoard.CurrentPlayer
printfn ""

// Print board around the critical area
for r in 5..10 do
    printf "Row %2d: " r
    for c in 3..12 do
        let cell = Board.getCell testBoard { Row = r; Col = c }
        let symbol = match cell with
                     | Cell.Empty -> '·'
                     | Cell.Black -> 'X'
                     | Cell.White -> 'O'
        printf "%c" symbol
    printfn ""

printfn ""
printfn "=== Checking specific threats manually ==="

// Check (8,10) - should win for Black
printfn "\n1. Position (8,10) - completes Black's XXXX_:"
match Board.makeMove testBoard { Row = 8; Col = 10 } with
| Some newBoard ->
    let isWin = Board.checkWinAtPosition newBoard { Row = 8; Col = 10 } Cell.Black
    printfn "   Would Black win? %b" isWin
    if isWin then
        printfn "   ✓ This IS a winning threat for Black!"
| None ->
    printfn "   ERROR: Can't place at (8,10)"

// Check (6,5) - what threat is this?
printfn "\n2. Position (6,5) - what threat is this?"
match Board.makeMove testBoard { Row = 6; Col = 5 } with
| Some newBoard ->
    let whiteWins = Board.checkWinAtPosition newBoard { Row = 6; Col = 5 } Cell.White
    let blackWins = Board.checkWinAtPosition newBoard { Row = 6; Col = 5 } Cell.Black
    printfn "   Would White win? %b" whiteWins
    printfn "   Would Black win? %b" blackWins
| None ->
    printfn "   ERROR: Can't place at (6,5)"

printfn ""
printfn "=== getImmediateThreat result ==="
match ThreatDetection.getImmediateThreat testBoard with
| Some pos ->
    printfn "Detected threat at: (%d,%d)" pos.Row pos.Col
| None ->
    printfn "No threat detected"
