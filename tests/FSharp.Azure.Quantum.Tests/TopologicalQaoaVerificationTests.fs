/// Step 0e: Topological QAOA Verification Tests
///
/// Verifies that QAOA-based solvers work on TopologicalUnifiedBackend
/// with both Ising and Fibonacci anyons. Documents precision loss
/// from Ising Rz discretization and performance overhead from
/// Fibonacci Solovay-Kitaev compilation.
///
/// Reference: QUANTUM-ALGORITHM-EXPANSION-PLAN.md, Step 0e (required)
module FSharp.Azure.Quantum.Tests.TopologicalQaoaVerificationTests

open Xunit
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.GraphOptimization
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Topological

// =============================================================================
// BACKEND FACTORIES
// =============================================================================

/// Create local (gate-based) backend for baseline comparison
let private createLocalBackend () : IQuantumBackend =
    LocalBackend.LocalBackend() :> IQuantumBackend

/// Create Ising anyon topological backend
/// Ising anyons have Rz discretization to pi/2 multiples,
/// which degrades QAOA angle precision.
let private createIsingBackend () : IQuantumBackend =
    TopologicalUnifiedBackendFactory.createIsing 50

/// Create Fibonacci anyon topological backend
/// Fibonacci anyons use Solovay-Kitaev compilation which
/// is more precise but significantly slower.
let private createFibonacciBackend () : IQuantumBackend =
    TopologicalUnifiedBackendFactory.createFibonacci 50

// =============================================================================
// TEST PROBLEM: 4-VERTEX GRAPH (4 QUBITS)
// =============================================================================

/// Simple 4-vertex path graph: A--B--C--D with unit weights.
/// Optimal MaxCut: {A,C} vs {B,D} with cut value = 3.0
/// (all 3 edges cross the partition).
let private create4VertexPathProblem () : QuantumMaxCutSolver.MaxCutProblem =
    {
        Vertices = [ "A"; "B"; "C"; "D" ]
        Edges = [ edge "A" "B" 1.0; edge "B" "C" 1.0; edge "C" "D" 1.0 ]
    }

/// Triangle graph: A--B--C (3 vertices = 3 qubits, fully connected).
/// Optimal MaxCut: any 1 vs 2 partition, cut value = 2.0
let private createTriangleProblem () : QuantumMaxCutSolver.MaxCutProblem =
    {
        Vertices = [ "A"; "B"; "C" ]
        Edges = [ edge "A" "B" 1.0; edge "B" "C" 1.0; edge "A" "C" 1.0 ]
    }

/// Default QAOA config for topological tests
let private defaultConfig = QuantumMaxCutSolver.defaultConfig

// =============================================================================
// STEP 0e: ISING ANYON TESTS
// =============================================================================

[<Fact>]
let ``Ising TopologicalBackend accepts QuantumMaxCutSolver.solve`` () =
    // Arrange: 4-qubit path graph on Ising backend
    let backend = createIsingBackend ()
    let problem = create4VertexPathProblem ()

    // Act: QAOA compiles to braids and executes on the topological backend
    let result = QuantumMaxCutSolver.solve backend problem defaultConfig

    // Assert: execution must succeed and return the optimal cut.
    // The solver reports the best of NumShots samples, so Ising Rz discretization
    // (pi/2 multiples) degrades the sampling distribution but does not stop the
    // optimum from appearing among 1000 shots of a 4-qubit problem.
    match result with
    | Ok solution ->
        Assert.Equal("Topological Quantum Backend", solution.BackendName)
        Assert.True(solution.NumShots > 0, "Should have executed shots")
        // Path A--B--C--D: optimal MaxCut is {A,C} vs {B,D}, cutting all 3 edges.
        Assert.Equal(3.0, solution.CutValue, 10)
        Assert.Equal(4, solution.PartitionS.Length + solution.PartitionT.Length)
    | Error err -> Assert.Fail($"Ising topological backend should execute QAOA: {err}")

[<Fact>]
let ``Ising TopologicalBackend MaxCut on triangle finds feasible partition`` () =
    // Arrange: simplest possible graph (3 qubits)
    let backend = createIsingBackend ()
    let problem = createTriangleProblem ()

    // Act
    let result = QuantumMaxCutSolver.solve backend problem defaultConfig

    // Assert: solve must succeed and find the optimal 1-vs-2 split
    match result with
    | Ok solution ->
        let totalVertices = solution.PartitionS.Length + solution.PartitionT.Length
        Assert.Equal(3, totalVertices)
        // Triangle optimal cut = 2.0 (any 1-vs-2 split cuts 2 of the 3 edges)
        Assert.Equal(2.0, solution.CutValue, 10)
    | Error err -> Assert.Fail($"Ising topological backend should execute QAOA: {err}")

[<Fact>]
let ``Ising TopologicalBackend reports correct backend name`` () =
    let backend = createIsingBackend ()
    Assert.Equal("Topological Quantum Backend", backend.Name)

// =============================================================================
// STEP 0e: FIBONACCI ANYON TESTS
// =============================================================================

