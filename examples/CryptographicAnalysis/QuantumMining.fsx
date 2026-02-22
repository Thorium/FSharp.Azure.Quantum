// Quantum Proof-of-Work Mining - Grover's Algorithm vs Bitcoin Mining
// Demonstrates using quantum search to break simplified Bitcoin-like PoW puzzles
//
// Usage:
//   dotnet fsi QuantumMining.fsx
//   dotnet fsi QuantumMining.fsx -- --help
//   dotnet fsi QuantumMining.fsx -- --qubits 10 --difficulty 2
//   dotnet fsi QuantumMining.fsx -- --quiet --output results.json --csv results.csv

(*
===============================================================================
 Background Theory
===============================================================================

Bitcoin Proof-of-Work mining requires finding a nonce N such that
SHA256(SHA256(BlockHeader || N)) < Target, where Target encodes the difficulty.
At current difficulty (~70 leading zero bits), this requires ~2^70 hash
evaluations on average -- an enormous classical brute-force search.

Grover's algorithm (1996) provides quadratic speedup for unstructured search:
given a search space of N items with M "marked" solutions, Grover finds a
solution in O(sqrt(N/M)) queries versus O(N/M) classical queries. For mining,
the oracle marks nonces whose SHA-256 hash meets the difficulty target.

In this demonstration:
  - We use a SIMPLIFIED mining puzzle with 10 qubits (1024-nonce search space)
  - Difficulty is set trivially low (2-4 leading zero bits instead of ~70)
  - We use real SHA-256 hashing to evaluate each nonce
  - Grover's algorithm finds valid nonces with quadratic speedup

This models what a "quantum miner" could do on a near-term device: not break
Bitcoin (which requires ~2^70 search space), but demonstrate the algorithmic
advantage. A 10-qubit quantum computer on Azure Quantum (Rigetti, IonQ, or
Quantinuum) or our LocalBackend can search 1024 nonces in ~sqrt(1024/M)
iterations instead of checking each one classically.

Key Equations:
  - Mining predicate: SHA256(blockData || nonce) has >= D leading zero bits
  - Classical search: O(N/M) where N = 2^n, M = solutions meeting difficulty
  - Grover search: O(sqrt(N/M)) quantum oracle queries
  - For D leading zeros: M ~ N / 2^D solutions exist on average
  - Optimal iterations: pi/4 * sqrt(N/M)
  - Speedup: sqrt(N/M) / (N/M) = sqrt(M/N) = 1/sqrt(N/M)

Practical Considerations:
  - Each Grover oracle query requires implementing SHA-256 as a reversible
    quantum circuit (~tens of thousands of qubits for ancillae and T-gates)
  - Real Bitcoin mining at difficulty ~70 would need ~2^35 Grover iterations
    (vs ~2^70 classical) -- still enormous, but quadratically better
  - Current quantum hardware (2025): ~20-100 logical qubits, far from the
    thousands needed for a full SHA-256 oracle circuit
  - This script demonstrates the principle on a toy-scale problem

References:
  [1] Grover, "A fast quantum mechanical algorithm for database search",
      STOC 1996, pp. 212-219. https://doi.org/10.1145/237814.237866
  [2] Aggarwal et al., "Quantum Attacks on Bitcoin, and How to Protect Against
      Them", Ledger 3 (2018). https://doi.org/10.5195/ledger.2018.127
  [3] Tessler & Byrnes, "Bitcoin and quantum computing",
      arXiv:1711.04235 (2017). https://arxiv.org/abs/1711.04235
  [4] Wikipedia: Proof_of_work
      https://en.wikipedia.org/wiki/Proof_of_work
  [5] Bitcoin Wiki: Block hashing algorithm
      https://en.bitcoin.it/wiki/Block_hashing_algorithm
*)

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "nuget: NBitcoin, 7.0.44"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.Security.Cryptography
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.GroverSearch.Oracle
open FSharp.Azure.Quantum.Examples.Common
open NBitcoin

