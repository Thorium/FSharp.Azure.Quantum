namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.DWaveBackend
open FSharp.Azure.Quantum.Backends.DWaveTypes

module DWaveAnnealingIntentTests =

    [<Fact>]
    let ``DWave backend advertises and executes annealing intent extension`` () =
        let backend = createDefaultMockBackend () :> IQuantumBackend

        let ising: IsingProblem =
            {
                LinearCoeffs = Map.ofList [ 0, -1.0; 1, 0.5 ]
                QuadraticCoeffs = Map.ofList [ (0, 1), -1.25 ]
                Offset = 0.0
            }

        let intent = AnnealIsingOperation(ising, 25, seed = 42) :> IQuantumOperationExtension
        let op = QuantumOperation.Extension intent

        Assert.True(backend.SupportsOperation op)

        match backend.InitializeState 2 with
        | Error err -> Assert.Fail(err.Message)
        | Ok initialState ->
            match backend.ApplyOperation op initialState with
            | Error err -> Assert.Fail(err.Message)
            | Ok (QuantumState.IsingSamples (_, solutionsObj)) ->
                let solutions = solutionsObj :?> DWaveSolution list
                Assert.NotEmpty(solutions)
            | Ok other -> Assert.Fail($"Expected IsingSamples state, got {QuantumState.stateType other}")

    [<Fact>]
    let ``DWave backend supports sequences of annealing intent operations`` () =
        let backend = createDefaultMockBackend () :> IQuantumBackend

        let ising: IsingProblem =
            {
                LinearCoeffs = Map.ofList [ 0, -1.0 ]
                QuadraticCoeffs = Map.empty
                Offset = 0.0
            }

        let intent = AnnealIsingOperation(ising, 5, seed = 42) :> IQuantumOperationExtension
        let op = QuantumOperation.Sequence [ QuantumOperation.Extension intent ]

        match backend.InitializeState 1 with
        | Error err -> Assert.Fail(err.Message)
        | Ok initialState ->
            match backend.ApplyOperation op initialState with
            | Error err -> Assert.Fail(err.Message)
            | Ok (QuantumState.IsingSamples _) -> Assert.True(true)
            | Ok other -> Assert.Fail($"Expected IsingSamples state, got {QuantumState.stateType other}")
