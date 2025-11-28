namespace FSharp.Azure.Quantum.LocalSimulator

open System
open System.Numerics

/// Quantum Measurement Module for Local Simulation
/// 
/// Implements quantum measurement operations including:
/// - Single-qubit measurements in computational basis
/// - State collapse after measurement
/// - Sampling from quantum probability distributions
/// - Probability extraction for measurement outcomes
module Measurement =
    
    // ============================================================================
    // 1. PROBABILITY COMPUTATION (Depends on StateVector)
    // ============================================================================
    
    /// Get probability of measuring a specific basis state
    /// 
    /// For basis state |i⟩, probability P(i) = |αᵢ|²
    let getBasisStateProbability (basisIndex: int) (state: StateVector.StateVector) : float =
        let dimension = StateVector.dimension state
        if basisIndex < 0 || basisIndex >= dimension then
            failwith $"Basis index {basisIndex} out of range for {dimension}-dimensional state"
        
        let amplitude = StateVector.getAmplitude basisIndex state
        amplitude.Magnitude * amplitude.Magnitude
    
    /// Get full probability distribution over all basis states
    /// 
    /// Returns array where element i is probability of measuring basis state |i⟩
    let getProbabilityDistribution (state: StateVector.StateVector) : float[] =
        let dimension = StateVector.dimension state
        [| 0 .. dimension - 1 |]
        |> Array.map (fun i -> getBasisStateProbability i state)
    
    /// Get probability of measuring specific qubit in |0⟩ or |1⟩
    /// 
    /// For qubit at index qubitIndex:
    /// - P(0) = sum of |amplitude|² for all basis states where qubit is 0
    /// - P(1) = sum of |amplitude|² for all basis states where qubit is 1
    /// 
    /// Returns: (probability of |0⟩, probability of |1⟩)
    let getQubitProbabilities (qubitIndex: int) (state: StateVector.StateVector) : float * float =
        let numQubits = StateVector.numQubits state
        if qubitIndex < 0 || qubitIndex >= numQubits then
            failwith $"Qubit index {qubitIndex} out of range for {numQubits}-qubit state"
        
        let dimension = StateVector.dimension state
        let bitMask = 1 <<< qubitIndex
        
        let (prob0, prob1) =
            [0 .. dimension - 1]
            |> List.fold (fun (p0, p1) basisIndex ->
                let probability = getBasisStateProbability basisIndex state
                let qubitIs1 = (basisIndex &&& bitMask) <> 0
                if qubitIs1 then (p0, p1 + probability)
                else (p0 + probability, p1)
            ) (0.0, 0.0)
        
        (prob0, prob1)
    
    // ============================================================================
    // 2. MEASUREMENT SIMULATION (Depends on probabilities)
    // ============================================================================
    
    /// Simulate measurement of all qubits in computational basis
    /// 
    /// Returns the measured basis state index according to Born rule:
    /// probability of outcome i is |αᵢ|²
    /// 
    /// Uses provided random number generator for reproducibility
    let measureComputationalBasis (rng: Random) (state: StateVector.StateVector) : int =
        let probabilities = getProbabilityDistribution state
        let randomValue = rng.NextDouble()
        
        // Find outcome by cumulative probability
        let rec findOutcome (cumulativeProb: float) (index: int) : int =
            if index >= probabilities.Length - 1 then
                probabilities.Length - 1  // Last outcome by default
            else
                let newCumulative = cumulativeProb + probabilities[index]
                if randomValue < newCumulative then index
                else findOutcome newCumulative (index + 1)
        
        findOutcome 0.0 0
    
    /// Simulate measurement of single qubit in computational basis
    /// 
    /// Returns 0 or 1 based on qubit measurement outcome
    let measureSingleQubit (rng: Random) (qubitIndex: int) (state: StateVector.StateVector) : int =
        let (prob0, _prob1) = getQubitProbabilities qubitIndex state
        let randomValue = rng.NextDouble()
        
        if randomValue < prob0 then 0 else 1
    
    /// Perform state collapse after measuring a qubit
    /// 
    /// After measuring qubit at qubitIndex with outcome (0 or 1):
    /// 1. Zero out amplitudes inconsistent with measurement
    /// 2. Renormalize remaining amplitudes
    /// 
    /// Returns collapsed state
    let collapseAfterMeasurement (qubitIndex: int) (outcome: int) (state: StateVector.StateVector) : StateVector.StateVector =
        let numQubits = StateVector.numQubits state
        if qubitIndex < 0 || qubitIndex >= numQubits then
            failwith $"Qubit index {qubitIndex} out of range for {numQubits}-qubit state"
        if outcome <> 0 && outcome <> 1 then
            failwith $"Measurement outcome must be 0 or 1, got {outcome}"
        
        let dimension = StateVector.dimension state
        let bitMask = 1 <<< qubitIndex
        
        // Create new amplitudes with inconsistent states zeroed
        let newAmplitudes =
            [| 0 .. dimension - 1 |]
            |> Array.map (fun basisIndex ->
                let qubitValue = if (basisIndex &&& bitMask) <> 0 then 1 else 0
                if qubitValue = outcome then
                    StateVector.getAmplitude basisIndex state
                else
                    Complex.Zero
            )
        
        // Renormalize
        let collapsedState = StateVector.create newAmplitudes
        StateVector.normalize collapsedState
    
    // ============================================================================
    // 3. SAMPLING (Depends on measurement)
    // ============================================================================
    
    /// Sample multiple measurements from quantum state
    /// 
    /// Returns array of measurement outcomes (basis state indices)
    /// Each sample is independent (non-destructive measurement)
    let sampleMeasurements (rng: Random) (numSamples: int) (state: StateVector.StateVector) : int[] =
        if numSamples < 1 then
            failwith $"Number of samples must be positive, got {numSamples}"
        
        [| 1 .. numSamples |]
        |> Array.map (fun _ -> measureComputationalBasis rng state)
    
    /// Sample measurements and return frequency counts
    /// 
    /// Returns dictionary mapping basis state index to count
    let sampleAndCount (rng: Random) (numSamples: int) (state: StateVector.StateVector) : Map<int, int> =
        let samples = sampleMeasurements rng numSamples state
        
        samples
        |> Array.groupBy id
        |> Array.map (fun (outcome, occurrences) -> (outcome, occurrences.Length))
        |> Map.ofArray
    
    /// Get most likely measurement outcome
    /// 
    /// Returns basis state with highest probability
    let getMostLikelyOutcome (state: StateVector.StateVector) : int =
        let probabilities = getProbabilityDistribution state
        
        probabilities
        |> Array.indexed
        |> Array.maxBy snd
        |> fst
    
    /// Get top N most likely measurement outcomes
    /// 
    /// Returns array of (basis state index, probability) sorted by probability descending
    let getTopOutcomes (n: int) (state: StateVector.StateVector) : (int * float)[] =
        let probabilities = getProbabilityDistribution state
        
        probabilities
        |> Array.indexed
        |> Array.sortByDescending snd
        |> Array.take (min n probabilities.Length)
    
    // ============================================================================
    // 4. MEASUREMENT STATISTICS (Depends on sampling)
    // ============================================================================
    
    /// Compute expected value of a classical function over measurement outcomes
    /// 
    /// E[f] = Σᵢ P(i) * f(i)
    /// where f(i) is the value of classical function for basis state i
    let computeExpectedValue (classicalFunction: int -> float) (state: StateVector.StateVector) : float =
        let dimension = StateVector.dimension state
        
        [0 .. dimension - 1]
        |> List.sumBy (fun basisIndex ->
            let probability = getBasisStateProbability basisIndex state
            let value = classicalFunction basisIndex
            probability * value
        )
    
    /// Compute standard deviation of classical function over measurement outcomes
    /// 
    /// σ = sqrt(E[f²] - E[f]²)
    let computeStandardDeviation (classicalFunction: int -> float) (state: StateVector.StateVector) : float =
        let expectedValue = computeExpectedValue classicalFunction state
        let expectedSquared = computeExpectedValue (fun i -> (classicalFunction i) ** 2.0) state
        let variance = expectedSquared - expectedValue ** 2.0
        sqrt variance
