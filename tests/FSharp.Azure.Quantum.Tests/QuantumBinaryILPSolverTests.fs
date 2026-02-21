module FSharp.Azure.Quantum.Tests.QuantumBinaryILPSolverTests

open Xunit
open FSharp.Azure.Quantum.Quantum.QuantumBinaryILPSolver
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends

/// Helper to create local backend for tests
let private createLocalBackend () : BackendAbstraction.IQuantumBackend =
    LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

// ============================================================================
// QUBO ENCODING TESTS
// ============================================================================

module QuboEncodingTests =

    [<Fact>]
    let ``toQubo produces correct size for single variable no constraints`` () =
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0 ]
            Constraints = []
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // 1 decision variable, 0 constraints → 1 qubit
            Assert.Equal(1, qubo.GetLength(0))
            Assert.Equal(1, qubo.GetLength(1))

    [<Fact>]
    let ``toQubo produces correct size with slack variables`` () =
        // min x0  subject to  2*x0 <= 3
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0 ]
            Constraints = [
                { Coefficients = [ 2.0 ]; Bound = 3.0 }
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // 1 decision var + ceil(log2(3+1)) = ceil(2) = 2 slack bits → 3 qubits
            Assert.Equal(3, qubo.GetLength(0))

    [<Fact>]
    let ``toQubo produces correct size for two variables two constraints`` () =
        // min x0 + x1  subject to  x0 + x1 <= 1, x0 <= 1
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0; 1.0 ]
            Constraints = [
                { Coefficients = [ 1.0; 1.0 ]; Bound = 1.0 }
                { Coefficients = [ 1.0; 0.0 ]; Bound = 1.0 }
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // 2 decision vars + ceil(log2(2)) + ceil(log2(2)) = 2 + 1 + 1 = 4
            Assert.Equal(4, qubo.GetLength(0))

    [<Fact>]
    let ``toQubo QUBO is symmetric`` () =
        let problem : Problem = {
            ObjectiveCoeffs = [ 2.0; -3.0 ]
            Constraints = [
                { Coefficients = [ 1.0; 2.0 ]; Bound = 3.0 }
                { Coefficients = [ 3.0; 1.0 ]; Bound = 4.0 }
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let n = qubo.GetLength(0)
            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    Assert.Equal(qubo.[i, j], qubo.[j, i], 6)

    [<Fact>]
    let ``toQubo encodes objective on diagonal`` () =
        // min 3*x0 - 2*x1 (no constraints)
        let problem : Problem = {
            ObjectiveCoeffs = [ 3.0; -2.0 ]
            Constraints = []
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // With no constraints, diagonal should be exactly the objective coefficients
            Assert.Equal(3.0, qubo.[0, 0], 6)
            Assert.Equal(-2.0, qubo.[1, 1], 6)
            // No off-diagonal terms
            Assert.Equal(0.0, qubo.[0, 1], 6)

    [<Fact>]
    let ``toQubo has non-zero penalty terms for constraints`` () =
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0; 1.0 ]
            Constraints = [
                { Coefficients = [ 1.0; 1.0 ]; Bound = 1.0 }
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // With constraint, there should be off-diagonal terms from penalty
            let totalNonZero =
                let mutable count = 0
                let sz = qubo.GetLength(0)
                for i in 0 .. sz - 1 do
                    for j in 0 .. sz - 1 do
                        if abs qubo.[i, j] > 1e-15 then count <- count + 1
                count
            Assert.True(totalNonZero > 2, $"QUBO should have penalty terms, got {totalNonZero} non-zero entries")

    [<Fact>]
    let ``toQubo optimal bitstring minimizes energy for simple problem`` () =
        // min -x0 subject to x0 <= 1
        // Optimal: x0 = 1, objective = -1
        let problem : Problem = {
            ObjectiveCoeffs = [ -1.0 ]
            Constraints = [
                { Coefficients = [ 1.0 ]; Bound = 1.0 }
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let n = qubo.GetLength(0)
            // Evaluate energy for all possible bitstrings
            let evaluateEnergy (bits: int[]) =
                let mutable energy = 0.0
                for i in 0 .. n - 1 do
                    for j in 0 .. n - 1 do
                        energy <- energy + qubo.[i, j] * float bits.[i] * float bits.[j]
                energy

            // Generate all 2^n bitstrings
            let allBitstrings =
                [ 0 .. (1 <<< n) - 1 ]
                |> List.map (fun k ->
                    Array.init n (fun i -> (k >>> i) &&& 1))

            let bestBits =
                allBitstrings
                |> List.minBy evaluateEnergy

            // The optimal x0 should be 1
            Assert.Equal(1, bestBits.[0])

// ============================================================================
// VALIDATION TESTS
// ============================================================================

module ValidationTests =

    [<Fact>]
    let ``toQubo rejects empty objective`` () =
        let problem : Problem = {
            ObjectiveCoeffs = []
            Constraints = []
        }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("objectiveCoeffs", field)
        | _ -> Assert.Fail("Should reject empty objective")

    [<Fact>]
    let ``toQubo rejects mismatched coefficient dimensions`` () =
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0; 2.0 ]
            Constraints = [
                { Coefficients = [ 1.0 ]; Bound = 5.0 }  // Only 1 coeff, but 2 variables
            ]
        }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("coefficients", field)
        | _ -> Assert.Fail("Should reject mismatched dimensions")

    [<Fact>]
    let ``toQubo rejects negative bound`` () =
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0 ]
            Constraints = [
                { Coefficients = [ 1.0 ]; Bound = -1.0 }
            ]
        }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("bound", field)
        | _ -> Assert.Fail("Should reject negative bound")

    [<Fact>]
    let ``toQubo accepts zero bound`` () =
        // x0 <= 0 is valid (forces x0 = 0)
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0 ]
            Constraints = [
                { Coefficients = [ 1.0 ]; Bound = 0.0 }
            ]
        }
        match toQubo problem with
        | Ok _ -> ()
        | Error err -> Assert.Fail($"Should accept zero bound, got: {err}")

    [<Fact>]
    let ``solveWithConfig rejects empty objective`` () =
        let backend = createLocalBackend ()
        let problem : Problem = {
            ObjectiveCoeffs = []
            Constraints = []
        }
        match solveWithConfig backend problem defaultConfig with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("objectiveCoeffs", field)
        | _ -> Assert.Fail("Should reject empty objective")