// ============================================================================
// CLI Setup
// ============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "QuantumMining.fsx" "Quantum Proof-of-Work mining using Grover's algorithm to break simplified Bitcoin-like puzzles." [
    { Name = "qubits"; Description = "Number of qubits (nonce search space = 2^qubits)"; Default = Some "10" }
    { Name = "difficulty"; Description = "Leading zero bits required in hash (1-8)"; Default = Some "2" }
    { Name = "block-data"; Description = "Block data string to hash with nonce"; Default = Some "QuantumBlock:1" }
    { Name = "shots"; Description = "Number of measurement shots"; Default = Some "1000" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress informational output"; Default = None }
] args

let numQubitsRaw = Cli.getIntOr "qubits" 10 args
let difficultyRaw = Cli.getIntOr "difficulty" 2 args
let blockDataStr = Cli.getOr "block-data" "QuantumBlock:1" args
let shots = Cli.getIntOr "shots" 1000 args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// Clamp qubits to valid range (Oracle.compile enforces 1-20)
let numQubits = max 1 (min 20 numQubitsRaw)
let difficulty = max 1 (min 8 difficultyRaw)

// ============================================================================
// Result Collection
// ============================================================================

let results = System.Collections.Generic.List<Map<string, string>>()

// ============================================================================
// SHA-256 Mining Functions
// ============================================================================

/// Compute SHA-256 hash of blockData concatenated with nonce bytes
let computeHash (blockData: byte[]) (nonce: int) : byte[] =
    let nonceBytes = BitConverter.GetBytes(nonce)
    let input = Array.append blockData nonceBytes
    use sha = SHA256.Create()
    sha.ComputeHash(input)

/// Check if a hash has at least the required number of leading zero bits
let hasLeadingZeroBits (hash: byte[]) (zeroBits: int) : bool =
    let mutable bitsChecked = 0
    let mutable allZero = true
    let mutable byteIndex = 0
    while allZero && bitsChecked < zeroBits && byteIndex < hash.Length do
        let remainingBits = zeroBits - bitsChecked
        if remainingBits >= 8 then
            // Check entire byte
            allZero <- hash.[byteIndex] = 0uy
            bitsChecked <- bitsChecked + 8
        else
            // Check top N bits of byte
            let mask = 0xFFuy <<< (8 - remainingBits)
            allZero <- (hash.[byteIndex] &&& mask) = 0uy
            bitsChecked <- bitsChecked + remainingBits
        byteIndex <- byteIndex + 1
    allZero

/// Format hash bytes as hex string
let hashToHex (hash: byte[]) : string =
    hash |> Array.map (fun b -> sprintf "%02x" b) |> String.concat ""

/// Mining predicate: does SHA256(blockData || nonce) meet difficulty?
let miningPredicate (blockData: byte[]) (zeroBits: int) (nonce: int) : bool =
    let hash = computeHash blockData nonce
    hasLeadingZeroBits hash zeroBits

// ============================================================================
// Main Execution
// ============================================================================

if not quiet then
    printfn "=== Quantum Proof-of-Work Mining ==="
    printfn ""
    printfn "HACKER SCENARIO:"
    printfn "An attacker with a 10-qubit quantum computer (e.g. Rigetti on Azure Quantum)"
    printfn "uses Grover's algorithm to mine blocks in a simplified Bitcoin-like system."
    printfn "Instead of checking nonces one by one, the quantum computer searches the"
    printfn "entire nonce space in sqrt(N/M) iterations -- a quadratic speedup."
    printfn ""
    printfn "Parameters:"
    printfn "  Qubits:        %d (search space = %d nonces)" numQubits (1 <<< numQubits)
    printfn "  Difficulty:    %d leading zero bits" difficulty
    printfn "  Block data:    \"%s\"" blockDataStr
    printfn "  Shots:         %d" shots
    printfn ""

let blockData = System.Text.Encoding.UTF8.GetBytes(blockDataStr)
let searchSpace = 1 <<< numQubits

// ============================================================================
// Scenario 1: Classical Mining (Brute Force)
// ============================================================================

