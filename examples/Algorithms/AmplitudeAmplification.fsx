/// Amplitude Amplification — educational example
///
/// Amplitude amplification is the generalisation of Grover's search: given a
/// state-preparation `A` and an oracle that marks "good" states, it boosts the
/// probability of measuring a good state quadratically faster than classical
/// sampling. Grover's algorithm is the special case where `A = H^⊗n` (uniform
/// superposition).
///
/// This example amplifies a single marked basis state (value 5 = |101⟩) in a
/// 3-qubit space, starting from the uniform superposition, and shows the marked
/// state's probability rising from 1/8 (12.5%) toward ~100%.
///
/// Executed on the unified IQuantumBackend (local simulator here). Swap in any
/// cloud backend to run the same code on hardware.
///
/// Run with: dotnet fsi AmplitudeAmplification.fsx

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.Backends

let numQubits = 3
let markedValue = 5          // |101⟩
let searchSpace = 1 <<< numQubits
let shots = 4000

// State preparation A = H^⊗n (uniform superposition) as a gate circuit.
let uniformPrep =
    let empty = CircuitBuilder.empty numQubits
    [ 0 .. numQubits - 1 ]
    |> List.map CircuitBuilder.H
    |> List.fold (fun c g -> CircuitBuilder.addGate g c) empty

let backend = LocalBackend.LocalBackend() :> IQuantumBackend

printfn "Amplitude Amplification — boosting the marked state |101⟩ (value %d)\n" markedValue

match Oracle.forValue markedValue numQubits with
| Error err -> eprintfn "Oracle build failed: %s" err.Message; exit 1
| Ok oracle ->
    // Optimal number of amplification rounds for one marked item in 8 states.
    let iterations = AmplitudeAmplification.optimalIterations searchSpace 1 (1.0 / float searchSpace)
    printfn "Search space: %d states, marked: 1, optimal iterations: %d\n" searchSpace iterations

    let intent : AmplitudeAmplification.Unified.AmplitudeAmplificationIntent =
        { NumQubits = numQubits
          StatePreparation = uniformPrep
          Oracle = oracle
          Iterations = iterations
          Exactness = AmplitudeAmplification.Unified.Exact }

    match AmplitudeAmplification.Unified.execute backend intent with
    | Error err -> eprintfn "Amplification failed: %s" err.Message; exit 1
    | Ok finalState ->
        let hist =
            UnifiedBackend.measureState finalState shots
            |> Array.map (fun bits -> bits |> Array.map string |> String.concat "")
            |> Array.countBy id
            |> Array.sortByDescending snd
        printfn "Measured distribution after amplification:"
        for (bitstring, count) in hist do
            printfn "  |%s⟩ : %5.1f%%" bitstring (100.0 * float count / float shots)
        printfn "\nStarting probability of the marked state was 1/%d = %.1f%%; amplification concentrates it."
            searchSpace (100.0 / float searchSpace)
