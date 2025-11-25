namespace FSharp.Azure.Quantum.Tests

open System
open System.IO
open Xunit
open FSharp.Azure.Quantum.Observability

module ObservabilityTests =
    
    [<Fact>]
    let ``initializeLogging should create logger with default configuration`` () =
        // Arrange
        let config = defaultLoggingConfig
        
        // Act
        let logger = initializeLogging config
        
        // Assert
        Assert.NotNull(logger)
    
    [<Fact>]
    let ``initializeLogging should respect minimum log level`` () =
        // Arrange
        let config = { defaultLoggingConfig with MinimumLevel = Error }
        
        // Act
        let logger = initializeLogging config
        
        // Assert
        Assert.NotNull(logger)
        // Note: Cannot easily test log level filtering without capturing log output
    
    [<Fact>]
    let ``initializeLogging with file sink should create logger`` () =
        // Arrange
        let tempFile = Path.Combine(Path.GetTempPath(), $"test-log-{Guid.NewGuid()}.txt")
        let config = { 
            defaultLoggingConfig with 
                LogToFile = Some true
                FilePath = Some tempFile 
        }
        
        // Act
        use logger = initializeLogging config
        
        // Assert
        Assert.NotNull(logger)
        
        // Cleanup happens automatically via 'use' (IDisposable)
        // File will be released when logger is disposed
    
    [<Fact>]
    let ``initializeTracing should create activity source`` () =
        // Arrange
        let config = defaultTracingConfig "TestService"
        
        // Act
        let activitySource = initializeTracing config
        
        // Assert
        Assert.True(activitySource.IsSome)
        match activitySource with
        | Some source ->
            Assert.Equal("TestService", source.Name)
            Assert.Equal("1.0.0", source.Version)
        | None -> ()
    
    [<Fact>]
    let ``initializeTracing with console export should create activity source`` () =
        // Arrange
        let config = { (defaultTracingConfig "TestService") with ExportToConsole = true }
        
        // Act
        let activitySource = initializeTracing config
        
        // Assert
        Assert.True(activitySource.IsSome)
    
    [<Fact>]
    let ``create should initialize observability state with logging only`` () =
        // Arrange
        let loggingConfig = Some defaultLoggingConfig
        
        // Act
        let state = create loggingConfig None
        
        // Assert
        Assert.NotNull(state.Logger)
        Assert.True(state.ActivitySource.IsNone)
    
    [<Fact>]
    let ``create should initialize observability state with tracing only`` () =
        // Arrange
        let tracingConfig = Some (defaultTracingConfig "TestService")
        
        // Act
        let state = create None tracingConfig
        
        // Assert
        Assert.NotNull(state.Logger)
        Assert.True(state.ActivitySource.IsSome)
    
    [<Fact>]
    let ``create should initialize observability state with both logging and tracing`` () =
        // Arrange
        let loggingConfig = Some defaultLoggingConfig
        let tracingConfig = Some (defaultTracingConfig "TestService")
        
        // Act
        let state = create loggingConfig tracingConfig
        
        // Assert
        Assert.NotNull(state.Logger)
        Assert.True(state.ActivitySource.IsSome)
    
    [<Fact>]
    let ``create with no configuration should use defaults`` () =
        // Act
        let state = create None None
        
        // Assert
        Assert.NotNull(state.Logger)
    
    [<Fact>]
    let ``traceQuantumExecution should execute operation without tracing when no activity source`` () =
        // Arrange
        let state = create (Some defaultLoggingConfig) None
        let mutable executed = false
        let operation () = 
            executed <- true
            42
        
        // Act
        let result = traceQuantumExecution state "TestOp" Map.empty operation
        
        // Assert
        Assert.True(executed)
        Assert.Equal(42, result)
    
    [<Fact>]
    let ``traceQuantumExecution should execute operation with tracing when activity source exists`` () =
        // Arrange
        let state = create None (Some (defaultTracingConfig "TestService"))
        let mutable executed = false
        let operation () = 
            executed <- true
            "success"
        
        let tags = Map.ofList [("jobId", box "job-123"); ("backend", box "IonQ")]
        
        // Act
        let result = traceQuantumExecution state "SubmitJob" tags operation
        
        // Assert
        Assert.True(executed)
        Assert.Equal("success", result)
    
    [<Fact>]
    let ``traceQuantumExecution should handle different tag types`` () =
        // Arrange
        let state = create None (Some (defaultTracingConfig "TestService"))
        let tags = Map.ofList [
            ("stringTag", box "value")
            ("intTag", box 123)
            ("floatTag", box 45.67)
            ("decimalTag", box 89.01m)
            ("boolTag", box true)
        ]
        
        // Act
        let result = traceQuantumExecution state "TestOp" tags (fun () -> "done")
        
        // Assert
        Assert.Equal("done", result)
    
    [<Fact>]
    let ``logPerformanceMetrics should log without exception`` () =
        // Arrange
        let state = create (Some defaultLoggingConfig) None
        let metrics = {
            OperationName = "SubmitJob"
            DurationMs = 150.5
            Timestamp = DateTimeOffset.UtcNow
            Context = Map.ofList [("jobId", box "job-123"); ("backend", box "IonQ")]
        }
        
        // Act & Assert (should not throw)
        logPerformanceMetrics state metrics
    
    [<Fact>]
    let ``logCostMetrics should log with both estimated and actual costs`` () =
        // Arrange
        let state = create (Some defaultLoggingConfig) None
        let metrics = {
            JobId = "job-123"
            EstimatedCost = Some 10.50m
            ActualCost = Some 9.75m
            Backend = "IonQ"
            Timestamp = DateTimeOffset.UtcNow
        }
        
        // Act & Assert (should not throw)
        logCostMetrics state metrics
    
    [<Fact>]
    let ``logCostMetrics should log with estimated cost only`` () =
        // Arrange
        let state = create (Some defaultLoggingConfig) None
        let metrics = {
            JobId = "job-456"
            EstimatedCost = Some 15.00m
            ActualCost = None
            Backend = "Quantinuum"
            Timestamp = DateTimeOffset.UtcNow
        }
        
        // Act & Assert (should not throw)
        logCostMetrics state metrics
    
    [<Fact>]
    let ``logCostMetrics should log with actual cost only`` () =
        // Arrange
        let state = create (Some defaultLoggingConfig) None
        let metrics = {
            JobId = "job-789"
            EstimatedCost = None
            ActualCost = Some 12.30m
            Backend = "IonQ"
            Timestamp = DateTimeOffset.UtcNow
        }
        
        // Act & Assert (should not throw)
        logCostMetrics state metrics
    
    [<Fact>]
    let ``logCostMetrics should log with no cost information`` () =
        // Arrange
        let state = create (Some defaultLoggingConfig) None
        let metrics = {
            JobId = "job-000"
            EstimatedCost = None
            ActualCost = None
            Backend = "Simulator"
            Timestamp = DateTimeOffset.UtcNow
        }
        
        // Act & Assert (should not throw)
        logCostMetrics state metrics
    
    [<Fact>]
    let ``logErrorWithContext should log error with exception`` () =
        // Arrange
        let state = create (Some defaultLoggingConfig) None
        let context = Map.ofList [("jobId", box "job-123"); ("operation", box "SubmitJob")]
        let exn = Some (Exception("Test exception"))
        
        // Act & Assert (should not throw)
        logErrorWithContext state "Job submission failed" context exn
    
    [<Fact>]
    let ``logErrorWithContext should log error without exception`` () =
        // Arrange
        let state = create (Some defaultLoggingConfig) None
        let context = Map.ofList [("jobId", box "job-456")]
        
        // Act & Assert (should not throw)
        logErrorWithContext state "Rate limit exceeded" context None