if not quiet then
    printfn "--- Scenario 1: Classical Brute-Force Mining ---"
    printfn ""

let sw = System.Diagnostics.Stopwatch.StartNew()

// Find ALL valid nonces classically
let validNonces =
    [| 0 .. searchSpace - 1 |]
    |> Array.filter (miningPredicate blockData difficulty)

sw.Stop()
let classicalTimeMs = sw.Elapsed.TotalMilliseconds

if not quiet then
    printfn "Classical miner checked all %d nonces in %.2f ms" searchSpace classicalTimeMs
    printfn "Valid nonces found: %d out of %d (%.1f%%)"
        validNonces.Length searchSpace
        (100.0 * float validNonces.Length / float searchSpace)
    printfn ""

    if validNonces.Length > 0 then
        let showCount = min 5 validNonces.Length
        printfn "First %d valid nonces:" showCount
        for i in 0 .. showCount - 1 do
            let nonce = validNonces.[i]
            let hash = computeHash blockData nonce
            printfn "  Nonce %4d -> %s" nonce (hashToHex hash)
        if validNonces.Length > showCount then
            printfn "  ... and %d more" (validNonces.Length - showCount)
    else
        printfn "  No valid nonces found! Try lowering --difficulty"
    printfn ""

results.Add(
    [ "scenario", "Classical Mining"
      "qubits", string numQubits
      "search_space", string searchSpace
      "difficulty_bits", string difficulty
      "valid_nonces", string validNonces.Length
      "solution_density", sprintf "%.4f" (float validNonces.Length / float searchSpace)
      "time_ms", sprintf "%.2f" classicalTimeMs
      "method", "brute_force"
      "queries", string searchSpace ]
    |> Map.ofList)

// ============================================================================
// Scenario 2: Quantum Mining with Grover's Algorithm
// ============================================================================

if not quiet then
    printfn "--- Scenario 2: Quantum Mining (Grover's Algorithm) ---"
    printfn ""

if validNonces.Length = 0 then
    if not quiet then
        printfn "  Skipping Grover search: no valid nonces exist at difficulty %d." difficulty
        printfn "  Try lowering --difficulty or changing --block-data."
        printfn ""

    results.Add(
        [ "scenario", "Quantum Mining"
          "qubits", string numQubits
          "search_space", string searchSpace
          "difficulty_bits", string difficulty
          "valid_nonces", "0"
          "status", "no_solutions"
          "method", "grover" ]
        |> Map.ofList)
