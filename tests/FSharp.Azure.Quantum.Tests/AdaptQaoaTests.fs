namespace FSharp.Azure.Quantum.Tests

open System.Numerics
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms

/// Tests for ADAPT-QAOA (adaptive mixer selection).
module AdaptQaoaTests =

    let private backend () : IQuantumBackend =
        LocalBackend.LocalBackend() :> IQuantumBackend

    let private ps (ops: char[]) (c: float) : TrotterSuzuki.PauliString =
        { Operators = ops; Coefficient = Complex(c, 0.0) }

    [<Fact>]
    let ``ADAPT-QAOA solves a 2-qubit MaxCut (H = Z0 Z1, ground -1)`` () =
        // Minimising ⟨Z₀Z₁⟩ anti-aligns the qubits — the max cut of a single edge.
        let h : TrotterSuzuki.PauliHamiltonian = { Terms = [ ps [| 'Z'; 'Z' |] 1.0 ]; NumQubits = 2 }
        let pool = [ ps [| 'X'; 'I' |] 1.0; ps [| 'I'; 'X' |] 1.0; ps [| 'Y'; 'I' |] 1.0; ps [| 'I'; 'Y' |] 1.0 ]
        match AdaptQaoa.run (backend ()) h pool 2 AdaptQaoa.defaultConfig with
        | Error e -> failwith $"ADAPT-QAOA failed: {e.Message}"
        | Ok result ->
            Assert.Equal(-1.0, result.Energy, 3)
            Assert.True(result.Converged)

    [<Fact>]
    let ``ADAPT-QAOA solves the frustrated triangle (ground -1)`` () =
        // Triangle MaxCut is frustrated: min Σ ZᵢZⱼ = -1 (two edges cut).
        let h : TrotterSuzuki.PauliHamiltonian =
            { Terms = [ ps [| 'Z'; 'Z'; 'I' |] 1.0; ps [| 'I'; 'Z'; 'Z' |] 1.0; ps [| 'Z'; 'I'; 'Z' |] 1.0 ]
              NumQubits = 3 }
        let pool =
            [ ps [| 'X'; 'I'; 'I' |] 1.0; ps [| 'I'; 'X'; 'I' |] 1.0; ps [| 'I'; 'I'; 'X' |] 1.0
              ps [| 'Y'; 'I'; 'I' |] 1.0; ps [| 'I'; 'Y'; 'I' |] 1.0; ps [| 'I'; 'I'; 'Y' |] 1.0 ]
        match AdaptQaoa.run (backend ()) h pool 3 AdaptQaoa.defaultConfig with
        | Error e -> failwith $"ADAPT-QAOA failed: {e.Message}"
        | Ok result ->
            Assert.Equal(-1.0, result.Energy, 3)
            // Energy never increases as layers are added.
            List.pairwise result.EnergyHistory
            |> List.iter (fun (a, b) -> Assert.True(b <= a + 1e-6, $"energy increased: {a} -> {b}"))

    [<Fact>]
    let ``ADAPT-QAOA rejects a mixer whose width mismatches the problem`` () =
        let h : TrotterSuzuki.PauliHamiltonian = { Terms = [ ps [| 'Z'; 'Z' |] 1.0 ]; NumQubits = 2 }
        match AdaptQaoa.run (backend ()) h [ ps [| 'X' |] 1.0 ] 2 AdaptQaoa.defaultConfig with
        | Error (QuantumError.ValidationError ("pool", _)) -> ()
        | other -> failwith $"expected a pool ValidationError, got: {other}"

    [<Fact>]
    let ``ADAPT-QAOA rejects an empty mixer pool`` () =
        let h : TrotterSuzuki.PauliHamiltonian = { Terms = [ ps [| 'Z'; 'Z' |] 1.0 ]; NumQubits = 2 }
        match AdaptQaoa.run (backend ()) h [] 2 AdaptQaoa.defaultConfig with
        | Error (QuantumError.ValidationError ("pool", _)) -> ()
        | other -> failwith $"expected a pool ValidationError, got: {other}"

    [<Fact>]
    let ``solveQubo rejects a QUBO key outside [0, numQubits) with an Error, not an exception`` () =
        // Variable index 2 with only 2 qubits — must surface as Error, not IndexOutOfRangeException.
        let qubo = Map.ofList [ ((2, 2), 1.0) ]
        match AdaptQaoa.solveQubo (backend ()) 2 qubo AdaptQaoa.defaultConfig with
        | Error (QuantumError.ValidationError ("quboMap", _)) -> ()
        | other -> failwith $"expected a quboMap ValidationError, got: {other}"

    [<Fact>]
    let ``solveQubo minimises a small QUBO`` () =
        // Q = [[-1, 2], [0, -1]]: min over x∈{0,1}² of -x0 - x1 + 2 x0 x1 is -1 at (1,0) or (0,1).
        let qubo = Map.ofList [ ((0, 0), -1.0); ((1, 1), -1.0); ((0, 1), 2.0) ]
        match AdaptQaoa.solveQubo (backend ()) 2 qubo AdaptQaoa.defaultConfig with
        | Error e -> failwith $"solveQubo failed: {e.Message}"
        | Ok solution ->
            Assert.Equal(-1.0, solution.QuboCost, 3)
            Assert.Equal(1, solution.Assignment.[0] + solution.Assignment.[1])   // exactly one variable set

    [<Fact>]
    let ``MaxCut.solveWithAdaptQaoa finds the max cut of a triangle`` () =
        // Triangle MaxCut = 2 (odd cycle: one edge can't be cut).
        let triangle =
            FSharp.Azure.Quantum.MaxCut.createProblem
                [ "A"; "B"; "C" ]
                [ ("A", "B", 1.0); ("B", "C", 1.0); ("A", "C", 1.0) ]
        match FSharp.Azure.Quantum.MaxCut.solveWithAdaptQaoa triangle None with
        | Error e -> failwith $"solveWithAdaptQaoa failed: {e.Message}"
        | Ok solution ->
            Assert.Equal(2.0, solution.CutValue, 3)
            Assert.True(solution.IsQuantum)

    [<Fact>]
    let ``MaxCut.solveWithAdaptQaoa finds the max cut of a 4-cycle`` () =
        // A 4-cycle is bipartite: MaxCut cuts all 4 edges.
        let square =
            FSharp.Azure.Quantum.MaxCut.createProblem
                [ "A"; "B"; "C"; "D" ]
                [ ("A", "B", 1.0); ("B", "C", 1.0); ("C", "D", 1.0); ("D", "A", 1.0) ]
        match FSharp.Azure.Quantum.MaxCut.solveWithAdaptQaoa square None with
        | Error e -> failwith $"solveWithAdaptQaoa failed: {e.Message}"
        | Ok solution ->
            Assert.Equal(4.0, solution.CutValue, 3)
