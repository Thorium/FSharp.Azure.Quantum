namespace FSharp.Azure.Quantum.Quantum

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Core

/// Quantum TSP Solver using QAOA and Backend Abstraction
/// 
/// IMPORTANT: Requires a quantum backend (IonQ, Rigetti, or Local).
/// - Execution: Quantum hardware/simulator via backend
/// - Algorithm: QAOA (Quantum Approximate Optimization Algorithm)
/// - Speed: Slower (seconds to minutes, includes job queue wait)
/// - Cost: ~$10-100 per run on real quantum hardware
///
/// Implements the full quantum pipeline:
/// 1. TSP Distance Matrix → GraphOptimization Problem
/// 2. GraphOptimization → QUBO Matrix  
/// 3. QUBO → QAOA Circuit (Hamiltonians + Layers)
/// 4. Execute on Quantum Backend (IonQ/Rigetti/Local)
/// 5. Decode Measurements → TSP Tours
/// 6. Return Best Solution
///
/// Example:
///   let! result = QuantumTspSolver.solve rigettiBackend distances 1000
module QuantumTspSolver =

    /// Convert sparse QUBO matrix (Map) to dense 2D array
    let private quboMapToArray (quboMatrix: GraphOptimization.QuboMatrix) : float[,] =
        let n = quboMatrix.NumVariables
        let dense = Array2D.zeroCreate n n
        
        for KeyValue((i, j), value) in quboMatrix.Q do
            dense.[i, j] <- value
        
        dense

    /// Quantum TSP solution with execution details
    type QuantumTspSolution = {
        /// Best tour found
        Tour: int array
        
        /// Tour length (distance)
        TourLength: float
        
        /// Backend used for execution
        BackendName: string
        
        /// Number of measurement shots
        NumShots: int
        
        /// Execution time in milliseconds
        ElapsedMs: float
        
        /// QUBO objective value (energy)
        BestEnergy: float
        
        /// Top N solutions with frequencies (tour, length, count)
        TopSolutions: (int array * float * int) list
    }

    /// Solve TSP using quantum backend via QAOA
    /// 
    /// Full Pipeline:
    /// 1. Distance matrix → GraphOptimization problem
    /// 2. GraphOptimization → QUBO matrix
    /// 3. QUBO → QaoaCircuit (Hamiltonians + layers)
    /// 4. Execute circuit on quantum backend
    /// 5. Decode measurements → tours
    /// 6. Return best tour
    /// 
    /// Parameters:
    ///   backend - Quantum backend to execute on (LocalBackend, IonQ, Rigetti)
    ///   distances - Distance matrix between cities (NxN)
    ///   numShots - Number of measurement shots (default: 1000)
    ///   
    /// Returns:
    ///   Result with QuantumTspSolution or error message
    let solve 
        (backend: BackendAbstraction.IQuantumBackend)
        (distances: float[,])
        (numShots: int)
        : Result<QuantumTspSolution, string> =
        
        let startTime = DateTime.UtcNow
        
        // Validate inputs
        let numCities = distances.GetLength(0)
        let requiredQubits = numCities * numCities  // TSP uses N^2 qubits for N cities
        
        if numCities < 2 then
            Error "TSP requires at least 2 cities"
        elif requiredQubits > backend.MaxQubits then
            Error (sprintf "Problem requires %d qubits but backend '%s' supports max %d qubits" 
                requiredQubits backend.Name backend.MaxQubits)
        elif numShots <= 0 then
            Error "Number of shots must be positive"
        else
            try
                // Step 1: Build GraphOptimization problem from distance matrix
                let nodes = 
                    [0 .. numCities - 1]
                    |> List.map (fun i -> GraphOptimization.node (string i) i)
                
                let edges = 
                    [for i in 0 .. numCities - 1 do
                        for j in i + 1 .. numCities - 1 do
                            yield GraphOptimization.edge (string i) (string j) distances.[i, j]]
                
                let problem =
                    GraphOptimization.GraphOptimizationBuilder()
                        .Nodes(nodes)
                        .Edges(edges)
                        .Objective(GraphOptimization.MinimizeTotalWeight)
                        .Build()
                
                // Step 2: Convert to QUBO matrix
                let quboMatrix = GraphOptimization.toQubo problem
                
                // Step 3: Generate QAOA circuit from QUBO
                // Convert sparse QUBO map to dense array for QaoaCircuit
                let quboArray = quboMapToArray quboMatrix
                let problemHam = QaoaCircuit.ProblemHamiltonian.fromQubo quboArray
                let mixerHam = QaoaCircuit.MixerHamiltonian.create problemHam.NumQubits
                
                // Build QAOA circuit with p=1 layer
                // TODO: Optimize parameters (gamma, beta) using classical optimizer
                let parameters = [| (0.5, 0.5) |]  // (gamma, beta) for 1 layer
                let qaoaCircuit = QaoaCircuit.QaoaCircuit.build problemHam mixerHam parameters
                
                // Step 4: Wrap circuit and execute on backend
                let circuitWrapper = 
                    CircuitAbstraction.QaoaCircuitWrapper(qaoaCircuit) 
                    :> CircuitAbstraction.ICircuit
                
                match backend.Execute circuitWrapper numShots with
                | Error msg -> Error (sprintf "Backend execution failed: %s" msg)
                | Ok execResult ->
                    // Step 5: Decode measurements to tours
                    let tourResults =
                        execResult.Measurements
                        |> Array.map (fun bitstring ->
                            // Convert bitstring to QUBO solution
                            let quboSolution = Array.toList bitstring
                            
                            // Decode to graph solution
                            let graphSolution = GraphOptimization.decodeSolution problem quboSolution
                            
                            // Extract tour from selected edges
                            match graphSolution.SelectedEdges with
                            | Some edges when edges.Length > 0 ->
                                // Build tour from edges (order may vary, reconstruct sequence)
                                let rec buildTour currentCity visited path =
                                    if List.length visited = numCities then
                                        List.rev path
                                    else
                                        // Find edge from current city
                                        let nextEdge = 
                                            edges 
                                            |> List.tryFind (fun e -> 
                                                (e.Source = string currentCity || e.Target = string currentCity) &&
                                                not (List.contains (int e.Source) visited && List.contains (int e.Target) visited))
                                        
                                        match nextEdge with
                                        | Some edge ->
                                            let nextCity = 
                                                if edge.Source = string currentCity then int edge.Target 
                                                else int edge.Source
                                            buildTour nextCity (nextCity :: visited) (nextCity :: path)
                                        | None ->
                                            // Incomplete tour, pad with missing cities in order
                                            let missing = [0 .. numCities - 1] |> List.filter (fun c -> not (List.contains c visited))
                                            List.rev path @ missing
                                
                                let tour = buildTour 0 [0] [0] |> Array.ofList
                                let tourLength = TspSolver.calculateTourLength distances tour
                                Some (tour, tourLength)
                            | _ -> None
                        )
                        |> Array.choose id
                    
                    if tourResults.Length = 0 then
                        Error "No valid tours found in quantum measurements"
                    else
                        // Group by tour and count frequencies
                        let tourFrequencies =
                            tourResults
                            |> Array.groupBy fst
                            |> Array.map (fun (tour, instances) ->
                                let frequency = instances.Length
                                let length = instances.[0] |> snd
                                (tour, length, frequency))
                            |> Array.sortBy (fun (_, length, _) -> length)
                        
                        // Best tour (shortest)
                        let (bestTour, bestLength, _) = tourFrequencies.[0]
                        
                        let elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                        
                        Ok {
                            Tour = bestTour
                            TourLength = bestLength
                            BackendName = backend.Name
                            NumShots = numShots
                            ElapsedMs = elapsedMs
                            BestEnergy = bestLength  // For TSP, tour length is the energy
                            TopSolutions = 
                                tourFrequencies 
                                |> Array.take (min 5 tourFrequencies.Length)
                                |> Array.toList
                        }
            
            with ex ->
                Error (sprintf "Quantum TSP solver failed: %s" ex.Message)

    /// Solve TSP with default parameters (1000 shots, local backend)
    let solveWithDefaults (distances: float[,]) : Result<QuantumTspSolution, string> =
        let backend = BackendAbstraction.createLocalBackend()
        solve backend distances 1000
