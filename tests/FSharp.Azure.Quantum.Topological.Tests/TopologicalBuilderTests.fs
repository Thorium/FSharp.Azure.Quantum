namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open System.Numerics
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

module TopologicalBuilderTests =

    [<Fact>]
    let ``Builder module exists and is accessible`` () =
        // This test verifies the builder infrastructure is in place
        let backend =
            TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 10

        let builder = topological backend
        Assert.NotNull(builder)

    [<Fact>]
    let ``Builder can execute simple program`` () =
        task {
            let backend =
                TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 10

            let program =
                topological backend {
                    do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
                    return ()
                }


            match! TopologicalBuilder.executeWithContext backend program with
            | Ok(_, ctx) ->
                Assert.Equal<TopologicalBuilder.OperationRecord list>(
                    [ TopologicalBuilder.Init(AnyonSpecies.AnyonType.Ising, 4) ],
                    List.rev ctx.History
                )
            | Error err -> Assert.Fail($"Program failed: {err.Message}")
        }

    [<Fact>]
    let ``Builder operations thread state correctly`` () =
        task {
            let backend =
                TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 10

            let program =
                topological backend {
                    do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
                    do! TopologicalBuilder.braid 0
                    do! TopologicalBuilder.braid 2
                    return ()
                }

            match! TopologicalBuilder.executeWithContext backend program with
            | Ok(_, ctx) ->
                // "Threaded correctly" means the context carried every operation
                // through in program order — not merely that nothing errored.
                Assert.Equal<TopologicalBuilder.OperationRecord list>(
                    [
                        TopologicalBuilder.Init(AnyonSpecies.AnyonType.Ising, 4)
                        TopologicalBuilder.Braid 0
                        TopologicalBuilder.Braid 2
                    ],
                    List.rev ctx.History
                )

                // ...and that the final state is the braided one, still normalized.
                match ctx.CurrentState with
                | QuantumState.FusionSuperposition fs ->
                    match TopologicalOperations.fromInterface fs with
                    | Some superposition ->
                        let norm =
                            superposition.Terms
                            |> List.sumBy (fun (amp, _) -> amp.Magnitude * amp.Magnitude)

                        Assert.Equal(1.0, norm, 10)
                    | None -> Assert.Fail("Could not unwrap final superposition")
                | other -> Assert.Fail($"Expected FusionSuperposition, got {other}")
            | Error err -> Assert.Fail($"Program failed: {err.Message}")
        }

    [<Fact>]
    let ``Builder works with ANY backend (backend-agnostic principle)`` () =
        task {
            // This test demonstrates the key architectural principle:
            // Programs work with ANY IQuantumBackend implementation

            // Test with simulator backend
            let simulatorBackend =
                TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 10

            let program =
                topological simulatorBackend {
                    do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
                    do! TopologicalBuilder.braid 0
                    return ()
                }

            match! TopologicalBuilder.executeWithContext simulatorBackend program with
            | Ok(_, ctx) ->
                Assert.Equal<TopologicalBuilder.OperationRecord list>(
                    [
                        TopologicalBuilder.Init(AnyonSpecies.AnyonType.Ising, 4)
                        TopologicalBuilder.Braid 0
                    ],
                    List.rev ctx.History
                )
            | Error err -> Assert.Fail($"Program failed: {err.Message}")

            // In future: Same program will work with hardware backend!
            // let hardwareBackend = MicrosoftMajoranaBackend.create(...)
            // let! hardwareResult = topological hardwareBackend { ... }
            // Programs are COMPLETELY backend-agnostic!
        }

    [<Fact>]
    let ``Builder with braiding sequence`` () =
        task {
            // Increased backend capacity to 20 anyons to support 6 logical qubits
            let backend =
                TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 20

            let topology = topological backend

            let program =
                topology {
                    do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 6
                    do! TopologicalBuilder.braid 0
                    do! TopologicalBuilder.braid 2
                    do! TopologicalBuilder.braid 4
                    return ()
                }

            match! TopologicalBuilder.executeWithContext backend program with
            | Ok(_, ctx) ->
                Assert.Equal<TopologicalBuilder.OperationRecord list>(
                    [
                        TopologicalBuilder.Init(AnyonSpecies.AnyonType.Ising, 6)
                        TopologicalBuilder.Braid 0
                        TopologicalBuilder.Braid 2
                        TopologicalBuilder.Braid 4
                    ],
                    List.rev ctx.History
                )
            | Error err -> Assert.Fail($"Program failed: {err.Message}")
        }

    // ============================================================================
    // MULTI-TERM SUPERPOSITION MEASUREMENT TESTS
    // ============================================================================

    [<Fact>]
    let ``Builder measure works on pure state (single term)`` () =
        task {
            let backend =
                TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 10

            let program =
                topological backend {
                    do! TopologicalBuilder.initialize AnyonSpecies.AnyonType.Ising 4
                    let! outcome = TopologicalBuilder.measure 0
                    return outcome
                }


            match! TopologicalBuilder.execute backend program with
            | Ok outcome ->
                // A σ-pair of the |0000⟩ encoding fuses deterministically to the vacuum,
                // so the measurement outcome is not merely "some particle" — it is fixed.
                Assert.Equal(AnyonSpecies.Particle.Vacuum, outcome)
            | Error err -> Assert.Fail($"Pure state measurement failed: {err.Message}")
        }

    [<Fact>]
    let ``Builder measure works on multi-term superposition after F-move`` () =
        task {
            // Apply an F-move via the backend to produce a genuine multi-term
            // superposition, then measure it through the builder.
            //
            // The anyon theory matters here. In the Ising |0…0⟩ encoding every
            // intermediate charge is abelian (1 or ψ), so every F-matrix is 1×1 and
            // the F-move is the identity — it can never create a superposition. The
            // Fibonacci |111⟩ encoding fuses three τ charges, whose F-matrix
            // (F^τττ_τ) is the 2×2 golden-ratio matrix, so the F-move is genuinely
            // non-trivial there.
            let anyonType = AnyonSpecies.AnyonType.Fibonacci

            let backend = TopologicalUnifiedBackendFactory.createUnified anyonType 20

            match FusionTree.fromComputationalBasis [ 1; 1; 1 ] anyonType with
            | Error err -> Assert.Fail($"Encoding failed: {err.Message}")
            | Ok tree ->
                let pure' = TopologicalOperations.pureState (FusionTree.create tree anyonType)

                let initState =
                    QuantumState.FusionSuperposition(TopologicalOperations.toInterface pure')

                match backend.ApplyOperation (QuantumOperation.FMove(FMoveDirection.Forward, 0)) initState with
                | Error err -> Assert.Fail($"F-move failed: {err.Message}")
                | Ok fMovedState ->

                    match fMovedState with
                    | QuantumState.FusionSuperposition fs ->
                        match TopologicalOperations.fromInterface fs with
                        | None -> Assert.Fail("Could not unwrap superposition")
                        | Some superposition ->
                            // The F-move must actually branch — no escape hatch.
                            Assert.Equal(2, superposition.Terms.Length)

                            // ...and it is a unitary basis change, so the norm is preserved.
                            let norm =
                                superposition.Terms
                                |> List.sumBy (fun (amp, _) -> amp.Magnitude * amp.Magnitude)

                            Assert.Equal(1.0, norm, 10)

                            let ctx: TopologicalBuilder.BuilderContext =
                                {
                                    Backend = backend
                                    CurrentState = fMovedState
                                    MeasurementResults = []
                                    ExecutionLog = []
                                    History = []
                                }

                            match! TopologicalBuilder.measure 0 ctx with
                            | Ok(particle, newCtx) ->
                                // Both terms of F^τττ_τ keep the leftmost pair fusing to τ,
                                // so the outcome is deterministic even though the state is not.
                                Assert.Equal(AnyonSpecies.Particle.Tau, particle)
                                Assert.NotEmpty(newCtx.MeasurementResults)
                            | Error err -> Assert.Fail($"Multi-term measurement should succeed but got: {err.Message}")
                    | _ -> Assert.Fail("Expected FusionSuperposition state")
        }

    [<Fact>]
    let ``Builder measure on superposition returns valid particle type`` () =
        task {
            let backend =
                TopologicalUnifiedBackendFactory.createUnified AnyonSpecies.AnyonType.Ising 10

            // Create multi-term superposition manually
            let sigma = AnyonSpecies.Particle.Sigma
            let vacuum = AnyonSpecies.Particle.Vacuum
            let tree1 = FusionTree.fuse (FusionTree.leaf sigma) (FusionTree.leaf sigma) vacuum

            let tree2 =
                FusionTree.fuse (FusionTree.leaf sigma) (FusionTree.leaf sigma) AnyonSpecies.Particle.Psi

            let state1 = FusionTree.create tree1 AnyonSpecies.AnyonType.Ising
            let state2 = FusionTree.create tree2 AnyonSpecies.AnyonType.Ising

            let superposition: TopologicalOperations.Superposition =
                {
                    Terms =
                        [
                            (Complex(1.0 / sqrt 2.0, 0.0), state1)
                            (Complex(1.0 / sqrt 2.0, 0.0), state2)
                        ]
                    AnyonType = AnyonSpecies.AnyonType.Ising
                }

            let multiTermState =
                QuantumState.FusionSuperposition(TopologicalOperations.toInterface superposition)

            let ctx: TopologicalBuilder.BuilderContext =
                {
                    Backend = backend
                    CurrentState = multiTermState
                    MeasurementResults = []
                    ExecutionLog = []
                    History = []
                }

            match! TopologicalBuilder.measure 0 ctx with
            | Ok(particle, _) ->
                // Result should be a valid Ising particle
                let validParticles =
                    [
                        AnyonSpecies.Particle.Vacuum
                        AnyonSpecies.Particle.Sigma
                        AnyonSpecies.Particle.Psi
                    ]

                Assert.Contains(particle, validParticles)
            | Error err -> Assert.Fail($"Multi-term measurement failed: {err.Message}")
        }
