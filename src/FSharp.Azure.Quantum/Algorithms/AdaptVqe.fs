namespace FSharp.Azure.Quantum.Algorithms

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

/// ADAPT-VQE — Adaptive Derivative-Assembled Pseudo-Trotter VQE.
///
/// Instead of a fixed variational form, ADAPT-VQE *grows* the ansatz one operator at a
/// time. Each iteration:
///   1. screens an operator pool by the energy gradient each operator would contribute,
///   2. appends the highest-gradient operator (as a new e^(-iθP) block with a fresh angle),
///   3. re-optimises **all** angles, and
///   4. stops when the largest remaining gradient falls below a threshold.
///
/// This yields a compact, problem-tailored ansatz — often far shallower than a fixed
/// hardware-efficient form for the same accuracy.
///
/// This implementation is state-vector exact: it reuses `Primitives.expectation` for the
/// energy ⟨H⟩, `TrotterSuzuki.synthesizePauliEvolution` to realise each e^(-iθP) block, and
/// the shared Nelder-Mead optimiser for re-optimisation. It therefore requires a
/// state-vector (gate-based simulator) backend; on a measurement-only backend `run`
/// returns the `Error` surfaced by `observe`.
module AdaptVqe =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// A pool operator is a Pauli-string generator; the ansatz applies e^(-iθP).
    /// Give the generators a unit coefficient — the variational angle carries the scale.
    type OperatorPool = TrotterSuzuki.PauliString list

    /// ADAPT-VQE configuration.
    type AdaptConfig = {
        /// Maximum number of operators to add to the ansatz.
        MaxIterations: int
        /// Stop when the largest pool gradient magnitude is below this.
        GradientThreshold: float
        /// Central-difference step used for gradient screening.
        FiniteDiffEps: float
    }

    /// Sensible defaults (20 operators, 1e-3 gradient cutoff).
    let defaultConfig = {
        MaxIterations = 20
        GradientThreshold = 1e-3
        FiniteDiffEps = 1e-4
    }

    /// Result of an ADAPT-VQE run.
    type AdaptResult = {
        /// Final variational energy ⟨H⟩.
        Energy: float
        /// Operators added to the ansatz, in the order selected.
        SelectedOperators: TrotterSuzuki.PauliString list
        /// Optimised angles, aligned with SelectedOperators.
        Parameters: float[]
        /// Number of operators added.
        Iterations: int
        /// True if the run stopped because every pool gradient was below threshold.
        Converged: bool
        /// Energy after each operator was added (chronological).
        EnergyHistory: float list
    }

    // ========================================================================
    // ANSATZ + ENERGY
    // ========================================================================

    /// Build the ADAPT ansatz circuit: |0…0⟩ followed by e^(-iθₖPₖ) for each selected
    /// operator/angle pair. `ops` and `parameters` must have equal length.
    let buildAnsatz (numQubits: int) (ops: TrotterSuzuki.PauliString list) (parameters: float[]) : CircuitBuilder.Circuit =
        let qubits = [| 0 .. numQubits - 1 |]
        List.zip ops (List.ofArray parameters)
        |> List.fold (fun circ (op, theta) ->
            TrotterSuzuki.synthesizePauliEvolution op theta qubits circ)
            (CircuitBuilder.empty numQubits)

    /// Energy ⟨H⟩ of the ansatz(ops, parameters) evaluated on the backend.
    let private stateEnergy
        (backend: IQuantumBackend)
        (hamiltonian: TrotterSuzuki.PauliHamiltonian)
        (numQubits: int)
        (ops: TrotterSuzuki.PauliString list)
        (parameters: float[])
        : QuantumResult<float> =
        let circuit = buildAnsatz numQubits ops parameters
        Primitives.getState backend circuit
        |> Result.bind (Primitives.expectation hamiltonian)

    // ========================================================================
    // OPTIMISATION (robust for 1 parameter, Nelder-Mead for ≥2)
    // ========================================================================

    /// Minimise `objective`. Nelder-Mead needs ≥2 dimensions, so a single angle is
    /// optimised with a coarse-then-fine 1-D scan.
    let private optimize (objective: float[] -> float) (init: float[]) : float[] * float =
        match init.Length with
        | 0 -> ([||], objective [||])
        | 1 ->
            let evalAt t = objective [| t |]
            let scan (centre: float) (halfWidth: float) (steps: int) =
                [ for k in 0 .. steps -> centre - halfWidth + float k * (2.0 * halfWidth / float steps) ]
                |> List.map (fun t -> t, evalAt t)
                |> List.minBy snd
            let (coarseT, _) = scan 0.0 System.Math.PI 60          // coarse over [-π, π]
            let (fineT, fineV) = scan coarseT (System.Math.PI / 30.0) 40  // refine locally
            ([| fineT |], fineV)
        | _ ->
            // Nelder-Mead can throw when it exhausts its iteration budget on a flat/multimodal
            // landscape; fall back to the seed so the adaptive loop never crashes.
            try
                let r = QaoaOptimizer.Optimizer.minimize objective init
                if System.Double.IsNaN r.FinalObjectiveValue then (init, objective init)
                else (r.OptimizedParameters, r.FinalObjectiveValue)
            with _ ->
                (init, objective init)

    // ========================================================================
    // RUN
    // ========================================================================

    let private widthError (label: string) (got: int) (expected: int) =
        Error (QuantumError.ValidationError (label,
            $"width {got} does not match the {expected}-qubit problem."))

    /// Run ADAPT-VQE: grow an ansatz from `pool` to minimise ⟨`hamiltonian`⟩ on `backend`.
    ///
    /// - `numQubits`  : number of qubits (must match the Hamiltonian and every pool operator).
    /// - `hamiltonian`: the observable to minimise (Pauli sum).
    /// - `pool`       : candidate generator operators (unit-coefficient Pauli strings).
    let run
        (backend: IQuantumBackend)
        (hamiltonian: TrotterSuzuki.PauliHamiltonian)
        (pool: OperatorPool)
        (numQubits: int)
        (config: AdaptConfig)
        : QuantumResult<AdaptResult> =

        // ---- validation --------------------------------------------------
        if numQubits <= 0 then
            Error (QuantumError.ValidationError ("numQubits", "must be positive"))
        elif List.isEmpty pool then
            Error (QuantumError.ValidationError ("pool", "operator pool must be non-empty"))
        else
            let badHamTerm = hamiltonian.Terms |> List.tryFind (fun t -> t.Operators.Length <> numQubits)
            let badPoolOp = pool |> List.tryFind (fun p -> p.Operators.Length <> numQubits)
            match badHamTerm, badPoolOp with
            | Some t, _ -> widthError "hamiltonian" t.Operators.Length numQubits
            | _, Some p -> widthError "pool" p.Operators.Length numQubits
            | None, None ->

            // Reference (|0…0⟩) energy — also validates the backend supports expectation.
            match stateEnergy backend hamiltonian numQubits [] [||] with
            | Error e -> Error e
            | Ok referenceEnergy ->

                // Gradient contributed by appending `op` to (ops, parameters):
                // central difference of the energy in the new angle around 0.
                let gradientAt (ops: TrotterSuzuki.PauliString list) (parameters: float[]) (op: TrotterSuzuki.PauliString) : QuantumResult<float> =
                    let opsWith = ops @ [ op ]
                    let eps = config.FiniteDiffEps
                    match stateEnergy backend hamiltonian numQubits opsWith (Array.append parameters [| eps |]),
                          stateEnergy backend hamiltonian numQubits opsWith (Array.append parameters [| -eps |]) with
                    | Ok ePlus, Ok eMinus -> Ok ((ePlus - eMinus) / (2.0 * eps))
                    | Error e, _ -> Error e
                    | _, Error e -> Error e

                let rec loop iter (ops: TrotterSuzuki.PauliString list) (parameters: float[]) (history: float list) (energy: float) : QuantumResult<AdaptResult> =
                    let finish converged =
                        Ok { Energy = energy
                             SelectedOperators = ops
                             Parameters = parameters
                             Iterations = iter
                             Converged = converged
                             EnergyHistory = List.rev history }

                    if iter >= config.MaxIterations then finish false
                    else
                        // Screen the whole pool (short-circuit on the first Error).
                        let gradsResult =
                            (Ok [], pool)
                            ||> List.fold (fun accR op ->
                                accR |> Result.bind (fun acc ->
                                    gradientAt ops parameters op |> Result.map (fun g -> (op, g) :: acc)))
                        match gradsResult with
                        | Error e -> Error e
                        | Ok grads ->
                            let (bestOp, bestGrad) = grads |> List.maxBy (fun (_, g) -> abs g)
                            if abs bestGrad < config.GradientThreshold then
                                finish true
                            else
                                let newOps = ops @ [ bestOp ]
                                let init = Array.append parameters [| 0.0 |]
                                let objective (p: float[]) =
                                    match stateEnergy backend hamiltonian numQubits newOps p with
                                    | Ok e -> e
                                    | Error _ -> System.Double.MaxValue
                                let (optParams, optEnergy) = optimize objective init
                                // Guarantee monotonic progress: if this operator can't improve the
                                // energy (e.g. the optimizer failed to converge), stop with the best.
                                if optEnergy > energy + 1e-9 then finish false
                                else loop (iter + 1) newOps optParams (optEnergy :: history) optEnergy

                loop 0 [] [||] [ referenceEnergy ] referenceEnergy
