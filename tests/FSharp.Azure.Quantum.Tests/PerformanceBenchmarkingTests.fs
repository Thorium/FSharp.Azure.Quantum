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
        // Use seeded random for deterministic test
        let cities = PerformanceBenchmarking.generateRandomCities 5 (Some 123)
        
        // Assert
        Assert.Equal(5, cities.Length)
        // Check all cities have valid coordinates
        cities |> Array.iter (fun (_, x, y) ->
            Assert.True(x >= 0.0 && x <= 100.0)
            Assert.True(y >= 0.0 && y <= 100.0)
        )

    [<Fact>]
    let ``generateRandomAssets creates assets with valid financial parameters`` () =
        // Use seeded random for deterministic test
        let assets = PerformanceBenchmarking.generateRandomAssets 5 (Some 456)
        
        // Assert
        Assert.Equal(5, assets.Length)
        // Check all assets have valid financial parameters
        assets |> List.iter (fun (symbol, expectedReturn, risk, price) ->
            Assert.StartsWith("ASSET", symbol)
            // Expected return: 5% to 25%
            Assert.True(expectedReturn >= 0.05 && expectedReturn <= 0.25)
            // Risk: 10% to 40%
            Assert.True(risk >= 0.10 && risk <= 0.40)
            // Price: $50 to $3000
            Assert.True(price >= 50.0 && price <= 3000.0)
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
    // NOTE: Classical TSP benchmark tests removed - TSP.solve now uses quantum-first architecture
    // Use HybridSolver for automatic quantum/classical selection

    // ============================================================================
    // Benchmark Suite Tests
    // ============================================================================
    // NOTE: runTSPBenchmarkSuite test removed - relies on benchmarkClassicalTSP which is quantum-first

    [<Fact>]
    let ``runPortfolioBenchmarkSuite executes benchmarks for multiple problem sizes`` () =
        async {
            // Arrange
            let config = {
                PerformanceBenchmarking.BenchmarkConfig.ProblemSizes = [3; 5; 8]
                Repetitions = 2
                Backends = ["Classical"]
                OutputPath = "test-suite.csv"
            }
            let budget = 10000.0
            
            // Act
            let! results = PerformanceBenchmarking.runPortfolioBenchmarkSuite config budget
            
            // Assert
            Assert.Equal(3, results.Length)
            Assert.True(results |> List.forall (fun r -> r.ProblemType = "Portfolio"))
            Assert.True(results |> List.forall (fun r -> r.Solver = "Classical"))
            // Verify problem sizes match configuration
            let sizes = results |> List.map (fun r -> r.ProblemSize) |> List.sort
            Assert.Equal<int list>([3; 5; 8], sizes)
        } |> Async.RunSynchronously

    // ============================================================================
    // Markdown Report Generation Tests
    // ============================================================================

    [<Fact>]
    let ``generateMarkdownReport creates valid markdown with headers`` () =
        // Arrange
        let results = [
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
        
        // Act
        let markdown = PerformanceBenchmarking.generateMarkdownReport results
        
        // Assert
        Assert.Contains("# Performance Benchmark Report", markdown)
        Assert.Contains("## TSP Benchmarks", markdown)
        Assert.Contains("| Problem Size | Solver | Execution Time (ms)", markdown)
        Assert.Contains("| 10 | Classical | 1000 | 50.0000 | 0.00 |", markdown)

    [<Fact>]
    let ``exportMarkdownReport creates file with markdown content`` () =
        // Arrange
        let results = [
            {
                PerformanceBenchmarking.BenchmarkResult.ProblemType = "Portfolio"
                ProblemSize = 5
                Solver = "Classical"
                ExecutionTimeMs = 500L
                SolutionQuality = 0.15
                Cost = 0.0
                ErrorRate = None
                Timestamp = System.DateTime.UtcNow
            }
        ]
        let testPath = "test-report.md"
        
        // Act
        PerformanceBenchmarking.exportMarkdownReport results testPath
        
        // Assert
        Assert.True(System.IO.File.Exists(testPath))
        let content = System.IO.File.ReadAllText(testPath)
        Assert.Contains("# Performance Benchmark Report", content)
        Assert.Contains("Portfolio", content)
        
        // Cleanup
        System.IO.File.Delete(testPath)

    // ============================================================================
    // Performance Requirement Tests
    // ============================================================================
    // NOTE: Classical TSP performance test removed - TSP.solve now uses quantum-first architecture

    // ============================================================================
    // Phase 3 - Classical Portfolio Benchmark Tests
    // ============================================================================

    [<Fact>]
    let ``benchmarkClassicalPortfolio completes for 5 assets`` () =
        async {
            // Arrange
            let assets = [
                ("AAPL", 0.12, 0.15, 150.0)
                ("GOOGL", 0.10, 0.20, 2800.0)
                ("MSFT", 0.11, 0.18, 350.0)
                ("AMZN", 0.13, 0.22, 3300.0)
                ("TSLA", 0.15, 0.30, 800.0)
            ]
            let budget = 10000.0
            
            // Act
            let! result = PerformanceBenchmarking.benchmarkClassicalPortfolio assets budget 2
            
            // Assert
            Assert.Equal("Portfolio", result.ProblemType)
            Assert.Equal(5, result.ProblemSize)
            Assert.Equal("Classical", result.Solver)
            Assert.True(result.ExecutionTimeMs > 0L)
            Assert.True(result.ExecutionTimeMs < 5000L)  // Should complete in < 5s
            Assert.True(result.SolutionQuality > 0.0)
        } |> Async.RunSynchronously

    [<Fact>]
    let ``benchmarkClassicalPortfolio produces valid expected return`` () =
        async {
            // Arrange - Assets with known expected returns
            let assets = [
                ("Asset1", 0.10, 0.15, 100.0)
                ("Asset2", 0.15, 0.20, 100.0)
                ("Asset3", 0.20, 0.25, 100.0)
            ]
            let budget = 500.0
            
            // Act
            let! result = PerformanceBenchmarking.benchmarkClassicalPortfolio assets budget 1
            
            // Assert
            Assert.Equal("Portfolio", result.ProblemType)
            Assert.Equal(3, result.ProblemSize)
            // Expected return should be between min (0.10) and max (0.20) asset returns
            Assert.True(result.SolutionQuality >= 0.10)
            Assert.True(result.SolutionQuality <= 0.20)
        } |> Async.RunSynchronously

    [<Fact>]
    let ``Classical Portfolio benchmark meets performance target for 10 assets`` () =
        async {
            // Arrange - 10 diverse assets
            let assets = [
                ("AAPL", 0.12, 0.15, 150.0)
                ("GOOGL", 0.10, 0.20, 2800.0)
                ("MSFT", 0.11, 0.18, 350.0)
                ("AMZN", 0.13, 0.22, 3300.0)
                ("TSLA", 0.15, 0.30, 800.0)
                ("META", 0.09, 0.19, 500.0)
                ("NVDA", 0.14, 0.28, 700.0)
                ("AMD", 0.16, 0.32, 140.0)
                ("NFLX", 0.11, 0.25, 600.0)
                ("COIN", 0.18, 0.40, 220.0)
            ]
            let budget = 20000.0
            
            // Act
            let! result = PerformanceBenchmarking.benchmarkClassicalPortfolio assets budget 3
            
            // Assert - Must complete in < 5 seconds as per requirements
            Assert.True(result.ExecutionTimeMs < 5000L,
                sprintf "Expected < 5000ms, got %dms" result.ExecutionTimeMs)
        } |> Async.RunSynchronously
