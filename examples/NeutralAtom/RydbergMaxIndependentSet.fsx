/// Neutral-atom (Rydberg) analog computing — Maximum Independent Set
///
/// A neutral-atom device isn't programmed with gates: you place atoms and drive them with a
/// global laser pulse (Rabi frequency Ω, detuning Δ). Nearby atoms can't both be excited —
/// the *Rydberg blockade* — which makes these machines natively good at Maximum Independent
/// Set (MIS): the largest set of atoms with no two neighbours both excited.
///
/// This library fits the analog paradigm into the unified model by Trotterizing the analog
/// evolution into a gate circuit (drive → RX, detuning → P, van-der-Waals → CP), so a Rydberg
/// program runs on any IQuantumBackend — here the local simulator.
///
/// We solve MIS on a 5-atom graph via the standard adiabatic detuning sweep.
///
/// Run with: dotnet fsi RydbergMaxIndependentSet.fsx

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms.NeutralAtom

let backend = LocalBackend.LocalBackend() :> IQuantumBackend

// A 5-atom "bowtie"/path arrangement on a line: neighbours (distance 1) blockade each other,
// next-nearest (distance 2) do not. The maximum independent set is {0, 2, 4} = "10101".
let register =
    [ { X = 0.0; Y = 0.0 }
      { X = 1.0; Y = 0.0 }
      { X = 2.0; Y = 0.0 }
      { X = 3.0; Y = 0.0 }
      { X = 4.0; Y = 0.0 } ]

[<Literal>]
let omega = 1.0
let program = maximumIndependentSetProgram register 30.0 omega 4.0 16.0

printfn "Neutral-atom MIS — 5 atoms on a line (neighbours blockade)\n"
printfn "Blockade radius (C₆=30, Ω=%.1f): %.2f  (so r=1 blockades, r=2 does not)\n" omega (blockadeRadius 30.0 omega)

match simulate backend program 160 6000 with
| Error e -> eprintfn "simulation failed: %s" e.Message; exit 1
| Ok histogram ->
    printfn "Most likely measured configurations (1 = atom excited / in the set):"
    histogram
    |> Map.toList
    |> List.sortByDescending snd
    |> List.truncate 5
    |> List.iter (fun (bits, count) ->
        let size = bits |> Seq.filter ((=) '1') |> Seq.length
        let ok = if isIndependentSet program omega bits then "independent set" else "INVALID (blockade violated)"
        printfn "  %s : %5.1f%%  (%d excited, %s)" bits (100.0 * float count / 6000.0) size ok)

    let best = histogram |> Map.toList |> List.maxBy snd |> fst
    printfn "\nMaximum independent set found: %s (%d atoms)" best (best |> Seq.filter ((=) '1') |> Seq.length)
