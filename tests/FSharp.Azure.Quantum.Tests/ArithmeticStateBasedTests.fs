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
            | Measure _ -> s) initialState

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
