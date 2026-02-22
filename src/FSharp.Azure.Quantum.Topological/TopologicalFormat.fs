namespace FSharp.Azure.Quantum.Topological

/// Parser and serializer for Topological Quantum Program (.tqp) format
///
/// This module provides import/export functionality for topological quantum programs
/// in a human-readable text format, similar to OpenQASM for gate-based QC.
///
/// Format specification: docs/topological-format-spec.md
module TopologicalFormat =
    
    open System
    open System.IO
    open System.Threading
    open System.Threading.Tasks
    
    // ========================================================================
    // AST (Abstract Syntax Tree)
    // ========================================================================
    
    /// Represents a complete topological quantum program
    type Program = {
        /// Type of anyons used in this program
        AnyonType: AnyonSpecies.AnyonType
        
        /// Sequence of operations to perform
        Operations: Operation list
    }
    
    /// Individual operations in a topological program
    and Operation =
        | Initialize of count: int
        | Braid of leftIndex: int
        | Measure of leftIndex: int
        | FMove of direction: FMoveDirection * depth: int
        | Comment of text: string
    
    /// F-move directions (basis transformations)
    and FMoveDirection =
        | Left
        | Right
        | Up
        | Down
    
    // ========================================================================
    // PARSER
    // ========================================================================
    
    module Parser =
        
        /// Parse result type
        type ParseResult<'T> = Result<'T, string>
        
        /// Parse anyon type from string
        let parseAnyonType (str: string) : ParseResult<AnyonSpecies.AnyonType> =
            match str.Trim().ToLowerInvariant() with
            | "ising" -> Ok AnyonSpecies.AnyonType.Ising
            | "fibonacci" -> Ok AnyonSpecies.AnyonType.Fibonacci
            | s when s.StartsWith("su2_") ->
                match Int32.TryParse(s.Substring(4)) with
                | (true, k) -> Ok (AnyonSpecies.AnyonType.SU2Level k)
                | _ -> Error $"Invalid SU(2)_k level: {s}"
            | _ -> Error $"Unknown anyon type: {str}"
        
        /// Parse F-move direction
        let parseFMoveDirection (str: string) : ParseResult<FMoveDirection> =
            match str.Trim().ToLowerInvariant() with
            | "left" -> Ok Left
            | "right" -> Ok Right
            | "up" -> Ok Up
            | "down" -> Ok Down
            | _ -> Error $"Invalid F-move direction: {str}"
        
        /// Parse a single line into an operation
        let parseLine (lineNum: int) (line: string) : ParseResult<Operation option> =
            let trimmed = line.Trim()
            
            // Empty lines
            if String.IsNullOrWhiteSpace(trimmed) then
                Ok None
            // Comments
            elif trimmed.StartsWith("#") then
                Ok (Some (Comment trimmed))
            else
                let parts = trimmed.Split([|' '; '\t'|], StringSplitOptions.RemoveEmptyEntries)
                
                match parts with
                | [||] -> Ok None
                | [| "ANYON"; anyonType |] ->
                    // ANYON declaration (handled separately)
                    Ok None
                | [| "INIT"; count |] ->
                    match Int32.TryParse(count) with
                    | (true, c) when c > 0 -> Ok (Some (Initialize c))
                    | _ -> Error $"Line {lineNum}: Invalid INIT count: {count}"
                | [| "BRAID"; index |] ->
                    match Int32.TryParse(index) with
                    | (true, i) when i >= 0 -> Ok (Some (Braid i))
                    | _ -> Error $"Line {lineNum}: Invalid BRAID index: {index}"
                | [| "MEASURE"; index |] ->
                    match Int32.TryParse(index) with
                    | (true, i) when i >= 0 -> Ok (Some (Measure i))
                    | _ -> Error $"Line {lineNum}: Invalid MEASURE index: {index}"
                | [| "FMOVE"; direction; depth |] ->
                    match parseFMoveDirection direction, Int32.TryParse(depth) with
                    | Ok dir, (true, d) when d >= 0 -> Ok (Some (FMove (dir, d)))
                    | Error msg, _ -> Error $"Line {lineNum}: {msg}"
                    | _, _ -> Error $"Line {lineNum}: Invalid FMOVE depth: {depth}"
                | _ -> Error $"Line {lineNum}: Unknown operation: {trimmed}"
        
        /// Parse entire program from text
        let parseProgram (text: string) : ParseResult<Program> =
            let lines = text.Split([|'\n'; '\r'|], StringSplitOptions.RemoveEmptyEntries)
            
            // Find ANYON declaration
            let anyonTypeLine = 
                lines 
                |> Array.tryFind (fun line -> 
                    let trimmed = line.Trim()
                    not (trimmed.StartsWith("#")) && trimmed.StartsWith("ANYON"))
            
            match anyonTypeLine with
            | None -> Error "Missing ANYON declaration (first line must be 'ANYON <type>')"
            | Some anyonLine ->
                let parts = anyonLine.Trim().Split([|' '; '\t'|], StringSplitOptions.RemoveEmptyEntries)
                
                match parts with
                | [| "ANYON"; anyonType |] ->
                    match parseAnyonType anyonType with
                    | Error msg -> Error msg
                    | Ok aType ->
                        // Parse operations
                        let operationsResult =
                            lines
                            |> Array.mapi (fun i line -> (i + 1, line))
                            |> Array.map (fun (lineNum, line) -> parseLine lineNum line)
                            |> Array.fold (fun acc result ->
                                match acc, result with
                                | Ok ops, Ok (Some op) -> Ok (op :: ops)
                                | Ok ops, Ok None -> Ok ops
                                | Error msg, _ -> Error msg
                                | _, Error msg -> Error msg
                            ) (Ok [])
                        
                        match operationsResult with
                        | Ok ops -> Ok { AnyonType = aType; Operations = List.rev ops }
                        | Error msg -> Error msg
                | _ -> Error "Invalid ANYON declaration format"
        
        /// Parse program from file
        let parseFile (filePath: string) : ParseResult<Program> =
            try
                let text = File.ReadAllText(filePath)
                parseProgram text
            with ex ->
                Error $"Failed to read file {filePath}: {ex.Message}"
        
        /// Parse program from file asynchronously
        let parseFileAsync (filePath: string) (cancellationToken: CancellationToken) : Task<ParseResult<Program>> =
            task {
                try
                    let! text = File.ReadAllTextAsync(filePath, cancellationToken)
                    return parseProgram text
                with ex ->
                    return Error $"Failed to read file {filePath}: {ex.Message}"
            }
    
    // ========================================================================
    // SERIALIZER
    // ========================================================================
    
    module Serializer =
        
        /// Serialize F-move direction to string
        let serializeFMoveDirection (dir: FMoveDirection) : string =
            match dir with
            | Left -> "Left"
            | Right -> "Right"
            | Up -> "Up"
            | Down -> "Down"
        
        /// Serialize anyon type to string
        let serializeAnyonType (anyonType: AnyonSpecies.AnyonType) : string =
            match anyonType with
            | AnyonSpecies.AnyonType.Ising -> "Ising"
            | AnyonSpecies.AnyonType.Fibonacci -> "Fibonacci"
            | AnyonSpecies.AnyonType.SU2Level k -> $"SU2_{k}"
        
        /// Serialize a single operation to string
        let serializeOperation (op: Operation) : string =
            match op with
            | Initialize count -> $"INIT {count}"
            | Braid index -> $"BRAID {index}"
            | Measure index -> $"MEASURE {index}"
            | FMove (dir, depth) -> $"FMOVE {serializeFMoveDirection dir} {depth}"
            | Comment text -> text
        
        /// Serialize program to .tqp format
        let serializeProgram (program: Program) : string =
            let timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            let headerLines =
                [ "# Topological Quantum Program"
                  $"# Generated: {timestamp}"
                  ""
                  $"ANYON {serializeAnyonType program.AnyonType}"
                  "" ]
            let operationLines =
                program.Operations |> List.map serializeOperation
            
            headerLines @ operationLines |> String.concat "\n"
        
        /// Serialize program to file
        let serializeToFile (program: Program) (filePath: string) : Result<unit, string> =
            try
                let text = serializeProgram program
                File.WriteAllText(filePath, text)
                Ok ()
            with ex ->
                Error $"Failed to write file {filePath}: {ex.Message}"
        
        /// Serialize program to file asynchronously
        let serializeToFileAsync (program: Program) (filePath: string) (cancellationToken: CancellationToken) : Task<Result<unit, string>> =
            task {
                try
                    let text = serializeProgram program
                    do! File.WriteAllTextAsync(filePath, text, cancellationToken)
                    return Ok ()
                with ex ->
                    return Error $"Failed to write file {filePath}: {ex.Message}"
            }
    
    // ========================================================================
    // EXECUTION
    // ========================================================================
    
    module Executor =
        
        open FSharp.Azure.Quantum.Core
        open FSharp.Azure.Quantum.Core.BackendAbstraction
        
        /// Result of executing a topological program
        type ExecutionResult = {
            /// The resulting quantum state
            FinalState: QuantumState
            
            /// Classical measurement outcomes (if any measurements were performed)
            MeasurementOutcomes: (AnyonSpecies.Particle * float) list
            
            /// Any log messages
            Messages: string list
        }
        
        /// Execute a parsed program on a topological backend
        let executeProgram 
            (backend: IQuantumBackend) 
            (program: Program) 
            : Result<ExecutionResult, QuantumError> =
            
            // Convert program operations to QuantumOperations
            let backendOperations =
                program.Operations
                |> List.choose (fun op ->
                    match op with
                    | Initialize _ -> None  // Initialization handled separately
                    | Braid index -> Some (QuantumOperation.Braid index)
                    | Measure index -> Some (QuantumOperation.Measure index)
                    | FMove (dir, depth) ->
                        let fmoveDir = 
                            match dir with
                            | Left -> FMoveDirection.Forward
                            | Right -> FMoveDirection.Backward
                            | Up -> FMoveDirection.Forward      // Map Up to Forward
                            | Down -> FMoveDirection.Backward    // Map Down to Backward
                        Some (QuantumOperation.FMove (fmoveDir, depth))
                    | Comment _ -> None
                )
            
            // Find initialization
            let initOp =
                program.Operations
                |> List.tryPick (fun op ->
                    match op with
                    | Initialize count -> Some count
                    | _ -> None
                )
            
            match initOp with
            | None -> Error (QuantumError.ValidationError ("field", "Program must contain INIT operation"))
            | Some count ->
                // Initialize backend
                match backend.InitializeState count with
                | Error err -> Error err
                | Ok initialState ->
                    // Execute operations sequentially with fold
                    let foldResult =
                        backendOperations
                        |> List.fold (fun acc op ->
                            match acc with
                            | Error err -> Error err  // Short-circuit on error
                            | Ok (state, measurements, messages) ->
                                match backend.ApplyOperation op state with
                                | Error err -> Error err
                                | Ok newState ->
                                    let msg = $"Applied operation: {op}"
                                    Ok (newState, measurements, msg :: messages)
                        ) (Ok (initialState, [], []))
                    
                    match foldResult with
                    | Error err -> Error err
                    | Ok (finalState, measurements, messages) ->
                        Ok {
                            FinalState = finalState
                            MeasurementOutcomes = List.rev measurements
                            Messages = List.rev messages
                        }
        
        /// Execute program from file
        let executeFile 
            (backend: IQuantumBackend) 
            (filePath: string) 
            : Result<ExecutionResult, QuantumError> =
            
            match Parser.parseFile filePath with
            | Error msg -> Error (QuantumError.ValidationError ("filePath", msg))
            | Ok program -> executeProgram backend program

        /// Execute a parsed program on a topological backend asynchronously.
        ///
        /// Uses backend.ApplyOperationAsync for each operation, enabling true non-blocking
        /// execution on cloud backends while remaining compatible with local simulators.
        ///
        /// Parameters:
        ///   backend - IQuantumBackend implementation
        ///   program - Parsed topological program
        ///   cancellationToken - Token for cooperative cancellation
        ///
        /// Returns:
        ///   Task<Result<ExecutionResult, QuantumError>>
        let executeProgramAsync
            (backend: IQuantumBackend)
            (program: Program)
            (cancellationToken: CancellationToken)
            : Task<Result<ExecutionResult, QuantumError>> =
            task {
                // Convert program operations to QuantumOperations
                let backendOperations =
                    program.Operations
                    |> List.choose (fun op ->
                        match op with
                        | Initialize _ -> None
                        | Braid index -> Some (QuantumOperation.Braid index)
                        | Measure index -> Some (QuantumOperation.Measure index)
                        | FMove (dir, depth) ->
                            let fmoveDir =
                                match dir with
                                | Left -> FMoveDirection.Forward
                                | Right -> FMoveDirection.Backward
                                | Up -> FMoveDirection.Forward
                                | Down -> FMoveDirection.Backward
                            Some (QuantumOperation.FMove (fmoveDir, depth))
                        | Comment _ -> None
                    )

                // Find initialization
                let initOp =
                    program.Operations
                    |> List.tryPick (fun op ->
                        match op with
                        | Initialize count -> Some count
                        | _ -> None
                    )

                match initOp with
                | None -> return Error (QuantumError.ValidationError ("field", "Program must contain INIT operation"))
                | Some count ->
                    // InitializeState is CPU-only (no I/O), wrapping in task is fine
                    match backend.InitializeState count with
                    | Error err -> return Error err
                    | Ok initialState ->
                        // Execute operations sequentially with async fold
                        let mutable currentState = initialState
                        let mutable currentError : QuantumError option = None
                        let messages = System.Collections.Generic.List<string>()

                        for op in backendOperations do
                            cancellationToken.ThrowIfCancellationRequested()
                            if currentError.IsNone then
                                let! result = backend.ApplyOperationAsync op currentState cancellationToken
                                match result with
                                | Error err -> currentError <- Some err
                                | Ok newState ->
                                    currentState <- newState
                                    messages.Add($"Applied operation: {op}")

                        match currentError with
                        | Some err -> return Error err
                        | None ->
                            return Ok {
                                FinalState = currentState
                                MeasurementOutcomes = []
                                Messages = messages |> Seq.toList
                            }
            }

        /// Execute program from file asynchronously.
        ///
        /// Chains parseFileAsync (async file I/O) with executeProgramAsync (async backend execution).
        ///
        /// Parameters:
        ///   backend - IQuantumBackend implementation
        ///   filePath - Path to .tqp file
        ///   cancellationToken - Token for cooperative cancellation
        ///
        /// Returns:
        ///   Task<Result<ExecutionResult, QuantumError>>
        let executeFileAsync
            (backend: IQuantumBackend)
            (filePath: string)
            (cancellationToken: CancellationToken)
            : Task<Result<ExecutionResult, QuantumError>> =
            task {
                let! parseResult = Parser.parseFileAsync filePath cancellationToken
                match parseResult with
                | Error msg -> return Error (QuantumError.ValidationError ("filePath", msg))
                | Ok program -> return! executeProgramAsync backend program cancellationToken
            }
