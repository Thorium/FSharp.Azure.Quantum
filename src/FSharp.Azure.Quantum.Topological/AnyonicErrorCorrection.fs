namespace FSharp.Azure.Quantum.Topological

/// Anyonic Error Correction for Fusion Tree States
///
/// This module provides error correction at the fusion tree level,
/// detecting and correcting topological charge violations. Unlike
/// lattice-based codes (toric/surface codes) that correct Pauli errors
/// on qubit edges, this operates on the anyonic charge structure:
///
/// - **Charge violation detection**: Identify fusion nodes with invalid channels
/// - **Syndrome extraction**: Locate and classify corrupted nodes
/// - **Charge flip errors**: Model the dominant error in anyonic systems
/// - **Greedy decoder**: Correct violations by choosing valid channels
/// - **Code space projection**: Project corrupted superposition back into
///   the protected subspace with a fixed total charge
///
/// Works with all supported anyon theories: Ising, Fibonacci, SU(2)_k.
[<RequireQualifiedAccess>]
module AnyonicErrorCorrection =

    open System.Numerics

    // ========================================================================
    // TYPES
    // ========================================================================

    /// Direction in a fusion tree path (for locating nodes)
    type PathDirection =
        | Left
        | Right

    /// Information about a single charge violation
    type ChargeViolation = {
        /// Path from root to the violating fusion node
        Path: PathDirection list
        /// The invalid channel that was found
        ActualChannel: AnyonSpecies.Particle
        /// Valid channels for the fusing particles at this node
        ExpectedChannels: AnyonSpecies.Particle list
        /// Left child's charge at the violation
        LeftCharge: AnyonSpecies.Particle
        /// Right child's charge at the violation
        RightCharge: AnyonSpecies.Particle
    }

    /// Syndrome extracted from a fusion tree state
    type Syndrome = {
        /// All detected charge violations
        Violations: ChargeViolation list
        /// True if no violations detected
        IsClean: bool
        /// Number of violations
        ViolationCount: int
        /// The anyon theory context
        AnyonType: AnyonSpecies.AnyonType
    }

    /// Result of charge correction
    type CorrectionResult = {
        /// The corrected fusion tree state
        Tree: FusionTree.Tree
        /// The anyon theory context
        AnyonType: AnyonSpecies.AnyonType
        /// Number of corrections applied
        CorrectionsApplied: int
    }

    // ========================================================================
    // CHARGE VIOLATION DETECTION
    // ========================================================================

    /// Detect all charge violations in a fusion tree.
    ///
    /// Walks the tree bottom-up, checking at each Fusion node whether
    /// the stored channel is a valid fusion outcome for the children's charges.
    /// Leaf nodes are always valid (no channel to violate).
    ///
    /// Returns a list of ChargeViolation records (empty if tree is valid).
    let detectChargeViolations
        (tree: FusionTree.Tree)
        (anyonType: AnyonSpecies.AnyonType)
        : TopologicalResult<ChargeViolation list> =

        let rec detect (t: FusionTree.Tree) (path: PathDirection list) : TopologicalResult<ChargeViolation list> =
            match t with
            | FusionTree.Leaf _ -> Ok []
            | FusionTree.Fusion (left, right, channel) ->
                // Recurse into children
                match detect left (path @ [Left]), detect right (path @ [Right]) with
                | Error e, _ | _, Error e -> Error e
                | Ok leftViolations, Ok rightViolations ->
                    let leftCharge = FusionTree.totalCharge left anyonType
                    let rightCharge = FusionTree.totalCharge right anyonType

                    // Check if channel is valid for these children
                    match FusionRules.fuse leftCharge rightCharge anyonType with
                    | Error e -> Error e
                    | Ok outcomes ->
                        let validChannels = outcomes |> List.map (fun o -> o.Result)
                        let isValid = validChannels |> List.contains channel

                        if isValid then
                            Ok (leftViolations @ rightViolations)
                        else
                            let violation = {
                                Path = path
                                ActualChannel = channel
                                ExpectedChannels = validChannels
                                LeftCharge = leftCharge
                                RightCharge = rightCharge
                            }
                            Ok (leftViolations @ rightViolations @ [violation])

        detect tree []

    // ========================================================================
    // CHARGE FLIP ERROR INJECTION
    // ========================================================================

    /// Inject a charge flip error at the specified path in the fusion tree.
    ///
    /// Flips the fusion channel at the target node to a different valid channel
    /// (the next valid channel in the list). If only one valid channel exists,
    /// returns the tree unchanged.
    ///
    /// Returns Error if:
    /// - The path targets a leaf node (no channel to flip)
    /// - The path is invalid (goes Left/Right on a leaf)
    let injectChargeFlip
        (tree: FusionTree.Tree)
        (path: PathDirection list)
        (anyonType: AnyonSpecies.AnyonType)
        : TopologicalResult<FusionTree.Tree> =

        let rec inject (t: FusionTree.Tree) (remaining: PathDirection list) : TopologicalResult<FusionTree.Tree> =
            match t, remaining with
            // Arrived at target — must be a Fusion node
            | FusionTree.Fusion (left, right, channel), [] ->
                let leftCharge = FusionTree.totalCharge left anyonType
                let rightCharge = FusionTree.totalCharge right anyonType
                match FusionRules.fuse leftCharge rightCharge anyonType with
                | Error e -> Error e
                | Ok outcomes ->
                    let validChannels = outcomes |> List.map (fun o -> o.Result)
                    // Pick a different channel (cycle to next)
                    let otherChannels = validChannels |> List.filter (fun c -> c <> channel)
                    match otherChannels with
                    | next :: _ -> Ok (FusionTree.Fusion (left, right, next))
                    | [] ->
                        // Only one valid channel — can't flip, return unchanged
                        Ok t

            // Leaf at target — error (can't flip a leaf)
            | FusionTree.Leaf _, [] ->
                TopologicalResult.validationError "path" "Cannot inject charge flip on a leaf node (no fusion channel)"

            // Navigate deeper
            | FusionTree.Fusion (left, right, channel), Left :: rest ->
                match inject left rest with
                | Error e -> Error e
                | Ok newLeft -> Ok (FusionTree.Fusion (newLeft, right, channel))

            | FusionTree.Fusion (left, right, channel), Right :: rest ->
                match inject right rest with
                | Error e -> Error e
                | Ok newRight -> Ok (FusionTree.Fusion (left, newRight, channel))

            // Path goes deeper but we hit a leaf
            | FusionTree.Leaf _, _ :: _ ->
                TopologicalResult.validationError "path" "Path extends beyond tree structure (reached leaf)"

        inject tree path

    // ========================================================================
    // SYNDROME EXTRACTION
    // ========================================================================

    /// Extract a syndrome from a fusion tree state.
    ///
    /// Combines charge violation detection with metadata about
    /// the tree's health for diagnostic purposes.
    let extractSyndrome
        (state: FusionTree.State)
        : TopologicalResult<Syndrome> =

        match detectChargeViolations state.Tree state.AnyonType with
        | Error e -> Error e
        | Ok violations ->
            Ok {
                Violations = violations
                IsClean = violations.IsEmpty
                ViolationCount = violations.Length
                AnyonType = state.AnyonType
            }

    // ========================================================================
    // GREEDY CHARGE CORRECTION DECODER
    // ========================================================================

    /// Correct charge violations by replacing invalid channels with valid ones.
    ///
    /// Strategy: Bottom-up greedy correction. For each violated node,
    /// replace the channel with the first valid fusion outcome for
    /// the children's charges. This is a greedy approach that
    /// prioritizes the vacuum channel when available (minimal charge).
    ///
    /// After correcting inner nodes, propagates upward to ensure
    /// parent nodes are also consistent with the new child charges.
    let correctChargeViolations
        (state: FusionTree.State)
        : TopologicalResult<CorrectionResult> =

        let anyonType = state.AnyonType

        let rec correct (t: FusionTree.Tree) : TopologicalResult<FusionTree.Tree * int> =
            match t with
            | FusionTree.Leaf _ -> Ok (t, 0)
            | FusionTree.Fusion (left, right, channel) ->
                // First, correct children
                match correct left, correct right with
                | Error e, _ | _, Error e -> Error e
                | Ok (correctedLeft, leftFixes), Ok (correctedRight, rightFixes) ->
                    let leftCharge = FusionTree.totalCharge correctedLeft anyonType
                    let rightCharge = FusionTree.totalCharge correctedRight anyonType

                    match FusionRules.fuse leftCharge rightCharge anyonType with
                    | Error e -> Error e
                    | Ok outcomes ->
                        let validChannels = outcomes |> List.map (fun o -> o.Result)

                        if validChannels |> List.contains channel then
                            // Current channel is valid — no correction needed
                            Ok (FusionTree.Fusion (correctedLeft, correctedRight, channel), leftFixes + rightFixes)
                        else
                            // Pick first valid channel (prefer vacuum if available)
                            let preferred =
                                match validChannels |> List.tryFind (fun c -> c = AnyonSpecies.Particle.Vacuum) with
                                | Some v -> v
                                | None -> validChannels |> List.head
                            Ok (FusionTree.Fusion (correctedLeft, correctedRight, preferred), leftFixes + rightFixes + 1)

        match correct state.Tree with
        | Error e -> Error e
        | Ok (correctedTree, fixes) ->
            Ok {
                Tree = correctedTree
                AnyonType = anyonType
                CorrectionsApplied = fixes
            }

    // ========================================================================
    // PROTECTED SUBSPACE PROJECTION
    // ========================================================================

    /// Project a superposition onto the code space with a fixed total charge.
    ///
    /// Keeps only terms whose fusion tree has the specified total charge,
    /// then renormalizes. This is the quantum error correction "syndrome
    /// measurement + projection" step for anyonic systems.
    ///
    /// Returns the projected (and renormalized) superposition.
    let projectToCodeSpace
        (superposition: TopologicalOperations.Superposition)
        (targetCharge: AnyonSpecies.Particle)
        : TopologicalResult<TopologicalOperations.Superposition> =

        let filteredTerms =
            superposition.Terms
            |> List.filter (fun (_, state) ->
                let charge = FusionTree.totalCharge state.Tree state.AnyonType
                charge = targetCharge)

        let projected = { superposition with Terms = filteredTerms }

        if filteredTerms.IsEmpty then
            Ok projected
        else
            Ok (TopologicalOperations.normalize projected)

    // ========================================================================
    // FULL CORRECTION PIPELINE
    // ========================================================================

    /// Full error correction pipeline: correct each term's fusion tree,
    /// then project the superposition to the target charge code space.
    ///
    /// Steps:
    /// 1. For each term in the superposition, correct charge violations
    /// 2. Rebuild the superposition with corrected trees
    /// 3. Project onto the target charge subspace
    /// 4. Renormalize
    let fullCorrection
        (superposition: TopologicalOperations.Superposition)
        (targetCharge: AnyonSpecies.Particle)
        : TopologicalResult<TopologicalOperations.Superposition> =

        let correctedTerms =
            superposition.Terms
            |> List.map (fun (amp, state) ->
                match correctChargeViolations state with
                | Error e -> Error e
                | Ok corrected ->
                    let newState = FusionTree.create corrected.Tree corrected.AnyonType
                    Ok (amp, newState))

        // Check for errors
        match correctedTerms |> List.tryPick (function Error e -> Some e | Ok _ -> None) with
        | Some err -> Error err
        | None ->
            let terms = correctedTerms |> List.choose (function Ok t -> Some t | Error _ -> None)
            let correctedSuperposition = { superposition with Terms = terms }
            projectToCodeSpace correctedSuperposition targetCharge

    // ========================================================================
    // DISPLAY
    // ========================================================================

    /// Display a syndrome in human-readable format
    let displaySyndrome (syndrome: Syndrome) : string =
        if syndrome.IsClean then
            $"Syndrome: Clean (no charge violations detected)\nTheory: {syndrome.AnyonType}"
        else
            let violationStrs =
                syndrome.Violations
                |> List.mapi (fun i v ->
                    let pathStr =
                        if v.Path.IsEmpty then "root"
                        else v.Path |> List.map (function Left -> "L" | Right -> "R") |> String.concat ""
                    let expectedStr = v.ExpectedChannels |> List.map (fun p -> $"{p}") |> String.concat ", "
                    $"  Violation {i + 1}: path={pathStr}, actual={v.ActualChannel}, expected=[{expectedStr}]")
                |> String.concat "\n"
            $"Syndrome: {syndrome.ViolationCount} charge violation(s) detected\nTheory: {syndrome.AnyonType}\n{violationStrs}"
