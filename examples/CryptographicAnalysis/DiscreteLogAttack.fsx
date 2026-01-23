// Discrete Logarithm Attack Example - Quantum Threat to Diffie-Hellman & ElGamal
// Demonstrates how quantum period finding breaks the Discrete Logarithm Problem (DLP)

(*
===============================================================================
 Background Theory
===============================================================================

The Discrete Logarithm Problem (DLP) is the mathematical foundation for
Diffie-Hellman key exchange, ElGamal encryption, DSA signatures, and elliptic
curve cryptography (ECDH, ECDSA). Given a prime p, generator g, and public key
y = g^x mod p, the DLP asks: find the secret exponent x. Classically, the best
algorithms (index calculus, baby-step giant-step) require O(exp(√(n log n)))
time for n-bit primes—considered computationally infeasible for 2048+ bit keys.

Shor's algorithm (1994) solves DLP in polynomial time O(n³) using quantum
period finding. The key insight: finding x where g^x ≡ y (mod p) reduces to
finding the period of f(a,b) = g^a · y^b mod p. Using quantum phase estimation
on a unitary that computes this function in superposition, we can extract the
period structure and recover x. This completely breaks Diffie-Hellman, ElGamal,
and (with curve-specific modifications) elliptic curve cryptography.

Key Equations:
  - DLP definition: Given g, y, p, find x such that g^x ≡ y (mod p)
  - Order of g: r = ord(g) where g^r ≡ 1 (mod p); for prime p, r divides (p-1)
  - Quantum reduction: Period of f(a) = g^a mod p gives order r
  - DLP solution: If g^a ≡ y (mod p), then x ≡ a (mod r)
  - Combined function: f(a,b) = g^a · y^(-b) mod p has period (r, x) relationship
  - Phase estimation: QPE extracts s/r from eigenvalue e^(2πi·s/r)

Quantum Advantage:
  Shor's algorithm provides exponential speedup: O(n³) quantum vs O(exp(√n))
  classical for n-bit DLP. This threatens ALL classical public-key systems:
  
  | System           | Classical Security | Quantum Attack Time |
  |------------------|-------------------|---------------------|
  | DH-2048          | ~2^112 operations | O(2048³) = feasible |
  | ECDH-256         | ~2^128 operations | O(256³) = feasible  |
  | RSA-2048         | ~2^112 operations | O(2048³) = feasible |
  
  The quantum threat to DLP is equally severe as for RSA factoring. Both use
  the same core technique: quantum period finding via phase estimation.

References:
  [1] Shor, "Polynomial-Time Algorithms for Prime Factorization and Discrete
      Logarithms on a Quantum Computer", SIAM J. Comput. 26(5), 1484-1509 (1997).
      https://doi.org/10.1137/S0097539795293172
  [2] Proos & Zalka, "Shor's discrete logarithm quantum algorithm for elliptic
      curves", QIC 3(4), 317-344 (2003). https://arxiv.org/abs/quant-ph/0301141
  [3] Roetteler et al., "Quantum Resource Estimates for Computing Elliptic Curve
      Discrete Logarithms", ASIACRYPT 2017. https://doi.org/10.1007/978-3-319-70697-9_9
  [4] Wikipedia: Discrete_logarithm
      https://en.wikipedia.org/wiki/Discrete_logarithm
*)

// Reference local build (use this for development/testing)
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumPeriodFinder

// ============================================================================
// Modular Arithmetic Helpers (Pure Functional)
// ============================================================================

/// Modular exponentiation using repeated squaring: base^exp mod m
/// Pure functional implementation with tail recursion
let modPow (baseVal: int) (exp: int) (modulus: int) : int =
    let rec loop acc b e =
        if e = 0 then acc
        elif e % 2 = 1 then loop ((acc * b) % modulus) ((b * b) % modulus) (e / 2)
        else loop acc ((b * b) % modulus) (e / 2)
    loop 1 (baseVal % modulus) exp

/// Solve discrete logarithm by brute force: find x where g^x ≡ y (mod p)
/// Returns None if no solution exists in range [1, p-2]
let solveDiscreteLog (g: int) (y: int) (p: int) : int option =
    [1 .. p - 2]
    |> List.tryFind (fun x -> modPow g x p = y)

// ============================================================================
// Domain Types
// ============================================================================

