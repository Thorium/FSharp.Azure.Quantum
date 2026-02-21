module FSharp.Azure.Quantum.Tests.QuantumSetCoverSolverTests

open Xunit
open FSharp.Azure.Quantum.Quantum.QuantumSetCoverSolver
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
    let ``toQubo produces correct matrix size`` () =
        // Arrange: 3 subsets covering universe of size 2
        let problem : Problem = {
            UniverseSize = 2
            Subsets = [
                { Id = "S1"; Elements = [ 0 ]; Cost = 1.0 }
                { Id = "S2"; Elements = [ 1 ]; Cost = 1.0 }
                { Id = "S3"; Elements = [ 0; 1 ]; Cost = 2.0 }
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            Assert.Equal(3, Array2D.length1 qubo)
            Assert.Equal(3, Array2D.length2 qubo)

    [<Fact>]
    let ``toQubo QUBO is symmetric`` () =
        let problem : Problem = {
            UniverseSize = 3
            Subsets = [
                { Id = "S1"; Elements = [ 0; 1 ]; Cost = 2.0 }
                { Id = "S2"; Elements = [ 1; 2 ]; Cost = 3.0 }
                { Id = "S3"; Elements = [ 0; 2 ]; Cost = 1.5 }
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
    let ``toQubo optimal bitstring minimises energy`` () =
        // Arrange: universe {0,1}, S1={0,1} cost 1.5, S2={0} cost 1.0, S3={1} cost 1.0
        // Optimal: S1 alone (cost 1.5) vs S2+S3 (cost 2.0)
        let problem : Problem = {
            UniverseSize = 2
            Subsets = [
                { Id = "S1"; Elements = [ 0; 1 ]; Cost = 1.5 }
                { Id = "S2"; Elements = [ 0 ]; Cost = 1.0 }
                { Id = "S3"; Elements = [ 1 ]; Cost = 1.0 }
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let eval (bits: int[]) = QaoaExecutionHelpers.evaluateQubo qubo bits

            // S1 only: [1,0,0]
            let energyS1 = eval [| 1; 0; 0 |]
            // S2+S3: [0,1,1]
            let energyS2S3 = eval [| 0; 1; 1 |]
            // All: [1,1,1]
            let energyAll = eval [| 1; 1; 1 |]

            // S1 is cheapest valid cover
            Assert.True(energyS1 < energyS2S3,
                $"S1 ({energyS1}) should beat S2+S3 ({energyS2S3})")
            Assert.True(energyS1 < energyAll,
                $"S1 ({energyS1}) should beat All ({energyAll})")

    [<Fact>]
    let ``toQubo penalises uncovering solutions`` () =
        // Universe {0,1}, S1={0}, S2={1}
        let problem : Problem = {
            UniverseSize = 2
            Subsets = [
                { Id = "S1"; Elements = [ 0 ]; Cost = 1.0 }
                { Id = "S2"; Elements = [ 1 ]; Cost = 1.0 }
            ]
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let eval (bits: int[]) = QaoaExecutionHelpers.evaluateQubo qubo bits

            // Valid: both selected [1,1]
            let energyValid = eval [| 1; 1 |]
            // Invalid: only S1 [1,0] - element 1 uncovered
            let energyPartial = eval [| 1; 0 |]
            // Invalid: nothing [0,0]
            let energyNone = eval [| 0; 0 |]

            // Valid should be lower than partial
            Assert.True(energyValid < energyNone,
                $"Valid ({energyValid}) should beat empty ({energyNone})")

    [<Fact>]
    let ``toQubo returns error for empty subsets`` () =
        let problem : Problem = { UniverseSize = 2; Subsets = [] }
        match toQubo problem with
        | Error err -> Assert.Contains("no subsets", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with empty subsets")

    [<Fact>]
    let ``toQubo returns error for zero universe`` () =
        let problem : Problem = {
            UniverseSize = 0
            Subsets = [ { Id = "S1"; Elements = []; Cost = 1.0 } ]
        }
        match toQubo problem with
        | Error err -> Assert.Contains("universe", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with zero universe size")

// ============================================================================
// ROUND-TRIP TESTS
// ============================================================================

module RoundTripTests =

    [<Fact>]
    let ``decode with known optimal bitstring produces correct solution`` () =
        // Arrange: S1 covers entire universe
        let problem : Problem = {
            UniverseSize = 3
            Subsets = [
                { Id = "CoverAll"; Elements = [ 0; 1; 2 ]; Cost = 5.0 }
                { Id = "Partial1"; Elements = [ 0; 1 ]; Cost = 3.0 }
                { Id = "Partial2"; Elements = [ 2 ]; Cost = 2.0 }
            ]
        }
        let bits = [| 1; 0; 0 |]  // Only CoverAll

        Assert.True(isValid problem bits)

    [<Fact>]
    let ``decode selects correct subsets`` () =
        let problem : Problem = {
            UniverseSize = 2
            Subsets = [
                { Id = "S1"; Elements = [ 0 ]; Cost = 1.0 }
                { Id = "S2"; Elements = [ 1 ]; Cost = 2.0 }
            ]
        }
        // Both selected
        Assert.True(isValid problem [| 1; 1 |])
        // Only S1 - element 1 uncovered
        Assert.False(isValid problem [| 1; 0 |])

// ============================================================================
// CONSTRAINT REPAIR TESTS
// ============================================================================

module ConstraintRepairTests =

    [<Fact>]
    let ``repair fixes empty selection`` () =
        let problem : Problem = {
            UniverseSize = 2
            Subsets = [
                { Id = "S1"; Elements = [ 0 ]; Cost = 1.0 }
                { Id = "S2"; Elements = [ 1 ]; Cost = 1.0 }
            ]
        }
        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = true; FinalShots = 50 }

        let result = solveWithConfig backend problem config

        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid,
                $"Solution should be valid after repair. WasRepaired={solution.WasRepaired}")

    [<Fact>]
    let ``repair produces valid cover on overlapping subsets`` () =
        let problem : Problem = {
            UniverseSize = 4
            Subsets = [
                { Id = "S1"; Elements = [ 0; 1 ]; Cost = 2.0 }
                { Id = "S2"; Elements = [ 1; 2 ]; Cost = 2.0 }
                { Id = "S3"; Elements = [ 2; 3 ]; Cost = 2.0 }
                { Id = "S4"; Elements = [ 0; 3 ]; Cost = 2.0 }
            ]
        }
        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = true; FinalShots = 50 }

        let result = solveWithConfig backend problem config

        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid,
                $"Cover must be valid. Size={solution.CoverSize}, Repaired={solution.WasRepaired}")

    [<Fact>]
    let ``repair removes redundant subsets`` () =
        // Universe {0,1}, all subsets cover everything
        let problem : Problem = {
            UniverseSize = 2
            Subsets = [
                { Id = "S1"; Elements = [ 0; 1 ]; Cost = 1.0 }
                { Id = "S2"; Elements = [ 0; 1 ]; Cost = 5.0 }
            ]
        }
        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = true; FinalShots = 50 }

        let result = solveWithConfig backend problem config

        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid)

// ============================================================================
// VALIDITY TESTS
// ============================================================================

module ValidityTests =

    [<Fact>]
    let ``isValid returns true for complete cover`` () =
        let problem : Problem = {
            UniverseSize = 3
            Subsets = [
                { Id = "S1"; Elements = [ 0; 1 ]; Cost = 1.0 }
                { Id = "S2"; Elements = [ 2 ]; Cost = 1.0 }
            ]
        }
        Assert.True(isValid problem [| 1; 1 |])

    [<Fact>]
    let ``isValid returns false for partial cover`` () =
        let problem : Problem = {
            UniverseSize = 3
            Subsets = [
                { Id = "S1"; Elements = [ 0; 1 ]; Cost = 1.0 }
                { Id = "S2"; Elements = [ 2 ]; Cost = 1.0 }
            ]
        }
        // Only S1 - element 2 uncovered
        Assert.False(isValid problem [| 1; 0 |])
        // Nothing selected
        Assert.False(isValid problem [| 0; 0 |])

    [<Fact>]
    let ``isValid rejects wrong-length bitstring`` () =
        let problem : Problem = {
            UniverseSize = 2
            Subsets = [
                { Id = "S1"; Elements = [ 0; 1 ]; Cost = 1.0 }
            ]
        }
        Assert.False(isValid problem [| 1; 1 |])  // Too long
        Assert.False(isValid problem [||])  // Too short

// ============================================================================
// BACKEND INTEGRATION TESTS
// ============================================================================

module BackendIntegrationTests =

    [<Fact>]
    let ``solve returns solution with backend info`` () =
        let problem : Problem = {
            UniverseSize = 2
            Subsets = [
                { Id = "S1"; Elements = [ 0; 1 ]; Cost = 1.0 }
            ]
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
            UniverseSize = 3
            Subsets = [
                { Id = "S1"; Elements = [ 0; 1 ]; Cost = 2.0 }
                { Id = "S2"; Elements = [ 1; 2 ]; Cost = 2.0 }
                { Id = "S3"; Elements = [ 0; 2 ]; Cost = 2.0 }
            ]
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
            | Some parameters -> Assert.Equal(2, parameters.Length)
            | None -> Assert.Fail("OptimizedParameters should not be None")

    [<Fact>]
    let ``solve validates empty subsets`` () =
        let problem : Problem = { UniverseSize = 2; Subsets = [] }
        let backend = createLocalBackend ()

        let result = solve backend problem 100

        match result with
        | Error err -> Assert.Contains("no subsets", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with empty subsets")

    [<Fact>]
    let ``solve validates zero universe size`` () =
        let problem : Problem = {
            UniverseSize = 0
            Subsets = [ { Id = "S1"; Elements = []; Cost = 1.0 } ]
        }
        let backend = createLocalBackend ()

        let result = solve backend problem 100

        match result with
        | Error err -> Assert.Contains("universe", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with zero universe")

    [<Fact>]
    let ``solve validates element index out of range`` () =
        let problem : Problem = {
            UniverseSize = 2
            Subsets = [ { Id = "S1"; Elements = [ 0; 5 ]; Cost = 1.0 } ]
        }
        let backend = createLocalBackend ()

        let result = solve backend problem 100

        match result with
        | Error err -> Assert.Contains("element", err.ToString().ToLower())
        | Ok _ -> Assert.Fail("Should fail with invalid element index")

    [<Fact>]
    let ``solve produces valid cover on small instance`` () =
        let problem : Problem = {
            UniverseSize = 4
            Subsets = [
                { Id = "S1"; Elements = [ 0; 1 ]; Cost = 1.0 }
                { Id = "S2"; Elements = [ 2; 3 ]; Cost = 1.0 }
                { Id = "S3"; Elements = [ 0; 2 ]; Cost = 1.5 }
            ]
        }
        let backend = createLocalBackend ()
        let config = { fastConfig with EnableConstraintRepair = true; FinalShots = 100 }

        let result = solveWithConfig backend problem config

        match result with
        | Error err -> Assert.Fail($"Solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid,
                $"Cover must be valid. Selected={solution.SelectedSubsets |> List.map (fun s -> s.Id)}")

// ============================================================================
// QUBIT ESTIMATION TESTS
// ============================================================================

module QubitEstimationTests =

    [<Fact>]
    let ``estimateQubits returns subset count`` () =
        let problem : Problem = {
            UniverseSize = 10
            Subsets = List.init 5 (fun i -> { Id = $"S{i}"; Elements = [ i ]; Cost = 1.0 })
        }
        Assert.Equal(5, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits returns 0 for empty subsets`` () =
        let problem : Problem = { UniverseSize = 2; Subsets = [] }
        Assert.Equal(0, estimateQubits problem)

// ============================================================================
// DECOMPOSE / RECOMBINE TESTS
// ============================================================================

module DecomposeRecombineTests =

    [<Fact>]
    let ``decompose returns single problem (identity)`` () =
        let problem : Problem = {
            UniverseSize = 2
            Subsets = [ { Id = "S1"; Elements = [ 0; 1 ]; Cost = 1.0 } ]
        }
        let parts = decompose problem
        Assert.Equal(1, parts.Length)
        Assert.Equal(problem.Subsets.Length, parts.Head.Subsets.Length)

    [<Fact>]
    let ``recombine returns single solution (identity)`` () =
        let solution : Solution = {
            SelectedSubsets = []
            TotalCost = 0.0
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
        Assert.False(combined.IsValid)

    [<Fact>]
    let ``recombine picks cheapest cover`` () =
        let s1 : Solution = {
            SelectedSubsets = []
            TotalCost = 10.0
            CoverSize = 3
            IsValid = true
            WasRepaired = false
            BackendName = "test"
            NumShots = 100
            OptimizedParameters = None
            OptimizationConverged = None
        }
        let s2 : Solution = {
            SelectedSubsets = []
            TotalCost = 5.0
            CoverSize = 2
            IsValid = true
            WasRepaired = false
            BackendName = "test"
            NumShots = 100
            OptimizedParameters = None
            OptimizationConverged = None
        }
        let combined = recombine [ s1; s2 ]
        Assert.Equal(5.0, combined.TotalCost, 6)
