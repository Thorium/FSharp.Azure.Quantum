namespace FSharp.Azure.Quantum

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Microsoft.FSharp.Collections

/// C#-friendly extensions for SubsetSelection module to improve interoperability.
///
/// **Design Philosophy:**
/// - F# remains the primary focus with idiomatic F# APIs
/// - C# overloads provide convenience without sacrificing F# ergonomics
/// - Use C# native types (ValueTuple, IEnumerable, Func) for better IDE support
/// - Extension methods keep the core F# API clean and maintainable
///
/// **What This Module Provides:**
/// 1. ItemBuilder helpers accepting C# value tuples (name, value)
/// 2. Builder method overloads accepting IEnumerable<T> instead of F# list
/// 3. Extension methods for common List<T> -> FSharpList conversions
/// 4. Result<T, E> extension methods for C#-style error handling
[<Extension>]
module SubsetSelectionCSharpExtensions =
    
    open SubsetSelection
    
    // ============================================================================
    // ITEM CREATION HELPERS - C# Value Tuple Support
    // ============================================================================
    
    /// Create an item with multi-dimensional weights using C# value tuples.
    /// 
    /// Example (C#):
    /// ```csharp
    /// var item = CSharp.ItemMulti("laptop", "Laptop", 
    ///     ("weight", 3.0),
    ///     ("value", 1000.0)
    /// );
    /// ```
    [<Extension>]
    let ItemMultiFromTuples (id: string) (value: 'T) (weights: (string * float)[]) : Item<'T> =
        itemMulti id value (Array.toList weights)
    
    /// Create an item with multi-dimensional weights from IEnumerable (C# collections).
    [<Extension>]
    let ItemMultiFromEnumerable (id: string) (value: 'T) (weights: IEnumerable<string * float>) : Item<'T> =
        itemMulti id value (Seq.toList weights)
    
    /// Create a numeric item using C# double.
    [<Extension>]
    let NumericItem (id: string) (value: float) : Item<float> =
        numericItem id value
    
    // ============================================================================
    // BUILDER EXTENSIONS - IEnumerable Support
    // ============================================================================
    
    /// Extension method for SubsetSelectionBuilder to accept IEnumerable<Item<T>>.
    [<Extension>]
    let ItemsFromEnumerable (builder: SubsetSelectionBuilder<'T>) (items: IEnumerable<Item<'T>>) : SubsetSelectionBuilder<'T> =
        builder.Items(Seq.toList items)
    
    /// Extension method for SubsetSelectionBuilder to accept params array of items.
    [<Extension>]
    let ItemsFromArray (builder: SubsetSelectionBuilder<'T>) (items: Item<'T>[]) : SubsetSelectionBuilder<'T> =
        builder.Items(Array.toList items)
    
    // ============================================================================
    // CONSTRAINT HELPERS - C# Friendly Constructors
    // ============================================================================
    
    /// Create MaxLimit constraint (C# helper)
    [<Extension>]
    let MaxLimitConstraint (dimension: string) (limit: float) : SelectionConstraint =
        MaxLimit(dimension, limit)
    
    /// Create MinLimit constraint (C# helper)
    [<Extension>]
    let MinLimitConstraint (dimension: string) (limit: float) : SelectionConstraint =
        MinLimit(dimension, limit)
    
    /// Create ExactTarget constraint (C# helper)
    [<Extension>]
    let ExactTargetConstraint (dimension: string) (target: float) : SelectionConstraint =
        ExactTarget(dimension, target)
    
    /// Create Range constraint (C# helper)
    [<Extension>]
    let RangeConstraint (dimension: string) (min: float) (max: float) : SelectionConstraint =
        Range(dimension, min, max)
    
    // ============================================================================
    // OBJECTIVE HELPERS - C# Friendly Constructors
    // ============================================================================
    
    /// Create MaximizeWeight objective (C# helper)
    [<Extension>]
    let MaximizeWeightObjective (dimension: string) : SelectionObjective =
        MaximizeWeight(dimension)
    
    /// Create MinimizeWeight objective (C# helper)
    [<Extension>]
    let MinimizeWeightObjective (dimension: string) : SelectionObjective =
        MinimizeWeight(dimension)
    
    /// MinimizeCount objective (C# helper)
    let MinimizeCountObjective () : SelectionObjective =
        MinimizeCount
    
    /// MaximizeCount objective (C# helper)
    let MaximizeCountObjective () : SelectionObjective =
        MaximizeCount
    
    // ============================================================================
    // RESULT EXTENSIONS - C# Try Pattern
    // ============================================================================
    
    /// Try-pattern for Result<T, string> (C# helper).
    /// Returns true if Ok, false if Error. Outputs the value or error message.
    [<Extension>]
    let TryGetValue (result: Result<'T, string>) (value: byref<'T>) : bool =
        match result with
        | Ok v -> 
            value <- v
            true
        | Error _ -> 
            false
    
    /// Get error message from Result<T, string> (C# helper).
    [<Extension>]
    let GetErrorMessage (result: Result<'T, string>) : string =
        match result with
        | Ok _ -> ""
        | Error msg -> msg
    
    /// Map Result<T, E> to Result<U, E> using C# Func<T, U>
    [<Extension>]
    let MapResult (result: Result<'T, 'E>) (mapper: System.Func<'T, 'U>) : Result<'U, 'E> =
        Result.map mapper.Invoke result
    
    /// Bind Result<T, E> to Result<U, E> using C# Func<T, Result<U, E>>
    [<Extension>]
    let BindResult (result: Result<'T, 'E>) (binder: System.Func<'T, Result<'U, 'E>>) : Result<'U, 'E> =
        Result.bind binder.Invoke result
    
    // ============================================================================
    // LIST EXTENSIONS - FSharpList <-> IEnumerable Conversions
    // ============================================================================
    
    /// Convert F# list to IEnumerable<T> (already supported natively, included for completeness)
    [<Extension>]
    let ToEnumerable (fsharpList: 'T list) : IEnumerable<'T> =
        fsharpList :> IEnumerable<'T>
    
    /// Convert F# list to List<T> (C# List)
    [<Extension>]
    let ToCSharpList (fsharpList: 'T list) : ResizeArray<'T> =
        ResizeArray<'T>(fsharpList)
    
    /// Convert F# list to T[] array
    [<Extension>]
    let ToArray (fsharpList: 'T list) : 'T[] =
        List.toArray fsharpList
    
    /// Get count of F# list (C# helper - avoids ListModule.Length)
    [<Extension>]
    let Count (fsharpList: 'T list) : int =
        List.length fsharpList

    // ============================================================================
    // C# STATIC BUILDER CLASS - Entry Point for C# Users
    // ============================================================================
    
    /// Static class providing C#-idiomatic entry points for SubsetSelection.
    /// 
    /// Example (C#):
    /// ```csharp
    /// using static FSharp.Azure.Quantum.SubsetSelectionCSharp;
    /// 
    /// var problem = CreateBuilder<string>()
    ///     .Items(item1, item2, item3)
    ///     .AddConstraint(MaxLimit("weight", 10.0))
    ///     .Objective(MaximizeWeight("value"))
    ///     .Build();
    /// ```
    [<AbstractClass; Sealed>]
    type SubsetSelectionCSharp private () =
        
        /// Create a new SubsetSelectionBuilder (C# entry point)
        static member CreateBuilder<'T when 'T : equality>() : SubsetSelectionBuilder<'T> =
            SubsetSelectionBuilder<'T>.Create()
        
        /// Create an item with multi-dimensional weights from C# value tuples
        static member Item<'T when 'T : equality>(id: string, value: 'T, weights: (string * float)[]) : Item<'T> =
            ItemMultiFromTuples id value weights
        
        /// Create a numeric item
        static member Item(id: string, value: float) : Item<float> =
            numericItem id value
        
        /// Create MaxLimit constraint
        static member MaxLimit(dimension: string, limit: float) : SelectionConstraint =
            MaxLimit(dimension, limit)
        
        /// Create MinLimit constraint
        static member MinLimit(dimension: string, limit: float) : SelectionConstraint =
            MinLimit(dimension, limit)
        
        /// Create ExactTarget constraint
        static member ExactTarget(dimension: string, target: float) : SelectionConstraint =
            ExactTarget(dimension, target)
        
        /// Create Range constraint
        static member Range(dimension: string, min: float, max: float) : SelectionConstraint =
            Range(dimension, min, max)
        
        /// Create MaximizeWeight objective
        static member MaximizeWeight(dimension: string) : SelectionObjective =
            MaximizeWeight(dimension)
        
        /// Create MinimizeWeight objective
        static member MinimizeWeight(dimension: string) : SelectionObjective =
            MinimizeWeight(dimension)
        
        /// MinimizeCount objective
        static member MinimizeCount : SelectionObjective =
            MinimizeCount
        
        /// MaximizeCount objective
        static member MaximizeCount : SelectionObjective =
            MaximizeCount
        
        /// Solve knapsack problem (C# wrapper)
        static member SolveKnapsack(problem: SubsetSelectionProblem<'T>, weightDim: string, valueDim: string) : Result<SubsetSelectionSolution<'T>, string> =
            solveKnapsack problem weightDim valueDim
