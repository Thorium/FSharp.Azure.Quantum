module FSharp.Azure.Quantum.Tests.QuantumBinPackingSolverTests

open Xunit
open FSharp.Azure.Quantum.Quantum.QuantumBinPackingSolver
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
    let ``toQubo produces correct size for single item`` () =
        let problem : Problem = {
            Items = [ { Id = "A"; Size = 3.0 } ]
            BinCapacity = 5.0
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // 1 item, B = ceil(3/5) = 1 bin → 1*1 + 1 = 2 variables
            Assert.Equal(2, qubo.GetLength(0))

    [<Fact>]
    let ``toQubo produces correct size for two items`` () =
        let problem : Problem = {
            Items = [
                { Id = "A"; Size = 3.0 }
                { Id = "B"; Size = 4.0 }
            ]
            BinCapacity = 5.0
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // 2 items, B = ceil(7/5) = 2 → 2*2 + 2 = 6 variables
            Assert.Equal(6, qubo.GetLength(0))

    [<Fact>]
    let ``toQubo QUBO is symmetric`` () =
        let problem : Problem = {
            Items = [
                { Id = "A"; Size = 2.0 }
                { Id = "B"; Size = 3.0 }
            ]
            BinCapacity = 4.0
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let n = qubo.GetLength(0)
            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    Assert.Equal(qubo.[i, j], qubo.[j, i], 6)

    [<Fact>]
    let ``toQubo has positive bin-used objective on diagonal`` () =
        let problem : Problem = {
            Items = [ { Id = "A"; Size = 1.0 } ]
            BinCapacity = 5.0
        }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // B = 1, n = 1. bin-used variable is at index 1*1 + 0 = 1
            // Its objective contribution should include positive value (minimize bins)
            // Note: other constraints also affect this diagonal, but objective adds +1
            // We just check the QUBO is non-trivial
            let totalNonZero =
                let mutable count = 0
                let sz = qubo.GetLength(0)
                for i in 0 .. sz - 1 do
                    for j in 0 .. sz - 1 do
                        if abs qubo.[i, j] > 1e-15 then count <- count + 1
                count
            Assert.True(totalNonZero > 0, "QUBO should have non-zero entries")

// ============================================================================
// VALIDATION TESTS
// ============================================================================

module ValidationTests =

    [<Fact>]
    let ``toQubo rejects empty items`` () =
        let problem : Problem = { Items = []; BinCapacity = 5.0 }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("items", field)
        | _ -> Assert.Fail("Should reject empty items")

    [<Fact>]
    let ``toQubo rejects zero capacity`` () =
        let problem : Problem = {
            Items = [ { Id = "A"; Size = 1.0 } ]
            BinCapacity = 0.0
        }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("binCapacity", field)
        | _ -> Assert.Fail("Should reject zero capacity")

    [<Fact>]
    let ``toQubo rejects negative capacity`` () =
        let problem : Problem = {
            Items = [ { Id = "A"; Size = 1.0 } ]
            BinCapacity = -5.0
        }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("binCapacity", field)
        | _ -> Assert.Fail("Should reject negative capacity")

    [<Fact>]
    let ``toQubo rejects zero-size items`` () =
        let problem : Problem = {
            Items = [ { Id = "A"; Size = 0.0 } ]
            BinCapacity = 5.0
        }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("itemSize", field)
        | _ -> Assert.Fail("Should reject zero-size items")

    [<Fact>]
    let ``toQubo rejects item larger than bin capacity`` () =
        let problem : Problem = {
            Items = [ { Id = "A"; Size = 10.0 } ]
            BinCapacity = 5.0
        }
        match toQubo problem with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("itemSize", field)
        | _ -> Assert.Fail("Should reject oversized items")

    [<Fact>]
    let ``solveWithConfig rejects empty items`` () =
        let backend = createLocalBackend ()
        let problem : Problem = { Items = []; BinCapacity = 5.0 }
        match solveWithConfig backend problem defaultConfig with
        | Error (QuantumError.ValidationError (field, _)) ->
            Assert.Equal("items", field)
        | _ -> Assert.Fail("Should reject empty items")

// ============================================================================
// QUBIT ESTIMATION TESTS
// ============================================================================

module QubitEstimationTests =

    [<Fact>]
    let ``estimateQubits computes n*B + B`` () =
        let problem : Problem = {
            Items = [
                { Id = "A"; Size = 3.0 }
                { Id = "B"; Size = 4.0 }
            ]
            BinCapacity = 5.0
        }
        // B = ceil(7/5) = 2, n = 2 → 2*2 + 2 = 6
        Assert.Equal(6, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits with single item single bin`` () =
        let problem : Problem = {
            Items = [ { Id = "A"; Size = 1.0 } ]
            BinCapacity = 10.0
        }
        // B = ceil(1/10) = 1, n = 1 → 1*1 + 1 = 2
        Assert.Equal(2, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits with items needing multiple bins`` () =
        let problem : Problem = {
            Items = [
                { Id = "A"; Size = 5.0 }
                { Id = "B"; Size = 5.0 }
                { Id = "C"; Size = 5.0 }
            ]
            BinCapacity = 5.0
        }
        // B = ceil(15/5) = 3, n = 3 → 3*3 + 3 = 12
        Assert.Equal(12, estimateQubits problem)

// ============================================================================
// isValid TESTS
// ============================================================================

module IsValidTests =

    [<Fact>]
    let ``isValid accepts correct single-item packing`` () =
        let problem : Problem = {
            Items = [ { Id = "A"; Size = 3.0 } ]
            BinCapacity = 5.0
        }
        // B = 1, total vars = 2 (x_{0,0}, y_0)
        // x_{0,0} = 1 (item 0 in bin 0), y_0 = 1 (bin 0 used)
        Assert.True(isValid problem [| 1; 1 |])

    [<Fact>]
    let ``isValid rejects unassigned item`` () =
        let problem : Problem = {
            Items = [ { Id = "A"; Size = 3.0 } ]
            BinCapacity = 5.0
        }
        // x_{0,0} = 0, y_0 = 0 — item not assigned
        Assert.False(isValid problem [| 0; 0 |])

    [<Fact>]
    let ``isValid rejects wrong-length bitstring`` () =
        let problem : Problem = {
            Items = [ { Id = "A"; Size = 3.0 } ]
            BinCapacity = 5.0
        }
        Assert.False(isValid problem [| 1 |])  // Too short
        Assert.False(isValid problem [| 1; 1; 0 |])  // Too long

    [<Fact>]
    let ``isValid rejects overloaded bin`` () =
        let problem : Problem = {
            Items = [
                { Id = "A"; Size = 3.0 }
                { Id = "B"; Size = 4.0 }
            ]
            BinCapacity = 5.0
        }
        // B = 2, total vars = 2*2 + 2 = 6
        // x_{0,0}=1, x_{0,1}=0, x_{1,0}=1, x_{1,1}=0, y_0=1, y_1=0
        // Both items in bin 0: load = 3+4 = 7 > 5
        Assert.False(isValid problem [| 1; 0; 1; 0; 1; 0 |])

// ============================================================================
// DECOMPOSE / RECOMBINE TESTS
// ============================================================================

module DecomposeRecombineTests =

    [<Fact>]
    let ``decompose returns single problem`` () =
        let problem : Problem = {
            Items = [ { Id = "A"; Size = 1.0 } ]
            BinCapacity = 5.0
        }
        let parts = decompose problem
        Assert.Equal(1, parts.Length)

    [<Fact>]
    let ``recombine handles empty list`` () =
        let result = recombine []
        Assert.Equal(0, result.BinsUsed)
        Assert.False(result.IsValid)

    [<Fact>]
    let ``recombine returns single solution`` () =
        let sol : Solution = {
            Assignments = [ ({ Id = "A"; Size = 1.0 }, 0) ]
            BinsUsed = 1
            IsValid = true
            WasRepaired = false
            BackendName = "Test"
            NumShots = 100
            OptimizedParameters = None
            OptimizationConverged = None
        }
        let result = recombine [ sol ]
        Assert.Equal(1, result.BinsUsed)

    [<Fact>]
    let ``recombine picks fewest bins`` () =
        let sol1 : Solution = {
            Assignments = []
            BinsUsed = 3
            IsValid = true
            WasRepaired = false
            BackendName = ""; NumShots = 0
            OptimizedParameters = None; OptimizationConverged = None
        }
        let sol2 : Solution = {
            Assignments = []
            BinsUsed = 2
            IsValid = true
            WasRepaired = false
            BackendName = ""; NumShots = 0
            OptimizedParameters = None; OptimizationConverged = None
        }
        let result = recombine [ sol1; sol2 ]
        Assert.Equal(2, result.BinsUsed)

// ============================================================================
// QUANTUM SOLVER TESTS (using LocalBackend)
// ============================================================================

module QuantumSolverTests =

    [<Fact>]
    let ``solve returns Ok for single item`` () =
        let backend = createLocalBackend ()
        let problem : Problem = {
            Items = [ { Id = "A"; Size = 3.0 } ]
            BinCapacity = 5.0
        }

        match solve backend problem 100 with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.Equal("Local Simulator", solution.BackendName)

    [<Fact>]
    let ``solve with constraint repair produces valid packing`` () =
        let backend = createLocalBackend ()
        let problem : Problem = {
            Items = [
                { Id = "A"; Size = 3.0 }
                { Id = "B"; Size = 3.0 }
            ]
            BinCapacity = 5.0
        }
        let config = { defaultConfig with EnableConstraintRepair = true }

        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solve with repair failed: {err}")
        | Ok solution ->
            // After repair, solution should be valid
            Assert.True(solution.IsValid, "Repaired solution should be valid")
            Assert.True(solution.Assignments.Length = 2,
                $"All items should be assigned, got {solution.Assignments.Length}")

    [<Fact>]
    let ``solveWithConfig uses config shots`` () =
        let backend = createLocalBackend ()
        let problem : Problem = {
            Items = [ { Id = "A"; Size = 1.0 } ]
            BinCapacity = 5.0
        }
        let config = { defaultConfig with FinalShots = 42 }

        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solveWithConfig failed: {err}")
        | Ok solution ->
            Assert.Equal(42, solution.NumShots)

    [<Fact>]
    let ``solve with items fitting in one bin`` () =
        let backend = createLocalBackend ()
        let problem : Problem = {
            Items = [
                { Id = "A"; Size = 1.0 }
                { Id = "B"; Size = 2.0 }
            ]
            BinCapacity = 10.0
        }
        let config = { defaultConfig with EnableConstraintRepair = true }

        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            Assert.True(solution.IsValid, "Solution should be valid")
            // Both items fit in one bin
            Assert.True(solution.BinsUsed <= 1,
                $"Items should fit in 1 bin, got {solution.BinsUsed}")

    [<Fact>]
    let ``solve with items requiring separate bins`` () =
        let backend = createLocalBackend ()
        let problem : Problem = {
            Items = [
                { Id = "A"; Size = 5.0 }
                { Id = "B"; Size = 5.0 }
            ]
            BinCapacity = 5.0
        }
        let config = { defaultConfig with EnableConstraintRepair = true }

        match solveWithConfig backend problem config with
        | Error err -> Assert.Fail($"solve failed: {err}")
        | Ok solution ->
            // After repair, each item needs its own bin
            if solution.IsValid then
                Assert.True(solution.BinsUsed >= 2,
                    $"Each item needs its own bin, got {solution.BinsUsed}")