/// Diffie-Hellman public parameters
type DHParameters = {
    Prime: int
    Generator: int
}

/// A party's key pair in Diffie-Hellman
type DHKeyPair = {
    PrivateKey: int
    PublicKey: int
}

/// Result of a DLP attack
type DLPAttackResult = 
    | Success of recoveredKey: int * sharedSecret: int
    | Failure of reason: string

// ============================================================================
// Diffie-Hellman Functions (Pure)
// ============================================================================

/// Generate public key from private key: g^private mod p
let generatePublicKey (dhParams: DHParameters) (privateKey: int) : int =
    modPow dhParams.Generator privateKey dhParams.Prime

/// Compute shared secret: otherPublic^myPrivate mod p
let computeSharedSecret (dhParams: DHParameters) (myPrivate: int) (otherPublic: int) : int =
    modPow otherPublic myPrivate dhParams.Prime

/// Create a key pair from private key
let createKeyPair (dhParams: DHParameters) (privateKey: int) : DHKeyPair =
    { PrivateKey = privateKey
      PublicKey = generatePublicKey dhParams privateKey }

/// Eve's quantum attack: solve DLP to recover private key, then compute shared secret
let quantumAttack (dhParams: DHParameters) (targetPublic: int) (otherPublic: int) : DLPAttackResult =
    match solveDiscreteLog dhParams.Generator targetPublic dhParams.Prime with
    | Some recoveredPrivate ->
        let sharedSecret = computeSharedSecret dhParams recoveredPrivate otherPublic
        Success (recoveredPrivate, sharedSecret)
    | None ->
        Failure "Could not solve discrete logarithm"

// ============================================================================
// Main Example
// ============================================================================

printfn "=== Discrete Logarithm Attack with Quantum Computing ==="
printfn ""
printfn "BUSINESS SCENARIO:"
printfn "A security team needs to assess vulnerability of Diffie-Hellman key exchange"
printfn "and ElGamal encryption to quantum attacks. This demonstrates how quantum"
printfn "period finding breaks the Discrete Logarithm Problem (DLP)."
printfn ""

// ============================================================================
// BACKGROUND: The Discrete Logarithm Problem
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn " THE DISCRETE LOGARITHM PROBLEM (DLP)"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""
printfn "Given:"
printfn "  - Prime modulus p (e.g., 23)"
printfn "  - Generator g of multiplicative group Z*_p (e.g., 5)"
printfn "  - Public key y = g^x mod p (e.g., 8)"
printfn ""
printfn "Find: Secret exponent x"
printfn ""
printfn "This is HARD classically (exponential time) but EASY quantumly (polynomial)!"
printfn ""

// ============================================================================
// SCENARIO 1: Small DLP Example (Educational)
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn " SCENARIO 1: Solve Small Discrete Logarithm"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

// Small DLP example: Find x where 5^x ≡ 8 (mod 23)
let p, g, y = 23, 5, 8

printfn "DLP Instance:"
printfn "  Prime p:      %d" p
printfn "  Generator g:  %d" g
printfn "  Public key y: %d" y
printfn "  Find x where: %d^x ≡ %d (mod %d)" g y p
printfn ""

match solveDiscreteLog g y p with
| Some x ->
    printfn "Classical brute-force solution: x = %d" x
    printfn "Verification: %d^%d mod %d = %d ✓" g x p (modPow g x p)
| None ->
    printfn "No solution found (y may not be in group generated by g)"

printfn ""

// ============================================================================
// QUANTUM APPROACH: DLP via Order Finding
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn " QUANTUM ATTACK: DLP via Order Finding"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "Shor's DLP Algorithm reduces discrete log to order finding:"
printfn ""
printfn "1. FIND ORDER: Compute r = ord(g) where g^r ≡ 1 (mod p)"
printfn "   -> Use quantum period finding (same as RSA factoring!)"
printfn ""
printfn "2. FIND x: Since g^x ≡ y (mod p), use quantum search to find x"
printfn "   -> x is uniquely determined modulo r"
printfn ""

// For prime p, order divides φ(p) = p-1
let groupOrder = p - 1
printfn "For prime p, the group order is p-1 = %d" groupOrder
printfn ""

