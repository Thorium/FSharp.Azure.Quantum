// Grover's Algorithm Threat to Symmetric Cryptography (AES)
// Demonstrates quantum speedup for brute-force key search attacks

(*
===============================================================================
 Background Theory
===============================================================================

Grover's algorithm (1996) provides quadratic speedup for unstructured search
problems: finding a marked item in an unsorted database of N items requires
O(√N) quantum queries versus O(N) classical queries. For symmetric cryptography
like AES, this means a brute-force key search on an n-bit key requires only
O(2^(n/2)) quantum operations instead of O(2^n) classical operations.

The algorithm works by amplitude amplification: starting from a uniform
superposition |s⟩ = (1/√N)Σ|x⟩, repeatedly apply the Grover iterate G = -H⊗ⁿ
U₀ H⊗ⁿ Uω, where Uω marks the target state and U₀ reflects about |s⟩. After
~(π/4)√N iterations, measuring yields the marked state with high probability.
For cryptographic key search, the oracle Uω checks if decrypting known
ciphertext with key |k⟩ produces expected plaintext.

Key Equations:
  - Classical brute force: O(2^n) operations for n-bit key
  - Grover search: O(2^(n/2)) operations = O(√(2^n))
  - Optimal iterations: k ≈ (π/4)√N where N = 2^n
  - Success probability: sin²((2k+1)θ) where sin²(θ) = 1/N
  - AES-128: 2^128 classical → 2^64 quantum (still infeasible)
  - AES-256: 2^256 classical → 2^128 quantum (quantum-safe margin)

Quantum Advantage:
  Grover provides QUADRATIC (not exponential) speedup. This is fundamentally
  different from Shor's exponential speedup for factoring/DLP:
  
  | Algorithm | Classical  | Quantum    | Speedup     |
  |-----------|------------|------------|-------------|
  | Shor      | O(e^n)     | O(n³)      | Exponential |
  | Grover    | O(2^n)     | O(2^(n/2)) | Quadratic   |
  
  For AES-128 (n=128): 2^64 quantum ops is ~10^19 operations—still infeasible
  with any foreseeable quantum computer. AES-256 provides 2^128 quantum
  security, equivalent to classical AES-128 security. The standard mitigation
  is to DOUBLE the key size: AES-128 → AES-256 for post-quantum security.

References:
  [1] Grover, "A fast quantum mechanical algorithm for database search",
      STOC 1996, pp. 212-219. https://doi.org/10.1145/237814.237866
  [2] Bennett et al., "Strengths and Weaknesses of Quantum Computing",
      SIAM J. Comput. 26(5), 1510-1523 (1997). https://arxiv.org/abs/quant-ph/9701001
  [3] Grassl et al., "Applying Grover's algorithm to AES: quantum resource
      estimates", PQCrypto 2016. https://doi.org/10.1007/978-3-319-29360-8_3
  [4] Wikipedia: Grover's_algorithm
      https://en.wikipedia.org/wiki/Grover%27s_algorithm
*)

// Reference local build (use this for development/testing)
#I "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0"
#r "FSharp.Azure.Quantum.dll"

// For published package, use instead:
// #r "nuget: FSharp.Azure.Quantum"

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.GroverSearch.Oracle

// ============================================================================
// Domain Types for Cryptographic Analysis
// ============================================================================

/// Symmetric cipher configuration
type SymmetricCipher = {
    Name: string
    KeyBits: int
    BlockBits: int
}

/// Security analysis result
type SecurityAnalysis = {
    Cipher: SymmetricCipher
    ClassicalSecurity: float      // log2 of classical operations
    QuantumSecurity: float        // log2 of quantum operations (Grover)
    QuantumSafe: bool             // True if quantum security >= 128 bits
    Recommendation: string
}

// ============================================================================
// Security Analysis Functions (Pure)
// ============================================================================

/// Calculate quantum security level using Grover's speedup
let quantumSecurityBits (classicalBits: int) : float =
    float classicalBits / 2.0

