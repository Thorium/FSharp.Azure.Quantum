# Observability Module Usage Guide

## Overview

The `FSharp.Azure.Quantum.Observability` module provides production-ready logging and telemetry for Azure Quantum operations using:
- **Serilog** for structured logging
- **OpenTelemetry** for distributed tracing
- Performance and cost metrics collection
- Error tracking with structured context

## Quick Start

### 1. Create Observability State

```fsharp
open FSharp.Azure.Quantum.Observability

// Using defaults (console logging, no tracing)
let state = create None None

// With custom logging configuration
let loggingConfig = {
    MinimumLevel = Information
    OutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
    LogToConsole = true
    LogToFile = Some true
    FilePath = Some "quantum-operations.log"
}

let stateWithLogging = create (Some loggingConfig) None

// With tracing enabled
let tracingConfig = defaultTracingConfig "AzureQuantum.Client"

let fullState = create (Some loggingConfig) (Some tracingConfig)
```

### 2. Log Performance Metrics

```fsharp
// Measure operation duration
let sw = System.Diagnostics.Stopwatch.StartNew()
let result = performQuantumOperation()
sw.Stop()

// Log the metrics
let perfMetrics = {
    OperationName = "SubmitJob"
    DurationMs = float sw.ElapsedMilliseconds
    Timestamp = DateTimeOffset.UtcNow
    Context = Map.ofList [
        ("jobId", box "job-12345")
        ("backend", box "IonQ")
        ("qubits", box 10)
    ]
}

logPerformanceMetrics state perfMetrics
// Output: [16:35:42 INF] Performance: SubmitJob completed in 150.5ms at 2025-11-25 16:35:42 +00:00 with context {...}
```

### 3. Log Cost Metrics

```fsharp
let costMetrics = {
    JobId = "job-12345"
    EstimatedCost = Some 10.50m
    ActualCost = Some 9.75m
    Backend = "IonQ"
    Timestamp = DateTimeOffset.UtcNow
}

logCostMetrics state costMetrics
// Output: [16:35:42 INF] Cost: Job job-12345 on backend IonQ - Estimated: $10.50, Actual: $9.75 at 2025-11-25 16:35:42 +00:00
```

### 4. Trace Quantum Operations

```fsharp
// Wrap operation with distributed tracing
let tags = Map.ofList [
    ("jobId", box "job-12345")
    ("backend", box "IonQ")
    ("operation", box "job_submission")
]

let result = traceQuantumExecution state "QuantumJobSubmission" tags (fun () ->
    // Your quantum operation here
    submitJobToAzure jobSubmission
)

// Creates a trace span with tags for distributed tracing
```

### 5. Log Errors with Context

```fsharp
try
    let result = submitJob submission
    result
with
| ex ->
    let context = Map.ofList [
        ("jobId", box jobId)
        ("backend", box "IonQ")
        ("operation", box "SubmitJob")
        ("timestamp", box DateTimeOffset.UtcNow)
    ]
    
    logErrorWithContext state "Job submission failed" context (Some ex)
    reraise()

// Output: [16:35:42 ERR] Error: Job submission failed with context {...}
//         System.Exception: Job submission failed
```

## Integration with QuantumClient

### Option 1: Extend Existing Client

```fsharp
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Observability

type InstrumentedQuantumClient(client: Client.QuantumClient, observability: ObservabilityState) =
    
    member this.SubmitJobWithTelemetry(submission: JobSubmission) =
        async {
            let sw = System.Diagnostics.Stopwatch.StartNew()
            
            // Trace the operation
            let tags = Map.ofList [
                ("operation", box "SubmitJob")
                ("target", box submission.Target.ToString())
            ]
            
            let! result = traceQuantumExecution observability "SubmitJob" tags (fun () ->
                client.SubmitJobAsync(submission) |> Async.RunSynchronously
            )
            
            sw.Stop()
            
            match result with
            | Ok response ->
                // Log performance metrics
                logPerformanceMetrics observability {
                    OperationName = "SubmitJob"
                    DurationMs = float sw.ElapsedMilliseconds
                    Timestamp = DateTimeOffset.UtcNow
                    Context = Map.ofList [("jobId", box response.JobId)]
                }
                
            | Error err ->
                // Log error
                logErrorWithContext observability "Job submission failed" 
                    (Map.ofList [("error", box err.ToString())]) None
            
            return result
        }
```

### Option 2: Functional Wrapper

```fsharp
module ObservableQuantumOperations =
    
    let withObservability (state: ObservabilityState) operationName (operation: Async<Result<'T, 'E>>) =
        async {
            let sw = System.Diagnostics.Stopwatch.StartNew()
            
            let! result = traceQuantumExecution state operationName Map.empty (fun () ->
                operation |> Async.RunSynchronously
            )
            
            sw.Stop()
            
            // Log performance
            logPerformanceMetrics state {
                OperationName = operationName
                DurationMs = float sw.ElapsedMilliseconds
                Timestamp = DateTimeOffset.UtcNow
                Context = Map.empty
            }
            
            return result
        }
    
    // Usage:
    let state = create (Some defaultLoggingConfig) (Some (defaultTracingConfig "MyApp"))
    let result = withObservability state "SubmitJob" (client.SubmitJobAsync(submission))
```

## Log Levels

The module supports four log levels:

- **Debug**: Detailed diagnostic information
- **Information**: General informational messages (default)
- **Warning**: Warning messages for non-critical issues
- **Error**: Error messages for failures

```fsharp
let config = { 
    defaultLoggingConfig with 
        MinimumLevel = Debug  // Show all logs
}
```

## Structured Logging

