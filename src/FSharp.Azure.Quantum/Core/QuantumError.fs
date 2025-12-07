namespace FSharp.Azure.Quantum.Core

/// Errors that can occur in quantum computing operations
/// 
/// Design principles:
/// - High-level categories for easy pattern matching
/// - Rich context data for debugging and recovery
/// - Pragmatic 'Other' catch-all for edge cases
/// - NOT exceptions - these are expected business outcomes
[<RequireQualifiedAccess>]
type QuantumError =
    // ========================================================================
    // VALIDATION ERRORS - Input data validation failures
    // ========================================================================
    
    /// Input validation failed (empty data, dimension mismatch, out of range, etc.)
    /// 
    /// Examples:
    /// - Empty training dataset
    /// - Feature/target dimension mismatch
    /// - Negative shot count
    /// - Invalid hyperparameter values
    | ValidationError of field: string * reason: string
    
    // ========================================================================
    // NOT IMPLEMENTED - Feature/algorithm not yet available
    // ========================================================================
    
    /// Requested feature or algorithm is not yet implemented
    /// 
    /// Examples:
    /// - Unsupported gate on specific backend
    /// - Algorithm variant not implemented
    /// - Backend feature not available
    | NotImplemented of feature: string * hint: string option
    
    // ========================================================================
    // OPERATION ERRORS - Runtime operation failures
    // ========================================================================
    
    /// Quantum operation failed during execution
    /// 
    /// Examples:
    /// - Algorithm failed to converge
    /// - Training failed (ML models)
    /// - QUBO encoding failed
    /// - Circuit execution failed
    /// - Optimization did not converge
    | OperationError of operation: string * context: string
    
    // ========================================================================
    // BACKEND ERRORS - Backend/infrastructure issues
    // ========================================================================
    
    /// Backend execution or communication error
    /// 
    /// Examples:
    /// - Backend unavailable
    /// - Authentication failed
    /// - Timeout
    /// - Network error
    /// - Backend service error
    | BackendError of backend: string * reason: string
    
    // ========================================================================
    // I/O ERRORS - File operations and serialization
    // ========================================================================
    
    /// File I/O or serialization error
    /// 
    /// Examples:
    /// - File not found
    /// - Parse error (XYZ, FCIDump, etc.)
    /// - Model serialization failed
    /// - Cannot write output file
    | IOError of operation: string * path: string * reason: string
    
    // ========================================================================
    // CATCH-ALL - Unexpected cases
    // ========================================================================
    
    /// Other error not covered by specific categories
    /// Prefer adding specific cases over using Other
    | Other of message: string
    
    // ========================================================================
    // HELPER METHODS
    // ========================================================================
    
    /// Get human-readable error message
    member this.Message =
        match this with
        | ValidationError (field, reason) ->
            $"Validation failed for '{field}': {reason}"
        
        | NotImplemented (feature, None) ->
            $"'{feature}' is not yet implemented"
        
        | NotImplemented (feature, Some hint) ->
            $"'{feature}' is not yet implemented. {hint}"
        
        | OperationError (operation, context) ->
            $"Operation '{operation}' failed: {context}"
        
        | BackendError (backend, reason) ->
            $"Backend '{backend}' error: {reason}"
        
        | IOError (operation, path, reason) ->
            $"I/O error during '{operation}' on '{path}': {reason}"
        
        | Other msg -> msg
    
    /// Get error category name (useful for logging/metrics)
    member this.Category =
        match this with
        | ValidationError _ -> "Validation"
        | NotImplemented _ -> "NotImplemented"
        | OperationError _ -> "Operation"
        | BackendError _ -> "Backend"
        | IOError _ -> "IO"
        | Other _ -> "Other"

/// Type alias for Result with QuantumError
/// Use this instead of Result<'T, string> throughout the library
type QuantumResult<'T> = Result<'T, QuantumError>

