namespace FSharp.Azure.Quantum.Backends

open System
open System.Numerics
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.CostEstimation

/// Shared helpers for cloud backend IQuantumBackend wrappers.
///
/// Provides histogram-to-QuantumState conversion and common utilities
/// used by RigettiCloudBackend, IonQCloudBackend, QuantinuumCloudBackend,
/// and AtomComputingCloudBackend.
module CloudBackendHelpers =

    // ============================================================================
    // HISTOGRAM → QUANTUM STATE CONVERSION
    // ============================================================================

    /// Convert a measurement histogram to a QuantumState. Three tiers:
    ///
    /// - up to StateVector.maxQubits: dense StateVector (amplitudes = sqrt(count/totalShots),
    ///   zero phase — measurement destroys phase information). That width is derived
    ///   from available memory, not fixed.
    /// - above that, through 31 qubits: SparseState — only observed outcomes carry
    ///   amplitude (≤ shots entries), avoiding the 2^n dense allocation
    /// - > 31 qubits: MeasurementHistogram — the honest sampled-data
    ///   representation with NO width limit (basis indices no longer fit Int32).
    ///   This is what makes wide cloud hardware usable through this path
    ///   (Quantinuum H2 56q, Rigetti Ankaa ~84q, IBM 127q+).
    ///
    /// Bitstring convention IN: rightmost char = qubit 0 (Azure histograms).
    /// MeasurementHistogram keys OUT use leftmost char = qubit 0 (the
    /// QuantumState convention), so keys are left-padded and reversed there.
    ///
    /// Parameters:
    ///   histogram - Map<bitstring, count> from cloud execution (e.g., {"00": 480, "11": 520})
    ///   numQubits - Number of qubits in the circuit
    ///
    /// The returned state carries the recorded counts (QuantumState.withRecordedCounts), so
    /// measuring it yields the job's own shots — never outcomes resampled from its amplitudes.
    let rec histogramToQuantumState (histogram: Map<string, int>) (numQubits: int) : QuantumState =
        buildHistogramState histogram numQubits
        |> QuantumState.withRecordedCounts (recordedCountsOf histogram numQubits)

    /// Histogram keys (rightmost char = qubit 0) in the QuantumState convention (character
    /// q = qubit q), with keys that collapse together merged.
    and private recordedCountsOf (histogram: Map<string, int>) (numQubits: int) : Map<string, int> =
        histogram
        |> Map.fold
            (fun acc (bitstring: string) count ->
                let padded = bitstring.PadLeft(numQubits, '0')
                let key = String(Array.rev (padded.ToCharArray()))
                let merged = (acc |> Map.tryFind key |> Option.defaultValue 0) + count
                acc |> Map.add key merged)
            Map.empty

    and private buildHistogramState (histogram: Map<string, int>) (numQubits: int) : QuantumState =
        // Parse bitstring (rightmost char = qubit 0) to basis state index
        // "00" → 0, "01" → 1, "10" → 2, "11" → 3
        let bitstringToIndex (bitstring: string) =
            let mutable index = 0

            for i in 0 .. bitstring.Length - 1 do
                if bitstring.[i] = '1' then
                    index <- index ||| (1 <<< (bitstring.Length - 1 - i))

            index

        // StateVector holds 2^n amplitudes, so its width depends on available memory.
        let maxDenseQubits = StateVector.maxQubits
        let maxSparseQubits = 31 // SparseState: basis indices must fit Int32

        if numQubits > maxSparseQubits then
            // Normalize keys to the MeasurementHistogram convention
            // (leftmost char = qubit 0): left-pad, reverse, merge collisions.
            let normalized =
                histogram
                |> Map.fold
                    (fun acc (bitstring: string) count ->
                        let padded = bitstring.PadLeft(numQubits, '0')
                        let key = String(Array.rev (padded.ToCharArray()))
                        let merged = (acc |> Map.tryFind key |> Option.defaultValue 0) + count
                        acc |> Map.add key merged)
                    Map.empty

            QuantumState.MeasurementHistogram(normalized, numQubits)

        elif numQubits > maxDenseQubits then
            let totalShots =
                histogram |> Map.fold (fun acc _ count -> acc + count) 0 |> max 1 |> float
            // Merge counts per index first (keys of differing lengths can collide),
            // then take sqrt once per basis state.
            let countsByIndex =
                histogram
                |> Map.fold
                    (fun acc (bitstring: string) count ->
                        let index = bitstringToIndex bitstring
                        let merged = (acc |> Map.tryFind index |> Option.defaultValue 0) + count
                        acc |> Map.add index merged)
                    Map.empty

            let amplitudes =
                countsByIndex
                |> Map.map (fun _ count -> Complex(sqrt (float count / totalShots), 0.0))

            QuantumState.SparseState(amplitudes, numQubits)

        else
            let dimension = 1 <<< numQubits

            let totalShots =
                histogram |> Map.fold (fun acc _ count -> acc + count) 0 |> max 1 |> float

            let amplitudes = Array.create dimension Complex.Zero

            for kvp in histogram do
                let index = bitstringToIndex kvp.Key

                if index >= 0 && index < dimension then
                    // Approximate amplitude = sqrt(count / totalShots)
                    // Phase is unknown from measurements, so use real positive amplitudes
                    let amplitude = sqrt (float kvp.Value / totalShots)
                    amplitudes.[index] <- Complex(amplitude, 0.0)

            QuantumState.StateVector(StateVector.create amplitudes)

    /// Undo the logical→physical qubit permutation introduced by routing on a
    /// measurement histogram, so results are reported in the caller's logical
    /// qubit order.
    ///
    /// `mapping.[logical] = physical`, as returned by `QubitRouting.route`.
    /// Bitstring keys follow the same convention as `histogramToQuantumState`:
    /// rightmost char = qubit 0. Physical qubits beyond the bitstring length are
    /// read as '0' (devices may report fewer qubits than the coupling map has),
    /// and distinct physical keys that collapse to the same logical key have
    /// their counts merged.
    let unrouteHistogram (mapping: int[]) (numLogical: int) (histogram: Map<string, int>) : Map<string, int> =
        histogram
        |> Map.fold
            (fun acc (bitstring: string) count ->
                let len = bitstring.Length

                let logicalBits =
                    Array.init numLogical (fun q ->
                        let physical = mapping.[q]

                        if physical < len then
                            bitstring.[len - 1 - physical]
                        else
                            '0')

                let key = String(Array.rev logicalBits)
                let merged = (acc |> Map.tryFind key |> Option.defaultValue 0) + count
                acc |> Map.add key merged)
            Map.empty

    /// Infer the number of qubits from histogram bitstring length.
    ///
    /// Takes the first key in the histogram and measures its string length.
    /// Returns None if the histogram is empty.
    let inferNumQubits (histogram: Map<string, int>) : int option =
        histogram
        |> Map.tryFindKey (fun _ _ -> true)
        |> Option.map (fun key -> key.Length)

    // ============================================================================
    // COMMON OPERATION SUPPORT
    // ============================================================================

    /// Check if a QuantumOperation is supported by gate-based cloud backends.
    ///
    /// Cloud backends run gate operations, sequences and measurement, as parts of a whole
    /// circuit submitted with ExecuteToState. They claim no algorithm intent (QFT, QPE, HHL,
    /// Grover…): an intent is applied natively through ApplyOperation, which cloud hardware
    /// refuses, so claiming one would steer planners onto a route that always fails. Nor do
    /// they support topological operations (Braid, FMove).
    let isCloudSupportedOperation (op: BackendAbstraction.QuantumOperation) : bool =
        match op with
        | BackendAbstraction.QuantumOperation.Gate _ -> true
        | BackendAbstraction.QuantumOperation.Sequence _ -> true
        | BackendAbstraction.QuantumOperation.Measure _ -> true
        | _ -> false

    // ============================================================================
    // TRANSPILATION (pre-conversion)
    // ============================================================================

    /// `circuit` in the native gates of the target named `backendName`
    /// (GateTranspiler.transpileForBackendFully, which matches "ionq", "rigetti", "quantinuum",
    /// "atom" in the name and decomposes every non-elementary gate for any other name). Cloud
    /// backends call this before converting to the provider format, so T/TDG, CP, CRZ, CCX,
    /// MCZ and the other composite gates reach every provider as gates it accepts. A circuit
    /// that is not a gate circuit is returned unchanged for the converter to reject.
    let transpileForTarget (backendName: string) (circuit: CircuitAbstraction.ICircuit) : CircuitAbstraction.ICircuit =
        match CircuitAbstraction.CircuitAdapter.tryGetCircuit circuit with
        | Some gateCircuit ->
            FSharp.Azure.Quantum.GateTranspiler.transpileForBackendFully backendName gateCircuit
            |> CircuitAbstraction.wrapCircuit
        | None -> circuit

    /// `circuit` with every qubit measured at the end when it measures none of them.
    ///
    /// Algorithms hand cloud backends unitary circuits and read the returned counts, but a
    /// provider reports only what the program measures: a Quil program that DECLAREs `ro`
    /// and never MEASUREs into it, or OpenQASM with no `measure`, comes back with an empty or
    /// all-zero readout. Qubit q is measured into classical bit q. A circuit that measures
    /// anything already chose its readout and is left alone. IonQ's JSON format measures
    /// every qubit implicitly and does not need this.
    let withTerminalMeasurements (circuit: CircuitAbstraction.ICircuit) : CircuitAbstraction.ICircuit =
        match CircuitAbstraction.CircuitAdapter.tryGetCircuit circuit with
        | Some gateCircuit when
            gateCircuit.Gates
            |> List.forall (function
                | FSharp.Azure.Quantum.CircuitBuilder.Measure _ -> false
                | _ -> true)
            ->
            // Gates are stored most-recent-first.
            let measurements =
                List.init gateCircuit.QubitCount FSharp.Azure.Quantum.CircuitBuilder.Measure |> List.rev

            CircuitAbstraction.wrapCircuit
                { gateCircuit with
                    Gates = measurements @ gateCircuit.Gates
                }
        | _ -> circuit

    // ============================================================================
    // JOB BUDGET
    // ============================================================================

    /// Counts the jobs cloud backends submit and, when MaxJobs is set, refuses the job after
    /// the last one allowed. Every ExecuteToState on a cloud backend is one separately queued
    /// and billed job, and iterative algorithms submit one per energy or per sample (a 3-city
    /// TSP by QAOA is several hundred), so a limit set before the run bounds the bill. Share
    /// one budget between backends to bound a run that uses several. Thread-safe.
    type JobBudget(maxJobs: int option) =
        let submitted = ref 0

        /// Budget that counts but never refuses.
        new() = JobBudget(None)

        /// Most jobs allowed; None = no limit.
        member _.MaxJobs = maxJobs

        /// Jobs reserved so far (each became a submission attempt).
        member _.Submitted = Threading.Volatile.Read(&submitted.contents)

        /// Jobs left before the limit; None = no limit.
        member this.Remaining = maxJobs |> Option.map (fun m -> max 0 (m - this.Submitted))

        /// Reserve one job for `backendName`: Ok, or a QuotaExceeded error once MaxJobs jobs
        /// have been reserved (the refused job is not counted).
        member _.TryReserve(backendName: string) : Result<unit, QuantumError> =
            let count = Threading.Interlocked.Increment(&submitted.contents)

            match maxJobs with
            | Some limit when count > limit ->
                Threading.Interlocked.Decrement(&submitted.contents) |> ignore

                Error(
                    QuantumError.AzureError(
                        AzureQuantumError.QuotaExceeded(
                            $"%s{backendName}: the job budget of %d{limit} jobs is used up. Each ExecuteToState is one cloud job; raise MaxJobs on the JobBudget or reduce the algorithm's iterations or samples."
                        )
                    )
                )
            | _ -> Ok()

        /// A budget allowing at most `maxJobs` jobs.
        static member Limit(maxJobs: int) = JobBudget(Some(max 0 maxJobs))

    /// A backend that submits separately billed jobs and counts them in a JobBudget.
    type IJobCountingBackend =
        inherit BackendAbstraction.IQuantumBackend
        /// The budget this backend reserves each job from.
        abstract member JobBudget: JobBudget

    // ============================================================================
    // ERROR HELPERS
    // ============================================================================

    /// Create a standard "unsupported operation" error for cloud backends.
    let unsupportedOperationError (backendName: string) (op: BackendAbstraction.QuantumOperation) : QuantumError =
        QuantumError.OperationError(
            "ApplyOperation",
            $"%s{backendName} does not support operation type: %A{op}. Only Gate, Sequence, and Measure are supported."
        )

    // ============================================================================
    // COST GUARD (pre-submission)
    // ============================================================================

    /// Pre-submission cost guard for cloud (QPU) execution.
    ///
    /// Estimates the job cost from the target and shot count and rejects the
    /// submission when the expected cost exceeds the caller-supplied per-job
    /// limit (USD). Behaviour:
    ///   • costLimitUsd = None  → no-op (guard disabled — default for all backends)
    ///   • estimation fails     → fail-open (an estimator error never blocks a job)
    ///   • simulator targets    → estimated at $0, so are never blocked
    ///
    /// Returns Ok () when the job may proceed, or a QuotaExceeded error otherwise.
    let checkCostGuard (target: string) (shots: int) (costLimitUsd: decimal option) : Result<unit, QuantumError> =
        match costLimitUsd with
        | None -> Ok()
        | Some limit ->
            match estimateCostSimple target shots with
            | Error _ -> Ok() // fail-open: a cost-estimation failure must not block submission
            | Ok estimate ->
                let expected = estimate.ExpectedCost / 1.0M<USD>

                if expected > limit then
                    Error(
                        QuantumError.AzureError(
                            AzureQuantumError.QuotaExceeded(
                                sprintf
                                    "Estimated job cost $%.2f exceeds the configured per-job limit $%.2f. Raise the limit or use a simulator target."
                                    (float expected)
                                    (float limit)
                            )
                        )
                    )
                else
                    Ok()
