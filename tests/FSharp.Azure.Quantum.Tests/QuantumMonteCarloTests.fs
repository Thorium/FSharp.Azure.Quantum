namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Algorithms.QuantumMonteCarlo
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Backends

module QuantumMonteCarloTests =

    let private createBackend () =
        LocalBackend.LocalBackend() :> IQuantumBackend

    let private createSimpleConfig numQubits iterations shots =
        let statePrep =
            [0 .. numQubits - 1]
            |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.H q))
                         (CircuitBuilder.empty numQubits)
        let oracle =
            CircuitBuilder.empty numQubits
            |> CircuitBuilder.addGate (CircuitBuilder.Z 0)
        {
            NumQubits = numQubits
            StatePreparation = statePrep
            Oracle = oracle
            GroverIterations = iterations
            Shots = shots
        }

    // ========================================================================
    // CORRECTNESS: amplitude estimation recovers a known marked probability
    // ========================================================================

    [<Fact>]
    let ``QAE recovers a known non-uniform marked amplitude`` () =
        // 2 qubits, 4 bins, non-uniform distribution p = [0.1; 0.2; 0.3; 0.4].
        // The oracle marks bins 0 and 1, so the true marked amplitude is a = p0 + p1 = 0.3.
        // This exercises the state-dependent diffusion (A S0 A†): the textbook uniform
        // diffusion would give the wrong answer for a non-uniform state preparation.
        let numQubits = 2
        let probs = [| 0.1; 0.2; 0.3; 0.4 |]
        let amps = probs |> Array.map (fun p -> System.Numerics.Complex(sqrt p, 0.0))
        let qubits = [| 0 .. numQubits - 1 |]
        let statePrep =
            FSharp.Azure.Quantum.Algorithms.MottonenStatePreparation.prepareStateFromAmplitudes
                amps qubits (CircuitBuilder.empty numQubits)
        // Phase oracle marking basis states 0 (|00>) and 1: flip the qubits that are 0 in
        // the target index, apply CZ to flip |11>, then undo the flips.
        let markState (c: CircuitBuilder.Circuit) (idx: int) =
            let flips = [0 .. numQubits - 1] |> List.filter (fun q -> (idx >>> q) &&& 1 = 0)
            let withFlips = flips |> List.fold (fun cc q -> cc |> CircuitBuilder.addGate (CircuitBuilder.X q)) c
            let withCZ = withFlips |> CircuitBuilder.addGate (CircuitBuilder.CZ(0, 1))
            flips |> List.fold (fun cc q -> cc |> CircuitBuilder.addGate (CircuitBuilder.X q)) withCZ
        let oracle = [0; 1] |> List.fold markState (CircuitBuilder.empty numQubits)
        let config =
            { NumQubits = numQubits
              StatePreparation = statePrep
              Oracle = oracle
              GroverIterations = 4
              Shots = 1000 }
        match estimateExpectation config (createBackend()) |> Async.RunSynchronously with
        | Ok qmc ->
            Assert.True(abs (qmc.ExpectationValue - 0.3) < 0.02,
                $"Expected marked amplitude a ≈ 0.3, got {qmc.ExpectationValue}")
        | Error e -> failwith $"QAE failed: {e}"

    // ========================================================================
    // VALIDATION
    // ========================================================================

    [<Fact>]
    let ``estimateExpectation rejects NumQubits < 1`` () =
        let config = { createSimpleConfig 1 1 100 with NumQubits = 0 }
        let qb = createBackend()
        let result = estimateExpectation config qb |> Async.RunSynchronously
        match result with
        | Error (QuantumError.ValidationError ("NumQubits", _)) -> ()
        | r -> failwith $"Expected ValidationError for NumQubits, got {r}"

    [<Fact>]
    let ``estimateExpectation rejects NumQubits > 20`` () =
        let config = { createSimpleConfig 1 1 100 with NumQubits = 21 }
        let qb = createBackend()
        let result = estimateExpectation config qb |> Async.RunSynchronously
        match result with
        | Error (QuantumError.ValidationError ("NumQubits", _)) -> ()
        | r -> failwith $"Expected ValidationError for NumQubits, got {r}"

    [<Fact>]
    let ``estimateExpectation rejects negative GroverIterations`` () =
        let config = { createSimpleConfig 2 1 100 with GroverIterations = -1 }
        let qb = createBackend()
        let result = estimateExpectation config qb |> Async.RunSynchronously
        match result with
        | Error (QuantumError.ValidationError ("GroverIterations", _)) -> ()
        | r -> failwith $"Expected ValidationError for GroverIterations, got {r}"

    [<Fact>]
    let ``estimateExpectation rejects Shots < 100`` () =
        let config = { createSimpleConfig 2 1 50 with Shots = 50 }
        let qb = createBackend()
        let result = estimateExpectation config qb |> Async.RunSynchronously
        match result with
        | Error (QuantumError.ValidationError ("Shots", _)) -> ()
        | r -> failwith $"Expected ValidationError for Shots, got {r}"

    [<Fact>]
    let ``estimateExpectation rejects state prep qubit count mismatch`` () =
        let config = { createSimpleConfig 3 1 100 with NumQubits = 2 }
        let qb = createBackend()
        let result = estimateExpectation config qb |> Async.RunSynchronously
        match result with
        | Error (QuantumError.ValidationError _) -> ()
        | r -> failwith $"Expected ValidationError for qubit count mismatch, got {r}"

    // ========================================================================
    // SUCCESSFUL EXECUTION
    // ========================================================================

    [<Fact>]
    let ``estimateExpectation returns QMCResult on success`` () =
        let config = createSimpleConfig 3 1 100
        let qb = createBackend()
        let result = estimateExpectation config qb |> Async.RunSynchronously
        match result with
        | Ok qmc ->
            Assert.True(qmc.ExpectationValue >= 0.0 && qmc.ExpectationValue <= 1.0,
                $"ExpectationValue {qmc.ExpectationValue} should be in [0,1]")
            Assert.True(qmc.StandardError > 0.0, "StandardError should be positive")
            Assert.True(qmc.SuccessProbability >= 0.0 && qmc.SuccessProbability <= 1.0,
                $"SuccessProbability {qmc.SuccessProbability} should be in [0,1]")
            Assert.True(qmc.QuantumQueries > 0, "QuantumQueries should be positive")
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``estimateExpectation with zero Grover iterations still works`` () =
        let config = createSimpleConfig 2 0 100
        let qb = createBackend()
        let result = estimateExpectation config qb |> Async.RunSynchronously
        match result with
        | Ok qmc ->
            Assert.True(qmc.ExpectationValue >= 0.0 && qmc.ExpectationValue <= 1.0)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================

    [<Fact>]
    let ``estimateProbability returns probability in valid range`` () =
        let numQubits = 3
        let statePrep =
            [0 .. numQubits - 1]
            |> List.fold (fun c q -> c |> CircuitBuilder.addGate (CircuitBuilder.H q))
                         (CircuitBuilder.empty numQubits)
        let oracle =
            CircuitBuilder.empty numQubits
            |> CircuitBuilder.addGate (CircuitBuilder.Z 0)
        let qb = createBackend()
        let result = estimateProbability statePrep oracle 1 qb |> Async.RunSynchronously
        match result with
        | Ok p ->
            Assert.True(p >= 0.0 && p <= 1.0, $"Probability {p} should be in [0,1]")
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``integrate returns a finite value`` () =
        let numQubits = 3
        let functionOracle =
            CircuitBuilder.empty numQubits
            |> CircuitBuilder.addGate (CircuitBuilder.Z 0)
        let qb = createBackend()
        let result = integrate functionOracle (0.0, 1.0) 1 qb |> Async.RunSynchronously
        match result with
        | Ok value ->
            Assert.True(System.Double.IsFinite(value), $"Integration result {value} should be finite")
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    // ========================================================================
    // QMC RESULT FIELDS
    // ========================================================================

    [<Fact>]
    let ``QMCResult has correct QuantumQueries calculation`` () =
        let config = createSimpleConfig 3 2 200
        let qb = createBackend()
        let result = estimateExpectation config qb |> Async.RunSynchronously
        match result with
        | Ok qmc ->
            // QuantumQueries = GroverIterations * Shots
            Assert.Equal(2 * 200, qmc.QuantumQueries)
        | Error e -> failwith $"Expected Ok, got Error: {e}"

    [<Fact>]
    let ``QMCResult has correct ClassicalEquivalent calculation`` () =
        let config = createSimpleConfig 3 3 100
        let qb = createBackend()
        let result = estimateExpectation config qb |> Async.RunSynchronously
        match result with
        | Ok qmc ->
            // ClassicalEquivalent = GroverIterations^2
            Assert.Equal(9, qmc.ClassicalEquivalent)
        | Error e -> failwith $"Expected Ok, got Error: {e}"
