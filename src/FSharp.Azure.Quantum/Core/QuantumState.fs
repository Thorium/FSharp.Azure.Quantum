namespace FSharp.Azure.Quantum.Core

open System
open System.Numerics
open FSharp.Azure.Quantum.LocalSimulator

/// Unified quantum state representation supporting multiple backend types
/// 
/// Design rationale:
/// - Discriminated union enables type-safe pattern matching
/// - Each case wraps a backend-specific state type
/// - Extensible: add DensityMatrix, SparseState, etc. in future
/// - Zero-cost abstraction when compiled (no vtable dispatch)
/// 
/// Architecture:
/// - StateVector: Gate-based quantum computing (2^n amplitudes)
/// - FusionSuperposition: Topological quantum computing (fusion trees)
/// - SparseState: Clifford simulation (stabilizer states)
/// - DensityMatrix: Mixed states, open quantum systems
/// 
/// Usage:
///   let state = QuantumState.StateVector (StateVector.init 3)
///   match state with
///   | QuantumState.StateVector sv -> (* gate operations *)
///   | QuantumState.FusionSuperposition (fs, _) -> (* braiding operations *)
[<RequireQualifiedAccess>]
type QuantumState =
    /// Gate-based quantum state (2^n complex amplitudes)
    /// 
    /// Representation: |ψ⟩ = Σ αᵢ|i⟩ where αᵢ are complex amplitudes
    /// 
    /// Used by:
    /// - LocalBackend (classical simulation)
    /// - IonQ, Rigetti (cloud quantum hardware)
    /// - Azure Quantum Resource Estimator
    /// 
    /// Properties:
    /// - Memory: O(2^n) complex numbers
    /// - Single-qubit gate: O(2^n) operations
    /// - Two-qubit gate: O(2^n) operations
    /// - Exact for arbitrary unitaries
    /// - Maximum ~20 qubits (1M dimensions = 16MB memory)
    /// 
    /// Best for:
    /// - Small circuits (n ≤ 15 qubits)
    /// - Arbitrary gate sets
    /// - Quick prototyping
    | StateVector of StateVector.StateVector
    
    /// Topological quantum state (superposition of fusion trees)
    /// 
    /// Represents quantum state as weighted combinations of fusion outcomes.
    /// Used when backend implements topological quantum computing:
    /// - TopologicalBackend (anyonic simulation)
    /// 
    /// NOTE: This stores the superposition as an Object to avoid circular dependency
    /// between FSharp.Azure.Quantum and FSharp.Azure.Quantum.Topological projects.
    /// The actual type is TopologicalOperations.Superposition from the Topological package.
    /// The int is the logical qubit count.
    | FusionSuperposition of superposition:obj * logicalQubits:int
    
    /// Sparse quantum state (non-zero amplitudes only)
    /// 
    /// Representation: Map<basisIndex, amplitude> + metadata
    /// 
    /// Used by:
    /// - Clifford simulation (stabilizer formalism)
    /// - Sparse Hamiltonian simulation
    /// 
    /// Properties:
    /// - Memory: O(k) where k = number of non-zero amplitudes
    /// - Clifford gates: Polynomial time (Gottesman-Knill)
    /// - Non-Clifford gates: May expand to dense representation
    /// - Exact for stabilizer states
    /// 
    /// Best for:
    /// - Clifford-only circuits (exponential speedup!)
    /// - Large sparse states
    /// - Stabilizer simulation
    /// 
    /// Note: Not yet implemented - placeholder for future
    | SparseState of amplitudes: Map<int, Complex> * numQubits: int
    
    /// Density matrix (mixed quantum states)
    /// 
    /// Representation: ρ = Σ pᵢ |ψᵢ⟩⟨ψᵢ| (2^n × 2^n matrix)
    /// 
    /// Used by:
    /// - Noisy quantum simulation
    /// - Open quantum systems
    /// - Decoherence modeling
    /// 
    /// Properties:
    /// - Memory: O(4^n) complex numbers (2^n × 2^n matrix)
    /// - Gate application: O(4^n) operations
    /// - Supports mixed states (classical uncertainty)
    /// - Supports decoherence channels
    /// 
    /// Best for:
    /// - Noisy circuit simulation
    /// - Quantum error correction studies
    /// - Realistic hardware modeling
    /// 
    /// Note: Not yet implemented - placeholder for future
    | DensityMatrix of matrix: Complex[,] * numQubits: int
    
    /// Ising model state (quantum annealing / D-Wave)
    /// 
    /// Representation: Collection of spin samples s ∈ {-1, +1}^n with energies
    /// 
    /// Used by:
    /// - D-Wave quantum annealers (Advantage, 2000Q)
    /// - Quantum annealing simulation
    /// - Optimization problems (QUBO, MaxCut, TSP, etc.)
    /// 
    /// Properties:
    /// - NOT a gate-based quantum state (different computational model!)
    /// - Represents SAMPLES from quantum annealing process
    /// - Each sample is a classical configuration + energy + metadata
    /// - No quantum superposition (collapsed to classical samples)
    /// 
    /// Architecture:
    /// - IsingProblem: Problem specification (h, J coefficients)
    /// - DWaveSolution list: Samples from annealing runs
    /// - Each solution: spins, energy, occurrences, chain breaks
    /// 
    /// Best for:
    /// - Combinatorial optimization (TSP, MaxCut, scheduling)
    /// - Problems naturally expressed as QUBO/Ising
    /// - Large problem sizes (5000+ variables on Advantage)
    /// 
    /// NOT suitable for:
    /// - Gate-based quantum algorithms (Shor, Grover, QFT)
    /// - Quantum state tomography
    /// - Quantum simulation requiring superposition
    /// 
    /// Example:
    ///   // From D-Wave annealing result
    ///   let isingState = QuantumState.IsingSamples (isingProblem, dwaveSolutions)
    ///   
    ///   // Get best solution
    ///   match isingState with
    ///   | QuantumState.IsingSamples (problem, solutions) ->
    ///       let best = solutions |> List.minBy (fun s -> s.Energy)
    ///       printfn "Best energy: %f" best.Energy
    | IsingSamples of problem: obj * solutions: obj  // Using obj to avoid circular dependency with DWaveTypes

