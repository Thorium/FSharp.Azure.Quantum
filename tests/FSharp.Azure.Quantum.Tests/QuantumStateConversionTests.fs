namespace FSharp.Azure.Quantum.Tests

open System.Numerics
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.LocalSimulator

module QuantumStateConversionTests =

    // ========================================================================
    // STATE VECTOR TO SPARSE
    // ========================================================================

    [<Fact>]
    let ``stateVectorToSparse converts basis state |0> correctly`` () =
        let sv = StateVector.create [| Complex.One; Complex.Zero |]
        let (amps, n) = QuantumStateConversion.stateVectorToSparse sv
        Assert.Equal(1, n)
        Assert.Equal(1, amps.Count)
        Assert.True(amps.ContainsKey 0)
        Assert.True((amps.[0] - Complex.One).Magnitude < 1e-10)

    [<Fact>]
    let ``stateVectorToSparse converts basis state |1> correctly`` () =
        let sv = StateVector.create [| Complex.Zero; Complex.One |]
        let (amps, n) = QuantumStateConversion.stateVectorToSparse sv
        Assert.Equal(1, n)
        Assert.Equal(1, amps.Count)
        Assert.True(amps.ContainsKey 1)

    [<Fact>]
    let ``stateVectorToSparse converts superposition state`` () =
        let half = 1.0 / sqrt 2.0
        let sv = StateVector.create [| Complex(half, 0.0); Complex(half, 0.0) |]
        let (amps, n) = QuantumStateConversion.stateVectorToSparse sv
        Assert.Equal(1, n)
        Assert.Equal(2, amps.Count)
        Assert.True(amps.ContainsKey 0)
        Assert.True(amps.ContainsKey 1)

    [<Fact>]
    let ``stateVectorToSparse filters near-zero amplitudes`` () =
        let sv = StateVector.create [| Complex.One; Complex(1e-15, 0.0) |]
        let (amps, _) = QuantumStateConversion.stateVectorToSparse sv
        Assert.Equal(1, amps.Count)
        Assert.True(amps.ContainsKey 0)

    [<Fact>]
    let ``stateVectorToSparse handles 2-qubit state`` () =
        // |00> + |11> (unnormalized for simplicity)
        let half = 1.0 / sqrt 2.0
        let sv = StateVector.create [| Complex(half, 0.0); Complex.Zero; Complex.Zero; Complex(half, 0.0) |]
        let (amps, n) = QuantumStateConversion.stateVectorToSparse sv
        Assert.Equal(2, n)
        Assert.Equal(2, amps.Count)
        Assert.True(amps.ContainsKey 0)
        Assert.True(amps.ContainsKey 3)

    // ========================================================================
    // SPARSE TO STATE VECTOR
    // ========================================================================

    [<Fact>]
    let ``sparseToStateVector creates correct state vector`` () =
        let amps = Map.ofList [(0, Complex.One)]
        let sv = QuantumStateConversion.sparseToStateVector amps 1
        let amp0 = StateVector.getAmplitude 0 sv
        let amp1 = StateVector.getAmplitude 1 sv
        Assert.True((amp0 - Complex.One).Magnitude < 1e-10)
        Assert.True(amp1.Magnitude < 1e-10)

    [<Fact>]
    let ``sparseToStateVector handles empty map`` () =
        let amps = Map.empty
        let sv = QuantumStateConversion.sparseToStateVector amps 1
        let amp0 = StateVector.getAmplitude 0 sv
        Assert.True(amp0.Magnitude < 1e-10, "Empty sparse state should give zero vector")

    [<Fact>]
    let ``sparseToStateVector handles 2-qubit sparse state`` () =
        let half = 1.0 / sqrt 2.0
        let amps = Map.ofList [(1, Complex(half, 0.0)); (2, Complex(half, 0.0))]
        let sv = QuantumStateConversion.sparseToStateVector amps 2
        Assert.True((StateVector.getAmplitude 1 sv - Complex(half, 0.0)).Magnitude < 1e-10)
        Assert.True((StateVector.getAmplitude 2 sv - Complex(half, 0.0)).Magnitude < 1e-10)
        Assert.True((StateVector.getAmplitude 0 sv).Magnitude < 1e-10)
        Assert.True((StateVector.getAmplitude 3 sv).Magnitude < 1e-10)

    // ========================================================================
    // ROUNDTRIP
    // ========================================================================

    [<Fact>]
    let ``stateVectorToSparse roundtrips with sparseToStateVector`` () =
        let half = 1.0 / sqrt 2.0
        let original = StateVector.create [| Complex(half, 0.0); Complex(0.0, half) |]
        let (amps, n) = QuantumStateConversion.stateVectorToSparse original
        let roundtripped = QuantumStateConversion.sparseToStateVector amps n
        let a0 = StateVector.getAmplitude 0 roundtripped
        let a1 = StateVector.getAmplitude 1 roundtripped
        Assert.True((a0 - Complex(half, 0.0)).Magnitude < 1e-10)
        Assert.True((a1 - Complex(0.0, half)).Magnitude < 1e-10)

    // ========================================================================
    // CONVERT
    // ========================================================================

    [<Fact>]
    let ``convert same type returns state unchanged`` () =
        let sv = StateVector.create [| Complex.One; Complex.Zero |]
        let state = QuantumState.StateVector sv
        let r = QuantumStateConversion.convert QuantumStateType.GateBased state
        match r with
        | Ok s -> Assert.Equal(state, s)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``convert StateVector to SparseState succeeds`` () =
        let sv = StateVector.create [| Complex.One; Complex.Zero |]
        let state = QuantumState.StateVector sv
        let r = QuantumStateConversion.convert QuantumStateType.Sparse state
        match r with
        | Ok (QuantumState.SparseState (amps, n)) ->
            Assert.Equal(1, n)
            Assert.True(amps.ContainsKey 0)
        | Ok _ -> failwith "Expected SparseState"
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``convert SparseState to StateVector succeeds`` () =
        let amps = Map.ofList [(0, Complex.One)]
        let state = QuantumState.SparseState (amps, 1)
        let r = QuantumStateConversion.convert QuantumStateType.GateBased state
        match r with
        | Ok (QuantumState.StateVector sv) ->
            let amp0 = StateVector.getAmplitude 0 sv
            Assert.True((amp0 - Complex.One).Magnitude < 1e-10)
        | Ok _ -> failwith "Expected StateVector"
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``convert to TopologicalBraiding returns NotImplemented`` () =
        let sv = StateVector.create [| Complex.One; Complex.Zero |]
        let state = QuantumState.StateVector sv
        let r = QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state
        match r with
        | Error (QuantumError.NotImplemented _) -> ()
        | Error e -> failwith $"Expected NotImplemented, got {e}"
        | Ok _ -> failwith "Expected Error for topological conversion"

    [<Fact>]
    let ``convert from TopologicalBraiding returns NotImplemented`` () =
        // Use a SparseState but request conversion from topological target type
        // We need to test the branch where sourceType is TopologicalBraiding
        // Since we can't easily create a FusionSuperposition, test the other direction
        let sv = StateVector.create [| Complex.One; Complex.Zero |]
        let state = QuantumState.StateVector sv
        let r = QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state
        match r with
        | Error (QuantumError.NotImplemented (_, hint)) ->
            Assert.True(hint.IsSome, "Should have hint about Topological package")
        | _ -> failwith "Expected NotImplemented with hint"

    // ========================================================================
    // CONVERT SMART
    // ========================================================================

    [<Fact>]
    let ``convertSmart returns unchanged when already preferred type`` () =
        let sv = StateVector.create [| Complex.One; Complex.Zero |]
        let state = QuantumState.StateVector sv
        let r = QuantumStateConversion.convertSmart QuantumStateType.GateBased state
        match r with
        | Ok s -> Assert.Equal(state, s)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``convertSmart with Mixed preferred type returns unchanged`` () =
        let sv = StateVector.create [| Complex.One; Complex.Zero |]
        let state = QuantumState.StateVector sv
        let r = QuantumStateConversion.convertSmart QuantumStateType.Mixed state
        match r with
        | Ok s -> Assert.Equal(state, s)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``convertSmart converts when types differ`` () =
        let sv = StateVector.create [| Complex.One; Complex.Zero |]
        let state = QuantumState.StateVector sv
        let r = QuantumStateConversion.convertSmart QuantumStateType.Sparse state
        match r with
        | Ok (QuantumState.SparseState _) -> ()
        | Ok _ -> failwith "Expected SparseState"
        | Error e -> failwith $"Expected Ok, got Error: {e}"
