module FSharp.Azure.Quantum.Tests.GraphColoringTests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GraphColoring
open System.Threading
open System.Threading.Tasks

module QSolver = FSharp.Azure.Quantum.Quantum.QuantumGraphColoringSolver

// ============================================================================
// HELPERS
// ============================================================================

[<Literal>]
let private penalty = 10.0

let private quantumProblem
    (vertices: string list)
    (edges: (string * string) list)
    (numColors: int)
    : QSolver.GraphColoringProblem =
    {
        Vertices = vertices
        Edges = edges |> List.map (fun (a, b) -> GraphOptimization.edge a b 1.0)
        NumColors = numColors
        FixedColors = Map.empty
    }

let private qubo (problem: QSolver.GraphColoringProblem) (preferences: QSolver.ColoringPreferences) =
    match QSolver.toQuboWithPreferences problem penalty preferences with
    | Ok(matrix, _) -> matrix
    | Error err -> failwith $"toQuboWithPreferences failed: {err}"

let private coefficient (matrix: GraphOptimization.QuboMatrix) (i: int) (j: int) =
    matrix.Q |> Map.tryFind (min i j, max i j) |> Option.defaultValue 0.0

/// QUBO variable of (vertex index, color) in the one-hot layout
let private var numColors vertexIdx color = vertexIdx * numColors + color

let private energy problem matrix (assignments: (string * int) list) =
    QSolver.assignmentEnergy problem matrix (Map.ofList assignments)

let private solution
    (problem: QSolver.GraphColoringProblem)
    (assignments: (string * int) list)
    energy
    : QSolver.GraphColoringSolution =
    let colors = Map.ofList assignments

    let conflicts =
        problem.Edges
        |> List.filter (fun e -> colors.[e.Source] = colors.[e.Target])
        |> List.length

    {
        ColorAssignments = colors
        ColorsUsed = assignments |> List.map snd |> List.distinct |> List.length
        ConflictCount = conflicts
        IsValid = conflicts = 0
        BackendName = ""
        NumShots = 0
        ElapsedMs = 0.0
        BestEnergy = energy
    }

let private solveOk problem numColors =
    task {
        match! GraphColoring.solveAsync problem numColors None CancellationToken.None with
        | Ok sol -> return sol
        | Error err -> return failwith $"solve failed: {err.Message}"
    }

// ============================================================================
// QUBO: EACH OPTION CHANGES THE ENCODING
// ============================================================================

[<Fact>]
let ``ConflictWeight scales the same-color edge penalty`` () =
    let problem = quantumProblem [ "A"; "B" ] [ "A", "B" ] 2

    let plain = qubo problem QSolver.defaultPreferences

    let heavy =
        qubo
            problem
            { QSolver.defaultPreferences with
                ConflictWeight = 3.0
            }

    Assert.Equal(penalty, coefficient plain (var 2 0 0) (var 2 1 0), 9)
    Assert.Equal(3.0 * penalty, coefficient heavy (var 2 0 0) (var 2 1 0), 9)
    Assert.Equal(3.0 * penalty, coefficient heavy (var 2 0 1) (var 2 1 1), 9)

[<Fact>]
let ``Non-positive ConflictWeight is rejected`` () =
    let problem = quantumProblem [ "A"; "B" ] [ "A", "B" ] 2

    for weight in [ 0.0; -1.0; nan ] do
        match
            QSolver.toQuboWithPreferences
                problem
                penalty
                { QSolver.defaultPreferences with
                    ConflictWeight = weight
                }
        with
        | Error(QuantumError.ValidationError("ConflictWeight", _)) -> ()
        | other -> Assert.Fail($"Expected ConflictWeight validation error for {weight}, got {other}")

