namespace FSharp.Azure.Quantum.Tests

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Algorithms.TrotterSuzuki
open FSharp.Azure.Quantum.Algorithms.NeutralAtom

/// Tests for the neutral-atom (Rydberg) analog mode.
module NeutralAtomTests =

    let private backend () : IQuantumBackend =
        LocalBackend.LocalBackend() :> IQuantumBackend

    let private piPulse : PulseSegment list =
        [ { Duration = Math.PI; RabiStart = 1.0; RabiEnd = 1.0; DetuningStart = 0.0; DetuningEnd = 0.0 } ]

    let private countOf (key: string) (h: Map<string, int>) = h |> Map.tryFind key |> Option.defaultValue 0

    [<Fact>]
    let ``Rydberg blockade suppresses double excitation of nearby atoms`` () =
        // Two atoms driven by a π pulse. Far apart they excite independently (|11⟩ ≈ all);
        // close together the blockade forbids exciting both (|11⟩ ≈ 0).
        let program register c6 = { Register = register; C6 = c6; Schedule = piPulse }
        let far = program [ { X = 0.0; Y = 0.0 }; { X = 10.0; Y = 0.0 } ] 1.0
        let near = program [ { X = 0.0; Y = 0.0 }; { X = 1.0; Y = 0.0 } ] 100.0

        match simulate (backend ()) far 200 4000, simulate (backend ()) near 200 4000 with
        | Ok farH, Ok nearH ->
            let far11 = countOf "11" farH
            let near11 = countOf "11" nearH
            Assert.True(far11 > 3800, $"far pair should mostly be the doubly-excited state, got {far11}")
            Assert.True(near11 < 200, $"blockade should suppress the doubly-excited state, got {near11}")
        | _ -> failwith "simulation failed"

    [<Fact>]
    let ``adiabatic sweep finds the maximum independent set of a 3-atom path`` () =
        // Path A–B–C: A,C sit outside each other's blockade but each blockades B.
        // The maximum independent set is {A, C} = bitstring "101".
        let register = [ { X = 0.0; Y = 0.0 }; { X = 1.0; Y = 0.0 }; { X = 2.0; Y = 0.0 } ]
        let program = maximumIndependentSetProgram register 30.0 1.0 3.0 12.0
        match simulate (backend ()) program 120 4000 with
        | Error e -> failwith $"simulation failed: {e.Message}"
        | Ok histogram ->
            let top = histogram |> Map.toList |> List.maxBy snd |> fst
            Assert.Equal("101", top)

    [<Fact>]
    let ``isIndependentSet accepts non-adjacent excitations and rejects blockaded ones`` () =
        // Blockade radius with C6=30, Ω=1 is ~1.76: neighbours (r=1) blockade, r=2 does not.
        let register = [ { X = 0.0; Y = 0.0 }; { X = 1.0; Y = 0.0 }; { X = 2.0; Y = 0.0 } ]
        let program = { Register = register; C6 = 30.0; Schedule = piPulse }
        Assert.True(isIndependentSet program 1.0 "101")   // A,C are 2 apart → independent
        Assert.False(isIndependentSet program 1.0 "110")  // A,B are 1 apart → blockaded

    [<Fact>]
    let ``blockade radius follows (C6/Omega)^(1/6)`` () =
        Assert.Equal(2.0, blockadeRadius 64.0 1.0, 6)   // 64^(1/6) = 2
        Assert.Equal(infinity, blockadeRadius 10.0 0.0)

    [<Fact>]
    let ``single-atom quench reproduces Rabi dynamics <n>(t) = sin^2(t/2)`` () =
        let backend = backend ()
        let densityAt (t: float) =
            (evolve backend (quench [ { X = 0.0; Y = 0.0 } ] 1.0 1.0 0.0 t) 200 |> Result.bind (rydbergDensities 1)) |> Result.map (fun d -> d.[0]) |> Result.defaultWith (fun e -> failwith e.Message)
        Assert.Equal(0.0, densityAt 0.0, 3)
        Assert.Equal(0.5, densityAt (Math.PI / 2.0), 3)
        Assert.Equal(1.0, densityAt Math.PI, 3)          // a π pulse fully excites the atom

    [<Fact>]
    let ``solveMaximumIndependentSet returns the MIS atom indices of a 3-atom path`` () =
        let register = [ { X = 0.0; Y = 0.0 }; { X = 1.0; Y = 0.0 }; { X = 2.0; Y = 0.0 } ]
        (solveMaximumIndependentSet (backend ()) register 30.0 1.0 3.0 12.0 120 4000) |> Result.map (fun mis -> Assert.Equal<int list>([ 0; 2 ], mis)) |> Result.defaultWith (fun e -> failwith e.Message)   // {A, C}

    [<Fact>]
    let ``optimizeAnalog tunes a pulse to minimise a cost Hamiltonian`` () =
        // Minimise ⟨Z₀⟩ for one atom by tuning the quench duration; a π pulse gives ⟨Z₀⟩ = -1.
        let z0 : TrotterSuzuki.PauliHamiltonian =
            { Terms = [ { Operators = [| 'Z' |]; Coefficient = System.Numerics.Complex(1.0, 0.0) } ]; NumQubits = 1 }
        let paramsToProgram (p: float[]) = quench [ { X = 0.0; Y = 0.0 } ] 1.0 1.0 0.0 p.[0]
        match optimizeAnalog (backend ()) paramsToProgram z0 200 [| 1.0 |] with
        | Error e -> failwith e.Message
        | Ok (_, energy) -> Assert.Equal(-1.0, energy, 2)

    [<Fact>]
    let ``blockade caps the collective excitation of a nearby pair`` () =
        let backend = backend ()
        let program = quench [ { X = 0.0; Y = 0.0 }; { X = 1.0; Y = 0.0 } ] 100.0 1.0 0.0 Math.PI
        match evolve backend program 200 |> Result.bind (rydbergDensities 2) with
        | Error e -> failwith e.Message
        | Ok d ->
            // Without blockade a π pulse would give ⟨n⟩=1 each (total 2); the blockade holds
            // the total well below that, and the two atoms are symmetric.
            Assert.True(d.[0] + d.[1] < 1.0, $"blockade should cap total excitation, got {d.[0] + d.[1]}")
            Assert.Equal(d.[0], d.[1], 2)
