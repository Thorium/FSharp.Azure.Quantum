module FSharp.Azure.Quantum.Tests.QaoaConventionTests

// The shared QAOA pipeline's gate order, sign convention and angle scale, checked against
// independent state-vector simulations, and every QAOA solver's default angles checked
// against uniform sampling with exact probabilities (no sampling, so no flakiness).

open System
open System.Numerics
open System.Threading
open System.Threading.Tasks
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.GraphOptimization
open FSharp.Azure.Quantum.Quantum

let private backend () =
    LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend

let private bitsOf (n: int) (index: int) =
    Array.init n (fun q -> (index >>> q) &&& 1)

/// QUBO energy of every basis state (bit q of the index = variable q).
let private energies (qubo: float[,]) =
    let n = Array2D.length1 qubo
    Array.init (1 <<< n) (bitsOf n >> QaoaExecutionHelpers.evaluateQubo qubo)

let private amplitudesOf (state: QuantumState) : Complex[] =
    match state with
    | QuantumState.StateVector sv -> Array.init (StateVector.dimension sv) (fun i -> StateVector.getAmplitude i sv)
    | other -> failwith $"expected a state vector, got {other}"

let private probabilities (amplitudes: Complex[]) =
    amplitudes |> Array.map (fun a -> a.Real * a.Real + a.Imaginary * a.Imaginary)

/// |⟨a|b⟩|², insensitive to a global phase.
let private fidelity (a: Complex[]) (b: Complex[]) =
    let overlap =
        Array.fold2 (fun acc (x: Complex) y -> acc + Complex.Conjugate x * y) Complex.Zero a b

    overlap.Magnitude * overlap.Magnitude

/// Exact probabilities of the circuit the solver pipeline runs for this QUBO and angles
/// (cost Hamiltonian normalised, standard mixer), executed on LocalBackend.
let private solverProbabilities (qubo: float[,]) (parameters: (float * float)[]) =
    let n = Array2D.length1 qubo
    let hamiltonian = ProblemHamiltonian.fromQubo qubo |> ProblemHamiltonian.normalize
    let circuit = QaoaCircuit.build hamiltonian (MixerHamiltonian.create n) parameters

    match (backend ()).ExecuteToState(CircuitAbstraction.QaoaCircuitWrapper(circuit)) with
    | Ok state -> amplitudesOf state |> probabilities
    | Error err -> failwith $"execution failed: {err}"

[<Struct>]
type private Quality = { Expected: float; POptimum: float }

let private quality (e: float[]) (p: float[]) =
    let minE = Array.min e

    {
        Expected = Array.fold2 (fun acc ei pi -> acc + ei * pi) 0.0 e p
        POptimum = Array.fold2 (fun acc ei pi -> if ei <= minE + 1e-9 then acc + pi else acc) 0.0 e p
    }

/// p-layer QAOA at `parameters` must beat uniform sampling both in expected energy and in
/// the probability of measuring an optimal bitstring.
let private assertBeatsUniform (label: string) (qubo: float[,]) (parameters: (float * float)[]) =
    let e = energies qubo
    let uniform = quality e (Array.create e.Length (1.0 / float e.Length))
    let qaoa = quality e (solverProbabilities qubo parameters)

    Assert.True(
        qaoa.Expected < uniform.Expected,
        $"{label}: E = {qaoa.Expected} at {parameters}, uniform E = {uniform.Expected}"
    )

    Assert.True(
        qaoa.POptimum > uniform.POptimum,
        $"{label}: P(optimum) = {qaoa.POptimum} at {parameters}, uniform = {uniform.POptimum}"
    )

