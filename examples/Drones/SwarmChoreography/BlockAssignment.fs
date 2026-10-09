// Copyright (c) 2025 FSharp.Azure.Quantum Contributors
// SPDX-License-Identifier: MIT

/// Assigning more drones to slots than one circuit holds.
///
/// An assignment of n drones to n slots is n² qubits as one QUBO, so a backend of q qubits
/// holds ⌊√q⌋ drones in one circuit: 4 at 20 qubits, 8 at 80, 31 at 1000. Beyond that the
/// assignment is improved one block of drones at a time
/// (QuboSplitting.searchNeighbourhoodsAsync). A block is k drones and the k slots they
/// hold, and a QAOA sample that permutes those k drones among those k slots keeps every
/// other drone's slot, so every valid block sample is a valid assignment of the whole
/// swarm, and no QUBO over the whole swarm ever exists. The work is linear in the swarm: a
/// sweep samples ⌈n/k⌉ blocks of k² qubits, and the blocks of the next sweep start half a
/// block further along, so improvements cross block borders.
///
/// The start is bookkeeping, not a solver: drones and slots are each sorted along a
/// space-filling curve (Morton order, O(n log n)) and paired in that order, which keeps
/// neighbours together, and a block is k consecutive drones in that order. The objective
/// and the safety gate are the caller's, scored on the whole assignment, so the block
/// QUBO's approximation never decides alone. The search keeps the best sample it sees; it
/// is a heuristic, not an exact split.
module FSharp.Azure.Quantum.Examples.Drones.SwarmChoreography.BlockAssignment

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Core.CircuitAbstraction

/// A point in metres.
[<Struct>]
type Point = { X: float; Y: float; Z: float }

/// The bits of a 16-bit value spread three apart, so the keys of three axes interleave.
let private spread (v: uint64) : uint64 =
    let mutable key = 0UL

    for b in 0..15 do
        key <- key ||| (((v >>> b) &&& 1UL) <<< (3 * b))

    key

/// The points in Morton order (along a Z-order curve over their bounding box, each axis
/// quantised to 16 bits): indices into `points`, ties by index.
let mortonOrder (points: Point[]) : int[] =
    let axis (coordinate: Point -> float) =
        let values = points |> Array.map coordinate

        if values.Length = 0 then
            values |> Array.map uint64
        else
            let low = Array.min values
            let high = Array.max values
            let span = if high > low then high - low else 1.0
            values |> Array.map (fun v -> uint64 (Math.Round((v - low) / span * 65535.0)))

    let xs = axis (fun p -> p.X)
    let ys = axis (fun p -> p.Y)
    let zs = axis (fun p -> p.Z)

    Array.init points.Length id
    |> Array.sortBy (fun i -> spread xs.[i] ||| (spread ys.[i] <<< 1) ||| (spread zs.[i] <<< 2), i)

/// The assignment (drone → slot) that pairs the drones and the slots in Morton order.
let startAssignment (drones: Point[]) (slots: Point[]) : int[] =
    let assignment = Array.zeroCreate drones.Length
    Array.iter2 (fun drone slot -> assignment.[drone] <- slot) (mortonOrder drones) (mortonOrder slots)
    assignment

/// The blocks of a sweep: `order` from `sweep` × ⌈k/2⌉ places on, wrapping around, in runs
/// of `blockDrones` (the last block shorter when they do not divide).
let blocksOf (blockDrones: int) (sweep: int) (order: int[]) : int[][] =
    let n = order.Length
    let k = max 1 blockDrones

    if n = 0 then
        [||]
    else
        let offset = (sweep * ((k + 1) / 2)) % n
        Array.append order.[offset..] order.[.. offset - 1] |> Array.chunkBySize k

/// The QUBO of the assignment problem over `cost` (row: drone, column: slot): variable
/// x[i,j] = 1 when drone i takes slot j, the cost on the diagonal, and `penaltyWeight` ×
/// (Σ_j x[i,j] − 1)² for every drone and (Σ_i x[i,j] − 1)² for every slot.
let assignmentQubo (penaltyWeight: float) (cost: float[,]) : float[,] =
    let n = Array2D.length1 cost
    let numVars = n * n
    let q = Array2D.zeroCreate<float> numVars numVars
    let varIndex i j = i * n + j

    // Objective: the cost of every pair
    for i in 0 .. n - 1 do
        for j in 0 .. n - 1 do
            let idx = varIndex i j
            q.[idx, idx] <- cost.[i, j]

    // Each drone takes exactly one slot
    for i in 0 .. n - 1 do
        for j in 0 .. n - 1 do
            let idx = varIndex i j
            q.[idx, idx] <- q.[idx, idx] - penaltyWeight

        for j1 in 0 .. n - 1 do
            for j2 in j1 + 1 .. n - 1 do
                let idx1 = varIndex i j1
                let idx2 = varIndex i j2
                q.[idx1, idx2] <- q.[idx1, idx2] + 2.0 * penaltyWeight
                q.[idx2, idx1] <- q.[idx2, idx1] + 2.0 * penaltyWeight

    // Each slot is taken by exactly one drone
    for j in 0 .. n - 1 do
        for i in 0 .. n - 1 do
            let idx = varIndex i j
            q.[idx, idx] <- q.[idx, idx] - penaltyWeight

        for i1 in 0 .. n - 1 do
            for i2 in i1 + 1 .. n - 1 do
                let idx1 = varIndex i1 j
                let idx2 = varIndex i2 j
                q.[idx1, idx2] <- q.[idx1, idx2] + 2.0 * penaltyWeight
                q.[idx2, idx1] <- q.[idx2, idx1] + 2.0 * penaltyWeight

    q