else
    let backend = LocalBackend() :> IQuantumBackend

    if not quiet then
        let expectedIters =
            int (Math.Round((Math.PI / 4.0) * Math.Sqrt(float searchSpace / float validNonces.Length)))
        printfn "Grover's algorithm setup:"
        printfn "  Search space:     N = %d nonces" searchSpace
        printfn "  Valid solutions:  M = %d" validNonces.Length
        printfn "  Classical avg:    N/M = %d queries" (searchSpace / max 1 validNonces.Length)
        printfn "  Grover optimal:   pi/4 * sqrt(N/M) ~ %d iterations" expectedIters
        printfn "  Speedup factor:   %.1fx fewer queries"
            (float searchSpace / float (max 1 validNonces.Length) / float (max 1 expectedIters))
        printfn ""
        printfn "Creating quantum oracle: SHA256(blockData || nonce) has >=%d leading zero bits..."
            difficulty

    // Create oracle from predicate -- the core of the quantum mining attack
    let oracleResult = Oracle.fromPredicate (miningPredicate blockData difficulty) numQubits

    match oracleResult with
    | Error err ->
        if not quiet then
            printfn "  Oracle creation failed: %A" err
        results.Add(
            [ "scenario", "Quantum Mining"
              "status", sprintf "oracle_error: %A" err ]
            |> Map.ofList)
    | Ok oracle ->
        if not quiet then
            printfn "  Oracle compiled (%d qubits)" oracle.NumQubits
            printfn ""
            printfn "Running Grover search (%d shots)..." shots

        let sw2 = System.Diagnostics.Stopwatch.StartNew()

        // With many solutions (M >> 1), each individual solution gets ~1/M of the
        // probability mass. Lower the threshold so we detect them. For M solutions
        // uniformly amplified, each gets ~shots/M hits, so threshold = 1/(2*M).
        let solutionThresh =
            let m = float validNonces.Length
            if m > 1.0 then 0.5 / m else Grover.defaultConfig.SolutionThreshold
        let config =
            { Grover.defaultConfig with
                Shots = shots
                SolutionThreshold = solutionThresh }

        match Grover.search oracle backend config with
        | Error err ->
            sw2.Stop()
            if not quiet then
                printfn "  Grover search failed: %A" err
            results.Add(
                [ "scenario", "Quantum Mining"
                  "status", sprintf "search_error: %A" err ]
                |> Map.ofList)
        | Ok result ->
            sw2.Stop()
            let quantumTimeMs = sw2.Elapsed.TotalMilliseconds

            if not quiet then
                printfn ""
                printfn "Grover Search Results:"
                printfn "  Solutions found:   %d" result.Solutions.Length
                printfn "  Iterations used:   %d" result.Iterations
                printfn "  Success prob:      %.1f%%" (result.SuccessProbability * 100.0)
                printfn "  Execution time:    %.2f ms" quantumTimeMs
                printfn ""

                // Verify solutions with actual SHA-256
                if result.Solutions.Length > 0 then
                    let showCount = min 5 result.Solutions.Length
                    printfn "Verification (SHA-256 hash of found nonces):"
                    for i in 0 .. showCount - 1 do
                        let nonce = result.Solutions.[i]
                        let hash = computeHash blockData nonce
                        let valid = hasLeadingZeroBits hash difficulty
                        let mark = if valid then "VALID" else "INVALID"
                        printfn "  Nonce %4d -> %s [%s]" nonce (hashToHex hash) mark
                    if result.Solutions.Length > showCount then
                        printfn "  ... and %d more" (result.Solutions.Length - showCount)
                    printfn ""

                // Show measurement distribution (top values)
                if result.Measurements.Count > 0 then
                    printfn "Top measured nonces (by frequency):"
                    result.Measurements
                    |> Map.toList
                    |> List.sortByDescending snd
                    |> List.truncate 8
                    |> List.iter (fun (nonce, count) ->
                        let hash = computeHash blockData nonce
                        let valid = hasLeadingZeroBits hash difficulty
                        let mark = if valid then "*" else " "
                        printfn "  %s Nonce %4d: %4d hits (%.1f%%) hash=%s"
                            mark nonce count
                            (100.0 * float count / float shots)
                            (hashToHex hash |> fun s -> s.[0..15] + "..."))
                    printfn "  (* = valid mining solution)"
                    printfn ""

            // Verify all quantum solutions are actually valid
            let verifiedSolutions =
                result.Solutions
                |> List.filter (miningPredicate blockData difficulty)

            results.Add(
                [ "scenario", "Quantum Mining"
                  "qubits", string numQubits
                  "search_space", string searchSpace
                  "difficulty_bits", string difficulty
                  "valid_nonces_classical", string validNonces.Length
                  "solutions_found", string result.Solutions.Length
                  "solutions_verified", string verifiedSolutions.Length
                  "iterations", string result.Iterations
                  "success_probability", sprintf "%.4f" result.SuccessProbability
                  "time_ms", sprintf "%.2f" quantumTimeMs
                  "method", "grover"
                  "shots", string shots ]
                |> Map.ofList)

// ============================================================================
// Scenario 3: Classical vs Quantum Comparison
// ============================================================================

if not quiet then
    printfn "--- Scenario 3: Classical vs Quantum Mining Comparison ---"
    printfn ""

let numSolutions = validNonces.Length
let classicalQueries = if numSolutions > 0 then searchSpace / numSolutions else searchSpace
let groverIters =
    if numSolutions > 0 then
        int (Math.Round((Math.PI / 4.0) * Math.Sqrt(float searchSpace / float numSolutions)))
    else 0

