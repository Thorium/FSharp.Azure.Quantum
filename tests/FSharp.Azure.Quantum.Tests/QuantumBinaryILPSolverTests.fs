module FSharp.Azure.Quantum.Tests.QuantumBinaryILPSolverTests

open Xunit
open FSharp.Azure.Quantum.Quantum.QuantumBinaryILPSolver
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends
open System.Threading
open System.Threading.Tasks

/// Helper to create local backend for tests
let private createLocalBackend () : BackendAbstraction.IQuantumBackend =
    LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

/// Solves with the default config and the given final shot count through the async API
/// (what the deprecated synchronous solve wrapper does).
let private solveDefaultAsync backend problem shots =
    solveWithConfigAsync
        backend
        problem
        { defaultConfig with
            FinalShots = shots
        }
        CancellationToken.None

// ============================================================================
// QUBO ENCODING TESTS
// ============================================================================

module QuboEncodingTests =

    [<Fact>]
    let ``toQubo produces correct size for single variable no constraints`` () =
        let problem: Problem =
            {
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
        // min x0 + x1 + x2  subject to  2*x0 + 3*x1 + 4*x2 <= 5
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0; 1.0; 1.0 ]
                Constraints =
                    [
                        {
                            Coefficients = [ 2.0; 3.0; 4.0 ]
                            Bound = 5.0
                        }
                    ]
            }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // 3 decision vars + slack weights [1; 2; 2] for the range 0..5 → 6 qubits
            Assert.Equal(6, qubo.GetLength(0))

    [<Fact>]
    let ``toQubo adds no slack for a constraint that always holds`` () =
        // min x0  subject to  2*x0 <= 3: the left side is at most 2
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0 ]
                Constraints = [ { Coefficients = [ 2.0 ]; Bound = 3.0 } ]
            }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            Assert.Equal(1, qubo.GetLength(0))
            Assert.Equal(1.0, qubo.[0, 0], 12)

    [<Fact>]
    let ``toQubo produces correct size for two variables two constraints`` () =
        // min x0 + x1  subject to  x0 + x1 <= 1, x0 <= 1
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0; 1.0 ]
                Constraints =
                    [
                        {
                            Coefficients = [ 1.0; 1.0 ]
                            Bound = 1.0
                        }
                        {
                            Coefficients = [ 1.0; 0.0 ]
                            Bound = 1.0
                        }
                    ]
            }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // 2 decision vars + 1 slack bit for x0 + x1 <= 1; x0 <= 1 always holds
            Assert.Equal(3, qubo.GetLength(0))

    [<Fact>]
    let ``toQubo QUBO is symmetric`` () =
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 2.0; -3.0 ]
                Constraints =
                    [
                        {
                            Coefficients = [ 1.0; 2.0 ]
                            Bound = 3.0
                        }
                        {
                            Coefficients = [ 3.0; 1.0 ]
                            Bound = 4.0
                        }
                    ]
            }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let n = qubo.GetLength 0

            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    Assert.Equal(qubo.[i, j], qubo.[j, i], 6)

    [<Fact>]
    let ``toQubo encodes objective on diagonal`` () =
        // min 3*x0 - 2*x1 (no constraints)
        let problem: Problem =
            {
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
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0; 1.0 ]
                Constraints =
                    [
                        {
                            Coefficients = [ 1.0; 1.0 ]
                            Bound = 1.0
                        }
                    ]
            }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // With constraint, there should be off-diagonal terms from penalty
            let totalNonZero =
                let mutable count = 0
                let sz = qubo.GetLength 0

                for i in 0 .. sz - 1 do
                    for j in 0 .. sz - 1 do
                        if abs qubo.[i, j] > 1e-15 then
                            count <- count + 1

                count

            Assert.True(totalNonZero > 2, $"QUBO should have penalty terms, got {totalNonZero} non-zero entries")

    [<Fact>]
    let ``toQubo optimal bitstring minimizes energy for simple problem`` () =
        // min -x0 subject to x0 <= 1
        // Optimal: x0 = 1, objective = -1
        let problem: Problem =
            {
                ObjectiveCoeffs = [ -1.0 ]
                Constraints = [ { Coefficients = [ 1.0 ]; Bound = 1.0 } ]
            }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let n = qubo.GetLength 0
            // Evaluate energy for all possible bitstrings
            let evaluateEnergy (bits: int[]) =
                let mutable energy = 0.0

                for i in 0 .. n - 1 do
                    for j in 0 .. n - 1 do
                        energy <- energy + qubo.[i, j] * float bits.[i] * float bits.[j]

                energy

            // Generate all 2^n bitstrings
            let allBitstrings =
                List.init (max 0 (1 <<< n)) (fun k -> Array.init n (fun i -> (k >>> i) &&& 1))

            let bestBits = allBitstrings |> List.minBy evaluateEnergy

            // The optimal x0 should be 1
            Assert.Equal(1, bestBits.[0])

