namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Quantum
open FSharp.Azure.Quantum.Core

/// High-level TSP Domain Builder - Quantum-First API
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for users who want to solve TSP problems
/// without understanding quantum computing internals (QAOA, QUBO, backends).
/// 
/// QUANTUM-FIRST:
/// - Uses quantum optimization (QAOA) by default via LocalBackend (simulation)
/// - Optional backend parameter for cloud quantum hardware (IonQ, Rigetti)
/// - For algorithm-level control, use QuantumTspSolver directly
/// 
/// EXAMPLE USAGE:
///   // Simple: Uses quantum simulation automatically
///   let tour = TSP.solve cities None
///   
///   // Advanced: Specify cloud quantum backend
///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
///   let tour = TSP.solve cities (Some ionqBackend)
///   
///   // Expert: Direct quantum solver access
///   open FSharp.Azure.Quantum.Quantum
///   let result = QuantumTspSolver.solve backend distances config
module TSP =

    // ============================================================================
    // TYPES - Domain-specific types for TSP problems
    // ============================================================================

    // City type is now in shared TspTypes module
    type City = TspTypes.City

    /// TSP Problem representation
    type TspProblem = {
        Cities: City array
        CityCount: int
        DistanceMatrix: float[,]
    }

    /// Tour result with ordered cities and total distance
    type Tour = {
        Cities: string list
        TotalDistance: float
        IsValid: bool
    }

    // ============================================================================
    // DISTANCE CALCULATIONS
    // ============================================================================

    /// Build distance matrix from cities
    let private buildDistanceMatrix (cities: City array) : float[,] =
        let n = cities.Length
        Array2D.init n n (fun i j ->
            if i = j then 0.0
            else TspTypes.distance cities.[i] cities.[j])

    // ============================================================================
    // TOUR VALIDATION
    // ============================================================================

    /// Validate that a tour visits all cities exactly once
    let private isValidTour (tour: int array) (cityCount: int) : bool =
        if tour.Length <> cityCount then false
        else
            let visited = tour |> Set.ofArray
            visited.Count = cityCount && tour |> Array.forall (fun i -> i >= 0 && i < cityCount)

    // ============================================================================
    // PUBLIC API
    // ============================================================================

    /// Create TSP problem from list of named cities with coordinates
    /// Input: List of (name, x, y) tuples
    /// Output: TspProblem ready for solving
    /// Example:
    ///   let problem = TSP.createProblem [("A", 0.0, 0.0); ("B", 1.0, 0.0); ("C", 0.0, 1.0)]
    let createProblem (cities: (string * float * float) list) : TspProblem =
        let cityArray =
            cities
            |> List.map (fun (name, x, y) -> TspTypes.createNamed name x y)
            |> List.toArray
        
        let distanceMatrix = buildDistanceMatrix cityArray
        
        {
            Cities = cityArray
            CityCount = cityArray.Length
            DistanceMatrix = distanceMatrix
        }

    /// Solve TSP problem using quantum optimization (QAOA)
    /// 
    /// QUANTUM-FIRST API:
    /// - Uses quantum backend by default (LocalBackend for simulation)
    /// - Specify custom backend for cloud quantum hardware (IonQ, Rigetti)
    /// - Returns business-domain Tour result (not low-level QAOA output)
    /// 
    /// PARAMETERS:
    ///   problem - TSP problem with cities and distance matrix
    ///   backend - Optional quantum backend (defaults to LocalBackend if None)
    /// 
    /// EXAMPLES:
    ///   // Simple: Automatic quantum simulation
    ///   let tour = TSP.solve problem None
    ///   
    ///   // Cloud execution: Specify IonQ backend
    ///   let ionqBackend = BackendAbstraction.createIonQBackend(...)
    ///   let tour = TSP.solve problem (Some ionqBackend)
    /// 
    /// RETURNS:
    ///   QuantumResult with Tour (city names, distance, validity) or QuantumError
    let solve (problem: TspProblem) (backend: BackendAbstraction.IQuantumBackend option) : QuantumResult<Tour> =
        try
            // Use provided backend or create LocalBackend for simulation
            let actualBackend = 
                backend 
                |> Option.defaultValue (BackendAbstraction.createLocalBackend())
            
            // Create quantum TSP solver configuration
            let quantumConfig : QuantumTspSolver.QuantumTspConfig = {
                OptimizationShots = 100
                FinalShots = 1000
                EnableOptimization = true
                InitialParameters = (0.5, 0.5)
            }
            
            // Call quantum TSP solver directly using computation expression
            quantumResult {
                let! quantumResult = QuantumTspSolver.solve actualBackend problem.DistanceMatrix quantumConfig
                
                // Validate tour
                let valid = isValidTour quantumResult.Tour problem.CityCount
                
                // Convert tour indices to city names
                let cityNames = 
                    quantumResult.Tour 
                    |> Array.map (fun idx -> 
                        match problem.Cities.[idx].Name with
                        | Some name -> name
                        | None -> $"City {idx}")
                    |> Array.toList
                
                return {
                    Cities = cityNames
                    TotalDistance = quantumResult.TourLength
                    IsValid = valid
                }
            }
        with
        | ex -> Error (QuantumError.OperationError ("TSP solve", $"Failed: {ex.Message}"))

    /// Convenience function: Create problem and solve in one step using quantum optimization
    /// 
    /// PARAMETERS:
    ///   cities - List of (name, x, y) tuples defining city locations
    ///   backend - Optional quantum backend (defaults to LocalBackend if None)
    /// 
    /// RETURNS:
    ///   Result with Tour or error message
    /// 
    /// EXAMPLE:
    ///   let tour = TSP.solveDirectly [("A", 0.0, 0.0); ("B", 1.0, 0.0)] None
    let solveDirectly (cities: (string * float * float) list) (backend: BackendAbstraction.IQuantumBackend option) : QuantumResult<Tour> =
        let problem = createProblem cities
        solve problem backend
