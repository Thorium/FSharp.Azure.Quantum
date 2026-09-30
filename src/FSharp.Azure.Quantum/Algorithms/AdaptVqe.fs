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
/// Each e^(-iθP) block is realised by `TrotterSuzuki.synthesizePauliEvolution`. Two routes:
///
/// - Exact backends (state vector, density matrix, topological): the energy is
///   `Primitives.expectation` of the returned state, gradients are central differences with
///   `FiniteDiffEps`, and the angles are re-optimised by Nelder-Mead (a 1-D scan for one).
/// - Shot-sampling backends (IShotSamplingBackend: cloud hardware and cloud simulators): the
///   returned state has no phases, so every energy is `Primitives.sampledExpectation` (one
///   whole-circuit job per qubit-wise commuting group G of the Hamiltonian's terms), every
///   gradient is the exact parameter-shift rule dE/dθ = c·[E(θ+π/4c) − E(θ−π/4c)] for a block
///   e^(-iθcP), and the angles are re-optimised by SampledOptimizerSteps steps of Adam on those
///   gradients. A gradient counts only above max(GradientThreshold, 3 standard errors); an
///   iteration is kept only if its fresh energy estimate is not significantly worse. Jobs per
///   iteration: G·(2·|pool| + 2·n·SampledOptimizerSteps + 1) with n angles — cap them with the
///   backend's JobBudget.
module AdaptVqe =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// A pool operator is a Pauli-string generator; the ansatz applies e^(-iθP).
    /// Give the generators a unit coefficient — the variational angle carries the scale.
    type OperatorPool = TrotterSuzuki.PauliString list

    /// ADAPT-VQE configuration.
    [<Struct>]
    type AdaptConfig =
        {
            /// Maximum number of operators to add to the ansatz.
            MaxIterations: int
            /// Stop when the largest pool gradient magnitude is below this.
            GradientThreshold: float
            /// Central-difference step used for gradient screening.
            FiniteDiffEps: float
        }

    /// Sensible defaults (20 operators, 1e-3 gradient cutoff).
    let defaultConfig =
        {
            MaxIterations = 20
            GradientThreshold = 1e-3
            FiniteDiffEps = 1e-4
        }

    /// Result of an ADAPT-VQE run.
    type AdaptResult =
        {
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
    let buildAnsatz
        (numQubits: int)
        (ops: TrotterSuzuki.PauliString list)
        (parameters: float[])
        : CircuitBuilder.Circuit =
        let qubits = [| 0 .. numQubits - 1 |]

        List.zip ops (List.ofArray parameters)
        |> List.fold
            (fun circ (op, theta) -> TrotterSuzuki.synthesizePauliEvolution op theta qubits circ)
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
                [
                    for k in 0..steps -> centre - halfWidth + float k * (2.0 * halfWidth / float steps)
                ]
                |> List.map (fun t -> t, evalAt t)
                |> List.minBy snd

            let (coarseT, _) = scan 0.0 System.Math.PI 60 // coarse over [-π, π]
            let (fineT, fineV) = scan coarseT (System.Math.PI / 30.0) 40 // refine locally
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

    /// Adam steps that re-optimise the angles on a shot-sampling backend.
    [<Literal>]
    let SampledOptimizerSteps = 40

    /// Adam's initial step size, in radians; it decays linearly to a tenth over the steps.
    [<Literal>]
    let SampledLearningRate = 0.1

    /// Standard errors a measured gradient must exceed to count as non-zero.
    [<Literal>]
    let SampledGradientSigmas = 3.0

    /// Parameter-shift derivative of the energy in the time θ of a block e^(-iθcP) (P a Pauli
    /// string, c real): c·[E(θ+π/4c) − E(θ−π/4c)], exact for any state and observable.
    /// `energyWithTime` gives (energy, standard error) with that block's time replaced; the
    /// result is (derivative, standard error).
    let internal parameterShift
        (coefficient: float)
        (time: float)
        (energyWithTime: float -> QuantumResult<float * float>)
        : QuantumResult<float * float> =
        if abs coefficient < 1e-12 then
            Ok(0.0, 0.0)
        else
            let shift = System.Math.PI / (4.0 * coefficient)

            energyWithTime (time + shift)
            |> Result.bind (fun (plus, plusError) ->
                energyWithTime (time - shift)
                |> Result.map (fun (minus, minusError) ->
                    coefficient * (plus - minus),
                    abs coefficient * sqrt (plusError * plusError + minusError * minusError)))

    /// Minimise by Adam from `init`, SampledOptimizerSteps steps of `gradient`: the optimiser
    /// for shot-sampling backends, where every gradient is a noisy measured estimate.
    let internal adamDescent (gradient: float[] -> QuantumResult<float[]>) (init: float[]) : QuantumResult<float[]> =
        let beta1, beta2, epsilon = 0.9, 0.999, 1e-8

        let rec step k (theta: float[]) (m: float[]) (v: float[]) =
            if k > SampledOptimizerSteps || theta.Length = 0 then
                Ok theta
            else
                match gradient theta with
                | Error err -> Error err
                | Ok g ->
                    let m = Array.map2 (fun mi gi -> beta1 * mi + (1.0 - beta1) * gi) m g
                    let v = Array.map2 (fun vi gi -> beta2 * vi + (1.0 - beta2) * gi * gi) v g

                    let rate =
                        SampledLearningRate * (1.0 - 0.9 * float (k - 1) / float SampledOptimizerSteps)

                    let correction1 = 1.0 - beta1 ** float k
                    let correction2 = 1.0 - beta2 ** float k

                    let next =
                        Array.init theta.Length (fun i ->
                            theta.[i]
                            - rate * (m.[i] / correction1) / (sqrt (v.[i] / correction2) + epsilon))

                    step (k + 1) next m v

        step 1 init (Array.zeroCreate init.Length) (Array.zeroCreate init.Length)

    /// ADAPT-VQE on a shot-sampling backend (see the module notes).
    let private runSampled
        (backend: IQuantumBackend)
        (hamiltonian: TrotterSuzuki.PauliHamiltonian)
        (pool: OperatorPool)
        (numQubits: int)
        (config: AdaptConfig)
        : QuantumResult<AdaptResult> =
        let energy (ops: TrotterSuzuki.PauliString list) (parameters: float[]) : QuantumResult<float * float> =
            Primitives.sampledExpectation backend (buildAnsatz numQubits ops parameters) hamiltonian
            |> Result.map (fun e -> e.Value, e.StandardError)

        /// d energy / d parameters.[k] by the parameter-shift rule.
        let gradientAt (ops: TrotterSuzuki.PauliString list) (parameters: float[]) (k: int) =
            parameterShift (ops.[k].Coefficient.Real) parameters.[k] (fun time ->
                energy ops (Array.updateAt k time parameters))

        let fullGradient (ops: TrotterSuzuki.PauliString list) (parameters: float[]) =
            let rec collect k (acc: float list) =
                if k >= parameters.Length then
                    Ok(acc |> List.rev |> Array.ofList)
                else
                    match gradientAt ops parameters k with
                    | Error err -> Error err
                    | Ok(g, _) -> collect (k + 1) (g :: acc)

            collect 0 []

        let rec screen (ops: TrotterSuzuki.PauliString list) (parameters: float[]) remaining acc =
            match remaining with
            | [] -> Ok(List.rev acc)
            | (op: TrotterSuzuki.PauliString) :: rest ->
                let opsWith = ops @ [ op ]

                match gradientAt opsWith (Array.append parameters [| 0.0 |]) parameters.Length with
                | Error err -> Error err
                | Ok estimate -> screen ops parameters rest ((op, estimate) :: acc)

        let rec loop iter ops parameters history ((current, currentError): float * float) =
            let finish converged =
                Ok
                    {
                        Energy = current
                        SelectedOperators = ops
                        Parameters = parameters
                        Iterations = iter
                        Converged = converged
                        EnergyHistory = List.rev history
                    }

            if iter >= config.MaxIterations then
                finish false
            else
                match screen ops parameters pool [] with
                | Error err -> Error err
                | Ok grads ->
                    let (bestOp, (bestGrad, bestError)) = grads |> List.maxBy (fun (_, (g, _)) -> abs g)

                    if abs bestGrad <= max config.GradientThreshold (SampledGradientSigmas * bestError) then
                        finish true
                    else
                        let newOps = ops @ [ bestOp ]

                        match adamDescent (fullGradient newOps) (Array.append parameters [| 0.0 |]) with
                        | Error err -> Error err
                        | Ok optimised ->
                            match energy newOps optimised with
                            | Error err -> Error err
                            | Ok(fresh, freshError) ->
                                // Keep the operator unless the fresh estimate is significantly worse.
                                let noise = 2.0 * sqrt (freshError * freshError + currentError * currentError)

                                if fresh > current + max 1e-9 noise then
                                    finish false
                                else
                                    loop (iter + 1) newOps optimised (fresh :: history) (fresh, freshError)

        energy [] [||]
        |> Result.bind (fun (reference, referenceError) -> loop 0 [] [||] [ reference ] (reference, referenceError))

    // ========================================================================
    // RUN
    // ========================================================================

    let private widthError (label: string) (got: int) (expected: int) =
        Error(QuantumError.ValidationError(label, $"width {got} does not match the {expected}-qubit problem."))

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
            Error(QuantumError.ValidationError("numQubits", "must be positive"))
        elif List.isEmpty pool then
            Error(QuantumError.ValidationError("pool", "operator pool must be non-empty"))
        else
            let badHamTerm =
                hamiltonian.Terms |> List.tryFind (fun t -> t.Operators.Length <> numQubits)

            let badPoolOp = pool |> List.tryFind (fun p -> p.Operators.Length <> numQubits)

            match badHamTerm, badPoolOp with
            | Some t, _ -> widthError "hamiltonian" t.Operators.Length numQubits
            | _, Some p -> widthError "pool" p.Operators.Length numQubits
            | None, None when (Primitives.shotsPerCircuit backend).IsSome ->
                runSampled backend hamiltonian pool numQubits config
            | None, None ->

                // Reference (|0…0⟩) energy — also validates the backend supports expectation.
                match stateEnergy backend hamiltonian numQubits [] [||] with
                | Error e -> Error e
                | Ok referenceEnergy ->

                    // Gradient contributed by appending `op` to (ops, parameters):
                    // central difference of the energy in the new angle around 0.
                    let gradientAt
                        (ops: TrotterSuzuki.PauliString list)
                        (parameters: float[])
                        (op: TrotterSuzuki.PauliString)
                        : QuantumResult<float> =
                        let opsWith = ops @ [ op ]
                        let eps = config.FiniteDiffEps

                        match
                            stateEnergy backend hamiltonian numQubits opsWith (Array.append parameters [| eps |]),
                            stateEnergy backend hamiltonian numQubits opsWith (Array.append parameters [| -eps |])
                        with
                        | Ok ePlus, Ok eMinus -> Ok((ePlus - eMinus) / (2.0 * eps))
                        | Error e, _ -> Error e
                        | _, Error e -> Error e

                    let rec loop
                        iter
                        (ops: TrotterSuzuki.PauliString list)
                        (parameters: float[])
                        (history: float list)
                        (energy: float)
                        : QuantumResult<AdaptResult> =
                        let finish converged =
                            Ok
                                {
                                    Energy = energy
                                    SelectedOperators = ops
                                    Parameters = parameters
                                    Iterations = iter
                                    Converged = converged
                                    EnergyHistory = List.rev history
                                }

                        if iter >= config.MaxIterations then
                            finish false
                        else
                            // Screen the whole pool (short-circuit on the first Error).
                            let gradsResult =
                                (Ok [], pool)
                                ||> List.fold (fun accR op ->
                                    accR
                                    |> Result.bind (fun acc ->
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
                                        (stateEnergy backend hamiltonian numQubits newOps p)
                                        |> Result.defaultWith (fun _ -> System.Double.MaxValue)

                                    let (optParams, optEnergy) = optimize objective init
                                    // Guarantee monotonic progress: if this operator can't improve the
                                    // energy (e.g. the optimizer failed to converge), stop with the best.
                                    if optEnergy > energy + 1e-9 then
                                        finish false
                                    else
                                        loop (iter + 1) newOps optParams (optEnergy :: history) optEnergy

                    loop 0 [] [||] [ referenceEnergy ] referenceEnergy
