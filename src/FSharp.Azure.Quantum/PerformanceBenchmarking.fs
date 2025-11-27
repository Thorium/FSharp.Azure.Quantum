namespace FSharp.Azure.Quantum

open System
open FSharp.Azure.Quantum

/// Performance Benchmarking Suite
/// Provides comprehensive performance measurement and comparison
/// for classical and quantum solvers
module PerformanceBenchmarking =

    // ============================================================================
    // TYPES - Benchmark Configuration and Results
    // ============================================================================

    /// Benchmark configuration
    type BenchmarkConfig = {
        ProblemSizes: int list        // e.g., [5, 10, 15, 20] cities
        Repetitions: int              // Run each benchmark N times
        Backends: string list         // ["Classical"; "IonQ"; "Rigetti"]
        OutputPath: string            // CSV export path
    }

    /// Single benchmark result
    type BenchmarkResult = {
        ProblemType: string           // "TSP" or "Portfolio"
        ProblemSize: int              // 10 cities, 5 assets, etc.
        Solver: string                // "Classical", "IonQ", "Rigetti"
        ExecutionTimeMs: int64
        SolutionQuality: float        // Objective function value
        Cost: float                   // USD cost
        ErrorRate: float option       // If ground truth known
        Timestamp: DateTime
    }

    /// Comparison between solvers
    type ComparisonReport = {
        ProblemType: string
        ProblemSize: int
        ClassicalResult: BenchmarkResult
        QuantumResults: BenchmarkResult list
        QuantumAdvantage: bool
        SpeedupFactor: float option   // Quantum time / Classical time
        CostPerAccuracy: float        // Cost per % accuracy gain
    }

    // ============================================================================
    // UTILITY FUNCTIONS
    // ============================================================================

    /// Generate random cities for TSP benchmarking
    /// Optional seed parameter ensures deterministic results for testing
    let generateRandomCities (count: int) (seed: int option) : (string * float * float) array =
        let rng = 
            match seed with
            | Some s -> Random(s)
            | None -> Random()
        Array.init count (fun i ->
            let name = sprintf "City%d" i
            let x = float (rng.Next(0, 100))
            let y = float (rng.Next(0, 100))
            (name, x, y)
        )

    /// Export results to CSV
    let exportToCSV (results: BenchmarkResult list) (path: string) : unit =
        let csv = 
            "ProblemType,ProblemSize,Solver,ExecutionTimeMs,SolutionQuality,Cost,Timestamp\n" +
            (results
             |> List.map (fun r ->
                 sprintf "%s,%d,%s,%d,%.4f,%.2f,%s"
                     r.ProblemType r.ProblemSize r.Solver
                     r.ExecutionTimeMs r.SolutionQuality r.Cost
                     (r.Timestamp.ToString("o")))
             |> String.concat "\n")
        
        System.IO.File.WriteAllText(path, csv)

    /// Performance regression test
    let checkPerformanceRegression 
        (current: BenchmarkResult list) 
        (baseline: BenchmarkResult list) 
        (threshold: float)  // e.g., 1.2 = allow 20% slowdown
        : (string * float) list =  // (test_name, regression_factor)
        
        current
        |> List.choose (fun curr ->
            baseline
            |> List.tryFind (fun baseResult -> 
                baseResult.ProblemType = curr.ProblemType &&
                baseResult.ProblemSize = curr.ProblemSize &&
                baseResult.Solver = curr.Solver)
            |> Option.bind (fun baseResult ->
                let regressionFactor = float curr.ExecutionTimeMs / float baseResult.ExecutionTimeMs
                if regressionFactor > threshold then
                    Some (sprintf "%s_%d_%s" curr.ProblemType curr.ProblemSize curr.Solver, regressionFactor)
                else
                    None))

    // ============================================================================
    // CLASSICAL TSP BENCHMARKING
    // ============================================================================

    /// Benchmark classical TSP solver
    let benchmarkClassicalTSP 
        (cities: (string * float * float) array) 
        (repetitions: int) 
        : Async<BenchmarkResult> =
        
        async {
            // Validate input
            if cities.Length < 2 then
                failwithf "Cannot benchmark TSP with less than 2 cities (got %d)" cities.Length
            if repetitions < 1 then
                failwithf "Repetitions must be at least 1 (got %d)" repetitions
                
            let results = [
                for _ in 1 .. repetitions do
                    let sw = System.Diagnostics.Stopwatch.StartNew()
                    let problem = TSP.createProblem (cities |> Array.toList)
                    let solution = TSP.solve problem None
                    sw.Stop()
                    
                    match solution with
                    | Ok tour -> yield (sw.ElapsedMilliseconds, tour.TotalDistance)
                    | Error err -> 
                        // Log error but continue - some runs might succeed
                        eprintfn "TSP solve failed: %s" err
            ]
            
            // Ensure we got at least one successful result
            if results.IsEmpty then
                failwith "All TSP solver runs failed - cannot produce benchmark result"
            
            let avgTime = 
                results |> List.averageBy (fst >> float) |> int64
                    
            let bestQuality = 
                results |> List.minBy snd |> snd
            
            return {
                ProblemType = "TSP"
                ProblemSize = cities.Length
                Solver = "Classical"
                ExecutionTimeMs = avgTime
                SolutionQuality = bestQuality
                Cost = 0.0
                ErrorRate = None
                Timestamp = DateTime.UtcNow
            }
        }

    // ============================================================================
    // CLASSICAL PORTFOLIO BENCHMARKING
    // ============================================================================

    /// Benchmark classical Portfolio solver
    let benchmarkClassicalPortfolio 
        (assets: (string * float * float * float) list) 
        (budget: float) 
        (repetitions: int) 
        : Async<BenchmarkResult> =
        
        async {
            // Validate input
            if assets.Length < 1 then
                failwithf "Cannot benchmark Portfolio with less than 1 asset (got %d)" assets.Length
            if budget <= 0.0 then
                failwithf "Budget must be positive (got %f)" budget
            if repetitions < 1 then
                failwithf "Repetitions must be at least 1 (got %d)" repetitions
                
            let results = [
                for _ in 1 .. repetitions do
                    let sw = System.Diagnostics.Stopwatch.StartNew()
                    let problem = Portfolio.createProblem assets budget
                    let solution = Portfolio.solve problem None
                    sw.Stop()
                    
                    match solution with
                    | Ok allocation -> yield (sw.ElapsedMilliseconds, allocation.ExpectedReturn)
                    | Error err -> 
                        // Log error but continue - some runs might succeed
                        eprintfn "Portfolio solve failed: %s" err
            ]
            
            // Ensure we got at least one successful result
            if results.IsEmpty then
                failwith "All Portfolio solver runs failed - cannot produce benchmark result"
            
            let avgTime = 
                results |> List.averageBy (fst >> float) |> int64
                    
            let bestQuality = 
                results |> List.maxBy snd |> snd  // Higher expected return is better
            
            return {
                ProblemType = "Portfolio"
                ProblemSize = assets.Length
                Solver = "Classical"
                ExecutionTimeMs = avgTime
                SolutionQuality = bestQuality
                Cost = 0.0
                ErrorRate = None
                Timestamp = DateTime.UtcNow
            }
        }

    // ============================================================================
    // COMPARISON AND REPORTING
    // ============================================================================

    /// Generate comparison report
    let generateComparisonReport (results: BenchmarkResult list) : ComparisonReport list =
        results
        |> List.groupBy (fun r -> (r.ProblemType, r.ProblemSize))
        |> List.choose (fun ((ptype, psize), group) ->
            let classicalOpt = group |> List.tryFind (fun r -> r.Solver = "Classical")
            let quantum = group |> List.filter (fun r -> r.Solver <> "Classical")
            
            match classicalOpt with
            | Some classical ->
                let quantumAdvantage = 
                    quantum 
                    |> List.exists (fun q -> q.SolutionQuality < classical.SolutionQuality)
                
                let speedup = 
                    quantum 
                    |> List.tryHead
                    |> Option.map (fun q -> float classical.ExecutionTimeMs / float q.ExecutionTimeMs)
                
                let costPerAccuracy =
                    quantum
                    |> List.tryHead
                    |> Option.map (fun q ->
                        let accuracyGain = abs(classical.SolutionQuality - q.SolutionQuality)
                        if accuracyGain > 0.0 then q.Cost / accuracyGain else 0.0)
                    |> Option.defaultValue 0.0
                
                Some {
                    ProblemType = ptype
                    ProblemSize = psize
                    ClassicalResult = classical
                    QuantumResults = quantum
                    QuantumAdvantage = quantumAdvantage
                    SpeedupFactor = speedup
                    CostPerAccuracy = costPerAccuracy
                }
            | None -> None
        )
