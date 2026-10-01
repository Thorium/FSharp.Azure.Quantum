namespace FSharp.Azure.Quantum.Tests

open System.Numerics
open System.Threading
open System.Threading.Tasks
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
        |> CircuitBuilder.addGate (CircuitBuilder.CNOT(0, 1))

    [<Fact>]
    let ``sample of a Bell state only yields |00> and |11>`` () =
        match Primitives.sample (backend ()) (bell ()) 2000 with
        | Error e -> failwith $"sample failed: {e.Message}"
        | Ok histogram ->
            // Only the correlated outcomes should appear.
            let keys = histogram |> Map.toList |> List.map fst |> Set.ofList
            Assert.True(Set.isSubset keys (Set.ofList [ "00"; "11" ]), $"unexpected outcomes: {keys}")
            // Both should actually occur with a fair share of the shots.
            Assert.True(histogram.ContainsKey "00" && histogram.["00"] > 500)
            Assert.True(histogram.ContainsKey "11" && histogram.["11"] > 500)

    [<Fact>]
    let ``observe of Z0 Z1 on a Bell state is +1`` () =
        let zz: TrotterSuzuki.PauliHamiltonian =
            {
                Terms =
                    [
                        {
                            Operators = [| 'Z'; 'Z' |]
                            Coefficient = Complex(1.0, 0.0)
                        }
                    ]
                NumQubits = 2
            }

        (Primitives.observe (backend ()) (bell ()) zz)
        |> Result.map (fun value -> Assert.Equal(1.0, value, 6))
        |> Result.defaultWith (fun e -> failwith $"observe failed: {e.Message}")

    [<Fact>]
    let ``observe of X0 on a Bell state is 0`` () =
        let x0: TrotterSuzuki.PauliHamiltonian =
            {
                Terms =
                    [
                        {
                            Operators = [| 'X'; 'I' |]
                            Coefficient = Complex(1.0, 0.0)
                        }
                    ]
                NumQubits = 2
            }

        (Primitives.observe (backend ()) (bell ()) x0)
        |> Result.map (fun value -> Assert.Equal(0.0, value, 6))
        |> Result.defaultWith (fun e -> failwith $"observe failed: {e.Message}")

    [<Fact>]
    let ``observe rejects a Pauli term whose width mismatches the state`` () =
        let wrongWidth: TrotterSuzuki.PauliHamiltonian =
            {
                Terms =
                    [
                        {
                            Operators = [| 'Z' |]
                            Coefficient = Complex(1.0, 0.0)
                        }
                    ]
                NumQubits = 1
            }

        match Primitives.observe (backend ()) (bell ()) wrongWidth with
        | Error(QuantumError.ValidationError("Hamiltonian", _)) -> ()
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
        | Ok(QuantumState.StateVector _) -> ()
        | Ok other -> failwith $"expected a StateVector, got: {other}"

    [<Fact>]
    let ``sampleBatchAsync runs several circuits concurrently, in order`` () : Task =
        task {
            // |0>, X|0> = |1>, Bell
            let zero = CircuitBuilder.empty 1
            let one = CircuitBuilder.empty 1 |> CircuitBuilder.addGate (CircuitBuilder.X 0)

            let! results =
                Primitives.sampleBatchAsync (backend ()) [ zero; one; bell () ] 1000 CancellationToken.None

            Assert.Equal(3, results.Length)

            match results with
            | [ Ok h0; Ok h1; Ok hBell ] ->
                Assert.Equal(1000, h0.["0"]) // |0> always measures 0
                Assert.Equal(1000, h1.["1"]) // |1> always measures 1
                let bellKeys = hBell |> Map.toList |> List.map fst |> Set.ofList
                Assert.True(Set.isSubset bellKeys (Set.ofList [ "00"; "11" ]))
            | _ -> failwith $"expected three Ok results, got: {results}"
        }
        :> Task

    [<Fact>]
    let ``observeBatchAsync computes an expectation per circuit`` () : Task =
        task {
            let zz: TrotterSuzuki.PauliHamiltonian =
                {
                    Terms =
                        [
                            {
                                Operators = [| 'Z'; 'Z' |]
                                Coefficient = Complex(1.0, 0.0)
                            }
                        ]
                    NumQubits = 2
                }
            // |00> has ⟨Z0Z1⟩ = +1; Bell also has ⟨Z0Z1⟩ = +1.
            let! results =
                Primitives.observeBatchAsync (backend ()) [ CircuitBuilder.empty 2; bell () ] zz CancellationToken.None

            match results with
            | [ Ok a; Ok b ] ->
                Assert.Equal(1.0, a, 6)
                Assert.Equal(1.0, b, 6)
            | _ -> failwith $"expected two Ok results, got: {results}"
        }
        :> Task

    [<Fact>]
    let ``sampleDistributedAsync fans out across backends`` () : Task =
        task {
            let jobs = [ (backend (), bell ()); (backend (), CircuitBuilder.empty 1) ]

            let! results = Primitives.sampleDistributedAsync jobs 500 CancellationToken.None

            Assert.Equal(2, results.Length)

            Assert.All(
                results,
                fun r ->
                    r
                    |> Result.map (fun _ -> ())
                    |> Result.defaultWith (fun e -> failwith e.Message)
            )
        }
        :> Task

    [<Fact>]
    let ``sample with negative shots returns a ValidationError`` () =
        match Primitives.sample (backend ()) (bell ()) -5 with
        | Error(QuantumError.ValidationError("shots", _)) -> ()
        | other -> failwith $"expected a shots ValidationError, got: {other}"

    [<Fact>]
    let ``expectation on a sparse state wider than the densify limit returns Error, not an exception`` () =
        // Densifying would blow past what StateVector can hold; must be a clean Error.
        // That width is derived from available memory, so size the state relative to it.
        let width = LocalSimulator.StateVector.maxQubits + 5

        let h: TrotterSuzuki.PauliHamiltonian =
            {
                Terms =
                    [
                        {
                            Operators = Array.create width 'Z'
                            Coefficient = Complex(1.0, 0.0)
                        }
                    ]
                NumQubits = width
            }

        match Primitives.expectation h (QuantumState.SparseState(Map.empty, width)) with
        | Error(QuantumError.ValidationError("numQubits", _)) -> ()
        | other -> failwith $"expected a numQubits ValidationError, got: {other}"

    // ========================================================================
    // Shot-sampling (cloud) backends: measured expectations, no resampling
    // ========================================================================

    let private pauliTerm (letters: string) (coefficient: float) : TrotterSuzuki.PauliString =
        {
            Operators = letters.ToCharArray()
            Coefficient = Complex(coefficient, 0.0)
        }

    let private hamiltonianOf (width: int) (terms: TrotterSuzuki.PauliString list) : TrotterSuzuki.PauliHamiltonian =
        { Terms = terms; NumQubits = width }

    let private exactObserve circuit hamiltonian =
        match Primitives.observe (backend ()) circuit hamiltonian with
        | Ok value -> value
        | Error e -> failwith $"exact observe failed: {e.Message}"

    /// |+i⟩ = S·H|0⟩: ⟨X⟩ = 0, ⟨Y⟩ = 1, ⟨Z⟩ = 0.
    let private plusI () =
        CircuitBuilder.empty 1
        |> CircuitBuilder.addGates [ CircuitBuilder.H 0; CircuitBuilder.S 0 ]

    [<Fact>]
    let ``observe on a shot-sampling backend measures X and Y of |+i> in rotated bases`` () =
        for (letter, expected) in [ "X", 0.0; "Y", 1.0; "Z", 0.0 ] do
            let cloud = CloudStyleBackends.ShotSamplingCloud(20000, 5)
            let h = hamiltonianOf 1 [ pauliTerm letter 1.0 ]

            match Primitives.sampledExpectation cloud (plusI ()) h with
            | Error e -> failwith $"sampledExpectation failed: {e.Message}"
            | Ok estimate ->
                Assert.True(
                    abs (estimate.Value - expected) <= 5.0 * estimate.StandardError + 1e-9,
                    $"<{letter}> = {estimate.Value} ± {estimate.StandardError}, expected {expected}"
                )

                Assert.Equal(Some 20000, estimate.ShotsPerCircuit)
                Assert.Equal(1, estimate.Circuits)

            // observe routes shot-sampling backends through the same measurement.
            match Primitives.observe cloud (plusI ()) h with
            | Ok value -> Assert.True(abs (value - expected) < 0.05, $"observe <{letter}> = {value}")
            | Error e -> failwith $"observe failed: {e.Message}"

            Assert.Equal(0, cloud.ApplyOperationCalls)

    /// Seeded random 3-qubit circuit of rotations and CNOTs.
    let private randomCircuit (seed: int) =
        let rng = System.Random(seed)

        let angle () =
            (rng.NextDouble() - 0.5) * 2.0 * System.Math.PI

        [
            for layer in 0..2 do
                for q in 0..2 do
                    yield CircuitBuilder.U3(q, angle (), angle (), angle ())

                yield CircuitBuilder.CNOT(layer % 3, (layer + 1) % 3)
        ]
        |> fun gates -> CircuitBuilder.empty 3 |> CircuitBuilder.addGates gates

    /// Multi-term Hamiltonian with every Pauli letter, overlapping supports and a constant.
    let private mixedHamiltonian () =
        hamiltonianOf
            3
            [
                pauliTerm "III" -0.7
                pauliTerm "ZII" 0.4
                pauliTerm "IZZ" -0.3
                pauliTerm "XXI" 0.25
                pauliTerm "IYY" 0.6
                pauliTerm "XIY" -0.45
                pauliTerm "YZX" 0.35
                pauliTerm "ZZZ" 0.2
            ]

    [<Fact>]
    let ``sampledExpectation agrees with the exact value within its standard error on random states`` () =
        let h = mixedHamiltonian ()

        for seed in 1..6 do
            let circuit = randomCircuit seed
            let exact = exactObserve circuit h
            let cloud = CloudStyleBackends.ShotSamplingCloud(8000, 100 + seed)

            match Primitives.sampledExpectation cloud circuit h with
            | Error e -> failwith $"seed {seed}: {e.Message}"
            | Ok estimate ->
                Assert.True(estimate.StandardError > 0.0)

                Assert.True(
                    abs (estimate.Value - exact) <= 5.0 * estimate.StandardError,
                    $"seed {seed}: sampled {estimate.Value} ± {estimate.StandardError}, exact {exact}"
                )

                // One whole-circuit job per qubit-wise commuting group, none for the constant.
                let groups = Primitives.measurementGroups h
                Assert.Equal(groups.Length, estimate.Circuits)
                Assert.Equal(groups.Length, cloud.Jobs)

    [<Fact>]
    let ``measurementGroups are qubit-wise commuting and cover every non-identity term once`` () =
        let h = mixedHamiltonian ()
        let groups = Primitives.measurementGroups h

        let covered = groups |> List.collect (fun g -> g.Terms)
        Assert.Equal(h.Terms.Length - 1, covered.Length) // all but the identity

        for group in groups do
            for term in group.Terms do
                term.Operators
                |> Array.iteri (fun q p ->
                    if p <> 'I' then
                        Assert.Equal(group.Basis.[q], p))

    [<Fact>]
    let ``sampledExpectation on an exact backend is the exact value and observe keeps its route`` () =
        let h = mixedHamiltonian ()
        let circuit = randomCircuit 42

        match Primitives.sampledExpectation (backend ()) circuit h with
        | Ok estimate ->
            Assert.True(abs (estimate.Value - exactObserve circuit h) < 1e-9)
            Assert.Equal(0.0, estimate.StandardError)
            Assert.Equal(None, estimate.ShotsPerCircuit)
        | Error e -> failwith e.Message

        // The exact route runs the circuit once and reads the state; it measures no groups.
        let counting =
            CloudStyleBackends.CountingBackend(LocalBackend.LocalBackend() :> IQuantumBackend)

        match Primitives.observe counting circuit h with
        | Ok value -> Assert.True(abs (value - exactObserve circuit h) < 1e-12)
        | Error e -> failwith e.Message

        Assert.Equal(1, counting.Executions)

    [<Fact>]
    let ``sample on a shot-sampling backend returns its measured counts without resampling`` () =
        let measured = Map.ofList [ "00", 13; "01", 5; "10", 1; "11", 981 ]
        let cloud = CloudStyleBackends.FixedHistogramCloud(measured, 2, 1000)

        // Azure keys are rightmost = qubit 0; sample keys are character q = qubit q.
        let expected = Map.ofList [ "00", 13; "10", 5; "01", 1; "11", 981 ]

        for _ in 1..3 do
            match Primitives.sample cloud (bell ()) 1000 with
            | Ok histogram -> Assert.Equal<Map<string, int>>(expected, histogram)
            | Error e -> failwith e.Message

        match Primitives.run cloud (bell ()) 1000 with
        | Ok shots ->
            Assert.Equal(1000, shots.Length)

            let counted =
                shots |> Array.countBy (Array.map string >> System.String.Concat) |> Map.ofArray

            Assert.Equal<Map<string, int>>(expected, counted)
        | Error e -> failwith e.Message

    [<Fact>]
    let ``sample on a shot-sampling backend refuses a shot count it did not measure`` () =
        let cloud =
            CloudStyleBackends.FixedHistogramCloud(Map.ofList [ "00", 1000 ], 2, 1000)

        match Primitives.sample cloud (bell ()) 2048 with
        | Error(QuantumError.ValidationError("shots", _)) -> ()
        | other -> failwith $"expected a shots ValidationError, got {other}"

        match Primitives.run cloud (bell ()) 10 with
        | Error(QuantumError.ValidationError("shots", _)) -> ()
        | other -> failwith $"expected a shots ValidationError, got {other}"

        Assert.Equal(0, cloud.Jobs)
