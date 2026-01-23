// RSA Factorization Example - Shor's Algorithm for Breaking RSA Keys
// Demonstrates quantum period finding to factor composite numbers

(*
===============================================================================
 Background Theory
===============================================================================

Shor's algorithm (1994) is the most famous quantum algorithm, demonstrating
exponential speedup for integer factorization—the problem underlying RSA
encryption security. Given a composite number N = p × q, classical algorithms
require O(exp(n^(1/3))) time (number field sieve) where n = log N, while Shor's
algorithm runs in O(n³) time. This means a sufficiently large quantum computer
could break 2048-bit RSA in hours instead of billions of years, fundamentally
threatening current public-key cryptography.

The algorithm reduces factoring to period finding: for random a coprime to N,
find the period r of f(x) = aˣ mod N. If r is even and a^(r/2) ≢ ±1 (mod N),
then gcd(a^(r/2) ± 1, N) yields a factor. Quantum Phase Estimation finds r by
extracting the eigenvalue e^(2πi·s/r) from the modular exponentiation operator
U_a|y⟩ = |ay mod N⟩. The period r appears in the phase, extracted via QFT and
continued fractions. Success probability is ≥ 1/poly(log N) per attempt.

Key Equations:
  - Factoring reduction: N = p × q → find period r of aˣ mod N
  - Modular exponentiation: U_a|y⟩ = |ay mod N⟩ has eigenvalues e^(2πi·s/r)
  - Period extraction: QPE on U_a gives phase s/r; continued fractions yield r
  - Factorization: gcd(a^(r/2) + 1, N) and gcd(a^(r/2) - 1, N) give p, q
  - Resource estimate: ~4n qubits and ~O(n³) gates for n-bit integer N
  - RSA-2048: ~4000 logical qubits, ~10⁹ T-gates (years away with error correction)

Quantum Advantage:
  Shor's algorithm provides exponential speedup: O(n³) quantum vs O(exp(n^(1/3)))
  classical. This threatens RSA, Diffie-Hellman, and elliptic curve cryptography.
  While current quantum computers (~1000 noisy qubits) cannot factor RSA-2048,
  the threat has driven "post-quantum cryptography" standardization (NIST, 2024).
  Shor's algorithm also works for discrete logarithms, breaking most current
  public-key systems. Demonstrating Shor for small numbers (15, 21) validates
  the quantum computing stack, even if practical RSA-breaking is decades away.

References:
  [1] Shor, "Polynomial-Time Algorithms for Prime Factorization and Discrete
      Logarithms on a Quantum Computer", SIAM J. Comput. 26(5), 1484-1509 (1997).
      https://doi.org/10.1137/S0097539795293172
  [2] Vandersypen et al., "Experimental realization of Shor's quantum factoring
      algorithm using nuclear magnetic resonance", Nature 414, 883-887 (2001).
      https://doi.org/10.1038/414883a
  [3] Gidney & Ekerå, "How to factor 2048 bit RSA integers in 8 hours using 20
      million noisy qubits", Quantum 5, 433 (2021). https://doi.org/10.22331/q-2021-04-15-433
  [4] Wikipedia: Shor's_algorithm
      https://en.wikipedia.org/wiki/Shor%27s_algorithm
*)

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
