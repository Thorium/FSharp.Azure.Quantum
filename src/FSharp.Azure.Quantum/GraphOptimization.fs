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
                let updatedQ =
                    penaltyTerms
                    |> List.fold (fun q (key, value) -> Map.add key value q) baseQubo.Q
                
                { baseQubo with Q = updatedQ }
            else
                baseQubo
        
        | MinimizeTotalWeight ->
            // TSP: one-hot time encoding
            // Variables: x_{i,t} = 1 if city i visited at time t
            let numVars = numNodes * numNodes
            emptyQubo numVars
        
        | MaximizeCut ->
            // MaxCut: binary partition encoding
            // Variables: x_i = 1 if node i in partition 1
            let numVars = numNodes
            emptyQubo numVars
        
        | _ ->
            // Default: binary encoding
            emptyQubo numNodes
    
    // ========================================================================
    // FR-8: SOLUTION DECODING
    // ========================================================================
    
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
            
            {
                Graph = problem.Graph
                NodeAssignments = Some assignments
                SelectedEdges = None
                ObjectiveValue = 0.0  // TODO: Calculate actual objective value
                IsFeasible = true
                Violations = []
            }
        
        | MinimizeTotalWeight ->
            // Decode TSP tour
            {
                Graph = problem.Graph
                NodeAssignments = None
                SelectedEdges = Some []  // TODO: Extract tour edges
                ObjectiveValue = 0.0
                IsFeasible = true
                Violations = []
            }
        
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
            
            {
                Graph = problem.Graph
                NodeAssignments = Some partition
                SelectedEdges = None
                ObjectiveValue = 0.0  // TODO: Calculate cut size
                IsFeasible = true
                Violations = []
            }
        
        | _ ->
            // Default: empty solution
            {
                Graph = problem.Graph
                NodeAssignments = None
                SelectedEdges = None
                ObjectiveValue = 0.0
                IsFeasible = false
                Violations = []
            }
        
        | MinimizeTotalWeight ->
            // Decode TSP tour
            {
                Graph = problem.Graph
                NodeAssignments = None
                SelectedEdges = Some []  // TODO: Extract tour edges
                ObjectiveValue = 0.0
                IsFeasible = true
                Violations = []
            }
        
        | MaximizeCut ->
            // Decode partition
            let nodeIds = problem.Graph.Nodes |> Map.toList |> List.map fst
            let partition =
                nodeIds
                |> List.mapi (fun i nodeId ->
                    let partitionValue = if i < quboSolution.Length then quboSolution.[i] else 0
                    nodeId, partitionValue
                )
                |> Map.ofList
            
            {
                Graph = problem.Graph
                NodeAssignments = Some partition
                SelectedEdges = None
                ObjectiveValue = 0.0  // TODO: Calculate cut size
                IsFeasible = true
                Violations = []
            }
        
        | _ ->
            {
                Graph = problem.Graph
                NodeAssignments = None
                SelectedEdges = None
                ObjectiveValue = 0.0
                IsFeasible = true
                Violations = []
            }
