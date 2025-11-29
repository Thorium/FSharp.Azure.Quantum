// Quick test script for win detection
#r "bin/Debug/net10.0/Gomoku.dll"

open FSharp.Azure.Quantum.Examples.Gomoku

let testWin (description: string) (moves: (int * int) list) (expectedWinner: Cell option) =
    printfn "Testing: %s" description
    
    let config = BoardConfig.standard15x15
    let mutable board = Board.create config
    
    // Apply moves
    for (row, col) in moves do
        let pos = { Row = row; Col = col }
        match Board.makeMove board pos with
        | Some newBoard -> board <- newBoard
        | None -> failwithf "Invalid move at (%d, %d)" row col
    
    // Check result
    let winner = Board.checkWin board
    
    match winner, expectedWinner with
    | Some w, Some ew when w = ew ->
        printfn "✅ PASS: Winner detected correctly (%A)" w
    | None, None ->
        printfn "✅ PASS: No winner (as expected)"
    | Some w, expected ->
        printfn "❌ FAIL: Expected %A, got Some %A" expected w
    | None, Some expected ->
        printfn "❌ FAIL: Expected Some %A, got None" expected
    
    printfn ""

// Test 1: Horizontal win
testWin "Horizontal win (Black)" 
    [(0, 0); (1, 0); (0, 1); (1, 1); (0, 2); (1, 2); (0, 3); (1, 3); (0, 4)]
    (Some Black)

// Test 2: Vertical win
testWin "Vertical win (White)"
    [(0, 0); (0, 1); (1, 0); (1, 1); (2, 0); (2, 1); (3, 0); (3, 1); (5, 5); (4, 1)]
    (Some White)

// Test 3: Diagonal down win
testWin "Diagonal down win (Black)"
    [(0, 0); (0, 1); (1, 1); (0, 2); (2, 2); (0, 3); (3, 3); (0, 4); (4, 4)]
    (Some Black)

// Test 4: Diagonal up win
testWin "Diagonal up win (White)"
    [(0, 0); (4, 0); (0, 1); (3, 1); (0, 2); (2, 2); (0, 3); (1, 3); (1, 4); (0, 4)]
    (Some White)

// Test 5: No win yet (only 4 in a row)
testWin "No win (4 in a row)"
    [(0, 0); (1, 0); (0, 1); (1, 1); (0, 2); (1, 2); (0, 3)]
    None

printfn "Win detection tests complete!"
