namespace FSharp.Azure.Quantum.GroverSearch

open System
open System.Numerics
open FSharp.Azure.Quantum.Core

/// Oracle Module for Grover's Search Algorithm
/// 
/// An oracle is a quantum operation that marks solution states by flipping their phase.
/// This module provides backend-agnostic oracle specifications that work with both
/// local simulation and Azure Quantum backends.
/// 
/// Design Philosophy:
/// - Pure functions (no mutable state)
/// - Backend-agnostic (works with Local, IonQ, Rigetti, Topological)
/// - Idiomatic F# (modules, not classes)
/// - Shared infrastructure for all Grover implementations
/// 
/// Usage:
/// ```fsharp
/// open FSharp.Azure.Quantum.GroverSearch.Oracle
/// let oracle = fromPredicate (fun x -> x = 5) 3
/// ```
/// 
/// ALL GROVER ORACLE CODE IN SINGLE FILE
module Oracle =
    
    open FSharp.Azure.Quantum.LocalSimulator
    
    // ============================================================================
    // TYPES - Pure data structures
    // ============================================================================
    
    /// Oracle specification - defines which states to mark as solutions
    /// 
    /// Oracles work by flipping the phase of solution states:
    /// |x⟩ → -|x⟩ if x is a solution, |x⟩ otherwise
    type OracleSpec =
        /// Mark states where predicate returns true
        | Predicate of (int -> bool)
        
        /// Mark specific solution indices
        | Solutions of int list
        
        /// Mark a single target value
        | SingleTarget of int
        
        /// Combine oracles with AND logic
        | And of OracleSpec * OracleSpec
        
        /// Combine oracles with OR logic
        | Or of OracleSpec * OracleSpec
        
        /// Negate oracle (mark non-solutions)
        | Not of OracleSpec
    
    /// Oracle result after compilation
    /// Contains both local and backend representations
    type CompiledOracle = {
        /// Oracle specification
        Spec: OracleSpec
        
        /// Number of qubits in search space
        NumQubits: int
        
        /// Local simulation function (for testing and small problems)
        LocalSimulation: StateVector.StateVector -> StateVector.StateVector
        
        /// Expected number of solutions (if known)
        ExpectedSolutions: int option
    }
    
    // ============================================================================
    // ORACLE EVALUATION - Pure functions
    // ============================================================================
    
    /// Evaluate if an index is a solution according to oracle spec
    /// Pure function - no side effects
    let rec isSolution (spec: OracleSpec) (index: int) : bool =
        match spec with
        | Predicate pred -> pred index
        | Solutions solList -> List.contains index solList
        | SingleTarget target -> index = target
        | And (spec1, spec2) -> isSolution spec1 index && isSolution spec2 index
        | Or (spec1, spec2) -> isSolution spec1 index || isSolution spec2 index
        | Not innerSpec -> not (isSolution innerSpec index)
    
    /// Count expected solutions for an oracle spec
    /// Returns None if count cannot be determined statically
    let rec countExpectedSolutions (spec: OracleSpec) (searchSpaceSize: int) : int option =
        match spec with
        | Predicate _ -> None  // Cannot statically count predicate results
        | Solutions solList -> Some (List.length solList)
        | SingleTarget _ -> Some 1
        | And (spec1, spec2) ->
            // AND: potentially fewer solutions than either spec alone
            // Conservative: return None unless we can compute exactly
            None
        | Or (spec1, spec2) ->
            // OR: potentially more solutions than either spec alone
            // For now, return None (would need set union logic)
            None
        | Not innerSpec ->
            // NOT: inverts the count
            match countExpectedSolutions innerSpec searchSpaceSize with
            | Some count -> Some (searchSpaceSize - count)
            | None -> None
    
    // ============================================================================
    // LOCAL SIMULATION - For testing and small problems
    // ============================================================================
    
    /// Apply oracle to quantum state (local simulation only)
    /// 
    /// Flips phase of solution states: |x⟩ → -|x⟩
    /// This is a pure function - creates new state, doesn't modify input
    let applyLocal (spec: OracleSpec) (state: StateVector.StateVector) : StateVector.StateVector =
        let dimension = StateVector.dimension state
        
        // Create new amplitude array with phase flips
        let newAmplitudes =
            [| 0 .. dimension - 1 |]
            |> Array.map (fun i ->
                let amp = StateVector.getAmplitude i state
                if isSolution spec i then
                    -amp  // Flip phase (multiply by -1)
                else
                    amp   // Keep original amplitude
            )
        
        StateVector.create newAmplitudes
    
    // ============================================================================
    // SAT SOLVER ORACLES - Boolean Satisfiability
    // ============================================================================
    
    /// Compile oracle specification into executable oracle
    /// 
    /// This is the main entry point for creating oracles.
    /// Returns CompiledOracle that works with both local and Azure backends.
    let compile (spec: OracleSpec) (numQubits: int) : QuantumResult<CompiledOracle> =
        if numQubits < 1 || numQubits > 20 then
            Error (QuantumError.ValidationError ("NumQubits", $"must be between 1 and 20, got {numQubits}"))
        else
            let searchSpaceSize = 1 <<< numQubits  // 2^numQubits
            
            Ok {
                Spec = spec
                NumQubits = numQubits
                LocalSimulation = applyLocal spec
                ExpectedSolutions = countExpectedSolutions spec searchSpaceSize
            }
    
    // ============================================================================
    // ORACLE BUILDERS - Convenient creation functions
    // ============================================================================
    
    /// Create oracle that marks a single target value
    let forValue (target: int) (numQubits: int) : QuantumResult<CompiledOracle> =
        let searchSpaceSize = 1 <<< numQubits
        
        if target < 0 then
            Error (QuantumError.ValidationError ("Target", $"must be non-negative, got {target}"))
        elif target >= searchSpaceSize then
            Error (QuantumError.ValidationError ("Target", $"{target} exceeds search space size {searchSpaceSize} for {numQubits} qubits"))
        else
            compile (SingleTarget target) numQubits
    
    /// Create oracle that marks multiple solution values
    let forValues (solutions: int list) (numQubits: int) : QuantumResult<CompiledOracle> =
        let searchSpaceSize = 1 <<< numQubits
        
        if solutions.IsEmpty then
            Error (QuantumError.ValidationError ("Solutions", "list cannot be empty"))
        else
            let invalidSolutions = solutions |> List.filter (fun s -> s < 0 || s >= searchSpaceSize)
            
            if not invalidSolutions.IsEmpty then
                Error (QuantumError.ValidationError ("Solutions", $"{invalidSolutions} are outside valid range [0, {searchSpaceSize - 1}] for {numQubits} qubits"))
            else
                compile (Solutions solutions) numQubits
    
    /// Create oracle from predicate function
    let fromPredicate (predicate: int -> bool) (numQubits: int) : QuantumResult<CompiledOracle> =
        compile (Predicate predicate) numQubits
    
    // ============================================================================
    // ORACLE COMBINATORS - Pure functional composition
    // ============================================================================
    
    /// Combine two oracle specs with AND logic
    let andSpec (spec1: OracleSpec) (spec2: OracleSpec) : OracleSpec =
        And (spec1, spec2)
    
    /// Combine two oracle specs with OR logic
    let orSpec (spec1: OracleSpec) (spec2: OracleSpec) : OracleSpec =
        Or (spec1, spec2)
    
    /// Negate oracle spec (mark non-solutions)
    let notSpec (spec: OracleSpec) : OracleSpec =
        Not spec
    
    /// Combine two compiled oracles with AND logic
    let andOracle (oracle1: CompiledOracle) (oracle2: CompiledOracle) : QuantumResult<CompiledOracle> =
        if oracle1.NumQubits <> oracle2.NumQubits then
            Error (QuantumError.ValidationError ("NumQubits", $"cannot combine oracles with different qubit counts ({oracle1.NumQubits} vs {oracle2.NumQubits})"))
        else
            let combinedSpec = andSpec oracle1.Spec oracle2.Spec
            compile combinedSpec oracle1.NumQubits
    
    /// Combine two compiled oracles with OR logic
    let orOracle (oracle1: CompiledOracle) (oracle2: CompiledOracle) : QuantumResult<CompiledOracle> =
        if oracle1.NumQubits <> oracle2.NumQubits then
            Error (QuantumError.ValidationError ("NumQubits", $"cannot combine oracles with different qubit counts ({oracle1.NumQubits} vs {oracle2.NumQubits})"))
        else
            let combinedSpec = orSpec oracle1.Spec oracle2.Spec
            compile combinedSpec oracle1.NumQubits
    
    /// Negate compiled oracle
    let notOracle (oracle: CompiledOracle) : QuantumResult<CompiledOracle> =
        let negatedSpec = notSpec oracle.Spec
        compile negatedSpec oracle.NumQubits
    
    // ============================================================================
    // COMMON ORACLE PATTERNS - Reusable predicates
    // ============================================================================
    
    /// Oracle that marks even numbers
    let even (numQubits: int) : QuantumResult<CompiledOracle> =
        fromPredicate (fun x -> x % 2 = 0) numQubits
    
    /// Oracle that marks odd numbers
    let odd (numQubits: int) : QuantumResult<CompiledOracle> =
        fromPredicate (fun x -> x % 2 = 1) numQubits
    
    /// Oracle that marks numbers divisible by n
    let divisibleBy (n: int) (numQubits: int) : QuantumResult<CompiledOracle> =
        if n = 0 then
            Error (QuantumError.ValidationError ("Divisor", "cannot be zero"))
        else
            fromPredicate (fun x -> x % n = 0) numQubits
    
    /// Oracle that marks numbers in range [min, max] (inclusive)
    let inRange (min: int) (max: int) (numQubits: int) : QuantumResult<CompiledOracle> =
        if min > max then
            Error (QuantumError.ValidationError ("Range", $"invalid range: min ({min}) > max ({max})"))
        else
            fromPredicate (fun x -> x >= min && x <= max) numQubits
    
    /// Oracle that marks numbers greater than threshold
    let greaterThan (threshold: int) (numQubits: int) : QuantumResult<CompiledOracle> =
        fromPredicate (fun x -> x > threshold) numQubits
    
    /// Oracle that marks numbers less than threshold
    let lessThan (threshold: int) (numQubits: int) : QuantumResult<CompiledOracle> =
        fromPredicate (fun x -> x < threshold) numQubits
    
    // ============================================================================
    // ORACLE VERIFICATION - Pure analysis functions
    // ============================================================================
    
    /// Verify oracle correctly marks solutions (local simulation only)
    /// Returns true if all expected solutions have flipped phase
    let verify (oracle: CompiledOracle) (expectedSolutions: int list) : bool =
        // Create uniform superposition
        let state = StateVector.init oracle.NumQubits
        
        // Apply Hadamard to all qubits to create uniform superposition
        let uniformState =
            [0 .. oracle.NumQubits - 1]
            |> List.fold (fun s qubitIdx -> Gates.applyH qubitIdx s) state
        
        // Apply oracle
        let markedState = oracle.LocalSimulation uniformState
        
        // Check that all expected solutions have negative phase
        let dimension = StateVector.dimension markedState
        
        expectedSolutions
        |> List.forall (fun sol ->
            if sol < dimension then
                let originalAmp = StateVector.getAmplitude sol uniformState
                let markedAmp = StateVector.getAmplitude sol markedState
                
                // Check phase flip: markedAmp ≈ -originalAmp
                let diff = Complex.Abs(markedAmp + originalAmp)
                diff < 1e-10  // Tolerance for floating-point comparison
            else
                false  // Solution index out of range
        )
    
    /// Count actual number of solutions marked by oracle
    /// Uses local simulation - expensive for large search spaces
    let countSolutions (oracle: CompiledOracle) : int =
        let searchSpaceSize = 1 <<< oracle.NumQubits
        
        [0 .. searchSpaceSize - 1]
        |> List.filter (fun i -> isSolution oracle.Spec i)
        |> List.length
    
    /// List all solutions marked by oracle
    /// Uses local evaluation - expensive for large search spaces
    let listSolutions (oracle: CompiledOracle) : int list =
        let searchSpaceSize = 1 <<< oracle.NumQubits
        
        [0 .. searchSpaceSize - 1]
        |> List.filter (fun i -> isSolution oracle.Spec i)
    
    // ============================================================================
    // ORACLE EXAMPLES - For documentation and testing
    // ============================================================================
    
    module Examples =
        
        /// Example: Search for specific value in 4-qubit space (0-15)
        let searchForValue (target: int) : QuantumResult<CompiledOracle> =
            forValue target 4
        
        /// Example: Search for multiple specific values
        let searchForMultiple (solutions: int list) : QuantumResult<CompiledOracle> =
            forValues solutions 4
        
        /// Example: Search for even numbers in 4-qubit space
        let searchEven : QuantumResult<CompiledOracle> =
            even 4
        
        /// Example: Search for numbers divisible by 3
        let searchDivisibleBy3 : QuantumResult<CompiledOracle> =
            divisibleBy 3 4
        
        /// Example: Search for numbers in range 5-10
        let searchRange5to10 : QuantumResult<CompiledOracle> =
            inRange 5 10 4
        
        /// Example: Complex query - even AND in range 4-12
        let searchEvenInRange : QuantumResult<CompiledOracle> =
            match even 4, inRange 4 12 4 with
            | Ok evenOracle, Ok rangeOracle -> andOracle evenOracle rangeOracle
            | Error err, _ -> Error err
            | _, Error err -> Error err
    
    // ============================================================================
    // SAT SOLVER ORACLES - Boolean Satisfiability
    // ============================================================================
    
    /// Literal in a SAT clause
    /// Represents either a variable or its negation
    type SatLiteral = {
        /// Variable index (0-based)
        VariableIndex: int
        
        /// True if variable should be negated
        IsNegated: bool
    }
    
    /// Clause in CNF (Conjunctive Normal Form)
    /// A clause is satisfied if ANY literal is true (OR of literals)
    type SatClause = {
        /// List of literals in the clause
        Literals: SatLiteral list
    }
    
    /// SAT formula in CNF
    /// Formula is satisfied if ALL clauses are true (AND of clauses)
    type SatFormula = {
        /// Number of variables in the formula
        NumVariables: int
        
        /// List of clauses (AND of ORs)
        Clauses: SatClause list
    }
    
    /// Helper: Create a literal
    let literal (varIndex: int) (isNegated: bool) : SatLiteral =
        { VariableIndex = varIndex; IsNegated = isNegated }
    
    /// Helper: Create a positive literal (variable)
    let var (varIndex: int) : SatLiteral =
        literal varIndex false
    
    /// Helper: Create a negative literal (NOT variable)
    let notVar (varIndex: int) : SatLiteral =
        literal varIndex true
    
    /// Helper: Create a clause from literals
    let clause (literals: SatLiteral list) : SatClause =
        { Literals = literals }
    
    /// Evaluate a literal given an assignment
    /// Assignment is encoded as an integer where bit i = value of variable i
    let private evaluateLiteral (assignment: int) (lit: SatLiteral) : bool =
        // Extract bit value for this variable
        let bitValue = (assignment >>> lit.VariableIndex) &&& 1 = 1
        
        // Apply negation if needed
        if lit.IsNegated then not bitValue else bitValue
    
    /// Evaluate a clause given an assignment
    /// Clause is satisfied if ANY literal is true
    let private evaluateClause (assignment: int) (clause: SatClause) : bool =
        clause.Literals
        |> List.exists (evaluateLiteral assignment)
    
    /// Evaluate a SAT formula given an assignment
    /// Formula is satisfied if ALL clauses are true
    let private evaluateFormula (assignment: int) (formula: SatFormula) : bool =
        formula.Clauses
        |> List.forall (evaluateClause assignment)
    
    /// Validate a SAT formula
    let private validateFormula (formula: SatFormula) : QuantumResult<unit> =
        if formula.NumVariables < 1 then
            Error (QuantumError.ValidationError ("NumVariables", $"must be at least 1, got {formula.NumVariables}"))
        elif formula.NumVariables > 20 then
            Error (QuantumError.ValidationError ("NumVariables", $"too large ({formula.NumVariables}), maximum is 20"))
        elif formula.Clauses.IsEmpty then
            Error (QuantumError.ValidationError ("Clauses", "formula must have at least one clause"))
        else
            // Validate each literal references a valid variable
            let invalidLiterals =
                formula.Clauses
                |> List.collect (fun c -> c.Literals)
                |> List.filter (fun lit -> 
                    lit.VariableIndex < 0 || lit.VariableIndex >= formula.NumVariables)
            
            if not invalidLiterals.IsEmpty then
                let badIndices = invalidLiterals |> List.map (fun lit -> lit.VariableIndex) |> List.distinct
                Error (QuantumError.ValidationError ("Literals", $"variable indices {badIndices} are out of range [0, {formula.NumVariables - 1}]"))
            else
                Ok ()
    
    /// Create oracle for SAT formula
    /// 
    /// This oracle marks assignments that satisfy the given SAT formula.
    /// The formula must be in CNF (Conjunctive Normal Form): AND of ORs.
    /// 
    /// Example: (x0 OR NOT x1) AND (x1 OR x2)
    /// ```fsharp
    /// let formula = {
    ///     NumVariables = 3
    ///     Clauses = [
    ///         clause [var 0; notVar 1]  // x0 OR NOT x1
    ///         clause [var 1; var 2]      // x1 OR x2
    ///     ]
    /// }
    /// let satOracle = Oracle.satOracle formula
    /// ```
    let satOracle (formula: SatFormula) : QuantumResult<CompiledOracle> =
        match validateFormula formula with
        | Error err -> Error err
        | Ok () ->
            // Create predicate that evaluates the formula
            let predicate = fun assignment -> evaluateFormula assignment formula
            
            // Compile oracle with the predicate
            fromPredicate predicate formula.NumVariables
    
    // ============================================================================
    // MAX-SAT ORACLE - Maximize Satisfied Clauses
    // ============================================================================
    
    /// <summary>
    /// Configuration for Max-SAT problem.
    /// </summary>
    /// <remarks>
    /// Max-SAT is the optimization version of SAT:
    /// Instead of "find ANY satisfying assignment", find assignment that
    /// satisfies the MAXIMUM number of clauses.
    /// 
    /// **Applications:**
    /// - AI planning: Maximize satisfied constraints
    /// - Resource allocation: Optimize assignments under constraints
    /// - Approximation algorithms: Find near-optimal solutions
    /// 
    /// **Example:**
    /// Formula with 4 clauses, min 3 satisfied
    /// Finds assignments that satisfy at least 3 out of 4 clauses
    /// </remarks>
    type MaxSatConfig = {
        /// SAT formula (all clauses)
        Formula: SatFormula
        
        /// Minimum number of clauses to satisfy
        MinClausesSatisfied: int
    }
    
    /// <summary>
    /// Count how many clauses are satisfied by an assignment.
    /// </summary>
    /// <param name="assignment">Variable assignment (bitstring)</param>
    /// <param name="formula">SAT formula</param>
    /// <returns>Number of satisfied clauses</returns>
    let private countSatisfiedClauses (assignment: int) (formula: SatFormula) : int =
        formula.Clauses
        |> List.filter (evaluateClause assignment)
        |> List.length
    
    /// <summary>
    /// Check if assignment satisfies minimum number of clauses.
    /// </summary>
    /// <param name="config">Max-SAT configuration</param>
    /// <param name="assignment">Variable assignment</param>
    /// <returns>True if at least minClauses satisfied</returns>
    let private satisfiesMinClauses (config: MaxSatConfig) (assignment: int) : bool =
        let satisfied = countSatisfiedClauses assignment config.Formula
        satisfied >= config.MinClausesSatisfied
    
    /// <summary>
    /// Validate Max-SAT configuration.
    /// </summary>
    let private validateMaxSat (config: MaxSatConfig) : QuantumResult<unit> =
        match validateFormula config.Formula with
        | Error err -> Error err
        | Ok () ->
            if config.MinClausesSatisfied < 1 then
                Error (QuantumError.ValidationError ("MinClausesSatisfied", $"must be at least 1, got {config.MinClausesSatisfied}"))
            elif config.MinClausesSatisfied > config.Formula.Clauses.Length then
                Error (QuantumError.ValidationError ("MinClausesSatisfied", 
                    $"cannot exceed number of clauses ({config.Formula.Clauses.Length})"))
            else
                Ok ()
    
    /// <summary>
    /// Create oracle for Max-SAT problem.
    /// </summary>
    /// <param name="config">Max-SAT configuration</param>
    /// <returns>Oracle that marks assignments satisfying at least k clauses</returns>
    /// <remarks>
    /// **Usage Pattern:**
    /// 1. Start with k = num_clauses (try to satisfy ALL)
    /// 2. If no solution, decrease k iteratively
    /// 3. Find maximum k where solution exists
    /// 
    /// **Example:**
    /// ```fsharp
    /// let formula = {
    ///     NumVariables = 3
    ///     Clauses = [
    ///         clause [var 0; var 1]
    ///         clause [notVar 0; var 2]
    ///         clause [notVar 1; notVar 2]
    ///         clause [var 0; notVar 1]
    ///     ]
    /// }
    /// let config = { Formula = formula; MinClausesSatisfied = 3 }
    /// let oracle = maxSatOracle config  // Find assignments satisfying ≥3 clauses
    /// ```
    /// </remarks>
    let maxSatOracle (config: MaxSatConfig) : QuantumResult<CompiledOracle> =
        match validateMaxSat config with
        | Error err -> Error err
        | Ok () ->
            let numQubits = config.Formula.NumVariables
            let predicate = fun assignment -> satisfiesMinClauses config assignment
            fromPredicate predicate numQubits
    
    // ============================================================================
    // GRAPH COLORING ORACLES - For gate-based quantum computing
    // ============================================================================
    //
    // This complements the existing GraphOptimization module (which uses QUBO
    // for quantum annealing). This Grover-based approach uses gate-based quantum
    // computers (IonQ, Rigetti, IBM) instead of annealers (D-Wave).
    //
    // Encoding: For n vertices and k colors, we use n*ceil(log2(k)) qubits
    // Each vertex gets ceil(log2(k)) qubits to encode its color in binary
    
    /// Graph represented as adjacency list
    /// Vertices are numbered 0, 1, 2, ...
    type GraphColoringGraph = {
        /// Number of vertices in the graph
        NumVertices: int
        
        /// List of edges (undirected): (vertex1, vertex2)
        Edges: (int * int) list
    }
    
    /// Graph coloring problem configuration
    type GraphColoringConfig = {
        /// The graph to color
        Graph: GraphColoringGraph
        
        /// Number of colors available
        NumColors: int
    }
    
    /// Helper: Create a simple graph from edge list
    let graph (numVertices: int) (edges: (int * int) list) : GraphColoringGraph =
        { NumVertices = numVertices; Edges = edges }
    
    /// Helper: Calculate number of qubits needed per vertex
    /// For k colors, we need ceil(log2(k)) qubits
    let private qubitsPerVertex (numColors: int) : int =
        if numColors <= 1 then 1
        else
            let log2 = System.Math.Log(float numColors) / System.Math.Log(2.0)
            int (System.Math.Ceiling(log2))
    
    /// Helper: Extract color assignment for a vertex from bit pattern
    /// For vertex v with c qubits per vertex, extract c bits starting at position v*c
    let private extractColor (assignment: int) (vertexIndex: int) (qubitsPerVert: int) : int =
        // Extract qubitsPerVert bits starting at position vertexIndex * qubitsPerVert
        let shift = vertexIndex * qubitsPerVert
        let mask = (1 <<< qubitsPerVert) - 1
        (assignment >>> shift) &&& mask
    
    /// Helper: Check if a coloring satisfies all edge constraints
    /// Two adjacent vertices must have different colors
    let private isValidColoring (config: GraphColoringConfig) (assignment: int) : bool =
        let qubitsPerVert = qubitsPerVertex config.NumColors
        
        config.Graph.Edges
        |> List.forall (fun (u, v) ->
            let colorU = extractColor assignment u qubitsPerVert
            let colorV = extractColor assignment v qubitsPerVert
            
            // Both colors must be in valid range AND different
            colorU < config.NumColors && 
            colorV < config.NumColors &&
            colorU <> colorV
        )
    
    /// Validate graph coloring configuration
    let private validateGraphColoring (config: GraphColoringConfig) : QuantumResult<unit> =
        if config.Graph.NumVertices < 1 then
            Error (QuantumError.ValidationError ("NumVertices", $"must be at least 1, got {config.Graph.NumVertices}"))
        elif config.Graph.NumVertices > 10 then
            Error (QuantumError.ValidationError ("NumVertices", $"too large ({config.Graph.NumVertices}), maximum is 10 for Grover"))
        elif config.NumColors < 2 then
            Error (QuantumError.ValidationError ("NumColors", $"must be at least 2, got {config.NumColors}"))
        elif config.NumColors > 8 then
            Error (QuantumError.ValidationError ("NumColors", $"too large ({config.NumColors}), maximum is 8"))
        else
            // Validate edges reference valid vertices
            let invalidEdges =
                config.Graph.Edges
                |> List.filter (fun (u, v) ->
                    u < 0 || u >= config.Graph.NumVertices ||
                    v < 0 || v >= config.Graph.NumVertices)
            
            if not invalidEdges.IsEmpty then
                Error (QuantumError.ValidationError ("Edges", $"edges {invalidEdges} reference invalid vertices (must be 0 to {config.Graph.NumVertices - 1})"))
            else
                Ok ()
    
    /// Create oracle for graph coloring problem
    /// 
    /// This oracle marks valid graph colorings where no two adjacent vertices
    /// have the same color. Uses binary encoding for colors.
    /// 
    /// Encoding: n vertices, k colors → n * ceil(log2(k)) qubits
    /// 
    /// Example: 3 vertices, 3 colors (needs 2 qubits per vertex = 6 qubits total)
    /// Assignment 0b00_01_10 = [v0:color0, v1:color1, v2:color2]
    /// 
    /// ```fsharp
    /// // Triangle graph (3 vertices, all connected)
    /// let triangleGraph = graph 3 [(0, 1); (1, 2); (2, 0)]
    /// let config = { Graph = triangleGraph; NumColors = 3 }
    /// let oracle = graphColoringOracle config
    /// ```
    /// 
    /// This will find valid 3-colorings of a triangle graph.
    /// Expected solutions: Any coloring where all 3 vertices have different colors.
    let graphColoringOracle (config: GraphColoringConfig) : QuantumResult<CompiledOracle> =
        match validateGraphColoring config with
        | Error err -> Error err
        | Ok () ->
            // Calculate number of qubits needed
            let qubitsPerVert = qubitsPerVertex config.NumColors
            let numQubits = config.Graph.NumVertices * qubitsPerVert
            
            // Validate qubit count is reasonable for Grover
            if numQubits > 20 then
                Error (QuantumError.ValidationError ("NumQubits", 
                    $"resulting qubit count ({numQubits}) exceeds Grover limit (20). " +
                    $"Try fewer vertices ({config.Graph.NumVertices}) or colors ({config.NumColors})"))
            else
                // Create predicate that checks valid colorings
                let predicate = fun assignment -> isValidColoring config assignment
                
                // Compile oracle with the predicate
                fromPredicate predicate numQubits
    
    // ============================================================================
    // CLIQUE DETECTION ORACLE - Find Fully Connected Subgraphs
    // ============================================================================
    
    /// <summary>
    /// Configuration for clique detection problem.
    /// </summary>
    /// <remarks>
    /// A clique is a subset of vertices where every pair is connected.
    /// 
    /// **Applications:**
    /// - Social networks: Finding tight-knit communities
    /// - Bioinformatics: Protein interaction clusters
    /// - Pattern recognition: Finding complete patterns in data
    /// 
    /// **Example (Triangle Clique):**
    /// Graph with edges: (0,1), (1,2), (2,0)
    /// Clique of size 3: {0, 1, 2} (all vertices connected)
    /// 
    /// **Encoding:** For n vertices, k-clique needs n qubits
    /// Each qubit i indicates if vertex i is in the clique
    /// Assignment 0b0111 = vertices {0, 1, 2} selected
    /// </remarks>
    type CliqueConfig = {
        /// The graph to search
        Graph: GraphColoringGraph
        
        /// Size of clique to find (k)
        CliqueSize: int
    }
    
    /// <summary>
    /// Check if a vertex subset forms a clique.
    /// </summary>
    /// <param name="config">Clique configuration</param>
    /// <param name="assignment">Bitstring encoding vertex selection</param>
    /// <returns>True if selected vertices form a clique</returns>
    /// <remarks>
    /// Algorithm:
    /// 1. Extract selected vertices from bitstring
    /// 2. Check exactly k vertices selected
    /// 3. For each pair, verify edge exists in graph
    /// </remarks>
    let private isClique (config: CliqueConfig) (assignment: int) : bool =
        // Extract selected vertices from bitstring
        let selectedVertices =
            [| for i in 0 .. config.Graph.NumVertices - 1 do
                if (assignment >>> i) &&& 1 = 1 then
                    yield i
            |]
        
        // Check exactly k vertices selected
        if selectedVertices.Length <> config.CliqueSize then
            false
        else
            // Create edge set for fast lookup
            let edgeSet = 
                config.Graph.Edges 
                |> List.collect (fun (u, v) -> [(u, v); (v, u)])  // Bidirectional
                |> Set.ofList
            
            // Check all pairs are connected
            let allPairsConnected =
                [| for i in 0 .. selectedVertices.Length - 1 do
                    for j in i + 1 .. selectedVertices.Length - 1 do
                        let u = selectedVertices[i]
                        let v = selectedVertices[j]
                        yield edgeSet.Contains((u, v))
                |]
                |> Array.forall id
            
            allPairsConnected
    
    /// <summary>
    /// Validate clique detection configuration.
    /// </summary>
    let private validateCliqueConfig (config: CliqueConfig) : QuantumResult<unit> =
        if config.Graph.NumVertices < 1 then
            Error (QuantumError.ValidationError ("NumVertices", $"must be at least 1, got {config.Graph.NumVertices}"))
        elif config.Graph.NumVertices > 20 then
            Error (QuantumError.ValidationError ("NumVertices", $"too large ({config.Graph.NumVertices}), maximum is 20 for Grover"))
        elif config.CliqueSize < 2 then
            Error (QuantumError.ValidationError ("CliqueSize", $"must be at least 2, got {config.CliqueSize}"))
        elif config.CliqueSize > config.Graph.NumVertices then
            Error (QuantumError.ValidationError ("CliqueSize", $"cannot be larger than number of vertices ({config.Graph.NumVertices})"))
        else
            Ok ()
    
    /// <summary>
    /// Create oracle for clique detection.
    /// </summary>
    /// <param name="config">Clique configuration</param>
    /// <returns>Oracle that marks k-cliques</returns>
    /// <remarks>
    /// **Encoding:** n vertices → n qubits (binary subset selection)
    /// Bit i = 1 means vertex i is in the clique
    /// 
    /// **Example:**
    /// ```fsharp
    /// // Find triangles (3-cliques) in a graph
    /// let graph = graph 5 [(0,1); (1,2); (2,0); (0,3); (3,4)]
    /// let config = { Graph = graph; CliqueSize = 3 }
    /// let oracle = cliqueOracle config
    /// ```
    /// 
    /// This finds all triangles in the graph.
    /// Expected solutions include: {0, 1, 2} (if all connected)
    /// </remarks>
    let cliqueOracle (config: CliqueConfig) : QuantumResult<CompiledOracle> =
        match validateCliqueConfig config with
        | Error err -> Error err
        | Ok () ->
            let numQubits = config.Graph.NumVertices
            let predicate = fun assignment -> isClique config assignment
            fromPredicate predicate numQubits
    
    // ============================================================================
    // WEIGHTED GRAPH COLORING ORACLE - Cost-Optimized Coloring
    // ============================================================================
    
    /// <summary>
    /// Configuration for weighted graph coloring.
    /// </summary>
    /// <remarks>
    /// Weighted graph coloring assigns costs to colors and finds
    /// valid colorings that minimize total cost.
    /// 
    /// **Applications:**
    /// - Scheduling with resource costs (fast CPU = expensive, slow CPU = cheap)
    /// - Register allocation (different register types have different speeds)
    /// - Frequency assignment (some frequencies require licensing fees)
    /// 
    /// **Example:**
    /// Graph with 4 vertices, 3 colors
    /// Color costs: [1.0, 2.0, 5.0] (cheap, medium, expensive)
    /// Find valid coloring minimizing total cost
    /// </remarks>
    type WeightedColoringConfig = {
        /// The graph to color
        Graph: GraphColoringGraph
        
        /// Number of available colors
        NumColors: int
        
        /// Cost of each color (length must equal NumColors)
        ColorCosts: float[]
        
        /// Maximum total cost allowed
        MaxTotalCost: float
    }
    
    /// <summary>
    /// Calculate total cost of a coloring.
    /// </summary>
    /// <param name="config">Weighted coloring configuration</param>
    /// <param name="assignment">Color assignment (bitstring)</param>
    /// <returns>Total cost of colors used</returns>
    let private calculateColoringCost (config: WeightedColoringConfig) (assignment: int) : float =
        let qubitsPerVert = qubitsPerVertex config.NumColors
        
        [| for i in 0 .. config.Graph.NumVertices - 1 do
            let colorValue = extractColor assignment i qubitsPerVert
            if colorValue < config.NumColors then
                yield config.ColorCosts[colorValue]
            else
                yield System.Double.PositiveInfinity  // Invalid color = infinite cost
        |]
        |> Array.sum
    
    /// <summary>
    /// Check if coloring is valid and within cost budget.
    /// </summary>
    /// <param name="config">Weighted coloring configuration</param>
    /// <param name="assignment">Color assignment</param>
    /// <returns>True if valid coloring with cost ≤ max</returns>
    let private isValidWeightedColoring (config: WeightedColoringConfig) (assignment: int) : bool =
        // First check it's a valid coloring (no adjacent same colors)
        let basicConfig = { Graph = config.Graph; NumColors = config.NumColors }
        let validColoring = isValidColoring basicConfig assignment
        
        if not validColoring then
            false
        else
            // Check cost constraint
            let totalCost = calculateColoringCost config assignment
            totalCost <= config.MaxTotalCost
    
    /// <summary>
    /// Validate weighted coloring configuration.
    /// </summary>
    let private validateWeightedColoring (config: WeightedColoringConfig) : QuantumResult<unit> =
        // Reuse basic graph coloring validation
        let basicConfig = { Graph = config.Graph; NumColors = config.NumColors }
        match validateGraphColoring basicConfig with
        | Error err -> Error err
        | Ok () ->
            if config.ColorCosts.Length <> config.NumColors then
                Error (QuantumError.ValidationError ("ColorCosts", 
                    $"length ({config.ColorCosts.Length}) must equal NumColors ({config.NumColors})"))
            elif config.ColorCosts |> Array.exists (fun c -> c < 0.0) then
                Error (QuantumError.ValidationError ("ColorCosts", "all costs must be non-negative"))
            elif config.MaxTotalCost < 0.0 then
                Error (QuantumError.ValidationError ("MaxTotalCost", $"must be non-negative, got {config.MaxTotalCost}"))
            else
                Ok ()
    
    /// <summary>
    /// Create oracle for weighted graph coloring.
    /// </summary>
    /// <param name="config">Weighted coloring configuration</param>
    /// <returns>Oracle that marks valid colorings within cost budget</returns>
    /// <remarks>
    /// **Usage Pattern:**
    /// 1. Start with low MaxTotalCost
    /// 2. If no solution, increase MaxTotalCost
    /// 3. Find minimum cost where solution exists
    /// 
    /// **Example:**
    /// ```fsharp
    /// let graph = graph 3 [(0,1); (1,2); (2,0)]  // Triangle
    /// let config = {
    ///     Graph = graph
    ///     NumColors = 3
    ///     ColorCosts = [|1.0; 2.0; 5.0|]  // Cheap, medium, expensive
    ///     MaxTotalCost = 8.0  // Budget constraint
    /// }
    /// let oracle = weightedColoringOracle config
    /// ```
    /// 
    /// Finds valid 3-colorings of triangle with total cost ≤ 8.0
    /// Best solution: Use colors {0,1,2} = cost 1+2+5=8.0
    /// </remarks>
    let weightedColoringOracle (config: WeightedColoringConfig) : QuantumResult<CompiledOracle> =
        match validateWeightedColoring config with
        | Error err -> Error err
        | Ok () ->
            let qubitsPerVert = qubitsPerVertex config.NumColors
            let numQubits = config.Graph.NumVertices * qubitsPerVert
            
            if numQubits > 20 then
                Error (QuantumError.ValidationError ("NumQubits", 
                    $"resulting qubit count ({numQubits}) exceeds Grover limit (20)"))
            else
                let predicate = fun assignment -> isValidWeightedColoring config assignment
                fromPredicate predicate numQubits
