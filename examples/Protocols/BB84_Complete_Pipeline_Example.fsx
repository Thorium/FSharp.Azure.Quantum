/// BB84 Complete QKD Pipeline Example
///
/// Demonstrates the full Quantum Key Distribution pipeline:
/// 1. BB84 quantum key exchange (basis reconciliation, sifting)
/// 2. Eavesdrop detection via QBER sampling
/// 3. Error correction (Cascade protocol)
/// 4. Privacy amplification (hash-based key compression)
///
/// **Textbook References**:
/// - Bennett & Brassard, "Quantum cryptography: Public key distribution and
///   coin tossing", Proceedings of IEEE ICCSSP, pp. 175-179 (1984).
/// - Nielsen & Chuang, "Quantum Computation and Quantum Information" - Section 12.6
/// - Scarani et al., "The security of practical quantum key distribution",
///   Rev. Mod. Phys. 81, 1301 (2009).
///
/// **Production Use Cases**:
/// - Secure communication links (government, financial)
/// - Quantum-safe key exchange for post-quantum networks
/// - Satellite-based QKD (Micius, QKDSat)
///
/// **Real-World Deployments**:
/// - ID Quantique: Commercial QKD systems (Geneva banking network, 2007)
/// - Toshiba: 600 km fiber QKD (2021)
/// - Micius satellite: Intercontinental QKD (Beijing-Vienna, 2017)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open FSharp.Azure.Quantum.Algorithms.QuantumKeyDistribution
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "BB84_Complete_Pipeline_Example.fsx"
    "Run the complete BB84 QKD pipeline: key exchange, error correction, privacy amplification."
    [ { Cli.OptionSpec.Name = "keylength"
        Description = "Initial key length in qubits"
        Default = Some "256" }
      { Cli.OptionSpec.Name = "security"
        Description = "Security parameter in bits"
        Default = Some "128" }
      { Cli.OptionSpec.Name = "error-correction"
        Description = "Enable error correction (true/false)"
        Default = Some "true" }
      { Cli.OptionSpec.Name = "seed"
        Description = "Random seed for reproducibility"
        Default = None }
      { Cli.OptionSpec.Name = "output"
        Description = "Write results to JSON file"
        Default = None }
      { Cli.OptionSpec.Name = "csv"
        Description = "Write results to CSV file"
        Default = None }
      { Cli.OptionSpec.Name = "quiet"
        Description = "Suppress console output"
        Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

let keyLength = Cli.getIntOr "keylength" 256 args
let securityParam = Cli.getIntOr "security" 128 args
let doErrorCorrection = (Cli.getOr "error-correction" "true" args).ToLowerInvariant() = "true"
let seed =
    Cli.tryGet "seed" args
    |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ---------------------------------------------------------------------------
// Quantum backend (Rule 1: IQuantumBackend dependency)
// ---------------------------------------------------------------------------

let quantumBackend = LocalBackend() :> IQuantumBackend

// ---------------------------------------------------------------------------
// Run complete QKD pipeline
// ---------------------------------------------------------------------------

pr "=== BB84 Complete QKD Pipeline ==="
pr ""
pr "Parameters:"
pr "  Key length:        %d qubits" keyLength
pr "  Error correction:  %b" doErrorCorrection
pr "  Security parameter:%d bits" securityParam
pr "  Seed:              %s" (seed |> Option.map string |> Option.defaultValue "random")
pr ""

let result = runCompleteQKD keyLength quantumBackend doErrorCorrection securityParam seed

match result with
| Error err ->
    pr "[FAIL] QKD pipeline failed: %A" err

| Ok qkd ->
    // --- Stage 1: BB84 ---
    let bb84 = qkd.BB84Result
    pr "--- Stage 1: BB84 Quantum Key Exchange ---"
    pr "  Initial qubits sent:  %d" bb84.InitialKeyLength
    pr "  Sifted key length:    %d bits" bb84.SiftedKey.Length
    pr "  QBER:                 %.2f%%" (bb84.EavesdropCheck.ErrorRate * 100.0)
    pr "  Eavesdrop detected:   %s" (if bb84.EavesdropCheck.EavesdropDetected then "YES - abort!" else "No")
    pr "  Sifting efficiency:   %.1f%%" (bb84.OverallEfficiency * 100.0)
    pr ""

    // --- Stage 2: Error Correction ---
    pr "--- Stage 2: Error Correction ---"
    match qkd.ErrorCorrection with
    | Some ec ->
        pr "  Errors detected:      %d" ec.ErrorsDetected
        pr "  Errors corrected:     %d" ec.ErrorsCorrected
        pr "  Information leaked:   %.2f bits" ec.InformationLeaked
        pr "  Status:               %s" (if ec.Success then "[OK]" else "[FAIL]")
    | None ->
        pr "  Skipped"
    pr ""

    // --- Stage 3: Privacy Amplification ---
    let pa = qkd.PrivacyAmplification
    pr "--- Stage 3: Privacy Amplification ---"
    pr "  Hash function:        %s" pa.HashFunction
    pr "  Input length:         %d bits" pa.OriginalLength
    pr "  Output length:        %d bits" pa.FinalLength
    pr "  Compression ratio:    %.1f%%" (pa.CompressionRatio * 100.0)
    pr "  Security level:       %d bits" pa.SecurityParameter
    pr ""

    // --- Final Result ---
    pr "--- Final Result ---"
    pr "  Final key length:     %d bits" qkd.FinalKeyLength
    pr "  End-to-end efficiency:%.1f%%" (qkd.EndToEndEfficiency * 100.0)
    pr "  Info leaked to Eve:   %.2f bits" qkd.TotalInformationLeaked
    pr "  Security level:       %d bits" qkd.SecurityLevel
    pr "  Status:               %s" (if qkd.Success then "[OK] Secure key established" else "[FAIL] Key not secure")
    pr ""

    // --- JSON output ---
    outputPath |> Option.iter (fun path ->
        let payload =
            {| keyLength = keyLength
               siftedKeyLength = bb84.SiftedKey.Length
               qber = bb84.EavesdropCheck.ErrorRate
               eavesdropDetected = bb84.EavesdropCheck.EavesdropDetected
               errorCorrectionApplied = doErrorCorrection
               errorsDetected = qkd.ErrorCorrection |> Option.map (fun ec -> ec.ErrorsDetected) |> Option.defaultValue 0
               errorsCorrected = qkd.ErrorCorrection |> Option.map (fun ec -> ec.ErrorsCorrected) |> Option.defaultValue 0
               privacyAmplificationInput = pa.OriginalLength
               privacyAmplificationOutput = pa.FinalLength
               compressionRatio = pa.CompressionRatio
               finalKeyLength = qkd.FinalKeyLength
               endToEndEfficiency = qkd.EndToEndEfficiency
               infoLeaked = qkd.TotalInformationLeaked
               securityLevel = qkd.SecurityLevel
               success = qkd.Success |}
        Reporting.writeJson path payload
        pr "JSON written to %s" path)

    // --- CSV output ---
    csvPath |> Option.iter (fun path ->
        let header =
            [ "keyLength"; "siftedKeyLength"; "qber"; "eavesdropDetected"
              "errorsDetected"; "errorsCorrected"; "paInput"; "paOutput"
              "finalKeyLength"; "efficiency"; "infoLeaked"; "securityLevel"; "success" ]
        let row =
            [ string keyLength
              string bb84.SiftedKey.Length
              sprintf "%.4f" bb84.EavesdropCheck.ErrorRate
              string bb84.EavesdropCheck.EavesdropDetected
              string (qkd.ErrorCorrection |> Option.map (fun ec -> ec.ErrorsDetected) |> Option.defaultValue 0)
              string (qkd.ErrorCorrection |> Option.map (fun ec -> ec.ErrorsCorrected) |> Option.defaultValue 0)
              string pa.OriginalLength
              string pa.FinalLength
              string qkd.FinalKeyLength
              sprintf "%.4f" qkd.EndToEndEfficiency
              sprintf "%.2f" qkd.TotalInformationLeaked
              string qkd.SecurityLevel
              string qkd.Success ]
        Reporting.writeCsv path header [ row ]
        pr "CSV written to %s" path)

// ---------------------------------------------------------------------------
// Usage hints
// ---------------------------------------------------------------------------

if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr ""
    pr "Tip: run with arguments for custom parameters:"
    pr "  dotnet fsi BB84_Complete_Pipeline_Example.fsx -- --keylength 512 --security 256"
    pr "  dotnet fsi BB84_Complete_Pipeline_Example.fsx -- --quiet --output results.json --csv results.csv"
    pr "  dotnet fsi BB84_Complete_Pipeline_Example.fsx -- --help"
