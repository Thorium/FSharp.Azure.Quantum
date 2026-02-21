namespace FSharp.Azure.Quantum.Quantum

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers

/// Quantum Binary Integer Linear Programming Solver (QAOA-based)
///
/// Problem: Minimize c^T x  subject to  Ax <= b,  x in {0,1}^n
///
/// This is the most general QUBO solver in the library — all other
/// combinatorial solvers (Vertex Cover, MaxCut, Knapsack, etc.) are
/// special cases of Binary ILP.  However, domain-specific QUBO
/// encodings typically outperform the generic encoding used here.
///
/// QUBO Formulation:
///   Decision variables: x_0 .. x_{n-1} (the original BIP variables)
///   Slack variables:    For constraint k with bound b_k, introduce
///                       T_k = ceil(log2(b_k + 1)) binary slack bits z_{k,t}
///                       so that s_k = sum_{t=0}^{T_k-1} 2^t * z_{k,t}
///
///   Objective (diagonal):
///     qubo[i,i] += c_i
///
///   Each inequality constraint k:  a_k^T x + s_k = b_k
///     Penalty: lambda_k * (a_k^T x + s_k - b_k)^2
///
///     Expanded:
///       Diagonal x_i:     lambda * (a_{k,i}^2 - 2*a_{k,i}*b_k)
///       Diagonal z_{k,t}: lambda * (2^{2t} - 2*2^t*b_k)
///       Cross x_i*x_j:    lambda * 2*a_{k,i}*a_{k,j}  → symmetric split
///       Cross x_i*z_{k,t}: lambda * 2*a_{k,i}*2^t      → symmetric split
///       Cross z_{k,t1}*z_{k,t2}: lambda * 2*2^{t1}*2^{t2} → symmetric split
///
/// Qubits: n + sum_k ceil(log2(b_k + 1))
///
/// Scaling concern: Slack bits grow logarithmically per constraint bound.
/// Practical for ~10 variables with ~5 constraints.
///
/// RULE 1 COMPLIANCE:
/// All public solve functions require IQuantumBackend parameter.
/// Classical solver is private.
module QuantumBinaryILPSolver =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// A single linear inequality constraint: a^T x <= b
    type Constraint = {
        /// Coefficients a_i for each decision variable
        Coefficients: float list
        /// Right-hand side bound (must be non-negative for slack encoding)
        Bound: float
    }

    /// Binary ILP problem definition
    type Problem = {
        /// Objective function coefficients c_i (minimize c^T x)
        ObjectiveCoeffs: float list
        /// Linear inequality constraints (a^T x <= b)
        Constraints: Constraint list
    }

    /// Binary ILP solution
    type Solution = {
        /// Decision variable assignments (0 or 1)
        Variables: int[]
        /// Objective function value: c^T x
        ObjectiveValue: float
        /// Number of constraints satisfied
        ConstraintsSatisfied: int
        /// Total number of constraints
        TotalConstraints: int
        /// Whether all constraints are satisfied
        IsValid: bool
        /// Whether constraint repair was applied
        WasRepaired: bool
        /// Name of the quantum backend used
        BackendName: string
        /// Number of measurement shots
        NumShots: int
        /// Optimized QAOA (gamma, beta) parameters per layer
        OptimizedParameters: (float * float)[] option
        /// Whether Nelder-Mead converged
        OptimizationConverged: bool option
    }

    // ========================================================================
    // CONFIGURATION (type alias for unified config)
    // ========================================================================

    type Config = QaoaSolverConfig

    let defaultConfig : Config = QaoaExecutionHelpers.defaultConfig
    let fastConfig : Config = QaoaExecutionHelpers.fastConfig
    let highQualityConfig : Config = QaoaExecutionHelpers.highQualityConfig

    // ========================================================================
    // SLACK VARIABLE HELPERS
    // ========================================================================

    /// Compute the number of slack bits needed for a constraint with bound b.
    /// T = ceil(log2(b + 1)), minimum 1 bit for b > 0, 0 bits for b = 0.
    /// Uses integer bit counting to avoid floating-point precision issues.
    let private slackBitsForBound (b: float) : int =
        if b <= 0.0 then 0
        elif b < 1.0 then 1
        else
            // Integer bit counting: find smallest t such that 2^t >= b+1
            let bInt = int (System.Math.Ceiling b)
            let rec countBits value bits =
                if value <= 0 then bits
                else countBits (value >>> 1) (bits + 1)
            max 1 (countBits bInt 0)

    /// Compute the starting index for slack variables of constraint k.
    /// Slack variables for constraint k start at:
    ///   n + sum_{j=0}^{k-1} slackBitsForBound(b_j)
    let private slackStartIndex (n: int) (constraints: Constraint list) (k: int) : int =
        let precedingSlack =
            constraints
            |> List.take k
            |> List.sumBy (fun c -> slackBitsForBound c.Bound)
        n + precedingSlack

    // ========================================================================
    // QUBIT ESTIMATION (Decision 11)
    // ========================================================================

    /// Estimate the number of qubits required.
    /// n (decision variables) + sum_k ceil(log2(b_k + 1)) (slack variables).
    let estimateQubits (problem: Problem) : int =
        let n = problem.ObjectiveCoeffs.Length
        let slackBits =
            problem.Constraints
            |> List.sumBy (fun c -> slackBitsForBound c.Bound)
        n + slackBits

    // ========================================================================
    // QUBO CONSTRUCTION (Decision 9: sparse internally, Decision 5: dense output)
    // ========================================================================

    /// Build the QUBO as a sparse map.
    /// Encodes: minimize c^T x + sum_k lambda_k * (a_k^T x + s_k - b_k)^2
    let private buildQuboMap (problem: Problem) : Map<int * int, float> =
        let n = problem.ObjectiveCoeffs.Length

        // Compute penalty weight: must dominate objective
        // lambda = max(|c_i|) * n + 1, ensuring constraint penalties dominate
        let maxAbsObj =
            problem.ObjectiveCoeffs
            |> List.map abs
            |> List.fold max 1.0
        let lambda = maxAbsObj * float n + 1.0

        let empty = Map.empty<int * int, float>

        // --- Objective: c^T x → diagonal terms ---
        let objectiveTerms =
            problem.ObjectiveCoeffs
            |> List.indexed
            |> List.fold (fun acc (i, ci) ->
                if abs ci < 1e-15 then acc
                else acc |> Qubo.combineTerms (i, i) ci) empty

        // --- Constraint penalties ---
        // For each constraint k:  a_k^T x + s_k - b_k = 0
        // Penalty: lambda * (sum_i a_{k,i} * x_i + sum_t 2^t * z_{k,t} - b_k)^2
        //
        // Let the coefficients vector be:
        //   c'_0 = a_{k,0}, ..., c'_{n-1} = a_{k,n-1},
        //   c'_{n+offset} = 2^0, c'_{n+offset+1} = 2^1, ..., c'_{n+offset+T-1} = 2^{T-1}
        //   constant = -b_k
        //
        // (sum c'_j * v_j - b_k)^2 =
        //   sum_j c'_j^2 * v_j          (diagonal, since v_j^2 = v_j)
        //   + 2 * sum_{j1<j2} c'_{j1}*c'_{j2} * v_{j1}*v_{j2}  (off-diagonal)
        //   - 2*b_k * sum_j c'_j * v_j  (diagonal contribution)
        //   + b_k^2                      (constant, ignored in QUBO)
        //
        // Diagonal: lambda * (c'_j^2 - 2*b_k*c'_j)
        // Off-diagonal: lambda * 2 * c'_{j1} * c'_{j2} → symmetric split

        let constraintTerms =
            problem.Constraints
            |> List.indexed
            |> List.fold (fun acc (k, constr) ->
                let bk = constr.Bound
                let tk = slackBitsForBound bk
                let slackStart = slackStartIndex n problem.Constraints k

                // Build the unified coefficient vector: (varIndex, coefficient)
                let decisionCoeffs =
                    constr.Coefficients
                    |> List.indexed
                    |> List.filter (fun (_, ai) -> abs ai > 1e-15)
                    |> List.map (fun (i, ai) -> (i, ai))

                let slackCoeffs =
                    [ 0 .. tk - 1 ]
                    |> List.map (fun t ->
                        let idx = slackStart + t
                        let coeff = pown 2.0 t
                        (idx, coeff))

                let allCoeffs = decisionCoeffs @ slackCoeffs

                // Diagonal terms: lambda * (c_j^2 - 2*b_k*c_j) for each variable
                let acc =
                    allCoeffs
                    |> List.fold (fun a (idx, cj) ->
                        let diagValue = lambda * (cj * cj - 2.0 * bk * cj)
                        a |> Qubo.combineTerms (idx, idx) diagValue) acc

                // Off-diagonal terms: lambda * 2 * c_{j1} * c_{j2} → symmetric split
                let pairs =
                    allCoeffs
                    |> List.collect (fun (j1, c1) ->
                        allCoeffs
                        |> List.filter (fun (j2, _) -> j2 > j1)
                        |> List.collect (fun (j2, c2) ->
                            let value = lambda * 2.0 * c1 * c2
                            // Symmetric split: value/2 to (j1,j2) and (j2,j1)
                            [ ((j1, j2), value / 2.0)
                              ((j2, j1), value / 2.0) ]))

                pairs
                |> List.fold (fun a (key, value) -> Qubo.combineTerms key value a) acc
            ) empty

        // Combine objective and constraint terms
        [ objectiveTerms; constraintTerms ]
        |> List.fold (fun combined termMap ->
            termMap |> Map.fold (fun acc key value ->
                Qubo.combineTerms key value acc) combined) Map.empty

    /// Validate a Binary ILP problem, returning Error if invalid.
    let private validateProblem (problem: Problem) : Result<unit, QuantumError> =
        if problem.ObjectiveCoeffs.IsEmpty then
            Error (QuantumError.ValidationError ("objectiveCoeffs", "Problem has no decision variables"))
        elif problem.Constraints
             |> List.exists (fun c -> c.Coefficients.Length <> problem.ObjectiveCoeffs.Length) then
            Error (QuantumError.ValidationError ("coefficients",
                "All constraint coefficient vectors must have the same length as the objective"))
        elif problem.Constraints |> List.exists (fun c -> c.Bound < 0.0) then
            Error (QuantumError.ValidationError ("bound",
                "Constraint bounds must be non-negative for slack variable encoding"))
        else
            Ok ()

    /// Convert problem to dense QUBO matrix.
    /// Returns Result to follow the canonical pattern (validates inputs).
    let toQubo (problem: Problem) : Result<float[,], QuantumError> =
        match validateProblem problem with
        | Error err -> Error err
        | Ok () ->
            let totalVars = estimateQubits problem
            let quboMap = buildQuboMap problem
            Ok (Qubo.toDenseArray totalVars quboMap)

    // ========================================================================
    // SOLUTION DECODING & VALIDATION
    // ========================================================================

    /// Compute the objective value c^T x.
    let private computeObjective (problem: Problem) (vars: int[]) : float =
        problem.ObjectiveCoeffs
        |> List.indexed
        |> List.sumBy (fun (i, ci) -> ci * float vars.[i])

    /// Check whether a constraint is satisfied: a^T x <= b.
    let private isConstraintSatisfied (constr: Constraint) (vars: int[]) : bool =
        let lhs =
            constr.Coefficients
            |> List.indexed
            |> List.sumBy (fun (i, ai) -> ai * float vars.[i])
        lhs <= constr.Bound + 1e-9

    /// Count the number of satisfied constraints.
    let private countSatisfiedConstraints (problem: Problem) (vars: int[]) : int =
        problem.Constraints
        |> List.filter (fun c -> isConstraintSatisfied c vars)
        |> List.length

    /// Validate a bitstring for this problem.
    /// Checks: correct length and all constraints satisfied.
    let isValid (problem: Problem) (bits: int[]) : bool =
        let totalVars = estimateQubits problem
        bits.Length = totalVars
        && (
            let n = problem.ObjectiveCoeffs.Length
            let vars = bits.[0 .. n - 1]
            countSatisfiedConstraints problem vars = problem.Constraints.Length)

    /// Decode a bitstring into a Solution.
    let private decodeSolution (problem: Problem) (bits: int[]) : Solution =
        let n = problem.ObjectiveCoeffs.Length
        let vars = bits.[0 .. n - 1]
        let obj = computeObjective problem vars
        let satisfied = countSatisfiedConstraints problem vars

        {
            Variables = vars
            ObjectiveValue = obj
            ConstraintsSatisfied = satisfied
            TotalConstraints = problem.Constraints.Length
            IsValid = satisfied = problem.Constraints.Length
            WasRepaired = false
            BackendName = ""
            NumShots = 0
            OptimizedParameters = None
            OptimizationConverged = None
        }

    // ========================================================================
    // CONSTRAINT REPAIR (greedy, idiomatic F#)
    // ========================================================================

    /// Repair an invalid Binary ILP solution.
    /// Strategy: For each violated constraint, greedily flip variables from 1→0
    /// (choosing the variable with the largest positive coefficient first)
    /// until the constraint is satisfied. This reduces the LHS most quickly.
    /// Wraps in a convergence loop since fixing one constraint may violate another.
    /// Then rebuild the full bitstring with correct slack values.
    let private repairConstraints (problem: Problem) (bits: int[]) : int[] =
        let n = problem.ObjectiveCoeffs.Length
        let totalVars = estimateQubits problem

        // Start with current decision variables
        let vars = Array.copy bits.[0 .. n - 1]

        // Convergence loop: iterate until all constraints satisfied or max iterations
        let maxIterations = problem.Constraints.Length * 2 + 1
        let rec converge iteration =
            if iteration >= maxIterations then
                ()  // Give up after max iterations
            else
                let mutable anyViolated = false

                // Repair each violated constraint by flipping x_i from 1→0
                problem.Constraints
                |> List.iter (fun constr ->
                    let lhs =
                        constr.Coefficients
                        |> List.indexed
                        |> List.sumBy (fun (i, ai) -> ai * float vars.[i])

                    if lhs > constr.Bound + 1e-9 then
                        anyViolated <- true
                        // Get variables that are 1, sorted by coefficient (largest first)
                        // Flipping these reduces LHS most
                        let candidates =
                            constr.Coefficients
                            |> List.indexed
                            |> List.filter (fun (i, ai) -> vars.[i] = 1 && ai > 0.0)
                            |> List.sortByDescending snd

                        let rec flipUntilSatisfied remaining currentLhs =
                            match remaining with
                            | _ when currentLhs <= constr.Bound + 1e-9 -> ()
                            | [] -> ()
                            | (i, ai) :: rest ->
                                vars.[i] <- 0
                                flipUntilSatisfied rest (currentLhs - ai)

                        flipUntilSatisfied candidates lhs)

                if anyViolated then
                    converge (iteration + 1)

        converge 0

        // Build the full bitstring: decision vars + optimal slack values
        let result = Array.zeroCreate totalVars

        // Copy repaired decision variables
        Array.blit vars 0 result 0 n

        // Set slack variables to their optimal values: s_k = b_k - a_k^T x
        // Encode s_k as binary: s_k = sum_t 2^t * z_{k,t}
        problem.Constraints
        |> List.indexed
        |> List.iter (fun (k, constr) ->
            let lhs =
                constr.Coefficients
                |> List.indexed
                |> List.sumBy (fun (i, ai) -> ai * float vars.[i])
            let slack = max 0.0 (constr.Bound - lhs) |> round |> int
            let tk = slackBitsForBound constr.Bound
            let slackStart = slackStartIndex n problem.Constraints k

            // Binary encode slack value
            for t in 0 .. tk - 1 do
                let bit = (slack >>> t) &&& 1
                let idx = slackStart + t
                if idx < totalVars then
                    result.[idx] <- bit)

        result

    // ========================================================================
    // DECOMPOSE / RECOMBINE HOOKS (Decision 10: identity stubs)
    // ========================================================================

    /// Decompose a Binary ILP problem into sub-problems.
    /// Currently identity — ILP constraints couple all variables.
    /// Future: partition by independent constraint groups.
    let decompose (problem: Problem) : Problem list = [ problem ]

    /// Recombine sub-solutions into a single solution. Currently identity.
    /// Handles empty list gracefully.
    let recombine (solutions: Solution list) : Solution =
        match solutions with
        | [] ->
            {
                Variables = [||]
                ObjectiveValue = System.Double.PositiveInfinity
                ConstraintsSatisfied = 0
                TotalConstraints = 0
                IsValid = false
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
            }
        | [ single ] -> single
        | _ ->
            solutions
            |> List.filter (fun s -> s.IsValid)
            |> List.sortBy (fun s -> s.ObjectiveValue)
            |> List.tryHead
            |> Option.defaultWith (fun () ->
                solutions |> List.minBy (fun s -> s.ObjectiveValue))

    // ========================================================================
    // QUANTUM SOLVERS (Rule 1: IQuantumBackend required)
    // ========================================================================

    /// Solve Binary ILP using QAOA with full configuration control.
    /// Supports automatic decomposition when problem exceeds backend capacity.
    let solveWithConfig
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (config: Config)
        : Result<Solution, QuantumError> =

        match validateProblem problem with
        | Error err -> Error err
        | Ok () ->
            let solveSingle (subProblem: Problem) =
                match toQubo subProblem with
                | Error err -> Error err
                | Ok qubo ->
                    let result =
                        if config.EnableOptimization then
                            executeQaoaWithOptimization backend qubo config
                            |> Result.map (fun (bits, optParams, converged) ->
                                (bits, Some optParams, Some converged))
                        else
                            executeQaoaWithGridSearch backend qubo config
                            |> Result.map (fun (bits, optParams) ->
                                (bits, Some optParams, None))

                    match result with
                    | Error err -> Error err
                    | Ok (bits, optParams, converged) ->
                        let decoded = decodeSolution subProblem bits
                        let needsRepair = not decoded.IsValid

                        let finalBits, wasRepaired =
                            if config.EnableConstraintRepair && needsRepair then
                                (repairConstraints subProblem bits, true)
                            else
                                (bits, false)

                        let solution = decodeSolution subProblem finalBits
                        Ok { solution with
                                BackendName = backend.Name
                                NumShots = config.FinalShots
                                WasRepaired = wasRepaired
                                OptimizedParameters = optParams
                                OptimizationConverged = converged }

            ProblemDecomposition.solveWithDecomposition
                backend problem estimateQubits decompose recombine solveSingle

    /// Solve Binary ILP using QAOA with default configuration.
    let solve
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (shots: int)
        : Result<Solution, QuantumError> =

        let config = { defaultConfig with FinalShots = shots }
        solveWithConfig backend problem config

    // ========================================================================
    // CLASSICAL SOLVER (Rule 1: private — not exposed without backend)
    // ========================================================================

    /// Classical greedy solver for Binary ILP comparison.
    /// Strategy: LP relaxation rounding — compute the fractional optimum,
    /// round each variable to {0,1}, then repair violated constraints
    /// by flipping the variable with the worst constraint-violation-to-objective
    /// ratio from 1→0.
    ///
    /// This is a simple heuristic; for production use, branch-and-bound
    /// or cutting-plane methods are preferred.
    let private solveClassical (problem: Problem) : Solution =
        if problem.ObjectiveCoeffs.IsEmpty then
            {
                Variables = [||]
                ObjectiveValue = 0.0
                ConstraintsSatisfied = 0
                TotalConstraints = problem.Constraints.Length
                IsValid = problem.Constraints.IsEmpty
                WasRepaired = false
                BackendName = "Classical Greedy"
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
            }
        else
            let n = problem.ObjectiveCoeffs.Length

            // Start with all variables = 0 (feasible for Ax <= b with b >= 0)
            // Greedily set variables to 1 if it improves objective without violating constraints
            let vars = Array.zeroCreate n

            // Sort variables by objective coefficient (ascending for minimization:
            // negative coefficients are good to set to 1)
            let sortedByBenefit =
                problem.ObjectiveCoeffs
                |> List.indexed
                |> List.sortBy snd

            sortedByBenefit
            |> List.iter (fun (i, ci) ->
                if ci < 0.0 then
                    // Setting x_i = 1 reduces objective — try it
                    vars.[i] <- 1
                    // Check if all constraints are still satisfied
                    let allSatisfied =
                        problem.Constraints
                        |> List.forall (fun c -> isConstraintSatisfied c vars)
                    if not allSatisfied then
                        vars.[i] <- 0  // Revert if it violates a constraint
            )

            let obj = computeObjective problem vars
            let satisfied = countSatisfiedConstraints problem vars

            {
                Variables = vars
                ObjectiveValue = obj
                ConstraintsSatisfied = satisfied
                TotalConstraints = problem.Constraints.Length
                IsValid = satisfied = problem.Constraints.Length
                WasRepaired = false
                BackendName = "Classical Greedy"
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
            }
