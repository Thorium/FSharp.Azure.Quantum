#!/usr/bin/env dotnet fsi

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.GroverSearch

printfn "==================================="
printfn "Grover Backend Integration Test"
printfn "==================================="
printfn ""

// Test 1: Basic Backend Execution
printfn "Test 1: Search for value 7 in 3-qubit space using LocalBackend"
printfn "---------------------------------------------------------------"

let backend = createLocalBackend()
printfn "✓ Created LocalBackend (max qubits: %d)" backend.MaxQubits

let config = {
    Search.MaxIterations = Some 10
    Search.OptimizeIterations = true
    Search.SuccessThreshold = 0.5
    Search.Shots = 1000
    Search.RandomSeed = None
}

match Search.searchSingleWithBackend 7 3 backend config with
| Error msg ->
    printfn "❌ Test 1 FAILED: %s" msg
    exit 1
| Ok result ->
    printfn "✓ Search completed:"
    printfn "  - Solutions found: %A" result.Solutions
    printfn "  - Success probability: %.2f%%" (result.SuccessProbability * 100.0)
    printfn "  - Iterations: %d" result.IterationsApplied
    printfn "  - Shots: %d" result.Shots
    
    if result.Success && List.contains 7 result.Solutions then
        printfn "✅ Test 1 PASSED: Found target value 7"
    else
        printfn "❌ Test 1 FAILED: Did not find target value 7"
        exit 1

printfn ""

// Test 2: Tree Search Integration
printfn "Test 2: Quantum Tree Search - Simple Game"
printfn "-------------------------------------------"

type SimpleGameState = int

let gameConfig = {
    TreeSearch.MaxDepth = 2
    TreeSearch.BranchingFactor = 3
    TreeSearch.EvaluationFunction = float  // Maximize value
    TreeSearch.MoveGenerator = fun x -> [x + 1; x + 2; x + 3]
}

let rootState = 0

match TreeSearch.searchGameTree rootState gameConfig backend 0.3 with
| Error msg ->
    printfn "❌ Test 2 FAILED: %s" msg
    exit 1
| Ok result ->
    printfn "✓ Tree search completed:"
    printfn "  - Best move: %d" result.BestMove
    printfn "  - Score: %.2f%%" (result.Score * 100.0)
    printfn "  - Nodes explored: %d" result.NodesExplored
    printfn "  - Quantum advantage: %b" result.QuantumAdvantage
    
    if result.BestMove > 0 then
        printfn "✅ Test 2 PASSED: Found valid move"
    else
        printfn "❌ Test 2 FAILED: Invalid move returned"
        exit 1

printfn ""

// Test 3: Resource Estimation
printfn "Test 3: Resource Estimation"
printfn "----------------------------"

let qubitsNeeded = TreeSearch.estimateQubitsNeeded 3 4
let searchSpace = TreeSearch.estimateSearchSpaceSize 3 4

printfn "✓ Resource estimation:"
printfn "  - Qubits needed (depth=3, branching=4): %d" qubitsNeeded
printfn "  - Search space size: %d" searchSpace

if qubitsNeeded > 0 && searchSpace > 0 then
    printfn "✅ Test 3 PASSED: Resource estimation correct"
else
    printfn "❌ Test 3 FAILED: Invalid resource estimates"
    exit 1

printfn ""
printfn "==================================="
printfn "✅ ALL TESTS PASSED!"
printfn "==================================="
printfn ""
printfn "Summary:"
printfn "- Grover backend execution: ✓"
printfn "- Quantum tree search: ✓"
printfn "- Resource estimation: ✓"
printfn ""
printfn "Next steps:"
printfn "1. Integrate with Gomoku AI (AI/QuantumTreeSearch.fs)"
printfn "2. Test with real game positions"
printfn "3. Benchmark quantum vs classical search"
