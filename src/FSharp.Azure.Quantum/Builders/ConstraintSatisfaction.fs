/// TKT-92: Generic Constraint Satisfaction Framework
/// 
/// Provides a unified, extensible framework for solving constraint satisfaction problems
/// including N-Queens, Sudoku, Map Coloring, and more.
///
/// ## Features
/// - Immutable data structures with functional composition
/// - Fluent builder API for problem specification
/// - QUBO encoding for quantum/annealing solvers
/// - Classical backtracking solver
/// - Extensible constraint system
///
/// ## Usage Example
/// ```fsharp
/// let problem =
///     ConstraintSatisfactionBuilder()
///         .Variables([variable "x" [1; 2; 3]])
///         .AddConstraint(AllDifferent ["x"; "y"; "z"])
///         .Build()
///
/// let qubo = toQubo problem
/// let solution = solveClassical problem
/// ```
namespace FSharp.Azure.Quantum

open System

/// <summary>
/// Generic Constraint Satisfaction Framework.
/// Powers N-Queens, Sudoku, Map Coloring, and other CSP problems.
/// </summary>
///
/// <remarks>
/// This module provides a unified API for defining and solving constraint satisfaction
/// problems using quantum annealing (QUBO), classical backtracking, or hybrid approaches.
/// </remarks>
module ConstraintSatisfaction =
    
    // ========================================================================
    // FR-1: VARIABLE DEFINITION
    // ========================================================================
    
    /// <summary>
    /// A variable in the CSP with a finite domain of possible values.
    /// </summary>
    ///
    /// <typeparam name="'T">The type of values in the domain</typeparam>
    ///
    /// <remarks>
    /// Variables are identified by unique string names and have a discrete domain
    /// of possible values. Properties can store arbitrary metadata.
    /// </remarks>
    type Variable<'T when 'T : equality> = {
        /// Unique name identifying this variable
        Name: string
        /// Finite domain of possible values
        Domain: 'T list
        /// Additional metadata for this variable
        Properties: Map<string, obj>
    }
    
    /// <summary>Create a variable with name and domain.</summary>
    /// <param name="name">Unique identifier for the variable</param>
    /// <param name="domain">List of possible values</param>
    /// <returns>A new variable with empty properties</returns>
    let variable name domain = {
        Name = name
        Domain = domain
        Properties = Map.empty
    }
    
    /// <summary>Create a variable with properties.</summary>
    /// <param name="name">Unique identifier for the variable</param>
    /// <param name="domain">List of possible values</param>
    /// <param name="properties">List of key-value pairs for metadata</param>
    /// <returns>A new variable with the specified properties</returns>
    let variableWithProps name domain properties = {
        Name = name
        Domain = domain
        Properties = Map.ofList properties
    }
    
    // ========================================================================
    // FR-2: CONSTRAINT TYPES
    // ========================================================================
    
    /// <summary>
    /// Constraint types for CSP problems.
    /// </summary>
    ///
    /// <remarks>
    /// Constraints define relationships between variables that must be satisfied:
    /// - AllDifferent: Variables must have different values
    /// - Binary: Two variables must satisfy a predicate
    /// - Custom: Arbitrary predicate over all assignments
    /// </remarks>
    [<NoComparison; NoEquality>]
    type CSPConstraint<'T when 'T : equality> =
        /// All specified variables must have different values
        | AllDifferent of variables: string list
        /// Two variables must satisfy a binary predicate
        | Binary of var1: string * var2: string * predicate: ('T * 'T -> bool)
        /// Custom predicate over all variable assignments
        | Custom of predicate: (Map<string, 'T> -> bool)
    
    // ========================================================================
    // QUBO REPRESENTATION
    // ========================================================================
    
    /// QUBO (Quadratic Unconstrained Binary Optimization) representation
    type QuboMatrix = {
        NumVariables: int
        Q: Map<int * int, float>  // Sparse matrix representation
    }
    
    /// Create empty QUBO matrix
    let emptyQubo numVars = {
        NumVariables = numVars
        Q = Map.empty
    }
    
    // ========================================================================
    // CONSTRAINT VIOLATIONS
    // ========================================================================
    
    /// Constraint violation information
    type ConstraintViolation<'T when 'T : equality> = {
        Constraint: CSPConstraint<'T>
        Description: string
        Severity: float  // Penalty value
    }
    
    // ========================================================================
    // FR-5: SOLUTION REPRESENTATION
    // ========================================================================
    
    /// <summary>
    /// Solution to a constraint satisfaction problem.
    /// </summary>
    type CSPSolution<'T when 'T : equality> = {
        /// Variable assignments (None if no solution found)
        Assignments: Map<string, 'T> option
        /// Whether the solution satisfies all constraints
        IsFeasible: bool
        /// List of constraint violations (empty if feasible)
        Violations: ConstraintViolation<'T> list
    }
    
    // ========================================================================
    // CSP PROBLEM DEFINITION
    // ========================================================================
    
    /// A constraint satisfaction problem specification
    type CSPProblem<'T when 'T : equality> = {
        Variables: Variable<'T> list
        Constraints: CSPConstraint<'T> list
    }
    
    // ========================================================================
    // FR-3: FLUENT BUILDER API (Idiomatic Immutable)
    // ========================================================================
    
    /// <summary>
    /// Fluent builder for constraint satisfaction problems (immutable).
    /// </summary>
    type ConstraintSatisfactionBuilder<'T when 'T : equality> = private {
        variables: Variable<'T> list
        constraints: CSPConstraint<'T> list
    } with
        /// Create a new builder with default values
        static member Create() : ConstraintSatisfactionBuilder<'T> = {
            variables = []
            constraints = []
        }
        
        /// <summary>Fluent API: Set the variables in the problem.</summary>
        member this.Variables(varList: Variable<'T> list) =
            { this with variables = varList }
        
        /// <summary>Fluent API: Add a constraint to the problem.</summary>
        member this.AddConstraint(c: CSPConstraint<'T>) =
            { this with constraints = c :: this.constraints }
        
        /// <summary>Build the CSP problem.</summary>
        member this.Build() : CSPProblem<'T> =
            {
                Variables = this.variables
                Constraints = List.rev this.constraints
            }
    
    /// Constructor-like syntax for C# compatibility
    let ConstraintSatisfactionBuilder<'T when 'T : equality> () =
        ConstraintSatisfactionBuilder<'T>.Create()
    
    // ========================================================================
    // QUBO ENCODING CONSTANTS
    // ========================================================================
    
    /// Default penalty weight for constraint violations
    let DefaultPenalty = 10.0
    
    // ========================================================================
    // FR-4: QUBO ENCODING
    // ========================================================================
    
    /// <summary>
    /// Encode a CSP problem to QUBO format.
    /// </summary>
    ///
    /// <param name="problem">The CSP problem to encode</param>
    /// <returns>A QUBO matrix suitable for quantum annealing</returns>
    ///
    /// <remarks>
    /// <para><b>Variable Encoding:</b></para>
    /// <para>Each variable with domain [v0, v1, ..., vn] is encoded using one-hot encoding:</para>
    /// <para>Binary variables x_{var,v0}, x_{var,v1}, ..., x_{var,vn} where exactly one is 1</para>
    ///
    /// <para><b>One-Hot Constraint:</b></para>
    /// <para>For each variable: Σ_{d∈domain} x_{var,d} = 1</para>
    /// <para>QUBO form: Σ_{d1&lt;d2} 2*penalty * x_{var,d1} * x_{var,d2}</para>
    ///
    /// <para><b>AllDifferent Constraint:</b></para>
    /// <para>For each pair of variables (v1, v2) and each domain value d:</para>
    /// <para>Penalty if x_{v1,d} = 1 AND x_{v2,d} = 1 (both assigned same value)</para>
    ///
    /// <para><b>Binary Constraint:</b></para>
    /// <para>For each pair of domain values (d1, d2):</para>
    /// <para>Penalty if predicate(d1, d2) is false AND x_{v1,d1} = 1 AND x_{v2,d2} = 1</para>
    /// </remarks>
    let toQubo (problem: CSPProblem<'T>) : QuboMatrix =
        // Create variable name to index mapping
        let varIndexMap =
            problem.Variables
            |> List.mapi (fun idx var -> var.Name, idx)
            |> Map.ofList
        
        // Create value to binary variable index mapping
        // varIdx * maxDomainSize + domainIdx
        let maxDomainSize =
            if problem.Variables.IsEmpty then 0
            else problem.Variables |> List.map (fun v -> v.Domain.Length) |> List.max
        
        // Calculate total number of binary variables
        // Using matrix layout: numVariables * maxDomainSize
        // This may waste some variables but simplifies indexing
        let numVars = problem.Variables.Length * maxDomainSize
        
        let getVarIndex varName domainValue =
            match varIndexMap.TryFind varName with
            | Some varIdx ->
                let var = problem.Variables.[varIdx]
                match var.Domain |> List.tryFindIndex ((=) domainValue) with
                | Some domainIdx -> Some (varIdx * maxDomainSize + domainIdx)
                | None -> None
            | None -> None
        
        let baseQubo = emptyQubo numVars
        
        // Helper: Add terms to QUBO matrix
        let addTermsToQubo (qubo: QuboMatrix) (terms: ((int * int) * float) list) : QuboMatrix =
            let updatedQ =
                terms
                |> List.fold (fun q (key, value) -> 
                    Map.add key (Map.tryFind key q |> Option.defaultValue 0.0 |> (+) value) q
                ) qubo.Q
            { qubo with Q = updatedQ }
        
        // ONE-HOT CONSTRAINT: Each variable must be assigned exactly one value
        let oneHotTerms =
            problem.Variables
            |> List.mapi (fun varIdx var ->
                var.Domain
                |> List.mapi (fun d1Idx d1 ->
                    var.Domain
                    |> List.mapi (fun d2Idx d2 ->
                        if d1Idx < d2Idx then
                            let var1 = varIdx * maxDomainSize + d1Idx
                            let var2 = varIdx * maxDomainSize + d2Idx
                            Some ((var1, var2), 2.0 * DefaultPenalty)
                        else
                            None
                    )
                    |> List.choose id
                )
                |> List.concat
            )
            |> List.concat
        
        let quboWithOneHot = addTermsToQubo baseQubo oneHotTerms
        
        // CONSTRAINT ENCODING
        let constraintTerms =
            problem.Constraints
            |> List.collect (fun constr ->
                match constr with
                | AllDifferent varNames ->
                    // For each pair of variables and each domain value
                    varNames
                    |> List.allPairs varNames
                    |> List.filter (fun (v1, v2) -> v1 < v2)
                    |> List.collect (fun (v1Name, v2Name) ->
                        match varIndexMap.TryFind v1Name, varIndexMap.TryFind v2Name with
                        | Some v1Idx, Some v2Idx ->
                            let var1 = problem.Variables.[v1Idx]
                            let var2 = problem.Variables.[v2Idx]
                            
                            // Common domain values
                            let commonValues = 
                                Set.intersect (Set.ofList var1.Domain) (Set.ofList var2.Domain)
                                |> Set.toList
                            
                            commonValues
                            |> List.choose (fun value ->
                                match getVarIndex v1Name value, getVarIndex v2Name value with
                                | Some idx1, Some idx2 ->
                                    Some ((idx1, idx2), DefaultPenalty)
                                | _ -> None
                            )
                        | _ -> []
                    )
                
                | Binary (v1Name, v2Name, predicate) ->
                    // For each pair of domain values where predicate is false
                    match varIndexMap.TryFind v1Name, varIndexMap.TryFind v2Name with
                    | Some v1Idx, Some v2Idx ->
                        let var1 = problem.Variables.[v1Idx]
                        let var2 = problem.Variables.[v2Idx]
                        
                        var1.Domain
                        |> List.allPairs var2.Domain
                        |> List.choose (fun (d2, d1) ->
                            if not (predicate (d1, d2)) then
                                match getVarIndex v1Name d1, getVarIndex v2Name d2 with
                                | Some idx1, Some idx2 ->
                                    let (i, j) = if idx1 < idx2 then (idx1, idx2) else (idx2, idx1)
                                    Some ((i, j), DefaultPenalty)
                                | _ -> None
                            else
                                None
                        )
                    | _ -> []
                
                | Custom _ ->
                    // Custom constraints are handled during solution validation
                    // Cannot be efficiently encoded in QUBO
                    []
            )
        
        addTermsToQubo quboWithOneHot constraintTerms
    
    // ========================================================================
    // FR-5: SOLUTION DECODING
    // ========================================================================
    
    /// <summary>
    /// Decode a QUBO solution to variable assignments.
    /// </summary>
    ///
    /// <param name="problem">The original CSP problem</param>
    /// <param name="quboSolution">Binary variable assignments from QUBO solver</param>
    /// <returns>A CSP solution with assignments and validation</returns>
    let decodeSolution (problem: CSPProblem<'T>) (quboSolution: int list) : CSPSolution<'T> =
        let maxDomainSize =
            if problem.Variables.IsEmpty then 0
            else problem.Variables |> List.map (fun v -> v.Domain.Length) |> List.max
        
        // Decode one-hot encoding to variable assignments
        let assignments =
            problem.Variables
            |> List.mapi (fun varIdx var ->
                // Find which domain value is assigned (has binary variable = 1)
                let assignedValue =
                    var.Domain
                    |> List.mapi (fun domainIdx value ->
                        let binaryVarIdx = varIdx * maxDomainSize + domainIdx
                        if binaryVarIdx < quboSolution.Length && quboSolution.[binaryVarIdx] = 1 then
                            Some value
                        else
                            None
                    )
                    |> List.choose id
                    |> List.tryHead
                    |> Option.defaultValue (var.Domain |> List.tryHead |> Option.defaultValue Unchecked.defaultof<'T>)
                
                var.Name, assignedValue
            )
            |> Map.ofList
        
        // Validate constraints
        let violations =
            problem.Constraints
            |> List.choose (fun constr ->
                match constr with
                | AllDifferent varNames ->
                    let values = varNames |> List.choose assignments.TryFind
                    let uniqueValues = values |> List.distinct
                    if values.Length <> uniqueValues.Length then
                        Some {
                            Constraint = constr
                            Description = $"AllDifferent violation: {varNames}"
                            Severity = DefaultPenalty
                        }
                    else
                        None
                
                | Binary (v1, v2, predicate) ->
                    match assignments.TryFind v1, assignments.TryFind v2 with
                    | Some val1, Some val2 ->
                        if not (predicate (val1, val2)) then
                            Some {
                                Constraint = constr
                                Description = $"Binary constraint violation: {v1}={val1}, {v2}={val2}"
                                Severity = DefaultPenalty
                            }
                        else
                            None
                    | _ -> None
                
                | Custom predicate ->
                    if not (predicate assignments) then
                        Some {
                            Constraint = constr
                            Description = "Custom constraint violation"
                            Severity = DefaultPenalty
                        }
                    else
                        None
            )
        
        {
            Assignments = Some assignments
            IsFeasible = List.isEmpty violations
            Violations = violations
        }
    
    // ========================================================================
    // FR-6: CLASSICAL BACKTRACKING SOLVER
    // ========================================================================
    
    /// <summary>
    /// Solve CSP using classical backtracking algorithm.
    /// </summary>
    ///
    /// <param name="problem">The CSP problem to solve</param>
    /// <returns>A solution with variable assignments</returns>
    ///
    /// <remarks>
    /// Uses recursive backtracking with forward checking.
    /// Time complexity: O(d^n) where d = domain size, n = number of variables.
    /// Suitable for small-to-medium CSPs.
    /// </remarks>
    let solveClassical (problem: CSPProblem<'T>) : CSPSolution<'T> =
        let varNames = problem.Variables |> List.map (fun v -> v.Name)
        
        /// Check if an assignment is consistent with constraints
        let isConsistent (assignments: Map<string, 'T>) =
            problem.Constraints
            |> List.forall (fun constr ->
                match constr with
                | AllDifferent varNames ->
                    let assignedVars = varNames |> List.filter assignments.ContainsKey
                    let values = assignedVars |> List.map (fun v -> assignments.[v])
                    values.Length = (values |> List.distinct |> List.length)
                
                | Binary (v1, v2, predicate) ->
                    match assignments.TryFind v1, assignments.TryFind v2 with
                    | Some val1, Some val2 -> predicate (val1, val2)
                    | _ -> true  // Not yet assigned
                
                | Custom predicate ->
                    // Only check if all variables are assigned
                    if varNames |> List.forall assignments.ContainsKey then
                        predicate assignments
                    else
                        true
            )
        
        /// Recursive backtracking search
        let rec backtrack (assignments: Map<string, 'T>) (remainingVars: string list) =
            match remainingVars with
            | [] -> 
                // All variables assigned
                Some assignments
            
            | varName :: rest ->
                let var = problem.Variables |> List.find (fun v -> v.Name = varName)
                
                // Try each value in domain
                var.Domain
                |> List.tryPick (fun value ->
                    let newAssignments = Map.add varName value assignments
                    
                    if isConsistent newAssignments then
                        backtrack newAssignments rest
                    else
                        None
                )
        
        match backtrack Map.empty varNames with
        | Some assignments ->
            {
                Assignments = Some assignments
                IsFeasible = true
                Violations = []
            }
        | None ->
            {
                Assignments = None
                IsFeasible = false
                Violations = []
            }
