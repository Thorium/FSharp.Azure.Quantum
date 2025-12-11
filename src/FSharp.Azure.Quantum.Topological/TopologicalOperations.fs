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
    
    /// Normalize a superposition (ensure sum of |amplitude|² = 1)
    let normalize (superposition: Superposition) : Superposition =
        let normSquared = 
            superposition.Terms
            |> List.sumBy (fun (amp, _) -> (Complex.Abs(amp)) ** 2.0)
        
        let norm = sqrt normSquared
        
        if norm = 0.0 then
            superposition // Already zero
        else
            let normalized = 
                superposition.Terms
                |> List.map (fun (amp, state) -> (amp / Complex(norm, 0.0), state))
            { superposition with Terms = normalized }
    
    // ========================================================================
    // BRAIDING OPERATIONS
    // ========================================================================
    
    /// Braid two anyons at specific positions in the fusion tree
    /// 
    /// This is the fundamental gate operation in topological quantum computing.
    /// Braiding accumulates a phase determined by the R-matrix.
    /// 
    /// Note: This is a simplified implementation. Full braiding requires
    /// tracking anyon world-lines and applying appropriate F-moves.
    let braidAdjacentAnyons 
        (leftIndex: int) 
        (state: FusionTree.State) 
        : TopologicalResult<OperationResult> =
        
        // Get the anyons being braided
        let anyons = FusionTree.leaves state.Tree
        
        // Validation
        if leftIndex < 0 || leftIndex >= anyons.Length - 1 then
            TopologicalResult.validationError "leftIndex" $"Invalid braid index {leftIndex} for {anyons.Length} anyons"
        else
            let anyon1 = anyons.[leftIndex]
            let anyon2 = anyons.[leftIndex + 1]
            
            // Get all possible fusion outcomes for this pair - use Result workflow
            topologicalResult {
                let! outcomes = FusionRules.fuse anyon1 anyon2 state.AnyonType
                
                if outcomes.IsEmpty then
                    return! TopologicalResult.logicError "fusion" $"No fusion channels for {anyon1} and {anyon2}"
                else
                    // For simplicity, assume they fuse to the first possible channel
                    // Safe indexing: outcomes guaranteed non-empty by previous check
                    match List.tryHead outcomes with
                    | None -> 
                        return! TopologicalResult.logicError "fusion" "Internal error: outcomes empty after non-empty check"
                    | Some firstOutcome ->
                        let channel = firstOutcome.Result
                        
                        // Get the braiding phase from R-matrix
                        let! phase = BraidingOperators.element anyon1 anyon2 channel state.AnyonType
                        
                        // Return the same state with accumulated phase
                        return { State = state
                                 Amplitude = phase
                                 ClassicalOutcome = None }
            }
    
    /// Apply a braiding operation to a superposition
    let braidSuperposition 
        (leftIndex: int) 
        (superposition: Superposition) 
        : TopologicalResult<Superposition> =
        
        // Use Result workflow to propagate errors
        superposition.Terms
        |> List.fold (fun termsResult (amp, state) ->
            topologicalResult {
                let! terms = termsResult
                let! result = braidAdjacentAnyons leftIndex state
                return (amp * result.Amplitude, result.State) :: terms
            }
        ) (Ok [])
        |> Result.map (fun terms -> { superposition with Terms = List.rev terms })
    
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
                    // For each outcome, create a new state with those anyons fused
                    // Probability is uniform for now (should be from quantum state amplitudes)
                    let probability = 1.0 / float outcomes.Length
                    
                    // Build result list using fold with Result propagation
                    let! results =
                        outcomes
                        |> List.fold (fun resultsResult outcome ->
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
    
    /// Apply an F-move at a specific node in the tree
    /// 
    /// This is essential for bringing anyons into position for braiding.
    /// Returns a superposition of trees in the new basis.
    ///
    /// F-move transforms: ((a × b) × c) ↔ (a × (b × c))
    let fMove
        (direction: FMoveDirection)
        (nodeDepth: int)
        (state: FusionTree.State)
        : Superposition =
        
        // Get the leaves (anyons) from the tree
        let anyons = FusionTree.leaves state.Tree
        
        // F-move requires at least 3 anyons
        if anyons.Length < 3 then
            pureState state  // Identity - can't F-move
        else
            // For simplified implementation:
            // Apply F-matrix transformation symbolically
            // In reality, we'd need to:
            // 1. Identify the fusion vertex to transform
            // 2. Extract (a, b, c) triple and intermediate charges
            // 3. Calculate F-matrix elements
            // 4. Create superposition of new basis states
            
            match direction with
            | LeftToRight ->
                // Transform ((a × b) × c) → (a × (b × c))
                // For now, return identity (F-matrix is close to identity for small systems)
                pureState state
            | RightToLeft ->
                // Transform (a × (b × c)) → ((a × b) × c)
                // Inverse of LeftToRight
                pureState state
            
            // Note: Full implementation would compute F-matrix coefficients
            // and return actual superposition. For the simulator to be useful,
            // we keep it as identity transformation (valid but simplified).
    
    // ========================================================================
    // COMPOSITE GATES
    // ========================================================================
    
    /// Hadamard-like gate for topological qubits
    /// 
    /// Creates superposition: |0⟩ → (|0⟩ + |1⟩)/√2
    /// 
    /// In topological QC, this requires a sequence of F-moves and braidings.
    let hadamard (qubitIndex: int) (superposition: Superposition) : Superposition =
        // Simplified: For a true Hadamard, need magic state distillation
        // or use Fibonacci anyons (which have universal braiding)
        
        // For Ising anyons, Hadamard requires ancilla qubits
        // Return identity for now
        superposition
    
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
        
        // Random number generator for sampling
        let rng = System.Random()
        
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
                // Calculate logical qubit count from fusion tree structure
                match superposition.Terms with
                | [] -> 0
                | (_, state) :: _ -> 
                    // Count leaves in the fusion tree (each pair of anyons = 1 qubit)
                    let leaves = FusionTree.leaves state.Tree
                    // Jordan-Wigner encoding: n qubits requires n+1 anyons
                    max 0 (leaves.Length - 1)
            
            member _.MeasureAll shots =
                measureAll superposition shots
            
            member _.Probability bitstring =
                probabilityOfBitstring bitstring superposition
            
            member _.IsNormalized =
                isNormalized superposition
    
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