if not quiet then
    printfn "  Search space:      N = %d" searchSpace
    printfn "  Valid solutions:   M = %d" numSolutions
    printfn ""
    printfn "  Classical mining:  %d queries (avg N/M)" classicalQueries
    printfn "  Quantum mining:    %d iterations (pi/4 * sqrt(N/M))" groverIters
    if groverIters > 0 then
        printfn "  Speedup:           %.1fx" (float classicalQueries / float groverIters)
    printfn ""

    // Scale analysis for different qubit counts
    printfn "Scaling analysis (difficulty = %d leading zeros):" difficulty
    printfn ""
    printfn "  Qubits   Search Space     Classical Avg    Grover Iters     Speedup"
    printfn "  ------   ------------     -------------    ------------     -------"

    let expectedSolutionFraction = float numSolutions / float searchSpace

    for q in [4; 6; 8; 10; 12; 14; 16; 18; 20] do
        let n = 1 <<< q
        let m = max 1 (int (float n * expectedSolutionFraction))
        let classical = n / m
        let grover = int (Math.Round((Math.PI / 4.0) * Math.Sqrt(float n / float m)))
        let speedup = float classical / float (max 1 grover)
        printfn "  %2d       %8d         %8d         %8d         %.1fx"
            q n classical grover speedup

    printfn ""

results.Add(
    [ "scenario", "Comparison"
      "search_space", string searchSpace
      "solutions", string numSolutions
      "classical_queries", string classicalQueries
      "grover_iterations", string groverIters
      "speedup", if groverIters > 0 then sprintf "%.1f" (float classicalQueries / float groverIters) else "N/A" ]
    |> Map.ofList)

// ============================================================================
// Scenario 4: Multiple Difficulty Levels
// ============================================================================

if not quiet then
    printfn "--- Scenario 4: Mining at Multiple Difficulty Levels ---"
    printfn ""
    printfn "Block data: \"%s\" | Search space: %d nonces (%d qubits)"
        blockDataStr searchSpace numQubits
    printfn ""
    printfn "  Difficulty   Valid Nonces   Density      Classical    Grover     Speedup"
    printfn "  ----------   -----------   ---------    ---------    ------     -------"

let maxDiffToTest = min 8 (numQubits - 1)

for d in 1 .. maxDiffToTest do
    let count =
        [| 0 .. searchSpace - 1 |]
        |> Array.filter (miningPredicate blockData d)
        |> Array.length

    let density = float count / float searchSpace
    let classicalAvg = if count > 0 then searchSpace / count else searchSpace
    let groverOpt =
        if count > 0 then
            int (Math.Round((Math.PI / 4.0) * Math.Sqrt(float searchSpace / float count)))
        else 0
    let speedup =
        if groverOpt > 0 then float classicalAvg / float groverOpt else 0.0

    if not quiet then
        printfn "  %2d bits       %5d         %.4f       %6d       %5d       %.1fx"
            d count density classicalAvg groverOpt speedup

    results.Add(
        [ "scenario", sprintf "Difficulty_%d" d
          "difficulty_bits", string d
          "valid_nonces", string count
          "density", sprintf "%.4f" density
          "classical_queries", string classicalAvg
          "grover_iterations", string groverOpt
          "speedup", if groverOpt > 0 then sprintf "%.1f" speedup else "N/A" ]
        |> Map.ofList)

if not quiet then
    printfn ""

// ============================================================================
// Scenario 5: Real Bitcoin Mining Parameters (NBitcoin)
// ============================================================================

if not quiet then
    printfn "--- Scenario 5: Real Bitcoin Mining Context (via NBitcoin) ---"
    printfn ""

// Generate a real Bitcoin block structure for comparison
let network = Network.Main

