namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.QuantumLinearSystemSolver

/// Admission of linear-system (HHL) problems against the backend's runnable qubits.
/// Only validation runs here: no circuit is simulated.
module QuantumLinearSystemSolverBuilderTests =

    /// 32×32 diagonal system with 16 eigenvalue qubits: 16 + 5 + 1 = 22 qubits.
    let private wideProblem (stub: BackendAbstraction.IQuantumBackend) =
        linearSystemSolver {
            diagonalMatrix [ for i in 1..32 -> float i ]
            vector [ for _ in 1..32 -> 1.0 ]
            eigenvalueQubits 16
            backend stub
        }

    [<Fact>]
    let ``A 22-qubit system is admitted when the backend declares no limit`` () =
        let stub = CapacityStubBackend("unlimited-stub", None)

        match wideProblem stub with
        | Ok problem ->
            Assert.Equal(32, problem.Matrix.Dimension)
            Assert.Equal(16, problem.EigenvalueQubits)
            Assert.Equal(0, stub.Requests)
        | Error e -> Assert.Fail($"the backend declares no limit, got {e.Message}")

    [<Fact>]
    let ``A system wider than the backend runs is refused naming the backend and its limit`` () =
        let stub = CapacityStubBackend("stub-20", Some 20)

        match wideProblem stub with
        | Ok _ -> Assert.Fail("22 qubits on a 20-qubit backend must be refused")
        | Error e ->
            Assert.Contains("needs 22 qubits", e.Message)
            Assert.Contains("'stub-20'", e.Message)
            Assert.Contains("runs at most 20", e.Message)
            Assert.Equal(0, stub.Requests)

    [<Fact>]
    let ``Solve refuses a system wider than the backend before running a circuit`` () =
        let stub = CapacityStubBackend("stub-4", Some 4)

        // 2 eigenvalue + 1 solution + 1 ancilla fits; the builder's own default of 4
        // eigenvalue qubits does not.
        let problem: LinearSystemProblem =
            match
                linearSystemSolver {
                    diagonalMatrix [ 2.0; 4.0 ]
                    vector [ 1.0; 1.0 ]
                    eigenvalueQubits 2
                    backend stub
                }
            with
            | Ok p -> p
            | Error e -> failwith e.Message

        match solve { problem with EigenvalueQubits = 4 } with
        | Ok _ -> Assert.Fail("6 qubits on a 4-qubit backend must be refused")
        | Error e ->
            Assert.Contains("runs at most 4", e.Message)
            Assert.Equal(0, stub.Requests)

    [<Fact>]
    let ``More qubits than an Int32 basis index holds are refused whatever the backend`` () =
        let stub = CapacityStubBackend("unlimited-stub", None)

        let result =
            linearSystemSolver {
                diagonalMatrix [ 2.0; 4.0 ]
                vector [ 1.0; 1.0 ]
                eigenvalueQubits 29
                backend stub
            }

        match result with
        | Ok _ -> Assert.Fail("29 + 1 + 1 qubits exceed the Int32 basis index")
        | Error e -> Assert.Contains($"at most {MaxIndexedQubits}", e.Message)
