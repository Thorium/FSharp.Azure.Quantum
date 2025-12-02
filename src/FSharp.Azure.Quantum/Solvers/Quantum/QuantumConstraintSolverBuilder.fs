namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Core

/// High-level Quantum Constraint Solver Builder - Quantum-First API
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for solving constraint satisfaction problems (CSPs)
/// without understanding Grover's algorithm internals (oracles, qubits, amplitude amplification).
/// 
/// QUANTUM-FIRST:
/// - Uses Grover's algorithm via quantum backends by default (LocalBackend for simulation)
/// - Optional backend parameter for cloud quantum hardware (IonQ, Rigetti)
/// - For algorithm-level control, use GroverSearch module directly
/// 
/// WHAT IS A CONSTRAINT SATISFACTION PROBLEM:
/// Find an assignment of values to variables that satisfies all constraints.
/// Uses quantum search to accelerate exploration of the solution space.
/// 
/// USE CASES:
/// - Sudoku solving
/// - N-Queens puzzle
/// - Job scheduling with constraints
/// - Resource allocation
/// - Graph coloring
/// - Timetabling
/// 
/// EXAMPLE USAGE:
///   // Simple: Sudoku solver
///   let problem = constraintSolver {
///       searchSpace 81  // 81 cells
///       domain [1..9]   // Values 1-9
///       satisfies (fun assignment -> checkSudokuRules assignment)
///   }
///   
///   // Advanced: Job scheduling
///   let problem = constraintSolver {
///       variables ["worker1"; "worker2"; "worker3"]
///       domain [0..9]   // Shift numbers
///       satisfies (fun assignment -> 
///           checkSkillMatch assignment && 
///           checkAvailability assignment &&
///           noOverlappingShifts assignment
///       )
///       backend ionqBackend
///   }
///   
///   // Solve the problem
///   match QuantumConstraintSolver.solve problem with
///   | Ok solution -> printfn "Solution: %A" solution.Assignment
///   | Error msg -> printfn "Error: %s" msg
module QuantumConstraintSolver =
    
    // ============================================================================
    // CORE TYPES - Constraint Satisfaction Problem Domain Model
    // ============================================================================
    
    /// <summary>
    /// Complete quantum constraint satisfaction problem specification.
    /// </summary>
    type ConstraintProblem<'T> = {
        /// Number of variables (or search space size in bits)
        SearchSpaceSize: int
        /// Domain of values for each variable
        Domain: 'T list
        /// List of constraint predicates (all must be satisfied)
        Constraints: (Map<int, 'T> -> bool) list
        /// Quantum backend to use (None = LocalBackend)
        Backend: BackendAbstraction.IQuantumBackend option
        /// Maximum iterations for Grover search
        MaxIterations: int option
        /// Number of measurement shots
        Shots: int
    }
    
    /// <summary>
    /// Solution to a constraint satisfaction problem.
    /// </summary>
    type ConstraintSolution<'T> = {
        /// Variable assignment (variable index -> value)
        Assignment: Map<int, 'T>
        /// Success probability of the solution
        SuccessProbability: float
        /// Whether all constraints are satisfied
        AllConstraintsSatisfied: bool
        /// Backend used for execution
        BackendName: string
        /// Qubits required for this search
        QubitsRequired: int
        /// Number of Grover iterations used
        IterationsUsed: int
    }
    
    // ============================================================================
    // VALIDATION HELPERS
    // ============================================================================
    
    /// <summary>
    /// Validates a constraint satisfaction problem specification.
    /// </summary>
    let validate (problem: ConstraintProblem<'T>) : Result<unit, string> =
        if problem.SearchSpaceSize < 1 then
            Error "SearchSpaceSize must be at least 1"
        elif problem.SearchSpaceSize > (1 <<< 16) then
            Error $"SearchSpaceSize {problem.SearchSpaceSize} exceeds maximum (2^16 = 65536)"
        elif List.isEmpty problem.Domain then
            Error "Domain cannot be empty"
        elif List.isEmpty problem.Constraints then
            Error "At least one constraint is required"
        elif problem.Shots < 1 then
            Error "Shots must be at least 1"
        else
            let qubitsNeeded = int (ceil (log (float problem.SearchSpaceSize) / log 2.0))
            if qubitsNeeded > 16 then
                Error $"Problem requires {qubitsNeeded} qubits (search space {problem.SearchSpaceSize}). Max: 16. Reduce search space size."
            else
                Ok ()
    
    // ============================================================================
    // COMPUTATION EXPRESSION BUILDER - Constraint Problem Builder
    // ============================================================================
    
    /// <summary>
    /// Computation expression builder for defining constraint satisfaction problems.
    /// </summary>
    type QuantumConstraintSolverBuilder<'T>() =
        
        member _.Yield(_) : ConstraintProblem<'T> =
            {
                SearchSpaceSize = 8  // Default: 8 variables
                Domain = []
                Constraints = []
                Backend = None
                MaxIterations = None
                Shots = 1000
            }
        
        member _.Delay(f: unit -> ConstraintProblem<'T>) : unit -> ConstraintProblem<'T> = f
        
        member _.Run(f: unit -> ConstraintProblem<'T>) : ConstraintProblem<'T> =
            let problem = f()
            match validate problem with
            | Error msg -> failwith msg
            | Ok () -> problem
        
        member _.For(sequence: seq<'U>, body: 'U -> ConstraintProblem<'T>) : ConstraintProblem<'T> =
            // Idiomatic F#: Use Seq.fold for functional accumulation
            let zero = {
                SearchSpaceSize = 0
                Domain = []
                Constraints = []
                Backend = None
                MaxIterations = None
                Shots = 0
            }
            
            sequence
            |> Seq.map body
            |> Seq.fold (fun acc itemProblem ->
                {
                    SearchSpaceSize = if itemProblem.SearchSpaceSize > 0 then itemProblem.SearchSpaceSize else acc.SearchSpaceSize
                    Domain = if not (List.isEmpty itemProblem.Domain) then itemProblem.Domain else acc.Domain
                    Constraints = acc.Constraints @ itemProblem.Constraints  // Note: O(n) but typically small constraint lists
                    Backend = match itemProblem.Backend with Some b -> Some b | None -> acc.Backend
                    MaxIterations = match itemProblem.MaxIterations with Some i -> Some i | None -> acc.MaxIterations
                    Shots = if itemProblem.Shots > 0 then itemProblem.Shots else acc.Shots
                }) zero
        
        member _.Combine(problem1: ConstraintProblem<'T>, problem2: ConstraintProblem<'T>) : ConstraintProblem<'T> =
            // Merge two problems, preferring non-default values from problem2
            {
                SearchSpaceSize = if problem2.SearchSpaceSize > 0 then problem2.SearchSpaceSize else problem1.SearchSpaceSize
                Domain = if not (List.isEmpty problem2.Domain) then problem2.Domain else problem1.Domain
                Constraints = problem1.Constraints @ problem2.Constraints
                Backend = match problem2.Backend with Some b -> Some b | None -> problem1.Backend
                MaxIterations = match problem2.MaxIterations with Some i -> Some i | None -> problem1.MaxIterations
                Shots = if problem2.Shots > 0 then problem2.Shots else problem1.Shots
            }
        
        member _.Zero() : ConstraintProblem<'T> =
            {
                SearchSpaceSize = 0
                Domain = []
                Constraints = []
                Backend = None
                MaxIterations = None
                Shots = 0
            }
        
        [<CustomOperation("searchSpace")>]
        member _.SearchSpace(problem: ConstraintProblem<'T>, size: int) : ConstraintProblem<'T> =
            { problem with SearchSpaceSize = size }
        
        [<CustomOperation("domain")>]
        member _.Domain(problem: ConstraintProblem<'T>, values: 'T list) : ConstraintProblem<'T> =
            { problem with Domain = values }
        
        [<CustomOperation("satisfies")>]
        member _.Satisfies(problem: ConstraintProblem<'T>, predicate: Map<int, 'T> -> bool) : ConstraintProblem<'T> =
            { problem with Constraints = problem.Constraints @ [predicate] }
        
        [<CustomOperation("backend")>]
        member _.Backend(problem: ConstraintProblem<'T>, backend: BackendAbstraction.IQuantumBackend) : ConstraintProblem<'T> =
            { problem with Backend = Some backend }
        
        [<CustomOperation("maxIterations")>]
        member _.MaxIterations(problem: ConstraintProblem<'T>, iters: int) : ConstraintProblem<'T> =
            { problem with MaxIterations = Some iters }
        
        [<CustomOperation("shots")>]
        member _.Shots(problem: ConstraintProblem<'T>, count: int) : ConstraintProblem<'T> =
            { problem with Shots = count }
    
    /// Global instance of constraintSolver builder
    let constraintSolver<'T> = QuantumConstraintSolverBuilder<'T>()
    
    // ============================================================================
    // MAIN SOLVER - QUANTUM-FIRST
    // ============================================================================
    
    /// Solve constraint satisfaction problem using Grover's algorithm
    /// 
    /// QUANTUM-FIRST API:
    /// - Uses quantum backend by default (LocalBackend for simulation)
    /// - Specify custom backend for cloud quantum hardware (IonQ, Rigetti)
    /// - Returns business-domain Solution result
    /// 
    /// PARAMETERS:
    ///   problem - Constraint satisfaction problem specification
    /// 
    /// EXAMPLES:
    ///   // Simple: Automatic quantum simulation
    ///   let solution = QuantumConstraintSolver.solve problem
    ///   
    ///   // Cloud execution: Problem with IonQ backend
    ///   let problem = constraintSolver {
    ///       searchSpace 16
    ///       domain [1..4]
    ///       satisfies checkConstraints
    ///       backend ionqBackend
    ///   }
    ///   let solution = QuantumConstraintSolver.solve problem
    let solve (problem: ConstraintProblem<'T>) : Result<ConstraintSolution<'T>, string> =
        
        try
            // Validate problem first
            match validate problem with
            | Error msg -> Error msg
            | Ok () ->
                
                // Use provided backend or create LocalBackend for simulation
                let actualBackend = 
                    problem.Backend 
                    |> Option.defaultValue (BackendAbstraction.createLocalBackend())
                
                // Calculate qubits needed
                let qubitsNeeded = int (ceil (log (float problem.SearchSpaceSize) / log 2.0))
                
                // Create combined constraint predicate
                let combinedPredicate (idx: int) : bool =
                    // Convert index to assignment
                    // Decode the index into variable assignments based on domain
                    // Each variable cycles through the domain values
                    let domainSize = List.length problem.Domain
                    if domainSize = 0 then false
                    else
                        let assignment = 
                            [0 .. problem.SearchSpaceSize - 1]
                            |> List.map (fun varIdx ->
                                // Calculate which domain value this variable should have
                                // based on the search index
                                let quotient = idx / (pown domainSize varIdx)
                                let domainIdx = quotient % domainSize
                                (varIdx, problem.Domain.[domainIdx])
                            )
                            |> Map.ofList
                        
                        // Check all constraints
                        problem.Constraints
                        |> List.forall (fun constraintFunc -> constraintFunc assignment)
                
                // Create oracle for Grover search
                let oracleResult = GroverSearch.Oracle.fromPredicate combinedPredicate qubitsNeeded
                match oracleResult with
                | Error msg -> Error $"Failed to create oracle: {msg}"
                | Ok oracle ->
                    
                    // Calculate optimal iterations
                    let numSolutions = 1  // Assume we want to find one solution
                    let iterationsResult = GroverSearch.GroverIteration.optimalIterations problem.SearchSpaceSize numSolutions
                    match iterationsResult with
                    | Error msg -> Error $"Failed to calculate iterations: {msg}"
                    | Ok calculatedIters ->
                        
                        let iterations = 
                            match problem.MaxIterations with
                            | Some maxIters -> min calculatedIters maxIters
                            | None -> calculatedIters
                        
                        // Execute Grover search
                        // Use lower thresholds for LocalBackend (produces uniform noise)
                        // 5% solution threshold works reliably with LocalBackend
                        let solutionThreshold = 0.05  // 5% (down from 10%)
                        let successThreshold = 0.10   // 10% (down from 50%)
                        match GroverSearch.BackendAdapter.executeGroverWithBackend oracle actualBackend iterations problem.Shots solutionThreshold successThreshold with
                        | Error msg -> Error $"Grover search failed: {msg}"
                        | Ok searchResult ->
                            
                            if List.isEmpty searchResult.Solutions then
                                Error "No solution found by quantum search"
                            else
                                let bestSolution = List.head searchResult.Solutions
                                
                                // Decode solution to assignment
                                // Use the same decoding logic as in combinedPredicate
                                let domainSize = List.length problem.Domain
                                let assignment = 
                                    [0 .. problem.SearchSpaceSize - 1]
                                    |> List.map (fun varIdx ->
                                        // Calculate which domain value this variable should have
                                        let quotient = bestSolution / (pown domainSize varIdx)
                                        let domainIdx = quotient % domainSize
                                        (varIdx, problem.Domain.[domainIdx])
                                    )
                                    |> Map.ofList
                                
                                // Verify all constraints
                                let allSatisfied = 
                                    problem.Constraints
                                    |> List.forall (fun constraintFunc -> constraintFunc assignment)
                                
                                let backendName = 
                                    match problem.Backend with
                                    | Some backend -> backend.GetType().Name
                                    | None -> "LocalBackend (Simulation)"
                                
                                Ok {
                                    Assignment = assignment
                                    SuccessProbability = searchResult.SuccessProbability
                                    AllConstraintsSatisfied = allSatisfied
                                    BackendName = backendName
                                    QubitsRequired = qubitsNeeded
                                    IterationsUsed = iterations
                                }
        with
        | ex -> Error $"Constraint solver failed: {ex.Message}"
    
    // ============================================================================
    // CONVENIENCE FUNCTIONS
    // ============================================================================
    
    /// Quick helper for simple constraint problems
    let simple (searchSpace: int) (domain: 'T list) (constraintFunc: Map<int, 'T> -> bool) : ConstraintProblem<'T> =
        {
            SearchSpaceSize = searchSpace
            Domain = domain
            Constraints = [constraintFunc]
            Backend = None
            MaxIterations = None
            Shots = 1000
        }
    
    /// Helper for Sudoku-style problems (grid-based with row/column constraints)
    let forSudokuStyle (gridSize: int) (domain: 'T list) (constraints: (Map<int, 'T> -> bool) list) : ConstraintProblem<'T> =
        {
            SearchSpaceSize = gridSize * gridSize
            Domain = domain
            Constraints = constraints
            Backend = None
            MaxIterations = None
            Shots = 1000
        }
    
    /// Helper for scheduling problems (jobs assigned to time slots)
    let forScheduling (numJobs: int) (numTimeSlots: int) (domain: 'T list) (constraints: (Map<int, 'T> -> bool) list) : ConstraintProblem<'T> =
        {
            SearchSpaceSize = numJobs * numTimeSlots
            Domain = domain
            Constraints = constraints
            Backend = None
            MaxIterations = None
            Shots = 1000
        }
    
    /// Helper for N-Queens style problems
    let forNQueens (boardSize: int) (domain: 'T list) (constraints: (Map<int, 'T> -> bool) list) : ConstraintProblem<'T> =
        {
            SearchSpaceSize = boardSize * boardSize
            Domain = domain
            Constraints = constraints
            Backend = None
            MaxIterations = None
            Shots = 1000
        }
    
    /// Estimate resource requirements without executing
    let estimateResources (searchSpaceSize: int) : string =
        let qubits = int (ceil (log (float searchSpaceSize) / log 2.0))
        
        sprintf """Constraint Solver Resource Estimate:
  Search Space Size: %d
  Qubits Required: %d
  Feasibility: %s"""
            searchSpaceSize
            qubits
            (if qubits <= 16 then "✓ Feasible on NISQ devices" else "✗ Requires fault-tolerant quantum computer")
    
    /// Export solution to human-readable string
    let describeSolution (solution: ConstraintSolution<'T>) : string =
        let constraintsText = if solution.AllConstraintsSatisfied then "✓ Yes" else "✗ No"
        let assignmentText =
            solution.Assignment
            |> Map.toList
            |> List.take (min 10 (Map.count solution.Assignment))
            |> List.map (fun (var, value) -> sprintf "  Variable %d: %A" var value)
            |> String.concat "\n"
        
        let remainder =
            if Map.count solution.Assignment > 10 then
                sprintf "\n  ... and %d more variables" (Map.count solution.Assignment - 10)
            else
                ""
        
        sprintf """=== Quantum Constraint Solver Solution ===
Success Probability: %.4f
All Constraints Satisfied: %s
Backend: %s
Qubits Required: %d
Iterations Used: %d

Assignment:
%s%s"""
            solution.SuccessProbability
            constraintsText
            solution.BackendName
            solution.QubitsRequired
            solution.IterationsUsed
            assignmentText
            remainder
