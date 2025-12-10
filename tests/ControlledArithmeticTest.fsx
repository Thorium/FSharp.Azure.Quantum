// Quick test for controlled arithmetic operations

#r "../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Algorithms.Arithmetic
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

printfn "Testing Controlled Arithmetic Operations..."
printfn "==========================================="

// Create backend
let backend = LocalBackend() :> IQuantumBackend

// Test 1: Controlled addition with control = |0⟩ (should not add)
printfn "\nTest 1: Controlled addition with control = |0⟩"
let numQubits = 3
let state1Result = backend.InitializeState (numQubits + 1)  // 3 register qubits + 1 control

match state1Result with
| Ok state1 ->
    // Control qubit is 0, register qubits are [1, 2, 3]
    // Initial state: |0⟩ ⊗ |000⟩
    let result1 = controlledAddConstant 0 [1; 2; 3] 5 state1 backend
    
    match result1 with
    | Ok res ->
        printfn "✓ Controlled addition (control=0) succeeded"
        printfn "  Operations: %d" res.OperationCount
    | Error err ->
        printfn "✗ Controlled addition (control=0) failed: %A" err
| Error err ->
    printfn "✗ Failed to initialize state: %A" err

// Test 2: Controlled addition with control = |1⟩ (should add)
printfn "\nTest 2: Controlled addition with control = |1⟩"

match backend.InitializeState (numQubits + 1) with
| Ok state2 ->
    // Apply X to control qubit to set it to |1⟩
    match backend.ApplyOperation (QuantumOperation.Gate (CircuitBuilder.X 0)) state2 with
    | Ok state2_controlled ->
        match controlledAddConstant 0 [1; 2; 3] 5 state2_controlled backend with
        | Ok res ->
            printfn "✓ Controlled addition (control=1) succeeded"
            printfn "  Operations: %d" res.OperationCount
        | Error err ->
            printfn "✗ Controlled addition (control=1) failed: %A" err
    | Error err ->
        printfn "✗ Failed to apply X gate: %A" err
| Error err ->
    printfn "✗ Failed to initialize state: %A" err

// Test 3: Doubly-controlled addition
printfn "\nTest 3: Doubly-controlled addition (both controls = |1⟩)"

// Need 2 controls + 3 register + 1 ancilla = 6 qubits total
match backend.InitializeState 6 with
| Ok state3 ->
    // Apply X to both control qubits
    match backend.ApplyOperation (QuantumOperation.Gate (CircuitBuilder.X 0)) state3 with
    | Ok state3_c1 ->
        match backend.ApplyOperation (QuantumOperation.Gate (CircuitBuilder.X 1)) state3_c1 with
        | Ok state3_c2 ->
            match doublyControlledAddConstant 0 1 [2; 3; 4] 3 state3_c2 backend with
            | Ok res ->
                printfn "✓ Doubly-controlled addition succeeded"
                printfn "  Operations: %d" res.OperationCount
            | Error err ->
                printfn "✗ Doubly-controlled addition failed: %A" err
        | Error err ->
            printfn "✗ Failed to apply second X gate: %A" err
    | Error err ->
        printfn "✗ Failed to apply first X gate: %A" err
| Error err ->
    printfn "✗ Failed to initialize state: %A" err

// Test 4: Controlled subtraction
printfn "\nTest 4: Controlled subtraction"

match backend.InitializeState (numQubits + 1) with
| Ok state4 ->
    // Apply X to control qubit
    match backend.ApplyOperation (QuantumOperation.Gate (CircuitBuilder.X 0)) state4 with
    | Ok state4_controlled ->
        match controlledSubtractConstant 0 [1; 2; 3] 2 state4_controlled backend with
        | Ok res ->
            printfn "✓ Controlled subtraction succeeded"
            printfn "  Operations: %d" res.OperationCount
        | Error err ->
            printfn "✗ Controlled subtraction failed: %A" err
    | Error err ->
        printfn "✗ Failed to apply X gate: %A" err
| Error err ->
    printfn "✗ Failed to initialize state: %A" err

printfn "\n==========================================="
printfn "All controlled arithmetic tests completed!"
