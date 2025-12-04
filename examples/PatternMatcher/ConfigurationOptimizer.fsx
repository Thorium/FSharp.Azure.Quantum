// ============================================================================
// Quantum Pattern Matcher Examples - FSharp.Azure.Quantum
// ============================================================================
//
// This script demonstrates the Quantum Pattern Matcher API using Grover's
// algorithm to find items matching a pattern in large search spaces:
//
// 1. System Configuration Optimization
// 2. Machine Learning Hyperparameter Tuning
// 3. Feature Selection for ML Models
//
// WHAT IS PATTERN MATCHING SEARCH:
// Find items in a search space that satisfy a pattern predicate (expensive
// evaluation). Uses quantum search to accelerate exploration when evaluation
// is computationally expensive.
//
// WHY USE QUANTUM:
// - Grover's algorithm provides O(√N) speedup over classical search
// - Ideal when evaluation is expensive (10+ seconds per config)
// - Quadratic speedup for exploring configuration spaces
// - Find top-N best matches efficiently
//
// ============================================================================

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"


open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumPatternMatcher
open FSharp.Azure.Quantum.Core.BackendAbstraction

// ============================================================================
// BACKEND CONFIGURATION
// ============================================================================

// Create local quantum simulator (fast, for development/testing)
let localBackend = LocalBackend() :> IQuantumBackend

// For cloud execution, use IonQ or Rigetti backend:
// let cloudBackend = IonQBackend(workspace, resourceId) :> IQuantumBackend
// let cloudBackend = RigettiBackend(workspace, resourceId) :> IQuantumBackend

// ============================================================================
// EXAMPLE 1: Database Configuration Optimization
// ============================================================================
//
// PROBLEM: Find optimal database configurations from 256 possible combinations
// that achieve:
// - Throughput > 10,000 queries/second
// - Latency < 50 milliseconds
// - CPU usage < 80%
//
// REAL-WORLD IMPACT:
// - Database tuning requires expensive benchmarks (30-60 seconds each)
// - Classical search: 256 benchmarks × 45 seconds = 192 minutes
// - Quantum search: √256 = 16 evaluations × 45 seconds = 12 minutes
// - 16× speedup saves hours of testing time
//
printfn "========================================="
printfn "EXAMPLE 1: Database Configuration"
printfn "========================================="
printfn ""

// Configuration parameters (8 bits = 256 combinations)
type DbConfig = {
    CacheSize: int        // 2 bits: 64/128/256/512 MB
    PoolSize: int         // 2 bits: 10/50/100/200 connections
    QueryTimeout: int     // 2 bits: 5/10/30/60 seconds
    LogLevel: string      // 2 bits: Debug/Info/Warn/Error
}

// Decode 8-bit index to configuration
let decodeDbConfig (index: int) : DbConfig =
    let cacheSizes = [| 64; 128; 256; 512 |]
    let poolSizes = [| 10; 50; 100; 200 |]
    let timeouts = [| 5; 10; 30; 60 |]
    let logLevels = [| "Debug"; "Info"; "Warn"; "Error" |]
    
    {
        CacheSize = cacheSizes.[(index >>> 6) &&& 0b11]
        PoolSize = poolSizes.[(index >>> 4) &&& 0b11]
        QueryTimeout = timeouts.[(index >>> 2) &&& 0b11]
        LogLevel = logLevels.[index &&& 0b11]
    }

printfn "Search Space: 256 configurations (4 parameters × 4 values each)"
printfn ""
printfn "Parameters:"
printfn "  - Cache Size:    64/128/256/512 MB"
printfn "  - Pool Size:     10/50/100/200 connections"
printfn "  - Query Timeout: 5/10/30/60 seconds"
printfn "  - Log Level:     Debug/Info/Warn/Error"
printfn ""

