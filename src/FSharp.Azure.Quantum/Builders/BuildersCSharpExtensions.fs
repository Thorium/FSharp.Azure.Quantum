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
    
    /// <summary>Factor an integer using a specific base value (C# helper).</summary>
    /// <param name="number">Integer to factor</param>
    /// <param name="baseValue">Base for period finding</param>
    /// <param name="precision">Number of precision qubits (optional, defaults to 8)</param>
    /// <returns>Period finder problem to solve</returns>
    static member FactorIntegerWithBase(number: int, baseValue: int, ?precision: int) =
        let p = defaultArg precision 8
        QuantumPeriodFinder.factorIntegerWithBase number baseValue p
    
    /// <summary>Break RSA encryption by factoring the modulus N (C# helper).</summary>
    /// <param name="rsaModulus">RSA modulus N = p * q</param>
    /// <returns>Period finder problem to solve</returns>
    static member BreakRSA(rsaModulus: int) =
        QuantumPeriodFinder.breakRSA rsaModulus
    
    /// <summary>Execute period finder problem to find factors (C# helper).</summary>
    /// <param name="problem">Period finder problem</param>
    /// <returns>Result with factors or error message</returns>
    static member ExecutePeriodFinder(problem: QuantumPeriodFinder.PeriodFinderProblem) =
        QuantumPeriodFinder.solve problem
    
    // ============================================================================
    // QUANTUM PHASE ESTIMATOR BUILDER EXTENSIONS (Phase 2: QPE)
    // ============================================================================
    
    /// <summary>Estimate eigenphase of T gate (e^(iπ/4)) using QPE (C# helper).</summary>
    /// <param name="precision">Number of precision qubits (optional, defaults to 8)</param>
    /// <returns>Phase estimator problem to solve</returns>
    static member EstimateTGate(?precision: int) =
        let p = defaultArg precision 8
        QuantumPhaseEstimator.estimateTGate p
    
    /// <summary>Estimate eigenphase of S gate (e^(iπ/2)) using QPE (C# helper).</summary>
    /// <param name="precision">Number of precision qubits (optional, defaults to 8)</param>
    /// <returns>Phase estimator problem to solve</returns>
    static member EstimateSGate(?precision: int) =
        let p = defaultArg precision 8
        QuantumPhaseEstimator.estimateSGate p
    
    /// <summary>Estimate eigenphase of Phase gate P(θ) = e^(iθ) using QPE (C# helper).</summary>
    /// <param name="theta">Phase angle in radians</param>
    /// <param name="precision">Number of precision qubits (optional, defaults to 8)</param>
    /// <returns>Phase estimator problem to solve</returns>
    static member EstimatePhaseGate(theta: float, ?precision: int) =
        let p = defaultArg precision 8
        QuantumPhaseEstimator.estimatePhaseGate theta p
    
    /// <summary>Estimate eigenphase of Rotation-Z gate Rz(θ) = e^(-iθ/2)|0⟩ + e^(iθ/2)|1⟩ (C# helper).</summary>
    /// <param name="theta">Rotation angle in radians</param>
    /// <param name="precision">Number of precision qubits (optional, defaults to 8)</param>
    /// <returns>Phase estimator problem to solve</returns>
    static member EstimateRotationZ(theta: float, ?precision: int) =
        let p = defaultArg precision 8
        QuantumPhaseEstimator.estimateRotationZ theta p
    
    /// <summary>Execute phase estimator problem to find eigenvalue (C# helper).</summary>
    /// <param name="problem">Phase estimator problem</param>
    /// <returns>Result with phase and eigenvalue or error message</returns>
    static member ExecutePhaseEstimator(problem: QuantumPhaseEstimator.PhaseEstimatorProblem) =
        QuantumPhaseEstimator.estimate problem

