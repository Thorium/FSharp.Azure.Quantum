module FSharp.Azure.Quantum.Tests.ProblemDecompositionTests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.ProblemDecomposition
open FSharp.Azure.Quantum.Backends

// ============================================================================
// HELPER TYPES
// ============================================================================

/// A mock backend that does NOT implement IQubitLimitedBackend.
type private UnlimitedBackend() =
    interface IQuantumBackend with
        member _.Name = "UnlimitedMock"
        member _.NativeStateType = QuantumStateType.GateBased
        member _.SupportsOperation (_op: QuantumOperation) = true
        member _.ApplyOperation (_op: QuantumOperation) (state: QuantumState) = Ok state
        member _.ExecuteToState (_circuit) =
            Ok (QuantumState.StateVector (FSharp.Azure.Quantum.LocalSimulator.StateVector.init 1))
        member _.InitializeState (n: int) =
            Ok (QuantumState.StateVector (FSharp.Azure.Quantum.LocalSimulator.StateVector.init n))

/// A mock backend that implements IQubitLimitedBackend with a configurable limit.
type private LimitedBackend(maxQubits: int) =
    interface IQuantumBackend with
        member _.Name = "LimitedMock"
        member _.NativeStateType = QuantumStateType.GateBased
        member _.SupportsOperation (_op: QuantumOperation) = true
        member _.ApplyOperation (_op: QuantumOperation) (state: QuantumState) = Ok state
        member _.ExecuteToState (_circuit) =
            Ok (QuantumState.StateVector (FSharp.Azure.Quantum.LocalSimulator.StateVector.init 1))
        member _.InitializeState (n: int) =
            Ok (QuantumState.StateVector (FSharp.Azure.Quantum.LocalSimulator.StateVector.init n))
    interface IQubitLimitedBackend with
        member _.MaxQubits = Some maxQubits

// ============================================================================
// connectedComponents TESTS
// ============================================================================

module ConnectedComponentsTests =

    [<Fact>]
    let ``connectedComponents with zero vertices returns empty list`` () =
        let result = connectedComponents 0 []
        Assert.Empty(result)

    [<Fact>]
    let ``connectedComponents with isolated vertices returns singletons`` () =
        let result = connectedComponents 3 []
        Assert.Equal(3, result.Length)
        // Each component should contain exactly 1 vertex
        for comp in result do
            Assert.Equal(1, comp.Length)
        // All vertices 0..2 should appear
        let allVertices = result |> List.concat |> List.sort
        Assert.Equal<int list>([0; 1; 2], allVertices)

    [<Fact>]
    let ``connectedComponents with single edge connects two vertices`` () =
        let result = connectedComponents 3 [(0, 1)]
        // Vertex 0 and 1 connected, vertex 2 isolated → 2 components
        Assert.Equal(2, result.Length)
        let sorted = result |> List.sortBy List.length |> List.rev
        Assert.Equal(2, sorted.[0].Length)  // {0, 1}
        Assert.Equal(1, sorted.[1].Length)  // {2}

    [<Fact>]
    let ``connectedComponents with fully connected graph returns one component`` () =
        let edges = [(0, 1); (1, 2); (0, 2)]
        let result = connectedComponents 3 edges
        Assert.Equal(1, result.Length)
        Assert.Equal(3, result.[0].Length)

    [<Fact>]
    let ``connectedComponents with two disconnected cliques`` () =
        // Two triangles: {0,1,2} and {3,4,5}
        let edges = [(0, 1); (1, 2); (0, 2); (3, 4); (4, 5); (3, 5)]
        let result = connectedComponents 6 edges
        Assert.Equal(2, result.Length)
        let sizes = result |> List.map List.length |> List.sort
        Assert.Equal<int list>([3; 3], sizes)

    [<Fact>]
    let ``connectedComponents with chain graph returns one component`` () =
        // 0--1--2--3--4 (path graph)
        let edges = [(0, 1); (1, 2); (2, 3); (3, 4)]
        let result = connectedComponents 5 edges
        Assert.Equal(1, result.Length)
        Assert.Equal(5, result.[0].Length)

    [<Fact>]
    let ``connectedComponents ignores out-of-range edges gracefully`` () =
        // Edge (5,6) is out of range for 3 vertices — should be silently ignored
        let edges = [(0, 1); (5, 6)]
        let result = connectedComponents 3 edges
        // 0-1 connected, 2 isolated → 2 components
        Assert.Equal(2, result.Length)

    [<Fact>]
    let ``connectedComponents with self-loops does not create extra components`` () =
        let edges = [(0, 0); (1, 1); (0, 1)]
        let result = connectedComponents 2 edges
        Assert.Equal(1, result.Length)
        Assert.Equal(2, result.[0].Length)

