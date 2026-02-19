namespace FSharp.Azure.Quantum

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Threading.Tasks
open Microsoft.FSharp.Collections
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Business

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
    
    /// <summary>Check if Result is Ok (C# helper).</summary>
    [<Extension>]
    let IsOk (result: Result<'T, 'E>) : bool =
        match result with
        | Ok _ -> true
        | Error _ -> false
    
    /// <summary>Check if Result is Error (C# helper).</summary>
    [<Extension>]
    let IsError (result: Result<'T, 'E>) : bool =
        match result with
        | Ok _ -> false
        | Error _ -> true
    
    /// <summary>Get Ok value from Result (throws if Error) (C# helper).</summary>
    /// <exception cref="InvalidOperationException">Thrown when result is Error</exception>
    [<Extension>]
    let GetOkValue (result: Result<'T, 'E>) : 'T =
        match result with
        | Ok v -> v
        | Error _ -> invalidOp "Cannot get Ok value from Error result"
    
    /// <summary>Get Error value from Result (throws if Ok) (C# helper).</summary>
    /// <exception cref="InvalidOperationException">Thrown when result is Ok</exception>
    [<Extension>]
    let GetErrorValue (result: Result<'T, 'E>) : 'E =
        match result with
        | Ok _ -> invalidOp "Cannot get Error value from Ok result"
        | Error e -> e
    
    /// <summary>Get Ok value or default (C# helper).</summary>
    [<Extension>]
    let GetOkValueOrDefault (result: Result<'T, 'E>) (defaultValue: 'T) : 'T =
        match result with
        | Ok v -> v
        | Error _ -> defaultValue
    
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
    
    /// <summary>Find all valid combinations that sum exactly to capacity (classical fallback).</summary>
    /// <param name="problem">Knapsack problem</param>
    /// <returns>Tuple of (all combinations, union of all items, combination count)</returns>
    /// <remarks>Uses classical enumeration. For quantum execution, use the overload with backend parameter.</remarks>
    static member FindAllValidCombinations(problem: Knapsack.Problem) =
        Knapsack.findAllValidCombinations problem None

    /// <summary>Find all valid combinations that sum exactly to capacity using quantum backend.</summary>
    /// <param name="problem">Knapsack problem</param>
    /// <param name="backend">Quantum backend for QAOA execution</param>
    /// <returns>Tuple of (all combinations, union of all items, combination count)</returns>
    /// <remarks>Uses iterative QAOA with exclusion penalties to discover all subset-sum solutions.</remarks>
    static member FindAllValidCombinations(problem: Knapsack.Problem, backend: BackendAbstraction.IQuantumBackend) =
        Knapsack.findAllValidCombinations problem (Some backend)

    /// <summary>Find all exact combinations that sum to capacity (classical fallback).</summary>
    /// <param name="problem">Knapsack problem</param>
    /// <returns>List of all valid combinations</returns>
    /// <remarks>Uses classical enumeration. For quantum execution, use the overload with backend parameter.</remarks>
    static member FindAllExactCombinations(problem: Knapsack.Problem) =
        Knapsack.findAllExactCombinations problem None

    /// <summary>Find all exact combinations that sum to capacity using quantum backend.</summary>
    /// <param name="problem">Knapsack problem</param>
    /// <param name="backend">Quantum backend for QAOA execution</param>
    /// <returns>List of all valid combinations</returns>
    /// <remarks>Uses iterative QAOA with exclusion penalties to discover all subset-sum solutions.</remarks>
    static member FindAllExactCombinations(problem: Knapsack.Problem, backend: BackendAbstraction.IQuantumBackend) =
        Knapsack.findAllExactCombinations problem (Some backend)

    /// <summary>Find union of all items across all exact combinations (classical fallback).</summary>
    /// <param name="problem">Knapsack problem</param>
    /// <returns>List of all items that appear in at least one valid combination</returns>
    /// <remarks>Uses classical enumeration. For quantum execution, use the overload with backend parameter.</remarks>
    static member FindAllCapturedItems(problem: Knapsack.Problem) =
        Knapsack.findAllCapturedItems problem None

    /// <summary>Find union of all items across all exact combinations using quantum backend.</summary>
    /// <param name="problem">Knapsack problem</param>
    /// <param name="backend">Quantum backend for QAOA execution</param>
    /// <returns>List of all items that appear in at least one valid combination</returns>
    /// <remarks>Uses iterative QAOA with exclusion penalties to discover all subset-sum solutions.</remarks>
    static member FindAllCapturedItems(problem: Knapsack.Problem, backend: BackendAbstraction.IQuantumBackend) =
        Knapsack.findAllCapturedItems problem (Some backend)
    
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
    
    // ============================================================================
    // OPTION PRICING BUILDER EXTENSIONS - Quantum Monte Carlo
    // ============================================================================
    
    /// <summary>Price European call option using quantum Monte Carlo (C# helper).</summary>
    /// <param name="spotPrice">Current price of underlying asset</param>
    /// <param name="strikePrice">Strike price of the option</param>
    /// <param name="riskFreeRate">Risk-free interest rate (annualized)</param>
    /// <param name="volatility">Volatility of underlying asset (annualized)</param>
    /// <param name="timeToExpiry">Time to expiry in years</param>
    /// <param name="backend">Quantum backend (REQUIRED - RULE1 compliance)</param>
    /// <returns>Async task with option price result</returns>
    /// <remarks>
    /// **RULE1 COMPLIANCE**: Backend is REQUIRED (not optional).
    /// 
    /// Uses Quantum Monte Carlo for quadratic speedup over classical Monte Carlo.
    /// Classical MC: O(1/ε²) samples, Quantum MC: O(1/ε) queries → 100x speedup!
    /// </remarks>
    static member PriceEuropeanCall(spotPrice: float, strikePrice: float, riskFreeRate: float, volatility: float, timeToExpiry: float, numQubits: int, groverIterations: int, shots: int, backend: IQuantumBackend) =
        OptionPricing.priceEuropeanCall spotPrice strikePrice riskFreeRate volatility timeToExpiry numQubits groverIterations shots backend
    
    /// <summary>Price European put option using quantum Monte Carlo (C# helper).</summary>
    /// <param name="spotPrice">Current price of underlying asset</param>
    /// <param name="strikePrice">Strike price of the option</param>
    /// <param name="riskFreeRate">Risk-free interest rate (annualized)</param>
    /// <param name="volatility">Volatility of underlying asset (annualized)</param>
    /// <param name="timeToExpiry">Time to expiry in years</param>
    /// <param name="backend">Quantum backend (REQUIRED - RULE1 compliance)</param>
    /// <returns>Async task with option price result</returns>
    static member PriceEuropeanPut(spotPrice: float, strikePrice: float, riskFreeRate: float, volatility: float, timeToExpiry: float, numQubits: int, groverIterations: int, shots: int, backend: IQuantumBackend) =
        OptionPricing.priceEuropeanPut spotPrice strikePrice riskFreeRate volatility timeToExpiry numQubits groverIterations shots backend
    
    /// <summary>Price Asian call option using quantum Monte Carlo (C# helper).</summary>
    /// <param name="spotPrice">Current price of underlying asset</param>
    /// <param name="strikePrice">Strike price of the option</param>
    /// <param name="riskFreeRate">Risk-free interest rate (annualized)</param>
    /// <param name="volatility">Volatility of underlying asset (annualized)</param>
    /// <param name="timeToExpiry">Time to expiry in years</param>
    /// <param name="timeSteps">Number of time steps for averaging</param>
    /// <param name="backend">Quantum backend (REQUIRED - RULE1 compliance)</param>
    /// <returns>Async task with option price result</returns>
    static member PriceAsianCall(spotPrice: float, strikePrice: float, riskFreeRate: float, volatility: float, timeToExpiry: float, timeSteps: int, numQubits: int, groverIterations: int, shots: int, backend: IQuantumBackend) =
        OptionPricing.priceAsianCall spotPrice strikePrice riskFreeRate volatility timeToExpiry timeSteps numQubits groverIterations shots backend
    
    /// <summary>Price Asian put option using quantum Monte Carlo (C# helper).</summary>
    /// <param name="spotPrice">Current price of underlying asset</param>
    /// <param name="strikePrice">Strike price of the option</param>
    /// <param name="riskFreeRate">Risk-free interest rate (annualized)</param>
    /// <param name="volatility">Volatility of underlying asset (annualized)</param>
    /// <param name="timeToExpiry">Time to expiry in years</param>
    /// <param name="timeSteps">Number of time steps for averaging</param>
    /// <param name="backend">Quantum backend (REQUIRED - RULE1 compliance)</param>
    /// <returns>Async task with option price result</returns>
    static member PriceAsianPut(spotPrice: float, strikePrice: float, riskFreeRate: float, volatility: float, timeToExpiry: float, timeSteps: int, numQubits: int, groverIterations: int, shots: int, backend: IQuantumBackend) =
        OptionPricing.priceAsianPut spotPrice strikePrice riskFreeRate volatility timeToExpiry timeSteps numQubits groverIterations shots backend
    
    // ============================================================================
    // QUANTUM TREE SEARCH BUILDER EXTENSIONS
    // ============================================================================
    
    /// <summary>Create a simple quantum tree search problem with defaults (C# helper).</summary>
    /// <typeparam name="T">Game state type</typeparam>
    /// <param name="initialState">Starting game state</param>
    /// <param name="evaluator">Function to evaluate positions</param>
    /// <param name="moveGenerator">Function to generate legal moves</param>
    static member QuantumTreeSearch<'T>(initialState: 'T, evaluator: Func<'T, float>, moveGenerator: Func<'T, IEnumerable<'T>>) =
        QuantumTreeSearch.simple 
            initialState 
            evaluator.Invoke 
            (fun state -> moveGenerator.Invoke(state) |> Seq.toList)
    
    /// <summary>Create a game AI tree search problem (C# helper).</summary>
    /// <typeparam name="T">Game board type</typeparam>
    /// <param name="board">Current board state</param>
    /// <param name="depth">Search depth</param>
    /// <param name="branching">Expected branching factor</param>
    /// <param name="evaluator">Board evaluation function</param>
    /// <param name="legalMoves">Legal move generator</param>
    static member GameAISearch<'T>(board: 'T, depth: int, branching: int, evaluator: Func<'T, float>, legalMoves: Func<'T, IEnumerable<'T>>) =
        QuantumTreeSearch.forGameAI 
            board 
            depth 
            branching 
            evaluator.Invoke 
            (fun state -> legalMoves.Invoke(state) |> Seq.toList)
    
    /// <summary>Create a decision problem tree search (C# helper).</summary>
    /// <typeparam name="T">Decision state type</typeparam>
    /// <param name="initialDecision">Starting decision state</param>
    /// <param name="steps">Number of decision steps</param>
    /// <param name="optionsPerStep">Options available per step</param>
    /// <param name="scorer">Function to score outcomes</param>
    /// <param name="nextOptions">Function to generate next options</param>
    static member DecisionProblem<'T>(initialDecision: 'T, steps: int, optionsPerStep: int, scorer: Func<'T, float>, nextOptions: Func<'T, IEnumerable<'T>>) =
        QuantumTreeSearch.forDecisionProblem 
            initialDecision 
            steps 
            optionsPerStep 
            scorer.Invoke 
            (fun state -> nextOptions.Invoke(state) |> Seq.toList)
    
    /// <summary>Estimate quantum resources for tree search (C# helper).</summary>
    /// <param name="maxDepth">Maximum search depth</param>
    /// <param name="branchingFactor">Expected branching factor</param>
    /// <returns>Resource estimate description</returns>
    static member EstimateTreeSearchResources(maxDepth: int, branchingFactor: int) =
        QuantumTreeSearch.estimateResources maxDepth branchingFactor
    
    /// <summary>Solve quantum tree search problem (C# helper).</summary>
    /// <typeparam name="T">State type</typeparam>
    /// <param name="problem">Tree search problem</param>
    /// <returns>Result with best move or error message</returns>
    static member SolveTreeSearch<'T>(problem: QuantumTreeSearch.TreeSearchProblem<'T>) =
        QuantumTreeSearch.solve problem
    
    // ============================================================================
    // QUANTUM CONSTRAINT SOLVER BUILDER EXTENSIONS
    // ============================================================================
    
    /// <summary>Create a simple quantum constraint solver problem (C# helper).</summary>
    /// <typeparam name="T">Domain value type</typeparam>
    /// <param name="searchSpaceSize">Size of search space</param>
    /// <param name="domain">Array of valid values</param>
    /// <param name="singleConstraint">Single constraint predicate</param>
    static member QuantumConstraintSolver<'T>(searchSpaceSize: int, domain: 'T[], singleConstraint: Func<IDictionary<int, 'T>, bool>) =
        let domainList = Array.toList domain
        let constraintFunc (m: Map<int, 'T>) = singleConstraint.Invoke(m :> IDictionary<int,'T>)
        QuantumConstraintSolver.simple searchSpaceSize domainList constraintFunc
    
    /// <summary>Create a constraint solver for Sudoku-style problems (C# helper).</summary>
    /// <typeparam name="T">Domain value type</typeparam>
    /// <param name="gridSize">Size of the grid</param>
    /// <param name="domain">Array of valid values</param>
    /// <param name="constraints">Array of constraint predicates</param>
    static member SudokuStyleConstraints<'T>(gridSize: int, domain: 'T[], [<ParamArray>] constraints: Func<IDictionary<int, 'T>, bool>[]) =
        let domainList = Array.toList domain
        let constraintList = constraints |> Array.map (fun f -> fun (m: Map<int, 'T>) -> f.Invoke(m :> IDictionary<int,'T>)) |> Array.toList
        QuantumConstraintSolver.forSudokuStyle gridSize domainList constraintList
    
    /// <summary>Create a constraint solver for N-Queens problems (C# helper).</summary>
    /// <typeparam name="T">Domain value type</typeparam>
    /// <param name="boardSize">Size of the board</param>
    /// <param name="domain">Array of valid values</param>
    /// <param name="constraints">Array of constraint predicates</param>
    static member NQueensConstraints<'T>(boardSize: int, domain: 'T[], [<ParamArray>] constraints: Func<IDictionary<int, 'T>, bool>[]) =
        let domainList = Array.toList domain
        let constraintList = constraints |> Array.map (fun f -> fun (m: Map<int, 'T>) -> f.Invoke(m :> IDictionary<int,'T>)) |> Array.toList
        QuantumConstraintSolver.forNQueens boardSize domainList constraintList
    
    /// <summary>Estimate quantum resources for constraint solving (C# helper).</summary>
    /// <param name="searchSpaceSize">Size of search space</param>
    /// <returns>Resource estimate description</returns>
    static member EstimateConstraintSolverResources(searchSpaceSize: int) =
        QuantumConstraintSolver.estimateResources searchSpaceSize
    
    /// <summary>Solve quantum constraint problem (C# helper).</summary>
    /// <typeparam name="T">Domain value type</typeparam>
    /// <param name="problem">Constraint solving problem</param>
    /// <returns>Result with solution or error message</returns>
    static member SolveConstraints<'T>(problem: QuantumConstraintSolver.ConstraintProblem<'T>) =
        QuantumConstraintSolver.solve problem
    
    // ============================================================================
    // QUANTUM PATTERN MATCHER BUILDER EXTENSIONS
    // ============================================================================
    
    /// <summary>Create a simple quantum pattern matcher problem (C# helper).</summary>
    /// <typeparam name="T">Configuration type</typeparam>
    /// <param name="configurations">Array of configurations to search</param>
    /// <param name="pattern">Pattern matching predicate</param>
    static member QuantumPatternMatcher<'T>(configurations: 'T[], pattern: Func<'T, bool>) =
        QuantumPatternMatcher.simple (Array.toList configurations) pattern.Invoke
    
    /// <summary>Find all matching configurations (C# helper).</summary>
    /// <typeparam name="T">Configuration type</typeparam>
    /// <param name="configurations">Array of configurations</param>
    /// <param name="pattern">Pattern matching predicate</param>
    static member FindAllMatches<'T>(configurations: 'T[], pattern: Func<'T, bool>) =
        QuantumPatternMatcher.findAll (Array.toList configurations) pattern.Invoke
    
    /// <summary>Create a pattern matcher for configuration optimization (C# helper).</summary>
    /// <typeparam name="T">Configuration type</typeparam>
    /// <param name="configurations">Array of configurations</param>
    /// <param name="performanceCheck">Performance validation predicate</param>
    /// <param name="topN">Number of top configurations to return</param>
    static member ConfigurationOptimizer<'T>(configurations: 'T[], performanceCheck: Func<'T, bool>, topN: int) =
        QuantumPatternMatcher.forConfigOptimization (Array.toList configurations) performanceCheck.Invoke topN
    
    /// <summary>Create a pattern matcher for hyperparameter tuning (C# helper).</summary>
    /// <param name="searchSpaceSize">Number of hyperparameter combinations</param>
    /// <param name="evaluator">Function to evaluate hyperparameter sets by index</param>
    /// <param name="topN">Number of top hyperparameter sets to return</param>
    static member HyperparameterTuning(searchSpaceSize: int, evaluator: Func<int, bool>, topN: int) =
        QuantumPatternMatcher.forHyperparameterTuning searchSpaceSize evaluator.Invoke topN
    
    /// <summary>Create a pattern matcher for feature selection (C# helper).</summary>
    /// <typeparam name="T">Feature set type</typeparam>
    /// <param name="featureSets">Array of feature combinations</param>
    /// <param name="modelPerformance">Function to evaluate feature set quality</param>
    /// <param name="topN">Number of top feature sets to return</param>
    static member FeatureSelection<'T>(featureSets: 'T[], modelPerformance: Func<'T, bool>, topN: int) =
        QuantumPatternMatcher.forFeatureSelection (Array.toList featureSets) modelPerformance.Invoke topN
    
    /// <summary>Create a pattern matcher for A/B test variant selection (C# helper).</summary>
    /// <typeparam name="T">Variant type</typeparam>
    /// <param name="variants">Array of test variants</param>
    /// <param name="conversionCheck">Function to check variant performance</param>
    /// <param name="topN">Number of top variants to return</param>
    static member ABTestSelection<'T>(variants: 'T[], conversionCheck: Func<'T, bool>, topN: int) =
        QuantumPatternMatcher.forABTesting (Array.toList variants) conversionCheck.Invoke topN
    
    /// <summary>Estimate quantum resources for pattern matching (C# helper).</summary>
    /// <param name="searchSpaceSize">Size of search space</param>
    /// <param name="topN">Number of top matches</param>
    /// <returns>Resource estimate description</returns>
    static member EstimatePatternMatcherResources(searchSpaceSize: int, topN: int) =
        QuantumPatternMatcher.estimateResources searchSpaceSize topN
    
    /// <summary>Solve quantum pattern matching problem (C# helper).</summary>
    /// <typeparam name="T">Configuration type</typeparam>
    /// <param name="problem">Pattern matching problem</param>
    /// <returns>Result with matching configurations or error message</returns>
    static member SolvePatternMatch<'T>(problem: QuantumPatternMatcher.PatternProblem<'T>) =
        QuantumPatternMatcher.solve problem
    
    // ============================================================================
    // QUANTUM ARITHMETIC BUILDER EXTENSIONS (Phase 2: QFT-Based)
    // ============================================================================
    
    /// <summary>Perform quantum addition: a + b (C# helper).</summary>
    /// <param name="a">First operand</param>
    /// <param name="b">Second operand</param>
    /// <param name="qubits">Number of qubits (optional, defaults to 8)</param>
    /// <returns>Arithmetic operation to execute</returns>
    static member Add(a: int, b: int, ?qubits: int) =
        let q = defaultArg qubits 8
        QuantumArithmeticOps.add a b q
    
    /// <summary>Perform modular addition: (a + b) mod N (C# helper).</summary>
    /// <param name="a">First operand</param>
    /// <param name="b">Second operand</param>
    /// <param name="modulus">Modulus N</param>
    /// <param name="qubits">Number of qubits (optional, defaults to 8)</param>
    /// <returns>Arithmetic operation to execute</returns>
    static member ModularAdd(a: int, b: int, modulus: int, ?qubits: int) =
        let q = defaultArg qubits 8
        QuantumArithmeticOps.modularAdd a b modulus q
    
    /// <summary>Perform modular multiplication: (a * b) mod N (C# helper).</summary>
    /// <param name="a">First operand</param>
    /// <param name="b">Second operand</param>
    /// <param name="modulus">Modulus N</param>
    /// <param name="qubits">Number of qubits (optional, defaults to 8)</param>
    /// <returns>Arithmetic operation to execute</returns>
    static member ModularMultiply(a: int, b: int, modulus: int, ?qubits: int) =
        let q = defaultArg qubits 8
        QuantumArithmeticOps.modularMultiply a b modulus q
    
    /// <summary>Perform modular exponentiation: (base^exponent) mod N (C# helper).</summary>
    /// <param name="baseValue">Base value</param>
    /// <param name="exponent">Exponent</param>
    /// <param name="modulus">Modulus N</param>
    /// <param name="qubits">Number of qubits (optional, defaults to 8)</param>
    /// <returns>Arithmetic operation to execute</returns>
    static member ModularExponentiate(baseValue: int, exponent: int, modulus: int, ?qubits: int) =
        let q = defaultArg qubits 8
        QuantumArithmeticOps.modularExponentiate baseValue exponent modulus q
    
    /// <summary>Execute quantum arithmetic operation (C# helper).</summary>
    /// <param name="operation">Arithmetic operation to execute</param>
    /// <returns>Result with computed value or error message</returns>
    static member ExecuteArithmetic(operation: QuantumArithmeticOps.ArithmeticOperation) =
        QuantumArithmeticOps.execute operation

    // ==========================================================================
    // QPE EXACTNESS HELPERS (C#-friendly discriminated union factories)
    // ==========================================================================

    /// <summary>Create QPE exactness = Exact (C# helper).</summary>
    static member QpeExactnessExact() : Algorithms.QPE.Exactness =
        Algorithms.QPE.Exactness.Exact

    /// <summary>Create QPE exactness = Approximate(epsilon) (C# helper).</summary>
    /// <param name="epsilon">Phase/gate cutoff threshold</param>
    static member QpeExactnessApproximate(epsilon: float) : Algorithms.QPE.Exactness =
        Algorithms.QPE.Exactness.Approximate epsilon

    // ============================================================================
    // QUANTUM PERIOD FINDER BUILDER EXTENSIONS (Phase 2: Shor's Algorithm)
    // ============================================================================
    
    /// <summary>Factor an integer using Shor's algorithm (C# helper).</summary>
    /// <param name="number">Integer to factor</param>
    /// <param name="precision">Number of precision qubits (optional, defaults to 8)</param>
    /// <returns>Period finder problem to solve</returns>
    static member FactorInteger(number: int, ?precision: int) =
        let p = defaultArg precision 8
        QuantumPeriodFinder.factorInteger number p

    /// <summary>Factor an integer using Shor's algorithm with explicit exactness (C# helper).</summary>
    static member FactorInteger(number: int, precision: int, exactness: Algorithms.QPE.Exactness) =
        QuantumPeriodFinder.factorInteger number precision
        |> Result.map (fun problem -> { problem with Exactness = exactness })
    
    /// <summary>Factor an integer using a specific base value (C# helper).</summary>
    /// <param name="number">Integer to factor</param>
    /// <param name="baseValue">Base for period finding</param>
    /// <param name="precision">Number of precision qubits (optional, defaults to 8)</param>
    /// <returns>Period finder problem to solve</returns>
    static member FactorIntegerWithBase(number: int, baseValue: int, ?precision: int) =
        let p = defaultArg precision 8
        QuantumPeriodFinder.factorIntegerWithBase number baseValue p

    /// <summary>Factor an integer with base and explicit exactness (C# helper).</summary>
    static member FactorIntegerWithBase(number: int, baseValue: int, precision: int, exactness: Algorithms.QPE.Exactness) =
        QuantumPeriodFinder.factorIntegerWithBase number baseValue precision
        |> Result.map (fun problem -> { problem with Exactness = exactness })
    
    /// <summary>Break RSA encryption by factoring the modulus N (C# helper).</summary>
    /// <param name="rsaModulus">RSA modulus N = p * q</param>
    /// <returns>Period finder problem to solve</returns>
    static member BreakRSA(rsaModulus: int) =
        QuantumPeriodFinder.breakRSA rsaModulus

    /// <summary>Break RSA encryption with explicit exactness (C# helper).</summary>
    static member BreakRSA(rsaModulus: int, exactness: Algorithms.QPE.Exactness) =
        QuantumPeriodFinder.breakRSA rsaModulus
        |> Result.map (fun problem -> { problem with Exactness = exactness })
    
    /// <summary>Execute period finder problem to find factors (C# helper).</summary>
    /// <param name="problem">Period finder problem</param>
    /// <returns>Result with factors or error message</returns>
    static member ExecutePeriodFinder(problem: QuantumPeriodFinder.PeriodFinderProblem) =
        QuantumPeriodFinder.solve problem

    /// <summary>Execute period finder problem but override its exactness (C# helper).</summary>
    static member ExecutePeriodFinder(problem: QuantumPeriodFinder.PeriodFinderProblem, exactness: Algorithms.QPE.Exactness) =
        QuantumPeriodFinder.solve { problem with Exactness = exactness }
    
    // ============================================================================
    // QUANTUM PHASE ESTIMATOR BUILDER EXTENSIONS (Phase 2: QPE)
    // ============================================================================
    
    /// <summary>Estimate eigenphase of T gate (e^(iπ/4)) using QPE (C# helper).</summary>
    /// <param name="precision">Number of precision qubits (optional, defaults to 8)</param>
    /// <returns>Phase estimator problem to solve</returns>
    static member EstimateTGate(?precision: int) =
        let p = defaultArg precision 8
        QuantumPhaseEstimator.estimateTGate p None

    /// <summary>Estimate eigenphase of T gate with explicit exactness (C# helper).</summary>
    static member EstimateTGate(precision: int, exactness: Algorithms.QPE.Exactness) =
        QuantumPhaseEstimator.estimateTGate precision None
        |> Result.map (fun problem -> { problem with Exactness = exactness })
    
    /// <summary>Estimate eigenphase of S gate (e^(iπ/2)) using QPE (C# helper).</summary>
    /// <param name="precision">Number of precision qubits (optional, defaults to 8)</param>
    /// <returns>Phase estimator problem to solve</returns>
    static member EstimateSGate(?precision: int) =
        let p = defaultArg precision 8
        QuantumPhaseEstimator.estimateSGate p None

    /// <summary>Estimate eigenphase of S gate with explicit exactness (C# helper).</summary>
    static member EstimateSGate(precision: int, exactness: Algorithms.QPE.Exactness) =
        QuantumPhaseEstimator.estimateSGate precision None
        |> Result.map (fun problem -> { problem with Exactness = exactness })
    
    /// <summary>Estimate eigenphase of Phase gate P(θ) = e^(iθ) using QPE (C# helper).</summary>
    /// <param name="theta">Phase angle in radians</param>
    /// <param name="precision">Number of precision qubits (optional, defaults to 8)</param>
    /// <returns>Phase estimator problem to solve</returns>
    static member EstimatePhaseGate(theta: float, ?precision: int) =
        let p = defaultArg precision 8
        QuantumPhaseEstimator.estimatePhaseGate theta p None

    /// <summary>Estimate eigenphase of Phase gate with explicit exactness (C# helper).</summary>
    static member EstimatePhaseGate(theta: float, precision: int, exactness: Algorithms.QPE.Exactness) =
        QuantumPhaseEstimator.estimatePhaseGate theta precision None
        |> Result.map (fun problem -> { problem with Exactness = exactness })
    
    /// <summary>Estimate eigenphase of Rotation-Z gate Rz(θ) = e^(-iθ/2)|0⟩ + e^(iθ/2)|1⟩ (C# helper).</summary>
    /// <param name="theta">Rotation angle in radians</param>
    /// <param name="precision">Number of precision qubits (optional, defaults to 8)</param>
    /// <returns>Phase estimator problem to solve</returns>
    static member EstimateRotationZ(theta: float, ?precision: int) =
        let p = defaultArg precision 8
        QuantumPhaseEstimator.estimateRotationZ theta p None

    /// <summary>Estimate eigenphase of Rotation-Z gate with explicit exactness (C# helper).</summary>
    static member EstimateRotationZ(theta: float, precision: int, exactness: Algorithms.QPE.Exactness) =
        QuantumPhaseEstimator.estimateRotationZ theta precision None
        |> Result.map (fun problem -> { problem with Exactness = exactness })
    
    /// <summary>Execute phase estimator problem to find eigenvalue (C# helper).</summary>
    /// <param name="problem">Phase estimator problem</param>
    /// <returns>Result with phase and eigenvalue or error message</returns>
    static member ExecutePhaseEstimator(problem: QuantumPhaseEstimator.PhaseEstimatorProblem) =
        QuantumPhaseEstimator.estimate problem

    /// <summary>Execute phase estimator problem but override its exactness (C# helper).</summary>
    static member ExecutePhaseEstimator(problem: QuantumPhaseEstimator.PhaseEstimatorProblem, exactness: Algorithms.QPE.Exactness) =
        QuantumPhaseEstimator.estimate { problem with Exactness = exactness }

