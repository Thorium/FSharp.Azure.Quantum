namespace FSharp.Azure.Quantum

/// High-level TSP Domain Builder
/// Provides intuitive API for solving Traveling Salesman Problems
/// without requiring knowledge of QUBO encoding or quantum circuits
module TSP =

    // ============================================================================
    // TYPES - Domain-specific types for TSP problems
    // ============================================================================

    /// Named city with coordinates
    type City = {
        Name: string
        X: float
        Y: float
    }

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

    /// Calculate Euclidean distance between two cities
    let private euclideanDistance (c1: City) (c2: City) : float =
        let dx = c2.X - c1.X
        let dy = c2.Y - c1.Y
        sqrt (dx * dx + dy * dy)

    /// Build distance matrix from cities
    let private buildDistanceMatrix (cities: City array) : float[,] =
        let n = cities.Length
        Array2D.init n n (fun i j ->
            if i = j then 0.0
            else euclideanDistance cities.[i] cities.[j])

    // ============================================================================
    // PUBLIC API
    // ============================================================================

    /// Create TSP problem from list of named cities with coordinates
    /// Input: List of (name, x, y) tuples
    /// Output: TspProblem ready for solving
    let createProblem (cities: (string * float * float) list) : TspProblem =
        let cityArray =
            cities
            |> List.map (fun (name, x, y) -> { Name = name; X = x; Y = y })
            |> List.toArray
        
        let distanceMatrix = buildDistanceMatrix cityArray
        
        {
            Cities = cityArray
            CityCount = cityArray.Length
            DistanceMatrix = distanceMatrix
        }
