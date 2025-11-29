// Debug test with detailed output
#r "bin/Debug/net10.0/Gomoku.dll"

open FSharp.Azure.Quantum.Examples.Gomoku
open FSharp.Azure.Quantum.Examples.Gomoku.AI

let board = Board.create BoardConfig.standard15x15

// Pattern: OXXXX_ at row 8, cols 5-10
let moves = [
    (7, 7)   // Black
    (8, 5)   // White at (8,5) - O
    (8, 6)   // Black at (8,6) - X
    (6, 6)   // White
    (8, 7)   // Black at (8,7) - X
    (6, 7)   // White
    (8, 8)   // Black at (8,8) - X  
    (6, 8)   // White
    (8, 9)   // Black at (8,9) - X
]

let rec makeMoves b moveList =
    match moveList with
    | [] -> b
    | (r, c) :: rest ->
        match Board.makeMove b { Row = r; Col = c } with
        | Some newB -> makeMoves newB rest
        | None -> failwithf "Invalid move at (%d,%d)" r c

let testBoard = makeMoves board moves

printfn "=== Board State ==="
printfn "Row 8: OXXXX_ (cols 5-9 occupied, col 10 is the threat)"
printfn "Current player: %A (should be White)" testBoard.CurrentPlayer
printfn ""

// Manually check what happens if Black plays at (8,10)
printfn "=== Manual Check: If Black plays at (8,10) ==="
match Board.makeMove testBoard { Row = 8; Col = 10 } with
| None -> printfn "ERROR: Can't place at (8,10)"
| Some testBoardAfter ->
    printfn "After Black at (8,10), checking for win..."
    
    // Check if Black wins
    match Board.checkWin testBoardAfter { Row = 8; Col = 10 } with
    | Some winner ->
        printfn "✓ %A wins! (5-in-a-row detected)" winner
        if winner = Cell.Black then
            printfn "✓✓ Black wins by playing at (8,10) - this is a CRITICAL THREAT!"
    | None ->
        printfn "✗ No win detected - this is the bug!"
        
    // Manual row check
    let row8 = [for c in 0..14 -> Board.getCell testBoardAfter { Row = 8; Col = c }]
    printfn "\nRow 8 after move:"
    for c in 0..14 do
        let cell = row8.[c]
        let symbol = match cell with
                     | Cell.Empty -> '.'
                     | Cell.Black -> 'X'
                     | Cell.White -> 'O'
        printf "%c" symbol
    printfn ""
    
    // Count consecutive Black pieces at (8,10)
    let mutable count = 1
    let mutable c = 9
    while c >= 0 && Board.getCell testBoardAfter { Row = 8; Col = c } = Cell.Black do
        count <- count + 1
        c <- c - 1
    printfn "Consecutive Black pieces from (8,10) going backward: %d" count

printfn ""
printfn "=== Threat Detection Result ==="
match ThreatDetection.getImmediateThreat testBoard with
| Some pos ->
    printfn "Threat detected at: (%d,%d)" pos.Row pos.Col
    if pos.Row = 8 && pos.Col = 10 then
        printfn "✓✓ CORRECT - Will block the threat!"
    else
        printfn "✗✗ WRONG POSITION - Will NOT block (8,10)!"
        printfn "   Computer will play at (%d,%d) instead and lose!" pos.Row pos.Col
| None ->
    printfn "✗✗ NO THREAT DETECTED - Computer will lose!"

printfn ""
printfn "=== Legal Moves Count ==="
let legalMoves = Board.getLegalMoves testBoard
printfn "Total legal moves: %d" legalMoves.Length
printfn "Is (8,10) in legal moves? %b" (legalMoves |> List.contains { Row = 8; Col = 10 })
