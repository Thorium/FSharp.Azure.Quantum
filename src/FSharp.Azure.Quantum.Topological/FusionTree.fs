namespace FSharp.Azure.Quantum.Topological

/// Fusion trees represent the quantum state of topological qubits
/// 
/// A fusion tree encodes how anyons combine (fuse) to produce a specific outcome.
/// The tree structure captures the order of fusion operations, and each internal
/// node stores a fusion channel (which outcome was selected when two anyons fused).
/// 
/// Key concepts:
/// - **Leaves**: Individual anyons (the "input" particles)
/// - **Internal nodes**: Fusion channels (which outcome: 1 or ψ for σ×σ)
/// - **Root**: Final fusion outcome (the "total charge")
/// - **Basis states**: Different fusion trees = different quantum states
/// 
/// Example: Four sigma anyons can fuse to vacuum in multiple ways:
///   ((σ×σ→1)×(σ×σ→1)) → 1    (different state from)
///   ((σ×σ→ψ)×(σ×σ→ψ)) → 1    (orthogonal quantum states!)
/// 
/// The dimension of the Hilbert space = number of distinct fusion trees
[<RequireQualifiedAccess>]
module FusionTree =
    
    /// A fusion tree node represents either a leaf (anyon) or fusion of subtrees
    type Tree =
        /// Leaf node: A single anyon particle
        | Leaf of particle: AnyonSpecies.Particle
        
        /// Fusion node: Two subtrees fused to produce an intermediate result
        | Fusion of 
            left: Tree * 
            right: Tree * 
            channel: AnyonSpecies.Particle  // Which outcome was selected
    
    /// Fusion tree state: Tree structure + anyon theory context
    type State = {
        /// The fusion tree structure
        Tree: Tree
        
        /// Which anyon theory we're working in (Ising, Fibonacci, etc.)
        AnyonType: AnyonSpecies.AnyonType
    }
    
    // ========================================================================
    // TREE CONSTRUCTION
    // ========================================================================
    
    /// Create a leaf node (single anyon)
    let leaf (particle: AnyonSpecies.Particle) : Tree =
        Leaf particle
    
    /// Fuse two trees with a specific fusion channel
    /// 
    /// Example: fuse (leaf Sigma) (leaf Sigma) Vacuum
    ///   Creates: σ × σ → 1 (vacuum channel selected)
    let fuse (left: Tree) (right: Tree) (channel: AnyonSpecies.Particle) : Tree =
        Fusion (left, right, channel)
    
    /// Create a fusion tree state with theory context
    let create (tree: Tree) (anyonType: AnyonSpecies.AnyonType) : State =
        { Tree = tree; AnyonType = anyonType }
    
    // ========================================================================
    // TREE INSPECTION
    // ========================================================================
    
    /// Get all leaf particles (anyons) in the tree (left-to-right order)
    let leaves (tree: Tree) : AnyonSpecies.Particle list =
        let rec collect (tree: Tree) (acc: AnyonSpecies.Particle list) : AnyonSpecies.Particle list =
            match tree with
            | Leaf p -> p :: acc
            | Fusion (left, right, _) -> collect left (collect right acc)
        collect tree []
    
    /// Get the total charge (root fusion outcome)
    let rec totalCharge (tree: Tree) (anyonType: AnyonSpecies.AnyonType) : AnyonSpecies.Particle =
        match tree with
        | Leaf p -> p
        | Fusion (_, _, channel) -> channel
    
    /// Count the number of anyons (leaves) in the tree
    let rec size (tree: Tree) : int =
        match tree with
        | Leaf _ -> 1
        | Fusion (left, right, _) -> size left + size right
    
    /// Get the depth (height) of the fusion tree
    let rec depth (tree: Tree) : int =
        match tree with
        | Leaf _ -> 0
        | Fusion (left, right, _) -> 1 + max (depth left) (depth right)
    
    // ========================================================================
    // TREE VALIDATION
    // ========================================================================
    
    /// Verify that a fusion tree is valid according to fusion rules
    /// 
    /// Checks:
    /// 1. All particles are valid in the given anyon theory
    /// 2. Each fusion node's channel is possible given its children
    /// 3. Left-to-right fusion order is consistent
    /// Returns Error if validation fails or anyon type is not implemented.
    let rec isValid (tree: Tree) (anyonType: AnyonSpecies.AnyonType) : TopologicalResult<bool> =
        match tree with
        | Leaf p -> 
            AnyonSpecies.isValid anyonType p
        
        | Fusion (left, right, channel) ->
            // Validate subtrees first
            match isValid left anyonType, isValid right anyonType with
            | Error err, _ | _, Error err -> Error err
            | Ok false, _ | _, Ok false -> Ok false
            | Ok true, Ok true ->
                // Get the fusion outcomes of left and right subtrees
                let leftCharge = totalCharge left anyonType
                let rightCharge = totalCharge right anyonType
                
                // Verify this fusion is possible
                match AnyonSpecies.isValid anyonType channel, FusionRules.isPossible leftCharge rightCharge channel anyonType with
                | Error err, _ | _, Error err -> Error err
                | Ok validChannel, Ok possibleFusion -> Ok (validChannel && possibleFusion)
    
    /// Validate a fusion tree state (tree + theory consistency)
    let validateState (state: State) : TopologicalResult<unit> =
        match isValid state.Tree state.AnyonType with
        | Error err -> Error err
        | Ok true -> Ok ()
        | Ok false -> Error (TopologicalError.Other "Invalid fusion tree: fusion channels inconsistent with anyon theory")
    
    // ========================================================================
    // HILBERT SPACE DIMENSION
    // ========================================================================
    
    /// Count the number of distinct fusion trees for given anyons and total charge
    /// 
    /// This is the Hilbert space dimension for this topological qubit encoding.
    /// 
    /// Example: 4 Sigma anyons fusing to Vacuum
    ///   Dimension = 2 (two orthogonal quantum states)
    /// Returns Error if anyon type is not implemented.
    let rec fusionSpaceDimension 
        (particles: AnyonSpecies.Particle list) 
        (totalCharge: AnyonSpecies.Particle)
        (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<int> =
        
        match particles with
        | [] -> Ok 0
        | [p] -> Ok (if p = totalCharge then 1 else 0)
        | [a; b] -> FusionRules.multiplicity a b totalCharge anyonType
        | _ ->
            // For n > 2 particles, we need to consider all possible binary fusion trees
            // This is a recursive calculation over tree structures
            
            match AnyonSpecies.particles anyonType with
            | Error err -> Error err
            | Ok allParticles ->
            
            // Split particles into left and right groups (all possible splits)
            let splits = 
                [1 .. particles.Length - 1]
                |> List.map (fun i -> 
                    List.splitAt i particles
                )
            
            // For each split, count trees that fuse to totalCharge via intermediate channels
            let intermediateResults =
                splits
                |> List.collect (fun (leftParticles, rightParticles) ->
                    // Try all possible intermediate fusion channels
                    allParticles
                    |> List.map (fun intermediate ->
                        match fusionSpaceDimension leftParticles intermediate anyonType,
                              fusionSpaceDimension rightParticles intermediate anyonType,
                              FusionRules.multiplicity intermediate intermediate totalCharge anyonType with
                        | Ok leftDim, Ok rightDim, Ok finalMultiplicity ->
                            // Total dimension is product of possibilities
                            Ok (leftDim * rightDim * finalMultiplicity)
                        | Error err, _, _ -> Error err
                        | _, Error err, _ -> Error err
                        | _, _, Error err -> Error err
                    )
                )
            
            // Aggregate results: stop on first error or sum all dimensions
            match intermediateResults |> List.choose (function Ok dim -> Some dim | Error _ -> None) with
            | dims when dims.Length = intermediateResults.Length -> 
                Ok (List.sum dims)  // All succeeded
            | _ -> 
                // Find first error
                match intermediateResults |> List.tryPick (function Error e -> Some e | Ok _ -> None) with
                | Some err -> Error err
                | None -> Error (TopologicalError.Other "Unexpected state in fusionSpaceDimension")
    
    // ========================================================================
    // TREE ENUMERATION
    // ========================================================================
    
    /// Generate all valid fusion trees for given particles and total charge
    /// 
    /// Returns a list of all possible fusion trees (basis states)
    /// Returns Error if anyon type is not implemented.
    let rec allTrees 
        (particles: AnyonSpecies.Particle list)
        (totalCharge: AnyonSpecies.Particle)
        (anyonType: AnyonSpecies.AnyonType)
        : TopologicalResult<Tree list> =
        
        match particles with
        | [] -> Ok []
        | [p] -> 
            Ok (if p = totalCharge then [Leaf p] else [])
        
        | [a; b] ->
            // Base case: two particles
            match FusionRules.isPossible a b totalCharge anyonType with
            | Error err -> Error err
            | Ok true -> Ok [Fusion (Leaf a, Leaf b, totalCharge)]
            | Ok false -> Ok []
        
        | _ ->
            // Recursive case: try all binary splits
            match AnyonSpecies.particles anyonType with
            | Error err -> Error err
            | Ok channels ->
            
            let splits = 
                [1 .. particles.Length - 1]
                |> List.map (fun i -> List.splitAt i particles)
            
            let allResults =
                splits
                |> List.collect (fun (leftParticles, rightParticles) ->
                    channels
                    |> List.collect (fun intermediate ->
                        // Get all trees for left side fusing to intermediate
                        match allTrees leftParticles intermediate anyonType,
                              allTrees rightParticles intermediate anyonType,
                              FusionRules.isPossible intermediate intermediate totalCharge anyonType with
                        | Ok leftTrees, Ok rightTrees, Ok true ->
                            // Combine all left and right trees
                            [ for leftTree in leftTrees do
                              for rightTree in rightTrees do
                                Ok (Fusion (leftTree, rightTree, totalCharge)) ]
                        | Ok _, Ok _, Ok false -> []
                        | Error err, _, _ -> [Error err]
                        | _, Error err, _ -> [Error err]
                        | _, _, Error err -> [Error err]
                    )
                )
            
            // Check if any errors occurred
            match allResults |> List.tryPick (function Error e -> Some e | Ok _ -> None) with
            | Some err -> Error err
            | None -> 
                Ok (allResults |> List.choose (function Ok tree -> Some tree | Error _ -> None))
    
    // ========================================================================
    // TREE EQUALITY
    // ========================================================================
    
    /// Check if two fusion trees are structurally equal
    /// 
    /// Two trees are equal if they have the same structure and same fusion channels
    let rec equals (tree1: Tree) (tree2: Tree) : bool =
        match tree1, tree2 with
        | Leaf p1, Leaf p2 -> p1 = p2
        | Fusion (l1, r1, c1), Fusion (l2, r2, c2) ->
            c1 = c2 && equals l1 l2 && equals r1 r2
        | _ -> false
    
    // ========================================================================
    // PRETTY PRINTING
    // ========================================================================
    
    /// Convert tree to string representation
    let rec toString (tree: Tree) : string =
        match tree with
        | Leaf p -> $"{p}"
        | Fusion (left, right, channel) ->
            $"({toString left} × {toString right} → {channel})"
    
    /// Pretty-print a fusion tree state
    let display (state: State) : string =
        let treeStr = toString state.Tree
        let chargeStr = totalCharge state.Tree state.AnyonType
        let sizeStr = size state.Tree
        $"Tree: {treeStr}\nTotal Charge: {chargeStr}\nAnyons: {sizeStr}\nTheory: {state.AnyonType}"
    // ========================================================================
    // COMPUTATIONAL BASIS CONVERSION (for QuantumState interop)
    // ========================================================================
    
    /// Get number of logical qubits encoded in fusion tree
    /// 
    /// Encoding notes:
    /// - **Ising (σ anyons)**: uses 2*(n+1) sigma anyons:
    ///     - n pairs encode n logical qubits (each pair fuses to 1/ψ)
    ///     - 1 extra parity pair enforces total charge vacuum
    ///     - Formula: (anyonCount / 2) - 1
    /// - **Fibonacci (τ anyons)**: uses 2*n tau anyons:
    ///     - n pairs encode n logical qubits (each pair fuses to 1/τ)
    ///     - No parity pair needed (Fibonacci has no fermion parity constraint)
    ///     - Formula: anyonCount / 2
    /// - Other anyon types keep legacy heuristics.
    let numQubits (tree: Tree) : int =
        let anyonCount = size tree
        let leavesList = leaves tree

        // Ising σ-pair encoding: 2*(n+1) σ anyons
        let isAllSigma = leavesList |> List.forall ((=) AnyonSpecies.Particle.Sigma)
        if isAllSigma && anyonCount >= 4 && anyonCount % 2 = 0 then
            max 0 ((anyonCount / 2) - 1)
        else
            // Fibonacci τ-pair encoding: 2*n τ anyons (no parity pair)
            let isAllTau = leavesList |> List.forall ((=) AnyonSpecies.Particle.Tau)
            if isAllTau && anyonCount >= 2 && anyonCount % 2 = 0 then
                anyonCount / 2
            else
                // Legacy fallback
                max 0 (anyonCount - 1)
    
    /// Convert computational basis bitstring to fusion tree
    /// 
    /// σ-pair encoding for Ising anyons:
    /// - Qubit i is encoded in a pair (σ × σ → 1 or ψ)
    /// - An extra σ-pair is appended to enforce overall vacuum (fermion parity constraint)
    /// 
    /// Parameters:
    ///   bits - List of bits [b₀, b₁, ..., bₙ₋₁] where bᵢ ∈ {0, 1}
    ///   anyonType - Anyon theory (Ising, Fibonacci, etc.)
    /// 
    /// Returns:
    ///   Fusion tree encoding this computational basis state
    /// 
    /// Example:
    ///   fromComputationalBasis [1; 0; 1] Ising
    ///   → |101⟩ in Ising anyon representation
    let fromComputationalBasis (bits: int list) (anyonType: AnyonSpecies.AnyonType) : TopologicalResult<Tree> =
        match anyonType with
        | AnyonSpecies.AnyonType.Ising ->
            // Each bit determines fusion outcome for its σ-pair:
            // 0 → σ × σ → 1 (vacuum)
            // 1 → σ × σ → ψ (fermion)
            let qubitPairs =
                bits
                |> List.map (fun bit ->
                    let channel =
                        if bit = 0 then AnyonSpecies.Particle.Vacuum
                        else AnyonSpecies.Particle.Psi

                    Fusion (
                        Leaf AnyonSpecies.Particle.Sigma,
                        Leaf AnyonSpecies.Particle.Sigma,
                        channel
                    )
                )

            // Extra parity pair to enforce total vacuum (overall fermion parity constraint)
            let parityChannel =
                let ones = bits |> List.sum
                if ones % 2 = 0 then AnyonSpecies.Particle.Vacuum else AnyonSpecies.Particle.Psi

            let parityPair =
                Fusion (
                    Leaf AnyonSpecies.Particle.Sigma,
                    Leaf AnyonSpecies.Particle.Sigma,
                    parityChannel
                )

            let pairTrees = qubitPairs @ [parityPair]

            // Fuse all pairs left-to-right, tracking running charge
            // Each intermediate channel must equal the fusion of accumulated charge
            // with the next pair's charge. Ising fusion: 1×1→1, 1×ψ→ψ, ψ×1→ψ, ψ×ψ→1
            match pairTrees with
            | [] -> Leaf AnyonSpecies.Particle.Vacuum
            | [single] -> single
            | first :: rest ->
                let firstCharge =
                    match first with
                    | Fusion (_, _, ch) -> ch
                    | Leaf p -> p
                rest
                |> List.fold (fun (acc, runningCharge) tree ->
                    let pairCharge =
                        match tree with
                        | Fusion (_, _, ch) -> ch
                        | Leaf p -> p
                    // Ising fusion: Vacuum acts as identity, Psi×Psi→Vacuum
                    let newCharge =
                        match runningCharge, pairCharge with
                        | AnyonSpecies.Particle.Vacuum, c | c, AnyonSpecies.Particle.Vacuum -> c
                        | AnyonSpecies.Particle.Psi, AnyonSpecies.Particle.Psi -> AnyonSpecies.Particle.Vacuum
                        | _ -> AnyonSpecies.Particle.Vacuum // fallback
                    (Fusion (acc, tree, newCharge), newCharge)
                ) (first, firstCharge)
                |> fst
            |> Ok
        
        | AnyonSpecies.AnyonType.Fibonacci ->
            // Similar encoding for Fibonacci anyons
            // 0 → τ × τ → 1 (vacuum)
            // 1 → τ × τ → τ
            let pairTrees =
                bits
                |> List.map (fun bit ->
                    let channel = 
                        if bit = 0 then AnyonSpecies.Particle.Vacuum
                        else AnyonSpecies.Particle.Tau
                    
                    Fusion (Leaf AnyonSpecies.Particle.Tau,
                           Leaf AnyonSpecies.Particle.Tau,
                           channel)
                )
            
            match pairTrees with
            | [] -> Leaf AnyonSpecies.Particle.Vacuum
            | [single] -> single
            | first :: rest ->
                let firstCharge =
                    match first with
                    | Fusion (_, _, ch) -> ch
                    | Leaf p -> p
                rest
                |> List.fold (fun (acc, runningCharge) tree ->
                    let pairCharge =
                        match tree with
                        | Fusion (_, _, ch) -> ch
                        | Leaf p -> p
                    // Fibonacci fusion: Vacuum is identity, τ×τ → pick Vacuum
                    // (standard convention for computational basis encoding)
                    let newCharge =
                        match runningCharge, pairCharge with
                        | AnyonSpecies.Particle.Vacuum, c | c, AnyonSpecies.Particle.Vacuum -> c
                        | AnyonSpecies.Particle.Tau, AnyonSpecies.Particle.Tau -> AnyonSpecies.Particle.Vacuum
                        | _ -> AnyonSpecies.Particle.Vacuum // fallback
                    (Fusion (acc, tree, newCharge), newCharge)
                ) (first, firstCharge)
                |> fst
            |> Ok
        
        | _ ->
            TopologicalResult.notImplemented
                "Computational basis encoding"
                (Some $"Encoding not yet implemented for anyon type {anyonType}")
    
    /// Convert fusion tree to computational basis bitstring
    /// 
    /// Inverse of fromComputationalBasis.
    /// Decodes Jordan-Wigner encoding back to classical bits.
    /// 
    /// Parameters:
    ///   tree - Fusion tree to decode
    /// 
    /// Returns:
    ///   List of bits [b₀, b₁, ..., bₙ₋₁]
    /// 
    /// Note: Assumes tree was created via fromComputationalBasis.
    /// For general fusion trees (superpositions), this extracts one
    /// component of the decomposition.
    let toComputationalBasis (tree: Tree) : int list =
        let rec toComputationalBasisRaw (treeRaw: Tree) : int list =
            match treeRaw with
            | Leaf AnyonSpecies.Particle.Vacuum -> []
            | Leaf _ -> [0]  // Single anyon → 0 bit

            | Fusion (Leaf AnyonSpecies.Particle.Sigma,
                      Leaf AnyonSpecies.Particle.Sigma,
                      channel) ->
                // Single σ-pair encodes one bit
                match channel with
                | AnyonSpecies.Particle.Vacuum -> [0]
                | AnyonSpecies.Particle.Psi -> [1]
                | _ -> [0]

            | Fusion (Leaf AnyonSpecies.Particle.Tau,
                      Leaf AnyonSpecies.Particle.Tau,
                      channel) ->
                // Fibonacci encoding
                match channel with
                | AnyonSpecies.Particle.Vacuum -> [0]
                | AnyonSpecies.Particle.Tau -> [1]
                | _ -> [0]

            | Fusion (left, right, _) ->
                // Recursive: Decode left and right subtrees
                toComputationalBasisRaw left @ toComputationalBasisRaw right

        let rawBits = toComputationalBasisRaw tree

        // If the tree came from Ising σ-pair encoding, the last bit is an internal parity pair.
        // This function is documented as decoding trees created via fromComputationalBasis,
        // so this heuristic is acceptable.
        let leafParticles = leaves tree
        let sigmaCount = leafParticles |> List.filter ((=) AnyonSpecies.Particle.Sigma) |> List.length
        let isAllSigma = leafParticles |> List.forall ((=) AnyonSpecies.Particle.Sigma)
        let isSigmaPairEncoding =
            isAllSigma && sigmaCount >= 4 && sigmaCount % 2 = 0 && rawBits.Length = sigmaCount / 2

        if isSigmaPairEncoding && rawBits.Length > 0 then
            rawBits |> List.take (rawBits.Length - 1)
        else
            rawBits
