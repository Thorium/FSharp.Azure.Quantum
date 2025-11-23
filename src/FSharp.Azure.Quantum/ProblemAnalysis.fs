namespace FSharp.Azure.Quantum.Classical

open System

/// Problem classification and complexity analysis module for quantum vs classical decision-making
module ProblemAnalysis =
    
    /// Supported problem types for quantum optimization
    type ProblemType =
        | TSP           // Traveling Salesman Problem
        | Portfolio     // Portfolio Optimization
        | QUBO          // Quadratic Unconstrained Binary Optimization (generic)
        | Unknown       // Cannot classify
    
    /// Problem characteristics and metadata
    type ProblemInfo = {
        /// Classified problem type
        ProblemType: ProblemType
        
        /// Problem size (number of variables/nodes/assets)
        Size: int
        
        /// Search space complexity (e.g., "O(n!)", "O(2^n)")
        Complexity: string
        
        /// Estimated search space size (approximate number of solutions)
        SearchSpaceSize: float
        
        /// Whether the problem is symmetric
        IsSymmetric: bool
        
        /// Density of constraints/connections (0.0 to 1.0)
        Density: float
    }
    
    /// Validate that a 2D array is not null
    let private validateNotNull (matrix: float[,]) : Result<unit, string> =
        if isNull (box matrix) then
            Error "Distance matrix cannot be null"
        else
            Ok ()
    
    /// Validate that a matrix is not empty
    let private validateNotEmpty (matrix: float[,]) : Result<unit, string> =
        let rows = Array2D.length1 matrix
        let cols = Array2D.length2 matrix
        if rows = 0 || cols = 0 then
            Error "Distance matrix cannot be empty"
        else
            Ok ()
    
    /// Validate that a matrix is square
    let private validateSquare (matrix: float[,]) : Result<unit, string> =
        let rows = Array2D.length1 matrix
        let cols = Array2D.length2 matrix
        if rows <> cols then
            Error $"Distance matrix must be square (got {rows}x{cols} dimensions)"
        else
            Ok ()
    
    /// Validate that all matrix values are valid (not NaN, not infinity, non-negative)
    let private validateValues (matrix: float[,]) : Result<unit, string> =
        let rows = Array2D.length1 matrix
        let cols = Array2D.length2 matrix
        
        let mutable hasInvalid = false
        let mutable errorMsg = ""
        
        for i in 0 .. rows - 1 do
            for j in 0 .. cols - 1 do
                let value = matrix.[i, j]
                if Double.IsNaN(value) then
                    hasInvalid <- true
                    errorMsg <- "Distance matrix contains NaN values"
                elif Double.IsInfinity(value) then
                    hasInvalid <- true
                    errorMsg <- "Distance matrix contains infinity values"
                elif value < 0.0 then
                    hasInvalid <- true
                    errorMsg <- $"Distance matrix contains negative values (found {value} at position [{i},{j}])"
        
        if hasInvalid then
            Error errorMsg
        else
            Ok ()
    
    /// Check if a matrix is symmetric
    let private isMatrixSymmetric (matrix: float[,]) : bool =
        let n = Array2D.length1 matrix
        let mutable symmetric = true
        
        for i in 0 .. n - 1 do
            for j in i + 1 .. n - 1 do
                if abs(matrix.[i, j] - matrix.[j, i]) > 1e-9 then
                    symmetric <- false
        
        symmetric
    
    /// Calculate matrix density (ratio of non-zero elements)
    let private calculateDensity (matrix: float[,]) : float =
        let rows = Array2D.length1 matrix
        let cols = Array2D.length2 matrix
        let mutable nonZeroCount = 0
        
        for i in 0 .. rows - 1 do
            for j in 0 .. cols - 1 do
                if abs(matrix.[i, j]) > 1e-9 then
                    nonZeroCount <- nonZeroCount + 1
        
        float nonZeroCount / float (rows * cols)
    
    /// Calculate factorial for search space estimation
    let private factorial (n: int) : float =
        if n <= 0 then 1.0
        elif n > 170 then Double.PositiveInfinity  // Overflow protection
        else
            let mutable result = 1.0
            for i in 2 .. n do
                result <- result * float i
            result
    
    /// Classify a distance matrix problem
    let private classifyDistanceMatrix (matrix: float[,]) : Result<ProblemInfo, string> =
        // Validate inputs with comprehensive error checking
        match validateNotNull matrix with
        | Error msg -> Error msg
        | Ok () ->
            match validateNotEmpty matrix with
            | Error msg -> Error msg
            | Ok () ->
                match validateSquare matrix with
                | Error msg -> Error msg
                | Ok () ->
                    match validateValues matrix with
                    | Error msg -> Error msg
                    | Ok () ->
                        // All validations passed - analyze the matrix
                        let n = Array2D.length1 matrix
                        let isSymmetric = isMatrixSymmetric matrix
                        let density = calculateDensity matrix
                        
                        // Classify as TSP (distance matrix characteristics)
                        let searchSpaceSize = factorial n
                        
                        Ok {
                            ProblemType = TSP
                            Size = n
                            Complexity = "O(n!)"
                            SearchSpaceSize = searchSpaceSize
                            IsSymmetric = isSymmetric
                            Density = density
                        }
    
    /// Classify a problem from its input representation
    /// Returns Result with ProblemInfo or error message
    let classifyProblem (input: 'T) : Result<ProblemInfo, string> =
        // Handle null first before type checking
        let boxed = box input
        if isNull boxed then
            Error "Input cannot be null"
        else
            match boxed with
            | :? (float[,]) as matrix -> classifyDistanceMatrix matrix
            | _ -> Error $"Cannot classify input of type {typeof<'T>.Name}"
    
    /// Quantum advantage estimation result
    type QuantumAdvantage = {
        /// Problem size
        ProblemSize: int
        
        /// Estimated classical solver time (milliseconds)
        EstimatedClassicalTimeMs: float
        
        /// Estimated quantum solver time (milliseconds)
        EstimatedQuantumTimeMs: float
        
        /// Quantum speedup factor (classical time / quantum time)
        QuantumSpeedup: float
        
        /// Recommendation: "quantum" or "classical"
        Recommendation: string
        
        /// Detailed reasoning for the recommendation
        Reasoning: string
    }
    
    /// Estimate quantum advantage for a given problem
    /// Returns Result with QuantumAdvantage or error message
    let estimateQuantumAdvantage (input: 'T) : Result<QuantumAdvantage, string> =
        // First classify the problem
        match classifyProblem input with
        | Error msg -> Error msg
        | Ok problemInfo ->
            let n = problemInfo.Size
            
            // Heuristic time estimates based on problem type and size
            let classicalTimeMs = 
                match problemInfo.ProblemType with
                | TSP ->
                    // Classical TSP: O(n!) complexity
                    // For small n, use exact algorithm time estimates
                    // For large n, use heuristic approximations
                    if n <= 10 then
                        // Small: 2-opt can solve quickly (< 1 second)
                        float n * float n * 0.1
                    elif n <= 20 then
                        // Medium: exponential growth (seconds to minutes)
                        float n * float n * float n * 0.5
                    elif n <= 50 then
                        // Large: practical limit for exact solutions (minutes to hours)
                        pown (float n) 4 * 2.0
                    else
                        // Very large: impractical for exact solutions (hours to days)
                        pown (float n) 5 * 5.0
                | Portfolio ->
                    // Portfolio optimization: typically O(n^3) for mean-variance
                    float n * float n * float n * 0.01
                | QUBO | Unknown ->
                    // Generic: assume exponential complexity O(2^n)
                    if n <= 20 then
                        pown 2.0 n * 0.1
                    else
                        Double.PositiveInfinity  // Too large
            
            let quantumTimeMs =
                match problemInfo.ProblemType with
                | TSP ->
                    // Quantum annealing for TSP: polynomial-time heuristic
                    // Real quantum annealers don't achieve full Grover speedup but much better than classical
                    if n <= 10 then
                        // Small problems: quantum overhead dominates
                        float n * float n * 1.0
                    elif n <= 20 then
                        // Medium: quantum starts to show advantage
                        float n * float n * float n * 0.1 + 100.0
                    elif n <= 50 then
                        // Large: significant quantum advantage with polynomial scaling
                        float n * float n * float n * 0.5 + 150.0
                    else
                        // Very large: quantum provides practical solution
                        pown (float n) 4 * 0.1 + 200.0
                | Portfolio ->
                    // Quantum speedup for portfolio: quadratic advantage
                    float n * float n * 0.05 + 50.0  // 50ms overhead
                | QUBO | Unknown ->
                    // Generic quantum annealing
                    if n <= 20 then
                        sqrt (pown 2.0 n) * 0.5 + 100.0
                    else
                        sqrt (pown 2.0 20) * 0.5 + 100.0  // Cap estimate
            
            let speedup = 
                if quantumTimeMs > 0.0 && not (Double.IsInfinity classicalTimeMs) then
                    classicalTimeMs / quantumTimeMs
                else
                    1.0
            
            // Recommendation logic
            let (recommendation, reasoning) =
                if n < 10 then
                    ("classical", 
                     $"Small problem (n={n}). Classical algorithms are faster due to quantum overhead.")
                elif speedup < 2.0 then
                    ("classical", 
                     $"Quantum speedup ({speedup:F2}x) does not justify quantum hardware costs.")
                elif speedup < 10.0 then
                    ("quantum", 
                     $"Moderate quantum advantage ({speedup:F2}x speedup). Consider quantum if available.")
                else
                    ("quantum", 
                     $"Significant quantum advantage ({speedup:F2}x speedup). Strong recommendation for quantum.")
            
            Ok {
                ProblemSize = n
                EstimatedClassicalTimeMs = classicalTimeMs
                EstimatedQuantumTimeMs = quantumTimeMs
                QuantumSpeedup = speedup
                Recommendation = recommendation
                Reasoning = reasoning
            }
