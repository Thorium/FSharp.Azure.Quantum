namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core
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
///   let solution = GraphColoring.solve problem 3 None
///   
///   // Advanced: Specify cloud quantum backend
///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
///   let solution = GraphColoring.solve problem 3 (Some ionqBackend)
module GraphColoring =
    
    // ============================================================================
    // CORE TYPES - Graph Coloring Domain Model
    // ============================================================================
    
    /// <summary>
    /// A node in the graph coloring problem.
    /// Represents entities that need to be assigned colors (e.g., variables, towers, tasks).
    /// </summary>
    type ColoredNode = {
        /// Unique identifier for this node
        Id: string
        /// List of node IDs that this node conflicts with (cannot have same color)
        ConflictsWith: string list
        /// Optional fixed color assignment (pre-assigned)
        FixedColor: string option
        /// Priority for tie-breaking (higher = assign first, default 0.0)
        Priority: float
        /// Colors to avoid if possible (soft constraint)
        AvoidColors: string list
        /// Additional metadata for this node
        Properties: Map<string, obj>
    }
    
    /// <summary>
    /// Optimization objective for graph coloring.
    /// </summary>
    [<Struct>]
    type ColoringObjective =
        /// Minimize the total number of colors used (chromatic number)
        | MinimizeColors
        /// Minimize conflicts (allow invalid colorings, penalize conflicts)
        | MinimizeConflicts
        /// Balanced usage of colors (load balancing)
        | BalanceColors
    
    /// <summary>
    /// Complete graph coloring problem specification.
    /// </summary>
    type GraphColoringProblem = {
        /// All nodes in the graph
        Nodes: ColoredNode list
        /// Available colors to assign
        AvailableColors: string list
        /// Optimization objective
        Objective: ColoringObjective
        /// Maximum colors to use (None = use as many as needed)
        MaxColors: int option
        /// Penalty weight for conflicts (used with MinimizeConflicts objective)
        ConflictPenalty: float
    }
    
    /// <summary>
    /// Color assignment for a single node.
    /// </summary>
    type ColorAssignment = {
        NodeId: string
        AssignedColor: string
    }
    
    /// <summary>
    /// Solution to a graph coloring problem.
    /// </summary>
    type ColoringSolution = {
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
            Error (QuantumError.ValidationError ("Nodes", "Graph coloring problem must have at least one node"))
        elif problem.AvailableColors.IsEmpty then
            Error (QuantumError.ValidationError ("Colors", "Graph coloring problem must have at least one available color"))
        elif problem.Nodes |> List.exists (fun n -> System.String.IsNullOrWhiteSpace(n.Id)) then
            Error (QuantumError.ValidationError ("NodeIds", "All nodes must have non-empty IDs"))
        else
            let nodeIds = problem.Nodes |> List.map (fun n -> n.Id) |> Set.ofList
            if nodeIds.Count <> problem.Nodes.Length then
                Error (QuantumError.ValidationError ("NodeIds", "Node IDs must be unique"))
            else
                let invalidConflicts = 
                    problem.Nodes
                    |> List.collect (fun n -> n.ConflictsWith)
                    |> List.filter (fun conflictId -> not (nodeIds.Contains conflictId))
                
                if not invalidConflicts.IsEmpty then
                    Error (QuantumError.ValidationError ("Conflicts", sprintf "Invalid conflict references: %A" invalidConflicts))
                else
                    let availableColorSet = Set.ofList problem.AvailableColors
                    let invalidFixedColors =
                        problem.Nodes
                        |> List.choose (fun n -> n.FixedColor)
                        |> List.filter (fun color -> not (availableColorSet.Contains color))
                    
                    if not invalidFixedColors.IsEmpty then
                        Error (QuantumError.ValidationError ("FixedColors", sprintf "Fixed colors not in available colors: %A" invalidFixedColors))
                    else
                        match problem.MaxColors with
                        | Some maxColors when maxColors < 1 ->
                            Error (QuantumError.ValidationError ("MaxColors", "MaxColors must be at least 1"))
                        | Some maxColors when maxColors > problem.AvailableColors.Length ->
                            Error (QuantumError.ValidationError ("MaxColors", sprintf "MaxColors (%d) exceeds available colors (%d)" maxColors problem.AvailableColors.Length))
                        | _ ->
                            Ok ()
    
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
        member _.NodeId(node: ColoredNode, nodeId: string) : ColoredNode =
            { node with Id = nodeId }
        
        [<CustomOperation("conflictsWith")>]
        member _.ConflictsWith(node: ColoredNode, conflicts: string list) : ColoredNode =
            { node with ConflictsWith = conflicts }
        
        [<CustomOperation("fixedColor")>]
        member _.FixedColor(node: ColoredNode, color: string) : ColoredNode =
            { node with FixedColor = Some color }
        
        [<CustomOperation("priority")>]
        member _.Priority(node: ColoredNode, priority: float) : ColoredNode =
            { node with Priority = priority }
        
        [<CustomOperation("avoidColors")>]
        member _.AvoidColors(node: ColoredNode, colors: string list) : ColoredNode =
            { node with AvoidColors = colors }
        
        [<CustomOperation("property")>]
        member _.Property(node: ColoredNode, key: string, value: obj) : ColoredNode =
            { node with Properties = node.Properties |> Map.add key value }
    
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
        
        member _.YieldFrom(problem: GraphColoringProblem) : GraphColoringProblem =
            problem
        
        member this.Zero() : GraphColoringProblem = this.Yield(())
        
        member _.Combine(first: GraphColoringProblem, second: GraphColoringProblem) : GraphColoringProblem =
            {
                Nodes = first.Nodes @ second.Nodes
                AvailableColors = if second.AvailableColors.IsEmpty then first.AvailableColors else second.AvailableColors
                Objective = second.Objective
                MaxColors = match second.MaxColors with | Some _ -> second.MaxColors | None -> first.MaxColors
                ConflictPenalty = if second.ConflictPenalty = 1.0 then first.ConflictPenalty else second.ConflictPenalty
            }
        
        member inline _.Delay([<InlineIfLambda>] f: unit -> GraphColoringProblem) : GraphColoringProblem = f()
        
        member inline this.For(problem: GraphColoringProblem, [<InlineIfLambda>] f: unit -> GraphColoringProblem) : GraphColoringProblem =
            this.Combine(problem, f())
        
        member this.For(sequence: seq<'T>, body: 'T -> GraphColoringProblem) : GraphColoringProblem =
            let mutable state = this.Zero()
            for item in sequence do
                state <- this.Combine(state, body item)
            state
        
        member _.Run(problem: GraphColoringProblem) : GraphColoringProblem =
            match validate problem with
            | Error err -> failwith err.Message
            | Ok () -> problem
        
        [<CustomOperation("node")>]
        member _.Node(problem: GraphColoringProblem, id: string, conflicts: string list) : GraphColoringProblem =
            let newNode = {
                Id = id
                ConflictsWith = conflicts
                FixedColor = None
                Priority = 0.0
                AvoidColors = []
                Properties = Map.empty
            }
            { problem with Nodes = problem.Nodes @ [newNode] }
        
        [<CustomOperation("nodes")>]
        member _.Nodes(problem: GraphColoringProblem, nodeList: ColoredNode list) : GraphColoringProblem =
            { problem with Nodes = problem.Nodes @ nodeList }
        
        [<CustomOperation("colors")>]
        member _.Colors(problem: GraphColoringProblem, colorList: string list) : GraphColoringProblem =
            { problem with AvailableColors = colorList }
        
        [<CustomOperation("objective")>]
        member _.Objective(problem: GraphColoringProblem, obj: ColoringObjective) : GraphColoringProblem =
            { problem with Objective = obj }
        
        [<CustomOperation("maxColors")>]
        member _.MaxColors(problem: GraphColoringProblem, max: int) : GraphColoringProblem =
            { problem with MaxColors = Some max }
        
        [<CustomOperation("conflictPenalty")>]
        member _.ConflictPenalty(problem: GraphColoringProblem, penalty: float) : GraphColoringProblem =
            { problem with ConflictPenalty = penalty }
    
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
            Nodes = [coloredNode]
            AvailableColors = []
            Objective = MinimizeColors
            MaxColors = None
            ConflictPenalty = 1.0
        }
    
    // ============================================================================
    // MAIN SOLVER - QUANTUM-FIRST
    // ============================================================================
    
    /// Solve graph coloring problem using quantum optimization (QAOA)
    /// 
    /// QUANTUM-FIRST API:
    /// - Uses quantum backend by default (LocalBackend for simulation)
    /// - Specify custom backend for cloud quantum hardware (IonQ, Rigetti)
    /// - Returns business-domain Solution result
    /// 
    /// PARAMETERS:
    ///   problem - Graph coloring problem with nodes and conflicts
    ///   numColors - Number of colors to use for solving
    ///   backend - Optional quantum backend (defaults to LocalBackend if None)
    /// 
    /// EXAMPLES:
    ///   // Simple: Automatic quantum simulation
    ///   let solution = GraphColoring.solve problem 3 None
    ///   
    ///   // Cloud execution: Specify IonQ backend
    ///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
    ///   let solution = GraphColoring.solve problem 3 (Some ionqBackend)
    let solve 
        (problem: GraphColoringProblem) 
        (numColors: int) 
        (backend: BackendAbstraction.IQuantumBackend option) 
        : QuantumResult<ColoringSolution> =
        
        quantumResult {
            try
                // Validate problem first
                do! validate problem
                
                // Use provided backend or create LocalBackend for simulation
                let actualBackend = 
                    backend 
                    |> Option.defaultValue (BackendAbstraction.createLocalBackend())
                
                // Create vertex list from nodes
                let vertices = problem.Nodes |> List.map (fun n -> n.Id)
                
                // Create edges from conflicts (undirected)
                let edges = 
                    problem.Nodes
                    |> List.collect (fun n ->
                        n.ConflictsWith
                        |> List.map (fun conflictId ->
                            GraphOptimization.edge n.Id conflictId 1.0
                        )
                    )
                    |> List.distinct
                
                // Build fixed color mapping (color names → indices)
                let colorToIndex = 
                    problem.AvailableColors 
                    |> List.mapi (fun i color -> color, i)
                    |> Map.ofList
                
                let fixedColors = 
                    problem.Nodes
                    |> List.choose (fun n ->
                        match n.FixedColor with
                        | Some color -> Some (n.Id, colorToIndex.[color])
                        | None -> None
                    )
                    |> Map.ofList
                
                // Convert to quantum solver format
                let quantumProblem : QuantumGraphColoringSolver.GraphColoringProblem = {
                    Vertices = vertices
                    Edges = edges
                    NumColors = min numColors problem.AvailableColors.Length
                    FixedColors = fixedColors
                }
                
                // Create quantum solver configuration
                let quantumConfig = QuantumGraphColoringSolver.defaultConfig numColors
                
                // Call quantum solver
                let! quantumResult = QuantumGraphColoringSolver.solve actualBackend quantumProblem quantumConfig
                
                // Map color indices back to color names
                let indexToColor = 
                    problem.AvailableColors 
                    |> List.mapi (fun i color -> i, color)
                    |> Map.ofList
                
                let assignments =
                    quantumResult.ColorAssignments
                    |> Map.toList
                    |> List.map (fun (nodeId, colorIdx) ->
                        let colorName = Map.find colorIdx indexToColor
                        nodeId, colorName
                    )
                    |> Map.ofList
                
                // Color distribution
                let colorDistribution =
                    assignments
                    |> Map.toList
                    |> List.map snd
                    |> List.groupBy id
                    |> List.map (fun (color, group) -> color, List.length group)
                    |> Map.ofList
                
                return {
                    Assignments = assignments
                    ColorsUsed = quantumResult.ColorsUsed
                    ConflictCount = quantumResult.ConflictCount
                    IsValid = quantumResult.IsValid
                    ColorDistribution = colorDistribution
                    Cost = quantumResult.BestEnergy
                    BackendName = quantumResult.BackendName
                    IsQuantum = true
                }
            with
            | ex -> 
                return! Error (QuantumError.OperationError ("Graph coloring solve", $"Failed: {ex.Message}"))
        }
    
    /// Solve graph coloring using classical greedy algorithm (for comparison)
    let solveClassical (problem: GraphColoringProblem) (numColors: int) : QuantumResult<ColoringSolution> =
        quantumResult {
            try
                // Validate problem first
                do! validate problem
                
                let vertices = problem.Nodes |> List.map (fun n -> n.Id)
                
                let edges = 
                    problem.Nodes
                    |> List.collect (fun n ->
                        n.ConflictsWith
                        |> List.map (fun conflictId ->
                            GraphOptimization.edge n.Id conflictId 1.0
                        )
                    )
                    |> List.distinct
                
                let colorToIndex = 
                    problem.AvailableColors 
                    |> List.mapi (fun i color -> color, i)
                    |> Map.ofList
                
                let fixedColors = 
                    problem.Nodes
                    |> List.choose (fun n ->
                        match n.FixedColor with
                        | Some color -> Some (n.Id, colorToIndex.[color])
                        | None -> None
                    )
                    |> Map.ofList
                
                let quantumProblem : QuantumGraphColoringSolver.GraphColoringProblem = {
                    Vertices = vertices
                    Edges = edges
                    NumColors = min numColors problem.AvailableColors.Length
                    FixedColors = fixedColors
                }
                
                let classicalResult = QuantumGraphColoringSolver.solveClassical quantumProblem
                
                let indexToColor = 
                    problem.AvailableColors 
                    |> List.mapi (fun i color -> i, color)
                    |> Map.ofList
                
                let assignments =
                    classicalResult.ColorAssignments
                    |> Map.toList
                    |> List.map (fun (nodeId, colorIdx) ->
                        let colorName = Map.find colorIdx indexToColor
                        nodeId, colorName
                    )
                    |> Map.ofList
                
                let colorDistribution =
                    assignments
                    |> Map.toList
                    |> List.map snd
                    |> List.groupBy id
                    |> List.map (fun (color, group) -> color, List.length group)
                    |> Map.ofList
                
                return {
                    Assignments = assignments
                    ColorsUsed = classicalResult.ColorsUsed
                    ConflictCount = classicalResult.ConflictCount
                    IsValid = classicalResult.IsValid
                    ColorDistribution = colorDistribution
                    Cost = classicalResult.BestEnergy
                    BackendName = "Classical Greedy"
                    IsQuantum = false
                }
            with
            | ex -> 
                return! Error (QuantumError.OperationError ("Classical graph coloring solve", $"Failed: {ex.Message}"))
        }
    
    // ============================================================================
    // COMMON GRAPH PATTERNS - HELPER FUNCTIONS
    // ============================================================================
    
    /// Create register allocation problem (compiler use case)
    let registerAllocation (variables: string list) (conflicts: (string * string) list) (registers: string list) : GraphColoringProblem =
        let nodes = 
            variables
            |> List.map (fun var ->
                let varConflicts = 
                    conflicts
                    |> List.collect (fun (v1, v2) ->
                        if v1 = var then [v2]
                        elif v2 = var then [v1]
                        else []
                    )
                    |> List.distinct
                node var varConflicts
            )
        
        {
            Nodes = nodes
            AvailableColors = registers
            Objective = MinimizeColors
            MaxColors = Some registers.Length
            ConflictPenalty = 1.0
        }
    
    /// Create frequency assignment problem (wireless network use case)
    let frequencyAssignment (towers: string list) (interferences: (string * string) list) (frequencies: string list) : GraphColoringProblem =
        let nodes = 
            towers
            |> List.map (fun tower ->
                let towerInterferences = 
                    interferences
                    |> List.collect (fun (t1, t2) ->
                        if t1 = tower then [t2]
                        elif t2 = tower then [t1]
                        else []
                    )
                    |> List.distinct
                node tower towerInterferences
            )
        
        {
            Nodes = nodes
            AvailableColors = frequencies
            Objective = MinimizeColors
            MaxColors = None
            ConflictPenalty = 1.0
        }
    
    /// Create exam scheduling problem (university use case)
    let examScheduling (exams: string list) (studentConflicts: (string * string) list) (timeSlots: string list) : GraphColoringProblem =
        let nodes = 
            exams
            |> List.map (fun exam ->
                let examConflicts = 
                    studentConflicts
                    |> List.collect (fun (e1, e2) ->
                        if e1 = exam then [e2]
                        elif e2 = exam then [e1]
                        else []
                    )
                    |> List.distinct
                node exam examConflicts
            )
        
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
        // Use greedy algorithm as lower bound approximation
        match solveClassical problem problem.AvailableColors.Length with
        | Ok solution -> solution.ColorsUsed
        | Error _ -> problem.AvailableColors.Length
    
    /// Export solution to human-readable string
    let describeSolution (solution: ColoringSolution) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine("=== Graph Coloring Solution ===") |> ignore
        sb.AppendLine(sprintf "Status: %s" (if solution.IsValid then "✓ Valid" else "✗ Invalid")) |> ignore
        sb.AppendLine(sprintf "Colors Used: %d" solution.ColorsUsed) |> ignore
        sb.AppendLine(sprintf "Conflicts: %d" solution.ConflictCount) |> ignore
        sb.AppendLine(sprintf "Backend: %s" solution.BackendName) |> ignore
        sb.AppendLine(sprintf "Algorithm: %s" (if solution.IsQuantum then "Quantum QAOA" else "Classical Greedy")) |> ignore
        sb.AppendLine("") |> ignore
        
        sb.AppendLine("Color Distribution:") |> ignore
        for (color, count) in Map.toList solution.ColorDistribution do
            sb.AppendLine(sprintf "  %s: %d nodes" color count) |> ignore
        
        sb.AppendLine("") |> ignore
        sb.AppendLine("Assignments:") |> ignore
        for (nodeId, color) in Map.toList solution.Assignments do
            sb.AppendLine(sprintf "  %s → %s" nodeId color) |> ignore
        
        sb.ToString()