// ============================================================================
// VALIDATION TESTS
// ============================================================================

module ValidationTests =

    [<Fact>]
    let ``toQubo rejects empty objective`` () =
        let problem: Problem =
            {
                ObjectiveCoeffs = []
                Constraints = []
            }

        match toQubo problem with
        | Error(QuantumError.ValidationError(field, _)) -> Assert.Equal("objectiveCoeffs", field)
        | _ -> Assert.Fail("Should reject empty objective")

    [<Fact>]
    let ``toQubo rejects mismatched coefficient dimensions`` () =
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0; 2.0 ]
                Constraints =
                    [
                        { Coefficients = [ 1.0 ]; Bound = 5.0 } // Only 1 coeff, but 2 variables
                    ]
            }

        match toQubo problem with
        | Error(QuantumError.ValidationError(field, _)) -> Assert.Equal("coefficients", field)
        | _ -> Assert.Fail("Should reject mismatched dimensions")

    [<Fact>]
    let ``toQubo rejects a bound below the smallest possible left side`` () =
        // x0 <= -1 has no solution; -x0 - x1 <= -3 neither (the left side is at least -2)
        for coefficients, bound in [ [ 1.0 ], -1.0; [ -1.0; -1.0 ], -3.0 ] do
            let problem: Problem =
                {
                    ObjectiveCoeffs = coefficients |> List.map (fun _ -> 1.0)
                    Constraints =
                        [
                            {
                                Coefficients = coefficients
                                Bound = bound
                            }
                        ]
                }

            match toQubo problem with
            | Error(QuantumError.ValidationError(field, message)) ->
                Assert.Equal("bound", field)
                Assert.Contains("Constraint 0", message)
            | _ -> Assert.Fail("Should reject an unsatisfiable constraint")

    [<Fact>]
    let ``toQubo accepts a negative bound that negative coefficients can meet`` () =
        // -x0 - x1 <= -1 means x0 + x1 >= 1
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0; 1.0 ]
                Constraints =
                    [
                        {
                            Coefficients = [ -1.0; -1.0 ]
                            Bound = -1.0
                        }
                    ]
            }

        match toQubo problem with
        | Error err -> Assert.Fail($"Should accept a satisfiable negative bound, got: {err}")
        | Ok qubo ->
            // slack range = bound - smallest left side = -1 - (-2) = 1 → 1 slack bit
            Assert.Equal(3, qubo.GetLength 0)

    [<Fact>]
    let ``toQubo rejects coefficients without a common integer scale and names the constraint`` () =
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0; 1.0 ]
                Constraints =
                    [
                        {
                            Coefficients = [ 1.0; 1.0 ]
                            Bound = 1.0
                        }
                        {
                            Coefficients = [ 1.0 / 3.0; 1.0 ]
                            Bound = 1.0
                        }
                    ]
            }

        match toQubo problem with
        | Error(QuantumError.ValidationError(field, message)) ->
            Assert.Equal("coefficients", field)
            Assert.Contains("Constraint 1", message)
        | _ -> Assert.Fail("Should reject coefficients that cannot be rescaled to integers")

    [<Fact>]
    let ``toQubo accepts zero bound`` () =
        // x0 <= 0 is valid (forces x0 = 0)
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0 ]
                Constraints = [ { Coefficients = [ 1.0 ]; Bound = 0.0 } ]
            }

        (toQubo problem)
        |> Result.map (fun _ -> ())
        |> Result.defaultWith (fun err -> Assert.Fail($"Should accept zero bound, got: {err}"))

    [<Fact>]
    let ``solveWithConfig rejects empty objective`` () : Task =
        task {
            let backend = createLocalBackend ()

            let problem: Problem =
                {
                    ObjectiveCoeffs = []
                    Constraints = []
                }

            match! solveWithConfigAsync backend problem defaultConfig CancellationToken.None with
            | Error(QuantumError.ValidationError(field, _)) -> Assert.Equal("objectiveCoeffs", field)
            | _ -> Assert.Fail("Should reject empty objective")
        }

