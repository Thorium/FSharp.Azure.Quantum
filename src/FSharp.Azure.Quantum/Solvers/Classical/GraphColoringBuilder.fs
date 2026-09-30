namespace FSharp.Azure.Quantum

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.GraphOptimization

/// High-level Graph Coloring Builder - Quantum-First API
///
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for users who want to solve graph coloring problems
/// without understanding quantum computing internals (QAOA, QUBO, backends).
///
/// QUANTUM-FIRST:
/// - Uses quantum optimization (QAOA) by default via LocalBackend (simulation)
/// - Optional backend parameter for cloud quantum hardware (IonQ, Rigetti)
/// - For algorithm-level control, use QuantumGraphColoringSolver directly
///
/// WHAT IS GRAPH COLORING:
/// Assign colors to graph vertices such that no adjacent vertices share the same color,
/// while minimizing the total number of colors used (chromatic number).
///
/// USE CASES:
/// - Register allocation: Assign CPU registers to variables (no conflicts)
/// - Frequency assignment: Assign radio frequencies to cell towers (no interference)
/// - Exam scheduling: Schedule exams so no student has conflicts
/// - Task scheduling: Assign time slots to tasks with dependencies
///
/// EXAMPLE USAGE:
///   // Simple: Uses quantum simulation automatically
///   let problem = graphColoring {
///       node "R1" conflictsWith ["R2"; "R3"]
///       node "R2" conflictsWith ["R1"; "R4"]
///       colors ["EAX"; "EBX"; "ECX"]
///   }
///   let! solution = GraphColoring.solveAsync problem 3 None cancellationToken
///
///   // Advanced: Specify cloud quantum backend
///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
///   let! solution = GraphColoring.solveAsync problem 3 (Some ionqBackend) cancellationToken
module GraphColoring =

    // ============================================================================
    // CORE TYPES - Graph Coloring Domain Model
    // ============================================================================

    /// <summary>
    /// A node in the graph coloring problem.
    /// Represents entities that need to be assigned colors (e.g., variables, towers, tasks).
    /// </summary>
    type ColoredNode =
        {
            /// Unique identifier for this node
            Id: string
            /// List of node IDs that this node conflicts with (cannot have same color)
            ConflictsWith: string list
            /// Optional fixed color assignment (pre-assigned)
            FixedColor: string option
            /// Priority for tie-breaking (higher = assign first, default 0.0).
            /// Among measured samples that rank equal on the objective and on QUBO energy,
            /// the one giving higher-priority nodes earlier colors (lower index in
            /// AvailableColors) is returned; the greedy coloring (graphs without conflicts,
            /// classical solver) visits nodes in descending priority. It never outweighs the
            /// objective or a soft penalty.
            Priority: float
            /// Colors to avoid if possible (soft constraint): assigning one costs 0.3 × P / n
            /// (P = the solver's penalty weight, n = number of nodes), so all avoided colors
            /// together cost at most 0.3 × P. Must be in AvailableColors.
            AvoidColors: string list
            /// Additional metadata for this node
            Properties: Map<string, obj>
        }

    /// <summary>
    /// Optimization objective for graph coloring.
    /// </summary>
    [<Struct>]
    type ColoringObjective =
        /// Minimize the total number of colors used (chromatic number): a small cost that
        /// grows with the color index, among valid samples the fewest colors wins, and the
        /// greedy coloring reuses a color already in use before opening a new one
        | MinimizeColors
        /// Minimize conflicts (allow invalid colorings, penalize conflicts): no color-count
        /// preference, and samples are ranked by conflict count
        | MinimizeConflicts
        /// Balanced usage of colors (load balancing): penalty Σ_c (nodes with color c)²,
        /// and among valid samples the most even class sizes win
        | BalanceColors

    /// <summary>
    /// Complete graph coloring problem specification.
    /// </summary>
    type GraphColoringProblem =
        {
            /// All nodes in the graph
            Nodes: ColoredNode list
            /// Available colors to assign
            AvailableColors: string list
            /// Optimization objective
            Objective: ColoringObjective
            /// Maximum colors to use (None = use as many as needed): only the first MaxColors
            /// entries of AvailableColors are encoded, so no other color can be assigned
            MaxColors: int option
            /// Weight of the conflict penalty (same color on both ends of an edge), as a
            /// multiplier on the solver's penalty weight P; applies to every objective. Must be
            /// positive. The soft terms (objective, avoided colors) total at most 0.5 × P on a
            /// valid coloring, so at 1.0 (the default), and at any value above 0.5, the QUBO
            /// minimum is a valid coloring whenever one exists; at 0.5 or below a coloring with
            /// conflicts can have lower energy than every valid coloring.
            ConflictPenalty: float
        }

    /// <summary>
    /// Color assignment for a single node.
    /// </summary>
    type ColorAssignment =
        {
            NodeId: string
            AssignedColor: string
        }

    /// <summary>
    /// Solution to a graph coloring problem.
    /// </summary>
    type ColoringSolution =
        {
            /// Color assignments for all nodes
            Assignments: Map<string, string>
            /// Number of distinct colors used
            ColorsUsed: int
            /// Number of conflicts (nodes with same color connected by edge)
            ConflictCount: int
            /// Whether solution is valid (no conflicts)
            IsValid: bool
            /// Color usage distribution (for BalanceColors objective)
            ColorDistribution: Map<string, int>
            /// Total cost/energy of solution
            Cost: float
            /// Backend used (LocalBackend, IonQ, etc.)
            BackendName: string
            /// Whether quantum or classical solver was used
            IsQuantum: bool
        }

    // ============================================================================
    // VALIDATION HELPERS
    // ============================================================================

    /// <summary>
    /// Validates a graph coloring problem specification.
    /// </summary>
    let validate (problem: GraphColoringProblem) : QuantumResult<unit> =
        if problem.Nodes.IsEmpty then
            Error(QuantumError.ValidationError("Nodes", "Graph coloring problem must have at least one node"))
        elif problem.AvailableColors.IsEmpty then
            Error(
                QuantumError.ValidationError("Colors", "Graph coloring problem must have at least one available color")
            )
        elif problem.Nodes |> List.exists (fun n -> System.String.IsNullOrWhiteSpace(n.Id)) then
            Error(QuantumError.ValidationError("NodeIds", "All nodes must have non-empty IDs"))
        else
            let nodeIds = problem.Nodes |> List.map (fun n -> n.Id) |> Set.ofList

            if nodeIds.Count <> problem.Nodes.Length then
                Error(QuantumError.ValidationError("NodeIds", "Node IDs must be unique"))
            else
                let invalidConflicts =
                    problem.Nodes
                    |> List.collect (fun n -> n.ConflictsWith)
                    |> List.filter (fun conflictId -> not (nodeIds.Contains conflictId))

                if not invalidConflicts.IsEmpty then
                    Error(
                        QuantumError.ValidationError("Conflicts", $"Invalid conflict references: %A{invalidConflicts}")
                    )
                else
                    let availableColorSet = Set.ofList problem.AvailableColors

                    let invalidFixedColors =
                        problem.Nodes
                        |> List.choose (fun n -> n.FixedColor)
                        |> List.filter (fun color -> not (availableColorSet.Contains color))

                    if not invalidFixedColors.IsEmpty then
                        Error(
                            QuantumError.ValidationError(
                                "FixedColors",
                                $"Fixed colors not in available colors: %A{invalidFixedColors}"
                            )
                        )
                    else
                        let invalidAvoidColors =
                            problem.Nodes
                            |> List.collect (fun n -> n.AvoidColors)
                            |> List.filter (fun color -> not (availableColorSet.Contains color))

                        let colorIndex color =
                            problem.AvailableColors |> List.findIndex ((=) color)

                        match problem.MaxColors with
                        | _ when not invalidAvoidColors.IsEmpty ->
                            Error(
                                QuantumError.ValidationError(
                                    "AvoidColors",
                                    $"Avoid colors not in available colors: %A{invalidAvoidColors}"
                                )
                            )
                        | _ when
                            not (problem.ConflictPenalty > 0.0)
                            || System.Double.IsInfinity problem.ConflictPenalty
                            ->
                            Error(
                                QuantumError.ValidationError(
                                    "ConflictPenalty",
                                    $"ConflictPenalty must be a positive finite number, got %g{problem.ConflictPenalty}"
                                )
                            )
                        | Some maxColors when maxColors < 1 ->
                            Error(QuantumError.ValidationError("MaxColors", "MaxColors must be at least 1"))
                        | Some maxColors when maxColors > problem.AvailableColors.Length ->
                            Error(
                                QuantumError.ValidationError(
                                    "MaxColors",
                                    $"MaxColors (%d{maxColors}) exceeds available colors (%d{problem.AvailableColors.Length})"
                                )
                            )
                        | Some maxColors ->
                            let fixedBeyondMax =
                                problem.Nodes
                                |> List.choose (fun n -> n.FixedColor)
                                |> List.filter (fun color -> colorIndex color >= maxColors)

                            if fixedBeyondMax.IsEmpty then
                                Ok()
                            else
                                Error(
                                    QuantumError.ValidationError(
                                        "FixedColors",
                                        $"Fixed colors outside the first MaxColors (%d{maxColors}) available colors: %A{fixedBeyondMax}"
                                    )
                                )
                        | None -> Ok()

    // ============================================================================
    // COMPUTATION EXPRESSION BUILDERS - Colored Node Builder
    // ============================================================================

    /// <summary>
    /// Computation expression builder for defining colored nodes with advanced features.
    /// </summary>
    type ColoredNodeBuilder() =

        member _.Yield(_) : ColoredNode =
            {
                Id = ""
                ConflictsWith = []
                FixedColor = None
                Priority = 0.0
                AvoidColors = []
                Properties = Map.empty
            }

        [<CustomOperation("nodeId")>]
        member _.NodeId(node: ColoredNode, nodeId: string) : ColoredNode = { node with Id = nodeId }

        [<CustomOperation("conflictsWith")>]
        member _.ConflictsWith(node: ColoredNode, conflicts: string list) : ColoredNode =
            { node with ConflictsWith = conflicts }

        [<CustomOperation("fixedColor")>]
        member _.FixedColor(node: ColoredNode, color: string) : ColoredNode = { node with FixedColor = Some color }

        [<CustomOperation("priority")>]
        member _.Priority(node: ColoredNode, priority: float) : ColoredNode = { node with Priority = priority }

        [<CustomOperation("avoidColors")>]
        member _.AvoidColors(node: ColoredNode, colors: string list) : ColoredNode = { node with AvoidColors = colors }

        [<CustomOperation("property")>]
        member _.Property(node: ColoredNode, key: string, value: obj) : ColoredNode =
            { node with
                Properties = node.Properties |> Map.add key value
            }

    /// Global instance of coloredNode builder
    let coloredNode = ColoredNodeBuilder()

    // ============================================================================
    // COMPUTATION EXPRESSION BUILDERS - Graph Coloring Problem Builder
    // ============================================================================

    /// <summary>
    /// Computation expression builder for defining graph coloring problems.
    /// </summary>
    type GraphColoringBuilder() =

        member _.Yield(_) : GraphColoringProblem =
            {
                Nodes = []
                AvailableColors = []
                Objective = MinimizeColors
                MaxColors = None
                ConflictPenalty = 1.0
            }

        member _.YieldFrom(problem: GraphColoringProblem) : GraphColoringProblem = problem

        member this.Zero() : GraphColoringProblem = this.Yield()

        member _.Combine(first: GraphColoringProblem, second: GraphColoringProblem) : GraphColoringProblem =
            {
                Nodes = first.Nodes @ second.Nodes
                AvailableColors =
                    if second.AvailableColors.IsEmpty then
                        first.AvailableColors
                    else
                        second.AvailableColors
                Objective = second.Objective
                MaxColors =
                    match second.MaxColors with
                    | Some _ -> second.MaxColors
                    | None -> first.MaxColors
                ConflictPenalty =
                    if second.ConflictPenalty = 1.0 then
                        first.ConflictPenalty
                    else
                        second.ConflictPenalty
            }

        member inline _.Delay([<InlineIfLambda>] f: unit -> GraphColoringProblem) : GraphColoringProblem = f ()

        member inline this.For
            (problem: GraphColoringProblem, [<InlineIfLambda>] f: unit -> GraphColoringProblem)
            : GraphColoringProblem =
            this.Combine(problem, f ())

        member this.For(sequence: seq<'T>, body: 'T -> GraphColoringProblem) : GraphColoringProblem =
            let state =
                sequence
                |> Seq.fold (fun state item -> this.Combine(state, body item)) (this.Zero())

            state

        member _.Run(problem: GraphColoringProblem) : GraphColoringProblem =
            match validate problem with
            | Error err -> failwith err.Message
            | Ok() -> problem

        [<CustomOperation("node")>]
        member _.Node(problem: GraphColoringProblem, id: string, conflicts: string list) : GraphColoringProblem =
            let newNode =
                {
                    Id = id
                    ConflictsWith = conflicts
                    FixedColor = None
                    Priority = 0.0
                    AvoidColors = []
                    Properties = Map.empty
                }

            { problem with
                Nodes = problem.Nodes @ [ newNode ]
            }

        [<CustomOperation("nodes")>]
        member _.Nodes(problem: GraphColoringProblem, nodeList: ColoredNode list) : GraphColoringProblem =
            { problem with
                Nodes = problem.Nodes @ nodeList
            }

        [<CustomOperation("colors")>]
        member _.Colors(problem: GraphColoringProblem, colorList: string list) : GraphColoringProblem =
            { problem with
                AvailableColors = colorList
            }

        [<CustomOperation("objective")>]
        member _.Objective(problem: GraphColoringProblem, obj: ColoringObjective) : GraphColoringProblem =
            { problem with Objective = obj }

        [<CustomOperation("maxColors")>]
        member _.MaxColors(problem: GraphColoringProblem, max: int) : GraphColoringProblem =
            { problem with MaxColors = Some max }

        [<CustomOperation("conflictPenalty")>]
        member _.ConflictPenalty(problem: GraphColoringProblem, penalty: float) : GraphColoringProblem =
            { problem with
                ConflictPenalty = penalty
            }

    /// Global instance of graphColoring builder
    let graphColoring = GraphColoringBuilder()

    // ============================================================================
    // HELPER FUNCTIONS - Quick Node Creation
    // ============================================================================

    /// Quick helper to create a simple node with ID and conflicts
    let node id conflicts : ColoredNode =
        {
            Id = id
            ConflictsWith = conflicts
            FixedColor = None
            Priority = 0.0
            AvoidColors = []
            Properties = Map.empty
        }

    /// Helper to create a single-node problem (for use in for loops with yield!)
    let singleNode (coloredNode: ColoredNode) : GraphColoringProblem =
        {
            Nodes = [ coloredNode ]
            AvailableColors = []
            Objective = MinimizeColors
            MaxColors = None
            ConflictPenalty = 1.0
        }

    // ============================================================================
    // MAIN SOLVER - QUANTUM-FIRST
    // ============================================================================

    /// Number of colors the solver encodes: numColors, capped by the available colors and MaxColors.
    let private effectiveColorCount (problem: GraphColoringProblem) (numColors: int) : int =
        let maxColors =
            problem.MaxColors |> Option.defaultValue problem.AvailableColors.Length

        List.min [ numColors; problem.AvailableColors.Length; maxColors ]

    /// Soft preferences for the quantum solver: conflict weight, objective, avoided colors, priorities.
    let private toPreferences (problem: GraphColoringProblem) : QuantumGraphColoringSolver.ColoringPreferences =
        let colorToIndex =
            problem.AvailableColors |> List.mapi (fun i color -> color, i) |> Map.ofList

        {
            ConflictWeight = problem.ConflictPenalty
            Goal =
                match problem.Objective with
                | MinimizeColors -> QuantumGraphColoringSolver.ColoringGoal.MinimizeColors
                | MinimizeConflicts -> QuantumGraphColoringSolver.ColoringGoal.MinimizeConflicts
                | BalanceColors -> QuantumGraphColoringSolver.ColoringGoal.BalanceColors
            AvoidColors =
                problem.Nodes
                |> List.filter (fun n -> not n.AvoidColors.IsEmpty)
                |> List.map (fun n -> n.Id, n.AvoidColors |> List.map (fun color -> colorToIndex.[color]))
                |> Map.ofList
            Priorities = problem.Nodes |> List.map (fun n -> n.Id, n.Priority) |> Map.ofList
        }

    /// Solver-level problem: one undirected edge per conflicting pair (a pair listed from
    /// both ends is one edge), fixed colors as indices, and the effective color count.
    let internal toQuantumProblem
        (problem: GraphColoringProblem)
        (numColors: int)
        : QuantumGraphColoringSolver.GraphColoringProblem =
        let edges =
            problem.Nodes
            |> List.collect (fun n ->
                n.ConflictsWith
                |> List.map (fun conflictId ->
                    if n.Id <= conflictId then
                        n.Id, conflictId
                    else
                        conflictId, n.Id))
            |> List.distinct
            |> List.map (fun (source, target) -> GraphOptimization.edge source target 1.0)

        let colorToIndex =
            problem.AvailableColors |> List.mapi (fun i color -> color, i) |> Map.ofList

        let fixedColors =
            problem.Nodes
            |> List.choose (fun n -> n.FixedColor |> Option.map (fun color -> n.Id, colorToIndex.[color]))
            |> Map.ofList

        {
            Vertices = problem.Nodes |> List.map (fun n -> n.Id)
            Edges = edges
            NumColors = effectiveColorCount problem numColors
            FixedColors = fixedColors
        }

    /// Solve graph coloring problem using quantum optimization (QAOA), asynchronously
    ///
    /// QUANTUM-FIRST API:
    /// - Uses quantum backend by default (LocalBackend for simulation)
    /// - Specify custom backend for cloud quantum hardware (IonQ, Rigetti)
    /// - Returns business-domain Solution result
    /// - Does not block: the backend call is awaited, so cloud jobs do not tie up a thread
    ///
    /// PARAMETERS:
    ///   problem - Graph coloring problem with nodes and conflicts
    ///   numColors - Number of colors to use for solving (capped by AvailableColors and MaxColors)
    ///   backend - Optional quantum backend (defaults to LocalBackend if None)
    ///   cancellationToken - Cancels the backend execution
    ///
    /// EXAMPLES:
    ///   // Simple: Automatic quantum simulation
    ///   let! solution = GraphColoring.solveAsync problem 3 None CancellationToken.None
    ///
    ///   // Cloud execution: Specify IonQ backend
    ///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
    ///   let! solution = GraphColoring.solveAsync problem 3 (Some ionqBackend) cancellationToken
    let solveAsync
        (problem: GraphColoringProblem)
        (numColors: int)
        (backend: BackendAbstraction.IQuantumBackend option)
        (cancellationToken: CancellationToken)
        : Task<QuantumResult<ColoringSolution>> =

        quantumResultTask {
            try
                // Validate problem first
                do! validate problem

                // Use provided backend or create LocalBackend for simulation
                let actualBackend =
                    backend
                    |> Option.defaultValue (LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend)

                // Convert to quantum solver format
                let quantumProblem = toQuantumProblem problem numColors

                // Create quantum solver configuration
                let quantumConfig =
                    QuantumGraphColoringSolver.defaultConfig quantumProblem.NumColors

                // Call quantum solver (a graph without conflicts is colored without a circuit)
                let! quantumResult =
                    QuantumGraphColoringSolver.solveWithPreferencesAsync
                        actualBackend
                        quantumProblem
                        (toPreferences problem)
                        quantumConfig
                        cancellationToken

                // Map color indices back to color names
                let indexToColor =
                    problem.AvailableColors |> List.mapi (fun i color -> i, color) |> Map.ofList

                let assignments =
                    quantumResult.ColorAssignments
                    |> Map.toList
                    |> List.map (fun (nodeId, colorIdx) ->
                        let colorName = Map.find colorIdx indexToColor
                        nodeId, colorName)
                    |> Map.ofList

                // Color distribution
                let colorDistribution =
                    assignments
                    |> Map.toList
                    |> List.map snd
                    |> List.groupBy id
                    |> List.map (fun (color, group) -> color, List.length group)
                    |> Map.ofList

                return
                    {
                        Assignments = assignments
                        ColorsUsed = quantumResult.ColorsUsed
                        ConflictCount = quantumResult.ConflictCount
                        IsValid = quantumResult.IsValid
                        ColorDistribution = colorDistribution
                        Cost = quantumResult.BestEnergy
                        BackendName = quantumResult.BackendName
                        IsQuantum = quantumResult.BackendName <> QuantumGraphColoringSolver.NoCircuitBackendName
                    }
            with ex ->
                return! Error(QuantumError.OperationError("Graph coloring solve", $"Failed: {ex.Message}"))
        }

    /// Solve graph coloring problem using quantum optimization (QAOA)
    ///
    /// This is a synchronous wrapper around `solveAsync` for backward compatibility:
    /// it blocks the calling thread until the backend has answered.
    ///
    /// PARAMETERS:
    ///   problem - Graph coloring problem with nodes and conflicts
    ///   numColors - Number of colors to use for solving (capped by AvailableColors and MaxColors)
    ///   backend - Optional quantum backend (defaults to LocalBackend if None)
    [<Obsolete("Use solveAsync for non-blocking execution against cloud backends")>]
    let solve
        (problem: GraphColoringProblem)
        (numColors: int)
        (backend: BackendAbstraction.IQuantumBackend option)
        : QuantumResult<ColoringSolution> =
        solveAsync problem numColors backend CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    /// Solve graph coloring using classical greedy algorithm (for comparison): fixed colors,
    /// MaxColors, AvoidColors, Priority (visiting order) and BalanceColors (smallest class
    /// first) apply; the greedy never creates a conflict, so ConflictPenalty has no effect.
    let internal solveClassical (problem: GraphColoringProblem) (numColors: int) : QuantumResult<ColoringSolution> =
        quantumResult {
            try
                // Validate problem first
                do! validate problem

                let quantumProblem = toQuantumProblem problem numColors

                // Propagates Error when the greedy heuristic cannot color the graph
                // with the available colors (instead of throwing).
                let! classicalResult =
                    QuantumGraphColoringSolver.solveClassicalWithPreferences quantumProblem (toPreferences problem)

                let indexToColor =
                    problem.AvailableColors |> List.mapi (fun i color -> i, color) |> Map.ofList

                let assignments =
                    classicalResult.ColorAssignments
                    |> Map.toList
                    |> List.map (fun (nodeId, colorIdx) ->
                        let colorName = Map.find colorIdx indexToColor
                        nodeId, colorName)
                    |> Map.ofList

                let colorDistribution =
                    assignments
                    |> Map.toList
                    |> List.map snd
                    |> List.groupBy id
                    |> List.map (fun (color, group) -> color, List.length group)
                    |> Map.ofList

                return
                    {
                        Assignments = assignments
                        ColorsUsed = classicalResult.ColorsUsed
                        ConflictCount = classicalResult.ConflictCount
                        IsValid = classicalResult.IsValid
                        ColorDistribution = colorDistribution
                        Cost = classicalResult.BestEnergy
                        BackendName = "Classical Greedy"
                        IsQuantum = false
                    }
            with ex ->
                return! Error(QuantumError.OperationError("Classical graph coloring solve", $"Failed: {ex.Message}"))
        }

    // ============================================================================
    // COMMON GRAPH PATTERNS - HELPER FUNCTIONS
    // ============================================================================

    /// Create register allocation problem (compiler use case)
    let registerAllocation
        (variables: string list)
        (conflicts: (string * string) list)
        (registers: string list)
        : GraphColoringProblem =
        let nodes =
            variables
            |> List.map (fun var ->
                let varConflicts =
                    conflicts
                    |> List.collect (fun (v1, v2) ->
                        if v1 = var then [ v2 ]
                        elif v2 = var then [ v1 ]
                        else [])
                    |> List.distinct

                node var varConflicts)

        {
            Nodes = nodes
            AvailableColors = registers
            Objective = MinimizeColors
            MaxColors = Some registers.Length
            ConflictPenalty = 1.0
        }

    /// Create frequency assignment problem (wireless network use case)
    let frequencyAssignment
        (towers: string list)
        (interferences: (string * string) list)
        (frequencies: string list)
        : GraphColoringProblem =
        let nodes =
            towers
            |> List.map (fun tower ->
                let towerInterferences =
                    interferences
                    |> List.collect (fun (t1, t2) ->
                        if t1 = tower then [ t2 ]
                        elif t2 = tower then [ t1 ]
                        else [])
                    |> List.distinct

                node tower towerInterferences)

        {
            Nodes = nodes
            AvailableColors = frequencies
            Objective = MinimizeColors
            MaxColors = None
            ConflictPenalty = 1.0
        }

    /// Create exam scheduling problem (university use case)
    let examScheduling
        (exams: string list)
        (studentConflicts: (string * string) list)
        (timeSlots: string list)
        : GraphColoringProblem =
        let nodes =
            exams
            |> List.map (fun exam ->
                let examConflicts =
                    studentConflicts
                    |> List.collect (fun (e1, e2) ->
                        if e1 = exam then [ e2 ]
                        elif e2 = exam then [ e1 ]
                        else [])
                    |> List.distinct

                node exam examConflicts)

        {
            Nodes = nodes
            AvailableColors = timeSlots
            Objective = MinimizeColors
            MaxColors = None
            ConflictPenalty = 1.0
        }

    // ============================================================================
    // VALIDATION AND UTILITIES
    // ============================================================================

    /// Check if solution is valid (no conflicts)
    let isValidSolution (problem: GraphColoringProblem) (solution: ColoringSolution) : bool =
        solution.IsValid && solution.ConflictCount = 0

    /// Calculate chromatic number (minimum colors needed) - approximation
    let approximateChromaticNumber (problem: GraphColoringProblem) : int =
        // Use greedy algorithm as lower bound approximation, without the soft preferences
        // and the MaxColors cap that would change the number of colors it uses
        let plainProblem =
            { problem with
                Objective = MinimizeColors
                MaxColors = None
                Nodes = problem.Nodes |> List.map (fun n -> { n with AvoidColors = [] })
            }

        (solveClassical plainProblem problem.AvailableColors.Length)
        |> Result.map (fun solution -> solution.ColorsUsed)
        |> Result.defaultWith (fun _ -> problem.AvailableColors.Length)

    /// Export solution to human-readable string
    let describeSolution (solution: ColoringSolution) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine("=== Graph Coloring Solution ===") |> ignore

        sb.AppendLine(sprintf "Status: %s" (if solution.IsValid then "✓ Valid" else "✗ Invalid"))
        |> ignore

        sb.AppendLine($"Colors Used: %d{solution.ColorsUsed}") |> ignore
        sb.AppendLine($"Conflicts: %d{solution.ConflictCount}") |> ignore
        sb.AppendLine($"Backend: %s{solution.BackendName}") |> ignore

        sb.AppendLine(
            sprintf
                "Algorithm: %s"
                (if solution.IsQuantum then
                     "Quantum QAOA"
                 else
                     "Classical Greedy")
        )
        |> ignore

        sb.AppendLine("") |> ignore

        sb.AppendLine("Color Distribution:") |> ignore

        for (color, count) in Map.toList solution.ColorDistribution do
            sb.AppendLine($"  %s{color}: %d{count} nodes") |> ignore

        sb.AppendLine("") |> ignore
        sb.AppendLine("Assignments:") |> ignore

        for (nodeId, color) in Map.toList solution.Assignments do
            sb.AppendLine($"  %s{nodeId} → %s{color}") |> ignore

        sb.ToString()
