namespace FSharp.Azure.Quantum.Quantum

open System
open System.Threading
open System.Threading.Tasks
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
///   Decision variables: x_0 .. x_{n-1} (the original BIP variables), first in the bitstring.
///
///   Integer form: each constraint's coefficients are rescaled to integers a_k without a
///   common divisor (Qubo.tryScaleToIntegers, factor f_k) and its bound becomes
///   b_k = floor(f_k * Bound) (with the 1e-9 tolerance of isValid); an integer left side is
///   at most f_k * Bound exactly when it is at most b_k. Coefficients and bounds may be
///   negative or fractional; coefficients without a short decimal form are a
///   ValidationError.
///
///   With m_k = sum of the negative a_{k,i} (the smallest possible left side) and
///   M_k = sum of the positive a_{k,i} (the largest):
///     b_k <  m_k : the constraint cannot be satisfied → ValidationError
///     b_k >= M_k : the constraint holds for every x → no slack bits, no penalty
///     otherwise  : slack s_k = sum_t w_{k,t} * z_{k,t} with the weights
///                  Qubo.boundedSlackWeights (b_k - m_k), whose subset sums are exactly
///                  0 .. b_k - m_k, so a_k^T x + s_k = b_k has a solution exactly when
///                  a_k^T x <= b_k
///
///   Objective (diagonal):
///     qubo[i,i] += c_i
///
///   Each encoded constraint k:
///     Penalty: lambda * (a_k^T x + s_k - b_k)^2
///     (Qubo.squaredLinearPenalty; each pair term is split evenly over (i,j) and (j,i))
///
///   lambda = sum|c_i| + max|c_i|, or 1 when every c_i is 0. Up to the constant
///   lambda * sum b_k^2 that the QUBO drops, an infeasible x leaves an integer residual of
///   magnitude >= 1 in some constraint, so its energy is at least
///   (sum of negative c_i) + lambda > (sum of positive c_i) >= every feasible objective,
///   while a feasible x with matching slack has energy c^T x. Every QUBO minimum is
///   therefore a feasible optimum.
///
/// Qubits: n + the number of slack weights of every encoded constraint
///         (about log2(b_k - m_k + 1) each).
///
/// Scaling concern: Slack bits grow logarithmically with a constraint's range b_k - m_k in
/// integer units, so coefficients with many decimal places cost more qubits.
/// Practical for ~10 variables with ~5 constraints.
///
/// The solver returns the lowest-energy sample. When that sample violates a constraint and
/// EnableConstraintRepair is set, a greedy repair flips variables to remove the violation:
/// the solution then reports WasRepaired = true, and IsValid = false when the repair found
/// no feasible assignment.
///
/// RULE 1 COMPLIANCE:
/// All public solve functions require IQuantumBackend parameter.
/// Classical solver is private.
module QuantumBinaryILPSolver =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// A single linear inequality constraint: a^T x <= b
    type Constraint =
        {
            /// Coefficients a_i for each decision variable (any sign)
            Coefficients: float list
            /// Right-hand side bound (any sign, at least the smallest possible left side)
            Bound: float
        }

    /// Binary ILP problem definition
    type Problem =
        {
            /// Objective function coefficients c_i (minimize c^T x)
            ObjectiveCoeffs: float list
            /// Linear inequality constraints (a^T x <= b)
            Constraints: Constraint list
        }

    /// Binary ILP solution
    type Solution =
        {
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
            /// Standing of this solution among the final samples; None when no single sampling run produced it
            Sampling: QaoaExecutionHelpers.SampleStatistics option
            /// How the problem was split into circuits that fit the backend; None when it ran as one circuit
            Split: QaoaExecutionHelpers.SplitReport option
        }

    // ========================================================================
    // CONFIGURATION (type alias for unified config)
    // ========================================================================

    type Config = QaoaSolverConfig

    let defaultConfig: Config = QaoaExecutionHelpers.defaultConfig
    let fastConfig: Config = QaoaExecutionHelpers.fastConfig
    let highQualityConfig: Config = QaoaExecutionHelpers.highQualityConfig

    // ========================================================================
    // INTEGER FORM OF A CONSTRAINT
    // ========================================================================

    /// Tolerance of the comparison a^T x <= Bound: 1e-9, relative for bounds above 1. It
    /// absorbs the rounding of summed fractional coefficients (0.1 + 0.2 against 0.3).
    let private boundTolerance (bound: float) : float = 1e-9 * max 1.0 (abs bound)

    /// A constraint in the integer form the QUBO encodes.
    type private EncodedConstraint =
        /// The constraint holds for every assignment: no slack bits, no penalty.
        | AlwaysSatisfied
        /// Σ aᵢ·xᵢ + Σ wₜ·zₜ = bound, with integer coefficients aᵢ, an integer bound and
        /// slack weights wₜ whose subset sums are exactly 0 .. bound − (smallest left side).
        | WithSlack of coefficients: int list * bound: float * slackWeights: int list

    /// Number of slack bits of an encoded constraint.
    let private slackBitCount (encoded: EncodedConstraint) : int =
        match encoded with
        | AlwaysSatisfied -> 0
        | WithSlack(_, _, slackWeights) -> slackWeights.Length

    /// Integer form of constraint `index`: coefficients rescaled to integers by a factor f,
    /// bound floor(f·(Bound + boundTolerance)). ValidationError when the coefficients have no
    /// common integer scale, when the bound is below the smallest possible left side, or when
    /// the slack range exceeds the 32-bit integer range.
    let private encodeConstraint (index: int) (constr: Constraint) : Result<EncodedConstraint, QuantumError> =
        if Double.IsNaN constr.Bound then
            Error(QuantumError.ValidationError("bound", $"Constraint {index}: the bound is not a number"))
        else
            match Qubo.tryScaleToIntegers constr.Coefficients with
            | None ->
                Error(
                    QuantumError.ValidationError(
                        "coefficients",
                        $"Constraint {index}: the coefficients cannot be rescaled to integers "
                        + "(each needs at most 6 decimal places and a scaled magnitude of at most 1e9)"
                    )
                )
            | Some(coefficients, factor) ->
                let minLhs = coefficients |> List.sumBy (fun a -> int64 (min a 0))
                let maxLhs = coefficients |> List.sumBy (fun a -> int64 (max a 0))
                let scaledBound = constr.Bound * factor

                // The same tolerance as isConstraintSatisfied, in integer units: the QUBO
                // and the classical check accept the same assignments.
                let bound =
                    if Double.IsInfinity scaledBound then
                        scaledBound
                    else
                        floor (scaledBound + boundTolerance constr.Bound * factor)

                if bound >= float maxLhs then
                    Ok AlwaysSatisfied
                elif bound < float minLhs then
                    Error(
                        QuantumError.ValidationError(
                            "bound",
                            $"Constraint {index} cannot be satisfied: its left side is at least "
                            + $"{float minLhs / factor}, above the bound {constr.Bound}"
                        )
                    )
                else
                    let range = int64 bound - minLhs

                    if range > int64 Int32.MaxValue then
                        Error(
                            QuantumError.ValidationError(
                                "coefficients",
                                $"Constraint {index}: the slack range {range} in integer units is too large to encode"
                            )
                        )
                    else
                        Ok(WithSlack(coefficients, bound, Qubo.boundedSlackWeights (int range)))

    // ========================================================================
    // QUBIT ESTIMATION (Decision 11)
    // ========================================================================

    /// Estimate the number of qubits required: n decision variables plus the slack bits of
    /// every encoded constraint (see the module comment). A constraint that fails validation
    /// counts no slack bits.
    let estimateQubits (problem: Problem) : int =
        let n = problem.ObjectiveCoeffs.Length

        let slackBits =
            problem.Constraints
            |> List.mapi encodeConstraint
            |> List.sumBy (Result.map slackBitCount >> Result.defaultValue 0)

        n + slackBits

    // ========================================================================
    // QUBO CONSTRUCTION (Decision 9: sparse internally, Decision 5: dense output)
    // ========================================================================

    /// Penalty weight λ = Σ|cᵢ| + max|cᵢ|, or 1 when every cᵢ is 0. λ exceeds the whole range
    /// of the objective, so one unit of integer constraint residual outweighs any objective
    /// gain.
    let private penaltyWeight (problem: Problem) : float =
        let magnitudes = problem.ObjectiveCoeffs |> List.map abs
        let largest = magnitudes |> List.fold max 0.0

        if largest > 0.0 then List.sum magnitudes + largest else 1.0

    /// Index of each constraint's first slack bit: slack bits follow the n decision bits in
    /// constraint order.
    let private slackStarts (n: int) (encoded: EncodedConstraint list) : int list =
        encoded
        |> List.scan (fun start constr -> start + slackBitCount constr) n
        |> List.truncate encoded.Length

    /// Build the QUBO as a sparse upper-triangular map.
    /// Encodes: minimize c^T x + lambda * sum_k (a_k^T x + s_k - b_k)^2 over the constraints
    /// that need slack.
    let private buildQuboMap (problem: Problem) (encoded: EncodedConstraint list) : Map<int * int, float> =
        let n = problem.ObjectiveCoeffs.Length
        let lambda = penaltyWeight problem

        // --- Objective: c^T x → diagonal terms ---
        let objectiveTerms =
            problem.ObjectiveCoeffs
            |> List.indexed
            |> List.fold
                (fun acc (i, ci) ->
                    if abs ci < 1e-15 then
                        acc
                    else
                        acc |> Qubo.combineTerms (i, i) ci)
                Map.empty<int * int, float>

        // --- Constraint penalties: lambda * (sum_i a_{k,i} x_i + sum_t w_{k,t} z_{k,t} - b_k)^2 ---
        List.zip encoded (slackStarts n encoded)
        |> List.fold
            (fun acc (constr, slackStart) ->
                match constr with
                | AlwaysSatisfied -> acc
                | WithSlack(coefficients, bound, slackWeights) ->
                    let decisionTerms =
                        coefficients
                        |> List.indexed
                        |> List.filter (fun (_, a) -> a <> 0)
                        |> List.map (fun (i, a) -> (i, float a))

                    let slackTerms =
                        slackWeights |> List.mapi (fun t weight -> (slackStart + t, float weight))

                    Qubo.squaredLinearPenalty lambda (decisionTerms @ slackTerms) (-bound)
                    |> Map.fold (fun combined key value -> Qubo.combineTerms key value combined) acc)
            objectiveTerms

    /// Validate a Binary ILP problem and return the integer form of its constraints,
    /// or the first error.
    let private validateProblem (problem: Problem) : Result<EncodedConstraint list, QuantumError> =
        let numVariables = problem.ObjectiveCoeffs.Length

        if numVariables = 0 then
            Error(QuantumError.ValidationError("objectiveCoeffs", "Problem has no decision variables"))
        elif
            problem.ObjectiveCoeffs
            |> List.exists (fun c -> Double.IsNaN c || Double.IsInfinity c)
        then
            Error(QuantumError.ValidationError("objectiveCoeffs", "Objective coefficients must be finite"))
        elif
            problem.Constraints
            |> List.exists (fun c -> c.Coefficients.Length <> numVariables)
        then
            Error(
                QuantumError.ValidationError(
                    "coefficients",
                    "All constraint coefficient vectors must have the same length as the objective"
                )
            )
        else
            (problem.Constraints |> List.mapi encodeConstraint, Ok [])
            ||> List.foldBack (fun encoded acc ->
                match encoded, acc with
                | Error err, _ -> Error err
                | Ok _, Error err -> Error err
                | Ok constr, Ok rest -> Ok(constr :: rest))

    /// Dense symmetric QUBO of a validated problem: each pair term is split evenly over
    /// (i, j) and (j, i).
    let private denseQubo (problem: Problem) (encoded: EncodedConstraint list) : float[,] =
        let totalVars =
            problem.ObjectiveCoeffs.Length + (encoded |> List.sumBy slackBitCount)

        let dense = Array2D.zeroCreate totalVars totalVars

        for KeyValue((i, j), value) in buildQuboMap problem encoded do
            if i = j then
                dense.[i, i] <- dense.[i, i] + value
            else
                dense.[i, j] <- dense.[i, j] + value / 2.0
                dense.[j, i] <- dense.[j, i] + value / 2.0

        dense

    /// Convert problem to dense QUBO matrix.
    /// Returns Result to follow the canonical pattern (validates inputs).
    let toQubo (problem: Problem) : Result<float[,], QuantumError> =
        validateProblem problem |> Result.map (denseQubo problem)

    // ========================================================================
    // SOLUTION DECODING & VALIDATION
    // ========================================================================

    /// Compute the objective value c^T x.
    let private computeObjective (problem: Problem) (vars: int[]) : float =
        problem.ObjectiveCoeffs
        |> List.indexed
        |> List.sumBy (fun (i, ci) -> ci * float vars.[i])

    /// Check whether a constraint is satisfied: a^T x <= b.
    let private isConstraintSatisfied (vars: int[]) (constr: Constraint) : bool =
        let lhs =
            constr.Coefficients
            |> List.indexed
            |> List.sumBy (fun (i, ai) -> ai * float vars.[i])

        lhs <= constr.Bound + boundTolerance constr.Bound

    /// Count the number of satisfied constraints.
    let private countSatisfiedConstraints (problem: Problem) (vars: int[]) : int =
        problem.Constraints |> List.filter (isConstraintSatisfied vars) |> List.length

    /// Validate a bitstring for this problem.
    /// Checks: correct length (estimateQubits) and all constraints satisfied by the decision
    /// bits, which come first; the slack bits are not checked.
    let isValid (problem: Problem) (bits: int[]) : bool =
        let totalVars = estimateQubits problem

        bits.Length = totalVars
        && (let n = problem.ObjectiveCoeffs.Length
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
            Sampling = None
            Split = None
        }

    // ========================================================================
    // CONSTRAINT REPAIR (greedy, idiomatic F#)
    // ========================================================================

    /// Total constraint violation Σ_k max(0, a_k^T x − b_k) of an assignment.
    let private totalViolation (problem: Problem) (vars: int[]) : float =
        problem.Constraints
        |> List.sumBy (fun constr ->
            let lhs =
                constr.Coefficients
                |> List.indexed
                |> List.sumBy (fun (i, ai) -> ai * float vars.[i])

            max 0.0 (lhs - constr.Bound))

    /// Repair an invalid Binary ILP assignment by greedy descent on the total violation:
    /// repeatedly flip the one variable (1→0 or 0→1) that lowers it the most, ties going to
    /// the smaller objective change, until every constraint holds or no single flip lowers
    /// it. With coefficients of both signs a flip that helps one constraint can hurt another,
    /// so the result can still be infeasible; the caller re-checks it.
    /// The slack bits are set to the values that match the repaired assignment.
    let private repairConstraints (problem: Problem) (encoded: EncodedConstraint list) (bits: int[]) : int[] =
        let n = problem.ObjectiveCoeffs.Length
        let objective = problem.ObjectiveCoeffs |> List.toArray
        let vars = Array.copy bits.[0 .. n - 1]

        let rec descend (current: float) =
            if not (problem.Constraints |> List.forall (isConstraintSatisfied vars)) then
                let improvingFlips =
                    [|
                        for i in 0 .. n - 1 do
                            vars.[i] <- 1 - vars.[i]
                            let violation = totalViolation problem vars

                            let objectiveChange = if vars.[i] = 1 then objective.[i] else -objective.[i]

                            vars.[i] <- 1 - vars.[i]

                            if violation < current - 1e-12 then
                                yield (violation, objectiveChange, i)
                    |]

                if improvingFlips.Length > 0 then
                    let (violation, _, i) = Array.min improvingFlips
                    vars.[i] <- 1 - vars.[i]
                    descend violation

        descend (totalViolation problem vars)

        // Full bitstring: decision variables, then each constraint's slack s_k = b_k − a_k^T x.
        let result = Array.zeroCreate (n + (encoded |> List.sumBy slackBitCount))
        Array.blit vars 0 result 0 n

        List.zip encoded (slackStarts n encoded)
        |> List.iter (fun (constr, slackStart) ->
            match constr with
            | AlwaysSatisfied -> ()
            | WithSlack(coefficients, bound, slackWeights) ->
                let lhs =
                    coefficients
                    |> List.indexed
                    |> List.sumBy (fun (i, a) -> int64 a * int64 vars.[i])

                // Largest weight first: each weight is at most one more than the sum of the
                // smaller ones, so the greedy choice reaches every value in 0 .. range.
                slackWeights
                |> List.indexed
                |> List.sortByDescending snd
                |> List.fold
                    (fun remaining (t, weight) ->
                        if int64 weight <= remaining then
                            result.[slackStart + t] <- 1
                            remaining - int64 weight
                        else
                            remaining)
                    (int64 bound - lhs)
                |> ignore)

        result

    /// repairConstraints for a bitstring of a problem that passes validation.
    let internal repair (problem: Problem) (bits: int[]) : Result<int[], QuantumError> =
        validateProblem problem
        |> Result.map (fun encoded -> repairConstraints problem encoded bits)

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
                ObjectiveValue = Double.PositiveInfinity
                ConstraintsSatisfied = 0
                TotalConstraints = 0
                IsValid = false
                WasRepaired = false
                BackendName = ""
                NumShots = 0
                OptimizedParameters = None
                OptimizationConverged = None
                Sampling = None
                Split = None
            }
        | [ single ] -> single
        | _ ->
            solutions
            |> List.filter (fun s -> s.IsValid)
            |> List.sortBy (fun s -> s.ObjectiveValue)
            |> List.tryHead
            |> Option.defaultWith (fun () -> solutions |> List.minBy (fun s -> s.ObjectiveValue))

    // ========================================================================
    // QUANTUM SOLVERS (Rule 1: IQuantumBackend required)
    // ========================================================================

    /// Shared implementation of solveWithConfigAsync.
    let private solveWithConfigCore
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (config: Config)
        (cancellationToken: CancellationToken)
        : Task<Result<Solution, QuantumError>> =

        match validateProblem problem with
        | Error err -> Task.FromResult(Error err)
        | Ok _ ->
            let solveSingle (subProblem: Problem) : Task<Result<Solution, QuantumError>> =
                task {
                    match validateProblem subProblem with
                    | Error err -> return Error err
                    | Ok encoded ->
                        let qubo = denseQubo subProblem encoded

                        match! QuboSplitting.runQaoaAsync backend qubo config cancellationToken with
                        | Error err -> return Error err
                        | Ok run ->
                            let bits = run.Best
                            let optParams = run.Direct |> Option.map (fun direct -> direct.Parameters)
                            let converged = run.Direct |> Option.bind (fun direct -> direct.Converged)

                            let decoded = decodeSolution subProblem bits
                            let needsRepair = not decoded.IsValid

                            let finalBits, wasRepaired =
                                if config.EnableConstraintRepair && needsRepair then
                                    (repairConstraints subProblem encoded bits, true)
                                else
                                    (bits, false)

                            let solution = decodeSolution subProblem finalBits

                            // A split run has no single sample set to take statistics from
                            let sampling =
                                run.Direct
                                |> Option.map (fun direct ->
                                    sampleStatistics
                                        bits.Length
                                        (fun sample -> (decodeSolution subProblem sample).IsValid)
                                        (fun sample ->
                                            (decodeSolution subProblem sample).Variables = solution.Variables)
                                        direct.Samples)

                            return
                                Ok
                                    { solution with
                                        BackendName = backend.Name
                                        NumShots = config.FinalShots
                                        WasRepaired = wasRepaired
                                        OptimizedParameters = optParams
                                        OptimizationConverged = converged
                                        Sampling = sampling
                                        Split = run.Split
                                    }
                }

            ProblemDecomposition.solveWithDecompositionAsync
                backend
                problem
                estimateQubits
                decompose
                recombine
                solveSingle

    /// Solve Binary ILP using QAOA with full configuration control (async).
    /// Supports automatic decomposition when problem exceeds backend capacity.
    let solveWithConfigAsync
        (backend: BackendAbstraction.IQuantumBackend)
        (problem: Problem)
        (config: Config)
        (cancellationToken: CancellationToken)
        : Task<Result<Solution, QuantumError>> =
        task {
            cancellationToken.ThrowIfCancellationRequested()
            return! solveWithConfigCore backend problem config cancellationToken
        }

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
                Sampling = None
                Split = None
            }
        else
            let n = problem.ObjectiveCoeffs.Length

            // Start with all variables = 0 and greedily set a variable to 1 when that improves
            // the objective and leaves every constraint satisfied
            let vars = Array.zeroCreate n

            // Sort variables by objective coefficient (ascending for minimization:
            // negative coefficients are good to set to 1)
            let sortedByBenefit = problem.ObjectiveCoeffs |> List.indexed |> List.sortBy snd

            sortedByBenefit
            |> List.iter (fun (i, ci) ->
                if ci < 0.0 then
                    // Setting x_i = 1 reduces objective — try it
                    vars.[i] <- 1
                    // Check if all constraints are still satisfied
                    let allSatisfied = problem.Constraints |> List.forall (isConstraintSatisfied vars)

                    if not allSatisfied then
                        vars.[i] <- 0 // Revert if it violates a constraint
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
                Sampling = None
                Split = None
            }