[<Fact>]
let ``AvoidColors adds a soft penalty to the avoided color only`` () =
    let problem = quantumProblem [ "A"; "B" ] [ "A", "B" ] 3

    let plain = qubo problem QSolver.defaultPreferences

    let avoiding =
        qubo
            problem
            { QSolver.defaultPreferences with
                AvoidColors = Map.ofList [ "A", [ 1 ] ]
            }

    let diagonalDelta v c =
        coefficient avoiding (var 3 v c) (var 3 v c)
        - coefficient plain (var 3 v c) (var 3 v c)

    // 0.3 × P / n with n = 2 vertices
    Assert.Equal(0.3 * penalty / 2.0, diagonalDelta 0 1, 9)
    Assert.Equal(0.0, diagonalDelta 0 0, 9)
    Assert.Equal(0.0, diagonalDelta 0 2, 9)
    Assert.Equal(0.0, diagonalDelta 1 1, 9)

    // The avoided color now costs more than a non-avoided higher color index
    Assert.True(energy problem avoiding [ "A", 1; "B", 0 ] > energy problem avoiding [ "A", 2; "B", 0 ])

[<Fact>]
let ``MinimizeColors charges higher color indices and MinimizeConflicts does not`` () =
    let problem = quantumProblem [ "A"; "B" ] [ "A", "B" ] 3

    let fewerColors = qubo problem QSolver.defaultPreferences

    let fewestConflicts =
        qubo
            problem
            { QSolver.defaultPreferences with
                Goal = QSolver.ColoringGoal.MinimizeConflicts
            }

    let indexCost matrix c =
        coefficient matrix (var 3 0 c) (var 3 0 c)
        - coefficient matrix (var 3 0 0) (var 3 0 0)

    // 0.2 × P × c / (n × (K - 1)) with n = 2, K = 3
    Assert.Equal(0.05 * penalty, indexCost fewerColors 1, 9)
    Assert.Equal(0.1 * penalty, indexCost fewerColors 2, 9)
    Assert.Equal(0.0, indexCost fewestConflicts 1, 9)
    Assert.Equal(0.0, indexCost fewestConflicts 2, 9)

[<Fact>]
let ``BalanceColors makes even color classes cheaper than uneven ones`` () =
    let problem = quantumProblem [ "A"; "B"; "C"; "D" ] [] 2

    let balance =
        qubo
            problem
            { QSolver.defaultPreferences with
                Goal = QSolver.ColoringGoal.BalanceColors
            }

    // Same-color pairs of non-adjacent vertices are penalised
    Assert.True(coefficient balance (var 2 0 0) (var 2 1 0) > 0.0)
    Assert.Equal(0.0, coefficient balance (var 2 0 0) (var 2 1 1), 9)

    let even = energy problem balance [ "A", 0; "B", 1; "C", 0; "D", 1 ]
    let uneven = energy problem balance [ "A", 0; "B", 0; "C", 0; "D", 1 ]
    let single = energy problem balance [ "A", 0; "B", 0; "C", 0; "D", 0 ]
    Assert.True(even < uneven)
    Assert.True(uneven < single)

    // Without the balance goal, all-one-color is the cheapest (fewest colors)
    let plain = qubo problem QSolver.defaultPreferences

    Assert.True(
        energy problem plain [ "A", 0; "B", 0; "C", 0; "D", 0 ] <
            energy problem plain [ "A", 0; "B", 1; "C", 0; "D", 1 ]
    )

[<Fact>]
let ``Hard constraints dominate all soft terms at the QUBO minimum`` () =
    // Path A-B-C with 3 colors, every vertex avoiding colors 0 and 1, balance goal:
    // the minimum over all 2^9 bitstrings must still be a one-hot proper coloring.
    let problem = quantumProblem [ "A"; "B"; "C" ] [ "A", "B"; "B", "C" ] 3

    for goal in [ QSolver.ColoringGoal.MinimizeColors; QSolver.ColoringGoal.BalanceColors ] do
        let matrix =
            qubo
                problem
                { QSolver.defaultPreferences with
                    Goal = goal
                    AvoidColors = Map.ofList [ "A", [ 0; 1 ]; "B", [ 0; 1 ]; "C", [ 0; 1 ] ]
                }

        let bits (mask: int) =
            Array.init matrix.NumVariables (fun i -> (mask >>> i) &&& 1)

        let best =
            [ 0 .. (1 <<< matrix.NumVariables) - 1 ]
            |> List.minBy (fun mask -> QaoaExecutionHelpers.evaluateQuboSparse matrix.Q (bits mask))
            |> bits

        let colorOf v =
            [ 0..2 ] |> List.filter (fun c -> best.[var 3 v c] = 1)

        for v in 0..2 do
            Assert.Equal(1, (colorOf v).Length)

        Assert.NotEqual<int list>(colorOf 0, colorOf 1)
        Assert.NotEqual<int list>(colorOf 1, colorOf 2)

