namespace FSharp.Azure.Quantum

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

    let private toICircuit (circuit: CircuitBuilder.Circuit) : ICircuit =
        CircuitWrapper(circuit) :> ICircuit

    let private bitsToString (bits: int[]) : string =
        bits |> Array.map string |> System.String.Concat

    // ========================================================================
    // get_state
    // ========================================================================

    /// Execute a circuit and return the resulting quantum state (full amplitudes
    /// on a simulator). Counterpart of `cudaq.get_state`.
    let getState (backend: IQuantumBackend) (circuit: CircuitBuilder.Circuit) : QuantumResult<QuantumState> =
        backend.ExecuteToState (toICircuit circuit)

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
        QuantumError.ValidationError ("shots", $"Shot count must be non-negative; got %d{shots}.")

    /// Execute a circuit and return a histogram of measured bitstrings
    /// (`bitstring -> count`). Counterpart of `cudaq.sample`.
    let sample (backend: IQuantumBackend) (circuit: CircuitBuilder.Circuit) (shots: int) : QuantumResult<Map<string, int>> =
        if shots < 0 then Error (shotsError shots)
        else getState backend circuit |> Result.map (histogramOf shots)

    /// Async `sample` — counterpart of `cudaq.sample_async`.
    let sampleAsync
        (backend: IQuantumBackend)
        (circuit: CircuitBuilder.Circuit)
        (shots: int)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<Map<string, int>>> =
        task {
            if shots < 0 then return Error (shotsError shots)
            else
                let! stateResult = getStateAsync backend circuit cancellationToken
                return stateResult |> Result.map (histogramOf shots)
        }

    // ========================================================================
    // run  (raw per-shot measurement outcomes)
    // ========================================================================

    /// Execute a circuit and return the raw per-shot measurement outcomes
    /// (`shots` arrays of one bit per qubit). Counterpart of `cudaq.run`.
    let run (backend: IQuantumBackend) (circuit: CircuitBuilder.Circuit) (shots: int) : QuantumResult<int[][]> =
        if shots < 0 then Error (shotsError shots)
        else getState backend circuit |> Result.map (fun state -> UnifiedBackend.measureState state shots)

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
        |> Array.fold (fun s (q, p) ->
            match System.Char.ToUpper p with
            | 'X' -> Gates.applyX q s
            | 'Y' -> Gates.applyY q s
            | 'Z' -> Gates.applyZ q s
            | _   -> s   // 'I' (identity) — no-op
        ) sv

    let private widthMismatch (hamiltonian: TrotterSuzuki.PauliHamiltonian) (n: int) =
        hamiltonian.Terms
        |> List.tryFind (fun t -> t.Operators.Length <> n)
        |> Option.map (fun t ->
            QuantumError.ValidationError ("Hamiltonian",
                $"Pauli term width {t.Operators.Length} does not match the {n}-qubit state."))

    /// ⟨ψ|H|ψ⟩ = Σ_terms cᵢ ⟨ψ|Pᵢ|ψ⟩ on a dense state vector.
    let private expectationOnStateVector (hamiltonian: TrotterSuzuki.PauliHamiltonian) (sv: StateVector.StateVector) : QuantumResult<float> =
        let n = StateVector.numQubits sv
        let dim = StateVector.dimension sv
        match widthMismatch hamiltonian n with
        | Some err -> Error err
        | None ->
            let termExpectation (term: TrotterSuzuki.PauliString) : float =
                let pPsi = applyPauliString term.Operators sv
                let mutable acc = Complex.Zero
                for i in 0 .. dim - 1 do
                    acc <- acc + Complex.Conjugate(StateVector.getAmplitude i sv) * StateVector.getAmplitude i pPsi
                (term.Coefficient * acc).Real
            hamiltonian.Terms |> List.sumBy termExpectation |> Ok

    /// Largest qubit count for which we densify a state/density matrix here. `StateVector.create`
    /// itself hard-fails above 20 qubits, so reject at that bound and return `Error` rather than
    /// throwing out of the API (or wastefully allocating a multi-GB dense vector first).
    [<Literal>]
    let private maxDenseQubits = 20

    /// ⟨H⟩ = Tr(ρH) = Σ_terms cᵢ Σⱼ [Pᵢ · (column j of ρ)]ⱼ on a density matrix.
    let private expectationOnDensityMatrix (hamiltonian: TrotterSuzuki.PauliHamiltonian) (rho: Complex[,]) (n: int) : QuantumResult<float> =
        match widthMismatch hamiltonian n with
        | Some err -> Error err
        | None when n > maxDenseQubits ->
            Error (QuantumError.ValidationError ("numQubits", $"Density-matrix expectation is limited to %d{maxDenseQubits} qubits; got %d{n}."))
        | None when Array2D.length1 rho <> (1 <<< n) || Array2D.length2 rho <> (1 <<< n) ->
            Error (QuantumError.OperationError ("observe",
                sprintf "Density matrix is %d×%d but %d qubits implies %d×%d." (Array2D.length1 rho) (Array2D.length2 rho) n (1 <<< n) (1 <<< n)))
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
        amplitudes |> Map.iter (fun i a -> if i >= 0 && i < dim then dense.[i] <- a)
        StateVector.create dense

    /// Expectation value ⟨H⟩ of a Pauli Hamiltonian for any state representation.
    let expectation (hamiltonian: TrotterSuzuki.PauliHamiltonian) (state: QuantumState) : QuantumResult<float> =
        match state with
        | QuantumState.StateVector sv -> expectationOnStateVector hamiltonian sv
        | QuantumState.FusionSuperposition superposition ->
            let amplitudes = superposition.GetAmplitudeVector()
            if amplitudes.Length > (1 <<< maxDenseQubits) then
                Error (QuantumError.ValidationError ("numQubits", $"Topological-state expectation is limited to %d{maxDenseQubits} qubits."))
            else
                expectationOnStateVector hamiltonian (StateVector.create amplitudes)
        | QuantumState.SparseState (amplitudes, n) when n > maxDenseQubits ->
            Error (QuantumError.ValidationError ("numQubits", $"Sparse-state expectation is limited to %d{maxDenseQubits} qubits; got %d{n}."))
        | QuantumState.SparseState (amplitudes, n) ->
            expectationOnStateVector hamiltonian (denseOfSparse amplitudes n)
        | QuantumState.DensityMatrix (rho, n) -> expectationOnDensityMatrix hamiltonian rho n
        | QuantumState.IsingSamples _ ->
            Error (QuantumError.OperationError ("observe",
                "Expectation values are not defined for annealing samples; observe requires a state-vector, " +
                "topological, sparse, or density-matrix backend."))
        | QuantumState.MeasurementHistogram _ ->
            Error (QuantumError.OperationError ("observe",
                "Expectation values cannot be computed from sampled measurement histograms: off-diagonal " +
                "(X/Y) Pauli terms require amplitudes, which Z-basis counts do not determine. Use a " +
                "state-vector, topological, sparse, or density-matrix backend."))

    /// Execute a circuit and return the expectation value ⟨H⟩ of a Pauli
    /// Hamiltonian. Counterpart of `cudaq.observe`. Works on any amplitude-carrying or
    /// density-matrix backend (state vector, topological, sparse, noisy density matrix).
    let observe
        (backend: IQuantumBackend)
        (circuit: CircuitBuilder.Circuit)
        (hamiltonian: TrotterSuzuki.PauliHamiltonian)
        : QuantumResult<float> =
        getState backend circuit |> Result.bind (expectation hamiltonian)

    /// Async `observe` — counterpart of `cudaq.observe_async`.
    let observeAsync
        (backend: IQuantumBackend)
        (circuit: CircuitBuilder.Circuit)
        (hamiltonian: TrotterSuzuki.PauliHamiltonian)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<float>> =
        task {
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
            let! results = Task.WhenAll(circuits |> List.map (fun c -> sampleAsync backend c shots cancellationToken))
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
            let! results = Task.WhenAll(circuits |> List.map (fun c -> observeAsync backend c hamiltonian cancellationToken))
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
            let! results = Task.WhenAll(jobs |> List.map (fun (backend, circuit) -> sampleAsync backend circuit shots cancellationToken))
            return List.ofArray results
        }
