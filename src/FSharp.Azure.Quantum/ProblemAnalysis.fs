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
