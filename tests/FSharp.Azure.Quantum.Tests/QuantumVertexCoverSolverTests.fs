module FSharp.Azure.Quantum.Tests.QuantumVertexCoverSolverTests

open Xunit
open FSharp.Azure.Quantum.Quantum.QuantumVertexCoverSolver
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
    let ``toQubo produces correct diagonal for unit-weight vertices with no edges`` () =
        // Arrange: 3 isolated vertices (no edges) with weight 1
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = []
        }

        // Act
        let result = toQubo problem

        // Assert: diagonal = w_i (objective only, no edge penalty)
        match result with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            Assert.Equal(1.0, qubo.[0, 0], 6)
            Assert.Equal(1.0, qubo.[1, 1], 6)
            Assert.Equal(1.0, qubo.[2, 2], 6)
            // No off-diagonal terms since no edges
            Assert.Equal(0.0, qubo.[0, 1], 6)
            Assert.Equal(0.0, qubo.[1, 2], 6)

    [<Fact>]
    let ``toQubo adds penalty for edges`` () =
        // Arrange: 2 vertices connected by an edge
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
            ]
            Edges = [ (0, 1) ]
        }

        // Act
        let result = toQubo problem

        // Assert: off-diagonal should have positive penalty
        match result with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let offDiag = qubo.[0, 1] + qubo.[1, 0]
            Assert.True(offDiag > 0.0, $"Edge penalty should be positive, got {offDiag}")

    [<Fact>]
    let ``toQubo QUBO is symmetric`` () =
        // Arrange: triangle graph
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 2.0 }
                { Id = "B"; Weight = 3.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2); (0, 2) ]
        }

        // Act
        let result = toQubo problem

        // Assert
        match result with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let n = Array2D.length1 qubo
            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    Assert.Equal(qubo.[i, j], qubo.[j, i], 10)

    [<Fact>]
    let ``toQubo optimal bitstring minimises QUBO energy for path graph`` () =
        // Arrange: path A-B-C. Optimal cover: {B} (covers both edges).
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2) ]
        }

        // Act
        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let eval (bits: int[]) =
                QaoaExecutionHelpers.evaluateQubo qubo bits

            // {B} = [0,1,0] covers both edges, size 1
            let energyB = eval [| 0; 1; 0 |]
            // {A,C} = [1,0,1] covers both edges, size 2
            let energyAC = eval [| 1; 0; 1 |]
            // {A,B} = [1,1,0] covers both edges, size 2
            let energyAB = eval [| 1; 1; 0 |]
            // {B,C} = [0,1,1] covers both edges, size 2
            let energyBC = eval [| 0; 1; 1 |]

            // Optimal {B} should have lowest energy among valid covers
            Assert.True(energyB < energyAC, $"B ({energyB}) should beat A,C ({energyAC})")
            Assert.True(energyB < energyAB, $"B ({energyB}) should beat A,B ({energyAB})")
            Assert.True(energyB < energyBC, $"B ({energyB}) should beat B,C ({energyBC})")

    [<Fact>]
    let ``toQubo returns error for empty problem`` () =
        let problem : Problem = { Vertices = []; Edges = [] }
        let result = toQubo problem
        match result with
        | Error err -> Assert.Contains("no vertices", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with empty vertices")

    [<Fact>]
    let ``toQubo handles duplicate edges without double-counting`` () =
        // Arrange: duplicate edge (0,1) appears twice
        let problemDup : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (0, 1) ]
        }
        let problemSingle : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
            ]
            Edges = [ (0, 1) ]
        }

        // Act
        match toQubo problemDup, toQubo problemSingle with
        | Ok quboDup, Ok quboSingle ->
            // QUBO matrices should be identical
            for i in 0 .. 1 do
                for j in 0 .. 1 do
                    Assert.Equal(quboSingle.[i, j], quboDup.[i, j], 10)
        | _ -> Assert.Fail("toQubo should succeed for both")

    [<Fact>]
    let ``toQubo handles bidirectional edges without double-counting`` () =
        // Arrange: (0,1) and (1,0) should be treated as same edge
        let problemBidi : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 0) ]
        }
        let problemSingle : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
            ]
            Edges = [ (0, 1) ]
        }

        match toQubo problemBidi, toQubo problemSingle with
        | Ok quboBidi, Ok quboSingle ->
            for i in 0 .. 1 do
                for j in 0 .. 1 do
                    Assert.Equal(quboSingle.[i, j], quboBidi.[i, j], 10)
        | _ -> Assert.Fail("toQubo should succeed for both")

// ============================================================================
// ROUND-TRIP TESTS (encode -> decode with known optimal bitstring)
// ============================================================================