[<Fact>]
let ``Edgeless graph encodes to a QUBO`` () =
    let problem = quantumProblem [ "A"; "B" ] [] 2

    match QSolver.toQubo problem penalty with
    | Ok(matrix, _) -> Assert.Equal(4, matrix.NumVariables)
    | Error err -> Assert.Fail($"Edgeless graph should encode, got {err}")

// ============================================================================
// SAMPLE SELECTION: OBJECTIVE AND PRIORITY
// ============================================================================

[<Fact>]
let ``Sample selection follows the objective`` () =
    let problem = quantumProblem [ "A"; "B"; "C"; "D" ] [] 3
    let twoColors = solution problem [ "A", 0; "B", 0; "C", 0; "D", 1 ] 0.0
    let threeColors = solution problem [ "A", 0; "B", 1; "C", 2; "D", 0 ] 0.0
    let candidates = [| threeColors; twoColors |]

    let pick goal =
        QSolver.selectBest
            problem
            { QSolver.defaultPreferences with
                Goal = goal
            }
            candidates

    Assert.Same(twoColors, pick QSolver.ColoringGoal.MinimizeColors)
    Assert.Same(threeColors, pick QSolver.ColoringGoal.BalanceColors)

[<Fact>]
let ``Priority breaks ties in favour of earlier colors for higher-priority vertices`` () =
    let problem = quantumProblem [ "A"; "B" ] [ "A", "B" ] 2
    let aFirst = solution problem [ "A", 0; "B", 1 ] 1.0
    let bFirst = solution problem [ "A", 1; "B", 0 ] 1.0
    let candidates = [| aFirst; bFirst |]

    Assert.Same(aFirst, QSolver.selectBest problem QSolver.defaultPreferences candidates)

    let bPriority =
        { QSolver.defaultPreferences with
            Priorities = Map.ofList [ "B", 1.0 ]
        }

    Assert.Same(bFirst, QSolver.selectBest problem bPriority candidates)

    // Priority never overrides a lower energy
    let bFirstCostly = { bFirst with BestEnergy = 2.0 }
    Assert.Same(aFirst, QSolver.selectBest problem bPriority [| bFirstCostly; aFirst |])

// ============================================================================
// BUILDER: OPTIONS THROUGH GraphColoring.solve
// ============================================================================

[<Fact>]
let ``Graph without conflicts is colored without running a circuit`` () : Task =
    task {
        let problem =
            graphColoring {
                nodes
                    [
                        coloredNode {
                            nodeId "A"
                            fixedColor "Blue"
                        }
                        GraphColoring.node "B" []
                        GraphColoring.node "C" []
                    ]

                colors [ "Red"; "Green"; "Blue" ]
            }

        let! sol = solveOk problem 3

        Assert.True(sol.IsValid)
        Assert.Equal(0, sol.ConflictCount)
        Assert.False(sol.IsQuantum)
        Assert.Equal(QSolver.NoCircuitBackendName, sol.BackendName)
        Assert.Equal("Blue", sol.Assignments.["A"])
        // MinimizeColors reuses the color already in use
        Assert.Equal("Blue", sol.Assignments.["B"])
        Assert.Equal("Blue", sol.Assignments.["C"])
        Assert.Equal(1, sol.ColorsUsed)
    }
    :> Task

[<Fact>]
let ``AvoidColors and MaxColors decide the color of an unconstrained node`` () : Task =
    task {
        let build maxColorsOpt =
            let problem =
                graphColoring {
                    nodes
                        [
                            coloredNode {
                                nodeId "A"
                                avoidColors [ "Red"; "Green" ]
                            }
                        ]

                    colors [ "Red"; "Green"; "Blue" ]
                }

            { problem with
                MaxColors = maxColorsOpt
            }

        // Avoided colors are skipped when another color is allowed
        let! unrestricted = solveOk (build None) 3
        Assert.Equal("Blue", unrestricted.Assignments.["A"])
        // MaxColors = 2 leaves only avoided colors: the first allowed one is used
        let! twoColors = solveOk (build (Some 2)) 3
        Assert.Equal("Red", twoColors.Assignments.["A"])
    }
    :> Task