// ============================================================================
// QUANTUM BACKEND EXTENSIONS - Task-based Async for C#
// ============================================================================

/// <summary>
/// C# extensions for IQuantumBackend to enable Task-based async/await.
/// </summary>
/// <remarks>
/// These extensions convert F# Async to C# Task for idiomatic async/await usage.
/// 
/// Example (C#):
/// <code>
/// var backend = BackendAbstraction.CreateFromWorkspace(workspace, "ionq.simulator");
/// var result = await backend.ExecuteAsyncTask(circuit, 1000);
/// 
/// if (result.IsOk())
/// {
///     var execResult = result.GetOkValue();
///     Console.WriteLine($"Shots: {execResult.NumShots}");
/// }
/// else
/// {
///     Console.WriteLine($"Error: {result.GetErrorValue()}");
/// }
/// </code>
/// </remarks>
[<Extension>]
module QuantumBackendCSharpExtensions =
    
    /// <summary>
    /// Execute a quantum circuit asynchronously using C# Task (enables async/await).
    /// </summary>
    /// <param name="backend">The quantum backend</param>
    /// <param name="circuit">Circuit to execute (ICircuit interface)</param>
    /// <param name="numShots">Number of measurement shots</param>
    /// <returns>Task with execution result or error message</returns>
    /// <remarks>
    /// This method converts F# Async to C# Task for idiomatic async/await usage.
    /// For F# code, use ExecuteAsync directly.
    /// 
    /// The returned Result can be checked using extension methods:
    /// - result.IsOk() - Returns true if execution succeeded
    /// - result.IsError() - Returns true if execution failed
    /// - result.GetOkValue() - Gets execution result (throws if error)
    /// - result.GetErrorValue() - Gets error message (throws if ok)
    /// - result.GetOkValueOrDefault(defaultValue) - Gets value or default
    /// </remarks>
    /// <example>
    /// C# async/await usage:
    /// <code>
    /// var backend = BackendAbstraction.CreateLocalBackend();
    /// var result = await backend.ExecuteAsyncTask(circuit, 1000);
    /// 
    /// if (result.IsOk())
    /// {
    ///     var execResult = result.GetOkValue();
    ///     Console.WriteLine($"Backend: {execResult.BackendName}");
    ///     Console.WriteLine($"Shots: {execResult.NumShots}");
    ///     
    ///     // Process measurements (int[][])
    ///     foreach (var measurement in execResult.Measurements)
    ///     {
    ///         Console.WriteLine(string.Join("", measurement));
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <summary>
    /// Execute circuit and get quantum state (C# Task wrapper for ExecuteToState).
    /// </summary>
    /// <param name="backend">The quantum backend</param>
    /// <param name="circuit">The circuit to execute</param>
    /// <returns>Task with Result containing quantum state or error</returns>
    [<Extension>]
    let ExecuteToStateTask 
        (backend: IQuantumBackend) 
        (circuit: ICircuit) : Task<Result<QuantumState, QuantumError>> =
        async {
            return backend.ExecuteToState circuit
        } |> Async.StartAsTask
    
    /// <summary>
    /// Get backend name (C# property helper).
    /// </summary>
    /// <param name="backend">The quantum backend</param>
    /// <returns>Backend name (e.g., "Local Simulator", "IonQ Simulator")</returns>
    [<Extension>]
    let GetName (backend: IQuantumBackend) : string =
        backend.Name
    
    /// <summary>
    /// Check if backend supports a specific operation (C# helper).
    /// </summary>
    /// <param name="backend">The quantum backend</param>
    /// <param name="operation">The operation to check</param>
    /// <returns>True if supported, false otherwise</returns>
    [<Extension>]
    let CheckSupportsOperation (backend: IQuantumBackend) (operation: QuantumOperation) : bool =
        backend.SupportsOperation operation

