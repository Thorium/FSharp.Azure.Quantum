namespace FSharp.Azure.Quantum.Topological

/// Computation expression builder for topological quantum programs
/// 
/// This builder provides idiomatic F# syntax for composing topological
/// quantum operations. Programs written with this builder are backend-agnostic
/// and work with ANY IQuantumBackend (simulator OR hardware).
/// 
/// Key features:
/// - Natural F# syntax with let! and do!
/// - Automatic state threading
/// - Backend abstraction
/// - Composable operations
/// 
/// Example:
/// ```fsharp
/// let program = topological backend {
///     let! qubit = initialize Ising 4
///     do! braid 0 qubit
///     do! braid 2 qubit
///     let! outcome = measure 0 qubit
///     return outcome
/// }
/// ```
[<RequireQualifiedAccess>]
module TopologicalBuilder =
    
    open System.Threading.Tasks
    open FSharp.Azure.Quantum.Core
    open FSharp.Azure.Quantum.Core.BackendAbstraction
    
    // ========================================================================
    // BUILDER CONTEXT
    // ========================================================================
    
    /// Record of a topological operation for visualization
    type OperationRecord =
        | Init of AnyonSpecies.AnyonType * int
        | Braid of int
        | Measure of int * AnyonSpecies.Particle * float
        | Comment of string

    /// Context that flows through the computation
    /// Contains the backend and current quantum state
    type BuilderContext = {
        /// The backend being used for execution
        Backend: IQuantumBackend
        
        /// Current quantum state
        CurrentState: QuantumState
        
        /// Accumulated measurement results
        MeasurementResults: (AnyonSpecies.Particle * float) list
        
        /// Execution log for debugging
        ExecutionLog: string list
        
        /// Structured history for visualization
        History: OperationRecord list
    }
    
    /// Create initial context with empty state
    let createContext (backend: IQuantumBackend) = 
        // Create a minimal initial state (vacuum)
        // Note: Real initialization happens via 'initialize' operation
        let vacuumTree = FusionTree.leaf AnyonSpecies.Particle.Vacuum
        let vacuumState = FusionTree.create vacuumTree AnyonSpecies.AnyonType.Ising
        let initialState = QuantumState.FusionSuperposition (TopologicalOperations.toInterface (TopologicalOperations.pureState vacuumState))
        
        {
            Backend = backend
            CurrentState = initialState
            MeasurementResults = []
            ExecutionLog = []
            History = []
        }
    
    /// Update state in context
    let updateState context newState = 
        { context with CurrentState = newState }
    
    /// Add measurement result to context
    let addMeasurement context (particle, probability) =
        { context with 
            MeasurementResults = (particle, probability) :: context.MeasurementResults }
    
    /// Add log entry to context
    let log context message =
        { context with ExecutionLog = message :: context.ExecutionLog }

    /// Add operation to history
    let addHistory context op =
        { context with History = op :: context.History }
    
    // ========================================================================
    // CORE OPERATIONS (Backend-Agnostic)
    // ========================================================================
    
    /// Initialize anyons
    let initialize (anyonType: AnyonSpecies.AnyonType) (count: int) (context: BuilderContext) : Task<Result<BuilderContext, QuantumError>> =
        task {
            // Note: IQuantumBackend.InitializeState takes logical qubits/size.
            // We map the requested count to the backend's initialization.
            // The anyonType is primarily determined by the backend configuration,
            // but we keep the parameter for DSL compatibility.
            // REMINDER: InitializeState returns a Result, not a Task<Result>. Use 'let', not 'let!'.
            let stateResult = context.Backend.InitializeState count
            return
                match stateResult with
                | Ok state ->
                    let ctx = updateState context state
                    let ctx' = log ctx $"Initialized {count} {anyonType} anyons"
                    let ctx'' = addHistory ctx' (Init (anyonType, count))
                    Ok ctx''
                | Error err ->
                    Error err
        }
    
    /// Braid anyons at given index
    let braid (leftIndex: int) (context: BuilderContext) : Task<Result<BuilderContext, QuantumError>> =
        task {
            // REMINDER: ApplyOperation returns a Result, not a Task<Result>. Use 'let', not 'let!'.
            let newStateResult = context.Backend.ApplyOperation (QuantumOperation.Braid leftIndex) context.CurrentState
            return
                match newStateResult with
                | Ok newState ->
                    let ctx = updateState context newState
                    let ctx' = log ctx $"Braided anyons at index {leftIndex}"
                    let ctx'' = addHistory ctx' (Braid leftIndex)
                    Ok ctx''
                | Error err ->
                    Error err
        }
    
    /// Measure fusion at given index
    let measure (leftIndex: int) (context: BuilderContext) : Task<Result<(AnyonSpecies.Particle * BuilderContext), QuantumError>> =
        task {
            // IQuantumBackend doesn't support returning measurement outcome from ApplyOperation.
            // We implement measurement logic client-side by inspecting the state.
            let measureResult =
                match context.CurrentState with
                | QuantumState.FusionSuperposition fs ->
                    match TopologicalOperations.fromInterface fs with
                    | Some superposition ->
                         // Check if it's a pure state (single term) which measureFusion supports
                         match superposition.Terms with
                         | [(_, singleState)] ->
                             TopologicalOperations.measureFusion leftIndex singleState
                             |> Result.mapError (fun err -> QuantumError.OperationError ("TopologicalBuilder", err.Message))
                         | multipleTerms ->
                             // Multi-term superposition measurement (Born rule):
                             // For each term (amplitude_i, state_i):
                             //   1. Call measureFusion to get possible outcomes with per-term probabilities
                             //   2. Weight each outcome probability by |amplitude_i|²
                             //   3. Aggregate outcomes across terms, summing probabilities for matching particles
                             // Then sample one outcome based on aggregated probabilities.
                             let termResults =
                                 multipleTerms
                                 |> List.map (fun (amplitude, termState) ->
                                     let weight = amplitude.Magnitude * amplitude.Magnitude
                                     TopologicalOperations.measureFusion leftIndex termState
                                     |> Result.map (fun outcomes ->
                                         outcomes |> List.map (fun (prob, opResult) -> (prob * weight, opResult))
                                     )
                                     |> Result.mapError (fun err -> QuantumError.OperationError ("TopologicalBuilder", err.Message))
                                 )
                             
                             // Check for errors
                             match termResults |> List.tryPick (function Error e -> Some e | Ok _ -> None) with
                             | Some err -> Error err
                             | None ->
                                 // Flatten all weighted outcomes
                                 let allOutcomes =
                                     termResults
                                     |> List.collect (function Ok outcomes -> outcomes | Error _ -> [])
                                 
                                 // Aggregate by classical outcome particle type
                                 let aggregated =
                                     allOutcomes
                                     |> List.groupBy (fun (_, opResult) -> opResult.ClassicalOutcome)
                                     |> List.choose (fun (maybeParticle, group) ->
                                         match maybeParticle with
                                         | Some _ ->
                                             let totalProb = group |> List.sumBy fst
                                             // Use the first operationResult as representative (collapsed state)
                                             let (_, representativeResult) = group |> List.head
                                             Some (totalProb, representativeResult)
                                         | None -> None
                                     )
                                 
                                 if aggregated.IsEmpty then
                                     Error (QuantumError.OperationError ("TopologicalBuilder", "Multi-term measurement produced no outcomes"))
                                 else
                                     // Normalize probabilities
                                     let totalProb = aggregated |> List.sumBy fst
                                     let normalized =
                                         if totalProb > 0.0 then
                                             aggregated |> List.map (fun (p, r) -> (p / totalProb, r))
                                         else aggregated
                                     Ok normalized
                    | None ->
                        Error (QuantumError.ValidationError ("state", "Could not unwrap FusionSuperposition"))
                | _ ->
                    Error (QuantumError.ValidationError ("state", "State is not a FusionSuperposition"))

            return
                match measureResult with
                | Ok outcomes ->
                    // Sample one outcome (mimic single-shot behavior)
                    match List.tryHead outcomes with
                    | Some (prob, opResult) ->
                         match opResult.ClassicalOutcome with
                         | Some outcome ->
                             // Correct: Re-wrap the raw FusionTree.State (opResult.State) into a Superposition
                             let collapsedSuperposition = TopologicalOperations.pureState opResult.State
                             let collapsedState = 
                                 QuantumState.FusionSuperposition (TopologicalOperations.toInterface collapsedSuperposition)
                             
                             let ctx = updateState context collapsedState
                             let ctx' = addMeasurement ctx (outcome, prob)
                             let ctx'' = log ctx' $"Measured fusion at index {leftIndex}: {outcome} (p={prob:F4})"
                             let ctx''' = addHistory ctx'' (Measure (leftIndex, outcome, prob))
                             Ok (outcome, ctx''')
                         | None ->
                             Error (QuantumError.OperationError ("TopologicalBuilder", "Measurement produced no classical outcome"))
                    | None ->
                        Error (QuantumError.OperationError ("TopologicalBuilder", "Measurement returned no outcomes"))
                | Error err ->
                    Error err
        }
    
    /// Apply a sequence of braiding operations
    let braidSequence (indices: int list) (context: BuilderContext) : Task<Result<BuilderContext, QuantumError>> =
        task {
            // Functional fold - no mutable state
            let! finalResult =
                indices
                |> List.fold (fun ctxTask index ->
                    task {
                        let! ctxResult = ctxTask
                        match ctxResult with
                        | Error err -> return Error err  // Short-circuit on error
                        | Ok ctx -> return! braid index ctx
                    }
                ) (Task.FromResult (Ok context))
            
            return finalResult
        }
    
    /// Get current state (for inspection/debugging)
    let getState (context: BuilderContext) =
        task { return Ok (context.CurrentState, context) }
    
    /// Get measurement results
    let getResults (context: BuilderContext) =
        task { return Ok (List.rev context.MeasurementResults, context) }
    
    /// Get execution log
    let getLog (context: BuilderContext) =
        task { return Ok (List.rev context.ExecutionLog, context) }
        
    /// Get full context (for visualization/debugging)
    let getContext (context: BuilderContext) =
        task { return Ok (context, context) }
    
    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ========================================================================
    
    /// Computation expression builder for topological quantum programs
    type TopologicalProgramBuilder(backend: IQuantumBackend) =
        
        /// Initial context
        let initialContext = createContext backend
        
        /// Bind operation for Result-wrapped values
        member _.Bind(operation: BuilderContext -> Task<Result<'a * BuilderContext, QuantumError>>, continuation: 'a -> BuilderContext -> Task<Result<'b * BuilderContext, QuantumError>>) =
            fun (context: BuilderContext) -> task {
                let! opResult = operation context
                return!
                    match opResult with
                    | Ok (result, ctx) -> continuation result ctx
                    | Error err -> Task.FromResult(Error err)
            }
        
        /// Bind for Result-wrapped context updates (for do!)
        member _.Bind(operation: BuilderContext -> Task<Result<BuilderContext, QuantumError>>, continuation: unit -> BuilderContext -> Task<Result<'a * BuilderContext, QuantumError>>) =
            fun (context: BuilderContext) -> task {
                let! opResult = operation context
                return!
                    match opResult with
                    | Ok ctx -> continuation () ctx
                    | Error err -> Task.FromResult(Error err)
            }
        
        /// Yield (return a value)
        member _.Yield(value: 'a) =
            fun (context: BuilderContext) -> task {
                return Ok (value, context)
            }
        
        /// Yield (return unit)
        member _.Yield(x: unit) =
            fun (context: BuilderContext) -> task {
                return Ok ((), context)
            }
            
        /// Return a value
        member _.Return(value: 'a) =
            fun (context: BuilderContext) -> task {
                return Ok (value, context)
            }
        
        /// Return from a computation
        member _.ReturnFrom(operation: BuilderContext -> Task<Result<'a * BuilderContext, QuantumError>>) =
            operation
        
        /// Zero (for computations without return)
        member _.Zero() =
            fun (context: BuilderContext) -> task {
                return Ok ((), context)
            }
        
        /// Delay evaluation
        member _.Delay(f: unit -> (BuilderContext -> Task<Result<'a * BuilderContext, QuantumError>>)) =
            fun (context: BuilderContext) -> task {
                return! f () context
            }
        
        /// Run the computation with initial context
        member _.Run(operation: BuilderContext -> Task<Result<'a * BuilderContext, QuantumError>>) = 
            operation
        
        /// Combine two computations
        member _.Combine(operation1: BuilderContext -> Task<Result<unit * BuilderContext, QuantumError>>, operation2: BuilderContext -> Task<Result<'a * BuilderContext, QuantumError>>) =
            fun (context: BuilderContext) -> task {
                let! op1Result = operation1 context
                return!
                    match op1Result with
                    | Ok (_, ctx) -> operation2 ctx
                    | Error err -> Task.FromResult(Error err)
            }
        
        /// For loop - functional fold pattern
        member _.For(sequence: seq<'T>, body: 'T -> BuilderContext -> Task<Result<unit * BuilderContext, QuantumError>>) =
            fun (context: BuilderContext) -> task {
                let! finalResult =
                    sequence
                    |> Seq.fold (fun ctxTask item ->
                        task {
                            let! ctxResult = ctxTask
                            match ctxResult with
                            | Error err -> return Error err  // Short-circuit
                            | Ok ctx ->
                                let! bodyResult = body item ctx
                                return
                                    match bodyResult with
                                    | Ok (_, newCtx) -> Ok newCtx
                                    | Error err -> Error err
                        }
                    ) (Task.FromResult (Ok context))
                
                return
                    match finalResult with
                    | Ok ctx -> Ok ((), ctx)
                    | Error err -> Error err
            }
        
        /// While loop - recursive pattern (no mutable state)
        member _.While(guard: unit -> bool, body: BuilderContext -> Task<Result<unit * BuilderContext, QuantumError>>) =
            fun (context: BuilderContext) -> 
                let rec loop ctx = task {
                    if guard () then
                        let! bodyResult = body ctx
                        match bodyResult with
                        | Ok (_, newCtx) -> return! loop newCtx
                        | Error err -> return Error err
                    else
                        return Ok ((), ctx)
                }
                loop context
        
        /// Try-finally
        member _.TryFinally(body: BuilderContext -> Task<'a * BuilderContext>, finalizer: unit -> unit) =
            fun (context: BuilderContext) -> task {
                try
                    return! body context
                finally
                    finalizer ()
            }
        
        /// Try-with
        member _.TryWith(body: BuilderContext -> Task<'a * BuilderContext>, handler: exn -> BuilderContext -> Task<'a * BuilderContext>) =
            fun (context: BuilderContext) -> task {
                try
                    return! body context
                with ex ->
                    return! handler ex context
            }
        
        /// Using (for IDisposable)
        member _.Using(resource: 'T when 'T :> System.IDisposable, body: 'T -> BuilderContext -> Task<'a * BuilderContext>) =
            fun (context: BuilderContext) -> task {
                try
                    return! body resource context
                finally
                    if not (isNull (box resource)) then
                        resource.Dispose()
            }
    
    // ========================================================================
    // CONVENIENCE FUNCTIONS
    // ========================================================================
    
    /// Create a topological program builder for a given backend
    let create (backend: IQuantumBackend) =
        TopologicalProgramBuilder(backend)
    
    /// Execute a program and return result with execution context
    let executeWithContext (backend: IQuantumBackend) (program: BuilderContext -> Task<Result<'a * BuilderContext, QuantumError>>) = task {
        let ctx = createContext backend
        let! opResult = program ctx
        return
            match opResult with
            | Ok (result, finalContext) -> Ok (result, finalContext)
            | Error err -> Error err
    }
    
    /// Execute a program and return just the result
    let execute (backend: IQuantumBackend) (program: BuilderContext -> Task<Result<'a * BuilderContext, QuantumError>>) = task {
        let! result = executeWithContext backend program
        return
            match result with
            | Ok (res, _) -> Ok res
            | Error err -> Error err
    }
    
    // ========================================================================
    // CUSTOM OPERATIONS (OPTIONAL SYNTAX SUGAR) - REMOVED FOR STABILITY
    // ========================================================================
    // Custom operations removed to resolve FS0708 and FS0001 errors.
    // The builder now strictly uses standard 'do!' / 'let!' syntax.

// ========================================================================
// GLOBAL BUILDER INSTANCE
// ========================================================================

/// Global namespace for topological computation expressions
[<AutoOpen>]
module TopologicalBuilderExtensions =
    
    open FSharp.Azure.Quantum.Core.BackendAbstraction

    /// Create a topological program for a given backend
    /// 
    /// Example:
    /// ```fsharp
    /// let program = topological backend {
    ///     let! ctx = TopologicalBuilder.initialize Ising 4
    ///     let! ctx = TopologicalBuilder.braid 0 ctx
    ///     let! (outcome, ctx) = TopologicalBuilder.measure 0 ctx
    ///     return outcome
    /// }
    /// ```
    let topological (backend: IQuantumBackend) =
        TopologicalBuilder.TopologicalProgramBuilder(backend)
