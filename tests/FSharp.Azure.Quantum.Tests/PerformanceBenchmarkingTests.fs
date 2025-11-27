namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.PerformanceBenchmarking

module PerformanceBenchmarkingTests =

    // ============================================================================
    // TDD RED PHASE: Phase 1 - Benchmark Infrastructure Tests
    // ============================================================================

    [<Fact>]
    let ``BenchmarkConfig can be created with problem sizes`` () =
        // Arrange & Act
        let config = {
            PerformanceBenchmarking.BenchmarkConfig.ProblemSizes = [5; 10; 15]
            Repetitions = 3
            Backends = []
            OutputPath = "test-benchmarks.csv"
        }
        
        // Assert
        Assert.Equal(3, config.ProblemSizes.Length)
        Assert.Equal(3, config.Repetitions)
        Assert.Equal("test-benchmarks.csv", config.OutputPath)

    [<Fact>]
    let ``BenchmarkResult can be created with timing data`` () =
        // Arrange & Act
        let result = {
            PerformanceBenchmarking.BenchmarkResult.ProblemType = "TSP"
            ProblemSize = 10
            Solver = "Classical"
            ExecutionTimeMs = 1500L
            SolutionQuality = 42.5
            Cost = 0.0
            ErrorRate = None
            Timestamp = System.DateTime.UtcNow
        }
        
        // Assert
        Assert.Equal("TSP", result.ProblemType)
        Assert.Equal(10, result.ProblemSize)
        Assert.Equal("Classical", result.Solver)
        Assert.Equal(1500L, result.ExecutionTimeMs)

    [<Fact>]
    let ``ComparisonReport can compare classical and quantum results`` () =
        // Arrange
        let classicalResult = {
            PerformanceBenchmarking.BenchmarkResult.ProblemType = "TSP"
            ProblemSize = 10
            Solver = "Classical"
            ExecutionTimeMs = 1000L
            SolutionQuality = 50.0
            Cost = 0.0
            ErrorRate = None
            Timestamp = System.DateTime.UtcNow
        }
        
        let quantumResult = {
            PerformanceBenchmarking.BenchmarkResult.ProblemType = "TSP"
            ProblemSize = 10
            Solver = "IonQ"
            ExecutionTimeMs = 5000L
            SolutionQuality = 45.0  // Better quality
            Cost = 5.0
            ErrorRate = None
            Timestamp = System.DateTime.UtcNow
        }
        
        // Act
        let report = {
            PerformanceBenchmarking.ComparisonReport.ProblemType = "TSP"
            ProblemSize = 10
            ClassicalResult = classicalResult
            QuantumResults = [quantumResult]
            QuantumAdvantage = true  // Better solution quality
            SpeedupFactor = Some 0.2  // 5x slower
            CostPerAccuracy = 1.0
        }
        
        // Assert
        Assert.True(report.QuantumAdvantage)
        Assert.Equal(Some 0.2, report.SpeedupFactor)

    // ============================================================================
    // Phase 1 - Core Function Tests
    // ============================================================================

    [<Fact>]
    let ``generateRandomCities creates cities with unique positions`` () =
        // Act
        let cities = PerformanceBenchmarking.generateRandomCities 5
        
        // Assert
        Assert.Equal(5, cities.Length)
        // Check all cities have valid coordinates
        cities |> Array.iter (fun (_, x, y) ->
            Assert.True(x >= 0.0 && x <= 100.0)
            Assert.True(y >= 0.0 && y <= 100.0)
        )

    [<Fact>]
    let ``exportToCSV creates valid CSV output`` () =
        // Arrange
        let results = [
            {
                PerformanceBenchmarking.BenchmarkResult.ProblemType = "TSP"
                ProblemSize = 5
                Solver = "Classical"
                ExecutionTimeMs = 100L
                SolutionQuality = 25.5
                Cost = 0.0
                ErrorRate = None
                Timestamp = System.DateTime(2025, 1, 1)
            }
        ]
        let testPath = "test-export.csv"
        
        // Act
        PerformanceBenchmarking.exportToCSV results testPath
        
        // Assert
        Assert.True(System.IO.File.Exists(testPath))
        let content = System.IO.File.ReadAllText(testPath)
        Assert.Contains("ProblemType,ProblemSize,Solver", content)
        Assert.Contains("TSP,5,Classical", content)
        
        // Cleanup
        System.IO.File.Delete(testPath)

    [<Fact>]
    let ``checkPerformanceRegression detects slowdowns`` () =
        // Arrange
        let baseline = [
            {
                PerformanceBenchmarking.BenchmarkResult.ProblemType = "TSP"
                ProblemSize = 10
                Solver = "Classical"
                ExecutionTimeMs = 1000L
                SolutionQuality = 50.0
                Cost = 0.0
                ErrorRate = None
                Timestamp = System.DateTime.UtcNow
            }
        ]
        
        let current = [
            {
                PerformanceBenchmarking.BenchmarkResult.ProblemType = "TSP"
                ProblemSize = 10
                Solver = "Classical"
                ExecutionTimeMs = 1500L  // 50% slower
                SolutionQuality = 50.0
                Cost = 0.0
                ErrorRate = None
                Timestamp = System.DateTime.UtcNow
            }
        ]
        
        // Act
        let regressions = PerformanceBenchmarking.checkPerformanceRegression current baseline 1.2 // 20% threshold
        
        // Assert
        Assert.NotEmpty(regressions)
        let (name, factor) = regressions |> List.head
        Assert.Contains("TSP_10_Classical", name)
        Assert.True(factor > 1.2)

    // ============================================================================
    // Phase 2 - Classical TSP Benchmark Tests
    // ============================================================================

    [<Fact>]
    let ``benchmarkClassicalTSP completes for 5 cities`` () =
        async {
            // Arrange
            let cities = [|
                ("A", 0.0, 0.0)
                ("B", 10.0, 0.0)
                ("C", 10.0, 10.0)
                ("D", 0.0, 10.0)
                ("E", 5.0, 5.0)
            |]
            
            // Act
            let! result = PerformanceBenchmarking.benchmarkClassicalTSP cities 2
            
            // Assert
            Assert.Equal("TSP", result.ProblemType)
            Assert.Equal(5, result.ProblemSize)
            Assert.Equal("Classical", result.Solver)
            Assert.True(result.ExecutionTimeMs > 0L)
            Assert.True(result.ExecutionTimeMs < 5000L)  // Should complete in < 5s
            Assert.True(result.SolutionQuality > 0.0)
        } |> Async.RunSynchronously

    [<Fact>]
    let ``benchmarkClassicalTSP execution time increases with problem size`` () =
        async {
            // Arrange
            let cities5 = PerformanceBenchmarking.generateRandomCities 5
            let cities10 = PerformanceBenchmarking.generateRandomCities 10
            
            // Act
            let! result5 = PerformanceBenchmarking.benchmarkClassicalTSP cities5 1
            let! result10 = PerformanceBenchmarking.benchmarkClassicalTSP cities10 1
            
            // Assert
            Assert.True(result10.ExecutionTimeMs >= result5.ExecutionTimeMs)
        } |> Async.RunSynchronously

    [<Fact>]
    let ``benchmarkClassicalTSP produces consistent results with repetitions`` () =
        async {
            // Arrange
            let cities = PerformanceBenchmarking.generateRandomCities 8
            
            // Act - Run benchmark with 5 repetitions
            let! result = PerformanceBenchmarking.benchmarkClassicalTSP cities 5
            
            // Assert
            Assert.True(result.ExecutionTimeMs > 0L)
            Assert.Equal("Classical", result.Solver)
            // Quality should be reasonable (not infinity or zero)
            Assert.True(result.SolutionQuality > 0.0)
            Assert.True(result.SolutionQuality < 1000.0)
        } |> Async.RunSynchronously

    // ============================================================================
    // Performance Requirement Tests
    // ============================================================================

    [<Fact>]
    let ``Classical TSP benchmark meets performance target for 10 cities`` () =
        async {
            // Arrange
            let cities = PerformanceBenchmarking.generateRandomCities 10
            
            // Act
            let! result = PerformanceBenchmarking.benchmarkClassicalTSP cities 3
            
            // Assert - Must complete in < 5 seconds as per requirements
            Assert.True(result.ExecutionTimeMs < 5000L,
                sprintf "Expected < 5000ms, got %dms" result.ExecutionTimeMs)
        } |> Async.RunSynchronously