// ============================================================================
// MODEL SERIALIZATION EXTENSIONS - Task-based Async for C#
// ============================================================================

/// <summary>
/// C# extensions for ModelSerialization to enable Task-based async/await.
/// </summary>
/// <remarks>
/// These extensions convert F# Async to C# Task for idiomatic async/await usage.
/// </remarks>
[<Extension>]
module ModelSerializationCSharpExtensions =
    open FSharp.Azure.Quantum.MachineLearning
    
    /// <summary>
    /// Save VQC model asynchronously using C# Task (enables async/await).
    /// </summary>
    [<Extension>]
    let SaveVQCModelTask
        (filePath: string)
        (parameters: float array)
        (finalLoss: float)
        (numQubits: int)
        (featureMapType: string)
        (featureMapDepth: int)
        (variationalFormType: string)
        (variationalFormDepth: int)
        (note: string option)
        : Task<Result<unit, string>> =
        async {
            let! result = ModelSerialization.saveVQCModelAsync 
                            filePath parameters finalLoss numQubits 
                            featureMapType featureMapDepth variationalFormType variationalFormDepth note
            return Result.mapError (fun (e: QuantumError) -> e.Message) result
        }
        |> Async.StartAsTask

// ============================================================================
// QUANTUM CHEMISTRY EXTENSIONS - Task-based Async for C#
// ============================================================================

