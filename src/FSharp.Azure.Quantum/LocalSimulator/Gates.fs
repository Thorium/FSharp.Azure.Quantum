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
    
    // ============================================================================
    // 5. PHASE GATES (Depend on applySingleQubitGate)
    // ============================================================================
    
    /// Apply S gate (√Z, phase gate) to specified qubit
    /// 
    /// S = [[1,  0],
    ///      [0,  i]]
    /// 
    /// Effect: |0⟩ → |0⟩, |1⟩ → i|1⟩
    /// Adds π/2 phase to |1⟩ state
    let applyS (qubitIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        let posI = Complex(0.0, 1.0)
        let matrix = (Complex.One, Complex.Zero, Complex.Zero, posI)
        applySingleQubitGate qubitIndex matrix state
    
    /// Apply S-dagger (S†, inverse phase gate) to specified qubit
    /// 
    /// SDG = [[1,  0],
    ///        [0, -i]]
    /// 
    /// Effect: |0⟩ → |0⟩, |1⟩ → -i|1⟩
    /// Adds -π/2 phase to |1⟩ state (inverse of S)
    let applySDG (qubitIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        let negI = Complex(0.0, -1.0)
        let matrix = (Complex.One, Complex.Zero, Complex.Zero, negI)
        applySingleQubitGate qubitIndex matrix state
    
    /// Apply T gate (√S, π/8 gate) to specified qubit
    /// 
    /// T = [[1,  0],
    ///      [0,  e^(iπ/4)]]
    /// 
    /// Effect: |0⟩ → |0⟩, |1⟩ → e^(iπ/4)|1⟩
    /// Adds π/4 phase to |1⟩ state
    let applyT (qubitIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        let piOver4 = Math.PI / 4.0
        let phase = Complex(cos piOver4, sin piOver4)  // e^(iπ/4)
        let matrix = (Complex.One, Complex.Zero, Complex.Zero, phase)
        applySingleQubitGate qubitIndex matrix state
    
    /// Apply T-dagger (T†, inverse π/8 gate) to specified qubit
    /// 
    /// TDG = [[1,  0],
    ///        [0,  e^(-iπ/4)]]
    /// 
    /// Effect: |0⟩ → |0⟩, |1⟩ → e^(-iπ/4)|1⟩
    /// Adds -π/4 phase to |1⟩ state (inverse of T)
    let applyTDG (qubitIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        let piOver4 = Math.PI / 4.0
        let phase = Complex(cos piOver4, -sin piOver4)  // e^(-iπ/4)
        let matrix = (Complex.One, Complex.Zero, Complex.Zero, phase)
        applySingleQubitGate qubitIndex matrix state
    
    // ============================================================================
    // 6. TWO-QUBIT GATES (Depend on StateVector operations)
    // ============================================================================
    
    /// Apply CNOT (Controlled-NOT) gate to specified control and target qubits
    /// 
    /// CNOT applies X to target qubit when control qubit is |1⟩
    /// 
    /// Truth table:
    /// |00⟩ → |00⟩  (control=0, no operation)
    /// |01⟩ → |01⟩  (control=0, no operation)
    /// |10⟩ → |11⟩  (control=1, flip target)
    /// |11⟩ → |10⟩  (control=1, flip target)
    /// 
    /// Implementation: For each basis state, if control qubit is 1, flip target qubit
    let applyCNOT (controlIndex: int) (targetIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        let numQubits = StateVector.numQubits state
        if controlIndex < 0 || controlIndex >= numQubits then
            failwith $"Control qubit index {controlIndex} out of range for {numQubits}-qubit state"
        if targetIndex < 0 || targetIndex >= numQubits then
            failwith $"Target qubit index {targetIndex} out of range for {numQubits}-qubit state"
        if controlIndex = targetIndex then
            failwith "Control and target qubits must be different"
        
        let dimension = StateVector.dimension state
        let controlMask = 1 <<< controlIndex
        let targetMask = 1 <<< targetIndex
        
        // Create new amplitude array
        let newAmplitudes = Array.zeroCreate dimension
        
        // Process each basis state
        for i in 0 .. dimension - 1 do
            let controlIs1 = (i &&& controlMask) <> 0
            
            if controlIs1 then
                // Control is 1: swap amplitudes between |...0...⟩ and |...1...⟩ at target position
                let targetIs1 = (i &&& targetMask) <> 0
                
                if targetIs1 then
                    // This is |...1...⟩ at target, get amplitude from |...0...⟩
                    let flippedIndex = i ^^^ targetMask
                    newAmplitudes[i] <- StateVector.getAmplitude flippedIndex state
                else
                    // This is |...0...⟩ at target, get amplitude from |...1...⟩
                    let flippedIndex = i ^^^ targetMask
                    newAmplitudes[i] <- StateVector.getAmplitude flippedIndex state
            else
                // Control is 0: no operation, keep amplitude as is
                newAmplitudes[i] <- StateVector.getAmplitude i state
        
        StateVector.create newAmplitudes
    
    /// Apply CZ (Controlled-Z) gate to specified control and target qubits
    /// 
    /// CZ applies Z to target qubit when control qubit is |1⟩
    /// Equivalently: adds phase -1 when both qubits are |1⟩
    /// 
    /// Truth table:
    /// |00⟩ → |00⟩
    /// |01⟩ → |01⟩
    /// |10⟩ → |10⟩
    /// |11⟩ → -|11⟩  (both qubits 1, add phase -1)
    /// 
    /// Note: CZ is symmetric - control and target roles are interchangeable
    let applyCZ (controlIndex: int) (targetIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        let numQubits = StateVector.numQubits state
        if controlIndex < 0 || controlIndex >= numQubits then
            failwith $"Control qubit index {controlIndex} out of range for {numQubits}-qubit state"
        if targetIndex < 0 || targetIndex >= numQubits then
            failwith $"Target qubit index {targetIndex} out of range for {numQubits}-qubit state"
        if controlIndex = targetIndex then
            failwith "Control and target qubits must be different"
        
        let dimension = StateVector.dimension state
        let controlMask = 1 <<< controlIndex
        let targetMask = 1 <<< targetIndex
        
        // Create new amplitude array
        let newAmplitudes = Array.zeroCreate dimension
        
        // Process each basis state
        for i in 0 .. dimension - 1 do
            let controlIs1 = (i &&& controlMask) <> 0
            let targetIs1 = (i &&& targetMask) <> 0
            
            if controlIs1 && targetIs1 then
                // Both qubits are 1: add phase -1
                newAmplitudes[i] <- -StateVector.getAmplitude i state
            else
                // Otherwise: no change
                newAmplitudes[i] <- StateVector.getAmplitude i state
        
        StateVector.create newAmplitudes
    
    /// Apply SWAP gate to specified qubits
    /// 
    /// SWAP exchanges the quantum states of two qubits
    /// 
    /// Truth table:
    /// |00⟩ → |00⟩
    /// |01⟩ → |10⟩  (swap)
    /// |10⟩ → |01⟩  (swap)
    /// |11⟩ → |11⟩
    /// 
    /// Implementation: For each basis state, swap the two qubit values
    let applySWAP (qubit1Index: int) (qubit2Index: int) (state: StateVector.StateVector) : StateVector.StateVector =
        let numQubits = StateVector.numQubits state
        if qubit1Index < 0 || qubit1Index >= numQubits then
            failwith $"Qubit1 index {qubit1Index} out of range for {numQubits}-qubit state"
        if qubit2Index < 0 || qubit2Index >= numQubits then
            failwith $"Qubit2 index {qubit2Index} out of range for {numQubits}-qubit state"
        if qubit1Index = qubit2Index then
            failwith "SWAP qubits must be different"
        
        let dimension = StateVector.dimension state
        let mask1 = 1 <<< qubit1Index
        let mask2 = 1 <<< qubit2Index
        
        // Create new amplitude array
        let newAmplitudes = Array.zeroCreate dimension
        
        // Process each basis state
        for i in 0 .. dimension - 1 do
            let qubit1Is1 = (i &&& mask1) <> 0
            let qubit2Is1 = (i &&& mask2) <> 0
            
            if qubit1Is1 <> qubit2Is1 then
                // Qubits are different: swap them
                // Flip both bits to get the swapped index
                let swappedIndex = i ^^^ mask1 ^^^ mask2
                newAmplitudes[i] <- StateVector.getAmplitude swappedIndex state
            else
                // Qubits are the same (both 0 or both 1): no change
                newAmplitudes[i] <- StateVector.getAmplitude i state
        
        StateVector.create newAmplitudes
    
    // ============================================================================
    // 7. THREE-QUBIT GATES (Depend on StateVector operations)
    // ============================================================================
    
    /// Apply CCX (Toffoli, CCNOT) gate to specified control and target qubits
    /// 
    /// CCX applies X to target qubit when both control qubits are |1⟩
    /// 
    /// Truth table:
    /// |110⟩ → |111⟩  (both controls 1, flip target)
    /// |111⟩ → |110⟩  (both controls 1, flip target)
    /// All other states: no change
    /// 
    /// Implementation: For each basis state, if both controls are 1, flip target
    let applyCCX (control1Index: int) (control2Index: int) (targetIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        let numQubits = StateVector.numQubits state
        if control1Index < 0 || control1Index >= numQubits then
            failwith $"Control1 qubit index {control1Index} out of range for {numQubits}-qubit state"
        if control2Index < 0 || control2Index >= numQubits then
            failwith $"Control2 qubit index {control2Index} out of range for {numQubits}-qubit state"
        if targetIndex < 0 || targetIndex >= numQubits then
            failwith $"Target qubit index {targetIndex} out of range for {numQubits}-qubit state"
        if control1Index = control2Index || control1Index = targetIndex || control2Index = targetIndex then
            failwith "CCX (Toffoli) control and target qubits must be distinct"
        
        let dimension = StateVector.dimension state
        let control1Mask = 1 <<< control1Index
        let control2Mask = 1 <<< control2Index
        let targetMask = 1 <<< targetIndex
        
        // Create new amplitude array
        let newAmplitudes = Array.zeroCreate dimension
        
        // Process each basis state
        for i in 0 .. dimension - 1 do
            let control1Is1 = (i &&& control1Mask) <> 0
            let control2Is1 = (i &&& control2Mask) <> 0
            
            if control1Is1 && control2Is1 then
                // Both controls are 1: flip target (swap amplitudes)
                let targetIs1 = (i &&& targetMask) <> 0
                
                if targetIs1 then
                    // This is |...1...⟩ at target, get amplitude from |...0...⟩
                    let flippedIndex = i ^^^ targetMask
                    newAmplitudes[i] <- StateVector.getAmplitude flippedIndex state
                else
                    // This is |...0...⟩ at target, get amplitude from |...1...⟩
                    let flippedIndex = i ^^^ targetMask
                    newAmplitudes[i] <- StateVector.getAmplitude flippedIndex state
            else
                // At least one control is 0: no operation
                newAmplitudes[i] <- StateVector.getAmplitude i state
        
        StateVector.create newAmplitudes
