/// Quantum Fourier Transform (QFT) — educational example
///
/// The QFT is the quantum analogue of the discrete Fourier transform and the
/// engine behind phase estimation, Shor's algorithm and more.
///
/// This example:
///   1. Applies the QFT to |000⟩ and shows the result is a *uniform* superposition
///      over all basis states (the Fourier transform of a delta is flat).
///   2. Applies the inverse QFT to undo it, recovering |000⟩.
///
/// Executed on the unified IQuantumBackend (local simulator here). Swap in any
/// cloud backend (IonQ / Rigetti / Quantinuum) to run the same code on hardware.
///
/// Run with: dotnet fsi QuantumFourierTransform.fsx

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

let backend = LocalBackend.LocalBackend() :> IQuantumBackend
let numQubits = 3
let shots = 4000

let histogram (state) =
    UnifiedBackend.measureState state shots
    |> Array.map (fun bits -> bits |> Array.map string |> String.concat "")
    |> Array.countBy id
    |> Array.sortByDescending snd

printfn "Quantum Fourier Transform (%d qubits)\n" numQubits

// 1. Forward QFT on |000⟩  ->  uniform superposition
match QFT.execute numQubits backend QFT.defaultConfig with
| Error err -> eprintfn "QFT failed: %s" err.Message; exit 1
| Ok qft ->
    printfn "Forward QFT applied: %d gates, %.2f ms" qft.GateCount qft.ExecutionTimeMs
    printfn "Measured distribution (expect ~uniform, ~%.1f%% each):" (100.0 / float (1 <<< numQubits))
    for (bitstring, count) in histogram qft.FinalState do
        printfn "  |%s⟩ : %5.1f%%" bitstring (100.0 * float count / float shots)

    // 2. Inverse QFT undoes it, recovering |000⟩
    match QFT.executeOnState qft.FinalState backend { QFT.defaultConfig with Inverse = true } with
    | Error err -> eprintfn "Inverse QFT failed: %s" err.Message; exit 1
    | Ok inv ->
        printfn "\nInverse QFT applied — state should collapse back to |000⟩:"
        for (bitstring, count) in histogram inv.FinalState do
            printfn "  |%s⟩ : %5.1f%%" bitstring (100.0 * float count / float shots)