/// Analyze a symmetric cipher's quantum resistance
let analyzeSymmetricCipher (cipher: SymmetricCipher) : SecurityAnalysis =
    let classicalSecurity = float cipher.KeyBits
    let quantumSecurity = quantumSecurityBits cipher.KeyBits
    let isQuantumSafe = quantumSecurity >= 128.0
    
    let recommendation =
        if isQuantumSafe then
            sprintf "✓ %s is quantum-safe (%.0f-bit quantum security)" cipher.Name quantumSecurity
        elif quantumSecurity >= 64.0 then
            sprintf "⚠ %s has reduced security (%.0f-bit quantum). Consider upgrading to %d-bit keys." 
                cipher.Name quantumSecurity (cipher.KeyBits * 2)
        else
            sprintf "✗ %s is NOT quantum-safe (%.0f-bit quantum). UPGRADE IMMEDIATELY." 
                cipher.Name quantumSecurity
    
    { Cipher = cipher
      ClassicalSecurity = classicalSecurity
      QuantumSecurity = quantumSecurity
      QuantumSafe = isQuantumSafe
      Recommendation = recommendation }

/// Estimate Grover iterations needed
let groverIterations (keyBits: int) : float =
    (Math.PI / 4.0) * Math.Sqrt(Math.Pow(2.0, float keyBits))

/// Estimate quantum resources for Grover attack on AES
/// Based on Grassl et al. (2016) estimates
let estimateAESGroverResources (keyBits: int) : string =
    let qubits = 
        match keyBits with
        | 128 -> 2953   // Grassl et al. estimate
        | 192 -> 4449
        | 256 -> 6681
        | _ -> keyBits * 20  // Rough estimate
    
    let tGates =
        match keyBits with
        | 128 -> "2^86"
        | 192 -> "2^118"
        | 256 -> "2^151"
        | _ -> sprintf "2^%d" (keyBits / 2 + 20)
    
    sprintf "%d qubits, %s T-gates" qubits tGates

// ============================================================================
// Main Example
// ============================================================================

printfn "=== Grover's Algorithm Threat to Symmetric Cryptography ==="
printfn ""
printfn "BUSINESS SCENARIO:"
printfn "A security architect needs to assess whether current symmetric encryption"
printfn "(AES-128, AES-256) will remain secure against quantum computers. This"
printfn "demonstrates Grover's quadratic speedup for brute-force key search."
printfn ""

// ============================================================================
// BACKGROUND: Grover's Algorithm
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn " GROVER'S ALGORITHM: Quantum Search Speedup"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""
printfn "Problem: Find a specific item in an unsorted database of N items"
printfn ""
printfn "Classical: Try each item → O(N) queries"
printfn "Quantum:   Grover search → O(√N) queries"
printfn ""
printfn "For cryptographic key search (N = 2^n possible keys):"
printfn "  Classical brute force: O(2^n) operations"
printfn "  Grover search:         O(2^(n/2)) operations"
printfn ""
printfn "This is QUADRATIC speedup, NOT exponential like Shor's algorithm!"
printfn ""

// ============================================================================
// SCENARIO 1: Compare Classical vs Quantum Key Search
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn " SCENARIO 1: Classical vs Quantum Brute Force"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

// Define common symmetric ciphers
let ciphers = [
    { Name = "DES";      KeyBits = 56;  BlockBits = 64 }
    { Name = "3DES";     KeyBits = 112; BlockBits = 64 }
    { Name = "AES-128";  KeyBits = 128; BlockBits = 128 }
    { Name = "AES-192";  KeyBits = 192; BlockBits = 128 }
    { Name = "AES-256";  KeyBits = 256; BlockBits = 128 }
    { Name = "ChaCha20"; KeyBits = 256; BlockBits = 512 }
]

printfn "Security Level Comparison:"
printfn ""
printfn "┌────────────┬──────────┬───────────────────┬───────────────────┐"
printfn "│ Cipher     │ Key Size │ Classical (bits)  │ Quantum (bits)    │"
printfn "├────────────┼──────────┼───────────────────┼───────────────────┤"

ciphers
|> List.map analyzeSymmetricCipher
|> List.iter (fun analysis ->
    let qStatus = if analysis.QuantumSafe then "✓" else "⚠"
    printfn "│ %-10s │ %3d-bit  │ %6.0f            │ %6.0f %s          │"
        analysis.Cipher.Name
        analysis.Cipher.KeyBits
        analysis.ClassicalSecurity
        analysis.QuantumSecurity
        qStatus)