/// Helper module for working with QuantumResult
[<RequireQualifiedAccess>]
module QuantumResult =
    
    /// Create a successful result
    let ok value : QuantumResult<'T> = Ok value
    
    /// Create an error result
    let error err : QuantumResult<'T> = Error err
    
    /// Create validation error
    let validationError field reason : QuantumResult<'T> = 
        Error (QuantumError.ValidationError (field, reason))
    
    /// Create not implemented error
    let notImplemented feature hint : QuantumResult<'T> = 
        Error (QuantumError.NotImplemented (feature, hint))
    
    /// Create operation error
    let operationError operation context : QuantumResult<'T> = 
        Error (QuantumError.OperationError (operation, context))
    
    /// Create backend error
    let backendError backend reason : QuantumResult<'T> = 
        Error (QuantumError.BackendError (backend, reason))
    
    /// Create I/O error
    let ioError operation path reason : QuantumResult<'T> = 
        Error (QuantumError.IOError (operation, path, reason))
    
    /// Map a function over the Ok value
    let map f result = Result.map f result
    
    /// Map a function over the Error value
    let mapError f result = Result.mapError f result
    
    /// Bind for Result (monadic composition)
    let bind f result = Result.bind f result
    
    /// Convert string-based Result to QuantumResult with Other error
    let ofStringResult (result: Result<'T, string>) : QuantumResult<'T> =
        result |> Result.mapError QuantumError.Other
    
    /// Combine multiple validation results
    /// Returns Ok if all succeed, otherwise aggregates errors
    let combineValidations (results: QuantumResult<unit> list) : QuantumResult<unit> =
        let errors = 
            results 
            |> List.choose (function Error e -> Some e | Ok _ -> None)
        
        match errors with
        | [] -> Ok ()
        | [single] -> Error single
        | multiple ->
            let messages = multiple |> List.map (fun e -> e.Message) |> String.concat "; "
            Error (QuantumError.ValidationError ("Multiple fields", messages))
    
    // ========================================================================
    // MIGRATION HELPERS - Temporary bridges during string->QuantumError migration
    // These will be removed once full migration is complete
    // ========================================================================
    
    /// Convert string error to QuantumError (wraps in Other)
    /// Temporary helper for gradual migration
    let ofString (context: string) (error: string) : QuantumError =
        QuantumError.Other $"{context}: {error}"
    
    /// Convert string-based Result to QuantumResult (wraps in Other with context)
    /// Temporary helper for gradual migration
    let ofStringResultWithContext (context: string) (result: Result<'T, string>) : QuantumResult<'T> =
        result |> Result.mapError (fun err -> QuantumError.Other $"{context}: {err}")
    
    /// Convert QuantumError to string (extracts Message)
    /// Temporary helper for backward compatibility
    let toString (error: QuantumError) : string =
        error.Message
    
    /// Convert QuantumResult to string-based Result
    /// Temporary helper for backward compatibility
    let toStringResult (result: QuantumResult<'T>) : Result<'T, string> =
        result |> Result.mapError toString

// ========================================================================
// COMPUTATION EXPRESSION BUILDER
// ========================================================================

/// Computation expression builder for QuantumResult
/// Enables clean, readable error handling without nested match clauses
/// 
/// Example usage:
///   quantumResult {
///       let! data = validateInput input
///       let! processed = processData data
///       let! result = executeQuantum processed backend
///       return result
///   }
[<AutoOpen>]
module QuantumResultBuilder =
    
    type QuantumResultBuilder() =
        
        /// Wraps a value in a successful QuantumResult
        member _.Return(value: 'T) : QuantumResult<'T> = 
            Ok value
        
        /// Wraps a value in a successful QuantumResult
        member _.ReturnFrom(result: QuantumResult<'T>) : QuantumResult<'T> = 
            result
        
        /// Binds a QuantumResult, short-circuiting on Error
        member _.Bind(result: QuantumResult<'T>, binder: 'T -> QuantumResult<'U>) : QuantumResult<'U> =
            Result.bind binder result
        
        /// Delays computation
        member _.Delay(f: unit -> QuantumResult<'T>) : unit -> QuantumResult<'T> = 
            f
        
        /// Runs delayed computation
        member _.Run(f: unit -> QuantumResult<'T>) : QuantumResult<'T> = 
            f()
        
        /// Combines two QuantumResults sequentially
        member _.Combine(result1: QuantumResult<unit>, result2: unit -> QuantumResult<'T>) : QuantumResult<'T> =
            Result.bind (fun () -> result2()) result1
        
        /// Zero value (unit result)
        member _.Zero() : QuantumResult<unit> = 
            Ok ()
        
        /// Try-with for exception handling
        member _.TryWith(body: unit -> QuantumResult<'T>, handler: exn -> QuantumResult<'T>) : QuantumResult<'T> =
            try body()
            with ex -> handler ex
        
        /// Try-finally for cleanup
        member _.TryFinally(body: unit -> QuantumResult<'T>, cleanup: unit -> unit) : QuantumResult<'T> =
            try body()
            finally cleanup()
        
        /// Using for IDisposable resources
        member this.Using(resource: 'T when 'T :> System.IDisposable, binder: 'T -> QuantumResult<'U>) : QuantumResult<'U> =
            this.TryFinally(
                (fun () -> binder resource),
                (fun () -> if not (isNull (box resource)) then resource.Dispose())
            )
        
        /// While loop support
        member this.While(guard: unit -> bool, body: unit -> QuantumResult<unit>) : QuantumResult<unit> =
            if not (guard()) then 
                this.Zero()
            else
                this.Bind(body(), fun () -> this.While(guard, body))
        
        /// For loop support
        member this.For(sequence: seq<'T>, body: 'T -> QuantumResult<unit>) : QuantumResult<unit> =
            this.Using(
                sequence.GetEnumerator(),
                fun enum -> this.While(enum.MoveNext, fun () -> body enum.Current)
            )
    
    /// Global instance of the QuantumResult computation expression builder
    let quantumResult = QuantumResultBuilder()