/// <summary>
/// C# extensions for QuantumChemistry to enable Task-based async/await.
/// </summary>
[<Extension>]
module QuantumChemistryCSharpExtensions =
    open FSharp.Azure.Quantum.QuantumChemistry
    
    /// <summary>
    /// Load molecule from XYZ file asynchronously using C# Task.
    /// </summary>
    [<Extension>]
    let FromXYZTask (filePath: string) : Task<Result<Molecule, QuantumError>> =
        Molecule.fromXyzFileAsync filePath |> Async.StartAsTask
    
    /// <summary>
    /// Save molecule to XYZ file asynchronously using C# Task.
    /// </summary>
    [<Extension>]
    let SaveXYZTask (filePath: string) (molecule: Molecule) : Task<Result<unit, QuantumError>> =
        Molecule.saveToXyzFileAsync filePath molecule |> Async.StartAsTask
    
    /// <summary>
    /// Load molecule from FCIDump file asynchronously using C# Task.
    /// </summary>
    [<Extension>]
    let FromFCIDumpTask (filePath: string) : Task<Result<Molecule, QuantumError>> =
        Molecule.fromFciDumpFileAsync filePath |> Async.StartAsTask

// ============================================================================
// SVM MODEL SERIALIZATION EXTENSIONS - Task-based Async for C#
// ============================================================================

