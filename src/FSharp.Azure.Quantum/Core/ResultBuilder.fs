namespace FSharp.Azure.Quantum.Core

open System

/// Result computation expression builder for Railway-Oriented Programming
/// 
/// Provides a clean, composable syntax for handling Result types throughout
/// the quantum algorithms. Enables idiomatic F# error handling without
/// explicit Result.bind chains.
/// 
/// Usage:
///   result {
///       let! state1 = backend.InitializeState 3
///       let! state2 = backend.ApplyOperation op state1
///       return state2
///   }
/// 
/// This is equivalent to:
///   backend.InitializeState 3
///   |> Result.bind (fun state1 -> backend.ApplyOperation op state1)
[<AutoOpen>]
module ResultBuilder =
    
    /// Result computation expression builder
    type ResultBuilder() =
        
        /// Return a value wrapped in Ok
        member inline _.Return(x: 'T) : Result<'T, 'E> = 
            Ok x
        
        /// Return a Result value directly
        member inline _.ReturnFrom(x: Result<'T, 'E>) : Result<'T, 'E> = 
            x
        
        /// Bind a Result value to a function
        member inline _.Bind(x: Result<'T, 'E>, f: 'T -> Result<'U, 'E>) : Result<'U, 'E> = 
            Result.bind f x
        
        /// Return unit wrapped in Ok
        member inline _.Zero() : Result<unit, 'E> = 
            Ok ()
        
        /// Combine two Result values, propagating errors
        member inline _.Combine(r1: Result<unit, 'E>, r2: Result<'T, 'E>) : Result<'T, 'E> = 
            Result.bind (fun () -> r2) r1
        
        /// Delay computation (for lazy evaluation)
        member inline _.Delay(f: unit -> Result<'T, 'E>) : unit -> Result<'T, 'E> = 
            f
        
        /// Run delayed computation
        member inline _.Run(f: unit -> Result<'T, 'E>) : Result<'T, 'E> = 
            f()
        
        /// Try-with for error handling within result blocks
        member inline _.TryWith(body: unit -> Result<'T, 'E>, handler: exn -> Result<'T, 'E>) : Result<'T, 'E> =
            try 
                body()
            with ex -> 
                handler ex
        
        /// Try-finally for cleanup within result blocks
        member inline _.TryFinally(body: unit -> Result<'T, 'E>, compensation: unit -> unit) : Result<'T, 'E> =
            try 
                body()
            finally 
                compensation()
        
        /// While loop support
        member inline _.While(guard: unit -> bool, body: unit -> Result<unit, 'E>) : Result<unit, 'E> =
            let rec loop() =
                if guard() then
                    match body() with
                    | Ok () -> loop()
                    | Error e -> Error e
                else
                    Ok ()
            loop()
        
        /// For loop support
        member inline _.For(sequence: seq<'T>, body: 'T -> Result<unit, 'E>) : Result<unit, 'E> =
            let rec loop (enumerator: System.Collections.Generic.IEnumerator<'T>) =
                if enumerator.MoveNext() then
                    match body enumerator.Current with
                    | Ok () -> loop enumerator
                    | Error e -> Error e
                else
                    Ok ()
            use enumerator = sequence.GetEnumerator()
            loop enumerator
    
    /// Global instance of ResultBuilder for use in computation expressions
    let result = ResultBuilder()


/// Extended Result utilities for common operations
module Result =
    
    /// Apply a function to the value inside Ok, or propagate Error
    let map (f: 'T -> 'U) (result: Result<'T, 'E>) : Result<'U, 'E> =
        match result with
        | Ok x -> Ok (f x)
        | Error e -> Error e
    
    /// Apply a function to the error inside Error, or propagate Ok
    let mapError (f: 'E -> 'F) (result: Result<'T, 'E>) : Result<'T, 'F> =
        match result with
        | Ok x -> Ok x
        | Error e -> Error (f e)
    
    /// Bind operation (already in F# Core, included for completeness)
    let bind (f: 'T -> Result<'U, 'E>) (result: Result<'T, 'E>) : Result<'U, 'E> =
        match result with
        | Ok x -> f x
        | Error e -> Error e
    
    /// Convert Result to Option (discards error information)
    let toOption (result: Result<'T, 'E>) : 'T option =
        match result with
        | Ok x -> Some x
        | Error _ -> None
    
    /// Get value from Ok or provide default
    let defaultValue (defaultVal: 'T) (result: Result<'T, 'E>) : 'T =
        match result with
        | Ok x -> x
        | Error _ -> defaultVal
    
    /// Get value from Ok or compute default
    let defaultWith (getDefault: 'E -> 'T) (result: Result<'T, 'E>) : 'T =
        match result with
        | Ok x -> x
        | Error e -> getDefault e
    
    /// Unwrap Ok value or throw exception.
    ///
    /// NOTE: Prefer matching on the Result or using `defaultValue` / `defaultWith`.
    /// This function exists mainly for tests/quick scripts.
    [<Obsolete("Result.get throws on Error. Prefer matching on Result or using defaultValue/defaultWith.")>]
    let get (result: Result<'T, 'E>) : 'T =
        match result with
        | Ok x -> x
        | Error e ->
            raise (InvalidOperationException($"Result.get called on Error: {e}"))

    /// Unwrap Ok value or throw exception.
    ///
    /// Use this only when a thrown exception is acceptable.
    let unsafeGet (result: Result<'T, 'E>) : 'T =
        match result with
        | Ok x -> x
        | Error e ->
            raise (InvalidOperationException($"Result.unsafeGet called on Error: {e}"))
    
    /// Check if result is Ok
    let isOk (result: Result<'T, 'E>) : bool =
        match result with
        | Ok _ -> true
        | Error _ -> false
    
    /// Return the result if Ok, otherwise compute an alternative Result
    let orElseWith (getAlternative: 'E -> Result<'T, 'F>) (result: Result<'T, 'E>) : Result<'T, 'F> =
        match result with
        | Ok x -> Ok x
        | Error e -> getAlternative e
    
    /// Check if result is Error
    let isError (result: Result<'T, 'E>) : bool =
        match result with
        | Ok _ -> false
        | Error _ -> true
    
    /// Combine two Results into a tuple if both are Ok
    let zip (r1: Result<'T, 'E>) (r2: Result<'U, 'E>) : Result<'T * 'U, 'E> =
        match r1, r2 with
        | Ok x, Ok y -> Ok (x, y)
        | Error e, _ -> Error e
        | _, Error e -> Error e
    
    /// Sequence a list of Results into a Result of list
    let sequence (results: Result<'T, 'E> list) : Result<'T list, 'E> =
        results
        |> List.fold (fun accResult item ->
            match accResult, item with
            | Ok acc, Ok x -> Ok (x :: acc)
            | Error e, _ -> Error e
            | _, Error e -> Error e
        ) (Ok [])
        |> map List.rev
    
    /// Traverse a list with a function returning Results
    let traverse (f: 'T -> Result<'U, 'E>) (items: 'T list) : Result<'U list, 'E> =
        items
        |> List.map f
        |> sequence
    
    /// Sequence an array of Results into a Result of array
    /// 
    /// Efficiently converts Result<'T, 'E>[] to Result<'T[], 'E>
    /// Returns first error encountered, or Ok with all values.
    /// 
    /// Performance: O(n) time, O(n) space (pre-allocated array)
    /// 
    /// Example:
    ///   let results = [| Ok 1; Ok 2; Ok 3 |]
    ///   sequenceArray results  // Ok [| 1; 2; 3 |]
    ///   
    ///   let withError = [| Ok 1; Error "oops"; Ok 3 |]
    ///   sequenceArray withError  // Error "oops"
    let sequenceArray (results: Result<'T, 'E> array) : Result<'T array, 'E> =
        let mutable error = None
        let arr = Array.zeroCreate results.Length
        let mutable i = 0
        
        // Scan array until error found or all processed
        while i < results.Length && error.IsNone do
            match results.[i] with
            | Ok x -> arr.[i] <- x
            | Error e -> error <- Some e
            i <- i + 1
        
        match error with
        | Some e -> Error e
        | None -> Ok arr
    
    /// Traverse an array with a function returning Results
    /// 
    /// Maps array elements with a Result-returning function, then sequences.
    /// More efficient than Array.map + sequenceArray (single allocation).
    /// 
    /// Performance: O(n) time, O(n) space
    /// 
    /// Example:
    ///   let parseInts = traverseArray Int32.TryParse [| "1"; "2"; "3" |]
    ///   
    ///   let processQubits backend qubits =
    ///       qubits
    ///       |> traverseArray (fun qubit ->
    ///           backend.ApplyOperation (Gate (H qubit)) initialState
    ///       )
    let traverseArray (f: 'T -> Result<'U, 'E>) (items: 'T array) : Result<'U array, 'E> =
        items
        |> Array.map f
        |> sequenceArray


/// Structured logging helpers for Microsoft.Extensions.Logging.ILogger.
///
/// Provides idiomatic F# functions that accept ILogger option, so callers
/// can pass None when logging is disabled. Replaces raw printfn calls
/// throughout the library with structured, configurable logging.
[<AutoOpen>]
module QuantumLogger =

    open Microsoft.Extensions.Logging

    /// Log an informational message if a logger is provided.
    let logInfo (logger: ILogger option) (message: string) =
        logger |> Option.iter (fun l -> l.LogInformation(message))

    /// Log a warning message if a logger is provided.
    let logWarning (logger: ILogger option) (message: string) =
        logger |> Option.iter (fun l -> l.LogWarning(message))

    /// Log an error message if a logger is provided.
    let logError (logger: ILogger option) (message: string) =
        logger |> Option.iter (fun l -> l.LogError(message))

    /// Log a debug message if a logger is provided.
    let logDebug (logger: ILogger option) (message: string) =
        logger |> Option.iter (fun l -> l.LogDebug(message))


/// Idiomatic F# helpers for System.Text.Json.JsonElement property access.
///
/// System.Text.Json requires outref parameters for TryGetProperty, which forces
/// mutable + Unchecked.defaultof in F#. These helpers encapsulate that pattern
/// once, providing clean Option-returning functions for all call sites.
[<AutoOpen>]
module JsonHelpers =

    open System.Text.Json

    /// Try to get a property from a JsonElement, returning Option.
    /// Encapsulates the mutable + Unchecked.defaultof pattern required by System.Text.Json.
    let tryGetJsonProperty (name: string) (element: JsonElement) : JsonElement option =
        let mutable prop = Unchecked.defaultof<JsonElement>
        if element.TryGetProperty(name, &prop) && prop.ValueKind <> JsonValueKind.Null then
            Some prop
        else
            None

    /// Try to get a string property from a JsonElement.
    let tryGetJsonString (name: string) (element: JsonElement) : string option =
        tryGetJsonProperty name element |> Option.map (fun e -> e.GetString())

    /// Try to get a double property from a JsonElement.
    let tryGetJsonDouble (name: string) (element: JsonElement) : float option =
        tryGetJsonProperty name element |> Option.map (fun e -> e.GetDouble())

    /// Try to get a DateTimeOffset property from a JsonElement.
    let tryGetJsonDateTimeOffset (name: string) (element: JsonElement) : System.DateTimeOffset option =
        tryGetJsonProperty name element |> Option.map (fun e -> e.GetDateTimeOffset())

    /// Get a string property, returning a default if missing or null.
    let getJsonStringOrDefault (name: string) (defaultValue: string) (element: JsonElement) : string =
        tryGetJsonString name element |> Option.defaultValue defaultValue
