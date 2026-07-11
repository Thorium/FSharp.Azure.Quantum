namespace FSharp.Azure.Quantum.Topological

open FSharp.Azure.Quantum.Core

/// Quantum operations on topological qubits
/// 
/// This module implements the fundamental quantum gates for topological quantum computing:
/// - **Braiding**: Exchange anyons to perform unitary gates
/// - **Measurement**: Fuse anyons to collapse quantum state
/// - **Basis Transformations**: F-moves to change fusion tree structure
/// 
/// Key insight: In topological QC, gates are GEOMETRIC operations (braiding),
/// not abstract matrix multiplications like in gate-based QC.
/// 
/// Example: Braiding two sigma anyons around each other applies a phase gate.
/// This is inherently fault-tolerant - small perturbations don't affect the topology!
[<RequireQualifiedAccess>]
module TopologicalOperations =
    
    open System.Numerics
    
    /// Result of a quantum operation on a fusion tree
    type OperationResult = {
        /// The resulting fusion tree state
        State: FusionTree.State
        
        /// The amplitude (complex coefficient) from the operation
        Amplitude: Complex
        
        /// Optional classical outcome (for measurements)
        ClassicalOutcome: AnyonSpecies.Particle option
    }
    
    /// A quantum superposition of fusion tree states
    type Superposition = {
        /// List of (amplitude, state) pairs
        Terms: (Complex * FusionTree.State) list
        
        /// The anyon theory context
        AnyonType: AnyonSpecies.AnyonType
    }
    
    // ========================================================================
    // SUPERPOSITION CONSTRUCTION
    // ========================================================================
    
    /// Create a superposition from a single basis state (pure state)
    let pureState (state: FusionTree.State) : Superposition =
        { Terms = [(Complex.One, state)]; AnyonType = state.AnyonType }
    
    /// Create a uniform superposition of all basis states
    let uniform (states: FusionTree.State list) (anyonType: AnyonSpecies.AnyonType) : Superposition =
        let n = states.Length
        let amplitude = Complex(1.0 / sqrt (float n), 0.0)
        { Terms = states |> List.map (fun s -> (amplitude, s))
          AnyonType = anyonType }
    
    /// Combine identical basis states by summing amplitudes.
    ///
    /// This is required for interference to work correctly (|ψ⟩ + |ψ⟩ = 2|ψ⟩).
    let combineLikeTerms (superposition: Superposition) : Superposition =
        let merged =
            superposition.Terms
            |> List.mapi (fun idx (amp, state) -> (idx, amp, state))
            |> List.fold (fun (acc: Map<string, int * Complex * FusionTree.State>) (idx, amp, state) ->
                let key = FusionTree.toString state.Tree
                match acc |> Map.tryFind key with
                | None -> acc |> Map.add key (idx, amp, state)
                | Some (firstIdx, existingAmp, existingState) ->
                    acc |> Map.add key (firstIdx, existingAmp + amp, existingState)
            ) Map.empty
            |> Map.toList
            |> List.map (fun (_, (idx, amp, state)) -> (idx, (amp, state)))
            |> List.sortBy fst
            |> List.map snd

        // Avoid dropping all terms for an all-zero state.
        let eps = 1e-14
        let nonZero = merged |> List.filter (fun (amp, _) -> Complex.Abs amp > eps)
        let finalTerms = if nonZero.IsEmpty then merged else nonZero

        { superposition with Terms = finalTerms }

    /// Normalize a superposition (ensure sum of |amplitude|² = 1)
    let normalize (superposition: Superposition) : Superposition =
        let combined = combineLikeTerms superposition

        let normSquared =
            combined.Terms
            |> List.sumBy (fun (amp, _) -> (Complex.Abs amp) ** 2.0)

        let norm = sqrt normSquared

        if norm = 0.0 then
            combined
        else
            let normalized =
                combined.Terms
                |> List.map (fun (amp, state) -> (amp / Complex(norm, 0.0), state))
            { combined with Terms = normalized }
    
    // ========================================================================
    // BASIS TRANSFORMATIONS (F-MOVES)
    // ========================================================================
    
    /// Apply F-matrix transformation to change fusion tree associativity
    /// 
    /// F-move: ((a × b) × c) ↔ (a × (b × c))
    /// 
    /// This changes the tree structure but represents the same quantum state
    /// in a different basis. The F-matrix gives the change-of-basis coefficients.
    type FMoveDirection =
        | LeftToRight  // ((a × b) × c) → (a × (b × c))
        | RightToLeft  // (a × (b × c)) → ((a × b) × c)

    type private Branch =
        | L
        | R

    let rec private collectNodesAtDepth (targetDepth: int) (tree: FusionTree.Tree) : (Branch list * FusionTree.Tree) list =
        let rec loop (currentDepth: int) (path: Branch list) (node: FusionTree.Tree) acc =
            let nextAcc =
                if currentDepth = targetDepth then
                    (path, node) :: acc
                else
                    acc

            match node with
            | FusionTree.Leaf _ -> nextAcc
            | FusionTree.Fusion (left, right, _) ->
                let acc1 = loop (currentDepth + 1) (path @ [ L ]) left nextAcc
                loop (currentDepth + 1) (path @ [ R ]) right acc1

        loop 0 [] tree [] |> List.rev

    let rec private replaceAtPath (path: Branch list) (replacement: FusionTree.Tree) (tree: FusionTree.Tree) : FusionTree.Tree =
        match path, tree with
        | [], _ -> replacement
        | _, FusionTree.Leaf _ -> tree
        | branch :: rest, FusionTree.Fusion (left, right, channel) ->
            match branch with
            | L -> FusionTree.Fusion (replaceAtPath rest replacement left, right, channel)
            | R -> FusionTree.Fusion (left, replaceAtPath rest replacement right, channel)

    let private swapOrder (direction: FMoveDirection) =
        match direction with
        | LeftToRight -> RightToLeft
        | RightToLeft -> LeftToRight

    let private applyLocalFMove (direction: FMoveDirection) (anyonType: AnyonSpecies.AnyonType) (subtree: FusionTree.Tree) : TopologicalResult<(Complex * FusionTree.Tree) list> =
        topologicalResult {
            match subtree with
            // Left-associated: ((a×b→e)×c→d)
            | FusionTree.Fusion (FusionTree.Fusion (aTree, bTree, e), cTree, d) when direction = LeftToRight ->
                match aTree, bTree, cTree with
                | FusionTree.Leaf a, FusionTree.Leaf b, FusionTree.Leaf c ->
                    let! fMatrix = BraidingOperators.fusionBasisChange a b c d anyonType
                    let! possibleF = FusionRules.channels b c anyonType
                    let validF =
                        possibleF
                        |> List.choose (fun f ->
                            match FusionRules.isPossible a f d anyonType with
                            | Ok true -> Some f
                            | _ -> None)

                    // fMatrix rows correspond to e-channels for (a×b)×c; columns correspond to f-channels for a×(b×c)
                    let! possibleE = FusionRules.channels a b anyonType
                    let validE =
                        possibleE
                        |> List.choose (fun e2 ->
                            match FusionRules.isPossible e2 c d anyonType with
                            | Ok true -> Some e2
                            | _ -> None)

                    match validE |> List.tryFindIndex ((=) e) with
                    | None ->
                        return [ (Complex.One, subtree) ]
                    | Some rowIndex ->
                        let terms =
                            validF
                            |> List.mapi (fun colIndex f ->
                                let coeff = fMatrix.[rowIndex, colIndex]
                                let newSubtree =
                                    FusionTree.Fusion (FusionTree.Leaf a, FusionTree.Fusion (FusionTree.Leaf b, FusionTree.Leaf c, f), d)
                                (coeff, newSubtree)
                            )
                            |> List.filter (fun (amp, _) -> Complex.Abs amp > 1e-14)

                        return terms
                | _ ->
                    return [ (Complex.One, subtree) ]

            // Right-associated: (a×(b×c→f)→d)
            | FusionTree.Fusion (aTree, FusionTree.Fusion (bTree, cTree, f), d) when direction = RightToLeft ->
                match aTree, bTree, cTree with
                | FusionTree.Leaf a, FusionTree.Leaf b, FusionTree.Leaf c ->
                    // Inverse basis change is conjugate transpose since F is unitary.
                    let! fMatrix = BraidingOperators.fusionBasisChange a b c d anyonType
                    let! possibleE = FusionRules.channels a b anyonType
                    let validE =
                        possibleE
                        |> List.choose (fun e ->
                            match FusionRules.isPossible e c d anyonType with
                            | Ok true -> Some e
                            | _ -> None)

                    // Determine column of current f in validF ordering.
                    let! possibleF = FusionRules.channels b c anyonType
                    let validF =
                        possibleF
                        |> List.choose (fun f2 ->
                            match FusionRules.isPossible a f2 d anyonType with
                            | Ok true -> Some f2
                            | _ -> None)

                    let colIndexOpt = validF |> List.tryFindIndex ((=) f)
                    match colIndexOpt with
                    | None -> return [ (Complex.One, subtree) ]
                    | Some colIndex ->
                        let terms =
                            validE
                            |> List.mapi (fun rowIndex e ->
                                let coeff = Complex.Conjugate fMatrix.[rowIndex, colIndex]
                                let newSubtree =
                                    FusionTree.Fusion (FusionTree.Fusion (FusionTree.Leaf a, FusionTree.Leaf b, e), FusionTree.Leaf c, d)
                                (coeff, newSubtree)
                            )
                            |> List.filter (fun (amp, _) -> Complex.Abs amp > 1e-14)

                        return terms
                | _ ->
                    return [ (Complex.One, subtree) ]

            | _ ->
                return [ (Complex.One, subtree) ]
        }

    /// Apply an F-move at a specific node depth in the tree.
    ///
    /// This walks all nodes at `nodeDepth` and applies a local associativity change.
    /// If the requested node doesn’t match a 3-leaf associator pattern, it acts as identity.
    let fMove
        (direction: FMoveDirection)
        (nodeDepth: int)
        (state: FusionTree.State)
        : Superposition =

        let targets = collectNodesAtDepth nodeDepth state.Tree

        // If nothing matches (e.g. depth too large), keep identity.
        if targets.IsEmpty then
            pureState state
        else
            targets
            |> List.fold (fun (superpos: Superposition) (path, subtree) ->
                let expanded =
                    superpos.Terms
                    |> List.collect (fun (amp, st) ->
                        // Important: use the corresponding subtree from the current state (not the initial capture)
                        let currentSubtree =
                            let rec getAtPath p t =
                                match p, t with
                                | [], _ -> t
                                | _, FusionTree.Leaf _ -> t
                                | L :: rest, FusionTree.Fusion (l, _, _) -> getAtPath rest l
                                | R :: rest, FusionTree.Fusion (_, r, _) -> getAtPath rest r
                            getAtPath path st.Tree

                        match applyLocalFMove direction st.AnyonType currentSubtree with
                        | Error _ -> [ (amp, st) ]
                        | Ok localTerms ->
                            localTerms
                            |> List.map (fun (localAmp, newSub) ->
                                let newTree = replaceAtPath path newSub st.Tree
                                (amp * localAmp, { st with Tree = newTree })
                            )
                    )

                { superpos with Terms = expanded }
            ) (pureState state)
            |> combineLikeTerms
            |> normalize

    // ========================================================================
    // BRAIDING OPERATIONS
    // ========================================================================
    
    let rec private tryFindFusedLeafPairChannel (targetLeftIndex: int) (tree: FusionTree.Tree) : AnyonSpecies.Particle option =
        let rec loop (idx: int) (node: FusionTree.Tree) : int * AnyonSpecies.Particle option =
            match node with
            | FusionTree.Leaf _ -> (idx + 1, None)
            | FusionTree.Fusion (FusionTree.Leaf _, FusionTree.Leaf _, channel) ->
                // This node represents a fused pair of adjacent leaves at position idx
                if idx = targetLeftIndex then
                    (idx + 2, Some channel)
                else
                    (idx + 2, None)
            | FusionTree.Fusion (left, right, _) ->
                let (nextIdx, foundLeft) = loop idx left
                match foundLeft with
                | Some _ -> (nextIdx + FusionTree.size right, foundLeft)
                | None ->
                    loop nextIdx right

        loop 0 tree |> snd

    let private conjugateIfInverse (isClockwise: bool) (phase: Complex) : Complex =
        if isClockwise then phase else Complex.Conjugate phase

    /// Braid two adjacent anyons.
    ///
    /// Unlike the earlier placeholder implementation, braiding can now produce a superposition
    /// (via F–R–F⁻¹ on σσσ triples) instead of only a global phase.
    let braidAdjacentAnyonsDirected
        (leftIndex: int)
        (isClockwise: bool)
        (state: FusionTree.State)
        : TopologicalResult<Superposition> =

        let anyons = FusionTree.leaves state.Tree

        if leftIndex < 0 || leftIndex >= anyons.Length - 1 then
            TopologicalResult.validationError "leftIndex" $"Invalid braid index {leftIndex} for {anyons.Length} anyons"
        else
            topologicalResult {
                // Special-case: explicit 3-anyon basis (required for nontrivial mixing)
                let braidWithinTriple
                    (treeLeftAssoc: FusionTree.Tree)
                    (a: AnyonSpecies.Particle)
                    (b: AnyonSpecies.Particle)
                    (c: AnyonSpecies.Particle)
                    (e: AnyonSpecies.Particle)
                    (d: AnyonSpecies.Particle)
                    =
                    topologicalResult {
                        // 1) Change basis so (b,c) fuse first
                        let! fTerms = applyLocalFMove LeftToRight state.AnyonType treeLeftAssoc

                        // 2) Apply R on the (b,c) fusion channel f
                        let braidedInRightBasis =
                            fTerms
                            |> List.choose (fun (fAmp, rightAssocSubtree) ->
                                match rightAssocSubtree with
                                | FusionTree.Fusion (_, FusionTree.Fusion (_, _, f), _) ->
                                    match BraidingOperators.element b c f state.AnyonType with
                                    | Ok rPhase -> Some (fAmp * conjugateIfInverse isClockwise rPhase, rightAssocSubtree)
                                    | Error _ -> None
                                | _ -> None)

                        // 3) Change basis back (inverse F)
                        let! backTerms =
                            braidedInRightBasis
                            |> List.fold (fun accResult (amp, rightAssocTree) ->
                                topologicalResult {
                                    let! acc = accResult
                                    let! invTerms = applyLocalFMove RightToLeft state.AnyonType rightAssocTree
                                    let expanded = invTerms |> List.map (fun (invAmp, leftAssocTree2) -> (amp * invAmp, leftAssocTree2))
                                    return expanded @ acc
                                }
                            ) (Ok [])

                        return backTerms
                    }

                match state.Tree with
                // ((a×b→e)×c→d)
                | FusionTree.Fusion (FusionTree.Fusion (FusionTree.Leaf a, FusionTree.Leaf b, e), FusionTree.Leaf c, d) when leftIndex = 0 ->
                    let! phase = BraidingOperators.element a b e state.AnyonType
                    return normalize { Terms = [ (conjugateIfInverse isClockwise phase, state) ]; AnyonType = state.AnyonType }

                | FusionTree.Fusion (FusionTree.Fusion (FusionTree.Leaf a, FusionTree.Leaf b, e), FusionTree.Leaf c, d) when leftIndex = 1 ->
                    let! mixed = braidWithinTriple state.Tree a b c e d
                    let mixedStates = mixed |> List.map (fun (amp, t) -> (amp, FusionTree.create t state.AnyonType))
                    return normalize { Terms = mixedStates; AnyonType = state.AnyonType }

                // (a×(b×c→f)→d)
                | FusionTree.Fusion (FusionTree.Leaf a, FusionTree.Fusion (FusionTree.Leaf b, FusionTree.Leaf c, f), d) when leftIndex = 1 ->
                    let! phase = BraidingOperators.element b c f state.AnyonType
                    return normalize { Terms = [ (conjugateIfInverse isClockwise phase, state) ]; AnyonType = state.AnyonType }

                | FusionTree.Fusion (FusionTree.Leaf a, FusionTree.Fusion (FusionTree.Leaf b, FusionTree.Leaf c, f), d) when leftIndex = 0 ->
                    // Convert to left-associated basis, braid (a,b) via R, then convert back
                    let! leftTerms = applyLocalFMove RightToLeft state.AnyonType state.Tree
                    let braidedLeft =
                        leftTerms
                        |> List.choose (fun (amp, leftAssocTree) ->
                            match leftAssocTree with
                            | FusionTree.Fusion (FusionTree.Fusion (FusionTree.Leaf a2, FusionTree.Leaf b2, e), FusionTree.Leaf c2, d2) ->
                                match BraidingOperators.element a2 b2 e state.AnyonType with
                                | Ok rPhase -> Some (amp * conjugateIfInverse isClockwise rPhase, leftAssocTree)
                                | Error _ -> None
                            | _ -> None)

                    let! backTerms =
                        braidedLeft
                        |> List.fold (fun accResult (amp, leftAssocTree) ->
                            topologicalResult {
                                let! acc = accResult
                                let! invTerms = applyLocalFMove LeftToRight state.AnyonType leftAssocTree
                                let expanded = invTerms |> List.map (fun (invAmp, rightAssocTree) -> (amp * invAmp, rightAssocTree))
                                return expanded @ acc
                            }
                        ) (Ok [])

                    let mixedStates = backTerms |> List.map (fun (amp, t) -> (amp, FusionTree.create t state.AnyonType))
                    return normalize { Terms = mixedStates; AnyonType = state.AnyonType }

                | _ ->
                    // If the adjacent pair is explicitly fused in this basis, use the stored channel.
                    match tryFindFusedLeafPairChannel leftIndex state.Tree with
                    | Some channel ->
                        let anyon1 = anyons.[leftIndex]
                        let anyon2 = anyons.[leftIndex + 1]
                        let! phase = BraidingOperators.element anyon1 anyon2 channel state.AnyonType
                        return normalize { Terms = [ (conjugateIfInverse isClockwise phase, state) ]; AnyonType = state.AnyonType }
                    | None ->
                        // The adjacent pair is NOT explicitly fused in this basis (e.g. a
                        // cross-pair braid on the σ-pair encoding: leaf 2q+1 with leaf 2q+2).
                        // Applying such a braid correctly requires F-move basis changes that
                        // are only implemented for 3-anyon trees above.
                        //
                        // The previous fallback applied the FIRST fusion channel's R-phase as
                        // a constant global phase to the whole state, silently turning the
                        // braid into (at best) identity. Fail explicitly instead: a wrong
                        // quantum state reported as success is worse than an honest error.
                        let anyon1 = anyons.[leftIndex]
                        let anyon2 = anyons.[leftIndex + 1]
                        return!
                            TopologicalResult.notImplemented
                                "Cross-pair anyon braiding"
                                (Some ($"Braiding anyons at positions ({leftIndex}, {leftIndex + 1}) [{anyon1} × {anyon2}] " +
                                       "is not supported for this tree shape: the pair is not an explicitly fused " +
                                       "leaf pair in the current basis, and the F-move machinery required for " +
                                       "cross-pair braids is only implemented for 3-anyon trees. Within-pair braids " +
                                       "(even leaf indices in the σ-pair encoding) are supported."))
            }

    let braidAdjacentAnyons (leftIndex: int) (state: FusionTree.State) : TopologicalResult<Superposition> =
        braidAdjacentAnyonsDirected leftIndex true state
    
    /// Apply a braiding operation to a superposition
    let braidSuperpositionDirected
        (leftIndex: int)
        (isClockwise: bool)
        (superposition: Superposition)
        : TopologicalResult<Superposition> =

        superposition.Terms
        |> List.fold (fun termsResult (amp, state) ->
            topologicalResult {
                let! terms = termsResult
                let! braided = braidAdjacentAnyonsDirected leftIndex isClockwise state

                let expanded =
                    braided.Terms
                    |> List.map (fun (braidAmp, braidedState) -> (amp * braidAmp, braidedState))

                return expanded @ terms
            }
        ) (Ok [])
        |> Result.map (fun terms ->
            { superposition with Terms = List.rev terms }
            |> combineLikeTerms
            |> normalize)

    let braidSuperposition (leftIndex: int) (superposition: Superposition) : TopologicalResult<Superposition> =
        braidSuperpositionDirected leftIndex true superposition
    
    // ========================================================================
    // MEASUREMENT OPERATIONS
    // ========================================================================
    
    /// Measure (fuse) two anyons at specific positions
    /// 
    /// This collapses the quantum state - we learn which fusion channel occurred.
    /// Unlike braiding, measurement is NOT reversible.
    /// 
    /// Returns: List of possible outcomes with their probabilities
    let measureFusion
        (leftIndex: int)
        (state: FusionTree.State)
        : TopologicalResult<(float * OperationResult) list> =
        
        let anyons = FusionTree.leaves state.Tree
        
        // Validation
        if leftIndex < 0 || leftIndex >= anyons.Length - 1 then
            TopologicalResult.validationError "leftIndex" $"Invalid measurement index {leftIndex} for {anyons.Length} anyons"
        else
            let anyon1 = anyons.[leftIndex]
            let anyon2 = anyons.[leftIndex + 1]
            
            topologicalResult {
                // Get all possible fusion outcomes
                let! outcomes = FusionRules.fuse anyon1 anyon2 state.AnyonType
                
                if outcomes.IsEmpty then
                    return! TopologicalResult.logicError "fusion" $"No fusion channels for {anyon1} and {anyon2}"
                else
                    // Born-rule outcome probabilities.
                    //
                    // Case 1 — the measured pair is explicitly fused in this basis state:
                    // the tree stores the pair's fusion channel, so this basis state is an
                    // EIGENSTATE of the measured charge and the outcome is deterministic
                    // (probability 1 for the stored channel). Superposition weighting
                    // across basis states is handled by the callers (e.g.
                    // TopologicalBackend.ApplyMeasure and TopologicalBuilder.measure sum
                    // |amplitude|² per channel), which together give the correct Born rule
                    // from the state's actual amplitudes.
                    //
                    // Case 2 — the tree stores no channel for this pair (bare/cross-pair
                    // measurement with no amplitude information): fall back to the
                    // canonical vacuum-pair Born rule
                    //     P(c | a × b) = d_c / (d_a · d_b)
                    // (quantum dimensions enter LINEARLY; note Σ_c N_ab^c d_c = d_a·d_b,
                    // so these sum to 1). The previous formula d_c²/Σd_c² was incorrect.
                    let outcomeProbs =
                        let raw =
                            match tryFindFusedLeafPairChannel leftIndex state.Tree with
                            | Some storedChannel ->
                                outcomes
                                |> List.map (fun o -> (o, if o.Result = storedChannel then 1.0 else 0.0))
                            | None ->
                                let dA = AnyonSpecies.quantumDimension anyon1
                                let dB = AnyonSpecies.quantumDimension anyon2
                                outcomes
                                |> List.map (fun o -> (o, AnyonSpecies.quantumDimension o.Result / (dA * dB)))
                        // Drop impossible outcomes (probability 0)
                        raw |> List.filter (fun (_, p) -> p > 1e-15)

                    if outcomeProbs.IsEmpty then
                        return!
                            TopologicalResult.logicError
                                "fusion measurement"
                                $"Stored fusion channel at position {leftIndex} is not a valid outcome of {anyon1} × {anyon2}"
                    else

                    // Build result list using fold with Result propagation
                    let! results =
                        outcomeProbs
                        |> List.fold (fun resultsResult (outcome, probability) ->
                            topologicalResult {
                                let! results = resultsResult

                                // Create new anyon list with fusion applied - optimized
                                // Use List.mapi for single-pass construction instead of 3 concatenations
                                let newAnyons =
                                    anyons
                                    |> List.mapi (fun i anyon ->
                                        if i < leftIndex then Some anyon
                                        elif i = leftIndex then Some outcome.Result  // Replace first fused anyon
                                        elif i = leftIndex + 1 then None  // Skip second fused anyon
                                        else Some anyon
                                    )
                                    |> List.choose id
                                
                                // Reconstruct fusion tree (simplified - just a linear chain)
                                let! newTree = 
                                    match newAnyons with
                                    | [] -> TopologicalResult.validationError "anyons" "Cannot create empty tree"
                                    | [p] -> Ok (FusionTree.leaf p)
                                    | p1::rest ->
                                        rest 
                                        |> List.fold (fun treeResult p ->
                                            topologicalResult {
                                                let! tree = treeResult
                                                // Fuse sequentially - in practice need proper tree structure
                                                let intermediate = FusionTree.totalCharge tree state.AnyonType
                                                let! channels = FusionRules.channels intermediate p state.AnyonType
                                                
                                                if channels.IsEmpty then
                                                    return! TopologicalResult.logicError "fusion" $"Cannot fuse {intermediate} and {p}"
                                                else
                                                    // Safe indexing with tryHead
                                                    match List.tryHead channels with
                                                    | None -> return! TopologicalResult.logicError "fusion" "Internal error: channels empty after non-empty check"
                                                    | Some firstChannel ->
                                                        return FusionTree.fuse tree (FusionTree.leaf p) firstChannel
                                            }
                                        ) (Ok (FusionTree.leaf p1))
                                
                                let newState = FusionTree.create newTree state.AnyonType
                                
                                let result = (probability, 
                                              { State = newState
                                                Amplitude = Complex.One
                                                ClassicalOutcome = Some outcome.Result })
                                
                                return result :: results
                            }
                        ) (Ok [])
                    
                    return List.rev results
            }
    
    // ========================================================================
    // COMPOSITE GATES
    // ========================================================================
    
    /// The pair-encoding computational-basis fusion channels (|0⟩-channel, |1⟩-channel)
    /// for each anyon theory, matching FusionTree.fromComputationalBasis:
    ///   Ising:     σ×σ → 1 (bit 0) or ψ (bit 1)
    ///   Fibonacci: τ×τ → 1 (bit 0) or τ (bit 1)
    ///   SU(2)_k:   ½×½ → j=0 (bit 0) or j=1 (bit 1)
    let private qubitChannels (anyonType: AnyonSpecies.AnyonType) : AnyonSpecies.Particle * AnyonSpecies.Particle =
        match anyonType with
        | AnyonSpecies.AnyonType.Fibonacci ->
            AnyonSpecies.Particle.Vacuum, AnyonSpecies.Particle.Tau
        | AnyonSpecies.AnyonType.SU2Level k ->
            AnyonSpecies.Particle.SpinJ(0, k), AnyonSpecies.Particle.SpinJ(2, k)
        | _ ->
            AnyonSpecies.Particle.Vacuum, AnyonSpecies.Particle.Psi

    /// Replace the fusion channel for a specific qubit's anyon pair within a tree,
    /// returning the new, FUSION-CONSISTENT tree. The qubit's pair is the
    /// `qubitIndex`-th (0-based) leaf pair of the left-associated pair chain
    ///   (((pair0 × pair1 → ch01) × pair2 → ch012) × ... )
    /// as produced by FusionTree.fromComputationalBasis.
    ///
    /// After swapping the pair channel this function ALSO:
    ///   1. Recomputes every intermediate (running) charge along the chain with
    ///      the same convention as fromComputationalBasis (vacuum-like charges act
    ///      as identity; equal non-vacuum charges fuse to the vacuum-like charge),
    ///      so each internal node still satisfies the fusion rules; and
    ///   2. For the Ising σ-pair encoding (all-σ leaves, ≥ 2 pairs), updates the
    ///      trailing PARITY pair so the total charge remains Vacuum.
    /// A previous implementation swapped only the pair channel, leaving stale
    /// intermediate charges and parity — every post-gate tree then failed
    /// FusionTree.validateState (and error correction would have "repaired"
    /// legitimate states).
    ///
    /// Returns None if the tree is not a left-associated chain of leaf pairs or
    /// the index is out of range (callers keep the term unchanged in that case).
    let private replaceQubitChannel
        (anyonType: AnyonSpecies.AnyonType)
        (qubitIndex: int)
        (newChannel: AnyonSpecies.Particle)
        (tree: FusionTree.Tree)
        : FusionTree.Tree option =

        // Flatten the left-associated chain into its leaf pairs (a, b, channel).
        let rec collectPairs (t: FusionTree.Tree)
            : (AnyonSpecies.Particle * AnyonSpecies.Particle * AnyonSpecies.Particle) list option =
            match t with
            | FusionTree.Fusion (FusionTree.Leaf a, FusionTree.Leaf b, ch) ->
                Some [ (a, b, ch) ]
            | FusionTree.Fusion (left, FusionTree.Fusion (FusionTree.Leaf a, FusionTree.Leaf b, ch), _) ->
                collectPairs left |> Option.map (fun ps -> ps @ [ (a, b, ch) ])
            | _ -> None

        // Vacuum-like ("zero") charge of the theory, e.g. SpinJ(0,k) for SU(2)_k.
        let zeroCharge = fst (qubitChannels anyonType)
        let isZeroLike (p: AnyonSpecies.Particle) =
            p = zeroCharge || p = AnyonSpecies.Particle.Vacuum

        // Running-charge fusion, mirroring FusionTree.fromComputationalBasis:
        // vacuum-like is the identity; equal charges annihilate to vacuum-like
        // (1×ψ→ψ, ψ×ψ→1; τ×τ→1 by encoding convention; j1×j1→j0 likewise).
        let fuseCharges (c1: AnyonSpecies.Particle) (c2: AnyonSpecies.Particle) =
            if isZeroLike c1 then c2
            elif isZeroLike c2 then c1
            else zeroCharge

        match collectPairs tree with
        | None -> None
        | Some pairs ->

        // Ising σ-pair encoding carries a trailing parity pair (see
        // FusionTree.numQubits): all-σ leaves, ≥ 4 anyons. The parity pair is not
        // an addressable qubit.
        let isIsingParityEncoding =
            pairs.Length >= 2
            && pairs |> List.forall (fun (a, b, _) ->
                a = AnyonSpecies.Particle.Sigma && b = AnyonSpecies.Particle.Sigma)
            && (match anyonType with
                | AnyonSpecies.AnyonType.Ising -> true
                | _ -> false)

        let qubitPairCount = if isIsingParityEncoding then pairs.Length - 1 else pairs.Length

        if qubitIndex < 0 || qubitIndex >= qubitPairCount then None
        else

        // 1. Swap the target pair's channel.
        let withNewChannel =
            pairs |> List.mapi (fun i (a, b, ch) ->
                if i = qubitIndex then (a, b, newChannel) else (a, b, ch))

        // 2. Recompute the Ising parity-pair channel so the total charge is Vacuum:
        //    parity = ψ iff an odd number of qubit pairs carry ψ.
        let updatedPairs =
            if isIsingParityEncoding then
                let qubitChs =
                    withNewChannel |> List.take (pairs.Length - 1) |> List.map (fun (_, _, ch) -> ch)
                let psiCount =
                    qubitChs |> List.filter ((=) AnyonSpecies.Particle.Psi) |> List.length
                let parityChannel =
                    if psiCount % 2 = 0 then AnyonSpecies.Particle.Vacuum
                    else AnyonSpecies.Particle.Psi
                withNewChannel
                |> List.mapi (fun i (a, b, ch) ->
                    if i = pairs.Length - 1 then (a, b, parityChannel) else (a, b, ch))
            else withNewChannel

        // 3. Rebuild the left-associated chain, recomputing intermediate charges.
        let mkPair (a, b, ch) =
            FusionTree.Fusion (FusionTree.Leaf a, FusionTree.Leaf b, ch)
        let channelOf (_, _, ch) = ch

        match updatedPairs with
        | [] -> None
        | [ single ] -> Some (mkPair single)
        | first :: rest ->
            rest
            |> List.fold (fun (acc, runningCharge) p ->
                let newCharge = fuseCharges runningCharge (channelOf p)
                (FusionTree.Fusion (acc, mkPair p, newCharge), newCharge))
                (mkPair first, channelOf first)
            |> fst
            |> Some

    /// Get the fusion channel for a specific qubit's σ-pair.
    /// Returns the channel (Vacuum = |0⟩, Psi = |1⟩) for the qubit at the given index.
    let private getQubitChannel (qubitIndex: int) (tree: FusionTree.Tree) : AnyonSpecies.Particle option =
        let rec findAtPairIndex (idx: int) (currentPairIdx: int) (t: FusionTree.Tree) : (int * AnyonSpecies.Particle option) =
            match t with
            | FusionTree.Fusion (FusionTree.Leaf _, FusionTree.Leaf _, ch) ->
                if currentPairIdx = idx then (currentPairIdx + 1, Some ch)
                else (currentPairIdx + 1, None)
            | FusionTree.Fusion (left, right, _) ->
                match findAtPairIndex idx currentPairIdx left with
                | (nextIdx, Some ch) -> (nextIdx, Some ch)
                | (nextIdx, None) -> findAtPairIndex idx nextIdx right
            | FusionTree.Leaf _ -> (currentPairIdx, None)

        findAtPairIndex qubitIndex 0 tree |> snd

    /// Hadamard gate for topological qubits
    ///
    /// Creates superposition: |0⟩ → (|0⟩ + |1⟩)/√2, |1⟩ → (|0⟩ - |1⟩)/√2
    ///
    /// Operates at the amplitude level by transforming the qubit pair's fusion
    /// channel (Vacuum ↔ ψ for Ising, 1 ↔ τ for Fibonacci) with the standard
    /// Hadamard matrix coefficients.
    ///
    /// Note: TopologicalBackend.ApplyGate handles braiding-faithful H compilation
    /// via GateToBraid + SolovayKitaev. This function provides the exact mathematical
    /// result for direct simulation use.
    let hadamard (qubitIndex: int) (superposition: Superposition) : TopologicalResult<Superposition> =
        // Validate qubit index
        let numQubits =
            match superposition.Terms with
            | [] -> 0
            | (_, state) :: _ -> FusionTree.numQubits state.Tree

        if qubitIndex < 0 || qubitIndex >= numQubits then
            TopologicalResult.validationError
                "qubitIndex"
                $"Invalid qubit index {qubitIndex} for {numQubits}-qubit system"
        else
            // Apply H at the amplitude level:
            //   H|0⟩ = (|0⟩ + |1⟩)/√2
            //   H|1⟩ = (|0⟩ - |1⟩)/√2
            //
            // For each term (amp, state) in the superposition, read the qubit's
            // fusion channel (Vacuum=0, Psi=1), produce two new terms with
            // channels swapped per the Hadamard matrix, and combine.
            let invSqrt2 = 1.0 / sqrt 2.0

            let newTerms =
                superposition.Terms
                |> List.collect (fun (amp, state) ->
                    match getQubitChannel qubitIndex state.Tree with
                    | Some channel ->
                        let chZero, chOne = qubitChannels state.AnyonType
                        let isZero = (channel = chZero)
                        // H|0⟩ = (|0⟩ + |1⟩)/√2  →  amp/√2 for both |0⟩ and |1⟩
                        // H|1⟩ = (|0⟩ - |1⟩)/√2  →  amp/√2 for |0⟩, -amp/√2 for |1⟩
                        let amp0 = amp * Complex(invSqrt2, 0.0)
                        let amp1 =
                            if isZero then amp * Complex(invSqrt2, 0.0)
                            else amp * Complex(-invSqrt2, 0.0)

                        let tree0 = replaceQubitChannel state.AnyonType qubitIndex chZero state.Tree
                        let tree1 = replaceQubitChannel state.AnyonType qubitIndex chOne state.Tree

                        match tree0, tree1 with
                        | Some t0, Some t1 ->
                            [ (amp0, FusionTree.create t0 state.AnyonType)
                              (amp1, FusionTree.create t1 state.AnyonType) ]
                        | _ ->
                            // If tree replacement fails, keep the original term unchanged
                            // (shouldn't happen for well-formed σ-pair trees)
                            [ (amp, state) ]
                    | None ->
                        // Qubit channel not found — keep term unchanged
                        [ (amp, state) ]
                )

            { superposition with Terms = newTerms }
            |> combineLikeTerms
            |> normalize
            |> Ok
    
    /// Controlled-NOT gate for topological qubits
    /// 
    /// Flips target qubit if control is |1⟩
    /// 
    /// Implemented as exact amplitude-level operation on σ-pair fusion channels.
    /// If the control qubit's channel is Psi (|1⟩), the target qubit's channel
    /// is flipped: Vacuum ↔ Psi. Otherwise the state is unchanged.
    ///
    /// This avoids the braiding-level CNOT decomposition (H * CZ * H) which
    /// requires Solovay-Kitaev approximation for the Hadamard components.
    let cnot (controlIndex: int) (targetIndex: int) (superposition: Superposition) : TopologicalResult<Superposition> =
        let numQubits =
            match superposition.Terms with
            | [] -> 0
            | (_, state) :: _ -> FusionTree.numQubits state.Tree

        if controlIndex < 0 || controlIndex >= numQubits then
            TopologicalResult.validationError
                "controlIndex"
                $"Invalid control qubit index {controlIndex} for {numQubits}-qubit system"
        elif targetIndex < 0 || targetIndex >= numQubits then
            TopologicalResult.validationError
                "targetIndex"
                $"Invalid target qubit index {targetIndex} for {numQubits}-qubit system"
        elif controlIndex = targetIndex then
            TopologicalResult.validationError
                "targetIndex"
                "Control and target qubits must be different"
        else
            // CNOT: if control=|1⟩, flip target. Otherwise unchanged.
            let newTerms =
                superposition.Terms
                |> List.choose (fun (amp, state) ->
                    let chZero, chOne = qubitChannels state.AnyonType
                    match getQubitChannel controlIndex state.Tree with
                    | Some controlChannel ->
                        if controlChannel = chOne then
                            // Control is |1⟩ — flip target channel
                            match getQubitChannel targetIndex state.Tree with
                            | Some targetChannel ->
                                let flipped = if targetChannel = chZero then chOne else chZero
                                match replaceQubitChannel state.AnyonType targetIndex flipped state.Tree with
                                | Some newTree -> Some (amp, FusionTree.create newTree state.AnyonType)
                                | None -> Some (amp, state)
                            | None -> Some (amp, state)
                        else
                            // Control is |0⟩ — no change
                            Some (amp, state)
                    | None -> Some (amp, state)
                )

            { superposition with Terms = newTerms }
            |> combineLikeTerms
            |> normalize
            |> Ok

    /// Pauli-X gate (NOT gate) for topological qubits
    ///
    /// Flips the qubit: |0⟩ → |1⟩, |1⟩ → |0⟩
    ///
    /// In the σ-pair encoding: Vacuum (|0⟩) ↔ Psi (|1⟩)
    /// This is exact — no approximation needed.
    let pauliX (qubitIndex: int) (superposition: Superposition) : TopologicalResult<Superposition> =
        let numQubits =
            match superposition.Terms with
            | [] -> 0
            | (_, state) :: _ -> FusionTree.numQubits state.Tree

        if qubitIndex < 0 || qubitIndex >= numQubits then
            TopologicalResult.validationError
                "qubitIndex"
                $"Invalid qubit index {qubitIndex} for {numQubits}-qubit system"
        else
            // X|0⟩ = |1⟩, X|1⟩ = |0⟩
            // Simply swap the fusion channel (Vacuum ↔ ψ for Ising, 1 ↔ τ for Fibonacci)
            let newTerms =
                superposition.Terms
                |> List.choose (fun (amp, state) ->
                    match getQubitChannel qubitIndex state.Tree with
                    | Some channel ->
                        let chZero, chOne = qubitChannels state.AnyonType
                        let flipped = if channel = chZero then chOne else chZero
                        match replaceQubitChannel state.AnyonType qubitIndex flipped state.Tree with
                        | Some newTree -> Some (amp, FusionTree.create newTree state.AnyonType)
                        | None -> Some (amp, state) // fallback: keep unchanged
                    | None -> Some (amp, state)
                )

            { superposition with Terms = newTerms }
            |> combineLikeTerms
            |> normalize
            |> Ok

    /// Pauli-Y gate for topological qubits
    ///
    /// Y|0⟩ = i|1⟩, Y|1⟩ = -i|0⟩
    ///
    /// In the σ-pair encoding: flips the channel with appropriate ±i phases.
    /// This is exact — no approximation needed.
    let pauliY (qubitIndex: int) (superposition: Superposition) : TopologicalResult<Superposition> =
        let numQubits =
            match superposition.Terms with
            | [] -> 0
            | (_, state) :: _ -> FusionTree.numQubits state.Tree

        if qubitIndex < 0 || qubitIndex >= numQubits then
            TopologicalResult.validationError
                "qubitIndex"
                $"Invalid qubit index {qubitIndex} for {numQubits}-qubit system"
        else
            // Y|0⟩ = i|1⟩   → amplitude * i, channel |0⟩-channel → |1⟩-channel
            // Y|1⟩ = -i|0⟩  → amplitude * (-i), channel |1⟩-channel → |0⟩-channel
            let newTerms =
                superposition.Terms
                |> List.choose (fun (amp, state) ->
                    match getQubitChannel qubitIndex state.Tree with
                    | Some channel ->
                        let chZero, chOne = qubitChannels state.AnyonType
                        let isZero = (channel = chZero)
                        let newAmp =
                            if isZero then amp * Complex(0.0, 1.0)   // * i
                            else amp * Complex(0.0, -1.0)            // * (-i)
                        let flipped = if isZero then chOne else chZero
                        match replaceQubitChannel state.AnyonType qubitIndex flipped state.Tree with
                        | Some newTree -> Some (newAmp, FusionTree.create newTree state.AnyonType)
                        | None -> Some (amp, state)
                    | None -> Some (amp, state)
                )

            { superposition with Terms = newTerms }
            |> combineLikeTerms
            |> normalize
            |> Ok

    /// Pauli-Z gate for topological qubits
    ///
    /// Z|0⟩ = |0⟩, Z|1⟩ = -|1⟩
    ///
    /// Unlike the GateToBraid implementation which treats Z as global phase (identity),
    /// this implementation correctly applies the relative phase. This matters in
    /// multi-qubit systems where Z⊗I ≠ I⊗I.
    let pauliZ (qubitIndex: int) (superposition: Superposition) : TopologicalResult<Superposition> =
        let numQubits =
            match superposition.Terms with
            | [] -> 0
            | (_, state) :: _ -> FusionTree.numQubits state.Tree

        if qubitIndex < 0 || qubitIndex >= numQubits then
            TopologicalResult.validationError
                "qubitIndex"
                $"Invalid qubit index {qubitIndex} for {numQubits}-qubit system"
        else
            // Z|0⟩ = |0⟩    → amplitude unchanged, channel unchanged
            // Z|1⟩ = -|1⟩   → amplitude * (-1), channel unchanged
            let newTerms =
                superposition.Terms
                |> List.map (fun (amp, state) ->
                    match getQubitChannel qubitIndex state.Tree with
                    | Some channel ->
                        if channel = snd (qubitChannels state.AnyonType) then
                            (amp * Complex(-1.0, 0.0), state)  // -1 phase for |1⟩
                        else
                            (amp, state)  // unchanged for |0⟩
                    | None -> (amp, state)
                )

            { superposition with Terms = newTerms }
            |> combineLikeTerms
            |> normalize
            |> Ok

    /// T gate (π/8 gate) for topological qubits
    ///
    /// Applies: |0⟩ → |0⟩, |1⟩ → e^{iπ/4} |1⟩
    ///
    /// **PHYSICS**: The T gate CANNOT be realized exactly by Ising anyon braiding.
    /// Ising braids only produce phases that are multiples of π/2 (Clifford gates).
    /// T requires non-topological supplementation (e.g., magic state distillation).
    /// This amplitude-level implementation provides exact T gate behavior.
    let tGate (qubitIndex: int) (superposition: Superposition) : TopologicalResult<Superposition> =
        let numQubits =
            match superposition.Terms with
            | [] -> 0
            | (_, state) :: _ -> FusionTree.numQubits state.Tree

        if qubitIndex < 0 || qubitIndex >= numQubits then
            TopologicalResult.validationError
                "qubitIndex"
                $"Invalid qubit index {qubitIndex} for {numQubits}-qubit system"
        else
            // T|0⟩ = |0⟩    → amplitude unchanged, channel unchanged
            // T|1⟩ = e^{iπ/4} |1⟩  → amplitude * e^{iπ/4}, channel unchanged
            let tPhase = Complex.Exp(Complex(0.0, System.Math.PI / 4.0))  // e^{iπ/4}
            let newTerms =
                superposition.Terms
                |> List.map (fun (amp, state) ->
                    match getQubitChannel qubitIndex state.Tree with
                    | Some channel ->
                        if channel = snd (qubitChannels state.AnyonType) then
                            (amp * tPhase, state)  // e^{iπ/4} phase for |1⟩
                        else
                            (amp, state)  // unchanged for |0⟩
                    | None -> (amp, state)
                )

            { superposition with Terms = newTerms }
            |> combineLikeTerms
            |> normalize
            |> Ok

    /// T† gate (inverse of T gate) for topological qubits
    ///
    /// Applies: |0⟩ → |0⟩, |1⟩ → e^{-iπ/4} |1⟩
    ///
    /// Like T, this cannot be realized by Ising anyon braiding alone.
    let tDaggerGate (qubitIndex: int) (superposition: Superposition) : TopologicalResult<Superposition> =
        let numQubits =
            match superposition.Terms with
            | [] -> 0
            | (_, state) :: _ -> FusionTree.numQubits state.Tree

        if qubitIndex < 0 || qubitIndex >= numQubits then
            TopologicalResult.validationError
                "qubitIndex"
                $"Invalid qubit index {qubitIndex} for {numQubits}-qubit system"
        else
            // T†|0⟩ = |0⟩
            // T†|1⟩ = e^{-iπ/4} |1⟩
            let tDaggerPhase = Complex.Exp(Complex(0.0, -System.Math.PI / 4.0))  // e^{-iπ/4}
            let newTerms =
                superposition.Terms
                |> List.map (fun (amp, state) ->
                    match getQubitChannel qubitIndex state.Tree with
                    | Some channel ->
                        if channel = snd (qubitChannels state.AnyonType) then
                            (amp * tDaggerPhase, state)  // e^{-iπ/4} phase for |1⟩
                        else
                            (amp, state)  // unchanged for |0⟩
                    | None -> (amp, state)
                )

            { superposition with Terms = newTerms }
            |> combineLikeTerms
            |> normalize
            |> Ok

    /// Phase gate Rz(θ) = P(θ) = diag(1, e^{iθ}) for topological qubits
    ///
    /// Applies: |0⟩ → |0⟩, |1⟩ → e^{iθ} |1⟩
    /// (Convention matches GateToBraid/GateTranspiler: Rz(θ) = diag(1, e^{iθ}),
    /// so CP decompositions built from RZ+CNOT reproduce the exact CP unitary.)
    ///
    /// **PHYSICS**: Ising anyon braiding can only realize θ that are multiples of
    /// π/2 (Clifford phases). Arbitrary θ requires non-topological supplementation
    /// (magic states / measurement). This amplitude-level implementation provides
    /// the exact ideal result of such supplementation, mirroring tGate/tDaggerGate.
    let phaseGate (qubitIndex: int) (angle: float) (superposition: Superposition) : TopologicalResult<Superposition> =
        let numQubits =
            match superposition.Terms with
            | [] -> 0
            | (_, state) :: _ -> FusionTree.numQubits state.Tree

        if qubitIndex < 0 || qubitIndex >= numQubits then
            TopologicalResult.validationError
                "qubitIndex"
                $"Invalid qubit index {qubitIndex} for {numQubits}-qubit system"
        else
            // Rz(θ)|0⟩ = |0⟩          → amplitude unchanged
            // Rz(θ)|1⟩ = e^{iθ} |1⟩   → amplitude * e^{iθ}, channel unchanged
            let phase = Complex.Exp(Complex(0.0, angle))
            let newTerms =
                superposition.Terms
                |> List.map (fun (amp, state) ->
                    match getQubitChannel qubitIndex state.Tree with
                    | Some channel ->
                        if channel = snd (qubitChannels state.AnyonType) then
                            (amp * phase, state)   // e^{iθ} phase for |1⟩
                        else
                            (amp, state)           // unchanged for |0⟩
                    | None -> (amp, state)
                )

            { superposition with Terms = newTerms }
            |> combineLikeTerms
            |> normalize
            |> Ok

    /// Apply an arbitrary single-qubit unitary U = [[u00, u01]; [u10, u11]]
    /// (columns indexed by the input channel: Vacuum=|0⟩, Psi=|1⟩) at the
    /// amplitude level. **⚠️ SIMULATOR ONLY** — same status as hadamard/phaseGate:
    /// on real topological hardware this requires non-topological supplementation.
    let private applySingleQubitUnitary
        (u00: Complex) (u01: Complex) (u10: Complex) (u11: Complex)
        (qubitIndex: int)
        (superposition: Superposition) : TopologicalResult<Superposition> =
        let numQubits =
            match superposition.Terms with
            | [] -> 0
            | (_, state) :: _ -> FusionTree.numQubits state.Tree

        if qubitIndex < 0 || qubitIndex >= numQubits then
            TopologicalResult.validationError
                "qubitIndex"
                $"Invalid qubit index {qubitIndex} for {numQubits}-qubit system"
        else
            let newTerms =
                superposition.Terms
                |> List.collect (fun (amp, state) ->
                    match getQubitChannel qubitIndex state.Tree with
                    | Some channel ->
                        let chZero, chOne = qubitChannels state.AnyonType
                        let isZero = (channel = chZero)
                        // Select the U column for the input channel
                        let amp0 = amp * (if isZero then u00 else u01)
                        let amp1 = amp * (if isZero then u10 else u11)

                        let tree0 = replaceQubitChannel state.AnyonType qubitIndex chZero state.Tree
                        let tree1 = replaceQubitChannel state.AnyonType qubitIndex chOne state.Tree

                        match tree0, tree1 with
                        | Some t0, Some t1 ->
                            [ (amp0, FusionTree.create t0 state.AnyonType)
                              (amp1, FusionTree.create t1 state.AnyonType) ]
                        | _ ->
                            [ (amp, state) ]
                    | None ->
                        [ (amp, state) ]
                )

            { superposition with Terms = newTerms }
            |> combineLikeTerms
            |> normalize
            |> Ok

    /// RX(θ) = [[cos(θ/2), -i·sin(θ/2)]; [-i·sin(θ/2), cos(θ/2)]] — exact
    /// amplitude-level X-rotation (simulator-only, like hadamard/phaseGate).
    let rxGate (qubitIndex: int) (angle: float) (superposition: Superposition) : TopologicalResult<Superposition> =
        let c = Complex(cos (angle / 2.0), 0.0)
        let s = Complex(0.0, -sin (angle / 2.0))
        applySingleQubitUnitary c s s c qubitIndex superposition

    /// RY(θ) = [[cos(θ/2), -sin(θ/2)]; [sin(θ/2), cos(θ/2)]] — exact
    /// amplitude-level Y-rotation (simulator-only, like hadamard/phaseGate).
    let ryGate (qubitIndex: int) (angle: float) (superposition: Superposition) : TopologicalResult<Superposition> =
        let c = Complex(cos (angle / 2.0), 0.0)
        let s = Complex(sin (angle / 2.0), 0.0)
        applySingleQubitUnitary c (-s) s c qubitIndex superposition

    /// SWAP gate for topological qubits
    ///
    /// Exchanges the quantum states of two qubits:
    /// SWAP|ab⟩ = |ba⟩
    ///
    /// In the σ-pair encoding, this swaps the fusion channels of two qubit pairs.
    /// This is exact — no approximation needed (avoids 3×CNOT decomposition with
    /// its accumulated Solovay-Kitaev approximation errors).
    let swap (qubitIndex1: int) (qubitIndex2: int) (superposition: Superposition) : TopologicalResult<Superposition> =
        let numQubits =
            match superposition.Terms with
            | [] -> 0
            | (_, state) :: _ -> FusionTree.numQubits state.Tree

        if qubitIndex1 < 0 || qubitIndex1 >= numQubits then
            TopologicalResult.validationError
                "qubitIndex1"
                $"Invalid qubit index {qubitIndex1} for {numQubits}-qubit system"
        elif qubitIndex2 < 0 || qubitIndex2 >= numQubits then
            TopologicalResult.validationError
                "qubitIndex2"
                $"Invalid qubit index {qubitIndex2} for {numQubits}-qubit system"
        elif qubitIndex1 = qubitIndex2 then
            Ok superposition  // SWAP of same qubit is identity
        else
            // Read both channels, write each into the other's position
            let newTerms =
                superposition.Terms
                |> List.choose (fun (amp, state) ->
                    match getQubitChannel qubitIndex1 state.Tree, getQubitChannel qubitIndex2 state.Tree with
                    | Some ch1, Some ch2 ->
                        // Swap: put ch2 at position 1, ch1 at position 2
                        match replaceQubitChannel state.AnyonType qubitIndex1 ch2 state.Tree with
                        | Some tree' ->
                            match replaceQubitChannel state.AnyonType qubitIndex2 ch1 tree' with
                            | Some tree'' -> Some (amp, FusionTree.create tree'' state.AnyonType)
                            | None -> Some (amp, state)
                        | None -> Some (amp, state)
                    | _ -> Some (amp, state)
                )

            { superposition with Terms = newTerms }
            |> combineLikeTerms
            |> normalize
            |> Ok

    // ========================================================================
    // UTILITY FUNCTIONS
    // ========================================================================
    
    /// Calculate the probability of measuring a specific fusion outcome
    let probability (amplitude: Complex) : float =
        let mag = Complex.Abs(amplitude)
        mag * mag
    
    /// Check if a superposition is normalized (probabilities sum to 1)
    let isNormalized (superposition: Superposition) : bool =
        let totalProb = 
            superposition.Terms
            |> List.sumBy (fun (amp, _) -> probability amp)
        
        abs (totalProb - 1.0) < 1e-10
    
    /// Get the dimension of the Hilbert space
    let dimension (superposition: Superposition) : int =
        superposition.Terms.Length
    
    /// Extract all distinct fusion tree states from superposition
    let basisStates (superposition: Superposition) : FusionTree.State list =
        superposition.Terms
        |> List.map snd
        |> List.distinctBy (fun s -> FusionTree.toString s.Tree)
    
    /// Pretty-print a superposition
    let displaySuperposition (superposition: Superposition) : string =
        let terms = 
            superposition.Terms
            |> List.mapi (fun i (amp, state) ->
                let prob = probability amp
                let treeStr = FusionTree.toString state.Tree
                $"  [{i}] {amp.Real:F4} + {amp.Imaginary:F4}i  |  P={prob:F4}  |  {treeStr}"
            )
            |> String.concat "\n"
        
        $"Superposition ({superposition.Terms.Length} terms):\n{terms}\nNormalized: {isNormalized superposition}"
    
    /// Measure all anyons in a superposition and return computational basis outcomes
    /// 
    /// This collapses the quantum superposition by sampling from the probability
    /// distribution of amplitudes. Each measurement produces a classical bitstring.
    /// 
    /// Parameters:
    ///   superposition - Quantum superposition of fusion tree states
    ///   shots - Number of measurement samples to take
    /// 
    /// Returns:
    ///   Array of bitstrings (int[][]), each representing one measurement outcome
    /// 
    /// Algorithm:
    ///   1. Calculate probabilities from amplitudes: P_i = |α_i|²
    ///   2. Sample from probability distribution (shots times)
    ///   3. Convert sampled fusion tree to computational basis bitstring
    let measureAll (superposition: Superposition) (shots: int) : int[][] =
        // Normalize superposition to ensure valid probability distribution
        let normalized = normalize superposition

        // An empty superposition has no outcomes to sample — fail loudly instead
        // of throwing KeyNotFoundException from an index lookup.
        if List.isEmpty normalized.Terms then
            invalidOp "measureAll: cannot measure an empty superposition (no terms)"

        // Calculate cumulative probability distribution for sampling
        let probabilities =
            normalized.Terms
            |> List.map (fun (amp, _) -> probability amp)

        let cumulativeProbs =
            probabilities
            |> List.scan (+) 0.0
            |> List.tail  // Remove initial 0.0

        let lastIndex = cumulativeProbs.Length - 1

        // Use shared random number generator (thread-safe, no per-call allocation)
        let rng = System.Random.Shared

        // Sample function: Given a random value [0,1), return the corresponding
        // term index. Floating-point rounding can leave the final cumulative sum
        // slightly below 1.0 (or below r); clamp to the last index on fall-through
        // instead of throwing KeyNotFoundException.
        let sample (r: float) : int =
            match cumulativeProbs |> List.tryFindIndex (fun cumProb -> r <= cumProb) with
            | Some idx -> idx
            | None -> lastIndex

        // Perform measurements
        [| for _ in 1 .. shots do
            let r = rng.NextDouble()
            let termIndex = sample r
            let (_, state) = normalized.Terms.[termIndex]

            // Convert fusion tree to computational basis bitstring
            let bits = FusionTree.toComputationalBasis state.Tree
            yield List.toArray bits
        |]
    
    /// Calculate probability of measuring a specific bitstring
    /// 
    /// Sums the probabilities (|amplitude|²) of all superposition terms
    /// that correspond to the given bitstring when measured.
    /// 
    /// Parameters:
    ///   bitstring - Target measurement outcome [|b0; b1; ...|]
    ///   superposition - Quantum superposition state
    /// 
    /// Returns:
    ///   Probability ∈ [0, 1] of measuring this bitstring
    let probabilityOfBitstring (bitstring: int[]) (superposition: Superposition) : float =
        // Normalize superposition to ensure valid probability distribution
        let normalized = normalize superposition
        
        // Sum probabilities of all terms that match the target bitstring
        normalized.Terms
        |> List.sumBy (fun (amp, state) ->
            // Convert fusion tree to computational basis
            let bits = FusionTree.toComputationalBasis state.Tree
            let bitsArray = List.toArray bits
            
            // Check if this term matches the target bitstring
            if bitsArray.Length = bitstring.Length && 
               Array.forall2 (=) bitsArray bitstring then
                // Add this term's probability
                probability amp
            else
                0.0)
    
    // ========================================================================
    // QUANTUM STATE INTEROP (for UnifiedQuantumState)
    // ========================================================================
    
    /// Create superposition from fusion trees and amplitudes
    /// 
    /// Compatibility function for QuantumStateConversion module.
    /// 
    /// Parameters:
    ///   trees - List of fusion trees (basis states)
    ///   amplitudes - Array of complex amplitudes (one per tree)
    ///   anyonType - Anyon theory
    /// 
    /// Returns:
    ///   Superposition with trees and amplitudes combined
    let createSuperposition
        (trees: FusionTree.Tree list)
        (amplitudes: Complex[])
        (anyonType: AnyonSpecies.AnyonType)
        : Superposition =
        
        if trees.Length <> amplitudes.Length then
            failwith $"Trees count ({trees.Length}) does not match amplitudes count ({amplitudes.Length})"
        
        let terms =
            List.zip (Array.toList amplitudes) (trees |> List.map (fun t -> FusionTree.create t anyonType))
        
        { Terms = terms; AnyonType = anyonType }
    
    /// Get basis states (trees) from superposition
    /// 
    /// Extracts fusion trees, discarding amplitudes.
    /// Used by QuantumStateConversion.
    let getBasisStates (superposition: Superposition) : FusionTree.Tree list =
        superposition.Terms
        |> List.map (fun (_, state) -> state.Tree)
    
    /// Get amplitudes from superposition
    /// 
    /// Extracts amplitudes as array.
    /// Used by QuantumStateConversion.
    let getAmplitudes (superposition: Superposition) : Complex[] =
        superposition.Terms
        |> List.map fst
        |> Array.ofList
    
    /// Compatibility: Get fields matching QuantumState.FusionSuperposition structure
    /// 
    /// QuantumStateConversion expects: { BasisStates; Amplitudes; AnyonType }
    /// TopologicalOperations uses: { Terms; AnyonType }
    /// 
    /// This creates a view matching the expected structure.
    type SuperpositionView = {
        BasisStates: FusionTree.Tree list
        Amplitudes: Complex[]
        AnyonType: AnyonSpecies.AnyonType
    }
    
    let toView (superposition: Superposition) : SuperpositionView =
        {
            BasisStates = getBasisStates superposition
            Amplitudes = getAmplitudes superposition
            AnyonType = superposition.AnyonType
        }
    
    let fromView (view: SuperpositionView) : Superposition =
        createSuperposition view.BasisStates view.Amplitudes view.AnyonType
    
    // ========================================================================
    // INTERFACE WRAPPER (for cross-package compatibility)
    // ========================================================================
    
    /// Wrapper type that holds a Superposition and implements ITopologicalSuperposition
    /// 
    /// This allows the Core package to work with topological superpositions
    /// without creating a circular dependency, while still allowing the
    /// Topological package to access the underlying Superposition for operations.
    type SuperpositionWrapper(superposition: Superposition) =
        member _.Superposition = superposition
        
        interface ITopologicalSuperposition with
            member _.LogicalQubits =
                match superposition.Terms with
                | [] -> 0
                | (_, state) :: _ -> FusionTree.numQubits state.Tree
            
            member _.MeasureAll shots =
                measureAll superposition shots
            
            member _.Probability bitstring =
                probabilityOfBitstring bitstring superposition
            
            member _.IsNormalized =
                isNormalized superposition

            member this.GetAmplitudeVector () =
                let n = (this :> ITopologicalSuperposition).LogicalQubits
                let dim = 1 <<< n
                let amplitudes = Array.create dim Complex.Zero

                let normalized = normalize superposition
                for (amp, state) in normalized.Terms do
                    let bits = FusionTree.toComputationalBasis state.Tree |> List.toArray
                    // Convert bitstring to basis index (LSB-first: bit[q] = 2^q)
                    // This matches the StateVector/Gates convention used throughout
                    // the codebase: qubit q controls bit q of the array index.
                    let idx = bits |> Array.mapi (fun q b -> b <<< q) |> Array.sum
                    amplitudes.[idx] <- amplitudes.[idx] + amp

                amplitudes
    
    /// Wrap a Superposition in an ITopologicalSuperposition interface
    let toInterface (superposition: Superposition) : ITopologicalSuperposition =
        SuperpositionWrapper(superposition) :> ITopologicalSuperposition
    
    /// Extract the underlying Superposition from an ITopologicalSuperposition
    /// 
    /// Returns None if the interface is not a SuperpositionWrapper.
    let fromInterface (itf: ITopologicalSuperposition) : Superposition option =
        match itf with
        | :? SuperpositionWrapper as wrapper -> Some wrapper.Superposition
        | _ -> None
