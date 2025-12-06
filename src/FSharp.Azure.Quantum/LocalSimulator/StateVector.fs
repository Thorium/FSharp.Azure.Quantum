namespace FSharp.Azure.Quantum.LocalSimulator

open System
open System.Numerics

/// State Vector Module for Local Quantum Simulation
/// 
/// Implements quantum state vector representation using complex number arrays.
/// State vector represents quantum state |ψ⟩ = Σ αᵢ|i⟩ where αᵢ are complex amplitudes.
/// 
/// For n qubits, state vector has 2^n dimensions.
/// Initial state |0⟩^⊗n has amplitude 1.0 at index 0, all others are 0.
module StateVector =
    
    // ============================================================================
    // 1. TYPES (Primitives, no dependencies)
    // ============================================================================
    
    /// Quantum state vector - array of complex amplitudes
    /// Dimension = 2^n for n qubits
    type StateVector = private {
        Amplitudes: Complex[]
        NumQubits: int
    }
    
    // ============================================================================
    // 2. CONSTRUCTION (depend on types)
    // ============================================================================
    
    /// Initialize state vector to |0⟩^⊗n (all qubits in |0⟩ state)
    /// 
    /// For n qubits, creates 2^n dimensional state vector with:
    /// - amplitude[0] = 1.0 + 0.0i (|00...0⟩ state)
    /// - amplitude[i] = 0.0 + 0.0i for all i > 0
    let init (numQubits: int) : StateVector =
        if numQubits < 0 || numQubits > 20 then
            failwith $"Number of qubits must be between 0 and 20, got {numQubits}"
        
        let dimension = 1 <<< numQubits  // 2^numQubits using bit shift
        let amplitudes = Array.create dimension Complex.Zero
        amplitudes[0] <- Complex(1.0, 0.0)  // |0⟩^⊗n state
        
        {
            Amplitudes = amplitudes
            NumQubits = numQubits
        }
    
    // ============================================================================
    // 3. ACCESSORS (depend on types)
    // ============================================================================
    
    /// Get dimension of state vector (2^n for n qubits)
    let dimension (state: StateVector) : int =
        state.Amplitudes.Length
    
    /// Get amplitude at specific basis state index
    let getAmplitude (index: int) (state: StateVector) : Complex =
        if index < 0 || index >= state.Amplitudes.Length then
            failwith $"Index {index} out of range for state vector of dimension {state.Amplitudes.Length}"
        
        state.Amplitudes[index]
    
    /// Get number of qubits
    let numQubits (state: StateVector) : int =
        state.NumQubits
    
    /// Create state vector from custom amplitudes
    let create (amplitudes: Complex[]) : StateVector =
        let n = amplitudes.Length
        if n = 0 || (n &&& (n - 1)) <> 0 then
            failwith $"Amplitude array length must be a power of 2, got {n}"
        
        let numQubits = int (Math.Log(float n, 2.0))
        
        if numQubits > 20 then
            failwith $"State vector supports maximum 20 qubits (1048576 dimensions), got {numQubits} qubits ({n} dimensions)"
        
        {
            Amplitudes = Array.copy amplitudes
            NumQubits = numQubits
        }
    
    // ============================================================================
    // 4. NORMALIZATION AND NORM (depend on accessors)
    // ============================================================================
    
    /// Calculate norm of state vector: ||ψ|| = sqrt(Σ |αᵢ|²)
    let norm (state: StateVector) : float =
        state.Amplitudes
        |> Array.sumBy (fun amp -> amp.Magnitude * amp.Magnitude)
        |> sqrt
    
    /// Normalize state vector to unit norm
    let normalize (state: StateVector) : StateVector =
        let normValue = norm state
        if normValue < 1e-10 then
            failwith "Cannot normalize zero state vector"
        
        let normalizedAmps = 
            state.Amplitudes
            |> Array.map (fun amp -> amp / normValue)
        
        {
            Amplitudes = normalizedAmps
            NumQubits = state.NumQubits
        }
    
    // ============================================================================
    // 5. INNER PRODUCT AND PROBABILITIES (depend on norm)
    // ============================================================================
    
    /// Calculate inner product <ψ|φ> = Σ ψᵢ* φᵢ
    let innerProduct (bra: StateVector) (ket: StateVector) : Complex =
        if bra.Amplitudes.Length <> ket.Amplitudes.Length then
            failwith "State vectors must have same dimension for inner product"
        
        Array.zip bra.Amplitudes ket.Amplitudes
        |> Array.fold (fun sum (braAmp, ketAmp) -> 
            sum + (Complex.Conjugate braAmp) * ketAmp) Complex.Zero
    
    /// Calculate probability of measuring basis state |i⟩
    /// P(i) = |αᵢ|² where αᵢ is amplitude at index i
    let probability (index: int) (state: StateVector) : float =
        if index < 0 || index >= state.Amplitudes.Length then
            failwith $"Index {index} out of range for state vector of dimension {state.Amplitudes.Length}"
        
        let amp = state.Amplitudes[index]
        amp.Magnitude * amp.Magnitude
    
    // ============================================================================
    // 6. EQUALITY AND COMPARISON (depend on all above)
    // ============================================================================
    
    /// Check if two state vectors are equal (within tolerance)
    let equals (state1: StateVector) (state2: StateVector) : bool =
        if state1.NumQubits <> state2.NumQubits then
            false
        else
            Array.zip state1.Amplitudes state2.Amplitudes
            |> Array.forall (fun (amp1, amp2) ->
                abs(amp1.Real - amp2.Real) < 1e-10 &&
                abs(amp1.Imaginary - amp2.Imaginary) < 1e-10
            )
    
    // ============================================================================
    // 7. TENSOR PRODUCT (depends on create)
    // ============================================================================
    
    /// Compute tensor product |ψ⟩ ⊗ |φ⟩
    /// 
    /// For state1 with dimension n and state2 with dimension m,
    /// result has dimension n*m with amplitudes:
    /// result[i*m + j] = state1[i] * state2[j]
    let tensorProduct (state1: StateVector) (state2: StateVector) : StateVector =
        let dim1 = state1.Amplitudes.Length
        let dim2 = state2.Amplitudes.Length
        let resultDim = dim1 * dim2
        
        let resultAmps =
            Array.init resultDim (fun idx ->
                let i = idx / dim2
                let j = idx % dim2
                state1.Amplitudes[i] * state2.Amplitudes[j])
        
        create resultAmps
