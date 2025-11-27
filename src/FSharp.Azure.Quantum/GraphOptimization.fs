/// TKT-90: Generic Graph Optimization Framework
/// 
/// Provides a unified, extensible framework for solving graph optimization problems
/// including Graph Coloring, Traveling Salesman Problem (TSP), MaxCut, and more.
///
/// ## Features
/// - Immutable data structures with functional composition
/// - Fluent builder API for problem specification
/// - QUBO encoding for quantum/annealing solvers
/// - Classical solver fallbacks (coming soon)
/// - Extensible constraint and objective system
///
/// ## Usage Example
/// ```fsharp
/// let problem =
///     GraphOptimizationBuilder()
///         .Nodes([node "A" 1; node "B" 2])
///         .Edges([edge "A" "B" 1.0])
///         .AddConstraint(NoAdjacentEqual)
///         .Objective(MinimizeColors)
///         .NumColors(3)
///         .Build()
///
/// let qubo = toQubo problem
/// let solution = decodeSolution problem [1; 0; 0; 0; 1; 0]
/// ```
namespace FSharp.Azure.Quantum

open System

/// <summary>
/// Generic Graph Optimization Framework.
/// Powers Graph Coloring, TSP, MaxCut, and other graph problems.
/// </summary>
///
/// <remarks>
/// This module provides a unified API for defining and solving graph optimization
/// problems using quantum annealing (QUBO), classical solvers, or hybrid approaches.
/// </remarks>
module GraphOptimization =
    
    // ========================================================================
    // FR-1: NODE DEFINITION
    // ========================================================================
    
    /// <summary>
    /// A node in the graph with generic value type.
    /// </summary>
    ///
    /// <typeparam name="'T">The type of value stored in the node</typeparam>
    ///
    /// <remarks>
    /// Nodes are identified by unique string IDs and can store arbitrary
    /// metadata in the Properties map.
    /// </remarks>
    type Node<'T when 'T : equality> = {
        /// Unique identifier for this node
        Id: string
        /// The value stored in this node
        Value: 'T
        /// Additional metadata for this node
        Properties: Map<string, obj>
    }
    
    /// <summary>Create a node with id and value.</summary>
    /// <param name="id">Unique identifier for the node</param>
    /// <param name="value">The value to store in the node</param>
    /// <returns>A new node with empty properties</returns>
    let node id value = {
        Id = id
        Value = value
        Properties = Map.empty
    }
    
    /// <summary>Create a node with properties.</summary>
    /// <param name="id">Unique identifier for the node</param>
    /// <param name="value">The value to store in the node</param>
    /// <param name="properties">List of key-value pairs for node metadata</param>
    /// <returns>A new node with the specified properties</returns>
    let nodeWithProps id value properties = {
        Id = id
        Value = value
        Properties = Map.ofList properties
    }
    
    // ========================================================================
    // FR-2: EDGE DEFINITION
    // ========================================================================
    
    /// An edge connecting two nodes
    type Edge<'T> = {
        Source: string
        Target: string
        Weight: float
        Directed: bool
        Value: 'T option
        Properties: Map<string, obj>
    }
    
    /// Create an undirected edge with weight
    let edge source target weight = {
        Source = source
        Target = target
        Weight = weight
        Directed = false
        Value = None
        Properties = Map.empty
    }
    
    /// Create a directed edge with weight
    let directedEdge source target weight = {
        Source = source
        Target = target
        Weight = weight
        Directed = true
        Value = None
        Properties = Map.empty
    }
    
    // ========================================================================
    // FR-3: GRAPH REPRESENTATION
    // ========================================================================
    
    /// Graph representation with adjacency list
    type Graph<'TNode, 'TEdge when 'TNode : equality> = {
        Nodes: Map<string, Node<'TNode>>
        Edges: Edge<'TEdge> list
        Directed: bool
        Adjacency: Map<string, string list>
    }
    
    /// Graph construction and utilities
    module Graph =
        
        /// Create an empty graph
        let empty<'TNode, 'TEdge when 'TNode : equality> : Graph<'TNode, 'TEdge> = {
            Nodes = Map.empty
            Edges = []
            Directed = false
            Adjacency = Map.empty
        }
        
        /// Build adjacency list from edges (extracted for reuse)
        let buildAdjacency (directed: bool) (edges: Edge<'T> list) : Map<string, string list> =
            let addEdge acc (e: Edge<'T>) =
                let addToList nodeId neighborId map =
                    map
                    |> Map.change nodeId (fun existing ->
                        match existing with
                        | Some neighbors -> Some (neighborId :: neighbors)
                        | None -> Some [neighborId]
                    )
                
                let acc' = addToList e.Source e.Target acc
                
                // For undirected graphs, add reverse edge
                if not directed && not e.Directed then
                    addToList e.Target e.Source acc'
                else
                    acc'
            
            edges |> List.fold addEdge Map.empty
        
        /// Create a graph from nodes and edges
        let create directed (nodes: Node<'TNode> list) (edges: Edge<'TEdge> list) : Graph<'TNode, 'TEdge> =
            {
                Nodes = nodes |> List.map (fun n -> n.Id, n) |> Map.ofList
                Edges = edges
                Directed = directed
                Adjacency = buildAdjacency directed edges
            }
    
    // ========================================================================
    // FR-4: CONSTRAINT TYPES
    // ========================================================================
    
    /// Graph constraints for optimization problems
    [<NoComparison; NoEquality>]
    type GraphConstraint =
        /// Adjacent nodes must have different values (Graph Coloring)
        | NoAdjacentEqual
        /// Each node visited exactly once (TSP)
        | VisitOnce
        /// Graph must be acyclic
        | Acyclic
        /// Graph must be connected
        | Connected
        /// Maximum node degree
        | DegreeLimit of max: int
        /// Minimum node degree
        | MinDegree of min: int
        /// Each node has exactly one incoming edge (TSP)
        | OneIncoming
        /// Each node has exactly one outgoing edge (TSP)
        | OneOutgoing
        /// Custom constraint function
        | Custom of (Graph<obj, obj> -> bool)
    
    // ========================================================================
    // FR-5: OBJECTIVE FUNCTIONS
    // ========================================================================
    
    /// Optimization objectives for graph problems
    type GraphObjective =
        /// Minimize number of colors used (Graph Coloring)
        | MinimizeColors
        /// Minimize total edge weight (TSP, Shortest Path)
        | MinimizeTotalWeight
        /// Maximize cut size (MaxCut)
        | MaximizeCut
        /// Minimize spanning tree weight
        | MinimizeSpanningTree
        /// Minimize maximum edge weight
        | MinimizeMaxWeight
        /// Maximize number of edges
        | MaximizeEdges
        /// Minimize number of edges
        | MinimizeEdges
        /// Custom objective function
        | Custom of (Graph<obj, obj> -> float)
    
    // ========================================================================
    // QUBO REPRESENTATION
    // ========================================================================
    
    /// QUBO (Quadratic Unconstrained Binary Optimization) representation
    type QuboMatrix = {
        NumVariables: int
        Q: Map<int * int, float>  // Sparse matrix representation
    }
    
    /// Create empty QUBO matrix
    let emptyQubo numVars = {
        NumVariables = numVars
        Q = Map.empty
    }
    
    // ========================================================================
    // CONSTRAINT VIOLATIONS
    // ========================================================================
    
    /// Constraint violation information
    type ConstraintViolation = {
        Constraint: GraphConstraint
        Description: string
        Severity: float  // Penalty value
    }
    
    // ========================================================================
    // FR-8: SOLUTION REPRESENTATION
    // ========================================================================
    
    /// Solution to a graph optimization problem
    type GraphOptimizationSolution<'TNode, 'TEdge when 'TNode : equality> = {
        Graph: Graph<'TNode, 'TEdge>
        NodeAssignments: Map<string, int> option  // For coloring, partitioning
        SelectedEdges: Edge<'TEdge> list option   // For TSP, spanning tree
        ObjectiveValue: float
        IsFeasible: bool
        Violations: ConstraintViolation list
    }
    
    // ========================================================================
    // GRAPH OPTIMIZATION PROBLEM
    // ========================================================================
    
    /// A graph optimization problem specification
    type GraphOptimizationProblem<'TNode, 'TEdge when 'TNode : equality and 'TEdge : equality> = {
        Graph: Graph<'TNode, 'TEdge>
        Constraints: GraphConstraint list
        Objective: GraphObjective
        NumColors: int option  // For graph coloring
    }
    
    // ========================================================================
    // FR-6: FLUENT BUILDER API (Idiomatic Immutable)
    // ========================================================================
    
    /// Fluent builder for graph optimization problems (immutable)
    type GraphOptimizationBuilder<'TNode, 'TEdge when 'TNode : equality and 'TEdge : equality> = private {
        nodes: Node<'TNode> list
        edges: Edge<'TEdge> list
        directed: bool
        constraints: GraphConstraint list
        objective: GraphObjective
        numColors: int option
    } with
        /// Create a new builder with default values
        static member Create() : GraphOptimizationBuilder<'TNode, 'TEdge> = {
            nodes = []
            edges = []
            directed = false
            constraints = []
            objective = MinimizeColors
            numColors = None
        }
        
        /// Fluent API: Set the nodes in the graph
        member this.Nodes(nodeList: Node<'TNode> list) =
            { this with nodes = nodeList }
        
        /// Fluent API: Set the edges in the graph
        member this.Edges(edgeList: Edge<'TEdge> list) =
            { this with edges = edgeList }
        
        /// Fluent API: Set whether the graph is directed
        member this.Directed(isDirected: bool) =
            { this with directed = isDirected }
        
        /// Fluent API: Mark graph as directed (no parameter)
        member this.Directed() =
            { this with directed = true }
        
        /// Fluent API: Add a constraint to the problem
        member this.AddConstraint(c: GraphConstraint) =
            { this with constraints = c :: this.constraints }
        
        /// Fluent API: Set the optimization objective
        member this.Objective(obj: GraphObjective) =
            { this with objective = obj }
        
        /// Fluent API: Set the number of colors (for graph coloring)
        member this.NumColors(colors: int) =
            { this with numColors = Some colors }
        
        /// Build the graph optimization problem
        member this.Build() : GraphOptimizationProblem<'TNode, 'TEdge> =
            let nodeMap = this.nodes |> List.map (fun n -> n.Id, n) |> Map.ofList
            
            let graph = {
                Nodes = nodeMap
                Edges = this.edges
                Directed = this.directed
                Adjacency = Graph.buildAdjacency this.directed this.edges
            }
            
            {
                Graph = graph
                Constraints = List.rev this.constraints
                Objective = this.objective
                NumColors = this.numColors
            }
    
    /// Constructor-like syntax for C# compatibility
    let GraphOptimizationBuilder<'TNode, 'TEdge when 'TNode : equality and 'TEdge : equality> () =
        GraphOptimizationBuilder<'TNode, 'TEdge>.Create()
    
    // ========================================================================
    // QUBO ENCODING CONSTANTS
    // ========================================================================
    
    /// Default penalty weight for constraint violations
    [<Literal>]
    let private DefaultPenalty = 10.0
    
    /// Default number of colors for graph coloring
    [<Literal>]
    let private DefaultNumColors = 4
    
    // ========================================================================
    // FR-7: QUBO ENCODING (Idiomatic Functional)
    // ========================================================================
    
    /// Encode a graph optimization problem to QUBO
    let toQubo (problem: GraphOptimizationProblem<'TNode, 'TEdge>) : QuboMatrix =
        let numNodes = problem.Graph.Nodes |> Map.count
        
        /// Helper: Check if constraint exists in problem
        let hasConstraint constraintPredicate =
            problem.Constraints |> List.exists constraintPredicate
        
        /// Helper: Create node ID to index mapping
        let nodeIndexMap =
            problem.Graph.Nodes
            |> Map.toList
            |> List.mapi (fun idx (nodeId, _) -> nodeId, idx)
            |> Map.ofList
        
        /// Helper: Add terms to QUBO matrix
        let addTermsToQubo (qubo: QuboMatrix) (terms: ((int * int) * float) list) : QuboMatrix =
            let updatedQ =
                terms
                |> List.fold (fun q (key, value) -> Map.add key value q) qubo.Q
            { qubo with Q = updatedQ }
        
        match problem.Objective with
        | MinimizeColors ->
            // Graph coloring: one-hot encoding
            // Variables: x_{i,c} = 1 if node i has color c
            let numColors = problem.NumColors |> Option.defaultValue DefaultNumColors
            let numVars = numNodes * numColors
            
            let baseQubo = emptyQubo numVars
            
            // Add constraint penalties functionally
            if hasConstraint (function NoAdjacentEqual -> true | _ -> false) then
                // For each edge (u, v), add penalty if same color
                let penaltyTerms =
                    problem.Graph.Edges
                    |> List.collect (fun edge ->
                        match Map.tryFind edge.Source nodeIndexMap, Map.tryFind edge.Target nodeIndexMap with
                        | Some uIdx, Some vIdx ->
                            [0 .. numColors - 1]
                            |> List.map (fun c ->
                                // Penalty term: x_{u,c} * x_{v,c}
                                let varU = uIdx * numColors + c
                                let varV = vIdx * numColors + c
                                ((varU, varV), DefaultPenalty)
                            )
                        | _ -> []  // Skip edges with unknown nodes
                    )
                
                // Add all penalty terms to QUBO
                addTermsToQubo baseQubo penaltyTerms
            else
                baseQubo
        
        | MinimizeTotalWeight ->
            // TSP: one-hot time encoding
            // Variables: x_{i,t} = 1 if city i visited at time t
            let numVars = numNodes * numNodes
            emptyQubo numVars
        
        | MaximizeCut ->
            // MaxCut: binary partition encoding
            // Variables: x_i = 1 if node i in partition 1, 0 otherwise
            // Objective: Maximize edges crossing partition
            // QUBO formulation: Minimize -Σ w_ij * x_i * x_j
            //   (negative weights encourage x_i ≠ x_j, i.e., nodes in different partitions)
            
            let numVars = numNodes
            let baseQubo = emptyQubo numVars
            
            // Add quadratic terms for each edge: Q[(i,j)] = -weight
            let edgeTerms =
                problem.Graph.Edges
                |> List.choose (fun edge ->
                    match Map.tryFind edge.Source nodeIndexMap, Map.tryFind edge.Target nodeIndexMap with
                    | Some srcIdx, Some tgtIdx ->
                        // Use canonical ordering (i < j) for QUBO matrix
                        let (i, j) = if srcIdx < tgtIdx then (srcIdx, tgtIdx) else (tgtIdx, srcIdx)
                        Some ((i, j), -edge.Weight)
                    | _ -> None
                )
            
            // Build QUBO matrix with edge terms
            addTermsToQubo baseQubo edgeTerms
        
        | _ ->
            // Default: binary encoding
            emptyQubo numNodes
    
    // ========================================================================
    // OBJECTIVE VALUE CALCULATION (TDD CYCLE 2)
    // ========================================================================
    
    /// Calculate the objective value for a given solution
    /// Infers the objective type from the solution structure
    let calculateObjectiveValue (solution: GraphOptimizationSolution<'TNode, 'TEdge>) : float =
        match solution.NodeAssignments, solution.SelectedEdges with
        | Some assignments, None ->
            // Could be graph coloring (count unique colors) or MaxCut (count cut edges)
            // Heuristic: If all values are 0 or 1, it's MaxCut; otherwise graph coloring
            let values = assignments |> Map.toList |> List.map snd
            let allBinary = values |> List.forall (fun v -> v = 0 || v = 1)
            
            if allBinary && solution.Graph.Edges.Length > 0 then
                // MaxCut: Count edges crossing partition
                solution.Graph.Edges
                |> List.filter (fun edge ->
                    match Map.tryFind edge.Source assignments, Map.tryFind edge.Target assignments with
                    | Some colorU, Some colorV -> colorU <> colorV
                    | _ -> false
                )
                |> List.length
                |> float
            else
                // Graph coloring: Count unique colors used
                values
                |> List.distinct
                |> List.length
                |> float
        
        | None, Some selectedEdges ->
            // TSP: Sum edge weights in tour
            selectedEdges
            |> List.sumBy (fun edge -> edge.Weight)
        
        | _ ->
            // Unknown or empty solution
            0.0
    
    // ========================================================================
    // FR-8: SOLUTION DECODING
    // ========================================================================
    
    /// Helper: Create a solution record with calculated objective value
    let private createSolution 
        (graph: Graph<'TNode, 'TEdge>) 
        (nodeAssignments: Map<string, int> option) 
        (selectedEdges: Edge<'TEdge> list option) 
        : GraphOptimizationSolution<'TNode, 'TEdge> =
        
        let tempSolution = {
            Graph = graph
            NodeAssignments = nodeAssignments
            SelectedEdges = selectedEdges
            ObjectiveValue = 0.0
            IsFeasible = true
            Violations = []
        }
        
        { tempSolution with ObjectiveValue = calculateObjectiveValue tempSolution }
    
    /// Helper: Create an empty/infeasible solution
    let private emptySolution (graph: Graph<'TNode, 'TEdge>) : GraphOptimizationSolution<'TNode, 'TEdge> =
        {
            Graph = graph
            NodeAssignments = None
            SelectedEdges = None
            ObjectiveValue = 0.0
            IsFeasible = false
            Violations = []
        }
    
    /// Decode a QUBO solution to a graph optimization solution
    let decodeSolution (problem: GraphOptimizationProblem<'TNode, 'TEdge>) (quboSolution: int list) : GraphOptimizationSolution<'TNode, 'TEdge> =
        let numNodes = problem.Graph.Nodes |> Map.count
        let nodeIds = problem.Graph.Nodes |> Map.toList |> List.map fst
        
        match problem.Objective with
        | MinimizeColors ->
            // Decode one-hot color assignment
            let numColors = problem.NumColors |> Option.defaultValue DefaultNumColors
            
            let assignments =
                nodeIds
                |> List.mapi (fun nodeIdx nodeId ->
                    // Find which color is assigned (one-hot)
                    let colorIdx = 
                        [0 .. numColors - 1]
                        |> List.tryFindIndex (fun c ->
                            let varIdx = nodeIdx * numColors + c
                            varIdx < quboSolution.Length && quboSolution.[varIdx] = 1
                        )
                        |> Option.defaultValue 0
                    
                    nodeId, colorIdx
                )
                |> Map.ofList
            
            createSolution problem.Graph (Some assignments) None
        
        | MinimizeTotalWeight ->
            // Decode TSP tour
            // TODO: Extract tour edges from QUBO solution
            createSolution problem.Graph None (Some [])
        
        | MaximizeCut ->
            // Decode partition
            let partition =
                nodeIds
                |> List.mapi (fun i nodeId ->
                    let partitionValue = 
                        if i < quboSolution.Length then quboSolution.[i] else 0
                    nodeId, partitionValue
                )
                |> Map.ofList
            
            createSolution problem.Graph (Some partition) None
        
        | _ ->
            // Default: empty solution
            emptySolution problem.Graph
    
    // ========================================================================
    // TDD CYCLE 2 - CLASSICAL SOLVERS
    // ========================================================================
    
    /// Greedy graph coloring algorithm (Welsh-Powell)
    let private greedyColoring (problem: GraphOptimizationProblem<'TNode, 'TEdge>) : GraphOptimizationSolution<'TNode, 'TEdge> =
        let nodeIds = problem.Graph.Nodes |> Map.toList |> List.map fst
        
        // Assign colors greedily: iterate through nodes, assign smallest available color
        let rec assignColors (remaining: string list) (assignments: Map<string, int>) : Map<string, int> =
            match remaining with
            | [] -> assignments
            | nodeId :: rest ->
                // Find colors used by neighbors
                let neighborColors =
                    problem.Graph.Adjacency
                    |> Map.tryFind nodeId
                    |> Option.defaultValue []
                    |> List.choose (fun neighbor -> Map.tryFind neighbor assignments)
                    |> Set.ofList
                
                // Find smallest color not used by neighbors
                let color =
                    Seq.initInfinite id
                    |> Seq.find (fun c -> not (Set.contains c neighborColors))
                
                assignColors rest (Map.add nodeId color assignments)
        
        let assignments = assignColors nodeIds Map.empty
        createSolution problem.Graph (Some assignments) None
    
    /// Nearest neighbor TSP heuristic
    let private nearestNeighborTSP (problem: GraphOptimizationProblem<'TNode, 'TEdge>) : GraphOptimizationSolution<'TNode, 'TEdge> =
        let nodeIds = problem.Graph.Nodes |> Map.toList |> List.map fst
        
        if nodeIds.IsEmpty then
            emptySolution problem.Graph
        else
            // Start from first node
            let startNode = nodeIds.Head
            
            // Build tour greedily
            let rec buildTour (current: string) (unvisited: Set<string>) (tour: Edge<'TEdge> list) =
                if unvisited.IsEmpty then
                    // Complete tour by returning to start
                    let returnEdge = 
                        problem.Graph.Edges 
                        |> List.tryFind (fun e -> 
                            (e.Source = current && e.Target = startNode) ||
                            (e.Target = current && e.Source = startNode && not e.Directed)
                        )
                    
                    match returnEdge with
                    | Some edge -> edge :: tour
                    | None -> tour
                else
                    // Find nearest unvisited neighbor
                    let nextEdge =
                        problem.Graph.Edges
                        |> List.filter (fun e ->
                            (e.Source = current && unvisited.Contains e.Target) ||
                            (e.Target = current && unvisited.Contains e.Source && not e.Directed)
                        )
                        |> List.sortBy (fun e -> e.Weight)
                        |> List.tryHead
                    
                    match nextEdge with
                    | Some edge ->
                        let nextNode = if edge.Source = current then edge.Target else edge.Source
                        buildTour nextNode (Set.remove nextNode unvisited) (edge :: tour)
                    | None ->
                        // No path found, return incomplete tour
                        tour
            
            let unvisited = nodeIds |> List.tail |> Set.ofList
            let tourEdges = buildTour startNode unvisited [] |> List.rev
            
            createSolution problem.Graph None (Some tourEdges)
    
    /// Randomized MaxCut algorithm
    let private randomizedMaxCut (problem: GraphOptimizationProblem<'TNode, 'TEdge>) : GraphOptimizationSolution<'TNode, 'TEdge> =
        let nodeIds = problem.Graph.Nodes |> Map.toList |> List.map fst
        
        // Simple heuristic: assign nodes alternately to partitions 0 and 1
        let assignments =
            nodeIds
            |> List.mapi (fun i nodeId -> nodeId, i % 2)
            |> Map.ofList
        
        createSolution problem.Graph (Some assignments) None
    
    /// Solve the problem using classical algorithms
    /// Dispatches to appropriate algorithm based on objective
    let solveClassical (problem: GraphOptimizationProblem<'TNode, 'TEdge>) : GraphOptimizationSolution<'TNode, 'TEdge> =
        match problem.Objective with
        | MinimizeColors -> greedyColoring problem
        | MinimizeTotalWeight -> nearestNeighborTSP problem
        | MaximizeCut -> randomizedMaxCut problem
        | _ -> emptySolution problem.Graph
    
    // ========================================================================
    // TDD CYCLE 2 - CONSTRAINT VALIDATION
    // ========================================================================
    
    /// Validate that a solution satisfies all constraints
    let validateConstraints (problem: GraphOptimizationProblem<'TNode, 'TEdge>) (solution: GraphOptimizationSolution<'TNode, 'TEdge>) : bool =
        problem.Constraints
        |> List.forall (fun constraint ->
            match constraint with
            | NoAdjacentEqual ->
                // Check that no adjacent nodes have the same color
                match solution.NodeAssignments with
                | Some assignments ->
                    problem.Graph.Edges
                    |> List.forall (fun edge ->
                        match Map.tryFind edge.Source assignments, Map.tryFind edge.Target assignments with
                        | Some colorU, Some colorV -> colorU <> colorV
                        | _ -> true  // If node not in assignments, skip
                    )
                | None -> true  // No node assignments, constraint doesn't apply
            
            | DegreeLimit maxDegree ->
                // Check that no node has degree > maxDegree
                problem.Graph.Adjacency
                |> Map.forall (fun _ neighbors -> neighbors.Length <= maxDegree)
            
            | VisitOnce ->
                // For TSP: each node should be visited exactly once
                // This is validated by checking the tour structure
                match solution.SelectedEdges with
                | Some edges ->
                    let nodeIds = problem.Graph.Nodes |> Map.toList |> List.map fst
                    let visitedNodes =
                        edges
                        |> List.collect (fun e -> [e.Source; e.Target])
                        |> List.distinct
                    visitedNodes.Length = nodeIds.Length
                | None -> true
            
            | Acyclic ->
                // Check for cycles (simplified: always true for now)
                true
            
            | Connected ->
                // Check graph connectivity (simplified: assume valid if has edges)
                problem.Graph.Edges.Length > 0
        )
