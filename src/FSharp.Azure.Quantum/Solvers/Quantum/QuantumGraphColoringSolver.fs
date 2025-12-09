namespace FSharp.Azure.Quantum.Quantum

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.GraphOptimization

/// Quantum Graph Coloring Solver using QAOA and Backend Abstraction
/// 
/// ALGORITHM-LEVEL API (for advanced users):
/// This module provides direct access to quantum K-coloring solving via QAOA.
/// Graph coloring assigns colors to vertices such that no adjacent vertices
/// share the same color, minimizing the total number of colors used.
/// 
/// RULE 1 COMPLIANCE:
/// ✅ Requires IQuantumBackend parameter (explicit quantum execution)
/// 
/// TECHNICAL DETAILS:
/// - Execution: Quantum hardware/simulator via backend
/// - Algorithm: QAOA (Quantum Approximate Optimization Algorithm)
/// - Speed: Seconds to minutes (includes job queue wait for cloud backends)
/// - Cost: ~$10-100 per run on real quantum hardware (IonQ, Rigetti)
/// - LocalBackend: Free simulation (limited by qubit count)
///
/// QUANTUM PIPELINE:
/// 1. Graph + K colors → K-coloring QUBO Matrix (one-hot encoding)
/// 2. QUBO → QAOA Circuit (Hamiltonians + Layers)
/// 3. Execute on Quantum Backend (IonQ/Rigetti/Local)
/// 4. Decode Measurements → Color Assignments
/// 5. Return Best Valid Coloring
///
/// K-Coloring Problem:
///   Given graph G = (V, E) and K colors,
///   assign color c_i ∈ {0..K-1} to each vertex i such that:
///   
///   ∀ (i,j) ∈ E: c_i ≠ c_j  (adjacent vertices have different colors)
///   
///   Minimize: Number of colors used (chromatic number)
///
/// Example:
///   let backend = LocalBackend.LocalBackend() :> IQuantumBackend
///   let config = { NumShots = 1000; NumColors = 3; InitialParameters = (0.5, 0.5) }
///   match QuantumGraphColoringSolver.solve backend problem config with
///   | Ok solution -> printfn "Used %d colors" solution.ColorsUsed
///   | Error msg -> printfn "Error: %s" msg
module QuantumGraphColoringSolver =

    // ================================================================================
    // PROBLEM DEFINITION
    // ================================================================================

    /// Graph coloring problem specification
    type GraphColoringProblem = {
        /// Graph vertices (nodes)
        Vertices: string list
        
        /// Graph edges (conflicts - adjacent vertices cannot have same color)
        Edges: Edge<unit> list
        
        /// Number of available colors
        NumColors: int
        
        /// Optional fixed color assignments (pre-colored vertices)
        FixedColors: Map<string, int>
    }
    
    /// Graph coloring solution result
    type GraphColoringSolution = {
        /// Color assignment for each vertex (color index 0..K-1)
        ColorAssignments: Map<string, int>
        
        /// Number of distinct colors used
        ColorsUsed: int
        
        /// Number of conflicts (adjacent vertices with same color)
        ConflictCount: int
        
        /// Whether solution is valid (no conflicts)
        IsValid: bool
        
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
    // QUBO ENCODING FOR K-COLORING
    // ================================================================================

    /// Convert sparse QUBO matrix (Map) to dense 2D array
    let private quboMapToArray (quboMatrix: QuboMatrix) : float[,] =
        let n = quboMatrix.NumVariables
        let dense = Array2D.zeroCreate n n
        
        for KeyValue((i, j), value) in quboMatrix.Q do
            dense.[i, j] <- value
        
        dense

    /// Encode K-coloring problem as QUBO using one-hot encoding
    /// 
    /// ONE-HOT ENCODING:
    /// For n vertices and K colors, we use n*K binary variables:
    ///   x_{i,c} = 1 if vertex i is assigned color c, else 0
    /// 
    /// CONSTRAINTS (as penalty terms):
    /// 
    /// 1. Each vertex gets exactly one color (one-hot constraint):
    ///    Penalty: Σ_i (1 - Σ_c x_{i,c})²
    ///           = Σ_i (1 - 2*Σ_c x_{i,c} + (Σ_c x_{i,c})²)
    /// 
    /// 2. Adjacent vertices have different colors:
    ///    Penalty: Σ_{(i,j) ∈ E} Σ_c x_{i,c} * x_{j,c}
    /// 
    /// 3. Minimize colors used (optional, soft constraint):
    ///    Penalty: Σ_c max_i(x_{i,c})  (penalize using higher color indices)
    /// 
    /// QUBO FORMULATION:
    ///   Minimize: λ₁ * OneHotPenalty + λ₂ * ConflictPenalty + λ₃ * ColorMinimizationPenalty
    let toQubo (problem: GraphColoringProblem) (penaltyWeight: float) : Result<QuboMatrix * Map<int, string * int>, QuantumError> =
        try
            // Create variable mapping: (vertex_index, color_index) → qubo_variable_index
            let vertexIndexMap = 
                problem.Vertices 
                |> List.mapi (fun i vertex -> vertex, i)
                |> Map.ofList
            
            let numVertices = problem.Vertices.Length
            let numColors = problem.NumColors
            let numVars = numVertices * numColors
            
            if numVertices = 0 then
                Error (QuantumError.ValidationError ("numVertices", "Graph coloring problem has no vertices"))
            elif numColors < 1 then
                Error (QuantumError.ValidationError ("numColors", "Graph coloring problem must have at least 1 color"))
            elif problem.Edges.Length = 0 then
                Error (QuantumError.ValidationError ("numEdges", "Graph coloring problem has no edges"))
            else
                // Create reverse mapping: qubo_variable_index → (vertex, color)
                let reverseMap = 
                    seq {
                        for v in 0 .. numVertices - 1 do
                            for c in 0 .. numColors - 1 do
                                let quboVar = v * numColors + c
                                yield quboVar, (problem.Vertices.[v], c)
                    }
                    |> Map.ofSeq
                
                // Helper: Get QUBO variable index for (vertex_index, color)
                let getVarIndex vertexIdx color = vertexIdx * numColors + color
                
                // Build QUBO terms as Map<(int * int), float>
                let mutable quboTerms = Map.empty
                
                // CONSTRAINT 1: One-hot constraint (each vertex gets exactly one color)
                // Penalty: Σ_i (1 - Σ_c x_{i,c})²
                //        = Σ_i (1 - 2*Σ_c x_{i,c} + (Σ_c x_{i,c})²)
                //        = Σ_i (1 - 2*Σ_c x_{i,c} + Σ_c x_{i,c} + Σ_{c≠d} x_{i,c}*x_{i,d})
                
                for v in 0 .. numVertices - 1 do
                    // Check if vertex has fixed color
                    let vertexName = problem.Vertices.[v]
                    match Map.tryFind vertexName problem.FixedColors with
                    | Some fixedColor ->
                        // For fixed color: force x_{v,fixedColor} = 1, others = 0
                        // Add large penalty to other colors
                        for c in 0 .. numColors - 1 do
                            if c <> fixedColor then
                                let varIdx = getVarIndex v c
                                let existing = quboTerms |> Map.tryFind (varIdx, varIdx) |> Option.defaultValue 0.0
                                quboTerms <- quboTerms |> Map.add (varIdx, varIdx) (existing + penaltyWeight * 10.0)
                    | None ->
                        // Normal one-hot constraint using shared helper
                        let varIndices = [ for c in 0 .. numColors - 1 -> getVarIndex v c ]
                        let oneHotTerms = Qubo.oneHotConstraint varIndices penaltyWeight
                        
                        // Merge one-hot terms into quboTerms
                        quboTerms <- 
                            oneHotTerms 
                            |> Map.fold (fun acc key value -> 
                                let existing = acc |> Map.tryFind key |> Option.defaultValue 0.0
                                acc |> Map.add key (existing + value)) quboTerms
                
                // CONSTRAINT 2: Adjacent vertices have different colors
                // Penalty: Σ_{(i,j) ∈ E} Σ_c x_{i,c} * x_{j,c}
                
                for edge in problem.Edges do
                    let i = vertexIndexMap.[edge.Source]
                    let j = vertexIndexMap.[edge.Target]
                    
                    for c in 0 .. numColors - 1 do
                        let varIdx1 = getVarIndex i c
                        let varIdx2 = getVarIndex j c
                        
                        if varIdx1 = varIdx2 then
                            // Self-loop (should not happen in valid graph)
                            let existingLinear = quboTerms |> Map.tryFind (varIdx1, varIdx1) |> Option.defaultValue 0.0
                            quboTerms <- quboTerms |> Map.add (varIdx1, varIdx1) (existingLinear + penaltyWeight)
                        else
                            let (row, col) = (min varIdx1 varIdx2, max varIdx1 varIdx2)
                            let existingQuad = quboTerms |> Map.tryFind (row, col) |> Option.defaultValue 0.0
                            quboTerms <- quboTerms |> Map.add (row, col) (existingQuad + penaltyWeight)
                
                // CONSTRAINT 3: Minimize colors used (soft constraint, small weight)
                // Penalty: Σ_c c * max_i(x_{i,c})
                // Approximation: Add small linear penalty proportional to color index
                let colorPenaltyWeight = penaltyWeight * 0.1
                for v in 0 .. numVertices - 1 do
                    for c in 0 .. numColors - 1 do
                        let varIdx = getVarIndex v c
                        let colorPenalty = float c * colorPenaltyWeight
                        let existingLinear = quboTerms |> Map.tryFind (varIdx, varIdx) |> Option.defaultValue 0.0
                        quboTerms <- quboTerms |> Map.add (varIdx, varIdx) (existingLinear + colorPenalty)
                
                Ok ({
                    Q = quboTerms
                    NumVariables = numVars
                }, reverseMap)
        with ex ->
            Error (QuantumError.OperationError ("QuboEncoding", sprintf "Graph coloring QUBO encoding failed: %s" ex.Message))

    // ================================================================================
    // SOLUTION DECODING
    // ================================================================================

    /// Decode binary solution to color assignments
    let private decodeSolution 
        (problem: GraphColoringProblem) 
        (bitstring: int[]) 
        (reverseMap: Map<int, string * int>) 
        : GraphColoringSolution =
        
        let numColors = problem.NumColors
        
        // Decode color assignments (handle one-hot encoding)
        let colorAssignments = 
            problem.Vertices
            |> List.map (fun vertex ->
                // Find which color variable is set to 1 for this vertex
                let vertexIdx = problem.Vertices |> List.findIndex ((=) vertex)
                let assignedColors = 
                    [for c in 0 .. numColors - 1 do
                        let varIdx = vertexIdx * numColors + c
                        if varIdx < bitstring.Length && bitstring.[varIdx] = 1 then
                            yield c]
                
                // Take first assigned color (or 0 if none/multiple)
                let color = 
                    match assignedColors with
                    | [] -> 0  // No color assigned, default to color 0
                    | c :: _ -> c  // Take first color
                
                vertex, color
            )
            |> Map.ofList
        
        // Count distinct colors used
        let colorsUsed = 
            colorAssignments 
            |> Map.toList 
            |> List.map snd 
            |> List.distinct 
            |> List.length
        
        // Count conflicts (adjacent vertices with same color)
        let conflictCount =
            problem.Edges
            |> List.filter (fun edge ->
                let sourceColor = Map.find edge.Source colorAssignments
                let targetColor = Map.find edge.Target colorAssignments
                sourceColor = targetColor
            )
            |> List.length
        
        {
            ColorAssignments = colorAssignments
            ColorsUsed = colorsUsed
            ConflictCount = conflictCount
            IsValid = conflictCount = 0
            BackendName = ""
            NumShots = 0
            ElapsedMs = 0.0
            BestEnergy = 0.0
        }

    // ================================================================================
    // QAOA CONFIGURATION
    // ================================================================================

    /// QAOA configuration parameters for graph coloring
    type QaoaConfig = {
        /// Number of measurement shots
        NumShots: int
        
        /// Number of colors to use
        NumColors: int
        
        /// Initial QAOA parameters (gamma, beta) for single layer
        InitialParameters: float * float
        
        /// Penalty weight for constraint violations (default: 10.0)
        PenaltyWeight: float
    }
    
    /// Default QAOA configuration for graph coloring
    let defaultConfig (numColors: int) : QaoaConfig = {
        NumShots = 1000
        NumColors = numColors
        InitialParameters = (0.5, 0.5)
        PenaltyWeight = 10.0
    }

    // ================================================================================
    // MAIN SOLVER
    // ================================================================================

    /// Solve graph coloring problem using quantum QAOA (async version)
    /// 
    /// Parameters:
    ///   - backend: Quantum backend (LocalBackend, IonQ, Rigetti)
    ///   - problem: Graph coloring problem (vertices, edges, colors)
    ///   - config: QAOA configuration (shots, colors, parameters)
    /// 
    /// Returns: Async<Result<GraphColoringSolution, QuantumError>> - Async computation with result or error
    /// 
    /// Example:
    ///   let backend = LocalBackend.LocalBackend() :> IQuantumBackend
    ///   let problem = { Vertices = ["A"; "B"; "C"]; Edges = [...]; NumColors = 3; FixedColors = Map.empty }
    ///   let config = defaultConfig 3
    ///   async {
    ///       match! solveAsync backend problem config with
    ///       | Ok solution -> printfn "Colors used: %d" solution.ColorsUsed
    ///       | Error msg -> printfn "Error: %s" msg
    ///   }
    let solveAsync 
        (backend: BackendAbstraction.IQuantumBackend) 
        (problem: GraphColoringProblem) 
        (config: QaoaConfig) 
        : Async<Result<GraphColoringSolution, QuantumError>> = async {
        
        let startTime = DateTime.Now
        
        try
            // Step 1: Validate problem inputs
            let numQubits = problem.Vertices.Length * config.NumColors
            
            if problem.Vertices.Length = 0 then
                return Error (QuantumError.ValidationError ("numVertices", "Graph coloring problem has no vertices"))
            elif config.NumColors < 1 then
                return Error (QuantumError.ValidationError ("numColors", "Graph coloring problem must have at least 1 color"))
            elif problem.Edges.Length = 0 then
                return Error (QuantumError.ValidationError ("numEdges", "Graph coloring problem has no edges"))
            else
                // Step 2: Encode graph coloring as QUBO
                match toQubo problem config.PenaltyWeight with
                | Error err -> return Error err
                | Ok (quboMatrix, reverseMap) ->
                    
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
                    
                    // Step 7: Execute on quantum backend to get state
                    match backend.ExecuteToState circuitWrapper with
                    | Error err -> return Error err
                    | Ok state ->
                        
                        // Step 8: Perform measurements on quantum state
                        let measurements = QuantumState.measure state config.NumShots
                        
                        // Step 9: Decode measurements to color assignments
                        let solutions = 
                            measurements
                            |> Array.map (fun bitstring -> decodeSolution problem bitstring reverseMap)
                        
                        // Step 10: Find best valid solution (prioritize valid, then minimize colors)
                        let bestSolution = 
                            solutions
                            |> Array.sortBy (fun sol -> 
                                if sol.IsValid then
                                    (0, sol.ColorsUsed, sol.ConflictCount)  // Valid: minimize colors
                                else
                                    (1, sol.ConflictCount, sol.ColorsUsed)  // Invalid: minimize conflicts
                            )
                            |> Array.head
                        
                        let elapsedMs = (DateTime.Now - startTime).TotalMilliseconds
                        
                        return Ok {
                            bestSolution with
                                BackendName = "QuantumBackend"
                                NumShots = config.NumShots
                                ElapsedMs = elapsedMs
                        }
        
        with ex ->
            return Error (QuantumError.OperationError ("QuantumGraphColoringSolver", sprintf "Quantum graph coloring solve failed: %s" ex.Message))
    }

    /// Solve graph coloring problem using quantum QAOA (synchronous wrapper)
    /// 
    /// This is a synchronous wrapper around solveAsync for backward compatibility.
    /// For cloud backends (IonQ, Rigetti), prefer using solveAsync directly.
    /// 
    /// Parameters:
    ///   - backend: Quantum backend (LocalBackend, IonQ, Rigetti)
    ///   - problem: Graph coloring problem (vertices, edges, colors)
    ///   - config: QAOA configuration (shots, colors, parameters)
    /// 
    /// Returns: Ok with best coloring found, or Error with QuantumError
    /// 
    /// Example:
    ///   let backend = LocalBackend.LocalBackend() :> IQuantumBackend
    ///   let problem = { Vertices = ["A"; "B"; "C"]; Edges = [...]; NumColors = 3; FixedColors = Map.empty }
    ///   let config = defaultConfig 3
    ///   match solve backend problem config with
    ///   | Ok solution -> printfn "Colors used: %d" solution.ColorsUsed
    ///   | Error msg -> printfn "Error: %s" msg
    let solve 
        (backend: BackendAbstraction.IQuantumBackend) 
        (problem: GraphColoringProblem) 
        (config: QaoaConfig) 
        : Result<GraphColoringSolution, QuantumError> =
        solveAsync backend problem config |> Async.RunSynchronously

    // ================================================================================
    // CLASSICAL GREEDY SOLVER (for comparison)
    // ================================================================================

    /// Solve graph coloring using greedy coloring algorithm (classical)
    /// 
    /// This provides a classical baseline for comparison with quantum QAOA.
    /// Uses greedy vertex ordering with first-fit color assignment.
    /// 
    /// Typical performance: Near-optimal for many graph types
    let solveClassical (problem: GraphColoringProblem) : GraphColoringSolution =
        // Build adjacency list for efficient neighbor lookup
        let adjacencyMap = 
            problem.Vertices
            |> List.map (fun vertex ->
                let neighbors = 
                    problem.Edges
                    |> List.collect (fun edge ->
                        if edge.Source = vertex then [edge.Target]
                        elif edge.Target = vertex then [edge.Source]
                        else []
                    )
                    |> Set.ofList
                vertex, neighbors
            )
            |> Map.ofList
        
        // Greedy coloring algorithm using functional fold
        let colorAssignments =
            problem.Vertices
            |> List.fold (fun assignments vertex ->
                // Check if vertex has fixed color
                match Map.tryFind vertex problem.FixedColors with
                | Some fixedColor ->
                    assignments |> Map.add vertex fixedColor
                | None ->
                    // Find colors used by neighbors
                    let neighbors = Map.find vertex adjacencyMap
                    let neighborColors = 
                        neighbors
                        |> Set.toList
                        |> List.choose (fun neighbor -> Map.tryFind neighbor assignments)
                        |> Set.ofList
                    
                    // Assign first available color (not used by any neighbor)
                    let assignedColor = 
                        seq { 0 .. problem.NumColors - 1 }
                        |> Seq.find (fun color -> not (Set.contains color neighborColors))
                    
                    assignments |> Map.add vertex assignedColor
            ) Map.empty
        
        // Count distinct colors used
        let colorsUsed = 
            colorAssignments 
            |> Map.toList 
            |> List.map snd 
            |> List.distinct 
            |> List.length
        
        // Count conflicts (should be 0 for greedy algorithm)
        let conflictCount =
            problem.Edges
            |> List.filter (fun edge ->
                let sourceColor = Map.find edge.Source colorAssignments
                let targetColor = Map.find edge.Target colorAssignments
                sourceColor = targetColor
            )
            |> List.length
        
        {
            ColorAssignments = colorAssignments
            ColorsUsed = colorsUsed
            ConflictCount = conflictCount
            IsValid = conflictCount = 0
            BackendName = "Classical Greedy"
            NumShots = 0
            ElapsedMs = 0.0
            BestEnergy = 0.0
        }