[<Fact>]
let ``BalanceColors spreads nodes and Priority orders them`` () : Task =
    task {
        let problem objectiveValue =
            graphColoring {
                nodes
                    [
                        GraphColoring.node "A" []
                        GraphColoring.node "B" []
                        GraphColoring.node "C" []
                        GraphColoring.node "D" []
                    ]

                colors [ "Red"; "Green" ]
                objective objectiveValue
            }

        let! minimize = solveOk (problem MinimizeColors) 2
        Assert.Equal(1, minimize.ColorsUsed)

        let! balanced = solveOk (problem BalanceColors) 2
        Assert.Equal(2, balanced.ColorDistribution.["Red"])
        Assert.Equal(2, balanced.ColorDistribution.["Green"])
        Assert.Equal("Red", balanced.Assignments.["A"])
        Assert.Equal("Green", balanced.Assignments.["B"])

        // Descending priority D, C, B, A: D takes the first color, C the second, ...
        let prioritized =
            { problem BalanceColors with
                Nodes =
                    (problem BalanceColors).Nodes
                    |> List.map (fun n ->
                        { n with
                            Priority = float (int n.Id.[0] - int 'A')
                        })
            }

        let! byPriority = solveOk prioritized 2
        Assert.Equal("Red", byPriority.Assignments.["D"])
        Assert.Equal("Green", byPriority.Assignments.["C"])
        Assert.Equal("Red", byPriority.Assignments.["B"])
        Assert.Equal("Green", byPriority.Assignments.["A"])
    }
    :> Task

[<Fact>]
let ``MaxColors restricts the quantum solver to the first colors`` () : Task =
    task {
        // Triangle needs 3 colors; with MaxColors = 2 only Red and Green are encoded
        let problem =
            graphColoring {
                node "A" [ "B"; "C" ]
                node "B" [ "C" ]
                node "C" []
                colors [ "Red"; "Green"; "Blue" ]
                maxColors 2
            }

        let! sol = solveOk problem 3

        Assert.True(sol.IsQuantum)

        for KeyValue(_, color) in sol.Assignments do
            Assert.Contains(color, [ "Red"; "Green" ])
    }
    :> Task

[<Fact>]
let ``Validation rejects bad ConflictPenalty, unknown avoid colors and fixed colors beyond MaxColors`` () =
    let baseProblem =
        {
            Nodes = [ node "A" [ "B" ]; node "B" [] ]
            AvailableColors = [ "Red"; "Green"; "Blue" ]
            Objective = MinimizeColors
            MaxColors = None
            ConflictPenalty = 1.0
        }

    let fieldOf problem =
        match GraphColoring.validate problem with
        | Error(QuantumError.ValidationError(field, _)) -> Some field
        | _ -> None

    Assert.Equal(None, fieldOf baseProblem)

    Assert.Equal(
        Some "ConflictPenalty",
        fieldOf
            { baseProblem with
                ConflictPenalty = 0.0
            }
    )

    let avoidUnknown =
        { baseProblem with
            Nodes =
                [
                    { node "A" [ "B" ] with
                        AvoidColors = [ "Purple" ]
                    }
                    node "B" []
                ]
        }

    Assert.Equal(Some "AvoidColors", fieldOf avoidUnknown)

    let fixedBeyondMax =
        { baseProblem with
            MaxColors = Some 2
            Nodes =
                [
                    { node "A" [ "B" ] with
                        FixedColor = Some "Blue"
                    }
                    node "B" []
                ]
        }

    Assert.Equal(Some "FixedColors", fieldOf fixedBeyondMax)

// ============================================================================
// CONFLICT EDGES: A PAIR LISTED FROM BOTH ENDS IS ONE EDGE
// ============================================================================

let private pairProblem (bothDirections: bool) =
    {
        Nodes =
            [
                GraphColoring.node "A" [ "B" ]
                GraphColoring.node "B" (if bothDirections then [ "A" ] else [])
            ]
        AvailableColors = [ "Red"; "Green" ]
        Objective = MinimizeColors
        MaxColors = None
        ConflictPenalty = 1.0
    }