// ============================================================================
// QUBIT ESTIMATION TESTS
// ============================================================================

module QubitEstimationTests =

    [<Fact>]
    let ``estimateQubits with no constraints`` () =
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0; 2.0; 3.0 ]
            Constraints = []
        }
        // 3 decision vars, no slack → 3
        Assert.Equal(3, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits with single constraint`` () =
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0 ]
            Constraints = [
                { Coefficients = [ 1.0 ]; Bound = 7.0 }
            ]
        }
        // 1 decision var + ceil(log2(8)) = 1 + 3 = 4
        Assert.Equal(4, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits with multiple constraints`` () =
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0; 2.0 ]
            Constraints = [
                { Coefficients = [ 1.0; 1.0 ]; Bound = 3.0 }   // ceil(log2(4)) = 2
                { Coefficients = [ 1.0; 0.0 ]; Bound = 1.0 }   // ceil(log2(2)) = 1
            ]
        }
        // 2 decision vars + 2 + 1 = 5
        Assert.Equal(5, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits with large bound`` () =
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0 ]
            Constraints = [
                { Coefficients = [ 1.0 ]; Bound = 15.0 }
            ]
        }
        // 1 + ceil(log2(16)) = 1 + 4 = 5
        Assert.Equal(5, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits with zero bound`` () =
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0 ]
            Constraints = [
                { Coefficients = [ 1.0 ]; Bound = 0.0 }
            ]
        }
        // 1 + 0 slack bits = 1
        Assert.Equal(1, estimateQubits problem)

