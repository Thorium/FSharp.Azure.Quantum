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
    /// - ≤ 20 qubits: dense StateVector (amplitudes = sqrt(count/totalShots),
    ///   zero phase — measurement destroys phase information)
    /// - 21–31 qubits: SparseState — only observed outcomes carry amplitude
    ///   (≤ shots entries), avoiding the 2^n dense allocation
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
    let histogramToQuantumState (histogram: Map<string, int>) (numQubits: int) : QuantumState =
        // Parse bitstring (rightmost char = qubit 0) to basis state index
        // "00" → 0, "01" → 1, "10" → 2, "11" → 3
        let bitstringToIndex (bitstring: string) =
            let mutable index = 0
            for i in 0 .. bitstring.Length - 1 do
                if bitstring.[i] = '1' then
                    index <- index ||| (1 <<< (bitstring.Length - 1 - i))
            index

        let maxDenseQubits = 20   // StateVector: 2^n amplitudes
        let maxSparseQubits = 31  // SparseState: basis indices must fit Int32

        if numQubits > maxSparseQubits then
            // Normalize keys to the MeasurementHistogram convention
            // (leftmost char = qubit 0): left-pad, reverse, merge collisions.
            let normalized =
                histogram
                |> Map.fold (fun acc (bitstring: string) count ->
                    let padded = bitstring.PadLeft(numQubits, '0')
                    let key = String(Array.rev (padded.ToCharArray()))
                    let merged = (acc |> Map.tryFind key |> Option.defaultValue 0) + count
                    acc |> Map.add key merged) Map.empty
            QuantumState.MeasurementHistogram (normalized, numQubits)

        elif numQubits > maxDenseQubits then
            let totalShots =
                histogram |> Map.fold (fun acc _ count -> acc + count) 0 |> max 1 |> float
            // Merge counts per index first (keys of differing lengths can collide),
            // then take sqrt once per basis state.
            let countsByIndex =
                histogram
                |> Map.fold (fun acc (bitstring: string) count ->
                    let index = bitstringToIndex bitstring
                    let merged = (acc |> Map.tryFind index |> Option.defaultValue 0) + count
                    acc |> Map.add index merged) Map.empty
            let amplitudes =
                countsByIndex
                |> Map.map (fun _ count -> Complex(sqrt (float count / totalShots), 0.0))
            QuantumState.SparseState (amplitudes, numQubits)

        else
            let dimension = 1 <<< numQubits
            let totalShots =
                histogram
                |> Map.fold (fun acc _ count -> acc + count) 0
                |> max 1
                |> float

            let amplitudes = Array.create dimension Complex.Zero

            for kvp in histogram do
                let index = bitstringToIndex kvp.Key
                if index >= 0 && index < dimension then
                    // Approximate amplitude = sqrt(count / totalShots)
                    // Phase is unknown from measurements, so use real positive amplitudes
                    let amplitude = sqrt (float kvp.Value / totalShots)
                    amplitudes.[index] <- Complex(amplitude, 0.0)

            QuantumState.StateVector (StateVector.create amplitudes)

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
        |> Map.fold (fun acc (bitstring: string) count ->
            let len = bitstring.Length
            let logicalBits =
                Array.init numLogical (fun q ->
                    let physical = mapping.[q]
                    if physical < len then bitstring.[len - 1 - physical] else '0')
            let key = String(Array.rev logicalBits)
            let merged = (acc |> Map.tryFind key |> Option.defaultValue 0) + count
            acc |> Map.add key merged) Map.empty

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
    /// Cloud backends support gate operations, sequences, and measurement.
    /// They do NOT support topological operations (Braid, FMove).
    let isCloudSupportedOperation (op: BackendAbstraction.QuantumOperation) : bool =
        match op with
        | BackendAbstraction.QuantumOperation.Gate _ -> true
        | BackendAbstraction.QuantumOperation.Sequence _ -> true
        | BackendAbstraction.QuantumOperation.Measure _ -> true
        | BackendAbstraction.QuantumOperation.Algorithm _ -> true
        | _ -> false

    // ============================================================================
    // ERROR HELPERS
    // ============================================================================

    /// Create a standard "unsupported operation" error for cloud backends.
    let unsupportedOperationError (backendName: string) (op: BackendAbstraction.QuantumOperation) : QuantumError =
        QuantumError.OperationError(
            "ApplyOperation",
            $"%s{backendName} does not support operation type: %A{op}. Only Gate, Sequence, and Measure are supported.")

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
        | None -> Ok ()
        | Some limit ->
            match estimateCostSimple target shots with
            | Error _ -> Ok ()  // fail-open: a cost-estimation failure must not block submission
            | Ok estimate ->
                let expected = estimate.ExpectedCost / 1.0M<USD>
                if expected > limit then
                    Error (QuantumError.AzureError (AzureQuantumError.QuotaExceeded(
                        sprintf "Estimated job cost $%.2f exceeds the configured per-job limit $%.2f. Raise the limit or use a simulator target."
                            (float expected) (float limit))))
                else
                    Ok ()
