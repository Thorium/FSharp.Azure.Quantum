namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Classical

/// High-level TSP (Traveling Salesman Problem) Domain Builder
/// 
/// Provides an intuitive API for solving TSP problems without requiring
/// knowledge of QUBO encoding or quantum circuits. Uses classical 2-opt
/// algorithm for optimization.
module TSP =
    
    /// Named city with coordinates
    type City =
        {
            /// City name
            Name: string
            
            /// X coordinate
            X: float
            
            /// Y coordinate
            Y: float
        }
    
    /// TSP Problem representation
    type TspProblem =
        {
            /// Array of cities in the problem
            Cities: City array
            
            /// Number of cities
            CityCount: int
            
            /// Pairwise distance matrix
            DistanceMatrix: float[,]
        }
    
    /// Tour result with ordered cities and total distance
    type Tour =
        {
            /// Ordered list of city names in the tour
            Cities: string list
            
            /// Total distance of the tour
            TotalDistance: float
            
            /// Whether the tour is valid (visits all cities once)
            IsValid: bool
        }
    
    /// Create TSP problem from list of named cities with coordinates
    /// 
    /// Parameters:
    /// - cities: List of (name, x, y) tuples representing cities
    /// 
    /// Returns: TspProblem ready for solving
    /// 
    /// Example:
    /// ```fsharp
    /// let problem = TSP.createProblem [("A", 0.0, 0.0); ("B", 1.0, 0.0); ("C", 0.0, 1.0)]
    /// ```
    val createProblem: cities: (string * float * float) list -> TspProblem
    
    /// Solve TSP problem using classical 2-opt algorithm
    /// 
    /// Parameters:
    /// - problem: TspProblem to solve
    /// - config: Optional configuration for solver behavior
    /// 
    /// Returns: Result with Tour or error message
    /// 
    /// Example:
    /// ```fsharp
    /// let tour = TSP.solve problem None
    /// let customTour = TSP.solve problem (Some customConfig)
    /// ```
    val solve: problem: TspProblem -> config: TspSolver.TspConfig option -> Result<Tour, string>
    
    /// Convenience function: Create problem and solve in one step
    /// 
    /// Parameters:
    /// - cities: List of (name, x, y) tuples
    /// - config: Optional configuration for solver behavior
    /// 
    /// Returns: Result with Tour or error message
    /// 
    /// Example:
    /// ```fsharp
    /// let tour = TSP.solveDirectly [("A", 0.0, 0.0); ("B", 1.0, 0.0)] None
    /// ```
    val solveDirectly: cities: (string * float * float) list -> config: TspSolver.TspConfig option -> Result<Tour, string>