/// Metadata about quantum state representation type
/// 
/// Used to determine optimal backend and operations.
type QuantumStateType =
    /// Gate-based representation (StateVector)
    | GateBased
    
    /// Topological representation (FusionSuperposition)
    | TopologicalBraiding
    
    /// Sparse representation (SparseState)
    | Sparse
    
    /// Mixed state representation (DensityMatrix)
    | Mixed
    
    /// Quantum annealing representation (IsingSamples)
    | Annealing

/// Error types for quantum state operations
type QuantumStateError =
    /// Conversion between state types failed
    | ConversionError of source: QuantumStateType * target: QuantumStateType * reason: string
    
    /// Unsupported operation for this state type
    | UnsupportedOperation of operation: string * stateType: QuantumStateType
    
    /// Invalid state (e.g., non-normalized, inconsistent dimensions)
    | InvalidState of reason: string
    
    /// Feature not yet implemented
    | NotImplemented of feature: string
    
    member this.Message =
        match this with
        | ConversionError (src, tgt, reason) ->
            $"Cannot convert {src} to {tgt}: {reason}"
        | UnsupportedOperation (op, stateType) ->
            $"Operation '{op}' not supported for {stateType} states"
        | InvalidState reason ->
            $"Invalid quantum state: {reason}"
        | NotImplemented feature ->
            $"Feature not yet implemented: {feature}"

