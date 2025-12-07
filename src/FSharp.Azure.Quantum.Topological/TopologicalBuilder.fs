namespace FSharp.Azure.Quantum.Topological

/// Computation expression builder for topological quantum programs
/// 
/// This builder provides idiomatic F# syntax for composing topological
/// quantum operations. Programs written with this builder are backend-agnostic
/// and work with ANY ITopologicalBackend (simulator OR hardware).
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
    
    // ========================================================================
    // BUILDER CONTEXT
    // ========================================================================
    
    /// Context that flows through the computation
    /// Contains the backend and current quantum state
    type BuilderContext = {
        /// The backend being used for execution
        Backend: TopologicalBackend.ITopologicalBackend
        
        /// Current quantum state (fusion tree superposition)
        CurrentState: TopologicalOperations.Superposition
        
        /// Accumulated measurement results
        MeasurementResults: (AnyonSpecies.Particle * float) list
        
        /// Execution log for debugging
        ExecutionLog: string list
    }
    
    /// Create initial context with empty state
    let createContext backend = 
        // Create a minimal initial state (will be replaced by Initialize)
        let vacuumTree = FusionTree.leaf AnyonSpecies.Particle.Vacuum
        let vacuumState = FusionTree.create vacuumTree AnyonSpecies.AnyonType.Ising
        let initialState = TopologicalOperations.pureState vacuumState
        
        {
            Backend = backend
            CurrentState = initialState
            MeasurementResults = []
            ExecutionLog = []
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
    
    // ========================================================================
    // CORE OPERATIONS (Backend-Agnostic)
    // ========================================================================
    
    /// Initialize anyons
    let initialize (anyonType: AnyonSpecies.AnyonType) (count: int) (context: BuilderContext) =
        task {
            let! stateResult = context.Backend.Initialize anyonType count
            return
                match stateResult with
                | Ok state ->
                    let ctx = updateState context state
                    let ctx' = log ctx $"Initialized {count} {anyonType} anyons"
                    Ok ctx'
                | Error err ->
                    Error err
        }
    
    /// Braid anyons at given index
    let braid (leftIndex: int) (context: BuilderContext) =
        task {
            let! newStateResult = context.Backend.Braid leftIndex context.CurrentState
            return
                match newStateResult with
                | Ok newState ->
                    let ctx = updateState context newState
                    let ctx' = log ctx $"Braided anyons at index {leftIndex}"
                    Ok ctx'
                | Error err ->
                    Error err
        }
    
    /// Measure fusion at given index
    let measure (leftIndex: int) (context: BuilderContext) =
        task {
            let! measureResult = context.Backend.MeasureFusion leftIndex context.CurrentState
            
            return
                match measureResult with
                | Ok (outcome, collapsed, probability) ->
                    let ctx = updateState context collapsed
                    let ctx' = addMeasurement ctx (outcome, probability)
                    let ctx'' = log ctx' $"Measured fusion at index {leftIndex}: {outcome} (p={probability:F4})"
                    Ok (outcome, ctx'')
                | Error err ->
                    Error err
        }
    
    /// Apply a sequence of braiding operations
    let braidSequence (indices: int list) (context: BuilderContext) =
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
        task { return (context.CurrentState, context) }
    
    /// Get measurement results
    let getResults (context: BuilderContext) =
        task { return (List.rev context.MeasurementResults, context) }
    
    /// Get execution log
    let getLog (context: BuilderContext) =
        task { return (List.rev context.ExecutionLog, context) }
    
    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ========================================================================
    
    /// Computation expression builder for topological quantum programs
    type TopologicalProgramBuilder(backend: TopologicalBackend.ITopologicalBackend) =
        
        /// Initial context
        let initialContext = createContext backend
        
        /// Bind operation for Result-wrapped values
        member _.Bind(operation: BuilderContext -> Task<TopologicalResult<'a * BuilderContext>>, continuation: 'a -> BuilderContext -> Task<TopologicalResult<'b * BuilderContext>>) =
            fun (context: BuilderContext) -> task {
                let! opResult = operation context
                return!
                    match opResult with
                    | Ok (result, ctx) -> continuation result ctx
                    | Error err -> Task.FromResult(Error err)
            }
        
        /// Bind for Result-wrapped context updates (for do!)
        member _.Bind(operation: BuilderContext -> Task<TopologicalResult<BuilderContext>>, continuation: unit -> BuilderContext -> Task<TopologicalResult<'a * BuilderContext>>) =
            fun (context: BuilderContext) -> task {
                let! opResult = operation context
                return!
                    match opResult with
                    | Ok ctx -> continuation () ctx
                    | Error err -> Task.FromResult(Error err)
            }
        
        /// Return a value
        member _.Return(value: 'a) =
            fun (context: BuilderContext) -> task {
                return Ok (value, context)
            }
        
        /// Return from a computation
        member _.ReturnFrom(operation: BuilderContext -> Task<TopologicalResult<'a * BuilderContext>>) =
            operation
        
        /// Zero (for computations without return)
        member _.Zero() =
            fun (context: BuilderContext) -> task {
                return Ok ((), context)
            }
        
        /// Delay evaluation
        member _.Delay(f: unit -> (BuilderContext -> Task<TopologicalResult<'a * BuilderContext>>)) =
            fun (context: BuilderContext) -> task {
                return! f () context
            }
        
        /// Run the computation with initial context
        member _.Run(operation: BuilderContext -> Task<TopologicalResult<'a * BuilderContext>>) = task {
            let! opResult = operation initialContext
            return
                match opResult with
                | Ok (result, _) -> Ok result
                | Error err -> Error err
        }
        
        /// Combine two computations
        member _.Combine(operation1: BuilderContext -> Task<TopologicalResult<unit * BuilderContext>>, operation2: BuilderContext -> Task<TopologicalResult<'a * BuilderContext>>) =
            fun (context: BuilderContext) -> task {
                let! op1Result = operation1 context
                return!
                    match op1Result with
                    | Ok (_, ctx) -> operation2 ctx
                    | Error err -> Task.FromResult(Error err)
            }
        
        /// For loop - functional fold pattern
        member _.For(sequence: seq<'T>, body: 'T -> BuilderContext -> Task<TopologicalResult<unit * BuilderContext>>) =
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
        member _.While(guard: unit -> bool, body: BuilderContext -> Task<TopologicalResult<unit * BuilderContext>>) =
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
    let create (backend: TopologicalBackend.ITopologicalBackend) =
        TopologicalProgramBuilder(backend)
    
    /// Execute a program and return result with execution context
    let executeWithContext (backend: TopologicalBackend.ITopologicalBackend) (program: BuilderContext -> Task<'a * BuilderContext>) = task {
        let ctx = createContext backend
        let! (result, finalContext) = program ctx
        return (result, finalContext)
    }
    
    /// Execute a program and return just the result
    let execute (backend: TopologicalBackend.ITopologicalBackend) (program: BuilderContext -> Task<'a * BuilderContext>) = task {
        let! (result, _) = executeWithContext backend program
        return result
    }
    
    // ========================================================================
    // CUSTOM OPERATIONS (OPTIONAL SYNTAX SUGAR)
    // ========================================================================
    
    type TopologicalProgramBuilder with
        
        /// Custom operation: initialize anyons
        /// Usage: initialize Ising 4
        [<CustomOperation("initialize")>]
        member _.Initialize(context: BuilderContext -> Task<unit * BuilderContext>, anyonType: AnyonSpecies.AnyonType, count: int) =
            fun (ctx: BuilderContext) -> task {
                let! (_, ctx') = context ctx
                let! ctx'' = initialize anyonType count ctx'
                return ((), ctx'')
            }
        
        /// Custom operation: braid
        /// Usage: braid 0
        [<CustomOperation("braid")>]
        member _.Braid(context: BuilderContext -> Task<unit * BuilderContext>, leftIndex: int) =
            fun (ctx: BuilderContext) -> task {
                let! (_, ctx') = context ctx
                let! ctx'' = braid leftIndex ctx'
                return ((), ctx'')
            }
        
        /// Custom operation: measure
        /// Usage: let! outcome = measure 0
        [<CustomOperation("measure")>]
        member _.Measure(context: BuilderContext -> Task<TopologicalResult<unit * BuilderContext>>, leftIndex: int) =
            fun (ctx: BuilderContext) -> task {
                let! ctxResult = context ctx
                return!
                    match ctxResult with
                    | Ok (_, ctx') ->
                        task {
                            let! measureResult = measure leftIndex ctx'
                            return
                                match measureResult with
                                | Ok (outcome, ctx'') -> Ok (outcome, ctx'')
                                | Error err -> Error err
                        }
                    | Error err -> Task.FromResult(Error err)
            }
        
        /// Custom operation: braid sequence
        /// Usage: braidSequence [0; 2; 0; 2]
        [<CustomOperation("braidSequence")>]
        member _.BraidSequence(context: BuilderContext -> Task<TopologicalResult<unit * BuilderContext>>, indices: int list) =
            fun (ctx: BuilderContext) -> task {
                let! ctxResult = context ctx
                return!
                    match ctxResult with
                    | Ok (_, ctx') ->
                        task {
                            let! braidResult = braidSequence indices ctx'
                            return
                                match braidResult with
                                | Ok ctx'' -> Ok ((), ctx'')
                                | Error err -> Error err
                        }
                    | Error err -> Task.FromResult(Error err)
            }

// ========================================================================
// GLOBAL BUILDER INSTANCE
// ========================================================================

/// Global namespace for topological computation expressions
[<AutoOpen>]
module TopologicalBuilderExtensions =
    
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
    let topological (backend: TopologicalBackend.ITopologicalBackend) =
        TopologicalBuilder.TopologicalProgramBuilder(backend)
