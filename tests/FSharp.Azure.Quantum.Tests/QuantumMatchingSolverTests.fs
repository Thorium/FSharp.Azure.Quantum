module FSharp.Azure.Quantum.Tests.QuantumMatchingSolverTests

open Xunit
open FSharp.Azure.Quantum.Quantum.QuantumMatchingSolver
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
    let ``toQubo produces correct size for single edge`` () =
        let problem : Problem = {
            NumVertices = 2
            Edges = [ { Source = 0; Target = 1; Weight = 3.0 } ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // 1 edge → 1x1 QUBO
            Assert.Equal(1, qubo.GetLength(0))
            Assert.Equal(1, qubo.GetLength(1))
            // Diagonal: -weight (maximize → minimize negated)
            Assert.True(qubo.[0, 0] < 0.0, $"Diagonal should be negative for weight maximization, got {qubo.[0, 0]}")

    [<Fact>]
    let ``toQubo adds penalty for edges sharing a vertex`` () =
        // Triangle: edges (0,1), (1,2), (0,2) — all share vertices
        let problem : Problem = {
            NumVertices = 3
            Edges = [
                { Source = 0; Target = 1; Weight = 1.0 }
                { Source = 1; Target = 2; Weight = 1.0 }
                { Source = 0; Target = 2; Weight = 1.0 }
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // 3 edges → 3x3 QUBO
            Assert.Equal(3, qubo.GetLength(0))
            // Off-diagonal terms should be positive (penalty for conflicting edges)
            // Edges 0 and 1 share vertex 1, edges 0 and 2 share vertex 0,
            // edges 1 and 2 share vertex 2
            let offDiag01 = qubo.[0, 1] + qubo.[1, 0]
            let offDiag02 = qubo.[0, 2] + qubo.[2, 0]
            let offDiag12 = qubo.[1, 2] + qubo.[2, 1]
            Assert.True(offDiag01 > 0.0, $"Penalty for edges sharing vertex should be positive, got {offDiag01}")
            Assert.True(offDiag02 > 0.0, $"Penalty for edges sharing vertex should be positive, got {offDiag02}")
            Assert.True(offDiag12 > 0.0, $"Penalty for edges sharing vertex should be positive, got {offDiag12}")

    [<Fact>]
    let ``toQubo no penalty for non-adjacent edges`` () =
        // Path: 0-1, 2-3 (edges don't share vertices)
        let problem : Problem = {
            NumVertices = 4
            Edges = [
                { Source = 0; Target = 1; Weight = 1.0 }
                { Source = 2; Target = 3; Weight = 1.0 }
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // No shared vertices → no off-diagonal penalty
            Assert.Equal(0.0, qubo.[0, 1], 6)
            Assert.Equal(0.0, qubo.[1, 0], 6)

    [<Fact>]
    let ``toQubo QUBO is symmetric`` () =
        let problem : Problem = {
            NumVertices = 4
            Edges = [
                { Source = 0; Target = 1; Weight = 2.0 }
                { Source = 1; Target = 2; Weight = 3.0 }
                { Source = 2; Target = 3; Weight = 1.0 }
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
    let ``toQubo weights affect diagonal values`` () =
        let problem : Problem = {
            NumVertices = 3
            Edges = [
                { Source = 0; Target = 1; Weight = 5.0 }
                { Source = 1; Target = 2; Weight = 10.0 }
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // Higher-weight edge should have more negative diagonal
            // (stronger incentive to select)
            Assert.True(qubo.[1, 1] < qubo.[0, 0],
                $"Higher-weight edge diagonal should be more negative: {qubo.[1, 1]} vs {qubo.[0, 0]}")

    [<Fact>]
    let ``toQubo handles duplicate edges by deduplication`` () =
        let problem : Problem = {
            NumVertices = 2
            Edges = [
                { Source = 0; Target = 1; Weight = 3.0 }
                { Source = 1; Target = 0; Weight = 5.0 }  // Reverse duplicate
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // After normalization, only one edge should remain → 1x1 QUBO
            Assert.Equal(1, qubo.GetLength(0))

// ============================================================================
// VALIDATION TESTS
// ============================================================================

module ValidationTests =

    [<Fact>]
    let ``toQubo rejects empty edges`` () =
        let problem : Problem = { NumVertices = 3; Edges = [] }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("edges", field)
        | _ -> Assert.Fail("Should reject empty edges")

    [<Fact>]
    let ``toQubo rejects zero vertices`` () =
        let problem : Problem = {
            NumVertices = 0
            Edges = [ { Source = 0; Target = 1; Weight = 1.0 } ]
        }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("numVertices", field)
        | _ -> Assert.Fail("Should reject zero vertices")

    [<Fact>]
    let ``toQubo rejects out-of-range edge endpoints`` () =
        let problem : Problem = {
            NumVertices = 2
            Edges = [ { Source = 0; Target = 5; Weight = 1.0 } ]
        }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("edge", field)
        | _ -> Assert.Fail("Should reject out-of-range endpoints")

    [<Fact>]
    let ``toQubo rejects negative edge endpoints`` () =
        let problem : Problem = {
            NumVertices = 3
            Edges = [ { Source = -1; Target = 1; Weight = 1.0 } ]
        }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("edge", field)
        | _ -> Assert.Fail("Should reject negative endpoints")

    [<Fact>]
    let ``toQubo rejects self-loops`` () =
        let problem : Problem = {
            NumVertices = 3
            Edges = [ { Source = 1; Target = 1; Weight = 1.0 } ]
        }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("edge", field)
        | _ -> Assert.Fail("Should reject self-loops")

    [<Fact>]
    let ``solveWithConfig rejects empty edges`` () =
        let backend = createLocalBackend ()
        let problem : Problem = { NumVertices = 3; Edges = [] }
        match solveWithConfig backend problem defaultConfig with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("edges", field)
        | _ -> Assert.Fail("Should reject empty edges")

// ============================================================================
// isValid TESTS
// ============================================================================

module IsValidTests =

    [<Fact>]
    let ``isValid accepts valid matching`` () =
        // Path: 0-1, 2-3 — selecting both is valid (no shared vertices)
        let problem : Problem = {
            NumVertices = 4
            Edges = [
                { Source = 0; Target = 1; Weight = 1.0 }
                { Source = 2; Target = 3; Weight = 1.0 }
            ]
        }
        Assert.True(isValid problem [| 1; 1 |])

    [<Fact>]
    let ``isValid rejects invalid matching`` () =
        // Triangle: edges (0,1), (1,2) share vertex 1
        let problem : Problem = {
            NumVertices = 3
            Edges = [
                { Source = 0; Target = 1; Weight = 1.0 }
                { Source = 1; Target = 2; Weight = 1.0 }
            ]
        }
        Assert.False(isValid problem [| 1; 1 |])

    [<Fact>]
    let ``isValid accepts empty selection`` () =
        let problem : Problem = {
            NumVertices = 3
            Edges = [ { Source = 0; Target = 1; Weight = 1.0 } ]
        }
        Assert.True(isValid problem [| 0 |])

    [<Fact>]
    let ``isValid rejects wrong-length bitstring`` () =
        let problem : Problem = {
            NumVertices = 3
            Edges = [
                { Source = 0; Target = 1; Weight = 1.0 }
                { Source = 1; Target = 2; Weight = 1.0 }
            ]
        }
        Assert.False(isValid problem [| 1 |])  // Too short
        Assert.False(isValid problem [| 1; 0; 1 |])  // Too long

// ============================================================================
// QUBIT ESTIMATION TESTS
// ============================================================================

module QubitEstimationTests =

    [<Fact>]
    let ``estimateQubits returns edge count`` () =
        let problem : Problem = {
            NumVertices = 4
            Edges = [
                { Source = 0; Target = 1; Weight = 1.0 }
                { Source = 1; Target = 2; Weight = 1.0 }
                { Source = 2; Target = 3; Weight = 1.0 }
            ]
        }
        Assert.Equal(3, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits handles empty graph`` () =
        let problem : Problem = { NumVertices = 5; Edges = [] }
        Assert.Equal(0, estimateQubits problem)

// ============================================================================
// DECOMPOSE / RECOMBINE TESTS
// ============================================================================

module DecomposeRecombineTests =

    [<Fact>]
    let ``decompose returns single problem`` () =
        let problem : Problem = {
            NumVertices = 2
            Edges = [ { Source = 0; Target = 1; Weight = 1.0 } ]
        }
        let parts = decompose problem
        Assert.Equal(1, parts.Length)

    [<Fact>]
    let ``recombine handles empty list`` () =
        let result = recombine []
        Assert.Equal(0, result.MatchingSize)
        Assert.False(result.IsValid)

    [<Fact>]
    let ``recombine returns single solution`` () =
        let sol : Solution = {
            SelectedEdges = [ { Source = 0; Target = 1; Weight = 5.0 } ]
            TotalWeight = 5.0
            MatchingSize = 1
            IsValid = true
            WasRepaired = false
            BackendName = "Test"
            NumShots = 100
            OptimizedParameters = None
            OptimizationConverged = None
        }
        let result = recombine [ sol ]
        Assert.Equal(5.0, result.TotalWeight, 6)

    [<Fact>]
    let ``recombine picks best of multiple solutions`` () =
        let sol1 : Solution = {
            SelectedEdges = []
            TotalWeight = 3.0
            MatchingSize = 1
            IsValid = true
            WasRepaired = false
            BackendName = ""; NumShots = 0
            OptimizedParameters = None; OptimizationConverged = None
        }
        let sol2 : Solution = {
            SelectedEdges = []
            TotalWeight = 7.0
            MatchingSize = 2
            IsValid = true
            WasRepaired = false
            BackendName = ""; NumShots = 0
            OptimizedParameters = None; OptimizationConverged = None
        }
        let result = recombine [ sol1; sol2 ]
        Assert.Equal(7.0, result.TotalWeight, 6)

// ============================================================================
// QUANTUM SOLVER TESTS (using LocalBackend)
// ============================================================================

module QuantumSolverTests =

    [<Fact>]
    let ``solve returns Ok for single edge`` () =
        let backend = createLocalBackend ()
        let problem : Problem = {
            NumVertices = 2
            Edges = [ { Source = 0; Target = 1; Weight = 5.0 } ]
        }

        match solve backend problem 100 with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid, "Solution should be a valid matching")
            Assert.Equal("Local Simulator", solution.BackendName)

    [<Fact>]
    let ``solve returns valid matching for path graph`` () =
        let backend = createLocalBackend ()
        // Path: 0-1-2-3 (3 edges)
        // Optimal matching: edges (0,1) and (2,3) → weight 2.0
        let problem : Problem = {
            NumVertices = 4
            Edges = [
                { Source = 0; Target = 1; Weight = 1.0 }
                { Source = 1; Target = 2; Weight = 1.0 }
                { Source = 2; Target = 3; Weight = 1.0 }
            ]
        }

        match solve backend problem 200 with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid, "Solution should be a valid matching")

    [<Fact>]
    let ``solve returns valid matching for triangle`` () =
        let backend = createLocalBackend ()
        // Triangle: only one edge can be selected
        let problem : Problem = {
            NumVertices = 3
            Edges = [
                { Source = 0; Target = 1; Weight = 1.0 }
                { Source = 1; Target = 2; Weight = 2.0 }
                { Source = 0; Target = 2; Weight = 3.0 }
            ]
        }

        match solve backend problem 200 with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid, "Solution should be a valid matching")
            Assert.True(solution.MatchingSize <= 1,
                $"Triangle matching should have at most 1 edge, got {solution.MatchingSize}")

    [<Fact>]
    let ``solveWithConfig uses config shots`` () =
        let backend = createLocalBackend ()
        let problem : Problem = {
            NumVertices = 2
            Edges = [ { Source = 0; Target = 1; Weight = 1.0 } ]
        }
        let config = { defaultConfig with FinalShots = 42 }

        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solveWithConfig failed: {err}")
        | Ok solution ->
            Assert.Equal(42, solution.NumShots)

    [<Fact>]
    let ``solve with constraint repair produces valid matching`` () =
        let backend = createLocalBackend ()
        // Star graph: center vertex 0 connected to 1,2,3
        // All edges share vertex 0, so at most 1 can be selected
        let problem : Problem = {
            NumVertices = 4
            Edges = [
                { Source = 0; Target = 1; Weight = 1.0 }
                { Source = 0; Target = 2; Weight = 2.0 }
                { Source = 0; Target = 3; Weight = 3.0 }
            ]
        }
        let config = { defaultConfig with EnableConstraintRepair = true }

        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solve with repair failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid, "Repaired solution should be valid")

    [<Fact>]
    let ``solve with disjoint edges returns valid matching`` () =
        let backend = createLocalBackend ()
        // Two disjoint edges: (0,1) and (2,3) — both can be selected
        let problem : Problem = {
            NumVertices = 4
            Edges = [
                { Source = 0; Target = 1; Weight = 5.0 }
                { Source = 2; Target = 3; Weight = 5.0 }
            ]
        }

        match solve backend problem 200 with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid, "Solution should be valid")

    [<Fact>]
    let ``solve with weighted edges prefers heavier`` () =
        let backend = createLocalBackend ()
        // Two edges sharing vertex 1: (0,1) weight 1 vs (1,2) weight 100
        // Should prefer the heavier edge
        let problem : Problem = {
            NumVertices = 3
            Edges = [
                { Source = 0; Target = 1; Weight = 1.0 }
                { Source = 1; Target = 2; Weight = 100.0 }
            ]
        }
        let config = { defaultConfig with EnableConstraintRepair = true }

        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid, "Solution should be valid")
            // After constraint repair, should keep the heavier edge
            if solution.MatchingSize = 1 && solution.WasRepaired then
                Assert.True(solution.TotalWeight >= 100.0,
                    $"Should prefer heavier edge, got weight {solution.TotalWeight}")
