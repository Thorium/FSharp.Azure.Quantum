namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Backends

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
/// SEARCH SPACE SEMANTICS:
/// `searchSpace` is the NUMBER OF VARIABLES in the problem. Each variable takes
/// one value from `domain`, so the solver explores domainSize ^ numVariables
/// candidate assignments; constraint functions receive an assignment Map with
/// keys 0 .. numVariables-1. The total state count is limited to 2^16 (16 qubits)
/// by the local simulator, so numVariables × log2(domainSize) must be ≤ 16.
///
/// EXAMPLE USAGE:
///   // Simple: 4 variables over values 1..9 (9^4 = 6561 candidate assignments)
///   let problem = constraintSolver {
///       searchSpace 4   // number of variables
///       domain [1..9]   // Values 1-9
///       satisfies (fun assignment -> checkRules assignment)
///   }
///
///   // Advanced: Job scheduling (3 workers, each assigned one of 10 shifts)
///   let problem = constraintSolver {
///       searchSpace 3   // one variable per worker
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
        /// Number of variables in the problem. Each variable takes one value from
        /// Domain, so the search explores domainSize ^ SearchSpaceSize candidate
        /// assignments; constraint functions receive Maps keyed 0..SearchSpaceSize-1.
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
        /// Progress reporter for long-running operations
        ProgressReporter: Progress.IProgressReporter option
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
    let validate (problem: ConstraintProblem<'T>) : Result<unit, QuantumError> =
        if problem.SearchSpaceSize < 1 then
            Error (QuantumError.ValidationError ("SearchSpaceSize", "must be at least 1 variable"))
        elif List.isEmpty problem.Domain then
            Error (QuantumError.ValidationError ("Domain", "cannot be empty"))
        elif List.isEmpty problem.Constraints then
            Error (QuantumError.ValidationError ("Constraints", "at least one constraint is required"))
        elif problem.Shots < 1 then
            Error (QuantumError.ValidationError ("Shots", "must be at least 1"))
        else
            // Qubits to address domainSize ^ numVariables states, computed in
            // floating point so oversized problems error instead of overflowing.
            let domainSize = List.length problem.Domain
            let qubitsNeeded =
                if domainSize <= 1 then 1
                else max 1 (int (ceil (float problem.SearchSpaceSize * log (float domainSize) / log 2.0 - 1e-9)))
            if qubitsNeeded > 16 then
                Error (QuantumError.ValidationError ("SearchSpaceSize", $"{problem.SearchSpaceSize} variables over a domain of {domainSize} values requires {qubitsNeeded} qubits. Max: 16 (2^16 states)"))
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
                ProgressReporter = None
            }
        
        member _.Delay(f: unit -> ConstraintProblem<'T>) : unit -> ConstraintProblem<'T> = f
        
        member _.Run(f: unit -> ConstraintProblem<'T>) : ConstraintProblem<'T> =
            let problem = f()
            match validate problem with
            | Error err -> failwith err.Message
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
                ProgressReporter = None
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
                    ProgressReporter = match itemProblem.ProgressReporter with Some r -> Some r | None -> acc.ProgressReporter
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
                ProgressReporter = match problem2.ProgressReporter with Some r -> Some r | None -> problem1.ProgressReporter
            }
        
        member _.Zero() : ConstraintProblem<'T> =
            {
                SearchSpaceSize = 0
                Domain = []
                Constraints = []
                Backend = None
                MaxIterations = None
                Shots = 0
                ProgressReporter = None
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
        
        [<CustomOperation("onProgress")>]
        member _.OnProgress(problem: ConstraintProblem<'T>, reporter: Progress.IProgressReporter) : ConstraintProblem<'T> =
            { problem with ProgressReporter = Some reporter }
    
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
    ///       searchSpace 4   // 4 variables (4^4 = 256 assignments, 8 qubits)
    ///       domain [1..4]
    ///       satisfies checkConstraints
    ///       backend ionqBackend
    ///   }
    ///   let solution = QuantumConstraintSolver.solve problem
    let solve (problem: ConstraintProblem<'T>) : Result<ConstraintSolution<'T>, QuantumError> =
        
        try
            // Validate problem first
            match validate problem with
            | Error err -> Error err
            | Ok () ->
                
                // Use provided backend or create LocalBackend for simulation
                let actualBackend = 
                    problem.Backend 
                    |> Option.defaultValue (Backends.LocalBackend.LocalBackend() :> Core.BackendAbstraction.IQuantumBackend)
                
                // SearchSpaceSize is the NUMBER OF VARIABLES (matching the shipped
                // helpers: forSudokuStyle = grid², forNQueens = board² — one variable
                // per cell). Constraint functions receive an assignment Map with keys
                // 0 .. numVariables-1; the qubit register covers every assignment
                // (domainSize ^ numVariables states).
                let domainSize = List.length problem.Domain
                let numVariables = max 1 problem.SearchSpaceSize

                // Qubits to address domainSize ^ numVariables states, computed in
                // floating point BEFORE any pown so oversized problems produce a clear
                // error instead of an integer overflow.
                let qubitsNeeded =
                    if domainSize <= 1 then 1
                    else max 1 (int (ceil (float numVariables * log (float domainSize) / log 2.0 - 1e-9)))
                let maxQubits = 16

                // Decode a search-space index into a variable assignment using
                // mixed-radix (base domainSize) positional decoding.
                let decodeAssignment (idx: int) : Map<int, 'T> =
                    [0 .. numVariables - 1]
                    |> List.map (fun varIdx ->
                        // Calculate which domain value this variable should have
                        // based on the search index
                        let quotient = idx / (pown domainSize varIdx)
                        let domainIdx = quotient % domainSize
                        (varIdx, problem.Domain.[domainIdx])
                    )
                    |> Map.ofList

                // Create combined constraint predicate
                let combinedPredicate (idx: int) : bool =
                    let assignment = decodeAssignment idx
                    problem.Constraints
                    |> List.forall (fun constraintFunc -> constraintFunc assignment)

                // Report progress: Starting search
                problem.ProgressReporter 
                |> Option.iter (fun reporter -> 
                    reporter.Report(Progress.PhaseChanged("Constraint Solving", Some "Building quantum oracle...")))
                
                // Create oracle for Grover search using new API
                result {
                    // Refuse search spaces the simulator cannot address, aligning with the
                    // validate() guard (max 2^16 states / 16 qubits). This also protects the
                    // Int32 `pown domainSize varIdx` in decodeAssignment from overflowing.
                    do! if qubitsNeeded <= maxQubits then Ok ()
                        else Error (QuantumError.ValidationError ("SearchSpaceSize", $"searching {numVariables} variables over a domain of {domainSize} values requires {qubitsNeeded} qubits. Max: {maxQubits} (2^16 states)"))

                    let! oracle = GroverSearch.Oracle.fromPredicate combinedPredicate qubitsNeeded

                    // Report progress: Oracle created
                    problem.ProgressReporter
                    |> Option.iter (fun reporter ->
                        reporter.Report(Progress.PhaseChanged("Quantum Search", Some $"Searching {numVariables} variables ({qubitsNeeded} qubits)...")))
                    
                    // Create Grover config with optional iterations
                    let groverConfig = {
                        GroverSearch.Grover.defaultConfig with
                            Iterations = problem.MaxIterations
                            Shots = problem.Shots
                            SolutionThreshold = 0.05  // 5% for LocalBackend reliability
                    }
                    
                    // Execute Grover search using new unified API
                    let! searchResult = GroverSearch.Grover.search oracle actualBackend groverConfig
                    
                    // Report progress: Search complete
                    problem.ProgressReporter 
                    |> Option.iter (fun reporter -> 
                        reporter.Report(Progress.PhaseChanged("Solution Found", Some $"Verified with {searchResult.SuccessProbability:P1} probability")))
                    
                    match searchResult.Solutions with
                    | [] -> return! Error (QuantumError.OperationError ("GroverSearch", "No solution found by quantum search"))
                    | bestSolution :: _ ->
                        
                        // Decode solution to assignment
                        // (same mixed-radix decoding as in combinedPredicate)
                        let assignment = decodeAssignment bestSolution
                        
                        // Verify all constraints
                        let allSatisfied = 
                            problem.Constraints
                            |> List.forall (fun constraintFunc -> constraintFunc assignment)
                        
                        let backendName = 
                            match problem.Backend with
                            | Some backend -> backend.GetType().Name
                            | None -> "LocalBackend (Simulation)"
                        
                        return {
                            Assignment = assignment
                            SuccessProbability = searchResult.SuccessProbability
                            AllConstraintsSatisfied = allSatisfied
                            BackendName = backendName
                            QubitsRequired = qubitsNeeded
                            IterationsUsed = searchResult.Iterations
                        }
                }
        with
        | ex -> Error (QuantumError.OperationError ("ConstraintSolver", $"Constraint solver failed: {ex.Message}"))
    
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
            ProgressReporter = None
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
            ProgressReporter = None
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
            ProgressReporter = None
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
            ProgressReporter = None
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
            |> List.map (fun (var, value) -> $"  Variable %d{var}: %A{value}")
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
