namespace FSharp.Azure.Quantum.Tests

open System.Numerics
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms

/// Tests for the CUDA-Q-style execution primitives (sample / observe / run / getState).
module PrimitivesTests =

    let private backend () : IQuantumBackend =
        LocalBackend.LocalBackend() :> IQuantumBackend

    /// Bell state |Φ⁺⟩ = (|00⟩ + |11⟩)/√2 as a circuit.
    let private bell () =
        CircuitBuilder.empty 2
        |> CircuitBuilder.addGate (CircuitBuilder.H 0)
        |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 1))

    [<Fact>]
    let ``sample of a Bell state only yields |00> and |11>`` () =
        match Primitives.sample (backend ()) (bell ()) 2000 with
        | Error e -> failwith $"sample failed: {e.Message}"
        | Ok histogram ->
            // Only the correlated outcomes should appear.
            let keys = histogram |> Map.toList |> List.map fst |> Set.ofList
            Assert.True(Set.isSubset keys (Set.ofList [ "00"; "11" ]),
                        $"unexpected outcomes: {keys}")
            // Both should actually occur with a fair share of the shots.
            Assert.True(histogram.ContainsKey "00" && histogram.["00"] > 500)
            Assert.True(histogram.ContainsKey "11" && histogram.["11"] > 500)

    [<Fact>]
    let ``observe of Z0 Z1 on a Bell state is +1`` () =
        let zz : TrotterSuzuki.PauliHamiltonian =
            { Terms = [ { Operators = [| 'Z'; 'Z' |]; Coefficient = Complex(1.0, 0.0) } ]; NumQubits = 2 }
        match Primitives.observe (backend ()) (bell ()) zz with
        | Error e -> failwith $"observe failed: {e.Message}"
        | Ok value -> Assert.Equal(1.0, value, 6)

    [<Fact>]
    let ``observe of X0 on a Bell state is 0`` () =
        let x0 : TrotterSuzuki.PauliHamiltonian =
            { Terms = [ { Operators = [| 'X'; 'I' |]; Coefficient = Complex(1.0, 0.0) } ]; NumQubits = 2 }
        match Primitives.observe (backend ()) (bell ()) x0 with
        | Error e -> failwith $"observe failed: {e.Message}"
        | Ok value -> Assert.Equal(0.0, value, 6)

    [<Fact>]
    let ``observe rejects a Pauli term whose width mismatches the state`` () =
        let wrongWidth : TrotterSuzuki.PauliHamiltonian =
            { Terms = [ { Operators = [| 'Z' |]; Coefficient = Complex(1.0, 0.0) } ]; NumQubits = 1 }
        match Primitives.observe (backend ()) (bell ()) wrongWidth with
        | Error (QuantumError.ValidationError ("Hamiltonian", _)) -> ()
        | other -> failwith $"expected a Hamiltonian ValidationError, got: {other}"

    [<Fact>]
    let ``run returns one bit per qubit for each requested shot`` () =
        match Primitives.run (backend ()) (bell ()) 7 with
        | Error e -> failwith $"run failed: {e.Message}"
        | Ok shots ->
            Assert.Equal(7, shots.Length)
            Assert.All(shots, fun shot -> Assert.Equal(2, shot.Length))

    [<Fact>]
    let ``getState returns a state vector on the local simulator`` () =
        match Primitives.getState (backend ()) (bell ()) with
        | Error e -> failwith $"getState failed: {e.Message}"
        | Ok (QuantumState.StateVector _) -> ()
        | Ok other -> failwith $"expected a StateVector, got: {other}"

    let private runSync (t: System.Threading.Tasks.Task<'a>) : 'a =
        t |> Async.AwaitTask |> Async.RunSynchronously

    [<Fact>]
    let ``sampleBatchAsync runs several circuits concurrently, in order`` () =
        // |0>, X|0> = |1>, Bell
        let zero = CircuitBuilder.empty 1
        let one = CircuitBuilder.empty 1 |> CircuitBuilder.addGate (CircuitBuilder.X 0)
        let results =
            Primitives.sampleBatchAsync (backend ()) [ zero; one; bell () ] 1000 System.Threading.CancellationToken.None
            |> runSync
        Assert.Equal(3, results.Length)
        match results with
        | [ Ok h0; Ok h1; Ok hBell ] ->
            Assert.Equal(1000, h0.["0"])                                   // |0> always measures 0
            Assert.Equal(1000, h1.["1"])                                   // |1> always measures 1
            let bellKeys = hBell |> Map.toList |> List.map fst |> Set.ofList
            Assert.True(Set.isSubset bellKeys (Set.ofList [ "00"; "11" ]))
        | _ -> failwith $"expected three Ok results, got: {results}"

    [<Fact>]
    let ``observeBatchAsync computes an expectation per circuit`` () =
        let zz : TrotterSuzuki.PauliHamiltonian =
            { Terms = [ { Operators = [| 'Z'; 'Z' |]; Coefficient = Complex(1.0, 0.0) } ]; NumQubits = 2 }
        // |00> has ⟨Z0Z1⟩ = +1; Bell also has ⟨Z0Z1⟩ = +1.
        let results =
            Primitives.observeBatchAsync (backend ()) [ CircuitBuilder.empty 2; bell () ] zz System.Threading.CancellationToken.None
            |> runSync
        match results with
        | [ Ok a; Ok b ] -> Assert.Equal(1.0, a, 6); Assert.Equal(1.0, b, 6)
        | _ -> failwith $"expected two Ok results, got: {results}"

    [<Fact>]
    let ``sampleDistributedAsync fans out across backends`` () =
        let jobs = [ (backend (), bell ()); (backend (), CircuitBuilder.empty 1) ]
        let results = Primitives.sampleDistributedAsync jobs 500 System.Threading.CancellationToken.None |> runSync
        Assert.Equal(2, results.Length)
        Assert.All(results, fun r -> match r with Ok _ -> () | Error e -> failwith e.Message)

    [<Fact>]
    let ``sample with negative shots returns a ValidationError`` () =
        match Primitives.sample (backend ()) (bell ()) -5 with
        | Error (QuantumError.ValidationError ("shots", _)) -> ()
        | other -> failwith $"expected a shots ValidationError, got: {other}"

    [<Fact>]
    let ``expectation on a sparse state above 20 qubits returns Error, not an exception`` () =
        // Densifying would blow past StateVector's 20-qubit limit; must be a clean Error.
        let h : TrotterSuzuki.PauliHamiltonian =
            { Terms = [ { Operators = Array.create 25 'Z'; Coefficient = Complex(1.0, 0.0) } ]; NumQubits = 25 }
        match Primitives.expectation h (QuantumState.SparseState (Map.empty, 25)) with
        | Error (QuantumError.ValidationError ("numQubits", _)) -> ()
        | other -> failwith $"expected a numQubits ValidationError, got: {other}"
