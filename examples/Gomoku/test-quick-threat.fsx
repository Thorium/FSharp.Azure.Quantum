// Simple test for OXXXX_ threat
#r "bin/Debug/net10.0/Gomoku.dll"

open FSharp.Azure.Quantum.Examples.Gomoku
open FSharp.Azure.Quantum.Examples.Gomoku.AI

let board = Board.create BoardConfig.standard15x15

// Pattern: OXXXX_ at row 8
let moves = [
    (7, 7)   // Black
    (8, 5)   // White - O
    (8, 6)   // Black - X
    (6, 6)   // White
    (8, 7)   // Black - X
    (6, 7)   // White
    (8, 8)   // Black - X  
    (6, 8)   // White
    (8, 9)   // Black - X (now OXXXX_)
]

let rec makeMoves b moveList =
    match moveList with
    | [] -> b
    | (r, c) :: rest ->
        match Board.makeMove b { Row = r; Col = c } with
        | Some newB -> makeMoves newB rest
        | None -> failwithf "Invalid move at (%d,%d)" r c

let testBoard = makeMoves board moves

printfn "Row 8: OXXXX_ (White at col 5, Black at cols 6-9)"
printfn "Current player: %A" testBoard.CurrentPlayer
printfn ""

printfn "=== Testing Threat Detection ==="
match ThreatDetection.getImmediateThreat testBoard with
| Some pos ->
    printfn "Threat detected at: (%d,%d)" pos.Row pos.Col
    if pos.Row = 8 && pos.Col = 10 then
        printfn "✓✓ CORRECT! Will block Black's threat at (8,10)"
    else
        printfn "✗✗ BUG! Expected (8,10) but got (%d,%d)" pos.Row pos.Col
| None ->
    printfn "✗✗ CRITICAL BUG! No threat detected!"