if not quiet then
    // Show real Bitcoin mining parameters
    printfn "Real Bitcoin Block Structure:"
    printfn "  Network:             %s" network.Name
    printfn "  Hash algorithm:      Double SHA-256 (SHA256d)"
    printfn "  Block header size:   80 bytes"
    printfn "  Nonce field:         32-bit (4 bytes, ~4 billion values)"
    printfn "  Extra nonce:         Coinbase transaction (effectively unlimited)"
    printfn ""

    // Bitcoin difficulty comparison
    printfn "Bitcoin Difficulty Context (2025):"
    printfn ""
    printfn "  Current difficulty:  ~2^70 leading zero bits equivalent"
    printfn "  Target hash space:   256-bit (SHA-256 output)"
    printfn "  Nonce space:         2^32 per block header (extraNonce extends this)"
    printfn "  Hash rate:           ~500 EH/s (5 x 10^20 hashes/sec)"
    printfn "  Block time:          ~10 minutes average"
    printfn ""

    // Show a real Bitcoin key pair (demonstrates NBitcoin integration)
    let key = new Key()
    let pubKey = key.PubKey
    let p2pkhAddr = pubKey.GetAddress(ScriptPubKeyType.Legacy, network)

    printfn "Sample Bitcoin Key Pair (for context):"
    printfn "  Private key (WIF):   %s" (key.GetWif(network).ToString())
    printfn "  Public key:          %s" (pubKey.ToHex())
    printfn "  P2PKH address:       %s" (p2pkhAddr.ToString())
    printfn ""

// ============================================================================
// Scenario 6: Quantum Threat Assessment for Bitcoin Mining
// ============================================================================

if not quiet then
    printfn "--- Scenario 6: Quantum Threat Assessment for Bitcoin PoW ---"
    printfn ""

    // Real-world quantum mining analysis
    let bitcoinDifficulty = 70.0  // ~70 leading zero bits
    let bitcoinSearchSpace = Math.Pow(2.0, 32.0)  // 2^32 nonces per header
    let effectiveSearchSpace = Math.Pow(2.0, bitcoinDifficulty)  // effective search per solution

    let classicalOps = effectiveSearchSpace
    let groverOps = Math.Sqrt(effectiveSearchSpace)
    let groverLog2 = bitcoinDifficulty / 2.0

    printfn "Bitcoin PoW Quantum Attack Analysis:"
    printfn ""
    printfn "  Difficulty:          ~2^%.0f" bitcoinDifficulty
    printfn "  Classical mining:    ~2^%.0f hash evaluations" bitcoinDifficulty
    printfn "  Grover mining:       ~2^%.0f quantum oracle queries" groverLog2
    printfn "  Speedup:             2^%.0f (quadratic)" (bitcoinDifficulty / 2.0)
    printfn ""

    // Resource estimates for quantum Bitcoin mining
    printfn "Quantum Resource Requirements for Bitcoin Mining:"
    printfn ""
    printfn "  SHA-256 quantum circuit:"
    printfn "    Logical qubits:    ~2,500-3,000 (SHA-256 in superposition)"
    printfn "    T-gate count:      ~10^8 per oracle call"
    printfn "    Circuit depth:     ~10^6 per iteration"
    printfn ""
    printfn "  Total Grover iterations: ~2^35 = %.2e" (Math.Pow(2.0, 35.0))
    printfn ""

    // Feasibility timeline
    printfn "Feasibility Assessment:"
    printfn ""
    printfn "  Current quantum hardware (2025):"
    printfn "    Logical qubits:    ~20-100 (with error correction)"
    printfn "    Required qubits:   ~2,500-3,000 for SHA-256 oracle"
    printfn "    Gap:               ~25-100x more qubits needed"
    printfn ""

    let opsPerSec = 1e6  // Optimistic quantum gate rate
    let totalOps = Math.Pow(2.0, 35.0) * 1e6  // iterations * gates_per_iteration
    let timeSeconds = totalOps / opsPerSec
    let timeYears = timeSeconds / (365.25 * 24.0 * 3600.0)

    printfn "  Time estimate (optimistic 10^6 gates/sec):"
    printfn "    Total operations:  ~%.2e" totalOps
    printfn "    Time required:     ~%.2e seconds" timeSeconds
    printfn "    Time required:     ~%.2e years" timeYears
    printfn ""

    let classicalMiningCost = 2e10  // ~$20B/year in electricity for Bitcoin mining
    printfn "  Economic comparison:"
    printfn "    Classical mining:  ~$%.0f billion/year (global electricity)" (classicalMiningCost / 1e9)
    printfn "    Quantum advantage: Would need quantum ops at <$%.2e per op" (classicalMiningCost / Math.Pow(2.0, 35.0))
    printfn "                       to be economically viable"
    printfn ""

    printfn "  Verdict: Quantum mining is NOT a near-term threat to Bitcoin."
    printfn "  The quadratic speedup (2^70 -> 2^35) is helpful but insufficient"
    printfn "  given the enormous overhead of quantum SHA-256 circuits."
    printfn "  Shor's algorithm (for ECDSA signatures) is a FAR greater threat."
    printfn ""

