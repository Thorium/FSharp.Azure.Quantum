namespace FSharp.Azure.Quantum.Algorithms

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Algorithms.TrotterSuzuki  // brings PauliString/PauliHamiltonian record labels into scope

/// Neutral-atom (Rydberg) analog quantum computing.
///
/// Unlike gate machines, a neutral-atom device is programmed as an *analog* time evolution:
/// atoms are placed at positions, then driven by a global laser pulse with time-dependent
/// Rabi frequency Ω(t) and detuning Δ(t). The Hamiltonian (per atom a two-level ground/Rydberg
/// system, |0⟩=ground, |1⟩=Rydberg) is
///
///   H(t) = Σᵢ (Ω(t)/2) Xᵢ  −  Σᵢ Δ(t) nᵢ  +  Σ_{i<j} Vᵢⱼ nᵢ nⱼ ,   nᵢ = |1⟩⟨1|ᵢ,  Vᵢⱼ = C₆ / rᵢⱼ⁶
///
/// The van-der-Waals term Vᵢⱼ produces the **Rydberg blockade**: two nearby atoms cannot both
/// be excited, which makes neutral-atom devices natively good at Maximum Independent Set.
///
/// This module fits the analog paradigm into the unified model the same way `BraidToGate` fits
/// topological braids: it **Trotterizes the analog evolution into a gate circuit**
/// (drive → `RX(Ω·dt)`, detuning → `P(Δ·dt)`, interaction → `CP(−Vᵢⱼ·dt)`), so a Rydberg
/// program runs on *any* `IQuantumBackend` — local simulator or a gate cloud QPU.
module NeutralAtom =

    /// An atom position in the 2-D plane (arbitrary length units, matched by C₆).
    type Atom = { X: float; Y: float }

    /// One segment of a global pulse, with Ω and Δ ramped linearly over `Duration`.
    type PulseSegment = {
        Duration: float
        RabiStart: float
        RabiEnd: float
        DetuningStart: float
        DetuningEnd: float
    }

    /// A neutral-atom analog program: where the atoms are, how they're driven, and how strong
    /// the van-der-Waals interaction is (C₆).
    type RydbergProgram = {
        Register: Atom list
        Schedule: PulseSegment list
        C6: float
    }

    /// Euclidean distance between two atoms.
    let distance (a: Atom) (b: Atom) : float =
        sqrt ((a.X - b.X) ** 2.0 + (a.Y - b.Y) ** 2.0)

    /// Blockade radius R_b where the interaction equals the drive: Vᵢⱼ = Ω ⇒ R_b = (C₆/Ω)^(1/6).
    /// Atoms closer than R_b cannot both be excited.
    let blockadeRadius (c6: float) (omega: float) : float =
        if omega <= 0.0 then infinity else (c6 / omega) ** (1.0 / 6.0)

    /// Compile the analog program to a gate circuit by first-order Trotterization with
    /// `stepsPerSegment` steps per pulse segment. More steps ⇒ smaller dt ⇒ less Trotter error
    /// (needed when interactions Vᵢⱼ are strong).
    let toCircuit (program: RydbergProgram) (stepsPerSegment: int) : CircuitBuilder.Circuit =
        let atoms = program.Register |> List.toArray
        let n = atoms.Length
        // Precompute pairwise interaction strengths Vᵢⱼ = C₆ / rᵢⱼ⁶.
        let interactions =
            [ for i in 0 .. n - 1 do
                for j in i + 1 .. n - 1 do
                    let r = distance atoms.[i] atoms.[j]
                    let v = if r <= 0.0 then infinity else program.C6 / (r ** 6.0)
                    yield (i, j, v) ]
        let mutable circuit = CircuitBuilder.empty n
        let addGate g = circuit <- CircuitBuilder.addGate g circuit
        for segment in program.Schedule do
            let steps = max 1 stepsPerSegment
            let dt = segment.Duration / float steps
            for s in 0 .. steps - 1 do
                // Sample Ω, Δ at the segment midpoint of this step.
                let frac = (float s + 0.5) / float steps
                let omega = segment.RabiStart + (segment.RabiEnd - segment.RabiStart) * frac
                let delta = segment.DetuningStart + (segment.DetuningEnd - segment.DetuningStart) * frac
                // Drive: exp(-i (Ω/2) X dt) = RX(Ω·dt).
                for i in 0 .. n - 1 do addGate (CircuitBuilder.RX (i, omega * dt))
                // Detuning: exp(i Δ dt nᵢ) = P(Δ·dt).
                if delta <> 0.0 then
                    for i in 0 .. n - 1 do addGate (CircuitBuilder.P (i, delta * dt))
                // Interaction: exp(-i Vᵢⱼ dt nᵢ nⱼ) = CP(-Vᵢⱼ·dt).
                for (i, j, v) in interactions do
                    if System.Double.IsFinite v && v <> 0.0 then
                        addGate (CircuitBuilder.CP (i, j, -v * dt))
        circuit

    /// Run a Rydberg program on a backend (Trotterized to gates) and return the measurement
    /// histogram. Bit i = 1 means atom i ended in the Rydberg state.
    let simulate
        (backend: IQuantumBackend)
        (program: RydbergProgram)
        (stepsPerSegment: int)
        (shots: int)
        : QuantumResult<Map<string, int>> =
        Primitives.sample backend (toCircuit program stepsPerSegment) shots

    // ========================================================================
    // Maximum Independent Set — the neutral-atom "killer app"
    // ========================================================================

    /// Build the standard adiabatic MIS pulse: sweep the detuning from strongly negative
    /// (all atoms in the ground state) to strongly positive (rewarding Rydberg excitations),
    /// while the blockade forbids exciting adjacent atoms — driving the system toward a
    /// maximum independent set of the unit-disk graph defined by the register.
    let maximumIndependentSetProgram (register: Atom list) (c6: float) (omegaMax: float) (finalDetuning: float) (totalTime: float) : RydbergProgram =
        let third = totalTime / 3.0
        { Register = register
          C6 = c6
          Schedule =
            [ // Turn on the drive at large negative detuning.
              { Duration = third; RabiStart = 0.0; RabiEnd = omegaMax; DetuningStart = -finalDetuning; DetuningEnd = -finalDetuning }
              // Sweep the detuning through zero to positive with the drive on.
              { Duration = third; RabiStart = omegaMax; RabiEnd = omegaMax; DetuningStart = -finalDetuning; DetuningEnd = finalDetuning }
              // Ramp the drive back off to freeze the assignment.
              { Duration = third; RabiStart = omegaMax; RabiEnd = 0.0; DetuningStart = finalDetuning; DetuningEnd = finalDetuning } ] }

    /// Whether a measured bitstring is an independent set of the register's unit-disk graph
    /// (no two excited atoms are within the blockade radius).
    let isIndependentSet (program: RydbergProgram) (omega: float) (bitstring: string) : bool =
        let atoms = program.Register |> List.toArray
        let excited = [ for i in 0 .. bitstring.Length - 1 do if bitstring.[i] = '1' then yield i ]
        let rb = blockadeRadius program.C6 omega
        excited
        |> List.forall (fun i ->
            excited |> List.forall (fun j -> i = j || distance atoms.[i] atoms.[j] > rb))

    // ========================================================================
    // Analog quantum simulation — quench dynamics
    // ========================================================================

    /// A sudden quench: a single constant-Ω, constant-Δ pulse of the given duration. Starting
    /// from all atoms in the ground state, this drives coherent Rabi / blockade dynamics that
    /// you can watch by varying `duration` and reading `rydbergDensities` — the neutral-atom
    /// analog-simulation workload (Ising-like quench dynamics) as opposed to optimisation (MIS).
    let quench (register: Atom list) (c6: float) (omega: float) (detuning: float) (duration: float) : RydbergProgram =
        { Register = register
          C6 = c6
          Schedule = [ { Duration = duration; RabiStart = omega; RabiEnd = omega; DetuningStart = detuning; DetuningEnd = detuning } ] }

    /// Evolve an analog program and return the final quantum state (rather than just samples),
    /// so observables such as Rydberg density and correlations can be computed exactly.
    let evolve (backend: IQuantumBackend) (program: RydbergProgram) (stepsPerSegment: int) : QuantumResult<QuantumState> =
        Primitives.getState backend (toCircuit program stepsPerSegment)

    /// Per-atom Rydberg occupation ⟨nᵢ⟩ = (1 − ⟨Zᵢ⟩)/2 for every atom — the key observable of
    /// neutral-atom analog simulation (how excited each atom is). Uses `Primitives.expectation`,
    /// so it works on the state-vector simulator and the density-matrix (noisy) backend alike.
    let rydbergDensities (numAtoms: int) (state: QuantumState) : QuantumResult<float[]> =
        let densityOf (i: int) : QuantumResult<float> =
            let ops = Array.init numAtoms (fun q -> if q = i then 'Z' else 'I')
            let zi : PauliHamiltonian =
                { Terms = [ { Operators = ops; Coefficient = System.Numerics.Complex(1.0, 0.0) } ]; NumQubits = numAtoms }
            Primitives.expectation zi state |> Result.map (fun z -> (1.0 - z) / 2.0)
        // Collect per-atom densities, short-circuiting on the first error.
        (Ok [], [ 0 .. numAtoms - 1 ])
        ||> List.fold (fun accR i -> accR |> Result.bind (fun acc -> densityOf i |> Result.map (fun d -> d :: acc)))
        |> Result.map (List.rev >> List.toArray)

    /// Solve Maximum Independent Set end to end: run the adiabatic sweep, sample, and return the
    /// largest measured configuration that is a *valid* independent set (as atom indices).
    ///
    /// This is the unweighted MIS a global pulse solves. Weighted MIS would need per-atom
    /// (local) detuning — a local-addressing capability the global-pulse model here doesn't have.
    let solveMaximumIndependentSet
        (backend: IQuantumBackend)
        (register: Atom list)
        (c6: float)
        (omega: float)
        (finalDetuning: float)
        (totalTime: float)
        (stepsPerSegment: int)
        (shots: int)
        : QuantumResult<int list> =
        let program = maximumIndependentSetProgram register c6 omega finalDetuning totalTime
        simulate backend program stepsPerSegment shots
        |> Result.map (Map.toList >> List.map fst >> List.filter (isIndependentSet program omega) >> List.sortByDescending (Seq.filter ((=) '1') >> Seq.length) >> List.tryHead >> Option.map (fun b -> [ for i in 0 .. b.Length - 1 do if b.[i] = '1' then yield i ]) >> Option.defaultValue [])

    // ========================================================================
    // Analog variational optimization (variational pulse shaping — "analog QAOA")
    // ========================================================================

    /// Optimize analog pulse parameters to minimise ⟨costHamiltonian⟩. You supply a mapping
    /// `paramsToProgram` from a parameter vector to a `RydbergProgram`; this evolves it, reads
    /// ⟨H⟩ via `Primitives.expectation`, and minimises with the shared Nelder-Mead optimiser
    /// (a coarse 1-D scan for a single parameter). Returns the best parameters and energy.
    ///
    /// This is the analog counterpart of variational gate optimisation (QAOA/VQE): instead of
    /// tuning gate angles you tune pulse knobs (durations, Ω, Δ). State-vector backends only.
    let optimizeAnalog
        (backend: IQuantumBackend)
        (paramsToProgram: float[] -> RydbergProgram)
        (costHamiltonian: PauliHamiltonian)
        (stepsPerSegment: int)
        (initialParameters: float[])
        : QuantumResult<float[] * float> =
        let energyOf (parameters: float[]) : QuantumResult<float> =
            evolve backend (paramsToProgram parameters) stepsPerSegment
            |> Result.bind (Primitives.expectation costHamiltonian)
        // Validate once so a real error (bad Hamiltonian width, non-state-vector backend) surfaces.
        match energyOf initialParameters with
        | Error e -> Error e
        | Ok _ ->
            let objective (p: float[]) =
                (energyOf p) |> Result.defaultWith (fun _ -> System.Double.MaxValue)
            match initialParameters.Length with
            | 0 -> Ok ([||], objective [||])
            | 1 ->
                // 1-D: coarse scan then local refine (Nelder-Mead needs ≥2 dimensions).
                let scan centre halfWidth steps =
                    [ for k in 0 .. steps -> centre - halfWidth + float k * (2.0 * halfWidth / float steps) ]
                    |> List.map (fun t -> t, objective [| t |])
                    |> List.minBy snd
                let seed = initialParameters.[0]
                let (coarseT, _) = scan seed (max 1.0 (abs seed * 2.0 + System.Math.PI)) 60
                let (fineT, fineV) = scan coarseT (System.Math.PI / 20.0) 40
                // If every scanned point errored (MaxValue) or produced a non-finite energy, don't
                // report a fabricated optimum — fall back to the (validated) seed parameter.
                if System.Double.IsNaN fineV || System.Double.IsInfinity fineV || fineV >= System.Double.MaxValue then
                    Ok ([| seed |], objective [| seed |])
                else
                    Ok ([| fineT |], fineV)
            | _ ->
                try
                    let r = QaoaOptimizer.Optimizer.minimize objective initialParameters
                    // Fall back to the seed if the optimizer returns a non-finite objective.
                    if System.Double.IsNaN r.FinalObjectiveValue || System.Double.IsInfinity r.FinalObjectiveValue then
                        Ok (initialParameters, objective initialParameters)
                    else
                        Ok (r.OptimizedParameters, r.FinalObjectiveValue)
                with _ ->
                    Ok (initialParameters, objective initialParameters)
