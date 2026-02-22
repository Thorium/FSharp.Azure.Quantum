// RSA Factorization Example - Shor's Algorithm for Breaking RSA Keys
// Demonstrates quantum period finding to factor composite numbers
//
// Usage:
//   dotnet fsi RSAFactorization.fsx
//   dotnet fsi RSAFactorization.fsx -- --help
//   dotnet fsi RSAFactorization.fsx -- --number 21 --precision 6
//   dotnet fsi RSAFactorization.fsx -- --quiet --output results.json --csv results.csv

(*
===============================================================================
 Background Theory
===============================================================================

Shor's algorithm (1994) is the most famous quantum algorithm, demonstrating
exponential speedup for integer factorizationâ€”the problem underlying RSA
encryption security. Given a composite number N = p x q, classical algorithms
require O(exp(n^(1/3))) time (number field sieve) where n = log N, while Shor's
algorithm runs in O(n^3) time. This means a sufficiently large quantum computer
could break 2048-bit RSA in hours instead of billions of years, fundamentally
threatening current public-key cryptography.

The algorithm reduces factoring to period finding: for random a coprime to N,
find the period r of f(x) = a^x mod N. If r is even and a^(r/2) is not +/-1
(mod N), then gcd(a^(r/2) +/- 1, N) yields a factor. Quantum Phase Estimation
finds r by extracting the eigenvalue e^(2*pi*i*s/r) from the modular
exponentiation operator U_a|y> = |ay mod N>. The period r appears in the phase,
extracted via QFT and continued fractions. Success probability is >= 1/poly(log N)
per attempt.

Key Equations:
  - Factoring reduction: N = p * q -> find period r of a^x mod N
  - Modular exponentiation: U_a|y> = |ay mod N> has eigenvalues e^(2*pi*i*s/r)
  - Period extraction: QPE on U_a gives phase s/r; continued fractions yield r
  - Factorization: gcd(a^(r/2) + 1, N) and gcd(a^(r/2) - 1, N) give p, q
  - Resource estimate: ~4n qubits and ~O(n^3) gates for n-bit integer N
  - RSA-2048: ~4000 logical qubits, ~10^9 T-gates (years away with error correction)

Quantum Advantage:
  Shor's algorithm provides exponential speedup: O(n^3) quantum vs O(exp(n^(1/3)))
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
  [3] Gidney & Ekera, "How to factor 2048 bit RSA integers in 8 hours using 20
      million noisy qubits", Quantum 5, 433 (2021). https://doi.org/10.22331/q-2021-04-15-433
  [4] Wikipedia: Shor's_algorithm
      https://en.wikipedia.org/wiki/Shor%27s_algorithm
*)

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumPeriodFinder
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ============================================================================
// CLI Setup
// ============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "RSAFactorization.fsx" "Quantum period finding (Shor's algorithm) to factor RSA moduli." [
    { Name = "number"; Description = "Composite number to factor"; Default = Some "15" }
    { Name = "precision"; Description = "QPE precision qubits"; Default = Some "4" }
    { Name = "max-attempts"; Description = "Max probabilistic attempts"; Default = Some "10" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress informational output"; Default = None }
] args

let numberToFactor = Cli.getIntOr "number" 15 args
let precision = Cli.getIntOr "precision" 4 args
let maxAttempts = Cli.getIntOr "max-attempts" 10 args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ============================================================================
// Result Collection
// ============================================================================

let results = System.Collections.Generic.List<Map<string, string>>()

let addResult scenario number status factors baseUsed period qubits attempts error =
    results.Add(
        [ "scenario", scenario
          "number", string number
          "status", status
          "factors", factors
          "base_used", string baseUsed
          "period", string period
          "qubits_used", string qubits
          "attempts", string attempts
          "precision", string precision
          "error", error ]
        |> Map.ofList)

// ============================================================================
// Quantum Backend
// ============================================================================

let quantumBackend = LocalBackend() :> IQuantumBackend

// ============================================================================
// Factorization Function
// ============================================================================