// ============================================================================
// partitionByComponents TESTS
// ============================================================================

module PartitionByComponentsTests =

    [<Fact>]
    let ``partitionByComponents with isolated vertices returns singleton components`` () =
        let result = partitionByComponents 3 []
        Assert.Equal(3, result.Length)
        for (vertices, edges) in result do
            Assert.Equal(1, vertices.Length)
            Assert.Empty(edges)

    [<Fact>]
    let ``partitionByComponents re-indexes edges to local indices`` () =
        // Graph: 0--1  2--3
        let edges = [(0, 1); (2, 3)]
        let result = partitionByComponents 4 edges
        Assert.Equal(2, result.Length)

        // Each component should have one edge with local indices (0, 1)
        for (vertices, localEdges) in result do
            Assert.Equal(2, vertices.Length)
            Assert.Equal(1, localEdges.Length)
            let (a, b) = localEdges.[0]
            Assert.Equal(0, a)
            Assert.Equal(1, b)

    [<Fact>]
    let ``partitionByComponents preserves global vertex indices`` () =
        // Graph: 0--1  2 (isolated)
        let edges = [(0, 1)]
        let result = partitionByComponents 3 edges
        let allGlobalVertices =
            result |> List.collect fst |> List.sort
        Assert.Equal<int list>([0; 1; 2], allGlobalVertices)

    [<Fact>]
    let ``partitionByComponents with single component returns one partition`` () =
        let edges = [(0, 1); (1, 2)]
        let result = partitionByComponents 3 edges
        Assert.Equal(1, result.Length)
        let (vertices, localEdges) = result.[0]
        Assert.Equal(3, vertices.Length)
        Assert.Equal(2, localEdges.Length)

    [<Fact>]
    let ``partitionByComponents with triangle has correct local edges`` () =
        let edges = [(0, 1); (1, 2); (0, 2)]
        let result = partitionByComponents 3 edges
        Assert.Equal(1, result.Length)
        let (_, localEdges) = result.[0]
        Assert.Equal(3, localEdges.Length)

// ============================================================================
// canDecomposeWithinLimit TESTS
// ============================================================================

