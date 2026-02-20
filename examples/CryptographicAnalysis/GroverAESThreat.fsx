// ==============================================================================
// Grover's Algorithm Threat to Symmetric Cryptography (AES)
// ==============================================================================
// Compares classical vs quantum security of symmetric ciphers using Grover's
// quadratic speedup. For each cipher, computes quantum security bits (key_bits/2),
// time estimates, and resource requirements (Grassl et al. 2016).
//
// Includes a live Grover search demo via the library's GroverSearch module to
// demonstrate the oracle + amplitude-amplification infrastructure.
//
// Usage:
//   dotnet fsi GroverAESThreat.fsx
//   dotnet fsi GroverAESThreat.fsx -- --help
//   dotnet fsi GroverAESThreat.fsx -- --ciphers aes-128,aes-256
//   dotnet fsi GroverAESThreat.fsx -- --input ciphers.csv
//   dotnet fsi GroverAESThreat.fsx -- --target 5 --qubits 3
//   dotnet fsi GroverAESThreat.fsx -- --quiet --output results.json --csv results.csv
//
// References:
//   [1] Grover, "A fast quantum mechanical algorithm for database search",
//       STOC 1996. https://doi.org/10.1145/237814.237866
//   [2] Grassl et al., "Applying Grover's algorithm to AES: quantum resource
//       estimates", PQCrypto 2016. https://doi.org/10.1007/978-3-319-29360-8_3
//   [3] Wikipedia: Grover's_algorithm
//       https://en.wikipedia.org/wiki/Grover%27s_algorithm
// ==============================================================================

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