/// <summary>
/// C# extensions for SVMModelSerialization to enable Task-based async/await.
/// </summary>
[<Extension>]
module SVMModelSerializationCSharpExtensions =
    open FSharp.Azure.Quantum.MachineLearning
    
    /// <summary>
    /// Save SVM model asynchronously using C# Task.
    /// </summary>
    [<Extension>]
    let SaveSVMModelTask
        (filePath: string)
        (model: QuantumKernelSVM.SVMModel)
        (note: string option)
        : Task<Result<unit, string>> =
        async {
            let! result = SVMModelSerialization.saveSVMModelAsync filePath model note
            return Result.mapError (fun (e: QuantumError) -> e.Message) result
        }
        |> Async.StartAsTask
    
    /// <summary>
    /// Load SVM model asynchronously using C# Task.
    /// </summary>
    [<Extension>]
    let LoadSVMModelTask (filePath: string) : Task<Result<QuantumKernelSVM.SVMModel, string>> =
        async {
            let! result = SVMModelSerialization.loadSVMModelAsync filePath
            return Result.mapError (fun (e: QuantumError) -> e.Message) result
        }
        |> Async.StartAsTask
    
    /// <summary>
    /// Save multi-class SVM model asynchronously using C# Task.
    /// </summary>
    [<Extension>]
    let SaveMultiClassSVMModelTask
        (filePath: string)
        (model: MultiClassSVM.MultiClassModel)
        (note: string option)
        : Task<Result<unit, string>> =
        async {
            let! result = SVMModelSerialization.saveMultiClassSVMModelAsync filePath model note
            return Result.mapError (fun (e: QuantumError) -> e.Message) result
        }
        |> Async.StartAsTask
    
    /// <summary>
    /// Load multi-class SVM model asynchronously using C# Task.
    /// </summary>
    [<Extension>]
    let LoadMultiClassSVMModelTask (filePath: string) : Task<Result<MultiClassSVM.MultiClassModel, string>> =
        async {
            let! result = SVMModelSerialization.loadMultiClassSVMModelAsync filePath
            return Result.mapError (fun (e: QuantumError) -> e.Message) result
        }
        |> Async.StartAsTask

