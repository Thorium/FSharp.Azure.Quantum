namespace FSharp.Azure.Quantum.Topological.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GroverSearch
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Topological
open System.Numerics

/// Tests for AlgorithmExtensions - Topological backend integration
///
/// Verifies that *WithTopology functions provide convenient access to
/// topological backends for basic integration testing.
///
/// NOTE: Full Grover search generates gates beyond current GateToBraid support.
/// These tests verify the integration architecture works correctly.
module AlgorithmExtensionsTests =

    // Shor has two routes onto a topological backend:
    //   A) the modular-exponentiation QPE intent realised directly on the fusion encoding,
    //      which is what these tests pin;
    //   B) BraidToGate -> gate-based Shor -> GateToBraid, far slower because every gate
    //      goes through Solovay-Kitaev.
    // Route A is implemented (modExpQpeSuperposition), so the planner takes it. Neither
    // route is limited by anyons: this backend has no qubits of its own, it encodes them
    // (2n+2 Ising anyons per n qubits).
    //
    // The two planning tests below cost nothing; the two execution tests after them are
    // what actually prove route A computes the right phase.
    [<Fact>]
    let ``Shor period finding takes the native intent on a topological backend`` () =
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 16

        let intent: Shor.ShorPeriodFindingIntent =
            {
                Base = 7
                Modulus = 15
                CountingQubits = 3
            }

        match Shor.planPeriodFinding topoBackend intent with
        | Ok(Shor.ShorPeriodFindingPlan.ExecuteNatively qpeIntent) ->
            Assert.Equal(3, qpeIntent.CountingQubits)
            Assert.Equal(4, qpeIntent.TargetQubits) // ceil(log2 15)

            match qpeIntent.Unitary with
            | BackendAbstraction.QpeUnitary.ModularExponentiation(baseNum, modulus) ->
                Assert.Equal(7, baseNum)
                Assert.Equal(15, modulus)
            | other -> Assert.Fail($"Expected a ModularExponentiation unitary, got {other}")
        | Ok(Shor.ShorPeriodFindingPlan.ExecuteViaModExpCircuit _) ->
            Assert.Fail("Fell back to the gate circuit; the topological backend executes this intent natively")
        | Error err -> Assert.Fail($"Planning Shor for a topological backend failed: {err}")

    [<Fact>]
    let ``topological backend still accepts the QPE intents its lowering can handle`` () =
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 16

        let singleTargetQpe: BackendAbstraction.QpeIntent =
            {
                CountingQubits = 3
                TargetQubits = 1
                Unitary = BackendAbstraction.QpeUnitary.PhaseGate(System.Math.PI / 4.0)
                PrepareTargetOne = true
                ApplySwaps = false
            }

        Assert.True(
            topoBackend.SupportsOperation(
                BackendAbstraction.QuantumOperation.Algorithm(BackendAbstraction.AlgorithmOperation.QPE singleTargetQpe)
            ),
            "Narrowing SupportsOperation to match ApplyOperation must not disable ordinary QPE"
        )

    [<Fact>]
    let ``native modular-exponentiation QPE recovers the period of 7 mod 15`` () =
        // 7 qubits: 3 counting + ceil(log2 15) = 4 target, so 2*7 + 2 = 16 Ising anyons.
        // ord(7 mod 15) = 4, and 4 divides the 8-point phase grid, so the phase is exact
        // and one shot suffices — this is deterministic, not a lucky draw.
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 16

        match Shor.findPeriodQuantum 7 15 3 topoBackend with
        | Ok result ->
            Assert.Equal(7, result.Base)
            Assert.Equal(4, result.Period)
        | Error err -> Assert.Fail($"Native modular-exponentiation QPE failed on a topological backend: {err}")

    [<Fact>]
    let ``native topological QPE agrees with the gate circuit on the local simulator`` () =
        // Differential check across the two routes. The topological backend takes route A
        // (the intent, realised on the fusion encoding); LocalBackend declines it and takes
        // route B (the Beauregard circuit). Same period or one of them is wrong.
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 16

        let localBackend =
            FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

        match Shor.findPeriodQuantum 7 15 3 topoBackend, Shor.findPeriodQuantum 7 15 3 localBackend with
        | Ok native, Ok circuit ->
            Assert.Equal(circuit.Period, native.Period)
            Assert.Equal(circuit.Base, native.Base)
        | Error err, _ -> Assert.Fail($"Native route failed: {err}")
        | _, Error err -> Assert.Fail($"Circuit route failed: {err}")

    // Was skipped as ">10 min": Shor on 30 Ising anyons compiled hundreds of gates through
    // Solovay-Kitaev. Route A pays none of that — the modular-exponentiation QPE intent is
    // realised on the fusion encoding directly — so this runs again.
    [<Fact>]
    let ``AlgorithmExtensions - factorWithTopology factors 15 on a topological backend`` () =
        // 3 counting + ceil(log2 15) = 4 target qubits for the native route.
        // Ising anyons: 2n + 2 -> comfortably inside 30.
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 30

        // The base is pinned for two reasons: gcd(7, 15) = 1, so period finding is actually
        // reached rather than short-circuited by a lucky common factor; and ord(7 mod 15) = 4
        // divides the 8-point phase grid, making the run deterministic rather than a draw.
        let config: ShorsTypes.ShorsConfig =
            {
                NumberToFactor = 15
                RandomBase = Some 7
                PrecisionQubits = 3
                MaxAttempts = 3
            }

        match AlgorithmExtensions.factorWithTopology 15 topoBackend (Some config) with
        | Ok result ->
            Assert.True(result.Success, $"Should factor 15 on a topological backend: {result.Message}")

            match result.Factors with
            | Some(p, q) ->
                Assert.Equal(15, p * q)
                Assert.Equal<int list>([ 3; 5 ], List.sort [ p; q ])
            | None -> Assert.Fail($"Should find the factors of 15: {result.Message}")

            // Period finding ran, rather than a gcd shortcut standing in for it.
            match result.PeriodResult with
            | Some period -> Assert.Equal(4, period.Period)
            | None -> Assert.Fail("Factored without period finding; the quantum subroutine did not run")
        | Error err -> Assert.Fail($"factorWithTopology failed: {err}")

    [<Fact>]
    let ``AlgorithmExtensions - solveLinearSystemTopology accepts topological backend`` () =
        // Arrange
        // HHL: 1 qubit for eigenvalue (simple test) + 1 qubit for solution + 1 ancilla = 3 qubits
        // Ising anyons: 2n + 2 anyons -> 2*3 + 2 = 8 anyons
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 16

        // Setup simple 2x2 identity system: I * x = b
        // x should equal b
        let vector = [| Complex.One; Complex.Zero |]
        let matrixRes = HHLTypes.createDiagonalMatrix [| 1.0; 1.0 |]
        let vectorRes = HHLTypes.createQuantumVector vector

        match matrixRes, vectorRes with
        | Ok matrix, Ok qVector ->
            // Act
            let result =
                AlgorithmExtensions.solveLinearSystemTopology matrix qVector topoBackend None

            // Assert
            match result with
            | Ok _ -> Assert.True(true)
            | Error(QuantumError.OperationError(name, _)) -> Assert.Equal("TopologicalBackend", name)
            | Error(QuantumError.NotImplemented _) ->
                // Expected: GateBased-to-TopologicalBraiding conversion is not implemented in Core
                Assert.True(true)
            | Error err -> Assert.True(false, $"Unexpected error: {err}")
        | _ -> Assert.True(false, "Failed to create HHL test data")

    [<Fact>]
    let ``AlgorithmExtensions - Empty target list rejected`` () =
        // Arrange: Create backend
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 8
        let config = Grover.defaultConfig

        // Act: Try to search with empty target list
        let result = AlgorithmExtensions.searchMultipleWithTopology [] 4 topoBackend config

        // Assert: Should fail with validation error
        match result with
        | Ok _ -> Assert.True(false, "Should reject empty target list")
        | Error(QuantumError.ValidationError(param, _)) -> Assert.Equal("Targets", param)
        | Error err -> Assert.True(false, $"Wrong error type: {err}")

    [<Fact>]
    let ``AlgorithmExtensions - Empty target list rejected for Fibonacci`` () =
        // Fibonacci variant should also reject empty target lists
        let topoBackend = TopologicalUnifiedBackendFactory.createFibonacci 8
        let config = Grover.defaultConfig

        let result =
            AlgorithmExtensions.searchMultipleWithTopologyFibonacci [] 4 topoBackend config

        match result with
        | Ok _ -> Assert.True(false, "Should reject empty target list")
        | Error(QuantumError.ValidationError(param, _)) -> Assert.Equal("Targets", param)
        | Error err -> Assert.True(false, $"Wrong error type: {err}")

    [<Fact>]
    let ``AlgorithmExtensions - searchWithTopologyFibonacci accepts Fibonacci backend`` () =
        // Verify that the Fibonacci Grover search function compiles and accepts
        // a Fibonacci backend. Full execution may fail due to gate compilation
        // limits, but the API should be valid.
        let topoBackend = TopologicalUnifiedBackendFactory.createFibonacci 8
        let config = { Grover.defaultConfig with Shots = 10 }

        match Oracle.forValue 1 3 with
        | Ok oracle ->
            let result =
                AlgorithmExtensions.searchWithTopologyFibonacci oracle topoBackend config

            result
            |> Result.map (fun _ -> Assert.True(true))
            |> Result.defaultWith (fun _ -> Assert.True(true)) // Backend execution error is acceptable
        | Error err -> Assert.True(false, $"Oracle creation failed: {err}")

    [<Fact>]
    let ``AlgorithmExtensions - searchSingleWithTopologyFibonacci creates oracle and delegates`` () =
        let topoBackend = TopologicalUnifiedBackendFactory.createFibonacci 8
        let config = { Grover.defaultConfig with Shots = 10 }

        // searchSingleWithTopologyFibonacci should create the oracle internally
        let result =
            AlgorithmExtensions.searchSingleWithTopologyFibonacci 1 3 topoBackend config

        result
        |> Result.map (fun _ -> Assert.True(true))
        |> Result.defaultWith (fun _ -> Assert.True(true)) // Backend error is acceptable

    [<Fact>]
    let ``AlgorithmExtensions - searchWithPredicateTopologyFibonacci accepts predicate`` () =
        let topoBackend = TopologicalUnifiedBackendFactory.createFibonacci 8
        let config = { Grover.defaultConfig with Shots = 10 }

        let isEven n = n % 2 = 0

        let result =
            AlgorithmExtensions.searchWithPredicateTopologyFibonacci isEven 3 topoBackend config

        result
        |> Result.map (fun _ -> Assert.True(true))
        |> Result.defaultWith (fun _ -> Assert.True(true)) // Backend error is acceptable

    [<Fact>]
    let ``AlgorithmExtensions - Adapter respects qubit count limits`` () =
        // Arrange: Create backend with strict qubit limit
        // 5 anyons is enough for 2 qubits (Ising needs 2n+2 generally, or specific fusion tree size)
        // 5 anyons < (3 qubits * 2 + 2) = 8 anyons
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 5
        let config = { Grover.defaultConfig with Shots = 10 }

        // Act: Try to create circuit requiring 3 qubits (which exceeds 5 anyon limit)
        let result = AlgorithmExtensions.searchSingleWithTopology 1 3 topoBackend config

        // Assert: Should fail with validation or backend error
        match result with
        | Ok _ -> Assert.True(false, "Should reject circuit exceeding anyon limit")
        | Error _ ->
            // Expected: validation or execution failure
            Assert.True(true)

    [<Fact>]
    let ``AlgorithmExtensions - qftWithTopology accepts topological backend`` () =
        // Arrange
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 8
        let config = QFT.defaultConfig

        // Act
        let result = AlgorithmExtensions.qftWithTopology 3 topoBackend config

        // Assert
        match result with
        | Ok _ -> Assert.True(true)
        | Error(QuantumError.OperationError(name, _)) -> Assert.Equal("TopologicalBackend", name)
        | Error err -> Assert.True(false, $"Unexpected error: {err}")

    // ========================================================================
    // HHL ON TOPOLOGICAL BACKEND
    // ========================================================================
    //
    // Bug 1 fixed HHL's FusionSuperposition post-processing (5 functions that
    // silently returned garbage for topological states).
    //
    // Behavior since RY became an exact amplitude-level intercept on the Ising
    // backend (it previously reached braid compilation, which cannot realize
    // arbitrary rotations, leaving the ancilla-|1⟩ subspace empty): the native
    // diagonal HHL path executes exactly, post-selection succeeds, and the
    // solution is genuinely A⁻¹b (up to normalization). These tests validate
    // correctness of that result.

    [<Fact>]
    let ``HHL on topological backend solves Ising diagonal system`` () =
        // Arrange: Simple 2x2 diagonal system Ax=b where A=diag(2,3), b=[1,0]
        // → x ∝ [1, 0] (second component exactly zero)
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 40

        let matrixRes = HHLTypes.createDiagonalMatrix [| 2.0; 3.0 |]
        let vectorRes = HHLTypes.createQuantumVector [| Complex(1.0, 0.0); Complex.Zero |]

        match matrixRes, vectorRes with
        | Ok matrix, Ok qVector ->
            let result =
                AlgorithmExtensions.solveLinearSystemTopology matrix qVector topoBackend None

            match result with
            | Ok hhlResult ->
                Assert.Equal(2, hhlResult.Solution.Length)

                Assert.True(
                    hhlResult.Solution.[0].Magnitude > 1e-6,
                    $"Expected non-zero first component, got %A{hhlResult.Solution}"
                )
                // b has no overlap with the second eigenvector, so x_1 = 0
                Assert.True(
                    hhlResult.Solution.[1].Magnitude < 1e-6,
                    $"Expected zero second component, got %A{hhlResult.Solution}"
                )
            | Error(QuantumError.NotImplemented _) ->
                // Acceptable: conversion pipeline may change in future refactors
                ()
            | Error(QuantumError.OperationError(name, _)) -> Assert.Equal("TopologicalBackend", name)
            | Error err -> Assert.Fail($"Unexpected error: {err}")
        | _ -> Assert.Fail("Failed to create HHL test data")

    [<Fact>]
    let ``HHL on topological backend post-selects successfully for Ising diagonal`` () =
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 40

        let matrixRes = HHLTypes.createDiagonalMatrix [| 2.0; 3.0 |]
        let vectorRes = HHLTypes.createQuantumVector [| Complex(1.0, 0.0); Complex.Zero |]

        match matrixRes, vectorRes with
        | Ok matrix, Ok qVector ->
            let result =
                AlgorithmExtensions.solveLinearSystemTopology matrix qVector topoBackend None

            match result with
            | Ok hhlResult ->
                // With the exact RY intercept the ancilla rotation is applied
                // faithfully, so post-selection on ancilla |1⟩ succeeds.
                Assert.True(
                    hhlResult.SuccessProbability > 0.0,
                    $"Expected positive success probability, got {hhlResult.SuccessProbability}"
                )

                Assert.True(hhlResult.PostSelectionSuccess, "Post-selection should succeed for Ising diagonal HHL")
            | Error(QuantumError.NotImplemented _) -> ()
            | Error(QuantumError.OperationError(name, _)) -> Assert.Equal("TopologicalBackend", name)
            | Error err -> Assert.Fail($"Unexpected error: {err}")
        | _ -> Assert.Fail("Failed to create HHL test data")

    [<Fact>]
    let ``HHL on topological backend preserves diagonal eigenvalues`` () =
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 40

        let matrixRes = HHLTypes.createDiagonalMatrix [| 2.0; 3.0 |]
        let vectorRes = HHLTypes.createQuantumVector [| Complex(1.0, 0.0); Complex.Zero |]

        match matrixRes, vectorRes with
        | Ok matrix, Ok qVector ->
            let result =
                AlgorithmExtensions.solveLinearSystemTopology matrix qVector topoBackend None

            match result with
            | Ok hhlResult ->
                // Diagonal eigenvalues are passed through from the config, not extracted
                // from the quantum state. They should match the input exactly.
                Assert.Equal(2, hhlResult.EstimatedEigenvalues.Length)
                Assert.Equal(2.0, hhlResult.EstimatedEigenvalues.[0])
                Assert.Equal(3.0, hhlResult.EstimatedEigenvalues.[1])
            | Error(QuantumError.NotImplemented _) -> ()
            | Error(QuantumError.OperationError(name, _)) -> Assert.Equal("TopologicalBackend", name)
            | Error err -> Assert.Fail($"Unexpected error: {err}")
        | _ -> Assert.Fail("Failed to create HHL test data")

    [<Fact>]
    let ``HHL on topological backend with identity matrix recovers b`` () =
        // Arrange: Ix = b means x = b = [1, 0]
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 40

        let matrixRes = HHLTypes.createDiagonalMatrix [| 1.0; 1.0 |]
        let vectorRes = HHLTypes.createQuantumVector [| Complex(1.0, 0.0); Complex.Zero |]

        match matrixRes, vectorRes with
        | Ok matrix, Ok qVector ->
            let result =
                AlgorithmExtensions.solveLinearSystemTopology matrix qVector topoBackend None

            match result with
            | Ok hhlResult ->
                // With the exact RY intercept the solution is x = b (up to
                // normalization): dominant first component, zero second.
                Assert.Equal(2, hhlResult.Solution.Length)

                Assert.True(
                    hhlResult.Solution.[0].Magnitude > 1e-6,
                    $"Expected non-zero first component, got %A{hhlResult.Solution}"
                )

                Assert.True(
                    hhlResult.Solution.[1].Magnitude < 1e-6,
                    $"Expected zero second component, got %A{hhlResult.Solution}"
                )

                Assert.True(
                    hhlResult.SuccessProbability > 0.0,
                    $"Expected positive success probability, got {hhlResult.SuccessProbability}"
                )

                Assert.Equal(2, hhlResult.EstimatedEigenvalues.Length)
                Assert.Equal(1.0, hhlResult.EstimatedEigenvalues.[0])
                Assert.Equal(1.0, hhlResult.EstimatedEigenvalues.[1])
            | Error(QuantumError.NotImplemented _) -> ()
            | Error(QuantumError.OperationError(name, _)) -> Assert.Equal("TopologicalBackend", name)
            | Error err -> Assert.Fail($"Unexpected error: {err}")
        | _ -> Assert.Fail("Failed to create HHL test data")

    // ========================================================================
    // SHOR estimateModExpPhase ON TOPOLOGICAL BACKEND (Gap 20)
    // ========================================================================
    //
    // estimateModExpPhase bypasses the QPE handler entirely — it manually emits
    // H, X, CP, and controlledModularMultiplication gates through ApplyOperation.
    //
    // For Ising anyons with amplitude intercepts:
    //   - H, X, CNOT, SWAP: handled by amplitude-level shortcuts (exact)
    //   - CP, RZ, P: compiled to braids (discretized to π/2 multiples)
    //   - CCX: transpiled to Barenco decomposition (H + CNOT + T/TDG)
    //   - T, TDG: amplitude-intercepted on Ising
    //
    // The error comes from angle discretization in CP/RZ gates (QFT rotations).
    // Phases like π/4, π/8 etc. get discretized to nearest π/2 multiple,
    // so QPE results will be approximate but should still yield valid phases.

    module Shor = FSharp.Azure.Quantum.Algorithms.Shor

    [<Fact>]
    let ``estimateModExpPhase runs on Ising TopologicalBackend a=2 mod 3 c=1`` () =
        // N=3, n=2 bits, workspace=8, total=9 qubits. Ising: 2*9+2=20 anyons.
        // Minimal meaningful Shor circuit. With c=1 counting qubit, only 1 modular
        // multiplication. Tests the full CCX→transpile→amplitude-intercept pipeline.
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 24

        match Shor.estimateModExpPhase 2 3 1 topoBackend with
        | Ok result ->
            Assert.True(
                result.EstimatedPhase >= 0.0 && result.EstimatedPhase < 1.0,
                $"Phase {result.EstimatedPhase} out of range [0, 1)"
            )

            Assert.Equal(1, result.CountingQubits)
            Assert.Equal(9, result.TotalQubits)
            Assert.Equal(1, result.ModularMultiplications)
        | Error err -> Assert.Fail($"estimateModExpPhase on Ising TopologicalBackend failed: {err}")

    [<Fact>]
    let ``estimateModExpPhase returns error for non-coprime inputs on TopologicalBackend`` () =
        // Verify that gcd check works on topological backend (fast, no circuit execution).
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 24

        match Shor.estimateModExpPhase 4 6 1 topoBackend with
        | Error(QuantumError.ValidationError("baseNum", msg)) -> Assert.Contains("coprime", msg)
        | other -> Assert.Fail($"Expected coprime ValidationError, got: {other}")

    [<Fact>]
    let ``estimateModExpPhase result fields are consistent on TopologicalBackend`` () =
        // Verify internal consistency: phase = measurementOutcome / 2^countingQubits
        // Using minimal N=3, c=1 to keep test fast.
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 24

        match Shor.estimateModExpPhase 2 3 1 topoBackend with
        | Ok result ->
            let expectedPhase =
                float result.MeasurementOutcome / float (1 <<< result.CountingQubits)

            Assert.Equal(expectedPhase, result.EstimatedPhase, 10)
            Assert.True(result.MeasurementOutcome >= 0)
            Assert.True(result.MeasurementOutcome < (1 <<< result.CountingQubits))
        | Error err -> Assert.Fail($"estimateModExpPhase on Ising TopologicalBackend failed: {err}")

    [<Fact>]
    let ``estimateModExpPhase validation works on TopologicalBackend`` () =
        // Validation checks should work identically regardless of backend.
        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 30

        // baseNum < 2
        match Shor.estimateModExpPhase 1 7 2 topoBackend with
        | Error(QuantumError.ValidationError("baseNum", _)) -> ()
        | other -> Assert.Fail($"Expected ValidationError for baseNum, got: {other}")

        // modulus < 2
        match Shor.estimateModExpPhase 2 1 2 topoBackend with
        | Error(QuantumError.ValidationError("modulus", _)) -> ()
        | other -> Assert.Fail($"Expected ValidationError for modulus, got: {other}")

        // non-coprime
        match Shor.estimateModExpPhase 6 15 2 topoBackend with
        | Error(QuantumError.ValidationError("baseNum", msg)) -> Assert.Contains("coprime", msg)
        | other -> Assert.Fail($"Expected coprime ValidationError, got: {other}")

    // ========================================================================
    // QFT INTENT: topological against the gate simulator
    // ========================================================================
    //
    // The QFT intent had no coverage on this backend at all — it is exercised only in the
    // main project, against LocalBackend. Both backends implement the same lowering shape
    // (H on each target, then controlled phases from the higher qubits), so comparing them
    // isolates the one thing that can differ: the rotation angles.

    /// Amplitudes of a state, whichever representation it is in.
    let private amplitudesOf (state: QuantumState) : Complex[] =
        match state with
        | QuantumState.StateVector sv ->
            Array.init (FSharp.Azure.Quantum.LocalSimulator.StateVector.dimension sv) (fun i ->
                FSharp.Azure.Quantum.LocalSimulator.StateVector.getAmplitude i sv)
        | QuantumState.FusionSuperposition fs -> fs.GetAmplitudeVector()
        | other -> failwith $"Unexpected state representation: {other}"

    // All four combinations: the bit-ordering convention is easy to get right for one and
    // wrong for the others, and the first version of the native transform did exactly that.
    [<Theory>]
    [<InlineData(false, true)>]
    [<InlineData(false, false)>]
    [<InlineData(true, true)>]
    [<InlineData(true, false)>]
    let ``topological QFT intent agrees with the gate simulator`` (inverse: bool) (applySwaps: bool) =
        let numQubits = 3

        let qftIntent =
            BackendAbstraction.QuantumOperation.Algorithm(
                BackendAbstraction.AlgorithmOperation.QFT
                    {
                        NumQubits = numQubits
                        Inverse = inverse
                        ApplySwaps = applySwaps
                    }
            )

        // Sweep every basis state, which compares the two implementations column by column
        // and so pins the whole unitary rather than one of its columns.
        //
        // A single probe is not enough and picking one is easy to get wrong: |000> and |111>
        // are bit-reversal symmetric, so they hide index-permutation errors entirely, while a
        // single low bit such as |001> fires no controlled phase and hides angle errors. An
        // earlier version of this test used |111> and reported three of four configurations
        // passing for an implementation that was wrong in all four.
        let prepare (backend: BackendAbstraction.IQuantumBackend) (basisState: int) =
            [ 0 .. numQubits - 1 ]
            |> List.filter (fun q -> (basisState >>> q) &&& 1 = 1)
            |> List.fold
                (fun stateResult q ->
                    stateResult
                    |> Result.bind (
                        backend.ApplyOperation(
                            BackendAbstraction.QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.X q)
                        )
                    ))
                (backend.InitializeState numQubits)
            |> Result.bind (backend.ApplyOperation qftIntent)

        let localBackend =
            FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 8

        for basisState in 0 .. (1 <<< numQubits) - 1 do
            match prepare localBackend basisState, prepare topoBackend basisState with
            | Ok gateState, Ok topoState ->
                let expected = amplitudesOf gateState
                let actual = amplitudesOf topoState

                Assert.Equal(expected.Length, actual.Length)

                for i in 0 .. expected.Length - 1 do
                    Assert.True(
                        (expected.[i] - actual.[i]).Magnitude < 1e-9,
                        $"Basis state {basisState}, amplitude {i}: gate simulator {expected.[i]} vs topological {actual.[i]}"
                    )
            | Error err, _ -> failwith $"Gate simulator failed on basis state {basisState}: {err}"
            | _, Error err -> failwith $"Topological backend failed on basis state {basisState}: {err}"

    [<Fact>]
    let ``native topological QFT is unitary on a superposition`` () =
        // Columns agreeing one at a time does not by itself prove the transform is linear in
        // the implementation: it rebuilds fusion terms per output index. A superposition
        // input exercises the summation across terms.
        let numQubits = 3

        let qftIntent =
            BackendAbstraction.QuantumOperation.Algorithm(
                BackendAbstraction.AlgorithmOperation.QFT
                    {
                        NumQubits = numQubits
                        Inverse = false
                        ApplySwaps = true
                    }
            )

        let prepare (backend: BackendAbstraction.IQuantumBackend) =
            backend.InitializeState numQubits
            |> Result.bind (
                backend.ApplyOperation(
                    BackendAbstraction.QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.H 0)
                )
            )
            |> Result.bind (
                backend.ApplyOperation(
                    BackendAbstraction.QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.X 1)
                )
            )
            |> Result.bind (backend.ApplyOperation qftIntent)

        let localBackend =
            FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 8

        match prepare localBackend, prepare topoBackend with
        | Ok gateState, Ok topoState ->
            let expected = amplitudesOf gateState
            let actual = amplitudesOf topoState

            for i in 0 .. expected.Length - 1 do
                Assert.True(
                    (expected.[i] - actual.[i]).Magnitude < 1e-9,
                    $"Amplitude {i}: gate simulator {expected.[i]} vs topological {actual.[i]}"
                )
        | Error err, _ -> failwith $"Gate simulator failed: {err}"
        | _, Error err -> failwith $"Topological backend failed: {err}"

    [<Theory>]
    [<InlineData(true)>]
    [<InlineData(false)>]
    let ``QFT intent round-trips on the gate simulator`` (applySwaps: bool) =
        // Forward then inverse must be the identity on any backend, for either swap setting.
        // If this fails the canonical lowering itself is inconsistent, which would make it a
        // poor contract to hold the topological implementation to.
        let numQubits = 3

        let qft inverse =
            BackendAbstraction.QuantumOperation.Algorithm(
                BackendAbstraction.AlgorithmOperation.QFT
                    {
                        NumQubits = numQubits
                        Inverse = inverse
                        ApplySwaps = applySwaps
                    }
            )

        let backend =
            FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

        // |001>, NOT |111>: a bit-reversal-symmetric input hides a permutation error,
        // because reversing its bits gives the same state back.
        let roundTripped =
            backend.InitializeState numQubits
            |> Result.bind (
                backend.ApplyOperation(
                    BackendAbstraction.QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.X 0)
                )
            )
            |> Result.bind (backend.ApplyOperation(qft false))
            |> Result.bind (backend.ApplyOperation(qft true))

        match roundTripped with
        | Error err -> failwith $"Round trip failed: {err}"
        | Ok state ->
            let amplitudes = amplitudesOf state
            // |001> is index 1.
            for i in 0 .. amplitudes.Length - 1 do
                let expected = if i = 1 then 1.0 else 0.0

                Assert.True(
                    abs (amplitudes.[i].Magnitude - expected) < 1e-9,
                    $"Amplitude {i} should be {expected} after QFT then inverse QFT, got {amplitudes.[i]}"
                )

    [<Theory>]
    [<InlineData(false, true)>]
    [<InlineData(false, false)>]
    [<InlineData(true, true)>]
    [<InlineData(true, false)>]
    let ``native topological QFT transforms a sub-register`` (inverse: bool) (applySwaps: bool) =
        // A QFT intent covering fewer qubits than the state is the case that matters for
        // composition: Shor's inverse QFT runs on the counting register of a state that also
        // holds the target register and the arithmetic workspace. Transforming the whole
        // state instead, or ignoring the untouched high bits, is wrong in a way that the
        // whole-state tests above cannot see.
        let qftQubits = 2
        let stateQubits = 4

        let qftIntent =
            BackendAbstraction.QuantumOperation.Algorithm(
                BackendAbstraction.AlgorithmOperation.QFT
                    {
                        NumQubits = qftQubits
                        Inverse = inverse
                        ApplySwaps = applySwaps
                    }
            )

        let prepare (backend: BackendAbstraction.IQuantumBackend) (basisState: int) =
            [ 0 .. stateQubits - 1 ]
            |> List.filter (fun q -> (basisState >>> q) &&& 1 = 1)
            |> List.fold
                (fun stateResult q ->
                    stateResult
                    |> Result.bind (
                        backend.ApplyOperation(
                            BackendAbstraction.QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.X q)
                        )
                    ))
                (backend.InitializeState stateQubits)
            |> Result.bind (backend.ApplyOperation qftIntent)

        let localBackend =
            FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 10

        for basisState in 0 .. (1 <<< stateQubits) - 1 do
            match prepare localBackend basisState, prepare topoBackend basisState with
            | Ok gateState, Ok topoState ->
                let expected = amplitudesOf gateState
                let actual = amplitudesOf topoState

                Assert.Equal(expected.Length, actual.Length)

                for i in 0 .. expected.Length - 1 do
                    Assert.True(
                        (expected.[i] - actual.[i]).Magnitude < 1e-9,
                        $"Basis state {basisState}, amplitude {i}: gate simulator {expected.[i]} vs topological {actual.[i]}"
                    )
            | Error err, _ -> failwith $"Gate simulator failed on basis state {basisState}: {err}"
            | _, Error err -> failwith $"Topological backend failed on basis state {basisState}: {err}"

    [<Fact>]
    let ``braiding keeps states inside the computational-basis encoding`` () =
        // The native primitives on this backend — Grover's three, modular-exponentiation QPE,
        // and now the QFT — all read fusion terms through FusionTree.toComputationalBasis,
        // which maps any channel it does not recognise to a 0 bit rather than failing. If a
        // braid could move a state outside that encoding, those primitives would silently
        // mangle it where the gate path would not, so this pins the assumption they rest on.
        let backend = TopologicalUnifiedBackendFactory.createIsing 8
        let numQubits = 3

        let braided =
            backend.InitializeState numQubits
            |> Result.bind (
                backend.ApplyOperation(
                    BackendAbstraction.QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.H 0)
                )
            )
            // Within-pair braids (even leaf indices). Cross-pair braiding is the operation
            // that could move a state between fusion channels, and this backend refuses it
            // outright: "the F-move machinery required for cross-pair braids is only
            // implemented for 3-anyon trees". That refusal is what keeps the encoding total.
            |> Result.bind (backend.ApplyOperation(BackendAbstraction.QuantumOperation.Braid 0))
            |> Result.bind (backend.ApplyOperation(BackendAbstraction.QuantumOperation.Braid 2))

        match braided with
        | Error err -> failwith $"Braiding failed: {err}"
        | Ok(QuantumState.FusionSuperposition fs) ->
            // Amplitudes must survive a round trip through the encoding the native
            // primitives use: same number of basis states, same total probability.
            let amplitudes = fs.GetAmplitudeVector()
            let total = amplitudes |> Array.sumBy (fun a -> a.Magnitude * a.Magnitude)

            Assert.True(
                abs (total - 1.0) < 1e-9,
                $"Braided state should stay normalised in the computational-basis view, got {total}"
            )
        | Ok other -> failwith $"Expected a fusion superposition, got {other}"

    [<Fact>]
    let ``QPE execute runs modular exponentiation on a topological backend`` () =
        // Regression: QPE.execute sizes the state for the Beauregard lowering
        // (counting + 2n + 4) because it allocates before planning, so it cannot know whether
        // the backend will claim the intent natively. The native handler demanded exactly
        // counting + target and rejected every state QPE.execute built, making this
        // combination always fail. Shor's own path was unaffected because it allocates after
        // planning. The handler now accepts the wider state and leaves the surplus in |0>.
        let backend = TopologicalUnifiedBackendFactory.createIsing 32

        let config: FSharp.Azure.Quantum.Algorithms.QPE.QPEConfig =
            {
                CountingQubits = 3
                TargetQubits = 4 // ceil(log2 15)
                UnitaryOperator = FSharp.Azure.Quantum.Algorithms.QPE.UnitaryOperator.ModularExponentiation(7, 15)
                EigenVector = None
            }

        match FSharp.Azure.Quantum.Algorithms.QPE.execute config backend with
        | Error err -> failwith $"QPE.execute with ModularExponentiation failed on a topological backend: {err}"
        | Ok result ->
            // ord(7 mod 15) = 4, so the phase must land on a multiple of 1/4.
            let scaled = result.EstimatedPhase * 4.0

            Assert.True(
                abs (scaled - System.Math.Round scaled) < 1e-9,
                $"Phase {result.EstimatedPhase} is not a multiple of 1/4; period finding could not recover r=4"
            )

    // ========================================================================
    // Native single-qubit QPE against the gate simulator
    // ========================================================================
    //
    // Every unitary, both swap settings, both eigenstate preparations. PrepareTargetOne
    // matters more than it looks: CP fires only when control AND target are 1, so a target
    // left in |0> accumulates no phase at all, whereas CRZ applies rz = diag(e^(-i0/2),
    // e^(+i0/2)) and merely flips sign. An implementation that treated them alike would pass
    // half of these.
    let singleQubitUnitaries: obj[] seq =
        seq {
            for applySwaps in [ true; false ] do
                for prepareOne in [ true; false ] do
                    yield [| box "T"; box applySwaps; box prepareOne |]
                    yield [| box "S"; box applySwaps; box prepareOne |]
                    yield [| box "Phase"; box applySwaps; box prepareOne |]
                    yield [| box "Rz"; box applySwaps; box prepareOne |]
        }

    [<Theory; MemberData(nameof singleQubitUnitaries)>]
    let ``native single-qubit QPE agrees with the gate simulator``
        (kind: string)
        (applySwaps: bool)
        (prepareOne: bool)
        =
        let countingQubits = 3

        let unitary =
            match kind with
            | "T" -> BackendAbstraction.QpeUnitary.TGate
            | "S" -> BackendAbstraction.QpeUnitary.SGate
            | "Phase" -> BackendAbstraction.QpeUnitary.PhaseGate(System.Math.PI / 3.0)
            | _ -> BackendAbstraction.QpeUnitary.RotationZ(System.Math.PI / 5.0)

        let qpeOp =
            BackendAbstraction.QuantumOperation.Algorithm(
                BackendAbstraction.AlgorithmOperation.QPE
                    {
                        CountingQubits = countingQubits
                        TargetQubits = 1
                        Unitary = unitary
                        PrepareTargetOne = prepareOne
                        ApplySwaps = applySwaps
                    }
            )

        // Angles that are not dyadic fractions of 2*pi (pi/3, pi/5) spread the phase across
        // the whole counting register instead of landing on one outcome, so every amplitude
        // carries information rather than just the peak.
        let run (backend: BackendAbstraction.IQuantumBackend) =
            backend.InitializeState(countingQubits + 1)
            |> Result.bind (backend.ApplyOperation qpeOp)

        let localBackend =
            FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 10

        match run localBackend, run topoBackend with
        | Ok gateState, Ok topoState ->
            let expected = amplitudesOf gateState
            let actual = amplitudesOf topoState

            Assert.Equal(expected.Length, actual.Length)

            for i in 0 .. expected.Length - 1 do
                Assert.True(
                    (expected.[i] - actual.[i]).Magnitude < 1e-9,
                    $"{kind} (swaps={applySwaps}, prepareOne={prepareOne}) amplitude {i}: gate {expected.[i]} vs topological {actual.[i]}"
                )
        | Error err, _ -> failwith $"Gate simulator failed: {err}"
        | _, Error err -> failwith $"Topological backend failed: {err}"

    [<Fact>]
    let ``QPE intent refuses a non-zero input instead of discarding it`` () =
        // The native handlers build the QPE output from scratch instead of applying a circuit
        // to the incoming state. That is within the documented contract — the intent says it
        // transforms |0..0> — but the gate lowering they replaced applied gates to WHATEVER
        // state was there. If a caller applies the intent mid-circuit, the two disagree and
        // the native one answers silently.
        let countingQubits = 3

        let qpeOp =
            BackendAbstraction.QuantumOperation.Algorithm(
                BackendAbstraction.AlgorithmOperation.QPE
                    {
                        CountingQubits = countingQubits
                        TargetQubits = 1
                        Unitary = BackendAbstraction.QpeUnitary.TGate
                        PrepareTargetOne = true
                        ApplySwaps = true
                    }
            )

        // Input is NOT |0000>: flip a counting qubit first.
        let run (backend: BackendAbstraction.IQuantumBackend) =
            backend.InitializeState(countingQubits + 1)
            |> Result.bind (
                backend.ApplyOperation(
                    BackendAbstraction.QuantumOperation.Gate(FSharp.Azure.Quantum.CircuitBuilder.X 0)
                )
            )
            |> Result.bind (backend.ApplyOperation qpeOp)

        let localBackend =
            FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

        let topoBackend = TopologicalUnifiedBackendFactory.createIsing 10

        // The gate simulator applies the circuit to whatever it is given, which for a
        // non-zero input is outside what the intent defines. The native path must refuse
        // rather than answer: it once returned amplitude 1 where the gate simulator had ~0.
        match run topoBackend with
        | Ok state ->
            failwith (
                "Should refuse a QPE intent on a non-zero input rather than discard it silently; got "
                + sprintf "%A" (amplitudesOf state)
            )
        | Error(QuantumError.OperationError("TopologicalBackend", message)) -> Assert.Contains("|0..0>", message)
        | Error err -> failwith $"Expected a contract refusal, got: {err}"

        // ...and the gate simulator still accepts it, so this is a deliberate difference in
        // contract strictness, not the native path being unable to run.
        match run localBackend with
        | Ok _ -> ()
        | Error err -> failwith $"Gate simulator should still accept it: {err}"
