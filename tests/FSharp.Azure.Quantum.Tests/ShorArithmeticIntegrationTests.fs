namespace FSharp.Azure.Quantum.Tests

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
                |> List.fold (fun st (i, q) ->
                    if (value >>> i) &&& 1 = 1 then
                        match bknd.ApplyOperation (QuantumOperation.Gate (X q)) st with
                        | Error err -> failwith $"State prep failed on qubit {q}: {err}"
                        | Ok s -> s
                    else
                        st) state0
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
        | other ->
            failwith $"Expected StateVector, got: {QuantumState.stateType other}"

    // ========================================================================
    // controlledModularMultiplication: control=|1⟩ tests
    // ========================================================================

    [<Fact>]
    let ``controlledModularMultiplication: 3*1 mod 5 = 3 (control=1)`` () =
        // N=5, a=3: 3*1 mod 5 = 3
        // n=3 bits, total = 2*3 + 5 = 11 qubits
        let controlQubit = 0
        let targetQubits = [1; 2; 3]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 1

        // Set control qubit to |1>
        let state' =
            match bknd.ApplyOperation (QuantumOperation.Gate (X controlQubit)) state with
            | Ok s -> s
            | Error err -> failwith $"Control prep failed: {err}"

        match Shor.controlledModularMultiplication controlQubit targetQubits 3 5 bknd state' with
        | Error err -> Assert.Fail($"controlledModularMultiplication failed: {err}")
        | Ok resultState ->
            let value = readRegisterValue targetQubits resultState
            Assert.Equal(3, value)

    [<Fact>]
    let ``controlledModularMultiplication: 3*2 mod 5 = 1 (control=1)`` () =
        let controlQubit = 0
        let targetQubits = [1; 2; 3]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 2

        let state' =
            match bknd.ApplyOperation (QuantumOperation.Gate (X controlQubit)) state with
            | Ok s -> s
            | Error err -> failwith $"Control prep failed: {err}"

        match Shor.controlledModularMultiplication controlQubit targetQubits 3 5 bknd state' with
        | Error err -> Assert.Fail($"controlledModularMultiplication failed: {err}")
        | Ok resultState ->
            let value = readRegisterValue targetQubits resultState
            Assert.Equal(1, value)

    [<Fact>]
    let ``controlledModularMultiplication: 3*4 mod 5 = 2 (control=1)`` () =
        let controlQubit = 0
        let targetQubits = [1; 2; 3]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 4

        let state' =
            match bknd.ApplyOperation (QuantumOperation.Gate (X controlQubit)) state with
            | Ok s -> s
            | Error err -> failwith $"Control prep failed: {err}"

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
        let targetQubits = [1; 2; 3]
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
        let targetQubits = [1; 2; 3]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 3

        let state' =
            match bknd.ApplyOperation (QuantumOperation.Gate (X controlQubit)) state with
            | Ok s -> s
            | Error err -> failwith $"Control prep failed: {err}"

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
        let targetQubits = [1; 2; 3]
        let totalQubits = 6  // Not enough
        let (bknd, state) = prepareState totalQubits targetQubits 1

        let state' =
            match bknd.ApplyOperation (QuantumOperation.Gate (X controlQubit)) state with
            | Ok s -> s
            | Error err -> failwith $"Control prep failed: {err}"

        match Shor.controlledModularMultiplication controlQubit targetQubits 3 5 bknd state' with
        | Error (QuantumError.ValidationError _) -> ()  // Expected
        | Error err -> Assert.Fail($"Expected ValidationError, got: {err}")
        | Ok _ -> Assert.Fail("Expected error for insufficient qubits")

    // ========================================================================
    // controlledModularExponentiation tests
    // ========================================================================

    [<Fact>]
    let ``controlledModularExponentiation: 2^1 * 1 mod 7 = 2 (control=1)`` () =
        // a=2, k=1, n=7: 2^1 = 2, then 2*1 mod 7 = 2
        let controlQubit = 0
        let targetQubits = [1; 2; 3]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 1

        let state' =
            match bknd.ApplyOperation (QuantumOperation.Gate (X controlQubit)) state with
            | Ok s -> s
            | Error err -> failwith $"Control prep failed: {err}"

        match Shor.controlledModularExponentiation controlQubit targetQubits 2 1 7 bknd state' with
        | Error err -> Assert.Fail($"controlledModularExponentiation failed: {err}")
        | Ok resultState ->
            let value = readRegisterValue targetQubits resultState
            Assert.Equal(2, value)

    [<Fact>]
    let ``controlledModularExponentiation: 2^2 * 1 mod 7 = 4 (control=1)`` () =
        // a=2, k=2, n=7: 2^2 = 4, then 4*1 mod 7 = 4
        let controlQubit = 0
        let targetQubits = [1; 2; 3]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 1

        let state' =
            match bknd.ApplyOperation (QuantumOperation.Gate (X controlQubit)) state with
            | Ok s -> s
            | Error err -> failwith $"Control prep failed: {err}"

        match Shor.controlledModularExponentiation controlQubit targetQubits 2 2 7 bknd state' with
        | Error err -> Assert.Fail($"controlledModularExponentiation failed: {err}")
        | Ok resultState ->
            let value = readRegisterValue targetQubits resultState
            Assert.Equal(4, value)

    [<Fact>]
    let ``controlledModularExponentiation: 2^3 * 3 mod 7 = 3 (control=1)`` () =
        // a=2, k=3, n=7: 2^3 mod 7 = 8 mod 7 = 1, then 1*3 mod 7 = 3
        let controlQubit = 0
        let targetQubits = [1; 2; 3]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 3

        let state' =
            match bknd.ApplyOperation (QuantumOperation.Gate (X controlQubit)) state with
            | Ok s -> s
            | Error err -> failwith $"Control prep failed: {err}"

        match Shor.controlledModularExponentiation controlQubit targetQubits 2 3 7 bknd state' with
        | Error err -> Assert.Fail($"controlledModularExponentiation failed: {err}")
        | Ok resultState ->
            let value = readRegisterValue targetQubits resultState
            Assert.Equal(3, value)

    [<Fact>]
    let ``controlledModularExponentiation: control=0 leaves state unchanged`` () =
        let controlQubit = 0
        let targetQubits = [1; 2; 3]
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
        let targetQubits = [1; 2; 3]
        let totalQubits = 11
        let (bknd, state) = prepareState totalQubits targetQubits 1

        let state' =
            match bknd.ApplyOperation (QuantumOperation.Gate (X controlQubit)) state with
            | Ok s -> s
            | Error err -> failwith $"Control prep failed: {err}"

        match Shor.controlledModularMultiplication controlQubit targetQubits 3 5 bknd state' with
        | Error (QuantumError.NotImplemented _) ->
            Assert.Fail("controlledModularMultiplication should no longer return NotImplemented")
        | Error err ->
            Assert.Fail($"Unexpected error: {err}")
        | Ok _ -> ()  // Success - the stub has been properly wired
