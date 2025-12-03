#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
open FSharp.Azure.Quantum

// Test 1: VariableEncoding.BoundedInteger (efficient)
printfn "=== Test 1: VariableEncoding.BoundedInteger ==="
let efficientEncoding = VariableEncoding.BoundedInteger(0, 20)
printfn "Range [0, 20] → Qubits: %d" (VariableEncoding.qubitCount efficientEncoding)
printfn ""

// Test 2: IntegerVar with QuboEncoding (check actual implementation)
printfn "=== Test 2: IntegerVar with QuboEncoding ==="
let variables = [
    { Name = "test_int"; VarType = IntegerVar(0, 20) }
]
let qubo = QuboEncoding.encodeVariables variables
printfn "Range [0, 20] → Qubits: %d" qubo.Size
printfn "Variable names count: %d" qubo.VariableNames.Length
printfn ""

printfn "ISSUE: If these numbers don't match, we have a problem!"