// ============================================================================
// isValid TESTS
// ============================================================================

module IsValidTests =

    [<Fact>]
    let ``isValid accepts feasible solution`` () =
        // min x0 subject to x0 <= 1
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0 ]
            Constraints = [
                { Coefficients = [ 1.0 ]; Bound = 1.0 }
            ]
        }
        // Total qubits: 1 + 1 = 2, bitstring [0; 0] means x0=0, slack z0=0
        // Constraint: 0 <= 1 ✓
        Assert.True(isValid problem [| 0; 0 |])

    [<Fact>]
    let ``isValid accepts tight constraint`` () =
        // min x0 subject to x0 <= 1
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0 ]
            Constraints = [
                { Coefficients = [ 1.0 ]; Bound = 1.0 }
            ]
        }
        // x0 = 1, slack z0 = 0: constraint 1 <= 1 ✓
        Assert.True(isValid problem [| 1; 0 |])

    [<Fact>]
    let ``isValid rejects violated constraint`` () =
        // min -x0 - x1 subject to x0 + x1 <= 1
        let problem : Problem = {
            ObjectiveCoeffs = [ -1.0; -1.0 ]
            Constraints = [
                { Coefficients = [ 1.0; 1.0 ]; Bound = 1.0 }
            ]
        }
        // Total qubits: 2 + 1 = 3
        // x0=1, x1=1, z0=0: constraint 2 <= 1 ✗
        Assert.False(isValid problem [| 1; 1; 0 |])

    [<Fact>]
    let ``isValid rejects wrong-length bitstring`` () =
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0 ]
            Constraints = [
                { Coefficients = [ 1.0 ]; Bound = 1.0 }
            ]
        }
        Assert.False(isValid problem [| 1 |])       // Too short
        Assert.False(isValid problem [| 1; 0; 0 |]) // Too long

    [<Fact>]
    let ``isValid with multiple constraints all satisfied`` () =
        // min x0 + x1 subject to x0 <= 1, x1 <= 1
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0; 1.0 ]
            Constraints = [
                { Coefficients = [ 1.0; 0.0 ]; Bound = 1.0 }
                { Coefficients = [ 0.0; 1.0 ]; Bound = 1.0 }
            ]
        }
        // Total: 2 + 1 + 1 = 4 qubits
        // x0=1, x1=0, z_c0=0, z_c1=0
        // Constraint 1: 1 <= 1 ✓, Constraint 2: 0 <= 1 ✓
        Assert.True(isValid problem [| 1; 0; 0; 0 |])

// ============================================================================
// DECOMPOSE / RECOMBINE TESTS
// ============================================================================

module DecomposeRecombineTests =

    [<Fact>]
    let ``decompose returns single problem`` () =
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0 ]
            Constraints = []
        }
        let parts = decompose problem
        Assert.Equal(1, parts.Length)

    [<Fact>]
    let ``recombine handles empty list`` () =
        let result = recombine []
        Assert.True(System.Double.IsPositiveInfinity(result.ObjectiveValue))
        Assert.False(result.IsValid)

    [<Fact>]
    let ``recombine returns single solution`` () =
        let sol : Solution = {
            Variables = [| 1; 0 |]
            ObjectiveValue = 1.0
            ConstraintsSatisfied = 1
            TotalConstraints = 1
            IsValid = true
            WasRepaired = false
            BackendName = "Test"
            NumShots = 100
            OptimizedParameters = None
            OptimizationConverged = None
        }
        let result = recombine [ sol ]
        Assert.Equal(1.0, result.ObjectiveValue)

    [<Fact>]
    let ``recombine picks best valid objective`` () =
        let sol1 : Solution = {
            Variables = [| 1; 1 |]
            ObjectiveValue = 5.0
            ConstraintsSatisfied = 1; TotalConstraints = 1
            IsValid = true; WasRepaired = false
            BackendName = ""; NumShots = 0
            OptimizedParameters = None; OptimizationConverged = None
        }
        let sol2 : Solution = {
            Variables = [| 1; 0 |]
            ObjectiveValue = 2.0
            ConstraintsSatisfied = 1; TotalConstraints = 1
            IsValid = true; WasRepaired = false
            BackendName = ""; NumShots = 0
            OptimizedParameters = None; OptimizationConverged = None
        }
        let result = recombine [ sol1; sol2 ]
        Assert.Equal(2.0, result.ObjectiveValue)

