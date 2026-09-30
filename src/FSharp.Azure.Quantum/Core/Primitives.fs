namespace FSharp.Azure.Quantum

open System
open System.Numerics
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Algorithms

/// CUDA-Q-style execution primitives over any `IQuantumBackend`.
///
/// These mirror the familiar `cudaq.sample` / `cudaq.observe` / `cudaq.run` /
/// `cudaq.get_state` surface, so code — or an agent — written against that mental
/// model maps directly onto this library:
///
/// | CUDA-Q                | FSharp.Azure.Quantum      |
/// |-----------------------|---------------------------|
/// | `cudaq.sample`        | `Primitives.sample`       |
/// | `cudaq.run`           | `Primitives.run`          |
/// | `cudaq.observe`       | `Primitives.observe`      |
/// | `cudaq.get_state`     | `Primitives.getState`     |
/// | `cudaq.sample_async`  | `Primitives.sampleAsync`  |
/// | `cudaq.observe_async` | `Primitives.observeAsync` |
///
/// A "kernel" here is a `CircuitBuilder.Circuit`; the backend is any
/// `IQuantumBackend` — the local simulator or a real cloud QPU (IonQ, Rigetti,
/// Quantinuum, Atom Computing). Every primitive returns a `Result`, so a backend
/// rejecting the circuit (a business outcome) surfaces as `Error` rather than throwing.
module Primitives =

    let private toICircuit (circuit: CircuitBuilder.Circuit) : ICircuit = CircuitWrapper(circuit) :> ICircuit

    let private bitsToString (bits: int[]) : string =
        bits |> Array.map string |> String.Concat

    // ========================================================================
    // get_state
    // ========================================================================

    /// Execute a circuit and return the resulting quantum state (full amplitudes
    /// on a simulator). Counterpart of `cudaq.get_state`.
    let getState (backend: IQuantumBackend) (circuit: CircuitBuilder.Circuit) : QuantumResult<QuantumState> =
        backend.ExecuteToState(toICircuit circuit)

    /// Async `getState` — counterpart of `cudaq.get_state` under async submission.
    let getStateAsync
        (backend: IQuantumBackend)
        (circuit: CircuitBuilder.Circuit)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<QuantumState>> =
        backend.ExecuteToStateAsync (toICircuit circuit) cancellationToken

    // ========================================================================
    // sample  (histogram of measured bitstrings)
    // ========================================================================

    let private histogramOf (shots: int) (state: QuantumState) : Map<string, int> =
        UnifiedBackend.measureState state shots
        |> Array.countBy bitsToString
        |> Map.ofArray

    let private shotsError (shots: int) : QuantumError =
        QuantumError.ValidationError("shots", $"Shot count must be non-negative; got %d{shots}.")

    // ========================================================================
    // shot-sampling backends
    //
    // A backend implementing IShotSamplingBackend (cloud hardware and cloud simulators)
    // returns the measured frequencies of its own shots, as amplitudes √(count/shots) with
    // no phase. Its outcomes are read off those frequencies, never resampled: resampling
    // would mix classical pseudo-randomness into a quantum measurement.
    // ========================================================================

    /// Shots per circuit of a backend that returns measured frequencies rather than an exact
    /// state (IShotSamplingBackend with Shots > 0); None for an exact backend.
    let shotsPerCircuit (backend: IQuantumBackend) : int option =
        match backend with
        | :? IShotSamplingBackend as sampling when sampling.Shots > 0 -> Some sampling.Shots
        | _ -> None

    /// Bitstring of a basis index, character q = qubit q.
    let private keyOfIndex (numQubits: int) (index: int) : string =
        String(Array.init numQubits (fun q -> if (index >>> q) &&& 1 = 1 then '1' else '0'))

    /// Outcome frequencies of a returned state, keyed by bitstring with character q = qubit q.
    /// For a shot-sampling backend these are its measured count / shots (|a|² of the returned
    /// amplitudes); for an exact backend, the exact outcome probabilities.
    let private outcomeFrequencies (state: QuantumState) : QuantumResult<(string * float)[]> =
        match state with
        | QuantumState.StateVector sv ->
            let n = StateVector.numQubits sv

            [|
                for i in 0 .. StateVector.dimension sv - 1 do
                    let a = StateVector.getAmplitude i sv
                    let p = a.Magnitude * a.Magnitude

                    if p > 0.0 then
                        yield keyOfIndex n i, p
            |]
            |> Ok
        | QuantumState.SparseState(amplitudes, n) ->
            amplitudes
            |> Map.toArray
            |> Array.choose (fun (i, a) ->
                let p = a.Magnitude * a.Magnitude
                if p > 0.0 then Some(keyOfIndex n i, p) else None)
            |> Ok
        | QuantumState.MeasurementHistogram(histogram, n) ->
            // Keys already have character q = qubit q.
            let total = histogram |> Map.fold (fun acc _ c -> acc + max 0 c) 0 |> max 1 |> float

            histogram
            |> Map.toArray
            |> Array.filter (fun (_, c) -> c > 0)
            |> Array.map (fun (key, c) -> key.PadRight(n, '0'), float c / total)
            |> Ok
        | QuantumState.DensityMatrix(rho, n) ->
            [|
                for i in 0 .. (1 <<< n) - 1 do
                    let p = rho.[i, i].Real

                    if p > 0.0 then
                        yield keyOfIndex n i, p
            |]
            |> Ok
        | other ->
            Error(
                QuantumError.OperationError(
                    "sampled measurement",
                    $"the backend returned a {QuantumState.stateType other} state, which has no computational-basis outcome frequencies"
                )
            )

    /// The counts a shot-sampling backend measured, recovered from its returned state.
    let private measuredCounts (deviceShots: int) (state: QuantumState) : QuantumResult<(string * int)[]> =
        outcomeFrequencies state
        |> Result.map (fun frequencies ->
            frequencies
            |> Array.map (fun (key, p) -> key, int (Math.Round(p * float deviceShots)))
            |> Array.filter (fun (_, count) -> count > 0))

    let private fixedShotsError (backend: IQuantumBackend) (deviceShots: int) (requested: int) : QuantumError =
        QuantumError.ValidationError(
            "shots",
            $"{backend.Name} measures {deviceShots} shots per job, fixed when the backend was created; request {deviceShots}, or create the backend with {requested}. Its results are not resampled to another count, which would mix classical randomness into the measurement."
        )

    /// Execute a circuit and return a histogram of measured bitstrings
    /// (`bitstring -> count`, character q = qubit q). Counterpart of `cudaq.sample`.
    ///
    /// On a shot-sampling backend (IShotSamplingBackend) the histogram is the backend's own
    /// measured counts, so `shots` must equal its Shots; anything else is an Error.
    let sample
        (backend: IQuantumBackend)
        (circuit: CircuitBuilder.Circuit)
        (shots: int)
        : QuantumResult<Map<string, int>> =
        if shots < 0 then
            Error(shotsError shots)
        else
            match shotsPerCircuit backend with
            | Some deviceShots when deviceShots <> shots -> Error(fixedShotsError backend deviceShots shots)
            | Some deviceShots ->
                getState backend circuit
                |> Result.bind (measuredCounts deviceShots)
                |> Result.map Map.ofArray
            | None -> getState backend circuit |> Result.map (histogramOf shots)

    /// Async `sample` — counterpart of `cudaq.sample_async`.
    let sampleAsync
        (backend: IQuantumBackend)
        (circuit: CircuitBuilder.Circuit)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Map<string, int>>> =
        task {
            if shots < 0 then
                return Error(shotsError shots)
            else
                match shotsPerCircuit backend with
                | Some deviceShots when deviceShots <> shots -> return Error(fixedShotsError backend deviceShots shots)
                | Some deviceShots ->
                    let! stateResult = getStateAsync backend circuit cancellationToken

                    return
                        stateResult
                        |> Result.bind (measuredCounts deviceShots)
                        |> Result.map Map.ofArray
                | None ->
                    let! stateResult = getStateAsync backend circuit cancellationToken
                    return stateResult |> Result.map (histogramOf shots)
        }

    // ========================================================================
    // run  (raw per-shot measurement outcomes)
    // ========================================================================

    /// Execute a circuit and return the raw per-shot measurement outcomes
    /// (`shots` arrays of one bit per qubit). Counterpart of `cudaq.run`.
    ///
    /// On a shot-sampling backend (IShotSamplingBackend) the outcomes are the backend's own
    /// measured shots, so `shots` must equal its Shots. The backend reports counts only, so
    /// equal outcomes come grouped (in bitstring order), not in the order they were measured.
    let run (backend: IQuantumBackend) (circuit: CircuitBuilder.Circuit) (shots: int) : QuantumResult<int[][]> =
        if shots < 0 then
            Error(shotsError shots)
        else
            match shotsPerCircuit backend with
            | Some deviceShots when deviceShots <> shots -> Error(fixedShotsError backend deviceShots shots)
            | Some deviceShots ->
                getState backend circuit
                |> Result.bind (measuredCounts deviceShots)
                |> Result.map (fun counts ->
                    counts
                    |> Array.collect (fun (key, count) ->
                        Array.init count (fun _ -> key.ToCharArray() |> Array.map (fun c -> if c = '1' then 1 else 0))))
            | None ->
                getState backend circuit
                |> Result.map (fun state -> UnifiedBackend.measureState state shots)

    // ========================================================================
    // observe  (expectation value of a Pauli Hamiltonian)
    // ========================================================================

    /// Expectation value ⟨ψ|H|ψ⟩ of a Pauli Hamiltonian on a given state vector.
    ///
    /// Pure helper — no backend execution. Works for every state representation that carries
    /// amplitudes (state vector, topological superposition, sparse) and for density matrices
    /// (via Tr(ρH)); returns `Error` for annealing samples, or if a term's width mismatches.
    /// Apply a Pauli string (one 'I'/'X'/'Y'/'Z' per qubit) to a state vector.
    let private applyPauliString (operators: char[]) (sv: StateVector.StateVector) : StateVector.StateVector =
        operators
        |> Array.indexed
        |> Array.fold
            (fun s (q, p) ->
                match Char.ToUpper p with
                | 'X' -> Gates.applyX q s
                | 'Y' -> Gates.applyY q s
                | 'Z' -> Gates.applyZ q s
                | _ -> s // 'I' (identity) — no-op
            )
            sv

    let private widthMismatch (hamiltonian: TrotterSuzuki.PauliHamiltonian) (n: int) =
        hamiltonian.Terms
        |> List.tryFind (fun t -> t.Operators.Length <> n)
        |> Option.map (fun t ->
            QuantumError.ValidationError(
                "Hamiltonian",
                $"Pauli term width {t.Operators.Length} does not match the {n}-qubit state."
            ))

    /// ⟨ψ|H|ψ⟩ = Σ_terms cᵢ ⟨ψ|Pᵢ|ψ⟩ on a dense state vector.
    let private expectationOnStateVector
        (hamiltonian: TrotterSuzuki.PauliHamiltonian)
        (sv: StateVector.StateVector)
        : QuantumResult<float> =
        let n = StateVector.numQubits sv
        let dim = StateVector.dimension sv

        match widthMismatch hamiltonian n with
        | Some err -> Error err
        | None ->
            let termExpectation (term: TrotterSuzuki.PauliString) : float =
                let pPsi = applyPauliString term.Operators sv
                let mutable acc = Complex.Zero

                for i in 0 .. dim - 1 do
                    acc <-
                        acc
                        + Complex.Conjugate(StateVector.getAmplitude i sv)
                          * StateVector.getAmplitude i pPsi

                (term.Coefficient * acc).Real

            hamiltonian.Terms |> List.sumBy termExpectation |> Ok

    /// Largest qubit count for which we densify a state/density matrix here.
    /// `StateVector.create` hard-fails above its own capacity, so reject at that same
    /// bound and return `Error` rather than throwing out of the API (or wastefully
    /// allocating a multi-GB dense vector first). Derived from available memory, so
    /// this is not a constant — see LocalSimulator.StateVector.maxQubits.
    let private maxDenseQubits = StateVector.maxQubits

    /// ⟨H⟩ = Tr(ρH) = Σ_terms cᵢ Σⱼ [Pᵢ · (column j of ρ)]ⱼ on a density matrix.
    let private expectationOnDensityMatrix
        (hamiltonian: TrotterSuzuki.PauliHamiltonian)
        (rho: Complex[,])
        (n: int)
        : QuantumResult<float> =
        match widthMismatch hamiltonian n with
        | Some err -> Error err
        | None when n > maxDenseQubits ->
            Error(
                QuantumError.ValidationError(
                    "numQubits",
                    $"Density-matrix expectation is limited to %d{maxDenseQubits} qubits; got %d{n}."
                )
            )
        | None when Array2D.length1 rho <> (1 <<< n) || Array2D.length2 rho <> (1 <<< n) ->
            Error(
                QuantumError.OperationError(
                    "observe",
                    sprintf
                        "Density matrix is %d×%d but %d qubits implies %d×%d."
                        (Array2D.length1 rho)
                        (Array2D.length2 rho)
                        n
                        (1 <<< n)
                        (1 <<< n)
                )
            )
        | None ->
            let dim = 1 <<< n

            let termTrace (term: TrotterSuzuki.PauliString) : Complex =
                let mutable acc = Complex.Zero

                for j in 0 .. dim - 1 do
                    let column = Array.init dim (fun i -> rho.[i, j])
                    let pColumn = applyPauliString term.Operators (StateVector.create column)
                    acc <- acc + StateVector.getAmplitude j pColumn

                term.Coefficient * acc

            hamiltonian.Terms |> List.sumBy (fun t -> (termTrace t).Real) |> Ok

    /// Build a dense state vector from sparse amplitudes.
    let private denseOfSparse (amplitudes: Map<int, Complex>) (n: int) : StateVector.StateVector =
        let dim = 1 <<< n
        let dense = Array.create dim Complex.Zero

        amplitudes
        |> Map.iter (fun i a ->
            if i >= 0 && i < dim then
                dense.[i] <- a)

        StateVector.create dense

    /// Expectation value ⟨H⟩ of a Pauli Hamiltonian for any state representation.
    ///
    /// The state must carry exact amplitudes. A state returned by a shot-sampling backend
    /// (IShotSamplingBackend) holds √(measured frequency) with every phase dropped, so X/Y
    /// terms read from it are wrong; measure such backends with `observe` or
    /// `sampledExpectation`, which rotate each term into the measured basis.
    let expectation (hamiltonian: TrotterSuzuki.PauliHamiltonian) (state: QuantumState) : QuantumResult<float> =
        match state with
        | QuantumState.StateVector sv -> expectationOnStateVector hamiltonian sv
        | QuantumState.FusionSuperposition superposition ->
            let amplitudes = superposition.GetAmplitudeVector()

            if amplitudes.Length > (1 <<< maxDenseQubits) then
                Error(
                    QuantumError.ValidationError(
                        "numQubits",
                        $"Topological-state expectation is limited to %d{maxDenseQubits} qubits."
                    )
                )
            else
                expectationOnStateVector hamiltonian (StateVector.create amplitudes)
        | QuantumState.SparseState(amplitudes, n) when n > maxDenseQubits ->
            Error(
                QuantumError.ValidationError(
                    "numQubits",
                    $"Sparse-state expectation is limited to %d{maxDenseQubits} qubits; got %d{n}."
                )
            )
        | QuantumState.SparseState(amplitudes, n) -> expectationOnStateVector hamiltonian (denseOfSparse amplitudes n)
        | QuantumState.DensityMatrix(rho, n) -> expectationOnDensityMatrix hamiltonian rho n
        | QuantumState.IsingSamples _ ->
            Error(
                QuantumError.OperationError(
                    "observe",
                    "Expectation values are not defined for annealing samples; observe requires a state-vector, "
                    + "topological, sparse, or density-matrix backend."
                )
            )
        | QuantumState.MeasurementHistogram _ ->
            Error(
                QuantumError.OperationError(
                    "observe",
                    "Expectation values cannot be computed from sampled measurement histograms: off-diagonal "
                    + "(X/Y) Pauli terms require amplitudes, which Z-basis counts do not determine. Use a "
                    + "state-vector, topological, sparse, or density-matrix backend."
                )
            )

    // ========================================================================
    // sampled expectation  (measured shots, one circuit per commuting group)
    // ========================================================================

    /// Pauli terms estimated together from one circuit: they are qubit-wise commuting, so one
    /// basis rotation per qubit diagonalises all of them.
    type PauliMeasurementGroup =
        {
            /// Measured Pauli per qubit: 'X', 'Y' or 'Z', or 'I' where no term of the group acts
            Basis: char[]
            /// Terms estimated from this group's circuit
            Terms: TrotterSuzuki.PauliString list
        }

    /// An expectation value estimated from measured outcomes.
    type SampledExpectation =
        {
            /// Σᵢ Re(cᵢ)·⟨Pᵢ⟩ over the Hamiltonian's terms
            Value: float
            /// Shot-noise standard error of Value; 0 on an exact backend
            StandardError: float
            /// Circuits executed (cloud jobs submitted): one per measurement group
            Circuits: int
            /// Shots per circuit (IShotSamplingBackend); None on an exact backend
            ShotsPerCircuit: int option
        }

    /// Pauli letter of `operators` at qubit q; 'I' past its end.
    let private pauliAt (operators: char[]) (q: int) : char =
        if q < operators.Length then
            Char.ToUpperInvariant operators.[q]
        else
            'I'

    let private isIdentityTerm (term: TrotterSuzuki.PauliString) =
        term.Operators |> Array.forall (fun p -> Char.ToUpperInvariant p = 'I')

    /// Qubit-wise commuting groups of the Hamiltonian's non-identity terms, first fit with the
    /// terms taken widest first (most non-identity qubits; ties in term order): a term joins
    /// the first group whose basis agrees with it on every qubit both act on. Identity terms
    /// are constants and need no circuit; terms with |coefficient| below 1e-12 are left out.
    let measurementGroups (hamiltonian: TrotterSuzuki.PauliHamiltonian) : PauliMeasurementGroup list =
        let width =
            hamiltonian.Terms
            |> List.fold (fun acc t -> max acc t.Operators.Length) hamiltonian.NumQubits

        let letters (term: TrotterSuzuki.PauliString) =
            Array.init width (pauliAt term.Operators)

        let weight (term: TrotterSuzuki.PauliString) =
            letters term |> Array.filter (fun p -> p <> 'I') |> Array.length

        let fits (basis: char[]) (ops: char[]) =
            Array.forall2 (fun b p -> p = 'I' || b = 'I' || b = p) basis ops

        hamiltonian.Terms
        |> List.filter (fun t -> t.Coefficient.Magnitude >= 1e-12 && not (isIdentityTerm t))
        |> List.sortBy (fun t -> -(weight t))
        |> List.fold
            (fun (groups: PauliMeasurementGroup list) term ->
                let ops = letters term

                match groups |> List.tryFindIndex (fun g -> fits g.Basis ops) with
                | Some index ->
                    groups
                    |> List.mapi (fun i g ->
                        if i = index then
                            {
                                Basis = Array.map2 (fun b p -> if p = 'I' then b else p) g.Basis ops
                                Terms = g.Terms @ [ term ]
                            }
                        else
                            g)
                | None -> groups @ [ { Basis = ops; Terms = [ term ] } ])
            []

    /// `preparation` followed by the rotations that map the group's basis to Z on every qubit:
    /// H for X, RX(π/2) for Y (RX(π/2)·Y·RX(-π/2) = Z). Both are native on every cloud target.
    let measurementCircuit
        (preparation: CircuitBuilder.Circuit)
        (group: PauliMeasurementGroup)
        : CircuitBuilder.Circuit =
        let rotations =
            group.Basis
            |> Array.indexed
            |> Array.toList
            |> List.choose (fun (q, p) ->
                match p with
                | 'X' -> Some(CircuitBuilder.H q)
                | 'Y' -> Some(CircuitBuilder.RX(q, Math.PI / 2.0))
                | _ -> None)

        preparation |> CircuitBuilder.addGates rotations

    /// (mean, variance) of Σ Re(cᵢ)·(±1) over the outcome frequencies of a group's circuit.
    let private groupMoments (group: PauliMeasurementGroup) (frequencies: (string * float)[]) : float * float =
        let valueAt (key: string) =
            group.Terms
            |> List.sumBy (fun term ->
                let mutable odd = false

                for q in 0 .. key.Length - 1 do
                    if key.[q] = '1' && pauliAt term.Operators q <> 'I' then
                        odd <- not odd

                if odd then
                    -term.Coefficient.Real
                else
                    term.Coefficient.Real)

        let mean = frequencies |> Array.sumBy (fun (key, p) -> p * valueAt key)
        let second = frequencies |> Array.sumBy (fun (key, p) -> p * (valueAt key) ** 2.0)
        mean, max 0.0 (second - mean * mean)

    let private combine (constant: float) (shots: int option) (perGroup: (float * float) list) : SampledExpectation =
        {
            Value = constant + (perGroup |> List.sumBy fst)
            StandardError =
                match shots with
                | Some s -> sqrt ((perGroup |> List.sumBy snd) / float s)
                | None -> 0.0
            Circuits = perGroup.Length
            ShotsPerCircuit = shots
        }

    let private constantOf (hamiltonian: TrotterSuzuki.PauliHamiltonian) =
        hamiltonian.Terms
        |> List.filter isIdentityTerm
        |> List.sumBy (fun t -> t.Coefficient.Real)

    /// ⟨H⟩ in the state `circuit` prepares, estimated from measured outcomes: one whole circuit
    /// per qubit-wise commuting group of terms (measurementGroups), each ending in the basis
    /// rotations of measurementCircuit and executed with ExecuteToState, the returned outcome
    /// frequencies weighting each term's parity. This is how an expectation is measured on a
    /// shot-sampling backend, whose returned state has no phases; on an exact backend it gives
    /// the exact value. Stops at the first failing circuit, so no further jobs are submitted.
    let sampledExpectation
        (backend: IQuantumBackend)
        (circuit: CircuitBuilder.Circuit)
        (hamiltonian: TrotterSuzuki.PauliHamiltonian)
        : QuantumResult<SampledExpectation> =
        match widthMismatch hamiltonian circuit.QubitCount with
        | Some err -> Error err
        | None ->
            let rec measure (acc: (float * float) list) (groups: PauliMeasurementGroup list) =
                match groups with
                | [] -> Ok(List.rev acc)
                | group :: rest ->
                    match
                        getState backend (measurementCircuit circuit group)
                        |> Result.bind outcomeFrequencies
                    with
                    | Error err -> Error err
                    | Ok frequencies -> measure (groupMoments group frequencies :: acc) rest

            measure [] (measurementGroups hamiltonian)
            |> Result.map (combine (constantOf hamiltonian) (shotsPerCircuit backend))

    /// Async `sampledExpectation`: the groups' circuits are submitted one after another and
    /// the first failure stops the estimate.
    let sampledExpectationAsync
        (backend: IQuantumBackend)
        (circuit: CircuitBuilder.Circuit)
        (hamiltonian: TrotterSuzuki.PauliHamiltonian)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<SampledExpectation>> =
        task {
            match widthMismatch hamiltonian circuit.QubitCount with
            | Some err -> return Error err
            | None ->
                let mutable failure = None
                let perGroup = ResizeArray<float * float>()

                for group in measurementGroups hamiltonian do
                    if failure.IsNone then
                        let! stateResult =
                            getStateAsync backend (measurementCircuit circuit group) cancellationToken

                        match stateResult |> Result.bind outcomeFrequencies with
                        | Error err -> failure <- Some err
                        | Ok frequencies -> perGroup.Add(groupMoments group frequencies)

                return
                    match failure with
                    | Some err -> Error err
                    | None -> Ok(combine (constantOf hamiltonian) (shotsPerCircuit backend) (List.ofSeq perGroup))
        }

    /// Execute a circuit and return the expectation value ⟨H⟩ of a Pauli
    /// Hamiltonian. Counterpart of `cudaq.observe`. Works on any amplitude-carrying or
    /// density-matrix backend (state vector, topological, sparse, noisy density matrix),
    /// exactly; on a shot-sampling backend (IShotSamplingBackend: cloud hardware and cloud
    /// simulators) it is estimated from measured shots by sampledExpectation, one job per
    /// qubit-wise commuting group of terms.
    let observe
        (backend: IQuantumBackend)
        (circuit: CircuitBuilder.Circuit)
        (hamiltonian: TrotterSuzuki.PauliHamiltonian)
        : QuantumResult<float> =
        match shotsPerCircuit backend with
        | Some _ -> sampledExpectation backend circuit hamiltonian |> Result.map (fun e -> e.Value)
        | None -> getState backend circuit |> Result.bind (expectation hamiltonian)

    /// Async `observe` — counterpart of `cudaq.observe_async`.
    let observeAsync
        (backend: IQuantumBackend)
        (circuit: CircuitBuilder.Circuit)
        (hamiltonian: TrotterSuzuki.PauliHamiltonian)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float>> =
        task {
            match shotsPerCircuit backend with
            | Some _ ->
                let! estimate = sampledExpectationAsync backend circuit hamiltonian cancellationToken
                return estimate |> Result.map (fun e -> e.Value)
            | None ->
                let! stateResult = getStateAsync backend circuit cancellationToken
                return stateResult |> Result.bind (expectation hamiltonian)
        }

    // ========================================================================
    // batch / multi-QPU execution
    //
    // Run many circuits concurrently. Cloud backends submit multiple jobs in flight;
    // the local simulator runs them across the thread pool. This is the library's
    // counterpart to CUDA-Q's mqpu circuit batching.
    // ========================================================================

    /// Sample many circuits concurrently on the same backend (e.g. a parameter sweep).
    /// Results are returned in input order.
    let sampleBatchAsync
        (backend: IQuantumBackend)
        (circuits: CircuitBuilder.Circuit list)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Map<string, int>> list> =
        task {
            let! results =
                Task.WhenAll(circuits |> List.map (fun c -> sampleAsync backend c shots cancellationToken))

            return List.ofArray results
        }

    /// Compute ⟨H⟩ for many circuits concurrently on the same backend (e.g. a VQE/QAOA
    /// parameter sweep). Results are returned in input order.
    let observeBatchAsync
        (backend: IQuantumBackend)
        (circuits: CircuitBuilder.Circuit list)
        (hamiltonian: TrotterSuzuki.PauliHamiltonian)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float> list> =
        task {
            let! results =
                Task.WhenAll(
                    circuits
                    |> List.map (fun c -> observeAsync backend c hamiltonian cancellationToken)
                )

            return List.ofArray results
        }

    /// Sample a set of (backend, circuit) jobs concurrently — one circuit per (possibly
    /// distinct) backend, i.e. fan out across multiple QPUs. Results are in input order.
    let sampleDistributedAsync
        (jobs: (IQuantumBackend * CircuitBuilder.Circuit) list)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Map<string, int>> list> =
        task {
            let! results =
                Task.WhenAll(
                    jobs
                    |> List.map (fun (backend, circuit) -> sampleAsync backend circuit shots cancellationToken)
                )

            return List.ofArray results
        }