// Simulate expensive benchmark (normally 30-60 seconds, here simplified)
let benchmarkDatabase (config: DbConfig) =
    // Real benchmark would: setup DB, load data, run queries, measure metrics
    // Here: simplified scoring based on parameter balance
    
    let cacheScore = 
        match config.CacheSize with
        | 256 | 512 -> 100.0  // Good cache
        | 128 -> 70.0
        | _ -> 40.0
    
    let poolScore = 
        match config.PoolSize with
        | 100 | 200 -> 100.0  // Good pool size
        | 50 -> 60.0
        | _ -> 30.0
    
    let timeoutScore = 
        match config.QueryTimeout with
        | 30 | 60 -> 100.0  // Reasonable timeouts
        | 10 -> 50.0
        | _ -> 20.0
    
    let logScore = 
        match config.LogLevel with
        | "Info" | "Warn" -> 100.0  // Production-ready
        | "Error" -> 80.0
        | _ -> 40.0  // Debug too verbose
    
    let throughput = (cacheScore + poolScore) * 50.0  // queries/sec
    let latency = 100.0 - (cacheScore + poolScore) / 4.0  // milliseconds
    let cpuUsage = 
        if config.LogLevel = "Debug" then 85.0  // Too high
        else 75.0 - poolScore / 10.0
    
    (throughput, latency, cpuUsage)

// Pattern: High-performance configuration
let isGoodConfig (index: int) : bool =
    let config = decodeDbConfig index
    let (throughput, latency, cpuUsage) = benchmarkDatabase config
    
    // Performance criteria
    throughput > 10000.0 && latency < 50.0 && cpuUsage < 80.0

// Build pattern matcher problem
let dbOptimizationProblem = patternMatcher {
    searchSpaceSize 256  // All possible configs (use searchSpaceSize for integer)
    matchPattern isGoodConfig
    findTop 5  // Find top 5 configurations
    
    // Use local quantum simulator
    backend localBackend
    shots 1000  // Number of measurements
}

printfn "Searching for optimal configurations..."
printfn "(Using Grover's algorithm for √N speedup)"
printfn ""

match solve dbOptimizationProblem with
| Ok result ->
    printfn "✅ FOUND %d MATCHING CONFIGURATIONS!" result.Matches.Length
    printfn ""
    
    printfn "  Top Configurations:"
    result.Matches 
    |> List.iteri (fun i index ->
        let config = decodeDbConfig index
        let (throughput, latency, cpuUsage) = benchmarkDatabase config
        
        printfn "    #%d - Config Index %d:" (i+1) index
        printfn "       Cache:      %d MB" config.CacheSize
        printfn "       Pool:       %d connections" config.PoolSize
        printfn "       Timeout:    %d seconds" config.QueryTimeout
        printfn "       Log Level:  %s" config.LogLevel
        printfn "       Performance:"
        printfn "         Throughput: %.0f queries/sec" throughput
        printfn "         Latency:    %.1f ms" latency
        printfn "         CPU Usage:  %.1f%%" cpuUsage
        printfn ""
    )
    
    printfn "  Quantum Resources:"
    printfn "    Search space: 256 configurations"
    printfn "    Quantum advantage: √256 = 16× fewer benchmarks"
    printfn "    Time saved: 256 → 16 evaluations (16× speedup)"

| Error msg ->
    printfn "❌ Error: %s" msg

printfn ""
printfn ""

// ============================================================================
// EXAMPLE 2: Machine Learning Hyperparameter Tuning
// ============================================================================
//
// PROBLEM: Find optimal hyperparameters for neural network training from
// 128 combinations that achieve >95% validation accuracy.
//
// REAL-WORLD IMPACT:
// - Training each config takes 5-30 minutes
// - Classical grid search: 128 × 15 min = 32 hours
// - Quantum search: √128 ≈ 11 evaluations × 15 min = 2.75 hours
// - 11× speedup saves days of GPU time
//
printfn "========================================="
printfn "EXAMPLE 2: ML Hyperparameter Tuning"
printfn "========================================="
printfn ""

// Hyperparameter space (7 bits = 128 combinations)
type MLConfig = {
    LearningRate: float     // 2 bits: 0.001/0.01/0.1/1.0
    BatchSize: int          // 2 bits: 16/32/64/128
    Layers: int             // 2 bits: 1/2/3/4 hidden layers
    DropoutRate: float      // 1 bit: 0.0/0.5
}

let decodeMLConfig (index: int) : MLConfig =
    let learningRates = [| 0.001; 0.01; 0.1; 1.0 |]
    let batchSizes = [| 16; 32; 64; 128 |]
    let layerCounts = [| 1; 2; 3; 4 |]
    
    {
        LearningRate = learningRates.[(index >>> 5) &&& 0b11]
        BatchSize = batchSizes.[(index >>> 3) &&& 0b11]
        Layers = layerCounts.[(index >>> 1) &&& 0b11]
        DropoutRate = if (index &&& 0b1) = 1 then 0.5 else 0.0
    }

