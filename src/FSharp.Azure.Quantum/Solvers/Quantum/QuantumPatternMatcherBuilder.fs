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
    let validate (problem: PatternProblem<'T>) : Result<unit, QuantumError> =
        let searchSpaceSize = 
            match problem.SearchSpace with
            | Choice1Of2 items -> List.length items
            | Choice2Of2 size -> size
        
        if searchSpaceSize < 1 then
            Error (QuantumError.ValidationError ("SearchSpace", "must contain at least 1 item"))
        elif searchSpaceSize > (1 <<< 16) then
            Error (QuantumError.ValidationError ("SearchSpace", $"size {searchSpaceSize} exceeds maximum (2^16 = 65536)"))
        elif problem.TopN < 1 then
            Error (QuantumError.ValidationError ("TopN", "must be at least 1"))
        elif problem.TopN > searchSpaceSize then
            Error (QuantumError.ValidationError ("TopN", $"({problem.TopN}) cannot exceed search space size ({searchSpaceSize})"))
        elif problem.Shots < 1 then
            Error (QuantumError.ValidationError ("Shots", "must be at least 1"))
        else
            let qubitsNeeded = int (ceil (log (float searchSpaceSize) / log 2.0))
            if qubitsNeeded > 16 then
                Error (QuantumError.ValidationError ("SearchSpace", $"requires {qubitsNeeded} qubits (size {searchSpaceSize}). Max: 16"))
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
            | Error err -> failwith err.Message
            | Ok () -> problem
        
        member _.For(sequence: seq<'U>, body: 'U -> PatternProblem<'T>) : PatternProblem<'T> =
            // Idiomatic F#: Use Seq.fold for functional accumulation with AND logic
            let zero = {
                SearchSpace = Choice2Of2 0
                Pattern = fun _ -> true  // Neutral element for AND
                TopN = 0
                Backend = None
                MaxIterations = None
                Shots = 0
            }
            
            sequence
            |> Seq.map body
            |> Seq.fold (fun acc itemProblem ->
                {
                    SearchSpace = match itemProblem.SearchSpace with Choice2Of2 0 -> acc.SearchSpace | s -> s
                    Pattern = fun x -> acc.Pattern x && itemProblem.Pattern x  // AND logic
                    TopN = if itemProblem.TopN > 0 then itemProblem.TopN else acc.TopN
                    Backend = match itemProblem.Backend with Some b -> Some b | None -> acc.Backend
                    MaxIterations = match itemProblem.MaxIterations with Some i -> Some i | None -> acc.MaxIterations
                    Shots = if itemProblem.Shots > 0 then itemProblem.Shots else acc.Shots
                }) zero
        
        member _.Combine(problem1: PatternProblem<'T>, problem2: PatternProblem<'T>) : PatternProblem<'T> =
            // Merge two problems with AND logic on patterns
            {
                SearchSpace = match problem2.SearchSpace with Choice2Of2 0 -> problem1.SearchSpace | s -> s
                Pattern = fun x -> problem1.Pattern x && problem2.Pattern x  // AND both patterns
                TopN = if problem2.TopN > 0 then problem2.TopN else problem1.TopN
                Backend = match problem2.Backend with Some b -> Some b | None -> problem1.Backend
                MaxIterations = match problem2.MaxIterations with Some i -> Some i | None -> problem1.MaxIterations
                Shots = if problem2.Shots > 0 then problem2.Shots else problem1.Shots
            }
        
        member _.Zero() : PatternProblem<'T> =
            {
                SearchSpace = Choice2Of2 0
                Pattern = fun _ -> true  // Neutral element for AND
                TopN = 0
                Backend = None
                MaxIterations = None
                Shots = 0
            }
        
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
    let solve (problem: PatternProblem<'T>) : Result<PatternSolution<'T>, QuantumError> =
        
        try
            // Validate problem first
            match validate problem with
            | Error err -> Error err
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
                    | Some items when idx >= 0 && idx < List.length items ->
                        let item = List.item idx items
                        problem.Pattern item
                    | None ->
                        // User provided only size, pattern must work on indices
                        // When using searchSpaceSize, 'T should typically be int
                        // Try to convert idx to 'T - works when 'T = int
                        try
                            let indexAsT = box idx :?> 'T
                            problem.Pattern indexAsT
                        with
                        | :? System.InvalidCastException ->
                            // Type mismatch: searchSpaceSize used with non-int pattern
                            false
                    | _ -> false
                
                // Create oracle for Grover search
                let oracleResult = GroverSearch.Oracle.fromPredicate oraclePredicate qubitsNeeded
                match oracleResult with
                | Error msg -> Error (QuantumError.OperationError ("OracleCreation", $"Failed to create oracle: {msg}"))
                | Ok oracle ->
                    
                    // Calculate optimal iterations (looking for TopN solutions)
                    let iterationsResult = GroverSearch.GroverIteration.optimalIterations searchSpaceSize problem.TopN
                    match iterationsResult with
                    | Error msg -> Error (QuantumError.OperationError ("IterationCalculation", $"Failed to calculate iterations: {msg}"))
                    | Ok calculatedIters ->
                        
                        let iterations = 
                            match problem.MaxIterations with
                            | Some maxIters -> min calculatedIters maxIters
                            | None -> calculatedIters
                        
                        // Execute Grover search
                        // Use lower thresholds for LocalBackend (produces uniform noise)
                        // 5% solution threshold works reliably with LocalBackend
                        let solutionThreshold = 0.05  // 5% (down from 10%)
                        let successThreshold = 0.10   // 10% (down from 50%)
                        match GroverSearch.BackendAdapter.executeGroverWithBackend oracle actualBackend iterations problem.Shots solutionThreshold successThreshold with
                        | Error msg -> Error (QuantumError.OperationError ("GroverSearch", $"Grover search failed: {msg}"))
                        | Ok searchResult ->
                            
                            if List.isEmpty searchResult.Solutions then
                                Error (QuantumError.OperationError ("GroverSearch", "No matching patterns found by quantum search"))
                            else
                                // Take top N solutions
                                let topIndices = searchResult.Solutions |> List.take (min problem.TopN (List.length searchResult.Solutions))
                                
                                // Convert indices to actual items (if we have the search space)
                                let matches =
                                    match searchSpaceItems with
                                    | Some items ->
                                        topIndices
                                        |> List.choose (fun idx -> 
                                            if idx >= 0 && idx < List.length items then
                                                Some (List.item idx items)
                                            else
                                                None
                                        )
                                    | None ->
                                        // Return indices as 'T (when using searchSpaceSize, 'T should be int)
                                        topIndices 
                                        |> List.choose (fun idx ->
                                            try
                                                Some (box idx :?> 'T)
                                            with
                                            | :? System.InvalidCastException -> None
                                        )
                                
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
        | ex -> Error (QuantumError.OperationError ("PatternMatcher", $"Pattern matcher failed: {ex.Message}"))
    
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
