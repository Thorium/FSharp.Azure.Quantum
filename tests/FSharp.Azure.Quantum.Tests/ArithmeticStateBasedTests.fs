namespace FSharp.Azure.Quantum.Tests

open System
open System.Numerics
open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.LocalSimulator

module QA = FSharp.Azure.Quantum.Algorithms.QuantumArithmetic

module ArithmeticStateBasedTests =

    let private formatTopOutcomes (sv: StateVector.StateVector) (topN: int) : string =
        Measurement.getTopOutcomes topN sv
        |> Array.map (fun (idx, prob) -> $"{idx}:{prob:F6}")
        |> String.concat ", "

    let private extractRegisterValue (registerQubits: int list) (basisIndex: int) : int =
        registerQubits
        |> List.mapi (fun pos q -> ((basisIndex >>> q) &&& 1) <<< pos)
        |> List.sum

    let private executeCircuit (circuit: Circuit) : StateVector.StateVector =
        let numQubits = qubitCount circuit
        let initialState = StateVector.init numQubits

        getGates circuit
        |> List.fold (fun s g ->
            match g with
            | X q -> Gates.applyX q s
            | Y q -> Gates.applyY q s
            | Z q -> Gates.applyZ q s
            | H q -> Gates.applyH q s
            | S q -> Gates.applyS q s
            | SDG q -> Gates.applySDG q s
            | T q -> Gates.applyT q s
            | TDG q -> Gates.applyTDG q s
            | P (q, angle) -> Gates.applyP q angle s
            | CP (c, t, angle) -> Gates.applyCPhase c t angle s
            | CRX (c, t, angle) -> Gates.applyCRX c t angle s
            | CRY (c, t, angle) -> Gates.applyCRY c t angle s
            | CRZ (c, t, angle) -> Gates.applyCRZ c t angle s
            | RX (q, angle) -> Gates.applyRx q angle s
            | RY (q, angle) -> Gates.applyRy q angle s
            | RZ (q, angle) -> Gates.applyRz q angle s
            | U3 (q, theta, phi, lambda) ->
                s |> Gates.applyRz q lambda |> Gates.applyRy q theta |> Gates.applyRz q phi
            | CNOT (c, t) -> Gates.applyCNOT c t s
            | CZ (c, t) -> Gates.applyCZ c t s
            | MCZ (controls, t) -> Gates.applyMultiControlledZ controls t s
            | SWAP (q1, q2) -> Gates.applySWAP q1 q2 s
            | CCX (c1, c2, t) -> Gates.applyCCX c1 c2 t s
            | Measure _ -> s
            | Reset _ -> s
            | Barrier _ -> s) initialState

    let private runCircuitAddConstant (numQubits: int) (registerQubits: int list) (constant: int) (prep: Circuit -> Circuit) : int * float =
        // Use the circuit-based compatible API as reference.
        // Controlled version with a dedicated control qubit to emulate unconditional add.
        // NOTE: The returned basis index is over the *total* qubits (including control).
        let controlQubit = numQubits
        let totalQubits = numQubits + 1

        let circuit =
            empty totalQubits
            |> addGate (X controlQubit)
            |> prep
            |> QA.controlledAddConstant controlQubit registerQubits constant

        let sv = executeCircuit circuit
        // Return top basis index/prob (circuit should be basis state)
        Measurement.getTopOutcomes 1 sv |> Array.head

    [<Fact>]
    let ``State-based addConstant(0) is identity on prefix register`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let registerQubits = [ 0; 1 ]
        let constantToAdd = 0

        match backend.InitializeState 2 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok state0 ->
            // Prepare a basis state: |10⟩ => index 2
            let prepOp = QuantumOperation.Gate (X 1)

            match backend.ApplyOperation prepOp state0 with
            | Error err -> Assert.Fail($"State prep failed: {err}")
            | Ok stateWithX ->
                match Arithmetic.addConstant registerQubits constantToAdd stateWithX backend with
                | Error err -> Assert.Fail($"addConstant failed: {err}")
                | Ok result ->
                    match result.State with
                    | QuantumState.StateVector sv ->
                        let expectedBasisIndex = 2
                        let prob = Measurement.getBasisStateProbability expectedBasisIndex sv
                        let top = formatTopOutcomes sv 4

                        Assert.True(
                            prob > 0.999999,
                            $"Expected identity (basis index {expectedBasisIndex}). Observed prob={prob:F6}. Top outcomes: {top}"
                        )
                    | otherStateType ->
                        Assert.Fail($"Expected StateVector simulation state, got: {QuantumState.stateType otherStateType}")

    [<Fact>]
    let ``State-based addConstant works on prefix register`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let registerQubits = [ 0; 1 ]
        let constantToAdd = 1

        match backend.InitializeState 2 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok state0 ->
            // Prepare |x⟩ = |10⟩ on the register => basis index 2.
            let prepOp = QuantumOperation.Gate (X 1)

            match backend.ApplyOperation prepOp state0 with
            | Error err -> Assert.Fail($"State prep failed: {err}")
            | Ok stateWithX ->
                match Arithmetic.addConstant registerQubits constantToAdd stateWithX backend with
                | Error err -> Assert.Fail($"addConstant failed: {err}")
                | Ok result ->
                    let expectedBasisIndex = 3

                    match result.State with
                    | QuantumState.StateVector sv ->
                        let prob = Measurement.getBasisStateProbability expectedBasisIndex sv
                        let top = formatTopOutcomes sv 4

                        let (circuitIdx, circuitProb) =
                            runCircuitAddConstant
                                2
                                registerQubits
                                constantToAdd
                                (fun c -> c |> addGate (X 1))

                        let expectedRegisterValue = extractRegisterValue registerQubits expectedBasisIndex
                        let observedCircuitRegValue = extractRegisterValue registerQubits circuitIdx

                        Assert.True(
                            prob > 0.999999,
                            $"Expected basis index {expectedBasisIndex} (reg={expectedRegisterValue}) with prob≈1. Observed prob={prob:F6}. Top outcomes: {top}. Circuit reference: idx={circuitIdx}, reg={observedCircuitRegValue}, prob={circuitProb:F6}"
                        )
                    | otherStateType ->
                        Assert.Fail($"Expected StateVector simulation state, got: {QuantumState.stateType otherStateType}")

    [<Fact>]
    let ``State-based addConstant(0) is identity on non-prefix register`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let registerQubits = [ 1; 2 ]
        let constantToAdd = 0

        match backend.InitializeState 3 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok state0 ->
            // Prepare |x⟩ = |10⟩ on the register => basis index 4.
            let prepOp = QuantumOperation.Gate (X 2)

            match backend.ApplyOperation prepOp state0 with
            | Error err -> Assert.Fail($"State prep failed: {err}")
            | Ok stateWithX ->
                match Arithmetic.addConstant registerQubits constantToAdd stateWithX backend with
                | Error err -> Assert.Fail($"addConstant failed: {err}")
                | Ok result ->
                    match result.State with
                    | QuantumState.StateVector sv ->
                        let expectedBasisIndex = 4
                        let prob = Measurement.getBasisStateProbability expectedBasisIndex sv
                        let top = formatTopOutcomes sv 8

                        Assert.True(
                            prob > 0.999999,
                            $"Expected identity (basis index {expectedBasisIndex}). Observed prob={prob:F6}. Top outcomes: {top}"
                        )
                    | otherStateType ->
                        Assert.Fail($"Expected StateVector simulation state, got: {QuantumState.stateType otherStateType}")

    [<Fact>]
    let ``Circuit reference harness prepares expected basis state`` () =
        // Sanity check: our circuit runner uses the same basis indexing convention
        // as Measurement.getBasisStateProbability.
        let circuit = empty 2 |> addGate (X 1)
        let sv = executeCircuit circuit
        Assert.True(Measurement.getBasisStateProbability 2 sv > 0.999999)

    [<Fact>]
    let ``QFT lowering round-trip is identity (non-prefix register)`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // QFT gate sequence should be unitary, even on a sub-register.
        // Use qubits [1;2] within a 3-qubit state.
        let registerQubits = [ 1; 2 ]

        match backend.InitializeState 3 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok state0 ->
            // Prepare a basis state with register value 2 (|10⟩ on [1;2]) => global index 4.
            let prepOp = QuantumOperation.Gate (X 2)

            match backend.ApplyOperation prepOp state0 with
            | Error err -> Assert.Fail($"State prep failed: {err}")
            | Ok prepared ->
                let qftOps = Arithmetic.buildQftGateOps registerQubits false false
                let iqftOps = Arithmetic.buildQftGateOps registerQubits true false

                match UnifiedBackend.applySequence backend qftOps prepared with
                | Error err -> Assert.Fail($"Applying QFT ops failed: {err}")
                | Ok inFourier ->
                    match UnifiedBackend.applySequence backend iqftOps inFourier with
                    | Error err -> Assert.Fail($"Applying inverse QFT ops failed: {err}")
                    | Ok back ->
                        match back with
                        | QuantumState.StateVector sv ->
                            let expectedBasisIndex = 4
                            let prob = Measurement.getBasisStateProbability expectedBasisIndex sv
                            let top = formatTopOutcomes sv 8

                            Assert.True(
                                prob > 0.999999,
                                $"Expected QFT then inverse QFT to return basis index {expectedBasisIndex}. Observed prob={prob:F6}. Top outcomes: {top}"
                            )
                        | otherStateType ->
                            Assert.Fail($"Expected StateVector simulation state, got: {QuantumState.stateType otherStateType}")

    // ========================================================================
    // MODULAR ARITHMETIC TESTS (Beauregard algorithm)
    // ========================================================================

    /// Helper: prepare a state with qubit values encoding an integer on registerQubits,
    /// allocating totalQubits for the full state (including ancilla space).
    let private prepareRegisterState (totalQubits: int) (registerQubits: int list) (value: int) =
        let bknd = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        match bknd.InitializeState totalQubits with
        | Error err -> failwith $"InitializeState failed: {err}"
        | Ok state0 ->
            let finalState =
                registerQubits
                |> List.indexed
                |> List.fold (fun st (i, q) ->
                    if (value >>> i) &&& 1 = 1 then
                        match bknd.ApplyOperation (QuantumOperation.Gate (X q)) st with
                        | Error err -> failwith $"State prep failed on qubit {q}: {err}"
                        | Ok s -> s
                    else
                        st) state0
            (bknd, finalState)

    /// Helper: read register value from a state (must be a computational basis state)
    let private readRegisterValue' (registerQubits: int list) (state: QuantumState) : int =
        match state with
        | QuantumState.StateVector sv ->
            let topIdx, topProb = Measurement.getTopOutcomes 1 sv |> Array.head
            if topProb < 0.99 then
                failwith $"State is not a computational basis state (top prob = {topProb:F6})"
            extractRegisterValue registerQubits topIdx
        | other ->
            failwith $"Expected StateVector, got: {QuantumState.stateType other}"

    [<Fact>]
    let ``addConstantModN: (3 + 4) mod 5 = 2`` () =
        let registerQubits = [0; 1; 2]  // 3-bit register (holds 0..7)
        let totalQubits = 5  // 3 register + overflow + flag
        let (bknd, state) = prepareRegisterState totalQubits registerQubits 3
        match Arithmetic.addConstantModN registerQubits 4 5 state bknd with
        | Error err -> Assert.Fail($"addConstantModN failed: {err}")
        | Ok result ->
            let value = readRegisterValue' registerQubits result.State
            Assert.Equal(2, value)

    [<Fact>]
    let ``addConstantModN: (1 + 2) mod 5 = 3 (no reduction)`` () =
        let registerQubits = [0; 1; 2]
        let totalQubits = 5
        let (bknd, state) = prepareRegisterState totalQubits registerQubits 1
        match Arithmetic.addConstantModN registerQubits 2 5 state bknd with
        | Error err -> Assert.Fail($"addConstantModN failed: {err}")
        | Ok result ->
            let value = readRegisterValue' registerQubits result.State
            Assert.Equal(3, value)

    [<Fact>]
    let ``addConstantModN: (4 + 3) mod 5 = 2`` () =
        let registerQubits = [0; 1; 2]
        let totalQubits = 5
        let (bknd, state) = prepareRegisterState totalQubits registerQubits 4
        match Arithmetic.addConstantModN registerQubits 3 5 state bknd with
        | Error err -> Assert.Fail($"addConstantModN failed: {err}")
        | Ok result ->
            let value = readRegisterValue' registerQubits result.State
            Assert.Equal(2, value)

    [<Fact>]
    let ``addConstantModN: (0 + 0) mod 5 = 0 (edge case)`` () =
        let registerQubits = [0; 1; 2]
        let totalQubits = 5
        let (bknd, state) = prepareRegisterState totalQubits registerQubits 0
        match Arithmetic.addConstantModN registerQubits 0 5 state bknd with
        | Error err -> Assert.Fail($"addConstantModN failed: {err}")
        | Ok result ->
            let value = readRegisterValue' registerQubits result.State
            Assert.Equal(0, value)

    [<Fact>]
    let ``addConstantModN: (4 + 4) mod 5 = 3 (boundary)`` () =
        let registerQubits = [0; 1; 2]
        let totalQubits = 5
        let (bknd, state) = prepareRegisterState totalQubits registerQubits 4
        match Arithmetic.addConstantModN registerQubits 4 5 state bknd with
        | Error err -> Assert.Fail($"addConstantModN failed: {err}")
        | Ok result ->
            let value = readRegisterValue' registerQubits result.State
            Assert.Equal(3, value)

    [<Fact>]
    let ``addConstantModN restores ancilla qubits to |0⟩`` () =
        let registerQubits = [0; 1; 2]
        let totalQubits = 5
        let overflowQubit = 3
        let flagQubit = 4
        let (bknd, state) = prepareRegisterState totalQubits registerQubits 3
        match Arithmetic.addConstantModN registerQubits 4 5 state bknd with
        | Error err -> Assert.Fail($"addConstantModN failed: {err}")
        | Ok result ->
            match result.State with
            | QuantumState.StateVector sv ->
                let dim = StateVector.dimension sv
                let overflowProb0, flagProb0 =
                    seq { 0 .. dim - 1 }
                    |> Seq.fold (fun (ov, fl) i ->
                        let amp = StateVector.getAmplitude i sv
                        let p = amp.Magnitude * amp.Magnitude
                        let ov' = if (i >>> overflowQubit) &&& 1 = 0 then ov + p else ov
                        let fl' = if (i >>> flagQubit) &&& 1 = 0 then fl + p else fl
                        (ov', fl')) (0.0, 0.0)
                Assert.True(overflowProb0 > 0.999, $"Overflow qubit should be |0>, P(0) = {overflowProb0:F6}")
                Assert.True(flagProb0 > 0.999, $"Flag qubit should be |0>, P(0) = {flagProb0:F6}")
            | _ -> Assert.Fail("Expected StateVector")

    [<Fact>]
    let ``subtractConstantModN: (2 - 4) mod 5 = 3`` () =
        let registerQubits = [0; 1; 2]
        let totalQubits = 5
        let (bknd, state) = prepareRegisterState totalQubits registerQubits 2
        match Arithmetic.subtractConstantModN registerQubits 4 5 state bknd with
        | Error err -> Assert.Fail($"subtractConstantModN failed: {err}")
        | Ok result ->
            let value = readRegisterValue' registerQubits result.State
            Assert.Equal(3, value)

    [<Fact>]
    let ``subtractConstantModN: (3 - 1) mod 5 = 2 (no wrap)`` () =
        let registerQubits = [0; 1; 2]
        let totalQubits = 5
        let (bknd, state) = prepareRegisterState totalQubits registerQubits 3
        match Arithmetic.subtractConstantModN registerQubits 1 5 state bknd with
        | Error err -> Assert.Fail($"subtractConstantModN failed: {err}")
        | Ok result ->
            let value = readRegisterValue' registerQubits result.State
            Assert.Equal(2, value)

    [<Fact>]
    let ``addConstantModN then subtractConstantModN is identity`` () =
        let registerQubits = [0; 1; 2]
        let totalQubits = 5
        let (bknd, state) = prepareRegisterState totalQubits registerQubits 3
        match Arithmetic.addConstantModN registerQubits 4 5 state bknd with
        | Error err -> Assert.Fail($"add failed: {err}")
        | Ok addResult ->
            match Arithmetic.subtractConstantModN registerQubits 4 5 addResult.State bknd with
            | Error err -> Assert.Fail($"subtract failed: {err}")
            | Ok subResult ->
                let value = readRegisterValue' registerQubits subResult.State
                Assert.Equal(3, value)

    [<Fact>]
    let ``controlledAddConstantModN: applies when control=|1⟩`` () =
        let registerQubits = [0; 1; 2]
        let controlQubit = 3
        // ancilla: overflow = max(3, 2)+1 = 4, flag = 5
        // internally, step 4 (doublyControlledAddConstant) allocates AND-ancilla at
        // max(control=3, flag=5, max(extendedReg)=4) + 1 = 6 → need 7 qubits
        let totalQubits = 7
        let bknd = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        match bknd.InitializeState totalQubits with
        | Error err -> Assert.Fail($"Init failed: {err}")
        | Ok state0 ->
            let ops = [
                QuantumOperation.Gate (X controlQubit)
                QuantumOperation.Gate (X 0)
                QuantumOperation.Gate (X 1)
            ]
            let preparedState =
                ops
                |> List.fold (fun st op ->
                    match bknd.ApplyOperation op st with
                    | Error err -> failwith $"Prep failed: {err}"
                    | Ok s -> s) state0
            match Arithmetic.controlledAddConstantModN controlQubit registerQubits 4 5 preparedState bknd with
            | Error err -> Assert.Fail($"controlledAddConstantModN failed: {err}")
            | Ok result ->
                let value = readRegisterValue' registerQubits result.State
                Assert.Equal(2, value)  // (3+4) mod 5 = 2

    [<Fact>]
    let ``controlledAddConstantModN: no-op when control=|0⟩`` () =
        let registerQubits = [0; 1; 2]
        let controlQubit = 3
        // Same qubit requirements as control=|1⟩ test (7 qubits needed)
        let totalQubits = 7
        let bknd = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        match bknd.InitializeState totalQubits with
        | Error err -> Assert.Fail($"Init failed: {err}")
        | Ok state0 ->
            let ops = [
                QuantumOperation.Gate (X 0)
                QuantumOperation.Gate (X 1)
            ]
            let preparedState =
                ops
                |> List.fold (fun st op ->
                    match bknd.ApplyOperation op st with
                    | Error err -> failwith $"Prep failed: {err}"
                    | Ok s -> s) state0
            match Arithmetic.controlledAddConstantModN controlQubit registerQubits 4 5 preparedState bknd with
            | Error err -> Assert.Fail($"controlledAddConstantModN failed: {err}")
            | Ok result ->
                let value = readRegisterValue' registerQubits result.State
                Assert.Equal(3, value)  // Unchanged

    // ========================================================================
    // MODULAR MULTIPLY TESTS (fast 2-bit register versions)
    // ========================================================================

    [<Fact>]
    let ``multiplyConstantModN: 1 * 3 mod 5 = 3 (fast)`` () =
        let inputQubits = [0; 1; 2]   // 3-bit register (holds 0..7)
        let outputQubits = [3; 4; 5]
        // controlledAddConstantModN with control=inputQubits[k], register=outputQubits
        // max qubit = max(2, 5) = 5, overflow = 6, flag = 7
        // doublyControlledAddConstant AND-ancilla = max(2, 7, 6)+1 = 8
        // → need 9 qubits
        let totalQubits = 9
        let (bknd, state) = prepareRegisterState totalQubits inputQubits 1
        match Arithmetic.multiplyConstantModN inputQubits outputQubits 3 5 state bknd with
        | Error err -> Assert.Fail($"multiplyConstantModN failed: {err}")
        | Ok result ->
            let outValue = readRegisterValue' outputQubits result.State
            Assert.Equal(3, outValue)

    [<Fact>]
    let ``multiplyConstantModN: 2 * 3 mod 5 = 1 (fast)`` () =
        let inputQubits = [0; 1; 2]
        let outputQubits = [3; 4; 5]
        let totalQubits = 9
        let (bknd, state) = prepareRegisterState totalQubits inputQubits 2
        match Arithmetic.multiplyConstantModN inputQubits outputQubits 3 5 state bknd with
        | Error err -> Assert.Fail($"multiplyConstantModN failed: {err}")
        | Ok result ->
            let outValue = readRegisterValue' outputQubits result.State
            Assert.Equal(1, outValue)  // 2 * 3 mod 5 = 6 mod 5 = 1

    [<Fact>]
    let ``multiplyConstantModN: 3 * 3 mod 5 = 4 (fast)`` () =
        let inputQubits = [0; 1; 2]
        let outputQubits = [3; 4; 5]
        let totalQubits = 9
        let (bknd, state) = prepareRegisterState totalQubits inputQubits 3
        match Arithmetic.multiplyConstantModN inputQubits outputQubits 3 5 state bknd with
        | Error err -> Assert.Fail($"multiplyConstantModN failed: {err}")
        | Ok result ->
            let outValue = readRegisterValue' outputQubits result.State
            Assert.Equal(4, outValue)  // 3 * 3 mod 5 = 9 mod 5 = 4
            let inValue = readRegisterValue' inputQubits result.State
            Assert.Equal(3, inValue)  // Input preserved

    // ========================================================================
    // MODULAR MULTIPLY TESTS (original 4-bit register — skipped for speed)
    // ========================================================================

    [<Fact(Skip = "Long-running: use 3-bit equivalent above")>]
    let ``multiplyConstantModN: 3 * 7 mod 15 = 6`` () =
        let inputQubits = [0; 1; 2; 3]
        let outputQubits = [4; 5; 6; 7]
        let totalQubits = 11
        let (bknd, state) = prepareRegisterState totalQubits inputQubits 3
        match Arithmetic.multiplyConstantModN inputQubits outputQubits 7 15 state bknd with
        | Error err -> Assert.Fail($"multiplyConstantModN failed: {err}")
        | Ok result ->
            let outValue = readRegisterValue' outputQubits result.State
            Assert.Equal(6, outValue)
            let inValue = readRegisterValue' inputQubits result.State
            Assert.Equal(3, inValue)

    [<Fact(Skip = "Long-running: use 3-bit equivalent above")>]
    let ``multiplyConstantModN: 2 * 7 mod 15 = 14`` () =
        let inputQubits = [0; 1; 2; 3]
        let outputQubits = [4; 5; 6; 7]
        let totalQubits = 11
        let (bknd, state) = prepareRegisterState totalQubits inputQubits 2
        match Arithmetic.multiplyConstantModN inputQubits outputQubits 7 15 state bknd with
        | Error err -> Assert.Fail($"multiplyConstantModN failed: {err}")
        | Ok result ->
            let outValue = readRegisterValue' outputQubits result.State
            Assert.Equal(14, outValue)

    [<Fact(Skip = "Long-running: use 3-bit equivalent above")>]
    let ``multiplyConstantModN: 1 * 7 mod 15 = 7`` () =
        let inputQubits = [0; 1; 2; 3]
        let outputQubits = [4; 5; 6; 7]
        let totalQubits = 11
        let (bknd, state) = prepareRegisterState totalQubits inputQubits 1
        match Arithmetic.multiplyConstantModN inputQubits outputQubits 7 15 state bknd with
        | Error err -> Assert.Fail($"multiplyConstantModN failed: {err}")
        | Ok result ->
            let outValue = readRegisterValue' outputQubits result.State
            Assert.Equal(7, outValue)

    // ========================================================================
    // CONTROLLED MULTIPLY IN-PLACE TESTS (fast 3-bit register versions)
    // ========================================================================

    [<Fact>]
    let ``controlledMultiplyConstantModNInPlace: 2 * 3 mod 5 = 1 with clean temp (fast)`` () =
        // controlQubit=0, register=[1;2;3], temp=[4;5;6]
        // doublyControlledAddConstantModN: max(0, 3, 6) = 6, AND-ancilla=7
        // controlledAddConstantModN with andAncilla=7 as control, temp as register:
        //   overflow=8, flag=9, inner doublyControlledAddConstant AND-ancilla=10
        // → need 11 qubits
        let controlQubit = 0
        let registerQubits = [1; 2; 3]
        let tempQubits = [4; 5; 6]
        let totalQubits = 11
        let bknd = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        match bknd.InitializeState totalQubits with
        | Error err -> Assert.Fail($"Init failed: {err}")
        | Ok state0 ->
            let ops = [
                QuantumOperation.Gate (X controlQubit)
                QuantumOperation.Gate (X registerQubits.[1])  // bit 1 → value 2
            ]
            let preparedState =
                ops
                |> List.fold (fun st op ->
                    match bknd.ApplyOperation op st with
                    | Error err -> failwith $"Prep failed: {err}"
                    | Ok s -> s) state0
            match Arithmetic.controlledMultiplyConstantModNInPlace controlQubit registerQubits tempQubits 3 5 preparedState bknd with
            | Error err -> Assert.Fail($"controlledMultiplyConstantModNInPlace failed: {err}")
            | Ok result ->
                let regValue = readRegisterValue' registerQubits result.State
                Assert.Equal(1, regValue)  // 2*3 mod 5 = 6 mod 5 = 1

                match result.State with
                | QuantumState.StateVector sv ->
                    let dim = StateVector.dimension sv
                    for i in 0 .. List.length tempQubits - 1 do
                        let tq = tempQubits.[i]
                        let prob0 =
                            seq { 0 .. dim - 1 }
                            |> Seq.sumBy (fun j ->
                                let amp = StateVector.getAmplitude j sv
                                let p = amp.Magnitude * amp.Magnitude
                                if (j >>> tq) &&& 1 = 0 then p else 0.0)
                        Assert.True(prob0 > 0.95,
                            $"Temp qubit {i} (qubit #{tq}) should be |0>, but P(0) = {prob0:F6}")
                | _ -> Assert.Fail("Expected StateVector")

    [<Fact>]
    let ``controlledMultiplyConstantModNInPlace: control=|0⟩ leaves state unchanged (fast)`` () =
        let controlQubit = 0
        let registerQubits = [1; 2; 3]
        let tempQubits = [4; 5; 6]
        let totalQubits = 11
        let bknd = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        match bknd.InitializeState totalQubits with
        | Error err -> Assert.Fail($"Init failed: {err}")
        | Ok state0 ->
            let ops = [
                QuantumOperation.Gate (X registerQubits.[0])
                QuantumOperation.Gate (X registerQubits.[2])
            ]
            let preparedState =
                ops
                |> List.fold (fun st op ->
                    match bknd.ApplyOperation op st with
                    | Error err -> failwith $"Prep failed: {err}"
                    | Ok s -> s) state0
            match Arithmetic.controlledMultiplyConstantModNInPlace controlQubit registerQubits tempQubits 3 5 preparedState bknd with
            | Error err -> Assert.Fail($"controlledMultiplyConstantModNInPlace failed: {err}")
            | Ok result ->
                let regValue = readRegisterValue' registerQubits result.State
                Assert.Equal(5, regValue)  // Unchanged

    // ========================================================================
    // CONTROLLED MULTIPLY IN-PLACE TESTS (original 4-bit register — skipped for speed)
    // ========================================================================

    [<Fact(Skip = "Long-running: use 3-bit equivalent above")>]
    let ``controlledMultiplyConstantModNInPlace: 2 * 7 mod 15 = 14 with clean temp`` () =
        // This tests the fix for Bug 3: temp qubits should now be cleanly restored to |0⟩
        // controlQubit=0, register=[1..4], temp=[5..8]
        // doublyControlledAddConstantModN needs AND-ancilla above max qubit in use
        // max qubit = max(0, 4, 8) = 8, AND-ancilla=9
        // then controlledAddConstantModN with andAncilla=9 as control, temp as register:
        //   overflow=10, flag=11, inner doublyControlledAddConstant AND-ancilla=12
        // → need 13 qubits
        let controlQubit = 0
        let registerQubits = [1; 2; 3; 4]
        let tempQubits = [5; 6; 7; 8]
        let totalQubits = 13
        let bknd = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        match bknd.InitializeState totalQubits with
        | Error err -> Assert.Fail($"Init failed: {err}")
        | Ok state0 ->
            let ops = [
                QuantumOperation.Gate (X controlQubit)
                QuantumOperation.Gate (X registerQubits.[1])  // bit 1 → value 2
            ]
            let preparedState =
                ops
                |> List.fold (fun st op ->
                    match bknd.ApplyOperation op st with
                    | Error err -> failwith $"Prep failed: {err}"
                    | Ok s -> s) state0
            match Arithmetic.controlledMultiplyConstantModNInPlace controlQubit registerQubits tempQubits 7 15 preparedState bknd with
            | Error err -> Assert.Fail($"controlledMultiplyConstantModNInPlace failed: {err}")
            | Ok result ->
                let regValue = readRegisterValue' registerQubits result.State
                Assert.Equal(14, regValue)  // 2*7 mod 15 = 14

                match result.State with
                | QuantumState.StateVector sv ->
                    let dim = StateVector.dimension sv
                    for i in 0 .. List.length tempQubits - 1 do
                        let tq = tempQubits.[i]
                        let prob0 =
                            seq { 0 .. dim - 1 }
                            |> Seq.sumBy (fun j ->
                                let amp = StateVector.getAmplitude j sv
                                let p = amp.Magnitude * amp.Magnitude
                                if (j >>> tq) &&& 1 = 0 then p else 0.0)
                        Assert.True(prob0 > 0.95,
                            $"Temp qubit {i} (qubit #{tq}) should be |0>, but P(0) = {prob0:F6}")
                | _ -> Assert.Fail("Expected StateVector")

    [<Fact(Skip = "Long-running: use 3-bit equivalent above")>]
    let ``controlledMultiplyConstantModNInPlace: control=|0⟩ leaves state unchanged`` () =
        let controlQubit = 0
        let registerQubits = [1; 2; 3; 4]
        let tempQubits = [5; 6; 7; 8]
        let totalQubits = 13
        let bknd = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
        match bknd.InitializeState totalQubits with
        | Error err -> Assert.Fail($"Init failed: {err}")
        | Ok state0 ->
            let ops = [
                QuantumOperation.Gate (X registerQubits.[0])
                QuantumOperation.Gate (X registerQubits.[2])
            ]
            let preparedState =
                ops
                |> List.fold (fun st op ->
                    match bknd.ApplyOperation op st with
                    | Error err -> failwith $"Prep failed: {err}"
                    | Ok s -> s) state0
            match Arithmetic.controlledMultiplyConstantModNInPlace controlQubit registerQubits tempQubits 7 15 preparedState bknd with
            | Error err -> Assert.Fail($"controlledMultiplyConstantModNInPlace failed: {err}")
            | Ok result ->
                let regValue = readRegisterValue' registerQubits result.State
                Assert.Equal(5, regValue)  // Unchanged

    [<Fact>]
    let ``State-based addConstant works on non-prefix register`` () =
        // Regression: previously the QFT execution ignored registerQubits and always
        // targeted qubits [0..n-1]. This test ensures sub-register QFT works.

        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // Use a 2-qubit register located at qubits [1;2] (non-prefix).
        // Register is LSB-first, so value = b1 + 2*b2.
        let registerQubits = [ 1; 2 ]
        let constantToAdd = 1

        match backend.InitializeState 3 with
        | Error err -> Assert.Fail($"InitializeState failed: {err}")
        | Ok state0 ->
            // Prepare |x⟩ = |10⟩ on the register: qubit1=0, qubit2=1.
            // Global bits then are [q0=0; q1=0; q2=1] => basis index 4.
            let prepOp = QuantumOperation.Gate (X 2)

            match backend.ApplyOperation prepOp state0 with
            | Error err -> Assert.Fail($"State prep failed: {err}")
            | Ok stateWithX ->
                match Arithmetic.addConstant registerQubits constantToAdd stateWithX backend with
                | Error err -> Assert.Fail($"addConstant failed: {err}")
                | Ok result ->
                    // Expected: 2 + 1 = 3 mod 4 => |11⟩ on qubits [1;2].
                    // Global bits: [q0=0; q1=1; q2=1] => basis index 6.
                    let expectedBasisIndex = 6

                    match result.State with
                    | QuantumState.StateVector sv ->
                        let prob = Measurement.getBasisStateProbability expectedBasisIndex sv
                        let top = formatTopOutcomes sv 8

                        // Cross-check against the circuit-based implementation.
                        let (circuitIdx, circuitProb) =
                            runCircuitAddConstant
                                3
                                registerQubits
                                constantToAdd
                                (fun c -> c |> addGate (X 2))

                        let expectedRegisterValue = extractRegisterValue registerQubits expectedBasisIndex
                        let observedCircuitRegValue = extractRegisterValue registerQubits circuitIdx

                        // Deterministic assertion: should be a computational basis state.
                        Assert.True(
                            prob > 0.999999,
                            $"Expected basis index {expectedBasisIndex} (reg={expectedRegisterValue}) with prob≈1. Observed prob={prob:F6}. Top outcomes: {top}. Circuit reference: idx={circuitIdx}, reg={observedCircuitRegValue}, prob={circuitProb:F6}"
                        )

                    | otherStateType ->
                        Assert.Fail($"Expected StateVector simulation state, got: {QuantumState.stateType otherStateType}")
