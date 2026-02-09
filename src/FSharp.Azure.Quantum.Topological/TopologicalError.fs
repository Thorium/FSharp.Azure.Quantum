namespace FSharp.Azure.Quantum.Topological

/// Errors that can occur in topological quantum computing operations
/// 
/// Design principles:
/// - High-level categories for easy pattern matching
/// - Rich context data (named fields) for debugging and recovery
/// - Pragmatic 'Other' catch-all for edge cases
/// - NOT exceptions - these are expected business outcomes
[<RequireQualifiedAccess>]
type TopologicalError =
    // ========================================================================
    // VALIDATION ERRORS - Input data validation failures
    // ========================================================================
    
    /// Input validation failed (out of bounds, invalid particle, dimension mismatch, etc.)
    /// 
    /// Examples:
    /// - Anyon index out of bounds
    /// - Invalid particle type for theory
    /// - Strand count mismatch
    /// - Invalid braid generator
    | ValidationError of field: string * reason: string
    
    // ========================================================================
    // NOT IMPLEMENTED - Feature/theory not yet available
    // ========================================================================
    
    /// Requested feature or theory is not yet implemented
    /// 
    /// Examples:
    /// - Unsupported anyon theory (e.g., SU(2)_3)
    /// - Algorithm variant not implemented
    /// - Backend feature not available
    | NotImplemented of feature: string * hint: string option
    
    // ========================================================================
    // LOGIC ERRORS - Quantum theory violations
    // ========================================================================
    
    /// Operation violates quantum theory rules
    /// 
    /// Examples:
    /// - Invalid fusion (σ×σ→τ in Ising)
    /// - Inconsistent fusion tree
    /// - Violates braiding relations
    /// - Invalid F-move or R-move
    | LogicError of operation: string * reason: string
    
    // ========================================================================
    // BACKEND ERRORS - Backend/infrastructure issues
    // ========================================================================
    
    /// Backend execution or capacity error
    /// 
    /// Examples:
    /// - Backend unavailable
    /// - Exceeds max anyon count
    /// - Unsupported anyon theory
    /// - Backend service error
    | BackendError of backend: string * reason: string
    
    // ========================================================================
    // COMPUTATION ERRORS - Numerical/algorithmic failures
    // ========================================================================
    
    /// Computation failed during execution
    /// 
    /// Examples:
    /// - Matrix inversion singular
    /// - R-matrix computation failed
    /// - Solovay-Kitaev did not converge
    /// - Numerical instability
    | ComputationError of operation: string * context: string
    
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
        
        | LogicError (operation, reason) ->
            $"Logic error in '{operation}': {reason}"
        
        | BackendError (backend, reason) ->
            $"Backend '{backend}' error: {reason}"
        
        | ComputationError (operation, context) ->
            $"Computation '{operation}' failed: {context}"
        
        | Other msg -> msg
    
    /// Get error category name (useful for logging/metrics)
    member this.Category =
        match this with
        | ValidationError _ -> "Validation"
        | NotImplemented _ -> "NotImplemented"
        | LogicError _ -> "Logic"
        | BackendError _ -> "Backend"
        | ComputationError _ -> "Computation"
        | Other _ -> "Other"
    
    /// Is this a user-fixable error (vs internal bug)?
    member this.IsUserError =
        match this with
        | ValidationError _ -> true     // User can fix input
        | NotImplemented _ -> false     // Needs code update
        | LogicError _ -> true          // User requested impossible operation
        | BackendError _ -> false       // Infrastructure issue
        | ComputationError _ -> false   // Internal failure
        | Other _ -> false

/// Type alias for Result with TopologicalError
/// Use this instead of Result<'T, string> throughout the library
type TopologicalResult<'T> = Result<'T, TopologicalError>

