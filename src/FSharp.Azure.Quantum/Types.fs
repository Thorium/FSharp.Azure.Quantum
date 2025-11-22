namespace FSharp.Azure.Quantum.Core

open System

module Types =
    
    /// Azure Quantum job status
    type JobStatus =
        /// Job is waiting in queue
        | Waiting
        
        /// Job is currently executing on quantum hardware
        | Executing
        
        /// Job completed successfully
        | Succeeded
        
        /// Job failed with error
        | Failed of errorCode: string * errorMessage: string
        
        /// Job was cancelled by user
        | Cancelled
        
        /// Parse job status from Azure API response
        static member Parse(status: string, errorCode: string option, errorMessage: string option) : JobStatus =
            match status.ToLowerInvariant() with
            | "waiting" -> Waiting
            | "executing" -> Executing
            | "succeeded" -> Succeeded
            | "failed" -> 
                Failed (
                    errorCode |> Option.defaultValue "Unknown",
                    errorMessage |> Option.defaultValue "No error message"
                )
            | "cancelled" -> Cancelled
            | _ -> Failed ("UnknownStatus", sprintf "Unknown job status: %s" status)
    
    /// Azure Quantum backend information
    type Backend = {
        /// Backend ID (e.g., "ionq.simulator", "ionq.qpu.aria-1")
        Id: string
        
        /// Provider name (e.g., "IonQ", "Microsoft")
        Provider: string
        
        /// Backend name for display
        Name: string
        
        /// Backend status (e.g., "Available", "Unavailable")
        Status: string
    }
    
    /// Circuit format specification
    type CircuitFormat =
        | QIR_V1
        | IonQ_V1
        | Qiskit_V1
        | Custom of string
        
        member this.ToFormatString() =
            match this with
            | QIR_V1 -> "qir.v1"
            | IonQ_V1 -> "ionq.circuit.v1"
            | Qiskit_V1 -> "qiskit.circuit.v1"
            | Custom s -> s
    
    /// Job submission request
    type JobSubmission = {
        /// Unique job ID (client-generated GUID)
        JobId: string
        
        /// Target backend (e.g., "ionq.simulator")
        Target: string
        
        /// Job name (for display)
        Name: string option
        
        /// Job input data (quantum circuit)
        InputData: obj
        
        /// Input data format
        InputDataFormat: CircuitFormat
        
        /// Input parameters (shots, etc.)
        InputParams: Map<string, obj>
        
        /// Metadata tags
        Tags: Map<string, string>
    }
    
    /// Quantum job information
    type QuantumJob = {
        /// Job ID
        JobId: string
        
        /// Job status
        Status: JobStatus
        
        /// Target backend
        Target: string
        
        /// Creation time
        CreationTime: DateTimeOffset
        
        /// Begin execution time
        BeginExecutionTime: DateTimeOffset option
        
        /// End execution time
        EndExecutionTime: DateTimeOffset option
        
        /// Cancellation time
        CancellationTime: DateTimeOffset option
    }
    
    /// Job result
    type JobResult = {
        /// Job ID
        JobId: string
        
        /// Job status
        Status: JobStatus
        
        /// Output data
        OutputData: obj
        
        /// Output data format
        OutputDataFormat: string
        
        /// Execution time
        ExecutionTime: TimeSpan option
    }
    
    /// Quantum error types
    type QuantumError =
        /// Invalid Azure credentials
        | InvalidCredentials
        
        /// Rate limited by Azure
        | RateLimited of retryAfter: TimeSpan
        
        /// Service temporarily unavailable
        | ServiceUnavailable of retryAfter: TimeSpan option
        
        /// Network timeout
        | NetworkTimeout of attemptNumber: int
        
        /// Backend not found
        | BackendNotFound of backendId: string
        
        /// Invalid circuit
        | InvalidCircuit of errors: string list
        
        /// Quota exceeded
        | QuotaExceeded of quotaType: string
        
        /// Hardware fault
        | HardwareFault of message: string
        
        /// Job polling timeout
        | Timeout of message: string
        
        /// Unknown error
        | UnknownError of statusCode: int * message: string
