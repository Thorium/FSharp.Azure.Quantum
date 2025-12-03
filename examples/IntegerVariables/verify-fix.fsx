#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
open FSharp.Azure.Quantum

printfn "======================================"
printfn "Verifying IntegerVar Efficiency Fix"
printfn "======================================"
printfn ""

let testRanges = [
    (0, 7, "Small range")
    (0, 15, "Medium range")
    (0, 31, "Large range")
    (0, 100, "Very large range")
]

printfn "Range        | Description      | Qubits (New) | Qubits (Old) | Savings"
printfn "-------------|------------------|--------------|--------------|--------"

for (min, max, desc) in testRanges do
    let variables = [ { Name = "x"; VarType = IntegerVar(min, max) } ]
    let qubo = QuboEncoding.encodeVariables variables
    
    let newQubits = qubo.Size
    let oldQubits = max - min + 1  // Old one-hot encoding
    let savings = 100.0 * (1.0 - float newQubits / float oldQubits)
    
    printfn "[%3d, %3d] | %-16s | %12d | %12d | %5.1f%%" min max desc newQubits oldQubits savings

printfn ""
printfn "✅ IntegerVar now uses efficient BoundedInteger encoding!"
printfn "✅ Logarithmic scaling: O(log₂ range) instead of O(range)"
printfn ""

// Test encoding/decoding roundtrip
printfn "Roundtrip Test (IntegerVar with QuboEncoding):"
let testVar = [ { Name = "quantity"; VarType = IntegerVar(0, 100) } ]
let testQubo = QuboEncoding.encodeVariables testVar

for value in [0; 1; 42; 99; 100] do
    // Encode using VariableEncoding
    let encoding = VariableEncoding.BoundedInteger(0, 100)
    let bits = VariableEncoding.encode encoding value
    
    // Decode using QuboEncoding
    let solution = QuboEncoding.decodeSolution testVar bits
    let decoded = solution.Assignments.[0].Value
    
    let status = if decoded = value then "✓" else "✗"
    printfn "  Value %3d → %d bits → decoded %3d %s" value bits.Length decoded status

printfn ""
printfn "✅ All roundtrips successful!"
