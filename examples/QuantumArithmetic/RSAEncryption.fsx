// RSA Encryption Example - Quantum Arithmetic for Cryptography
// Demonstrates modular exponentiation using quantum circuits (QFT-based)

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum.Examples.Common
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumArithmeticOps
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "RSAEncryption.fsx"
    "Toy RSA encryption demo using quantum modular exponentiation (m^e mod n)."
    [ { Name = "message";  Description = "Plaintext integer to encrypt (0 < m < n)"; Default = Some "5" }
      { Name = "p";        Description = "First RSA prime";                          Default = Some "3" }
      { Name = "q";        Description = "Second RSA prime";                         Default = Some "11" }
      { Name = "e";        Description = "Public exponent (must be coprime to phi)"; Default = Some "3" }
      { Name = "qubits";   Description = "Qubits for quantum circuit";              Default = Some "8" }
      { Name = "output";   Description = "Write results to JSON file";              Default = None }
      { Name = "csv";      Description = "Write results to CSV file";               Default = None }
      { Name = "quiet";    Description = "Suppress console output";                 Default = None } ]
    args

let quiet     = Cli.hasFlag "quiet" args
let pr fmt    = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

let message   = Cli.getIntOr "message" 5 args
let p         = Cli.getIntOr "p" 3 args
let q         = Cli.getIntOr "q" 11 args
let pubExp    = Cli.getIntOr "e" 3 args
let nQubits   = Cli.getIntOr "qubits" 8 args
let outputPath = Cli.tryGet "output" args
let csvPath    = Cli.tryGet "csv" args

// ---------------------------------------------------------------------------
// Rule 1: Explicit IQuantumBackend
// ---------------------------------------------------------------------------

let quantumBackend = LocalBackend() :> IQuantumBackend

// ---------------------------------------------------------------------------
// RSA Key Setup
// ---------------------------------------------------------------------------

let n   = p * q
let phi = (p - 1) * (q - 1)

pr "=== RSA Encryption Demo (Toy Example) ==="
pr ""
pr "--- Step 1: RSA Key Setup ---"
pr "RSA Modulus (n):        %d (public)" n
pr "Public Exponent (e):    %d (public)" pubExp
pr "Factorization (p, q):   %d, %d (private - must stay secret!)" p q
pr ""

// ---------------------------------------------------------------------------
// Encrypt using quantum arithmetic
// ---------------------------------------------------------------------------

pr "--- Step 2: Encrypt Message ---"
pr "Original Message (m):   %d" message
pr "Encryption Formula:     c = m^e mod n"
pr "                        c = %d^%d mod %d" message pubExp n
pr ""

let encryptOperation = quantumArithmetic {
    operands message pubExp
    operation ModularExponentiate
    modulus n
    qubits nQubits
    backend quantumBackend
}

let mutable jsonResults : Map<string, string> list = []
let mutable csvRows : string list list = []

pr "Executing quantum circuit..."

match encryptOperation with
| Ok op ->
    match execute op with
    | Ok result ->
        let ciphertext = result.Value

        pr "[OK] Encrypted Message (c): %d" ciphertext
        pr ""
        pr "CIRCUIT STATISTICS:"
        pr "  Qubits Used:     %d" result.QubitsUsed
        pr "  Gate Count:      %d" result.GateCount
        pr "  Circuit Depth:   %d" result.CircuitDepth
        pr ""

        // ------------------------------------------------------------------
        // Decrypt (classical) for verification
        // ------------------------------------------------------------------

        pr "--- Step 3: Decrypt Message (Classical) ---"

        // Private exponent d where (e * d) mod phi = 1
        // Simple brute-force search (toy sizes only)
        let d =
            seq { 1 .. phi - 1 }
            |> Seq.find (fun d -> (pubExp * d) % phi = 1)

        pr "Private Exponent (d):   %d (secret)" d
        pr "Decryption Formula:     m = c^d mod n"
        pr "                        m = %d^%d mod %d" ciphertext d n

        // Idiomatic modular exponentiation via fold
        let decrypted =
            List.init d (fun _ -> ciphertext)
            |> List.fold (fun acc x -> (acc * x) % n) 1

        pr "Decrypted Message:      %d" decrypted
        pr ""

        let success = decrypted = message
        if success then
            pr "[OK] SUCCESS: Decryption matches original message!"
        else
            pr "[FAIL] ERROR: Decryption failed!"

        pr ""
        pr "=== Key Takeaways ==="
        pr "  * RSA security relies on difficulty of factoring n = p * q"
        pr "  * Quantum computers (Shor's algorithm) can break RSA efficiently"
        pr "  * This example uses quantum circuits for the encryption operation m^e mod n"
        pr "  * Real RSA uses 2048+ bit keys (requires ~4096+ qubits)"
        pr "  * Current NISQ hardware limited to ~100 qubits, so only toy examples work"

        // Collect results
        let row = Map.ofList [
            "message",     string message
            "p",           string p
            "q",           string q
            "n",           string n
            "e",           string pubExp
            "d",           string d
            "ciphertext",  string ciphertext
            "decrypted",   string decrypted
            "match",       string success
            "qubits_used", string result.QubitsUsed
            "gate_count",  string result.GateCount
            "circuit_depth", string result.CircuitDepth
        ]
        jsonResults <- [ row ]
        csvRows <- [ [ string message; string p; string q; string n
                       string pubExp; string d; string ciphertext
                       string decrypted; string success
                       string result.QubitsUsed; string result.GateCount
                       string result.CircuitDepth ] ]

    | Error err ->
        pr "[FAIL] Execution Error: %s" err.Message

| Error err ->
    pr "[FAIL] Builder Error: %s" err.Message

// ---------------------------------------------------------------------------
// Structured output
// ---------------------------------------------------------------------------

outputPath |> Option.iter (fun path -> Reporting.writeJson path jsonResults)
csvPath    |> Option.iter (fun path ->
    Reporting.writeCsv path
        [ "message"; "p"; "q"; "n"; "e"; "d"; "ciphertext"; "decrypted";
          "match"; "qubits_used"; "gate_count"; "circuit_depth" ]
        csvRows)

// ---------------------------------------------------------------------------
// Usage hints
// ---------------------------------------------------------------------------

if argv.Length = 0 && outputPath.IsNone && csvPath.IsNone then
    pr ""
    pr "Tip: run with arguments for custom parameters:"
    pr "  dotnet fsi RSAEncryption.fsx -- --message 7 --p 3 --q 11 --e 3"
    pr "  dotnet fsi RSAEncryption.fsx -- --quiet --output results.json --csv results.csv"
    pr "  dotnet fsi RSAEncryption.fsx -- --help"
