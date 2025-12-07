namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GraphOptimization

/// High-level MaxCut Domain Builder - Quantum-First API
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for users who want to solve MaxCut problems
/// without understanding quantum computing internals (QAOA, QUBO, backends).
/// 
/// QUANTUM-FIRST:
/// - Uses quantum optimization (QAOA) by default via LocalBackend (simulation)
/// - Optional backend parameter for cloud quantum hardware (IonQ, Rigetti)
/// - For algorithm-level control, use QuantumMaxCutSolver directly
/// 
/// WHAT IS MAXCUT:
/// MaxCut is the canonical QAOA problem - partition a graph into two sets
/// to maximize the total weight of edges crossing the partition.
/// 
/// USE CASES:
/// - Circuit design: Minimize wire crossings between chip regions
/// - Social networks: Detect communities or polarized groups
/// - Load balancing: Distribute tasks across servers to minimize communication
/// - Image segmentation: Split pixels into foreground/background
/// 
/// EXAMPLE USAGE:
///   // Simple: Uses quantum simulation automatically
///   let solution = MaxCut.solve graph None
///   
///   // Advanced: Specify cloud quantum backend
///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
///   let solution = MaxCut.solve graph (Some ionqBackend)
///   
///   // Expert: Direct quantum solver access
///   open FSharp.Azure.Quantum.Quantum
///   let result = QuantumMaxCutSolver.solve backend problem config
module MaxCut =

    // ============================================================================
    // TYPES - Domain-specific types for MaxCut problems
    // ============================================================================

    /// MaxCut Problem representation
    type MaxCutProblem = {
        /// Graph vertices (nodes)
        Vertices: string list
        
        /// Graph edges with weights (undirected)
        Edges: Edge<float> list
        
        /// Number of vertices
        VertexCount: int
        
        /// Number of edges
        EdgeCount: int
    }

    /// MaxCut Solution with partition and cut value
    type Solution = {
        /// Vertices in partition S
        PartitionS: string list
        
        /// Vertices in partition T (complement of S)
        PartitionT: string list
        
        /// Total cut value (sum of edge weights crossing partition)
        CutValue: float
        
        /// Edges crossing the partition
        CutEdges: Edge<float> list
        
        /// Backend used (LocalBackend, IonQ, etc.)
        BackendName: string
        
        /// Whether quantum or classical solver was used
        IsQuantum: bool
    }

    // ============================================================================
    // PROBLEM CREATION
    // ============================================================================

    /// Create MaxCut problem from vertices and edges
    /// 
    /// PARAMETERS:
    ///   vertices - List of vertex names
    ///   edges - List of (source, target, weight) tuples
    /// 
    /// RETURNS:
    ///   MaxCutProblem ready for solving
    /// 
    /// EXAMPLE:
    ///   let vertices = ["A"; "B"; "C"; "D"]
    ///   let edges = [("A", "B", 1.0); ("B", "C", 2.0); ("C", "D", 1.0); ("D", "A", 1.0)]
    ///   let problem = MaxCut.createProblem vertices edges
    let createProblem (vertices: string list) (edges: (string * string * float) list) : MaxCutProblem =
        let edgeList = 
            edges
            |> List.map (fun (source, target, weight) -> 
                GraphOptimization.edge source target weight)
        
        {
            Vertices = vertices
            Edges = edgeList
            VertexCount = vertices.Length
            EdgeCount = edges.Length
        }

    // ============================================================================
    // HELPER FUNCTIONS - COMMON GRAPH STRUCTURES
    // ============================================================================

    /// Create a complete graph (all vertices connected to all others)
    /// 
    /// PARAMETERS:
    ///   vertices - List of vertex names
    ///   weight - Weight for all edges (default 1.0)
    /// 
    /// EXAMPLE:
    ///   let problem = MaxCut.completeGraph ["A"; "B"; "C"] 1.0
    let completeGraph (vertices: string list) (weight: float) : MaxCutProblem =
        let edges = 
            [for i in 0 .. vertices.Length - 1 do
             for j in i + 1 .. vertices.Length - 1 do
                 yield (vertices.[i], vertices.[j], weight)]
        
        createProblem vertices edges

    /// Create a cycle graph (vertices connected in a ring)
    /// 
    /// PARAMETERS:
    ///   vertices - List of vertex names (in cycle order)
    ///   weight - Weight for all edges (default 1.0)
    /// 
    /// EXAMPLE:
    ///   let problem = MaxCut.cycleGraph ["A"; "B"; "C"; "D"] 1.0
    ///   // Creates: A-B-C-D-A
    let cycleGraph (vertices: string list) (weight: float) : MaxCutProblem =
        let n = vertices.Length
        let edges = 
            [for i in 0 .. n - 1 do
                let j = (i + 1) % n
                yield (vertices.[i], vertices.[j], weight)]
        
        createProblem vertices edges

    /// Create a path graph (vertices connected in a line)
    /// 
    /// PARAMETERS:
    ///   vertices - List of vertex names (in path order)
    ///   weight - Weight for all edges (default 1.0)
    /// 
    /// EXAMPLE:
    ///   let problem = MaxCut.pathGraph ["A"; "B"; "C"; "D"] 1.0
    ///   // Creates: A-B-C-D
    let pathGraph (vertices: string list) (weight: float) : MaxCutProblem =
        let edges = 
            [for i in 0 .. vertices.Length - 2 do
                yield (vertices.[i], vertices.[i + 1], weight)]
        
        createProblem vertices edges

    /// Create a grid graph (vertices arranged in 2D grid)
    /// 
    /// PARAMETERS:
    ///   rows - Number of rows
    ///   cols - Number of columns
    ///   weight - Weight for all edges (default 1.0)
    /// 
    /// EXAMPLE:
    ///   let problem = MaxCut.gridGraph 3 3 1.0
    ///   // Creates 3x3 grid with vertices named "R0C0", "R0C1", etc.
    let gridGraph (rows: int) (cols: int) (weight: float) : MaxCutProblem =
        let vertices = 
            [for r in 0 .. rows - 1 do
             for c in 0 .. cols - 1 do
                 yield sprintf "R%dC%d" r c]
        
        let edges = 
            [// Horizontal edges
             for r in 0 .. rows - 1 do
             for c in 0 .. cols - 2 do
                 yield (sprintf "R%dC%d" r c, sprintf "R%dC%d" r (c + 1), weight)
             
             // Vertical edges
             for r in 0 .. rows - 2 do
             for c in 0 .. cols - 1 do
                 yield (sprintf "R%dC%d" r c, sprintf "R%dC%d" (r + 1) c, weight)]
        
        createProblem vertices edges

    /// Create a star graph (one central vertex connected to all others)
    /// 
    /// PARAMETERS:
    ///   center - Central vertex name
    ///   spokes - List of spoke vertex names
    ///   weight - Weight for all edges (default 1.0)
    /// 
    /// EXAMPLE:
    ///   let problem = MaxCut.starGraph "Center" ["A"; "B"; "C"; "D"] 1.0
    let starGraph (center: string) (spokes: string list) (weight: float) : MaxCutProblem =
        let vertices = center :: spokes
        let edges = 
            spokes
            |> List.map (fun spoke -> (center, spoke, weight))
        
        createProblem vertices edges

    // ============================================================================
    // MAIN SOLVER
    // ============================================================================

    /// Solve MaxCut problem using quantum optimization (QAOA)
    /// 
    /// QUANTUM-FIRST API:
    /// - Uses quantum backend by default (LocalBackend for simulation)
    /// - Specify custom backend for cloud quantum hardware (IonQ, Rigetti)
    /// - Returns business-domain Solution result (not low-level QAOA output)
    /// 
    /// PARAMETERS:
    ///   problem - MaxCut problem with vertices and weighted edges
    ///   backend - Optional quantum backend (defaults to LocalBackend if None)
    /// 
    /// EXAMPLES:
    ///   // Simple: Automatic quantum simulation
    ///   let solution = MaxCut.solve problem None
    ///   
    ///   // Cloud execution: Specify IonQ backend
    ///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
    ///   let solution = MaxCut.solve problem (Some ionqBackend)
    /// 
    /// RETURNS:
    ///   Result with Solution (partitions, cut value) or error message
    let solve (problem: MaxCutProblem) (backend: BackendAbstraction.IQuantumBackend option) : QuantumResult<Solution> =
        try
            // Use provided backend or create LocalBackend for simulation
            let actualBackend = 
                backend 
                |> Option.defaultValue (BackendAbstraction.createLocalBackend())
            
            // Convert to quantum solver format
            let quantumProblem : QuantumMaxCutSolver.MaxCutProblem = {
                Vertices = problem.Vertices
                Edges = problem.Edges
            }
            
            // Create quantum MaxCut solver configuration
            let quantumConfig : QuantumMaxCutSolver.QaoaConfig = {
                NumShots = 1000
                InitialParameters = (0.5, 0.5)
            }
            
            // Call quantum MaxCut solver directly using computation expression
            quantumResult {
                let! quantumResult = QuantumMaxCutSolver.solve actualBackend quantumProblem quantumConfig
                
                return {
                    PartitionS = quantumResult.PartitionS
                    PartitionT = quantumResult.PartitionT
                    CutValue = quantumResult.CutValue
                    CutEdges = quantumResult.CutEdges
                    BackendName = quantumResult.BackendName
                    IsQuantum = true
                }
            }
        with
        | ex -> Error (QuantumError.OperationError ("MaxCut solve failed: ", $"Failed: {ex.Message}"))

    /// Solve MaxCut using classical greedy algorithm (for comparison)
    /// 
    /// PARAMETERS:
    ///   problem - MaxCut problem with vertices and weighted edges
    /// 
    /// RETURNS:
    ///   Solution using classical local search
    /// 
    /// EXAMPLE:
    ///   let classicalSolution = MaxCut.solveClassical problem
    let solveClassical (problem: MaxCutProblem) : Solution =
        // Convert to quantum solver format
        let quantumProblem : QuantumMaxCutSolver.MaxCutProblem = {
            Vertices = problem.Vertices
            Edges = problem.Edges
        }
        
        let classicalResult = QuantumMaxCutSolver.solveClassical quantumProblem
        
        {
            PartitionS = classicalResult.PartitionS
            PartitionT = classicalResult.PartitionT
            CutValue = classicalResult.CutValue
            CutEdges = classicalResult.CutEdges
            BackendName = "Classical Greedy"
            IsQuantum = false
        }

    /// Convenience function: Create problem and solve in one step using quantum optimization
    /// 
    /// PARAMETERS:
    ///   vertices - List of vertex names
    ///   edges - List of (source, target, weight) tuples
    ///   backend - Optional quantum backend (defaults to LocalBackend if None)
    /// 
    /// RETURNS:
    ///   Result with Solution or error message
    /// 
    /// EXAMPLE:
    ///   let vertices = ["A"; "B"; "C"; "D"]
    ///   let edges = [("A", "B", 1.0); ("B", "C", 1.0); ("C", "D", 1.0); ("D", "A", 1.0)]
    ///   let solution = MaxCut.solveDirectly vertices edges None
    let solveDirectly 
        (vertices: string list) 
        (edges: (string * string * float) list) 
        (backend: BackendAbstraction.IQuantumBackend option) 
        : QuantumResult<Solution> =
        
        let problem = createProblem vertices edges
        solve problem backend

    // ============================================================================
    // VALIDATION AND UTILITIES
    // ============================================================================

    /// Calculate the cut value for a given partition (validation/verification)
    /// 
    /// PARAMETERS:
    ///   problem - MaxCut problem
    ///   partitionS - Vertices in partition S
    /// 
    /// RETURNS:
    ///   Cut value (sum of weights of edges crossing the partition)
    let calculateCutValue (problem: MaxCutProblem) (partitionS: string list) : float =
        let quantumProblem : QuantumMaxCutSolver.MaxCutProblem = {
            Vertices = problem.Vertices
            Edges = problem.Edges
        }
        
        QuantumMaxCutSolver.calculateCutValue quantumProblem partitionS

    /// Validate that a partition is valid (all vertices assigned, no overlap)
    /// 
    /// PARAMETERS:
    ///   problem - MaxCut problem
    ///   partitionS - Vertices in partition S
    ///   partitionT - Vertices in partition T
    /// 
    /// RETURNS:
    ///   true if partition is valid, false otherwise
    let isValidPartition (problem: MaxCutProblem) (partitionS: string list) (partitionT: string list) : bool =
        let allVertices = Set.ofList problem.Vertices
        let sSet = Set.ofList partitionS
        let tSet = Set.ofList partitionT
        
        // Check: No overlap between S and T
        let noOverlap = Set.intersect sSet tSet |> Set.isEmpty
        
        // Check: Union of S and T equals all vertices
        let coversAll = Set.union sSet tSet = allVertices
        
        noOverlap && coversAll
