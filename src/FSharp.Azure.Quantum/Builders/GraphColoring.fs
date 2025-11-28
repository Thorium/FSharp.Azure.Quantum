namespace FSharp.Azure.Quantum

/// <summary>
/// Graph Coloring Domain Builder - F# Computation Expression API
/// 
/// Provides idiomatic F# builders for graph coloring problems including
/// register allocation, frequency assignment, and scheduling.
/// </summary>
/// <remarks>
/// <para>Uses underlying Generic Graph Optimization Framework (TKT-90) for solving.</para>
/// 
/// <para><b>Available Builders:</b></para>
/// <list type="bullet">
/// <item><c>coloredNode { ... }</c> - Define nodes with conflicts and constraints (advanced)</item>
/// <item><c>graphColoring { ... }</c> - Compose complete coloring problems</item>
/// </list>
/// 
/// <para><b>Example Usage:</b></para>
/// <code>
/// open FSharp.Azure.Quantum.GraphColoring
/// 
/// // Simple inline node definition (80% use case)
/// let problem = graphColoring {
///     node "R1" conflictsWith ["R2"; "R3"]
///     node "R2" conflictsWith ["R1"; "R4"]
///     node "R3" conflictsWith ["R1"; "R4"]
///     node "R4" conflictsWith ["R2"; "R3"]
///     
///     colors ["EAX"; "EBX"; "ECX"; "EDX"]
///     objective MinimizeColors
/// }
/// 
/// // Advanced node builder (20% use case)
/// let r1 = coloredNode {
///     id "R1"
///     conflictsWith ["R2"; "R3"]
///     fixedColor "EAX"
///     priority 10.0
/// }
/// 
/// let advancedProblem = graphColoring {
///     nodes [r1; r2; r3; r4]
///     colors ["EAX"; "EBX"; "ECX"; "EDX"]
///     objective MinimizeColors
/// }
/// 
/// // Solve
/// let! solution = solve problem
/// printfn "Used %d colors" solution.ColorsUsed
/// </code>
/// </remarks>
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
    }
    
    // ============================================================================
    // VALIDATION HELPERS
    // ============================================================================
    
    /// <summary>
    /// Validates a graph coloring problem specification.
    /// </summary>
    /// <param name="problem">The problem to validate</param>
    /// <returns>Result with unit on success, or error message on failure</returns>
    let validate (problem: GraphColoringProblem) : Result<unit, string> =
        // Check: At least one node
        if problem.Nodes.IsEmpty then
            Error "Graph coloring problem must have at least one node"
        // Check: At least one color
        elif problem.AvailableColors.IsEmpty then
            Error "Graph coloring problem must have at least one available color"
        // Check: All nodes have IDs
        elif problem.Nodes |> List.exists (fun n -> System.String.IsNullOrWhiteSpace(n.Id)) then
            Error "All nodes must have non-empty IDs"
        // Check: Node IDs are unique
        else
            let nodeIds = problem.Nodes |> List.map (fun n -> n.Id) |> Set.ofList
            if nodeIds.Count <> problem.Nodes.Length then
                Error "Node IDs must be unique"
            else
                // Check: Conflict references are valid
                let invalidConflicts = 
                    problem.Nodes
                    |> List.collect (fun n -> n.ConflictsWith)
                    |> List.filter (fun conflictId -> not (nodeIds.Contains conflictId))
                
                if not invalidConflicts.IsEmpty then
                    Error (sprintf "Invalid conflict references: %A" invalidConflicts)
                else
                    // Check: Fixed colors are in available colors
                    let availableColorSet = Set.ofList problem.AvailableColors
                    let invalidFixedColors =
                        problem.Nodes
                        |> List.choose (fun n -> n.FixedColor)
                        |> List.filter (fun color -> not (availableColorSet.Contains color))
                    
                    if not invalidFixedColors.IsEmpty then
                        Error (sprintf "Fixed colors not in available colors: %A" invalidFixedColors)
                    else
                        // Check: MaxColors constraint is feasible
                        match problem.MaxColors with
                        | Some maxColors when maxColors < 1 ->
                            Error "MaxColors must be at least 1"
                        | Some maxColors when maxColors > problem.AvailableColors.Length ->
                            Error (sprintf "MaxColors (%d) exceeds available colors (%d)" maxColors problem.AvailableColors.Length)
                        | _ ->
                            Ok ()
    
    /// <summary>
    /// Detects cycles in conflict graph using depth-first search.
    /// </summary>
    /// <param name="nodes">List of nodes with conflict relationships</param>
    /// <returns>Result with unit on success (no cycles/undirected), or cycle path on failure</returns>
    let detectCycles (nodes: ColoredNode list) : Result<unit, string list> =
        // Note: For graph coloring, conflicts are undirected edges
        // Cycles are normal (e.g., triangle conflict graph)
        // This is primarily for debugging/analysis, not validation
        
        let adjacency = 
            nodes
            |> List.map (fun n -> n.Id, Set.ofList n.ConflictsWith)
            |> Map.ofList
        
        let rec dfs visited path current =
            if Set.contains current visited then
                // Found a cycle
                let cycleStart = List.findIndex ((=) current) path
                Error (List.skip cycleStart path @ [current])
            else
                let visited' = Set.add current visited
                let neighbors = Map.tryFind current adjacency |> Option.defaultValue Set.empty
                
                // Try to find cycle from any neighbor
                neighbors
                |> Set.toList
                |> List.filter (fun neighbor -> not (List.isEmpty path) && neighbor <> (List.head path)) // Skip parent in undirected graph
                |> List.tryPick (fun neighbor ->
                    match dfs visited' (current :: path) neighbor with
                    | Error cycle -> Some (Error cycle)
                    | Ok () -> None
                )
                |> Option.defaultValue (Ok ())
        
        // For undirected graph, cycles are expected (not an error)
        // Just return Ok for now
        Ok ()
    
    // ============================================================================
    // COMPUTATION EXPRESSION BUILDERS - Colored Node Builder
    // ============================================================================
    
    /// <summary>
    /// Computation expression builder for defining colored nodes with advanced features.
    /// </summary>
    /// <remarks>
    /// <para><b>Available Operations:</b></para>
    /// <list type="table">
    /// <listheader><term>Operation</term><description>Description</description></listheader>
    /// <item><term><c>id "R1"</c></term><description>Set unique node identifier (required)</description></item>
    /// <item><term><c>conflictsWith ["R2"; "R3"]</c></term><description>Set list of conflicting nodes (required)</description></item>
    /// <item><term><c>fixedColor "EAX"</c></term><description>Pre-assign a specific color (optional)</description></item>
    /// <item><term><c>priority 10.0</c></term><description>Set priority for tie-breaking (higher = assign first, default 0.0)</description></item>
    /// <item><term><c>avoidColors ["EDX"]</c></term><description>Soft constraint to avoid certain colors (optional)</description></item>
    /// <item><term><c>property "key" value</c></term><description>Add custom metadata (optional)</description></item>
    /// </list>
    /// 
    /// <para><b>Example Usage:</b></para>
    /// <code>
    /// let node = coloredNode {
    ///     id "R1"
    ///     conflictsWith ["R2"; "R3"]
    ///     fixedColor "EAX"
    ///     priority 10.0
    ///     avoidColors ["EDX"]
    ///     property "spill_cost" 500.0
    /// }
    /// </code>
    /// </remarks>
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
        
        /// <summary>Set node unique identifier.</summary>
        /// <param name="id">Node ID (must be unique within problem)</param>
        [<CustomOperation("id")>]
        member _.Id(node: ColoredNode, id: string) : ColoredNode =
            { node with Id = id }
        
        /// <summary>Set list of conflicting nodes (cannot have same color).</summary>
        /// <param name="conflicts">List of node IDs that conflict with this node</param>
        [<CustomOperation("conflictsWith")>]
        member _.ConflictsWith(node: ColoredNode, conflicts: string list) : ColoredNode =
            { node with ConflictsWith = conflicts }
        
        /// <summary>Pre-assign a specific color to this node.</summary>
        /// <param name="color">Color to fix for this node</param>
        [<CustomOperation("fixedColor")>]
        member _.FixedColor(node: ColoredNode, color: string) : ColoredNode =
            { node with FixedColor = Some color }
        
        /// <summary>Set priority for tie-breaking when assigning colors.</summary>
        /// <param name="priority">Priority value (higher = assign first, default 0.0)</param>
        [<CustomOperation("priority")>]
        member _.Priority(node: ColoredNode, priority: float) : ColoredNode =
            { node with Priority = priority }
        
        /// <summary>Add colors to avoid if possible (soft constraint).</summary>
        /// <param name="colors">List of colors to avoid</param>
        [<CustomOperation("avoidColors")>]
        member _.AvoidColors(node: ColoredNode, colors: string list) : ColoredNode =
            { node with AvoidColors = colors }
        
        /// <summary>Add custom metadata property to this node.</summary>
        /// <param name="key">Property key</param>
        /// <param name="value">Property value</param>
        [<CustomOperation("property")>]
        member _.Property(node: ColoredNode, key: string, value: obj) : ColoredNode =
            { node with Properties = node.Properties |> Map.add key value }
    
    /// <summary>
    /// Global instance of the <c>coloredNode</c> computation expression builder.
    /// Use this for advanced node definition with full control over properties.
    /// </summary>
    /// <example>
    /// <code>
    /// let r1 = coloredNode {
    ///     id "R1"
    ///     conflictsWith ["R2"; "R3"]
    ///     fixedColor "EAX"
    ///     priority 10.0
    /// }
    /// </code>
    /// </example>
    let coloredNode = ColoredNodeBuilder()
    
    // ============================================================================
    // COMPUTATION EXPRESSION BUILDERS - Graph Coloring Problem Builder
    // ============================================================================
    
    /// <summary>
    /// Computation expression builder for defining graph coloring problems.
    /// Supports both inline node definition and pre-built node composition.
    /// </summary>
    /// <remarks>
    /// <para><b>Available Operations:</b></para>
    /// <list type="table">
    /// <listheader><term>Operation</term><description>Description</description></listheader>
    /// <item><term><c>node "R1" conflictsWith ["R2"]</c></term><description>Inline node definition (simple, 80% use case)</description></item>
    /// <item><term><c>nodes [r1; r2; r3]</c></term><description>Add pre-built nodes (advanced, 20% use case)</description></item>
    /// <item><term><c>colors ["EAX"; "EBX"]</c></term><description>Set available colors (required)</description></item>
    /// <item><term><c>objective MinimizeColors</c></term><description>Set optimization objective (default: MinimizeColors)</description></item>
    /// <item><term><c>maxColors 3</c></term><description>Set maximum colors to use (optional constraint)</description></item>
    /// <item><term><c>conflictPenalty 100.0</c></term><description>Set penalty weight for conflicts (default: 1.0)</description></item>
    /// </list>
    /// 
    /// <para><b>Example Usage:</b></para>
    /// <code>
    /// // Simple inline nodes
    /// let problem = graphColoring {
    ///     node "R1" conflictsWith ["R2"; "R3"]
    ///     node "R2" conflictsWith ["R1"; "R4"]
    ///     node "R3" conflictsWith ["R1"; "R4"]
    ///     node "R4" conflictsWith ["R2"; "R3"]
    ///     colors ["EAX"; "EBX"; "ECX"]
    ///     objective MinimizeColors
    /// }
    /// 
    /// // Advanced with pre-built nodes
    /// let r1 = coloredNode { id "R1"; conflictsWith ["R2"]; priority 10.0 }
    /// let problem2 = graphColoring {
    ///     nodes [r1; r2; r3]
    ///     colors ["EAX"; "EBX"]
    ///     maxColors 2
    /// }
    /// </code>
    /// </remarks>
    type GraphColoringBuilder() =
        
        member _.Yield(_) : GraphColoringProblem =
            {
                Nodes = []
                AvailableColors = []
                Objective = MinimizeColors
                MaxColors = None
                ConflictPenalty = 1.0
            }
        
        // ========================================================================
        // ADVANCED BUILDER METHODS - Enable Composition & Lazy Evaluation
        // ========================================================================
        
        /// <summary>
        /// Wraps operations in functions for lazy evaluation.
        /// Required for proper computation expression composition.
        /// </summary>
        /// <param name="f">Function to delay</param>
        member _.Delay(f: unit -> GraphColoringProblem) : unit -> GraphColoringProblem = f
        
        /// <summary>
        /// Final transformation and validation step.
        /// Called automatically by F# compiler at the end of computation expression.
        /// </summary>
        /// <param name="f">Delayed problem builder function</param>
        /// <returns>Validated problem</returns>
        /// <exception cref="System.Exception">Thrown if validation fails</exception>
        member _.Run(f: unit -> GraphColoringProblem) : GraphColoringProblem =
            let problem = f()
            match validate problem with
            | Error msg -> failwith msg
            | Ok () -> problem
        
        /// <summary>
        /// Combines two operations, merging their state.
        /// Enables chaining multiple operations in sequence.
        /// </summary>
        /// <param name="first">First operation result</param>
        /// <param name="second">Second operation (delayed)</param>
        member _.Combine(first: GraphColoringProblem, second: unit -> GraphColoringProblem) : GraphColoringProblem =
            let second' = second()
            {
                // Merge nodes (later additions accumulate)
                Nodes = first.Nodes @ second'.Nodes
                // Later specification wins for these
                AvailableColors = if second'.AvailableColors.IsEmpty then first.AvailableColors else second'.AvailableColors
                Objective = second'.Objective  // Later overrides
                MaxColors = match second'.MaxColors with | Some _ -> second'.MaxColors | None -> first.MaxColors
                ConflictPenalty = if second'.ConflictPenalty = 1.0 then first.ConflictPenalty else second'.ConflictPenalty
            }
        
        /// <summary>
        /// Empty/no-op value for conditional branches.
        /// Required for `if` expressions without `else`.
        /// </summary>
        member this.Zero() : GraphColoringProblem = this.Yield(())
        
        /// <summary>
        /// Enables iteration over sequences within computation expression.
        /// Accumulates state across all iterations.
        /// </summary>
        /// <param name="sequence">Sequence to iterate over</param>
        /// <param name="body">Body function to apply to each element</param>
        member this.For(sequence: seq<'T>, body: 'T -> GraphColoringProblem) : GraphColoringProblem =
            sequence
            |> Seq.fold (fun state item ->
                this.Combine(state, fun () -> body item)
            ) (this.Zero())
        
        // ========================================================================
        // CUSTOM OPERATIONS - Domain-Specific API
        // ========================================================================
        
        /// <summary>Inline node definition with conflicts (simple syntax).</summary>
        /// <param name="id">Node ID</param>
        /// <param name="conflicts">List of conflicting node IDs</param>
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
        
        /// <summary>Add multiple pre-built nodes at once.</summary>
        /// <param name="nodeList">List of ColoredNode instances</param>
        [<CustomOperation("nodes")>]
        member _.Nodes(problem: GraphColoringProblem, nodeList: ColoredNode list) : GraphColoringProblem =
            { problem with Nodes = problem.Nodes @ nodeList }
        
        /// <summary>Set available colors for assignment.</summary>
        /// <param name="colorList">List of color identifiers</param>
        [<CustomOperation("colors")>]
        member _.Colors(problem: GraphColoringProblem, colorList: string list) : GraphColoringProblem =
            { problem with AvailableColors = colorList }
        
        /// <summary>Set optimization objective.</summary>
        /// <param name="obj">Objective type (MinimizeColors, MinimizeConflicts, BalanceColors)</param>
        [<CustomOperation("objective")>]
        member _.Objective(problem: GraphColoringProblem, obj: ColoringObjective) : GraphColoringProblem =
            { problem with Objective = obj }
        
        /// <summary>Set maximum number of colors to use (hard constraint).</summary>
        /// <param name="max">Maximum colors</param>
        [<CustomOperation("maxColors")>]
        member _.MaxColors(problem: GraphColoringProblem, max: int) : GraphColoringProblem =
            { problem with MaxColors = Some max }
        
        /// <summary>Set penalty weight for conflicts (used with MinimizeConflicts objective).</summary>
        /// <param name="penalty">Penalty weight (default 1.0)</param>
        [<CustomOperation("conflictPenalty")>]
        member _.ConflictPenalty(problem: GraphColoringProblem, penalty: float) : GraphColoringProblem =
            { problem with ConflictPenalty = penalty }
    
    /// <summary>
    /// Global instance of the <c>graphColoring</c> computation expression builder.
    /// Use this to compose complete graph coloring problems.
    /// </summary>
    /// <example>
    /// <code>
    /// let problem = graphColoring {
    ///     node "R1" conflictsWith ["R2"; "R3"]
    ///     node "R2" conflictsWith ["R1"; "R4"]
    ///     colors ["EAX"; "EBX"; "ECX"; "EDX"]
    ///     objective MinimizeColors
    /// }
    /// </code>
    /// </example>
    let graphColoring = GraphColoringBuilder()
    
    // ============================================================================
    // HELPER FUNCTIONS - Quick Node Creation
    // ============================================================================
    
    /// <summary>
    /// Quick helper to create a simple node with ID and conflicts.
    /// For use outside computation expressions.
    /// </summary>
    /// <param name="id">Node identifier</param>
    /// <param name="conflicts">List of conflicting node IDs</param>
    /// <returns>ColoredNode with default properties</returns>
    /// <example>
    /// <code>
    /// let r1 = node "R1" ["R2"; "R3"]
    /// let r2 = node "R2" ["R1"; "R4"]
    /// </code>
    /// </example>
    let node id conflicts : ColoredNode =
        {
            Id = id
            ConflictsWith = conflicts
            FixedColor = None
            Priority = 0.0
            AvoidColors = []
            Properties = Map.empty
        }
    
    // ============================================================================
    // INTEGRATION WITH TKT-90 - Generic Graph Optimization
    // ============================================================================
    
    open GraphOptimization
    
    /// <summary>
    /// Converts graph coloring problem to generic graph optimization problem (TKT-90).
    /// </summary>
    /// <param name="problem">Graph coloring problem</param>
    /// <returns>Generic graph problem for TKT-90 solver</returns>
    let toGraphProblem (problem: GraphColoringProblem) : GraphOptimizationProblem<int, unit> =
        // Map color strings to indices
        let colorToIndex = 
            problem.AvailableColors 
            |> List.mapi (fun i color -> color, i)
            |> Map.ofList
        
        // Create nodes with color indices as values
        let graphNodes =
            problem.Nodes
            |> List.map (fun n ->
                let value = 
                    match n.FixedColor with
                    | Some color -> colorToIndex.[color]
                    | None -> 0  // Default to first color
                
                let props =
                    n.Properties
                    |> Map.add "priority" (box n.Priority)
                    |> Map.add "fixed" (box n.FixedColor.IsSome)
                
                nodeWithProps n.Id value (Map.toList props)
            )
        
        // Create edges from conflicts (undirected)
        let graphEdges =
            problem.Nodes
            |> List.collect (fun n ->
                n.ConflictsWith
                |> List.map (fun conflictId ->
                    edge n.Id conflictId 1.0  // Weight 1.0 for conflicts
                )
            )
            |> List.distinct  // Remove duplicates from symmetric conflicts
        
        // Map objective (use MinimizeColors from GraphObjective)
        let graphObjective = MinimizeColors
        
        // Build using TKT-90 fluent API
        GraphOptimizationBuilder()
            .Nodes(graphNodes)
            .Edges(graphEdges)
            .AddConstraint(NoAdjacentEqual)
            .Objective(graphObjective)
            .NumColors(problem.AvailableColors.Length)
            .Build()
    
    /// <summary>
    /// Converts TKT-90 solution back to graph coloring solution.
    /// </summary>
    /// <param name="problem">Original graph coloring problem</param>
    /// <param name="graphSolution">Solution from TKT-90 solver</param>
    /// <returns>Graph coloring solution</returns>
    let fromGraphSolution (problem: GraphColoringProblem) (graphSolution: GraphOptimizationSolution<int, unit>) : ColoringSolution =
        // Map indices back to color strings
        let indexToColor = 
            problem.AvailableColors 
            |> List.mapi (fun i color -> i, color)
            |> Map.ofList
        
        // Build assignments map (handle Option type)
        let assignments =
            match graphSolution.NodeAssignments with
            | Some nodeMap ->
                nodeMap
                |> Map.toList
                |> List.map (fun (nodeId, colorIndex) -> nodeId, indexToColor.[colorIndex])
                |> Map.ofList
            | None -> Map.empty
        
        // Count distinct colors used
        let colorsUsed = 
            assignments 
            |> Map.toList 
            |> List.map snd 
            |> List.distinct 
            |> List.length
        
        // Count conflicts
        let conflictCount =
            problem.Nodes
            |> List.sumBy (fun n ->
                let nodeColor = Map.tryFind n.Id assignments
                match nodeColor with
                | Some color ->
                    n.ConflictsWith
                    |> List.filter (fun conflictId ->
                        Map.tryFind conflictId assignments
                        |> Option.map ((=) color)
                        |> Option.defaultValue false
                    )
                    |> List.length
                | None -> 0
            )
            |> fun total -> total / 2  // Each conflict counted twice
        
        // Color distribution
        let colorDistribution =
            assignments
            |> Map.toList
            |> List.map snd
            |> List.groupBy id
            |> List.map (fun (color, group) -> color, List.length group)
            |> Map.ofList
        
        {
            Assignments = assignments
            ColorsUsed = colorsUsed
            ConflictCount = conflictCount
            IsValid = conflictCount = 0
            ColorDistribution = colorDistribution
            Cost = graphSolution.ObjectiveValue
        }
    
    /// <summary>
    /// Solves a graph coloring problem using TKT-90 generic graph optimization.
    /// </summary>
    /// <param name="problem">Graph coloring problem to solve</param>
    /// <returns>Coloring solution</returns>
    /// <example>
    /// <code>
    /// let problem = graphColoring {
    ///     node "R1" conflictsWith ["R2"; "R3"]
    ///     node "R2" conflictsWith ["R1"; "R4"]
    ///     colors ["EAX"; "EBX"; "ECX"]
    /// }
    /// 
    /// let solution = solve problem
    /// printfn "Used %d colors" solution.ColorsUsed
    /// </code>
    /// </example>
    let solve (problem: GraphColoringProblem) : ColoringSolution =
        // Convert to generic graph problem
        let graphProblem = toGraphProblem problem
        
        // Solve using TKT-90 classical solver
        let graphSolution = solveClassical graphProblem
        
        // Convert back to graph coloring solution
        fromGraphSolution problem graphSolution
    
    // ============================================================================
    // VISUALIZATION AND EXPORT
    // ============================================================================
    
    /// <summary>
    /// Exports graph coloring solution to DOT format for visualization.
    /// </summary>
    /// <param name="problem">Original problem</param>
    /// <param name="solution">Solution to visualize</param>
    /// <returns>DOT format string</returns>
    /// <example>
    /// <code>
    /// let dot = exportToDot problem solution
    /// System.IO.File.WriteAllText("coloring.dot", dot)
    /// // Then: dot -Tpng coloring.dot -o coloring.png
    /// </code>
    /// </example>
    let exportToDot (problem: GraphColoringProblem) (solution: ColoringSolution) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine("graph G {") |> ignore
        sb.AppendLine("  node [style=filled];") |> ignore
        
        // Add nodes with colors
        for node in problem.Nodes do
            let color = Map.find node.Id solution.Assignments
            sb.AppendLine(sprintf "  %s [label=\"%s\\n%s\" fillcolor=\"%s\"];" node.Id node.Id color color) |> ignore
        
        // Add edges (conflicts)
        let addedEdges = System.Collections.Generic.HashSet<string * string>()
        for node in problem.Nodes do
            for conflictId in node.ConflictsWith do
                let edge1 = (node.Id, conflictId)
                let edge2 = (conflictId, node.Id)
                if not (addedEdges.Contains edge1 || addedEdges.Contains edge2) then
                    sb.AppendLine(sprintf "  %s -- %s;" node.Id conflictId) |> ignore
                    addedEdges.Add(edge1) |> ignore
        
        sb.AppendLine("}") |> ignore
        sb.ToString()
    
    /// <summary>
    /// Exports solution statistics to human-readable format.
    /// </summary>
    /// <param name="solution">Solution to describe</param>
    /// <returns>Formatted statistics string</returns>
    let describeSolution (solution: ColoringSolution) : string =
        let sb = System.Text.StringBuilder()
        sb.AppendLine("=== Graph Coloring Solution ===") |> ignore
        sb.AppendLine(sprintf "Status: %s" (if solution.IsValid then "✓ Valid" else "✗ Invalid")) |> ignore
        sb.AppendLine(sprintf "Colors Used: %d" solution.ColorsUsed) |> ignore
        sb.AppendLine(sprintf "Conflicts: %d" solution.ConflictCount) |> ignore
        sb.AppendLine(sprintf "Cost: %.2f" solution.Cost) |> ignore
        sb.AppendLine("") |> ignore
        
        sb.AppendLine("Color Distribution:") |> ignore
        for (color, count) in Map.toList solution.ColorDistribution do
            sb.AppendLine(sprintf "  %s: %d nodes" color count) |> ignore
        
        sb.AppendLine("") |> ignore
        sb.AppendLine("Assignments:") |> ignore
        for (nodeId, color) in Map.toList solution.Assignments do
            sb.AppendLine(sprintf "  %s → %s" nodeId color) |> ignore
        
        sb.ToString()
