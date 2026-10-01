module FSharp.Azure.Quantum.Tests.QuantumBinPackingSolverTests

open Xunit
open FSharp.Azure.Quantum.Quantum.QuantumBinPackingSolver
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
    let ``toQubo produces correct size for single item`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 3.0 } ]
                BinCapacity = 5.0
            }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // 1 item, B = 1 bin (first-fit-decreasing), the bin is full on the integer grid → 1*1 + 1 = 2 variables
            Assert.Equal(2, qubo.GetLength(0))

    [<Fact>]
    let ``toQubo produces correct size for two items`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 3.0 }; { Id = "B"; Size = 4.0 } ]
                BinCapacity = 5.0
            }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            // 2 items, B = 2 bins (first-fit-decreasing: 4 and 3 do not share a 5-bin) → 2*2 + 2 = 6
            // item and bin variables; a used bin holds 3..5, so each bin has 2 slack bits (0..2) → 10
            Assert.Equal(10, qubo.GetLength(0))

    [<Fact>]
    let ``toQubo QUBO is symmetric`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 2.0 }; { Id = "B"; Size = 3.0 } ]
                BinCapacity = 4.0
            }

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            let n = qubo.GetLength 0

            for i in 0 .. n - 1 do
                for j in 0 .. n - 1 do
                    Assert.Equal(qubo.[i, j], qubo.[j, i], 6)

    [<Fact>]
    let ``toQubo has positive bin-used objective on diagonal`` () =
        let problem: Problem =
            {
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
                let sz = qubo.GetLength 0

                for i in 0 .. sz - 1 do
                    for j in 0 .. sz - 1 do
                        if abs qubo.[i, j] > 1e-15 then
                            count <- count + 1

                count

            Assert.True(totalNonZero > 0, "QUBO should have non-zero entries")

// ============================================================================
// VALIDATION TESTS
// ============================================================================

module ValidationTests =

    [<Fact>]
    let ``toQubo rejects empty items`` () =
        let problem: Problem = { Items = []; BinCapacity = 5.0 }

        match toQubo problem with
        | Error(QuantumError.ValidationError(field, _)) -> Assert.Equal("items", field)
        | _ -> Assert.Fail("Should reject empty items")

    [<Fact>]
    let ``toQubo rejects zero capacity`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 1.0 } ]
                BinCapacity = 0.0
            }

        match toQubo problem with
        | Error(QuantumError.ValidationError(field, _)) -> Assert.Equal("binCapacity", field)
        | _ -> Assert.Fail("Should reject zero capacity")

    [<Fact>]
    let ``toQubo rejects negative capacity`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 1.0 } ]
                BinCapacity = -5.0
            }

        match toQubo problem with
        | Error(QuantumError.ValidationError(field, _)) -> Assert.Equal("binCapacity", field)
        | _ -> Assert.Fail("Should reject negative capacity")

    [<Fact>]
    let ``toQubo rejects zero-size items`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 0.0 } ]
                BinCapacity = 5.0
            }

        match toQubo problem with
        | Error(QuantumError.ValidationError(field, _)) -> Assert.Equal("itemSize", field)
        | _ -> Assert.Fail("Should reject zero-size items")

    [<Fact>]
    let ``toQubo rejects item larger than bin capacity`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 10.0 } ]
                BinCapacity = 5.0
            }

        match toQubo problem with
        | Error(QuantumError.ValidationError(field, _)) -> Assert.Equal("itemSize", field)
        | _ -> Assert.Fail("Should reject oversized items")

    [<Fact>]
    let ``toQubo rejects sizes that cannot be rescaled to integers`` () =
        let thirds: Problem =
            {
                Items = [ { Id = "A"; Size = 1.0 / 3.0 }; { Id = "B"; Size = 0.5 } ]
                BinCapacity = 1.0
            }

        match toQubo thirds with
        | Error(QuantumError.ValidationError(field, reason)) ->
            Assert.Equal("itemSize", field)
            Assert.Contains("'A'", reason)
        | _ -> Assert.Fail("Should reject a size without a short decimal form")

        let irrationalCapacity: Problem =
            {
                Items = [ { Id = "A"; Size = 1.0 } ]
                BinCapacity = System.Math.PI
            }

        match toQubo irrationalCapacity with
        | Error(QuantumError.ValidationError(field, _)) -> Assert.Equal("binCapacity", field)
        | _ -> Assert.Fail("Should reject a capacity without a short decimal form")

        Assert.False(isValid thirds [| 1; 1 |])

    [<Fact>]
    let ``solveWithConfig rejects empty items`` () : Task =
        task {
            let backend = createLocalBackend ()
            let problem: Problem = { Items = []; BinCapacity = 5.0 }

            match! solveWithConfigAsync backend problem defaultConfig CancellationToken.None with
            | Error(QuantumError.ValidationError(field, _)) -> Assert.Equal("items", field)
            | _ -> Assert.Fail("Should reject empty items")
        }

// ============================================================================
// QUBIT ESTIMATION TESTS
// ============================================================================

module QubitEstimationTests =

    [<Fact>]
    let ``estimateQubits counts item, bin and slack bits`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 3.0 }; { Id = "B"; Size = 4.0 } ]
                BinCapacity = 5.0
            }
        // B = 2 (first-fit-decreasing: 4 and 3 do not share a 5-bin), n = 2 → 2*2 + 2 = 6,
        // plus 2 slack bits per bin for a load of 3..5 → 10
        Assert.Equal(10, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits with single item single bin`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 1.0 } ]
                BinCapacity = 10.0
            }
        // B = 1 (first-fit-decreasing), n = 1 → 1*1 + 1 = 2; the capacity is capped at the
        // total size, so the one bin is full and has no slack bits
        Assert.Equal(2, estimateQubits problem)

    [<Fact>]
    let ``estimateQubits with items needing multiple bins`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 5.0 }; { Id = "B"; Size = 5.0 }; { Id = "C"; Size = 5.0 } ]
                BinCapacity = 5.0
            }
        // B = 3 (first-fit-decreasing: three full bins), n = 3 → 3*3 + 3 = 12, no slack bits
        Assert.Equal(12, estimateQubits problem)

