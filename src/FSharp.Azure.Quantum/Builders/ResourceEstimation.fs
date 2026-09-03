namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.CircuitBuilder

/// Quantum resource estimation.
///
/// Estimates the resources a circuit needs, at two levels:
///   * Logical  — algorithm-level counts (qubits, gates, T-count, depth), reusing
///                `CircuitBuilder.statistics`/`depth`.
///   * Physical — a fault-tolerant estimate under a standard surface-code model
///                (code distance, physical qubits, runtime). This is the same kind
///                of estimate the Azure Quantum Resource Estimator produces, but
///                computed locally (no cloud call) from a transparent model, so it
///                is fully reproducible and testable.
module ResourceEstimation =

    /// Algorithm-level (logical) resource counts.
    type LogicalResources =
        { LogicalQubits: int
          TotalGates: int
          SingleQubitGates: int
          TwoQubitGates: int
          /// T + T-dagger gates — the dominant cost in fault-tolerant computing.
          TCount: int
          MeasurementCount: int
          Depth: int
          GateTypeCounts: Map<string, int> }

    /// Physical fault-tolerant resource estimate under a surface-code model.
    type PhysicalResources =
        { /// Surface-code distance chosen to meet the target error budget.
          CodeDistance: int
          /// Physical qubits per logical qubit (~2·d² for a rotated surface-code patch).
          PhysicalQubitsPerLogical: int
          TotalPhysicalQubits: int
          EstimatedRuntimeSeconds: float
          /// Per-logical-qubit, per-cycle logical error actually achieved at this distance.
          LogicalErrorRateAchieved: float }

    /// Hardware / fault-tolerance assumptions used for the physical estimate.
    [<Struct>]
    type FaultToleranceParams =
        { /// Physical error rate per physical operation (e.g. 1e-3).
          PhysicalErrorRate: float
          /// Surface-code error threshold (~1e-2).
          ErrorThreshold: float
          /// Acceptable total failure probability for the whole computation.
          TargetErrorRate: float
          /// Syndrome-extraction cycle time in seconds (e.g. 1e-6).
          CycleTimeSeconds: float }

    /// Reasonable defaults for a superconducting-style device.
    let defaultFaultToleranceParams =
        { PhysicalErrorRate = 1e-3
          ErrorThreshold = 1e-2
          TargetErrorRate = 1e-2
          CycleTimeSeconds = 1e-6 }

    type ResourceEstimate =
        { Logical: LogicalResources
          Physical: PhysicalResources }

    /// Estimate the logical (algorithm-level) resources for a circuit.
    let estimateLogical (circuit: Circuit) : LogicalResources =
        let stats = statistics circuit
        let countOf name = stats.GateTypeCounts |> Map.tryFind name |> Option.defaultValue 0
        { LogicalQubits = max circuit.QubitCount (stats.MaxQubitIndex + 1)
          TotalGates = stats.TotalGates
          SingleQubitGates = stats.SingleQubitGates
          TwoQubitGates = stats.TwoQubitGates
          TCount = countOf "T" + countOf "TDG"
          MeasurementCount = stats.MeasurementCount
          Depth = depth circuit
          GateTypeCounts = stats.GateTypeCounts }

    /// Estimate physical fault-tolerant resources from logical resources under a
    /// surface-code model. The required per-cycle logical error is the total error
    /// budget shared across all logical qubits and all cycles; the code distance is
    /// the smallest odd distance whose suppressed logical error meets it.
    let estimatePhysical (ftp: FaultToleranceParams) (logical: LogicalResources) : PhysicalResources =
        let nLogical = max 1 logical.LogicalQubits
        // One round of error correction per unit of logical depth.
        let cycles = max 1 logical.Depth
        let requiredLogicalError = ftp.TargetErrorRate / float (nLogical * cycles)
        // Surface-code suppression: p_L(d) ≈ 0.03 · (p/p_th)^((d+1)/2).
        let ratio = ftp.PhysicalErrorRate / ftp.ErrorThreshold
        let logicalErrorAt d = 0.03 * (ratio ** (float (d + 1) / 2.0))
        // Smallest odd distance meeting the budget (capped; if p >= threshold the
        // code does not suppress and we report the capped distance honestly).
        let mutable d = 3
        while logicalErrorAt d > requiredLogicalError && d < 101 do
            d <- d + 2
        let perLogical = 2 * d * d
        { CodeDistance = d
          PhysicalQubitsPerLogical = perLogical
          TotalPhysicalQubits = nLogical * perLogical
          EstimatedRuntimeSeconds = float cycles * float d * ftp.CycleTimeSeconds
          LogicalErrorRateAchieved = logicalErrorAt d }

    /// Full estimate (logical + physical) for a circuit with explicit assumptions.
    let estimate (ftp: FaultToleranceParams) (circuit: Circuit) : ResourceEstimate =
        let logical = estimateLogical circuit
        { Logical = logical
          Physical = estimatePhysical ftp logical }

    /// Full estimate using the default fault-tolerance assumptions.
    let estimateDefault (circuit: Circuit) : ResourceEstimate =
        estimate defaultFaultToleranceParams circuit

    /// Human-readable summary of a resource estimate.
    let describe (est: ResourceEstimate) : string =
        let l = est.Logical
        let p = est.Physical
        sprintf
            "Resource Estimate\n\
             ─────────────────\n\
             Logical qubits:        %d\n\
             Total gates:           %d (1q: %d, 2q: %d)\n\
             T-count:               %d\n\
             Circuit depth:         %d\n\
             ─────────────────\n\
             Code distance:         %d\n\
             Physical qubits:       %d (%d per logical)\n\
             Est. runtime:          %.3e s\n\
             Logical error/cycle:   %.3e"
            l.LogicalQubits l.TotalGates l.SingleQubitGates l.TwoQubitGates l.TCount l.Depth
            p.CodeDistance p.TotalPhysicalQubits p.PhysicalQubitsPerLogical
            p.EstimatedRuntimeSeconds p.LogicalErrorRateAchieved
