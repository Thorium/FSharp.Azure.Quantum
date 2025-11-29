namespace FSharp.Azure.Quantum.Quantum

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GraphOptimization

/// Quantum MaxCut Solver using QAOA and Backend Abstraction
/// 
/// ALGORITHM-LEVEL API (for advanced users):
/// This module provides direct access to quantum MaxCut solving via QAOA.
/// MaxCut is THE canonical QAOA benchmark problem - finding a partition
/// of graph vertices that maximizes edges crossing the partition.
/// 
/// RULE 1 COMPLIANCE:
/// ✅ Requires IQuantumBackend parameter (explicit quantum execution)
/// 
/// TECHNICAL DETAILS:
/// - Execution: Quantum hardware/simulator via backend
/// - Algorithm: QAOA (Quantum Approximate Optimization Algorithm)
/// - Speed: Seconds to minutes (includes job queue wait for cloud backends)
/// - Cost: ~$10-100 per run on real quantum hardware (IonQ, Rigetti)
/// - LocalBackend: Free simulation (limited to ~16 qubits = 16 vertices)
///
/// QUANTUM PIPELINE:
/// 1. Graph → MaxCut QUBO Matrix (quadratic encoding)
/// 2. QUBO → QAOA Circuit (Hamiltonians + Layers)
/// 3. Execute on Quantum Backend (IonQ/Rigetti/Local)
/// 4. Decode Measurements → Vertex Partitions
/// 5. Return Best Partition
///
/// MaxCut Problem:
///   Given graph G = (V, E) with edge weights w_ij,
///   partition V into two sets S and T to maximize:
///   
///   MaxCut = Σ w_ij  where i ∈ S, j ∈ T
///
/// Example:
///   let backend = BackendAbstraction.createLocalBackend()
///   let config = { NumShots = 1000; InitialParameters = (0.5, 0.5) }
///   match QuantumMaxCutSolver.solve backend graph config with
///   | Ok solution -> printfn "Cut value: %f" solution.CutValue
///   | Error msg -> printfn "Error: %s" msg
module QuantumMaxCutSolver =

    // ================================================================================
    // PROBLEM DEFINITION
    // ================================================================================

    /// MaxCut problem specification (undirected weighted graph)
    type MaxCutProblem = {
        /// Graph vertices (nodes)
        Vertices: string list
        
        /// Graph edges with weights (undirected)
        Edges: Edge<float> list
    }
    
    /// MaxCut solution result
    type MaxCutSolution = {
        /// Vertices in partition S
        PartitionS: string list
        
        /// Vertices in partition T (complement of S)
        PartitionT: string list
        
        /// Total cut value (sum of edge weights crossing partition)
        CutValue: float
        
        /// Edges that cross the partition
        CutEdges: Edge<float> list
        
        /// Backend used for execution
        BackendName: string
        
        /// Number of measurement shots
        NumShots: int
        
        /// Execution time in milliseconds
        ElapsedMs: float
        
        /// QUBO objective value (energy)
        BestEnergy: float
    }

    // ================================================================================
    // QUBO ENCODING FOR MAXCUT
    // ================================================================================

    /// Convert sparse QUBO matrix (Map) to dense 2D array
    let private quboMapToArray (quboMatrix: QuboMatrix) : float[,] =
        let n = quboMatrix.NumVariables
        let dense = Array2D.zeroCreate n n
        
        for KeyValue((i, j), value) in quboMatrix.Q do
            dense.[i, j] <- value
        
        dense

    /// Encode MaxCut problem as QUBO
    /// 
    /// MaxCut QUBO formulation (THE canonical QAOA problem):
    /// 
    /// Variables: x_i ∈ {0, 1} where x_i = 1 means vertex i is in partition S
    /// 
    /// Objective (to MAXIMIZE):
    ///   MaxCut = Σ_{(i,j) ∈ E} w_ij * (x_i + x_j - 2*x_i*x_j)
    ///          = Σ_{(i,j) ∈ E} w_ij * (x_i XOR x_j)
    /// 
    /// QUBO form (to MINIMIZE for QAOA, so we negate):
    ///   Minimize: -Σ_{(i,j) ∈ E} w_ij * (x_i + x_j - 2*x_i*x_j)
    /// 
    /// Expanded to QUBO matrix Q:
    ///   Q_ii = -Σ_{j: (i,j) ∈ E} w_ij         (linear terms)
    ///   Q_ij = 2*w_ij for edge (i,j)           (quadratic terms)
    let toQubo (problem: MaxCutProblem) : Result<QuboMatrix, string> =
        try
            // Create vertex index mapping
            let vertexIndexMap = 
                problem.Vertices 
                |> List.mapi (fun i vertex -> vertex, i)
                |> Map.ofList
            
            let numVars = problem.Vertices.Length
            
            if numVars = 0 then
                Error "MaxCut problem has no vertices"
            elif problem.Edges.Length = 0 then
                Error "MaxCut problem has no edges"
            else
                // Build QUBO terms as Map<(int * int), float>
                let mutable quboTerms = Map.empty
                
                // Process each edge: add contributions to QUBO matrix
                for edge in problem.Edges do
                    let i = vertexIndexMap.[edge.Source]
                    let j = vertexIndexMap.[edge.Target]
                    let weight = edge.Weight
                    
                    // MaxCut QUBO formulation: 
                    // For edge (i,j) with weight w:
                    //   Minimize: -w * (x_i + x_j - 2*x_i*x_j)
                    //           = -w*x_i - w*x_j + 2*w*x_i*x_j
                    
                    // Linear terms: Q_ii and Q_jj
                    let existingQii = quboTerms |> Map.tryFind (i, i) |> Option.defaultValue 0.0
                    let existingQjj = quboTerms |> Map.tryFind (j, j) |> Option.defaultValue 0.0
                    
                    quboTerms <- quboTerms |> Map.add (i, i) (existingQii - weight)
                    quboTerms <- quboTerms |> Map.add (j, j) (existingQjj - weight)
                    
                    // Quadratic term: Q_ij (only upper triangle for symmetry)
                    let (row, col) = if i <= j then (i, j) else (j, i)
                    let existingQij = quboTerms |> Map.tryFind (row, col) |> Option.defaultValue 0.0
                    quboTerms <- quboTerms |> Map.add (row, col) (existingQij + 2.0 * weight)
                
                Ok {
                    Q = quboTerms
                    NumVariables = numVars
                }
        with ex ->
            Error (sprintf "MaxCut QUBO encoding failed: %s" ex.Message)

    // ================================================================================
    // SOLUTION DECODING
    // ================================================================================

    /// Decode binary solution to MaxCut partition
    let private decodeSolution (problem: MaxCutProblem) (bitstring: int[]) : MaxCutSolution =
        let vertexPartitions = 
            problem.Vertices 
            |> List.mapi (fun i vertex -> vertex, bitstring.[i])
        
        // Partition S: vertices with bit = 1
        let partitionS = 
            vertexPartitions 
            |> List.filter (fun (_, bit) -> bit = 1)
            |> List.map fst
        
        // Partition T: vertices with bit = 0 (complement)
        let partitionT = 
            vertexPartitions 
            |> List.filter (fun (_, bit) -> bit = 0)
            |> List.map fst
        
        // Calculate cut value: sum weights of edges crossing partition
        let cutEdges = 
            problem.Edges
            |> List.filter (fun edge ->
                let sourcePartition = List.contains edge.Source partitionS
                let targetPartition = List.contains edge.Target partitionS
                sourcePartition <> targetPartition  // Edge crosses partition
            )
        
        let cutValue = cutEdges |> List.sumBy (fun edge -> edge.Weight)
        
        {
            PartitionS = partitionS
            PartitionT = partitionT
            CutValue = cutValue
            CutEdges = cutEdges
            BackendName = ""
            NumShots = 0
            ElapsedMs = 0.0
            BestEnergy = -cutValue  // QUBO minimizes -cutValue
        }

    /// Calculate cut value for a given partition (for validation)
    let calculateCutValue (problem: MaxCutProblem) (partitionS: string list) : float =
        let partitionSSet = Set.ofList partitionS
        
        problem.Edges
        |> List.filter (fun edge ->
            let sourceInS = partitionSSet.Contains(edge.Source)
            let targetInS = partitionSSet.Contains(edge.Target)
            sourceInS <> targetInS  // Edge crosses partition
        )
        |> List.sumBy (fun edge -> edge.Weight)

    // ================================================================================
    // QAOA CONFIGURATION
    // ================================================================================

    /// QAOA configuration parameters
    type QaoaConfig = {
        /// Number of measurement shots
        NumShots: int
        
        /// Initial QAOA parameters (gamma, beta) for single layer
        /// Typical values: (0.5, 0.5) or (π/4, π/2)
        InitialParameters: float * float
    }
    
    /// Default QAOA configuration for MaxCut
    let defaultConfig : QaoaConfig = {
        NumShots = 1000
        InitialParameters = (0.5, 0.5)  // Reasonable starting point
    }

    // ================================================================================
    // MAIN SOLVER
    // ================================================================================

    /// Solve MaxCut problem using quantum QAOA
    /// 
    /// Parameters:
    ///   - backend: Quantum backend (LocalBackend, IonQ, Rigetti)
    ///   - problem: MaxCut problem (graph with vertices and weighted edges)
    ///   - config: QAOA configuration (shots, initial parameters)
    /// 
    /// Returns: Ok with best partition found, or Error with message
    /// 
    /// Example:
    ///   let backend = BackendAbstraction.createLocalBackend()
    ///   let problem = { Vertices = ["A"; "B"; "C"]; Edges = [...] }
    ///   let config = { NumShots = 1000; InitialParameters = (0.5, 0.5) }
    ///   match solve backend problem config with
    ///   | Ok solution -> printfn "Cut: %f" solution.CutValue
    ///   | Error msg -> printfn "Error: %s" msg
    let solve 
        (backend: BackendAbstraction.IQuantumBackend) 
        (problem: MaxCutProblem) 
        (config: QaoaConfig) 
        : Result<MaxCutSolution, string> =
        
        let startTime = DateTime.Now
        
        try
            // Step 1: Validate problem size against backend
            let numQubits = problem.Vertices.Length
            
            if numQubits > backend.MaxQubits then
                Error (sprintf "Problem requires %d qubits but backend '%s' supports max %d qubits" 
                    numQubits backend.Name backend.MaxQubits)
            elif numQubits = 0 then
                Error "MaxCut problem has no vertices"
            elif problem.Edges.Length = 0 then
                Error "MaxCut problem has no edges"
            else
                // Step 2: Encode MaxCut as QUBO
                match toQubo problem with
                | Error msg -> Error (sprintf "MaxCut encoding failed: %s" msg)
                | Ok quboMatrix ->
                    
                    // Step 3: Convert QUBO to dense array for QAOA
                    let quboArray = quboMapToArray quboMatrix
                    
                    // Step 4: Create QAOA Hamiltonians from QUBO
                    let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo quboArray
                    let mixerHam = QaoaCircuit.MixerHamiltonian.create problemHam.NumQubits
                    
                    // Step 5: Build QAOA circuit with parameters
                    let (gamma, beta) = config.InitialParameters
                    let parameters = [| gamma, beta |]
                    let qaoaCircuit = QaoaCircuit.QaoaCircuit.build problemHam mixerHam parameters
                    
                    // Step 6: Wrap QAOA circuit for backend execution
                    let circuitWrapper = CircuitAbstraction.QaoaCircuitWrapper(qaoaCircuit) :> CircuitAbstraction.ICircuit
                    
                    // Step 7: Execute on quantum backend
                    match backend.Execute circuitWrapper config.NumShots with
                    | Error msg -> Error (sprintf "Backend execution failed: %s" msg)
                    | Ok execResult ->
                        
                        // Step 7: Decode measurements to partitions
                        let solutions = 
                            execResult.Measurements
                            |> Array.map (fun bitstring -> decodeSolution problem bitstring)
                        
                        // Step 8: Find best solution (maximum cut value)
                        let bestSolution = 
                            solutions
                            |> Array.maxBy (fun sol -> sol.CutValue)
                        
                        let elapsedMs = (DateTime.Now - startTime).TotalMilliseconds
                        
                        Ok {
                            bestSolution with
                                BackendName = backend.Name
                                NumShots = config.NumShots
                                ElapsedMs = elapsedMs
                        }
        
        with ex ->
            Error (sprintf "Quantum MaxCut solve failed: %s" ex.Message)

    // ================================================================================
    // CLASSICAL GREEDY SOLVER (for comparison)
    // ================================================================================

    /// Solve MaxCut using greedy local search (classical algorithm)
    /// 
    /// This provides a classical baseline for comparison with quantum QAOA.
    /// Uses random initial partition with local improvement.
    /// 
    /// Typical performance: 80-90% of optimal for random graphs
    let solveClassical (problem: MaxCutProblem) : MaxCutSolution =
        let rng = Random()
        
        // Start with random partition
        let initialPartitionS = 
            problem.Vertices
            |> List.filter (fun _ -> rng.NextDouble() > 0.5)
        
        // Local search: try moving each vertex to improve cut
        let rec improvePartition (currentS: string list) (improved: bool) =
            if not improved then
                currentS  // No improvement found, done
            else
                let mutable bestS = currentS
                let mutable bestCut = calculateCutValue problem currentS
                let mutable foundImprovement = false
                
                // Try moving each vertex
                for vertex in problem.Vertices do
                    let inS = List.contains vertex currentS
                    let newS = 
                        if inS then
                            currentS |> List.filter ((<>) vertex)  // Remove from S
                        else
                            vertex :: currentS  // Add to S
                    
                    let newCut = calculateCutValue problem newS
                    
                    if newCut > bestCut then
                        bestS <- newS
                        bestCut <- newCut
                        foundImprovement <- true
                
                improvePartition bestS foundImprovement
        
        let finalPartitionS = improvePartition initialPartitionS true
        let finalPartitionT = 
            problem.Vertices |> List.filter (fun v -> not (List.contains v finalPartitionS))
        
        let cutEdges = 
            problem.Edges
            |> List.filter (fun edge ->
                let sourceInS = List.contains edge.Source finalPartitionS
                let targetInS = List.contains edge.Target finalPartitionS
                sourceInS <> targetInS
            )
        
        let cutValue = cutEdges |> List.sumBy (fun edge -> edge.Weight)
        
        {
            PartitionS = finalPartitionS
            PartitionT = finalPartitionT
            CutValue = cutValue
            CutEdges = cutEdges
            BackendName = "Classical Greedy"
            NumShots = 0
            ElapsedMs = 0.0
            BestEnergy = -cutValue
        }