/// The permutation (row → column) a one-hot k × k sample encodes, or ValueNone when a row
/// or a column has other than one bit set.
let decodePermutation (k: int) (bits: int[]) : int[] voption =
    if bits.Length < k * k then
        ValueNone
    else
        let permutation = Array.create k -1
        let taken = Array.zeroCreate<bool> k
        let mutable valid = true

        for a in 0 .. k - 1 do
            for b in 0 .. k - 1 do
                if bits.[a * k + b] = 1 then
                    if permutation.[a] >= 0 || taken.[b] then
                        valid <- false
                    else
                        permutation.[a] <- b
                        taken.[b] <- true

        if valid && permutation |> Array.forall (fun b -> b >= 0) then
            ValueSome permutation
        else
            ValueNone

/// The problem for QuboSplitting.searchNeighbourhoodsAsync. A solution is the slot of every
/// drone; a neighbourhood is a block of `blockDrones` drones: first one around each pair
/// `steer` names for the solution so far (the pair and the first drone's neighbours in
/// Morton order, worst pair first, at most as many as the partition has blocks), then the
/// blocks of drones consecutive in Morton order (blocksOf). A block's QUBO is the
/// assignment of its drones to the slots they hold (assignmentQubo, the penalty 2 × k ×
/// the largest cost in the block, the Lucas rule the one-circuit solver uses); a sample
/// that is a permutation moves the block's drones among those slots; the score is the
/// total cost; `accept` is the caller's gate.
let neighbourhoods
    (blockDrones: int)
    (cost: int -> int -> float)
    (drones: Point[])
    (accept: int[] -> bool)
    (steer: int[] -> (int * int)[])
    : QuboSplitting.Neighbourhoods<int[], int[]> =
    let n = drones.Length
    let k = max 1 blockDrones
    let order = mortonOrder drones
    let rank = Array.zeroCreate<int> n
    order |> Array.iteri (fun position drone -> rank.[drone] <- position)

    // The pair and the first drone's nearest in Morton order, alternating sides, up to k
    let around (first: int, second: int) : int[] =
        let block = ResizeArray<int>([ first; second ] |> List.distinct)

        for step in 1 .. n - 1 do
            for position in [ rank.[first] - step; rank.[first] + step ] do
                if block.Count < k && position >= 0 && position < n && not (block.Contains order.[position]) then
                    block.Add order.[position]

        block.ToArray() |> Array.sort

    {
        OfSweep =
            fun sweep assignment ->
                let partition = blocksOf blockDrones sweep order

                let steered =
                    steer assignment
                    |> Array.truncate partition.Length
                    |> Array.map around
                    |> Array.distinct

                Array.append steered partition
        Qubo =
            fun assignment block ->
                let k = block.Length
                let costs = Array2D.init k k (fun a b -> cost block.[a] assignment.[block.[b]])
                let largest = costs |> Seq.cast<float> |> Seq.max
                let penalty = if largest > 0.0 then largest * float k * 2.0 else 1.0
                assignmentQubo penalty costs
        Apply =
            fun assignment block bits ->
                match decodePermutation block.Length bits with
                | ValueNone -> ValueNone
                | ValueSome permutation ->
                    let moved = Array.copy assignment
                    permutation |> Array.iteri (fun a b -> moved.[block.[a]] <- assignment.[block.[b]])
                    ValueSome moved
        Score = fun assignment -> assignment |> Array.mapi cost |> Array.sum
        Accept = accept
    }

/// The fixed QAOA angles of a block (p = 1 on the normalised Hamiltonian, as the one-circuit
/// solver's).
let private angles = [| (0.5, 0.3) |]