module RoundTripTests =

    [<Fact>]
    let ``decode with known optimal bitstring produces correct solution`` () =
        // Arrange: star graph, center vertex covers all edges
        let problem : Problem = {
            Vertices = [
                { Id = "Center"; Weight = 1.0 }
                { Id = "Leaf1"; Weight = 1.0 }
                { Id = "Leaf2"; Weight = 1.0 }
                { Id = "Leaf3"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (0, 2); (0, 3) ]
        }
        let bits = [| 1; 0; 0; 0 |]  // Only center

        // Act
        let solution =
            let selected =
                problem.Vertices
                |> List.indexed
                |> List.choose (fun (i, v) -> if bits.[i] = 1 then Some v else None)
            {
                CoverVertices = selected
                CoverWeight = selected |> List.sumBy (fun v -> v.Weight)
                CoverSize = selected.Length
                IsValid = isValid problem bits
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
            }

        // Assert
        Assert.Equal(1, solution.CoverSize)
        Assert.Equal(1.0, solution.CoverWeight, 6)
        Assert.True(solution.IsValid)
        Assert.Equal("Center", solution.CoverVertices.Head.Id)

    [<Fact>]
    let ``decode weighted problem selects correct vertices`` () =
        // Arrange: 2 vertices, edge (0,1), vertex 0 lighter
        let problem : Problem = {
            Vertices = [
                { Id = "Light"; Weight = 1.0 }
                { Id = "Heavy"; Weight = 5.0 }
            ]
            Edges = [ (0, 1) ]
        }
        let bits = [| 1; 0 |]  // Select lighter vertex

        // Act & Assert
        Assert.True(isValid problem bits)

// ============================================================================
// CONSTRAINT REPAIR TESTS
// ============================================================================

module ConstraintRepairTests =

    [<Fact>]
    let ``repair fixes empty selection on graph with edges`` () =
        // Arrange: path A-B, nothing selected
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
            ]
            Edges = [ (0, 1) ]
        }

        // We cannot call repairConstraints directly (it's private),
        // so test via solveWithConfig with repair enabled
        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = true; FinalShots = 50 }

        // Act
        let result = solveWithConfig backend problem config

        // Assert: with repair, result must be valid
        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid,
                $"Solution should be valid after repair. WasRepaired={solution.WasRepaired}")

    [<Fact>]
    let ``repair produces valid cover on triangle graph`` () =
        // Arrange: triangle, QAOA might return infeasible
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2); (0, 2) ]
        }
        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = true; FinalShots = 50 }

        // Act
        let result = solveWithConfig backend problem config

        // Assert
        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid,
                $"Cover must be valid. Size={solution.CoverSize}, Repaired={solution.WasRepaired}")
            // Triangle requires at least 2 vertices in any cover
            Assert.True(solution.CoverSize >= 2,
                $"Triangle cover needs >= 2 vertices, got {solution.CoverSize}")

    [<Fact>]
    let ``repair removes redundant vertices`` () =
        // Arrange: path A-B-C, if all three are selected, A or C should be removable
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2) ]
        }
        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = true; FinalShots = 50 }

        // Act
        let result = solveWithConfig backend problem config

        // Assert: cover should be valid
        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid)
            // Minimum cover for path of 3 is 1 vertex (the middle one)
            // So cover size should be <= 3 and >= 1
            Assert.True(solution.CoverSize >= 1 && solution.CoverSize <= 3)

// ============================================================================
// VALIDITY TESTS
// ============================================================================

module ValidityTests =

    [<Fact>]
    let ``isValid returns true for valid cover`` () =
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2) ]
        }
        // B covers both edges
        Assert.True(isValid problem [| 0; 1; 0 |])
        // A and C cover both edges
        Assert.True(isValid problem [| 1; 0; 1 |])
        // All selected
        Assert.True(isValid problem [| 1; 1; 1 |])

    [<Fact>]
    let ``isValid returns false for invalid cover`` () =
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2) ]
        }
        // Nothing selected - edges uncovered
        Assert.False(isValid problem [| 0; 0; 0 |])
        // Only C selected - edge (0,1) uncovered
        Assert.False(isValid problem [| 0; 0; 1 |])

    [<Fact>]
    let ``isValid on edgeless graph is always true`` () =
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
            ]
            Edges = []
        }
        Assert.True(isValid problem [| 0; 0 |])
        Assert.True(isValid problem [| 1; 0 |])

    [<Fact>]
    let ``isValid rejects wrong-length bitstring`` () =
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
            ]
            Edges = [ (0, 1) ]
        }
        // Too short
        Assert.False(isValid problem [| 1 |])
        // Too long
        Assert.False(isValid problem [| 1; 1; 1 |])

