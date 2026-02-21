namespace FSharp.Azure.Quantum.Quantum

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers

/// Quantum Bin Packing Solver (QAOA-based)
///
/// Problem: Given n items with sizes s_i and bin capacity C,
/// pack all items into the minimum number of bins.
///
/// QUBO Formulation:
///   Variables:
///     x_{ij} in {0,1}: item i assigned to bin j  (indices: i*B + j)
///     y_j in {0,1}: bin j is used                 (indices: n*B + j)
///
///   Objective: Minimize sum_j y_j
///     qubo[y_j, y_j] += 1.0
///
///   Constraint 1 (assignment): Each item in exactly one bin.
///     For each item i: one-hot over {x_{i,0}, x_{i,1}, ..., x_{i,B-1}}
///     Penalty: lambda_1 * (1 - sum_j x_{ij})^2
///
///   Constraint 2 (capacity): Bin j not overloaded.
///     For each bin j: sum_i s_i * x_{ij} <= C
///     Penalty: lambda_2 * (sum_i s_i * x_{ij} - C * y_j)^2
///     This encourages y_j = 1 when bin j has items, and penalizes overload.
///
///   Constraint 3 (activation): y_j >= x_{ij} for all i, j.
///     Ensures bin is marked used when any item is assigned.
///     Penalty: lambda_3 * x_{ij} * (1 - y_j)
///
/// Qubits: n*B + B  where B = upper bound on bins needed.
///
/// Scaling concern: O(n*B) qubits. Practical for ~10 items with ~5 bins.
///
/// RULE 1 COMPLIANCE:
/// All public solve functions require IQuantumBackend parameter.
/// Classical solver is private.
module QuantumBinPackingSolver =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// An item to be packed
    type Item = {
        /// Unique identifier
        Id: string
        /// Size of the item (must be positive, must fit in a single bin)
        Size: float
    }

    /// Bin packing problem definition
    type Problem = {
        /// Items to pack
        Items: Item list
        /// Capacity of each bin (all bins have same capacity)
        BinCapacity: float
    }

    /// Bin packing solution
    type Solution = {
        /// Assignment: list of (item, bin index) pairs
        Assignments: (Item * int) list
        /// Number of bins used
        BinsUsed: int
        /// Whether all items are assigned and no bin exceeds capacity
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
    }

    // ========================================================================
    // CONFIGURATION (type alias for unified config)
    // ========================================================================

    type Config = QaoaSolverConfig

    let defaultConfig : Config = QaoaExecutionHelpers.defaultConfig
    let fastConfig : Config = QaoaExecutionHelpers.fastConfig
    let highQualityConfig : Config = QaoaExecutionHelpers.highQualityConfig

    // ========================================================================
    // QUBIT ESTIMATION (Decision 11)
    // ========================================================================

    /// Compute the upper bound on bins needed.
    /// B = n (number of items) — the worst case is each item in its own bin.
    /// Using ceil(totalSize / C) is only a *lower* bound and under-dimensions
    /// the QUBO for pathological inputs (e.g., items of size just over C/2).
    let private computeMaxBins (problem: Problem) : int =
        problem.Items.Length

    /// Estimate the number of qubits required.
    /// n*B (item-bin assignment variables) + B (bin-used indicator variables).
    let estimateQubits (problem: Problem) : int =
        let n = problem.Items.Length
        let b = computeMaxBins problem
        n * b + b

    // ========================================================================
    // INDEX HELPERS
    // ========================================================================

    /// Get the QUBO variable index for "item i assigned to bin j".
    let private itemBinIndex (numBins: int) (i: int) (j: int) : int =
        i * numBins + j

    /// Get the QUBO variable index for "bin j is used".
    let private binUsedIndex (numItems: int) (numBins: int) (j: int) : int =
        numItems * numBins + j

    // ========================================================================
    // QUBO CONSTRUCTION (Decision 9: sparse internally, Decision 5: dense output)
    // ========================================================================

    /// Build the QUBO as a sparse map.
    let private buildQuboMap (problem: Problem) (numBins: int) : Map<int * int, float> =
        let n = problem.Items.Length
        let b = numBins

        // Penalty weights: must dominate objective (which is at most B)
        let lambda1 = float b + 1.0   // Assignment constraint
        let lambda2 = float b + 1.0   // Capacity constraint
        let lambda3 = float b + 1.0   // Activation constraint

        let empty = Map.empty<int * int, float>

        // --- Objective: Minimize sum_j y_j ---
        let objectiveTerms =
            [ 0 .. b - 1 ]
            |> List.fold (fun acc j ->
                let yj = binUsedIndex n b j
                acc |> Qubo.combineTerms (yj, yj) 1.0) empty

        // --- Constraint 1: Each item in exactly one bin (one-hot, symmetric) ---
        // (1 - Σ x_{ij})^2 = 1 - 2Σ x_{ij} + (Σ x_{ij})^2
        //   Diagonal: -λ * x_{ij}  (from the -2Σ x + Σ x^2 = x terms)
        //   Off-diagonal: λ * x_{ij1} * x_{ij2}  (symmetric split: λ/2 each side)
        let assignmentTerms =
            [ 0 .. n - 1 ]
            |> List.fold (fun acc i ->
                let varIndices = [ 0 .. b - 1 ] |> List.map (itemBinIndex b i)
                // Linear: -lambda1 per variable
                let acc =
                    varIndices
                    |> List.fold (fun a vi ->
                        a |> Qubo.combineTerms (vi, vi) (-lambda1)) acc
                // Quadratic: symmetric split for each pair
                varIndices
                |> List.collect (fun vi ->
                    varIndices
                    |> List.filter (fun vj -> vj > vi)
                    |> List.collect (fun vj ->
                        [ ((vi, vj), lambda1); ((vj, vi), lambda1) ]))
                |> List.fold (fun a (key, value) -> Qubo.combineTerms key value a) acc) empty

        // --- Constraint 2: Bin capacity ---
        // For each bin j: lambda2 * (sum_i s_i * x_{ij} - C * y_j)^2
        // Let S_j = sum_i s_i * x_{ij} and Y = C * y_j.
        // (S_j - Y)^2 = S_j^2 - 2*S_j*Y + Y^2
        //
        // S_j^2 = (sum_i s_i * x_{ij})^2
        //       = sum_i s_i^2 * x_{ij} + 2 * sum_{i1<i2} s_{i1}*s_{i2} * x_{i1,j}*x_{i2,j}
        //   (since x_{ij}^2 = x_{ij} for binary)
        //
        // -2*S_j*Y = -2*C * sum_i s_i * x_{ij} * y_j
        //
        // Y^2 = C^2 * y_j  (since y_j^2 = y_j)
        let capacityTerms =
            [ 0 .. b - 1 ]
            |> List.fold (fun acc j ->
                let yj = binUsedIndex n b j
                let c = problem.BinCapacity

                // Y^2 = C^2 * y_j → diagonal on y_j
                let acc = acc |> Qubo.combineTerms (yj, yj) (lambda2 * c * c)

                // S_j^2 terms
                let acc =
                    [ 0 .. n - 1 ]
                    |> List.fold (fun a i ->
                        let xij = itemBinIndex b i j
                        let si = problem.Items.[i].Size
                        // s_i^2 * x_{ij} (diagonal)
                        a |> Qubo.combineTerms (xij, xij) (lambda2 * si * si)) acc

                let acc =
                    [ 0 .. n - 1 ]
                    |> List.collect (fun i1 ->
                        [ i1 + 1 .. n - 1 ]
                        |> List.map (fun i2 -> (i1, i2)))
                    |> List.fold (fun a (i1, i2) ->
                        let xi1j = itemBinIndex b i1 j
                        let xi2j = itemBinIndex b i2 j
                        let s1 = problem.Items.[i1].Size
                        let s2 = problem.Items.[i2].Size
                        // 2 * s_{i1} * s_{i2} * x_{i1,j} * x_{i2,j}
                        let value = lambda2 * 2.0 * s1 * s2
                        a |> Qubo.combineTerms (xi1j, xi2j) (value / 2.0)
                          |> Qubo.combineTerms (xi2j, xi1j) (value / 2.0)) acc

                // -2*S_j*Y terms: -2*C * s_i * x_{ij} * y_j
                let acc =
                    [ 0 .. n - 1 ]
                    |> List.fold (fun a i ->
                        let xij = itemBinIndex b i j
                        let si = problem.Items.[i].Size
                        let value = lambda2 * (-2.0) * c * si
                        if xij = yj then
                            a |> Qubo.combineTerms (xij, xij) value
                        else
                            a |> Qubo.combineTerms (xij, yj) (value / 2.0)
                              |> Qubo.combineTerms (yj, xij) (value / 2.0)) acc

                acc) empty

        // --- Constraint 3: Activation: y_j >= x_{ij} ---
        // Penalty: lambda3 * x_{ij} * (1 - y_j)
        //        = lambda3 * x_{ij} - lambda3 * x_{ij} * y_j
        let activationTerms =
            [ 0 .. n - 1 ]
            |> List.collect (fun i ->
                [ 0 .. b - 1 ] |> List.map (fun j -> (i, j)))
            |> List.fold (fun acc (i, j) ->
                let xij = itemBinIndex b i j
                let yj = binUsedIndex n b j
                // lambda3 * x_{ij} → diagonal
                let acc = acc |> Qubo.combineTerms (xij, xij) lambda3
                // -lambda3 * x_{ij} * y_j → off-diagonal
                let value = -lambda3
                if xij = yj then
                    acc |> Qubo.combineTerms (xij, xij) value
                else
                    acc |> Qubo.combineTerms (xij, yj) (value / 2.0)
                      |> Qubo.combineTerms (yj, xij) (value / 2.0)) empty

        // Combine all terms
        [ objectiveTerms; assignmentTerms; capacityTerms; activationTerms ]
        |> List.fold (fun combined termMap ->
            termMap |> Map.fold (fun acc key value ->
                Qubo.combineTerms key value acc) combined) Map.empty

    /// Validate a bin packing problem, returning Error if invalid.
    let private validateProblem (problem: Problem) : Result<unit, QuantumError> =
        if problem.Items.IsEmpty then
            Error (QuantumError.ValidationError ("items", "Problem has no items"))
        elif problem.BinCapacity <= 0.0 then
            Error (QuantumError.ValidationError ("binCapacity", "Bin capacity must be positive"))
        elif problem.Items |> List.exists (fun item -> item.Size <= 0.0) then
            Error (QuantumError.ValidationError ("itemSize", "All item sizes must be positive"))
        elif problem.Items |> List.exists (fun item -> item.Size > problem.BinCapacity) then
            Error (QuantumError.ValidationError ("itemSize", "Item size exceeds bin capacity"))
        else
            Ok ()

    /// Convert problem to dense QUBO matrix.
    /// Returns Result to follow the canonical pattern (validates inputs).
    let toQubo (problem: Problem) : Result<float[,], QuantumError> =
        match validateProblem problem with
        | Error err -> Error err
        | Ok () ->
            let b = computeMaxBins problem
            let totalVars = problem.Items.Length * b + b
            let quboMap = buildQuboMap problem b
            Ok (Qubo.toDenseArray totalVars quboMap)

    // ========================================================================
    // SOLUTION DECODING & VALIDATION
    // ========================================================================

    /// Decode a bitstring into item-bin assignments.
    let private decodeAssignments (problem: Problem) (numBins: int) (bits: int[]) : (Item * int) list =
        problem.Items
        |> List.indexed
        |> List.choose (fun (i, item) ->
            [ 0 .. numBins - 1 ]
            |> List.tryFind (fun j ->
                let idx = itemBinIndex numBins i j
                idx < bits.Length && bits.[idx] = 1)
            |> Option.map (fun j -> (item, j)))

    /// Check whether each bin's total load does not exceed capacity.
    let private binsWithinCapacity (problem: Problem) (assignments: (Item * int) list) : bool =
        assignments
        |> List.groupBy snd
        |> List.forall (fun (_, items) ->
            let totalSize = items |> List.sumBy (fun (item, _) -> item.Size)
            totalSize <= problem.BinCapacity + 1e-9)

    /// Validate a bitstring for this problem.
    /// Checks: correct length, each item in exactly one bin, no bin over capacity.
    let isValid (problem: Problem) (bits: int[]) : bool =
        let b = computeMaxBins problem
        let n = problem.Items.Length
        let expectedLen = n * b + b
        bits.Length = expectedLen
        && (
            let assignments = decodeAssignments problem b bits
            // Each item must be assigned exactly once
            assignments.Length = n
            && binsWithinCapacity problem assignments)

    /// Decode a bitstring into a Solution.
    let private decodeSolution (problem: Problem) (numBins: int) (bits: int[]) : Solution =
        let assignments = decodeAssignments problem numBins bits
        let allAssigned = assignments.Length = problem.Items.Length
        let withinCapacity = binsWithinCapacity problem assignments

        let binsUsed =
            if assignments.IsEmpty then 0
            else
                assignments
                |> List.map snd
                |> List.distinct
                |> List.length

        {
            Assignments = assignments
            BinsUsed = binsUsed
            IsValid = allAssigned && withinCapacity
            WasRepaired = false
            BackendName = ""
            NumShots = 0
            OptimizedParameters = None
            OptimizationConverged = None
        }

    // ========================================================================
    // CONSTRAINT REPAIR (recursive, idiomatic F#)
    // ========================================================================

    /// Repair an invalid bin packing solution.
    /// Strategy:
    ///   Phase 1: Ensure each item is assigned to exactly one bin (remove duplicates,
    ///            assign unassigned items using first-fit decreasing).
    ///   Phase 2: Fix overloaded bins by evicting smallest items and re-assigning them.
    ///   Phase 3: Build the output bitstring with correct bin-used indicators.
    let private repairConstraints (problem: Problem) (numBins: int) (bits: int[]) : int[] =
        let n = problem.Items.Length
        let b = numBins
        let cap = problem.BinCapacity

        // Phase 1: Extract current assignments, keeping first assignment per item
        let currentAssignments =
            problem.Items
            |> List.indexed
            |> List.choose (fun (i, _) ->
                [ 0 .. b - 1 ]
                |> List.tryFind (fun j ->
                    let idx = itemBinIndex b i j
                    idx < bits.Length && bits.[idx] = 1)
                |> Option.map (fun j -> (i, j)))
            |> Map.ofList

        // Compute bin loads from current assignments
        let binLoads = Array.zeroCreate<float> b
        currentAssignments
        |> Map.iter (fun itemIdx binIdx ->
            binLoads.[binIdx] <- binLoads.[binIdx] + problem.Items.[itemIdx].Size)

        // Phase 1b: Assign unassigned items using first-fit decreasing
        let unassigned =
            [ 0 .. n - 1 ]
            |> List.filter (fun i -> currentAssignments |> Map.containsKey i |> not)
            |> List.sortByDescending (fun i -> problem.Items.[i].Size)

        let phase1Assignments, _ =
            unassigned
            |> List.fold (fun (assignments: Map<int, int>, loads: float[]) itemIdx ->
                let itemSize = problem.Items.[itemIdx].Size
                let targetBin =
                    [ 0 .. b - 1 ]
                    |> List.tryFind (fun j -> loads.[j] + itemSize <= cap + 1e-9)
                match targetBin with
                | Some j ->
                    loads.[j] <- loads.[j] + itemSize
                    (assignments |> Map.add itemIdx j, loads)
                | None ->
                    let leastLoaded =
                        [ 0 .. b - 1 ]
                        |> List.minBy (fun j -> loads.[j])
                    loads.[leastLoaded] <- loads.[leastLoaded] + itemSize
                    (assignments |> Map.add itemIdx leastLoaded, loads)
            ) (currentAssignments, binLoads)

        // Phase 2: Fix overloaded bins by evicting items
        // Recompute loads from phase1 assignments
        let loads2 = Array.zeroCreate<float> b
        phase1Assignments
        |> Map.iter (fun itemIdx binIdx ->
            loads2.[binIdx] <- loads2.[binIdx] + problem.Items.[itemIdx].Size)

        // Find overloaded bins and evict smallest items until within capacity
        let evictedItems, updatedAssignments =
            [ 0 .. b - 1 ]
            |> List.fold (fun (evicted: int list, assignments: Map<int, int>) binJ ->
                if loads2.[binJ] <= cap + 1e-9 then
                    (evicted, assignments)
                else
                    // Get items in this bin, sorted by size ascending (evict smallest first)
                    let itemsInBin =
                        assignments
                        |> Map.toList
                        |> List.filter (fun (_, bj) -> bj = binJ)
                        |> List.sortBy (fun (iIdx, _) -> problem.Items.[iIdx].Size)

                    // Evict items until bin is within capacity
                    let rec evictUntilFit remaining load evictAcc assignAcc =
                        match remaining with
                        | [] -> (evictAcc, assignAcc)
                        | _ when load <= cap + 1e-9 ->
                            (evictAcc, assignAcc)
                        | (iIdx, _) :: rest ->
                            let newLoad = load - problem.Items.[iIdx].Size
                            loads2.[binJ] <- newLoad
                            evictUntilFit rest newLoad (iIdx :: evictAcc) (Map.remove iIdx assignAcc)

                    evictUntilFit itemsInBin loads2.[binJ] evicted assignments
            ) ([], phase1Assignments)

        // Re-assign evicted items using first-fit (sorted by size descending)
        let sortedEvicted =
            evictedItems |> List.sortByDescending (fun i -> problem.Items.[i].Size)

        let finalAssignments, _ =
            sortedEvicted
            |> List.fold (fun (assignments: Map<int, int>, loads: float[]) itemIdx ->
                let itemSize = problem.Items.[itemIdx].Size
                let targetBin =
                    [ 0 .. b - 1 ]
                    |> List.tryFind (fun j -> loads.[j] + itemSize <= cap + 1e-9)
                match targetBin with
                | Some j ->
                    loads.[j] <- loads.[j] + itemSize
                    (assignments |> Map.add itemIdx j, loads)
                | None ->
                    let leastLoaded =
                        [ 0 .. b - 1 ]
                        |> List.minBy (fun j -> loads.[j])
                    loads.[leastLoaded] <- loads.[leastLoaded] + itemSize
                    (assignments |> Map.add itemIdx leastLoaded, loads)
            ) (updatedAssignments, loads2)

        // Phase 3: Build output bitstring
        let totalVars = n * b + b
        let result = Array.zeroCreate totalVars

        // Set item-bin assignment bits
        finalAssignments
        |> Map.iter (fun itemIdx binIdx ->
            let idx = itemBinIndex b itemIdx binIdx
            if idx < totalVars then
                result.[idx] <- 1)

        // Set bin-used indicator bits
        let usedBins =
            finalAssignments
            |> Map.toSeq
            |> Seq.map snd
            |> Set.ofSeq

        [ 0 .. b - 1 ]
        |> List.iter (fun j ->
            let idx = binUsedIndex n b j
            if usedBins |> Set.contains j && idx < totalVars then
                result.[idx] <- 1)

        result

    // ========================================================================
    // DECOMPOSE / RECOMBINE HOOKS (Decision 10: identity stubs)
    // ========================================================================

    /// Decompose a problem into sub-problems. Currently identity (single problem).
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
            }
        | [ single ] -> single
        | _ -> solutions |> List.minBy (fun s -> s.BinsUsed)

    // ========================================================================
    // QUANTUM SOLVERS (Rule 1: IQuantumBackend required)
    // ========================================================================

    /// Solve bin packing using QAOA with full configuration control.
    let solveWithConfig
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (config: Config)
        : Result<Solution, QuantumError> =

        match validateProblem problem with
        | Error err -> Error err
        | Ok () ->
            let b = computeMaxBins problem
            match toQubo problem with
            | Error err -> Error err
            | Ok qubo ->
                let result =
                    if config.EnableOptimization then
                        executeQaoaWithOptimization backend qubo config
                        |> Result.map (fun (bits, optParams, converged) ->
                            (bits, Some optParams, Some converged))
                    else
                        executeQaoaWithGridSearch backend qubo config
                        |> Result.map (fun (bits, optParams) ->
                            (bits, Some optParams, None))

                match result with
                | Error err -> Error err
                | Ok (bits, optParams, converged) ->
                    let decoded = decodeSolution problem b bits
                    let needsRepair = not decoded.IsValid

                    let finalBits, wasRepaired =
                        if config.EnableConstraintRepair && needsRepair then
                            (repairConstraints problem b bits, true)
                        else
                            (bits, false)

                    let solution = decodeSolution problem b finalBits
                    Ok { solution with
                            BackendName = backend.Name
                            NumShots = config.FinalShots
                            WasRepaired = wasRepaired
                            OptimizedParameters = optParams
                            OptimizationConverged = converged }

    /// Solve bin packing using QAOA with default configuration.
    let solve
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (shots: int)
        : Result<Solution, QuantumError> =

        let config = { defaultConfig with FinalShots = shots }
        solveWithConfig backend problem config

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
            }
        else
            let sorted =
                problem.Items
                |> List.indexed
                |> List.sortByDescending (fun (_, item) -> item.Size)

            let assignments, binLoads =
                sorted
                |> List.fold (fun (assigns: (Item * int) list, loads: Map<int, float>) (_, item) ->
                    // Find first bin with enough space
                    let targetBin =
                        loads
                        |> Map.toSeq
                        |> Seq.tryFind (fun (_, load) ->
                            load + item.Size <= problem.BinCapacity + 1e-9)
                        |> Option.map fst

                    match targetBin with
                    | Some binIdx ->
                        let newLoad = loads.[binIdx] + item.Size
                        ((item, binIdx) :: assigns, loads |> Map.add binIdx newLoad)
                    | None ->
                        // Open a new bin
                        let newBin = if loads.IsEmpty then 0 else (loads |> Map.toSeq |> Seq.map fst |> Seq.max) + 1
                        ((item, newBin) :: assigns, loads |> Map.add newBin item.Size)
                ) ([], Map.empty)

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
            }
