namespace FSharp.Azure.Quantum.LocalSimulator

open System
open System.Numerics

/// QAOA (Quantum Approximate Optimization Algorithm) Simulator Module
/// 
/// Implements local simulation of QAOA for combinatorial optimization problems.
/// QAOA alternates between problem-specific cost Hamiltonian and mixing Hamiltonian layers.
/// 
/// Typical usage:
/// 1. Initialize state to uniform superposition
/// 2. Apply alternating cost and mixer layers
/// 3. Measure to get approximate solution
module QaoaSimulator =
    
    // ============================================================================
    // 1. INITIALIZATION (Depends on StateVector and Gates)
    // ============================================================================
    
    /// Initialize QAOA state to uniform superposition over all basis states
    /// 
    /// For n qubits, creates state |+⟩^⊗n = (1/√(2^n)) Σ|i⟩
    /// This is achieved by applying Hadamard gates to all qubits starting from |0⟩^⊗n
    let initializeUniformSuperposition (numQubits: int) : StateVector.StateVector =
        if numQubits < 1 || numQubits > 10 then
            failwith $"Number of qubits must be between 1 and 10, got {numQubits}"
        
        // Apply Hadamard to each qubit to create uniform superposition
        [0 .. numQubits - 1]
        |> List.fold (fun state qubitIndex -> Gates.applyH qubitIndex state) (StateVector.init numQubits)
    
    // ============================================================================
    // 2. COST HAMILTONIAN LAYER (Depends on Gates)
    // ============================================================================
    
    /// Apply cost Hamiltonian layer for QAOA
    /// 
    /// For a problem encoded in the diagonal cost Hamiltonian C = Σ cᵢZᵢ + Σ cᵢⱼZᵢZⱼ,
    /// this applies e^(-iγC) ≈ Π Rz(2γcᵢ) Π Rzz(2γcᵢⱼ)
    /// 
    /// Simplified version: Applies Rz rotations based on provided cost coefficients
    /// 
    /// Parameters:
    /// - gamma: QAOA angle parameter for cost layer
    /// - costCoefficients: Array of cost coefficients for each qubit (cᵢ values)
    /// - state: Current quantum state
    let applyCostLayer 
        (gamma: float) 
        (costCoefficients: float[]) 
        (state: StateVector.StateVector) : StateVector.StateVector =
        
        let numQubits = StateVector.numQubits state
        if costCoefficients.Length <> numQubits then
            failwith $"Cost coefficients array length ({costCoefficients.Length}) must match number of qubits ({numQubits})"
        
        // Apply Rz(2*gamma*cᵢ) to each qubit based on its cost coefficient
        costCoefficients
        |> Array.indexed
        |> Array.fold (fun currentState (i, coeff) ->
            let angle = 2.0 * gamma * coeff
            Gates.applyRz i angle currentState
        ) state
    
    /// Apply two-qubit interaction cost term using CZ gates
    /// 
    /// For interaction terms cᵢⱼZᵢZⱼ in the cost Hamiltonian,
    /// applies controlled-Z gates with angle 2*gamma*cᵢⱼ
    /// 
    /// Note: This is a simplified implementation using CZ.
    /// Full Rzz(θ) = exp(-iθ/2 ZZ) would require: Rzz(θ) = CNOT(i,j) · Rz(θ) on j · CNOT(i,j)
    let applyCostInteraction
        (gamma: float)
        (qubit1: int)
        (qubit2: int)
        (coefficient: float)
        (state: StateVector.StateVector) : StateVector.StateVector =
        
        let numQubits = StateVector.numQubits state
        if qubit1 < 0 || qubit1 >= numQubits || qubit2 < 0 || qubit2 >= numQubits then
            failwith $"Qubit indices ({qubit1}, {qubit2}) out of range for {numQubits}-qubit state"
        if qubit1 = qubit2 then
            failwith "Qubit indices must be different for interaction term"
        
        // Apply interaction via CNOT-Rz-CNOT decomposition
        // Rzz(θ) = CNOT(q1,q2) · Rz(θ) on q2 · CNOT(q1,q2)
        let angle = 2.0 * gamma * coefficient
        state
        |> Gates.applyCNOT qubit1 qubit2
        |> Gates.applyRz qubit2 angle
        |> Gates.applyCNOT qubit1 qubit2
    
    // ============================================================================
    // 3. MIXER HAMILTONIAN LAYER (Depends on Gates)
    // ============================================================================
    
    /// Apply mixer Hamiltonian layer for QAOA
    /// 
    /// Standard mixer: B = Σ Xᵢ
    /// Applies e^(-iβB) ≈ Π Rx(2β)
    /// 
    /// This drives transitions between computational basis states,
    /// exploring the solution space.
    /// 
    /// Parameters:
    /// - beta: QAOA angle parameter for mixer layer
    /// - state: Current quantum state
    let applyMixerLayer (beta: float) (state: StateVector.StateVector) : StateVector.StateVector =
        let numQubits = StateVector.numQubits state
        let angle = 2.0 * beta
        
        // Apply Rx(2*beta) to each qubit
        [0 .. numQubits - 1]
        |> List.fold (fun currentState i -> Gates.applyRx i angle currentState) state
    
    // ============================================================================
    // 4. FULL QAOA CIRCUIT (Depends on cost and mixer layers)
    // ============================================================================
    
    /// Apply a complete QAOA layer (cost + mixer)
    /// 
    /// One QAOA layer consists of:
    /// 1. Cost Hamiltonian evolution: e^(-iγC)
    /// 2. Mixer Hamiltonian evolution: e^(-iβB)
    let applyQaoaLayer
        (gamma: float)
        (beta: float)
        (costCoefficients: float[])
        (state: StateVector.StateVector) : StateVector.StateVector =
        
        state
        |> applyCostLayer gamma costCoefficients
        |> applyMixerLayer beta
    
    /// Run full QAOA circuit with p layers
    /// 
    /// Parameters:
    /// - numQubits: Number of qubits in the problem
    /// - gammas: Array of gamma angles for each layer (length p)
    /// - betas: Array of beta angles for each layer (length p)
    /// - costCoefficients: Cost coefficients for each qubit
    /// 
    /// Returns: Final quantum state after p QAOA layers
    let runQaoaCircuit
        (numQubits: int)
        (gammas: float[])
        (betas: float[])
        (costCoefficients: float[]) : StateVector.StateVector =
        
        if gammas.Length <> betas.Length then
            failwith $"Gammas and betas must have same length (got {gammas.Length} vs {betas.Length})"
        
        // Apply p layers of QAOA
        let initialState = initializeUniformSuperposition numQubits
        Array.zip gammas betas
        |> Array.fold (fun state (gamma, beta) ->
            applyQaoaLayer gamma beta costCoefficients state
        ) initialState
    
    // ============================================================================
    // 5. EXPECTATION VALUE COMPUTATION (Depends on StateVector)
    // ============================================================================
    
    /// Compute expectation value of diagonal cost Hamiltonian C = Σ cᵢZᵢ
    /// 
    /// For diagonal Hamiltonian in computational basis:
    /// ⟨C⟩ = Σᵢ cᵢ⟨Zᵢ⟩ = Σᵢ cᵢ(P(0) - P(1))
    /// 
    /// where P(b) is probability of measuring qubit i in state b
    let computeCostExpectation
        (costCoefficients: float[])
        (state: StateVector.StateVector) : float =
        
        let numQubits = StateVector.numQubits state
        if costCoefficients.Length <> numQubits then
            failwith "Cost coefficients array length must match number of qubits"
        
        let dimension = StateVector.dimension state
        
        // Compute cost for a single basis state
        let computeBasisCost (basisIndex: int) : float =
            [0 .. numQubits - 1]
            |> List.sumBy (fun qubitIndex ->
                let bitMask = 1 <<< qubitIndex
                // Z eigenvalue: |0⟩ → +1, |1⟩ → -1
                let qubitValue = if (basisIndex &&& bitMask) <> 0 then -1.0 else 1.0
                costCoefficients[qubitIndex] * qubitValue
            )
        
        // Sum over all basis states
        [0 .. dimension - 1]
        |> List.sumBy (fun basisIndex ->
            let amplitude = StateVector.getAmplitude basisIndex state
            let probability = amplitude.Magnitude * amplitude.Magnitude
            probability * computeBasisCost basisIndex
        )
