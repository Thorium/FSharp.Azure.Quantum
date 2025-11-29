// Test with the EXACT pattern from user's game: Row 8 = ·····OXXXX·····
// Position (8,10) should be blocked!
#r "bin/Debug/net10.0/Gomoku.dll"

open FSharp.Azure.Quantum.Examples.Gomoku
open FSharp.Azure.Quantum.Examples.Gomoku.AI

let board = Board.create BoardConfig.standard15x15

// Recreate the exact pattern:
// Row 8: White at col 5, Black at cols 6,7,8,9
let moves = [
    (7, 7)   // Black - some other move
    (8, 5)   // White at (8,5)
    (8, 6)   // Black 
    (6, 6)   // White - dummy
    (8, 7)   // Black
    (6, 7)   // White - dummy
    (8, 8)   // Black
    (6, 8)   // White - dummy
    (8, 9)   // Black - now we have OXXXX_ at row 8
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

printfn "===== EXACT GAME SCENARIO ====="
printfn "Row 8 pattern: ·····OXXXX·····"
printfn "  Col 5: White (O)"
printfn "  Col 6-9: Black (XXXX)"
printfn "  Col 10: EMPTY - THIS MUST BE BLOCKED!"
printfn ""
printfn "Current player: %A" testBoard.CurrentPlayer
printfn "Move history length: %d" testBoard.MoveHistory.Length
printfn ""

// Test threat detection
printfn "=== Threat Detection Test ==="
match ThreatDetection.getImmediateThreat testBoard with
| Some pos ->
    printfn "✓ Threat detected at (%d,%d)" pos.Row pos.Col
    if pos = { Row = 8; Col = 10 } then
        printfn "  ✓✓ CORRECT! Will block at (8,10)"
    else
        printfn "  ✗✗ WRONG! Expected (8,10) but got (%d,%d)" pos.Row pos.Col
        printfn "     This means the computer will NOT block the threat!"
| None ->
    printfn "✗✗ CATASTROPHIC BUG! NO THREAT DETECTED!"
    printfn "   Computer will make a random move and lose next turn!"

printfn ""
printfn "=== Classical AI Decision ==="
match Classical.selectBestMove testBoard with
| Some pos ->
    printfn "Classical AI chose: (%d,%d)" pos.Row pos.Col
    if pos = { Row = 8; Col = 10 } then
        printfn "  ✓ Would block correctly"
    else
        printfn "  ✗ Would NOT block - game lost!"
| None ->
    printfn "✗ AI returned no move"

printfn ""
printfn "=== Quantum AI Decision ==="
let (quantumMove, iters) = Quantum.selectBestMove testBoard None
match quantumMove with
| Some pos ->
    printfn "Quantum AI chose: (%d,%d) (after %d Grover iterations)" pos.Row pos.Col iters
    if pos = { Row = 8; Col = 10 } then
        printfn "  ✓ Would block correctly"
    else
        printfn "  ✗ Would NOT block - game lost!"
| None ->
    printfn "✗ AI returned no move"