[<Fact>]
let ``Fibonacci TopologicalBackend accepts QuantumMaxCutSolver.solve`` () =
    // Arrange: 4-qubit path graph on Fibonacci backend
    let backend = createFibonacciBackend ()
    let problem = create4VertexPathProblem ()

    // Act: Fibonacci uses Solovay-Kitaev compilation — may be slow
    let result = QuantumMaxCutSolver.solve backend problem defaultConfig

    // Assert: execution must succeed and return the optimal cut.
    // Solovay-Kitaev only costs wall-clock time; it does not change the answer.
    match result with
    | Ok solution ->
        Assert.Equal("Topological Quantum Backend", solution.BackendName)
        // Path A--B--C--D: optimal MaxCut is {A,C} vs {B,D}, cutting all 3 edges.
        Assert.Equal(3.0, solution.CutValue, 10)
        Assert.Equal(4, solution.PartitionS.Length + solution.PartitionT.Length)
    | Error err -> Assert.Fail($"Fibonacci topological backend should execute QAOA: {err}")

[<Fact>]
let ``Fibonacci TopologicalBackend MaxCut on triangle finds feasible partition`` () =
    // Arrange
    let backend = createFibonacciBackend ()
    let problem = createTriangleProblem ()

    // Act
    let result = QuantumMaxCutSolver.solve backend problem defaultConfig

    // Assert
    match result with
    | Ok solution ->
        let totalVertices = solution.PartitionS.Length + solution.PartitionT.Length
        Assert.Equal(3, totalVertices)
        // Triangle optimal cut = 2.0 (any 1-vs-2 split cuts 2 of the 3 edges)
        Assert.Equal(2.0, solution.CutValue, 10)
    | Error err -> Assert.Fail($"Fibonacci topological backend should execute QAOA: {err}")

[<Fact>]
let ``Fibonacci TopologicalBackend reports correct backend name`` () =
    let backend = createFibonacciBackend ()
    Assert.Equal("Topological Quantum Backend", backend.Name)

// =============================================================================
// COMPARISON: LOCAL vs TOPOLOGICAL BACKENDS
// =============================================================================

[<Fact>]
let ``LocalBackend MaxCut on triangle produces optimal cut`` () =
    // Baseline: gate-based LocalBackend should find optimal cut
    let backend = createLocalBackend ()
    let problem = createTriangleProblem ()

    let config =
        { QuantumMaxCutSolver.defaultConfig with
            NumShots = 2000
        }

    let result = QuantumMaxCutSolver.solve backend problem config

    match result with
    | Ok solution ->
        Assert.Equal("Local Simulator", solution.BackendName)
        // Triangle: optimal cut = 2.0 (any 1-vs-2 split). The solver reports the
        // best of 2000 shots, so the optimum is found even though each shot is random.
        Assert.Equal(2.0, solution.CutValue, 10)
        let totalVertices = solution.PartitionS.Length + solution.PartitionT.Length
        Assert.Equal(3, totalVertices)
    | Error err -> Assert.Fail($"LocalBackend should not fail on triangle: {err}")

[<Fact; Trait("Category", "Slow")>]
let ``LocalBackend MaxCut on 4-vertex path produces optimal cut`` () =
    // Baseline: gate-based LocalBackend on 4-qubit problem
    let backend = createLocalBackend ()
    let problem = create4VertexPathProblem ()

    let config =
        { QuantumMaxCutSolver.defaultConfig with
            NumShots = 2000
        }

    let result = QuantumMaxCutSolver.solve backend problem config

    match result with
    | Ok solution ->
        Assert.Equal("Local Simulator", solution.BackendName)
        // Path A--B--C--D: optimal MaxCut is {A,C} vs {B,D}, cutting all 3 edges.
        Assert.Equal(3.0, solution.CutValue, 10)
        let totalVertices = solution.PartitionS.Length + solution.PartitionT.Length
        Assert.Equal(4, totalVertices)
    | Error err -> Assert.Fail($"LocalBackend should not fail on 4-vertex path: {err}")

// =============================================================================
// PER-SOLVER TOPOLOGICAL TESTS: EXPANSION SOLVERS ON ISING BACKEND
// =============================================================================
//
// These tests check that each expansion solver's QAOA circuit runs on the Ising
// topological backend and returns a valid solution — not merely that it does not
// crash. They use `fastConfig` (EnableOptimization = false): the Nelder-Mead loop
// of `defaultConfig` would execute ~1000 circuits, and a topological circuit
// execution is orders of magnitude costlier than a state-vector one (the fusion-tree
// simulation carries 2^n explicit terms), while adding nothing to what is verified.

[<Fact>]
let ``Ising TopologicalBackend accepts QuantumVertexCoverSolver.solve`` () =
    let backend = createIsingBackend ()

    let problem: QuantumVertexCoverSolver.Problem =
        {
            Vertices = [ { Id = "A"; Weight = 1.0 }; { Id = "B"; Weight = 1.0 } ]
            Edges = [ (0, 1) ]
        }

    let result =
        QuantumVertexCoverSolver.solveWithConfig backend problem QuantumVertexCoverSolver.fastConfig

    match result with
    | Ok solution ->
        Assert.Equal("Topological Quantum Backend", solution.BackendName)
        // The single edge (A,B) must be covered, so the cover is non-empty.
        Assert.True(solution.IsValid, "Vertex cover should be valid")
        Assert.NotEmpty(solution.CoverVertices)
    | Error err -> Assert.Fail($"VertexCover should run on the Ising backend: {err}")

