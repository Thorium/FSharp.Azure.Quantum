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
        {
            Operators = ops
            Coefficient = Complex(1.0, 0.0)
        }

    [<Fact>]
    let ``ADAPT-VQE finds the ground energy of H = X (single qubit)`` () =
        // X has eigenvalues ±1; ground energy is -1. Reference |0> has ⟨X⟩ = 0 but a
        // non-zero gradient along Y, so the pool {Y} drives the ansatz to |−⟩.
        let h: TrotterSuzuki.PauliHamiltonian =
            {
                Terms = [ ps [| 'X' |] ]
                NumQubits = 1
            }

        match AdaptVqe.run (backend ()) h [ ps [| 'Y' |] ] 1 AdaptVqe.defaultConfig with
        | Error e -> failwith $"ADAPT-VQE failed: {e.Message}"
        | Ok result ->
            Assert.Equal(-1.0, result.Energy, 3)
            Assert.True(result.Converged)
            Assert.Equal(1, result.SelectedOperators.Length)

    [<Fact>]
    let ``ADAPT-VQE finds the ground energy of H = X0 + X1 (two qubits)`` () =
        // Separable; ground energy -2, reached by rotating both qubits to |−⟩.
        let h: TrotterSuzuki.PauliHamiltonian =
            {
                Terms = [ ps [| 'X'; 'I' |]; ps [| 'I'; 'X' |] ]
                NumQubits = 2
            }

        let pool = [ ps [| 'Y'; 'I' |]; ps [| 'I'; 'Y' |] ]

        match AdaptVqe.run (backend ()) h pool 2 AdaptVqe.defaultConfig with
        | Error e -> failwith $"ADAPT-VQE failed: {e.Message}"
        | Ok result ->
            Assert.Equal(-2.0, result.Energy, 3)
            Assert.True(result.Converged)
            // Energy is monotonically non-increasing as operators are added.
            let hist = result.EnergyHistory

            List.pairwise hist
            |> List.iter (fun (a, b) -> Assert.True(b <= a + 1e-6, $"energy increased: {a} -> {b}"))

    [<Fact>]
    let ``ADAPT-VQE rejects a pool operator whose width mismatches the problem`` () =
        let h: TrotterSuzuki.PauliHamiltonian =
            {
                Terms = [ ps [| 'X'; 'I' |] ]
                NumQubits = 2
            }

        match AdaptVqe.run (backend ()) h [ ps [| 'Y' |] ] 2 AdaptVqe.defaultConfig with
        | Error(QuantumError.ValidationError("pool", _)) -> ()
        | other -> failwith $"expected a pool ValidationError, got: {other}"

    [<Fact>]
    let ``ADAPT-VQE rejects an empty operator pool`` () =
        let h: TrotterSuzuki.PauliHamiltonian =
            {
                Terms = [ ps [| 'Z' |] ]
                NumQubits = 1
            }

        match AdaptVqe.run (backend ()) h [] 1 AdaptVqe.defaultConfig with
        | Error(QuantumError.ValidationError("pool", _)) -> ()
        | other -> failwith $"expected a pool ValidationError, got: {other}"

    // ========================================================================
    // Shot-sampling (cloud) backends: measured energies, parameter-shift gradients
    // ========================================================================

    let private term (letters: string) (c: float) : TrotterSuzuki.PauliString =
        {
            Operators = letters.ToCharArray()
            Coefficient = Complex(c, 0.0)
        }

    /// Two-qubit H2 (STO-3G, 0.735 Å, parity-reduced). From |00⟩ with this pool ADAPT reaches
    /// the XX-coupled {|00⟩, |11⟩} minimum, -1.0636 - 0.1809 = -1.2445.
    let private h2: TrotterSuzuki.PauliHamiltonian =
        {
            Terms =
                [
                    term "II" -1.0524
                    term "ZI" 0.3979
                    term "IZ" -0.3979
                    term "ZZ" -0.0112
                    term "XX" 0.1809
                ]
            NumQubits = 2
        }

    let private h2Pool = [ term "YI" 1.0; term "IY" 1.0; term "XY" 1.0; term "YX" 1.0 ]

    [<Fact>]
    let ``ADAPT-VQE exact route is unchanged on the local simulator (H2)`` () =
        match AdaptVqe.run (backend ()) h2 h2Pool 2 AdaptVqe.defaultConfig with
        | Error e -> failwith $"ADAPT-VQE failed: {e.Message}"
        | Ok result ->
            Assert.Equal(-1.2445, result.Energy, 4)
            Assert.Equal(1, result.Iterations)
            Assert.True(result.Converged)

    [<Fact>]
    let ``ADAPT-VQE on a shot-sampling backend reaches the exact energy by whole-circuit jobs (H2)`` () =
        let cloud = CloudStyleBackends.ShotSamplingCloud(4000, 11)

        match AdaptVqe.run cloud h2 h2Pool 2 AdaptVqe.defaultConfig with
        | Error e -> failwith $"ADAPT-VQE failed: {e.Message}"
        | Ok result ->
            Assert.True(abs (result.Energy - -1.2445) < 0.03, $"sampled energy {result.Energy}")
            Assert.True(result.Iterations >= 1, "an operator must have been selected")
            Assert.True(cloud.Jobs > 0)
            Assert.Equal(0, cloud.ApplyOperationCalls)

    [<Fact>]
    let ``ADAPT-VQE on a shot-sampling backend finds the ground energy of the one-qubit X Hamiltonian`` () =
        let h: TrotterSuzuki.PauliHamiltonian =
            {
                Terms = [ ps [| 'X' |] ]
                NumQubits = 1
            }

        let cloud = CloudStyleBackends.ShotSamplingCloud(4000, 3)

        match AdaptVqe.run cloud h [ ps [| 'Y' |] ] 1 AdaptVqe.defaultConfig with
        | Error e -> failwith $"ADAPT-VQE failed: {e.Message}"
        | Ok result -> Assert.True(abs (result.Energy - -1.0) < 0.03, $"sampled energy {result.Energy}")

    [<Fact>]
    let ``parameterShift is the exact derivative of a Pauli rotation`` () =
        // E(θ) = ⟨0|e^{iθcY} Z e^{-iθcY}|0⟩ = cos(2cθ), dE/dθ = -2c·sin(2cθ).
        let c, theta = 0.7, 0.4

        let energy time = Ok(cos (2.0 * c * time), 0.0)

        match AdaptVqe.parameterShift c theta energy with
        | Ok(derivative, _) -> Assert.Equal(-2.0 * c * sin (2.0 * c * theta), derivative, 12)
        | Error e -> failwith e.Message
