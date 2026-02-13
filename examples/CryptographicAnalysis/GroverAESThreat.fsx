// Grover's Algorithm Threat to Symmetric Cryptography (AES)
// Demonstrates quantum speedup for brute-force key search attacks
//
// Usage:
//   dotnet fsi GroverAESThreat.fsx
//   dotnet fsi GroverAESThreat.fsx -- --help
//   dotnet fsi GroverAESThreat.fsx -- --key-size 256 --target 5 --qubits 3
//   dotnet fsi GroverAESThreat.fsx -- --quiet --output results.json --csv results.csv

(*
===============================================================================
 Background Theory
===============================================================================

Grover's algorithm (1996) provides quadratic speedup for unstructured search
problems: finding a marked item in an unsorted database of N items requires
O(sqrt(N)) quantum queries versus O(N) classical queries. For symmetric
cryptography like AES, this means a brute-force key search on an n-bit key
requires only O(2^(n/2)) quantum operations instead of O(2^n) classical
operations.

The algorithm works by amplitude amplification: starting from a uniform
superposition |s> = (1/sqrt(N)) Sum|x>, repeatedly apply the Grover iterate
G = -H^n U0 H^n Uw, where Uw marks the target state and U0 reflects about
|s>. After ~(pi/4)*sqrt(N) iterations, measuring yields the marked state with
high probability. For cryptographic key search, the oracle Uw checks if
decrypting known ciphertext with key |k> produces expected plaintext.

Key Equations:
  - Classical brute force: O(2^n) operations for n-bit key
  - Grover search: O(2^(n/2)) operations = O(sqrt(2^n))
  - Optimal iterations: k ~ (pi/4)*sqrt(N) where N = 2^n
  - Success probability: sin^2((2k+1)*theta) where sin^2(theta) = 1/N
  - AES-128: 2^128 classical -> 2^64 quantum (still infeasible)
  - AES-256: 2^256 classical -> 2^128 quantum (quantum-safe margin)

Quantum Advantage:
  Grover provides QUADRATIC (not exponential) speedup. This is fundamentally
  different from Shor's exponential speedup for factoring/DLP:

  | Algorithm | Classical  | Quantum    | Speedup     |
  |-----------|------------|------------|-------------|
  | Shor      | O(e^n)     | O(n^3)     | Exponential |
  | Grover    | O(2^n)     | O(2^(n/2)) | Quadratic   |

  For AES-128 (n=128): 2^64 quantum ops is ~10^19 operations--still infeasible
  with any foreseeable quantum computer. AES-256 provides 2^128 quantum
  security, equivalent to classical AES-128 security. The standard mitigation
  is to DOUBLE the key size: AES-128 -> AES-256 for post-quantum security.

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

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.GroverSearch.Oracle
open FSharp.Azure.Quantum.Examples.Common

// ============================================================================
// CLI Setup
// ============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "GroverAESThreat.fsx" "Grover's algorithm threat analysis for symmetric cryptography (AES)." [
    { Name = "key-size"; Description = "AES key size to focus on (128, 192, 256)"; Default = Some "128" }
    { Name = "target"; Description = "Target value for Grover demo search"; Default = Some "3" }
    { Name = "qubits"; Description = "Number of qubits for Grover demo search"; Default = Some "2" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress informational output"; Default = None }
] args

let focusKeySize = Cli.getIntOr "key-size" 128 args
let targetValue = Cli.getIntOr "target" 3 args
let numQubits = Cli.getIntOr "qubits" 2 args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ============================================================================
// Result Collection
// ============================================================================

let results = System.Collections.Generic.List<Map<string, string>>()

let addResult scenario cipher keyBits classicalBits quantumBits quantumSafe recommendation =
    results.Add(
        [ "scenario", scenario
          "cipher", cipher
          "key_bits", string keyBits
          "classical_security_bits", string classicalBits
          "quantum_security_bits", string quantumBits
          "quantum_safe", string quantumSafe
          "recommendation", recommendation ]
        |> Map.ofList)

let addGroverResult target found iterations successProb qubitsUsed =
    results.Add(
        [ "scenario", "Grover Demo"
          "cipher", "N/A"
          "target_value", string target
          "found_value", found
          "iterations", string iterations
          "success_probability", sprintf "%.4f" successProb
          "qubits_used", string qubitsUsed
          "search_space", string (1 <<< qubitsUsed) ]
        |> Map.ofList)

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
            sprintf "%s is quantum-safe (%.0f-bit quantum security)" cipher.Name quantumSecurity
        elif quantumSecurity >= 64.0 then
            sprintf "%s has reduced security (%.0f-bit quantum). Consider upgrading to %d-bit keys."
                cipher.Name quantumSecurity (cipher.KeyBits * 2)
        else
            sprintf "%s is NOT quantum-safe (%.0f-bit quantum). UPGRADE IMMEDIATELY."
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
// Main Execution
// ============================================================================

if not quiet then
    printfn "=== Grover's Algorithm Threat to Symmetric Cryptography ==="
    printfn ""
    printfn "BUSINESS SCENARIO:"
    printfn "A security architect needs to assess whether current symmetric encryption"
    printfn "(AES-128, AES-256) will remain secure against quantum computers. This"
    printfn "demonstrates Grover's quadratic speedup for brute-force key search."
    printfn ""

// ============================================================================
// Scenario 1: Compare Classical vs Quantum Key Search
// ============================================================================

let ciphers = [
    { Name = "DES";      KeyBits = 56;  BlockBits = 64 }
    { Name = "3DES";     KeyBits = 112; BlockBits = 64 }
    { Name = "AES-128";  KeyBits = 128; BlockBits = 128 }
    { Name = "AES-192";  KeyBits = 192; BlockBits = 128 }
    { Name = "AES-256";  KeyBits = 256; BlockBits = 128 }
    { Name = "ChaCha20"; KeyBits = 256; BlockBits = 512 }
]

let analyses = ciphers |> List.map analyzeSymmetricCipher

if not quiet then
    printfn "--- Scenario 1: Classical vs Quantum Brute Force ---"
    printfn ""
    printfn "Security Level Comparison:"
    printfn ""
    printfn "  Cipher       Key Size   Classical (bits)   Quantum (bits)   Safe?"
    printfn "  ----------   --------   ----------------   --------------   -----"

    analyses |> List.iter (fun a ->
        let qStatus = if a.QuantumSafe then "Yes" else "No"
        printfn "  %-10s   %3d-bit    %6.0f             %6.0f           %s"
            a.Cipher.Name a.Cipher.KeyBits
            a.ClassicalSecurity a.QuantumSecurity qStatus)

    printfn ""
    printfn "128-bit quantum security is considered the post-quantum minimum."
    printfn ""

// Collect all cipher analysis results
analyses |> List.iter (fun a ->
    addResult "Cipher Comparison" a.Cipher.Name a.Cipher.KeyBits
        (int a.ClassicalSecurity) (int a.QuantumSecurity)
        a.QuantumSafe a.Recommendation)

// ============================================================================
// Scenario 2: Detailed AES Analysis (focused on user-specified key size)
// ============================================================================

let aesVariants = [
    { Name = "AES-128"; KeyBits = 128; BlockBits = 128 }
    { Name = "AES-192"; KeyBits = 192; BlockBits = 128 }
    { Name = "AES-256"; KeyBits = 256; BlockBits = 128 }
]

if not quiet then
    printfn "--- Scenario 2: AES Quantum Security Analysis ---"
    printfn ""

    aesVariants |> List.iter (fun cipher ->
        let analysis = analyzeSymmetricCipher cipher
        let resources = estimateAESGroverResources cipher.KeyBits
        let iters = groverIterations cipher.KeyBits
        let focusMarker = if cipher.KeyBits = focusKeySize then " [FOCUS]" else ""

        printfn "%s:%s" cipher.Name focusMarker
        printfn "  Key space:           2^%d = %.2e possible keys" cipher.KeyBits (Math.Pow(2.0, float cipher.KeyBits))
        printfn "  Classical security:  %.0f bits (brute force: 2^%.0f ops)" analysis.ClassicalSecurity analysis.ClassicalSecurity
        printfn "  Quantum security:    %.0f bits (Grover: 2^%.0f ops)" analysis.QuantumSecurity analysis.QuantumSecurity
        printfn "  Grover iterations:   ~2^%.0f" (Math.Log(iters, 2.0))
        printfn "  Quantum resources:   %s" resources
        printfn "  Assessment:          %s" analysis.Recommendation
        printfn "")

// ============================================================================
// Scenario 3: Why Grover Doesn't Break AES
// ============================================================================

let focusCipher = aesVariants |> List.find (fun c -> c.KeyBits = focusKeySize)
let focusQuantumBits = quantumSecurityBits focusCipher.KeyBits
let opsPerSecond = 1e9  // Optimistic: 1 billion quantum ops/sec
let quantumOps = Math.Pow(2.0, focusQuantumBits)
let timeSeconds = quantumOps / opsPerSecond
let timeYears = timeSeconds / (365.25 * 24.0 * 3600.0)

if not quiet then
    printfn "--- Scenario 3: Why Grover Doesn't Break AES (Yet) ---"
    printfn ""
    printfn "Even with Grover's speedup, breaking %s requires:" focusCipher.Name
    printfn ""
    printfn "  Quantum operations:  2^%.0f = %.2e" focusQuantumBits quantumOps
    printfn "  Qubits needed:       %s" (estimateAESGroverResources focusCipher.KeyBits)
    printfn "  Circuit depth:       ~2^%.0f sequential operations" focusQuantumBits
    printfn ""
    printfn "Time estimate (optimistic 10^9 ops/sec):"
    printfn "  Operations:  %.2e" quantumOps
    printfn "  Time:        %.2e seconds" timeSeconds
    printfn "  Time:        %.2e years" timeYears

    if timeYears > 1e9 then
        printfn "  Time:        ~%.0f billion years" (timeYears / 1e9)
    elif timeYears > 1e6 then
        printfn "  Time:        ~%.0f million years" (timeYears / 1e6)

    printfn ""
    printfn "KEY INSIGHT: Even with Grover, %s requires impractical time!" focusCipher.Name
    printfn "             The circuit depth alone makes it infeasible."
    printfn ""

// Add time estimate to results
results.Add(
    [ "scenario", "Time Estimate"
      "cipher", focusCipher.Name
      "key_bits", string focusCipher.KeyBits
      "quantum_security_bits", string (int focusQuantumBits)
      "quantum_ops", sprintf "%.2e" quantumOps
      "time_seconds", sprintf "%.2e" timeSeconds
      "time_years", sprintf "%.2e" timeYears
      "ops_per_second", sprintf "%.0e" opsPerSecond ]
    |> Map.ofList)

// ============================================================================
// Scenario 4: Practical Recommendations
// ============================================================================

if not quiet then
    printfn "--- Scenario 4: Post-Quantum Symmetric Crypto Recommendations ---"
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

    recommendations |> List.iter (fun (useCase, rec') ->
        printfn "  %-16s -> %s" useCase rec')

    printfn ""

    // Comparison with asymmetric crypto
    printfn "COMPARISON: Symmetric vs Asymmetric Quantum Threats"
    printfn ""
    printfn "  Crypto Type      Algorithm       Quantum Attack   Mitigation"
    printfn "  ---------------  --------------  ---------------  ---------------"
    printfn "  Symmetric        AES-128         Grover (2^64)    Use AES-256"
    printfn "  Symmetric        AES-256         Grover (2^128)   Already safe"
    printfn "  Asymmetric       RSA-2048        Shor (poly)      Use PQ crypto"
    printfn "  Asymmetric       ECDH-256        Shor (poly)      Use PQ crypto"
    printfn "  Hash             SHA-256         Grover (2^128)   Already safe"
    printfn ""
    printfn "Key insight: Symmetric crypto needs only key doubling; asymmetric"
    printfn "             crypto needs complete algorithm replacement!"
    printfn ""

// ============================================================================
// Demonstration: Grover Search Using Our Library
// ============================================================================

if not quiet then
    printfn "--- Demonstration: Grover Search Infrastructure ---"
    printfn ""
    printfn "Our library's GroverSearch module demonstrates Grover's algorithm."
    printfn "For a real AES attack, the oracle would check: Decrypt(key, ciphertext) = plaintext"
    printfn ""

let searchSpace = 1 <<< numQubits

if not quiet then
    let optimalIters = int (Math.Round((Math.PI / 4.0) * Math.Sqrt(float searchSpace)))
    printfn "Example: Search %d-item database (%d qubits)" searchSpace numQubits
    printfn "  Database size: N = %d" searchSpace
    printfn "  Classical:     O(%d) = %d queries average" searchSpace searchSpace
    printfn "  Grover:        O(sqrt(%d)) = %d queries" searchSpace (int (Math.Sqrt(float searchSpace)))
    printfn "  Optimal iterations: pi/4 * sqrt(%d) ~ %d" searchSpace optimalIters
    printfn ""

if targetValue >= searchSpace then
    if not quiet then
        printfn "  WARNING: Target value %d exceeds search space 2^%d = %d" targetValue numQubits searchSpace
        printfn "           Skipping Grover demo (use --target with a smaller value)"
        printfn ""
    addGroverResult targetValue "out_of_range" 0 0.0 numQubits
else
    if not quiet then
        printfn "Creating oracle for target value %d in %d-qubit space..." targetValue numQubits

    match Oracle.forValue targetValue numQubits with
    | Error err ->
        if not quiet then
            printfn "  Failed to create oracle: %A" err
        addGroverResult targetValue "oracle_error" 0 0.0 numQubits
    | Ok oracle ->
        if not quiet then
            printfn "  Oracle created successfully"
            printfn ""

            // Verify which values are marked by the oracle
            printfn "Oracle verification (classical check):"
            for i in 0 .. searchSpace - 1 do
                let isTarget = Oracle.isSolution oracle.Spec i
                let mark = if isTarget then "TARGET" else "      "
                printfn "  %s |%d> = |%s>" mark i (Convert.ToString(i, 2).PadLeft(numQubits, '0'))

            printfn ""
            printfn "Running Grover search..."

        // Create local backend and search configuration
        let backend = LocalBackend() :> IQuantumBackend
        let optIters = int (Math.Round((Math.PI / 4.0) * Math.Sqrt(float searchSpace)))
        let config = { Grover.defaultConfig with Iterations = Some (max 1 optIters) }

        match Grover.search oracle backend config with
        | Error err ->
            if not quiet then
                printfn "  Search failed: %A" err
            addGroverResult targetValue "search_error" 0 0.0 numQubits
        | Ok result ->
            let foundStr =
                if result.Solutions.IsEmpty then "(none)"
                else result.Solutions |> List.map string |> String.concat ", "

            if not quiet then
                printfn ""
                printfn "Grover Search Result:"
                printfn "  Search space:    2^%d = %d items" numQubits searchSpace
                printfn "  Target:          |%d>" targetValue

                if result.Solutions.IsEmpty then
                    printfn "  Found:           (no solution)"
                else
                    for solution in result.Solutions do
                        printfn "  Found:           |%d> = |%s>" solution (Convert.ToString(solution, 2).PadLeft(numQubits, '0'))

                printfn "  Iterations:      %d" result.Iterations
                printfn "  Success prob:    %.1f%%" (result.SuccessProbability * 100.0)
                printfn ""
                printfn "This same technique would search the AES key space--just with"
                printfn "128+ qubits and an oracle that checks decryption!"
                printfn ""

            addGroverResult targetValue foundStr result.Iterations result.SuccessProbability numQubits

// ============================================================================
// Key Takeaways
// ============================================================================

if not quiet then
    printfn "--- Key Takeaways ---"
    printfn ""

    let takeaways = [
        "Grover provides QUADRATIC speedup (sqrt(N)), not exponential like Shor"
        "AES-128: 128-bit classical -> 64-bit quantum security"
        "AES-256: 256-bit classical -> 128-bit quantum security (safe!)"
        "Even with Grover, AES-128 attack needs billions of years"
        "Simple mitigation: DOUBLE the key size (AES-128 -> AES-256)"
        "Symmetric crypto is FAR easier to make quantum-safe than asymmetric"
        "NIST recommendation: Use 256-bit symmetric keys for post-quantum"
    ]

    takeaways |> List.iter (printfn "- %s")
    printfn ""

// ============================================================================
// Structured Output
// ============================================================================

let resultsList = results |> Seq.toList

match outputPath with
| Some path -> Reporting.writeJson path resultsList
| None -> ()

match csvPath with
| Some path ->
    // Use union of all keys across result rows
    let allKeys =
        resultsList
        |> List.collect (fun m -> m |> Map.toList |> List.map fst)
        |> List.distinct
    let rows =
        resultsList
        |> List.map (fun m -> allKeys |> List.map (fun k -> m |> Map.tryFind k |> Option.defaultValue ""))
    Reporting.writeCsv path allKeys rows
| None -> ()

// ============================================================================
// Usage Hints
// ============================================================================

if not quiet && outputPath.IsNone && csvPath.IsNone && argv.Length = 0 then
    printfn ""
    printfn "Hint: Customize this run with CLI options:"
    printfn "  dotnet fsi GroverAESThreat.fsx -- --key-size 256"
    printfn "  dotnet fsi GroverAESThreat.fsx -- --target 5 --qubits 3"
    printfn "  dotnet fsi GroverAESThreat.fsx -- --quiet --output results.json --csv results.csv"
    printfn "  dotnet fsi GroverAESThreat.fsx -- --help"
