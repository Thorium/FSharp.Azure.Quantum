// Comprehensive test to verify the threat detection bug fix
#load "Board.fs"
#load "AI/ThreatDetection.fs"

open FSharp.Azure.Quantum.Examples.Gomoku
open FSharp.Azure.Quantum.Examples.Gomoku.AI

printfn "=== THREAT DETECTION BUG FIX VERIFICATION ==="
printfn ""

let config = { Size = 15; WinLength = 5 }
let mutable allTestsPassed = true

// Test 1: Computer (White) should block Black's XXXX_ pattern
printfn "Test 1: White blocks Black's immediate threat (OXXXX_)"
let test1Board = Board.create config
let cells1 = Array2D.copy test1Board.Cells
cells1.[8, 5] <- White  // O
cells1.[8, 6] <- Black  // X
cells1.[8, 7] <- Black  // X
cells1.[8, 8] <- Black  // X
cells1.[8, 9] <- Black  // X
let gameBoard1 = { test1Board with Cells = cells1; CurrentPlayer = White }

match ThreatDetection.getImmediateThreat gameBoard1 with
| Some pos when pos.Row = 8 && pos.Col = 10 -> 
    printfn "  ✅ PASS: Detected threat at (8,10)"
| Some pos -> 
    printfn "  ❌ FAIL: Detected wrong position (%d,%d) instead of (8,10)" pos.Row pos.Col
    allTestsPassed <- false
| None -> 
    printfn "  ❌ FAIL: No threat detected!"
    allTestsPassed <- false

// Test 2: Black should block White's _OOOO_ pattern (two-ended threat)
printfn "\nTest 2: Black blocks White's immediate threat (_OOOO_)"
let test2Board = Board.create config
let cells2 = Array2D.copy test2Board.Cells
cells2.[5, 4] <- White  // O
cells2.[5, 5] <- White  // O
cells2.[5, 6] <- White  // O
cells2.[5, 7] <- White  // O
// Both (5,3) and (5,8) are valid - White wins on either side
let gameBoard2 = { test2Board with Cells = cells2; CurrentPlayer = Black }

match ThreatDetection.getImmediateThreat gameBoard2 with
| Some pos when pos.Row = 5 && (pos.Col = 3 || pos.Col = 8) -> 
    printfn "  ✅ PASS: Detected threat at (5,%d)" pos.Col
| Some pos -> 
    printfn "  ❌ FAIL: Detected wrong position (%d,%d) instead of (5,3) or (5,8)" pos.Row pos.Col
    allTestsPassed <- false
| None -> 
    printfn "  ❌ FAIL: No threat detected!"
    allTestsPassed <- false

// Test 3: Vertical threat pattern (two-ended)
printfn "\nTest 3: Vertical threat detection"
let test3Board = Board.create config
let cells3 = Array2D.copy test3Board.Cells
cells3.[3, 7] <- Black
cells3.[4, 7] <- Black
cells3.[5, 7] <- Black
cells3.[6, 7] <- Black
// Both (2,7) and (7,7) are empty - Black's winning positions
let gameBoard3 = { test3Board with Cells = cells3; CurrentPlayer = White }

match ThreatDetection.getImmediateThreat gameBoard3 with
| Some pos when pos.Col = 7 && (pos.Row = 2 || pos.Row = 7) -> 
    printfn "  ✅ PASS: Detected vertical threat at (%d,7)" pos.Row
| Some pos -> 
    printfn "  ❌ FAIL: Detected wrong position (%d,%d) instead of (2,7) or (7,7)" pos.Row pos.Col
    allTestsPassed <- false
| None -> 
    printfn "  ❌ FAIL: No vertical threat detected!"
    allTestsPassed <- false

// Test 4: Diagonal threat pattern (two-ended)
printfn "\nTest 4: Diagonal threat detection"
let test4Board = Board.create config
let cells4 = Array2D.copy test4Board.Cells
cells4.[5, 5] <- White
cells4.[6, 6] <- White
cells4.[7, 7] <- White
cells4.[8, 8] <- White
// Both (4,4) and (9,9) are empty - White's winning positions
let gameBoard4 = { test4Board with Cells = cells4; CurrentPlayer = Black }

match ThreatDetection.getImmediateThreat gameBoard4 with
| Some pos when (pos.Row = 4 && pos.Col = 4) || (pos.Row = 9 && pos.Col = 9) -> 
    printfn "  ✅ PASS: Detected diagonal threat at (%d,%d)" pos.Row pos.Col
| Some pos -> 
    printfn "  ❌ FAIL: Detected wrong position (%d,%d) instead of (4,4) or (9,9)" pos.Row pos.Col
    allTestsPassed <- false
| None -> 
    printfn "  ❌ FAIL: No diagonal threat detected!"
    allTestsPassed <- false

// Test 5: Two-ended threat (threat on both ends)
printfn "\nTest 5: Two-ended threat (can win on either side: _XXXX_)"
let test5Board = Board.create config
let cells5 = Array2D.copy test5Board.Cells
cells5.[10, 6] <- Black
cells5.[10, 7] <- Black
cells5.[10, 8] <- Black
cells5.[10, 9] <- Black
// Both (10,5) and (10,10) are winning positions
let gameBoard5 = { test5Board with Cells = cells5; CurrentPlayer = White }

match ThreatDetection.getImmediateThreat gameBoard5 with
| Some pos when (pos.Row = 10 && pos.Col = 5) || (pos.Row = 10 && pos.Col = 10) -> 
    printfn "  ✅ PASS: Detected threat at (%d,%d)" pos.Row pos.Col
| Some pos -> 
    printfn "  ❌ FAIL: Detected wrong position (%d,%d)" pos.Row pos.Col
    allTestsPassed <- false
| None -> 
    printfn "  ❌ FAIL: No two-ended threat detected!"
    allTestsPassed <- false

// Final summary
printfn ""
printfn "=========================================="
if allTestsPassed then
    printfn "✅ ALL TESTS PASSED!"
    printfn "Bug fix verified: Computer now correctly detects and blocks opponent threats."
else
    printfn "❌ SOME TESTS FAILED"
    printfn "Bug still present or new issues introduced."
printfn "=========================================="