All logs use structured logging with Serilog, making them searchable and queryable:

```fsharp
// Logs are structured as JSON when using appropriate sinks
logPerformanceMetrics state {
    OperationName = "SubmitJob"
    DurationMs = 150.5
    Timestamp = DateTimeOffset.UtcNow
    Context = Map.ofList [
        ("jobId", box "job-123")
        ("backend", box "IonQ")
    ]
}

// JSON output (when using JSON sink):
{
  "Timestamp": "2025-11-25T16:35:42.123Z",
  "Level": "Information",
  "Message": "Performance: SubmitJob completed in 150.5ms",
  "OperationName": "SubmitJob",
  "DurationMs": 150.5,
  "Context": {
    "jobId": "job-123",
    "backend": "IonQ"
  }
}
```

## OpenTelemetry Integration

### Exporting to Application Insights

```fsharp
// Configure OpenTelemetry with Application Insights exporter
// (requires additional NuGet package: Azure.Monitor.OpenTelemetry.Exporter)

open OpenTelemetry.Trace
open Azure.Monitor.OpenTelemetry.Exporter

let configureTracing() =
    let tracerProvider =
        Sdk.CreateTracerProviderBuilder()
            .AddSource("AzureQuantum.Client")
            .AddAzureMonitorTraceExporter(fun options ->
                options.ConnectionString <- "InstrumentationKey=your-key"
            )
            .Build()
    
    let tracingConfig = {
        ServiceName = "AzureQuantum.Client"
        ServiceVersion = "1.0.0"
        ExportToConsole = false
    }
    
    Some tracingConfig
```

### Viewing Traces

Traces created with `traceQuantumExecution` create OpenTelemetry spans that can be viewed in:
- Azure Application Insights
- Jaeger
- Zipkin
- Console (when ExportToConsole = true)

## Best Practices

1. **Create ObservabilityState once per application lifecycle**
   ```fsharp
   // At application startup
   let observability = create (Some loggingConfig) (Some tracingConfig)
   ```

2. **Use structured context consistently**
   ```fsharp
   let context = Map.ofList [
       ("jobId", box jobId)
       ("backend", box backend)
       ("timestamp", box DateTimeOffset.UtcNow)
   ]
   ```

3. **Log performance metrics for all operations**
   ```fsharp
   let sw = Stopwatch.StartNew()
   let result = operation()
   sw.Stop()
   logPerformanceMetrics state { ... }
   ```

4. **Always include context with errors**
   ```fsharp
   logErrorWithContext state message context (Some ex)
   ```

5. **Use appropriate log levels**
   - Debug: Internal state, diagnostics
   - Information: Normal operations, metrics
   - Warning: Unusual but handled conditions
   - Error: Failures, exceptions

## Performance Considerations

- Logger initialization has ~50ms overhead (one-time cost)
- Activity creation (tracing) has ~5µs overhead per operation
- Structured logging has minimal overhead when log level is filtered
- File logging is buffered and asynchronous

## Complete Example

```fsharp
open System
open System.Diagnostics
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Observability

// Initialize observability at application startup
let initializeApp() =
    let loggingConfig = {
        MinimumLevel = Information
        OutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        LogToConsole = true
        LogToFile = Some true
        FilePath = Some "quantum-operations.log"
    }
    
    let tracingConfig = defaultTracingConfig "MyQuantumApp"
    
    create (Some loggingConfig) (Some tracingConfig)

// Wrap quantum operations with observability
let submitJobWithObservability observability client submission =
    async {
        let sw = Stopwatch.StartNew()
        
        let tags = Map.ofList [
            ("operation", box "SubmitJob")
            ("backend", box (submission.Target.ToString()))
        ]
        
        try
            let! result = traceQuantumExecution observability "SubmitJob" tags (fun () ->
                client.SubmitJobAsync(submission) |> Async.RunSynchronously
            )
            
            sw.Stop()
            
            match result with
            | Ok response ->
                // Log success metrics
                logPerformanceMetrics observability {
                    OperationName = "SubmitJob"
                    DurationMs = float sw.ElapsedMilliseconds
                    Timestamp = DateTimeOffset.UtcNow
                    Context = Map.ofList [
                        ("jobId", box response.JobId)
                        ("status", box "success")
                    ]
                }
                
                return result
                
            | Error err ->
                // Log error with context
                let context = Map.ofList [
                    ("operation", box "SubmitJob")
                    ("durationMs", box sw.ElapsedMilliseconds)
                    ("error", box err.ToString())
                ]
                
                logErrorWithContext observability "Job submission failed" context None
                
                return result
        with
        | ex ->
            sw.Stop()
            
            let context = Map.ofList [
                ("operation", box "SubmitJob")
                ("durationMs", box sw.ElapsedMilliseconds)
            ]
            
            logErrorWithContext observability "Unexpected error during job submission" context (Some ex)
            
            return Error (QuantumError.TransientError("Job submission failed", 500))
    }

// Application entry point
[<EntryPoint>]
let main argv =
    let observability = initializeApp()
    
    // Your quantum operations here with full observability
    0
```

## Testing with Observability

```fsharp
module Tests =
    open Xunit
    
    [<Fact>]
    let ``test operation with logging`` () =
        // Create observability state for testing
        let state = create (Some defaultLoggingConfig) None
        
        // Your test code with logging
        let result = performOperation()
        
        // Logs will appear in test output
        Assert.True(result.IsOk)
```

## See Also

- TKT-49: Logging and Telemetry specification
- Serilog documentation: https://serilog.net/
- OpenTelemetry .NET: https://github.com/open-telemetry/opentelemetry-dotnet
