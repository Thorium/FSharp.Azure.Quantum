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
/// 1. GraphOptimizationBuilder - Graph coloring, TSP, MaxCut
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
/// // Use KnapsackBuilder from Solvers/Classical/ instead
/// </code>
/// 
/// **Note:** Named `CSharpBuilders` (not `Builders`) to avoid namespace collision.
/// This allows C# code to use `using FSharp.Azure.Quantum.Builders;` for extension methods
/// without shadowing the conceptual "Builders" namespace.
/// </remarks>
[<AbstractClass; Sealed>]
type CSharpBuilders private () =
    
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
    // MAXCUT BUILDER EXTENSIONS
    // ============================================================================
    
    /// <summary>Create MaxCut problem from vertices and edges (C# helper).</summary>
    /// <param name="vertices">Array of vertex names</param>
    /// <param name="edges">Array of (source, target, weight) tuples</param>
    static member MaxCutProblem(vertices: string[], edges: struct(string * string * float)[]) =
        let edgeList = edges |> Array.map (fun struct(s, t, w) -> (s, t, w)) |> Array.toList
        MaxCut.createProblem (Array.toList vertices) edgeList
    
    /// <summary>Create complete graph for MaxCut (all vertices connected) (C# helper).</summary>
    /// <param name="vertices">Array of vertex names</param>
    /// <param name="weight">Weight for all edges</param>
    static member CompleteGraph(vertices: string[], weight: float) =
        MaxCut.completeGraph (Array.toList vertices) weight
    
    /// <summary>Create cycle graph for MaxCut (vertices connected in a ring) (C# helper).</summary>
    /// <param name="vertices">Array of vertex names in cycle order</param>
    /// <param name="weight">Weight for all edges</param>
    static member CycleGraph(vertices: string[], weight: float) =
        MaxCut.cycleGraph (Array.toList vertices) weight
    
    // ============================================================================
    // KNAPSACK BUILDER EXTENSIONS
    // ============================================================================
    
    /// <summary>Create knapsack problem from items and capacity (C# helper).</summary>
    /// <param name="items">Array of (id, weight, value) tuples</param>
    /// <param name="capacity">Maximum total weight allowed</param>
    static member KnapsackProblem(items: struct(string * float * float)[], capacity: float) =
        let itemList = items |> Array.map (fun struct(id, w, v) -> (id, w, v)) |> Array.toList
        Knapsack.createProblem itemList capacity
    
    // ============================================================================
    // TSP BUILDER EXTENSIONS
    // ============================================================================
    
    /// <summary>Create TSP problem from cities with coordinates (C# helper).</summary>
    /// <param name="cities">Array of (name, x, y) tuples</param>
    static member TspProblem(cities: struct(string * float * float)[]) =
        let cityList = cities |> Array.map (fun struct(name, x, y) -> (name, x, y)) |> Array.toList
        TSP.createProblem cityList
    
    // ============================================================================
    // PORTFOLIO BUILDER EXTENSIONS
    // ============================================================================
    
    /// <summary>Create portfolio problem from assets and budget (C# helper).</summary>
    /// <param name="assets">Array of (symbol, expectedReturn, risk, price) tuples</param>
    /// <param name="budget">Total budget available</param>
    static member PortfolioProblem(assets: struct(string * float * float * float)[], budget: float) =
        let assetList = assets |> Array.map (fun struct(s, r, k, p) -> (s, r, k, p)) |> Array.toList
        Portfolio.createProblem assetList budget
