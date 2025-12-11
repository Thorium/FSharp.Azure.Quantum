namespace FSharp.Azure.Quantum.Core

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
    
    /// Unwrap Ok value or throw exception with error message
    let get (result: Result<'T, 'E>) : 'T =
        match result with
        | Ok x -> x
        | Error e -> failwith $"Result.get called on Error: {e}"
    
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