/// Run Shor's algorithm on a target number and collect results
let runFactorization (scenario: string) (n: int) (prec: int) (maxAtt: int) =
    if not quiet then
        printfn "--- %s: Factor n = %d ---" scenario n
        printfn ""
        printfn "  Target RSA Modulus (n): %d" n
        printfn "  QPE Precision:          %d qubits" prec
        printfn "  Max Attempts:           %d" maxAtt
        printfn ""

    let problem = periodFinder {
        number n
        precision prec
        maxAttempts maxAtt
        backend quantumBackend
    }

    match problem with
    | Ok prob ->
        if not quiet then
            printfn "  Running Shor's algorithm..."

        match solve prob with
        | Ok result ->
            match result.Factors with
            | Some (p, q) ->
                if not quiet then
                    printfn "  SUCCESS: %d = %d x %d" n p q
                    printfn "  Base used (a):    %d" result.Base
                    printfn "  Period found (r): %d" result.Period
                    printfn "  Qubits used:      %d" result.QubitsUsed
                    printfn "  Attempts:         %d/%d" result.Attempts prob.MaxAttempts
                    printfn ""
                    printfn "  SECURITY IMPACT:"
                    printfn "    With factors p=%d and q=%d, an attacker can:" p q
                    printfn "    1. Calculate phi(n) = (p-1)(q-1) = %d" ((p-1)*(q-1))
                    printfn "    2. Derive private key d from public key e"
                    printfn "    3. Decrypt all messages encrypted with this RSA key"
                    printfn ""

                addResult scenario n "factored"
                    (sprintf "%d x %d" p q)
                    result.Base result.Period result.QubitsUsed result.Attempts ""

            | None ->
                if not quiet then
                    printfn "  Period found (r=%d) but did not yield factors" result.Period
                    printfn "  (This happens probabilistically; try again or increase attempts)"
                    printfn ""

                addResult scenario n "period_only"
                    "none" result.Base result.Period result.QubitsUsed result.Attempts
                    "Period found but did not yield factors"

        | Error err ->
            if not quiet then
                printfn "  Execution error: %s" err.Message
                printfn ""

            addResult scenario n "error" "none" 0 0 0 0 err.Message

    | Error err ->
        if not quiet then
            printfn "  Builder error: %s" err.Message
            printfn ""

        addResult scenario n "error" "none" 0 0 0 0 err.Message

// ============================================================================
// Main Execution
// ============================================================================

if not quiet then
    printfn "=== RSA Security Analysis with Shor's Algorithm ==="
    printfn ""
    printfn "BUSINESS SCENARIO:"
    printfn "A security consulting firm needs to assess RSA key strength against"
    printfn "quantum attacks. This demonstrates how quantum computers can factor"
    printfn "RSA moduli and break public-key encryption."
    printfn ""

// --- Scenario 1: User-specified number (or default 15) ---

runFactorization "User Target" numberToFactor precision maxAttempts

// --- Scenario 2: Factor 143 = 11 x 13 (if different from user target) ---

if numberToFactor <> 143 then
    runFactorization "Larger Modulus" 143 precision maxAttempts

// --- Scenario 3: Real-world RSA assessment (informational only) ---

if not quiet then
    printfn "--- Real-World RSA Threat Assessment ---"
    printfn ""
    printfn "  RSA Key Size Analysis:"
    printfn ""
    printfn "  Key Size   Required Qubits   Current Hardware   Status"
    printfn "  --------   ---------------   ----------------   ------"
    printfn "  512-bit    ~1,000 qubits     Feasible soon      DEPRECATED"
    printfn "  1024-bit   ~2,000 qubits     Not yet            WEAK"
    printfn "  2048-bit   ~4,000 qubits     Far from reach     Standard"
    printfn "  4096-bit   ~8,000 qubits     Far from reach     Recommended"
    printfn ""
    printfn "  Current NISQ Hardware:  ~100-1000 noisy qubits (IBM, Google)"
    printfn "  Fault-tolerant target:  ~20 million noisy qubits for RSA-2048"
    printfn "                          (Gidney & Ekera, 2021)"
    printfn ""
    printfn "  RECOMMENDATIONS:"
    printfn "    1. Start transitioning to post-quantum cryptography (NIST standards)"
    printfn "    2. Use 4096-bit RSA keys as interim measure"
    printfn "    3. Monitor quantum computing progress (qubit count, error rates)"
    printfn "    4. Plan for 'Q-Day' when quantum computers can break current crypto"
    printfn ""

// Add assessment row for structured output
results.Add(
    [ "scenario", "RSA-2048 Assessment"
      "number", "0"
      "status", "infeasible"
      "factors", "none"
      "base_used", "0"
      "period", "0"
      "qubits_used", "0"
      "attempts", "0"
      "precision", "0"
      "error", "Requires ~4000 logical qubits; current hardware insufficient" ]
    |> Map.ofList)

// ============================================================================
// Structured Output
// ============================================================================

let resultsList = results |> Seq.toList

match outputPath with
| Some path -> Reporting.writeJson path resultsList
| None -> ()

match csvPath with
| Some path ->
    let header = [ "scenario"; "number"; "status"; "factors"; "base_used"; "period";
                   "qubits_used"; "attempts"; "precision"; "error" ]
    let rows =
        resultsList
        |> List.map (fun m -> header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
| None -> ()

// ============================================================================
// Usage Hints
// ============================================================================

if not quiet && outputPath.IsNone && csvPath.IsNone && argv.Length = 0 then
    printfn ""
    printfn "Hint: Customize this run with CLI options:"
    printfn "  dotnet fsi RSAFactorization.fsx -- --number 21 --precision 6"
    printfn "  dotnet fsi RSAFactorization.fsx -- --number 77 --max-attempts 20"
    printfn "  dotnet fsi RSAFactorization.fsx -- --quiet --output results.json --csv results.csv"
    printfn "  dotnet fsi RSAFactorization.fsx -- --help"