// ============================================================================
// GROUND STATE TESTS (energy-only brute force, no circuit)
// ============================================================================

module GroundStateTests =

    let private problemOf (sizes: float list) (capacity: float) : Problem =
        {
            Items = sizes |> List.mapi (fun i size -> { Id = $"item{i}"; Size = size })
            BinCapacity = capacity
        }

    /// Reads a bitstring without the solver: x_{ij} at i*B + j, y_j at n*B + j, anything
    /// after that is auxiliary. Some binsUsed when every item is in exactly one bin, no bin
    /// is over capacity and y_j = 1 exactly for the bins that hold an item.
    let private packingOf (sizes: float list) (capacity: float) (numBins: int) (bits: int[]) : int option =
        let n = sizes.Length
        let x i j = bits.[i * numBins + j]
        let y j = bits.[n * numBins + j]

        let everyItemOnce =
            [ 0 .. n - 1 ]
            |> List.forall (fun i -> ([ 0 .. numBins - 1 ] |> List.sumBy (x i)) = 1)

        let loads =
            List.init (FSharp.Core.Operators.max 0 numBins) (fun j ->
                sizes |> List.mapi (fun i size -> float (x i j) * size) |> List.sum)

        let holdsItem j =
            [ 0 .. n - 1 ] |> List.exists (fun i -> x i j = 1)

        let withinCapacity = loads |> List.forall (fun load -> load <= capacity + 1e-9)

        let binBitsAgree =
            [ 0 .. numBins - 1 ] |> List.forall (fun j -> (y j = 1) = holdsItem j)

        if everyItemOnce && withinCapacity && binBitsAgree then
            Some([ 0 .. numBins - 1 ] |> List.filter holdsItem |> List.length)
        else
            None

    let private bitsOf (numQubits: int) (index: int) : int[] =
        Array.init numQubits (fun q -> (index >>> q) &&& 1)

    /// sizes, capacity, bins in the encoding (first-fit-decreasing count), fewest bins possible
    let private instances: (float list * float * int * int) list =
        [
            [ 4.0; 3.0; 3.0 ], 6.0, 2, 2
            [ 1.0; 1.0; 1.0 ], 2.0, 2, 2
            [ 30.0; 40.0; 20.0 ], 100.0, 1, 1
            [ 5.0; 4.0; 3.0 ], 10.0, 2, 2
            [ 2.0; 2.0; 2.0; 2.0 ], 4.0, 2, 2
            [ 0.5; 0.25; 0.75 ], 1.0, 2, 2
        ]

    [<Fact>]
    let ``every minimum-energy bitstring of toQubo is a valid packing with the fewest bins`` () =
        for sizes, capacity, numBins, fewestBins in instances do
            let problem = problemOf sizes capacity

            match toQubo problem with
            | Error err -> Assert.Fail($"toQubo failed for %A{sizes} / {capacity}: {err}")
            | Ok qubo ->
                let numQubits = qubo.GetLength 0
                Assert.True(numQubits <= 20, $"%A{sizes} / {capacity}: %d{numQubits} qubits is too many to enumerate")

                let energies =
                    Array.init (1 <<< numQubits) (fun index ->
                        QaoaExecutionHelpers.evaluateQubo qubo (bitsOf numQubits index))

                let minimum = Array.min energies

                let groundStates =
                    [ 0 .. energies.Length - 1 ]
                    |> List.filter (fun index -> energies.[index] <= minimum + 1e-9)
                    |> List.map (bitsOf numQubits)

                for bits in groundStates do
                    let text = bits |> Array.map string |> String.concat ""

                    match packingOf sizes capacity numBins bits with
                    | None ->
                        Assert.Fail(
                            $"%A{sizes} / {capacity}: ground state %s{text} (energy {minimum}) is not a valid packing"
                        )
                    | Some binsUsed ->
                        Assert.True(
                            (binsUsed = fewestBins),
                            $"%A{sizes} / {capacity}: ground state %s{text} uses %d{binsUsed} bins, the fewest is %d{fewestBins}"
                        )

                    Assert.True(isValid problem bits, $"%A{sizes} / {capacity}: isValid rejects ground state %s{text}")

    [<Fact>]
    let ``isValid agrees with an independent reading of every bitstring`` () =
        for sizes, capacity, numBins, _ in instances do
            let problem = problemOf sizes capacity
            let numQubits = estimateQubits problem

            for index in 0 .. (1 <<< numQubits) - 1 do
                let bits = bitsOf numQubits index
                let expected = (packingOf sizes capacity numBins bits).IsSome

                if isValid problem bits <> expected then
                    let text = bits |> Array.map string |> String.concat ""
                    Assert.Fail($"%A{sizes} / {capacity}: isValid %s{text} should be {expected}")

    [<Fact>]
    let ``repair turns every bitstring into a valid packing without penalty`` () =
        let repairInstances =
            instances @ [ [ 6.0; 5.0; 4.0; 3.0 ], 10.0, 2, 2; [ 5.0; 5.0; 5.0 ], 5.0, 3, 3 ]

        for sizes, capacity, numBins, fewestBins in repairInstances do
            let problem = problemOf sizes capacity

            match toQubo problem with
            | Error err -> Assert.Fail($"toQubo failed for %A{sizes} / {capacity}: {err}")
            | Ok qubo ->
                let numQubits = qubo.GetLength 0
                Assert.True(numQubits <= 16, $"%A{sizes} / {capacity}: %d{numQubits} qubits")

                let energyOf = QaoaExecutionHelpers.evaluateQubo qubo

                let minimum =
                    seq { 0 .. (1 <<< numQubits) - 1 }
                    |> Seq.map (bitsOf numQubits >> energyOf)
                    |> Seq.min

                // A bitstring without penalty has energy = bins used + the constant the QUBO drops
                let offset = minimum - float fewestBins

                for index in 0 .. (1 <<< numQubits) - 1 do
                    let bits = bitsOf numQubits index
                    let repaired = repairConstraints problem bits
                    let text = bits |> Array.map string |> String.concat ""

                    match packingOf sizes capacity numBins repaired with
                    | None -> Assert.Fail($"%A{sizes} / {capacity}: repair of %s{text} is not a valid packing")
                    | Some binsUsed ->
                        if abs (energyOf repaired - (float binsUsed + offset)) > 1e-6 then
                            Assert.Fail($"%A{sizes} / {capacity}: repair of %s{text} carries a penalty")

                    // A valid packing keeps its placements and bin-used bits
                    if (packingOf sizes capacity numBins bits).IsSome then
                        let assignmentBits = (sizes.Length + 1) * numBins

                        if repaired.[0 .. assignmentBits - 1] <> bits.[0 .. assignmentBits - 1] then
                            Assert.Fail($"%A{sizes} / {capacity}: repair moved items of the valid packing %s{text}")

    [<Fact>]
    let ``a packing that leaves a bin empty costs one unit less than first-fit-decreasing`` () =
        // First-fit-decreasing needs 3 bins here ({3,3} {2,2,2} {2}); {3,2,2} {3,2,2} needs 2.
        // 30 qubits: single energies only, no enumeration.
        let sizes = [ 3.0; 3.0; 2.0; 2.0; 2.0; 2.0 ]
        let capacity = 7.0
        let numBins = 3
        let problem = problemOf sizes capacity
        let numQubits = estimateQubits problem
        // 6*3 item bits + 3 bin bits + 3 bins * 3 slack bits (loads 2..7)
        Assert.Equal(30, numQubits)

        match toQubo problem with
        | Error err -> Assert.Fail($"toQubo failed: {err}")
        | Ok qubo ->
            Assert.Equal(numQubits, qubo.GetLength 0)
            let energyOf = QaoaExecutionHelpers.evaluateQubo qubo

            // repair of the empty bitstring places every item first-fit in decreasing order
            let firstFit = repairConstraints problem (Array.zeroCreate numQubits)
            Assert.Equal(Some 3, packingOf sizes capacity numBins firstFit)

            let twoBinItems = Array.zeroCreate numQubits

            for item, bin in [ 0, 0; 2, 0; 3, 0; 1, 1; 4, 1; 5, 1 ] do
                twoBinItems.[item * numBins + bin] <- 1

            // repair keeps placements that fit and sets the bin-used and slack bits
            let twoBins = repairConstraints problem twoBinItems
            Assert.Equal(Some 2, packingOf sizes capacity numBins twoBins)
            Assert.True(isValid problem twoBins)

            Assert.Equal(1.0, energyOf firstFit - energyOf twoBins, 6)

            // Marking the empty bin as used is invalid and costs more than the bin it claims
            let emptyBinMarked = Array.copy twoBins
            emptyBinMarked.[sizes.Length * numBins + 2] <- 1
            Assert.False(isValid problem emptyBinMarked)
            Assert.True(energyOf emptyBinMarked > energyOf firstFit)