printfn "Mathematical Connection:"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""
printfn "Both RSA factoring and DLP use the SAME quantum subroutine:"
printfn ""
printfn "  ┌─────────────────────────────────────────────────────────────┐"
printfn "  │  QUANTUM PHASE ESTIMATION on Modular Exponentiation       │"
printfn "  │                                                            │"
printfn "  │  U_a |y⟩ = |a·y mod N⟩   ->   eigenvalues e^(2πi·s/r)     │"
printfn "  │                                                            │"
printfn "  │  QPE extracts s/r, continued fractions give period r      │"
printfn "  └─────────────────────────────────────────────────────────────┘"
printfn ""
printfn "  RSA Attack:  r = period of a^k mod N  ->  gcd gives factors"
printfn "  DLP Attack:  r = order of g mod p    ->  x = discrete log"
printfn ""

// ============================================================================
// SCENARIO 2: Simulated Diffie-Hellman Key Exchange Attack
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn " SCENARIO 2: Attack on Diffie-Hellman Key Exchange"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

// Set up Diffie-Hellman parameters
let dhParams = { Prime = 23; Generator = 5 }

// Alice and Bob create their key pairs
let alice = createKeyPair dhParams 6   // Alice's private key = 6
let bob = createKeyPair dhParams 15    // Bob's private key = 15

// Compute the legitimate shared secret
let legitimateSecret = computeSharedSecret dhParams alice.PrivateKey bob.PublicKey

printfn "Diffie-Hellman Key Exchange (intercepted by Eve):"
printfn ""
printfn "  Public Parameters:"
printfn "    Prime p:      %d" dhParams.Prime
printfn "    Generator g:  %d" dhParams.Generator
printfn ""
printfn "  Alice -> Bob:   A = g^a mod p = %d" alice.PublicKey
printfn "  Bob -> Alice:   B = g^b mod p = %d" bob.PublicKey
printfn ""
printfn "  Eve intercepts: p=%d, g=%d, A=%d, B=%d" 
    dhParams.Prime dhParams.Generator alice.PublicKey bob.PublicKey
printfn ""

// Eve's quantum attack
printfn "EVE'S QUANTUM ATTACK:"
printfn ""
printfn "  Step 1: Solve DLP to find Alice's private key a"
printfn "          Find a where %d^a ≡ %d (mod %d)" 
    dhParams.Generator alice.PublicKey dhParams.Prime

match quantumAttack dhParams alice.PublicKey bob.PublicKey with
| Success (recoveredKey, eveSecret) ->
    printfn "          -> Found: a = %d" recoveredKey
    printfn ""
    printfn "  Step 2: Compute shared secret S = B^a mod p"
    printfn "          -> S = %d^%d mod %d = %d" 
        bob.PublicKey recoveredKey dhParams.Prime eveSecret
    printfn ""
    
    if eveSecret = legitimateSecret then
        printfn "  ⚠️  ATTACK SUCCESSFUL!"
        printfn "      Eve recovered shared secret: %d" eveSecret
        printfn "      (Actual shared secret:       %d)" legitimateSecret
    else
        printfn "  Attack verification failed (unexpected)"
        
| Failure reason ->
    printfn "          -> Attack failed: %s" reason

printfn ""

// ============================================================================
// SCENARIO 3: Real-World Threat Assessment
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn " SCENARIO 3: Real-World Quantum Threat Assessment"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

// Vulnerability table as structured data
let vulnerableSystems = [
    ("DH-2048",       "2048-bit prime", "~4000 qubits, 10^9 gates")
    ("DH-3072",       "3072-bit prime", "~6000 qubits, 10^10 gates")
    ("ECDH-256",      "256-bit curve",  "~2330 qubits, 10^8 gates")
    ("ECDH-384",      "384-bit curve",  "~3500 qubits, 10^9 gates")
    ("DSA-2048",      "2048-bit mod",   "~4000 qubits, 10^9 gates")
    ("ECDSA-256",     "256-bit curve",  "~2330 qubits, 10^8 gates")
]

printfn "CRYPTOGRAPHIC SYSTEMS VULNERABLE TO QUANTUM DLP ATTACK:"
printfn ""
printfn "┌──────────────────┬────────────────────┬─────────────────────────┐"
printfn "│ System           │ Key Size           │ Quantum Resource Est.   │"
printfn "├──────────────────┼────────────────────┼─────────────────────────┤"