results.Add(
    [ "scenario", "Bitcoin Threat Assessment"
      "bitcoin_difficulty_bits", "70"
      "classical_ops_log2", "70"
      "grover_ops_log2", "35"
      "sha256_qubits_needed", "2500-3000"
      "current_qubits_available", "20-100"
      "near_term_threat", "false"
      "greater_threat", "Shor on ECDSA signatures" ]
    |> Map.ofList)

// ============================================================================
// Scenario 7: Quantum vs Classical Mining Cost Scaling
// ============================================================================

if not quiet then
    printfn "--- Scenario 7: Mining Cost Scaling (Quantum vs Classical) ---"
    printfn ""
    printfn "How does the quantum advantage scale with difficulty?"
    printfn ""
    printfn "  Difficulty   Classical Ops     Grover Ops       Speedup      Feasible?"
    printfn "  ----------   -------------     ----------       -------      ---------"

    let difficulties = [4; 8; 16; 32; 48; 64; 70; 80; 128]

    for d in difficulties do
        let classicalLog2 = float d
        let groverLog2 = float d / 2.0
        let feasible =
            if groverLog2 <= 20.0 then "Now (demo)"
            elif groverLog2 <= 35.0 then "5-10 years"
            elif groverLog2 <= 50.0 then "15-25 years"
            else "Not foreseeable"

        printfn "  %3d bits     2^%-5.0f            2^%-5.0f          2^%-3.0f        %s"
            d classicalLog2 groverLog2 (classicalLog2 - groverLog2) feasible

    printfn ""
    printfn "KEY INSIGHT: Grover halves the exponent. Bitcoin's 2^70 becomes 2^35,"
    printfn "which is still ~34 billion iterations with a massive SHA-256 quantum circuit."
    printfn ""

// ============================================================================
// Key Takeaways
// ============================================================================

if not quiet then
    printfn "--- Key Takeaways ---"
    printfn ""

    let takeaways = [
        "Grover's algorithm provides QUADRATIC speedup for mining (2^n -> 2^(n/2))"
        "Our 10-qubit demo mines a toy puzzle; real Bitcoin needs ~3000 qubits for SHA-256"
        "At Bitcoin difficulty (~70 bits), Grover reduces search from 2^70 to 2^35"
        "But 2^35 iterations of a quantum SHA-256 circuit is still enormously expensive"
        "Quantum mining is NOT an imminent threat -- ECDSA key recovery (Shor) is far worse"
        "Bitcoin could add post-quantum defenses: larger nonce space, PQ hash functions"
        sprintf "Demo: %d-qubit Grover searched %d nonces, found %d valid solutions"
            numQubits searchSpace validNonces.Length
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
    printfn "  dotnet fsi QuantumMining.fsx -- --qubits 8 --difficulty 3"
    printfn "  dotnet fsi QuantumMining.fsx -- --block-data \"MyBlock:42\" --difficulty 1"
    printfn "  dotnet fsi QuantumMining.fsx -- --quiet --output results.json --csv results.csv"
    printfn "  dotnet fsi QuantumMining.fsx -- --help"
