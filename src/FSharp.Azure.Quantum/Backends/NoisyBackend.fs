namespace FSharp.Azure.Quantum.Backends

open System.Numerics
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.LocalSimulator

/// Density-matrix simulator with a per-gate depolarizing noise channel.
///
/// Unlike the pure state-vector `LocalBackend`, this evolves a full density matrix ρ so it
/// can model *mixed* states produced by noise: after each gate it applies a single-qubit
/// depolarizing channel to the gate's qubits. Use it to predict how hardware noise degrades
/// a circuit's measurement statistics.
///
/// Implementation note: `U ρ U†` is computed by applying the gate to each column of ρ with
/// the state-vector simulator (`LocalBackend.ApplyOperation`), so gate semantics and qubit
/// ordering match the noiseless simulator exactly. This is O(gates · 2^(2n)); intended for
/// small circuits (≤ 8 qubits), the same regime as exact noisy simulation elsewhere.
module DensityMatrixSimulator =

    /// Depolarizing-noise configuration. Probabilities in [0, 1]; 0 = noiseless.
    type NoiseConfig = {
        /// Depolarizing probability applied to the qubit of each single-qubit gate.
        SingleQubitDepolarizing: float
        /// Depolarizing probability applied to each qubit of a multi-qubit gate.
        TwoQubitDepolarizing: float
    }

    /// Noiseless configuration (recovers the pure-state result as a density matrix).
    let noiseless = { SingleQubitDepolarizing = 0.0; TwoQubitDepolarizing = 0.0 }

    /// Depolarizing model with single- and two-qubit gate error probabilities.
    let depolarizing (singleQubit: float) (twoQubit: float) : NoiseConfig =
        { SingleQubitDepolarizing = singleQubit; TwoQubitDepolarizing = twoQubit }

    let private maxQubits = 8

    /// Qubits a gate acts on — delegates to CircuitBuilder so every Gate case
    /// (including Reset, Barrier and Conditional) is covered.
    let private qubitsOf (g: CircuitBuilder.Gate) : int list =
        CircuitBuilder.getAffectedQubits g

    let private conjTranspose (dim: int) (m: Complex[,]) : Complex[,] =
        Array2D.init dim dim (fun i j -> Complex.Conjugate m.[j, i])

    /// Apply the unitary U of `gate` to every column of ρ, i.e. compute U·ρ, by running the
    /// gate on each column with the state-vector simulator (guaranteeing identical semantics).
    let private applyGateColumns (backend: IQuantumBackend) (dim: int) (gate: CircuitBuilder.Gate) (rho: Complex[,]) : Complex[,] =
        let result = Array2D.zeroCreate dim dim
        for j in 0 .. dim - 1 do
            let column = Array.init dim (fun i -> rho.[i, j])
            let state = QuantumState.StateVector (StateVector.create column)
            match backend.ApplyOperation (QuantumOperation.Gate gate) state with
            | Ok (QuantumState.StateVector sv) ->
                for i in 0 .. dim - 1 do result.[i, j] <- StateVector.getAmplitude i sv
            | Ok _ -> failwith "density-matrix simulator: expected a state-vector result from gate application"
            | Error e -> failwith $"density-matrix simulator: gate application failed: {e.Message}"
        result

    /// U ρ U†  (ρ Hermitian): apply gate to columns of ρ, conjugate-transpose, apply again.
    let private conjugateByGate (backend: IQuantumBackend) (dim: int) (gate: CircuitBuilder.Gate) (rho: Complex[,]) : Complex[,] =
        rho |> applyGateColumns backend dim gate |> conjTranspose dim |> applyGateColumns backend dim gate

    /// Single-qubit depolarizing channel on qubit q: ρ → (1-p)ρ + (p/3)(XρX + YρY + ZρZ).
    let private depolarize (backend: IQuantumBackend) (dim: int) (q: int) (p: float) (rho: Complex[,]) : Complex[,] =
        if p <= 0.0 then rho
        else
            let xr = conjugateByGate backend dim (CircuitBuilder.X q) rho
            let yr = conjugateByGate backend dim (CircuitBuilder.Y q) rho
            let zr = conjugateByGate backend dim (CircuitBuilder.Z q) rho
            let keep = Complex(1.0 - p, 0.0)
            let share = Complex(p / 3.0, 0.0)
            Array2D.init dim dim (fun i j -> keep * rho.[i, j] + share * (xr.[i, j] + yr.[i, j] + zr.[i, j]))

    /// Simulate a circuit under the depolarizing noise model, returning (ρ, numQubits).
    let simulate (config: NoiseConfig) (circuit: CircuitBuilder.Circuit) : Result<Complex[,] * int, QuantumError> =
        let n = circuit.QubitCount
        if n > maxQubits then
            Error (QuantumError.ValidationError ("numQubits",
                $"Density-matrix simulation is limited to {maxQubits} qubits (a {1 <<< maxQubits}×{1 <<< maxQubits} matrix); got {n}."))
        else
            // Column-wise gate application can fail (e.g. a gate referencing a qubit ≥ QubitCount,
            // which addGate does not validate); catch it so we honour the Result contract.
            try
                let dim = 1 <<< n
                let backend = LocalBackend.LocalBackend() :> IQuantumBackend
                let rho0 = Array2D.zeroCreate dim dim
                rho0.[0, 0] <- Complex.One   // |0…0⟩⟨0…0|
                let final =
                    circuit.Gates
                    |> List.rev   // Gates are stored most-recent-first; execute in program order.
                    |> List.fold (fun (rho: Complex[,]) gate ->
                        match gate with
                        | CircuitBuilder.Measure _ -> rho   // terminal measurement is read off the diagonal
                        | CircuitBuilder.Barrier _ -> rho   // synchronization directive — no physical effect
                        | _ ->
                            let afterGate =
                                match gate with
                                | CircuitBuilder.Reset q ->
                                    // Reset = measure q, flip to |0⟩ on outcome 1 — the (non-unitary)
                                    // channel ρ → P₀ρP₀ + X P₁ρP₁ X, computed elementwise: both terms
                                    // land in the bit_q = 0 block.
                                    let mask = 1 <<< q
                                    Array2D.init dim dim (fun i j ->
                                        if i &&& mask = 0 && j &&& mask = 0 then
                                            rho.[i, j] + rho.[i ||| mask, j ||| mask]
                                        else Complex.Zero)
                                | CircuitBuilder.Conditional (q, inner) ->
                                    // Classically-controlled gate: ρ → P₀ρP₀ + (U P₁)ρ(P₁U†).
                                    // The projections encode the (dephasing) measurement of q, so
                                    // repeated conditionals on the same qubit stay correlated with
                                    // the same outcome.
                                    let mask = 1 <<< q
                                    let block (want: int) =
                                        Array2D.init dim dim (fun i j ->
                                            if i &&& mask = want && j &&& mask = want then rho.[i, j]
                                            else Complex.Zero)
                                    let untriggered = block 0
                                    let triggered = conjugateByGate backend dim inner (block mask)
                                    Array2D.init dim dim (fun i j -> untriggered.[i, j] + triggered.[i, j])
                                | _ -> conjugateByGate backend dim gate rho
                            let qubits, p =
                                match qubitsOf gate with
                                | [ single ] -> [ single ], config.SingleQubitDepolarizing
                                | many -> many, config.TwoQubitDepolarizing
                            qubits |> List.fold (fun r q -> depolarize backend dim q p r) afterGate)
                        rho0
                Ok (final, n)
            with ex ->
                Error (QuantumError.OperationError ("NoisyLocalBackend", sprintf "density-matrix simulation failed: %s" ex.Message))

    /// A noisy `IQuantumBackend` that returns a `DensityMatrix` state. Plugs into the shared
    /// primitives: `Primitives.sample`/`run` read noisy measurement statistics off its diagonal.
    type NoisyLocalBackend(config: NoiseConfig) =

        interface IQuantumBackend with

            member _.Name = "Noisy Local Simulator (density matrix)"

            member _.NativeStateType = QuantumStateType.Mixed

            member this.ExecuteToState (circuit: ICircuit) : Result<QuantumState, QuantumError> =
                match CircuitAdapter.tryGetCircuit circuit with
                | Some builderCircuit ->
                    simulate config builderCircuit
                    |> Result.map (fun (rho, n) -> QuantumState.DensityMatrix (rho, n))
                | None ->
                    Error (QuantumError.OperationError ("NoisyLocalBackend",
                        "Only gate circuits are supported; wrap a CircuitBuilder.Circuit with CircuitWrapper."))

            member this.ExecuteToStateAsync (circuit: ICircuit) (_ct: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                task { return (this :> IQuantumBackend).ExecuteToState circuit }

            member _.InitializeState (numQubits: int) : Result<QuantumState, QuantumError> =
                Ok (QuantumState.StateVector (StateVector.init numQubits))

            member _.ApplyOperation (_op: QuantumOperation) (_state: QuantumState) : Result<QuantumState, QuantumError> =
                Error (QuantumError.OperationError ("ApplyOperation",
                    "NoisyLocalBackend does not support incremental ApplyOperation; use ExecuteToState with a complete circuit."))

            member this.ApplyOperationAsync (op: QuantumOperation) (state: QuantumState) (_ct: CancellationToken) : Task<Result<QuantumState, QuantumError>> =
                task { return (this :> IQuantumBackend).ApplyOperation op state }

            member _.SupportsOperation (op: QuantumOperation) : bool =
                match op with
                | QuantumOperation.Gate _ | QuantumOperation.Sequence _ -> true
                | _ -> false