/// Helper module for working with TopologicalResult
[<RequireQualifiedAccess>]
module TopologicalResult =
    
    /// Create a successful result
    let ok value : TopologicalResult<'T> = Ok value
    
    /// Create an error result
    let error err : TopologicalResult<'T> = Error err
    
    /// Create validation error
    let validationError field reason : TopologicalResult<'T> = 
        Error (TopologicalError.ValidationError (field, reason))
    
    /// Create not implemented error
    let notImplemented feature hint : TopologicalResult<'T> = 
        Error (TopologicalError.NotImplemented (feature, hint))
    
    /// Create logic error
    let logicError operation reason : TopologicalResult<'T> = 
        Error (TopologicalError.LogicError (operation, reason))
    
    /// Create backend error
    let backendError backend reason : TopologicalResult<'T> = 
        Error (TopologicalError.BackendError (backend, reason))
    
    /// Create computation error
    let computationError operation context : TopologicalResult<'T> = 
        Error (TopologicalError.ComputationError (operation, context))
    
    /// Map a function over the Ok value
    let map f result = Result.map f result
    
    /// Map a function over the Error value
    let mapError f result = Result.mapError f result
    
    /// Bind for Result (monadic composition)
    let bind f result = Result.bind f result
    
    /// Convert string-based Result to TopologicalResult with Other error
    let ofStringResult (result: Result<'T, string>) : TopologicalResult<'T> =
        result |> Result.mapError TopologicalError.Other
    
    /// Combine multiple validation results
    /// Returns Ok if all succeed, otherwise aggregates errors
    let combineValidations (results: TopologicalResult<unit> list) : TopologicalResult<unit> =
        let errors = 
            results 
            |> List.choose (function Error e -> Some e | Ok _ -> None)
        
        match errors with
        | [] -> Ok ()
        | [single] -> Error single
        | multiple ->
            let messages = multiple |> List.map (fun e -> e.Message) |> String.concat "; "
            Error (TopologicalError.ValidationError ("Multiple fields", messages))
    
    // ========================================================================
    // MIGRATION HELPERS - Temporary bridges during string->TopologicalError migration
    // These will be removed once full migration is complete
    // ========================================================================
    
    /// Convert string error to TopologicalError (wraps in Other)
    /// Temporary helper for gradual migration
    let ofString (context: string) (error: string) : TopologicalError =
        TopologicalError.Other $"{context}: {error}"
    
    /// Convert string-based Result to TopologicalResult (wraps in Other with context)
    /// Temporary helper for gradual migration
    let ofStringResultWithContext (context: string) (result: Result<'T, string>) : TopologicalResult<'T> =
        result |> Result.mapError (fun err -> TopologicalError.Other $"{context}: {err}")
    
    /// Convert TopologicalError to string (extracts Message)
    /// Temporary helper for backward compatibility
    let toString (error: TopologicalError) : string =
        error.Message
    
    /// Convert TopologicalResult to string-based Result
    /// Temporary helper for backward compatibility
    let toStringResult (result: TopologicalResult<'T>) : Result<'T, string> =
        result |> Result.mapError toString

// ========================================================================
// COMPUTATION EXPRESSION BUILDER
// ========================================================================

/// Computation expression builder for TopologicalResult
/// Enables clean, readable error handling without nested match clauses
/// 
/// Example usage:
///   topologicalResult {
///       let! braiding = validateBraid generators strandCount
///       let! fusion = computeFusion particles
///       let! result = applyBraiding braiding fusion
///       return result
///   }
[<AutoOpen>]
module TopologicalResultBuilder =
    
    type TopologicalResultBuilder() =
        
        /// Wraps a value in a successful TopologicalResult
        member _.Return(value: 'T) : TopologicalResult<'T> = 
            Ok value
        
        /// Wraps a value in a successful TopologicalResult
        member _.ReturnFrom(result: TopologicalResult<'T>) : TopologicalResult<'T> = 
            result
        
        /// Binds a TopologicalResult, short-circuiting on Error
        member _.Bind(result: TopologicalResult<'T>, binder: 'T -> TopologicalResult<'U>) : TopologicalResult<'U> =
            Result.bind binder result
        
        /// Delays computation
        member _.Delay(f: unit -> TopologicalResult<'T>) : unit -> TopologicalResult<'T> = 
            f
        
        /// Runs delayed computation
        member _.Run(f: unit -> TopologicalResult<'T>) : TopologicalResult<'T> = 
            f()
        
        /// Combines two TopologicalResults sequentially
        member _.Combine(result1: TopologicalResult<unit>, result2: unit -> TopologicalResult<'T>) : TopologicalResult<'T> =
            Result.bind (fun () -> result2()) result1
        
        /// Zero value (unit result)
        member _.Zero() : TopologicalResult<unit> = 
            Ok ()
        
        /// Try-with for exception handling
        member _.TryWith(body: unit -> TopologicalResult<'T>, handler: exn -> TopologicalResult<'T>) : TopologicalResult<'T> =
            try body()
            with ex -> handler ex
        
        /// Try-finally for cleanup
        member _.TryFinally(body: unit -> TopologicalResult<'T>, cleanup: unit -> unit) : TopologicalResult<'T> =
            try body()
            finally cleanup()
        
        /// Using for IDisposable resources
        member this.Using(resource: 'T when 'T :> System.IDisposable, binder: 'T -> TopologicalResult<'U>) : TopologicalResult<'U> =
            this.TryFinally(
                (fun () -> binder resource),
                (fun () -> if not (isNull (box resource)) then resource.Dispose())
            )
        
        /// While loop support
        member this.While(guard: unit -> bool, body: unit -> TopologicalResult<unit>) : TopologicalResult<unit> =
            if not (guard()) then 
                this.Zero()
            else
                this.Bind(body(), fun () -> this.While(guard, body))
        
        /// For loop support
        member this.For(sequence: seq<'T>, body: 'T -> TopologicalResult<unit>) : TopologicalResult<unit> =
            this.Using(
                sequence.GetEnumerator(),
                fun enum -> this.While(enum.MoveNext, fun () -> body enum.Current)
            )
    
    /// Global instance of the TopologicalResult computation expression builder
    let topologicalResult = TopologicalResultBuilder()
