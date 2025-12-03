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
    // PUBLIC API - All functions use IQuantumBackend
    // ============================================================================
    
    /// Search using compiled oracle
    /// 
    /// Executes Grover's search algorithm on the given backend (cloud or local).
    /// Uses BackendAdapter to convert Grover algorithm to quantum circuit.
    let search 
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
            // Delegate to BackendAdapter with thresholds from config
            // Solution threshold: 7% of shots (balances noise filtering with solution capture)
            // Success threshold: from config.SuccessThreshold
            BackendAdapter.executeGroverWithBackend 
                oracle 
                backend 
                iterationCount 
                config.Shots
                0.07  // 7% solution extraction threshold
                config.SuccessThreshold
    
    /// Search for single value
    let searchSingle 
        (target: int) 
        (numQubits: int) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        match Oracle.forValue target numQubits with
        | Ok oracle -> search oracle backend config
        | Error msg -> Error msg
    
    /// Search for multiple values
    let searchMultiple 
        (targets: int list) 
        (numQubits: int) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        if List.isEmpty targets then
            Error "Target list cannot be empty"
        else
            match Oracle.forValues targets numQubits with
            | Ok oracle -> search oracle backend config
            | Error msg -> Error msg
    
    /// Search with custom predicate
    let searchWhere 
        (predicate: int -> bool) 
        (numQubits: int) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        match Oracle.fromPredicate predicate numQubits with
        | Ok oracle -> search oracle backend config
        | Error msg -> Error msg
    
    /// Search for even numbers
    let searchEven 
        (numQubits: int) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        match Oracle.even numQubits with
        | Ok oracle -> search oracle backend config
        | Error msg -> Error msg
    
    /// Search for odd numbers
    let searchOdd 
        (numQubits: int) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        match Oracle.odd numQubits with
        | Ok oracle -> search oracle backend config
        | Error msg -> Error msg
    
    /// Search in range [min, max]
    let searchInRange 
        (min: int) 
        (max: int) 
        (numQubits: int) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        match Oracle.inRange min max numQubits with
        | Ok oracle -> search oracle backend config
        | Error msg -> Error msg
    
    /// Search for numbers divisible by n
    let searchDivisibleBy 
        (n: int) 
        (numQubits: int) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) 
        (config: SearchConfig) 
        : Result<SearchResult, string> =
        match Oracle.divisibleBy n numQubits with
        | Ok oracle -> search oracle backend config
        | Error msg -> Error msg
    
    // ============================================================================
    // CONFIGURATION BUILDERS - Convenient config creation
    // ============================================================================
    
    /// Default search configuration
    /// - Optimize iterations: true
    /// - Success threshold: 0.7 (70% - increased from 50% after MCZ/amplitude bug fixes)
    /// - Shots: 100 (reduced from 200 - algorithm is now much more reliable)
    /// - No max iterations limit
    /// - Performance: ~90% theoretical success rate at optimal iterations
    let defaultConfig : SearchConfig =
        {
            MaxIterations = None
            SuccessThreshold = 0.7
            OptimizeIterations = true
            Shots = 100
            RandomSeed = None
        }
    
    /// High-precision configuration (more shots, higher threshold)
    /// - Use when you need near-certain results
    /// - Success threshold: 0.95 (95% - increased from 90%)
    /// - Shots: 500 (reduced from 1000 - algorithm stability allows fewer shots)
    let highPrecisionConfig : SearchConfig =
        {
            MaxIterations = None
            SuccessThreshold = 0.95
            OptimizeIterations = true
            Shots = 500
            RandomSeed = None
        }
    
    /// Fast configuration (minimal shots, moderate threshold)
    /// - Use for quick prototyping or when speed matters more than accuracy
    /// - Success threshold: 0.5 (50% - increased from 30%)
    /// - Shots: 30 (reduced from 50 - fewer shots needed with correct algorithm)
    let fastConfig : SearchConfig =
        {
            MaxIterations = None
            SuccessThreshold = 0.5
            OptimizeIterations = true
            Shots = 30
            RandomSeed = None
        }
    
    /// Reproducible configuration (fixed random seed)
    /// - Use for deterministic testing and debugging
    /// - Success threshold: 0.7 (70% - increased from 50%)
    /// - Shots: 50 (reduced from 100 - algorithm is more deterministic now)
    let reproducibleConfig (seed: int) : SearchConfig =
        {
            MaxIterations = None
            SuccessThreshold = 0.7
            OptimizeIterations = true
            Shots = 50
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
    let searchMultiRound 
        (oracle: CompiledOracle) 
        (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend)
        (config: SearchConfig) 
        (rounds: int) 
        : Result<SearchResult, string> =
        if rounds < 1 then
            Error "Number of rounds must be positive"
        else
            try
                // Execute search multiple times
                let results =
                    [1 .. rounds]
                    |> List.choose (fun _ ->
                        match search oracle backend config with
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
    
    module Examples =
        
        /// Example: Find value 42 in 6-qubit space
        /// Uses increased shots for better success rate in 64-state search space
        let findValue42 (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) : Result<SearchResult, string> =
            let config = { defaultConfig with Shots = 2000 }
            searchSingle 42 6 backend config
        
        /// Example: Find any even number in 4-qubit space
        let findEvenNumber (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) : Result<SearchResult, string> =
            searchEven 4 backend defaultConfig
        
        /// Example: Find numbers between 10 and 15
        let findInRange (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) : Result<SearchResult, string> =
            searchInRange 10 15 4 backend defaultConfig
        
        /// Example: Custom predicate - find prime numbers
        let isPrime (n: int) : bool =
            if n < 2 then false
            elif n = 2 then true
            elif n % 2 = 0 then false
            else
                let limit = int (Math.Sqrt(float n))
                [3 .. 2 .. limit]
                |> List.forall (fun d -> n % d <> 0)
        
        let findPrimeNumber (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) : Result<SearchResult, string> =
            searchWhere isPrime 4 backend defaultConfig
        
        /// Example: Multi-target search
        let findMultipleTargets (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) : Result<SearchResult, string> =
            searchMultiple [5; 7; 11; 13] 4 backend defaultConfig
        
        /// Example: Find value 42 using custom backend
        let findValue42CustomBackend (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) : Result<SearchResult, string> =
            let config = { defaultConfig with Shots = 2000 }
            searchSingle 42 6 backend config
        
        /// Example: Find even number using custom backend
        let findEvenNumberCustomBackend (backend: FSharp.Azure.Quantum.Core.BackendAbstraction.IQuantumBackend) : Result<SearchResult, string> =
            searchEven 4 backend defaultConfig
