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
                        // Fallback: use first allowed fusion channel (phase-only).
                        let anyon1 = anyons.[leftIndex]
                        let anyon2 = anyons.[leftIndex + 1]
                        let! outcomes = FusionRules.fuse anyon1 anyon2 state.AnyonType

                        match outcomes |> List.tryHead with
                        | None ->
                            return! TopologicalResult.logicError "fusion" $"No fusion channels for {anyon1} and {anyon2}"
                        | Some firstOutcome ->
                            let! phase = BraidingOperators.element anyon1 anyon2 firstOutcome.Result state.AnyonType
                            return normalize { Terms = [ (conjugateIfInverse isClockwise phase, state) ]; AnyonType = state.AnyonType }
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
                    // Born-rule probabilities from quantum dimensions:
                    // P(c | a × b) = d_c² / Σ_{c'} d_{c'}²
                    // where d_c is the quantum dimension of fusion outcome c.
                    // This is the correct topological measurement probability for
                    // a state whose internal fusion channels are in the canonical basis.
                    let outcomeDimSq =
                        outcomes
                        |> List.map (fun o -> 
                            let d = AnyonSpecies.quantumDimension o.Result
                            (o, d * d))
                    
                    let totalDimSq = outcomeDimSq |> List.sumBy snd
                    
                    // Build result list using fold with Result propagation
                    let! results =
                        outcomeDimSq
                        |> List.fold (fun resultsResult (outcome, dimSq) ->
                            topologicalResult {
                                let! results = resultsResult
                                
                                let probability = dimSq / totalDimSq
                                
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
    
    /// Hadamard gate for topological qubits
    /// 
    /// Creates superposition: |0⟩ → (|0⟩ + |1⟩)/√2, |1⟩ → (|0⟩ - |1⟩)/√2
    /// 
    /// Operates at the amplitude level by transforming σ-pair fusion channels
    /// (Vacuum ↔ Psi) with the standard Hadamard matrix coefficients.
    ///
    /// Note: TopologicalBackend.ApplyGate handles braiding-faithful H compilation
    /// via GateToBraid + SolovayKitaev. This function provides the exact mathematical
    /// result for direct simulation use.
    /// Replace the fusion channel for a specific qubit's σ-pair within a tree,
    /// returning the new tree. The qubit's pair is at depth-first σ-pair index
    /// `qubitIndex` (0-based). Returns None if the qubit pair cannot be found.
    let private replaceQubitChannel
        (qubitIndex: int)
        (newChannel: AnyonSpecies.Particle)
        (tree: FusionTree.Tree)
        : FusionTree.Tree option =

        // In the σ-pair encoding, the tree is a left-associated chain of σ-pairs:
        //   (((pair0 × pair1 → ch01) × pair2 → ch012) × ... × parityPair → Vacuum)
        // Each pairN = Fusion(Leaf σ, Leaf σ, channel_N)
        //
        // We flatten to a list of pairs, replace the channel for the target qubit,
        // and reconstruct.
        let rec collectPairs (t: FusionTree.Tree) : (AnyonSpecies.Particle * FusionTree.Tree) list option =
            match t with
            | FusionTree.Fusion (FusionTree.Leaf _, FusionTree.Leaf _, _) ->
                // Single pair — base case
                Some [(AnyonSpecies.Particle.Vacuum, t)]  // channel placeholder, we'll use the tree directly
            | FusionTree.Fusion (left, right, outerChannel) ->
                match collectPairs left with
                | Some leftPairs ->
                    // right should be a leaf-pair
                    Some (leftPairs @ [(outerChannel, right)])
                | None -> None
            | FusionTree.Leaf _ -> None

        // Simpler approach: directly modify the tree by navigating to the target pair
        let rec replaceAtPairIndex (idx: int) (currentPairIdx: int) (t: FusionTree.Tree) : (int * FusionTree.Tree) option =
            match t with
            | FusionTree.Fusion (FusionTree.Leaf a, FusionTree.Leaf b, _ch) ->
                // This is a σ-pair (leaf pair)
                if currentPairIdx = idx then
                    Some (currentPairIdx + 1, FusionTree.Fusion (FusionTree.Leaf a, FusionTree.Leaf b, newChannel))
                else
                    Some (currentPairIdx + 1, t)  // not our target, keep unchanged
            | FusionTree.Fusion (left, right, ch) ->
                match replaceAtPairIndex idx currentPairIdx left with
                | Some (nextIdx, newLeft) ->
                    match replaceAtPairIndex idx nextIdx right with
                    | Some (finalIdx, newRight) ->
                        Some (finalIdx, FusionTree.Fusion (newLeft, newRight, ch))
                    | None -> None
                | None -> None
            | FusionTree.Leaf _ -> Some (currentPairIdx, t)

        replaceAtPairIndex qubitIndex 0 tree |> Option.map snd

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
                        let isZero = (channel = AnyonSpecies.Particle.Vacuum)
                        // H|0⟩ = (|0⟩ + |1⟩)/√2  →  amp/√2 for both |0⟩ and |1⟩
                        // H|1⟩ = (|0⟩ - |1⟩)/√2  →  amp/√2 for |0⟩, -amp/√2 for |1⟩
                        let amp0 = amp * Complex(invSqrt2, 0.0)
                        let amp1 =
                            if isZero then amp * Complex(invSqrt2, 0.0)
                            else amp * Complex(-invSqrt2, 0.0)

                        let tree0 = replaceQubitChannel qubitIndex AnyonSpecies.Particle.Vacuum state.Tree
                        let tree1 = replaceQubitChannel qubitIndex AnyonSpecies.Particle.Psi state.Tree

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
    /// Implemented via braiding operations in topological QC.
    ///
    /// For Ising anyons (each qubit = 4 sigma anyons):
    /// CNOT requires specific braiding sequence + measurements
    let cnot (controlIndex: int) (targetIndex: int) (superposition: Superposition) : TopologicalResult<Superposition> =
        // CNOT in topological QC requires careful choreography of braidings
        // This is one of the key advantages: gates are geometric, not algebraic!
        
        // For Ising anyons, CNOT protocol:
        // 1. Apply braiding between control and target qubits
        // 2. Use fusion measurement to entangle
        // 3. Apply correction braiding based on measurement outcome
        
        // Simplified implementation:
        // Each qubit is 4 anyons: control = indices [4*controlIndex..4*controlIndex+3]
        //                        target = indices [4*targetIndex..4*targetIndex+3]
        
        let controlStartIdx = 4 * controlIndex
        let targetStartIdx = 4 * targetIndex
        
        // Apply sequence of braidings to implement CNOT - using Result workflow
        topologicalResult {
            let! step1 = braidSuperposition (controlStartIdx + 1) superposition      // Braid within control
            let! step2 = braidSuperposition (targetStartIdx + 1) step1               // Braid within target
            let! step3 = braidSuperposition (controlStartIdx + 3) step2              // Cross-braid control-target
            return step3
        }
        
        // Note: Full topological CNOT requires:
        // - F-moves to align fusion tree
        // - Multiple braiding operations
        // - Measurement and feedforward corrections
        // This simplified version captures the essence but is not complete
    
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
        
        // Calculate cumulative probability distribution for sampling
        let probabilities = 
            normalized.Terms
            |> List.map (fun (amp, _) -> probability amp)
        
        let cumulativeProbs = 
            probabilities
            |> List.scan (+) 0.0
            |> List.tail  // Remove initial 0.0
        
        // Use shared random number generator (thread-safe, no per-call allocation)
        let rng = System.Random.Shared
        
        // Sample function: Given a random value [0,1), return the corresponding term index
        let sample (r: float) : int =
            cumulativeProbs
            |> List.findIndex (fun cumProb -> r <= cumProb)
        
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
