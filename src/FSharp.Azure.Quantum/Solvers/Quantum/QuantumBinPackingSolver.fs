namespace FSharp.Azure.Quantum.Quantum

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers

/// Quantum Bin Packing Solver (QAOA-based)
///
/// Problem: Given n items with sizes s_i and bin capacity C,
/// pack all items into the minimum number of bins.
///
/// Integer grid: sizes and capacity are rescaled to integers (Qubo.tryScaleToIntegers),
/// the sizes are divided by their common divisor and the capacity is rounded down to that
/// grid and capped at the total size. Bin loads are sums of sizes, so this changes no
/// packing. Sizes that have no such integer form are a ValidationError.
///
/// QUBO Formulation (B bins, K slack bits per bin):
///   Variables:
///     x_{ij} in {0,1}: item i assigned to bin j        (indices: i*B + j)
///     y_j in {0,1}: bin j is used                       (indices: n*B + j)
///     z_{jk} in {0,1}: slack bit k of bin j, weight w_k (indices: n*B + B + j*K + k)
///
///   Objective: Minimize sum_j y_j
///
///   Constraint 1 (assignment): Each item in exactly one bin.
///     Penalty: lambda * (sum_j x_{ij} - 1)^2 for each item i
///
///   Constraint 2 (capacity): Bin j holds at most C, and y_j = 1 exactly when it holds an item.
///     Penalty: lambda * (sum_i s_i * x_{ij} + sum_k w_k * z_{jk} - C * y_j)^2 for each bin j
///     The slack takes every integer in 0..C - L, where L is the least load a used bin can
///     have in a complete packing: the smallest item, or what is left when the other B - 1
///     bins are full, whichever is larger. With y_j = 0 the term is zero only for an empty
///     bin with zero slack; with y_j = 1 only for a load in L..C.
///
///   Penalty weight: lambda = B + 1. Every penalty term is the square of an integer, so a
///   bitstring that breaks a constraint costs at least B + 1, more than the B bins of the
///   first-fit-decreasing packing. The minimum-energy bitstrings are therefore exactly the
///   valid packings with the fewest bins.
///
/// Qubits: n*B + B + B*K, where B = first-fit-decreasing bin count and
/// K = number of slack bits for the range C - L (0 when every used bin must be full).
///
/// Scaling concern: O(n*B + B*log2 C) qubits. Practical for ~5 items with 2 bins.
///
/// RULE 1 COMPLIANCE:
/// All public solve functions require IQuantumBackend parameter.
/// Classical solver is private.
module QuantumBinPackingSolver =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// An item to be packed
    type Item =
        {
            /// Unique identifier
            Id: string
            /// Size of the item (must be positive, must fit in a single bin)
            Size: float
        }

    /// Bin packing problem definition
    type Problem =
        {
            /// Items to pack
            Items: Item list
            /// Capacity of each bin (all bins have same capacity)
            BinCapacity: float
        }

    /// Bin packing solution
    type Solution =
        {
            /// Assignment: (item, bin index) for every item placed in exactly one bin
            Assignments: (Item * int) list
            /// Number of bins that hold an item
            BinsUsed: int
            /// Whether the bitstring behind this solution passes isValid
            IsValid: bool
            /// Whether constraint repair was applied
            WasRepaired: bool
            /// Name of the quantum backend used
            BackendName: string
            /// Number of measurement shots
            NumShots: int
            /// Optimized QAOA (gamma, beta) parameters per layer
            OptimizedParameters: (float * float)[] option
            /// Whether Nelder-Mead converged
            OptimizationConverged: bool option
            /// Standing of this solution among the final samples; None when no single sampling run produced it
            Sampling: QaoaExecutionHelpers.SampleStatistics option
        }

    // ========================================================================
    // CONFIGURATION (type alias for unified config)
    // ========================================================================

    type Config = QaoaSolverConfig

    let defaultConfig: Config = QaoaExecutionHelpers.defaultConfig
    let fastConfig: Config = QaoaExecutionHelpers.fastConfig
    let highQualityConfig: Config = QaoaExecutionHelpers.highQualityConfig

    // ========================================================================
    // INTEGER ENCODING
    // ========================================================================

    /// The instance on its integer grid, with the sizes of the QUBO's variable blocks.
    type private Encoding =
        {
            /// Item sizes as positive integers, in the order of Problem.Items
            Sizes: int[]
            /// Bin capacity on the same grid, at most the total size
            Capacity: int
            /// Bins in the encoding: the first-fit-decreasing bin count
            NumBins: int
            /// Weights of the slack bits of one bin; their subset sums are 0..Capacity - least load
            SlackWeights: int[]
        }

    /// First-fit decreasing: items in order of decreasing size, each into the first bin
    /// with room, a new bin when none has room. Returns the bin index of every item.
    let private firstFitDecreasing (sizes: int[]) (capacity: int) : int[] =
        let binOf = Array.create sizes.Length -1
        let loads = ResizeArray<int64>()

        for i in [ 0 .. sizes.Length - 1 ] |> List.sortByDescending (fun i -> sizes.[i]) do
            let size = int64 sizes.[i]

            match loads |> Seq.tryFindIndex (fun load -> load + size <= int64 capacity) with
            | Some j ->
                loads.[j] <- loads.[j] + size
                binOf.[i] <- j
            | None ->
                loads.Add size
                binOf.[i] <- loads.Count - 1

        binOf

    /// Validate a problem and put it on its integer grid.
    ///
    /// The bin bound B is the bin count of a first-fit-decreasing packing. That packing is
    /// feasible, so the optimum never needs more bins: no solution is lost, and the QUBO has
    /// n*B item variables instead of n*n. ceil(totalSize / C) is only a lower bound and
    /// under-dimensions the QUBO for some inputs (e.g. items of size just over C/2).
    let private encodingOf (problem: Problem) : Result<Encoding, QuantumError> =
        let sizes = problem.Items |> List.map (fun item -> item.Size)

        if problem.Items.IsEmpty then
            Error(QuantumError.ValidationError("items", "Problem has no items"))
        elif not (problem.BinCapacity > 0.0) then
            Error(QuantumError.ValidationError("binCapacity", "Bin capacity must be positive"))
        elif sizes |> List.exists (fun size -> not (size > 0.0)) then
            Error(QuantumError.ValidationError("itemSize", "All item sizes must be positive"))
        elif sizes |> List.exists (fun size -> size > problem.BinCapacity) then
            Error(QuantumError.ValidationError("itemSize", "Item size exceeds bin capacity"))
        else
            match Qubo.tryScaleToIntegers (problem.BinCapacity :: sizes) with
            | Some(scaledCapacity :: scaledSizes, _) ->
                let rec gcd (a: int) (b: int) = if b = 0 then abs a else gcd b (a % b)
                let divisor = scaledSizes |> List.fold gcd 0 |> max 1
                let gridSizes = scaledSizes |> List.map (fun size -> size / divisor) |> List.toArray
                let total = gridSizes |> Array.sumBy int64
                let capacity = int (min (int64 (scaledCapacity / divisor)) total)
                let numBins = (firstFitDecreasing gridSizes capacity |> Array.max) + 1

                let leastLoad =
                    max (int64 (Array.min gridSizes)) (total - int64 (numBins - 1) * int64 capacity)

                Ok
                    {
                        Sizes = gridSizes
                        Capacity = capacity
                        NumBins = numBins
                        SlackWeights = Qubo.boundedSlackWeights (capacity - int leastLoad) |> List.toArray
                    }
            | _ ->
                let noIntegerForm (value: float) =
                    (Qubo.tryScaleToIntegers [ value ]).IsNone

                let requirement =
                    "sizes and capacity need a common integer form: at most 6 decimal places, at most 10^9 after rescaling"

                if noIntegerForm problem.BinCapacity then
                    Error(
                        QuantumError.ValidationError(
                            "binCapacity",
                            $"Bin capacity {problem.BinCapacity} cannot be rescaled to an integer; {requirement}"
                        )
                    )
                else
                    match problem.Items |> List.tryFind (fun item -> noIntegerForm item.Size) with
                    | Some item ->
                        Error(
                            QuantumError.ValidationError(
                                "itemSize",
                                $"Size {item.Size} of item '{item.Id}' cannot be rescaled to an integer; {requirement}"
                            )
                        )
                    | None ->
                        Error(
                            QuantumError.ValidationError(
                                "itemSize",
                                $"Item sizes cannot be rescaled to integers together with the bin capacity; {requirement}"
                            )
                        )

    /// Number of QUBO variables of an encoding: n*B item-bin bits, B bin-used bits, B*K slack bits.
    let private totalVariables (encoding: Encoding) : int =
        encoding.Sizes.Length * encoding.NumBins
        + encoding.NumBins
        + encoding.NumBins * encoding.SlackWeights.Length

    // ========================================================================
    // QUBIT ESTIMATION (Decision 11)
    // ========================================================================

    /// Number of qubits the QUBO of this problem has: n*B + B + B*K, with B the
    /// first-fit-decreasing bin count and K the slack bits per bin (see the module comment).
    /// A problem that toQubo rejects has no encoding; the result is then the
    /// one-bin-per-item count n*n + n.
    let estimateQubits (problem: Problem) : int =
        match encodingOf problem with
        | Ok encoding -> totalVariables encoding
        | Error _ ->
            let n = problem.Items.Length
            n * n + n

    // ========================================================================
    // INDEX HELPERS
    // ========================================================================

    /// Get the QUBO variable index for "item i assigned to bin j".
    let private itemBinIndex (encoding: Encoding) (i: int) (j: int) : int = i * encoding.NumBins + j

    /// Get the QUBO variable index for "bin j is used".
    let private binUsedIndex (encoding: Encoding) (j: int) : int =
        encoding.Sizes.Length * encoding.NumBins + j

    /// Get the QUBO variable index for slack bit k of bin j.
    let private slackIndex (encoding: Encoding) (j: int) (k: int) : int =
        encoding.Sizes.Length * encoding.NumBins
        + encoding.NumBins
        + j * encoding.SlackWeights.Length
        + k

    // ========================================================================
    // QUBO CONSTRUCTION (Decision 9: sparse internally, Decision 5: dense output)
    // ========================================================================

    /// Build the QUBO as a sparse, symmetric map (each pair coefficient split over (i, j) and (j, i)).
    let private buildQuboMap (encoding: Encoding) : Map<int * int, float> =
        let n = encoding.Sizes.Length
        let b = encoding.NumBins

        // One penalty weight for both constraints: above the largest objective value B, and
        // every violation is at least one whole unit, squared.
        let penalty = float b + 1.0

        // Objective: sum_j y_j
        let objectiveTerms =
            [
                for j in 0 .. b - 1 do
                    let yj = binUsedIndex encoding j
                    yield Map.ofList [ ((yj, yj), 1.0) ]
            ]

        // Constraint 1: lambda * (sum_j x_{ij} - 1)^2 for each item
        let assignmentTerms =
            [
                for i in 0 .. n - 1 do
                    let row = [ for j in 0 .. b - 1 -> (itemBinIndex encoding i j, 1.0) ]
                    yield Qubo.squaredLinearPenalty penalty row -1.0
            ]

        // Constraint 2: lambda * (sum_i s_i x_{ij} + sum_k w_k z_{jk} - C y_j)^2 for each bin
        let capacityTerms =
            [
                for j in 0 .. b - 1 do
                    let load =
                        [ for i in 0 .. n - 1 -> (itemBinIndex encoding i j, float encoding.Sizes.[i]) ]

                    let slack =
                        [
                            for k in 0 .. encoding.SlackWeights.Length - 1 ->
                                (slackIndex encoding j k, float encoding.SlackWeights.[k])
                        ]

                    let binUsed = [ (binUsedIndex encoding j, -(float encoding.Capacity)) ]
                    yield Qubo.squaredLinearPenalty penalty (load @ slack @ binUsed) 0.0
            ]

        (objectiveTerms @ assignmentTerms @ capacityTerms)
        |> List.fold
            (fun combined termMap ->
                termMap
                |> Map.fold
                    (fun acc (i, j) value ->
                        if i = j then
                            Qubo.combineTerms (i, i) value acc
                        else
                            acc
                            |> Qubo.combineTerms (i, j) (value / 2.0)
                            |> Qubo.combineTerms (j, i) (value / 2.0))
                    combined)
            Map.empty

    /// Convert problem to dense QUBO matrix.
    /// Returns Result to follow the canonical pattern (validates inputs).
    let toQubo (problem: Problem) : Result<float[,], QuantumError> =
        encodingOf problem
        |> Result.map (fun encoding -> Qubo.toDenseArray (totalVariables encoding) (buildQuboMap encoding))

    // ========================================================================
    // SOLUTION DECODING & VALIDATION
    // ========================================================================

    /// The bins whose item-bin bit is set for item i.
    let private binsOfItem (encoding: Encoding) (bits: int[]) (i: int) : int list =
        [ 0 .. encoding.NumBins - 1 ]
        |> List.filter (fun j -> bits.[itemBinIndex encoding i j] = 1)

    /// Bin of every item that has exactly one item-bin bit set; ValueNone for the other items.
    let private placements (encoding: Encoding) (bits: int[]) : int voption[] =
        Array.init encoding.Sizes.Length (fun i ->
            match binsOfItem encoding bits i with
            | [ j ] -> ValueSome j
            | _ -> ValueNone)

    /// Load of bin j: the sizes of all items whose bit for this bin is set.
    let private loadOf (encoding: Encoding) (bits: int[]) (j: int) : int64 =
        [ 0 .. encoding.Sizes.Length - 1 ]
        |> List.sumBy (fun i -> int64 (bits.[itemBinIndex encoding i j] * encoding.Sizes.[i]))

    /// The validity rule of isValid on an encoding.
    let private isValidPacking (encoding: Encoding) (bits: int[]) : bool =
        bits.Length = totalVariables encoding
        && placements encoding bits |> Array.forall ValueOption.isSome
        && [ 0 .. encoding.NumBins - 1 ]
           |> List.forall (fun j ->
               let load = loadOf encoding bits j

               load <= int64 encoding.Capacity
               && (bits.[binUsedIndex encoding j] = 1) = (load > 0L))

    /// Validate a bitstring for this problem. A bitstring is valid when
    ///   - it has estimateQubits entries,
    ///   - every item has exactly one item-bin bit set (several bins set is not an assignment),
    ///   - no bin's load exceeds the capacity, and
    ///   - the bin-used bit y_j is 1 exactly for the bins that hold an item, so the number of
    ///     set bin-used bits is the number of bins used.
    /// Slack bits are auxiliary and not checked. A problem that toQubo rejects has no valid bitstring.
    let isValid (problem: Problem) (bits: int[]) : bool =
        match encodingOf problem with
        | Ok encoding -> isValidPacking encoding bits
        | Error _ -> false

    /// Decode a bitstring of totalVariables entries into a Solution.
    let private decodeSolution (problem: Problem) (encoding: Encoding) (bits: int[]) : Solution =
        let assignments =
            placements encoding bits
            |> Array.toList
            |> List.zip problem.Items
            |> List.choose (fun (item, bin) ->
                match bin with
                | ValueSome j -> Some(item, j)
                | ValueNone -> None)

        {
            Assignments = assignments
            BinsUsed = assignments |> List.map snd |> List.distinct |> List.length
            IsValid = isValidPacking encoding bits
            WasRepaired = false
            BackendName = ""
            NumShots = 0
            OptimizedParameters = None
            OptimizationConverged = None
            Sampling = None
        }

    // ========================================================================
    // CONSTRAINT REPAIR
    // ========================================================================

    /// Slack bits whose weighted sum is value, for 0 <= value <= sum of weights.
    /// Qubo.boundedSlackWeights gives powers of two followed by one remainder weight: the
    /// remainder is used when the powers alone cannot reach the value, the rest is binary.
    let private slackBitsFor (weights: int[]) (value: int) : int[] =
        let bits = Array.zeroCreate weights.Length

        if weights.Length > 0 then
            let last = weights.Length - 1
            let powerSum = Array.sum weights - weights.[last]
            let mutable remaining = value

            if remaining > powerSum then
                bits.[last] <- 1
                remaining <- remaining - weights.[last]

            for k in last - 1 .. -1 .. 0 do
                if weights.[k] <= remaining then
                    bits.[k] <- 1
                    remaining <- remaining - weights.[k]

        bits

    /// The bitstring of a complete packing (bin index per item, every bin within capacity):
    /// item-bin bits, bin-used bits for the bins that hold an item, and each used bin's
    /// slack set to capacity - load, so that every penalty term is zero.
    let private bitsOfPacking (encoding: Encoding) (binOf: int[]) : int[] =
        let bits = Array.zeroCreate (totalVariables encoding)
        let loads = Array.zeroCreate<int> encoding.NumBins

        binOf
        |> Array.iteri (fun i j ->
            bits.[itemBinIndex encoding i j] <- 1
            loads.[j] <- loads.[j] + encoding.Sizes.[i])

        for j in 0 .. encoding.NumBins - 1 do
            if loads.[j] > 0 then
                bits.[binUsedIndex encoding j] <- 1

                slackBitsFor encoding.SlackWeights (encoding.Capacity - loads.[j])
                |> Array.iteri (fun k bit -> bits.[slackIndex encoding j k] <- bit)

        bits

    /// Repair a bitstring into a valid packing.
    ///   1. In order of decreasing size, an item with exactly one bin set keeps that bin
    ///      while the bin stays within capacity.
    ///   2. Every other item goes, in the same order, into the first bin with room.
    ///   3. When an item finds no room, the measured placements are dropped and the
    ///      first-fit-decreasing packing is returned; it fits the B bins by the choice of B.
    /// The result always passes isValidPacking.
    let private repairPacking (encoding: Encoding) (bits: int[]) : int[] =
        let n = encoding.Sizes.Length
        let capacity = int64 encoding.Capacity
        let measured = placements encoding bits
        let binOf = Array.create n -1
        let loads = Array.zeroCreate<int64> encoding.NumBins

        let order = [ 0 .. n - 1 ] |> List.sortByDescending (fun i -> encoding.Sizes.[i])

        let place (i: int) (j: int) =
            binOf.[i] <- j
            loads.[j] <- loads.[j] + int64 encoding.Sizes.[i]

        let fits (i: int) (j: int) =
            loads.[j] + int64 encoding.Sizes.[i] <= capacity

        for i in order do
            match measured.[i] with
            | ValueSome j when fits i j -> place i j
            | _ -> ()

        for i in order do
            if binOf.[i] < 0 then
                match [ 0 .. encoding.NumBins - 1 ] |> List.tryFind (fits i) with
                | Some j -> place i j
                | None -> ()

        if binOf |> Array.forall (fun j -> j >= 0) then
            bitsOfPacking encoding binOf
        else
            bitsOfPacking encoding (firstFitDecreasing encoding.Sizes encoding.Capacity)

    /// Repair a bitstring of estimateQubits entries into one that passes isValid
    /// (see repairPacking). A problem that toQubo rejects is returned unchanged.
    let internal repairConstraints (problem: Problem) (bits: int[]) : int[] =
        match encodingOf problem with
        | Ok encoding when bits.Length = totalVariables encoding -> repairPacking encoding bits
        | _ -> bits

    // ========================================================================
    // DECOMPOSE / RECOMBINE HOOKS (Decision 10: identity stubs)
    // ========================================================================

    /// Decompose a bin packing problem into sub-problems.
    /// Currently identity — bin packing has shared capacity constraints.
    /// Future: partition by item-size groups or greedy pre-assignment.
    let decompose (problem: Problem) : Problem list = [ problem ]

    /// Recombine sub-solutions into a single solution. Currently identity.
    /// Handles empty list gracefully.
    let recombine (solutions: Solution list) : Solution =
        match solutions with
        | [] ->
            {
                Assignments = []
                BinsUsed = 0
                IsValid = false
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
                Sampling = None
            }
        | [ single ] -> single
        | _ -> solutions |> List.minBy (fun s -> s.BinsUsed)

    // ========================================================================
    // QUANTUM SOLVERS (Rule 1: IQuantumBackend required)
    // ========================================================================

    /// Shared implementation of solveWithConfigAsync.
    let private solveWithConfigCore
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (config: Config)
        (cancellationToken: CancellationToken)
        : Task<Result<Solution, QuantumError>> =

        match encodingOf problem with
        | Error err -> Task.FromResult(Error err)
        | Ok _ ->
            let solveSingle (subProblem: Problem) : Task<Result<Solution, QuantumError>> =
                task {
                    match encodingOf subProblem with
                    | Error err -> return Error err
                    | Ok encoding ->
                        let qubo = Qubo.toDenseArray (totalVariables encoding) (buildQuboMap encoding)

                        match! runQaoaSampledAsync backend qubo config cancellationToken with
                        | Error err -> return Error err
                        | Ok run ->
                            let optParams, converged = Some run.Parameters, run.Converged

                            // Slack bits are auxiliary: a sample can be a valid packing with
                            // slack that does not match its loads, at a higher energy than an
                            // invalid sample. The pick is therefore the valid sample with the
                            // fewest bins; only a run without a valid sample falls to its
                            // lowest-energy sample, and only that one is ever repaired.
                            let validSamples = run.Samples |> Array.filter (isValidPacking encoding)

                            let bits, needsRepair =
                                if validSamples.Length > 0 then
                                    let binsUsed (sample: int[]) =
                                        [ 0 .. encoding.NumBins - 1 ]
                                        |> List.sumBy (fun j -> sample.[binUsedIndex encoding j])

                                    (validSamples |> Array.minBy binsUsed, false)
                                else
                                    (run.Best, true)

                            let finalBits, wasRepaired =
                                if config.EnableConstraintRepair && needsRepair then
                                    (repairPacking encoding bits, true)
                                else
                                    (bits, false)

                            let solution = decodeSolution subProblem encoding finalBits
                            let returned = placements encoding finalBits

                            // A sample is a hit when it is the returned packing: the same
                            // placements and the same verdict of the validity rule.
                            let sampling =
                                sampleStatistics
                                    bits.Length
                                    (isValidPacking encoding)
                                    (fun sample ->
                                        placements encoding sample = returned
                                        && isValidPacking encoding sample = solution.IsValid)
                                    run.Samples

                            return
                                Ok
                                    { solution with
                                        BackendName = backend.Name
                                        NumShots = config.FinalShots
                                        WasRepaired = wasRepaired
                                        OptimizedParameters = optParams
                                        OptimizationConverged = converged
                                        Sampling = Some sampling
                                    }
                }

            ProblemDecomposition.solveWithDecompositionAsync
                backend
                problem
                estimateQubits
                decompose
                recombine
                solveSingle

    /// Solve bin packing using QAOA with full configuration control (async).
    /// Supports automatic decomposition when problem exceeds backend capacity.
    ///
    /// Returns the final sample that is a valid packing (isValid) with the fewest bins. When
    /// no final sample is valid, the lowest-energy sample is returned: repaired into a valid
    /// packing with WasRepaired = true when config.EnableConstraintRepair, as measured with
    /// IsValid = false otherwise. WasRepaired = true therefore implies Sampling.Valid = 0.
    let solveWithConfigAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (config: Config)
        (cancellationToken: CancellationToken)
        : Task<Result<Solution, QuantumError>> =
        task {
            cancellationToken.ThrowIfCancellationRequested()
            return! solveWithConfigCore backend problem config cancellationToken
        }

    // ========================================================================
    // CLASSICAL SOLVER (Rule 1: private — not exposed without backend)
    // ========================================================================

    /// Classical First-Fit Decreasing (FFD) bin packing for comparison.
    /// Strategy: sort items by size descending, assign each to the first
    /// bin with enough remaining capacity. FFD achieves 11/9*OPT + 6/9.
    let private solveClassical (problem: Problem) : Solution =
        if problem.Items.IsEmpty || problem.BinCapacity <= 0.0 then
            {
                Assignments = []
                BinsUsed = 0
                IsValid = problem.Items.IsEmpty
                WasRepaired = false
                BackendName = "Classical FFD"
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
                Sampling = None
            }
        else
            let sorted =
                problem.Items
                |> List.indexed
                |> List.sortByDescending (fun (_, item) -> item.Size)

            let assignments, binLoads =
                sorted
                |> List.fold
                    (fun (assigns: (Item * int) list, loads: Map<int, float>) (_, item) ->
                        // Find first bin with enough space
                        let targetBin =
                            loads
                            |> Map.toSeq
                            |> Seq.tryFind (fun (_, load) -> load + item.Size <= problem.BinCapacity + 1e-9)
                            |> Option.map fst

                        match targetBin with
                        | Some binIdx ->
                            let newLoad = loads.[binIdx] + item.Size
                            ((item, binIdx) :: assigns, loads |> Map.add binIdx newLoad)
                        | None ->
                            // Open a new bin
                            let newBin =
                                if loads.IsEmpty then
                                    0
                                else
                                    (loads |> Map.toSeq |> Seq.map fst |> Seq.max) + 1

                            ((item, newBin) :: assigns, loads |> Map.add newBin item.Size))
                    ([], Map.empty)

            let reversed = assignments |> List.rev

            {
                Assignments = reversed
                BinsUsed = binLoads.Count
                IsValid = true
                WasRepaired = false
                BackendName = "Classical FFD"
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
                Sampling = None
            }