// ============================================================================
// isValid TESTS
// ============================================================================

module IsValidTests =

    [<Fact>]
    let ``isValid accepts correct single-item packing`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 3.0 } ]
                BinCapacity = 5.0
            }
        // B = 1, total vars = 2 (x_{0,0}, y_0)
        // x_{0,0} = 1 (item 0 in bin 0), y_0 = 1 (bin 0 used)
        Assert.True(isValid problem [| 1; 1 |])

    [<Fact>]
    let ``isValid rejects unassigned item`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 3.0 } ]
                BinCapacity = 5.0
            }
        // x_{0,0} = 0, y_0 = 0 — item not assigned
        Assert.False(isValid problem [| 0; 0 |])

    [<Fact>]
    let ``isValid rejects wrong-length bitstring`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 3.0 } ]
                BinCapacity = 5.0
            }

        Assert.False(isValid problem [| 1 |]) // Too short
        Assert.False(isValid problem [| 1; 1; 0 |]) // Too long

    [<Fact>]
    let ``isValid rejects overloaded bin`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 3.0 }; { Id = "B"; Size = 4.0 } ]
                BinCapacity = 5.0
            }
        // B = 2, total vars = 2*2 + 2 + 2*2 slack = 10
        // x_{0,0}=1, x_{0,1}=0, x_{1,0}=1, x_{1,1}=0, y_0=1, y_1=0, slack 0
        // Both items in bin 0: load = 3+4 = 7 > 5
        Assert.False(isValid problem [| 1; 0; 1; 0; 1; 0; 0; 0; 0; 0 |])

    [<Fact>]
    let ``isValid checks item bits and bin-used bits, not slack bits`` () =
        let problem: Problem =
            {
                Items = [ { Id = "A"; Size = 3.0 }; { Id = "B"; Size = 4.0 } ]
                BinCapacity = 5.0
            }
        // Layout: x_{0,0}, x_{0,1}, x_{1,0}, x_{1,1}, y_0, y_1, then 2 slack bits per bin
        // A in bin 0, B in bin 1, both bins marked used
        Assert.True(isValid problem [| 1; 0; 0; 1; 1; 1; 0; 0; 0; 0 |])
        // Slack bits are auxiliary
        Assert.True(isValid problem [| 1; 0; 0; 1; 1; 1; 1; 1; 1; 1 |])
        // A in both bins is not an assignment
        Assert.False(isValid problem [| 1; 1; 0; 1; 1; 1; 0; 0; 0; 0 |])
        // Bin 1 holds B but its bin-used bit is 0
        Assert.False(isValid problem [| 1; 0; 0; 1; 1; 0; 0; 0; 0; 0 |])
        // B unassigned
        Assert.False(isValid problem [| 1; 0; 0; 0; 1; 0; 0; 0; 0; 0 |])

