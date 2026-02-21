module FSharp.Azure.Quantum.Tests.QuantumCliqueSolverTests

open Xunit
open FSharp.Azure.Quantum.Quantum.QuantumCliqueSolver
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
    let ``toQubo produces negative diagonal (maximize clique size)`` () =
        // Arrange: 3-vertex complete graph (K3 = triangle)
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2); (0, 2) ]
        }

        // Act
        let result = toQubo problem

        // Assert: diagonal should be -w_i (incentivise selection)
        match result with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            Assert.Equal(-1.0, qubo.[0, 0], 6)
            Assert.Equal(-1.0, qubo.[1, 1], 6)
            Assert.Equal(-1.0, qubo.[2, 2], 6)

    [<Fact>]
    let ``toQubo adds no non-edge penalty for complete graph`` () =
        // Arrange: K3 - every pair is an edge, so no non-edges
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2); (0, 2) ]
        }

        // Act
        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // No non-edges => off-diagonal should be 0
            Assert.Equal(0.0, qubo.[0, 1], 6)
            Assert.Equal(0.0, qubo.[1, 0], 6)
            Assert.Equal(0.0, qubo.[0, 2], 6)
            Assert.Equal(0.0, qubo.[1, 2], 6)

    [<Fact>]
    let ``toQubo adds penalty for non-edges`` () =
        // Arrange: path A-B-C (non-edge: A-C)
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2) ]  // Missing (0,2)
        }

        // Act
        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // Non-edge (0,2) should have positive penalty
            let penalty02 = qubo.[0, 2] + qubo.[2, 0]
            Assert.True(penalty02 > 0.0, $"Non-edge penalty should be positive, got {penalty02}")

            // Edge (0,1) should have no penalty
            Assert.Equal(0.0, qubo.[0, 1] + qubo.[1, 0], 6)

    [<Fact>]
    let ``toQubo QUBO is symmetric`` () =
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 2.0 }
                { Id = "B"; Weight = 3.0 }
                { Id = "C"; Weight = 1.0 }
                { Id = "D"; Weight = 4.0 }
            ]
            Edges = [ (0, 1); (2, 3) ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let n = Array2D.length1 qubo
            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    Assert.Equal(qubo.[i, j], qubo.[j, i], 10)

    [<Fact>]
    let ``toQubo optimal bitstring minimises QUBO energy for K3 in 4-vertex graph`` () =
        // Arrange: 4 vertices, triangle on {0,1,2}, vertex 3 isolated
        // Maximum clique = {0,1,2}
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
                { Id = "D"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2); (0, 2) ]  // Triangle on 0,1,2
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let eval (bits: int[]) = QaoaExecutionHelpers.evaluateQubo qubo bits

            // {0,1,2} = valid clique of size 3
            let energyClique = eval [| 1; 1; 1; 0 |]
            // {0,1,2,3} = invalid (3 is not connected to others)
            let energyAll = eval [| 1; 1; 1; 1 |]
            // {0,1} = valid clique of size 2
            let energyPair = eval [| 1; 1; 0; 0 |]

            // Optimal clique should have lowest energy
            Assert.True(energyClique < energyPair,
                $"Clique {{0,1,2}} ({energyClique}) should beat pair {{0,1}} ({energyPair})")
            // Including non-adjacent vertex should be penalised
            Assert.True(energyClique < energyAll,
                $"Valid clique ({energyClique}) should beat invalid ({energyAll})")

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
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (0, 1); (1, 2) ]
        }
        let problemSingle : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2) ]
        }

        match toQubo problemDup, toQubo problemSingle with
        | Ok quboDup, Ok quboSingle ->
            let n = Array2D.length1 quboSingle
            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    Assert.Equal(quboSingle.[i, j], quboDup.[i, j], 10)
        | _ -> Assert.Fail("toQubo should succeed for both")

    [<Fact>]
    let ``toQubo handles bidirectional edges without double-counting`` () =
        // Arrange: (0,1) and (1,0) should be treated as same edge
        let problemBidi : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 0); (1, 2) ]
        }
        let problemSingle : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2) ]
        }

        match toQubo problemBidi, toQubo problemSingle with
        | Ok quboBidi, Ok quboSingle ->
            let n = Array2D.length1 quboSingle
            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    Assert.Equal(quboSingle.[i, j], quboBidi.[i, j], 10)
        | _ -> Assert.Fail("toQubo should succeed for both")

// ============================================================================
// ROUND-TRIP TESTS
// ============================================================================

module RoundTripTests =

    [<Fact>]
    let ``decode with known clique bitstring produces correct solution`` () =
        // Arrange: K4 graph, all 4 vertices form a clique
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
                { Id = "D"; Weight = 1.0 }
            ]
            Edges = [ (0,1); (0,2); (0,3); (1,2); (1,3); (2,3) ]
        }
        let bits = [| 1; 1; 1; 1 |]

        // Act & Assert
        Assert.True(isValid problem bits)

    [<Fact>]
    let ``single vertex is always a valid clique`` () =
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = []  // No edges at all
        }
        // Any single vertex is a valid clique of size 1
        Assert.True(isValid problem [| 1; 0; 0 |])
        Assert.True(isValid problem [| 0; 1; 0 |])
        Assert.True(isValid problem [| 0; 0; 1 |])

// ============================================================================
// CONSTRAINT REPAIR TESTS
// ============================================================================

