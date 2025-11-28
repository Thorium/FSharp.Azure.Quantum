namespace FSharp.Azure.Quantum

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Microsoft.FSharp.Collections

/// <summary>
/// C#-friendly extensions for ALL builder APIs in FSharp.Azure.Quantum.
/// </summary>
///
/// <remarks>
/// **Design Philosophy:**
/// - F# remains the primary focus with idiomatic F# APIs
/// - C# overloads provide convenience without sacrificing F# ergonomics
/// - Use C# native types (IEnumerable, arrays) for better IDE support
/// - Extension methods keep the core F# API clean and maintainable
///
/// **Builders Covered:**
/// 1. SubsetSelectionBuilder - Subset selection (knapsack, Kasino, etc.)
/// 2. GraphOptimizationBuilder - Graph coloring, TSP, MaxCut
/// 3. SchedulingBuilder - Task scheduling, resource allocation
/// 4. ConstraintSatisfactionBuilder - CSP problems
///
/// **What This Module Provides:**
/// - Builder method overloads accepting IEnumerable&lt;T&gt; / T[] instead of F# list
/// - Extension methods for common conversions (Count, ToArray, etc.)
/// - C#-friendly helper methods for constructors
/// - Result&lt;T, E&gt; extension methods for C#-style error handling
/// </remarks>
[<Extension>]
module BuildersCSharpExtensions =
    
    // ============================================================================
    // SUBSET SELECTION BUILDER EXTENSIONS
    // ============================================================================
    
    // NOTE: SubsetSelection extensions are provided by SubsetSelectionCSharpExtensions.fs
    // to avoid ambiguity. Use the following instead:
    //   - .ItemsFromArray(items[]) - defined in SubsetSelectionCSharpExtensions
    //   - .ItemsFromEnumerable(items) - defined in SubsetSelectionCSharpExtensions
    //   - CSharpBuilders.Item(...) for creating items with C# value tuples
    
    // ============================================================================
    // GRAPH OPTIMIZATION BUILDER EXTENSIONS
    // ============================================================================
    
    /// <summary>Extension method for GraphOptimizationBuilder to accept IEnumerable&lt;Node&lt;T&gt;&gt;.</summary>
    [<Extension>]
    let NodesFromEnumerable (builder: GraphOptimization.GraphOptimizationBuilder<'TNode, 'TEdge>) (nodes: IEnumerable<GraphOptimization.Node<'TNode>>) =
        builder.Nodes(Seq.toList nodes)
    
    /// <summary>Extension method for GraphOptimizationBuilder to accept Node&lt;T&gt;[].</summary>
    [<Extension>]
    let NodesFromArray (builder: GraphOptimization.GraphOptimizationBuilder<'TNode, 'TEdge>) (nodes: GraphOptimization.Node<'TNode>[]) =
        builder.Nodes(Array.toList nodes)
    
    /// <summary>Extension method for GraphOptimizationBuilder to accept IEnumerable&lt;Edge&lt;T&gt;&gt;.</summary>
    [<Extension>]
    let EdgesFromEnumerable (builder: GraphOptimization.GraphOptimizationBuilder<'TNode, 'TEdge>) (edges: IEnumerable<GraphOptimization.Edge<'TEdge>>) =
        builder.Edges(Seq.toList edges)
    
    /// <summary>Extension method for GraphOptimizationBuilder to accept Edge&lt;T&gt;[].</summary>
    [<Extension>]
    let EdgesFromArray (builder: GraphOptimization.GraphOptimizationBuilder<'TNode, 'TEdge>) (edges: GraphOptimization.Edge<'TEdge>[]) =
        builder.Edges(Array.toList edges)
    
    // ============================================================================
    // SCHEDULING BUILDER EXTENSIONS
    // ============================================================================
    
    /// <summary>Extension method for SchedulingBuilder to accept IEnumerable&lt;ScheduledTask&lt;T&gt;&gt;.</summary>
    [<Extension>]
    let TasksFromEnumerable (builder: Scheduling.SchedulingBuilder<'TTask, 'TResource>) (tasks: IEnumerable<Scheduling.ScheduledTask<'TTask>>) =
        builder.Tasks(Seq.toList tasks)
    
    /// <summary>Extension method for SchedulingBuilder to accept ScheduledTask&lt;T&gt;[].</summary>
    [<Extension>]
    let TasksFromArray (builder: Scheduling.SchedulingBuilder<'TTask, 'TResource>) (tasks: Scheduling.ScheduledTask<'TTask>[]) =
        builder.Tasks(Array.toList tasks)
    
    /// <summary>Extension method for SchedulingBuilder to accept IEnumerable&lt;Resource&lt;T&gt;&gt;.</summary>
    [<Extension>]
    let ResourcesFromEnumerable (builder: Scheduling.SchedulingBuilder<'TTask, 'TResource>) (resources: IEnumerable<Scheduling.Resource<'TResource>>) =
        builder.Resources(Seq.toList resources)
    
    /// <summary>Extension method for SchedulingBuilder to accept Resource&lt;T&gt;[].</summary>
    [<Extension>]
    let ResourcesFromArray (builder: Scheduling.SchedulingBuilder<'TTask, 'TResource>) (resources: Scheduling.Resource<'TResource>[]) =
        builder.Resources(Array.toList resources)
    
    // ============================================================================
    // CONSTRAINT SATISFACTION BUILDER EXTENSIONS
    // ============================================================================
    
    /// <summary>Extension method for ConstraintSatisfactionBuilder to accept IEnumerable&lt;Variable&lt;T&gt;&gt;.</summary>
    [<Extension>]
    let VariablesFromEnumerable (builder: ConstraintSatisfaction.ConstraintSatisfactionBuilder<'T>) (variables: IEnumerable<ConstraintSatisfaction.Variable<'T>>) =
        builder.Variables(Seq.toList variables)
    
    /// <summary>Extension method for ConstraintSatisfactionBuilder to accept Variable&lt;T&gt;[].</summary>
    [<Extension>]
    let VariablesFromArray (builder: ConstraintSatisfaction.ConstraintSatisfactionBuilder<'T>) (variables: ConstraintSatisfaction.Variable<'T>[]) =
        builder.Variables(Array.toList variables)
    
    // ============================================================================
    // COMMON LIST EXTENSIONS - Works for all F# lists
    // ============================================================================
    
    /// <summary>Convert F# list to IEnumerable&lt;T&gt; (already supported natively, included for completeness).</summary>
    [<Extension>]
    let ToEnumerable (fsharpList: 'T list) : IEnumerable<'T> =
        fsharpList :> IEnumerable<'T>
    
    /// <summary>Convert F# list to List&lt;T&gt; (C# List).</summary>
    [<Extension>]
    let ToCSharpList (fsharpList: 'T list) : ResizeArray<'T> =
        ResizeArray<'T>(fsharpList)
    
    /// <summary>Convert F# list to T[] array.</summary>
    [<Extension>]
    let ToArrayExt (fsharpList: 'T list) : 'T[] =
        List.toArray fsharpList
    
    /// <summary>Get count of F# list (C# helper - avoids ListModule.Length).</summary>
    [<Extension>]
    let Count (fsharpList: 'T list) : int =
        List.length fsharpList
    
    // ============================================================================
    // RESULT EXTENSIONS - C# Try Pattern
    // ============================================================================
    
    /// <summary>Try-pattern for Result&lt;T, string&gt; (C# helper).</summary>
    /// <remarks>Returns true if Ok, false if Error. Outputs the value or error message.</remarks>
    [<Extension>]
    let TryGetValue (result: Result<'T, string>) (value: byref<'T>) : bool =
        match result with
        | Ok v -> 
            value <- v
            true
        | Error _ -> 
            false
    
    /// <summary>Get error message from Result&lt;T, string&gt; (C# helper).</summary>
    [<Extension>]
    let GetErrorMessage (result: Result<'T, string>) : string =
        match result with
        | Ok _ -> ""
        | Error msg -> msg
    
    /// <summary>Map Result&lt;T, E&gt; to Result&lt;U, E&gt; using C# Func&lt;T, U&gt;.</summary>
    [<Extension>]
    let MapResult (result: Result<'T, 'E>) (mapper: System.Func<'T, 'U>) : Result<'U, 'E> =
        Result.map mapper.Invoke result
    
    /// <summary>Bind Result&lt;T, E&gt; to Result&lt;U, E&gt; using C# Func&lt;T, Result&lt;U, E&gt;&gt;.</summary>
    [<Extension>]
    let BindResult (result: Result<'T, 'E>) (binder: System.Func<'T, Result<'U, 'E>>) : Result<'U, 'E> =
        Result.bind binder.Invoke result
    
    // ============================================================================
    // TUPLE CONVERSIONS - C# Value Tuples <-> F# Tuples
    // ============================================================================
    
    /// <summary>Convert C# value tuple array to F# tuple list.</summary>
    let ValueTuplesToFSharpList (valueTuples: struct(string * float)[]) : (string * float) list =
        valueTuples
        |> Array.map (fun struct(a, b) -> (a, b))
        |> Array.toList
    
    /// <summary>Convert F# tuple list to C# value tuple array.</summary>
    let FSharpListToValueTuples (fsharpTuples: (string * float) list) : struct(string * float)[] =
        fsharpTuples
        |> List.map (fun (a, b) -> struct(a, b))
        |> List.toArray

// ============================================================================
// C# STATIC HELPER CLASS - Entry Point for C# Users
// ============================================================================

/// <summary>
/// Static class providing C#-idiomatic entry points for ALL builder APIs.
/// </summary>
///
/// <remarks>
/// Example usage (C#):
/// <code>
/// using static FSharp.Azure.Quantum.CSharpBuilders;
/// 
/// var item1 = Item("card_2", "2", ("weight", 2.0));
/// var item2 = Item("card_5", "5", ("weight", 5.0));
/// 
/// var subsetProblem = SubsetSelection&lt;string&gt;()
///     .ItemsFromArray(new[] { item1, item2 })
///     .AddConstraint(MaxLimit("weight", 10.0))
///     .Objective(MaximizeWeight("value"))
///     .Build();
/// </code>
/// 
/// **Note:** Named `CSharpBuilders` (not `Builders`) to avoid namespace collision.
/// This allows C# code to use `using FSharp.Azure.Quantum.Builders;` for extension methods
/// without shadowing the conceptual "Builders" namespace.
/// </remarks>
[<AbstractClass; Sealed>]
type CSharpBuilders private () =
    
    // ============================================================================
    // SUBSET SELECTION
    // ============================================================================
    
    /// <summary>Create a new SubsetSelectionBuilder (C# entry point).</summary>
    static member SubsetSelection<'T when 'T : equality>() =
        SubsetSelection.SubsetSelectionBuilder<'T>.Create()
    
    /// <summary>Create an item with multi-dimensional weights from C# value tuples.</summary>
    static member Item<'T when 'T : equality>(id: string, value: 'T, [<ParamArray>] weights: struct(string * float)[]) =
        let fsharpWeights = weights |> Array.map (fun struct(name, weight) -> (name, weight)) |> Array.toList
        SubsetSelection.itemMulti id value fsharpWeights
    
    /// <summary>Create a numeric item.</summary>
    static member Item(id: string, value: float) =
        SubsetSelection.numericItem id value
    
    /// <summary>Create MaxLimit constraint.</summary>
    static member MaxLimit(dimension: string, limit: float) =
        SubsetSelection.SelectionConstraint.MaxLimit(dimension, limit)
    
    /// <summary>Create MinLimit constraint.</summary>
    static member MinLimit(dimension: string, limit: float) =
        SubsetSelection.SelectionConstraint.MinLimit(dimension, limit)
    
    /// <summary>Create ExactTarget constraint.</summary>
    static member ExactTarget(dimension: string, target: float) =
        SubsetSelection.SelectionConstraint.ExactTarget(dimension, target)
    
    /// <summary>Create Range constraint.</summary>
    static member Range(dimension: string, min: float, max: float) =
        SubsetSelection.SelectionConstraint.Range(dimension, min, max)
    
    /// <summary>Create MaximizeWeight objective.</summary>
    static member MaximizeWeight(dimension: string) =
        SubsetSelection.SelectionObjective.MaximizeWeight(dimension)
    
    /// <summary>Create MinimizeWeight objective.</summary>
    static member MinimizeWeight(dimension: string) =
        SubsetSelection.SelectionObjective.MinimizeWeight(dimension)
    
    /// <summary>MinimizeCount objective.</summary>
    static member MinimizeCount =
        SubsetSelection.SelectionObjective.MinimizeCount
    
    /// <summary>MaximizeCount objective.</summary>
    static member MaximizeCount =
        SubsetSelection.SelectionObjective.MaximizeCount
    
    // ============================================================================
    // GRAPH OPTIMIZATION
    // ============================================================================
    
    // TODO: GraphOptimizationBuilder.Create() is not accessible from this module context
    // C# users can use: GraphOptimization.GraphOptimizationBuilder<TNode, TEdge>.Create()
    
    /// <summary>Create a graph node.</summary>
    static member Node<'T when 'T : equality>(id: string, value: 'T) =
        GraphOptimization.node id value
    
    /// <summary>Create a graph node with properties.</summary>
    static member Node<'T when 'T : equality>(id: string, value: 'T, [<ParamArray>] properties: struct(string * obj)[]) =
        let fsharpProps = properties |> Array.map (fun struct(k, v) -> (k, v)) |> Array.toList
        GraphOptimization.nodeWithProps id value fsharpProps
    
    /// <summary>Create an undirected edge.</summary>
    static member Edge(source: string, target: string, weight: float) =
        GraphOptimization.edge source target weight
    
    /// <summary>Create a directed edge.</summary>
    static member DirectedEdge(source: string, target: string, weight: float) =
        GraphOptimization.directedEdge source target weight
    
    // ============================================================================
    // SCHEDULING
    // ============================================================================
    
    // TODO: SchedulingBuilder.Create() is not accessible from this module context
    // C# users can use: Scheduling.SchedulingBuilder<TTask, TResource>.Create()
    
    /// <summary>Create a scheduled task.</summary>
    static member Task<'T when 'T : equality>(id: string, value: 'T, duration: float) =
        Scheduling.task id value duration
    
    /// <summary>Create a task dependency (Finish-to-Start).</summary>
    static member Dependency(from: string, _to: string) =
        Scheduling.Dependency.FinishToStart(from, _to, 0.0)
    
    /// <summary>Create a task dependency (Finish-to-Start with lag).</summary>
    static member Dependency(from: string, _to: string, lag: float) =
        Scheduling.Dependency.FinishToStart(from, _to, lag)
    
    // ============================================================================
    // CONSTRAINT SATISFACTION
    // ============================================================================
    
    // TODO: ConstraintSatisfactionBuilder.Create() is not accessible from this module context
    // C# users can use: ConstraintSatisfaction.ConstraintSatisfactionBuilder<T>.Create()
    
    /// <summary>Create a CSP variable.</summary>
    static member Variable<'T when 'T : equality>(id: string, domain: IEnumerable<'T>) =
        ConstraintSatisfaction.variable id (Seq.toList domain)
    
    /// <summary>Create a CSP variable from array.</summary>
    static member Variable<'T when 'T : equality>(id: string, [<ParamArray>] domain: 'T[]) =
        ConstraintSatisfaction.variable id (Array.toList domain)
