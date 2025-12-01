namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Core

/// High-level Quantum Pattern Matcher Builder - Quantum-First API
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for finding items in a search space that match a pattern
/// without understanding Grover's algorithm internals (oracles, qubits, amplitude amplification).
/// 
/// QUANTUM-FIRST:
/// - Uses Grover's algorithm via quantum backends by default (LocalBackend for simulation)
/// - Optional backend parameter for cloud quantum hardware (IonQ, Rigetti)
/// - For algorithm-level control, use GroverSearch module directly
/// 
/// WHAT IS PATTERN MATCHING SEARCH:
/// Find items in a search space that satisfy a pattern predicate (expensive evaluation).
/// Uses quantum search to accelerate exploration when evaluation is computationally expensive.
/// 
/// USE CASES:
/// - Configuration optimization (find best system config from 100+ options)
/// - Hyperparameter tuning (ML model parameters)
/// - Feature selection (choose best features from large set)
/// - System tuning (database configs, compiler flags, etc.)
/// - A/B testing at scale
/// 
/// EXAMPLE USAGE:
///   // Simple: Find configurations meeting performance criteria
///   let problem = patternMatcher {
///       searchSpace allConfigurations
///       matchPattern (fun config ->
///           let perf = runBenchmark config  // Expensive: 10 seconds
///           perf.Throughput > 1000.0 && perf.Latency < 50.0
///       )
///       findTop 10
///   }
///   
///   // Advanced: ML hyperparameter search
///   let problem = patternMatcher {
///       searchSpace 256  // 256 hyperparameter combinations
///       matchPattern (fun idx ->
///           let params = decodeHyperparameters idx
///           let accuracy = trainModel params  // Expensive!
///           accuracy > 0.95
///       )
///       findTop 5
///       backend azureQuantum
///   }
///   
///   // Solve the problem
///   match QuantumPatternMatcher.solve problem with
///   | Ok solution -> printfn "Matches: %A" solution.Matches
///   | Error msg -> printfn "Error: %s" msg
module QuantumPatternMatcher =
    
    // ============================================================================
    // CORE TYPES - Pattern Matching Domain Model
    // ============================================================================
    
    /// <summary>
    /// Complete quantum pattern matching problem specification.
    /// </summary>
    type PatternProblem<'T> = {
        /// Search space (list of items to search) OR size as integer
        SearchSpace: Choice<'T list, int>
        /// Pattern predicate (returns true if item matches)
        Pattern: 'T -> bool
        /// Number of top matches to return
        TopN: int
        /// Quantum backend to use (None = LocalBackend)
        Backend: BackendAbstraction.IQuantumBackend option
        /// Maximum iterations for Grover search
        MaxIterations: int option
        /// Number of measurement shots
        Shots: int
    }
    
    /// <summary>
    /// Solution to a pattern matching problem.
    /// </summary>
    type PatternSolution<'T> = {
        /// Items that matched the pattern
        Matches: 'T list
        /// Success probability of the search
        SuccessProbability: float
        /// Backend used for execution
        BackendName: string
        /// Qubits required for this search
        QubitsRequired: int
        /// Number of Grover iterations used
        IterationsUsed: int
        /// Total items searched
        SearchSpaceSize: int
    }
    
    // ============================================================================
    // VALIDATION HELPERS
    // ============================================================================
    
    /// <summary>
    /// Validates a pattern matching problem specification.
    /// </summary>
    let validate (problem: PatternProblem<'T>) : Result<unit, string> =
        let searchSpaceSize = 
            match problem.SearchSpace with
            | Choice1Of2 items -> List.length items
            | Choice2Of2 size -> size
        
        if searchSpaceSize < 1 then
            Error "SearchSpace must contain at least 1 item"
        elif searchSpaceSize > (1 <<< 16) then
            Error $"SearchSpace size {searchSpaceSize} exceeds maximum (2^16 = 65536)"
        elif problem.TopN < 1 then
            Error "TopN must be at least 1"
        elif problem.TopN > searchSpaceSize then
            Error $"TopN ({problem.TopN}) cannot exceed search space size ({searchSpaceSize})"
        elif problem.Shots < 1 then
            Error "Shots must be at least 1"
        else
            let qubitsNeeded = int (ceil (log (float searchSpaceSize) / log 2.0))
            if qubitsNeeded > 16 then
                Error $"Problem requires {qubitsNeeded} qubits (search space {searchSpaceSize}). Max: 16. Reduce search space size."
            else
                Ok ()
    
    // ============================================================================
    // COMPUTATION EXPRESSION BUILDER - Pattern Matcher Builder
    // ============================================================================
    
    /// <summary>
    /// Computation expression builder for defining pattern matching problems.
    /// </summary>
    type QuantumPatternMatcherBuilder<'T>() =
        
        member _.Yield(_) : PatternProblem<'T> =
            {
                SearchSpace = Choice2Of2 16  // Default: 16 items
                Pattern = fun _ -> false
                TopN = 1
                Backend = None
                MaxIterations = None
                Shots = 1000
            }
        
        member _.Delay(f: unit -> PatternProblem<'T>) : unit -> PatternProblem<'T> = f
        
        member _.Run(f: unit -> PatternProblem<'T>) : PatternProblem<'T> =
            let problem = f()
            match validate problem with
            | Error msg -> failwith msg
            | Ok () -> problem
        
        [<CustomOperation("searchSpace")>]
        member _.SearchSpaceItems(problem: PatternProblem<'T>, items: 'T list) : PatternProblem<'T> =
            { problem with SearchSpace = Choice1Of2 items }
        
        [<CustomOperation("searchSpaceSize")>]
        member _.SearchSpaceSize(problem: PatternProblem<'T>, size: int) : PatternProblem<'T> =
            { problem with SearchSpace = Choice2Of2 size }
        
        [<CustomOperation("matchPattern")>]
        member _.MatchPattern(problem: PatternProblem<'T>, predicate: 'T -> bool) : PatternProblem<'T> =
            { problem with Pattern = predicate }
        
        [<CustomOperation("findTop")>]
        member _.FindTop(problem: PatternProblem<'T>, n: int) : PatternProblem<'T> =
            { problem with TopN = n }
        
        [<CustomOperation("backend")>]
        member _.Backend(problem: PatternProblem<'T>, backend: BackendAbstraction.IQuantumBackend) : PatternProblem<'T> =
            { problem with Backend = Some backend }
        
        [<CustomOperation("maxIterations")>]
        member _.MaxIterations(problem: PatternProblem<'T>, iters: int) : PatternProblem<'T> =
            { problem with MaxIterations = Some iters }
        
        [<CustomOperation("shots")>]
        member _.Shots(problem: PatternProblem<'T>, count: int) : PatternProblem<'T> =
            { problem with Shots = count }
    
    /// Global instance of patternMatcher builder
    let patternMatcher<'T> = QuantumPatternMatcherBuilder<'T>()
    
    // ============================================================================
    // MAIN SOLVER - QUANTUM-FIRST
    // ============================================================================
    
    /// Solve pattern matching problem using Grover's algorithm
    /// 
    /// QUANTUM-FIRST API:
    /// - Uses quantum backend by default (LocalBackend for simulation)
    /// - Specify custom backend for cloud quantum hardware (IonQ, Rigetti)
    /// - Returns business-domain Solution result
    /// 
    /// PARAMETERS:
    ///   problem - Pattern matching problem specification
    /// 
    /// EXAMPLES:
    ///   // Simple: Automatic quantum simulation
    ///   let solution = QuantumPatternMatcher.solve problem
    ///   
    ///   // Cloud execution: Problem with IonQ backend
    ///   let problem = patternMatcher {
    ///       searchSpace configs
    ///       matchPattern checkPerformance
    ///       backend ionqBackend
    ///   }
    ///   let solution = QuantumPatternMatcher.solve problem
    let solve (problem: PatternProblem<'T>) : Result<PatternSolution<'T>, string> =
        
        try
            // Validate problem first
            match validate problem with
            | Error msg -> Error msg
            | Ok () ->
                
                // Use provided backend or create LocalBackend for simulation
                let actualBackend = 
                    problem.Backend 
                    |> Option.defaultValue (BackendAbstraction.createLocalBackend())
                
                // Extract search space
                let (searchSpaceItems, searchSpaceSize) =
                    match problem.SearchSpace with
                    | Choice1Of2 items -> (Some items, List.length items)
                    | Choice2Of2 size -> (None, size)
                
                // Calculate qubits needed
                let qubitsNeeded = int (ceil (log (float searchSpaceSize) / log 2.0))
                
                // Create pattern predicate for oracle
                // If we have actual items, check pattern on item
                // If we only have size, check pattern on index (user must handle decoding)
                let oraclePredicate (idx: int) : bool =
                    match searchSpaceItems with
                    | Some items when idx < List.length items ->
                        let item = List.item idx items
                        problem.Pattern item
                    | None ->
                        // User provided only size, pattern must work on indices
                        // Cast idx to 'T (this is a limitation - works for int, not for other types)
                        // Better approach: require explicit decoder function
                        // For now, we'll try the pattern and catch errors
                        try
                            problem.Pattern (unbox idx)
                        with
                        | _ -> false
                    | _ -> false
                
                // Create oracle for Grover search
                let oracleResult = GroverSearch.Oracle.fromPredicate oraclePredicate qubitsNeeded
                match oracleResult with
                | Error msg -> Error $"Failed to create oracle: {msg}"
                | Ok oracle ->
                    
                    // Calculate optimal iterations (looking for TopN solutions)
                    let iterationsResult = GroverSearch.GroverIteration.optimalIterations searchSpaceSize problem.TopN
                    match iterationsResult with
                    | Error msg -> Error $"Failed to calculate iterations: {msg}"
                    | Ok calculatedIters ->
                        
                        let iterations = 
                            match problem.MaxIterations with
                            | Some maxIters -> min calculatedIters maxIters
                            | None -> calculatedIters
                        
                        // Execute Grover search
                        match GroverSearch.BackendAdapter.executeGroverWithBackend oracle actualBackend iterations problem.Shots 0.1 0.5 with
                        | Error msg -> Error $"Grover search failed: {msg}"
                        | Ok searchResult ->
                            
                            if List.isEmpty searchResult.Solutions then
                                Error "No matching patterns found by quantum search"
                            else
                                // Take top N solutions
                                let topIndices = searchResult.Solutions |> List.take (min problem.TopN (List.length searchResult.Solutions))
                                
                                // Convert indices to actual items (if we have the search space)
                                let matches =
                                    match searchSpaceItems with
                                    | Some items ->
                                        topIndices
                                        |> List.choose (fun idx -> 
                                            if idx < List.length items then
                                                Some (List.item idx items)
                                            else
                                                None
                                        )
                                    | None ->
                                        // Return indices as 'T (requires 'T to be compatible with int)
                                        topIndices |> List.map unbox
                                
                                let backendName = 
                                    match problem.Backend with
                                    | Some backend -> backend.GetType().Name
                                    | None -> "LocalBackend (Simulation)"
                                
                                Ok {
                                    Matches = matches
                                    SuccessProbability = searchResult.SuccessProbability
                                    BackendName = backendName
                                    QubitsRequired = qubitsNeeded
                                    IterationsUsed = iterations
                                    SearchSpaceSize = searchSpaceSize
                                }
        with
        | ex -> Error $"Pattern matcher failed: {ex.Message}"
    
    // ============================================================================
    // CONVENIENCE FUNCTIONS
    // ============================================================================
    
    /// Quick helper for simple pattern matching
    let simple (items: 'T list) (patternFunc: 'T -> bool) : PatternProblem<'T> =
        {
            SearchSpace = Choice1Of2 items
            Pattern = patternFunc
            TopN = 1
            Backend = None
            MaxIterations = None
            Shots = 1000
        }
    
    /// Find all matching items (up to reasonable limit)
    let findAll (items: 'T list) (patternFunc: 'T -> bool) : PatternProblem<'T> =
        {
            SearchSpace = Choice1Of2 items
            Pattern = patternFunc
            TopN = min 10 (List.length items)  // Cap at 10 for performance
            Backend = None
            MaxIterations = None
            Shots = 1000
        }
    
    /// Helper for configuration optimization (cloud deployments, CI/CD configs)
    let forConfigOptimization (configs: 'T list) (performanceCheck: 'T -> bool) (topN: int) : PatternProblem<'T> =
        {
            SearchSpace = Choice1Of2 configs
            Pattern = performanceCheck
            TopN = topN
            Backend = None
            MaxIterations = None
            Shots = 1000
        }
    
    /// Helper for hyperparameter tuning (ML model optimization)
    let forHyperparameterTuning (searchSpaceSize: int) (evaluator: int -> bool) (topN: int) : PatternProblem<int> =
        {
            SearchSpace = Choice2Of2 searchSpaceSize
            Pattern = evaluator
            TopN = topN
            Backend = None
            MaxIterations = None
            Shots = 1000
        }
    
    /// Helper for feature selection (dimensionality reduction)
    let forFeatureSelection (featureSets: 'T list) (modelPerformance: 'T -> bool) (topN: int) : PatternProblem<'T> =
        {
            SearchSpace = Choice1Of2 featureSets
            Pattern = modelPerformance
            TopN = topN
            Backend = None
            MaxIterations = None
            Shots = 1000
        }
    
    /// Helper for A/B test variant selection
    let forABTesting (variants: 'T list) (conversionCheck: 'T -> bool) (topN: int) : PatternProblem<'T> =
        {
            SearchSpace = Choice1Of2 variants
            Pattern = conversionCheck
            TopN = topN
            Backend = None
            MaxIterations = None
            Shots = 1000
        }
    
    /// Estimate resource requirements without executing
    let estimateResources (searchSpaceSize: int) (topN: int) : string =
        let qubits = int (ceil (log (float searchSpaceSize) / log 2.0))
        
        sprintf """Pattern Matcher Resource Estimate:
  Search Space Size: %d
  Top N Results: %d
  Qubits Required: %d
  Feasibility: %s"""
            searchSpaceSize
            topN
            qubits
            (if qubits <= 16 then "✓ Feasible on NISQ devices" else "✗ Requires fault-tolerant quantum computer")
    
    /// Export solution to human-readable string
    let describeSolution (solution: PatternSolution<'T>) : string =
        let matchesText =
            if List.isEmpty solution.Matches then
                "  No matches found"
            else
                let displayCount = min 10 (List.length solution.Matches)
                let matches = 
                    solution.Matches 
                    |> List.take displayCount
                    |> List.mapi (fun i item -> sprintf "  Match %d: %A" (i + 1) item)
                    |> String.concat "\n"
                
                let remainder =
                    if List.length solution.Matches > 10 then
                        sprintf "\n  ... and %d more matches" (List.length solution.Matches - 10)
                    else
                        ""
                
                sprintf "%s%s" matches remainder
        
        sprintf """=== Quantum Pattern Matcher Solution ===
Success Probability: %.4f
Matches Found: %d / %d searched
Backend: %s
Qubits Required: %d
Iterations Used: %d

Matching Items:
%s"""
            solution.SuccessProbability
            (List.length solution.Matches)
            solution.SearchSpaceSize
            solution.BackendName
            solution.QubitsRequired
            solution.IterationsUsed
            matchesText