module CanDecomposeWithinLimitTests =

    [<Fact>]
    let ``canDecomposeWithinLimit returns true for empty graph`` () =
        let result = canDecomposeWithinLimit 0 [] 10 1
        Assert.True(result)

    [<Fact>]
    let ``canDecomposeWithinLimit returns true when all components fit`` () =
        // Two isolated components: {0,1} and {2,3}, each size 2
        let edges = [(0, 1); (2, 3)]
        let result = canDecomposeWithinLimit 4 edges 2 1  // limit 2 qubits, 1 qubit/vertex
        Assert.True(result)

    [<Fact>]
    let ``canDecomposeWithinLimit returns false when a component exceeds limit`` () =
        // One connected component of size 3
        let edges = [(0, 1); (1, 2)]
        let result = canDecomposeWithinLimit 3 edges 2 1  // limit 2, but component has 3
        Assert.False(result)

    [<Fact>]
    let ``canDecomposeWithinLimit accounts for qubitsPerVertex`` () =
        // Two isolated vertices, each needing 3 qubits → 3 qubits per component
        let result = canDecomposeWithinLimit 2 [] 3 3  // limit 3, 3 qubits/vertex, each comp has 1 vertex * 3 = 3
        Assert.True(result)

    [<Fact>]
    let ``canDecomposeWithinLimit returns false with high qubitsPerVertex`` () =
        // Single isolated vertex needing 5 qubits, limit 4
        let result = canDecomposeWithinLimit 1 [] 4 5
        Assert.False(result)

    [<Fact>]
    let ``canDecomposeWithinLimit with disconnected graph that fits`` () =
        // 6 vertices: three pairs {0,1}, {2,3}, {4,5} → each component size 2
        let edges = [(0, 1); (2, 3); (4, 5)]
        let result = canDecomposeWithinLimit 6 edges 4 2  // limit 4, 2 qubits/vertex → each comp needs 4
        Assert.True(result)

    [<Fact>]
    let ``canDecomposeWithinLimit returns false when one large component exists`` () =
        // 6 vertices: one big chain {0,1,2,3,4,5}
        let edges = [(0, 1); (1, 2); (2, 3); (3, 4); (4, 5)]
        let result = canDecomposeWithinLimit 6 edges 4 1  // limit 4, but chain has 6 vertices
        Assert.False(result)

// ============================================================================
// plan TESTS
// ============================================================================

module PlanTests =

    let private simpleEstimate (n: int) = n
    let private simpleDecompose (n: int) = [n / 2; n - n / 2]

    [<Fact>]
    let ``plan with NoDecomposition always returns RunDirect`` () =
        let backend = LimitedBackend(4) :> IQuantumBackend
        let result = plan NoDecomposition backend simpleEstimate simpleDecompose 10
        match result with
        | RunDirect p -> Assert.Equal(10, p)
        | RunDecomposed _ -> Assert.Fail("Expected RunDirect")

    [<Fact>]
    let ``plan with FixedPartition returns RunDirect when within limit`` () =
        let backend = UnlimitedBackend() :> IQuantumBackend
        let result = plan (FixedPartition 10) backend simpleEstimate simpleDecompose 8
        match result with
        | RunDirect p -> Assert.Equal(8, p)
        | RunDecomposed _ -> Assert.Fail("Expected RunDirect")

    [<Fact>]
    let ``plan with FixedPartition returns RunDecomposed when exceeding limit`` () =
        let backend = UnlimitedBackend() :> IQuantumBackend
        let result = plan (FixedPartition 5) backend simpleEstimate simpleDecompose 10
        match result with
        | RunDirect _ -> Assert.Fail("Expected RunDecomposed")
        | RunDecomposed subs ->
            Assert.Equal(2, subs.Length)
            Assert.Equal(5, subs.[0])
            Assert.Equal(5, subs.[1])

    [<Fact>]
    let ``plan with FixedPartition returns RunDirect when decompose returns single item`` () =
        let backend = UnlimitedBackend() :> IQuantumBackend
        // Decompose function returns single item (can't decompose)
        let noDecompose x = [x]
        let result = plan (FixedPartition 5) backend simpleEstimate noDecompose 10
        match result with
        | RunDirect p -> Assert.Equal(10, p)
        | RunDecomposed _ -> Assert.Fail("Expected RunDirect when decompose returns single item")

    [<Fact>]
    let ``plan with AdaptiveToBackend uses backend MaxQubits`` () =
        let backend = LimitedBackend(5) :> IQuantumBackend
        let result = plan AdaptiveToBackend backend simpleEstimate simpleDecompose 10
        match result with
        | RunDirect _ -> Assert.Fail("Expected RunDecomposed")
        | RunDecomposed subs -> Assert.Equal(2, subs.Length)

    [<Fact>]
    let ``plan with AdaptiveToBackend returns RunDirect when within limit`` () =
        let backend = LimitedBackend(20) :> IQuantumBackend
        let result = plan AdaptiveToBackend backend simpleEstimate simpleDecompose 10
        match result with
        | RunDirect p -> Assert.Equal(10, p)
        | RunDecomposed _ -> Assert.Fail("Expected RunDirect when within limit")

    [<Fact>]
    let ``plan with AdaptiveToBackend and unlimited backend returns RunDirect`` () =
        let backend = UnlimitedBackend() :> IQuantumBackend
        let result = plan AdaptiveToBackend backend simpleEstimate simpleDecompose 100
        match result with
        | RunDirect p -> Assert.Equal(100, p)
        | RunDecomposed _ -> Assert.Fail("Expected RunDirect for unlimited backend")

    [<Fact>]
    let ``plan with FixedPartition at exact boundary returns RunDirect`` () =
        let backend = UnlimitedBackend() :> IQuantumBackend
        let result = plan (FixedPartition 10) backend simpleEstimate simpleDecompose 10
        match result with
        | RunDirect p -> Assert.Equal(10, p)
        | RunDecomposed _ -> Assert.Fail("Expected RunDirect at exact boundary")