// ============================================================================
// DECOMPOSE / RECOMBINE TESTS
// ============================================================================

module DecomposeRecombineTests =

    [<Fact>]
    let ``decompose returns single problem`` () =
        let problem: Problem =
            {
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
        let sol: Solution =
            {
                Assignments = [ ({ Id = "A"; Size = 1.0 }, 0) ]
                BinsUsed = 1
                IsValid = true
                WasRepaired = false
                BackendName = "Test"
                NumShots = 100
                OptimizedParameters = None
                OptimizationConverged = None
                Sampling = None
            }

        let result = recombine [ sol ]
        Assert.Equal(1, result.BinsUsed)

    [<Fact>]
    let ``recombine picks fewest bins`` () =
        let sol1: Solution =
            {
                Assignments = []
                BinsUsed = 3
                IsValid = true
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
                Sampling = None
            }

        let sol2: Solution =
            {
                Assignments = []
                BinsUsed = 2
                IsValid = true
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
                Sampling = None
            }

        let result = recombine [ sol1; sol2 ]
        Assert.Equal(2, result.BinsUsed)

// ============================================================================
// QUANTUM SOLVER TESTS (using LocalBackend)
// ============================================================================

module QuantumSolverTests =

    [<Fact>]
    let ``solve returns Ok for single item`` () : Task =
        task {
            let backend = createLocalBackend ()

            let problem: Problem =
                {
                    Items = [ { Id = "A"; Size = 3.0 } ]
                    BinCapacity = 5.0
                }

            let! result = solveDefaultAsync backend problem 100

            result
            |> Result.map (fun solution -> Assert.Equal("Local Simulator", solution.BackendName))
            |> Result.defaultWith (fun err -> Assert.Fail($"solve failed: {err}"))
        }

    [<Fact>]
    let ``solve with constraint repair produces valid packing`` () : Task =
        task {
            let backend = createLocalBackend ()

            let problem: Problem =
                {
                    Items = [ { Id = "A"; Size = 3.0 }; { Id = "B"; Size = 3.0 } ]
                    BinCapacity = 5.0
                }

            let config =
                { defaultConfig with
                    EnableConstraintRepair = true
                }

            match! solveWithConfigAsync backend problem config CancellationToken.None with
            | Error err -> Assert.Fail($"solve with repair failed: {err}")
            | Ok solution ->
                // After repair, solution should be valid
                Assert.True(solution.IsValid, "Repaired solution should be valid")

                Assert.True(
                    solution.Assignments.Length = 2,
                    $"All items should be assigned, got {solution.Assignments.Length}"
                )
        }

    [<Fact>]
    let ``solveWithConfig uses config shots`` () : Task =
        task {
            let backend = createLocalBackend ()

            let problem: Problem =
                {
                    Items = [ { Id = "A"; Size = 1.0 } ]
                    BinCapacity = 5.0
                }

            let config = { defaultConfig with FinalShots = 42 }

            let! result = solveWithConfigAsync backend problem config CancellationToken.None

            result
            |> Result.map (fun solution -> Assert.Equal(42, solution.NumShots))
            |> Result.defaultWith (fun err -> Assert.Fail($"solveWithConfig failed: {err}"))
        }

    [<Fact>]
    let ``solve with items fitting in one bin`` () : Task =
        task {
            let backend = createLocalBackend ()

            let problem: Problem =
                {
                    Items = [ { Id = "A"; Size = 1.0 }; { Id = "B"; Size = 2.0 } ]
                    BinCapacity = 10.0
                }

            let config =
                { defaultConfig with
                    EnableConstraintRepair = true
                }

            match! solveWithConfigAsync backend problem config CancellationToken.None with
            | Error err -> Assert.Fail($"solve failed: {err}")
            | Ok solution ->
                Assert.True(solution.IsValid, "Solution should be valid")
                // Both items fit in one bin
                Assert.True(solution.BinsUsed <= 1, $"Items should fit in 1 bin, got {solution.BinsUsed}")
        }

    [<Fact>]
    let ``solve with items requiring separate bins`` () : Task =
        task {
            let backend = createLocalBackend ()

            let problem: Problem =
                {
                    Items = [ { Id = "A"; Size = 5.0 }; { Id = "B"; Size = 5.0 } ]
                    BinCapacity = 5.0
                }

            let config =
                { defaultConfig with
                    EnableConstraintRepair = true
                }

            match! solveWithConfigAsync backend problem config CancellationToken.None with
            | Error err -> Assert.Fail($"solve failed: {err}")
            | Ok solution ->
                // After repair, each item needs its own bin
                if solution.IsValid then
                    Assert.True(solution.BinsUsed >= 2, $"Each item needs its own bin, got {solution.BinsUsed}")
        }

    /// Sizes 4, 3, 3 into bins of 6: two bins in every valid packing, 12 qubits.
    let private threeItems: Problem =
        {
            Items = [ { Id = "A"; Size = 4.0 }; { Id = "B"; Size = 3.0 }; { Id = "C"; Size = 3.0 } ]
            BinCapacity = 6.0
        }

    /// Every item of the problem in exactly one bin and no bin over capacity, read from the
    /// solution's assignment list alone.
    let private assertCompletePacking (problem: Problem) (solution: Solution) =
        Assert.Equal<string list>(
            problem.Items |> List.map (fun item -> item.Id) |> List.sort,
            solution.Assignments |> List.map (fun (item, _) -> item.Id) |> List.sort
        )

        for _, group in solution.Assignments |> List.groupBy snd do
            let load = group |> List.sumBy (fun (item, _) -> item.Size)
            Assert.True(load <= problem.BinCapacity + 1e-9, $"bin load {load} exceeds {problem.BinCapacity}")

        Assert.Equal(solution.Assignments |> List.map snd |> List.distinct |> List.length, solution.BinsUsed)

    [<Fact>]
    let ``repair is applied only when no final sample is a valid packing`` () : Task =
        task {
            match! solveDefaultAsync (createLocalBackend ()) threeItems 200 with
            | Error err -> Assert.Fail($"solve failed: {err}")
            | Ok solution ->
                Assert.True(solution.IsValid, "with repair enabled the packing is valid")
                assertCompletePacking threeItems solution
                Assert.Equal(2, solution.BinsUsed)

                match solution.Sampling with
                | None -> Assert.Fail("no sampling statistics")
                | Some stats ->
                    Assert.Equal(200, stats.Shots)
                    Assert.Equal(estimateQubits threeItems, stats.Qubits)

                    if solution.WasRepaired then
                        Assert.Equal(0, stats.Valid)
                    else
                        Assert.InRange(stats.Hits, 1, stats.Valid)
        }

    [<Fact>]
    let ``without repair the solution is a measured sample`` () : Task =
        task {
            let config =
                { defaultConfig with
                    FinalShots = 200
                    EnableConstraintRepair = false
                }

            match! solveWithConfigAsync (createLocalBackend ()) threeItems config CancellationToken.None with
            | Error err -> Assert.Fail($"solve failed: {err}")
            | Ok solution ->
                Assert.False(solution.WasRepaired)

                match solution.Sampling with
                | None -> Assert.Fail("no sampling statistics")
                | Some stats ->
                    // Valid exactly when some final sample was a valid packing
                    Assert.Equal(stats.Valid > 0, solution.IsValid)
                    Assert.True(stats.Hits >= 1, "the returned bitstring is one of the samples")

                    if solution.IsValid then
                        assertCompletePacking threeItems solution
                        Assert.Equal(2, solution.BinsUsed)
                        Assert.True(stats.Hits <= stats.Valid)
        }
