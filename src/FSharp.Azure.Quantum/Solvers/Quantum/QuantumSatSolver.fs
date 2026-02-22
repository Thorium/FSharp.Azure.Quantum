namespace FSharp.Azure.Quantum.Quantum

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.QaoaExecutionHelpers

/// Quantum MAX-SAT Solver (QAOA-based)
///
/// Problem: Given a Boolean formula in CNF (conjunctive normal form),
/// find an assignment that satisfies the maximum number of clauses.
///
/// QUBO Formulation:
///   Variables: x_i in {0,1} per Boolean variable (1 = true, 0 = false)
///   For each clause, add a penalty for the assignment that falsifies it.
///
///   1-literal clause (a): penalty lambda * (1-a)
///     where a = x_i if positive, a = (1-x_i) if negated
///
///   2-literal clause (a OR b): penalty lambda * (1-a)(1-b)
///     Expands to: lambda * (1 - a - b + a*b)
///
///   3-literal clause (a OR b OR c): Uses Rosenberg reduction.
///     (1-a)(1-b)(1-c) is cubic, so introduce auxiliary z:
///     penalty = lambda * [ (1-a)(1-b) - z*(1-a)(1-b) + z*(1-c) ]
///            = lambda * [ (1-a-b+ab) - z(1-a-b+ab) + z(1-c) ]
///     The auxiliary z replaces the product (1-a)(1-b) via:
///       z*(1-a)(1-b) with penalty for z != (1-a)(1-b).
///     Simplified Rosenberg: penalty = lambda * [(1-a-b+ab)(1-z) + z(1-c)]
///                                   = lambda * [1-a-b+ab - z+az+bz-abz + z-zc]
///                                   = lambda * [1-a-b+ab + az+bz-abz - zc]
///     Since QUBO is quadratic, we use the standard Rosenberg linearization:
///       Minimize (1-a)(1-b)(1-c) via auxiliary z representing (1-c):
///       = (1-a)(1-b)*z + M*(z - (1-c))^2   ... but this is also cubic in a,b,z.
///
///     Correct Rosenberg approach for 3-literal clauses:
///       Let w = a*b (introduce auxiliary for the product of two literals).
///       Then (1-a)(1-b)(1-c) = (1 - a - b + ab)(1-c) = (1 - a - b + w)(1-c)
///       with penalty P_aux = M*(w - a*b + ... ) to enforce w = a*b.
///
///     We use the standard quadratization:
///       f(a,b,c) = (1-a)(1-b)(1-c)
///       Introduce w for product ab. Penalty to enforce w = ab:
///         P_w = M*(3w + ab - 2aw - 2bw)  [Rosenberg penalty for w=ab]
///       Then (1-a-b+w)(1-c) = 1-c-a+ac-b+bc+w-wc
///       Total clause penalty = lambda*(1-c-a+ac-b+bc+w-wc) + lambda*M*(3w+ab-2aw-2bw)
///
/// Qubits: n + a, where n = number of Boolean variables,
///         a = number of auxiliary variables (one per 3+-literal clause)
///
/// RULE 1 COMPLIANCE:
/// All public solve functions require IQuantumBackend parameter.
/// Classical solver is private.
module QuantumSatSolver =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// A literal is a Boolean variable (by index) that may be negated.
    type Literal = {
        /// Index of the Boolean variable (0-based)
        Variable: int
        /// True if the literal is negated (NOT x_i)
        IsNegated: bool
    }

    /// A clause is a disjunction (OR) of literals.
    type Clause = {
        /// The literals in this clause
        Literals: Literal list
    }

    /// MAX-SAT problem in CNF form.
    type Problem = {
        /// Number of Boolean variables
        NumVariables: int
        /// List of clauses (each is a disjunction of literals)
        Clauses: Clause list
    }

    /// MAX-SAT solution.
    type Solution = {
        /// Variable assignments (true/false for each variable)
        Assignment: bool[]
        /// Number of satisfied clauses
        SatisfiedClauses: int
        /// Total number of clauses
        TotalClauses: int
        /// Whether all clauses are satisfied
        AllSatisfied: bool
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
    // QUBIT ESTIMATION (Decision 11)
    // ========================================================================

    /// Count the number of auxiliary variables needed for Rosenberg reduction.
    /// 3-literal clauses need 1 auxiliary each.
    /// 4+-literal clauses need (n-2) auxiliaries each (iterative chaining).
    let private countAuxiliaryVariables (problem: Problem) : int =
        problem.Clauses
        |> List.sumBy (fun c ->
            let n = c.Literals.Length
            if n >= 4 then n - 2
            elif n = 3 then 1
            else 0)

    /// Estimate the number of qubits required.
    /// n original variables + auxiliaries from Rosenberg reduction.
    let estimateQubits (problem: Problem) : int =
        problem.NumVariables + countAuxiliaryVariables problem

    // ========================================================================
    // QUBO CONSTRUCTION (Decision 9: sparse internally, Decision 5: dense output)
    // ========================================================================

    /// Get the effective QUBO variable index for a literal.
    /// For positive literal x_i: variable index is i.
    /// The negation is handled during penalty expansion, not here.
    let private literalVariable (lit: Literal) : int = lit.Variable

    /// Evaluate a literal's "active" form in QUBO terms:
    /// positive literal → x_i (qubit value), negated → (1 - x_i)
    /// Returns (coefficient, constant) for the linear expression.
    /// active_value = constant + coefficient * x_variable
    let private literalLinear (lit: Literal) : float * float =
        if lit.IsNegated then
            // (1 - x_i): coefficient = -1, constant = 1
            (-1.0, 1.0)
        else
            // x_i: coefficient = 1, constant = 0
            (1.0, 0.0)

    /// Add a single QUBO term to the accumulator map.
    let private addTerm (i: int) (j: int) (value: float) (qubo: Map<int * int, float>) =
        if abs value < 1e-15 then qubo
        else Qubo.combineTerms (i, j) value qubo

    /// Build QUBO penalty for a 1-literal clause: penalty * (1 - a)
    /// where a is the effective literal value.
    /// If positive: penalty * (1 - x_i) = penalty - penalty*x_i
    ///   → Q[i,i] -= penalty (linear), constant = penalty (ignored)
    /// If negated: penalty * (1 - (1-x_i)) = penalty * x_i
    ///   → Q[i,i] += penalty (linear)
    let private build1LiteralPenalty
        (penalty: float)
        (lit: Literal)
        (qubo: Map<int * int, float>)
        : Map<int * int, float> =
        let i = literalVariable lit
        let (coeff, constant) = literalLinear lit
        // penalty * (1 - (constant + coeff * x_i))
        // = penalty * ((1 - constant) - coeff * x_i)
        // = penalty * (1 - constant) - penalty * coeff * x_i
        // Linear term on diagonal: -penalty * coeff
        qubo |> addTerm i i (-penalty * coeff)

    /// Build QUBO penalty for a 2-literal clause: penalty * (1-a)(1-b)
    /// where a, b are effective literal values.
    ///
    /// Let a = ca*x_i + da, b = cb*x_j + db (ca,da from literalLinear)
    /// (1-a) = (1-da) - ca*x_i
    /// (1-b) = (1-db) - cb*x_j
    /// Product: ((1-da) - ca*x_i) * ((1-db) - cb*x_j)
    ///        = (1-da)(1-db) - (1-da)*cb*x_j - ca*(1-db)*x_i + ca*cb*x_i*x_j
    /// Multiply by penalty:
    ///   constant: penalty*(1-da)(1-db)  [ignored]
    ///   linear x_j: -penalty*(1-da)*cb  → Q[j,j]
    ///   linear x_i: -penalty*ca*(1-db)  → Q[i,i]
    ///   quadratic: penalty*ca*cb         → Q[i,j] (symmetric split: Q[i,j] and Q[j,i])
    let private build2LiteralPenalty
        (penalty: float)
        (lit1: Literal)
        (lit2: Literal)
        (qubo: Map<int * int, float>)
        : Map<int * int, float> =
        let i = literalVariable lit1
        let j = literalVariable lit2
        let (ca, da) = literalLinear lit1
        let (cb, db) = literalLinear lit2

        let oneMinusDa = 1.0 - da
        let oneMinusDb = 1.0 - db

        let qubo = qubo |> addTerm i i (-penalty * ca * oneMinusDb)
        let qubo = qubo |> addTerm j j (-penalty * cb * oneMinusDa)

        // Quadratic term: penalty * ca * cb
        // Handle same-variable case (i = j) → becomes diagonal
        if i = j then
            qubo |> addTerm i i (penalty * ca * cb)
        else
            let halfQuad = penalty * ca * cb / 2.0
            qubo |> addTerm i j halfQuad
                 |> addTerm j i halfQuad

    /// Build Rosenberg penalty to enforce w = (1-a)(1-b), i.e., w = A*B.
    /// Penalty: M*(3w + AB - 2Aw - 2Bw) where A=(1-a), B=(1-b).
    /// This is the shared core used by both 3-literal and 4+-literal reductions.
    let private buildRosenbergPenalty
        (penalty: float)
        (lit1: Literal)
        (lit2: Literal)
        (auxIndex: int)
        (qubo: Map<int * int, float>)
        : Map<int * int, float> =
        let i = literalVariable lit1
        let j = literalVariable lit2
        let w = auxIndex

        let (ca, da) = literalLinear lit1
        let (cb, db) = literalLinear lit2

        let a0 = 1.0 - da  // constant part of A = (1-a)
        let a1 = -ca        // coefficient of x_i in A
        let b0 = 1.0 - db
        let b1 = -cb

        let m = 1.0

        // 3w: linear on w diagonal
        let qubo = qubo |> addTerm w w (penalty * m * 3.0)

        // AB = (a0 + a1*x_i)(b0 + b1*x_j)
        //    = a0*b0 + a0*b1*x_j + a1*b0*x_i + a1*b1*x_i*x_j
        let qubo = qubo |> addTerm j j (penalty * m * a0 * b1)
        let qubo = qubo |> addTerm i i (penalty * m * a1 * b0)
        let qubo =
            if i = j then
                qubo |> addTerm i i (penalty * m * a1 * b1)
            else
                let half = penalty * m * a1 * b1 / 2.0
                qubo |> addTerm i j half |> addTerm j i half

        // -2Aw = -2*(a0 + a1*x_i)*w
        let qubo = qubo |> addTerm w w (penalty * m * (-2.0) * a0)
        let qubo =
            if i = w then
                qubo |> addTerm w w (penalty * m * (-2.0) * a1)
            else
                let half = penalty * m * (-2.0) * a1 / 2.0
                qubo |> addTerm i w half |> addTerm w i half

        // -2Bw = -2*(b0 + b1*x_j)*w
        let qubo = qubo |> addTerm w w (penalty * m * (-2.0) * b0)
        let qubo =
            if j = w then
                qubo |> addTerm w w (penalty * m * (-2.0) * b1)
            else
                let half = penalty * m * (-2.0) * b1 / 2.0
                qubo |> addTerm j w half |> addTerm w j half

        qubo

    /// Build QUBO penalty for a 3-literal clause using Rosenberg reduction.
    ///
    /// For clause (a OR b OR c), the unsatisfied penalty is (1-a)(1-b)(1-c).
    /// This is cubic, so we introduce auxiliary variable w to represent the
    /// product of (1-a)(1-b).
    ///
    /// Let A = (1-a), B = (1-b), C = (1-c).
    /// We want to minimize A*B*C.
    /// Introduce w to represent A*B. Then A*B*C = w*C.
    /// To enforce w = A*B, add Rosenberg penalty: M*(3w + AB - 2Aw - 2Bw)
    ///   which is minimized when w = AB (w=0 if A=0 or B=0, w=1 if A=B=1).
    ///
    /// Total: penalty * w*C + penalty * rosenbergM * (3w + AB - 2Aw - 2Bw)
    ///
    /// auxIndex: the QUBO variable index for the auxiliary w.
    let private build3LiteralPenalty
        (penalty: float)
        (lit1: Literal)
        (lit2: Literal)
        (lit3: Literal)
        (auxIndex: int)
        (qubo: Map<int * int, float>)
        : Map<int * int, float> =
        let k = literalVariable lit3
        let w = auxIndex

        let (cc, dc) = literalLinear lit3
        let c0 = 1.0 - dc
        let c1 = -cc

        // --- Part 1: penalty * w * C = penalty * w * (c0 + c1*x_k) ---
        let qubo = qubo |> addTerm w w (penalty * c0)
        let qubo =
            if w = k then
                qubo |> addTerm w w (penalty * c1)
            else
                let half = penalty * c1 / 2.0
                qubo |> addTerm w k half |> addTerm k w half

        // --- Part 2: Rosenberg penalty to enforce w = (1-a)(1-b) ---
        buildRosenbergPenalty penalty lit1 lit2 auxIndex qubo

    /// Build the complete QUBO as a sparse map.
    ///
    /// Each clause contributes a penalty for the assignment that falsifies it.
    /// Clauses with 3+ literals use Rosenberg reduction with auxiliary variables.
    /// Clauses with 4+ literals are reduced to 3-literal sub-penalties recursively
    /// by grouping the first two literals with an auxiliary, then treating
    /// (aux, remaining...) as a smaller clause.
    let private buildQuboMap (problem: Problem) : Map<int * int, float> =
        // Penalty per clause = 1.0 (all clauses equally weighted)
        let penalty = 1.0

        let mutable nextAux = problem.NumVariables

        problem.Clauses
        |> List.fold (fun qubo clause ->
            match clause.Literals with
            | [] -> qubo  // Empty clause: always false, skip
            | [ a ] ->
                build1LiteralPenalty penalty a qubo
            | [ a; b ] ->
                build2LiteralPenalty penalty a b qubo
            | [ a; b; c ] ->
                let auxIdx = nextAux
                nextAux <- nextAux + 1
                build3LiteralPenalty penalty a b c auxIdx qubo
            | lits ->
                // For 4+ literals: chain Rosenberg reductions.
                // Group first two into an auxiliary, then recurse.
                // (1-a1)(1-a2)...(1-an) → introduce w1 for (1-a1)(1-a2),
                // then treat (w1, a3, ..., an) as a shorter clause.
                // We reduce to 3-literal form iteratively.
                let rec reduce (remainingLits: Literal list) (q: Map<int * int, float>) =
                    match remainingLits with
                    | [] -> q  // Should not happen
                    | [ a ] -> build1LiteralPenalty penalty a q
                    | [ a; b ] -> build2LiteralPenalty penalty a b q
                    | [ a; b; c ] ->
                        let auxIdx = nextAux
                        nextAux <- nextAux + 1
                        build3LiteralPenalty penalty a b c auxIdx q
                    | a :: b :: rest ->
                        // Introduce auxiliary w for product of (1-a)(1-b)
                        let auxIdx = nextAux
                        nextAux <- nextAux + 1
                        // Add Rosenberg penalty to enforce w = (1-a)(1-b)
                        let q = buildRosenbergPenalty penalty a b auxIdx q
                        // Now w represents (1-a)(1-b): w=1 when both a,b are false.
                        // literalLinear with IsNegated=true gives effective=(1-x_w),
                        // so (1 - effective) = x_w. This is correct.
                        let auxLit = { Variable = auxIdx; IsNegated = true }
                        reduce (auxLit :: rest) q
                reduce lits qubo
        ) Map.empty

    /// Compute the actual total number of QUBO variables (original + auxiliaries).
    /// Delegates to estimateQubits which now correctly counts auxiliaries.
    let private computeTotalQubits (problem: Problem) : int =
        estimateQubits problem

    /// Validate a SAT problem, returning Error if invalid.
    let private validateProblem (problem: Problem) : Result<unit, QuantumError> =
        if problem.Clauses.IsEmpty then
            Error (QuantumError.ValidationError ("clauses", "Problem has no clauses"))
        elif problem.NumVariables <= 0 then
            Error (QuantumError.ValidationError ("numVariables", "Number of variables must be positive"))
        elif problem.Clauses |> List.exists (fun c -> c.Literals.IsEmpty) then
            Error (QuantumError.ValidationError ("clause", "Empty clause found"))
        elif problem.Clauses |> List.exists (fun c ->
                c.Literals |> List.exists (fun l ->
                    l.Variable < 0 || l.Variable >= problem.NumVariables)) then
            Error (QuantumError.ValidationError ("variable", "Variable index out of range"))
        else
            Ok ()

    /// Convert problem to dense QUBO matrix.
    /// Returns Result to follow the canonical pattern (validates inputs).
    let toQubo (problem: Problem) : Result<float[,], QuantumError> =
        match validateProblem problem with
        | Error err -> Error err
        | Ok () ->
            let n = computeTotalQubits problem
            let quboMap = buildQuboMap problem
            Ok (Qubo.toDenseArray n quboMap)

    // ========================================================================
    // SOLUTION DECODING & VALIDATION
    // ========================================================================

    /// Evaluate whether a clause is satisfied by the given Boolean assignment.
    let private evaluateClause (assignment: bool[]) (clause: Clause) : bool =
        clause.Literals
        |> List.exists (fun lit ->
            let value = assignment.[lit.Variable]
            if lit.IsNegated then not value else value)

    /// Count how many clauses are satisfied by the given assignment.
    let private countSatisfied (problem: Problem) (assignment: bool[]) : int =
        problem.Clauses
        |> List.filter (evaluateClause assignment)
        |> List.length

    /// Validate a bitstring for this problem. Checks that the bitstring
    /// has enough entries for all variables (including auxiliaries).
    let isValid (problem: Problem) (bits: int[]) : bool =
        bits.Length = computeTotalQubits problem

    /// Decode a bitstring into a Solution.
    /// Only the first NumVariables bits represent the actual Boolean assignment;
    /// the remaining bits are auxiliary variables from Rosenberg reduction.
    let private decodeSolution (problem: Problem) (bits: int[]) : Solution =
        let assignment =
            Array.init problem.NumVariables (fun i ->
                if i < bits.Length then bits.[i] = 1 else false)

        let satisfied = countSatisfied problem assignment
        {
            Assignment = assignment
            SatisfiedClauses = satisfied
            TotalClauses = problem.Clauses.Length
            AllSatisfied = satisfied = problem.Clauses.Length
            WasRepaired = false
            BackendName = ""
            NumShots = 0
            OptimizedParameters = None
            OptimizationConverged = None
        }

    // ========================================================================
    // CONSTRAINT REPAIR (recursive, idiomatic F#)
    // ========================================================================

    /// Count how many currently-unsatisfied clauses would become satisfied
    /// if variable at index varIdx were flipped.
    let private flipGain (problem: Problem) (assignment: bool[]) (varIdx: int) : int =
        let flipped = Array.copy assignment
        flipped.[varIdx] <- not flipped.[varIdx]
        let oldSatisfied = countSatisfied problem assignment
        let newSatisfied = countSatisfied problem flipped
        newSatisfied - oldSatisfied

    /// Repair a solution by greedily flipping variables to maximize
    /// satisfied clauses. Iterates until no single flip improves the count.
    let private repairConstraints (problem: Problem) (bits: int[]) : int[] =
        let assignment =
            Array.init problem.NumVariables (fun i ->
                if i < bits.Length then bits.[i] = 1 else false)

        let rec improve (current: bool[]) =
            let gains =
                [ 0 .. problem.NumVariables - 1 ]
                |> List.map (fun v -> (v, flipGain problem current v))
                |> List.filter (fun (_, g) -> g > 0)
                |> List.sortByDescending snd

            match gains with
            | [] -> current  // No beneficial flip
            | (bestVar, _) :: _ ->
                let updated = Array.copy current
                updated.[bestVar] <- not updated.[bestVar]
                improve updated

        let repaired = improve assignment

        // Rebuild full bitstring (original vars + aux set to 0)
        let totalQubits = computeTotalQubits problem
        Array.init totalQubits (fun i ->
            if i < problem.NumVariables then
                if repaired.[i] then 1 else 0
            else
                0)  // Auxiliary variables reset to 0 after repair

    // ========================================================================
    // DECOMPOSE / RECOMBINE HOOKS (Decision 10: identity stubs)
    // ========================================================================

    /// Decompose a SAT problem into sub-problems.
    /// Currently identity — SAT clause dependencies make partitioning non-trivial.
    /// Future: partition by independent variable groups (disjoint clause sets).
    let decompose (problem: Problem) : Problem list = [ problem ]

    /// Recombine sub-solutions into a single solution. Currently identity.
    /// Handles empty list gracefully.
    let recombine (solutions: Solution list) : Solution =
        match solutions with
        | [] ->
            {
                Assignment = [||]
                SatisfiedClauses = 0
                TotalClauses = 0
                AllSatisfied = false
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
            }
        | [ single ] -> single
        | _ -> solutions |> List.maxBy (fun s -> s.SatisfiedClauses)

    // ========================================================================
    // QUANTUM SOLVERS (Rule 1: IQuantumBackend required)
    // ========================================================================

    /// Solve MAX-SAT using QAOA with full configuration control.
    /// Supports automatic decomposition when problem exceeds backend capacity.
    [<Obsolete("Use solveWithConfigAsync for non-blocking execution against cloud backends")>]
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
                        let needsRepair =
                            let assignment =
                                Array.init subProblem.NumVariables (fun i ->
                                    if i < bits.Length then bits.[i] = 1 else false)
                            countSatisfied subProblem assignment < subProblem.Clauses.Length

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

    /// Solve MAX-SAT using QAOA with full configuration control (async).
    /// Wraps the synchronous solveWithConfig in a task; will become truly async
    /// once ProblemDecomposition supports async solve functions.
    let solveWithConfigAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (config: Config)
        (cancellationToken: CancellationToken)
        : Task<Result<Solution, QuantumError>> = task {
        cancellationToken.ThrowIfCancellationRequested()
        return solveWithConfig backend problem config
    }

    /// Solve MAX-SAT using QAOA with default configuration.
    [<Obsolete("Use solveWithConfigAsync for non-blocking execution against cloud backends")>]
    let solve
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (shots: int)
        : Result<Solution, QuantumError> =

        let config = { defaultConfig with FinalShots = shots }
        solveWithConfigAsync backend problem config CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    // ========================================================================
    // CLASSICAL SOLVER (Rule 1: private — not exposed without backend)
    // ========================================================================

    /// Classical greedy MAX-SAT solver for comparison.
    /// Strategy: start with all variables false, then greedily flip the
    /// variable that satisfies the most additional clauses, repeating
    /// until no single flip improves the count.
    let private solveClassical (problem: Problem) : Solution =
        if problem.Clauses.IsEmpty || problem.NumVariables <= 0 then
            {
                Assignment = Array.create (max 0 problem.NumVariables) false
                SatisfiedClauses = 0
                TotalClauses = problem.Clauses.Length
                AllSatisfied = problem.Clauses.IsEmpty
                WasRepaired = false
                BackendName = "Classical Greedy"
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
            }
        else
            let initial = Array.create problem.NumVariables false

            let rec improve (current: bool[]) =
                let gains =
                    [ 0 .. problem.NumVariables - 1 ]
                    |> List.map (fun v -> (v, flipGain problem current v))
                    |> List.filter (fun (_, g) -> g > 0)
                    |> List.sortByDescending snd

                match gains with
                | [] -> current
                | (bestVar, _) :: _ ->
                    let updated = Array.copy current
                    updated.[bestVar] <- not updated.[bestVar]
                    improve updated

            let optimized = improve initial
            let satisfied = countSatisfied problem optimized
            {
                Assignment = optimized
                SatisfiedClauses = satisfied
                TotalClauses = problem.Clauses.Length
                AllSatisfied = satisfied = problem.Clauses.Length
                WasRepaired = false
                BackendName = "Classical Greedy"
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
            }
