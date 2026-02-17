namespace FSharp.Azure.Quantum.Core

open System
open System.Numerics
open FSharp.Azure.Quantum.LocalSimulator

/// Interface for topological quantum state representations
/// 
/// This interface allows the Topological package to provide measurement
/// and probability calculation without creating circular dependencies.
type ITopologicalSuperposition =
    /// Get the number of logical qubits
    abstract member LogicalQubits : int
    
    /// Measure all qubits and return bitstrings
    abstract member MeasureAll : shots:int -> int[][]
    
    /// Calculate probability of measuring a specific bitstring
    abstract member Probability : bitstring:int[] -> float
    
    /// Check if superposition is normalized
    abstract member IsNormalized : bool

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
///   | QuantumState.FusionSuperposition fs -> (* braiding operations *)
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
    /// The superposition implements ITopologicalSuperposition interface,
    /// which provides measurement and probability calculation methods.
    /// The actual type is TopologicalOperations.Superposition from the Topological package.
    | FusionSuperposition of superposition:ITopologicalSuperposition
    
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
    /// Status: Read/analysis operations supported (numQubits, measure, probability,
    /// isNormalized, toString). Conversion to/from StateVector available via
    /// QuantumStateConversion. Gate application and backend initialization not yet
    /// available -- states can be constructed directly or via conversion.
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
    /// Status: Read/analysis operations supported (numQubits, measure, probability,
    /// isNormalized, toString). Gate application and backend initialization not yet
    /// available -- states can be constructed directly.
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
[<Struct>]
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
            Seq.cast<obj> enumerable
        | _ -> Seq.empty
    
    /// Convert bitstring to basis index
    let private bitstringToIndex (bitstring: int[]) : int =
        Array.fold (fun acc bit -> (acc <<< 1) + bit) 0 bitstring
    
    /// Convert basis index to bitstring representation
    let private indexToBitstring (n: int) (index: int) : int[] =
        Array.init n (fun i -> (index >>> (n - 1 - i)) &&& 1)
    
    /// Sample index from probability distribution using cumulative sampling
    let private sampleFromDistribution (rng: Random) (probabilities: (int * float)[]) (totalProb: float) : int =
        let r = rng.NextDouble() * totalProb
        probabilities
        |> Array.scan (fun (_, cumul) (idx, prob) -> (idx, cumul + prob)) (0, 0.0)
        |> Array.tail
        |> Array.tryFind (fun (_, cumul) -> cumul >= r)
        |> Option.map fst
        |> Option.defaultWith (fun () -> fst probabilities.[probabilities.Length - 1])
    
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
        
        | QuantumState.FusionSuperposition superposition ->
            // FusionSuperposition provides logical qubit count via interface
            superposition.LogicalQubits
        
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
            
            let allIndices = 
                seq {
                    yield! Map.keys linearCoeffs
                    yield! Map.keys quadraticCoeffs |> Seq.collect (fun (i, j) -> [i; j])
                }
            
            if Seq.isEmpty allIndices then 
                0
            else 
                Seq.max allIndices + 1
    
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
            Array.init shots (fun _ -> Measurement.measureAll sv)
        
        | QuantumState.FusionSuperposition superposition ->
            // Measure fusion outcomes and convert to computational basis
            // Delegate to the superposition's MeasureAll method (interface call)
            superposition.MeasureAll shots
        
        | QuantumState.SparseState (amplitudes, n) ->
            // Implement measurement for SparseState by sampling from probability distribution
            let rng = Random()
            
            let probabilities =
                amplitudes
                |> Map.toArray
                |> Array.map (fun (idx, amp) -> 
                    let prob = amp.Magnitude
                    idx, prob * prob)
                |> Array.sortBy fst
            
            let totalProb = Array.sumBy snd probabilities
            
            let sampleOnce () =
                let selectedIdx = sampleFromDistribution rng probabilities totalProb
                indexToBitstring n selectedIdx
            
            Array.init shots (fun _ -> sampleOnce ())
        
        | QuantumState.DensityMatrix (rho, n) ->
            // Implement measurement for DensityMatrix by sampling from diagonal
            let rng = Random()
            let dim = 1 <<< n
            
            let probabilities =
                Array.init dim (fun i -> i, rho.[i, i].Real)  // Diagonal elements are real and represent probabilities
            
            let totalProb = Array.sumBy snd probabilities
            
            let sampleOnce () =
                let selectedIdx = sampleFromDistribution rng probabilities totalProb
                indexToBitstring n selectedIdx
            
            Array.init shots (fun _ -> sampleOnce ())
        
        | QuantumState.IsingSamples (problem, solutions) ->
            // Sample from D-Wave annealing solutions using reflection
            let solutionsSeq = objToSeq solutions
            let n = numQubits state
            let rng = System.Random()
            
            let spinToBit = function 
                | -1 -> 0 
                | _ -> 1
            
            let spinsToBitstring (spins: Map<int, int>) =
                Array.init n (fun i -> 
                    spins 
                    |> Map.tryFind i 
                    |> Option.map spinToBit 
                    |> Option.defaultValue 0)
            
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
                    samplePool.[rng.Next(samplePool.Length)] |> spinsToBitstring)
    
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
        let n = numQubits state
        if bitstring.Length <> n then
            invalidArg (nameof bitstring) $"Bitstring length {bitstring.Length} does not match state qubits {n}"
        
        let index = bitstringToIndex bitstring
        
        match state with
        | QuantumState.StateVector sv ->
            let amplitude = StateVector.getAmplitude index sv
            let prob = amplitude.Magnitude
            prob * prob  // |α|²
        
        | QuantumState.FusionSuperposition superposition ->
            // Probability calculation for fusion superposition
            // Delegate to the superposition's Probability method (interface call)
            superposition.Probability bitstring
        
        | QuantumState.SparseState (amplitudes, _) ->
            amplitudes
            |> Map.tryFind index
            |> Option.map (fun amplitude -> 
                let prob = amplitude.Magnitude
                prob * prob)
            |> Option.defaultValue 0.0  // Not in sparse representation → amplitude is 0
        
        | QuantumState.DensityMatrix (rho, _) ->
            // Probability = ⟨bitstring|ρ|bitstring⟩ = ρ[i,i]
            rho.[index, index].Magnitude  // Already real for density matrix diagonal
        
        | QuantumState.IsingSamples (problem, solutions) ->
            // For annealing samples, compute empirical probability from solution occurrences
            // This is NOT a quantum probability - these are classical samples
            let solutionsSeq = objToSeq solutions
            
            if Seq.isEmpty solutionsSeq then
                0.0
            else
                let spinToBit = function 
                    | -1 -> 0 
                    | _ -> 1
                
                let spinsToBitstring spins i =
                    spins 
                    |> Map.tryFind i 
                    |> Option.map spinToBit 
                    |> Option.defaultValue 0
                
                let totalOcc, matchingOcc =
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
                        
                        total + occ, (if matches then matching + occ else matching)
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
                    magnitude * magnitude)
            abs (totalProb - 1.0) < 1e-10
        
        | QuantumState.FusionSuperposition superposition ->
            // Delegate to the superposition's IsNormalized property (interface call)
            superposition.IsNormalized
        
        | QuantumState.SparseState (amplitudes, _) ->
            let totalProb =
                amplitudes
                |> Map.toSeq
                |> Seq.sumBy (fun (_, amp) ->
                    let magnitude = amp.Magnitude
                    magnitude * magnitude)
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
        
        | QuantumState.FusionSuperposition _ ->
            // We can't access fields from obj, so provide minimal info
            $"FusionSuperposition ({n} qubits, topological state)"
        
        | QuantumState.SparseState (amplitudes, _) ->
            let numNonZero = Map.count amplitudes
            $"SparseState ({n} qubits, {numNonZero}/{dim} non-zero amplitudes)"
        
        | QuantumState.DensityMatrix _ ->
            $"DensityMatrix ({n} qubits, {dim}×{dim} matrix, {dim * dim * 16}B memory)"
        
        | QuantumState.IsingSamples (_, solutions) ->
            // Show D-Wave annealing results summary
            let solutionsSeq = objToSeq solutions
            
            if Seq.isEmpty solutionsSeq then
                $"IsingSamples ({n} variables, no solutions)"
            else
                let solutionsList = Seq.toList solutionsSeq
                let numSolutions = List.length solutionsList
                
                let totalSamples, bestEnergy =
                    solutionsList
                    |> List.fold (fun (total, best) sol ->
                        let solType = sol.GetType()
                        let occ = solType.GetProperty("NumOccurrences").GetValue(sol) :?> int
                        let energy = solType.GetProperty("Energy").GetValue(sol) :?> float
                        total + occ, min best energy
                    ) (0, Double.MaxValue)
                
                $"IsingSamples ({n} variables, {numSolutions} unique solutions, {totalSamples} samples, best energy: {bestEnergy:F4})"