// ============================================================================
// OPTION PRICING EXTENSIONS - Task-based Async for C#
// ============================================================================

/// <summary>
/// C# extensions for OptionPricing to enable Task-based async/await.
/// </summary>
/// <remarks>
/// These extensions convert F# Async to C# Task for idiomatic async/await usage.
/// 
/// Example usage (C#):
/// <code>
/// using FSharp.Azure.Quantum;
/// using static FSharp.Azure.Quantum.CSharpBuilders;
/// 
/// // Simple: Use default LocalBackend (quantum simulation)
/// var result = await OptionPricingExtensions.PriceEuropeanCallTask(100.0, 105.0, 0.05, 0.2, 1.0, null);
/// 
/// if (result.IsOk())
/// {
///     var price = result.GetOkValue();
///     Console.WriteLine($"Option Price: ${price.Price:F2}");
///     Console.WriteLine($"Confidence Interval: ±${price.ConfidenceInterval:F2}");
///     Console.WriteLine($"Quantum Speedup: {price.Speedup:F1}x");
///     Console.WriteLine($"Qubits Used: {price.QubitsUsed}");
/// }
/// 
/// // Advanced: Use IonQ cloud backend
/// var ionqBackend = BackendAbstraction.CreateIonQBackend(...);
/// var cloudResult = await OptionPricingExtensions.PriceEuropeanCallTask(100.0, 105.0, 0.05, 0.2, 1.0, ionqBackend);
/// </code>
/// </remarks>
[<Extension>]
module OptionPricingExtensions =
    
    /// <summary>
    /// Price European put option asynchronously using C# Task (enables async/await).
    /// </summary>
    /// <param name="spotPrice">Current price of underlying asset (S₀)</param>
    /// <param name="strikePrice">Strike price of the option (K)</param>
    /// <param name="riskFreeRate">Risk-free interest rate (annualized, r)</param>
    /// <param name="volatility">Volatility of underlying asset (annualized, σ)</param>
    /// <param name="timeToExpiry">Time to expiry in years (T)</param>
    /// <param name="backend">Quantum backend (REQUIRED - RULE1 compliance)</param>
    /// <returns>Task with option price result or error</returns>
    /// <remarks>
    /// **RULE1 COMPLIANCE**: Backend parameter is REQUIRED (not optional).
    /// 
    /// Payoff: max(K - S_T, 0)
    /// 
    /// Uses Quantum Monte Carlo for quadratic speedup over classical methods.
    /// </remarks>
    [<Extension>]
    let PriceEuropeanPutTask
        (spotPrice: float)
        (strikePrice: float)
        (riskFreeRate: float)
        (volatility: float)
        (timeToExpiry: float)
        (backend: Core.BackendAbstraction.IQuantumBackend)
        : Task<QuantumResult<OptionPricing.OptionPrice>> =
        
        OptionPricing.priceEuropeanPut spotPrice strikePrice riskFreeRate volatility timeToExpiry 6 5 1000 backend
        |> Async.StartAsTask
    
    /// <summary>
    /// Price Asian call option asynchronously using C# Task (enables async/await).
    /// </summary>
    /// <param name="spotPrice">Current price of underlying asset (S₀)</param>
    /// <param name="strikePrice">Strike price of the option (K)</param>
    /// <param name="riskFreeRate">Risk-free interest rate (annualized, r)</param>
    /// <param name="volatility">Volatility of underlying asset (annualized, σ)</param>
    /// <param name="timeToExpiry">Time to expiry in years (T)</param>
    /// <param name="timeSteps">Number of time steps for path averaging</param>
    /// <param name="backend">Quantum backend (REQUIRED - RULE1 compliance)</param>
    /// <returns>Task with option price result or error</returns>
    /// <remarks>
    /// **RULE1 COMPLIANCE**: Backend parameter is REQUIRED (not optional).
    /// 
    /// Payoff: max(Avg(S_t) - K, 0)
    /// 
    /// Asian options average the price over time, reducing volatility exposure.
    /// Uses Quantum Monte Carlo for quadratic speedup.
    /// </remarks>
    [<Extension>]
    let PriceAsianCallTask
        (spotPrice: float)
        (strikePrice: float)
        (riskFreeRate: float)
        (volatility: float)
        (timeToExpiry: float)
        (timeSteps: int)
        (backend: Core.BackendAbstraction.IQuantumBackend)
        : Task<QuantumResult<OptionPricing.OptionPrice>> =
        
        OptionPricing.priceAsianCall spotPrice strikePrice riskFreeRate volatility timeToExpiry timeSteps 6 5 1000 backend
        |> Async.StartAsTask
    
    /// <summary>
    /// Price Asian put option asynchronously using C# Task (enables async/await).
    /// </summary>
    /// <param name="spotPrice">Current price of underlying asset (S₀)</param>
    /// <param name="strikePrice">Strike price of the option (K)</param>
    /// <param name="riskFreeRate">Risk-free interest rate (annualized, r)</param>
    /// <param name="volatility">Volatility of underlying asset (annualized, σ)</param>
    /// <param name="timeToExpiry">Time to expiry in years (T)</param>
    /// <param name="timeSteps">Number of time steps for path averaging</param>
    /// <param name="backend">Quantum backend (REQUIRED - RULE1 compliance)</param>
    /// <returns>Task with option price result or error</returns>
    /// <remarks>
    /// **RULE1 COMPLIANCE**: Backend parameter is REQUIRED (not optional).
    /// 
    /// Payoff: max(K - Avg(S_t), 0)
    /// 
    /// Asian options average the price over time, reducing volatility exposure.
    /// Uses Quantum Monte Carlo for quadratic speedup.
    /// </remarks>
    [<Extension>]
    let PriceAsianPutTask
        (spotPrice: float)
        (strikePrice: float)
        (riskFreeRate: float)
        (volatility: float)
        (timeToExpiry: float)
        (timeSteps: int)
        (backend: Core.BackendAbstraction.IQuantumBackend)
        : Task<QuantumResult<OptionPricing.OptionPrice>> =
        
        OptionPricing.priceAsianPut spotPrice strikePrice riskFreeRate volatility timeToExpiry timeSteps 6 5 1000 backend
        |> Async.StartAsTask
