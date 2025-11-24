namespace FSharp.Azure.Quantum.LocalSimulator

open System
open System.Numerics

/// Quantum Gates Module for Local Simulation
/// 
/// Implements single-qubit quantum gates for state vector manipulation.
/// All gates are unitary transformations that preserve state vector norm.
/// 
/// Gate matrices are applied via direct state vector transformation
/// rather than explicit matrix multiplication for efficiency.
module Gates =
    
    // ============================================================================
    // 1. HELPER FUNCTIONS (Primitives for gate application)
    // ============================================================================
    
    /// Apply a 2x2 unitary matrix to a specific qubit in the state vector
    /// 
    /// For a single qubit gate matrix [[a, b], [c, d]] applied to qubit q:
    /// - For each basis state |...i_q...⟩, split into |...0...⟩ and |...1...⟩ components
    /// - Transform: α|...0...⟩ + β|...1...⟩ → (aα+bβ)|...0...⟩ + (cα+dβ)|...1...⟩
    let private applySingleQubitGate 
        (qubitIndex: int) 
        (matrix: Complex * Complex * Complex * Complex)  // (a, b, c, d) for [[a,b],[c,d]]
        (state: StateVector.StateVector) : StateVector.StateVector =
        
        let numQubits = StateVector.numQubits state
        if qubitIndex < 0 || qubitIndex >= numQubits then
            failwith $"Qubit index {qubitIndex} out of range for {numQubits}-qubit state"
        
        let dimension = StateVector.dimension state
        let (a, b, c, d) = matrix
        
        // Create new amplitude array
        let newAmplitudes = Array.zeroCreate dimension
        
        // Iterate over all basis states
        for i in 0 .. dimension - 1 do
            // Check if qubit at position qubitIndex is 0 or 1
            let bitMask = 1 <<< qubitIndex
            let qubitIs1 = (i &&& bitMask) <> 0
            
            if qubitIs1 then
                // This basis state has qubit=1, compute index with qubit=0
                let i0 = i ^^^ bitMask  // Flip the qubit bit to get |...0...⟩ index
                let amp0 = StateVector.getAmplitude i0 state
                let amp1 = StateVector.getAmplitude i state
                
                // Apply matrix: new_amp1 = c*amp0 + d*amp1
                newAmplitudes[i] <- c * amp0 + d * amp1
            else
                // This basis state has qubit=0, compute index with qubit=1
                let i1 = i ||| bitMask  // Set the qubit bit to get |...1...⟩ index
                let amp0 = StateVector.getAmplitude i state
                let amp1 = StateVector.getAmplitude i1 state
                
                // Apply matrix: new_amp0 = a*amp0 + b*amp1
                newAmplitudes[i] <- a * amp0 + b * amp1
        
        StateVector.create newAmplitudes
    
    // ============================================================================
    // 2. PAULI GATES (Depend on applySingleQubitGate)
    // ============================================================================
    
    /// Apply Pauli-X gate (bit flip) to specified qubit
    /// 
    /// X = [[0, 1],
    ///      [1, 0]]
    /// 
    /// Effect: |0⟩ → |1⟩, |1⟩ → |0⟩
    let applyX (qubitIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        let matrix = (Complex.Zero, Complex.One, Complex.One, Complex.Zero)
        applySingleQubitGate qubitIndex matrix state
    
    /// Apply Pauli-Y gate to specified qubit
    /// 
    /// Y = [[0, -i],
    ///      [i,  0]]
    /// 
    /// Effect: |0⟩ → i|1⟩, |1⟩ → -i|0⟩
    let applyY (qubitIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        let negI = Complex(0.0, -1.0)
        let posI = Complex(0.0, 1.0)
        let matrix = (Complex.Zero, negI, posI, Complex.Zero)
        applySingleQubitGate qubitIndex matrix state
    
    /// Apply Pauli-Z gate (phase flip) to specified qubit
    /// 
    /// Z = [[1,  0],
    ///      [0, -1]]
    /// 
    /// Effect: |0⟩ → |0⟩, |1⟩ → -|1⟩
    let applyZ (qubitIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        let matrix = (Complex.One, Complex.Zero, Complex.Zero, -Complex.One)
        applySingleQubitGate qubitIndex matrix state
    
    // ============================================================================
    // 3. HADAMARD GATE (Depends on applySingleQubitGate)
    // ============================================================================
    
    /// Apply Hadamard gate to specified qubit
    /// 
    /// H = (1/√2) * [[1,  1],
    ///               [1, -1]]
    /// 
    /// Effect: Creates equal superposition
    /// |0⟩ → (|0⟩+|1⟩)/√2
    /// |1⟩ → (|0⟩-|1⟩)/√2
    let applyH (qubitIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        let sqrtHalf = 1.0 / sqrt 2.0
        let h = Complex(sqrtHalf, 0.0)
        let matrix = (h, h, h, -h)
        applySingleQubitGate qubitIndex matrix state
    
    // ============================================================================
    // 4. ROTATION GATES (Depend on applySingleQubitGate)
    // ============================================================================
    
    /// Apply Rx rotation gate around X axis
    /// 
    /// Rx(θ) = [[cos(θ/2),    -i*sin(θ/2)],
    ///          [-i*sin(θ/2),  cos(θ/2)]]
    /// 
    /// Rotates qubit state around X axis by angle θ
    let applyRx (qubitIndex: int) (theta: float) (state: StateVector.StateVector) : StateVector.StateVector =
        let halfTheta = theta / 2.0
        let cosHalf = cos halfTheta
        let sinHalf = sin halfTheta
        
        let a = Complex(cosHalf, 0.0)
        let b = Complex(0.0, -sinHalf)
        let c = Complex(0.0, -sinHalf)
        let d = Complex(cosHalf, 0.0)
        
        let matrix = (a, b, c, d)
        applySingleQubitGate qubitIndex matrix state
    
    /// Apply Ry rotation gate around Y axis
    /// 
    /// Ry(θ) = [[cos(θ/2),  -sin(θ/2)],
    ///          [sin(θ/2),   cos(θ/2)]]
    /// 
    /// Rotates qubit state around Y axis by angle θ
    let applyRy (qubitIndex: int) (theta: float) (state: StateVector.StateVector) : StateVector.StateVector =
        let halfTheta = theta / 2.0
        let cosHalf = cos halfTheta
        let sinHalf = sin halfTheta
        
        let a = Complex(cosHalf, 0.0)
        let b = Complex(-sinHalf, 0.0)
        let c = Complex(sinHalf, 0.0)
        let d = Complex(cosHalf, 0.0)
        
        let matrix = (a, b, c, d)
        applySingleQubitGate qubitIndex matrix state
    
    /// Apply Rz rotation gate around Z axis
    /// 
    /// Rz(θ) = [[e^(-iθ/2),  0],
    ///          [0,          e^(iθ/2)]]
    /// 
    /// Rotates qubit state around Z axis by angle θ
    /// Adds relative phase between |0⟩ and |1⟩ components
    let applyRz (qubitIndex: int) (theta: float) (state: StateVector.StateVector) : StateVector.StateVector =
        let halfTheta = theta / 2.0
        
        // e^(-iθ/2) = cos(θ/2) - i*sin(θ/2)
        let a = Complex(cos halfTheta, -sin halfTheta)
        let b = Complex.Zero
        let c = Complex.Zero
        // e^(iθ/2) = cos(θ/2) + i*sin(θ/2)
        let d = Complex(cos halfTheta, sin halfTheta)
        
        let matrix = (a, b, c, d)
        applySingleQubitGate qubitIndex matrix state