vulnerableSystems
|> List.iter (fun (system, keySize, resources) ->
    printfn "│ %-16s │ %-18s │ %-23s │" system keySize resources)

printfn "└──────────────────┴────────────────────┴─────────────────────────┘"
printfn ""
printfn "Note: Estimates from Roetteler et al. (2017) and Gidney & Ekera (2021)"
printfn ""

// Current hardware status as structured data
let hardwareStatus = [
    ("IBM Condor",      "~1000 qubits", "noisy")
    ("Google Sycamore", "~100 qubits",  "high fidelity")
    ("IonQ Forte",      "~36 qubits",   "high fidelity")
]

printfn "CURRENT QUANTUM HARDWARE STATUS (2024-2025):"

hardwareStatus
|> List.iter (fun (name, qubits, quality) ->
    printfn "  - %-18s %s (%s)" name qubits quality)

printfn "  - Error rates:          ~0.1-1%% per gate"
printfn "  - Logical qubits:       ~0 (fault tolerance not achieved)"
printfn ""

// Timeline as structured data
let timeline = [
    ("Today (2024-2025)", [
        (false, "Cannot break real DH/ECDH (insufficient qubits, high errors)")
        (true,  "Demonstrations on toy examples (DH with 5-bit primes)")
    ])
    ("Near-term (2025-2030)", [
        (false, "NISQ devices still far from cryptographic relevance")
        (false, "\"Harvest now, decrypt later\" attacks may begin")
        (true,  "Post-quantum migration should be underway")
    ])
    ("Long-term (2030+)", [
        (false, "Fault-tolerant quantum computers may emerge")
        (false, "All DH, ECDH, DSA, ECDSA potentially broken")
        (true,  "Post-quantum cryptography should be deployed")
    ])
]

printfn "QUANTUM THREAT TIMELINE:"
printfn ""

timeline
|> List.iter (fun (period, items) ->
    printfn "  %s:" period
    items |> List.iter (fun (isPositive, text) ->
        let icon = if isPositive then "✓ " else "⚠️ "
        printfn "    %s %s" icon text)
    printfn "")

let recommendations = [
    "INVENTORY: Identify all systems using DH, ECDH, DSA, ECDSA"
    "PLAN: Develop post-quantum migration roadmap"
    "ADOPT: NIST post-quantum standards (ML-KEM, ML-DSA, SLH-DSA)"
    "HYBRID: Use hybrid classical+PQ schemes during transition"
    "MONITOR: Track quantum computing progress"
]

printfn "RECOMMENDATIONS FOR SECURITY TEAMS:"
printfn ""

recommendations
|> List.iteri (fun i rec' -> printfn "  %d. %s" (i + 1) rec')

printfn ""

// ============================================================================
// Demonstration: Using Our Library's Period Finder
// ============================================================================

printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn " DEMONSTRATION: Quantum Period Finding Infrastructure"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

printfn "Our library's QuantumPeriodFinder demonstrates the core QPE technique"
printfn "used in both RSA factoring AND discrete logarithm attacks."
printfn ""

// Demonstrate period finding using computation expression
let demoProblem = periodFinder {
    number 15
    precision 4
    maxAttempts 5
}

printfn "Running period finder (demonstrates QPE infrastructure)..."
printfn ""

// Pattern match on nested Results
demoProblem
|> Result.bind solve
|> function
    | Ok result ->
        printfn "Period Finding Result:"
        printfn "  Input N:        %d" result.Number
        printfn "  Base a:         %d" result.Base
        printfn "  Period r:       %d (where a^r ≡ 1 mod N)" result.Period
        printfn "  Phase estimate: %.4f" result.PhaseEstimate
        printfn ""
        printfn "This same QPE technique extracts the order of g mod p for DLP!"
    | Error err ->
        printfn "Error: %A" err

printfn ""
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn " KEY TAKEAWAYS"
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

let takeaways = [
    "DLP and RSA factoring use the SAME quantum attack technique"
    "Quantum Phase Estimation extracts periods/orders in polynomial time"
    "ALL classical public-key crypto (DH, ECDH, RSA, DSA) is vulnerable"
    "Current quantum hardware cannot yet break real cryptographic keys"
    "Organizations should begin post-quantum migration planning NOW"
    "NIST post-quantum standards (2024) provide migration targets"
]

takeaways |> List.iter (printfn "- %s")
printfn ""
