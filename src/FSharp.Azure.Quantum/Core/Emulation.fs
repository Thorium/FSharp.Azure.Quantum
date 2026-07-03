namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitValidator  // brings CircuitStats record labels into scope

/// Emulate a specific cloud target locally — the CUDA-Q `emulate=True` counterpart.
///
/// `Emulation.emulate target shots circuit`:
///   1. transpiles the circuit to the target's native gate set (`GateTranspiler`),
///   2. validates it against the target's constraints — qubit count, connectivity,
///      supported gates (`CircuitValidator.KnownTargets`),
///   3. runs the transpiled circuit on the local simulator and returns the histogram.
///
/// This lets you see what a hardware target would actually run and catch "won't fit this
/// device" locally — before spending money on a real job. It complements `Primitives`:
/// `Primitives.sample` runs a circuit as-is; `Emulation.emulate` runs it as the *target*
/// would, with a constraint report.
module Emulation =

    /// Result of locally emulating a hardware target.
    type EmulationReport = {
        /// The emulated target (e.g. "ionq.qpu.aria-1").
        Target: string
        /// Gate count after transpiling to the target's native gate set.
        NativeGateCount: int
        /// Whether constraint data is known for this target (false ⇒ validation was skipped).
        KnownTarget: bool
        /// Constraint violations found (empty ⇒ the circuit would run on the target as-is).
        ConstraintViolations: string list
        /// Measurement histogram from running the transpiled circuit on the local simulator.
        Counts: Map<string, int>
    }

    /// Measurement/reset/barrier/conditional are structural operations, not gate-set members —
    /// a target's SupportedGates never lists them, so including them in UsedGates produces bogus
    /// "unsupported gate" violations. Filter them out before validating against the gate set.
    let private isGateSetMember (g: CircuitBuilder.Gate) : bool =
        match g with
        | CircuitBuilder.Measure _ | CircuitBuilder.Reset _
        | CircuitBuilder.Barrier _ | CircuitBuilder.Conditional _ -> false
        | _ -> true

    let private twoQubitPair (g: CircuitBuilder.Gate) : (int * int) option =
        match g with
        | CircuitBuilder.CNOT (a, b) | CircuitBuilder.CZ (a, b) | CircuitBuilder.SWAP (a, b) -> Some (a, b)
        | CircuitBuilder.CP (a, b, _)  | CircuitBuilder.CRX (a, b, _) | CircuitBuilder.CRY (a, b, _)
        | CircuitBuilder.CRZ (a, b, _) | CircuitBuilder.RXX (a, b, _) | CircuitBuilder.RYY (a, b, _)
        | CircuitBuilder.RZZ (a, b, _) -> Some (a, b)
        | _ -> None

    let private statsOf (circuit: CircuitBuilder.Circuit) : CircuitStats =
        { NumQubits = circuit.QubitCount
          GateCount = List.length circuit.Gates
          Depth = None
          UsedGates =
              circuit.Gates
              |> List.filter isGateSetMember
              |> List.map CircuitBuilder.getGateName   // canonical names ("Rx"/"Ry"/"Rz"…) match the validator's SupportedGates
              |> Set.ofList
          TwoQubitGates = circuit.Gates |> List.choose twoQubitPair }

    /// Emulate `target` locally: transpile → validate → run on the local simulator.
    let emulate (target: string) (shots: int) (circuit: CircuitBuilder.Circuit) : QuantumResult<EmulationReport> =
        // 1. Transpile to the target's native gate set.
        let native = GateTranspiler.transpileForBackend target circuit
        let stats = statsOf native
        // 2. Validate against the target's constraints (if we know them).
        let known, violations =
            match CircuitValidator.KnownTargets.getConstraints target with
            | Some constraints ->
                match CircuitValidator.validateCircuit constraints stats with
                | Ok () -> true, []
                | Error errors -> true, errors |> List.map CircuitValidator.formatValidationError
            | None -> false, []
        // 3. Run the transpiled circuit on the local simulator.
        let backend = FSharp.Azure.Quantum.Backends.LocalBackend.LocalBackend() :> IQuantumBackend
        Primitives.sample backend native shots
        |> Result.map (fun counts ->
            { Target = target
              NativeGateCount = stats.GateCount
              KnownTarget = known
              ConstraintViolations = violations
              Counts = counts })
