namespace FSharp.Azure.Quantum.Tests

open System.Numerics
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.LocalSimulator

/// Tests for Shor's controlledModularMultiplication wired to the Arithmetic module.
///
/// These tests verify that the previously-stubbed controlledModularMultiplication
/// and controlledModularExponentiation functions in Shor.fs now correctly delegate
/// to Arithmetic.controlledMultiplyConstantModNInPlace (Beauregard 2003).
///
/// Qubit layout for n-bit register:
///   - controlQubit: 1 qubit
///   - targetQubits: n qubits (register)
///   - tempQubits:   n qubits (allocated by controlledModularMultiplication)
///   - ancilla:      4 qubits (AND-ancilla + overflow + flag + dcAdd-ancilla)
///   Total: 2n + 5 qubits
module ShorArithmeticIntegrationTests =

    module Shor = FSharp.Azure.Quantum.Algorithms.Shor

    /// Helper: encode integer value into register qubits within a state of totalQubits
    let private prepareState (totalQubits: int) (registerQubits: int list) (value: int) =
        let bknd = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

        match bknd.InitializeState totalQubits with
        | Error err -> failwith $"InitializeState failed: {err}"
        | Ok state0 ->
            let finalState =
                registerQubits
                |> List.indexed
                |> List.fold
                    (fun st (i, q) ->
                        if (value >>> i) &&& 1 = 1 then
                            (bknd.ApplyOperation (QuantumOperation.Gate(X q)) st)
                            |> Result.defaultWith (fun err -> failwith $"State prep failed on qubit {q}: {err}")
                        else
                            st)
                    state0

            (bknd, finalState)

    /// Helper: read register value from a computational basis state
    let private readRegisterValue (registerQubits: int list) (state: QuantumState) : int =
        match state with
        | QuantumState.StateVector sv ->
            let topIdx, topProb = Measurement.getTopOutcomes 1 sv |> Array.head

            if topProb < 0.99 then
                failwith $"State is not a computational basis state (top prob = {topProb:F6})"

            registerQubits
            |> List.mapi (fun pos q -> ((topIdx >>> q) &&& 1) <<< pos)
            |> List.sum
        | other -> failwith $"Expected StateVector, got: {QuantumState.stateType other}"

    // ========================================================================
    // controlledModularMultiplication: control=|1⟩ tests
    // ========================================================================

    [<Fact>]
    let ``controlledModularMultiplication: 3*1 mod 5 = 3 (control=1)`` () =
        // N=5, a=3: 3*1 mod 5 = 3
        // n=3 bits, total = 2*3 + 5 = 11 qubits
        let controlQubit = 0
        let targetQubits = [ 1; 2; 3 ]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 1

        // Set control qubit to |1>
        let state' =
            (bknd.ApplyOperation (QuantumOperation.Gate(X controlQubit)) state)
            |> Result.defaultWith (fun err -> failwith $"Control prep failed: {err}")

        match Shor.controlledModularMultiplication controlQubit targetQubits 3 5 bknd state' with
        | Error err -> Assert.Fail($"controlledModularMultiplication failed: {err}")
        | Ok resultState ->
            let value = readRegisterValue targetQubits resultState
            Assert.Equal(3, value)

    [<Fact>]
    let ``controlledModularMultiplication: 3*2 mod 5 = 1 (control=1)`` () =
        let controlQubit = 0
        let targetQubits = [ 1; 2; 3 ]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 2

        let state' =
            (bknd.ApplyOperation (QuantumOperation.Gate(X controlQubit)) state)
            |> Result.defaultWith (fun err -> failwith $"Control prep failed: {err}")

        match Shor.controlledModularMultiplication controlQubit targetQubits 3 5 bknd state' with
        | Error err -> Assert.Fail($"controlledModularMultiplication failed: {err}")
        | Ok resultState ->
            let value = readRegisterValue targetQubits resultState
            Assert.Equal(1, value)

    [<Fact>]
    let ``controlledModularMultiplication: 3*4 mod 5 = 2 (control=1)`` () =
        let controlQubit = 0
        let targetQubits = [ 1; 2; 3 ]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 4

        let state' =
            (bknd.ApplyOperation (QuantumOperation.Gate(X controlQubit)) state)
            |> Result.defaultWith (fun err -> failwith $"Control prep failed: {err}")

        match Shor.controlledModularMultiplication controlQubit targetQubits 3 5 bknd state' with
        | Error err -> Assert.Fail($"controlledModularMultiplication failed: {err}")
        | Ok resultState ->
            let value = readRegisterValue targetQubits resultState
            Assert.Equal(2, value)

    // ========================================================================
    // controlledModularMultiplication: control=|0⟩ (no-op) tests
    // ========================================================================

    [<Fact>]
    let ``controlledModularMultiplication: control=0 leaves state unchanged`` () =
        // When control=|0>, multiplication should NOT happen, register should stay as-is
        let controlQubit = 0
        let targetQubits = [ 1; 2; 3 ]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 4

        // Control qubit stays |0> (not flipped)
        match Shor.controlledModularMultiplication controlQubit targetQubits 3 5 bknd state with
        | Error err -> Assert.Fail($"controlledModularMultiplication failed: {err}")
        | Ok resultState ->
            let value = readRegisterValue targetQubits resultState
            Assert.Equal(4, value)

    // ========================================================================
    // controlledModularMultiplication: identity (a=1)
    // ========================================================================

    [<Fact>]
    let ``controlledModularMultiplication: a=1 is identity`` () =
        // Multiplying by 1 should leave register unchanged
        let controlQubit = 0
        let targetQubits = [ 1; 2; 3 ]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 3

        let state' =
            (bknd.ApplyOperation (QuantumOperation.Gate(X controlQubit)) state)
            |> Result.defaultWith (fun err -> failwith $"Control prep failed: {err}")

        match Shor.controlledModularMultiplication controlQubit targetQubits 1 5 bknd state' with
        | Error err -> Assert.Fail($"controlledModularMultiplication failed: {err}")
        | Ok resultState ->
            let value = readRegisterValue targetQubits resultState
            Assert.Equal(3, value)

    // ========================================================================
    // controlledModularMultiplication: validation errors
    // ========================================================================

    [<Fact>]
    let ``controlledModularMultiplication: insufficient qubits returns error`` () =
        // Provide too few qubits (need 11, give 6)
        let controlQubit = 0
        let targetQubits = [ 1; 2; 3 ]
        let totalQubits = 6 // Not enough
        let (bknd, state) = prepareState totalQubits targetQubits 1

        let state' =
            (bknd.ApplyOperation (QuantumOperation.Gate(X controlQubit)) state)
            |> Result.defaultWith (fun err -> failwith $"Control prep failed: {err}")

        match Shor.controlledModularMultiplication controlQubit targetQubits 3 5 bknd state' with
        | Error(QuantumError.ValidationError _) -> () // Expected
        | Error err -> Assert.Fail($"Expected ValidationError, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for insufficient qubits")

    // ========================================================================
    // controlledModularExponentiation tests
    // ========================================================================

    [<Fact>]
    let ``controlledModularExponentiation: 2^1 * 1 mod 7 = 2 (control=1)`` () =
        // a=2, k=1, n=7: 2^1 = 2, then 2*1 mod 7 = 2
        let controlQubit = 0
        let targetQubits = [ 1; 2; 3 ]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 1

        let state' =
            (bknd.ApplyOperation (QuantumOperation.Gate(X controlQubit)) state)
            |> Result.defaultWith (fun err -> failwith $"Control prep failed: {err}")

        match Shor.controlledModularExponentiation controlQubit targetQubits 2 1 7 bknd state' with
        | Error err -> Assert.Fail($"controlledModularExponentiation failed: {err}")
        | Ok resultState ->
            let value = readRegisterValue targetQubits resultState
            Assert.Equal(2, value)

    [<Fact>]
    let ``controlledModularExponentiation: 2^2 * 1 mod 7 = 4 (control=1)`` () =
        // a=2, k=2, n=7: 2^2 = 4, then 4*1 mod 7 = 4
        let controlQubit = 0
        let targetQubits = [ 1; 2; 3 ]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 1

        let state' =
            (bknd.ApplyOperation (QuantumOperation.Gate(X controlQubit)) state)
            |> Result.defaultWith (fun err -> failwith $"Control prep failed: {err}")

        match Shor.controlledModularExponentiation controlQubit targetQubits 2 2 7 bknd state' with
        | Error err -> Assert.Fail($"controlledModularExponentiation failed: {err}")
        | Ok resultState ->
            let value = readRegisterValue targetQubits resultState
            Assert.Equal(4, value)

    [<Fact>]
    let ``controlledModularExponentiation: 2^3 * 3 mod 7 = 3 (control=1)`` () =
        // a=2, k=3, n=7: 2^3 mod 7 = 8 mod 7 = 1, then 1*3 mod 7 = 3
        let controlQubit = 0
        let targetQubits = [ 1; 2; 3 ]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 3

        let state' =
            (bknd.ApplyOperation (QuantumOperation.Gate(X controlQubit)) state)
            |> Result.defaultWith (fun err -> failwith $"Control prep failed: {err}")

        match Shor.controlledModularExponentiation controlQubit targetQubits 2 3 7 bknd state' with
        | Error err -> Assert.Fail($"controlledModularExponentiation failed: {err}")
        | Ok resultState ->
            let value = readRegisterValue targetQubits resultState
            Assert.Equal(3, value)

    [<Fact>]
    let ``controlledModularExponentiation: control=0 leaves state unchanged`` () =
        let controlQubit = 0
        let targetQubits = [ 1; 2; 3 ]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 5

        // Control qubit stays |0>
        match Shor.controlledModularExponentiation controlQubit targetQubits 2 2 7 bknd state with
        | Error err -> Assert.Fail($"controlledModularExponentiation failed: {err}")
        | Ok resultState ->
            let value = readRegisterValue targetQubits resultState
            Assert.Equal(5, value)

    // ========================================================================
    // No longer NotImplemented
    // ========================================================================

    [<Fact>]
    let ``controlledModularMultiplication no longer returns NotImplemented`` () =
        // The whole point: this function used to always return NotImplemented.
        // Now it should succeed (or fail with a different error, never NotImplemented).
        let controlQubit = 0
        let targetQubits = [ 1; 2; 3 ]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 1

        let state' =
            (bknd.ApplyOperation (QuantumOperation.Gate(X controlQubit)) state)
            |> Result.defaultWith (fun err -> failwith $"Control prep failed: {err}")

        match Shor.controlledModularMultiplication controlQubit targetQubits 3 5 bknd state' with
        | Error(QuantumError.NotImplemented _) ->
            Assert.Fail("controlledModularMultiplication should no longer return NotImplemented")
        | Error err -> Assert.Fail($"Unexpected error: {err}")
        | Ok _ -> () // Success - the stub has been properly wired

    // ========================================================================
    // WORKSPACE CLEANLINESS
    // ========================================================================
    //
    // Shor's correctness rests on the modular-multiplication workspace being
    // *uncomputed*, not merely unread. If the temp and ancilla qubits kept any
    // record of the input y, they would stay entangled with the target register
    // and dephase the counting register, so QPE would return noise instead of
    // s/r. These tests assert the workspace returns to |0…0⟩ exactly — which is
    // what the Beauregard (2003) circuit in Arithmetic buys over a "dirty
    // ancilla" construction.

    /// Read the single computational basis state a deterministic circuit produced.
    let private basisIndexOf (state: QuantumState) : int =
        match state with
        | QuantumState.StateVector sv ->
            let topIdx, topProb = Measurement.getTopOutcomes 1 sv |> Array.head

            if topProb < 0.99 then
                failwith $"State is not a computational basis state (top prob = {topProb:F6})"

            topIdx
        | other -> failwith $"Expected StateVector, got: {QuantumState.stateType other}"

    [<Fact>]
    let ``controlledModularMultiplication restores every workspace qubit to zero`` () =
        // N=5, a=3, n=3 bits. Layout: control=0, target=[1;2;3],
        // temp=[4;5;6], ancilla=[7;8;9;10] — everything but control and target.
        let controlQubit = 0
        let targetQubits = [ 1; 2; 3 ]
        let totalQubits = 11
        let workspaceQubits = [ 4 .. totalQubits - 1 ]

        for y in 1..4 do
            let (bknd, state) = prepareState totalQubits targetQubits y

            let state' =
                (bknd.ApplyOperation (QuantumOperation.Gate(X controlQubit)) state)
                |> Result.defaultWith (fun err -> failwith $"Control prep failed: {err}")

            match Shor.controlledModularMultiplication controlQubit targetQubits 3 5 bknd state' with
            | Error err -> Assert.Fail($"controlledModularMultiplication failed for y={y}: {err}")
            | Ok resultState ->
                // Target holds 3y mod 5 ...
                Assert.Equal((3 * y) % 5, readRegisterValue targetQubits resultState)

                // ... and every workspace qubit is back to |0⟩.
                let idx = basisIndexOf resultState

                for q in workspaceQubits do
                    Assert.Equal(0, (idx >>> q) &&& 1)

    [<Fact>]
    let ``controlledModularMultiplication leaves no workspace residue in superposition`` () =
        // The real test of uncomputation: run the multiplication on a superposition
        // of inputs. With a dirty workspace the temp register would stay correlated
        // with y and the target register would be a mixed state; with clean
        // uncomputation the result is a pure superposition over 3y mod 5 alone.
        let controlQubit = 0
        let targetQubits = [ 1; 2; 3 ]
        let totalQubits = 11

        let bknd = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

        let prepared =
            result {
                let! s0 = bknd.InitializeState totalQubits
                let! s1 = bknd.ApplyOperation (QuantumOperation.Gate(X controlQubit)) s0
                // |y⟩ over {0,1,2,3}: Hadamard the two low target qubits
                let! s2 = bknd.ApplyOperation (QuantumOperation.Gate(H targetQubits.[0])) s1
                return! bknd.ApplyOperation (QuantumOperation.Gate(H targetQubits.[1])) s2
            }

        let state =
            prepared |> Result.defaultWith (fun err -> failwith $"State prep failed: {err}")

        match Shor.controlledModularMultiplication controlQubit targetQubits 3 5 bknd state with
        | Error err -> Assert.Fail($"controlledModularMultiplication failed: {err}")
        | Ok resultState ->
            match resultState with
            | QuantumState.StateVector sv ->
                let dim = StateVector.dimension sv

                let amplitudes: Complex array =
                    Array.init dim (fun i -> StateVector.getAmplitude i sv)

                let occupied =
                    amplitudes
                    |> Array.indexed
                    |> Array.filter (fun (_, amp) -> amp.Magnitude > 1e-9)
                    |> Array.map fst

                // Four inputs → four outputs, one basis state each.
                Assert.Equal(4, occupied.Length)

                let outputs =
                    occupied
                    |> Array.map (fun idx ->
                        // No amplitude may sit outside the target register.
                        for q in 4 .. totalQubits - 1 do
                            Assert.Equal(0, (idx >>> q) &&& 1)

                        targetQubits |> List.mapi (fun pos q -> ((idx >>> q) &&& 1) <<< pos) |> List.sum)
                    |> Array.sort

                Assert.Equal<int array>([| 0; 1; 3; 4 |], outputs) // 3·{0,1,2,3} mod 5
            | other -> failwith $"Expected StateVector, got: {QuantumState.stateType other}"

    // ========================================================================
    // BEAUREGARD CIRCUIT AS OPERATIONS (unified lowering)
    // ========================================================================
    //
    // ModularExponentiationCircuit exposes the same Beauregard circuit as a
    // QuantumOperation list, so QPE can lower a ModularExponentiation unitary for any
    // backend instead of refusing it. These tests check the extracted circuit really is
    // period-finding QPE, not merely that something was produced.

    /// Marginal distribution over the counting register of a final state vector.
    let private countingDistribution (countingQubits: int) (state: QuantumState) : float[] =
        match state with
        | QuantumState.StateVector sv ->
            let dimension = StateVector.dimension sv
            let counts = Array.zeroCreate<float>(1 <<< countingQubits)

            for index in 0 .. dimension - 1 do
                let amplitude = StateVector.getAmplitude index sv
                let countingValue = index &&& ((1 <<< countingQubits) - 1)
                counts.[countingValue] <- counts.[countingValue] + amplitude.Magnitude * amplitude.Magnitude

            counts
        | other -> failwith $"Expected StateVector, got: {QuantumState.stateType other}"

    [<Fact>]
    let ``extracted Beauregard QPE circuit concentrates on multiples of 2^c / r`` () =
        // ord(7 mod 15) = 4 and 4 divides 2^3, so an exact QPE puts ALL of its probability
        // on k in {0, 2, 4, 6} — the multiples of 2^c / r. Anything leaking elsewhere means
        // the extracted circuit is not the circuit Arithmetic executes.
        let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        let baseNum, modulus, countingQubits = 7, 15, 3

        match ModularExponentiationCircuit.buildModExpQpe baseNum modulus countingQubits true with
        | Error err -> failwith $"Building the modular-exponentiation QPE circuit failed: {err}"
        | Ok ops ->
            let totalQubits = ModularExponentiationCircuit.totalQubitsFor modulus countingQubits

            let finalState =
                backend.InitializeState totalQubits
                |> Result.bind (UnifiedBackend.applySequence backend ops)

            match finalState with
            | Error err -> failwith $"Executing the extracted circuit failed: {err}"
            | Ok state ->
                let distribution = countingDistribution countingQubits state

                let onPeriodMultiples = [ 0; 2; 4; 6 ] |> List.sumBy (fun k -> distribution.[k])

                Assert.True(
                    onPeriodMultiples > 0.99,
                    $"Expected the counting register on multiples of 8/4, got %.4f{onPeriodMultiples} there; distribution = %A{distribution}"
                )

    [<Fact>]
    let ``extracted circuit and the executed one agree on the modular multiplication`` () =
        // The extraction runs Arithmetic's own code against a recording backend, so this
        // pins that recording and executing cannot drift: same control, same register, same
        // constant, same modulus, compared as final state vectors rather than as op lists.
        let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        let targetQubits = [ 1; 2; 3 ]
        let constant, modulus = 3, 5
        let totalQubits = 1 + ModularExponentiationCircuit.workspaceQubits 3 + 3

        // Control set to |1> so the multiplication actually fires.
        let withControlSet () =
            let (_, prepared) = prepareState totalQubits targetQubits 2
            backend.ApplyOperation (QuantumOperation.Gate(X 0)) prepared

        let executed =
            withControlSet ()
            |> Result.bind (Shor.controlledModularMultiplication 0 targetQubits constant modulus backend)

        let extracted =
            ModularExponentiationCircuit.buildControlledModularMultiplication 0 targetQubits constant modulus
            |> Result.bind (fun ops -> withControlSet () |> Result.bind (UnifiedBackend.applySequence backend ops))

        match executed, extracted with
        | Ok(QuantumState.StateVector left), Ok(QuantumState.StateVector right) ->
            let dimension = StateVector.dimension left
            Assert.Equal(dimension, StateVector.dimension right)

            for index in 0 .. dimension - 1 do
                let a = StateVector.getAmplitude index left
                let b = StateVector.getAmplitude index right
                Assert.True((a - b).Magnitude < 1e-9, $"Amplitude {index} differs: {a} vs {b}")
        | Error err, _ -> failwith $"Executed path failed: {err}"
        | _, Error err -> failwith $"Extracted path failed: {err}"
        | other -> failwith $"Expected two state vectors, got: %A{other}"

    [<Fact>]
    let ``QPE execute now accepts a modular-exponentiation unitary`` () =
        // This is the unification in one assertion. QPE.execute used to refuse this config
        // outright and tell the caller to go to Shor.estimateModExpPhase, because the
        // Beauregard circuit existed only as an execution inside Shor. It is now lowered by
        // QPE.plan like any other unitary, so the generic entry point handles it.
        let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

        let config: QPE.QPEConfig =
            {
                CountingQubits = 3
                TargetQubits = 4 // ceil(log2 15)
                UnitaryOperator = QPE.UnitaryOperator.ModularExponentiation(7, 15)
                EigenVector = None
            }

        match QPE.execute config backend with
        | Error err -> failwith $"QPE.execute refused a modular-exponentiation unitary: {err}"
        | Ok result ->
            // ord(7 mod 15) = 4, so the phase is s/4 and the peak sits on a multiple of 1/4.
            let scaled = result.EstimatedPhase * 4.0
            let nearestMultiple = System.Math.Round scaled

            Assert.True(
                abs (scaled - nearestMultiple) < 1e-9,
                $"Phase %.6f{result.EstimatedPhase} is not a multiple of 1/4, so period finding would not recover r=4"
            )

    [<Fact>]
    let ``QPE lowers modular exponentiation for a backend that cannot take the intent`` () =
        // LocalBackend declines the modular-exponentiation QPE intent (its native handler has
        // no Beauregard arithmetic), so the planner must hand it the lowered circuit rather
        // than failing. That fallback is what makes this work on any gate backend.
        let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

        let intent: QPE.QpeExecutionIntent =
            {
                ApplyBitReversalSwaps = false
                Config =
                    {
                        CountingQubits = 3
                        TargetQubits = 4
                        UnitaryOperator = QPE.UnitaryOperator.ModularExponentiation(7, 15)
                        EigenVector = None
                    }
                Exactness = QPE.Exact
            }

        match QPE.plan backend intent with
        | Ok(QPE.QpePlan.ExecuteViaOps(ops, _)) -> Assert.NotEmpty ops
        | Ok(QPE.QpePlan.ExecuteNatively _) ->
            failwith "LocalBackend has no native modular-exponentiation QPE; planning it would error at execution"
        | Error err -> failwith $"Planning failed: {err}"

    [<Fact>]
    let ``lowered modular-exponentiation QPE keeps its inverse QFT as an intent`` () =
        // Route A only composes if sub-algorithms survive lowering. An op list that expands
        // the inverse QFT into H and controlled-phase gates dissolves it into primitives that
        // never reach a backend's intent dispatcher, so a backend able to transform the
        // counting register directly would never be asked. This is a structural assertion
        // because the behavioural difference is invisible on a gate backend, which lowers the
        // intent straight back to the same gates.
        match ModularExponentiationCircuit.buildModExpQpe 7 15 3 false with
        | Error err -> failwith $"Building the circuit failed: {err}"
        | Ok ops ->
            let qftIntents =
                ops
                |> List.choose (function
                    | QuantumOperation.Algorithm(AlgorithmOperation.QFT intent) -> Some intent
                    | _ -> None)

            let intent = Assert.Single qftIntents
            Assert.Equal(3, intent.NumQubits)
            Assert.True(intent.Inverse, "Period finding reads the counting register with an INVERSE QFT")

            // The swaps must NOT be folded into the intent: its inverse form applies them
            // before the rotations, whereas here they belong after. Getting this wrong put
            // only 62% of the probability on the period multiples instead of over 99%.
            Assert.False(intent.ApplySwaps, "Bit reversal is appended explicitly, not delegated to the intent")

    [<Fact>]
    let ``gates-form modular-exponentiation QPE carries no intents and matches the intent form`` () =
        // The whole-circuit path lowers an op list to a gate circuit and REFUSES any
        // algorithm intent it meets, so it needs the inverse QFT spelled out. That is what
        // buildModExpQpeAsGates is for. It must be the same unitary as the intent form —
        // buildQftGateOps [0..c-1] true false is exactly what the intent lowers to — so both
        // must put the counting register on the same period multiples.
        let backend = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        let baseNum, modulus, countingQubits = 7, 15, 3

        match ModularExponentiationCircuit.buildModExpQpeAsGates baseNum modulus countingQubits true with
        | Error err -> failwith $"Building the gates-form circuit failed: {err}"
        | Ok gateOps ->
            let intents =
                gateOps
                |> List.filter (function
                    | QuantumOperation.Algorithm _ -> true
                    | _ -> false)

            Assert.Empty intents

            let totalQubits = ModularExponentiationCircuit.totalQubitsFor modulus countingQubits

            let onMultiples (ops: QuantumOperation list) =
                backend.InitializeState totalQubits
                |> Result.bind (UnifiedBackend.applySequence backend ops)
                |> Result.map (fun state ->
                    let d = countingDistribution countingQubits state
                    [ 0; 2; 4; 6 ] |> List.sumBy (fun k -> d.[k]))

            match
                onMultiples gateOps,
                ModularExponentiationCircuit.buildModExpQpe baseNum modulus countingQubits true
                |> Result.bind onMultiples
            with
            | Ok gates, Ok intent ->
                Assert.True(gates > 0.99, $"gates form puts only %.4f{gates} on the period multiples")
                Assert.True(abs (gates - intent) < 1e-9, $"gates form %.6f{gates} vs intent form %.6f{intent}")
            | Error err, _ -> failwith $"Gates form failed to execute: {err}"
            | _, Error err -> failwith $"Intent form failed to execute: {err}"
