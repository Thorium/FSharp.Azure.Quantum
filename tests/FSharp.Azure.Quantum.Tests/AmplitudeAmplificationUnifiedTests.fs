namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.LocalSimulator

/// Tests for Unified Backend amplitude amplification.
module AmplitudeAmplificationUnifiedTests =

    let createLocalBackend () : IQuantumBackend =
        LocalBackend.LocalBackend() :> IQuantumBackend

    type private NoGroverIntentBackend(inner: IQuantumBackend) =
        interface IQuantumBackend with
            member _.ExecuteToState circuit = inner.ExecuteToState circuit
            member _.NativeStateType = inner.NativeStateType
            member _.ApplyOperation operation state = inner.ApplyOperation operation state

            member _.SupportsOperation operation =
                match operation with
                | QuantumOperation.Algorithm (AlgorithmOperation.GroverPrepare _)
                | QuantumOperation.Algorithm (AlgorithmOperation.GroverOraclePhaseFlip _)
                | QuantumOperation.Algorithm (AlgorithmOperation.GroverDiffusion _) -> false
                | _ -> inner.SupportsOperation operation

            member _.Name = inner.Name + " (no-grover-intent)"
            member _.InitializeState numQubits = inner.InitializeState numQubits

    let private hadamardPrepCircuit (numQubits: int) : CircuitBuilder.Circuit =
        let circuit = CircuitBuilder.empty numQubits
        [ 0 .. numQubits - 1 ]
        |> List.map CircuitBuilder.H
        |> List.fold (fun c g -> CircuitBuilder.addGate g c) circuit

    [<Fact>]
    let ``Amplitude amplification planner prefers Grover intents for uniform superposition`` () =
        let backend = createLocalBackend ()

        match Oracle.forValue 5 3 with
        | Error err ->
            Assert.True(false, $"Oracle compilation failed: {err}")
        | Ok oracle ->
            let intent: AmplitudeAmplification.Unified.AmplitudeAmplificationIntent =
                {
                    NumQubits = 3
                    StatePreparation = hadamardPrepCircuit 3
                    Oracle = oracle
                    Iterations = 1
                    Exactness = AmplitudeAmplification.Unified.Exact
                }

            match AmplitudeAmplification.Unified.plan backend intent with
            | Ok (AmplitudeAmplification.Unified.AmplitudeAmplificationPlan.ExecuteViaGroverIntents _) ->
                Assert.True(true)
            | Ok _ ->
                Assert.True(false, "Expected ExecuteViaGroverIntents plan")
            | Error err ->
                Assert.True(false, $"Planning failed: {err}")

    [<Fact>]
    let ``Amplitude amplification planner lowers to ops when Grover intents are unsupported`` () =
        let backend = createLocalBackend () |> NoGroverIntentBackend :> IQuantumBackend

        match Oracle.forValue 5 3 with
        | Error err ->
            Assert.True(false, $"Oracle compilation failed: {err}")
        | Ok oracle ->
            let intent: AmplitudeAmplification.Unified.AmplitudeAmplificationIntent =
                {
                    NumQubits = 3
                    StatePreparation = hadamardPrepCircuit 3
                    Oracle = oracle
                    Iterations = 1
                    Exactness = AmplitudeAmplification.Unified.Exact
                }

            match AmplitudeAmplification.Unified.plan backend intent with
            | Ok (AmplitudeAmplification.Unified.AmplitudeAmplificationPlan.ExecuteViaOps (prepOps, iterationOps, iterations, exactness)) ->
                Assert.Equal(1, iterations)
                Assert.Equal(AmplitudeAmplification.Unified.Exact, exactness)
                Assert.NotEmpty prepOps
                Assert.NotEmpty iterationOps

                let allOps = prepOps @ iterationOps
                Assert.True(allOps |> List.forall backend.SupportsOperation)
            | Ok _ ->
                Assert.True(false, "Expected ExecuteViaOps plan")
            | Error err ->
                Assert.True(false, $"Planning failed: {err}")

    [<Fact>]
    let ``Amplitude amplification execute runs on LocalBackend`` () =
        let backend = createLocalBackend ()

        match Oracle.forValue 5 3 with
        | Error err ->
            Assert.True(false, $"Oracle compilation failed: {err}")
        | Ok oracle ->
            let intent: AmplitudeAmplification.Unified.AmplitudeAmplificationIntent =
                {
                    NumQubits = 3
                    StatePreparation = hadamardPrepCircuit 3
                    Oracle = oracle
                    Iterations = 1
                    Exactness = AmplitudeAmplification.Unified.Exact
                }

            match AmplitudeAmplification.Unified.execute backend intent with
            | Error err ->
                Assert.True(false, $"Execution failed: {err}")
            | Ok finalState ->
                // Basic smoke assertion: we got a state back.
                // LocalBackend uses the StateVector representation.
                match finalState with
                | QuantumState.StateVector sv -> Assert.Equal(3, StateVector.numQubits sv)
                | _ -> Assert.True(false, "Expected StateVector quantum state")
