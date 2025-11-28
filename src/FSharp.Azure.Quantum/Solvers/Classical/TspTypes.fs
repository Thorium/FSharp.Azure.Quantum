namespace FSharp.Azure.Quantum

/// <summary>
/// Shared types for TSP (Traveling Salesman Problem) across both Classical and Quantum solvers
/// </summary>
module TspTypes =

    /// <summary>
    /// Represents a city with coordinates and optional name
    /// </summary>
    type City = {
        /// Optional city name (e.g., "New York", "London")
        /// Use None for anonymous cities
        Name: string option
        
        /// X coordinate
        X: float
        
        /// Y coordinate
        Y: float
    }
    
    /// <summary>
    /// Create a named city
    /// </summary>
    let createNamed (name: string) (x: float) (y: float) : City =
        { Name = Some name; X = x; Y = y }
    
    /// <summary>
    /// Create an anonymous city from coordinates
    /// </summary>
    let create (x: float) (y: float) : City =
        { Name = None; X = x; Y = y }
    
    /// <summary>
    /// Convert from coordinate tuple to City
    /// </summary>
    let fromTuple ((x, y): float * float) : City =
        { Name = None; X = x; Y = y }
    
    /// <summary>
    /// Convert City to coordinate tuple (for legacy compatibility)
    /// </summary>
    let toTuple (city: City) : float * float =
        (city.X, city.Y)
    
    /// <summary>
    /// Calculate Euclidean distance between two cities
    /// </summary>
    let distance (c1: City) (c2: City) : float =
        let dx = c1.X - c2.X
        let dy = c1.Y - c2.Y
        sqrt (dx * dx + dy * dy)
