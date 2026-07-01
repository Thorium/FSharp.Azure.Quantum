namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.MachineLearning
open FSharp.Azure.Quantum.Backends.DensityMatrixSimulator

/// Tests for the density-matrix noisy simulator.
module NoisyBackendTests =

    let private bell () =
        CircuitBuilder.empty 2
        |> CircuitBuilder.addGate (CircuitBuilder.H 0)
        |> CircuitBuilder.addGate (CircuitBuilder.CNOT (0, 1))

    let private sampleWith (config: NoiseConfig) (shots: int) : Map<string, int> =
        let backend = NoisyLocalBackend(config) :> IQuantumBackend
        match Primitives.sample backend (bell ()) shots with
        | Ok histogram -> histogram
        | Error e -> failwith $"sample failed: {e.Message}"

    [<Fact>]
    let ``noiseless density-matrix simulation reproduces the pure Bell state`` () =
        let histogram = sampleWith noiseless 4000
        // A pure Bell state only ever measures 00 or 11.
        let keys = histogram |> Map.toList |> List.map fst |> Set.ofList
        Assert.True(Set.isSubset keys (Set.ofList [ "00"; "11" ]), $"unexpected outcomes: {keys}")
        Assert.True(histogram.["00"] > 1500 && histogram.["11"] > 1500)

    [<Fact>]
    let ``depolarizing noise leaks probability into the anti-correlated outcomes`` () =
        let histogram = sampleWith (depolarizing 0.1 0.1) 4000
        // With noise, the forbidden Bell outcomes 01/10 acquire non-zero probability.
        let leak =
            (histogram |> Map.tryFind "01" |> Option.defaultValue 0)
            + (histogram |> Map.tryFind "10" |> Option.defaultValue 0)
        Assert.True(leak > 0, "expected depolarizing noise to leak into 01/10")

    [<Fact>]
    let ``more noise leaks more probability`` () =
        let leakOf (p: float) =
            let h = sampleWith (depolarizing p p) 6000
            (h |> Map.tryFind "01" |> Option.defaultValue 0) + (h |> Map.tryFind "10" |> Option.defaultValue 0)
        Assert.True(leakOf 0.2 > leakOf 0.05, "20% depolarizing should leak more than 5%")

    [<Fact>]
    let ``the density matrix has unit trace (probability is conserved)`` () =
        match simulate (depolarizing 0.1 0.1) (bell ()) with
        | Error e -> failwith $"simulate failed: {e.Message}"
        | Ok (rho, n) ->
            let dim = 1 <<< n
            let trace = [ 0 .. dim - 1 ] |> List.sumBy (fun i -> rho.[i, i].Real)
            Assert.Equal(1.0, trace, 6)

    [<Fact>]
    let ``observe works on the density-matrix backend and decays under noise`` () =
        // ⟨Z₀Z₁⟩ = 1 for an ideal Bell state; depolarizing noise reduces the correlation.
        let zz : TrotterSuzuki.PauliHamiltonian =
            { Terms = [ { Operators = [| 'Z'; 'Z' |]; Coefficient = System.Numerics.Complex(1.0, 0.0) } ]; NumQubits = 2 }
        let ideal = NoisyLocalBackend(noiseless) :> IQuantumBackend
        let noisy = NoisyLocalBackend(depolarizing 0.1 0.1) :> IQuantumBackend
        match Primitives.observe ideal (bell ()) zz, Primitives.observe noisy (bell ()) zz with
        | Ok clean, Ok degraded ->
            Assert.Equal(1.0, clean, 6)
            Assert.True(degraded < 0.95 && degraded > 0.0, $"noisy ⟨Z₀Z₁⟩ should be reduced but positive, got {degraded}")
        | _ -> failwith "observe failed on the density-matrix backend"

    [<Fact>]
    let ``a quantum ML kernel runs end-to-end on the noisy density-matrix backend`` () =
        // The ML path (quantum kernel) is parameterised on IQuantumBackend, so it runs on the
        // noisy backend too. K(x, x) should be near 1 (identical points), slightly reduced by noise.
        let noisy = NoisyLocalBackend(depolarizing 0.02 0.02) :> IQuantumBackend
        let x = [| 0.4; 0.7 |]
        match QuantumKernels.computeKernel noisy FeatureMapType.AngleEncoding x x 1000 with
        | Error e -> failwith $"noisy kernel failed: {e.Message}"
        | Ok k ->
            Assert.True(k >= 0.0 && k <= 1.0, $"kernel value out of range: {k}")
            Assert.True(k > 0.7, $"K(x,x) should be near 1 even with light noise, got {k}")

    [<Fact>]
    let ``density-matrix simulation rejects circuits that are too large`` () =
        let big = CircuitBuilder.empty 9 |> CircuitBuilder.addGate (CircuitBuilder.H 0)
        match simulate noiseless big with
        | Error (QuantumError.ValidationError ("numQubits", _)) -> ()
        | other -> failwith $"expected a numQubits ValidationError, got: {other}"

    [<Fact>]
    let ``a gate on an out-of-range qubit returns Error, not an exception`` () =
        // addGate does not validate qubit indices, so a circuit can reference a qubit ≥ QubitCount.
        // The failure must surface through the Result channel, not escape as an exception.
        let bad = CircuitBuilder.empty 2 |> CircuitBuilder.addGate (CircuitBuilder.X 5)
        match simulate noiseless bad with
        | Error _ -> ()
        | Ok _ -> failwith "expected an Error for a gate referencing a qubit outside the register"
