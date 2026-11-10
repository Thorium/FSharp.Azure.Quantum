namespace FSharp.Azure.Quantum

open System
open System.Collections.Generic

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
    type Node<'T when 'T: equality> =
        {
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
    let node id value =
        {
            Id = id
            Value = value
            Properties = Map.empty
        }

    /// <summary>Create a node with properties.</summary>
    /// <param name="id">Unique identifier for the node</param>
    /// <param name="value">The value to store in the node</param>
    /// <param name="properties">List of key-value pairs for node metadata</param>
    /// <returns>A new node with the specified properties</returns>
    let nodeWithProps id value properties =
        {
            Id = id
            Value = value
            Properties = Map.ofList properties
        }

    // ========================================================================
    // FR-2: EDGE DEFINITION
    // ========================================================================

    /// An edge connecting two nodes
    type Edge<'T> =
        {
            Source: string
            Target: string
            Weight: float
            Directed: bool
            Value: 'T option
            Properties: Map<string, obj>
        }

    /// Create an undirected edge with weight
    let edge source target weight =
        {
            Source = source
            Target = target
            Weight = weight
            Directed = false
            Value = None
            Properties = Map.empty
        }

    /// Create a directed edge with weight
    let directedEdge source target weight =
        {
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
    type Graph<'TNode, 'TEdge when 'TNode: equality> =
        {
            Nodes: Map<string, Node<'TNode>>
            Edges: Edge<'TEdge> list
            Directed: bool
            Adjacency: Map<string, string list>
        }

    /// Graph construction and utilities
    module Graph =

        /// Create an empty graph
        let empty<'TNode, 'TEdge when 'TNode: equality> : Graph<'TNode, 'TEdge> =
            {
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
                        | Some neighbors -> Some(neighborId :: neighbors)
                        | None -> Some [ neighborId ])

                let acc' = addToList e.Source e.Target acc

                // For undirected graphs, add reverse edge
                if not (directed || e.Directed) then
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
        /// Every node carries exactly one value (a colour, a side of the cut). The colouring
        /// and cut encodings always require it; decodeSolution reports a node whose bits give
        /// no single value as a violation of this constraint
        | OneValuePerNode
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
    type QuboMatrix =
        {
            NumVariables: int
            Q: Map<int * int, float> // Sparse matrix representation
        }

    /// Create empty QUBO matrix
    let emptyQubo numVars =
        {
            NumVariables = numVars
            Q = Map.empty
        }

    // ========================================================================
    // CONSTRAINT VIOLATIONS
    // ========================================================================

    /// Constraint violation information
    type ConstraintViolation =
        {
            Constraint: GraphConstraint
            Description: string
            Severity: float // Penalty value
        }

    // ========================================================================
    // FR-8: SOLUTION REPRESENTATION
    // ========================================================================

    /// Solution to a graph optimization problem
    type GraphOptimizationSolution<'TNode, 'TEdge when 'TNode: equality> =
        {
            Graph: Graph<'TNode, 'TEdge>
            NodeAssignments: Map<string, int> option // For coloring, partitioning
            SelectedEdges: Edge<'TEdge> list option // For TSP, spanning tree
            ObjectiveValue: float
            IsFeasible: bool
            Violations: ConstraintViolation list
        }

    // ========================================================================
    // GRAPH OPTIMIZATION PROBLEM
    // ========================================================================

    /// A graph optimization problem specification
    type GraphOptimizationProblem<'TNode, 'TEdge when 'TNode: equality and 'TEdge: equality> =
        {
            Graph: Graph<'TNode, 'TEdge>
            Constraints: GraphConstraint list
            Objective: GraphObjective
            NumColors: int option // For graph coloring
        }

    // ========================================================================
    // FR-6: FLUENT BUILDER API (Idiomatic Immutable)
    // ========================================================================

    /// Fluent builder for graph optimization problems (immutable)
    type GraphOptimizationBuilder<'TNode, 'TEdge when 'TNode: equality and 'TEdge: equality> =
        private
            {
                nodes: Node<'TNode> list
                edges: Edge<'TEdge> list
                directed: bool
                constraints: GraphConstraint list
                objective: GraphObjective
                numColors: int option
            }

        /// Create a new builder with default values
        static member Create() : GraphOptimizationBuilder<'TNode, 'TEdge> =
            {
                nodes = []
                edges = []
                directed = false
                constraints = []
                objective = MinimizeColors
                numColors = None
            }

        /// Fluent API: Set the nodes in the graph
        member this.Nodes(nodeList: Node<'TNode> list) = { this with nodes = nodeList }

        /// Fluent API: Set the edges in the graph
        member this.Edges(edgeList: Edge<'TEdge> list) = { this with edges = edgeList }

        /// Fluent API: Set whether the graph is directed
        member this.Directed(isDirected: bool) = { this with directed = isDirected }

        /// Fluent API: Mark graph as directed (no parameter)
        member this.Directed() = { this with directed = true }

        /// Fluent API: Add a constraint to the problem
        member this.AddConstraint(c: GraphConstraint) =
            { this with
                constraints = c :: this.constraints
            }

        /// Fluent API: Set the optimization objective
        member this.Objective(obj: GraphObjective) = { this with objective = obj }

        /// Fluent API: Set the number of colors (for graph coloring)
        member this.NumColors(colors: int) = { this with numColors = Some colors }

        /// Build the graph optimization problem
        member this.Build() : GraphOptimizationProblem<'TNode, 'TEdge> =
            let nodeMap = this.nodes |> List.map (fun n -> n.Id, n) |> Map.ofList

            let graph =
                {
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
    let GraphOptimizationBuilder<'TNode, 'TEdge when 'TNode: equality and 'TEdge: equality> () =
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
    // TSP TOUR STEPS
    // ========================================================================

    /// Node ids in QUBO index order.
    let private orderedNodeIds (graph: Graph<'TNode, 'TEdge>) : string list =
        graph.Nodes |> Map.toList |> List.map fst

    /// The cheapest edge that allows each tour step source → target between two distinct
    /// nodes of the graph, keyed by (source index, target index) and oriented along the step.
    /// An undirected edge of an undirected graph allows both directions; a directed edge, or
    /// any edge of a directed graph, allows only its own direction.
    let private tourStepEdges (graph: Graph<'TNode, 'TEdge>) : Map<int * int, Edge<'TEdge>> =
        let indexOf =
            orderedNodeIds graph |> List.mapi (fun index pid -> pid, index) |> Map.ofList

        graph.Edges
        |> List.collect (fun e ->
            match Map.tryFind e.Source indexOf, Map.tryFind e.Target indexOf with
            | Some source, Some target when source <> target ->
                if graph.Directed || e.Directed then
                    [ (source, target), e ]
                else
                    [
                        (source, target), e
                        (target, source),
                        { e with
                            Source = e.Target
                            Target = e.Source
                        }
                    ]
            | _ -> [])
        |> List.fold
            (fun steps (key, e) ->
                match Map.tryFind key steps with
                | Some existing when existing.Weight <= e.Weight -> steps
                | _ -> Map.add key e steps)
            Map.empty

    /// Cost of every tour step between distinct nodes, and the constraint penalty λ of the
    /// TSP QUBO.
    ///
    /// A step costs the weight of its cheapest edge, shifted so that no step costs less than
    /// zero (a closed tour has exactly n steps, so a shift moves every tour by the same amount).
    /// A step that no edge allows costs (n + 1) times the largest edge cost, which is more
    /// than a whole tour over existing edges can cost.
    ///
    /// λ is twice the largest step cost. Placing an unvisited city into an empty time slot
    /// removes 2λ of penalty and adds at most two steps, and dropping a surplus placement
    /// (a second city in a slot, a second slot for a city) never adds cost, so with λ above
    /// the largest step cost every minimum of the QUBO is a permutation matrix, whatever
    /// the scale of the weights.
    let private tourStepCosts
        (numNodes: int)
        (stepEdges: Map<int * int, Edge<'TEdge>>)
        : Map<int * int, float> * float =
        let weights = stepEdges |> Map.toList |> List.map (fun (_, e) -> e.Weight)

        let shift =
            match weights with
            | [] -> 0.0
            | _ -> -(min 0.0 (List.min weights))

        let largestEdgeCost =
            match weights with
            | [] -> 0.0
            | _ -> List.max weights + shift

        let scale = if largestEdgeCost > 0.0 then largestEdgeCost else 1.0
        let missingStepCost = float (numNodes + 1) * scale

        let costs =
            [
                for source in 0 .. numNodes - 1 do
                    for target in 0 .. numNodes - 1 do
                        if source <> target then
                            match Map.tryFind (source, target) stepEdges with
                            | Some e -> yield (source, target), e.Weight + shift
                            | None -> yield (source, target), missingStepCost
            ]
            |> Map.ofList

        let largestStepCost =
            if stepEdges.Count < numNodes * (numNodes - 1) then
                missingStepCost
            else
                scale

        costs, 2.0 * largestStepCost

    // ========================================================================
    // FR-7: QUBO ENCODING (Idiomatic Functional)
    // ========================================================================

    /// <summary>
    /// Encode a graph optimization problem to Quadratic Unconstrained Binary Optimization (QUBO) format.
    /// </summary>
    ///
    /// <param name="problem">The graph optimization problem to encode</param>
    /// <returns>A QUBO matrix suitable for quantum annealing or classical optimization</returns>
    ///
    /// <remarks>
    /// <para><b>Graph Coloring (MinimizeColors):</b></para>
    /// <para>Variables: x_{i,c} = 1 if node i has color c (one-hot encoding)</para>
    /// <para>Constraints:</para>
    /// <list type="bullet">
    ///   <item>One-hot: Each node gets exactly one color (always enforced)</item>
    ///   <item>NoAdjacentEqual: Adjacent nodes must have different colors (if specified)</item>
    /// </list>
    /// <para>Formula: Σ_{c1&lt;c2} x_{i,c1} * x_{i,c2} + Σ_{(u,v)∈E} Σ_c x_{u,c} * x_{v,c}</para>
    /// <para>The QUBO has no term for the number of colours: its minima are the proper
    /// colourings with NumColors colours, whichever of them use fewer. ObjectiveValue of a
    /// decoded solution reports the colours used.</para>
    ///
    /// <para><b>Traveling Salesman Problem (MinimizeTotalWeight):</b></para>
    /// <para>Variables: x_{i,t} = 1 if city i is visited at time t (n² variables, index i·n + t,
    /// cities in node-id order)</para>
    /// <para>Constraints (always enforced, penalty λ each):</para>
    /// <list type="bullet">
    ///   <item>Each city visited exactly once: Σ_t x_{i,t} = 1</item>
    ///   <item>Each time slot has one city: Σ_i x_{i,t} = 1</item>
    /// </list>
    /// <para>Objective: Minimize Σ_t Σ_{u≠v} c(u,v) * x_{u,t} * x_{v,(t+1) mod n}: a closed tour,
    /// every step charged in the direction it is taken, the step from the last slot back to
    /// the first included.</para>
    /// <para>c(u,v) is the weight of the cheapest edge that allows the step u → v. An undirected
    /// edge allows both directions; a directed edge (or any edge of a directed graph) allows only
    /// source → target. A step that no edge allows costs more than any tour over existing edges.</para>
    /// <para>λ is twice the largest step cost, so the minimum is a permutation matrix (a tour)
    /// at any scale of the weights, and among tours the shortest one.</para>
    ///
    /// <para><b>MaxCut (MaximizeCut):</b></para>
    /// <para>Variables: x_i = 1 if node i is in partition 1, else 0</para>
    /// <para>Objective: Maximize Σ_{(i,j)∈E} w_{i,j} * (x_i ⊕ x_j)</para>
    /// <para>QUBO form: Minimize Σ_{(i,j)∈E} w_{i,j} * (2·x_i·x_j − x_i − x_j), which is minus
    /// the weight of the cut (x_i ⊕ x_j = x_i + x_j − 2·x_i·x_j)</para>
    ///
    /// <para><b>Edge selection (MaximizeEdges, MinimizeEdges):</b></para>
    /// <para>Variables: x_e = 1 if edge e is selected; the QUBO only counts the selected edges.</para>
    ///
    /// <para><b>Constraints outside the QUBO:</b> only the constraints named above are encoded.
    /// Every other constraint of the problem (DegreeLimit, MinDegree, Acyclic, Connected,
    /// Custom, …) is absent from the QUBO, so a minimum can break it; decodeSolution checks
    /// all of the problem's constraints and reports the broken ones in Violations.</para>
    /// </remarks>
    ///
    /// <example>
    /// <code>
    /// let problem =
    ///     GraphOptimizationBuilder()
    ///         .Nodes([node "A" 1; node "B" 2])
    ///         .Edges([edge "A" "B" 1.0])
    ///         .Objective(MinimizeColors)
    ///         .NumColors(3)
    ///         .Build()
    ///
    /// let qubo = toQubo problem
    /// // qubo.NumVariables = 6 (2 nodes * 3 colors)
    /// // qubo.Q contains penalty terms for constraints and objective
    /// </code>
    /// </example>
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
            // Accumulate coefficients on duplicate keys (e.g. parallel edges):
            // QUBO terms with the same index pair sum, they don't overwrite
            let updatedQ =
                terms
                |> List.fold
                    (fun q (key, value) ->
                        q
                        |> Map.change key (function
                            | Some existing -> Some(existing + value)
                            | None -> Some value))
                    qubo.Q

            { qubo with Q = updatedQ }

        match problem.Objective with
        | MinimizeColors ->
            // Graph coloring: one-hot encoding
            // Variables: x_{i,c} = 1 if node i has color c
            let numColors = problem.NumColors |> Option.defaultValue DefaultNumColors
            let numVars = numNodes * numColors

            let baseQubo = emptyQubo numVars

            // ONE-HOT CONSTRAINT: each node must have exactly one color (Σ_c x_{i,c} = 1).
            // Reuse the canonical encoder so the constraint contributes BOTH the linear
            // reward (-λ·x_i, the QUBO diagonal) and the quadratic penalty (2λ·x_i·x_j).
            // Emitting only the quadratic pairs encodes "at most one", which leaves the
            // all-zeros (uncoloured) assignment a global optimum of the QUBO.
            let oneHotTerms =
                nodeIndexMap
                |> Map.toList
                |> List.collect (fun (_, nodeIdx) ->
                    let nodeVars = [ for c in 0 .. numColors - 1 -> nodeIdx * numColors + c ]
                    Qubo.oneHotConstraint nodeVars DefaultPenalty |> Map.toList)

            let quboWithOneHot = addTermsToQubo baseQubo oneHotTerms

            // NO-ADJACENT-EQUAL CONSTRAINT: Adjacent nodes cannot have same color
            if
                hasConstraint (function
                    | NoAdjacentEqual -> true
                    | _ -> false)
            then
                // For each edge (u, v), add penalty if same color
                let penaltyTerms =
                    problem.Graph.Edges
                    |> List.collect (fun edge ->
                        match Map.tryFind edge.Source nodeIndexMap, Map.tryFind edge.Target nodeIndexMap with
                        | Some uIdx, Some vIdx ->
                            List.init (max 0 numColors) (fun c ->
                                // Penalty term: x_{u,c} * x_{v,c}
                                let varU = uIdx * numColors + c
                                let varV = vIdx * numColors + c
                                ((varU, varV), DefaultPenalty))
                        | _ -> [] // Skip edges with unknown nodes
                    )

                // Add all penalty terms to QUBO
                addTermsToQubo quboWithOneHot penaltyTerms
            else
                quboWithOneHot

        | MinimizeTotalWeight ->
            // TSP: one-hot time encoding of a closed tour
            // Variables: x_{i,t} = 1 if city i visited at time t
            // Two constraints, each with penalty λ:
            //   1. Each city visited exactly once: Σ_t x_{i,t} = 1
            //   2. Each time slot has one city: Σ_i x_{i,t} = 1
            // Objective: Minimize Σ_t Σ_{u≠v} c(u,v) * x_{u,t} * x_{v,(t+1) mod n}

            let numVars = numNodes * numNodes
            let baseQubo = emptyQubo numVars

            // Helper: Get variable index for city i at time t
            let varIndex i t = i * numNodes + t

            // Step costs c(u,v) and the penalty λ that keeps every minimum a permutation matrix
            let stepCosts, penalty = tourStepCosts numNodes (tourStepEdges problem.Graph)

            // Helper: Generate one-hot constraint terms (exactly one variable = 1)
            // For each outer index, penalize having multiple inner indices selected
            // Exactly-one per outer index. Reuse the canonical encoder so each group
            // contributes the linear reward (-λ) as well as the quadratic penalty (2λ).
            // The two TSP one-hot families (city-once and slot-once) share the same
            // x_{i,t} variables, so their diagonal terms accumulate to -2λ via
            // addTermsToQubo (which sums duplicate keys).
            let oneHotConstraintTerms
                (outerRange: int)
                (innerRange: int)
                (varFn: int -> int -> int)
                : ((int * int) * float) list =
                [ 0 .. outerRange - 1 ]
                |> List.collect (fun outer ->
                    let groupVars = [ for inner in 0 .. innerRange - 1 -> varFn outer inner ]
                    Qubo.oneHotConstraint groupVars penalty |> Map.toList)

            // Constraint 1: Each city i must be visited exactly once
            let constraint1Terms = oneHotConstraintTerms numNodes numNodes varIndex

            // Constraint 2: Each time slot t must have exactly one city
            let constraint2Terms =
                oneHotConstraintTerms numNodes numNodes (fun t i -> varIndex i t)

            // Distance objective: every step u → v is charged for every pair of consecutive
            // time slots, the last slot being followed by the first (the tour is closed).
            // The step v → u is a separate entry of stepCosts with its own cost.
            let distanceTerms =
                [
                    for KeyValue((source, target), cost) in stepCosts do
                        if cost <> 0.0 then
                            for t in 0 .. numNodes - 1 do
                                let v1 = varIndex source t
                                let v2 = varIndex target ((t + 1) % numNodes)
                                // Canonical ordering for QUBO
                                yield ((min v1 v2, max v1 v2), cost)
                ]

            // Combine all terms
            let allTerms = constraint1Terms @ constraint2Terms @ distanceTerms
            addTermsToQubo baseQubo allTerms

        | MaximizeCut ->
            // MaxCut: binary partition encoding
            // Variables: x_i = 1 if node i in partition 1, 0 otherwise
            // Objective: Maximize the weight of the edges crossing the partition.
            // Edge (i, j, w) is cut when x_i ⊕ x_j = x_i + x_j − 2·x_i·x_j = 1, so the
            // QUBO minimizes Σ w_ij · (2·x_i·x_j − x_i − x_j) = −(cut weight).
            // A quadratic term alone (±w·x_i·x_j) is not MaxCut: its minimum puts both
            // endpoints on side 1 or leaves every node on side 0.

            let numVars = numNodes
            let baseQubo = emptyQubo numVars

            // A self-loop is never cut and contributes nothing
            let edgeTerms =
                problem.Graph.Edges
                |> List.collect (fun edge ->
                    match Map.tryFind edge.Source nodeIndexMap, Map.tryFind edge.Target nodeIndexMap with
                    | Some srcIdx, Some tgtIdx when srcIdx <> tgtIdx ->
                        // Canonical ordering (i < j) for the quadratic entry
                        let i = min srcIdx tgtIdx
                        let j = max srcIdx tgtIdx

                        [
                            ((i, j), 2.0 * edge.Weight)
                            ((i, i), -edge.Weight)
                            ((j, j), -edge.Weight)
                        ]
                    | _ -> [])

            // Build QUBO matrix with edge terms
            addTermsToQubo baseQubo edgeTerms

        | MaximizeEdges
        | MinimizeEdges ->
            // Edge-selection encoding: one binary variable per edge, x_e = 1 when
            // edge e is selected. QUBO always minimizes, so maximizing the edge
            // count means minimizing its negation. The problem's constraints are not
            // part of this QUBO; decodeSolution checks them on the decoded selection.
            let numEdges = problem.Graph.Edges.Length
            let baseQubo = emptyQubo numEdges

            let sign =
                match problem.Objective with
                | MinimizeEdges -> 1.0
                | _ -> -1.0

            let edgeTerms = problem.Graph.Edges |> List.mapi (fun e _ -> ((e, e), sign))

            addTermsToQubo baseQubo edgeTerms

        | MinimizeSpanningTree
        | MinimizeMaxWeight
        | Custom _ ->
            // Spanning-tree and minimax objectives need auxiliary-variable
            // encodings that are not implemented; Custom objectives have no
            // generic QUBO form.
            invalidOp (
                sprintf
                    "GraphOptimization.toQubo: QUBO encoding is not implemented for objective '%A'. Supported objectives: MinimizeColors, MinimizeTotalWeight, MaximizeCut, MaximizeEdges, MinimizeEdges."
                    problem.Objective
            )

    // ========================================================================
    // OBJECTIVE VALUE CALCULATION (TDD CYCLE 2)
    // ========================================================================

    /// A graph with boxed node and edge payloads, the shape Custom constraints and objectives
    /// take. A direct cast of Graph<'TNode, 'TEdge> throws InvalidCastException for any
    /// concretely-typed graph.
    let private boxGraph (graph: Graph<'TNode, 'TEdge>) : Graph<obj, obj> =
        {
            Nodes =
                graph.Nodes
                |> Map.map (fun _ n ->
                    {
                        Id = n.Id
                        Value = box n.Value
                        Properties = n.Properties
                    })
            Edges =
                graph.Edges
                |> List.map (fun e ->
                    {
                        Source = e.Source
                        Target = e.Target
                        Weight = e.Weight
                        Directed = e.Directed
                        Value = e.Value |> Option.map box
                        Properties = e.Properties
                    })
            Directed = graph.Directed
            Adjacency = graph.Adjacency
        }

    /// <summary>
    /// The value of the problem's objective for a solution.
    /// </summary>
    ///
    /// <param name="problem">The problem whose objective is evaluated</param>
    /// <param name="solution">The solution to evaluate</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><b>MinimizeColors:</b> the number of distinct values among the node assignments</item>
    ///   <item><b>MaximizeCut:</b> the total weight of the problem's edges whose endpoints are
    ///   assigned different values (an edge with an unassigned endpoint is not counted)</item>
    ///   <item><b>MinimizeTotalWeight, MinimizeSpanningTree:</b> the total weight of the selected edges</item>
    ///   <item><b>MinimizeMaxWeight:</b> the largest weight among the selected edges (0 without any)</item>
    ///   <item><b>MaximizeEdges, MinimizeEdges:</b> the number of selected edges</item>
    ///   <item><b>Custom:</b> the function applied to the graph of the selected edges, or to the
    ///   problem graph when the solution has no selection</item>
    /// </list>
    /// The value says nothing about feasibility; see IsFeasible and Violations.
    /// </returns>
    let objectiveValue
        (problem: GraphOptimizationProblem<'TNode, 'TEdge>)
        (solution: GraphOptimizationSolution<'TNode, 'TEdge>)
        : float =
        let assignments = solution.NodeAssignments |> Option.defaultValue Map.empty
        let selected = solution.SelectedEdges |> Option.defaultValue []

        match problem.Objective with
        | MinimizeColors -> assignments |> Map.toList |> List.map snd |> List.distinct |> List.length |> float
        | MaximizeCut ->
            problem.Graph.Edges
            |> List.sumBy (fun edge ->
                match Map.tryFind edge.Source assignments, Map.tryFind edge.Target assignments with
                | Some sideU, Some sideV when sideU <> sideV -> edge.Weight
                | _ -> 0.0)
        | MinimizeTotalWeight
        | MinimizeSpanningTree -> selected |> List.sumBy (fun edge -> edge.Weight)
        | MinimizeMaxWeight ->
            match selected with
            | [] -> 0.0
            | edges -> edges |> List.map (fun edge -> edge.Weight) |> List.max
        | MaximizeEdges
        | MinimizeEdges -> float selected.Length
        | Custom objective ->
            match solution.SelectedEdges with
            | Some edges ->
                objective (
                    boxGraph
                        { problem.Graph with
                            Edges = edges
                            Adjacency = Graph.buildAdjacency problem.Graph.Directed edges
                        }
                )
            | None -> objective (boxGraph problem.Graph)

    /// <summary>
    /// Calculate the objective value for a given solution, guessing the objective from the
    /// shape of the solution.
    /// </summary>
    ///
    /// <param name="solution">The graph optimization solution to evaluate</param>
    /// <returns>The objective value (lower is better for minimization, higher for maximization)</returns>
    ///
    /// <remarks>
    /// <para><b>Smart Inference:</b> Automatically detects objective type from solution structure:</para>
    /// <list type="bullet">
    ///   <item><b>Graph Coloring:</b> Counts unique colors used in node assignments</item>
    ///   <item><b>MaxCut:</b> Counts edges crossing partition (detected when all assignments are 0 or 1)</item>
    ///   <item><b>TSP:</b> Sums edge weights in selected tour</item>
    /// </list>
    /// </remarks>
    ///
    /// <example>
    /// <code>
    /// // Graph Coloring
    /// let solution = { NodeAssignments = Some (Map ["A", 0; "B", 1; "C", 0]); ... }
    /// let value = calculateObjectiveValue solution  // Returns 2.0 (two colors used)
    ///
    /// // MaxCut
    /// let solution = { NodeAssignments = Some (Map ["A", 0; "B", 1; "C", 1]); ... }
    /// let value = calculateObjectiveValue solution  // Returns count of edges between different partitions
    ///
    /// // TSP
    /// let solution = { SelectedEdges = Some [edge "A" "B" 5.0; edge "B" "C" 3.0]; ... }
    /// let value = calculateObjectiveValue solution  // Returns 8.0 (sum of edge weights)
    /// </code>
    /// </example>
    [<Obsolete("calculateObjectiveValue guesses the objective from the shape of the solution (a 0/1 colouring counts as MaxCut) and counts cut edges instead of their weights. Use objectiveValue problem solution.")>]
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
                    | _ -> false)
                |> List.length
                |> float
            else
                // Graph coloring: Count unique colors used
                values |> List.distinct |> List.length |> float

        | None, Some selectedEdges ->
            // TSP: Sum edge weights in tour
            selectedEdges |> List.sumBy (fun edge -> edge.Weight)

        | _ ->
            // Unknown or empty solution
            0.0

    // ========================================================================
    // FR-8: SOLUTION DECODING
    // ========================================================================

    /// What the bits say, before the objective and the constraints are evaluated: decodeSolution
    /// fills in ObjectiveValue, IsFeasible and Violations.
    let private readAssignment
        (graph: Graph<'TNode, 'TEdge>)
        (nodeAssignments: Map<string, int> option)
        (selectedEdges: Edge<'TEdge> list option)
        : GraphOptimizationSolution<'TNode, 'TEdge> =
        {
            Graph = graph
            NodeAssignments = nodeAssignments
            SelectedEdges = selectedEdges
            ObjectiveValue = 0.0
            IsFeasible = false
            Violations = []
        }

    /// A broken rule of the encoding or of the problem
    let private violation (broken: GraphConstraint) (description: string) : ConstraintViolation =
        {
            Constraint = broken
            Description = description
            Severity = DefaultPenalty
        }

    /// <summary>
    /// The closed tour a TSP (MinimizeTotalWeight) QUBO assignment encodes: node ids in time-slot order.
    /// </summary>
    ///
    /// <param name="problem">The graph optimization problem the QUBO was built from</param>
    /// <param name="quboSolution">Binary variable assignments, x_{i,t} at index i·n + t</param>
    /// <returns>
    /// Some tour only when the assignment is a permutation matrix: every time slot holds exactly
    /// one city and every city holds exactly one slot. Any other assignment is not a tour and
    /// gives None; nothing is repaired or filled in.
    /// </returns>
    let tryDecodeTour
        (problem: GraphOptimizationProblem<'TNode, 'TEdge>)
        (quboSolution: int list)
        : string list option =
        let nodeIds = orderedNodeIds problem.Graph |> List.toArray
        let n = nodeIds.Length
        let bits = List.toArray quboSolution

        if bits.Length <> n * n then
            None
        else
            let cities =
                Array.init n (fun t ->
                    [
                        for i in 0 .. n - 1 do
                            if bits.[i * n + t] = 1 then
                                i
                    ])
                |> Array.choose (function
                    | [ city ] -> Some city
                    | _ -> None)

            if cities.Length = n && (Array.distinct cities).Length = n then
                Some [ for city in cities -> nodeIds.[city] ]
            else
                None

    /// The assignment the bits encode, and the rules of the encoding itself that they break
    /// (a node without exactly one value, bits that are no tour, a tour step without an edge).
    /// Nothing is repaired or filled in: a node whose bits give no single value is left out
    /// of NodeAssignments and reported.
    let private decodeBits
        (problem: GraphOptimizationProblem<'TNode, 'TEdge>)
        (quboSolution: int list)
        : GraphOptimizationSolution<'TNode, 'TEdge> * ConstraintViolation list =
        let nodeIds = problem.Graph.Nodes |> Map.toList |> List.map fst
        let bits = List.toArray quboSolution

        match problem.Objective with
        | MinimizeColors ->
            // Decode one-hot color assignment: exactly one set bit in a node's group is its
            // colour. A group with no set bit or several is not a colour; picking one
            // (colour 0, the first set bit) would report a colouring the bits do not hold.
            let numColors = problem.NumColors |> Option.defaultValue DefaultNumColors

            let decoded =
                nodeIds
                |> List.mapi (fun nodeIdx nodeId ->
                    let colours =
                        [ 0 .. numColors - 1 ]
                        |> List.filter (fun c ->
                            let varIdx = nodeIdx * numColors + c
                            varIdx < bits.Length && bits.[varIdx] = 1)

                    match colours with
                    | [ colour ] -> Ok(nodeId, colour)
                    | [] -> Error(violation OneValuePerNode $"node '{nodeId}' has no colour")
                    | several ->
                        let listed = several |> List.map string |> String.concat ", "
                        Error(violation OneValuePerNode $"node '{nodeId}' has {several.Length} colours ({listed})"))

            let assignments =
                decoded
                |> List.choose (function
                    | Ok assigned -> Some assigned
                    | Error _ -> None)
                |> Map.ofList

            let undecodable =
                decoded
                |> List.choose (function
                    | Ok _ -> None
                    | Error broken -> Some broken)

            readAssignment problem.Graph (Some assignments) None, undecodable

        | MinimizeTotalWeight ->
            // Decode TSP tour from one-hot time encoding
            // Variables: x_{i,t} = 1 if city i visited at time t
            match tryDecodeTour problem quboSolution with
            | None ->
                readAssignment problem.Graph None None,
                [
                    violation
                        VisitOnce
                        "the bits are not a tour: every time slot needs exactly one city and every city exactly one slot"
                ]
            | Some tour ->
                let indexOf = nodeIds |> List.mapi (fun index pid -> pid, index) |> Map.ofList
                let order = tour |> List.map (fun pid -> Map.find pid indexOf) |> List.toArray
                let stepEdges = tourStepEdges problem.Graph

                // The steps of the closed tour: consecutive time slots, the last slot being
                // followed by the first. A single city has no step.
                let steps =
                    if order.Length < 2 then
                        []
                    else
                        [ for t in 0 .. order.Length - 1 -> order.[t], order.[(t + 1) % order.Length] ]

                let tourEdges = steps |> List.choose (fun step -> Map.tryFind step stepEdges)
                let nodeIdArray = List.toArray nodeIds

                let missingSteps =
                    steps
                    |> List.filter (fun step -> not (Map.containsKey step stepEdges))
                    |> List.map (fun (source, target) ->
                        violation
                            OneOutgoing
                            $"the tour steps from '{nodeIdArray.[source]}' to '{nodeIdArray.[target]}', which no edge allows")

                readAssignment problem.Graph None (Some tourEdges), missingSteps

        | MaximizeCut ->
            // Decode partition: bit i is the side of node i. A node without a bit has no
            // side; placing it on side 0 would report a cut the bits do not hold.
            let partition =
                nodeIds
                |> List.indexed
                |> List.filter (fun (i, _) -> i < bits.Length)
                |> List.map (fun (i, nodeId) -> nodeId, bits.[i])
                |> Map.ofList

            let unplaced =
                nodeIds
                |> List.skip (min bits.Length nodeIds.Length)
                |> List.map (fun nodeId -> violation OneValuePerNode $"node '{nodeId}' has no side of the cut")

            readAssignment problem.Graph (Some partition) None, unplaced

        | MaximizeEdges
        | MinimizeEdges ->
            // Decode edge selection: edge e is selected when its bit is 1
            let selectedEdges =
                problem.Graph.Edges
                |> List.indexed
                |> List.filter (fun (e, _) -> e < bits.Length && bits.[e] = 1)
                |> List.map snd

            readAssignment problem.Graph None (Some selectedEdges), []

        | MinimizeSpanningTree
        | MinimizeMaxWeight
        | Custom _ ->
            invalidOp (
                sprintf
                    "GraphOptimization.decodeSolution: decoding is not implemented for objective '%A'. Supported objectives: MinimizeColors, MinimizeTotalWeight, MaximizeCut, MaximizeEdges, MinimizeEdges."
                    problem.Objective
            )

    // ========================================================================
    // TDD CYCLE 2 - CONSTRAINT VALIDATION
    // ========================================================================

    /// Weak connectivity: every node in `nodeIds` reachable from the first one when
    /// edge direction is ignored. An empty node set is vacuously connected.
    let private isConnected (nodeIds: string list) (edges: Edge<'T> list) : bool =
        match nodeIds with
        | [] -> true
        | start :: _ ->
            let neighbours = Graph.buildAdjacency false edges
            let visited = HashSet<string>()
            let pending = Stack<string>()
            pending.Push start
            visited.Add start |> ignore

            while pending.Count > 0 do
                let current = pending.Pop()

                for next in neighbours |> Map.tryFind current |> Option.defaultValue [] do
                    if visited.Add next then
                        pending.Push next

            nodeIds |> List.forall visited.Contains

    /// True when the edges contain a cycle. Directed edges (or a directed graph) are
    /// checked for a directed cycle (Kahn's algorithm); otherwise any cycle ignoring
    /// direction counts, including a self-loop or two parallel edges (union-find).
    let private hasCycle (directed: bool) (edges: Edge<'T> list) : bool =
        if directed || (not edges.IsEmpty && edges |> List.forall (fun e -> e.Directed)) then
            let nodes = edges |> List.collect (fun e -> [ e.Source; e.Target ]) |> List.distinct
            let successors = Graph.buildAdjacency true edges
            let inDegree = Dictionary<string, int>()

            for n in nodes do
                inDegree[n] <- 0

            for e in edges do
                inDegree[e.Target] <- inDegree[e.Target] + 1

            let ready = Queue<string>(nodes |> List.filter (fun n -> inDegree[n] = 0))

            let mutable removed = 0

            while ready.Count > 0 do
                let current = ready.Dequeue()
                removed <- removed + 1

                for next in successors |> Map.tryFind current |> Option.defaultValue [] do
                    inDegree[next] <- inDegree[next] - 1

                    if inDegree[next] = 0 then
                        ready.Enqueue next

            // Nodes on (or downstream of) a cycle never reach in-degree 0.
            removed < nodes.Length
        else
            let parent = Dictionary<string, string>()

            let rec find (x: string) =
                match parent.TryGetValue x with
                | true, p when p <> x ->
                    let root = find p
                    parent[x] <- root
                    root
                | true, _ -> x
                | false, _ ->
                    parent[x] <- x
                    x

            edges
            |> List.exists (fun e ->
                let rootS = find e.Source
                let rootT = find e.Target

                if rootS = rootT then
                    true
                else
                    parent[rootS] <- rootT
                    false)

    /// <summary>
    /// Validate that a solution satisfies all problem constraints.
    /// </summary>
    ///
    /// <param name="problem">The graph optimization problem with constraints</param>
    /// <param name="solution">The solution to validate</param>
    /// <returns>True if all constraints are satisfied, false otherwise</returns>
    ///
    /// <remarks>
    /// The structural constraints (DegreeLimit, MinDegree, Connected, Acyclic) check the
    /// solution's SelectedEdges when it has them, and the problem graph otherwise.
    /// <para><b>Supported Constraints:</b></para>
    /// <list type="bullet">
    ///   <item><b>NoAdjacentEqual:</b> Adjacent nodes must have different values (graph coloring)</item>
    ///   <item><b>DegreeLimit:</b> Each node's degree ≤ maxDegree (network design)</item>
    ///   <item><b>MinDegree:</b> Every node's degree ≥ minDegree, isolated nodes included</item>
    ///   <item><b>VisitOnce:</b> Each node visited exactly once (TSP)</item>
    ///   <item><b>Connected:</b> Selected edges form a connected subgraph; without a selection,
    ///   every node of the problem graph is reachable (edge direction ignored)</item>
    ///   <item><b>Acyclic:</b> No cycle: a directed cycle for directed edges, any cycle otherwise</item>
    /// </list>
    /// </remarks>
    ///
    /// <example>
    /// <code>
    /// let problem =
    ///     GraphOptimizationBuilder()
    ///         .Nodes([node "A" 1; node "B" 2])
    ///         .Edges([edge "A" "B" 1.0])
    ///         .AddConstraint(NoAdjacentEqual)
    ///         .Objective(MinimizeColors)
    ///         .Build()
    ///
    /// let solution = { NodeAssignments = Some (Map ["A", 0; "B", 1]); ... }
    /// let isValid = validateConstraints problem solution  // Returns true
    ///
    /// let invalidSolution = { NodeAssignments = Some (Map ["A", 0; "B", 0]); ... }
    /// let isValid2 = validateConstraints problem invalidSolution  // Returns false (same color)
    /// </code>
    /// </example>
    let validateConstraints
        (problem: GraphOptimizationProblem<'TNode, 'TEdge>)
        (solution: GraphOptimizationSolution<'TNode, 'TEdge>)
        : bool =
        // The edge set the structural constraints judge: the solution's selection when it
        // has one, else the problem graph. Checking the problem graph while a selection
        // exists would pass any selection at all.
        let edgesUnderTest =
            solution.SelectedEdges |> Option.defaultValue problem.Graph.Edges

        let adjacencyUnderTest =
            match solution.SelectedEdges with
            | Some selected -> Graph.buildAdjacency problem.Graph.Directed selected
            | None -> problem.Graph.Adjacency

        let degreeOf nodeId =
            adjacencyUnderTest
            |> Map.tryFind nodeId
            |> Option.map List.length
            |> Option.defaultValue 0

        let allNodeIds =
            [
                yield! problem.Graph.Nodes |> Map.keys
                yield! adjacencyUnderTest |> Map.keys
            ]
            |> List.distinct

        problem.Constraints
        |> List.forall (fun constr ->
            match constr with
            | NoAdjacentEqual ->
                // Check that no adjacent nodes have the same color
                match solution.NodeAssignments with
                | Some assignments ->
                    problem.Graph.Edges
                    |> List.forall (fun edge ->
                        match Map.tryFind edge.Source assignments, Map.tryFind edge.Target assignments with
                        | Some colorU, Some colorV -> colorU <> colorV
                        | _ -> true // If node not in assignments, skip
                    )
                | None -> true // No node assignments, constraint doesn't apply

            | DegreeLimit maxDegree ->
                // Check that no node has degree > maxDegree
                adjacencyUnderTest
                |> Map.forall (fun _ neighbors -> neighbors.Length <= maxDegree)

            | VisitOnce ->
                // For TSP: each node should be visited exactly once
                // This is validated by checking the tour structure
                match solution.SelectedEdges with
                | Some edges ->
                    let nodeIds = problem.Graph.Nodes |> Map.toList |> List.map fst

                    let visitedNodes =
                        edges |> List.collect (fun e -> [ e.Source; e.Target ]) |> List.distinct

                    // The tour of a single node has no edge
                    nodeIds.Length <= 1 || visitedNodes.Length = nodeIds.Length
                | None -> true

            | OneValuePerNode ->
                match solution.NodeAssignments with
                | Some assignments -> problem.Graph.Nodes |> Map.forall (fun nodeId _ -> assignments.ContainsKey nodeId)
                | None -> true

            | Acyclic -> not (hasCycle problem.Graph.Directed edgesUnderTest)

            | Connected ->
                match solution.SelectedEdges with
                | Some selected ->
                    // The subgraph the selection forms: its own endpoints, one component.
                    let touched =
                        selected |> List.collect (fun e -> [ e.Source; e.Target ]) |> List.distinct

                    isConnected touched selected
                | None -> isConnected allNodeIds problem.Graph.Edges

            | MinDegree minDegree ->
                // Every node, including isolated ones: an adjacency map has no entry for
                // a node without edges, so iterating the map alone passed them unchecked.
                allNodeIds |> List.forall (fun nodeId -> degreeOf nodeId >= minDegree)

            | OneIncoming ->
                // Check that each node has exactly one incoming edge
                match solution.SelectedEdges with
                | Some edges ->
                    let nodeIds = problem.Graph.Nodes |> Map.toList |> List.map fst

                    nodeIds
                    |> List.forall (fun nodeId ->
                        let incomingCount = edges |> List.filter (fun e -> e.Target = nodeId) |> List.length
                        incomingCount = 1)
                | None -> true

            | OneOutgoing ->
                // Check that each node has exactly one outgoing edge
                match solution.SelectedEdges with
                | Some edges ->
                    let nodeIds = problem.Graph.Nodes |> Map.toList |> List.map fst

                    nodeIds
                    |> List.forall (fun nodeId ->
                        let outgoingCount = edges |> List.filter (fun e -> e.Source = nodeId) |> List.length
                        outgoingCount = 1)
                | None -> true

            | GraphConstraint.Custom predicate ->
                // Custom constraint validation using provided predicate.
                // Note: predicate takes the graph, not problem/solution.
                predicate (boxGraph problem.Graph))

    // ========================================================================
    // FR-8: SOLUTION DECODING WITH FEASIBILITY
    // ========================================================================

    /// What a solution breaks of one constraint of the problem, in words
    let private describeBroken
        (problem: GraphOptimizationProblem<'TNode, 'TEdge>)
        (solution: GraphOptimizationSolution<'TNode, 'TEdge>)
        (broken: GraphConstraint)
        : string =
        match broken, solution.NodeAssignments with
        | NoAdjacentEqual, Some assignments ->
            let clashes =
                problem.Graph.Edges
                |> List.filter (fun edge ->
                    match Map.tryFind edge.Source assignments, Map.tryFind edge.Target assignments with
                    | Some valueU, Some valueV -> valueU = valueV
                    | _ -> false)
                |> List.map (fun edge -> $"{edge.Source}-{edge.Target}")
                |> String.concat ", "

            $"adjacent nodes share a value: {clashes}"
        | DegreeLimit maxDegree, _ -> $"a node has more than {maxDegree} edges"
        | MinDegree minDegree, _ -> $"a node has fewer than {minDegree} edges"
        | VisitOnce, _ -> "the selected edges do not visit every node"
        | Acyclic, _ -> "the edges contain a cycle"
        | Connected, _ -> "the edges are not connected"
        | OneIncoming, _ -> "a node does not have exactly one incoming edge"
        | OneOutgoing, _ -> "a node does not have exactly one outgoing edge"
        | OneValuePerNode, _ -> "a node has no value"
        | NoAdjacentEqual, None -> "adjacent nodes share a value"
        | GraphConstraint.Custom _, _ -> "the custom constraint does not hold"

    /// <summary>
    /// Decode a QUBO solution (binary variable assignments) back to a graph optimization solution.
    /// </summary>
    ///
    /// <param name="problem">The original graph optimization problem</param>
    /// <param name="quboSolution">Binary variable assignments from QUBO solver (list of 0s and 1s)</param>
    /// <returns>
    /// The assignment the bits encode, with ObjectiveValue = objectiveValue problem solution,
    /// Violations = the rules of the encoding the bits break plus every constraint of the
    /// problem that validateConstraints finds broken, and IsFeasible = no violation.
    /// </returns>
    ///
    /// <remarks>
    /// <para>The bits are read as they are; nothing is repaired or filled in.</para>
    ///
    /// <para><b>Graph Coloring:</b></para>
    /// <para>Decodes one-hot color encoding: x_{i,c} = 1 means node i has color c</para>
    /// <para>A node with no color bit set, or with several, has no color: it is left out of
    /// NodeAssignments and reported as a OneValuePerNode violation.</para>
    ///
    /// <para><b>TSP:</b></para>
    /// <para>Decodes one-hot time encoding: x_{i,t} = 1 means city i visited at time t</para>
    /// <para>A permutation matrix (see tryDecodeTour) gives the edges of the closed tour in
    /// time-slot order, each oriented in the direction of travel, the edge from the last city
    /// back to the first included. The solution is infeasible, without edges, when the bits
    /// are not a permutation matrix, and infeasible when a step of the tour has no edge.</para>
    ///
    /// <para><b>MaxCut:</b></para>
    /// <para>Decodes binary partition: x_i = 1 means node i is in partition 1, else partition 0.
    /// A node without a bit is left out and reported as a OneValuePerNode violation.</para>
    ///
    /// <para><b>Edge selection (MaximizeEdges, MinimizeEdges):</b></para>
    /// <para>x_e = 1 selects edge e. The QUBO does not encode the problem's constraints, so
    /// the selection is checked against them here.</para>
    /// </remarks>
    ///
    /// <example>
    /// <code>
    /// let problem = GraphOptimizationBuilder().Nodes(...).Objective(MinimizeColors).NumColors(3).Build()
    /// let quboSolution = [1; 0; 0; 0; 1; 0] // Node 0 → color 0, Node 1 → color 1
    /// let solution = decodeSolution problem quboSolution
    /// // solution.NodeAssignments = Some (Map ["N0", 0; "N1", 1])
    /// // solution.ObjectiveValue = 2.0 (two colors used)
    /// // solution.IsFeasible = true
    /// </code>
    /// </example>
    let decodeSolution
        (problem: GraphOptimizationProblem<'TNode, 'TEdge>)
        (quboSolution: int list)
        : GraphOptimizationSolution<'TNode, 'TEdge> =
        let decoded, brokenEncoding = decodeBits problem quboSolution

        let brokenConstraints =
            problem.Constraints
            |> List.filter (fun constr ->
                not (
                    validateConstraints
                        { problem with
                            Constraints = [ constr ]
                        }
                        decoded
                ))
            |> List.map (fun constr -> violation constr (describeBroken problem decoded constr))

        let violations = brokenEncoding @ brokenConstraints

        { decoded with
            ObjectiveValue = objectiveValue problem decoded
            IsFeasible = violations.IsEmpty
            Violations = violations
        }
