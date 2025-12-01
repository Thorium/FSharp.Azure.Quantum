// Verification script for README.md F# syntax examples
// This script tests that all computation expression examples compile correctly

#r "src/FSharp.Azure.Quantum/bin/Release/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum
open System

printfn "=== Verifying README.md F# Syntax Examples ==="
printfn ""

// Test 1: quantumTreeSearch (not tree)
printfn "✓ Testing QuantumTreeSearch.quantumTreeSearch computation expression..."
try
    let treeTest = QuantumTreeSearch.quantumTreeSearch {
        initialState [1; 2; 3]
        maxDepth 3
        branchingFactor 2
        evaluateWith (fun state -> float (List.sum state))
        generateMovesWith (fun state -> [state @ [4]; state @ [5]])
    }
    printfn "  ✓ QuantumTreeSearch.quantumTreeSearch { } compiles correctly"
with ex ->
    printfn "  ✗ ERROR: %s" ex.Message
    
printfn ""

// Test 2: constraintSolver
printfn "✓ Testing QuantumConstraintSolver.constraintSolver computation expression..."
try
    let constraintTest = QuantumConstraintSolver.constraintSolver {
        searchSpace 4
        domain [1; 2; 3; 4]
        satisfies (fun _ -> true)
    }
    printfn "  ✓ QuantumConstraintSolver.constraintSolver { } compiles correctly"
with ex ->
    printfn "  ✗ ERROR: %s" ex.Message

printfn ""

// Test 3: patternMatcher
printfn "✓ Testing QuantumPatternMatcher.patternMatcher computation expression..."
try
    let patternTest = QuantumPatternMatcher.patternMatcher {
        searchSpace [1; 2; 3; 4; 5]
        matchPattern (fun x -> x > 3)
        findTop 2
    }
    printfn "  ✓ QuantumPatternMatcher.patternMatcher { } compiles correctly"
with ex ->
    printfn "  ✗ ERROR: %s" ex.Message

printfn ""

// Test 4: quantumArithmetic (not arithmetic)
printfn "✓ Testing QuantumArithmeticOps.quantumArithmetic computation expression..."
try
    let arithmeticTest = QuantumArithmeticOps.quantumArithmetic {
        operands 15 27
        operation QuantumArithmeticOps.Add
        qubits 10
    }
    printfn "  ✓ QuantumArithmeticOps.quantumArithmetic { } compiles correctly"
with ex ->
    printfn "  ✗ ERROR: %s" ex.Message

printfn ""

// Test 5: periodFinder (not period)
printfn "✓ Testing QuantumPeriodFinder.periodFinder computation expression..."
try
    let periodTest = QuantumPeriodFinder.periodFinder {
        number 15
        precision 8
    }
    printfn "  ✓ QuantumPeriodFinder.periodFinder { } compiles correctly"
with ex ->
    printfn "  ✗ ERROR: %s" ex.Message

printfn ""

// Test 6: phaseEstimator
printfn "✓ Testing QuantumPhaseEstimator.phaseEstimator computation expression..."
try
    let phaseTest = QuantumPhaseEstimator.phaseEstimator {
        unitary (QuantumPhaseEstimator.RotationZ (Math.PI / 4.0))
        precision 12
    }
    printfn "  ✓ QuantumPhaseEstimator.phaseEstimator { } compiles correctly"
with ex ->
    printfn "  ✗ ERROR: %s" ex.Message

printfn ""
printfn "=== Verification Complete ==="
printfn ""
printfn "Summary of correct computation expression names:"
printfn "  1. QuantumTreeSearch.quantumTreeSearch { }"
printfn "  2. QuantumConstraintSolver.constraintSolver { }"
printfn "  3. QuantumPatternMatcher.patternMatcher { }"
printfn "  4. QuantumArithmeticOps.quantumArithmetic { }"
printfn "  5. QuantumPeriodFinder.periodFinder { }"
printfn "  6. QuantumPhaseEstimator.phaseEstimator { }"
