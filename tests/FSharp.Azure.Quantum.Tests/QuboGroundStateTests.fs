module FSharp.Azure.Quantum.Tests.QuboGroundStateTests

// The rule every QUBO encoding must meet: each minimum-energy bitstring is a feasible
// solution of the original problem with the optimal objective value. Checked by
// enumerating every bitstring (energy only, no circuit), so the tests are exact.

open System
open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GraphOptimization
open FSharp.Azure.Quantum.Quantum

let private ok (label: string) (result: Result<'T, QuantumError>) : 'T =
    result |> Result.defaultWith (fun err -> failwith $"{label}: {err}")

let private bitsOf (n: int) (index: int) =
    Array.init n (fun q -> (index >>> q) &&& 1)

/// Every minimum-energy bitstring of `qubo`, cut to its first `decisionBits` bits, is feasible
/// and has the best objective any feasible assignment of those bits reaches.
let private assertGroundStatesOptimal
    (label: string)
    (qubo: float[,])
    (decisionBits: int)
    (feasible: int[] -> bool)
    (objective: int[] -> float)
    (minimise: bool)
    =
    let n = Array2D.length1 qubo
    Assert.True(n <= 20, $"{label}: {n} qubits is too wide to enumerate")

    let states = Array.init (1 <<< n) (bitsOf n)
    let energies = states |> Array.map (QaoaExecutionHelpers.evaluateQubo qubo)
    let lowest = Array.min energies
    let tolerance = 1e-9 * max 1.0 (abs lowest)

    let ground =
        Array.zip states energies
        |> Array.filter (fun (_, e) -> e - lowest <= tolerance)
        |> Array.map (fun (bits, _) -> Array.truncate decisionBits bits)
        |> Array.distinct

    let feasibleValues =
        Array.init (1 <<< decisionBits) (bitsOf decisionBits)
        |> Array.filter feasible
        |> Array.map objective

    Assert.True(feasibleValues.Length > 0, $"{label}: the instance has no feasible assignment")

    let best =
        if minimise then
            Array.min feasibleValues
        else
            Array.max feasibleValues

    for bits in ground do
        Assert.True(feasible bits, $"%s{label}: minimum-energy state %A{bits} is infeasible")

        Assert.True(
            abs (objective bits - best) <= 1e-9 * max 1.0 (abs best),
            $"%s{label}: minimum-energy state %A{bits} has objective {objective bits}, the optimum is {best}"
        )

/// Seeded edge list over n vertices: every pair with the given probability.
let private randomEdges (seed: int) (n: int) (probability: float) : (int * int) list =
    let rng = Random(seed)

    [
        for i in 0 .. n - 1 do
            for j in i + 1 .. n - 1 do
                if rng.NextDouble() < probability then
                    yield (i, j)
    ]

let private selectedWeight (weights: float list) (bits: int[]) =
    weights |> List.mapi (fun i w -> float bits.[i] * w) |> List.sum

[<Fact>]
let ``vertex cover minimum is a minimum-weight cover`` () =
    let instances: (string * float list * (int * int) list) list =
        [
            "path of 5", List.replicate 5 1.0, [ (0, 1); (1, 2); (2, 3); (3, 4) ]
            "star with a heavy hub", 10.0 :: List.replicate 5 1.0, [ for leaf in 1..5 -> (0, leaf) ]
            "random 10 vertices", [ for i in 0..9 -> 1.0 + float (i % 3) ], randomEdges 11 10 0.35
        ]

    for label, weights, edges in instances do
        let problem: QuantumVertexCoverSolver.Problem =
            {
                Vertices = weights |> List.mapi (fun i w -> { Id = string i; Weight = w })
                Edges = edges
            }

        assertGroundStatesOptimal
            $"vertex cover, {label}"
            (QuantumVertexCoverSolver.toQubo problem |> ok label)
            weights.Length
            (QuantumVertexCoverSolver.isValid problem)
            (selectedWeight weights)
            true

[<Fact>]
let ``clique minimum is a maximum-weight clique`` () =
    let instances: (string * float list * (int * int) list) list =
        [
            "triangle with a tail", List.replicate 5 1.0, [ (0, 1); (1, 2); (0, 2); (2, 3); (3, 4) ]
            "random 10 vertices", List.replicate 10 1.0, randomEdges 5 10 0.5
            "weighted, 8 vertices", [ for i in 0..7 -> 1.0 + float (i % 4) ], randomEdges 23 8 0.5
        ]

    for label, weights, edges in instances do
        let problem: QuantumCliqueSolver.Problem =
            {
                Vertices = weights |> List.mapi (fun i w -> { Id = string i; Weight = w })
                Edges = edges
            }

        assertGroundStatesOptimal
            $"clique, {label}"
            (QuantumCliqueSolver.toQubo problem |> ok label)
            weights.Length
            (QuantumCliqueSolver.isValid problem)
            (selectedWeight weights)
            false

[<Fact>]
let ``matching minimum is a maximum-weight matching`` () =
    let instances: (string * int * (int * int * float) list) list =
        [
            "path, weights 3 4 3 4", 5, [ (0, 1, 3.0); (1, 2, 4.0); (2, 3, 3.0); (3, 4, 4.0) ]
            "triangle with a zero and a negative edge", 4, [ (0, 1, 2.0); (1, 2, 0.0); (0, 2, -1.0); (2, 3, 1.5) ]
            "random 6 vertices", 6, randomEdges 3 6 0.6 |> List.mapi (fun k (i, j) -> (i, j, 1.0 + float (k % 4)))
        ]

    for label, numVertices, edges in instances do
        let problem: QuantumMatchingSolver.Problem =
            {
                NumVertices = numVertices
                Edges =
                    edges
                    |> List.map (fun (i, j, w) ->
                        {
                            QuantumMatchingSolver.Edge.Source = i
                            Target = j
                            Weight = w
                        })
            }

        assertGroundStatesOptimal
            $"matching, {label}"
            (QuantumMatchingSolver.toQubo problem |> ok label)
            edges.Length
            (QuantumMatchingSolver.isValid problem)
            (selectedWeight (edges |> List.map (fun (_, _, w) -> w)))
            false

[<Fact>]
let ``set cover minimum is a minimum-cost cover`` () =
    let instances: (string * int * (int list * float) list) list =
        [
            "one big set against three small ones",
            5,
            [ [ 0; 1; 2; 3; 4 ], 2.5; [ 0; 1 ], 1.0; [ 2; 3 ], 1.0; [ 4 ], 1.0 ]
            "two halves beat the whole", 4, [ [ 0; 1; 2; 3 ], 2.9; [ 0; 1 ], 1.0; [ 2; 3 ], 1.0; [ 1; 2 ], 0.5 ]
            "elements in 3, 2 and 2 sets", 3, [ [ 0; 1 ], 1.0; [ 0; 2 ], 1.0; [ 0; 1; 2 ], 1.5; [ 2 ], 0.4 ]
        ]

    for label, universe, subsets in instances do
        let problem: QuantumSetCoverSolver.Problem =
            {
                UniverseSize = universe
                Subsets =
                    subsets
                    |> List.mapi (fun i (elements, cost) ->
                        {
                            Id = $"S{i}"
                            Elements = elements
                            Cost = cost
                        })
            }

        assertGroundStatesOptimal
            $"set cover, {label}"
            (QuantumSetCoverSolver.toQubo problem |> ok label)
            subsets.Length
            (QuantumSetCoverSolver.isValid problem)
            (selectedWeight (subsets |> List.map snd))
            true

[<Fact>]
let ``MAX-SAT minimum satisfies the largest clause weight`` () =
    let literal (variable: int) (negated: bool) : QuantumSatSolver.Literal =
        {
            Variable = variable
            IsNegated = negated
        }

    let instances: (string * int * (float * (int * bool) list) list) list =
        [
            "satisfiable 3-SAT",
            4,
            [
                1.0, [ (0, false); (1, false); (2, true) ]
                1.0, [ (0, true); (2, false); (3, false) ]
                1.0, [ (1, true); (2, true); (3, true) ]
            ]
            "unsatisfiable, weighted, clause sizes 1 to 4",
            4,
            [
                2.0, [ (0, false) ]
                3.0, [ (0, true) ]
                1.0, [ (1, false); (2, false) ]
                1.5, [ (1, true); (2, true); (3, false) ]
                1.0, [ (0, false); (1, false); (2, false); (3, true) ]
            ]
            "a variable repeated inside a clause",
            3,
            [
                1.0, [ (0, false); (0, false); (1, true) ]
                1.0, [ (1, false); (2, true) ]
                1.0, [ (2, false) ]
            ]
        ]

    for label, numVariables, clauses in instances do
        let problem: QuantumSatSolver.Problem =
            {
                NumVariables = numVariables
                Clauses =
                    clauses
                    |> List.map (fun (weight, literals) ->
                        QuantumSatSolver.weightedClause weight (literals |> List.map (fun (v, neg) -> literal v neg)))
            }

        let satisfiedWeight (bits: int[]) =
            clauses
            |> List.sumBy (fun (weight, literals) ->
                if literals |> List.exists (fun (v, negated) -> (bits.[v] = 1) <> negated) then
                    weight
                else
                    0.0)

        assertGroundStatesOptimal
            $"MAX-SAT, {label}"
            (QuantumSatSolver.toQubo problem |> ok label)
            numVariables
            (fun _ -> true)
            satisfiedWeight
            false

[<Fact>]
let ``independent set minimum is a maximum-weight independent set`` () =
    let instances: (string * float list * (int * int) list) list =
        [
            "path, weights 1 3 1 3 1", [ 1.0; 3.0; 1.0; 3.0; 1.0 ], [ (0, 1); (1, 2); (2, 3); (3, 4) ]
            "random 10 nodes", [ for i in 0..9 -> 1.0 + float (i % 3) ], randomEdges 17 10 0.3
        ]

    for label, weights, edges in instances do
        let problem: DrugDiscoverySolvers.IndependentSet.Problem =
            {
                Nodes = weights |> List.mapi (fun i w -> { Id = string i; Weight = w })
                Edges = edges
            }

        assertGroundStatesOptimal
            $"independent set, {label}"
            (DrugDiscoverySolvers.IndependentSet.toQubo problem)
            weights.Length
            (DrugDiscoverySolvers.IndependentSet.isValid problem)
            (selectedWeight weights)
            false

[<Fact>]
let ``MaxCut energy is minus the cut value on every bitstring`` () =
    let instances: (string * int * (int * int * float) list) list =
        [
            "ring of 5", 5, [ for i in 0..4 -> (i, (i + 1) % 5, 1.0) ]
            "mixed-sign weights", 6, randomEdges 9 6 0.7 |> List.mapi (fun k (i, j) -> (i, j, float (k % 5) - 1.5))
        ]

    for label, n, edges in instances do
        let problem: QuantumMaxCutSolver.MaxCutProblem =
            {
                Vertices = [ for i in 0 .. n - 1 -> string i ]
                Edges = edges |> List.map (fun (i, j, w) -> edge (string i) (string j) w)
            }

        let matrix = QuantumMaxCutSolver.toQubo problem |> ok label
        let qubo = Qubo.toDenseArray matrix.NumVariables matrix.Q

        for index in 0 .. (1 <<< n) - 1 do
            let bits = bitsOf n index

            let partition =
                [
                    for i in 0 .. n - 1 do
                        if bits.[i] = 1 then
                            string i
                ]

            let cut = QuantumMaxCutSolver.calculateCutValue problem partition
            Assert.Equal(-cut, QaoaExecutionHelpers.evaluateQubo qubo bits, 9)