// ============================================================================
// execute TESTS
// ============================================================================

module ExecuteTests =

    [<Fact>]
    let ``execute RunDirect calls solveFn directly`` () =
        let solveFn (n: int) = Ok (n * 2)
        let recombineFn (xs: int list) = xs |> List.sum
        let plan = RunDirect 5
        let result = execute solveFn recombineFn plan
        match result with
        | Ok solution -> Assert.Equal(10, solution)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``execute RunDecomposed solves sub-problems and recombines`` () =
        let solveFn (n: int) = Ok (n * 10)
        let recombineFn (xs: int list) = xs |> List.sum
        let plan = RunDecomposed [3; 4; 5]
        let result = execute solveFn recombineFn plan
        match result with
        | Ok solution -> Assert.Equal(120, solution)  // 30 + 40 + 50
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``execute RunDecomposed preserves order`` () =
        let solveFn (s: string) = Ok (s.ToUpper())
        let recombineFn (xs: string list) = xs |> String.concat ","
        let plan = RunDecomposed ["a"; "b"; "c"]
        let result = execute solveFn recombineFn plan
        match result with
        | Ok solution -> Assert.Equal("A,B,C", solution)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``execute RunDecomposed short-circuits on first error`` () =
        let mutable callCount = 0
        let solveFn (n: int) =
            callCount <- callCount + 1
            if n = 2 then Error (QuantumError.OperationError ("test", "fail on 2"))
            else Ok (n * 10)
        let recombineFn (xs: int list) = xs |> List.sum
        let plan = RunDecomposed [1; 2; 3]
        let result = execute solveFn recombineFn plan
        match result with
        | Error _ ->
            // After the error on item 2, item 3 should not be solved
            Assert.Equal(2, callCount)
        | Ok _ -> Assert.Fail("Expected Error but got Ok")

    [<Fact>]
    let ``execute RunDirect propagates solver error`` () =
        let solveFn (_: int) = Error (QuantumError.OperationError ("test", "always fails"))
        let recombineFn (xs: int list) = xs |> List.sum
        let plan = RunDirect 5
        let result = execute solveFn recombineFn plan
        match result with
        | Error (QuantumError.OperationError (op, _)) -> Assert.Equal("test", op)
        | Error _ -> Assert.Fail("Expected OperationError")
        | Ok _ -> Assert.Fail("Expected Error but got Ok")

    [<Fact>]
    let ``execute RunDecomposed with single sub-problem works`` () =
        let solveFn (n: int) = Ok (n + 1)
        let recombineFn (xs: int list) = xs |> List.sum
        let plan = RunDecomposed [7]
        let result = execute solveFn recombineFn plan
        match result with
        | Ok solution -> Assert.Equal(8, solution)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``execute RunDecomposed with empty sub-problems calls recombine with empty list`` () =
        let solveFn (_: int) = Ok 0
        let recombineFn (xs: int list) = xs.Length  // returns count
        let plan : DecompositionPlan<int> = RunDecomposed []
        let result = execute solveFn recombineFn plan
        match result with
        | Ok solution -> Assert.Equal(0, solution)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")

