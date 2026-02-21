namespace FSharp.Azure.Quantum.Business

open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Quantum

/// <summary>
/// High-level coverage optimization using quantum set cover.
/// </summary>
/// <remarks>
/// **Business Use Cases:**
/// - Shift Coverage: Select minimum shifts to cover all required time slots
/// - Facility Location: Place minimum facilities to serve all demand zones
/// - Service Coverage: Select service packages covering all customer needs
/// - Network Coverage: Place minimum sensors to monitor all network segments
/// - Test Coverage: Select minimum test suites covering all code paths
///
/// **Quantum Advantage:**
/// Uses QAOA-based set cover optimization to find minimum-cost subsets
/// that cover all required elements, with approximate optimization
/// via QUBO formulation.
///
/// **Example:**
/// ```fsharp
/// let result = coverageOptimizer {
///     element 0  // Time slot 0
///     element 1  // Time slot 1
///     element 2  // Time slot 2
///
///     option "MorningShift" [0; 1] 25.0   // Covers slots 0,1 at cost $25
///     option "AfternoonShift" [1; 2] 20.0 // Covers slots 1,2 at cost $20
///     option "FullDay" [0; 1; 2] 40.0     // Covers all at cost $40
///
///     backend myBackend
/// }
/// ```
/// </remarks>
module CoverageOptimizer =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// A coverage option (shift, facility, service package, etc.)
    type CoverageOption = {
        /// Unique identifier
        Id: string
        /// Elements this option covers (0-based indices)
        CoveredElements: int list
        /// Cost of selecting this option
        Cost: float
    }

    /// Coverage optimization problem
    type CoverageProblem = {
        /// Total number of elements to cover
        UniverseSize: int
        /// Available coverage options
        Options: CoverageOption list
        /// Quantum backend (None = error, Some = quantum optimization)
        Backend: IQuantumBackend option
        /// Number of measurement shots (default: 1000)
        Shots: int
    }

    /// Coverage solution
    type CoverageResult = {
        /// Selected coverage options
        SelectedOptions: CoverageOption list
        /// Total cost of selected options
        TotalCost: float
        /// Number of elements covered
        ElementsCovered: int
        /// Total elements that need coverage
        TotalElements: int
        /// Whether all elements are covered
        IsComplete: bool
        /// Execution message
        Message: string
    }

    // ========================================================================
    // CONVERSION & SOLVING
    // ========================================================================

    /// Convert CoverageProblem to QuantumSetCoverSolver.Problem
    let private toSetCoverProblem (problem: CoverageProblem) : QuantumSetCoverSolver.Problem =
        let subsets =
            problem.Options
            |> List.map (fun opt ->
                ({ Id = opt.Id
                   Elements = opt.CoveredElements
                   Cost = opt.Cost } : QuantumSetCoverSolver.Subset))
        { UniverseSize = problem.UniverseSize
          Subsets = subsets }

    /// Decode a QuantumSetCoverSolver.Solution to CoverageResult
    let private decodeSolution (problem: CoverageProblem) (solution: QuantumSetCoverSolver.Solution) : CoverageResult =
        let selectedOptions =
            solution.SelectedSubsets
            |> List.choose (fun subset ->
                problem.Options |> List.tryFind (fun opt -> opt.Id = subset.Id))

        let coveredElements =
            selectedOptions
            |> List.collect (fun opt -> opt.CoveredElements)
            |> List.distinct
            |> List.length

        { SelectedOptions = selectedOptions
          TotalCost = solution.TotalCost
          ElementsCovered = coveredElements
          TotalElements = problem.UniverseSize
          IsComplete = solution.IsValid
          Message =
            if solution.IsValid then
                $"Found complete coverage with {selectedOptions.Length} options, cost ${solution.TotalCost:F2}"
            else
                $"Partial coverage: {coveredElements}/{problem.UniverseSize} elements covered" }

    /// Execute coverage optimization
    let solve (problem: CoverageProblem) : QuantumResult<CoverageResult> =
        if problem.UniverseSize <= 0 then
            Error (QuantumError.ValidationError ("UniverseSize", "must be positive"))
        elif problem.Options.IsEmpty then
            Error (QuantumError.ValidationError ("Options", "must have at least one coverage option"))
        elif problem.Options |> List.exists (fun opt -> opt.Cost < 0.0) then
            Error (QuantumError.ValidationError ("Cost", "option costs must be non-negative"))
        elif problem.Options |> List.exists (fun opt ->
                opt.CoveredElements |> List.exists (fun e -> e < 0 || e >= problem.UniverseSize)) then
            Error (QuantumError.ValidationError ("CoveredElements", "element indices must be in range [0, UniverseSize)"))
        else
            match problem.Backend with
            | Some backend ->
                let setCoverProblem = toSetCoverProblem problem
                match QuantumSetCoverSolver.solve backend setCoverProblem problem.Shots with
                | Error err -> Error err
                | Ok solution ->
                    Ok (decodeSolution problem solution)
            | None ->
                Error (QuantumError.NotImplemented (
                    "Classical coverage optimization",
                    Some "Provide a quantum backend via CoverageProblem.Backend."))

    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ========================================================================

    /// Fluent builder for coverage optimization.
    type CoverageOptimizerBuilder() =

        let defaultProblem = {
            UniverseSize = 0
            Options = []
            Backend = None
            Shots = 1000
        }

        member _.Yield(_) = defaultProblem
        member _.Delay(f: unit -> CoverageProblem) = f
        member _.Run(f: unit -> CoverageProblem) : QuantumResult<CoverageResult> =
            let problem = f()
            solve problem
        member _.Combine(p1: CoverageProblem, p2: CoverageProblem) = p2
        member _.Zero() = defaultProblem

        /// <summary>Add an element to the universe (expands UniverseSize if needed).</summary>
        [<CustomOperation("element")>]
        member _.Element(problem: CoverageProblem, elementIndex: int) : CoverageProblem =
            let newSize = max problem.UniverseSize (elementIndex + 1)
            { problem with UniverseSize = newSize }

        /// <summary>Set the universe size directly.</summary>
        [<CustomOperation("universeSize")>]
        member _.UniverseSize(problem: CoverageProblem, size: int) : CoverageProblem =
            { problem with UniverseSize = size }

        /// <summary>Add a coverage option (shift, facility, service, etc.).</summary>
        /// <param name="id">Unique identifier</param>
        /// <param name="coveredElements">List of element indices this option covers</param>
        /// <param name="cost">Cost of selecting this option</param>
        [<CustomOperation("option")>]
        member _.Option(problem: CoverageProblem, id: string, coveredElements: int list, cost: float) : CoverageProblem =
            let opt = { Id = id; CoveredElements = coveredElements; Cost = cost }
            { problem with Options = opt :: problem.Options }

        /// <summary>Set the quantum backend.</summary>
        [<CustomOperation("backend")>]
        member _.Backend(problem: CoverageProblem, backend: IQuantumBackend) : CoverageProblem =
            { problem with Backend = Some backend }

        /// <summary>Set the number of measurement shots.</summary>
        [<CustomOperation("shots")>]
        member _.Shots(problem: CoverageProblem, shots: int) : CoverageProblem =
            { problem with Shots = shots }

    /// Create a coverage optimizer builder.
    let coverageOptimizer = CoverageOptimizerBuilder()