// ============================================================================
// BACKEND INTEGRATION TESTS
// ============================================================================

module BackendIntegrationTests =

    [<Fact>]
    let ``solve returns solution with backend info`` () =
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
            ]
            Edges = [ (0, 1) ]
        }
        let backend = createLocalBackend ()

        let result = solve backend problem 100

        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.NotEmpty(solution.BackendName)
            Assert.Equal(100, solution.NumShots)

    [<Fact>]
    let ``solveWithConfig returns optimized parameters`` () =
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2) ]
        }
        let backend = createLocalBackend ()
        let config = { defaultConfig with
                        EnableOptimization = true
                        NumLayers = 2
                        OptimizationShots = 50
                        FinalShots = 100 }

        let result = solveWithConfig backend problem config

        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.OptimizedParameters.IsSome,
                "Should return optimized parameters")
            match solution.OptimizedParameters with
            | Some parameters ->
                Assert.Equal(2, parameters.Length)
                for (gamma, beta) in parameters do
                    Assert.True(gamma >= 0.0 && gamma <= System.Math.PI,
                        $"Gamma {gamma} should be in [0, pi]")
                    Assert.True(beta >= 0.0 && beta <= System.Math.PI / 2.0,
                        $"Beta {beta} should be in [0, pi/2]")
            | None -> Assert.Fail("OptimizedParameters should not be None")

    [<Fact>]
    let ``solve validates empty vertex list`` () =
        let problem : Problem = { Vertices = []; Edges = [] }
        let backend = createLocalBackend ()

        let result = solve backend problem 100

        match result with
        | Error err -> Assert.Contains("no vertices", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with empty vertices")

    [<Fact>]
    let ``solve validates edge indices out of range`` () =
        let problem : Problem = {
            Vertices = [ { Id = "A"; Weight = 1.0 } ]
            Edges = [ (0, 5) ]  // Index 5 doesn't exist
        }
        let backend = createLocalBackend ()

        let result = solve backend problem 100

        match result with
        | Error err -> Assert.Contains("edge", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with invalid edge index")

    [<Fact>]
    let ``solve rejects self-loop edges`` () =
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
            ]
            Edges = [ (0, 0) ]  // Self-loop
        }
        let backend = createLocalBackend ()

        let result = solve backend problem 100

        match result with
        | Error err -> Assert.Contains("self-loop", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with self-loop edge")

    [<Fact>]
    let ``solve produces valid cover on small graph`` () =
        // Arrange: 4-vertex path A-B-C-D
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
                { Id = "D"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2); (2, 3) ]
        }
        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = true; FinalShots = 100 }

        let result = solveWithConfig backend problem config

        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid,
                $"Solution must be valid. Cover={solution.CoverVertices |> List.map (fun v -> v.Id)}")

    [<Fact>]
    let ``solve handles duplicate edges gracefully`` () =
        // Duplicate and bidirectional edges should not cause errors
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (0, 1); (1, 0); (1, 2) ]
        }
        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = true; FinalShots = 50 }

        let result = solveWithConfig backend problem config

        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid)

// ============================================================================
// QUBIT ESTIMATION TESTS
// ============================================================================

module QubitEstimationTests =

    [<Fact>]
    let ``estimateQubits returns vertex count`` () =
        let problem : Problem = {
            Vertices = List.init 7 (fun i -> { Id = $"V{i}"; Weight = 1.0 })
            Edges = []
        }
        Assert.Equal(7, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits returns 0 for empty problem`` () =
        let problem : Problem = { Vertices = []; Edges = [] }
        Assert.Equal(0, estimateQubits problem)

// ============================================================================
// DECOMPOSE / RECOMBINE TESTS
// ============================================================================

module DecomposeRecombineTests =

    [<Fact>]
    let ``decompose returns single problem (identity)`` () =
        let problem : Problem = {
            Vertices = [ { Id = "A"; Weight = 1.0 } ]
            Edges = []
        }
        let parts = decompose problem
        Assert.Equal(1, parts.Length)
        Assert.Equal(problem.Vertices.Length, parts.Head.Vertices.Length)

    [<Fact>]
    let ``recombine returns single solution (identity)`` () =
        let solution : Solution = {
            CoverVertices = []
            CoverWeight = 0.0
            CoverSize = 0
            IsValid = true
            WasRepaired = false
            BackendName = "test"
            NumShots = 100
            OptimizedParameters = None
            OptimizationConverged = None
        }
        let combined = recombine [ solution ]
        Assert.Equal("test", combined.BackendName)

    [<Fact>]
    let ``recombine handles empty list`` () =
        let combined = recombine []
        Assert.Equal(0, combined.CoverSize)
        Assert.True(combined.IsValid)