[<Fact>]
let ``Conflict listed from both ends gives the same edges and QUBO as one listing`` () =
    let oneWay = GraphColoring.toQuantumProblem (pairProblem false) 2
    let bothWays = GraphColoring.toQuantumProblem (pairProblem true) 2

    Assert.Equal(1, bothWays.Edges.Length)
    Assert.Equal(oneWay.Edges.Length, bothWays.Edges.Length)

    let quboOf problem =
        match QSolver.toQubo problem penalty with
        | Ok(matrix, _) -> matrix.Q
        | Error err -> failwith $"toQubo failed: {err}"

    Assert.Equal<Map<int * int, float>>(quboOf oneWay, quboOf bothWays)

[<Fact>]
let ``Conflict listed from both ends is counted once`` () : Task =
    task {
        // One color only: A and B must share it, which is exactly one conflict
        let solveWithOneColor bothDirections =
            solveOk
                { pairProblem bothDirections with
                    MaxColors = Some 1
                }
                2

        let! oneWay = solveWithOneColor false
        let! bothWays = solveWithOneColor true

        Assert.Equal(1, oneWay.ConflictCount)
        Assert.Equal(1, bothWays.ConflictCount)
        Assert.Equal(oneWay.Cost, bothWays.Cost, 9)
    }
    :> Task

// ============================================================================
// CLASSICAL GREEDY: FIXED COLORS, AVOID COLORS, PRIORITY, OBJECTIVE
// ============================================================================

let private classicalOk problem numColors =
    match GraphColoring.solveClassical problem numColors with
    | Ok sol -> sol
    | Error err -> failwith $"solveClassical failed: {err.Message}"

let private pathAB (nodeA: ColoredNode) (nodeB: ColoredNode) =
    {
        Nodes = [ nodeA; nodeB ]
        AvailableColors = [ "Red"; "Green"; "Blue" ]
        Objective = MinimizeColors
        MaxColors = None
        ConflictPenalty = 1.0
    }

[<Fact>]
let ``Classical greedy honours AvoidColors`` () =
    let nodeA =
        { GraphColoring.node "A" [ "B" ] with
            AvoidColors = [ "Red" ]
        }

    let sol = classicalOk (pathAB nodeA (GraphColoring.node "B" [])) 3

    Assert.Equal("Green", sol.Assignments.["A"])
    Assert.Equal("Red", sol.Assignments.["B"])

[<Fact>]
let ``Classical greedy visits nodes in descending priority`` () =
    let nodeB =
        { GraphColoring.node "B" [] with
            Priority = 1.0
        }

    let sol = classicalOk (pathAB (GraphColoring.node "A" [ "B" ]) nodeB) 3

    Assert.Equal("Red", sol.Assignments.["B"])
    Assert.Equal("Green", sol.Assignments.["A"])

[<Fact>]
let ``Classical greedy places fixed colors before free nodes`` () =
    // A is visited first but must not take B's fixed color
    let nodeB =
        { GraphColoring.node "B" [] with
            FixedColor = Some "Red"
        }

    let sol = classicalOk (pathAB (GraphColoring.node "A" [ "B" ]) nodeB) 3

    Assert.True(sol.IsValid)
    Assert.Equal("Red", sol.Assignments.["B"])
    Assert.Equal("Green", sol.Assignments.["A"])

[<Fact>]
let ``Classical greedy follows BalanceColors`` () =
    let problem objectiveValue =
        {
            Nodes =
                [
                    GraphColoring.node "A" [ "B" ]
                    GraphColoring.node "B" []
                    GraphColoring.node "C" []
                    GraphColoring.node "D" []
                ]
            AvailableColors = [ "Red"; "Green" ]
            Objective = objectiveValue
            MaxColors = None
            ConflictPenalty = 1.0
        }

    let minimize = classicalOk (problem MinimizeColors) 2
    Assert.Equal(1, minimize.ColorDistribution.["Green"])

    let balanced = classicalOk (problem BalanceColors) 2
    Assert.Equal(2, balanced.ColorDistribution.["Red"])
    Assert.Equal(2, balanced.ColorDistribution.["Green"])

