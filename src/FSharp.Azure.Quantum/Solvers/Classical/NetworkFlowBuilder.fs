namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GraphOptimization

/// High-level Network Flow Domain Builder - Quantum-First API
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for supply chain managers and logistics planners
/// who want to optimize network flows without understanding quantum computing
/// internals (QAOA, QUBO, backends).
/// 
/// QUANTUM-FIRST:
/// - Uses quantum optimization (QAOA) by default via LocalBackend (simulation)
/// - Optional backend parameter for cloud quantum hardware (IonQ, Rigetti)
/// - For algorithm-level control, use QuantumNetworkFlowSolver directly
/// 
/// EXAMPLE USAGE:
///   // Simple: Uses quantum simulation automatically
///   let flow = NetworkFlow.solve problem None
///   
///   // Advanced: Specify cloud quantum backend
///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
///   let flow = NetworkFlow.solve problem (Some ionqBackend)
///   
///   // Expert: Direct quantum solver access
///   open FSharp.Azure.Quantum.Quantum
///   let result = QuantumNetworkFlowSolver.solve backend problem config
module NetworkFlow =

    // ============================================================================
    // TYPES - Domain-specific types for Network Flow problems
    // ============================================================================

    /// Node type in the supply chain network
    type NodeType =
        | Source        // Supplier, factory, origin
        | Sink          // Customer, destination, demand point
        | Intermediate  // Warehouse, distribution center, hub

    /// Network node with capacity and properties
    type Node = {
        Id: string
        NodeType: NodeType
        Capacity: int
        Demand: int option      // For sinks only
        Supply: int option      // For sources only
    }

    /// Transport route with cost
    type Route = {
        From: string
        To: string
        Cost: float
    }

    /// Network flow problem representation
    type NetworkFlowProblem = {
        Nodes: Node list
        Routes: Route list
    }

    /// Flow solution with selected routes and metrics
    type FlowSolution = {
        /// Selected routes with flow amounts
        SelectedRoutes: (string * string * float) list
        /// Total cost of the flow
        TotalCost: float
        /// Demand satisfied vs. total demand
        DemandSatisfied: float
        TotalDemand: float
        FillRate: float
        /// Whether the solution satisfies all constraints
        IsValid: bool
        /// Backend used
        BackendName: string
    }

    // ============================================================================
    // HELPER FUNCTIONS
    // ============================================================================

    /// Convert domain problem to quantum solver format
    let private toQuantumProblem (problem: NetworkFlowProblem) : QuantumNetworkFlowSolver.NetworkFlowProblem =
        // Separate nodes by type
        let sources = 
            problem.Nodes 
            |> List.filter (fun n -> n.NodeType = Source) 
            |> List.map (fun n -> n.Id)
        
        let sinks = 
            problem.Nodes 
            |> List.filter (fun n -> n.NodeType = Sink) 
            |> List.map (fun n -> n.Id)
        
        let intermediateNodes = 
            problem.Nodes 
            |> List.filter (fun n -> n.NodeType = Intermediate) 
            |> List.map (fun n -> n.Id)
        
        // Build capacity map
        let capacities =
            problem.Nodes
            |> List.map (fun n -> n.Id, n.Capacity)
            |> Map.ofList
        
        // Build demand map (sinks only)
        let demands =
            problem.Nodes
            |> List.choose (fun n -> 
                match n.Demand with
                | Some d -> Some (n.Id, d)
                | None -> None)
            |> Map.ofList
        
        // Build supply map (sources only)
        let supplies =
            problem.Nodes
            |> List.choose (fun n -> 
                match n.Supply with
                | Some s -> Some (n.Id, s)
                | None -> None)
            |> Map.ofList
        
        // Convert routes to edges
        let edges =
            problem.Routes
            |> List.map (fun route ->
                { 
                    Source = route.From
                    Target = route.To
                    Weight = route.Cost
                    Directed = true
                    Value = Some route.Cost
                    Properties = Map.empty
                })
        
        {
            Sources = sources
            Sinks = sinks
            IntermediateNodes = intermediateNodes
            Edges = edges
            Capacities = capacities
            Demands = demands
            Supplies = supplies
        }

    /// Convert quantum solver result to domain solution
    let private fromQuantumSolution (quantumResult: QuantumNetworkFlowSolver.NetworkFlowSolution) : FlowSolution =
        // Extract selected routes with flow amounts
        let selectedRoutes =
            quantumResult.SelectedEdges
            |> List.map (fun edge ->
                let flowAmount = 
                    quantumResult.FlowAmounts 
                    |> Map.tryFind (edge.Source, edge.Target)
                    |> Option.defaultValue 0.0
                (edge.Source, edge.Target, flowAmount))
        
        // Validate solution
        let isValid = quantumResult.FillRate >= 0.95  // 95% fill rate threshold
        
        {
            SelectedRoutes = selectedRoutes
            TotalCost = quantumResult.TotalCost
            DemandSatisfied = quantumResult.DemandSatisfied
            TotalDemand = quantumResult.TotalDemand
            FillRate = quantumResult.FillRate
            IsValid = isValid
            BackendName = quantumResult.BackendName
        }

    // ============================================================================
    // PUBLIC API
    // ============================================================================

    /// Create a source node (supplier)
    let createSource (id: string) (supply: int) (capacity: int) : Node =
        {
            Id = id
            NodeType = Source
            Capacity = capacity
            Demand = None
            Supply = Some supply
        }

    /// Create a sink node (customer)
    let createSink (id: string) (demand: int) : Node =
        {
            Id = id
            NodeType = Sink
            Capacity = 0
            Demand = Some demand
            Supply = None
        }

    /// Create an intermediate node (warehouse, distribution center)
    let createIntermediate (id: string) (capacity: int) : Node =
        {
            Id = id
            NodeType = Intermediate
            Capacity = capacity
            Demand = None
            Supply = None
        }

    /// Create a transport route
    let createRoute (from: string) (to_: string) (cost: float) : Route =
        {
            From = from
            To = to_
            Cost = cost
        }

    /// Create network flow problem from nodes and routes
    let createProblem (nodes: Node list) (routes: Route list) : NetworkFlowProblem =
        {
            Nodes = nodes
            Routes = routes
        }

    /// Solve network flow problem using quantum optimization (QAOA)
    /// 
    /// QUANTUM-FIRST API:
    /// - Uses quantum backend by default (LocalBackend for simulation)
    /// - Specify custom backend for cloud quantum hardware (IonQ, Rigetti)
    /// - Returns business-domain FlowSolution result (not low-level QAOA output)
    /// 
    /// PARAMETERS:
    ///   problem - Network flow problem with nodes and routes
    ///   backend - Optional quantum backend (defaults to LocalBackend if None)
    /// 
    /// EXAMPLES:
    ///   // Simple: Automatic quantum simulation
    ///   let solution = NetworkFlow.solve problem None
    ///   
    ///   // Cloud execution: Specify IonQ backend
    ///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
    ///   let solution = NetworkFlow.solve problem (Some ionqBackend)
    /// 
    /// BACKEND LIMITATIONS:
    ///   LocalBackend supports up to 16 qubits (approximately 16 routes)
    ///   For larger problems, use cloud quantum backends (IonQ, Rigetti)
    /// 
    /// RETURNS:
    ///   Result with FlowSolution (routes, cost, fill rate) or error message
    let solve (problem: NetworkFlowProblem) (backend: BackendAbstraction.IQuantumBackend option) : QuantumResult<FlowSolution> =
        try
            // Use provided backend or create LocalBackend for simulation
            let actualBackend = 
                backend 
                |> Option.defaultValue (BackendAbstraction.createLocalBackend())
            
            // Convert to quantum solver format
            let quantumProblem = toQuantumProblem problem
            
            // Call quantum network flow solver with default shots
            match QuantumNetworkFlowSolver.solveWithShots actualBackend quantumProblem 1000 with
            | Error err -> Error err  // Quantum Network Flow solve failed
            | Ok quantumResult ->
                let solution = fromQuantumSolution quantumResult
                Ok solution
        with
        | ex -> Error (QuantumError.OperationError ("Network Flow solve failed: ", $"Failed: {ex.Message}"))

    /// Convenience function: Create problem and solve in one step using quantum optimization
    /// 
    /// PARAMETERS:
    ///   nodes - List of source/sink/intermediate nodes
    ///   routes - List of transport routes with costs
    ///   backend - Optional quantum backend (defaults to LocalBackend if None)
    /// 
    /// RETURNS:
    ///   Result with FlowSolution or error message
    /// 
    /// EXAMPLE:
    ///   let nodes = [
    ///       NetworkFlow.createSource "S1" 100 100
    ///       NetworkFlow.createSink "C1" 50
    ///   ]
    ///   let routes = [
    ///       NetworkFlow.createRoute "S1" "C1" 10.0
    ///   ]
    ///   let solution = NetworkFlow.solveDirectly nodes routes None
    let solveDirectly (nodes: Node list) (routes: Route list) (backend: BackendAbstraction.IQuantumBackend option) : QuantumResult<FlowSolution> =
        let problem = createProblem nodes routes
        solve problem backend