printfn "└────────────┴──────────┴───────────────────┴───────────────────┘"
printfn ""
printfn "128-bit quantum security is considered the post-quantum minimum."
printfn ""

// ============================================================================
// SCENARIO 2: Detailed AES Analysis
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn " SCENARIO 2: AES Quantum Security Analysis"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

let aesVariants = [
    { Name = "AES-128"; KeyBits = 128; BlockBits = 128 }
    { Name = "AES-192"; KeyBits = 192; BlockBits = 128 }
    { Name = "AES-256"; KeyBits = 256; BlockBits = 128 }
]

aesVariants
|> List.iter (fun cipher ->
    let analysis = analyzeSymmetricCipher cipher
    let resources = estimateAESGroverResources cipher.KeyBits
    let iterations = groverIterations cipher.KeyBits
    
    printfn "%s:" cipher.Name
    printfn "  Key space:           2^%d = %.2e possible keys" cipher.KeyBits (Math.Pow(2.0, float cipher.KeyBits))
    printfn "  Classical security:  %.0f bits (brute force: 2^%.0f ops)" analysis.ClassicalSecurity analysis.ClassicalSecurity
    printfn "  Quantum security:    %.0f bits (Grover: 2^%.0f ops)" analysis.QuantumSecurity analysis.QuantumSecurity
    printfn "  Grover iterations:   ~2^%.0f" (Math.Log(iterations, 2.0))
    printfn "  Quantum resources:   %s" resources
    printfn "  Assessment:          %s" analysis.Recommendation
    printfn "")

// ============================================================================
// SCENARIO 3: Why Grover Doesn't Break AES
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn " SCENARIO 3: Why Grover Doesn't Break AES (Yet)"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "Even with Grover's speedup, breaking AES-128 requires:"
printfn ""
printfn "  Quantum operations:  2^64 ≈ 1.8 × 10^19"
printfn "  Qubits needed:       ~3000 (Grassl et al. 2016)"
printfn "  T-gates:             ~2^86"
printfn "  Circuit depth:       ~2^64 sequential operations"
printfn ""

// Calculate time estimates
let opsPerSecond = 1e9  // Optimistic: 1 billion quantum ops/sec
let aes128QuantumOps = Math.Pow(2.0, 64.0)
let timeSeconds = aes128QuantumOps / opsPerSecond
let timeYears = timeSeconds / (365.25 * 24.0 * 3600.0)

printfn "Time estimate (optimistic 10^9 ops/sec):"
printfn "  Operations:  %.2e" aes128QuantumOps
printfn "  Time:        %.2e seconds" timeSeconds
printfn "  Time:        %.2e years" timeYears
printfn "  Time:        ~%.0f billion years" (timeYears / 1e9)
printfn ""

printfn "KEY INSIGHT: Even with Grover, AES-128 requires billions of years!"
printfn "             The circuit depth alone makes it impractical."
printfn ""

// ============================================================================
// SCENARIO 4: Practical Recommendations
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn " SCENARIO 4: Post-Quantum Symmetric Crypto Recommendations"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

let recommendations = [
    ("AES-128 users", "Upgrade to AES-256 for post-quantum security margin")
    ("AES-256 users", "Already quantum-safe (128-bit quantum security)")
    ("New systems",   "Use AES-256 or ChaCha20-Poly1305 by default")
    ("Hash functions","Use SHA-3 or SHA-256 with 256-bit output")
    ("MACs",          "Use HMAC-SHA-256 or Poly1305")
]

printfn "Recommendations by use case:"
printfn ""