printfn "Search Space: 128 hyperparameter combinations"
printfn ""
printfn "Parameters:"
printfn "  - Learning Rate: 0.001/0.01/0.1/1.0"
printfn "  - Batch Size:    16/32/64/128"
printfn "  - Hidden Layers: 1/2/3/4"
printfn "  - Dropout:       0.0/0.5"
printfn ""

// Simulate expensive model training
let trainModel (config: MLConfig) : float =
    // Real training would: build network, train epochs, validate
    // Here: simplified accuracy scoring
    
    let lrScore = 
        match config.LearningRate with
        | 0.01 | 0.1 -> 95.0  // Good learning rates
        | 0.001 -> 85.0
        | _ -> 60.0  // Too high
    
    let batchScore = 
        match config.BatchSize with
        | 32 | 64 -> 95.0  // Good batch sizes
        | 16 -> 88.0
        | _ -> 75.0
    
    let layerScore = 
        match config.Layers with
        | 2 | 3 -> 95.0  // Good depth
        | 1 -> 82.0
        | _ -> 70.0  // Too deep, overfits
    
    let dropoutBonus = if config.DropoutRate > 0.0 then 5.0 else 0.0
    
    // Validation accuracy
    (lrScore + batchScore + layerScore) / 3.0 + dropoutBonus

// Pattern: High-accuracy model
let isGoodModel (index: int) : bool =
    let config = decodeMLConfig index
    let accuracy = trainModel config
    accuracy > 95.0

let mlTuningProblem = patternMatcher {
    searchSpaceSize 128  // Use searchSpaceSize for integer
    matchPattern isGoodModel
    findTop 3  // Top 3 best models
    
    // Use local quantum simulator
    backend localBackend
    shots 1000
}

printfn "Searching for optimal hyperparameters..."
printfn ""

match solve mlTuningProblem with
| Ok result ->
    printfn "✅ FOUND %d HIGH-ACCURACY CONFIGURATIONS!" result.Matches.Length
    printfn ""
    
    printfn "  Top Model Configurations:"
    result.Matches 
    |> List.iteri (fun i index ->
        let config = decodeMLConfig index
        let accuracy = trainModel config
        
        printfn "    #%d - Config Index %d:" (i+1) index
        printfn "       Learning Rate: %.3f" config.LearningRate
        printfn "       Batch Size:    %d" config.BatchSize
        printfn "       Hidden Layers: %d" config.Layers
        printfn "       Dropout Rate:  %.1f" config.DropoutRate
        printfn "       Val Accuracy:  %.2f%%" accuracy
        printfn ""
    )
    
    printfn "  Quantum Resources:"
    printfn "    Search space: 128 configurations"
    printfn "    Quantum advantage: √128 ≈ 11× fewer training runs"

| Error msg ->
    printfn "❌ Error: %s" msg

printfn ""
printfn ""

// ============================================================================
// EXAMPLE 3: Feature Selection for ML
// ============================================================================
//
// PROBLEM: Select best subset of 8 features (256 combinations) that achieves
// high model accuracy while minimizing feature count (simpler model).
//
// REAL-WORLD IMPACT:
// - Each feature subset requires full model training
// - Fewer features = faster inference, lower costs
// - Quantum search finds optimal subsets 16× faster
//
printfn "========================================="
printfn "EXAMPLE 3: Feature Selection"
printfn "========================================="
printfn ""

let features = [|
    "Age"; "Income"; "CreditScore"; "Education"
    "Employment"; "LoanHistory"; "Assets"; "Debt"
|]

printfn "Feature Pool: %d features" features.Length
printfn "Features: %s" (String.concat ", " features)
printfn ""
printfn "Goal: Select features achieving >90%% accuracy with minimal count"
printfn ""

// Decode bit pattern to feature subset
let decodeFeatureSet (index: int) : string list =
    features 
    |> Array.mapi (fun i feature -> 
        if (index &&& (1 <<< i)) <> 0 then Some feature else None
    )
    |> Array.choose id
    |> Array.toList

