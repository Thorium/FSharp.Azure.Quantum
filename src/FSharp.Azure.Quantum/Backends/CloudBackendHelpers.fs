namespace FSharp.Azure.Quantum.Backends

open System
open System.Numerics
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Core

/// Shared helpers for cloud backend IQuantumBackend wrappers.
///
/// Provides histogram-to-QuantumState conversion and common utilities
/// used by RigettiCloudBackend, IonQCloudBackend, QuantinuumCloudBackend,
/// and AtomComputingCloudBackend.
module CloudBackendHelpers =

    // ============================================================================
    // HISTOGRAM → QUANTUM STATE CONVERSION
    // ============================================================================

    /// Convert a measurement histogram to an approximate QuantumState.StateVector.
    ///
    /// Cloud backends return Map<string, int> histograms (bitstring → count).
    /// We approximate amplitudes as sqrt(count / totalShots) with zero phase,
    /// since measurement destroys phase information.
    ///
    /// Parameters:
    ///   histogram - Map<bitstring, count> from cloud execution (e.g., {"00": 480, "11": 520})
    ///   numQubits - Number of qubits in the circuit
    ///
    /// Returns:
    ///   QuantumState.StateVector with approximate amplitudes
    ///
    /// Example:
    ///   {"00": 500, "11": 500} with 1000 shots, 2 qubits →
    ///   |ψ⟩ ≈ 0.707|00⟩ + 0.707|11⟩  (approximate Bell state)
    ///
    /// Limitations:
    ///   - Phase information is lost (all amplitudes are real and non-negative)
    ///   - Accuracy depends on number of shots (more shots = better approximation)
    ///   - For exact state reconstruction, use quantum state tomography
    let histogramToQuantumState (histogram: Map<string, int>) (numQubits: int) : QuantumState =
        let dimension = 1 <<< numQubits
        let totalShots =
            histogram
            |> Map.fold (fun acc _ count -> acc + count) 0
            |> float

        let amplitudes = Array.create dimension Complex.Zero

        for kvp in histogram do
            let bitstring = kvp.Key
            let count = kvp.Value

            // Parse bitstring to basis state index
            // "00" → 0, "01" → 1, "10" → 2, "11" → 3
            let mutable index = 0
            for i in 0 .. bitstring.Length - 1 do
                if bitstring.[i] = '1' then
                    index <- index ||| (1 <<< (bitstring.Length - 1 - i))

            if index >= 0 && index < dimension then
                // Approximate amplitude = sqrt(count / totalShots)
                // Phase is unknown from measurements, so use real positive amplitudes
                let amplitude = sqrt (float count / totalShots)
                amplitudes.[index] <- Complex(amplitude, 0.0)

        QuantumState.StateVector (StateVector.create amplitudes)

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
            sprintf "%s does not support operation type: %A. Only Gate, Sequence, and Measure are supported." backendName op)
