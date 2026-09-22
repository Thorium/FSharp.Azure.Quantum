namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.LocalSimulator

module BackendAbstractionTests =

    // ========================================================================
    // Local Backend Tests - State-Based Interface
    // ========================================================================

    [<Fact>]
    let ``LocalBackend should initialize quantum state`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        match backend.InitializeState 3 with
        | Ok state ->
            match state with
            | QuantumState.StateVector sv -> Assert.True(true, "State initialized successfully")
            | _ -> Assert.True(false, "Expected StateVector representation")
        | Error err -> Assert.True(false, $"InitializeState failed: %A{err}")

    [<Fact>]
    let ``LocalBackend should execute simple circuit`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let circuit = CircuitBuilder.empty 2
        let wrapper = CircuitWrapper(circuit) :> ICircuit

        match backend.ExecuteToState wrapper with
        | Ok state ->
            match state with
            | QuantumState.StateVector _ -> Assert.True(true, "Circuit executed successfully")
            | _ -> Assert.True(false, "Expected StateVector")
        | Error err -> Assert.True(false, $"ExecuteToState failed: %A{err}")

    type private XViaStateVectorExtension() =
        interface IApplyToStateVectorExtension with
            member _.Id = "tests.x-via-statevector"
            member _.ApplyToStateVector stateVector = Gates.applyX 0 stateVector

    type private XViaLoweringExtension() =
        interface ILowerToOperationsExtension with
            member _.Id = "tests.x-via-lowering"
            member _.LowerToGates() = [ CircuitBuilder.X 0 ]

    type private UnsupportedExtension() =
        interface IQuantumOperationExtension with
            member _.Id = "tests.unsupported"

    [<Fact>]
    let ``LocalBackend should support gate operations`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // Test various gate operations
        Assert.True(backend.SupportsOperation(QuantumOperation.Gate(CircuitBuilder.H 0)))
        Assert.True(backend.SupportsOperation(QuantumOperation.Gate(CircuitBuilder.X 0)))
        Assert.True(backend.SupportsOperation(QuantumOperation.Gate(CircuitBuilder.CNOT(0, 1))))
        Assert.True(backend.SupportsOperation(QuantumOperation.Gate(CircuitBuilder.RZ(0, 0.5))))

    [<Fact>]
    let ``LocalBackend should support StateVector extension operations`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let ext = XViaStateVectorExtension() :> IQuantumOperationExtension
        Assert.True(backend.SupportsOperation(QuantumOperation.Extension ext))

    [<Fact>]
    let ``LocalBackend should support lowering extension operations`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let ext = XViaLoweringExtension() :> IQuantumOperationExtension
        Assert.True(backend.SupportsOperation(QuantumOperation.Extension ext))

    [<Fact>]
    let ``LocalBackend should not support unknown extension operations`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let ext = UnsupportedExtension() :> IQuantumOperationExtension
        Assert.False(backend.SupportsOperation(QuantumOperation.Extension ext))

    [<Fact>]
    let ``LocalBackend should not support braiding operations`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // LocalBackend is gate-based, not topological
        Assert.False(backend.SupportsOperation(QuantumOperation.Braid 0))

    [<Fact>]
    let ``LocalBackend should have GateBased native state type`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        Assert.Equal(QuantumStateType.GateBased, backend.NativeStateType)

    [<Fact>]
    let ``LocalBackend should apply single gate operations`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        match backend.InitializeState 2 with
        | Ok initialState ->
            let hGate = QuantumOperation.Gate(CircuitBuilder.H 0)

            (backend.ApplyOperation hGate initialState)
            |> Result.map (fun _finalState -> Assert.True(true, "Gate operation applied successfully"))
            |> Result.defaultWith (fun err -> Assert.True(false, $"ApplyOperation failed: %A{err}"))
        | Error err -> Assert.True(false, $"InitializeState failed: %A{err}")

    [<Fact>]
    let ``LocalBackend should apply StateVector extension using fast-path`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        match backend.InitializeState 1 with
        | Error err -> Assert.True(false, $"InitializeState failed: %A{err}")
        | Ok initialState ->
            let ext = XViaStateVectorExtension() :> IQuantumOperationExtension
            let op = QuantumOperation.Extension ext

            match backend.ApplyOperation op initialState with
            | Error err -> Assert.True(false, $"ApplyOperation (extension) failed: %A{err}")
            | Ok(QuantumState.StateVector sv) ->
                // Starting from |0⟩, applying X yields |1⟩
                Assert.Equal(0.0, (StateVector.getAmplitude 0 sv).Real, 10)
                Assert.Equal(1.0, (StateVector.getAmplitude 1 sv).Real, 10)
            | Ok _ -> Assert.True(false, "Expected StateVector output")

    [<Fact>]
    let ``LocalBackend should apply lowering extension by executing lowered gates`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        match backend.InitializeState 1 with
        | Error err -> Assert.True(false, $"InitializeState failed: %A{err}")
        | Ok initialState ->
            let ext = XViaLoweringExtension() :> IQuantumOperationExtension
            let op = QuantumOperation.Extension ext

            match backend.ApplyOperation op initialState with
            | Error err -> Assert.True(false, $"ApplyOperation (extension) failed: %A{err}")
            | Ok(QuantumState.StateVector sv) ->
                // Starting from |0⟩, applying X yields |1⟩
                Assert.Equal(0.0, (StateVector.getAmplitude 0 sv).Real, 10)
                Assert.Equal(1.0, (StateVector.getAmplitude 1 sv).Real, 10)
            | Ok _ -> Assert.True(false, "Expected StateVector output")

    [<Fact>]
    let ``LocalBackend should execute circuit with multiple gates`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // Create Bell state circuit: H 0; CNOT 0 1
        let circuit =
            CircuitBuilder.empty 2
            |> CircuitBuilder.addGate (CircuitBuilder.H 0)
            |> CircuitBuilder.addGate (CircuitBuilder.CNOT(0, 1))

        let wrapper = CircuitWrapper(circuit) :> ICircuit

        match backend.ExecuteToState wrapper with
        | Ok state ->
            match state with
            | QuantumState.StateVector sv ->
                // Verify state is valid (implementation details checked elsewhere)
                Assert.True(true, "Bell state circuit executed")
            | _ -> Assert.True(false, "Expected StateVector")
        | Error err -> Assert.True(false, $"Circuit execution failed: %A{err}")

    [<Fact>]
    let ``LocalBackend should handle empty circuit`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let circuit = CircuitBuilder.empty 3
        let wrapper = CircuitWrapper(circuit) :> ICircuit

        match backend.ExecuteToState wrapper with
        | Ok state ->
            match state with
            | QuantumState.StateVector _ -> Assert.True(true, "Empty circuit returns |000⟩")
            | _ -> Assert.True(false, "Expected StateVector")
        | Error err -> Assert.True(false, $"Empty circuit failed: %A{err}")

    [<Fact>]
    let ``LocalBackend should support QPE intent operation`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let intent =
            {
                CountingQubits = 3
                TargetQubits = 1
                Unitary = QpeUnitary.TGate
                PrepareTargetOne = true
                ApplySwaps = false
            }

        Assert.True(backend.SupportsOperation(QuantumOperation.Algorithm(AlgorithmOperation.QPE intent)))

    [<Fact>]
    let ``LocalBackend should support HHL intent operation`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let intent =
            {
                EigenvalueQubits = 2
                SolutionQubits = 1
                DiagonalEigenvalues = [| 2.0; 3.0 |]
                InversionMethod = HhlEigenvalueInversionMethod.ExactRotation 1.0
                MinEigenvalue = 1e-6
            }

        Assert.True(backend.SupportsOperation(QuantumOperation.Algorithm(AlgorithmOperation.HHL intent)))

    [<Fact>]
    let ``LocalBackend should apply QPE intent and return StateVector`` () =
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let intent =
            {
                CountingQubits = 3
                TargetQubits = 1
                Unitary = QpeUnitary.TGate
                PrepareTargetOne = true
                ApplySwaps = false
            }

        match backend.InitializeState 4 with
        | Error err -> Assert.True(false, $"InitializeState failed: %A{err}")
        | Ok initialState ->
            match backend.ApplyOperation (QuantumOperation.Algorithm(AlgorithmOperation.QPE intent)) initialState with
            | Error err -> Assert.True(false, $"ApplyOperation (QPE intent) failed: %A{err}")
            | Ok(QuantumState.StateVector _) -> Assert.True(true)
            | Ok _ -> Assert.True(false, "Expected StateVector output")

    // ========================================================================
    // IonQ/Rigetti Backend Tests
    // ========================================================================
    // NOTE: IonQ and Rigetti backends are Azure Quantum cloud backends.
    // These require real workspace credentials and are tested via integration tests.
    // Unit tests for backend abstraction are covered by LocalBackend tests above.

    // ========================================================================
    // Legacy Backend Abstraction Tests
    // ========================================================================
    // NOTE: Legacy tests for validateCircuitForBackend and convertCircuitToProviderFormat
    // are commented out. These functions exist in Backends/Legacy/BackendAbstraction.fs
    // but are not exposed to the test project. They're tested via integration tests
    // with real Azure Quantum backends.
    //
    // If needed, these can be re-enabled by adding explicit module imports

    // ========================================================================
    // IN-PLACE EXECUTION DIFFERENTIAL TESTS
    // ========================================================================
    //
    // LocalBackend.executeCircuit overwrites the state as it folds, because nothing
    // else can see the intermediate states. That saves a 2^n allocation per gate but
    // means the in-place kernels are a SECOND implementation of every gate. These
    // tests pin whole circuits against the public, allocating gates — the ones the
    // rest of the library uses, and the ones GatesTests already checks against a
    // naive reference. If the two ever disagree, one of them is wrong.

    /// Fold the public (allocating, value-semantics) gates over a circuit.
    let private foldAllocatingGates (numQubits: int) (gates: CircuitBuilder.Gate list) =
        gates
        |> List.fold
            (fun state gate ->
                match gate with
                | CircuitBuilder.H q -> Gates.applyH q state
                | CircuitBuilder.X q -> Gates.applyX q state
                | CircuitBuilder.Y q -> Gates.applyY q state
                | CircuitBuilder.Z q -> Gates.applyZ q state
                | CircuitBuilder.S q -> Gates.applyS q state
                | CircuitBuilder.SDG q -> Gates.applySDG q state
                | CircuitBuilder.T q -> Gates.applyT q state
                | CircuitBuilder.TDG q -> Gates.applyTDG q state
                | CircuitBuilder.P(q, a) -> Gates.applyP q a state
                | CircuitBuilder.RX(q, a) -> Gates.applyRx q a state
                | CircuitBuilder.RY(q, a) -> Gates.applyRy q a state
                | CircuitBuilder.RZ(q, a) -> Gates.applyRz q a state
                | CircuitBuilder.CNOT(c, t) -> Gates.applyCNOT c t state
                | CircuitBuilder.CZ(c, t) -> Gates.applyCZ c t state
                | CircuitBuilder.CP(c, t, a) -> Gates.applyCP c t a state
                | CircuitBuilder.CRX(c, t, a) -> Gates.applyCRX c t a state
                | CircuitBuilder.CRY(c, t, a) -> Gates.applyCRY c t a state
                | CircuitBuilder.CRZ(c, t, a) -> Gates.applyCRZ c t a state
                | CircuitBuilder.CCX(c1, c2, t) -> Gates.applyCCX c1 c2 t state
                | CircuitBuilder.MCZ(cs, t) -> Gates.applyMultiControlledZ cs t state
                | CircuitBuilder.SWAP(a, b) -> Gates.applySWAP a b state
                | CircuitBuilder.U3(q, theta, phi, lambda) ->
                    state |> Gates.applyRz q lambda |> Gates.applyRy q theta |> Gates.applyRz q phi
                | other -> failwith $"test circuit should not contain {other}")
            (StateVector.init numQubits)

    /// Every gate the in-place dispatcher claims to handle, on 4 qubits.
    let private allInPlaceGates: CircuitBuilder.Gate list =
        [
            CircuitBuilder.H 0
            CircuitBuilder.X 1
            CircuitBuilder.Y 2
            CircuitBuilder.Z 3
            CircuitBuilder.S 0
            CircuitBuilder.SDG 1
            CircuitBuilder.T 2
            CircuitBuilder.TDG 3
            CircuitBuilder.P(0, 0.7)
            CircuitBuilder.RX(1, 1.3)
            CircuitBuilder.RY(2, -0.4)
            CircuitBuilder.RZ(3, 2.1)
            CircuitBuilder.U3(0, 0.9, 1.2, -0.3)
            CircuitBuilder.CNOT(0, 1)
            CircuitBuilder.CZ(1, 2)
            CircuitBuilder.CP(2, 3, 0.55)
            CircuitBuilder.CRX(0, 2, 1.1)
            CircuitBuilder.CRY(1, 3, -0.8)
            CircuitBuilder.CRZ(2, 0, 0.35)
            CircuitBuilder.CCX(0, 1, 2)
            CircuitBuilder.MCZ([ 0; 1; 2 ], 3)
            CircuitBuilder.SWAP(1, 3)
            CircuitBuilder.H 3
            CircuitBuilder.CNOT(3, 0)
        ]

    let private assertSameState (expected: StateVector.StateVector) (actual: QuantumState) (label: string) =
        match actual with
        | QuantumState.StateVector sv ->
            Assert.Equal(StateVector.dimension expected, StateVector.dimension sv)

            for i in 0 .. StateVector.dimension expected - 1 do
                let e = StateVector.getAmplitude i expected
                let a = StateVector.getAmplitude i sv

                Assert.True(System.Numerics.Complex.Abs(e - a) < 1e-12, $"{label}: amplitude {i} differs, {e} vs {a}")
        | other -> failwith $"{label}: expected a StateVector, got {QuantumState.stateType other}"

    let private buildCircuit (numQubits: int) (gates: CircuitBuilder.Gate list) =
        // CircuitBuilder stores gates most-recent-first, so build by folding addGate.
        gates
        |> List.fold (fun c g -> CircuitBuilder.addGate g c) (CircuitBuilder.empty numQubits)

    [<Fact>]
    let ``in-place circuit execution matches the allocating gates`` () =
        let numQubits = 4
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let circuit = buildCircuit numQubits allInPlaceGates

        match backend.ExecuteToState(CircuitWrapper(circuit) :> ICircuit) with
        | Error err -> failwith $"ExecuteToState failed: {err}"
        | Ok actual -> assertSameState (foldAllocatingGates numQubits allInPlaceGates) actual "all in-place gates"

    [<Fact>]
    let ``in-place execution matches the allocating gates for each gate alone`` () =
        // One gate per circuit, so a disagreement names the gate instead of pointing
        // at a 24-gate sequence.
        let numQubits = 4
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        // Put the register in a non-trivial state first: on |0…0⟩ a gate acting only
        // on the |1⟩ components would look exactly like the identity.
        let prefix =
            [
                CircuitBuilder.H 0
                CircuitBuilder.H 1
                CircuitBuilder.T 2
                CircuitBuilder.RY(3, 0.6)
            ]

        for gate in allInPlaceGates do
            let gates = prefix @ [ gate ]
            let circuit = buildCircuit numQubits gates

            match backend.ExecuteToState(CircuitWrapper(circuit) :> ICircuit) with
            | Error err -> failwith $"ExecuteToState failed for {gate}: {err}"
            | Ok actual -> assertSameState (foldAllocatingGates numQubits gates) actual $"{gate}"

    [<Fact>]
    let ``in-place execution still allocates for gates it cannot overwrite`` () =
        // RXX/RYY mix four indices at once and have no in-place kernel, so the fold
        // must fall back rather than skip them. A skipped gate would behave as the
        // identity, which this catches.
        let numQubits = 3
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let gates =
            [
                CircuitBuilder.H 0
                CircuitBuilder.RXX(0, 1, 0.9)
                CircuitBuilder.CNOT(1, 2)
                CircuitBuilder.RYY(1, 2, -0.5)
                CircuitBuilder.RZZ(0, 2, 1.4)
                CircuitBuilder.H 2
            ]

        let circuit = buildCircuit numQubits gates

        let expected =
            gates
            |> List.fold
                (fun state gate ->
                    match gate with
                    | CircuitBuilder.H q -> Gates.applyH q state
                    | CircuitBuilder.CNOT(c, t) -> Gates.applyCNOT c t state
                    | CircuitBuilder.RXX(a, b, angle) -> Gates.applyRxx a b angle state
                    | CircuitBuilder.RYY(a, b, angle) -> Gates.applyRyy a b angle state
                    | CircuitBuilder.RZZ(a, b, angle) -> Gates.applyRzz a b angle state
                    | other -> failwith $"unexpected {other}")
                (StateVector.init numQubits)

        match backend.ExecuteToState(CircuitWrapper(circuit) :> ICircuit) with
        | Error err -> failwith $"ExecuteToState failed: {err}"
        | Ok actual -> assertSameState expected actual "fallback gates"

    [<Fact>]
    let ``in-place execution still rejects out-of-range and degenerate gates`` () =
        // The in-place kernels address amplitudes by bit mask and do not range-check.
        // Left unguarded they would compute a wrong state for an invalid circuit
        // instead of failing — strictly worse than the optimisation is worth. The
        // dispatcher therefore refuses such gates so the allocating path raises.
        let numQubits = 3
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let invalidCircuits: (string * CircuitBuilder.Gate) list =
            [
                "qubit index past the register", CircuitBuilder.H 7
                "negative qubit index", CircuitBuilder.X -1
                "control past the register", CircuitBuilder.CNOT(0, 9)
                "control equal to target", CircuitBuilder.CNOT(1, 1)
                "toffoli with repeated control", CircuitBuilder.CCX(0, 0, 1)
                "toffoli control equal to target", CircuitBuilder.CCX(0, 1, 1)
                "swap of a qubit with itself", CircuitBuilder.SWAP(2, 2)
                "rotation past the register", CircuitBuilder.RZ(5, 0.4)
            ]

        for (label, gate) in invalidCircuits do
            let circuit =
                CircuitBuilder.empty numQubits
                |> CircuitBuilder.addGate (CircuitBuilder.H 0)
                |> CircuitBuilder.addGate gate

            match backend.ExecuteToState(CircuitWrapper(circuit) :> ICircuit) with
            | Error _ -> () // expected: surfaced as a structured error, not a wrong state
            | Ok _ -> failwith $"{label}: expected an error for {gate}, got a state"

    /// Apply one gate through the public, allocating API.
    let private applyAllocating (gate: CircuitBuilder.Gate) (state: StateVector.StateVector) =
        match gate with
        | CircuitBuilder.H q -> Gates.applyH q state
        | CircuitBuilder.X q -> Gates.applyX q state
        | CircuitBuilder.Y q -> Gates.applyY q state
        | CircuitBuilder.Z q -> Gates.applyZ q state
        | CircuitBuilder.S q -> Gates.applyS q state
        | CircuitBuilder.SDG q -> Gates.applySDG q state
        | CircuitBuilder.T q -> Gates.applyT q state
        | CircuitBuilder.TDG q -> Gates.applyTDG q state
        | CircuitBuilder.P(q, a) -> Gates.applyP q a state
        | CircuitBuilder.RX(q, a) -> Gates.applyRx q a state
        | CircuitBuilder.RY(q, a) -> Gates.applyRy q a state
        | CircuitBuilder.RZ(q, a) -> Gates.applyRz q a state
        | CircuitBuilder.U3(q, theta, phi, lambda) ->
            state |> Gates.applyRz q lambda |> Gates.applyRy q theta |> Gates.applyRz q phi
        | CircuitBuilder.CNOT(c, t) -> Gates.applyCNOT c t state
        | CircuitBuilder.CZ(c, t) -> Gates.applyCZ c t state
        | CircuitBuilder.CP(c, t, a) -> Gates.applyCP c t a state
        | CircuitBuilder.CRX(c, t, a) -> Gates.applyCRX c t a state
        | CircuitBuilder.CRY(c, t, a) -> Gates.applyCRY c t a state
        | CircuitBuilder.CRZ(c, t, a) -> Gates.applyCRZ c t a state
        | CircuitBuilder.CCX(a, b, c) -> Gates.applyCCX a b c state
        | CircuitBuilder.MCZ(cs, t) -> Gates.applyMultiControlledZ cs t state
        | CircuitBuilder.SWAP(a, b) -> Gates.applySWAP a b state
        | CircuitBuilder.RXX(a, b, angle) -> Gates.applyRxx a b angle state
        | CircuitBuilder.RYY(a, b, angle) -> Gates.applyRyy a b angle state
        | CircuitBuilder.RZZ(a, b, angle) -> Gates.applyRzz a b angle state
        | other -> failwith $"test circuit should not contain {other}"

    [<Fact>]
    let ``in-place execution matches the allocating gates on random circuits`` () =
        // The hand-written circuits above prove the kernels agree on the gates they
        // happen to contain, in the order someone chose. This sweeps random circuits
        // instead, mixing gates that have an in-place kernel with ones that do not so
        // the fallback boundary is crossed repeatedly within a single fold.
        //
        // It is also the guard behind every probabilistic test in this suite: when a
        // sampling test starts behaving differently after an execution change, the
        // question is whether the STATE changed, and this answers it directly.
        let rng = System.Random(20260925) // fixed seed: a failure must be reproducible
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend

        let randomGate (numQubits: int) =
            let angle () = rng.NextDouble() * 6.0 - 3.0

            // Distinct qubit indices, so no gate is degenerate.
            let distinct count =
                let rec pick acc =
                    if List.length acc = count then
                        acc
                    else
                        let candidate = rng.Next numQubits

                        if List.contains candidate acc then
                            pick acc
                        else
                            pick (candidate :: acc)

                pick []

            let single = rng.Next numQubits

            match rng.Next 20, distinct (min 3 numQubits) with
            | 0, _ -> CircuitBuilder.H single
            | 1, _ -> CircuitBuilder.X single
            | 2, _ -> CircuitBuilder.Y single
            | 3, _ -> CircuitBuilder.Z single
            | 4, _ -> CircuitBuilder.S single
            | 5, _ -> CircuitBuilder.SDG single
            | 6, _ -> CircuitBuilder.T single
            | 7, _ -> CircuitBuilder.TDG single
            | 8, _ -> CircuitBuilder.P(single, angle ())
            | 9, _ -> CircuitBuilder.RX(single, angle ())
            | 10, _ -> CircuitBuilder.RY(single, angle ())
            | 11, _ -> CircuitBuilder.RZ(single, angle ())
            | 12, _ -> CircuitBuilder.U3(single, angle (), angle (), angle ())
            | 13, a :: b :: _ -> CircuitBuilder.CNOT(a, b)
            | 14, a :: b :: _ -> CircuitBuilder.CZ(a, b)
            | 15, a :: b :: _ -> CircuitBuilder.CP(a, b, angle ())
            | 16, a :: b :: _ -> CircuitBuilder.CRZ(a, b, angle ())
            | 17, a :: b :: _ -> CircuitBuilder.SWAP(a, b)
            | 18, a :: b :: c :: _ -> CircuitBuilder.CCX(a, b, c)
            // RZZ has no in-place kernel, so every trial that draws it crosses the
            // fallback boundary mid-fold.
            | _, a :: b :: _ -> CircuitBuilder.RZZ(a, b, angle ())
            | _, _ -> CircuitBuilder.H single

        for trial in 1..40 do
            let numQubits = 2 + rng.Next 4 // 2..5 qubits
            let gates = List.init (5 + rng.Next 20) (fun _ -> randomGate numQubits)

            let circuit =
                gates
                |> List.fold (fun c g -> CircuitBuilder.addGate g c) (CircuitBuilder.empty numQubits)

            let expected =
                gates
                |> List.fold (fun state gate -> applyAllocating gate state) (StateVector.init numQubits)

            match backend.ExecuteToState(CircuitWrapper(circuit) :> ICircuit) with
            | Error err -> failwith $"trial {trial}: ExecuteToState failed: {err}"
            | Ok actual -> assertSameState expected actual $"trial {trial} ({numQubits} qubits, {gates.Length} gates)"
