namespace FSharp.Azure.Quantum

open System
open System.Diagnostics
open Serilog
open Serilog.Events
open OpenTelemetry
open OpenTelemetry.Trace

/// Logging and telemetry for Azure Quantum operations
module Observability =
    
    /// Log level configuration
    [<Struct>]
    type LogLevel =
        | Debug
        | Information
        | Warning
        | Error
    
    /// Logging configuration
    type LoggingConfig = {
        MinimumLevel: LogLevel
        OutputTemplate: string
        LogToConsole: bool
        LogToFile: bool option
        FilePath: string option
    }
    
    /// Tracing configuration
    type TracingConfig = {
        ServiceName: string
        ServiceVersion: string
        ExportToConsole: bool
    }
    
    /// Performance metrics data
    type PerformanceMetrics = {
        OperationName: string
        DurationMs: float
        Timestamp: DateTimeOffset
        Context: Map<string, obj>
    }
    
    /// Cost metrics data
    type CostMetrics = {
        JobId: string
        EstimatedCost: decimal option
        ActualCost: decimal option
        Backend: string
        Timestamp: DateTimeOffset
    }
    
    /// Observability state (encapsulates logger and tracer)
    type ObservabilityState = {
        Logger: ILogger
        ActivitySource: ActivitySource option
    }
    
    /// Convert LogLevel to Serilog LogEventLevel
    let private toSerilogLevel level =
        match level with
        | Debug -> LogEventLevel.Debug
        | Information -> LogEventLevel.Information
        | Warning -> LogEventLevel.Warning
        | Error -> LogEventLevel.Error
    
    /// Sensitive field names that should never be logged (security protection)
    let private sensitiveKeys = Set.ofList [
        "password"; "apikey"; "api_key"; "secret"; "token"; 
        "authorization"; "credential"; "connectionstring"; "connection_string";
        "email"; "ssn"; "creditcard"; "credit_card"; "accountnumber"; "account_number";
        "bearer"; "oauth"; "accesstoken"; "access_token"; "refreshtoken"; "refresh_token"
    ]
    
    /// Sanitize context map by redacting sensitive values
    let private sanitizeContext (context: Map<string, obj>) : Map<string, obj> =
        context
        |> Map.map (fun key value ->
            let keyLower = key.ToLowerInvariant().Replace("-", "").Replace("_", "")
            if sensitiveKeys |> Set.exists (fun sens -> keyLower.Contains(sens)) then
                box "***REDACTED***"
            else
                value
        )
    
    /// Default logging configuration
    let defaultLoggingConfig = {
        MinimumLevel = Information
        OutputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        LogToConsole = true
        LogToFile = None
        FilePath = None
    }
    
    /// Default tracing configuration
    let defaultTracingConfig serviceName = {
        ServiceName = serviceName
        ServiceVersion = "1.0.0"
        ExportToConsole = false
    }
    
    /// Initialize Serilog logger with configuration
    let initializeLogging (config: LoggingConfig) =
        let loggerConfig =
            LoggerConfiguration()
                .MinimumLevel.Is(toSerilogLevel config.MinimumLevel)
        
        let withConsole = 
            if config.LogToConsole then
                loggerConfig.WriteTo.Console(outputTemplate = config.OutputTemplate)
            else
                loggerConfig
        
        let withFile =
            match config.LogToFile, config.FilePath with
            | Some true, Some path ->
                withConsole.WriteTo.File(path, outputTemplate = config.OutputTemplate)
            | _ ->
                withConsole
        
        withFile.CreateLogger()
    
    /// Initialize OpenTelemetry tracing with configuration
    let initializeTracing (config: TracingConfig) =
        let activitySource = new ActivitySource(config.ServiceName, config.ServiceVersion)
        
        if config.ExportToConsole then
            // Note: TracerProvider setup would typically be done at application startup
            // This is a simplified version for the library
            Some activitySource
        else
            Some activitySource
    
    /// Create observability state with logging and tracing
    let create (loggingConfig: LoggingConfig option) (tracingConfig: TracingConfig option) =
        let logger = 
            loggingConfig
            |> Option.map initializeLogging
            |> Option.defaultWith (fun () -> initializeLogging defaultLoggingConfig)
        
        let activitySource =
            tracingConfig
            |> Option.bind initializeTracing
        
        { Logger = logger; ActivitySource = activitySource }
    
    /// Trace a quantum execution operation
    let traceQuantumExecution (state: ObservabilityState) operationName (tags: Map<string, obj>) (operation: unit -> 'T) =
        match state.ActivitySource with
        | Some source ->
            use activity : Activity = source.StartActivity(operationName, ActivityKind.Internal)
            
            if not (isNull activity) then
                // Add tags to the activity
                tags |> Map.iter (fun key value ->
                    match value with
                    | :? string as s -> activity.SetTag(key, s) |> ignore
                    | :? int as i -> activity.SetTag(key, i) |> ignore
                    | :? float as f -> activity.SetTag(key, f) |> ignore
                    | :? decimal as d -> activity.SetTag(key, d) |> ignore
                    | :? bool as b -> activity.SetTag(key, b) |> ignore
                    | _ -> activity.SetTag(key, value.ToString()) |> ignore
                )
            
            operation()
        | None ->
            operation()
    
    /// Log performance metrics (with automatic sensitive data redaction)
    let logPerformanceMetrics (state: ObservabilityState) (metrics: PerformanceMetrics) =
        let safeContext = sanitizeContext metrics.Context
        state.Logger.Information(
            "Performance: {OperationName} completed in {DurationMs}ms at {Timestamp} with context {@Context}",
            metrics.OperationName,
            metrics.DurationMs,
            metrics.Timestamp,
            safeContext  // ✅ SAFE: Redacted context
        )
    
    /// Log cost metrics
    let logCostMetrics (state: ObservabilityState) (metrics: CostMetrics) =
        match metrics.EstimatedCost, metrics.ActualCost with
        | Some estimated, Some actual ->
            state.Logger.Information(
                "Cost: Job {JobId} on backend {Backend} - Estimated: ${EstimatedCost:F2}, Actual: ${ActualCost:F2} at {Timestamp}",
                metrics.JobId,
                metrics.Backend,
                estimated,
                actual,
                metrics.Timestamp
            )
        | Some estimated, None ->
            state.Logger.Information(
                "Cost: Job {JobId} on backend {Backend} - Estimated: ${EstimatedCost:F2} at {Timestamp}",
                metrics.JobId,
                metrics.Backend,
                estimated,
                metrics.Timestamp
            )
        | None, Some actual ->
            state.Logger.Information(
                "Cost: Job {JobId} on backend {Backend} - Actual: ${ActualCost:F2} at {Timestamp}",
                metrics.JobId,
                metrics.Backend,
                actual,
                metrics.Timestamp
            )
        | None, None ->
            state.Logger.Information(
                "Cost: Job {JobId} on backend {Backend} - No cost information available at {Timestamp}",
                metrics.JobId,
                metrics.Backend,
                metrics.Timestamp
            )
    
    /// Log error with structured context
    let logErrorWithContext (state: ObservabilityState) message (context: Map<string, obj>) (exn: Exception option) =
        match exn with
        | Some ex ->
            state.Logger.Error(
                ex,
                "Error: {Message} with context {@Context}",
                message,
                context
            )
        | None ->
            state.Logger.Error(
                "Error: {Message} with context {@Context}",
                message,
                context
            )
