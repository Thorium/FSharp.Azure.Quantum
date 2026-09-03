namespace FSharp.Azure.Quantum.Algorithms

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Algorithms.TrotterSuzuki  // brings PauliString record labels into scope

/// ADAPT-QAOA — QAOA with an adaptively-chosen mixer at each layer.
///
/// Standard QAOA repeats a *fixed* mixer (usually Σ Xᵢ). ADAPT-QAOA instead picks, at each
/// new layer, the mixer from a pool whose energy gradient is largest, giving a shallower,
/// problem-tailored circuit. Each layer applies the cost evolution `e^(-iγ H)` followed by
/// the selected mixer `e^(-iβ A)`, starting from the uniform superposition |+…+⟩:
///
///   |ψₚ⟩ = e^(-iβₚ Aₚ) e^(-iγₚ H) … e^(-iβ₁ A₁) e^(-iγ₁ H) |+…+⟩
///
/// Like ADAPT-VQE this is state-vector exact — it reuses `Primitives.expectation` for the
/// energy ⟨H⟩, `TrotterSuzuki.synthesizePauliEvolution` for each `e^(-iθP)` block, and the
/// shared Nelder-Mead optimiser. It requires a state-vector (gate-based simulator) backend.
module AdaptQaoa =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// A mixer pool operator is a Pauli-string generator; each layer applies e^(-iβ A).
    /// Give the generators a unit coefficient — the variational angle carries the scale.
    type MixerPool = TrotterSuzuki.PauliString list

    /// ADAPT-QAOA configuration.
    type AdaptQaoaConfig = {
        /// Maximum number of layers (cost + mixer) to add.
        MaxLayers: int
        /// Stop when the largest mixer gradient magnitude is below this.
        GradientThreshold: float
        /// Central-difference step used for gradient screening.
        FiniteDiffEps: float
        /// Initial γ used when a new layer is added (before re-optimisation).
        GammaInit: float
    }

    /// Sensible defaults (10 layers, 1e-3 gradient cutoff, γ₀ = 0.1).
    let defaultConfig = {
        MaxLayers = 10
        GradientThreshold = 1e-3
        FiniteDiffEps = 1e-4
        GammaInit = 0.1
    }

    /// Result of an ADAPT-QAOA run.
    type AdaptQaoaResult = {
        /// Final variational energy ⟨H⟩.
        Energy: float
        /// Mixers added, one per layer, in selection order.
        SelectedMixers: TrotterSuzuki.PauliString list
        /// Optimised angles, interleaved as [γ₁; β₁; γ₂; β₂; …].
        Parameters: float[]
        /// Number of layers added.
        Layers: int
        /// True if the run stopped because every mixer gradient was below threshold.
        Converged: bool
        /// Energy after each layer was added (chronological).
        EnergyHistory: float list
    }

    // ========================================================================
    // ANSATZ + ENERGY
    // ========================================================================

    /// Build the ADAPT-QAOA ansatz: |+…+⟩ then, per layer k, the cost evolution
    /// e^(-iγₖ H) (first-order Trotter over the Hamiltonian terms) followed by the mixer
    /// e^(-iβₖ Aₖ). `parameters` is interleaved [γ₁; β₁; γ₂; β₂; …] with length 2·|mixers|.
    let buildAnsatz
        (numQubits: int)
        (costHamiltonian: TrotterSuzuki.PauliHamiltonian)
        (mixers: TrotterSuzuki.PauliString list)
        (parameters: float[])
        : CircuitBuilder.Circuit =
        let qubits = [| 0 .. numQubits - 1 |]

        // Reference state |+…+⟩ = H^⊗n |0…0⟩.
        let plus =
            [ 0 .. numQubits - 1 ]
            |> List.fold (fun c q -> CircuitBuilder.addGate (CircuitBuilder.H q) c) (CircuitBuilder.empty numQubits)

        mixers
        |> List.mapi (fun k m -> (k, m))
        |> List.fold (fun circ (k, mixer) ->
            let gamma = parameters.[2 * k]
            let beta = parameters.[2 * k + 1]
            // Cost evolution e^(-iγ H) = ∏ e^(-iγ cₜ Pₜ) (first-order Trotter).
            let afterCost =
                costHamiltonian.Terms
                |> List.fold (fun c term -> TrotterSuzuki.synthesizePauliEvolution term gamma qubits c) circ
            // Mixer e^(-iβ A).
            TrotterSuzuki.synthesizePauliEvolution mixer beta qubits afterCost)
            plus

    let private stateEnergy
        (backend: IQuantumBackend)
        (costHamiltonian: TrotterSuzuki.PauliHamiltonian)
        (numQubits: int)
        (mixers: TrotterSuzuki.PauliString list)
        (parameters: float[])
        : QuantumResult<float> =
        let circuit = buildAnsatz numQubits costHamiltonian mixers parameters
        Primitives.getState backend circuit
        |> Result.bind (Primitives.expectation costHamiltonian)

    // ========================================================================
    // OPTIMISATION (robust for 1 parameter, Nelder-Mead for ≥2)
    // ========================================================================

    let private optimize (objective: float[] -> float) (init: float[]) : float[] * float =
        match init.Length with
        | 0 -> ([||], objective [||])
        | 1 ->
            let evalAt t = objective [| t |]
            let scan (centre: float) (halfWidth: float) (steps: int) =
                [ for k in 0 .. steps -> centre - halfWidth + float k * (2.0 * halfWidth / float steps) ]
                |> List.map (fun t -> t, evalAt t)
                |> List.minBy snd
            let (coarseT, _) = scan 0.0 System.Math.PI 60
            let (fineT, fineV) = scan coarseT (System.Math.PI / 30.0) 40
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

    /// Run ADAPT-QAOA: grow a QAOA ansatz whose mixer at each layer is chosen from `pool`
    /// to minimise ⟨`costHamiltonian`⟩ on `backend`.
    let run
        (backend: IQuantumBackend)
        (costHamiltonian: TrotterSuzuki.PauliHamiltonian)
        (pool: MixerPool)
        (numQubits: int)
        (config: AdaptQaoaConfig)
        : QuantumResult<AdaptQaoaResult> =

        if numQubits <= 0 then
            Error (QuantumError.ValidationError ("numQubits", "must be positive"))
        elif List.isEmpty pool then
            Error (QuantumError.ValidationError ("pool", "mixer pool must be non-empty"))
        else
            let badHamTerm = costHamiltonian.Terms |> List.tryFind (fun t -> t.Operators.Length <> numQubits)
            let badPoolOp = pool |> List.tryFind (fun p -> p.Operators.Length <> numQubits)
            match badHamTerm, badPoolOp with
            | Some t, _ -> widthError "costHamiltonian" t.Operators.Length numQubits
            | _, Some p -> widthError "pool" p.Operators.Length numQubits
            | None, None ->

            // Reference energy ⟨+…+|H|+…+⟩ — also validates the backend supports expectation.
            match stateEnergy backend costHamiltonian numQubits [] [||] with
            | Error e -> Error e
            | Ok referenceEnergy ->

                // Mixer gradient at the current optimised state: append a fresh layer whose cost
                // angle is the small seed γ₀ (this breaks the |+…+⟩ symmetry so single-Pauli
                // mixers have a non-zero gradient) and central-difference the mixer angle β
                // around 0. γ₀ is the same seed the layer is initialised with before re-optimising.
                let gradientAt (mixers: TrotterSuzuki.PauliString list) (parameters: float[]) (mixer: TrotterSuzuki.PauliString) : QuantumResult<float> =
                    let mixersWith = mixers @ [ mixer ]
                    let eps = config.FiniteDiffEps
                    let g0 = config.GammaInit
                    match stateEnergy backend costHamiltonian numQubits mixersWith (Array.append parameters [| g0; eps |]),
                          stateEnergy backend costHamiltonian numQubits mixersWith (Array.append parameters [| g0; -eps |]) with
                    | Ok ePlus, Ok eMinus -> Ok ((ePlus - eMinus) / (2.0 * eps))
                    | Error e, _ -> Error e
                    | _, Error e -> Error e

                let rec loop layer (mixers: TrotterSuzuki.PauliString list) (parameters: float[]) (history: float list) (energy: float) : QuantumResult<AdaptQaoaResult> =
                    let finish converged =
                        Ok { Energy = energy
                             SelectedMixers = mixers
                             Parameters = parameters
                             Layers = layer
                             Converged = converged
                             EnergyHistory = List.rev history }

                    if layer >= config.MaxLayers then finish false
                    else
                        let gradsResult =
                            (Ok [], pool)
                            ||> List.fold (fun accR mixer ->
                                accR |> Result.bind (fun acc ->
                                    gradientAt mixers parameters mixer |> Result.map (fun g -> (mixer, g) :: acc)))
                        match gradsResult with
                        | Error e -> Error e
                        | Ok grads ->
                            let (bestMixer, bestGrad) = grads |> List.maxBy (fun (_, g) -> abs g)
                            if abs bestGrad < config.GradientThreshold then
                                finish true
                            else
                                let newMixers = mixers @ [ bestMixer ]
                                // New layer seeds: γ = GammaInit, β = 0.
                                let init = Array.append parameters [| config.GammaInit; 0.0 |]
                                let objective (p: float[]) =
                                    (stateEnergy backend costHamiltonian numQubits newMixers p) |> Result.defaultWith (fun _ -> System.Double.MaxValue)
                                let (optParams, optEnergy) = optimize objective init
                                // Guarantee monotonic progress: if this layer can't improve the
                                // energy (e.g. the optimizer failed to converge), stop with the best.
                                if optEnergy > energy + 1e-9 then finish false
                                else loop (layer + 1) newMixers optParams (optEnergy :: history) optEnergy

                loop 0 [] [||] [ referenceEnergy ] referenceEnergy

    // ========================================================================
    // QUBO CONVENIENCE — solve a QUBO / Ising problem end to end
    // ========================================================================

    /// A standard mixer pool for QUBO/optimization problems: single-qubit X and Y on every
    /// qubit. X mixers are the usual QAOA driver; Y mixers give the gradient screen a
    /// symmetry-breaking option so a useful mixer is always found.
    let defaultMixerPool (numQubits: int) : MixerPool =
        [ for q in 0 .. numQubits - 1 do
            for p in [ 'X'; 'Y' ] do
                let ops = Array.create numQubits 'I'
                ops.[q] <- p
                yield { Operators = ops; Coefficient = System.Numerics.Complex(1.0, 0.0) } ]

    /// Convert a QAOA `ProblemHamiltonian` (Z/ZZ Ising terms) to the Pauli-string form
    /// ADAPT-QAOA consumes.
    let ofProblemHamiltonian (ph: QaoaCircuit.ProblemHamiltonian) : TrotterSuzuki.PauliHamiltonian =
        let letterOf (p: QaoaCircuit.PauliOperator) =
            match p with
            | QaoaCircuit.PauliI -> 'I'
            | QaoaCircuit.PauliX -> 'X'
            | QaoaCircuit.PauliY -> 'Y'
            | QaoaCircuit.PauliZ -> 'Z'
        let terms =
            ph.Terms
            |> Array.toList
            |> List.map (fun t ->
                let ops = Array.create ph.NumQubits 'I'
                Array.iter2 (fun (q: int) (p: QaoaCircuit.PauliOperator) -> ops.[q] <- letterOf p) t.QubitsIndices t.PauliOperators
                { Operators = ops; Coefficient = System.Numerics.Complex(t.Coefficient, 0.0) })
        { Terms = terms; NumQubits = ph.NumQubits }

    /// Classical QUBO cost of a 0/1 assignment: Σ Qᵢⱼ xᵢ xⱼ (diagonal entries are the
    /// linear terms since x² = x for x ∈ {0,1}).
    let quboCost (quboMap: Map<int * int, float>) (assignment: int[]) : float =
        (0.0, quboMap) ||> Map.fold (fun acc (i, j) q -> acc + q * float assignment.[i] * float assignment.[j])

    /// Result of solving a QUBO with ADAPT-QAOA.
    type QuboSolution = {
        /// Best 0/1 assignment found (index = variable).
        Assignment: int[]
        /// Classical QUBO cost of `Assignment` (the quantity minimised).
        QuboCost: float
        /// Variational energy ⟨H⟩ reported by ADAPT-QAOA.
        ExpectedEnergy: float
        /// The underlying ADAPT-QAOA run (mixers, angles, history).
        Adapt: AdaptQaoaResult
    }

    /// Solve a QUBO end to end with ADAPT-QAOA: map it to an Ising Hamiltonian, grow an
    /// adaptive ansatz with the standard X/Y mixer pool, sample the final state, and return
    /// the lowest-cost assignment observed.
    ///
    /// State-vector backends only (ADAPT-QAOA needs exact expectation values). Suitable for
    /// small problems (a handful of qubits) — the same regime as exact QAOA simulation.
    let solveQubo
        (backend: IQuantumBackend)
        (numQubits: int)
        (quboMap: Map<int * int, float>)
        (config: AdaptQaoaConfig)
        : QuantumResult<QuboSolution> =
        // fromQuboSparse copies QUBO keys straight into qubit indices without range-checking, so an
        // out-of-range key would later throw IndexOutOfRange out of this Result API. Validate first.
        let indexOutOfRange =
            numQubits <= 0
            || (quboMap |> Map.exists (fun (i, j) _ -> i < 0 || j < 0 || i >= numQubits || j >= numQubits))
        if indexOutOfRange then
            Error (QuantumError.ValidationError ("quboMap",
                $"QUBO variable indices must be in [0, %d{numQubits}); check the keys against numQubits=%d{numQubits}."))
        else
        let hamiltonian = ofProblemHamiltonian (QaoaCircuit.ProblemHamiltonian.fromQuboSparse numQubits quboMap)
        run backend hamiltonian (defaultMixerPool numQubits) numQubits config
        |> Result.bind (fun adapt ->
            // Sample the optimised ansatz and keep the lowest-cost bitstring observed.
            let circuit = buildAnsatz numQubits hamiltonian adapt.SelectedMixers adapt.Parameters
            Primitives.sample backend circuit 2048
            |> Result.map (fun histogram ->
                let best =
                    histogram
                    |> Map.toList
                    |> List.map (fun (bits, _) -> bits |> Seq.map (fun c -> int c - int '0') |> Seq.toArray)
                    |> List.minBy (quboCost quboMap)
                { Assignment = best
                  QuboCost = quboCost quboMap best
                  ExpectedEnergy = adapt.Energy
                  Adapt = adapt }))