// ==============================================================================
// CLI
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "GroverAESThreat.fsx"
    "Grover's algorithm threat analysis for symmetric ciphers (AES, ChaCha20, DES, etc.)."
    [ { Cli.OptionSpec.Name = "input"; Description = "CSV file with custom cipher definitions"; Default = Some "built-in presets" }
      { Cli.OptionSpec.Name = "ciphers"; Description = "Comma-separated cipher names to analyse (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "target"; Description = "Target value for Grover demo search"; Default = Some "3" }
      { Cli.OptionSpec.Name = "qubits"; Description = "Number of qubits for Grover demo search"; Default = Some "2" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputFile = Cli.tryGet "input" args
let cipherFilter = Cli.getCommaSeparated "ciphers" args
let targetValue = Cli.getIntOr "target" 3 args
let numQubits = Cli.getIntOr "qubits" 2 args

// ==============================================================================
// TYPES
// ==============================================================================

/// Symmetric cipher configuration.
type CipherInfo =
    { Name: string
      KeyBits: int
      BlockBits: int }

/// Security analysis result for one cipher.
type CipherResult =
    { Cipher: CipherInfo
      ClassicalSecurityBits: float
      QuantumSecurityBits: float
      QuantumSafe: bool
      GroverIterationsLog2: float
      ResourceEstimate: string
      TimeYears: float
      Recommendation: string
      HasQuantumFailure: bool }

// ==============================================================================
// PHYSICAL CONSTANTS
// ==============================================================================

let opsPerSecond = 1e9  // optimistic: 1 billion quantum ops/sec

// ==============================================================================
// SECURITY ANALYSIS FUNCTIONS (Pure)
// ==============================================================================

/// Quantum security bits = classical / 2  (Grover's quadratic speedup).
let quantumSecurityBits (classicalBits: int) : float =
    float classicalBits / 2.0

/// Estimate Grover iterations (log2) for a key space of keyBits.
let groverIterationsLog2 (keyBits: int) : float =
    // Optimal iterations ~ (pi/4) * sqrt(2^n)
    // log2 of that is roughly n/2 + constant
    let n = float keyBits
    n / 2.0 + Math.Log(Math.PI / 4.0, 2.0)

/// Estimate quantum resources based on Grassl et al. (2016).
let estimateResources (keyBits: int) : string =
    match keyBits with
    | 56  -> "~1000 qubits, 2^48 T-gates"
    | 112 -> "~2200 qubits, 2^76 T-gates"
    | 128 -> "2953 qubits, 2^86 T-gates"
    | 192 -> "4449 qubits, 2^118 T-gates"
    | 256 -> "6681 qubits, 2^151 T-gates"
    | _   -> sprintf "~%d qubits, 2^%d T-gates" (keyBits * 20) (keyBits / 2 + 20)

/// Estimate attack time in years at opsPerSecond.
let estimateTimeYears (keyBits: int) : float =
    let qBits = quantumSecurityBits keyBits
    let quantumOps = Math.Pow(2.0, qBits)
    let timeSec = quantumOps / opsPerSecond
    timeSec / (365.25 * 24.0 * 3600.0)

/// Build a recommendation string.
let recommend (cipher: CipherInfo) (qSafe: bool) (qBits: float) : string =
    if qSafe then
        sprintf "%s is quantum-safe (%.0f-bit quantum security)" cipher.Name qBits
    elif qBits >= 64.0 then
        sprintf "%s has reduced security (%.0f-bit quantum). Consider %d-bit keys."
            cipher.Name qBits (cipher.KeyBits * 2)
    else
        sprintf "%s is NOT quantum-safe (%.0f-bit quantum). UPGRADE IMMEDIATELY."
            cipher.Name qBits

/// Analyse one cipher.
let analyseCipher (cipher: CipherInfo) : CipherResult =
    let classical = float cipher.KeyBits
    let quantum = quantumSecurityBits cipher.KeyBits
    let safe = quantum >= 128.0
    { Cipher = cipher
      ClassicalSecurityBits = classical
      QuantumSecurityBits = quantum
      QuantumSafe = safe
      GroverIterationsLog2 = groverIterationsLog2 cipher.KeyBits
      ResourceEstimate = estimateResources cipher.KeyBits
      TimeYears = estimateTimeYears cipher.KeyBits
      Recommendation = recommend cipher safe quantum
      HasQuantumFailure = false }

// ==============================================================================
// BUILT-IN CIPHER PRESETS
// ==============================================================================

let private builtinPresets : Map<string, CipherInfo> =
    [ { Name = "DES";      KeyBits = 56;  BlockBits = 64 }
      { Name = "3DES";     KeyBits = 112; BlockBits = 64 }
      { Name = "AES-128";  KeyBits = 128; BlockBits = 128 }
      { Name = "AES-192";  KeyBits = 192; BlockBits = 128 }
      { Name = "AES-256";  KeyBits = 256; BlockBits = 128 }
      { Name = "ChaCha20"; KeyBits = 256; BlockBits = 512 } ]
    |> List.map (fun c -> c.Name.ToLowerInvariant(), c)
    |> Map.ofList

let private presetNames =
    builtinPresets |> Map.toList |> List.map fst |> String.concat ", "

// ==============================================================================
// CSV INPUT PARSING
// ==============================================================================

/// Load ciphers from a CSV file.
/// Expected columns: name, key_bits, block_bits
/// OR: name, preset (to reference a built-in preset)
let private loadCiphersFromCsv (path: string) : CipherInfo list =
    let rows, errors = Data.readCsvWithHeaderWithErrors path
    if not (List.isEmpty errors) && not quiet then
        for err in errors do
            eprintfn "  Warning (CSV): %s" err

    rows
    |> List.choose (fun row ->
        let get key = row.Values |> Map.tryFind key
        let name = get "name" |> Option.defaultValue "Unknown"
        match get "preset" with
        | Some presetKey ->
            let key = presetKey.Trim().ToLowerInvariant()
            match builtinPresets |> Map.tryFind key with
            | Some c -> Some { c with Name = name }
            | None ->
                if not quiet then
                    eprintfn "  Warning: unknown preset '%s' (available: %s)" presetKey presetNames
                None
        | None ->
            match get "key_bits" with
            | Some kbStr ->
                match Int32.TryParse kbStr with
                | true, kb ->
                    let bb =
                        get "block_bits"
                        |> Option.bind (fun s -> match Int32.TryParse s with true, v -> Some v | _ -> None)
                        |> Option.defaultValue 128
                    Some { Name = name; KeyBits = kb; BlockBits = bb }
                | _ ->
                    if not quiet then eprintfn "  Warning: invalid key_bits '%s' for '%s'" kbStr name
                    None
            | None ->
                if not quiet then eprintfn "  Warning: row '%s' missing 'key_bits' or 'preset'" name
                None)

// ==============================================================================
// CIPHER SELECTION
// ==============================================================================

let ciphers : CipherInfo list =
    let allCiphers =
        match inputFile with
        | Some path ->
            let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
            if not quiet then printfn "Loading ciphers from: %s" resolved
            loadCiphersFromCsv resolved
        | None ->
            builtinPresets |> Map.toList |> List.map snd

    match cipherFilter with
    | [] -> allCiphers
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        allCiphers
        |> List.filter (fun c ->
            let key = c.Name.ToLowerInvariant()
            filterSet |> Set.exists (fun f -> key.Contains f))

if List.isEmpty ciphers then
    eprintfn "Error: No ciphers selected. Available presets: %s" presetNames
    exit 1

// ==============================================================================
// QUANTUM BACKEND (Rule 1)
// ==============================================================================

let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Grover's Algorithm Threat to Symmetric Cryptography"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:  %s" backend.Name
    printfn "  Ciphers:  %d" ciphers.Length
    printfn ""

// ==============================================================================
// ANALYSE ALL CIPHERS
// ==============================================================================

let results = ciphers |> List.map analyseCipher

// Sort: least quantum-safe first (lowest quantum security bits).
let ranked =
    results
    |> List.sortBy (fun r -> r.QuantumSecurityBits)

// ==============================================================================
// GROVER DEMO
// ==============================================================================

/// Run a small Grover search to demonstrate the library's oracle/search infra.
/// Returns (target, found, iterations, successProb, qubitsUsed, hasFailure).
let runGroverDemo (target: int) (qubits: int) : int * string * int * float * int * bool =
    let searchSpace = 1 <<< qubits
    if target >= searchSpace then
        (target, "out_of_range", 0, 0.0, qubits, false)
    else
        match Oracle.forValue target qubits with
        | Error err ->
            if not quiet then eprintfn "  Grover oracle error: %s" err.Message
            (target, "oracle_error", 0, 0.0, qubits, true)
        | Ok oracle ->
            let optIters = int (Math.Round((Math.PI / 4.0) * Math.Sqrt(float searchSpace)))
            let config = { Grover.defaultConfig with Iterations = Some (max 1 optIters) }
            match Grover.search oracle backend config with
            | Error err ->
                if not quiet then eprintfn "  Grover search error: %s" err.Message
                (target, "search_error", 0, 0.0, qubits, true)
            | Ok result ->
                let foundStr =
                    if result.Solutions.IsEmpty then "(none)"
                    else result.Solutions |> List.map string |> String.concat ","
                (target, foundStr, result.Iterations, result.SuccessProbability, qubits, false)

let (demoTarget, demoFound, demoIters, demoProb, demoQubits, demoFailed) =
    runGroverDemo targetValue numQubits

if not quiet then
    let searchSpace = 1 <<< numQubits
    printfn "Grover Demo: search %d-item space (%d qubits) for target=%d"
        searchSpace numQubits targetValue
    printfn "  Found:       %s" demoFound
    printfn "  Iterations:  %d" demoIters
    printfn "  Success:     %.1f%%" (demoProb * 100.0)
    printfn ""

// ==============================================================================
// RANKED COMPARISON TABLE
// ==============================================================================

let printTable () =
    printfn "=================================================================="
    printfn "  Ranked Symmetric Ciphers (by quantum security)"
    printfn "=================================================================="
    printfn ""
    printfn "  %-4s  %-12s  %4s  %8s  %8s  %8s  %-6s  %s"
        "#" "Cipher" "Key" "Classic" "Quantum" "Time(yr)" "Safe?" "Resource Estimate"
    printfn "  %s" (String('=', 95))

    ranked
    |> List.iteri (fun i r ->
        let safeStr = if r.QuantumSafe then "Yes" else "No"
        let timeStr =
            if r.TimeYears > 1e15 then sprintf "%.0e" r.TimeYears
            elif r.TimeYears > 1e9 then sprintf "%.0fB" (r.TimeYears / 1e9)
            elif r.TimeYears > 1e6 then sprintf "%.0fM" (r.TimeYears / 1e6)
            else sprintf "%.1f" r.TimeYears
        printfn "  %-4d  %-12s  %4d  %8.0f  %8.0f  %8s  %-6s  %s"
            (i + 1) r.Cipher.Name r.Cipher.KeyBits
            r.ClassicalSecurityBits r.QuantumSecurityBits
            timeStr safeStr r.ResourceEstimate)

    printfn ""

// Always print the table (even in --quiet mode).
printTable ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    let safe = ranked |> List.filter (fun r -> r.QuantumSafe)
    let unsafe = ranked |> List.filter (fun r -> not r.QuantumSafe)
    printfn "  Quantum-safe ciphers:   %d  (%s)"
        safe.Length
        (safe |> List.map (fun r -> r.Cipher.Name) |> String.concat ", "
         |> fun s -> if s = "" then "none" else s)
    printfn "  Vulnerable ciphers:     %d  (%s)"
        unsafe.Length
        (unsafe |> List.map (fun r -> r.Cipher.Name) |> String.concat ", "
         |> fun s -> if s = "" then "none" else s)
    printfn "  128-bit quantum security is the post-quantum minimum."
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    ranked
    |> List.mapi (fun i r ->
        [ "rank", string (i + 1)
          "cipher", r.Cipher.Name
          "key_bits", string r.Cipher.KeyBits
          "block_bits", string r.Cipher.BlockBits
          "classical_security_bits", sprintf "%.0f" r.ClassicalSecurityBits
          "quantum_security_bits", sprintf "%.0f" r.QuantumSecurityBits
          "quantum_safe", string r.QuantumSafe
          "grover_iterations_log2", sprintf "%.1f" r.GroverIterationsLog2
          "resource_estimate", r.ResourceEstimate
          "attack_time_years", sprintf "%.2e" r.TimeYears
          "recommendation", r.Recommendation
          "grover_demo_target", string demoTarget
          "grover_demo_found", demoFound
          "grover_demo_iterations", string demoIters
          "grover_demo_success_prob", sprintf "%.4f" demoProb
          "has_quantum_failure", string (r.HasQuantumFailure || demoFailed) ]
        |> Map.ofList)

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path resultMaps
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header =
        [ "rank"; "cipher"; "key_bits"; "block_bits"
          "classical_security_bits"; "quantum_security_bits"; "quantum_safe"
          "grover_iterations_log2"; "resource_estimate"; "attack_time_years"
          "recommendation"; "grover_demo_target"; "grover_demo_found"
          "grover_demo_iterations"; "grover_demo_success_prob"; "has_quantum_failure" ]
    let rows =
        resultMaps
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Run with --help to see all options."
    printfn "     --ciphers aes-128,aes-256       Analyse specific ciphers"
    printfn "     --input ciphers.csv              Load custom ciphers from CSV"
    printfn "     --csv results.csv                Export ranked table as CSV"
    printfn ""