let private ok (r: Result<'T, QuantumError>) =
    r |> Result.defaultWith (fun err -> failwith $"%A{err}")

let private dense (q: QuboMatrix) = Qubo.toDenseArray q.NumVariables q.Q

let private randomQubo (seed: int) (n: int) =
    let rng = Random(seed)

    Array2D.init n n (fun i j ->
        if j >= i then
            Math.Round(rng.NextDouble() * 4.0 - 2.0, 2)
        else
            0.0)

// ============================================================================
// GATE ORDER
// ============================================================================

/// Independent state-vector simulator for QaoaCircuit gates (own matrices, no library
/// simulator code), applied in the given order starting from |0…0⟩.
let private simulateGates (numQubits: int) (gates: QuantumGate seq) : Complex[] =
    let dim = 1 <<< numQubits
    let amp = Array.create dim Complex.Zero
    amp.[0] <- Complex.One

    let single q (m00: Complex) (m01: Complex) (m10: Complex) (m11: Complex) =
        let mask = 1 <<< q

        for i in 0 .. dim - 1 do
            if i &&& mask = 0 then
                let a0 = amp.[i]
                let a1 = amp.[i ||| mask]
                amp.[i] <- m00 * a0 + m01 * a1
                amp.[i ||| mask] <- m10 * a0 + m11 * a1

    let bit i q = (i >>> q) &&& 1

    for gate in gates do
        match gate with
        | H q ->
            let h = Complex(1.0 / sqrt 2.0, 0.0)
            single q h h h (-h)
        | RX(q, t) ->
            let c = Complex(cos (t / 2.0), 0.0)
            let s = Complex(0.0, -sin(t / 2.0))
            single q c s s c
        | RY(q, t) ->
            let c = Complex(cos (t / 2.0), 0.0)
            let s = Complex(sin (t / 2.0), 0.0)
            single q c (-s) s c
        | RZ(q, t) ->
            for i in 0 .. dim - 1 do
                let z = if bit i q = 0 then 1.0 else -1.0
                amp.[i] <- amp.[i] * Complex.Exp(Complex(0.0, -t * z / 2.0))
        | RZZ(q1, q2, t) ->
            for i in 0 .. dim - 1 do
                let zz = if bit i q1 = bit i q2 then 1.0 else -1.0
                amp.[i] <- amp.[i] * Complex.Exp(Complex(0.0, -t * zz / 2.0))
        | CNOT(c, t) ->
            for i in 0 .. dim - 1 do
                if bit i c = 1 && bit i t = 0 then
                    let j = i ||| (1 <<< t)
                    let tmp = amp.[i]
                    amp.[i] <- amp.[j]
                    amp.[j] <- tmp

    amp

let private programOrder (circuit: QaoaCircuit) : QuantumGate list =
    [
        yield! circuit.InitialStateGates
        for layer in circuit.Layers do
            yield! layer.CostGates
            yield! layer.MixerGates
    ]

[<Fact>]
let ``LocalBackend runs qaoaCircuitToCircuit in program order`` () =
    let qubo = randomQubo 3 5
    let hamiltonian = ProblemHamiltonian.fromQubo qubo

    let circuit =
        QaoaCircuit.build hamiltonian (MixerHamiltonian.create 5) [| (0.37, 0.61); (-0.8, 0.3) |]

    let viaBackend =
        match (backend ()).ExecuteToState(CircuitAbstraction.QaoaCircuitWrapper(circuit)) with
        | Ok state -> amplitudesOf state
        | Error err -> failwith $"{err}"

    let viaGateCircuit =
        let gateCircuit = CircuitAbstraction.CircuitAdapter.qaoaCircuitToCircuit circuit

        match (backend ()).ExecuteToState(CircuitAbstraction.CircuitWrapper(gateCircuit)) with
        | Ok state -> amplitudesOf state
        | Error err -> failwith $"{err}"

    let forward = simulateGates 5 (programOrder circuit)
    let backward = simulateGates 5 (programOrder circuit |> List.rev)

    Assert.Equal(1.0, fidelity forward viaBackend, 9)
    Assert.Equal(1.0, fidelity forward viaGateCircuit, 9)
    // The check discriminates: the same gates run backwards give a different state.
    Assert.True(
        fidelity backward viaBackend < 0.99,
        $"fidelity with the reversed circuit {fidelity backward viaBackend}"
    )

[<Fact>]
let ``qaoaCircuitToCircuit stores gates most-recent-first like every CircuitBuilder circuit`` () =
    let circuit =
        QaoaCircuit.build (ProblemHamiltonian.fromQubo (randomQubo 5 3)) (MixerHamiltonian.create 3) [| (0.4, 0.2) |]

    let gateCircuit = CircuitAbstraction.CircuitAdapter.qaoaCircuitToCircuit circuit
    let first = CircuitBuilder.getGates gateCircuit |> List.head
    Assert.Equal(CircuitBuilder.Gate.H 0, first)
    // the last mixer rotation is the most recent gate
    Assert.Equal(CircuitBuilder.Gate.RX(2, -0.4), List.head gateCircuit.Gates)

[<Fact>]
let ``CircuitBuilder multiGate keeps program order`` () =
    let c =
        CircuitBuilder.circuit {
            qubits 1
            yield! CircuitBuilder.multiGate [ CircuitBuilder.Gate.H 0; CircuitBuilder.Gate.RZ(0, 0.3) ]
        }

    Assert.Equal<CircuitBuilder.Gate list>(
        [ CircuitBuilder.Gate.H 0; CircuitBuilder.Gate.RZ(0, 0.3) ],
        CircuitBuilder.getGates c
    )

// ============================================================================
// SIGN CONVENTION AND SCALE
// ============================================================================

[<Fact>]
let ``QAOA state is e^(-iβ(-ΣX)) e^(-iγ f(x)/s) |+⟩ with s the largest Ising coefficient`` () =
    let n = 5
    let qubo = randomQubo 7 n
    let e = energies qubo

    let scale =
        (ProblemHamiltonian.fromQubo qubo).Terms
        |> Array.map (fun t -> abs t.Coefficient)
        |> Array.max

    let reference (gamma: float) (beta: float) =
        let dim = 1 <<< n

        let amp =
            Array.init dim (fun i -> Complex.Exp(Complex(0.0, -gamma * e.[i] / scale)) / sqrt (float dim))
        // e^(+iβX) on every qubit
        let c = Complex(cos beta, 0.0)
        let s = Complex(0.0, sin beta)

        for q in 0 .. n - 1 do
            let mask = 1 <<< q

            for i in 0 .. dim - 1 do
                if i &&& mask = 0 then
                    let a0 = amp.[i]
                    let a1 = amp.[i ||| mask]
                    amp.[i] <- c * a0 + s * a1
                    amp.[i ||| mask] <- s * a0 + c * a1

        probabilities amp

    let actual = solverProbabilities qubo [| (0.43, 0.29) |]
    let expected = reference 0.43 0.29

    Array.iter2 (fun (a: float) (b: float) -> Assert.Equal(b, a, 10)) actual expected

[<Fact>]
let ``positive small angles lower the expected energy and the mirrored angles raise it`` () =
    let qubo = randomQubo 11 6
    let e = energies qubo
    let uniform = Array.average e
    let lowered = quality e (solverProbabilities qubo [| (0.05, Math.PI / 8.0) |])
    let raised = quality e (solverProbabilities qubo [| (-0.05, Math.PI / 8.0) |])
    Assert.True(lowered.Expected < uniform, $"E(+γ) = {lowered.Expected}, uniform {uniform}")
    Assert.True(raised.Expected > uniform, $"E(-γ) = {raised.Expected}, uniform {uniform}")

[<Fact>]
let ``normalize scales the largest coefficient to 1 and keeps the ratios`` () =
    let hamiltonian =
        ProblemHamiltonian.fromQubo (array2D [ [ -40.0; 30.0 ]; [ 0.0; 10.0 ] ])

    let normalized = ProblemHamiltonian.normalize hamiltonian

    let largest =
        hamiltonian.Terms |> Array.map (fun t -> abs t.Coefficient) |> Array.max

    Assert.Equal(1.0, normalized.Terms |> Array.map (fun t -> abs t.Coefficient) |> Array.max, 12)

    Array.iter2
        (fun (t: HamiltonianTerm) (u: HamiltonianTerm) ->
            Assert.Equal(t.Coefficient / largest, u.Coefficient, 12)
            Assert.Equal<int[]>(t.QubitsIndices, u.QubitsIndices))
        hamiltonian.Terms
        normalized.Terms

    Assert.Equal(normalized, ProblemHamiltonian.normalize normalized)

    let empty: ProblemHamiltonian = { NumQubits = 2; Terms = [||] }
    Assert.Equal(empty, ProblemHamiltonian.normalize empty)

[<Fact>]
let ``QaoaSimulator mixer follows the same minimising convention`` () =
    let coefficients = [| 1.0; -0.5; 0.7 |]

    let state =
        QaoaSimulator.runQaoaCircuit 3 [| 0.1 |] [| Math.PI / 8.0 |] coefficients

    let mirrored =
        QaoaSimulator.runQaoaCircuit 3 [| -0.1 |] [| Math.PI / 8.0 |] coefficients
    // ⟨C⟩ = 0 in the uniform superposition
    Assert.True(QaoaSimulator.computeCostExpectation coefficients state < 0.0)
    Assert.True(QaoaSimulator.computeCostExpectation coefficients mirrored > 0.0)

// ============================================================================
// THE CIRCUIT EACH FIXED-ANGLE SOLVER EXECUTES
// ============================================================================

/// The QUBO QuantumTspSolver builds for a distance matrix.
let private tspQubo (d: float[,]) = QuantumTspSolver.toQubo d

let private tsp3 =
    array2D [ [ 0.0; 2.0; 3.0 ]; [ 2.0; 0.0; 4.0 ]; [ 3.0; 4.0; 0.0 ] ]

/// LocalBackend that records every QAOA circuit it executes.
type private RecordingBackend() =
    let inner = LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
    let circuits = ResizeArray<QaoaCircuit>()

    let record (circuit: CircuitAbstraction.ICircuit) =
        match circuit with
        | :? CircuitAbstraction.QaoaCircuitWrapper as w -> circuits.Add w.QaoaCircuit
        | _ -> ()

    member _.Circuits = circuits

    interface BackendAbstraction.IQuantumBackend with
        member _.Name = inner.Name
        member _.NativeStateType = inner.NativeStateType
        member _.SupportsOperation op = inner.SupportsOperation op
        member _.InitializeState n = inner.InitializeState n
        member _.ApplyOperation op state = inner.ApplyOperation op state

        member _.ExecuteToState circuit =
            record circuit
            inner.ExecuteToState circuit

        member _.ExecuteToStateAsync circuit ct =
            record circuit
            inner.ExecuteToStateAsync circuit ct

        member _.ApplyOperationAsync op state ct = inner.ApplyOperationAsync op state ct

/// The recorded circuit is p = 1 at `angles`, built from the normalised cost Hamiltonian
/// of `qubo` and the standard mixer: exactly what solverProbabilities simulates.
let private assertSolverCircuitAt
    (angles: float * float)
    (label: string)
    (recorder: RecordingBackend)
    (qubo: float[,])
    =
    Assert.True(recorder.Circuits.Count > 0, $"{label}: no QAOA circuit executed")
    let circuit = recorder.Circuits |> Seq.last
    let n = Array2D.length1 qubo

    let expected =
        QaoaCircuit.build
            (ProblemHamiltonian.fromQubo qubo |> ProblemHamiltonian.normalize)
            (MixerHamiltonian.create n)
            [| angles |]

    Assert.Equal(expected, circuit)

/// assertSolverCircuitAt for the solvers whose default angles are (0.5, 0.5).
let private assertSolverCircuit = assertSolverCircuitAt (0.5, 0.5)

[<Fact>]
let ``fixed-angle solvers execute the normalised default circuit`` () : Task =
    let run (solve: BackendAbstraction.IQuantumBackend -> Threading.Tasks.Task<Result<'T, QuantumError>>) =
        task {
            let recorder = RecordingBackend()
            let! result = solve (recorder :> BackendAbstraction.IQuantumBackend)
            result |> ok |> ignore
            return recorder
        }

    task {
        let maxCut: QuantumMaxCutSolver.MaxCutProblem =
            {
                Vertices = [ for i in 0..5 -> string i ]
                Edges =
                    [ for i in 0..5 -> edge (string i) (string ((i + 1) % 6)) 1.0 ]
                    @ [ edge "0" "3" 1.0 ]
            }

        let! recorder =
            run (fun b ->
                QuantumMaxCutSolver.solveAsync b maxCut QuantumMaxCutSolver.defaultConfig CancellationToken.None)

        assertSolverCircuit "MaxCut" recorder (QuantumMaxCutSolver.toQubo maxCut |> ok |> dense)

        let knapsack: QuantumKnapsackSolver.KnapsackProblem =
            {
                Items =
                    [
                        { Id = "a"; Weight = 2.0; Value = 3.0 }
                        { Id = "b"; Weight = 3.0; Value = 4.0 }
                        { Id = "c"; Weight = 4.0; Value = 5.0 }
                    ]
                Capacity = 5.0
            }

        let! recorder =
            run (fun b ->
                QuantumKnapsackSolver.solveAsync b knapsack QuantumKnapsackSolver.defaultConfig CancellationToken.None)

        assertSolverCircuit "Knapsack" recorder (QuantumKnapsackSolver.toQubo knapsack |> ok |> dense)

        // The TSP circuit runs whether or not one of its samples is a valid tour
        let recorder = RecordingBackend()

        let! _ =
            QuantumTspSolver.solveAsync
                (recorder :> BackendAbstraction.IQuantumBackend)
                tsp3
                QuantumTspSolver.fastConfig
                CancellationToken.None

        assertSolverCircuit "TSP" recorder (tspQubo tsp3)

        let ue a b : Edge<unit> =
            {
                Source = a
                Target = b
                Weight = 1.0
                Directed = false
                Value = None
                Properties = Map.empty
            }

        let coloring: QuantumGraphColoringSolver.GraphColoringProblem =
            {
                Vertices = [ "a"; "b"; "c" ]
                Edges = [ ue "a" "b"; ue "b" "c" ]
                NumColors = 2
                FixedColors = Map.empty
            }

        let coloringConfig = QuantumGraphColoringSolver.defaultConfig 2

        let! recorder =
            run (fun b -> QuantumGraphColoringSolver.solveAsync b coloring coloringConfig CancellationToken.None)

        let coloringQubo, _ =
            QuantumGraphColoringSolver.toQubo coloring coloringConfig.PenaltyWeight |> ok

        assertSolverCircuit "GraphColoring" recorder (dense coloringQubo)

        let de s t w : Edge<float> = { edge s t w with Directed = true }

        let flow: QuantumNetworkFlowSolver.NetworkFlowProblem =
            {
                Sources = [ "S" ]
                Sinks = [ "T" ]
                IntermediateNodes = [ "A"; "B" ]
                Edges =
                    [
                        de "S" "A" 1.0
                        de "S" "B" 3.0
                        de "A" "T" 1.0
                        de "B" "T" 1.0
                        de "A" "B" 0.5
                    ]
                Capacities = Map [ "A", 1; "B", 1 ]
                Demands = Map [ "T", 1 ]
                Supplies = Map [ "S", 1 ]
            }

        let! recorder =
            run (fun b ->
                QuantumNetworkFlowSolver.solveAsync
                    b
                    flow
                    QuantumNetworkFlowSolver.defaultConfig
                    CancellationToken.None)

        assertSolverCircuitAt
            QuantumNetworkFlowSolver.defaultConfig.InitialParameters
            "NetworkFlow"
            recorder
            (QuantumNetworkFlowSolver.toQubo flow |> ok |> dense)
    }

// ============================================================================
// SOLVER DEFAULTS VS UNIFORM SAMPLING (exact probabilities)
// ============================================================================

let private maxCutQubo () =
    let problem: QuantumMaxCutSolver.MaxCutProblem =
        {
            Vertices = [ for i in 0..5 -> string i ]
            Edges =
                [ for i in 0..5 -> edge (string i) (string ((i + 1) % 6)) 1.0 ]
                @ [ edge "0" "3" 1.0 ]
        }

    QuantumMaxCutSolver.toQubo problem |> ok |> dense

let private weightedMaxCutQubo () =
    let rnd = Random(7)

    let problem: QuantumMaxCutSolver.MaxCutProblem =
        {
            Vertices = [ for i in 0..7 -> string i ]
            Edges =
                [
                    for i in 0..7 do
                        for j in i + 1 .. 7 do
                            if rnd.NextDouble() < 0.5 then
                                yield edge (string i) (string j) (Math.Round(0.5 + rnd.NextDouble() * 2.0, 2))
                ]
        }

    QuantumMaxCutSolver.toQubo problem |> ok |> dense

[<Fact>]
let ``MaxCut default angles beat uniform sampling`` () =
    let (g, b) = QuantumMaxCutSolver.defaultConfig.InitialParameters
    assertBeatsUniform "MaxCut ring-6 + chord" (maxCutQubo ()) [| (g, b) |]
    assertBeatsUniform "MaxCut weighted 8-node" (weightedMaxCutQubo ()) [| (g, b) |]

[<Fact>]
let ``Knapsack default angles beat uniform sampling on a penalty QUBO with coefficients near 3e4`` () =
    let problem: QuantumKnapsackSolver.KnapsackProblem =
        {
            Items =
                [
                    { Id = "a"; Weight = 2.0; Value = 3.0 }
                    { Id = "b"; Weight = 3.0; Value = 4.0 }
                    { Id = "c"; Weight = 4.0; Value = 5.0 }
                    { Id = "d"; Weight = 5.0; Value = 6.0 }
                ]
            Capacity = 5.0
        }

    let qubo = QuantumKnapsackSolver.toQubo problem |> ok |> dense
    Assert.True(qubo |> Seq.cast<float> |> Seq.exists (fun v -> abs v > 1e4))
    let (g, b) = QuantumKnapsackSolver.defaultConfig.InitialParameters
    assertBeatsUniform "Knapsack" qubo [| (g, b) |]

// ============================================================================
// KNAPSACK AND SUBSET-SUM ENCODINGS (brute force over every bitstring)
// ============================================================================

let private knapsackOf (items: (float * float) list) (capacity: float) : QuantumKnapsackSolver.KnapsackProblem =
    {
        Items =
            items
            |> List.mapi (fun i (weight, value) ->
                {
                    Id = $"i{i}"
                    Weight = weight
                    Value = value
                })
        Capacity = capacity
    }

/// The item parts of the minimum-energy bitstrings of the knapsack QUBO are exactly the
/// most valuable selections that fit the capacity. Returns the qubit count and the optima.
let private knapsackGroundStates (items: (float * float) list) (capacity: float) : int * int[][] =
    let problem = knapsackOf items capacity
    let qubo = QuantumKnapsackSolver.toQubo problem |> ok |> dense
    let total = Array2D.length1 qubo
    let n = items.Length
    let e = energies qubo
    let minEnergy = Array.min e
    let tolerance = 1e-9 * max 1.0 (abs minEnergy)

    let weightOf (bits: int[]) =
        items |> List.mapi (fun i (weight, _) -> float bits.[i] * weight) |> List.sum

    let valueOf (bits: int[]) =
        items |> List.mapi (fun i (_, value) -> float bits.[i] * value) |> List.sum

    let fitting =
        Array.init (1 <<< n) (bitsOf n)
        |> Array.filter (fun bits -> weightOf bits <= capacity + 1e-9)

    let best = fitting |> Array.map valueOf |> Array.max

    let optima =
        fitting |> Array.filter (fun bits -> valueOf bits >= best - 1e-9) |> Array.sort

    let ground =
        [|
            for index in 0 .. e.Length - 1 do
                if e.[index] <= minEnergy + tolerance then
                    yield (bitsOf total index).[0 .. n - 1]
        |]
        |> Array.distinct
        |> Array.sort

    Assert.Equal<int[][]>(optima, ground)
    total, optima

[<Fact>]
let ``Knapsack QUBO minimum is the best fitting selection for non-integer weights and capacities`` () =
    // Weights 1.2 / 2.3 / 3.1 in tenths, capacity 50 tenths: 6 slack bits.
    let qubits, optima = knapsackGroundStates [ 1.2, 5.0; 2.3, 6.0; 3.1, 9.0 ] 5.0
    Assert.Equal(9, qubits)
    Assert.Equal<int[][]>([| [| 1; 0; 1 |] |], optima)

    // Capacity 4.5 holds 4 whole units: both items together (weight 5) do not fit.
    let qubits, optima = knapsackGroundStates [ 2.0, 5.0; 3.0, 6.0 ] 4.5
    Assert.Equal(5, qubits)
    Assert.Equal<int[][]>([| [| 0; 1 |] |], optima)

    // Weights 1.5 / 2.0 in halves (3 / 4), capacity 6 halves.
    let qubits, optima = knapsackGroundStates [ 1.5, 10.0; 2.0, 1.0 ] 3.0
    Assert.Equal(5, qubits)
    Assert.Equal<int[][]>([| [| 1; 0 |] |], optima)

    // 0.1 + 0.2 fits a capacity of 0.3 although the floating-point sum exceeds it.
    let _, optima = knapsackGroundStates [ 0.1, 1.0; 0.2, 1.0; 0.3, 1.5 ] 0.3
    Assert.Equal<int[][]>([| [| 1; 1; 0 |] |], optima)

[<Fact>]
let ``Knapsack QUBO minimum is the best fitting selection for integer weights`` () =
    let qubits, _ = knapsackGroundStates [ 2.0, 3.0; 3.0, 4.0; 4.0, 5.0; 5.0, 6.0 ] 5.0
    // Slack weights [1; 2; 2] reach exactly 0..5.
    Assert.Equal(7, qubits)

    knapsackGroundStates [ 3.0, 10.0; 4.0, 12.0; 5.0, 13.0; 2.0, 3.0; 1.0, 1.0 ] 7.0
    |> ignore

    let qubits, optima =
        knapsackGroundStates [ 5.0, 10.0; 4.0, 40.0; 6.0, 30.0; 3.0, 50.0; 2.0, 5.0; 7.0, 35.0 ] 10.0

    Assert.Equal(10, qubits)
    Assert.Equal<int[][]>([| [| 0; 1; 0; 1; 1; 0 |] |], optima)

    // A common factor is divided out: weights 20 / 30 / 40 against 50 are 2 / 3 / 4 against 5.
    let qubits, _ = knapsackGroundStates [ 20.0, 3.0; 30.0, 4.0; 40.0, 5.0 ] 50.0
    Assert.Equal(6, qubits)

    // A weightless item and an item of no value.
    knapsackGroundStates [ 0.0, 2.0; 3.0, 0.0; 2.0, 4.0; 2.0, 1.0 ] 3.0 |> ignore

[<Fact>]
let ``Knapsack QUBO has no slack bits when every item fits or the capacity holds no weight unit`` () =
    // All items fit together: the constraint always holds.
    let qubits, optima = knapsackGroundStates [ 1.0, 2.0; 2.0, 3.0 ] 5.0
    Assert.Equal(2, qubits)
    Assert.Equal<int[][]>([| [| 1; 1 |] |], optima)

    // Capacity 0.5 holds no whole unit: only the empty selection fits.
    let qubits, optima = knapsackGroundStates [ 2.0, 5.0; 3.0, 6.0 ] 0.5
    Assert.Equal(2, qubits)
    Assert.Equal<int[][]>([| [| 0; 0 |] |], optima)

[<Fact>]
let ``Knapsack QUBO rejects weights it cannot encode`` () =
    let fieldOf (items: (float * float) list) =
        match QuantumKnapsackSolver.toQubo (knapsackOf items 5.0) with
        | Error(QuantumError.ValidationError(field, _)) -> field
        | other -> failwith $"expected a validation error, got %A{other}"

    Assert.Equal("weight", fieldOf [ 1.0 / 3.0, 1.0; 1.0, 1.0 ])
    Assert.Equal("weight", fieldOf [ -1.0, 1.0; 1.0, 1.0 ])
    Assert.Equal("weight", fieldOf [ nan, 1.0 ])
    Assert.Equal("value", fieldOf [ 1.0, nan ])

[<Fact>]
let ``Knapsack.randomInstance weights have a common integer scale`` () =
    for maxWeight in [ 100.0; 7.5; 0.5 ] do
        let problem = Knapsack.randomInstance 6 maxWeight 500.0 0.5

        for item in problem.Items do
            // k · maxWeight / 100 with k in 1 .. 100
            let k = item.Weight * 100.0 / maxWeight
            Assert.Equal(Math.Round k, k, 9)
            Assert.InRange(k, 1.0 - 1e-9, 100.0 + 1e-9)

        let encoded =
            QuantumKnapsackSolver.toQubo
                {
                    Items = problem.Items
                    Capacity = problem.Capacity
                }

        Assert.True(Result.isOk encoded, $"%A{encoded}")

let private subsetSumItems: QuantumKnapsackSolver.KnapsackItem list =
    [ 2.0; 5.0; 3.0; 4.0 ]
    |> List.map (fun w -> { Id = string w; Weight = w; Value = w })

[<Fact>]
let ``subset-sum QUBO minima are the exact subsets and the exclusion term raises the known one`` () =
    let states = Array.init 16 (bitsOf 4)

    let sumOf (bits: int[]) =
        subsetSumItems
        |> List.mapi (fun i item -> float bits.[i] * item.Weight)
        |> List.sum

    let exact = states |> Array.map (fun bits -> sumOf bits = 7.0)

    Assert.Equal<int[][]>(
        [| [| 1; 1; 0; 0 |]; [| 0; 0; 1; 1 |] |],
        Array.zip states exact |> Array.filter snd |> Array.map fst
    )

    let energiesWith (extra: Map<int * int, float>) =
        QuantumKnapsackSolver.toSubsetSumQubo subsetSumItems 7.0 extra
        |> ok
        |> dense
        |> energies

    let plain = energiesWith Map.empty
    let lowest = Array.min plain
    Assert.Equal<bool[]>(exact, plain |> Array.map (fun e -> e <= lowest + 1e-9))

    // Known solution {2, 5} = bits 1100 (index 3); the other exact subset {3, 4} is index 12.
    // Strength 10 over 4 bits stays below the base penalty step of 200 between an exact
    // subset and a sum that is off by one.
    let excluded =
        energiesWith (QuantumKnapsackSolver.buildExclusionPenalty [| 1; 1; 0; 0 |] 10.0)

    Assert.True(excluded.[3] > excluded.[12], $"known {excluded.[3]}, other exact subset {excluded.[12]}")
    Assert.Equal(12, excluded |> Array.indexed |> Array.minBy snd |> fst)

    for index in 0..15 do
        if not exact.[index] then
            Assert.True(excluded.[index] > excluded.[3], $"non-solution {index} below the known subset")

[<Fact>]
let ``findAllExactCombinationsAsync returns distinct exact subsets only`` () : Task =
    task {
        let! result =
            QuantumKnapsackSolver.findAllExactCombinationsAsync
                (backend ())
                subsetSumItems
                7.0
                { QuantumKnapsackSolver.defaultSubsetSumConfig with
                    NumShots = 200
                }
                CancellationToken.None

        let found = (ok result).Combinations
        Assert.InRange(found.Length, 0, 2)
        Assert.Equal(found.Length, (found |> List.distinct).Length)

        for combination in found do
            Assert.Equal(7.0, combination |> List.sumBy (fun item -> item.Weight))
    }

[<Fact>]
let ``GraphColoring default angles beat uniform sampling`` () =
    let ue a b : Edge<unit> =
        {
            Source = a
            Target = b
            Weight = 1.0
            Directed = false
            Value = None
            Properties = Map.empty
        }

    let problem: QuantumGraphColoringSolver.GraphColoringProblem =
        {
            Vertices = [ "a"; "b"; "c"; "d" ]
            Edges = [ ue "a" "b"; ue "b" "c"; ue "c" "d"; ue "a" "c" ]
            NumColors = 3
            FixedColors = Map.empty
        }

    let config = QuantumGraphColoringSolver.defaultConfig 3
    let qubo, _ = QuantumGraphColoringSolver.toQubo problem config.PenaltyWeight |> ok
    assertBeatsUniform "GraphColoring" (dense qubo) [| config.InitialParameters |]

[<Fact>]
let ``NetworkFlow default angles beat uniform sampling`` () =
    let de s t w : Edge<float> = { edge s t w with Directed = true }

    let problem: QuantumNetworkFlowSolver.NetworkFlowProblem =
        {
            Sources = [ "S" ]
            Sinks = [ "T" ]
            IntermediateNodes = [ "A"; "B" ]
            Edges =
                [
                    de "S" "A" 1.0
                    de "S" "B" 3.0
                    de "A" "T" 1.0
                    de "B" "T" 1.0
                    de "A" "B" 0.5
                ]
            Capacities = Map [ "A", 1; "B", 1 ]
            Demands = Map [ "T", 1 ]
            Supplies = Map [ "S", 1 ]
        }

    let qubo = QuantumNetworkFlowSolver.toQubo problem |> ok |> dense
    assertBeatsUniform "NetworkFlow" qubo [| QuantumNetworkFlowSolver.defaultConfig.InitialParameters |]

module TaskSchedulingQubo =
    open FSharp.Azure.Quantum.TaskScheduling
    open FSharp.Azure.Quantum.TaskScheduling.Types
    open FSharp.Azure.Quantum.TaskScheduling.Builders

    [<Fact>]
    let ``TaskScheduling runs the configured layers on the normalised Hamiltonian and beats uniform sampling``
        ()
        : Task =
        task {
            let problem =
                scheduling {
                    tasks
                        [
                            scheduledTask {
                                taskId "A"
                                duration (minutes 60.0)
                            }
                            scheduledTask {
                                taskId "B"
                                duration (minutes 60.0)
                                after "A"
                            }
                            scheduledTask {
                                taskId "C"
                                duration (minutes 60.0)
                            }
                        ]

                    objective MinimizeMakespan
                    // 180 minutes of work in a 180-minute window: the solver's grid is
                    // 3 slots of 60 minutes, 3 tasks x 3 slots = 9 qubits
                    timeHorizon (minutes 180.0)
                }

            let qubo =
                QuboEncoding.toQubo problem 3 60.0
                |> ok
                |> fun q -> Qubo.toDenseArray q.NumVariables q.Q

            Assert.Equal(9, Array2D.length1 qubo)

            let recorder = RecordingBackend()

            let! solved =
                QuantumSolver.solveAsync (recorder :> BackendAbstraction.IQuantumBackend) problem CancellationToken.None

            solved |> ok |> ignore

            // The default entry point runs the shared default configuration: the last circuit
            // (the final sampling run) has its layer count, on the normalised Hamiltonian
            // with the standard mixer
            let circuit = Seq.last recorder.Circuits
            Assert.Equal(9, circuit.ProblemHamiltonian.NumQubits)
            let angles = circuit.Layers |> Array.map (fun l -> (l.Gamma, l.Beta))
            Assert.Equal(QaoaExecutionHelpers.defaultConfig.NumLayers, angles.Length)

            Assert.Equal(
                1.0,
                circuit.ProblemHamiltonian.Terms
                |> Array.map (fun t -> abs t.Coefficient)
                |> Array.max,
                12
            )

            Assert.All(circuit.MixerHamiltonian.Terms, (fun t -> Assert.Equal(-1.0, t.Coefficient)))

            // The angles the optimisation settled on beat uniform sampling on the QUBO
            assertBeatsUniform "TaskScheduling" qubo angles

            // Another layer count in the configuration is what runs
            let singleLayer = RecordingBackend()

            let! solvedSingle =
                QuantumSolver.solveWithConfigAsync
                    (singleLayer :> BackendAbstraction.IQuantumBackend)
                    problem
                    { QaoaExecutionHelpers.defaultConfig with
                        NumLayers = 1
                        FinalShots = 200
                    }
                    CancellationToken.None

            let sampling = (solvedSingle |> ok).Sampling |> Option.get
            Assert.Equal(200, sampling.Shots)
            Assert.Equal(1, (Seq.last singleLayer.Circuits).Layers.Length)
        }

/// The circuit must put more probability on valid tours (measurements the solver's decode
/// accepts) and on optimal tours than uniform sampling does: the tours come from the
/// circuit, not from the decode.
let private assertToursBeatUniform (label: string) (distances: float[,]) (parameters: (float * float)[]) =
    let n = distances.GetLength 0
    let p = solverProbabilities (tspQubo distances) parameters

    let lengths =
        Array.init p.Length (fun index ->
            QuantumTspSolver.tryDecodeTour distances (bitsOf (n * n) index)
            |> Option.map (FSharp.Azure.Quantum.Classical.TspSolver.calculateTourLength distances))

    let optimum = lengths |> Array.choose id |> Array.min
    let isValid (length: float option) = length.IsSome

    let isOptimal (length: float option) =
        length |> Option.exists (fun l -> l <= optimum + 1e-9)

    let probabilityOf (accept: float option -> bool) =
        Array.fold2 (fun acc length pi -> if accept length then acc + pi else acc) 0.0 lengths p

    let uniformOf (accept: float option -> bool) =
        float (lengths |> Array.filter accept |> Array.length) / float lengths.Length

    Assert.True(
        probabilityOf isValid > uniformOf isValid,
        $"{label}: P(valid tour) = {probabilityOf isValid} at {parameters}, uniform = {uniformOf isValid}"
    )

    Assert.True(
        probabilityOf isOptimal > uniformOf isOptimal,
        $"{label}: P(optimal tour) = {probabilityOf isOptimal} at {parameters}, uniform = {uniformOf isOptimal}"
    )

[<Fact>]
let ``TSP fixed angles beat uniform sampling for 3 and 4 cities`` () =
    let fast = QuantumTspSolver.fastConfig
    let angles = Array.create fast.NumLayers fast.InitialParameters
    assertBeatsUniform "TSP 3 cities" (tspQubo tsp3) angles
    assertToursBeatUniform "TSP 3 cities" tsp3 angles

    // 16 qubits. The cheapest cycle 0-2-1-3 is not the index order.
    let tsp4 =
        array2D
            [
                [ 0.0; 5.0; 1.0; 1.5 ]
                [ 5.0; 0.0; 2.0; 1.0 ]
                [ 1.0; 2.0; 0.0; 4.0 ]
                [ 1.5; 1.0; 4.0; 0.0 ]
            ]

    assertBeatsUniform "TSP 4 cities (16 qubits)" (tspQubo tsp4) angles
    assertToursBeatUniform "TSP 4 cities (16 qubits)" tsp4 angles

[<Fact>]
let ``TSP default optimisation runs angles that beat uniform sampling`` () : Task =
    task {
        // The final circuit is the last one executed, whether or not a sample is a valid tour
        let recorder = RecordingBackend()

        let! _ =
            QuantumTspSolver.solveAsync
                (recorder :> BackendAbstraction.IQuantumBackend)
                tsp3
                QuantumTspSolver.defaultConfig
                CancellationToken.None

        let final = Seq.last recorder.Circuits
        let angles = final.Layers |> Array.map (fun l -> (l.Gamma, l.Beta))

        Assert.Equal(QuantumTspSolver.defaultConfig.NumLayers, angles.Length)
        assertBeatsUniform "TSP 3 cities, optimised" (tspQubo tsp3) angles
        assertToursBeatUniform "TSP 3 cities, optimised" tsp3 angles
    }

let private lit v neg : QuantumSatSolver.Literal = { Variable = v; IsNegated = neg }

/// QUBOs of the solvers that run through QaoaExecutionHelpers' grid search / Nelder-Mead.
let private helperQubos () : (string * float[,]) list =
    [
        "SAT",
        QuantumSatSolver.toQubo
            {
                NumVariables = 5
                Clauses =
                    [
                        [ lit 0 false; lit 1 false ]
                        [ lit 0 true; lit 2 false ]
                        [ lit 1 true; lit 3 false ]
                        [ lit 2 true; lit 4 true ]
                        [ lit 3 true; lit 4 false ]
                        [ lit 0 true; lit 1 true ]
                        [ lit 2 false; lit 3 false ]
                    ]
                    |> List.map QuantumSatSolver.clause
            }
        |> ok

        "VertexCover",
        QuantumVertexCoverSolver.toQubo
            {
                Vertices = [ for i in 0..6 -> { Id = string i; Weight = 1.0 } ]
                Edges = [ (0, 1); (1, 2); (2, 3); (3, 4); (4, 5); (5, 6); (6, 0); (0, 3); (2, 5) ]
            }
        |> ok

        "Clique",
        QuantumCliqueSolver.toQubo
            {
                Vertices = [ for i in 0..6 -> { Id = string i; Weight = 1.0 } ]
                Edges =
                    [
                        (0, 1)
                        (0, 2)
                        (1, 2)
                        (2, 3)
                        (3, 4)
                        (4, 5)
                        (3, 5)
                        (2, 4)
                        (5, 6)
                        (1, 6)
                    ]
            }
        |> ok

        "Matching",
        QuantumMatchingSolver.toQubo
            {
                NumVertices = 6
                Edges =
                    [
                        (0, 1, 1.0)
                        (1, 2, 2.0)
                        (2, 3, 1.0)
                        (3, 4, 2.0)
                        (4, 5, 1.0)
                        (5, 0, 1.5)
                        (1, 4, 1.0)
                    ]
                    |> List.map (fun (s, t, w) -> { Source = s; Target = t; Weight = w })
            }
        |> ok

        "SetCover",
        QuantumSetCoverSolver.toQubo
            {
                UniverseSize = 4
                Subsets =
                    [
                        {
                            Id = "A"
                            Elements = [ 0; 1 ]
                            Cost = 1.0
                        }
                        {
                            Id = "B"
                            Elements = [ 1; 2 ]
                            Cost = 1.0
                        }
                        {
                            Id = "C"
                            Elements = [ 2; 3 ]
                            Cost = 1.0
                        }
                        {
                            Id = "D"
                            Elements = [ 0; 3 ]
                            Cost = 1.5
                        }
                    ]
            }
        |> ok

        "BinPacking",
        QuantumBinPackingSolver.toQubo
            {
                Items = [ { Id = "a"; Size = 3.0 }; { Id = "b"; Size = 2.0 }; { Id = "c"; Size = 2.0 } ]
                BinCapacity = 4.0
            }
        |> ok

        "BinaryILP",
        QuantumBinaryILPSolver.toQubo
            {
                ObjectiveCoeffs = [ -3.0; -2.0; -4.0; -1.0; -2.5 ]
                Constraints =
                    [
                        {
                            Coefficients = [ 2.0; 1.0; 3.0; 1.0; 2.0 ]
                            Bound = 5.0
                        }
                    ]
            }
        |> ok

        "IndependentSet",
        DrugDiscoverySolvers.IndependentSet.toQubo
            {
                Nodes =
                    [
                        for i in 0..6 ->
                            {
                                Id = string i
                                Weight = 1.0 + 0.2 * float i
                            }
                    ]
                Edges = [ (0, 1); (1, 2); (2, 3); (3, 4); (4, 5); (5, 6); (6, 0); (1, 4) ]
            }
    ]

[<Fact>]
let ``QaoaExecutionHelpers grid search (fastConfig, p = 1) picks angles that beat uniform sampling`` () : Task =
    task {
        for (name, qubo) in helperQubos () do
            Assert.True(Array2D.length1 qubo <= 12, $"{name} has {Array2D.length1 qubo} qubits")

            let! result =
                QaoaExecutionHelpers.executeQaoaWithGridSearchAsync
                    (backend ())
                    qubo
                    QaoaExecutionHelpers.fastConfig
                    1
                    CancellationToken.None

            match result with
            | Ok(_, angles) -> assertBeatsUniform $"{name} grid" qubo angles
            | Error err -> Assert.Fail($"{name}: {err}")
    }

[<Fact>]
let ``QaoaExecutionHelpers Nelder-Mead (defaultConfig, p = 2) returns angles that beat uniform sampling`` () : Task =
    task {
        for (name, qubo) in helperQubos () do
            let! result =
                QaoaExecutionHelpers.executeQaoaWithOptimizationAsync
                    (backend ())
                    qubo
                    QaoaExecutionHelpers.defaultConfig
                    CancellationToken.None

            match result with
            | Ok(_, angles, _) -> assertBeatsUniform $"{name} Nelder-Mead" qubo angles
            | Error err -> Assert.Fail($"{name}: {err}")
    }

[<Fact>]
let ``QaoaExecutionHelpers Nelder-Mead improves on its starting point`` () : Task =
    task {
        // The objective is the exact expectation on a state-vector backend, so the optimum is
        // reproducible and no worse than the p = 2 ramp start (γ 0.1875 → 0.5625, β 0.5625 → 0.1875).
        let _, qubo = helperQubos () |> List.find (fun (name, _) -> name = "SetCover")
        let e = energies qubo

        let start =
            quality e (solverProbabilities qubo [| (0.1875, 0.5625); (0.5625, 0.1875) |])

        let! result =
            QaoaExecutionHelpers.executeQaoaWithOptimizationAsync
                (backend ())
                qubo
                QaoaExecutionHelpers.defaultConfig
                CancellationToken.None

        match result with
        | Ok(_, angles, converged) ->
            Assert.True(converged)
            let optimised = quality e (solverProbabilities qubo angles)
            Assert.True(optimised.Expected < start.Expected - 1e-6, $"{optimised.Expected} vs start {start.Expected}")
        | Error err -> Assert.Fail($"{err}")
    }

[<Fact>]
let ``Portfolio default and grid-chosen angles beat uniform sampling`` () =
    let assets: PortfolioTypes.Asset list =
        [
            ("A", 0.12, 0.20, 100.0)
            ("B", 0.10, 0.15, 80.0)
            ("C", 0.07, 0.08, 60.0)
            ("D", 0.15, 0.30, 120.0)
            ("E", 0.09, 0.12, 90.0)
            ("F", 0.05, 0.05, 50.0)
            ("G", 0.11, 0.18, 70.0)
            ("H", 0.08, 0.10, 65.0)
        ]
        |> List.map (fun (s, r, k, p) ->
            {
                Symbol = s
                ExpectedReturn = r
                Risk = k
                Price = p
            })

    let problem: QuantumPortfolioSolver.PortfolioProblem =
        {
            Assets = assets
            Constraints =
                {
                    Budget = 1000.0
                    MinHolding = 0.0
                    MaxHolding = 400.0
                }
            RiskAversion = 0.5
            Covariance = None
        }

    let qubo, kept =
        QuantumPortfolioSolver.circuitQubo problem (QuantumPortfolioSolver.toQubo problem |> ok)

    Assert.Equal(8, kept.Length)
    assertBeatsUniform "Portfolio default" qubo [| QuantumPortfolioSolver.defaultConfig.InitialParameters |]

    // Every candidate the solver can pick has γ > 0 (the minimising sign); the best of them
    // by exact energy beats uniform sampling.
    Assert.True(QuantumPortfolioSolver.angleGridGammas |> Array.forall (fun g -> g > 0.0))
    let e = energies qubo

    let best =
        [|
            for g in QuantumPortfolioSolver.angleGridGammas do
                for b in QuantumPortfolioSolver.angleGridBetas -> [| (g, b) |]
        |]
        |> Array.minBy (fun angles -> (quality e (solverProbabilities qubo angles)).Expected)

    assertBeatsUniform "Portfolio best grid point" qubo best
