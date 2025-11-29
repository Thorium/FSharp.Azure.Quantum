namespace FSharp.Azure.Quantum.GroverSearch

open System

/// Grover Search API Module
/// 
/// High-level API for Grover's quantum search algorithm.
/// Provides convenient functions for searching with single/multiple solutions,
/// predicates, and automatic optimal iteration calculation.
/// 
/// This is the main user-facing API for Grover search.
/// 
/// ALL GROVER SEARCH API CODE IN SINGLE FILE
module Search =
    
    open FSharp.Azure.Quantum.LocalSimulator
    open FSharp.Azure.Quantum.GroverSearch.Oracle
    open FSharp.Azure.Quantum.GroverSearch.GroverIteration
    open FSharp.Azure.Quantum.GroverSearch.BackendAdapter
    
    // ============================================================================
    // TYPES - Search configuration and results
    // ============================================================================
    
    /// Configuration for Grover search
    type SearchConfig = {
        /// Maximum iterations (safety limit)
        MaxIterations: int option
        
        /// Success probability threshold (0.0 - 1.0)
        SuccessThreshold: float
        
        /// Auto-calculate optimal iterations
        OptimizeIterations: bool
        
        /// Number of measurement shots (for result verification)
        Shots: int
        
        /// Random seed for reproducible results
        RandomSeed: int option
    }
    
    /// Type alias for SearchResult (defined in GroverIteration module)
    type SearchResult = GroverIteration.SearchResult
    
    // ============================================================================
    // MEASUREMENT - Converting quantum state to classical results
    // ============================================================================
    
    /// Measure quantum state and return most likely outcomes
    /// 
    /// Performs multiple measurements and returns distribution
    let measureState (state: StateVector.StateVector) (shots: int) (randomSeed: int option) : Map<int, int> =
        let rng = 
            match randomSeed with
            | Some seed -> Random(seed)
            | None -> Random()
        
        // Use Measurement module to sample (fully qualified)
        let rawCounts = FSharp.Azure.Quantum.LocalSimulator.Measurement.sampleAndCount rng shots state
        
        rawCounts
    
    /// Extract most likely solution from measurement counts
    let getMostLikelySolution (counts: Map<int, int>) : int option =
        if Map.isEmpty counts then
            None
        else
            counts
            |> Map.toList
            |> List.maxBy snd
            |> fst
            |> Some
    
    /// Extract top N solutions from measurement counts
    let getTopSolutions (n: int) (counts: Map<int, int>) : int list =
        counts
        |> Map.toList
        |> List.sortByDescending snd
        |> List.take (min n (Map.count counts))
        |> List.map fst
    
    // ============================================================================
    // SEARCH EXECUTION - Core search functions
    // ============================================================================
    
    /// Execute Grover search and measure results
    /// 
    /// Internal function that runs the full search pipeline
    let private executeSearch (oracle: CompiledOracle) (config: SearchConfig) : Result<SearchResult, string> =
        try
            // Determine iteration count
            let iterationCountResult =
                if config.OptimizeIterations then
                    match optimalIterationsForOracle oracle with
                    | Some (Ok k) -> 
                        // Apply max iterations limit if specified
                        let finalK = match config.MaxIterations with
                                     | Some maxK -> min k maxK
                                     | None -> k
                        Ok finalK
                    | Some (Error msg) ->
                        Error msg
                    | None ->
                        // Cannot optimize - use heuristic
                        let searchSpaceSize = 1 <<< oracle.NumQubits
                        let defaultK = int (Math.Sqrt(float searchSpaceSize))
                        let finalK = match config.MaxIterations with
                                     | Some maxK -> min defaultK maxK
                                     | None -> defaultK
                        Ok finalK
                else
                    // Use max iterations or default
                    let k = match config.MaxIterations with
                            | Some k -> k
                            | None -> 
                                let searchSpaceSize = 1 <<< oracle.NumQubits
                                int (Math.Sqrt(float searchSpaceSize))
                    Ok k
            
            match iterationCountResult with
            | Error msg -> Error msg
            | Ok iterationCount ->
                // Execute Grover iterations
                let iterConfig = {
                    NumIterations = iterationCount
                    TrackProbabilities = false
                }
                
                match GroverIteration.execute oracle iterConfig with
                | Error msg -> Error msg
                | Ok iterResult ->
                    // Measure final state
                    let counts = measureState iterResult.FinalState config.Shots config.RandomSeed
                    
                    // Extract solutions (states with high measurement counts)
                    let threshold = config.Shots / 10  // At least 10% of shots
                    let solutions =
                        counts
                        |> Map.toList
                        |> List.filter (fun (_, count) -> count > threshold)
                        |> List.sortByDescending snd
                        |> List.map fst
                    
                    // Calculate empirical success probability
                    let successCounts =
                        solutions
                        |> List.sumBy (fun sol ->
                            counts |> Map.tryFind sol |> Option.defaultValue 0)
                    
                    let successProb = float successCounts / float config.Shots
                    
                    Ok {
                        Solutions = solutions
                        SuccessProbability = successProb
                        IterationsApplied = iterationCount
                        MeasurementCounts = counts
                        Shots = config.Shots
                        Success = successProb >= config.SuccessThreshold
                    }
        with
        | ex -> Error $"Search execution failed: {ex.Message}"
    
    // ============================================================================
    // PUBLIC API - User-facing search functions
    // ============================================================================
    
    /// Search for a single specific value
    /// 
    /// Example: Search.searchSingle 42 6 defaultConfig
    /// Searches for value 42 in 6-qubit space (0-63)
    let searchSingle (target: int) (numQubits: int) (config: SearchConfig) : Result<SearchResult, string> =
        match Oracle.forValue target numQubits with
        | Ok oracle -> executeSearch oracle config
        | Error msg -> Error msg
    
    /// Search for multiple specific values
    /// 
    /// Example: Search.searchMultiple [5; 7; 11] 4 defaultConfig
    /// Searches for 5, 7, or 11 in 4-qubit space (0-15)
    let searchMultiple (targets: int list) (numQubits: int) (config: SearchConfig) : Result<SearchResult, string> =
        if List.isEmpty targets then
            Error "Target list cannot be empty"
        else
            match Oracle.forValues targets numQubits with
            | Ok oracle -> executeSearch oracle config
            | Error msg -> Error msg
    
    /// Search for values satisfying a predicate
    /// 
    /// Example: Search.searchWhere (fun x -> x % 2 = 0) 4 defaultConfig
    /// Searches for even numbers in 4-qubit space (0-15)
    let searchWhere (predicate: int -> bool) (numQubits: int) (config: SearchConfig) : Result<SearchResult, string> =
        match Oracle.fromPredicate predicate numQubits with
        | Ok oracle -> executeSearch oracle config
        | Error msg -> Error msg
    
    /// Search using a compiled oracle
    /// 
    /// Most flexible - allows custom oracle composition
    let searchWithOracle (oracle: CompiledOracle) (config: SearchConfig) : Result<SearchResult, string> =
        executeSearch oracle config
    
    // ============================================================================
    // CONVENIENCE FUNCTIONS - Common search patterns
    // ============================================================================
    
    /// Search for even numbers
    let searchEven (numQubits: int) (config: SearchConfig) : Result<SearchResult, string> =
        match Oracle.even numQubits with
        | Ok oracle -> executeSearch oracle config
        | Error msg -> Error msg
    
    /// Search for odd numbers
    let searchOdd (numQubits: int) (config: SearchConfig) : Result<SearchResult, string> =
        match Oracle.odd numQubits with
        | Ok oracle -> executeSearch oracle config
        | Error msg -> Error msg
    
    /// Search for numbers in range [min, max]
    let searchInRange (min: int) (max: int) (numQubits: int) (config: SearchConfig) : Result<SearchResult, string> =
        match Oracle.inRange min max numQubits with
        | Ok oracle -> executeSearch oracle config
        | Error msg -> Error msg
    
    /// Search for numbers divisible by n
    let searchDivisibleBy (n: int) (numQubits: int) (config: SearchConfig) : Result<SearchResult, string> =
        match Oracle.divisibleBy n numQubits with
        | Ok oracle -> executeSearch oracle config
        | Error msg -> Error msg
    
    // ============================================================================
    // CONFIGURATION BUILDERS - Convenient config creation
    // ============================================================================
    
    /// Default search configuration
    /// - Optimize iterations: true
    /// - Success threshold: 0.5 (50%)
    /// - Shots: 100
    /// - No max iterations limit
    let defaultConfig : SearchConfig =
        {
            MaxIterations = None
            SuccessThreshold = 0.5
            OptimizeIterations = true
            Shots = 100
            RandomSeed = None
        }
    
    /// High-precision configuration (more shots)
    let highPrecisionConfig : SearchConfig =
        {
            MaxIterations = None
            SuccessThreshold = 0.9
            OptimizeIterations = true
            Shots = 1000
            RandomSeed = None
        }
    
    /// Fast configuration (fewer shots, lower threshold)
    let fastConfig : SearchConfig =
        {
            MaxIterations = None
            SuccessThreshold = 0.3
            OptimizeIterations = true
            Shots = 50
            RandomSeed = None
        }
    
    /// Reproducible configuration (fixed random seed)
    let reproducibleConfig (seed: int) : SearchConfig =
        {
            MaxIterations = None
            SuccessThreshold = 0.5
            OptimizeIterations = true
            Shots = 100
            RandomSeed = Some seed
        }
    
    /// Manual iteration configuration (no auto-optimization)
    let manualConfig (iterations: int) (shots: int) : SearchConfig =
        {
            MaxIterations = Some iterations
            SuccessThreshold = 0.5
            OptimizeIterations = false
            Shots = shots
            RandomSeed = None
        }
    
    // ============================================================================
    // SEARCH VERIFICATION - Classical comparison
    // ============================================================================
    
    /// Classical linear search (for comparison)
    /// 
    /// Returns first value satisfying predicate
    let classicalSearch (predicate: int -> bool) (searchSpace: int) : int option =
        [0 .. searchSpace - 1]
        |> List.tryFind predicate
    
    /// Classical exhaustive search (find all solutions)
    let classicalSearchAll (predicate: int -> bool) (searchSpace: int) : int list =
        [0 .. searchSpace - 1]
        |> List.filter predicate
    
    /// Verify quantum search result against classical
    /// 
    /// Returns true if quantum found a valid solution
    let verifySolution (predicate: int -> bool) (result: SearchResult) : bool =
        if List.isEmpty result.Solutions then
            false
        else
            // Check that all found solutions satisfy the predicate
            result.Solutions
            |> List.forall predicate
    
    // ============================================================================
    // ADVANCED SEARCH - Multi-round search
    // ============================================================================
    
    /// Multi-round search with retry logic
    /// 
    /// Runs search multiple times and aggregates results
    let searchMultiRound (oracle: CompiledOracle) (config: SearchConfig) (rounds: int) : Result<SearchResult, string> =
        if rounds < 1 then
            Error "Number of rounds must be positive"
        else
            try
                // Execute search multiple times
                let results =
                    [1 .. rounds]
                    |> List.choose (fun _ ->
                        match executeSearch oracle config with
                        | Ok res -> Some res
                        | Error _ -> None
                    )
                
                if List.isEmpty results then
                    Error "All search rounds failed"
                else
                    // Aggregate results
                    let allSolutions =
                        results
                        |> List.collect (fun r -> r.Solutions)
                        |> List.distinct
                    
                    let avgSuccessProb =
                        results
                        |> List.averageBy (fun r -> r.SuccessProbability)
                    
                    let totalIterations =
                        results
                        |> List.sumBy (fun r -> r.IterationsApplied)
                    
                    let totalShots =
                        results
                        |> List.sumBy (fun r -> r.Shots)
                    
                    // Aggregate measurement counts
                    let aggregatedCounts =
                        results
                        |> List.collect (fun r -> r.MeasurementCounts |> Map.toList)
                        |> List.groupBy fst
                        |> List.map (fun (key, values) -> (key, values |> List.sumBy snd))
                        |> Map.ofList
                    
                    Ok {
                        Solutions = allSolutions
                        SuccessProbability = avgSuccessProb
                        IterationsApplied = totalIterations / rounds
                        MeasurementCounts = aggregatedCounts
                        Shots = totalShots
                        Success = avgSuccessProb >= config.SuccessThreshold
                    }
            with
            | ex -> Error $"Multi-round search failed: {ex.Message}"
    
    // ============================================================================
    // EXAMPLES - For documentation
    // ============================================================================
    
    // ============================================================================
    // BACKEND EXECUTION - Cloud quantum hardware support
    // ============================================================================
    
    /// Search using IQuantumBackend (cloud or local)
    /// 
    /// Enables execution on IonQ, Rigetti, or other quantum backends.
    /// Uses BackendAdapter to convert Grover algorithm to quantum circuit.
    let searchWithBackend 
        (oracle: CompiledOracle) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        
        // Determine iteration count
        let iterationCountResult =
            if config.OptimizeIterations then
                match optimalIterationsForOracle oracle with
                | Some (Ok k) -> 
                    let finalK = match config.MaxIterations with
                                 | Some maxK -> min k maxK
                                 | None -> k
                    Ok finalK
                | Some (Error msg) -> Error msg
                | None ->
                    let defaultK = int (Math.Sqrt(float (1 <<< oracle.NumQubits)))
                    let finalK = match config.MaxIterations with
                                 | Some maxK -> min defaultK maxK
                                 | None -> defaultK
                    Ok finalK
            else
                let k = match config.MaxIterations with
                        | Some k -> k
                        | None -> int (Math.Sqrt(float (1 <<< oracle.NumQubits)))
                Ok k
        
        match iterationCountResult with
        | Error msg -> Error msg
        | Ok iterationCount ->
            // Delegate to BackendAdapter
            BackendAdapter.executeGroverWithBackend oracle backend iterationCount config.Shots
    
    /// Search for single value using backend
    let searchSingleWithBackend 
        (target: int) 
        (numQubits: int) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        match Oracle.forValue target numQubits with
        | Ok oracle -> searchWithBackend oracle backend config
        | Error msg -> Error msg
    
    /// Search for multiple values using backend
    let searchMultipleWithBackend 
        (targets: int list) 
        (numQubits: int) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        if List.isEmpty targets then
            Error "Target list cannot be empty"
        else
            match Oracle.forValues targets numQubits with
            | Ok oracle -> searchWithBackend oracle backend config
            | Error msg -> Error msg
    
    /// Search with predicate using backend
    let searchWhereWithBackend 
        (predicate: int -> bool) 
        (numQubits: int) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        match Oracle.fromPredicate predicate numQubits with
        | Ok oracle -> searchWithBackend oracle backend config
        | Error msg -> Error msg
    
    /// Search for even numbers using backend
    let searchEvenWithBackend 
        (numQubits: int) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        match Oracle.even numQubits with
        | Ok oracle -> searchWithBackend oracle backend config
        | Error msg -> Error msg
    
    /// Search for odd numbers using backend
    let searchOddWithBackend 
        (numQubits: int) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        match Oracle.odd numQubits with
        | Ok oracle -> searchWithBackend oracle backend config
        | Error msg -> Error msg
    
    /// Search in range using backend
    let searchInRangeWithBackend 
        (min: int) 
        (max: int) 
        (numQubits: int) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        match Oracle.inRange min max numQubits with
        | Ok oracle -> searchWithBackend oracle backend config
        | Error msg -> Error msg
    
    /// Search for numbers divisible by n using backend
    let searchDivisibleByWithBackend 
        (n: int) 
        (numQubits: int) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        match Oracle.divisibleBy n numQubits with
        | Ok oracle -> searchWithBackend oracle backend config
        | Error msg -> Error msg
    
    module Examples =
        
        /// Example: Find value 42 in 6-qubit space
        let findValue42 () : Result<SearchResult, string> =
            searchSingle 42 6 defaultConfig
        
        /// Example: Find any even number in 4-qubit space
        let findEvenNumber () : Result<SearchResult, string> =
            searchEven 4 defaultConfig
        
        /// Example: Find numbers between 10 and 15
        let findInRange () : Result<SearchResult, string> =
            searchInRange 10 15 4 defaultConfig
        
        /// Example: Custom predicate - find prime numbers
        let isPrime (n: int) : bool =
            if n < 2 then false
            elif n = 2 then true
            elif n % 2 = 0 then false
            else
                let limit = int (Math.Sqrt(float n))
                [3 .. 2 .. limit]
                |> List.forall (fun d -> n % d <> 0)
        
        let findPrimeNumber () : Result<SearchResult, string> =
            searchWhere isPrime 4 defaultConfig
        
        /// Example: Multi-target search
        let findMultipleTargets () : Result<SearchResult, string> =
            searchMultiple [5; 7; 11; 13] 4 defaultConfig
        
        /// Example: Find value 42 using cloud backend
        let findValue42WithBackend (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) : Result<SearchResult, string> =
            searchSingleWithBackend 42 6 backend defaultConfig
        
        /// Example: Find even number using cloud backend
        let findEvenNumberWithBackend (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) : Result<SearchResult, string> =
            searchEvenWithBackend 4 backend defaultConfig
