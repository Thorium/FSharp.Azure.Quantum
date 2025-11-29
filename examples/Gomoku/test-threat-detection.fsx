// Test threat detection for pattern XXXX_
#load "Board.fs"
#load "AI/ThreatDetection.fs"
#load "AI/Classical.fs"

open FSharp.Azure.Quantum.Examples.Gomoku
open FSharp.Azure.Quantum.Examples.Gomoku.AI

// Create test board with XXXX_ pattern (horizontal)
let config = { Size = 15; WinLength = 5 }
let emptyBoard = Board.create config

// Place 4 black pieces in a row: positions (7,5), (7,6), (7,7), (7,8)
// Gap at (7,9) - this is the critical position that MUST be blocked!
let moves = [
    { Row = 7; Col = 5 }  // Black
    { Row = 0; Col = 0 }  // White (irrelevant)
    { Row = 7; Col = 6 }  // Black
    { Row = 0; Col = 1 }  // White (irrelevant)
    { Row = 7; Col = 7 }  // Black
    { Row = 0; Col = 2 }  // White (irrelevant)
    { Row = 7; Col = 8 }  // Black - now we have XXXX_
]

let rec applyMoves board moveList =
    match moveList with
    | [] -> board
    | pos :: rest ->
        match Board.makeMove board pos with
        | Some newBoard -> applyMoves newBoard rest
        | None -> 
            printfn "ERROR: Invalid move at %A" pos
            board

let testBoard = applyMoves emptyBoard moves

printfn "Test Board State:"
printfn "=================="
printfn "Black pieces: (7,5), (7,6), (7,7), (7,8)"
printfn "Expected threat: (7,9) - MUST BLOCK HERE!"
printfn ""
printfn "Current player: %A" testBoard.CurrentPlayer

// Test 1: Check immediate threat detection
printfn "\n=== Test 1: Immediate Threat Detection ==="
match ThreatDetection.getImmediateThreat testBoard with
| Some pos -> 
    printfn "✓ Threat detected at: Row=%d, Col=%d" pos.Row pos.Col
    if pos = { Row = 7; Col = 9 } then
        printfn "✓✓ CORRECT! Detected the critical position (7,9)"
    else
        printfn "✗✗ WRONG! Expected (7,9) but got (%d,%d)" pos.Row pos.Col
| None ->
    printfn "✗✗ CRITICAL BUG! No threat detected for XXXX_ pattern!"

// Test 2: Check if Classical AI blocks it
printfn "\n=== Test 2: Classical AI Move Selection ==="
match Classical.selectBestMove testBoard with
| Some pos ->
    printfn "Classical AI chose: Row=%d, Col=%d" pos.Row pos.Col
    if pos = { Row = 7; Col = 9 } then
        printfn "✓✓ CORRECT! Classical AI blocks the threat"
    else
        printfn "✗✗ WRONG! Classical AI failed to block (7,9)"
| None ->
    printfn "✗✗ ERROR! Classical AI returned no move"

// Test 3: Check if Quantum AI blocks it
printfn "\n=== Test 3: Quantum AI Move Selection ==="
let (quantumMove, iterations) = Quantum.selectBestMove testBoard None
match quantumMove with
| Some pos ->
    printfn "Quantum AI chose: Row=%d, Col=%d (after %d Grover iterations)" pos.Row pos.Col iterations
    if pos = { Row = 7; Col = 9 } then
        printfn "✓✓ CORRECT! Quantum AI blocks the threat"
    else
        printfn "✗✗ WRONG! Quantum AI failed to block (7,9)"
| None ->
    printfn "✗✗ ERROR! Quantum AI returned no move"

// Test 4: Check threat at start of line _XXXX
printfn "\n=== Test 4: Threat at Start (_XXXX) ==="
let moves2 = [
    { Row = 8; Col = 6 }  // Black
    { Row = 0; Col = 3 }  // White
    { Row = 8; Col = 7 }  // Black
    { Row = 0; Col = 4 }  // White
    { Row = 8; Col = 8 }  // Black
    { Row = 0; Col = 5 }  // White
    { Row = 8; Col = 9 }  // Black - now we have _XXXX at position (8,5)
]

let testBoard2 = applyMoves emptyBoard moves2
printfn "Black pieces: (8,6), (8,7), (8,8), (8,9)"
printfn "Expected threat: (8,5) - MUST BLOCK HERE!"

match ThreatDetection.getImmediateThreat testBoard2 with
| Some pos ->
    printfn "✓ Threat detected at: Row=%d, Col=%d" pos.Row pos.Col
    if pos = { Row = 8; Col = 5 } then
        printfn "✓✓ CORRECT! Detected start position (8,5)"
    else
        printfn "✗ Detected different position (%d,%d)" pos.Row pos.Col
| None ->
    printfn "✗✗ CRITICAL BUG! No threat detected for _XXXX pattern!"

// Test 5: Check gap in middle XXX_X
printfn "\n=== Test 5: Gap in Middle (XXX_X) ==="
let moves3 = [
    { Row = 9; Col = 5 }  // Black
    { Row = 0; Col = 6 }  // White
    { Row = 9; Col = 6 }  // Black
    { Row = 0; Col = 7 }  // White
    { Row = 9; Col = 7 }  // Black
    { Row = 0; Col = 8 }  // White
    { Row = 9; Col = 9 }  // Black - now we have XXX_X with gap at (9,8)
]

let testBoard3 = applyMoves emptyBoard moves3
printfn "Black pieces: (9,5), (9,6), (9,7), (9,9)"
printfn "Expected threat: (9,8) - MUST BLOCK HERE!"

match ThreatDetection.getImmediateThreat testBoard3 with
| Some pos ->
    printfn "✓ Threat detected at: Row=%d, Col=%d" pos.Row pos.Col
    if pos = { Row = 9; Col = 8 } then
        printfn "✓✓ CORRECT! Detected gap position (9,8)"
    else
        printfn "✗ Detected different position (%d,%d)" pos.Row pos.Col
| None ->
    printfn "✗✗ CRITICAL BUG! No threat detected for XXX_X pattern!"

printfn "\n=== Test Complete ==="
