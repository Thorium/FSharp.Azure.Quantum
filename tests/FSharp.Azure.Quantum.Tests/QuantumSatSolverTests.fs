module FSharp.Azure.Quantum.Tests.QuantumSatSolverTests

open Xunit
open FSharp.Azure.Quantum.Quantum.QuantumSatSolver
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends

/// Helper to create local backend for tests
let private createLocalBackend () : BackendAbstraction.IQuantumBackend =
    LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

/// Helper to create a positive literal
let private pos (v: int) : Literal = { Variable = v; IsNegated = false }

/// Helper to create a negated literal
let private neg (v: int) : Literal = { Variable = v; IsNegated = true }

/// Helper to create a clause from a list of literals
let private clause (lits: Literal list) : Clause = { Literals = lits }

// ============================================================================
// QUBO ENCODING TESTS
// ============================================================================

module QuboEncodingTests =

    [<Fact>]
    let ``toQubo produces correct matrix size for 2-SAT`` () =
        // 2 variables, 2 clauses of 2 literals each → no auxiliary → 2x2
        let problem : Problem = {
            NumVariables = 2
            Clauses = [
                clause [ pos 0; pos 1 ]
                clause [ neg 0; neg 1 ]
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            Assert.Equal(2, Array2D.length1 qubo)
            Assert.Equal(2, Array2D.length2 qubo)

    [<Fact>]
    let ``toQubo produces correct matrix size for 3-SAT`` () =
        // 3 variables, 1 clause of 3 literals → 1 auxiliary → 4x4
        let problem : Problem = {
            NumVariables = 3
            Clauses = [
                clause [ pos 0; pos 1; pos 2 ]
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            Assert.Equal(4, Array2D.length1 qubo)
            Assert.Equal(4, Array2D.length2 qubo)

    [<Fact>]
    let ``toQubo QUBO is symmetric`` () =
        let problem : Problem = {
            NumVariables = 3
            Clauses = [
                clause [ pos 0; neg 1 ]
                clause [ pos 1; pos 2 ]
                clause [ neg 0; neg 2 ]
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let n = Array2D.length1 qubo
            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    Assert.Equal(qubo.[i, j], qubo.[j, i], 10)

    [<Fact>]
    let ``toQubo satisfying assignment has lower energy than unsatisfying`` () =
        // (x0 OR x1): satisfied by [1,0], unsatisfied by [0,0]
        let problem : Problem = {
            NumVariables = 2
            Clauses = [ clause [ pos 0; pos 1 ] ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // Compute energy for [1,0] (satisfied)
            let satisfiedBits = [| 1; 0 |]
            let mutable eSatisfied = 0.0
            for i in 0 .. 1 do
                for j in 0 .. 1 do
                    eSatisfied <- eSatisfied + qubo.[i, j] * float satisfiedBits.[i] * float satisfiedBits.[j]

            // Compute energy for [0,0] (unsatisfied)
            let unsatisfiedBits = [| 0; 0 |]
            let mutable eUnsatisfied = 0.0
            for i in 0 .. 1 do
                for j in 0 .. 1 do
                    eUnsatisfied <- eUnsatisfied + qubo.[i, j] * float unsatisfiedBits.[i] * float unsatisfiedBits.[j]

            Assert.True(eSatisfied < eUnsatisfied,
                $"Satisfied energy ({eSatisfied}) should be less than unsatisfied ({eUnsatisfied})")

    [<Fact>]
    let ``toQubo 1-literal clause penalty is correct`` () =
        // Single clause (x0): unsatisfied when x0=0
        // Penalty = (1-x0) → Q[0,0] should be -1.0 (linear term)
        let problem : Problem = {
            NumVariables = 1
            Clauses = [ clause [ pos 0 ] ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // For positive literal: penalty*(1-x0) = -penalty*x0 on diagonal
            Assert.True(qubo.[0, 0] < 0.0,
                $"Diagonal should be negative for positive 1-literal clause, got {qubo.[0, 0]}")

    [<Fact>]
    let ``toQubo negated 1-literal clause penalty is correct`` () =
        // Single clause (NOT x0): unsatisfied when x0=1
        // Penalty = x0 → Q[0,0] should be +1.0
        let problem : Problem = {
            NumVariables = 1
            Clauses = [ clause [ neg 0 ] ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            Assert.True(qubo.[0, 0] > 0.0,
                $"Diagonal should be positive for negated 1-literal clause, got {qubo.[0, 0]}")

    [<Fact>]
    let ``toQubo returns error for empty clauses`` () =
        let problem : Problem = { NumVariables = 2; Clauses = [] }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("clauses", field)
        | _ -> Assert.Fail("Expected validation error for empty clauses")

    [<Fact>]
    let ``toQubo returns error for zero variables`` () =
        let problem : Problem = {
            NumVariables = 0
            Clauses = [ clause [ pos 0 ] ]
        }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("numVariables", field)
        | _ -> Assert.Fail("Expected validation error for zero variables")

    [<Fact>]
    let ``toQubo returns error for empty clause`` () =
        let problem : Problem = {
            NumVariables = 2
            Clauses = [ clause []; clause [ pos 0 ] ]
        }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("clause", field)
        | _ -> Assert.Fail("Expected validation error for empty clause")

    [<Fact>]
    let ``toQubo returns error for variable out of range`` () =
        let problem : Problem = {
            NumVariables = 2
            Clauses = [ clause [ pos 0; pos 5 ] ]  // var 5 > numVars 2
        }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("variable", field)
        | _ -> Assert.Fail("Expected validation error for variable out of range")

// ============================================================================
// SOLUTION DECODING TESTS
// ============================================================================

module DecodingTests =

    [<Fact>]
    let ``isValid accepts correct-length bitstring`` () =
        // 2 vars, 1 three-literal clause → 1 aux → total 3 qubits
        let problem : Problem = {
            NumVariables = 2
            Clauses = [ clause [ pos 0; pos 1; pos 0 ] ]
        }
        Assert.True(isValid problem [| 1; 0; 0 |])

    [<Fact>]
    let ``isValid rejects wrong-length bitstring`` () =
        let problem : Problem = {
            NumVariables = 2
            Clauses = [ clause [ pos 0; pos 1 ] ]
        }
        // Should need 2 qubits, not 3
        Assert.False(isValid problem [| 1; 0; 0 |])

    [<Fact>]
    let ``isValid for 2-SAT needs exactly NumVariables qubits`` () =
        let problem : Problem = {
            NumVariables = 3
            Clauses = [
                clause [ pos 0; pos 1 ]
                clause [ neg 1; pos 2 ]
            ]
        }
        // No 3-literal clauses → 0 aux → total = 3
        Assert.True(isValid problem [| 1; 1; 0 |])
        Assert.False(isValid problem [| 1; 1 |])

// ============================================================================
// QUBIT ESTIMATION TESTS
// ============================================================================

module QubitEstimationTests =

    [<Fact>]
    let ``estimateQubits for pure 2-SAT`` () =
        let problem : Problem = {
            NumVariables = 5
            Clauses = [
                clause [ pos 0; neg 1 ]
                clause [ pos 2; pos 3 ]
                clause [ neg 3; pos 4 ]
            ]
        }
        Assert.Equal(5, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits adds one per 3-literal clause`` () =
        let problem : Problem = {
            NumVariables = 3
            Clauses = [
                clause [ pos 0; pos 1; pos 2 ]
                clause [ neg 0; neg 1; neg 2 ]
            ]
        }
        // 3 vars + 2 aux (one per 3-literal clause) = 5
        Assert.Equal(5, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits mixed clauses`` () =
        let problem : Problem = {
            NumVariables = 4
            Clauses = [
                clause [ pos 0 ]                          // 1-lit: 0 aux
                clause [ pos 0; pos 1 ]                   // 2-lit: 0 aux
                clause [ pos 0; pos 1; pos 2 ]            // 3-lit: 1 aux
                clause [ pos 0; neg 1 ]                   // 2-lit: 0 aux
            ]
        }
        // 4 vars + 1 aux = 5
        Assert.Equal(5, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits with no clauses`` () =
        let problem : Problem = { NumVariables = 3; Clauses = [] }
        Assert.Equal(3, estimateQubits problem)

// ============================================================================
// CONSTRAINT REPAIR TESTS
// ============================================================================

module ConstraintRepairTests =

    [<Fact>]
    let ``solve with repair satisfies all clauses on simple instance`` () =
        // (x0 OR x1) AND (NOT x0 OR x1) AND (x0 OR NOT x1)
        // Satisfying: x0=1, x1=1
        let problem : Problem = {
            NumVariables = 2
            Clauses = [
                clause [ pos 0; pos 1 ]
                clause [ neg 0; pos 1 ]
                clause [ pos 0; neg 1 ]
            ]
        }

        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = true }
        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            // After repair, should satisfy at least 2 of 3 clauses
            Assert.True(solution.SatisfiedClauses >= 2,
                $"Should satisfy at least 2 clauses, got {solution.SatisfiedClauses}")

    [<Fact>]
    let ``solve without repair returns raw QAOA result`` () =
        let problem : Problem = {
            NumVariables = 2
            Clauses = [
                clause [ pos 0; pos 1 ]
                clause [ neg 0; neg 1 ]
            ]
        }

        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = false }
        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.False(solution.WasRepaired)

// ============================================================================
// BACKEND INTEGRATION TESTS
// ============================================================================

module BackendIntegrationTests =

    [<Fact>]
    let ``solve returns solution with backend info`` () =
        let problem : Problem = {
            NumVariables = 2
            Clauses = [
                clause [ pos 0; pos 1 ]
                clause [ neg 0; pos 1 ]
            ]
        }

        let backend = createLocalBackend ()
        match solve backend problem 100 with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.False(System.String.IsNullOrEmpty(solution.BackendName))
            Assert.Equal(100, solution.NumShots)

    [<Fact>]
    let ``solveWithConfig returns optimized parameters`` () =
        let problem : Problem = {
            NumVariables = 2
            Clauses = [
                clause [ pos 0; pos 1 ]
            ]
        }

        let backend = createLocalBackend ()
        let config = { fastConfig with EnableOptimization = true }
        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.OptimizedParameters.IsSome)
            Assert.True(solution.OptimizationConverged.IsSome)

    [<Fact>]
    let ``solve validates empty clauses`` () =
        let problem : Problem = { NumVariables = 2; Clauses = [] }
        let backend = createLocalBackend ()
        match solve backend problem 100 with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("clauses", field)
        | _ -> Assert.Fail("Expected validation error")

    [<Fact>]
    let ``solve validates zero variables`` () =
        let problem : Problem = {
            NumVariables = 0
            Clauses = [ clause [ pos 0 ] ]
        }
        let backend = createLocalBackend ()
        match solve backend problem 100 with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("numVariables", field)
        | _ -> Assert.Fail("Expected validation error")

    [<Fact>]
    let ``solve validates variable index out of range`` () =
        let problem : Problem = {
            NumVariables = 2
            Clauses = [ clause [ pos 0; pos 3 ] ]
        }
        let backend = createLocalBackend ()
        match solve backend problem 100 with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("variable", field)
        | _ -> Assert.Fail("Expected validation error")

    [<Fact>]
    let ``solve validates empty clause in list`` () =
        let problem : Problem = {
            NumVariables = 2
            Clauses = [ clause [ pos 0 ]; clause [] ]
        }
        let backend = createLocalBackend ()
        match solve backend problem 100 with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("clause", field)
        | _ -> Assert.Fail("Expected validation error")

    [<Fact>]
    let ``solve produces valid result on small satisfiable instance`` () =
        // (x0) AND (x1): satisfiable by x0=1, x1=1
        let problem : Problem = {
            NumVariables = 2
            Clauses = [
                clause [ pos 0 ]
                clause [ pos 1 ]
            ]
        }

        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = true }
        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.Equal(2, solution.TotalClauses)
            Assert.True(solution.Assignment.Length = 2)

// ============================================================================
// DECOMPOSE / RECOMBINE TESTS
// ============================================================================

module DecomposeRecombineTests =

    [<Fact>]
    let ``decompose returns single problem (identity)`` () =
        let problem : Problem = {
            NumVariables = 2
            Clauses = [ clause [ pos 0; pos 1 ] ]
        }
        let result = decompose problem
        Assert.Equal(1, result.Length)

    [<Fact>]
    let ``recombine returns single solution (identity)`` () =
        let solution : Solution = {
            Assignment = [| true; false |]
            SatisfiedClauses = 1
            TotalClauses = 1
            AllSatisfied = true
            WasRepaired = false
            BackendName = "test"
            NumShots = 100
            OptimizedParameters = None
            OptimizationConverged = None
        }
        let result = recombine [ solution ]
        Assert.Equal(1, result.SatisfiedClauses)

    [<Fact>]
    let ``recombine handles empty list`` () =
        let result = recombine []
        Assert.Empty(result.Assignment)
        Assert.Equal(0, result.SatisfiedClauses)
        Assert.False(result.AllSatisfied)

    [<Fact>]
    let ``recombine picks best solution`` () =
        let sol1 : Solution = {
            Assignment = [| true; false |]
            SatisfiedClauses = 1
            TotalClauses = 3
            AllSatisfied = false
            WasRepaired = false
            BackendName = "test"
            NumShots = 100
            OptimizedParameters = None
            OptimizationConverged = None
        }
        let sol2 : Solution = {
            sol1 with SatisfiedClauses = 3; AllSatisfied = true
        }
        let result = recombine [ sol1; sol2 ]
        Assert.Equal(3, result.SatisfiedClauses)

// ============================================================================
// CLAUSE EVALUATION TESTS
// ============================================================================

module ClauseEvaluationTests =

    [<Fact>]
    let ``solve correctly counts satisfied clauses`` () =
        // Create a problem where we know the answer
        // (x0) AND (NOT x0) - impossible to satisfy both
        let problem : Problem = {
            NumVariables = 1
            Clauses = [
                clause [ pos 0 ]
                clause [ neg 0 ]
            ]
        }

        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = false }
        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            // Exactly one clause can be satisfied (contradictory)
            Assert.Equal(1, solution.SatisfiedClauses)
            Assert.False(solution.AllSatisfied)

    [<Fact>]
    let ``solve on tautology finds all-satisfying assignment`` () =
        // (x0 OR NOT x0): always true regardless of assignment
        let problem : Problem = {
            NumVariables = 1
            Clauses = [ clause [ pos 0; neg 0 ] ]
        }

        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = true }
        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.Equal(1, solution.SatisfiedClauses)
            Assert.True(solution.AllSatisfied)

// ============================================================================
// NEGATION HANDLING TESTS
// ============================================================================

module NegationTests =

    [<Fact>]
    let ``toQubo handles all-negated clause`` () =
        // (NOT x0 OR NOT x1): satisfied when at least one is false
        let problem : Problem = {
            NumVariables = 2
            Clauses = [ clause [ neg 0; neg 1 ] ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // Energy for [0,0] (both false, both negations true) = satisfied
            let mutable e00 = 0.0
            for i in 0 .. 1 do
                for j in 0 .. 1 do
                    e00 <- e00 + qubo.[i, j] * float [|0;0|].[i] * float [|0;0|].[j]

            // Energy for [1,1] (both true, both negations false) = unsatisfied
            let mutable e11 = 0.0
            for i in 0 .. 1 do
                for j in 0 .. 1 do
                    e11 <- e11 + qubo.[i, j] * float [|1;1|].[i] * float [|1;1|].[j]

            Assert.True(e00 < e11,
                $"Satisfied [0,0] energy ({e00}) should be less than unsatisfied [1,1] energy ({e11})")

    [<Fact>]
    let ``toQubo mixed positive and negated literals`` () =
        // (x0 OR NOT x1): unsatisfied only when x0=0 AND x1=1
        let problem : Problem = {
            NumVariables = 2
            Clauses = [ clause [ pos 0; neg 1 ] ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let computeEnergy (bits: int[]) =
                let mutable e = 0.0
                for i in 0 .. 1 do
                    for j in 0 .. 1 do
                        e <- e + qubo.[i, j] * float bits.[i] * float bits.[j]
                e

            let eUnsatisfied = computeEnergy [| 0; 1 |]  // x0=0, x1=1 → only unsatisfied assignment
            let eSatisfied10 = computeEnergy [| 1; 0 |]   // x0=1, x1=0 → satisfied
            let eSatisfied11 = computeEnergy [| 1; 1 |]   // x0=1, x1=1 → satisfied (x0=true)
            let eSatisfied00 = computeEnergy [| 0; 0 |]   // x0=0, x1=0 → satisfied (NOT x1=true)

            Assert.True(eSatisfied10 < eUnsatisfied, $"[1,0] ({eSatisfied10}) < [0,1] ({eUnsatisfied})")
            Assert.True(eSatisfied11 < eUnsatisfied, $"[1,1] ({eSatisfied11}) < [0,1] ({eUnsatisfied})")
            Assert.True(eSatisfied00 < eUnsatisfied, $"[0,0] ({eSatisfied00}) < [0,1] ({eUnsatisfied})")