/// Operations on unified quantum states
module QuantumState =
    
    /// Convert obj containing F# list or IList to a uniform sequence
    let private objToSeq (obj: obj) : obj seq =
        match obj with
        | :? System.Collections.IEnumerable as enumerable ->
            enumerable |> Seq.cast<obj>
        | _ -> Seq.empty
    
    /// Get number of qubits/variables in state
    /// 
    /// Returns the number of logical qubits (gate-based) or variables (annealing) represented by this quantum state.
    /// 
    /// Note: For topological states, this is the number of LOGICAL qubits,
    /// not the number of physical anyons (which is n+1 for Jordan-Wigner encoding).
    /// For Ising states, this is the number of spin variables.
    let numQubits (state: QuantumState) : int =
        match state with
        | QuantumState.StateVector sv ->
            StateVector.numQubits sv
        
        | QuantumState.FusionSuperposition (_, logicalQubits) ->
            // FusionSuperposition stores the logical qubit count explicitly
            logicalQubits
        
        | QuantumState.SparseState (_, n) ->
            n
        
        | QuantumState.DensityMatrix (_, n) ->
            n
        
        | QuantumState.IsingSamples (problem, _) ->
            // Extract numQubits from IsingProblem (stored as obj to avoid circular dependency)
            // Uses reflection to access LinearCoeffs and QuadraticCoeffs maps
            let problemType = problem.GetType()
            let linearCoeffs = problemType.GetProperty("LinearCoeffs").GetValue(problem) :?> Map<int, float>
            let quadraticCoeffs = problemType.GetProperty("QuadraticCoeffs").GetValue(problem) :?> Map<(int * int), float>
            
            let allIndices = seq {
                yield! linearCoeffs |> Map.keys
                yield! quadraticCoeffs |> Map.keys |> Seq.collect (fun (i, j) -> [i; j])
            }
            
            if Seq.isEmpty allIndices then 0
            else Seq.max allIndices + 1
    
    /// Convert StateVector.StateVector to QuantumState
    /// 
    /// Creates a QuantumState from a LocalSimulator StateVector.
    /// This is useful for bridging between the LocalSimulator types
    /// and the unified backend abstraction.
    /// 
    /// Example:
    /// ```fsharp
    /// let stateVec = StateVector.init 3
    /// let quantumState = QuantumState.fromStateVector stateVec
    /// ```
    let fromStateVector (sv: StateVector.StateVector) : QuantumState =
        QuantumState.StateVector sv
    
    /// Get native representation type
    /// 
    /// Returns which type of quantum state representation is being used.
    let stateType (state: QuantumState) : QuantumStateType =
        match state with
        | QuantumState.StateVector _ -> GateBased
        | QuantumState.FusionSuperposition _ -> TopologicalBraiding
        | QuantumState.SparseState _ -> Sparse
        | QuantumState.DensityMatrix _ -> Mixed
        | QuantumState.IsingSamples _ -> Annealing
    
    /// Check if state is pure (vs mixed)
    /// 
    /// Pure states: Can be represented as |ψ⟩ (ket vector)
    /// Mixed states: Require density matrix ρ
    /// 
    /// Returns:
    ///   true if state is pure (StateVector, FusionSuperposition, SparseState)
    ///   false if state is mixed (DensityMatrix)
    let isPure (state: QuantumState) : bool =
        match state with
        | QuantumState.StateVector _ -> true
        | QuantumState.FusionSuperposition _ -> true
        | QuantumState.SparseState _ -> true
        | QuantumState.DensityMatrix _ -> false
        | QuantumState.IsingSamples _ -> false  // Annealing samples are classical (collapsed)
    
    /// Get dimension of state space (2^n for n qubits)
    let dimension (state: QuantumState) : int =
        let n = numQubits state
        1 <<< n  // 2^n
    
    /// Measure all qubits and get classical bitstrings
    /// 
    /// Parameters:
    ///   state - Quantum state to measure
    ///   shots - Number of measurement samples
    /// 
    /// Returns:
    ///   Array of bitstrings, each bitstring is int[] where [|b0; b1; ...|]
    ///   represents measurement outcome with bi ∈ {0, 1}
    /// 
    /// Note: Measurement COLLAPSES the quantum state. For multiple measurements,
    /// this function samples from the probability distribution without collapsing
    /// (i.e., performs independent measurements on copies of the state).
    let measure (state: QuantumState) (shots: int) : int[][] =
        match state with
        | QuantumState.StateVector sv ->
            // Use LocalSimulator's measurement
            [| for _ in 1 .. shots do
                yield Measurement.measureAll sv
            |]
        
        | QuantumState.FusionSuperposition (fs, _) ->
            // Measure fusion outcomes and convert to computational basis
            // Implementation delegated to TopologicalBackend module to avoid circular dependency
            failwith "FusionSuperposition measurement not yet implemented - use QuantumStateConversion.convert to StateVector first, or call TopologicalBackend.sampleMeasurements directly"
        
        | QuantumState.SparseState _ ->
            failwith "SparseState not yet implemented"
        
        | QuantumState.DensityMatrix _ ->
            failwith "DensityMatrix not yet implemented"
        
        | QuantumState.IsingSamples (problem, solutions) ->
            // Sample from D-Wave annealing solutions using reflection
            let solutionsSeq = objToSeq solutions
            let n = numQubits state
            let rng = System.Random()
            
            let spinToBit = function -1 -> 0 | _ -> 1
            
            let spinsTobitstring (spins: Map<int, int>) =
                Array.init n (fun i -> spins |> Map.tryFind i |> Option.map spinToBit |> Option.defaultValue 0)
            
            if Seq.isEmpty solutionsSeq then
                Array.replicate shots (Array.zeroCreate n)
            else
                // Build weighted sample pool based on NumOccurrences
                let samplePool =
                    solutionsSeq
                    |> Seq.collect (fun sol ->
                        let solType = sol.GetType()
                        let spins = solType.GetProperty("Spins").GetValue(sol) :?> Map<int, int>
                        let occurrences = solType.GetProperty("NumOccurrences").GetValue(sol) :?> int
                        Seq.replicate occurrences spins
                    )
                    |> Array.ofSeq
                
                // Sample with replacement from solution pool
                Array.init shots (fun _ -> 
                    samplePool.[rng.Next(samplePool.Length)] |> spinsTobitstring
                )
    
    /// Get probability of measuring specific bitstring
    /// 
    /// Parameters:
    ///   bitstring - Target bitstring [|b0; b1; ...; bn-1|]
    ///   state - Quantum state
    /// 
    /// Returns:
    ///   Probability ∈ [0, 1] of measuring this bitstring
    /// 
    /// Example:
    ///   let bellState = (* create |00⟩ + |11⟩ *)
    ///   probability [|0;0|] bellState = 0.5
    ///   probability [|1;1|] bellState = 0.5
    ///   probability [|0;1|] bellState = 0.0
    let probability (bitstring: int[]) (state: QuantumState) : float =
        if bitstring.Length <> numQubits state then
            failwith $"Bitstring length {bitstring.Length} does not match state qubits {numQubits state}"
        
        match state with
        | QuantumState.StateVector sv ->
            // Convert bitstring to basis index
            let index = 
                bitstring 
                |> Array.fold (fun acc bit -> (acc <<< 1) + bit) 0
            
            let amplitude = StateVector.getAmplitude index sv
            let prob = amplitude.Magnitude
            prob * prob  // |α|²
        
        | QuantumState.FusionSuperposition (fs, _) ->
            // Probability calculation for fusion superposition
            // Implementation delegated to TopologicalBackend module to avoid circular dependency
            failwith "FusionSuperposition probability not yet implemented - use QuantumStateConversion.convert to StateVector first"
        
        | QuantumState.SparseState (amplitudes, n) ->
            let index = 
                bitstring 
                |> Array.fold (fun acc bit -> (acc <<< 1) + bit) 0
            
            match Map.tryFind index amplitudes with
            | Some amplitude ->
                let prob = amplitude.Magnitude
                prob * prob
            | None -> 0.0  // Not in sparse representation → amplitude is 0
        
        | QuantumState.DensityMatrix (rho, n) ->
            // Probability = ⟨bitstring|ρ|bitstring⟩ = ρ[i,i]
            let index = 
                bitstring 
                |> Array.fold (fun acc bit -> (acc <<< 1) + bit) 0
            
            let diagonalElement = rho.[index, index]
            diagonalElement.Magnitude  // Already real for density matrix diagonal
        
        | QuantumState.IsingSamples (problem, solutions) ->
            // For annealing samples, compute empirical probability from solution occurrences
            // This is NOT a quantum probability - these are classical samples
            let solutionsSeq = objToSeq solutions
            
            if Seq.isEmpty solutionsSeq then
                0.0
            else
                let spinToBit = function -1 -> 0 | _ -> 1
                let spinsToBitstring spins i =
                    Map.tryFind i spins |> Option.map spinToBit |> Option.defaultValue 0
                
                let (totalOcc, matchingOcc) =
                    solutionsSeq
                    |> Seq.fold (fun (total, matching) sol ->
                        let solType = sol.GetType()
                        let spins = solType.GetProperty("Spins").GetValue(sol) :?> Map<int, int>
                        let occ = solType.GetProperty("NumOccurrences").GetValue(sol) :?> int
                        
                        // Check if this solution matches the bitstring
                        let matches = 
                            bitstring
                            |> Array.mapi (fun i bit -> spinsToBitstring spins i = bit)
                            |> Array.forall id
                        
                        (total + occ, if matches then matching + occ else matching)
                    ) (0, 0)
                
                float matchingOcc / float totalOcc
    
    /// Check if state is normalized (‖ψ‖ = 1)
    /// 
    /// Returns true if state is properly normalized, false otherwise.
    /// 
    /// Tolerance: Accepts ‖ψ‖ within [1 - ε, 1 + ε] where ε = 1e-10
    let isNormalized (state: QuantumState) : bool =
        match state with
        | QuantumState.StateVector sv ->
            // Check if state vector is normalized (∑|αᵢ|² ≈ 1)
            let n = StateVector.numQubits sv
            let dim = 1 <<< n
            let totalProb =
                [0 .. dim - 1]
                |> List.sumBy (fun i -> 
                    let amp = StateVector.getAmplitude i sv
                    let magnitude = amp.Magnitude
                    magnitude * magnitude
                )
            abs (totalProb - 1.0) < 1e-10
        
        | QuantumState.FusionSuperposition (fs, _) ->
            // Cannot directly check normalization of obj type
            // Assume topological states are properly normalized by their constructors
            true
        
        | QuantumState.SparseState (amplitudes, _) ->
            let totalProb =
                amplitudes
                |> Map.toSeq
                |> Seq.sumBy (fun (_, amp) ->
                    let magnitude = amp.Magnitude
                    magnitude * magnitude
                )
            
            abs (totalProb - 1.0) < 1e-10
        
        | QuantumState.DensityMatrix (rho, n) ->
            // Trace(ρ) should be 1
            let dim = 1 <<< n
            let trace =
                [0 .. dim - 1]
                |> List.sumBy (fun i -> rho.[i, i].Magnitude)
            
            abs (trace - 1.0) < 1e-10
        
        | QuantumState.IsingSamples (_, solutions) ->
            // Annealing samples are always "normalized" (they are classical samples)
            // No quantum superposition to normalize
            true
    
    /// Create string representation of state (for debugging)
    /// 
    /// Returns human-readable description of quantum state.
    /// For large states, truncates output.
    let toString (state: QuantumState) : string =
        let n = numQubits state
        let dim = dimension state
        
        match state with
        | QuantumState.StateVector sv ->
            if dim <= 8 then
                // Small state: Show all amplitudes
                let amplitudeStrs =
                    [0 .. dim - 1]
                    |> List.map (fun i ->
                        let amp = StateVector.getAmplitude i sv
                        let bitstring = Convert.ToString(i, 2).PadLeft(n, '0')
                        $"|{bitstring}⟩: {amp.Real:F4} + {amp.Imaginary:F4}i"
                    )
                    |> String.concat "\n  "
                
                $"StateVector ({n} qubits, {dim} dimensions):\n  {amplitudeStrs}"
            else
                // Large state: Just show metadata
                $"StateVector ({n} qubits, {dim} dimensions, {dim * 16}B memory)"
        
        | QuantumState.FusionSuperposition (fs, _) ->
            // We can't access fields from obj, so provide minimal info
            $"FusionSuperposition ({n} qubits, topological state)"
        
        | QuantumState.SparseState (amplitudes, n) ->
            let numNonZero = Map.count amplitudes
            $"SparseState ({n} qubits, {numNonZero}/{dim} non-zero amplitudes)"
        
        | QuantumState.DensityMatrix (_, n) ->
            $"DensityMatrix ({n} qubits, {dim}×{dim} matrix, {dim * dim * 16}B memory)"
        
        | QuantumState.IsingSamples (_, solutions) ->
            // Show D-Wave annealing results summary
            let solutionsSeq = objToSeq solutions
            
            if Seq.isEmpty solutionsSeq then
                $"IsingSamples ({n} variables, no solutions)"
            else
                let solutionsList = Seq.toList solutionsSeq
                let numSolutions = List.length solutionsList
                
                let (totalSamples, bestEnergy) =
                    solutionsList
                    |> List.fold (fun (total, best) sol ->
                        let solType = sol.GetType()
                        let occ = solType.GetProperty("NumOccurrences").GetValue(sol) :?> int
                        let energy = solType.GetProperty("Energy").GetValue(sol) :?> float
                        (total + occ, min best energy)
                    ) (0, System.Double.MaxValue)
                
                $"IsingSamples ({n} variables, {numSolutions} unique solutions, {totalSamples} samples, best energy: {bestEnergy:F4})"
