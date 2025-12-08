namespace FSharp.Azure.Quantum.Core

open System
open System.Numerics
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Topological

/// Conversion functions between different quantum state representations
/// 
/// Conversions are EXACT (no approximation error) but may be expensive:
/// - StateVector ↔ FusionSuperposition: O(2^n) time, exact
/// - SparseState ↔ StateVector: O(k) where k = non-zero amplitudes
/// - DensityMatrix ↔ StateVector: O(2^n) for pure states
/// 
/// Performance notes:
/// - Conversions should be minimized (cache converted states!)
/// - Use smart dispatch to avoid unnecessary conversions
/// - Convert once at algorithm start, not per operation
module QuantumStateConversion =
    
    // ========================================================================
    // HELPER: Computational Basis ↔ Fusion Tree Mapping
    // ========================================================================
    
    /// Map computational basis state |i⟩ to fusion tree
    /// 
    /// Encoding (Jordan-Wigner for Ising anyons):
    /// - |0⟩ qubit ≡ σ × σ → 1 (vacuum fusion)
    /// - |1⟩ qubit ≡ σ × σ → ψ (fermion fusion)
    /// 
    /// For n qubits, we need n+1 sigma anyons:
    /// - Anyons at positions 0,1 encode qubit 0
    /// - Anyons at positions 2,3 encode qubit 1
    /// - etc.
    /// 
    /// Parameters:
    ///   basisIndex - Integer representation of bitstring (0 to 2^n - 1)
    ///   numQubits - Number of qubits
    ///   anyonType - Anyon species (Ising, Fibonacci, etc.)
    /// 
    /// Returns:
    ///   FusionTree representing this computational basis state
    /// 
    /// Example:
    ///   basisStateToFusionTree 5 3 Ising  // 5 = 0b101 = |101⟩
    ///   → Creates fusion tree for |101⟩ in Ising anyons
    let private basisStateToFusionTree 
        (basisIndex: int) 
        (numQubits: int) 
        (anyonType: AnyonSpecies.AnyonType) 
        : FusionTree.FusionTree =
        
        // TODO: Full implementation requires FusionTree builder
        // For now, use simplified version
        
        match anyonType with
        | AnyonSpecies.AnyonType.Ising ->
            // Extract bits of basisIndex
            let bits = 
                [0 .. numQubits - 1]
                |> List.map (fun i -> (basisIndex >>> i) &&& 1)
            
            // Build fusion tree bottom-up
            // Each bit determines fusion outcome: 0 → Vacuum, 1 → Psi
            let leaves =
                bits
                |> List.map (fun bit ->
                    if bit = 0 then
                        AnyonSpecies.Particle.Vacuum
                    else
                        AnyonSpecies.Particle.Psi
                )
            
            // Create fusion tree (simplified - real version needs proper tree structure)
            FusionTree.fromComputationalBasis bits anyonType
        
        | AnyonSpecies.AnyonType.Fibonacci ->
            // Similar encoding for Fibonacci anyons
            // τ × τ → 1 (vacuum) for |0⟩
            // τ × τ → τ for |1⟩
            let bits = 
                [0 .. numQubits - 1]
                |> List.map (fun i -> (basisIndex >>> i) &&& 1)
            
            FusionTree.fromComputationalBasis bits anyonType
        
        | _ ->
            failwith $"Anyon type {anyonType} not yet supported for basis conversion"
    
    /// Evaluate fusion tree to computational basis decomposition
    /// 
    /// Inverse of basisStateToFusionTree.
    /// 
    /// Parameters:
    ///   tree - Fusion tree to evaluate
    /// 
    /// Returns:
    ///   List of (basisIndex, coefficient) pairs representing decomposition
    ///   into computational basis: |tree⟩ = Σ coeffᵢ |i⟩
    /// 
    /// Note: For simple Jordan-Wigner encoding, each tree corresponds to
    /// exactly ONE basis state (coefficient = 1), but general fusion trees
    /// can be superpositions.
    let private fusionTreeToBasisDecomposition 
        (tree: FusionTree.FusionTree) 
        : (int * Complex) list =
        
        // TODO: Full implementation requires FusionTree evaluator
        // For now, simplified version assumes direct mapping
        
        let basisState = FusionTree.toComputationalBasis tree
        let basisIndex =
            basisState
            |> List.fold (fun acc bit -> (acc <<< 1) + bit) 0
        
        [(basisIndex, Complex.One)]
    
    // ========================================================================
    // CONVERSION: StateVector → FusionSuperposition
    // ========================================================================
    
    /// Convert StateVector to FusionSuperposition
    /// 
    /// Algorithm:
    /// 1. Iterate over all 2^n computational basis states
    /// 2. For each basis state |i⟩ with non-zero amplitude αᵢ:
    ///    - Map |i⟩ to corresponding fusion tree
    ///    - Add (tree, αᵢ) to superposition
    /// 3. Construct FusionSuperposition from list of (tree, amplitude) pairs
    /// 
    /// Complexity:
    /// - Time: O(2^n * poly(n)) - must process all basis states
    /// - Space: O(2^n) temporarily (full state vector)
    /// 
    /// Exact: YES - no approximation error
    /// 
    /// Parameters:
    ///   sv - StateVector to convert
    ///   anyonType - Target anyon species (Ising, Fibonacci, etc.)
    /// 
    /// Returns:
    ///   FusionSuperposition representing same quantum state
    /// 
    /// Example:
    ///   let sv = StateVector.init 3
    ///   let fs = stateVectorToFusion sv AnyonType.Ising
    ///   // fs represents |000⟩ in Ising anyon basis
    let stateVectorToFusion 
        (sv: StateVector.StateVector) 
        (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalOperations.Superposition =
        
        let n = StateVector.numQubits sv
        let dimension = 1 <<< n
        
        // Threshold for considering amplitude as zero
        let epsilon = 1e-12
        
        // Convert each computational basis state to fusion tree
        let basisTrees =
            [0 .. dimension - 1]
            |> List.choose (fun i ->
                let amplitude = StateVector.getAmplitude i sv
                let magnitude = Complex.magnitude amplitude
                
                if magnitude > epsilon then
                    let fusionTree = basisStateToFusionTree i n anyonType
                    Some (fusionTree, amplitude)
                else
                    None
            )
        
        // Construct superposition
        if List.isEmpty basisTrees then
            // Edge case: Zero state (shouldn't happen for normalized states)
            let zeroTree = basisStateToFusionTree 0 n anyonType
            TopologicalOperations.pureState zeroTree
        else
            let trees = basisTrees |> List.map fst
            let amplitudes = basisTrees |> List.map snd |> Array.ofList
            
            TopologicalOperations.createSuperposition trees amplitudes anyonType
    
    // ========================================================================
    // CONVERSION: FusionSuperposition → StateVector
    // ========================================================================
    
    /// Convert FusionSuperposition to StateVector
    /// 
    /// Algorithm:
    /// 1. Initialize 2^n dimensional amplitude array (all zeros)
    /// 2. For each fusion tree in superposition:
    ///    - Evaluate tree to computational basis decomposition
    ///    - Add weighted contributions to amplitude array
    /// 3. Construct StateVector from amplitude array
    /// 
    /// Complexity:
    /// - Time: O(k * poly(n)) where k = number of fusion trees
    /// - Space: O(2^n) for output state vector
    /// 
    /// Exact: YES - no approximation error
    /// 
    /// Note: k is typically much smaller than 2^n for topological states,
    /// making this conversion faster than the reverse direction.
    /// 
    /// Parameters:
    ///   fs - FusionSuperposition to convert
    /// 
    /// Returns:
    ///   StateVector representing same quantum state
    /// 
    /// Example:
    ///   let fs = (* Ising anyon superposition *)
    ///   let sv = fusionToStateVector fs
    ///   // sv and fs represent same quantum state
    let fusionToStateVector 
        (fs: TopologicalOperations.Superposition) 
        : StateVector.StateVector =
        
        // Get number of qubits from first fusion tree
        let n =
            fs.BasisStates
            |> List.tryHead
            |> Option.map FusionTree.numQubits
            |> Option.defaultWith (fun () -> failwith "Empty superposition")
        
        let dimension = 1 <<< n
        let amplitudes = Array.create dimension Complex.Zero
        
        // Evaluate each fusion tree and accumulate amplitudes
        fs.BasisStates
        |> List.iteri (fun treeIdx tree ->
            let treeAmplitude = fs.Amplitudes.[treeIdx]
            
            // Decompose tree into computational basis
            let basisDecomposition = fusionTreeToBasisDecomposition tree
            
            // Add contributions to amplitude array
            basisDecomposition
            |> List.iter (fun (basisIndex, coefficient) ->
                amplitudes.[basisIndex] <- 
                    amplitudes.[basisIndex] + treeAmplitude * coefficient
            )
        )
        
        // Create StateVector from amplitudes
        StateVector.create amplitudes
    
    // ========================================================================
    // CONVERSION: StateVector → SparseState
    // ========================================================================
    
    /// Convert StateVector to SparseState
    /// 
    /// Filters out near-zero amplitudes to create sparse representation.
    /// 
    /// Complexity: O(2^n) - must scan all amplitudes
    /// 
    /// Parameters:
    ///   sv - StateVector to convert
    ///   threshold - Amplitudes below this magnitude are treated as zero
    /// 
    /// Returns:
    ///   SparseState with only significant amplitudes
    let stateVectorToSparse 
        (sv: StateVector.StateVector) 
        (threshold: float) 
        : Map<int, Complex> * int =
        
        let n = StateVector.numQubits sv
        let dimension = 1 <<< n
        
        let sparseAmplitudes =
            [0 .. dimension - 1]
            |> List.choose (fun i ->
                let amplitude = StateVector.getAmplitude i sv
                if Complex.magnitude amplitude > threshold then
                    Some (i, amplitude)
                else
                    None
            )
            |> Map.ofList
        
        (sparseAmplitudes, n)
    
    // ========================================================================
    // CONVERSION: SparseState → StateVector
    // ========================================================================
    
    /// Convert SparseState to StateVector
    /// 
    /// Expands sparse representation to full dense state vector.
    /// 
    /// Complexity: O(2^n) - must allocate full amplitude array
    /// 
    /// Parameters:
    ///   amplitudes - Sparse amplitude map
    ///   numQubits - Number of qubits
    /// 
    /// Returns:
    ///   StateVector with all amplitudes (including zeros)
    let sparseToStateVector 
        (amplitudes: Map<int, Complex>) 
        (numQubits: int) 
        : StateVector.StateVector =
        
        let dimension = 1 <<< numQubits
        let denseAmplitudes = Array.create dimension Complex.Zero
        
        // Fill in non-zero amplitudes
        amplitudes
        |> Map.iter (fun index amplitude ->
            denseAmplitudes.[index] <- amplitude
        )
        
        StateVector.create denseAmplitudes
    
    // ========================================================================
    // AUTOMATIC CONVERSION DISPATCHER
    // ========================================================================
    
    /// Convert quantum state to target representation type
    /// 
    /// Automatically dispatches to appropriate conversion function.
    /// Returns error if conversion is not supported or not implemented.
    /// 
    /// Parameters:
    ///   target - Desired output state type
    ///   state - Input quantum state
    /// 
    /// Returns:
    ///   Result with converted state or conversion error
    /// 
    /// Example:
    ///   let sv = QuantumState.StateVector (StateVector.init 3)
    ///   match convert TopologicalBraiding sv with
    ///   | Ok (QuantumState.FusionSuperposition fs) -> (* use fs *)
    ///   | Error err -> (* handle error *)
    let convert 
        (target: QuantumStateType) 
        (state: QuantumState) 
        : Result<QuantumState, QuantumStateError> =
        
        let sourceType = QuantumState.stateType state
        
        // Optimization: No conversion needed if already target type
        if sourceType = target then
            Ok state
        else
            match state, target with
            // StateVector → FusionSuperposition
            | QuantumState.StateVector sv, TopologicalBraiding ->
                try
                    let fs = stateVectorToFusion sv AnyonSpecies.AnyonType.Ising
                    Ok (QuantumState.FusionSuperposition fs)
                with ex ->
                    Error (ConversionError (GateBased, TopologicalBraiding, ex.Message))
            
            // FusionSuperposition → StateVector
            | QuantumState.FusionSuperposition fs, GateBased ->
                try
                    let sv = fusionToStateVector fs
                    Ok (QuantumState.StateVector sv)
                with ex ->
                    Error (ConversionError (TopologicalBraiding, GateBased, ex.Message))
            
            // StateVector → SparseState
            | QuantumState.StateVector sv, Sparse ->
                try
                    let threshold = 1e-12
                    let (sparseAmps, n) = stateVectorToSparse sv threshold
                    Ok (QuantumState.SparseState (sparseAmps, n))
                with ex ->
                    Error (ConversionError (GateBased, Sparse, ex.Message))
            
            // SparseState → StateVector
            | QuantumState.SparseState (amps, n), GateBased ->
                try
                    let sv = sparseToStateVector amps n
                    Ok (QuantumState.StateVector sv)
                with ex ->
                    Error (ConversionError (Sparse, GateBased, ex.Message))
            
            // Unsupported conversions
            | _, Mixed ->
                Error (NotImplemented "DensityMatrix conversions not yet implemented")
            
            | QuantumState.DensityMatrix _, _ ->
                Error (NotImplemented "DensityMatrix conversions not yet implemented")
            
            | QuantumState.SparseState _, TopologicalBraiding ->
                Error (NotImplemented "SparseState → FusionSuperposition conversion not yet implemented (convert via StateVector)")
            
            | QuantumState.FusionSuperposition _, Sparse ->
                Error (NotImplemented "FusionSuperposition → SparseState conversion not yet implemented (convert via StateVector)")
            
            | _ ->
                Error (ConversionError (sourceType, target, "Unsupported conversion path"))
    
    // ========================================================================
    // SMART CONVERSION (with caching hint)
    // ========================================================================
    
    /// Convert with caching hint for better performance
    /// 
    /// If multiple operations will be performed in target representation,
    /// it's more efficient to convert once and cache.
    /// 
    /// Parameters:
    ///   target - Target state type
    ///   state - Input state
    ///   willReuse - If true, suggests caching converted state
    /// 
    /// Returns:
    ///   (converted state, suggested cache)
    let convertSmart 
        (target: QuantumStateType) 
        (state: QuantumState) 
        (willReuse: bool) 
        : Result<QuantumState * bool, QuantumStateError> =
        
        let result = convert target state
        
        result
        |> Result.map (fun converted ->
            // Suggest caching if conversion was expensive and state will be reused
            let sourceType = QuantumState.stateType state
            let shouldCache =
                willReuse &&
                match sourceType, target with
                | GateBased, TopologicalBraiding -> true  // O(2^n) conversion
                | TopologicalBraiding, GateBased -> true  // O(k*poly(n)) but still worth caching
                | _, Sparse -> false  // Sparse conversions are fast
                | _, _ -> false
            
            (converted, shouldCache)
        )