// ============================================================================
// SOFT TERMS NEVER OUTWEIGH A HARD CONSTRAINT (TOTAL, NOT PER VERTEX)
// ============================================================================

/// Hub h between a vertex f fixed to color 1 and m leaves; K = 2. The only valid
/// coloring puts h on color 0 and every leaf on color 1.
let private hubProblem (leafCount: int) : QSolver.GraphColoringProblem =
    let leaves = [ for i in 1..leafCount -> $"l{i}" ]

    {
        Vertices = [ "f"; "h" ] @ leaves
        Edges =
            GraphOptimization.edge "f" "h" 1.0
            :: [ for l in leaves -> GraphOptimization.edge "h" l 1.0 ]
        NumColors = 2
        FixedColors = Map.ofList [ "f", 1 ]
    }

/// Minimum-energy bitstring over all 2^n assignments (n ≤ 18)
let private groundState (matrix: GraphOptimization.QuboMatrix) =
    let terms = matrix.Q |> Map.toArray

    let energyOf (mask: int) =
        let mutable e = 0.0

        for ((i, j), v) in terms do
            if (mask >>> i) &&& 1 = 1 && (mask >>> j) &&& 1 = 1 then
                e <- e + v

        e

    [ 0 .. (1 <<< matrix.NumVariables) - 1 ] |> List.minBy energyOf

/// One-hot on every vertex, fixed colors kept, no edge with both ends on one color
let private isValidBitstring (problem: QSolver.GraphColoringProblem) (mask: int) =
    let k = problem.NumColors
    let bit v c = (mask >>> (v * k + c)) &&& 1 = 1
    let index = problem.Vertices |> List.mapi (fun i v -> v, i) |> Map.ofList

    let oneHot =
        problem.Vertices
        |> List.mapi (fun v name ->
            let active = [ 0 .. k - 1 ] |> List.filter (bit v)

            match Map.tryFind name problem.FixedColors with
            | Some fixedColor -> active = [ fixedColor ]
            | None -> active.Length = 1)
        |> List.forall id

    let noConflict =
        problem.Edges
        |> List.forall (fun e ->
            [ 0 .. k - 1 ]
            |> List.forall (fun c -> not (bit index.[e.Source] c && bit index.[e.Target] c)))

    oneHot && noConflict

[<Fact>]
let ``QUBO minimum is a valid coloring for a hub with avoiding leaves (Case A)`` () =
    // 5 vertices x 2 colors: the leaves avoid color 1, which the only valid coloring gives them
    let problem = hubProblem 3

    let preferences =
        { QSolver.defaultPreferences with
            AvoidColors = Map.ofList [ for i in 1..3 -> $"l{i}", [ 1 ] ]
        }

    let matrix = qubo problem preferences
    Assert.True(isValidBitstring problem (groundState matrix))

[<Fact>]
let ``QUBO minimum is a valid coloring for a hub with many leaves (Case B)`` () =
    // 9 vertices x 2 colors = 18 variables; with per-vertex soft budgets the leaves'
    // color-index savings added up past one conflict
    let problem = hubProblem 7

    for preferences in
        [
            QSolver.defaultPreferences
            { QSolver.defaultPreferences with
                Goal = QSolver.ColoringGoal.BalanceColors
            }
            { QSolver.defaultPreferences with
                AvoidColors = Map.ofList [ for i in 1..7 -> $"l{i}", [ 1 ] ]
            }
            { QSolver.defaultPreferences with
                Goal = QSolver.ColoringGoal.BalanceColors
                AvoidColors = Map.ofList [ for i in 1..7 -> $"l{i}", [ 1 ] ]
            }
        ] do
        let matrix = qubo problem preferences
        Assert.True(isValidBitstring problem (groundState matrix), $"Invalid ground state for {preferences.Goal}")