// ============================================================================
// QUBIT ESTIMATION TESTS
// ============================================================================

module QubitEstimationTests =

    [<Fact>]
    let ``estimateQubits with no constraints`` () =
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0; 2.0; 3.0 ]
                Constraints = []
            }
        // 3 decision vars, no slack → 3
        Assert.Equal(3, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits with single constraint`` () =
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0; 1.0; 1.0; 1.0 ]
                Constraints =
                    [
                        {
                            Coefficients = [ 3.0; 4.0; 5.0; 6.0 ]
                            Bound = 7.0
                        }
                    ]
            }
        // 4 decision vars + slack weights [1; 2; 4] for the range 0..7 = 7
        Assert.Equal(7, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits with multiple constraints`` () =
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0; 2.0; 3.0 ]
                Constraints =
                    [
                        {
                            Coefficients = [ 2.0; 2.0; 2.0 ]
                            Bound = 3.0
                        } // integer form x0 + x1 + x2 <= 1: 1 slack bit
                        {
                            Coefficients = [ 1.0; 0.0; 0.0 ]
                            Bound = 1.0
                        } // always holds: no slack
                        {
                            Coefficients = [ 1.0; -1.0; -1.0 ]
                            Bound = 0.0
                        } // range 0 - (-2) = 2: slack weights [1; 1]
                    ]
            }
        // 3 decision vars + 1 + 0 + 2 = 6
        Assert.Equal(6, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits with large bound`` () =
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0; 1.0 ]
                Constraints =
                    [
                        {
                            Coefficients = [ 10.0; 9.0 ]
                            Bound = 15.0
                        }
                    ]
            }
        // 2 + slack weights [1; 2; 4; 8] for the range 0..15 = 6
        Assert.Equal(6, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits with zero bound`` () =
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0 ]
                Constraints = [ { Coefficients = [ 1.0 ]; Bound = 0.0 } ]
            }
        // 1 + 0 slack bits = 1
        Assert.Equal(1, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits equals the QUBO size`` () =
        let problems: Problem list =
            [
                {
                    ObjectiveCoeffs = [ 1.0; -5.0; -5.0 ]
                    Constraints =
                        [
                            {
                                Coefficients = [ 1.0; -1.0; -1.0 ]
                                Bound = 0.0
                            }
                        ]
                }
                {
                    ObjectiveCoeffs = [ -1.0; -1.0; -1.0 ]
                    Constraints =
                        [
                            {
                                Coefficients = [ 0.6; 0.6; 0.6 ]
                                Bound = 1.0
                            }
                        ]
                }
                {
                    ObjectiveCoeffs = [ -1.0; -1.0 ]
                    Constraints =
                        [
                            {
                                Coefficients = [ 1.0; 1.0 ]
                                Bound = 1.5
                            }
                        ]
                }
            ]

        // x0 - x1 - x2 <= 0: range 2 → 2 slack bits; 0.6·Σx <= 1 is Σx <= 1 and
        // x0 + x1 <= 1.5 is x0 + x1 <= 1: 1 slack bit each.
        Assert.Equal<int list>([ 5; 4; 3 ], problems |> List.map estimateQubits)

        for problem in problems do
            match toQubo problem with
            | Error err -> Assert.Fail($"toQubo failed: {err}")
            | Ok qubo -> Assert.Equal(estimateQubits problem, qubo.GetLength 0)

// ============================================================================
// isValid TESTS
// ============================================================================

module IsValidTests =

    /// min x0 + x1 subject to x0 + x1 <= 1: 2 decision bits and 1 slack bit
    let private atMostOne: Problem =
        {
            ObjectiveCoeffs = [ 1.0; 1.0 ]
            Constraints =
                [
                    {
                        Coefficients = [ 1.0; 1.0 ]
                        Bound = 1.0
                    }
                ]
        }

    [<Fact>]
    let ``isValid accepts feasible solution`` () =
        // x0 = 0, x1 = 0, slack 1: constraint 0 <= 1 ✓
        Assert.True(isValid atMostOne [| 0; 0; 1 |])

    [<Fact>]
    let ``isValid accepts tight constraint`` () =
        // x0 = 1, x1 = 0, slack 0: constraint 1 <= 1 ✓
        Assert.True(isValid atMostOne [| 1; 0; 0 |])

    [<Fact>]
    let ``isValid judges the decision bits only`` () =
        // A feasible assignment stays valid whatever the slack bit says.
        Assert.True(isValid atMostOne [| 1; 0; 1 |])

    [<Fact>]
    let ``isValid rejects violated constraint`` () =
        // x0 = 1, x1 = 1: constraint 2 <= 1 ✗
        Assert.False(isValid atMostOne [| 1; 1; 0 |])

    [<Fact>]
    let ``isValid rejects wrong-length bitstring`` () =
        Assert.False(isValid atMostOne [| 1; 0 |]) // Too short
        Assert.False(isValid atMostOne [| 1; 0; 0; 0 |]) // Too long

    [<Fact>]
    let ``isValid with a negative coefficient`` () =
        // x0 - x1 <= 0 (x0 implies x1): 2 decision bits, range 0 - (-1) = 1 → 1 slack bit
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0; 1.0 ]
                Constraints =
                    [
                        {
                            Coefficients = [ 1.0; -1.0 ]
                            Bound = 0.0
                        }
                    ]
            }

        Assert.True(isValid problem [| 0; 1; 1 |])
        Assert.True(isValid problem [| 1; 1; 0 |])
        Assert.False(isValid problem [| 1; 0; 0 |])

    [<Fact>]
    let ``isValid with multiple constraints all satisfied`` () =
        // min x0 + x1 + x2 subject to x0 + x1 <= 1, x1 + x2 <= 1
        let problem: Problem =
            {
                ObjectiveCoeffs = [ 1.0; 1.0; 1.0 ]
                Constraints =
                    [
                        {
                            Coefficients = [ 1.0; 1.0; 0.0 ]
                            Bound = 1.0
                        }
                        {
                            Coefficients = [ 0.0; 1.0; 1.0 ]
                            Bound = 1.0
                        }
                    ]
            }
        // Total: 3 + 1 + 1 = 5 qubits
        Assert.True(isValid problem [| 1; 0; 1; 0; 0 |])
        Assert.False(isValid problem [| 0; 1; 1; 0; 0 |])

