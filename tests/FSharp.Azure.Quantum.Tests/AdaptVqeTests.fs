namespace FSharp.Azure.Quantum.Tests

open System.Numerics
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms

/// Tests for ADAPT-VQE (adaptive ansatz growth).
module AdaptVqeTests =

    let private backend () : IQuantumBackend =
        LocalBackend.LocalBackend() :> IQuantumBackend

    let private ps (ops: char[]) : TrotterSuzuki.PauliString =
        { Operators = ops; Coefficient = Complex(1.0, 0.0) }

    [<Fact>]
    let ``ADAPT-VQE finds the ground energy of H = X (single qubit)`` () =
        // X has eigenvalues ±1; ground energy is -1. Reference |0> has ⟨X⟩ = 0 but a
        // non-zero gradient along Y, so the pool {Y} drives the ansatz to |−⟩.
        let h : TrotterSuzuki.PauliHamiltonian = { Terms = [ ps [| 'X' |] ]; NumQubits = 1 }
        match AdaptVqe.run (backend ()) h [ ps [| 'Y' |] ] 1 AdaptVqe.defaultConfig with
        | Error e -> failwith $"ADAPT-VQE failed: {e.Message}"
        | Ok result ->
            Assert.Equal(-1.0, result.Energy, 3)
            Assert.True(result.Converged)
            Assert.Equal(1, result.SelectedOperators.Length)

    [<Fact>]
    let ``ADAPT-VQE finds the ground energy of H = X0 + X1 (two qubits)`` () =
        // Separable; ground energy -2, reached by rotating both qubits to |−⟩.
        let h : TrotterSuzuki.PauliHamiltonian =
            { Terms = [ ps [| 'X'; 'I' |]; ps [| 'I'; 'X' |] ]; NumQubits = 2 }
        let pool = [ ps [| 'Y'; 'I' |]; ps [| 'I'; 'Y' |] ]
        match AdaptVqe.run (backend ()) h pool 2 AdaptVqe.defaultConfig with
        | Error e -> failwith $"ADAPT-VQE failed: {e.Message}"
        | Ok result ->
            Assert.Equal(-2.0, result.Energy, 3)
            Assert.True(result.Converged)
            // Energy is monotonically non-increasing as operators are added.
            let hist = result.EnergyHistory
            List.pairwise hist |> List.iter (fun (a, b) -> Assert.True(b <= a + 1e-6, $"energy increased: {a} -> {b}"))

    [<Fact>]
    let ``ADAPT-VQE rejects a pool operator whose width mismatches the problem`` () =
        let h : TrotterSuzuki.PauliHamiltonian = { Terms = [ ps [| 'X'; 'I' |] ]; NumQubits = 2 }
        match AdaptVqe.run (backend ()) h [ ps [| 'Y' |] ] 2 AdaptVqe.defaultConfig with
        | Error (QuantumError.ValidationError ("pool", _)) -> ()
        | other -> failwith $"expected a pool ValidationError, got: {other}"

    [<Fact>]
    let ``ADAPT-VQE rejects an empty operator pool`` () =
        let h : TrotterSuzuki.PauliHamiltonian = { Terms = [ ps [| 'Z' |] ]; NumQubits = 1 }
        match AdaptVqe.run (backend ()) h [] 1 AdaptVqe.defaultConfig with
        | Error (QuantumError.ValidationError ("pool", _)) -> ()
        | other -> failwith $"expected a pool ValidationError, got: {other}"
