namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Classical

/// High-level TSP Domain Builder
/// Provides intuitive API for solving Traveling Salesman Problems
/// Routes through HybridSolver for consistent quantum-classical decision making
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

    /// Solve TSP problem using HybridSolver (automatic quantum-classical routing)
    /// Routes through HybridSolver for intelligent method selection
    /// Optional config parameter is currently ignored (HybridSolver uses defaults)
    /// Returns Result with Tour or error message
    /// Example:
    ///   let tour = TSP.solve problem
    ///   let customTour = TSP.solve problem (Some customConfig)
    let solve (problem: TspProblem) (config: TspSolver.TspConfig option) : Result<Tour, string> =
        // Route through HybridSolver for consistent quantum-classical decision making
        // This ensures all solving goes through the same routing logic
        match HybridSolver.solveTsp problem.DistanceMatrix None None None with
        | Error msg -> Error $"TSP solve failed: {msg}"
        | Ok hybridResult ->
            try
                // Validate tour
                let valid = isValidTour hybridResult.Result.Tour problem.CityCount
                
                // Convert tour indices to city names
                let cityNames = 
                    hybridResult.Result.Tour 
                    |> Array.map (fun idx -> 
                        match problem.Cities.[idx].Name with
                        | Some name -> name
                        | None -> $"City {idx}")
                    |> Array.toList
                
                Ok {
                    Cities = cityNames
                    TotalDistance = hybridResult.Result.TourLength
                    IsValid = valid
                }
            with
            | ex -> Error $"TSP result conversion failed: {ex.Message}"

    /// Convenience function: Create problem and solve in one step
    /// Input: List of (name, x, y) tuples
    /// Optional config parameter allows customization
    /// Output: Result with Tour or error message
    /// Example:
    ///   let tour = TSP.solveDirectly [("A", 0.0, 0.0); ("B", 1.0, 0.0)] None
    let solveDirectly (cities: (string * float * float) list) (config: TspSolver.TspConfig option) : Result<Tour, string> =
        let problem = createProblem cities
        solve problem config