// ============================================================================
// DECOMPOSE / RECOMBINE TESTS
// ============================================================================

module DecomposeRecombineTests =

    [<Fact>]
    let ``decompose returns single problem`` () =
        let problem: Problem =
            {
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
        let sol: Solution =
            {
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
                Sampling = None
                Split = None
            }

        let result = recombine [ sol ]
        Assert.Equal(1.0, result.ObjectiveValue)

    [<Fact>]
    let ``recombine picks best valid objective`` () =
        let sol1: Solution =
            {
                Variables = [| 1; 1 |]
                ObjectiveValue = 5.0
                ConstraintsSatisfied = 1
                TotalConstraints = 1
                IsValid = true
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
                Sampling = None
                Split = None
            }

        let sol2: Solution =
            {
                Variables = [| 1; 0 |]
                ObjectiveValue = 2.0
                ConstraintsSatisfied = 1
                TotalConstraints = 1
                IsValid = true
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
                Sampling = None
                Split = None
            }

        let result = recombine [ sol1; sol2 ]
        Assert.Equal(2.0, result.ObjectiveValue)

// ============================================================================
// QUANTUM SOLVER TESTS (using LocalBackend)
// ============================================================================

module QuantumSolverTests =

    [<Fact>]
    let ``solve returns Ok for unconstrained problem`` () : Task =
        task {
            let backend = createLocalBackend ()

            let problem: Problem =
                {
                    ObjectiveCoeffs = [ 1.0 ]
                    Constraints = []
                }

            let! result = solveDefaultAsync backend problem 100

            result
            |> Result.map (fun solution -> Assert.Equal("Local Simulator", solution.BackendName))
            |> Result.defaultWith (fun err -> Assert.Fail($"solve failed: {err}"))
        }

    [<Fact; Trait("Category", "Slow")>]
    let ``solve returns Ok for single-variable single-constraint`` () : Task =
        task {
            let backend = createLocalBackend ()
            // min x0 subject to x0 <= 1
            let problem: Problem =
                {
                    ObjectiveCoeffs = [ 1.0 ]
                    Constraints = [ { Coefficients = [ 1.0 ]; Bound = 1.0 } ]
                }

            match! solveDefaultAsync backend problem 100 with
            | Error err -> Assert.Fail($"solve failed: {err}")
            | Ok solution ->
                Assert.Equal("Local Simulator", solution.BackendName)
                Assert.Equal(1, solution.TotalConstraints)
        }

    [<Fact>]
    let ``solve with constraint repair produces feasible solution`` () : Task =
        task {
            let backend = createLocalBackend ()
            // min -x0 - x1 subject to x0 + x1 <= 1
            let problem: Problem =
                {
                    ObjectiveCoeffs = [ -1.0; -1.0 ]
                    Constraints =
                        [
                            {
                                Coefficients = [ 1.0; 1.0 ]
                                Bound = 1.0
                            }
                        ]
                }

            let config =
                { defaultConfig with
                    EnableConstraintRepair = true
                }

            match! solveWithConfigAsync backend problem config CancellationToken.None with
            | Error err -> Assert.Fail($"solve with repair failed: {err}")
            | Ok solution ->
                Assert.True(solution.IsValid, "Repaired solution should be feasible")
                Assert.Equal(1, solution.ConstraintsSatisfied)
        }

    [<Fact>]
    let ``solveWithConfig uses config shots`` () : Task =
        task {
            let backend = createLocalBackend ()

            let problem: Problem =
                {
                    ObjectiveCoeffs = [ 1.0 ]
                    Constraints = []
                }

            let config = { defaultConfig with FinalShots = 42 }

            let! result = solveWithConfigAsync backend problem config CancellationToken.None

            result
            |> Result.map (fun solution -> Assert.Equal(42, solution.NumShots))
            |> Result.defaultWith (fun err -> Assert.Fail($"solveWithConfig failed: {err}"))
        }

    [<Fact>]
    let ``solve two variables with constraint`` () : Task =
        task {
            let backend = createLocalBackend ()
            // Knapsack-like: min -3*x0 - 5*x1 subject to 2*x0 + 4*x1 <= 5
            let problem: Problem =
                {
                    ObjectiveCoeffs = [ -3.0; -5.0 ]
                    Constraints =
                        [
                            {
                                Coefficients = [ 2.0; 4.0 ]
                                Bound = 5.0
                            }
                        ]
                }

            let config =
                { defaultConfig with
                    EnableConstraintRepair = true
                }

            let! result = solveWithConfigAsync backend problem config CancellationToken.None

            result
            |> Result.map (fun solution -> Assert.True(solution.IsValid, "Solution should be feasible"))
            |> Result.defaultWith (fun err -> Assert.Fail($"solve failed: {err}"))
        }

    [<Fact>]
    let ``solve with multiple constraints`` () : Task =
        task {
            let backend = createLocalBackend ()
            // min -x0 - x1 - x2 subject to x0 + x1 <= 1, x1 + x2 <= 1
            let problem: Problem =
                {
                    ObjectiveCoeffs = [ -1.0; -1.0; -1.0 ]
                    Constraints =
                        [
                            {
                                Coefficients = [ 1.0; 1.0; 0.0 ]
                                Bound = 1.0
                            }
                            {
                                Coefficients = [ 0.0; 1.0; 1.0 ]
                                Bound = 1.0
                            }
                        ]
                }

            let config =
                { defaultConfig with
                    EnableConstraintRepair = true
                }

            match! solveWithConfigAsync backend problem config CancellationToken.None with
            | Error err -> Assert.Fail($"solve failed: {err}")
            | Ok solution ->
                Assert.True(solution.IsValid, "Solution should be feasible")
                Assert.Equal(2, solution.TotalConstraints)
                Assert.Equal(2, solution.ConstraintsSatisfied)
        }

// ============================================================================
// GROUND STATES AND REPAIR (brute force over every bitstring)
// ============================================================================

module GroundStateTests =

    let private bitsOf (n: int) (index: int) =
        Array.init n (fun q -> (index >>> q) &&& 1)

    let private problemOf (objective: float list) (constraints: (float list * float) list) : Problem =
        {
            ObjectiveCoeffs = objective
            Constraints = constraints |> List.map (fun (a, b) -> { Coefficients = a; Bound = b })
        }

    let private leftSide (constr: Constraint) (x: int[]) =
        constr.Coefficients |> List.mapi (fun i a -> a * float x.[i]) |> List.sum

    let private feasible (problem: Problem) (x: int[]) =
        problem.Constraints
        |> List.forall (fun constr -> leftSide constr x <= constr.Bound + 1e-9)

    let private objective (problem: Problem) (x: int[]) =
        problem.ObjectiveCoeffs |> List.mapi (fun i c -> c * float x.[i]) |> List.sum

    /// The feasible assignments with the lowest objective, by enumeration.
    let private classicalOptima (problem: Problem) : int[] list =
        let n = problem.ObjectiveCoeffs.Length

        let candidates = List.init (1 <<< n) (bitsOf n) |> List.filter (feasible problem)

        match candidates with
        | [] -> []
        | _ ->
            let best = candidates |> List.map (objective problem) |> List.min
            candidates |> List.filter (fun x -> objective problem x <= best + 1e-9)

    let private energy (qubo: float[,]) (bits: int[]) =
        let n = qubo.GetLength 0
        let mutable sum = 0.0

        for i in 0 .. n - 1 do
            if bits.[i] = 1 then
                for j in 0 .. n - 1 do
                    if bits.[j] = 1 then
                        sum <- sum + qubo.[i, j]

        sum

    /// Every bitstring of the QUBO with its energy.
    let private spectrum (problem: Problem) : (int[] * float)[] =
        match toQubo problem with
        | Error err -> failwith $"toQubo failed: {err}"
        | Ok qubo ->
            let total = qubo.GetLength 0
            Assert.True(total <= 16, $"{total} qubits")
            Array.init (1 <<< total) (fun index -> let bits = bitsOf total index in bits, energy qubo bits)

    /// The decision parts of the minimum-energy bitstrings are exactly the classical optima,
    /// and every infeasible bitstring lies above the minimum by at least max |c|.
    let private assertGroundStatesAreOptima (label: string) (problem: Problem) =
        let n = problem.ObjectiveCoeffs.Length
        let states = spectrum problem
        let minEnergy = states |> Array.map snd |> Array.min
        let tolerance = 1e-9 * max 1.0 (abs minEnergy)

        let ground =
            states
            |> Array.filter (fun (_, e) -> e <= minEnergy + tolerance)
            |> Array.map (fun (bits, _) -> bits)

        for bits in ground do
            Assert.True(isValid problem bits, $"%s{label}: ground state %A{bits} is infeasible")

        let groundDecisions =
            ground
            |> Array.map (fun bits -> bits.[0 .. n - 1])
            |> Array.distinct
            |> Array.sort

        let optima = classicalOptima problem |> List.toArray |> Array.sort
        Assert.Equal<int[][]>(optima, groundDecisions)

        let margin = problem.ObjectiveCoeffs |> List.map abs |> List.max

        for (bits, e) in states do
            if not (isValid problem bits) then
                Assert.True(
                    e >= minEnergy + margin - tolerance,
                    $"%s{label}: infeasible %A{bits} at {e}, minimum {minEnergy}, margin {margin}"
                )

    [<Fact>]
    let ``negative coefficient: the minimum is the constrained optimum`` () =
        // min x0 - 5 x1 - 5 x2  subject to  x0 - x1 - x2 <= 0: optimum (0, 1, 1) with -10
        let problem = problemOf [ 1.0; -5.0; -5.0 ] [ [ 1.0; -1.0; -1.0 ], 0.0 ]
        Assert.Equal<int[] list>([ [| 0; 1; 1 |] ], classicalOptima problem)
        assertGroundStatesAreOptima "negative coefficient" problem

    [<Fact>]
    let ``non-integer bounds and coefficients: the minimum is the constrained optimum`` () =
        assertGroundStatesAreOptima "bound 1.5" (problemOf [ -1.0; -1.0 ] [ [ 1.0; 1.0 ], 1.5 ])
        assertGroundStatesAreOptima "coefficients 0.6" (problemOf [ -1.0; -1.0; -1.0 ] [ [ 0.6; 0.6; 0.6 ], 1.0 ])

        assertGroundStatesAreOptima
            "weights 1.2 / 2.3 / 3.1"
            (problemOf [ -5.0; -6.0; -9.0 ] [ [ 1.2; 2.3; 3.1 ], 5.0 ])

    [<Fact>]
    let ``negative bound: the minimum is the constrained optimum`` () =
        // -x0 - x1 + x2 <= -1 and x0 + x1 >= 1 written as -x0 - x1 <= -1
        assertGroundStatesAreOptima "negative bound" (problemOf [ 1.0; 2.0; -1.0 ] [ [ -1.0; -1.0; 1.0 ], -1.0 ])

        assertGroundStatesAreOptima
            "cover"
            (problemOf [ 3.0; 2.0; 4.0 ] [ [ -1.0; -1.0; 0.0 ], -1.0; [ 0.0; -1.0; -1.0 ], -1.0 ])

    [<Fact>]
    let ``objective near the penalty scale: the minimum is the constrained optimum`` () =
        assertGroundStatesAreOptima "one dominant variable" (problemOf [ -10.0; -1.0; -1.0 ] [ [ 1.0; 1.0; 1.0 ], 2.0 ])

        // One unit of violation would gain almost the whole objective range.
        assertGroundStatesAreOptima
            "large mixed objective"
            (problemOf
                [ -1000.5; -999.25; 0.125; 400.0 ]
                [ [ 1.0; 1.0; 0.0; 0.0 ], 1.0; [ 1.0; 0.0; -1.0; -1.0 ], -1.0 ])

        assertGroundStatesAreOptima "small objective" (problemOf [ -0.003; -0.002; 0.001 ] [ [ 2.0; 3.0; -1.0 ], 2.0 ])

    [<Fact>]
    let ``seeded random instances: the minimum is the constrained optimum`` () =
        let rng = System.Random(20261001)
        let halves = [| -1.0; -0.5; 0.0; 0.5; 1.0 |]
        let mutable optimised = 0
        let mutable unsatisfiable = 0

        for instance in 1..60 do
            let n = 2 + rng.Next 3

            let objectiveCoeffs = List.init n (fun _ -> float (rng.Next 21 - 10) / 2.0)

            let constraints =
                List.init (1 + rng.Next 2) (fun _ ->
                    List.init n (fun _ -> halves.[rng.Next halves.Length]), float (rng.Next 8 - 3) / 2.0)

            let problem = problemOf objectiveCoeffs constraints

            if objectiveCoeffs |> List.exists (fun c -> c <> 0.0) then
                match toQubo problem with
                | Error(QuantumError.ValidationError("bound", _)) ->
                    // Rejected only when one constraint has no solution by itself.
                    let all = List.init (1 <<< n) (bitsOf n)

                    Assert.True(
                        problem.Constraints
                        |> List.exists (fun constr ->
                            all |> List.forall (fun x -> leftSide constr x > constr.Bound + 1e-9)),
                        $"instance {instance}: rejected although every constraint has a solution"
                    )

                    unsatisfiable <- unsatisfiable + 1
                | Error err -> Assert.Fail($"instance {instance}: {err}")
                | Ok _ ->
                    // Constraints that exclude each other leave nothing to compare with.
                    if not (classicalOptima problem).IsEmpty then
                        assertGroundStatesAreOptima $"instance {instance}" problem
                        optimised <- optimised + 1

        Assert.True(optimised >= 30, $"{optimised} instances compared")
        Assert.True(unsatisfiable >= 1, $"{unsatisfiable} unsatisfiable instances")

    [<Fact>]
    let ``repair reaches a feasible assignment from every bitstring with negative coefficients`` () =
        let problems =
            [
                problemOf [ 1.0; -5.0; -5.0 ] [ [ 1.0; -1.0; -1.0 ], 0.0 ]
                // x0 implies x1 implies x2
                problemOf [ -3.0; 1.0; 1.0 ] [ [ 1.0; -1.0; 0.0 ], 0.0; [ 0.0; 1.0; -1.0 ], 0.0 ]
                // at least one of x0, x1 and at most one of x1, x2
                problemOf [ 2.0; 3.0; -4.0 ] [ [ -1.0; -1.0; 0.0 ], -1.0; [ 0.0; 1.0; 1.0 ], 1.0 ]
            ]

        for problem in problems do
            let n = problem.ObjectiveCoeffs.Length
            let states = spectrum problem
            let minEnergy = states |> Array.map snd |> Array.min
            let optimum = classicalOptima problem |> List.head |> objective problem

            let qubo = (toQubo problem) |> Result.defaultWith (fun err -> failwith $"{err}")

            for (bits, _) in states do
                match repair problem bits with
                | Error err -> Assert.Fail($"repair failed: {err}")
                | Ok repaired ->
                    Assert.True(isValid problem repaired, $"%A{bits} repaired to infeasible %A{repaired}")

                    if isValid problem bits then
                        Assert.Equal<int[]>(bits.[0 .. n - 1], repaired.[0 .. n - 1])

                    // The slack bits match the assignment: no penalty is left, so the energy
                    // is the objective up to the constant the QUBO drops.
                    Assert.Equal(objective problem repaired.[0 .. n - 1] - optimum, energy qubo repaired - minEnergy, 9)

    [<Fact>]
    let ``constraints that exclude each other are reported as invalid after the repair`` () : Task =
        task {
            // x0 <= 0 and -x0 <= -1: each has a solution, together they have none.
            let problem = problemOf [ 1.0; 1.0 ] [ [ 1.0; 0.0 ], 0.0; [ -1.0; 0.0 ], -1.0 ]

            for index in 0..3 do
                match repair problem (bitsOf 2 index) with
                | Error err -> Assert.Fail($"repair failed: {err}")
                | Ok repaired -> Assert.False(isValid problem repaired)

            match! solveWithConfigAsync (createLocalBackend ()) problem fastConfig CancellationToken.None with
            | Error err -> Assert.Fail($"solve failed: {err}")
            | Ok solution ->
                Assert.False(solution.IsValid)
                Assert.True(solution.WasRepaired)
                Assert.Equal(1, solution.ConstraintsSatisfied)

                match solution.Sampling with
                | None -> Assert.Fail("no sampling statistics")
                | Some stats -> Assert.Equal(0, stats.Valid)
        }

    [<Fact>]
    let ``solve with a negative coefficient reports what it returns`` () : Task =
        task {
            let problem = problemOf [ 1.0; -5.0; -5.0 ] [ [ 1.0; -1.0; -1.0 ], 0.0 ]

            match! solveWithConfigAsync (createLocalBackend ()) problem defaultConfig CancellationToken.None with
            | Error err -> Assert.Fail($"solve failed: {err}")
            | Ok solution ->
                Assert.Equal(3, solution.Variables.Length)
                // Feasible either as sampled or after the repair, which always succeeds here.
                Assert.True(solution.IsValid)
                Assert.True(feasible problem solution.Variables)
                Assert.Equal(objective problem solution.Variables, solution.ObjectiveValue, 12)
                Assert.True(solution.ObjectiveValue >= -10.0)

                match solution.Sampling with
                | None -> Assert.Fail("no sampling statistics")
                | Some stats ->
                    // 3 decision bits + 2 slack bits for the range 0 - (-2)
                    Assert.Equal(5, stats.Qubits)
                    Assert.Equal(defaultConfig.FinalShots, stats.Shots)

                    if solution.WasRepaired then
                        Assert.True(stats.Valid < stats.Shots)
                    else
                        Assert.True(stats.Hits >= 1)
                        Assert.True(stats.Hits <= stats.Valid)
        }
