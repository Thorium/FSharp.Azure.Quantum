// Simple test for XXXX_ threat detection
#r "bin/Debug/net10.0/Gomoku.dll"

open FSharp.Azure.Quantum.Examples.Gomoku
open FSharp.Azure.Quantum.Examples.Gomoku.AI

// Create board
let board = Board.create BoardConfig.standard15x15

// Manually place pieces to create XXXX_ at row 8
// O at (8,5), then XXXX at (8,6-9), gap at (8,10)
let moves = [
    (8, 5)   // Black
    (0, 0)   // White (dummy)
    (8, 6)   // Black
    (0, 1)   // White (dummy)
    (8, 7)   // Black
    (0, 2)   // White (dummy)
    (8, 8)   // Black
]

let rec makeMoves b moveList =
    match moveList with
    | [] -> b
    | (r, c) :: rest ->
        match Board.makeMove b { Row = r; Col = c } with
        | Some newB -> makeMoves newB rest
        | None -> 
            printfn "ERROR at (%d,%d)" r c
            b

let testBoard = makeMoves board moves

printfn "Board state after moves:"
printfn "Row 8: Pieces at cols 5,6,7,8"
printfn "Current player: %A" testBoard.CurrentPlayer
printfn ""

// Now White's turn - should detect Black's threat at (8,9)
printfn "=== Testing threat detection ==="

// Use the public API: getImmediateThreat
match ThreatDetection.getImmediateThreat testBoard with
| Some pos ->
    printfn "✓ Threat detected at (%d,%d)" pos.Row pos.Col
    if pos = { Row = 8; Col = 9 } then
        printfn "  ✓✓ CORRECT! Detected threat at (8,9)"
    else
        printfn "  ✗ Wrong position (expected 8,9 but got %d,%d)" pos.Row pos.Col
| None ->
    printfn "✗✗ CRITICAL BUG! NO THREAT DETECTED for XXXX_ pattern!"
    printfn ""
    printfn "Expected: Black at (8,5)(8,6)(8,7)(8,8) with gap at (8,9)"
    printfn "Computer (White) should have blocked at (8,9)"
