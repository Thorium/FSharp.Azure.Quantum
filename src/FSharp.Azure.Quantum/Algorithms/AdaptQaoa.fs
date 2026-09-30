namespace FSharp.Azure.Quantum.Algorithms

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Algorithms.TrotterSuzuki // brings PauliString record labels into scope

/// ADAPT-QAOA — QAOA with an adaptively-chosen mixer at each layer.
///
/// Standard QAOA repeats a *fixed* mixer (usually Σ Xᵢ). ADAPT-QAOA instead picks, at each
/// new layer, the mixer from a pool whose energy gradient is largest, giving a shallower,
/// problem-tailored circuit. Each layer applies the cost evolution `e^(-iγ H)` followed by
/// the selected mixer `e^(-iβ A)`, starting from the uniform superposition |+…+⟩:
///
///   |ψₚ⟩ = e^(-iβₚ Aₚ) e^(-iγₚ H) … e^(-iβ₁ A₁) e^(-iγ₁ H) |+…+⟩
///
/// Like ADAPT-VQE it has two routes, with `TrotterSuzuki.synthesizePauliEvolution` for each
/// `e^(-iθP)` block. On an exact backend the energy is `Primitives.expectation`, gradients are
/// central differences with `FiniteDiffEps`, and the angles are re-optimised by Nelder-Mead. On
/// a shot-sampling backend (IShotSamplingBackend) every energy is
/// `Primitives.sampledExpectation` (whole-circuit jobs), gradients use the parameter-shift rule
/// (dE/dγₖ as the sum of one shift per cost term, since γₖ drives every cost block of layer k),
/// and the angles are re-optimised by `AdaptVqe.SampledOptimizerSteps` Adam steps. Jobs per
/// layer, with T cost terms, L layers and G measurement groups (1 for a Z/ZZ Ising cost):
/// G·(2·|pool| + 2·L·(T + 1)·SampledOptimizerSteps + 1) (estimateCloudJobs), which grows
/// quadratically with the layers. A run is capped by AdaptQaoaConfig.MaxCloudJobs (default
/// AdaptVqe.DefaultMaxCloudJobs = 2,000): refused up front when the reference energy and the
/// first layer do not fit, and stopped with the best ansatz so far (JobCapReached) before any
/// layer that would cross the cap. The backend's JobBudget still applies on top.
///
/// The angles are unconstrained and each new β starts at 0, so the sign of a pool generator
/// does not matter: +X here and the standard QAOA mixer -Σ Xᵢ (Core.QaoaCircuit) reach the
/// same states. γ multiplies the Hamiltonian as given, without QaoaExecutionHelpers'
/// normalisation.
module AdaptQaoa =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// A mixer pool operator is a Pauli-string generator; each layer applies e^(-iβ A).
    /// Give the generators a unit coefficient — the variational angle carries the scale.
    type MixerPool = TrotterSuzuki.PauliString list

    /// ADAPT-QAOA configuration.
    [<Struct>]
    type AdaptQaoaConfig =
        {
            /// Maximum number of layers (cost + mixer) to add.
            MaxLayers: int
            /// Stop when the largest mixer gradient magnitude is below this.
            GradientThreshold: float
            /// Central-difference step used for gradient screening.
            FiniteDiffEps: float
            /// Initial γ used when a new layer is added (before re-optimisation).
            GammaInit: float
            /// Most whole-circuit jobs one run may submit on a shot-sampling backend
            /// (IShotSamplingBackend: each is a separately queued and billed cloud job); None =
            /// no cap. Ignored on exact backends. A run whose reference energy and first layer
            /// already exceed it is refused before any job; otherwise the run stops, with the
            /// best ansatz so far and JobCapReached set, before a layer that could cross it.
            /// solveQubo keeps one job of it for its final sample.
            MaxCloudJobs: int option
        }

    /// Sensible defaults (10 layers, 1e-3 gradient cutoff, γ₀ = 0.1, at most 2,000 cloud jobs).
    let defaultConfig =
        {
            MaxLayers = 10
            GradientThreshold = 1e-3
            FiniteDiffEps = 1e-4
            GammaInit = 0.1
            MaxCloudJobs = Some AdaptVqe.DefaultMaxCloudJobs
        }

    /// Result of an ADAPT-QAOA run.
    type AdaptQaoaResult =
        {
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
            /// Shot-noise standard error of Energy on a shot-sampling backend (the
            /// Primitives.sampledExpectation error of the final energy estimate); None on an
            /// exact backend, where Energy is exact.
            EnergyStandardError: float option
            /// Whole-circuit jobs submitted on a shot-sampling backend; 0 on an exact backend.
            CloudJobs: int
            /// True when the run stopped because the next layer could exceed
            /// AdaptQaoaConfig.MaxCloudJobs: the result is the best ansatz so far, not Converged.
            JobCapReached: bool
        }

    // ========================================================================
    // ANSATZ + ENERGY
    // ========================================================================

    /// The ADAPT-QAOA ansatz with each block's time given separately: cost block t of layer k
    /// runs for costTime k t, the mixer of layer k for mixerTime k.
    let private ansatzWithTimes
        (numQubits: int)
        (costHamiltonian: TrotterSuzuki.PauliHamiltonian)
        (mixers: TrotterSuzuki.PauliString list)
        (costTime: int -> int -> float)
        (mixerTime: int -> float)
        : CircuitBuilder.Circuit =
        let qubits = [| 0 .. numQubits - 1 |]

        // Reference state |+…+⟩ = H^⊗n |0…0⟩.
        let plus =
            [ 0 .. numQubits - 1 ]
            |> List.fold (fun c q -> CircuitBuilder.addGate (CircuitBuilder.H q) c) (CircuitBuilder.empty numQubits)

        mixers
        |> List.mapi (fun k m -> (k, m))
        |> List.fold
            (fun circ (k, mixer) ->
                // Cost evolution e^(-iγ H) = ∏ e^(-iγ cₜ Pₜ) (first-order Trotter); block t of
                // layer k runs for costTime k t (γₖ for every t, except in a parameter shift).
                let afterCost =
                    costHamiltonian.Terms
                    |> List.indexed
                    |> List.fold
                        (fun c (t, term) -> TrotterSuzuki.synthesizePauliEvolution term (costTime k t) qubits c)
                        circ
                // Mixer e^(-iβ A).
                TrotterSuzuki.synthesizePauliEvolution mixer (mixerTime k) qubits afterCost)
            plus

    /// Build the ADAPT-QAOA ansatz: |+…+⟩ then, per layer k, the cost evolution
    /// e^(-iγₖ H) (first-order Trotter over the Hamiltonian terms) followed by the mixer
    /// e^(-iβₖ Aₖ). `parameters` is interleaved [γ₁; β₁; γ₂; β₂; …] with length 2·|mixers|.
    let buildAnsatz
        (numQubits: int)
        (costHamiltonian: TrotterSuzuki.PauliHamiltonian)
        (mixers: TrotterSuzuki.PauliString list)
        (parameters: float[])
        : CircuitBuilder.Circuit =
        ansatzWithTimes numQubits costHamiltonian mixers (fun k _ -> parameters.[2 * k]) (fun k ->
            parameters.[2 * k + 1])

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
                [
                    for k in 0..steps -> centre - halfWidth + float k * (2.0 * halfWidth / float steps)
                ]
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

                if System.Double.IsNaN r.FinalObjectiveValue then
                    (init, objective init)
                else
                    (r.OptimizedParameters, r.FinalObjectiveValue)
            with _ ->
                (init, objective init)

    // ========================================================================
    // SHOT-SAMPLING BACKENDS (measured energies, parameter-shift gradients)
    // ========================================================================

    let private isIdentity (term: TrotterSuzuki.PauliString) =
        term.Operators |> Array.forall (fun p -> p = 'I' || p = 'i')

    /// Whole-circuit jobs of one shot-sampling ADAPT-QAOA layer that ends with `layers` layers,
    /// for G measurement groups, T non-identity cost terms and a pool of `poolSize`:
    /// G·(2·poolSize + 2·layers·(T + 1)·S + 1) — the mixer-gradient screen, S Adam steps of the
    /// full parameter-shift gradient (T shift pairs for each γ, one pair for each β), and the
    /// fresh energy. An upper bound: a zero-coefficient generator costs nothing.
    let internal layerJobs (groups: int) (costTerms: int) (poolSize: int) (layers: int) : int =
        groups
        * (2 * poolSize + 2 * layers * (costTerms + 1) * AdaptVqe.SampledOptimizerSteps + 1)

    /// Most whole-circuit jobs ADAPT-QAOA can submit on a shot-sampling backend for a run of
    /// `layers` layers: the reference energy plus every layer's layerJobs, with G =
    /// Primitives.measurementGroups of the cost Hamiltonian (1 for a Z/ZZ Ising cost). Grows as
    /// layers²·G·T·S. solveQubo adds one job for its final sample.
    let estimateCloudJobs (costHamiltonian: TrotterSuzuki.PauliHamiltonian) (poolSize: int) (layers: int) : int =
        let groups = Primitives.measurementGroups costHamiltonian |> List.length

        let costTerms =
            costHamiltonian.Terms |> List.filter (isIdentity >> not) |> List.length

        groups
        + ([ 1 .. max 0 layers ] |> List.sumBy (layerJobs groups costTerms poolSize))

    /// ADAPT-QAOA on a shot-sampling backend (see the module notes).
    let private runSampled
        (backend: IQuantumBackend)
        (costHamiltonian: TrotterSuzuki.PauliHamiltonian)
        (pool: MixerPool)
        (numQubits: int)
        (config: AdaptQaoaConfig)
        : QuantumResult<AdaptQaoaResult> =
        let terms = costHamiltonian.Terms |> Array.ofList
        let groups = Primitives.measurementGroups costHamiltonian |> List.length
        let costTerms = terms |> Array.filter (isIdentity >> not) |> Array.length
        let perLayer = layerJobs groups costTerms pool.Length
        // Jobs submitted so far (each energy estimate reports its circuits).
        let submitted = ref 0

        let energyOf (circuit: CircuitBuilder.Circuit) =
            match config.MaxCloudJobs with
            | Some cap when submitted.Value + groups > cap ->
                // Unreachable while layerJobs bounds every layer; kept so no job is ever
                // submitted past the cap.
                Error(
                    QuantumError.ValidationError(
                        "MaxCloudJobs",
                        $"ADAPT-QAOA reached its cap of {cap} cloud jobs ({submitted.Value} submitted)"
                    )
                )
            | _ ->
                Primitives.sampledExpectation backend circuit costHamiltonian
                |> Result.map (fun e ->
                    submitted.Value <- submitted.Value + e.Circuits
                    e.Value, e.StandardError)

        /// Energy with every block at its parameter's time except `overrideTime` (block, time).
        let energyWith (mixers: TrotterSuzuki.PauliString list) (parameters: float[]) overrideTime =
            let costTime k t =
                match overrideTime with
                | Some(Choice1Of2(layer, term), time) when layer = k && term = t -> time
                | _ -> parameters.[2 * k]

            let mixerTime k =
                match overrideTime with
                | Some(Choice2Of2 layer, time) when layer = k -> time
                | _ -> parameters.[2 * k + 1]

            energyOf (ansatzWithTimes numQubits costHamiltonian mixers costTime mixerTime)

        /// dE/dβₖ: the mixer is one Pauli block.
        let mixerGradient (mixers: TrotterSuzuki.PauliString list) (parameters: float[]) k =
            AdaptVqe.parameterShift mixers.[k].Coefficient.Real parameters.[2 * k + 1] (fun time ->
                energyWith mixers parameters (Some(Choice2Of2 k, time)))

        /// dE/dγₖ: γₖ drives every cost block of layer k, so the derivative is the sum of each
        /// block's parameter shift.
        let costGradient (mixers: TrotterSuzuki.PauliString list) (parameters: float[]) k =
            let rec sum t (total, variance) =
                if t >= terms.Length then
                    Ok(total, sqrt variance)
                elif isIdentity terms.[t] then
                    sum (t + 1) (total, variance) // a global phase: no dependence on γ
                else
                    match
                        AdaptVqe.parameterShift terms.[t].Coefficient.Real parameters.[2 * k] (fun time ->
                            energyWith mixers parameters (Some(Choice1Of2(k, t), time)))
                    with
                    | Error err -> Error err
                    | Ok(g, e) -> sum (t + 1) (total + g, variance + e * e)

            sum 0 (0.0, 0.0)

        let fullGradient (mixers: TrotterSuzuki.PauliString list) (parameters: float[]) =
            let rec collect i (acc: float list) =
                if i >= parameters.Length then
                    Ok(acc |> List.rev |> Array.ofList)
                else
                    let k = i / 2

                    match
                        (if i % 2 = 0 then
                             costGradient mixers parameters k
                         else
                             mixerGradient mixers parameters k)
                    with
                    | Error err -> Error err
                    | Ok(g, _) -> collect (i + 1) (g :: acc)

            collect 0 []

        let energy mixers parameters = energyWith mixers parameters None

        let rec screen (mixers: TrotterSuzuki.PauliString list) (parameters: float[]) remaining acc =
            match remaining with
            | [] -> Ok(List.rev acc)
            | (mixer: TrotterSuzuki.PauliString) :: rest ->
                let mixersWith = mixers @ [ mixer ]
                let seeded = Array.append parameters [| config.GammaInit; 0.0 |]

                match mixerGradient mixersWith seeded mixers.Length with
                | Error err -> Error err
                | Ok estimate -> screen mixers parameters rest ((mixer, estimate) :: acc)

        let rec loop layer mixers parameters history ((current, currentError): float * float) =
            let finish converged capped =
                Ok
                    {
                        Energy = current
                        SelectedMixers = mixers
                        Parameters = parameters
                        Layers = layer
                        Converged = converged
                        EnergyHistory = List.rev history
                        EnergyStandardError = Some currentError
                        CloudJobs = submitted.Value
                        JobCapReached = capped
                    }

            // Would the next layer cross the cap? Then stop before submitting any of it.
            let overCap =
                config.MaxCloudJobs
                |> Option.exists (fun cap -> submitted.Value + perLayer (layer + 1) > cap)

            if layer >= config.MaxLayers then
                finish false false
            elif overCap then
                finish false true
            else
                match screen mixers parameters pool [] with
                | Error err -> Error err
                | Ok grads ->
                    let (bestMixer, (bestGrad, bestError)) =
                        grads |> List.maxBy (fun (_, (g, _)) -> abs g)

                    if
                        abs bestGrad
                        <= max config.GradientThreshold (AdaptVqe.SampledGradientSigmas * bestError)
                    then
                        finish true false
                    else
                        let newMixers = mixers @ [ bestMixer ]
                        let init = Array.append parameters [| config.GammaInit; 0.0 |]

                        match AdaptVqe.adamDescent (fullGradient newMixers) init with
                        | Error err -> Error err
                        | Ok optimised ->
                            match energy newMixers optimised with
                            | Error err -> Error err
                            | Ok(fresh, freshError) ->
                                // Keep the layer unless the fresh estimate is significantly worse.
                                let noise = 2.0 * sqrt (freshError * freshError + currentError * currentError)

                                if fresh > current + max 1e-9 noise then
                                    finish false false
                                else
                                    loop (layer + 1) newMixers optimised (fresh :: history) (fresh, freshError)

        // Refuse up front, before any job, when the reference energy and the first layer
        // cannot fit under the cap: such a run could only return the reference state.
        let firstPlan = groups + (if config.MaxLayers >= 1 then perLayer 1 else 0)

        match config.MaxCloudJobs with
        | Some cap when firstPlan > cap ->
            Error(
                QuantumError.ValidationError(
                    "MaxCloudJobs",
                    $"ADAPT-QAOA on shot-sampling backend '{backend.Name}' needs {firstPlan} cloud jobs for the reference energy and its first layer "
                    + $"({groups} per energy: 2·{pool.Length} gradient-screen energies, 2·({costTerms} + 1)·{AdaptVqe.SampledOptimizerSteps} Adam-step energies per layer, 1 fresh energy), "
                    + $"over MaxCloudJobs = {cap}; {config.MaxLayers} layers could need up to {estimateCloudJobs costHamiltonian pool.Length config.MaxLayers}. "
                    + "Raise MaxCloudJobs (None = no cap), shrink the mixer pool, or use fewer cost terms."
                )
            )
        | _ ->
            energy [] [||]
            |> Result.bind (fun (reference, referenceError) -> loop 0 [] [||] [ reference ] (reference, referenceError))

    // ========================================================================
    // RUN
    // ========================================================================

    let private widthError (label: string) (got: int) (expected: int) =
        Error(QuantumError.ValidationError(label, $"width {got} does not match the {expected}-qubit problem."))

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
            Error(QuantumError.ValidationError("numQubits", "must be positive"))
        elif List.isEmpty pool then
            Error(QuantumError.ValidationError("pool", "mixer pool must be non-empty"))
        else
            let badHamTerm =
                costHamiltonian.Terms |> List.tryFind (fun t -> t.Operators.Length <> numQubits)

            let badPoolOp = pool |> List.tryFind (fun p -> p.Operators.Length <> numQubits)

            match badHamTerm, badPoolOp with
            | Some t, _ -> widthError "costHamiltonian" t.Operators.Length numQubits
            | _, Some p -> widthError "pool" p.Operators.Length numQubits
            | None, None when (Primitives.shotsPerCircuit backend).IsSome ->
                runSampled backend costHamiltonian pool numQubits config
            | None, None ->

                // Reference energy ⟨+…+|H|+…+⟩ — also validates the backend supports expectation.
                match stateEnergy backend costHamiltonian numQubits [] [||] with
                | Error e -> Error e
                | Ok referenceEnergy ->

                    // Mixer gradient at the current optimised state: append a fresh layer whose cost
                    // angle is the small seed γ₀ (this breaks the |+…+⟩ symmetry so single-Pauli
                    // mixers have a non-zero gradient) and central-difference the mixer angle β
                    // around 0. γ₀ is the same seed the layer is initialised with before re-optimising.
                    let gradientAt
                        (mixers: TrotterSuzuki.PauliString list)
                        (parameters: float[])
                        (mixer: TrotterSuzuki.PauliString)
                        : QuantumResult<float> =
                        let mixersWith = mixers @ [ mixer ]
                        let eps = config.FiniteDiffEps
                        let g0 = config.GammaInit

                        match
                            stateEnergy
                                backend
                                costHamiltonian
                                numQubits
                                mixersWith
                                (Array.append parameters [| g0; eps |]),
                            stateEnergy
                                backend
                                costHamiltonian
                                numQubits
                                mixersWith
                                (Array.append parameters [| g0; -eps |])
                        with
                        | Ok ePlus, Ok eMinus -> Ok((ePlus - eMinus) / (2.0 * eps))
                        | Error e, _ -> Error e
                        | _, Error e -> Error e

                    let rec loop
                        layer
                        (mixers: TrotterSuzuki.PauliString list)
                        (parameters: float[])
                        (history: float list)
                        (energy: float)
                        : QuantumResult<AdaptQaoaResult> =
                        let finish converged =
                            Ok
                                {
                                    Energy = energy
                                    SelectedMixers = mixers
                                    Parameters = parameters
                                    Layers = layer
                                    Converged = converged
                                    EnergyHistory = List.rev history
                                    EnergyStandardError = None
                                    CloudJobs = 0
                                    JobCapReached = false
                                }

                        if layer >= config.MaxLayers then
                            finish false
                        else
                            let gradsResult =
                                (Ok [], pool)
                                ||> List.fold (fun accR mixer ->
                                    accR
                                    |> Result.bind (fun acc ->
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
                                        (stateEnergy backend costHamiltonian numQubits newMixers p)
                                        |> Result.defaultWith (fun _ -> System.Double.MaxValue)

                                    let (optParams, optEnergy) = optimize objective init
                                    // Guarantee monotonic progress: if this layer can't improve the
                                    // energy (e.g. the optimizer failed to converge), stop with the best.
                                    if optEnergy > energy + 1e-9 then
                                        finish false
                                    else
                                        loop (layer + 1) newMixers optParams (optEnergy :: history) optEnergy

                    loop 0 [] [||] [ referenceEnergy ] referenceEnergy

    // ========================================================================
    // QUBO CONVENIENCE — solve a QUBO / Ising problem end to end
    // ========================================================================

    /// A standard mixer pool for QUBO/optimization problems: single-qubit X and Y on every
    /// qubit. X mixers are the usual QAOA driver; Y mixers give the gradient screen a
    /// symmetry-breaking option so a useful mixer is always found.
    let defaultMixerPool (numQubits: int) : MixerPool =
        [
            for q in 0 .. numQubits - 1 do
                for p in [ 'X'; 'Y' ] do
                    let ops = Array.create numQubits 'I'
                    ops.[q] <- p

                    yield
                        {
                            Operators = ops
                            Coefficient = System.Numerics.Complex(1.0, 0.0)
                        }
        ]

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

                Array.iter2
                    (fun (q: int) (p: QaoaCircuit.PauliOperator) -> ops.[q] <- letterOf p)
                    t.QubitsIndices
                    t.PauliOperators

                {
                    Operators = ops
                    Coefficient = System.Numerics.Complex(t.Coefficient, 0.0)
                })

        {
            Terms = terms
            NumQubits = ph.NumQubits
        }

    /// Classical QUBO cost of a 0/1 assignment: Σ Qᵢⱼ xᵢ xⱼ (diagonal entries are the
    /// linear terms since x² = x for x ∈ {0,1}).
    let quboCost (quboMap: Map<int * int, float>) (assignment: int[]) : float =
        (0.0, quboMap)
        ||> Map.fold (fun acc (i, j) q -> acc + q * float assignment.[i] * float assignment.[j])

    /// Result of solving a QUBO with ADAPT-QAOA.
    type QuboSolution =
        {
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
    /// Suitable for small problems (a handful of qubits). On a shot-sampling backend `run`
    /// takes the measured route (see the module notes) and the final sample is the backend's
    /// own shots; every energy there is a paid job, capped by config.MaxCloudJobs (the final
    /// sample included).
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
            || (quboMap
                |> Map.exists (fun (i, j) _ -> i < 0 || j < 0 || i >= numQubits || j >= numQubits))

        if indexOutOfRange then
            Error(
                QuantumError.ValidationError(
                    "quboMap",
                    $"QUBO variable indices must be in [0, %d{numQubits}); check the keys against numQubits=%d{numQubits}."
                )
            )
        else
            let hamiltonian =
                ofProblemHamiltonian (QaoaCircuit.ProblemHamiltonian.fromQuboSparse numQubits quboMap)

            // On a shot-sampling backend the final sample below is one more job: keep it under
            // the cap too.
            let runConfig =
                match Primitives.shotsPerCircuit backend with
                | Some _ ->
                    { config with
                        MaxCloudJobs = config.MaxCloudJobs |> Option.map (fun cap -> cap - 1)
                    }
                | None -> config

            run backend hamiltonian (defaultMixerPool numQubits) numQubits runConfig
            |> Result.bind (fun adapt ->
                // Sample the optimised ansatz and keep the lowest-cost bitstring observed.
                let circuit =
                    buildAnsatz numQubits hamiltonian adapt.SelectedMixers adapt.Parameters

                // A shot-sampling backend measures its own fixed number of shots per job.
                let shots = Primitives.shotsPerCircuit backend |> Option.defaultValue 2048

                Primitives.sample backend circuit shots
                |> Result.map (fun histogram ->
                    let best =
                        histogram
                        |> Map.toList
                        |> List.map (fun (bits, _) -> bits |> Seq.map (fun c -> int c - int '0') |> Seq.toArray)
                        |> List.minBy (quboCost quboMap)

                    {
                        Assignment = best
                        QuboCost = quboCost quboMap best
                        ExpectedEnergy = adapt.Energy
                        Adapt = adapt
                    }))