// ============================================================================
// solveWithDecomposition TESTS
// ============================================================================

module SolveWithDecompositionTests =

    [<Fact>]
    let ``solveWithDecomposition runs directly when backend is unlimited`` () =
        let backend = UnlimitedBackend() :> IQuantumBackend
        let solveFn (n: int) = Ok (n * 2)
        let decomposeFn (n: int) = [n / 2; n - n / 2]
        let recombineFn (xs: int list) = xs |> List.sum
        let estimateQubits (n: int) = n

        let result = solveWithDecomposition backend 10 estimateQubits decomposeFn recombineFn solveFn
        match result with
        | Ok solution -> Assert.Equal(20, solution)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``solveWithDecomposition decomposes when problem exceeds backend limit`` () =
        let backend = LimitedBackend(5) :> IQuantumBackend
        let solveFn (n: int) = Ok (n * 2)
        let decomposeFn (n: int) = [n / 2; n - n / 2]
        let recombineFn (xs: int list) = xs |> List.sum
        let estimateQubits (n: int) = n

        // 10 qubits > 5 limit → decompose into [5; 5] → solve each → 10 + 10 = 20
        let result = solveWithDecomposition backend 10 estimateQubits decomposeFn recombineFn solveFn
        match result with
        | Ok solution -> Assert.Equal(20, solution)
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``solveWithDecomposition runs directly when within backend limit`` () =
        let backend = LimitedBackend(20) :> IQuantumBackend
        let mutable wasDecomposed = false
        let solveFn (n: int) = Ok (n * 2)
        let decomposeFn (n: int) =
            wasDecomposed <- true
            [n / 2; n - n / 2]
        let recombineFn (xs: int list) = xs |> List.sum
        let estimateQubits (n: int) = n

        let result = solveWithDecomposition backend 10 estimateQubits decomposeFn recombineFn solveFn
        match result with
        | Ok solution ->
            Assert.Equal(20, solution)
            Assert.False(wasDecomposed, "Should not decompose when within limit")
        | Error err -> Assert.Fail($"Expected Ok but got Error: {err}")

    [<Fact>]
    let ``solveWithDecomposition propagates error from solver`` () =
        let backend = UnlimitedBackend() :> IQuantumBackend
        let solveFn (_: int) = Error (QuantumError.OperationError ("solver", "test error"))
        let decomposeFn (n: int) = [n]
        let recombineFn (xs: int list) = xs |> List.sum
        let estimateQubits (n: int) = n

        let result = solveWithDecomposition backend 5 estimateQubits decomposeFn recombineFn solveFn
        match result with
        | Error _ -> ()  // expected
        | Ok _ -> Assert.Fail("Expected Error but got Ok")

    [<Fact>]
    let ``solveWithDecomposition with LocalBackend respects 20 qubit limit`` () =
        // LocalBackend implements IQubitLimitedBackend with MaxQubits = Some 20
        let backend = LocalBackend.LocalBackend() :> IQuantumBackend
        let maxQubits = UnifiedBackend.getMaxQubits backend
        Assert.Equal(Some 20, maxQubits)

        let mutable wasDecomposed = false
        let solveFn (n: int) = Ok n
        let decomposeFn (n: int) =
            wasDecomposed <- true
            [n / 2; n - n / 2]
        let recombineFn (xs: int list) = xs |> List.sum
        let estimateQubits (n: int) = n

        // 25 qubits > 20 limit → should decompose
        let _result = solveWithDecomposition backend 25 estimateQubits decomposeFn recombineFn solveFn
        Assert.True(wasDecomposed, "Should decompose when problem exceeds LocalBackend limit")
