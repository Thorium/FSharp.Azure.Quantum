namespace FSharp.Azure.Quantum.Business

open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Quantum

/// <summary>
/// High-level resource pairing optimization using quantum matching.
/// </summary>
/// <remarks>
/// **Business Use Cases:**
/// - Recruiting: Match job candidates to open positions
/// - Mentoring: Pair mentors with mentees based on compatibility
/// - Trading: Match buyers with sellers for optimal deals
/// - Healthcare: Assign patients to specialists by expertise fit
/// - Ride-sharing: Match drivers with passengers by proximity/preference
///
/// **Quantum Advantage:**
/// Uses QAOA-based maximum weight matching to find optimal 1:1 pairings
/// that maximize total compatibility/weight, via QUBO formulation.
///
/// **Example:**
/// ```fsharp
/// let result = resourcePairing {
///     participant "Alice"
///     participant "Bob"
///     participant "Carol"
///
///     compatibility "Alice" "Bob" 0.9    // High compatibility
///     compatibility "Alice" "Carol" 0.5  // Medium compatibility
///     compatibility "Bob" "Carol" 0.7    // Good compatibility
///
///     backend myBackend
/// }
/// ```
/// </remarks>
module ResourcePairing =

    // ========================================================================
    // TYPES
    // ========================================================================

    /// Participant identifier
    type ParticipantId = string

    /// Compatibility/preference between two participants
    type Compatibility = {
        Participant1: ParticipantId
        Participant2: ParticipantId
        /// Weight/score representing compatibility (higher = better match)
        Weight: float
    }

    /// A pairing of two participants
    type Pairing = {
        Participant1: ParticipantId
        Participant2: ParticipantId
        Weight: float
    }

    /// Resource pairing problem
    type PairingProblem = {
        /// All participants
        Participants: ParticipantId list
        /// Compatibility scores between participants
        Compatibilities: Compatibility list
        /// Quantum backend (None = error, Some = quantum optimization)
        Backend: IQuantumBackend option
        /// Number of measurement shots (default: 1000)
        Shots: int
    }

    /// Resource pairing solution
    type PairingResult = {
        /// Optimal pairings found
        Pairings: Pairing list
        /// Total compatibility score
        TotalScore: float
        /// Number of participants paired
        ParticipantsPaired: int
        /// Total participants
        TotalParticipants: int
        /// Whether the matching is valid (no participant in multiple pairs)
        IsValid: bool
        /// Execution message
        Message: string
    }

    // ========================================================================
    // CONVERSION & SOLVING
    // ========================================================================

    /// Convert PairingProblem to QuantumMatchingSolver.Problem
    let private toMatchingProblem (problem: PairingProblem) : QuantumMatchingSolver.Problem =
        let participantIdx =
            problem.Participants
            |> List.mapi (fun i p -> (p, i))
            |> Map.ofList

        let edges =
            problem.Compatibilities
            |> List.choose (fun c ->
                match Map.tryFind c.Participant1 participantIdx, Map.tryFind c.Participant2 participantIdx with
                | Some idx1, Some idx2 ->
                    Some ({ Source = idx1; Target = idx2; Weight = c.Weight } : QuantumMatchingSolver.Edge)
                | _ -> None)

        { NumVertices = problem.Participants.Length
          Edges = edges }

    /// Decode a QuantumMatchingSolver.Solution to PairingResult
    let private decodeSolution (problem: PairingProblem) (solution: QuantumMatchingSolver.Solution) : PairingResult =
        let pairings =
            solution.SelectedEdges
            |> List.choose (fun edge ->
                if edge.Source < problem.Participants.Length && edge.Target < problem.Participants.Length then
                    Some { Participant1 = problem.Participants.[edge.Source]
                           Participant2 = problem.Participants.[edge.Target]
                           Weight = edge.Weight }
                else
                    None)

        let pairedCount =
            pairings
            |> List.collect (fun p -> [p.Participant1; p.Participant2])
            |> List.distinct
            |> List.length

        { Pairings = pairings
          TotalScore = solution.TotalWeight
          ParticipantsPaired = pairedCount
          TotalParticipants = problem.Participants.Length
          IsValid = solution.IsValid
          Message =
            if solution.IsValid then
                $"Found {pairings.Length} optimal pairings with total score {solution.TotalWeight:F2}"
            else
                $"Found {pairings.Length} pairings (may have conflicts)" }

    /// Execute resource pairing optimization
    let solve (problem: PairingProblem) : QuantumResult<PairingResult> =
        if problem.Participants.Length < 2 then
            Error (QuantumError.ValidationError ("Participants", "must have at least 2 participants"))
        elif problem.Compatibilities.IsEmpty then
            Error (QuantumError.ValidationError ("Compatibilities", "must have at least one compatibility score"))
        elif problem.Compatibilities |> List.exists (fun c -> c.Weight < 0.0) then
            Error (QuantumError.ValidationError ("Weight", "compatibility weights must be non-negative"))
        elif problem.Compatibilities |> List.exists (fun c ->
                not (List.contains c.Participant1 problem.Participants) ||
                not (List.contains c.Participant2 problem.Participants)) then
            Error (QuantumError.ValidationError ("Participants", "compatibility references unknown participant"))
        else
            match problem.Backend with
            | Some backend ->
                let matchingProblem = toMatchingProblem problem
                match QuantumMatchingSolver.solve backend matchingProblem problem.Shots with
                | Error err -> Error err
                | Ok solution ->
                    Ok (decodeSolution problem solution)
            | None ->
                Error (QuantumError.NotImplemented (
                    "Classical resource pairing",
                    Some "Provide a quantum backend via PairingProblem.Backend."))

    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ========================================================================

    /// Fluent builder for resource pairing optimization.
    type ResourcePairingBuilder() =

        let defaultProblem = {
            Participants = []
            Compatibilities = []
            Backend = None
            Shots = 1000
        }

        member _.Yield(_) = defaultProblem
        member _.Delay(f: unit -> PairingProblem) = f
        member _.Run(f: unit -> PairingProblem) : QuantumResult<PairingResult> =
            let problem = f()
            solve problem
        member _.Combine(p1: PairingProblem, p2: PairingProblem) = p2
        member _.Zero() = defaultProblem

        /// <summary>Add a participant.</summary>
        [<CustomOperation("participant")>]
        member _.Participant(problem: PairingProblem, id: ParticipantId) : PairingProblem =
            { problem with Participants = id :: problem.Participants }

        /// <summary>Add multiple participants at once.</summary>
        [<CustomOperation("participants")>]
        member _.Participants(problem: PairingProblem, ids: ParticipantId list) : PairingProblem =
            { problem with Participants = ids @ problem.Participants }

        /// <summary>Add a compatibility score between two participants.</summary>
        /// <param name="p1">First participant</param>
        /// <param name="p2">Second participant</param>
        /// <param name="weight">Compatibility score (higher = better match)</param>
        [<CustomOperation("compatibility")>]
        member _.Compatibility(problem: PairingProblem, p1: ParticipantId, p2: ParticipantId, weight: float) : PairingProblem =
            let compat : Compatibility = { Participant1 = p1; Participant2 = p2; Weight = weight }
            { problem with Compatibilities = compat :: problem.Compatibilities }

        /// <summary>Set the quantum backend.</summary>
        [<CustomOperation("backend")>]
        member _.Backend(problem: PairingProblem, backend: IQuantumBackend) : PairingProblem =
            { problem with Backend = Some backend }

        /// <summary>Set the number of measurement shots.</summary>
        [<CustomOperation("shots")>]
        member _.Shots(problem: PairingProblem, shots: int) : PairingProblem =
            { problem with Shots = shots }

    /// Create a resource pairing builder.
    let resourcePairing = ResourcePairingBuilder()