module ConstraintRepairTests =

    [<Fact>]
    let ``repair produces valid clique on path graph`` () =
        // Arrange: path A-B-C. Max clique = any edge pair {A,B} or {B,C}
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

        let result = solveWithConfig backend problem config

        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid,
                $"Solution should be valid clique. Size={solution.CliqueSize}, Repaired={solution.WasRepaired}")

    [<Fact>]
    let ``repair preserves valid clique`` () =
        // Arrange: K3 (triangle), QAOA might already find valid clique
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

        let result = solveWithConfig backend problem config

        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid)

    [<Fact>]
    let ``repair on disconnected graph produces valid clique`` () =
        // Arrange: 4 isolated vertices (no edges). Max clique = 1
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
                { Id = "D"; Weight = 1.0 }
            ]
            Edges = []
        }
        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = true; FinalShots = 50 }

        let result = solveWithConfig backend problem config

        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid)
            // No edges means max clique is 1
            Assert.True(solution.CliqueSize <= 1,
                $"Disconnected graph max clique is 1, got {solution.CliqueSize}")

// ============================================================================
// VALIDITY TESTS
// ============================================================================

module ValidityTests =

    [<Fact>]
    let ``isValid returns true for valid clique`` () =
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2); (0, 2) ]  // K3
        }
        // All three form a clique
        Assert.True(isValid problem [| 1; 1; 1 |])
        // Any pair is a clique
        Assert.True(isValid problem [| 1; 1; 0 |])
        // Empty selection is trivially valid
        Assert.True(isValid problem [| 0; 0; 0 |])

    [<Fact>]
    let ``isValid returns false for invalid clique`` () =
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1) ]  // Only A-B edge
        }
        // A and C are not connected
        Assert.False(isValid problem [| 1; 0; 1 |])
        // A, B, C - B and C are not connected
        Assert.False(isValid problem [| 1; 1; 1 |])

    [<Fact>]
    let ``isValid on graph with no edges rejects pairs`` () =
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
            ]
            Edges = []
        }
        Assert.False(isValid problem [| 1; 1 |])
        Assert.True(isValid problem [| 1; 0 |])

    [<Fact>]
    let ``isValid rejects wrong-length bitstring`` () =
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2); (0, 2) ]
        }
        // Too short
        Assert.False(isValid problem [| 1; 1 |])
        // Too long
        Assert.False(isValid problem [| 1; 1; 1; 0 |])

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
            Edges = [ (0, 1); (1, 2); (0, 2) ]
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
            Assert.True(solution.OptimizedParameters.IsSome)
            match solution.OptimizedParameters with
            | Some parameters ->
                Assert.Equal(2, parameters.Length)
                for (gamma, beta) in parameters do
                    Assert.True(gamma >= 0.0 && gamma <= System.Math.PI)
                    Assert.True(beta >= 0.0 && beta <= System.Math.PI / 2.0)
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
            Edges = [ (0, 3) ]
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
            Edges = [ (1, 1) ]  // Self-loop
        }
        let backend = createLocalBackend ()

        let result = solve backend problem 100

        match result with
        | Error err -> Assert.Contains("self-loop", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with self-loop edge")

    [<Fact>]
    let ``solve finds clique on small complete graph`` () =
        // K3 + isolated vertex. Max clique = 3
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
                { Id = "D"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (1, 2); (0, 2) ]
        }
        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = true; FinalShots = 100 }

        let result = solveWithConfig backend problem config

        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid,
                $"Clique must be valid. Vertices={solution.CliqueVertices |> List.map (fun v -> v.Id)}")

    [<Fact>]
    let ``solve handles duplicate edges gracefully`` () =
        // Duplicate and bidirectional edges should not cause errors
        let problem : Problem = {
            Vertices = [
                { Id = "A"; Weight = 1.0 }
                { Id = "B"; Weight = 1.0 }
                { Id = "C"; Weight = 1.0 }
            ]
            Edges = [ (0, 1); (0, 1); (1, 0); (1, 2); (0, 2) ]
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
            Vertices = List.init 5 (fun i -> { Id = $"V{i}"; Weight = 1.0 })
            Edges = []
        }
        Assert.Equal(5, estimateQubits problem)

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
            Vertices = [ { Id = "A"; Weight = 1.0 }; { Id = "B"; Weight = 1.0 } ]
            Edges = [ (0, 1) ]
        }
        let parts = decompose problem
        Assert.Equal(1, parts.Length)
        Assert.Equal(2, parts.Head.Vertices.Length)

    [<Fact>]
    let ``recombine picks largest clique`` () =
        let s1 : Solution = {
            CliqueVertices = [ { Id = "A"; Weight = 1.0 } ]
            CliqueSize = 1
            CliqueWeight = 1.0
            IsValid = true
            WasRepaired = false
            BackendName = "test"
            NumShots = 100
            OptimizedParameters = None
            OptimizationConverged = None
        }
        let s2 : Solution = {
            CliqueVertices = [ { Id = "A"; Weight = 1.0 }; { Id = "B"; Weight = 1.0 } ]
            CliqueSize = 2
            CliqueWeight = 2.0
            IsValid = true
            WasRepaired = false
            BackendName = "test"
            NumShots = 100
            OptimizedParameters = None
            OptimizationConverged = None
        }
        let combined = recombine [ s1; s2 ]
        Assert.Equal(2, combined.CliqueSize)

    [<Fact>]
    let ``recombine handles empty list`` () =
        let combined = recombine []
        Assert.Equal(0, combined.CliqueSize)
        Assert.True(combined.IsValid)