/// The samples of a block QUBO on the backend: a fixed-angle QAOA circuit measured `shots`
/// times, retried on QaoaAttempts.schedule while no sample is a permutation; the samples
/// of the first attempt that has one, or of the last attempt. Every circuit run is counted
/// in `circuits`. An execution error is the backend's.
let sampleBlock
    (backend: IQuantumBackend)
    (shots: int)
    (circuits: int ref)
    (qubo: float[,])
    : Task<Result<int[][], QuantumError>> =
    let width = Array2D.length1 qubo
    let k = int (Math.Round(sqrt (float width)))
    let problemHam = ProblemHamiltonian.fromQubo qubo |> ProblemHamiltonian.normalize
    let mixerHam = MixerHamiltonian.create width

    let run (attemptShots: int) (attemptAngles: (float * float)[]) =
        circuits.Value <- circuits.Value + 1

        let circuit =
            QaoaCircuitWrapper(QaoaCircuit.build problemHam mixerHam attemptAngles) :> ICircuit

        backend.ExecuteToState circuit
        |> Result.map (fun state -> QuantumState.measure state attemptShots)

    let rec go (attempts: (int * (float * float)[]) list) (last: Result<int[][], QuantumError>) =
        match attempts with
        | [] -> last
        | (attemptShots, attemptAngles) :: rest ->
            match run attemptShots attemptAngles with
            | Error err -> Error err
            | Ok samples when samples |> Array.exists (fun bits -> (decodePermutation k bits).IsSome) -> Ok samples
            | Ok samples -> go rest (Ok samples)

    Task.FromResult(go (QaoaAttempts.schedule shots angles) (Ok [||]))

/// The most drones a block may hold on the backend, one k² qubit circuit each, or None when
/// the backend reports no limit.
let widestBlock (backend: IQuantumBackend) : int option =
    UnifiedBackend.getRunnableQubits backend
    |> Option.map (fun limit -> int (floor (sqrt (float limit))))

/// Assign `drones` to as many `slots` by a block search: from the Morton-order pairing,
/// `maxSweeps` sweeps at most over blocks of `blockDrones` drones (the partition of a sweep
/// and, before it, a block around each pair `steer` names for the assignment so far, so
/// the search can reach `accept`), every block a QAOA circuit of blockDrones² qubits on
/// the backend (sampleBlock), lowering the total `cost` and taking only assignments
/// `accept` takes once it holds one. The assignment (drone → slot) and how it was found:
/// drones, blocks, sweeps, runs, circuits with retries, improvements, and the cost of the
/// start pairing against the result. An Error names a backend failure, or that no accepted
/// assignment was found; no classical solver stands in.
let solve
    (backend: IQuantumBackend)
    (shots: int)
    (blockDrones: int)
    (maxSweeps: int)
    (accept: int[] -> bool)
    (steer: int[] -> (int * int)[])
    (cost: int -> int -> float)
    (drones: Point[])
    (slots: Point[])
    : Result<int[] * string, string> =
    let n = drones.Length
    let k = min blockDrones n

    // The backend's limit when a block's circuit would pass it
    let tooWide =
        match UnifiedBackend.getRunnableQubits backend with
        | Some limit when k * k > limit -> Some limit
        | _ -> None

    if slots.Length <> n then
        Error $"%d{n} drones and %d{slots.Length} slots: one slot per drone is required"
    elif n = 0 then
        Ok([||], "no drones")
    elif blockDrones < 2 then
        Error $"a block of %d{blockDrones} drones cannot move any; at least 2 (4 qubits) are needed"
    elif tooWide.IsSome then
        Error
            $"a block of %d{k} drones is a circuit of %d{k * k} qubits; backend '%s{backend.Name}' runs at most %d{tooWide.Value}"
    else
        let blocks = (n + k - 1) / k
        let sweeps = max 1 maxSweeps
        let start = startAssignment drones slots
        let problem = neighbourhoods k cost drones accept steer
        let circuits = ref 0

        // A sweep: the partition and up to as many steered blocks
        let limits: QuboSplitting.NeighbourhoodLimits =
            {
                MaxPieceQubits = k * k
                MaxSweeps = sweeps
                MaxRuns = 2 * blocks * sweeps
            }

        let search =
            QuboSplitting.searchNeighbourhoodsAsync
                limits
                problem
                start
                (sampleBlock backend shots circuits)
                CancellationToken.None

        match search.GetAwaiter().GetResult() with
        | Error err -> Error err.Message
        | Ok found ->
            Ok(
                found.Solution,
                sprintf
                    "block search: %d drones in %d blocks of %d (%d qubits) plus steered blocks, %d sweeps, %d runs (%d circuits with retries), %d improvements, cost %.2f at the start pairing, %.2f found"
                    n
                    blocks
                    k
                    (k * k)
                    found.Sweeps
                    found.Runs
                    circuits.Value
                    found.Improvements
                    (problem.Score start)
                    found.Score
            )