[<Fact>]
let ``Valid coloring of two joined stars is cheaper than one conflict (Case C)`` () =
    let leaves prefix = [ for i in 1..6 -> $"{prefix}{i}" ]

    let problem =
        quantumProblem
            ([ "h1"; "h2" ] @ leaves "a" @ leaves "b")
            (("h1", "h2")
             :: ([ for l in leaves "a" -> "h1", l ] @ [ for l in leaves "b" -> "h2", l ]))
            2

    let matrix = qubo problem QSolver.defaultPreferences

    let valid =
        [ "h1", 0; "h2", 1 ]
        @ [ for l in leaves "a" -> l, 1 ]
        @ [ for l in leaves "b" -> l, 0 ]

    let oneConflict =
        [ "h1", 1; "h2", 1 ]
        @ [ for l in leaves "a" -> l, 0 ]
        @ [ for l in leaves "b" -> l, 0 ]

    Assert.True(energy problem matrix valid < energy problem matrix oneConflict)

[<Fact>]
let ``Direct coloring energy equals the QUBO energy`` () =
    let problem =
        { quantumProblem [ "A"; "B"; "C"; "D" ] [ "A", "B"; "B", "C" ] 3 with
            FixedColors = Map.ofList [ "D", 2 ]
        }

    let assignments =
        [
            [ "A", 0; "B", 0; "C", 1; "D", 2 ]
            [ "A", 2; "B", 1; "C", 2; "D", 2 ]
            [ "A", 1; "B", 1; "C", 1; "D", 0 ]
        ]

    for goal in
        [
            QSolver.ColoringGoal.MinimizeColors
            QSolver.ColoringGoal.MinimizeConflicts
            QSolver.ColoringGoal.BalanceColors
        ] do
        let preferences =
            { QSolver.defaultPreferences with
                Goal = goal
                ConflictWeight = 1.5
                AvoidColors = Map.ofList [ "A", [ 0; 2 ]; "D", [ 2 ] ]
            }

        let matrix = qubo problem preferences

        for assignment in assignments do
            Assert.Equal(
                energy problem matrix assignment,
                QSolver.coloringEnergy problem penalty preferences (Map.ofList assignment),
                9
            )

[<Fact>]
let ``Edgeless BalanceColors reports the QUBO energy of its coloring`` () : Task =
    task {
        let problem =
            graphColoring {
                nodes
                    [
                        GraphColoring.node "A" []
                        GraphColoring.node "B" []
                        GraphColoring.node "C" []
                    ]

                colors [ "Red"; "Green" ]
                objective BalanceColors
            }

        let! sol = solveOk problem 2
        let quantum = GraphColoring.toQuantumProblem problem 2

        let preferences =
            { QSolver.defaultPreferences with
                Goal = QSolver.ColoringGoal.BalanceColors
            }

        let colorIndex = Map.ofList [ "Red", 0; "Green", 1 ]

        let assignment =
            sol.Assignments |> Map.toList |> List.map (fun (n, c) -> n, colorIndex.[c])

        Assert.Equal(energy quantum (qubo quantum preferences) assignment, sol.Cost, 9)
    }
    :> Task

[<Fact>]
let ``Edgeless MinimizeColors reuses a color before opening a new one`` () : Task =
    task {
        let problem =
            graphColoring {
                nodes
                    [
                        coloredNode {
                            nodeId "A"
                            avoidColors [ "Red" ]
                        }
                        GraphColoring.node "B" []
                    ]

                colors [ "Red"; "Green" ]
            }

        let! sol = solveOk problem 2

        Assert.Equal("Green", sol.Assignments.["A"])
        Assert.Equal("Green", sol.Assignments.["B"])
        Assert.Equal(1, sol.ColorsUsed)
    }
    :> Task

[<Fact>]
let ``HybridSolver reports an edgeless graph as classical`` () =
    task {
        let problem = quantumProblem [ "A"; "B" ] [] 2

        match!
            HybridSolver.solveGraphColoringAsync
                problem
                2
                None
                None
                (Some HybridSolver.Quantum)
                System.Threading.CancellationToken.None
        with
        | Ok solution ->
            Assert.Equal(HybridSolver.Classical, solution.Method)
            Assert.Contains("no edges", solution.Reasoning)
            Assert.Equal(QSolver.NoCircuitBackendName, solution.Result.BackendName)
        | Error err -> Assert.Fail($"solveGraphColoringAsync failed: {err.Message}")
    }
    :> System.Threading.Tasks.Task