[<Fact>]
let ``Ising TopologicalBackend accepts QuantumCliqueSolver.solve`` () =
    let backend = createIsingBackend ()

    let problem: QuantumCliqueSolver.Problem =
        {
            Vertices =
                [
                    { Id = "A"; Weight = 1.0 }
                    { Id = "B"; Weight = 1.0 }
                    { Id = "C"; Weight = 1.0 }
                ]
            Edges = [ (0, 1); (1, 2); (0, 2) ] // Complete K3
        }

    let result =
        QuantumCliqueSolver.solveWithConfig backend problem QuantumCliqueSolver.fastConfig

    match result with
    | Ok solution ->
        Assert.Equal("Topological Quantum Backend", solution.BackendName)
        Assert.True(solution.IsValid, "Clique should be valid")
    | Error err -> Assert.Fail($"Clique should run on the Ising backend: {err}")

[<Fact>]
let ``Ising TopologicalBackend accepts QuantumSetCoverSolver.solve`` () =
    let backend = createIsingBackend ()

    let problem: QuantumSetCoverSolver.Problem =
        {
            UniverseSize = 3
            Subsets =
                [
                    {
                        Id = "S1"
                        Elements = [ 0; 1 ]
                        Cost = 1.0
                    }
                    {
                        Id = "S2"
                        Elements = [ 1; 2 ]
                        Cost = 1.0
                    }
                ]
        }

    let result =
        QuantumSetCoverSolver.solveWithConfig backend problem QuantumSetCoverSolver.fastConfig

    match result with
    | Ok solution ->
        Assert.Equal("Topological Quantum Backend", solution.BackendName)
        Assert.True(solution.IsValid, "Set cover should be valid")
        Assert.NotEmpty(solution.SelectedSubsets)
    | Error err -> Assert.Fail($"SetCover should run on the Ising backend: {err}")

[<Fact>]
let ``Ising TopologicalBackend accepts QuantumMatchingSolver.solve`` () =
    let backend = createIsingBackend ()

    let problem: QuantumMatchingSolver.Problem =
        {
            NumVertices = 4
            Edges =
                [
                    { Source = 0; Target = 1; Weight = 1.0 }
                    { Source = 2; Target = 3; Weight = 1.0 }
                ]
        }

    let result =
        QuantumMatchingSolver.solveWithConfig backend problem QuantumMatchingSolver.fastConfig

    match result with
    | Ok solution ->
        Assert.Equal("Topological Quantum Backend", solution.BackendName)
        Assert.True(solution.IsValid, "Matching should be valid")
    | Error err -> Assert.Fail($"Matching should run on the Ising backend: {err}")

// Slow: bin packing needs n·B + B = 6 qubits for 2 items, and a 6-qubit
// fusion-tree state carries 64 explicit terms through every gate.
[<Fact; Trait("Category", "Slow")>]
let ``Ising TopologicalBackend accepts QuantumBinPackingSolver.solve`` () =
    let backend = createIsingBackend ()

    let problem: QuantumBinPackingSolver.Problem =
        {
            Items = [ { Id = "A"; Size = 3.0 }; { Id = "B"; Size = 2.0 } ]
            BinCapacity = 5.0
        }

    let result =
        QuantumBinPackingSolver.solveWithConfig backend problem QuantumBinPackingSolver.fastConfig

    match result with
    | Ok solution ->
        Assert.Equal("Topological Quantum Backend", solution.BackendName)
        Assert.True(solution.IsValid, "Bin packing should be valid")
        // Items A (3.0) and B (2.0) fit one bin of capacity 5.0.
        Assert.Equal(2, solution.Assignments.Length)
    | Error err -> Assert.Fail($"BinPacking should run on the Ising backend: {err}")

[<Fact>]
let ``Ising TopologicalBackend accepts QuantumBinaryILPSolver.solve`` () =
    let backend = createIsingBackend ()

    let problem: QuantumBinaryILPSolver.Problem =
        {
            ObjectiveCoeffs = [ 1.0; 2.0 ]
            Constraints =
                [
                    {
                        Coefficients = [ 1.0; 1.0 ]
                        Bound = 1.0
                    }
                ]
        }

    let result =
        QuantumBinaryILPSolver.solveWithConfig backend problem QuantumBinaryILPSolver.fastConfig

    match result with
    | Ok solution ->
        Assert.Equal("Topological Quantum Backend", solution.BackendName)
        Assert.Equal(2, solution.Variables.Length)
        Assert.Equal(solution.TotalConstraints, solution.ConstraintsSatisfied)
    | Error err -> Assert.Fail($"BinaryILP should run on the Ising backend: {err}")
