/// Density-matrix noisy simulation — see how hardware noise degrades a circuit
///
/// The `NoisyLocalBackend` evolves a full density matrix ρ and applies a depolarizing
/// channel after every gate, so it models the *mixed* states real hardware produces. It is
/// a drop-in `IQuantumBackend`, so the same `Primitives.sample` reads its (noisy) statistics.
///
/// Here a Bell state should only ever measure 00 or 11. As the depolarizing probability
/// rises, probability leaks into the forbidden 01/10 outcomes — a direct picture of how
/// noise erodes entanglement.
///
/// Run with: dotnet fsi NoisyDensityMatrix.fsx

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.DensityMatrixSimulator

let bell =
    CircuitBuilder.empty 2
    |> CircuitBuilder.addGate (CircuitBuilder.H 0)
    |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 1))

let shots = 4000

let show (label: string) (config: NoiseConfig) =
    let backend = NoisyLocalBackend(config) :> IQuantumBackend
    match Primitives.sample backend bell shots with
    | Ok histogram ->
        let pct k = 100.0 * float (histogram |> Map.tryFind k |> Option.defaultValue 0) / float shots
        printfn "%-14s |00⟩ %4.1f%%  |01⟩ %4.1f%%  |10⟩ %4.1f%%  |11⟩ %4.1f%%"
            label (pct "00") (pct "01") (pct "10") (pct "11")
    | Error e -> eprintfn "%s failed: %s" label e.Message

printfn "Bell state under a depolarizing channel (density-matrix simulation)\n"
show "noiseless" noiseless
show "depol 2%" (depolarizing 0.02 0.02)
show "depol 5%" (depolarizing 0.05 0.05)
show "depol 10%" (depolarizing 0.10 0.10)
show "depol 20%" (depolarizing 0.20 0.20)
printfn "\n01/10 are forbidden for an ideal Bell state; their growth tracks the noise level."