recommendations
|> List.iter (fun (useCase, rec') ->
    printfn "  %-16s → %s" useCase rec')

printfn ""

// Comparison with asymmetric crypto
printfn "COMPARISON: Symmetric vs Asymmetric Quantum Threats"
printfn ""
printfn "┌─────────────────┬─────────────────┬─────────────────┬──────────────┐"
printfn "│ Crypto Type     │ Algorithm       │ Quantum Attack  │ Mitigation   │"
printfn "├─────────────────┼─────────────────┼─────────────────┼──────────────┤"
printfn "│ Symmetric       │ AES-128         │ Grover (2^64)   │ Use AES-256  │"
printfn "│ Symmetric       │ AES-256         │ Grover (2^128)  │ Already safe │"
printfn "│ Asymmetric      │ RSA-2048        │ Shor (poly)     │ Use PQ crypto│"
printfn "│ Asymmetric      │ ECDH-256        │ Shor (poly)     │ Use PQ crypto│"
printfn "│ Hash            │ SHA-256         │ Grover (2^128)  │ Already safe │"
printfn "└─────────────────┴─────────────────┴─────────────────┴──────────────┘"
printfn ""
printfn "Key insight: Symmetric crypto needs only key doubling; asymmetric"
printfn "             crypto needs complete algorithm replacement!"
printfn ""

// ============================================================================
// Demonstration: Grover Search Using Our Library
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn " DEMONSTRATION: Grover Search Infrastructure"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "Our library's GroverSearch module demonstrates Grover's algorithm."
printfn "For a real AES attack, the oracle would check: Decrypt(key, ciphertext) = plaintext"
printfn ""

// Small demonstration: search for a marked item in 4-item database (2 qubits)
// This shows the Grover infrastructure without requiring actual AES oracle

printfn "Example: Search 4-item database (2 qubits)"
printfn "  Database size: N = 4"
printfn "  Classical:     O(4) = 4 queries average"
printfn "  Grover:        O(√4) = 2 queries"
printfn "  Optimal iterations: π/4 × √4 ≈ 1.57 → 1 iteration"
printfn ""

// Create oracle for target value 3 (binary: 11)
// In a real AES attack, this oracle would verify: Decrypt(key, ciphertext) = plaintext
let targetValue = 3  // |11⟩
let numQubits = 2    // 2^2 = 4 item search space

printfn "Creating oracle for target value %d in %d-qubit space..." targetValue numQubits

match Oracle.forValue targetValue numQubits with
| Error err ->
    printfn "❌ Failed to create oracle: %A" err
| Ok oracle ->
    printfn "✅ Oracle created successfully"
    printfn ""
    
    // Verify which values are marked by the oracle
    printfn "Oracle verification (classical check):"
    for i in 0 .. (1 <<< numQubits) - 1 do
        let isTarget = Oracle.isSolution oracle.Spec i
        let mark = if isTarget then "✅ TARGET" else "  "
        printfn "  %s |%d⟩ = |%s⟩" mark i (Convert.ToString(i, 2).PadLeft(numQubits, '0'))
    
    printfn ""
    printfn "Running Grover search..."
    
    // Create local backend and search configuration
    let backend = LocalBackend() :> IQuantumBackend
    let config = { Grover.defaultConfig with Iterations = Some 1 }  // Optimal for N=4
    
    match Grover.search oracle backend config with
    | Error err ->
        printfn "❌ Search failed: %A" err
    | Ok result ->
        printfn ""
        printfn "Grover Search Result:"
        printfn "  Search space:    2^%d = %d items" numQubits (1 <<< numQubits)
        printfn "  Target:          |%d⟩" targetValue
        
        if result.Solutions.IsEmpty then
            printfn "  Found:           (no solution)"
        else
            for solution in result.Solutions do
                printfn "  Found:           |%d⟩ = |%s⟩" solution (Convert.ToString(solution, 2).PadLeft(numQubits, '0'))
        
        printfn "  Iterations:      %d" result.Iterations
        printfn "  Success prob:    %.1f%%" (result.SuccessProbability * 100.0)
        printfn ""
        printfn "This same technique would search the AES key space—just with"
        printfn "128+ qubits and an oracle that checks decryption!"

printfn ""

// ============================================================================
// Key Takeaways
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn " KEY TAKEAWAYS"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

let takeaways = [
    "Grover provides QUADRATIC speedup (√N), not exponential like Shor"
    "AES-128: 128-bit classical → 64-bit quantum security"
    "AES-256: 256-bit classical → 128-bit quantum security (safe!)"
    "Even with Grover, AES-128 attack needs billions of years"
    "Simple mitigation: DOUBLE the key size (AES-128 → AES-256)"
    "Symmetric crypto is FAR easier to make quantum-safe than asymmetric"
    "NIST recommendation: Use 256-bit symmetric keys for post-quantum"
]

takeaways |> List.iter (printfn "- %s")
printfn ""
