namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.CircuitAbstraction

module TopologicalBackendTests =

    type private LoweringProbeExtension() =
        let mutable wasLowered = false

        member _.WasLowered = wasLowered

        interface ILowerToOperationsExtension with
            member _.Id = "tests.topological.lowering-probe"
            member _.LowerToGates () =
                wasLowered <- true
                [ CircuitBuilder.X 0 ]

    type private UnsupportedExtension() =
        interface IQuantumOperationExtension with
            member _.Id = "tests.topological.unsupported"
    
    // ========================================================================
    // CAPABILITY VALIDATION
    // ========================================================================
    
    [<Fact>]
    let ``Unified Topological backend should support lowering extension operations`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend
        let ext = LoweringProbeExtension() :> IQuantumOperationExtension
        Assert.True(backend.SupportsOperation (QuantumOperation.Extension ext))

    [<Fact>]
    let ``Unified Topological backend should not support unknown extension operations`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend
        let ext = UnsupportedExtension() :> IQuantumOperationExtension
        Assert.False(backend.SupportsOperation (QuantumOperation.Extension ext))

    [<Fact>]
    let ``Unified Topological backend should apply lowering extension by executing lowered gates`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend
 
        match backend.InitializeState 2 with
        | Error err ->
            Assert.True(false, sprintf "InitializeState failed: %A" err)
        | Ok initialState ->
            let probe = LoweringProbeExtension()
            let op = QuantumOperation.Extension (probe :> IQuantumOperationExtension)
 
            match backend.ApplyOperation op initialState with
            | Error err ->
                Assert.True(false, sprintf "ApplyOperation (extension) failed: %A" err)
            | Ok (QuantumState.FusionSuperposition fs) ->
                Assert.True(probe.WasLowered, "Expected LowerToGates() to be invoked")
                Assert.True(fs.IsNormalized, "Expected resulting topological state to be normalized")
            | Ok _ ->
                Assert.True(false, "Expected FusionSuperposition output")

    [<Fact>]
    let ``Unified Topological backend should support QPE intent operation`` () =
        // Note: CP/CRZ transpilation may require additional anyon resources.
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 40) :> IQuantumBackend
        let intent =
            {
                CountingQubits = 3
                TargetQubits = 1
                Unitary = QpeUnitary.TGate
                PrepareTargetOne = true
                ApplySwaps = false
            }

        Assert.True(backend.SupportsOperation (QuantumOperation.Algorithm (AlgorithmOperation.QPE intent)))

    [<Fact>]
    let ``Unified Topological backend should support HHL intent operation`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 40) :> IQuantumBackend
        let intent =
            {
                EigenvalueQubits = 2
                SolutionQubits = 1
                DiagonalEigenvalues = [| 2.0; 3.0 |]
                InversionMethod = HhlEigenvalueInversionMethod.ExactRotation 1.0
                MinEigenvalue = 1e-6
            }

        Assert.True(backend.SupportsOperation (QuantumOperation.Algorithm (AlgorithmOperation.HHL intent)))

    // ========================================================================
    // SUPPORTS-OPERATION FOR TRANSPILABLE GATES (Bug 3 fix)
    // ========================================================================

    [<Fact>]
    let ``SupportsOperation returns true for CZ gate after transpilation fix`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.CZ (0, 1))))

    [<Fact>]
    let ``SupportsOperation returns true for MCZ gate after transpilation fix`` () =
        // MCZ decomposes to H + CCX in first transpilation pass.
        // CCX requires a second transpilation pass to decompose to CNOT + T gates.
        // Multi-pass transpilation (transpileToFixpoint) now handles this correctly.
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend
        // MCZ is now supported via multi-pass transpilation
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.MCZ ([0; 1], 2))))

    [<Fact>]
    let ``SupportsOperation returns true for SWAP gate after transpilation fix`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.SWAP (0, 1))))

    [<Fact>]
    let ``SupportsOperation returns true for CCX gate after transpilation fix`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.CCX (0, 1, 2))))

    [<Fact>]
    let ``ApplyOperation CCX gate executes on Ising backend via transpile-then-route`` () =
        // CCX decomposes to {H, T, TDG, CNOT} elementary gates.
        // On Ising, T/TDG/H/CNOT are amplitude-intercepted — they must NOT reach braid compilation.
        // This test verifies the transpile-then-route fix in ApplyGate.
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend
        match backend.InitializeState 3 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok state ->
            // Apply CCX(0,1,2) — should succeed without "T† gate is not exact" error
            match backend.ApplyOperation (QuantumOperation.Gate (CircuitBuilder.CCX (0, 1, 2))) state with
            | Error err -> Assert.Fail($"CCX on Ising backend failed: {err}")
            | Ok (QuantumState.FusionSuperposition fs) ->
                Assert.True(fs.IsNormalized, "State should be normalized after CCX on |000⟩")
            | Ok other -> Assert.Fail($"Expected FusionSuperposition, got {other}")

    [<Fact>]
    let ``SupportsOperation returns true for elementary gates H X Z CNOT`` () =
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.H 0)))
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.X 0)))
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.Z 0)))
        Assert.True(backend.SupportsOperation (QuantumOperation.Gate (CircuitBuilder.CNOT (0, 1))))

    [<Fact>]
    let ``Unified Topological backend should apply QPE intent and return FusionSuperposition`` () =
        // Note: CP/CRZ transpilation may require additional anyon resources.
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 40) :> IQuantumBackend
        let intent =
            {
                CountingQubits = 3
                TargetQubits = 1
                Unitary = QpeUnitary.TGate
                PrepareTargetOne = true
                ApplySwaps = false
            }

        match backend.InitializeState 4 with
        | Error err ->
            Assert.True(false, sprintf "InitializeState failed: %A" err)
        | Ok initialState ->
            match backend.ApplyOperation (QuantumOperation.Algorithm (AlgorithmOperation.QPE intent)) initialState with
            | Error err ->
                Assert.True(false, sprintf "ApplyOperation (QPE intent) failed: %A" err)
            | Ok (QuantumState.FusionSuperposition fs) ->
                Assert.True(fs.IsNormalized, "Expected resulting topological state to be normalized")
            | Ok _ ->
                Assert.True(false, "Expected FusionSuperposition output")


    // ========================================================================
    // UNIFIED BACKEND: SUPPORTS-OPERATION QUERIES
    // ========================================================================

    [<Fact>]
    let ``Unified Ising backend supports Braid operations`` () =
        let backend = TopologicalUnifiedBackendFactory.createIsing 10
        Assert.True(backend.SupportsOperation (QuantumOperation.Braid 0))

    [<Fact>]
    let ``Unified Ising backend supports Measure operations`` () =
        let backend = TopologicalUnifiedBackendFactory.createIsing 10
        Assert.True(backend.SupportsOperation (QuantumOperation.Measure 0))

    [<Fact>]
    let ``Unified Ising backend supports FMove operations`` () =
        let backend = TopologicalUnifiedBackendFactory.createIsing 10
        Assert.True(backend.SupportsOperation (QuantumOperation.FMove (FMoveDirection.Forward, 1)))

    [<Fact>]
    let ``Unified Fibonacci backend supports Braid operations`` () =
        let backend = TopologicalUnifiedBackendFactory.createFibonacci 10
        Assert.True(backend.SupportsOperation (QuantumOperation.Braid 0))

    // ========================================================================
    // UNIFIED BACKEND: INITIALIZATION
    // ========================================================================

    [<Fact>]
    let ``InitializeState creates valid state for Ising anyons`` () =
        let backend = TopologicalUnifiedBackendFactory.createIsing 10

        match backend.InitializeState 2 with
        | Ok (QuantumState.FusionSuperposition fs) ->
            Assert.True(fs.IsNormalized, "Initialized state should be normalized")
        | Ok other ->
            Assert.Fail($"Expected FusionSuperposition, got {other}")
        | Error err ->
            Assert.Fail($"InitializeState failed: {err}")

    [<Fact>]
    let ``InitializeState fails when too many qubits requested`` () =
        // Backend with maxAnyons=4 cannot support many logical qubits
        let backend = TopologicalUnifiedBackendFactory.createIsing 4

        match backend.InitializeState 10 with
        | Error (QuantumError.ValidationError _) -> () // Expected
        | Error _ -> () // Any error is acceptable here
        | Ok _ ->
            Assert.Fail("Expected error for too many qubits")

    [<Fact>]
    let ``Two-qubit Ising initialization produces FusionSuperposition`` () =
        // Business-meaningful: σ × σ = 1 + ψ means 2-dimensional space
        let backend = TopologicalUnifiedBackendFactory.createIsing 10

        match backend.InitializeState 2 with
        | Ok (QuantumState.FusionSuperposition fs) ->
            Assert.True(fs.LogicalQubits >= 1, "Should encode at least 1 logical qubit")
            Assert.True(fs.IsNormalized, "State should be normalized")
        | Ok other ->
            Assert.Fail($"Expected FusionSuperposition, got {other}")
        | Error err ->
            Assert.Fail($"InitializeState failed: {err}")

    [<Fact>]
    let ``Four-qubit Ising initialization produces valid state`` () =
        let backend = TopologicalUnifiedBackendFactory.createIsing 20

        match backend.InitializeState 4 with
        | Ok (QuantumState.FusionSuperposition fs) ->
            Assert.True(fs.IsNormalized, "State should be normalized")
        | Ok other ->
            Assert.Fail($"Expected FusionSuperposition, got {other}")
        | Error err ->
            Assert.Fail($"InitializeState failed: {err}")

    // ========================================================================
    // UNIFIED BACKEND: BRAIDING OPERATIONS
    // ========================================================================

    [<Fact>]
    let ``Braid operation preserves normalization`` () =
        let backend = TopologicalUnifiedBackendFactory.createIsing 20

        match backend.InitializeState 2 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok initialState ->
            match backend.ApplyOperation (QuantumOperation.Braid 0) initialState with
            | Error err -> Assert.Fail($"Braid failed: {err}")
            | Ok (QuantumState.FusionSuperposition fs) ->
                Assert.True(fs.IsNormalized, "State should remain normalized after braiding")
            | Ok other ->
                Assert.Fail($"Expected FusionSuperposition, got {other}")

    [<Fact>]
    let ``Braid operation changes the quantum state`` () =
        // Business-meaningful: Braiding implements quantum gates
        let backend = TopologicalUnifiedBackendFactory.createIsing 20

        match backend.InitializeState 2 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok initialState ->
            match backend.ApplyOperation (QuantumOperation.Braid 0) initialState with
            | Error err -> Assert.Fail($"Braid failed: {err}")
            | Ok braidedState ->
                Assert.NotEqual(initialState, braidedState)

    [<Fact>]
    let ``Sequential braid operations compose correctly`` () =
        let backend = TopologicalUnifiedBackendFactory.createIsing 20

        match backend.InitializeState 3 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok state0 ->
            match backend.ApplyOperation (QuantumOperation.Braid 0) state0 with
            | Error err -> Assert.Fail($"First braid failed: {err}")
            | Ok state1 ->
                match backend.ApplyOperation (QuantumOperation.Braid 0) state1 with
                | Error err -> Assert.Fail($"Second braid failed: {err}")
                | Ok state2 ->
                    match state2 with
                    | QuantumState.FusionSuperposition fs ->
                        Assert.True(fs.IsNormalized, "State should remain normalized after sequential braids")
                    | other ->
                        Assert.Fail($"Expected FusionSuperposition, got {other}")

    // ========================================================================
    // UNIFIED BACKEND: MEASUREMENT
    // ========================================================================

    [<Fact>]
    let ``Measure operation produces valid FusionSuperposition`` () =
        let backend = TopologicalUnifiedBackendFactory.createIsing 20

        match backend.InitializeState 2 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok initialState ->
            match backend.ApplyOperation (QuantumOperation.Measure 0) initialState with
            | Error err -> Assert.Fail($"Measure failed: {err}")
            | Ok (QuantumState.FusionSuperposition fs) ->
                Assert.True(fs.IsNormalized, "State should be normalized after measurement")
            | Ok other ->
                Assert.Fail($"Expected FusionSuperposition, got {other}")

    [<Fact>]
    let ``Measure after braid produces valid state`` () =
        // Business-meaningful: σ × σ = 1 + ψ
        let backend = TopologicalUnifiedBackendFactory.createIsing 20

        match backend.InitializeState 2 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok state0 ->
            match backend.ApplyOperation (QuantumOperation.Braid 0) state0 with
            | Error err -> Assert.Fail($"Braid failed: {err}")
            | Ok state1 ->
                match backend.ApplyOperation (QuantumOperation.Measure 0) state1 with
                | Error err -> Assert.Fail($"Measure failed: {err}")
                | Ok (QuantumState.FusionSuperposition fs) ->
                    Assert.True(fs.IsNormalized, "Post-measurement state should be normalized")
                | Ok other ->
                    Assert.Fail($"Expected FusionSuperposition, got {other}")

    // ========================================================================
    // UNIFIED BACKEND: SEQUENCE OPERATIONS
    // ========================================================================

    [<Fact>]
    let ``Sequence of braids executes correctly`` () =
        let backend = TopologicalUnifiedBackendFactory.createIsing 20

        match backend.InitializeState 3 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok initialState ->
            let program = QuantumOperation.Sequence [
                QuantumOperation.Braid 0
                QuantumOperation.Braid 0
                QuantumOperation.Braid 0
            ]
            match backend.ApplyOperation program initialState with
            | Error err -> Assert.Fail($"Sequence failed: {err}")
            | Ok (QuantumState.FusionSuperposition fs) ->
                Assert.True(fs.IsNormalized, "State should be normalized after sequence")
            | Ok other ->
                Assert.Fail($"Expected FusionSuperposition, got {other}")

    [<Fact>]
    let ``Sequence with braid and measure executes correctly`` () =
        let backend = TopologicalUnifiedBackendFactory.createIsing 20

        match backend.InitializeState 2 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok initialState ->
            let program = QuantumOperation.Sequence [
                QuantumOperation.Braid 0
                QuantumOperation.Measure 0
            ]
            match backend.ApplyOperation program initialState with
            | Error err -> Assert.Fail($"Sequence failed: {err}")
            | Ok (QuantumState.FusionSuperposition fs) ->
                Assert.True(fs.IsNormalized, "State should be normalized after braid+measure")
            | Ok other ->
                Assert.Fail($"Expected FusionSuperposition, got {other}")

    [<Fact>]
    let ``Complex sequence with interleaved braids and measures`` () =
        // Business-meaningful: Simulate a simple topological quantum circuit
        let backend = TopologicalUnifiedBackendFactory.createIsing 20

        match backend.InitializeState 3 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok initialState ->
            let program = QuantumOperation.Sequence [
                QuantumOperation.Braid 0   // Braid first pair
                QuantumOperation.Braid 0   // Braid first pair again
                QuantumOperation.Measure 0 // Measure first fusion
                QuantumOperation.Braid 0   // Continue
                QuantumOperation.Measure 0 // Measure again
            ]
            match backend.ApplyOperation program initialState with
            | Error err -> Assert.Fail($"Complex sequence failed: {err}")
            | Ok (QuantumState.FusionSuperposition fs) ->
                Assert.True(fs.IsNormalized, "State should be normalized after complex sequence")
            | Ok other ->
                Assert.Fail($"Expected FusionSuperposition, got {other}")

    [<Fact>]
    let ``Empty sequence preserves state`` () =
        let backend = TopologicalUnifiedBackendFactory.createIsing 20

        match backend.InitializeState 2 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok initialState ->
            let program = QuantumOperation.Sequence []
            match backend.ApplyOperation program initialState with
            | Error err -> Assert.Fail($"Empty sequence failed: {err}")
            | Ok finalState ->
                Assert.Equal(initialState, finalState)

    // ========================================================================
    // UNIFIED BACKEND: FIBONACCI ANYONS
    // ========================================================================

    [<Fact>]
    let ``Fibonacci backend initializes valid state`` () =
        let backend = TopologicalUnifiedBackendFactory.createFibonacci 20

        match backend.InitializeState 2 with
        | Ok (QuantumState.FusionSuperposition fs) ->
            Assert.True(fs.IsNormalized, "Fibonacci state should be normalized")
        | Ok other ->
            Assert.Fail($"Expected FusionSuperposition, got {other}")
        | Error err ->
            Assert.Fail($"InitializeState failed: {err}")

    [<Fact>]
    let ``Fibonacci anyons support braiding via unified API`` () =
        // Business-meaningful: τ × τ = 1 + τ
        let backend = TopologicalUnifiedBackendFactory.createFibonacci 20

        match backend.InitializeState 2 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok initialState ->
            match backend.ApplyOperation (QuantumOperation.Braid 0) initialState with
            | Error err -> Assert.Fail($"Braid failed: {err}")
            | Ok braidedState ->
                Assert.NotEqual(initialState, braidedState)
                match braidedState with
                | QuantumState.FusionSuperposition fs ->
                    Assert.True(fs.IsNormalized, "Fibonacci braided state should be normalized")
                | other ->
                    Assert.Fail($"Expected FusionSuperposition, got {other}")

    [<Fact>]
    let ``Fibonacci backend supports sequence operations`` () =
        let backend = TopologicalUnifiedBackendFactory.createFibonacci 20

        match backend.InitializeState 2 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok initialState ->
            let program = QuantumOperation.Sequence [
                QuantumOperation.Braid 0
                QuantumOperation.Braid 0
                QuantumOperation.Measure 0
            ]
            match backend.ApplyOperation program initialState with
            | Error err -> Assert.Fail($"Fibonacci sequence failed: {err}")
            | Ok (QuantumState.FusionSuperposition fs) ->
                Assert.True(fs.IsNormalized, "State should be normalized after Fibonacci sequence")
            | Ok other ->
                Assert.Fail($"Expected FusionSuperposition, got {other}")

    // ========================================================================
    // QUANTUM STATE CONVERSION (Bug 2 fix)
    // ========================================================================

    [<Fact>]
    let ``FusionSuperposition converts to GateBased StateVector`` () =
        // Test conversion from FusionSuperposition to GateBased using the ground state.
        // Note: Off-diagonal gates (H, X) cannot be faithfully implemented by Ising
        // anyon braiding alone (the S-K base set is diagonal-only), so we test with
        // the initial |0⟩ state which has a well-defined amplitude vector.
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend

        match backend.InitializeState 1 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok fusionState ->
            // Convert FusionSuperposition → GateBased
            match QuantumStateConversion.convert QuantumStateType.GateBased fusionState with
            | Error err -> Assert.Fail($"Conversion to GateBased failed: {err}")
            | Ok (QuantumState.StateVector sv) ->
                // Should be a 1-qubit state with 2 amplitudes
                let n = FSharp.Azure.Quantum.LocalSimulator.StateVector.numQubits sv
                Assert.Equal(1, n)
                // Ground state: amplitude 1.0 at |0⟩, 0 at |1⟩
                let amp0 = FSharp.Azure.Quantum.LocalSimulator.StateVector.getAmplitude 0 sv
                let amp1 = FSharp.Azure.Quantum.LocalSimulator.StateVector.getAmplitude 1 sv
                Assert.True(abs (amp0.Magnitude - 1.0) < 1e-6,
                    $"|0⟩ amplitude magnitude should be ~1.0, got {amp0.Magnitude}")
                Assert.True(amp1.Magnitude < 1e-6,
                    $"|1⟩ amplitude magnitude should be ~0, got {amp1.Magnitude}")
            | Ok other ->
                Assert.Fail($"Expected StateVector, got {other}")

    [<Fact>]
    let ``FusionSuperposition converts to Sparse state`` () =
        // Test conversion from FusionSuperposition to Sparse using ground state.
        // The sparse representation should contain only the |0⟩ basis state.
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend

        match backend.InitializeState 1 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok fusionState ->
            // Convert FusionSuperposition → Sparse
            match QuantumStateConversion.convert QuantumStateType.Sparse fusionState with
            | Error err -> Assert.Fail($"Conversion to Sparse failed: {err}")
            | Ok (QuantumState.SparseState (amps, n)) ->
                Assert.Equal(1, n)
                // Ground state should have non-zero amplitude only at |0⟩
                Assert.True(amps.ContainsKey 0, "Sparse state should have amplitude for |0⟩")
                Assert.True(abs (amps.[0].Magnitude - 1.0) < 1e-6,
                    $"|0⟩ sparse amplitude should be ~1.0, got {amps.[0].Magnitude}")
            | Ok other ->
                Assert.Fail($"Expected SparseState, got {other}")

    [<Fact>]
    let ``FusionSuperposition initial state converts to ground state`` () =
        // |0⟩ state should convert to StateVector with amplitude 1.0 at index 0
        let backend = TopologicalUnifiedBackend.TopologicalUnifiedBackend(AnyonSpecies.AnyonType.Ising, 10) :> IQuantumBackend

        match backend.InitializeState 2 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok initialState ->
            match QuantumStateConversion.convert QuantumStateType.GateBased initialState with
            | Error err -> Assert.Fail($"Conversion failed: {err}")
            | Ok (QuantumState.StateVector sv) ->
                let n = FSharp.Azure.Quantum.LocalSimulator.StateVector.numQubits sv
                Assert.Equal(2, n)
                // Ground state: amplitude 1.0 at |00⟩, 0 everywhere else
                let amp0 = FSharp.Azure.Quantum.LocalSimulator.StateVector.getAmplitude 0 sv
                Assert.True(abs (amp0.Magnitude - 1.0) < 1e-6,
                    $"|00⟩ amplitude should be ~1.0, got {amp0.Magnitude}")
                for i in 1..3 do
                    let amp = FSharp.Azure.Quantum.LocalSimulator.StateVector.getAmplitude i sv
                    Assert.True(amp.Magnitude < 1e-6,
                        $"|{i}⟩ amplitude should be ~0, got {amp.Magnitude}")
            | Ok other ->
                Assert.Fail($"Expected StateVector, got {other}")

    [<Fact>]
    let ``Conversion to TopologicalBraiding returns NotImplemented`` () =
        // Creating a gate-based state and trying to convert TO TopologicalBraiding should fail
        let sv = FSharp.Azure.Quantum.LocalSimulator.StateVector.init 1
        let state = QuantumState.StateVector sv

        match QuantumStateConversion.convert QuantumStateType.TopologicalBraiding state with
        | Error (QuantumError.NotImplemented _) -> () // Expected
        | Error err -> Assert.Fail($"Expected NotImplemented error, got: {err}")
        | Ok _ -> Assert.Fail("Conversion to TopologicalBraiding should return NotImplemented")
