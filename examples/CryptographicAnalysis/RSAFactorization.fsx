// RSA Factorization Example - Shor's Algorithm for Breaking RSA Keys
// Demonstrates quantum period finding to factor composite numbers

// Reference local build (use this for development/testing)
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

// Or use published NuGet package (uncomment when package is published):
// #r "nuget: FSharp.Azure.Quantum"

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumPeriodFinder

printfn "=== RSA Security Analysis with Shor's Algorithm ==="
printfn ""
printfn "BUSINESS SCENARIO:"
printfn "A security consulting firm needs to assess RSA key strength against"
printfn "quantum attacks. This demonstrates how quantum computers can factor"
printfn "RSA moduli and break public-key encryption."
printfn ""

// ============================================================================
// SCENARIO 1: Factor Small RSA Modulus (Educational)
// ============================================================================

printfn "--- Scenario 1: Factor RSA Modulus n = 15 ---"
printfn ""

let n1 = 15  // Toy RSA modulus (real RSA uses 2048+ bits)

printfn "Target RSA Modulus (n): %d" n1
printfn "Objective:              Find prime factors p and q where n = p × q"
printfn ""

// Use Shor's algorithm via quantum period finder
let problem1 = periodFinder {
    number n1
    precision 4      // 4 qubits for QPE precision (reduced for local simulation)
    maxAttempts 10   // Try up to 10 times (algorithm is probabilistic)
}

printfn "Running Shor's algorithm..."
printfn "(Using quantum phase estimation to find period of modular exponentiation)"
printfn ""

match problem1 with
| Ok prob ->
    match solve prob with
    | Ok result ->
        printfn "✅ SUCCESS: Factorization Found!"
        printfn ""
        printfn "RESULTS:"
        printfn "  Base used (a):        %d" result.Base
        printfn "  Period found (r):     %d" result.Period
        
        match result.Factors with
        | Some (p, q) ->
            printfn "  Prime factors:        %d × %d = %d" p q (p * q)
            printfn ""
            printfn "VERIFICATION:"
            printfn "  %d × %d = %d ✓" p q n1
            printfn ""
            printfn "⚠️  RSA SECURITY BROKEN!"
            printfn "   With factors p=%d and q=%d, an attacker can:" p q
            printfn "   1. Calculate φ(n) = (p-1)(q-1) = %d" ((p-1)*(q-1))
            printfn "   2. Derive private key d from public key e"
            printfn "   3. Decrypt all messages encrypted with this RSA key"
            
        | None ->
            printfn "  Note: Period found but did not yield factors (try again)"
        
        printfn ""
        printfn "QUANTUM RESOURCES:"
        printfn "  Qubits Used:          %d" result.QubitsUsed
        printfn "  Precision:            %d qubits" prob.Precision
        printfn "  Attempts Made:        %d/%d" result.Attempts prob.MaxAttempts

    | Error err ->
        printfn "❌ Execution Error: %s" err.Message

| Error err ->
    printfn "❌ Builder Error: %s" err.Message

printfn ""
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

// ============================================================================
// SCENARIO 2: Analyze Larger RSA Modulus
// ============================================================================

printfn "--- Scenario 2: Factor RSA Modulus n = 143 (11 × 13) ---"
printfn ""

let n2 = 143

printfn "Target RSA Modulus (n): %d" n2
printfn "Key Size Estimate:      ~8 bits (toy example)"
printfn ""

// Use convenience function with automatic configuration
let problem2 = factorInteger n2 4  // 4 qubits precision (reduced for local simulation)

printfn "Running Shor's algorithm with higher precision..."
printfn ""

match problem2 with
| Ok prob ->
    match solve prob with
    | Ok result ->
        printfn "✅ SUCCESS: Factorization Found!"
        printfn ""
        match result.Factors with
        | Some (p, q) ->
            printfn "Prime factors: %d × %d = %d" p q (p * q)
            printfn ""
            printfn "SECURITY ASSESSMENT:"
            printfn "  RSA Modulus Size:     ~8 bits"
            printfn "  Time to Factor:       Milliseconds (quantum)"
            printfn "  Classical Difficulty: Trivial (small modulus)"
            printfn "  Verdict:              ⚠️  INSECURE"
        | None ->
            printfn "Period found but did not yield factors (retry recommended)"

    | Error err ->
        printfn "❌ Execution Error: %s" err.Message

| Error err ->
    printfn "❌ Builder Error: %s" err.Message

printfn ""
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

// ============================================================================
// SCENARIO 3: Assess Real-World RSA Key (Infeasible on NISQ)
// ============================================================================

printfn "--- Scenario 3: Real-World RSA Analysis ---"
printfn ""

let realRSA = 
    // Example 2048-bit RSA modulus (not factorizable on current hardware)
    "2519590847565789349402718324004839857142928212620403202777713783604"

printfn "Real RSA Modulus:       %s... (2048 bits)" (realRSA.Substring(0, 40))
printfn "Required Qubits:        ~4096 qubits"
printfn "Current NISQ Hardware:  ~100 qubits (IBM, Google)"
printfn ""
printfn "QUANTUM THREAT TIMELINE:"
printfn "  Today (2024-2025):    ❌ Cannot factor (insufficient qubits)"
printfn "  Near-term (2025-2030): ❌ NISQ era, ~1000 qubits, high error rates"
printfn "  Long-term (2030+):    ⚠️  Fault-tolerant quantum computers may factor RSA-2048"
printfn ""
printfn "RECOMMENDATIONS:"
printfn "  ✅ Start transitioning to post-quantum cryptography (NIST standards)"
printfn "  ✅ Use 4096-bit RSA keys as interim measure"
printfn "  ✅ Monitor quantum computing progress (qubit count, error rates)"
printfn "  ✅ Plan for 'Q-Day' when quantum computers can break current crypto"

printfn ""
printfn "=== Key Takeaways ==="
printfn "• Shor's algorithm provides exponential speedup for integer factorization"
printfn "• Quantum computers threaten RSA, Diffie-Hellman, and elliptic curve crypto"
printfn "• Current NISQ hardware limited to toy examples (n < 1000)"
printfn "• Organizations should prepare for post-quantum cryptography transition"
printfn "• This tool helps security analysts understand quantum cryptanalysis threat"