// ============================================================================
// QUANTUM SOLVER TESTS (using LocalBackend)
// ============================================================================

module QuantumSolverTests =

    [<Fact>]
    let ``solve returns Ok for unconstrained problem`` () =
        let backend = createLocalBackend ()
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0 ]
            Constraints = []
        }

        match solve backend problem 100 with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.Equal("Local Simulator", solution.BackendName)

    [<Fact>]
    let ``solve returns Ok for single-variable single-constraint`` () =
        let backend = createLocalBackend ()
        // min x0 subject to x0 <= 1
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0 ]
            Constraints = [
                { Coefficients = [ 1.0 ]; Bound = 1.0 }
            ]
        }

        match solve backend problem 100 with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.Equal("Local Simulator", solution.BackendName)
            Assert.Equal(1, solution.TotalConstraints)

    [<Fact>]
    let ``solve with constraint repair produces feasible solution`` () =
        let backend = createLocalBackend ()
        // min -x0 - x1 subject to x0 + x1 <= 1
        let problem : Problem = {
            ObjectiveCoeffs = [ -1.0; -1.0 ]
            Constraints = [
                { Coefficients = [ 1.0; 1.0 ]; Bound = 1.0 }
            ]
        }
        let config = { defaultConfig with EnableConstraintRepair = true }

        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solve with repair failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid, "Repaired solution should be feasible")
            Assert.Equal(1, solution.ConstraintsSatisfied)

    [<Fact>]
    let ``solveWithConfig uses config shots`` () =
        let backend = createLocalBackend ()
        let problem : Problem = {
            ObjectiveCoeffs = [ 1.0 ]
            Constraints = []
        }
        let config = { defaultConfig with FinalShots = 42 }

        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solveWithConfig failed: {err}")
        | Ok solution ->
            Assert.Equal(42, solution.NumShots)

    [<Fact>]
    let ``solve two variables with constraint`` () =
        let backend = createLocalBackend ()
        // Knapsack-like: min -3*x0 - 5*x1 subject to 2*x0 + 4*x1 <= 5
        let problem : Problem = {
            ObjectiveCoeffs = [ -3.0; -5.0 ]
            Constraints = [
                { Coefficients = [ 2.0; 4.0 ]; Bound = 5.0 }
            ]
        }
        let config = { defaultConfig with EnableConstraintRepair = true }

        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid, "Solution should be feasible")

    [<Fact>]
    let ``solve with multiple constraints`` () =
        let backend = createLocalBackend ()
        // min -x0 - x1 - x2 subject to x0 + x1 <= 1, x1 + x2 <= 1
        let problem : Problem = {
            ObjectiveCoeffs = [ -1.0; -1.0; -1.0 ]
            Constraints = [
                { Coefficients = [ 1.0; 1.0; 0.0 ]; Bound = 1.0 }
                { Coefficients = [ 0.0; 1.0; 1.0 ]; Bound = 1.0 }
            ]
        }
        let config = { defaultConfig with EnableConstraintRepair = true }

        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid, "Solution should be feasible")
            Assert.Equal(2, solution.TotalConstraints)
            Assert.Equal(2, solution.ConstraintsSatisfied)
