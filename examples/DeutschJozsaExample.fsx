/// Deutsch-Jozsa Algorithm Example
/// 
/// Demonstrates the canonical first quantum algorithm from textbooks:
/// - "Learn Quantum Computing with Python and Q#" (Kaiser, 2021) - Appendix D
/// - "Quantum Programming in Depth" (Manning, 2024) - Chapter 6
/// 
/// The Deutsch-Jozsa algorithm determines if a function is constant or balanced
/// with a single query, compared to 2^(n-1)+1 queries classically.

//#r "nuget: FSharp.Azure.Quantum"
#r "../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum.Algorithms.DeutschJozsa
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction

printfn "=== Deutsch-Jozsa Algorithm Demo ==="
printfn ""

// Create quantum backend (local simulator)
let backend = LocalBackend() :> IQuantumBackend

// Number of qubits (search space = 2^n)
let numQubits = 3
let shots = 100

printfn "Testing with %d qubits (search space = %d)" numQubits (1 <<< numQubits)
printfn ""

// Test 1: Constant-Zero Oracle
printfn "Test 1: Constant-Zero Oracle (f(x) = 0 for all x)"
printfn "-----------------------------------------------"

match runConstantZero numQubits backend shots with
| Ok result ->
    printfn "%s" (formatResult result)
    printfn "Expected: Constant ✓" 
| Error err ->
    printfn "Error: %A" err

printfn ""

// Test 2: Constant-One Oracle
printfn "Test 2: Constant-One Oracle (f(x) = 1 for all x)"
printfn "----------------------------------------------"

match runConstantOne numQubits backend shots with
| Ok result ->
    printfn "%s" (formatResult result)
    printfn "Expected: Constant ✓"
| Error err ->
    printfn "Error: %A" err

printfn ""

// Test 3: Balanced First-Bit Oracle
printfn "Test 3: Balanced Oracle (f(x) = first bit of x)"
printfn "-------------------------------------------------"

match runBalancedFirstBit numQubits backend shots with
| Ok result ->
    printfn "%s" (formatResult result)
    printfn "Expected: Balanced ✓"
| Error err ->
    printfn "Error: %A" err

printfn ""

// Test 4: Balanced Parity Oracle
printfn "Test 4: Balanced Oracle (f(x) = XOR of all bits)"
printfn "--------------------------------------------------"

match runBalancedParity numQubits backend shots with
| Ok result ->
    printfn "%s" (formatResult result)
    printfn "Expected: Balanced ✓"
| Error err ->
    printfn "Error: %A" err

printfn ""
printfn "=== Quantum Advantage Demonstration ==="
printfn ""
printfn "Classical approach (deterministic):"
printfn "  - Worst case: 2^(n-1) + 1 = %d queries" ((1 <<< (numQubits - 1)) + 1)
printfn "  - Must check >50%% of inputs to be certain"
printfn ""
printfn "Quantum approach (deterministic):"
printfn "  - Exactly 1 query to oracle"
printfn "  - 100%% certainty (in theory, ~95%% on NISQ hardware with noise)"
printfn ""
printfn "Speedup factor: %dx" ((1 <<< (numQubits - 1)) + 1)