// Simulate model training with feature subset
let evaluateFeatureSet (featureSet: string list) : (float * int) =
    // Real evaluation: train model with subset, measure accuracy
    // Here: simplified scoring
    
    // Key features
    let hasIncome = featureSet |> List.contains "Income"
    let hasCreditScore = featureSet |> List.contains "CreditScore"
    let hasLoanHistory = featureSet |> List.contains "LoanHistory"
    
    let baseAccuracy = 
        match (hasIncome, hasCreditScore, hasLoanHistory) with
        | (true, true, true) -> 94.0   // All key features
        | (true, true, false) -> 91.0  // Missing loan history
        | (true, false, true) -> 88.0
        | _ -> 75.0  // Missing too many
    
    // Penalty for too many features (overfitting)
    let featureCount = featureSet.Length
    let penalty = 
        if featureCount > 5 then float (featureCount - 5) * 2.0
        else 0.0
    
    let finalAccuracy = baseAccuracy - penalty
    
    (finalAccuracy, featureCount)

// Pattern: Good accuracy with reasonable feature count
let isGoodFeatureSet (index: int) : bool =
    let featureSet = decodeFeatureSet index
    let (accuracy, count) = evaluateFeatureSet featureSet
    
    // Criteria: >90% accuracy, 3-6 features
    accuracy > 90.0 && count >= 3 && count <= 6

let featureSelectionProblem = patternMatcher {
    searchSpaceSize 256  // 2^8 = 256 subsets (use searchSpaceSize for integer)
    matchPattern isGoodFeatureSet
    findTop 5
    
    // Use local quantum simulator
    backend localBackend
    shots 1000
}

printfn "Searching for optimal feature subsets..."
printfn ""

match solve featureSelectionProblem with
| Ok result ->
    printfn "✅ FOUND %d OPTIMAL FEATURE SUBSETS!" result.Matches.Length
    printfn ""
    
    printfn "  Top Feature Combinations:"
    result.Matches 
    |> List.iteri (fun i index ->
        let featureSet = decodeFeatureSet index
        let (accuracy, count) = evaluateFeatureSet featureSet
        
        printfn "    #%d - Subset Index %d:" (i+1) index
        printfn "       Features (%d): %s" count (String.concat ", " featureSet)
        printfn "       Accuracy:      %.2f%%" accuracy
        printfn "       Complexity:    %s" 
            (if count <= 4 then "Simple" else "Moderate")
        printfn ""
    )
    
    printfn "  Quantum Resources:"
    printfn "    Search space: 256 feature subsets"
    printfn "    Quantum advantage: √256 = 16× fewer model trainings"

| Error msg ->
    printfn "❌ Error: %s" msg

printfn ""
printfn ""

// ============================================================================
// SUMMARY: When to Use Quantum Pattern Matcher
// ============================================================================

printfn "========================================="
printfn "WHEN TO USE QUANTUM PATTERN MATCHER"
printfn "========================================="
printfn ""
printfn "✅ GOOD FITS:"
printfn "  - Configuration optimization (databases, compilers, systems)"
printfn "  - Hyperparameter tuning (ML, simulation parameters)"
printfn "  - Feature selection (ML feature engineering)"
printfn "  - A/B testing at scale (find best variants)"
printfn "  - Search spaces: 100-10,000 candidates"
printfn "  - Expensive evaluation (10+ seconds per candidate)"
printfn ""
printfn "❌ NOT SUITABLE FOR:"
printfn "  - Fast evaluations (<1 second) - classical is better"
printfn "  - Very small search spaces (<50 items)"
printfn "  - Problems with known structure (use domain-specific)"
printfn "  - Need exact optimum (use optimization algorithms)"
printfn ""
printfn "🚀 QUANTUM ADVANTAGE:"
printfn "  - Grover's algorithm: O(√N) vs O(N) classical"
printfn "  - Best for: expensive evaluation + large search space"
printfn "  - Example: 256 configs × 60 sec = 4 hours → 16 × 60 sec = 16 min"
printfn "  - 15× speedup saves hours of compute time"
printfn ""
printfn "📚 RELATED BUILDERS:"
printfn "  - Constraint satisfaction: QuantumConstraintSolverBuilder"
printfn "  - Tree search problems: QuantumTreeSearchBuilder"
printfn "  - Graph problems: GraphColoringBuilder"
printfn ""
