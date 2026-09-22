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
    // 0. GATE MATRICES (single definition, shared by every application path)
    // ============================================================================

    /// The 2×2 unitaries, as (a, b, c, d) for [[a, b], [c, d]].
    ///
    /// Defined once because there are now two ways to apply a gate — the allocating
    /// path below and `InPlace` at the bottom of this file — and a gate whose matrix
    /// was transcribed differently between them would be a silent numerical bug that
    /// only shows up on whichever path a given caller happens to take.
    module Matrices =

        let x = (Complex.Zero, Complex.One, Complex.One, Complex.Zero)

        let y = (Complex.Zero, Complex(0.0, -1.0), Complex(0.0, 1.0), Complex.Zero)

        let z = (Complex.One, Complex.Zero, Complex.Zero, -Complex.One)

        let h =
            let s = Complex(1.0 / sqrt 2.0, 0.0)
            (s, s, s, -s)

        let s = (Complex.One, Complex.Zero, Complex.Zero, Complex(0.0, 1.0))

        let sdg = (Complex.One, Complex.Zero, Complex.Zero, Complex(0.0, -1.0))

        let t =
            let piOver4 = Math.PI / 4.0
            (Complex.One, Complex.Zero, Complex.Zero, Complex(cos piOver4, sin piOver4))

        let tdg =
            let piOver4 = Math.PI / 4.0
            (Complex.One, Complex.Zero, Complex.Zero, Complex(cos piOver4, -sin piOver4))

        /// P(θ): phase on |1⟩ only — the gate QFT-based addition is built from.
        let p (theta: float) =
            (Complex.One, Complex.Zero, Complex.Zero, Complex(cos theta, sin theta))

        let rx (theta: float) =
            let half = theta / 2.0

            (Complex(cos half, 0.0), Complex(0.0, -sin half), Complex(0.0, -sin half), Complex(cos half, 0.0))

        let ry (theta: float) =
            let half = theta / 2.0

            (Complex(cos half, 0.0), Complex(-sin half, 0.0), Complex(sin half, 0.0), Complex(cos half, 0.0))

        let rz (theta: float) =
            let half = theta / 2.0
            (Complex(cos half, -sin half), Complex.Zero, Complex.Zero, Complex(cos half, sin half))

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
        (matrix: Complex * Complex * Complex * Complex) // (a, b, c, d) for [[a,b],[c,d]]
        (state: StateVector.StateVector)
        : StateVector.StateVector =

        let numQubits = StateVector.numQubits state

        if qubitIndex < 0 || qubitIndex >= numQubits then
            failwith $"Qubit index {qubitIndex} out of range for {numQubits}-qubit state"

        let dimension = StateVector.dimension state
        let (a, b, c, d) = matrix

        // Walk the index PAIRS (i0, i1) that the gate mixes, touching each amplitude
        // once, instead of walking every index and re-reading its partner. That is one
        // read pass instead of two; `src` is read directly because `getAmplitude`'s
        // bounds check and closure indirection dominate at 2^n elements.
        //
        // `src` is only ever read here — the result goes into a fresh array, so the
        // caller's state keeps its value semantics.
        let src = StateVector.amplitudesView state
        let dst = Array.zeroCreate<Complex> dimension
        let bitMask = 1 <<< qubitIndex

        for i in 0 .. dimension - 1 do
            if i &&& bitMask = 0 then
                let j = i ||| bitMask
                let amp0 = src.[i]
                let amp1 = src.[j]
                dst.[i] <- a * amp0 + b * amp1
                dst.[j] <- c * amp0 + d * amp1


        StateVector.ofAmplitudesOwned dst

    /// Apply a 2×2 unitary to `targetIndex`, but only on basis states where every bit
    /// of `controlMask` is set. Amplitudes outside that block are copied through.
    ///
    /// CNOT, CZ, CP, CRX, CRY, CRZ, CCX and multi-controlled Z are all this function
    /// with a different mask and matrix; they used to carry a copy of the loop each,
    /// which is how the same walk-and-re-read shape ended up repeated eight times.
    ///
    /// Caller must have validated the indices: `controlMask` must not overlap the
    /// target bit, or the pair walk below would address the same amplitude twice.
    let private applyControlledMatrix
        (controlMask: int)
        (targetIndex: int)
        (matrix: Complex * Complex * Complex * Complex)
        (state: StateVector.StateVector)
        : StateVector.StateVector =

        let dimension = StateVector.dimension state
        let (a, b, c, d) = matrix
        let src = StateVector.amplitudesView state
        let dst = Array.zeroCreate<Complex> dimension
        let targetMask = 1 <<< targetIndex

        for i in 0 .. dimension - 1 do
            if i &&& targetMask = 0 then
                let j = i ||| targetMask
                let amp0 = src.[i]
                let amp1 = src.[j]

                // Both members of the pair share the control bits, so one test covers
                // them. Each amplitude is read once; `src` is never written.
                if i &&& controlMask = controlMask then
                    dst.[i] <- a * amp0 + b * amp1
                    dst.[j] <- c * amp0 + d * amp1
                else
                    dst.[i] <- amp0
                    dst.[j] <- amp1


        StateVector.ofAmplitudesOwned dst

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
        applySingleQubitGate qubitIndex Matrices.x state

    /// Apply Pauli-Y gate to specified qubit
    ///
    /// Y = [[0, -i],
    ///      [i,  0]]
    ///
    /// Effect: |0⟩ → i|1⟩, |1⟩ → -i|0⟩
    let applyY (qubitIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        applySingleQubitGate qubitIndex Matrices.y state

    /// Apply Pauli-Z gate (phase flip) to specified qubit
    ///
    /// Z = [[1,  0],
    ///      [0, -1]]
    ///
    /// Effect: |0⟩ → |0⟩, |1⟩ → -|1⟩
    let applyZ (qubitIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        applySingleQubitGate qubitIndex Matrices.z state

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
        applySingleQubitGate qubitIndex Matrices.h state

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
        applySingleQubitGate qubitIndex (Matrices.rx theta) state

    /// Apply Ry rotation gate around Y axis
    ///
    /// Ry(θ) = [[cos(θ/2),  -sin(θ/2)],
    ///          [sin(θ/2),   cos(θ/2)]]
    ///
    /// Rotates qubit state around Y axis by angle θ
    let applyRy (qubitIndex: int) (theta: float) (state: StateVector.StateVector) : StateVector.StateVector =
        applySingleQubitGate qubitIndex (Matrices.ry theta) state

    /// Apply Rz rotation gate around Z axis
    ///
    /// Rz(θ) = [[e^(-iθ/2),  0],
    ///          [0,          e^(iθ/2)]]
    ///
    /// Rotates qubit state around Z axis by angle θ
    /// Adds relative phase between |0⟩ and |1⟩ components
    let applyRz (qubitIndex: int) (theta: float) (state: StateVector.StateVector) : StateVector.StateVector =
        applySingleQubitGate qubitIndex (Matrices.rz theta) state

    /// Apply U3 gate (universal single-qubit gate) to specified qubit
    ///
    /// U3(θ, φ, λ) is the most general single-qubit unitary gate in OpenQASM 2.0
    ///
    /// Matrix form:
    /// U3(θ,φ,λ) = [[cos(θ/2),          -e^(iλ) * sin(θ/2)     ],
    ///              [e^(iφ) * sin(θ/2),  e^(i(φ+λ)) * cos(θ/2) ]]
    ///
    /// Decomposition: U3(θ, φ, λ) = RZ(φ) · RY(θ) · RZ(λ)
    ///
    /// Parameters:
    /// - θ: Rotation angle (0 to π)
    /// - φ: Phase angle for RZ before RY
    /// - λ: Phase angle for RZ after RY
    ///
    /// Special cases:
    /// - U3(π/2, 0, π) = H (Hadamard)
    /// - U3(π, 0, π) = X (Pauli-X)
    /// - U3(π, π/2, π/2) = Y (Pauli-Y)
    /// - U3(0, 0, λ) = RZ(λ)
    let applyU3
        (qubitIndex: int)
        (theta: float)
        (phi: float)
        (lambda: float)
        (state: StateVector.StateVector)
        : StateVector.StateVector =
        let halfTheta = theta / 2.0
        let cosHalfTheta = cos halfTheta
        let sinHalfTheta = sin halfTheta

        // Matrix elements:
        // a = cos(θ/2)
        let a = Complex(cosHalfTheta, 0.0)

        // b = -e^(iλ) * sin(θ/2) = -sin(θ/2) * (cos(λ) + i*sin(λ))
        let b = Complex(-sinHalfTheta * cos lambda, -sinHalfTheta * sin lambda)

        // c = e^(iφ) * sin(θ/2) = sin(θ/2) * (cos(φ) + i*sin(φ))
        let c = Complex(sinHalfTheta * cos phi, sinHalfTheta * sin phi)

        // d = e^(i(φ+λ)) * cos(θ/2) = cos(θ/2) * (cos(φ+λ) + i*sin(φ+λ))
        let phiPlusLambda = phi + lambda
        let d = Complex(cosHalfTheta * cos phiPlusLambda, cosHalfTheta * sin phiPlusLambda)

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
        applySingleQubitGate qubitIndex Matrices.s state

    /// Apply S-dagger (S†, inverse phase gate) to specified qubit
    ///
    /// SDG = [[1,  0],
    ///        [0, -i]]
    ///
    /// Effect: |0⟩ → |0⟩, |1⟩ → -i|1⟩
    /// Adds -π/2 phase to |1⟩ state (inverse of S)
    let applySDG (qubitIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        applySingleQubitGate qubitIndex Matrices.sdg state

    /// Apply T gate (√S, π/8 gate) to specified qubit
    ///
    /// T = [[1,  0],
    ///      [0,  e^(iπ/4)]]
    ///
    /// Effect: |0⟩ → |0⟩, |1⟩ → e^(iπ/4)|1⟩
    /// Adds π/4 phase to |1⟩ state
    let applyT (qubitIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        applySingleQubitGate qubitIndex Matrices.t state

    /// Apply T-dagger (T†, inverse π/8 gate) to specified qubit
    ///
    /// TDG = [[1,  0],
    ///        [0,  e^(-iπ/4)]]
    ///
    /// Effect: |0⟩ → |0⟩, |1⟩ → e^(-iπ/4)|1⟩
    /// Adds -π/4 phase to |1⟩ state (inverse of T)
    let applyTDG (qubitIndex: int) (state: StateVector.StateVector) : StateVector.StateVector =
        applySingleQubitGate qubitIndex Matrices.tdg state

    /// Apply Phase gate P(θ) to specified qubit
    ///
    /// P(θ) = [[1,  0      ],
    ///         [0,  e^(iθ) ]]
    ///
    /// Effect: |0⟩ → |0⟩, |1⟩ → e^(iθ)|1⟩
    /// Adds phase θ to |1⟩ state
    ///
    /// This is the key gate for QFT-based addition (Draper algorithm).
    /// Unlike RZ(θ) which adds ±θ/2 to both states, P(θ) only affects |1⟩.
    let applyP (qubitIndex: int) (theta: float) (state: StateVector.StateVector) : StateVector.StateVector =
        applySingleQubitGate qubitIndex (Matrices.p theta) state

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
            failwith
                $"Control and target qubits must be different, calling applyCNOT with controlIndex: {controlIndex}, targetIndex: {targetIndex}, state: {state}"

        applyControlledMatrix (1 <<< controlIndex) targetIndex Matrices.x state

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
            failwith
                $"Control and target qubits must be different, calling applyCZ with controlIndex: {controlIndex}, targetIndex: {targetIndex}, state: {state}"

        applyControlledMatrix (1 <<< controlIndex) targetIndex Matrices.z state

    /// Apply controlled phase gate (CPhase) to specified control and target qubits
    ///
    /// CPhase applies phase rotation e^(iθ) to target qubit when control qubit is |1⟩
    /// Equivalently: multiplies amplitude by e^(iθ) when both qubits are |1⟩
    ///
    /// Truth table:
    /// |00⟩ → |00⟩
    /// |01⟩ → |01⟩
    /// |10⟩ → |10⟩
    /// |11⟩ → e^(iθ)|11⟩  (both qubits 1, add phase e^(iθ))
    ///
    /// Note: CPhase is symmetric - control and target roles are interchangeable
    let applyCPhase
        (controlIndex: int)
        (targetIndex: int)
        (angle: float)
        (state: StateVector.StateVector)
        : StateVector.StateVector =
        let numQubits = StateVector.numQubits state

        if controlIndex < 0 || controlIndex >= numQubits then
            failwith $"Control qubit index {controlIndex} out of range for {numQubits}-qubit state"

        if targetIndex < 0 || targetIndex >= numQubits then
            failwith $"Target qubit index {targetIndex} out of range for {numQubits}-qubit state"

        if controlIndex = targetIndex then
            failwith
                $"Control and target qubits must be different, calling applyCPhase with controlIndex: {controlIndex}, targetIndex: {targetIndex}, angle: {angle}, state: {state}"

        applyControlledMatrix (1 <<< controlIndex) targetIndex (Matrices.p angle) state

    /// Apply Controlled-Phase gate CP(θ) to specified control and target qubits
    /// This is an alias for applyCPhase with clearer naming for circuit building
    ///
    /// CP(θ) applies P(θ) to target when control is |1⟩
    let applyCP
        (controlIndex: int)
        (targetIndex: int)
        (theta: float)
        (state: StateVector.StateVector)
        : StateVector.StateVector =
        applyCPhase controlIndex targetIndex theta state

    /// Apply CRX (Controlled-RX) gate to specified control and target qubits
    ///
    /// CRX applies RX(θ) rotation to target qubit when control qubit is |1⟩
    /// Matrix: [[1, 0, 0, 0],
    ///          [0, 1, 0, 0],
    ///          [0, 0, cos(θ/2), -i*sin(θ/2)],
    ///          [0, 0, -i*sin(θ/2), cos(θ/2)]]
    let applyCRX
        (controlIndex: int)
        (targetIndex: int)
        (angle: float)
        (state: StateVector.StateVector)
        : StateVector.StateVector =
        let numQubits = StateVector.numQubits state

        if controlIndex < 0 || controlIndex >= numQubits then
            failwith $"Control qubit index {controlIndex} out of range for {numQubits}-qubit state"

        if targetIndex < 0 || targetIndex >= numQubits then
            failwith $"Target qubit index {targetIndex} out of range for {numQubits}-qubit state"

        if controlIndex = targetIndex then
            failwith
                $"Control and target qubits must be different, calling applyCRX with controlIndex: {controlIndex}, targetIndex: {targetIndex}, angle: {angle}, state: {state}"

        applyControlledMatrix (1 <<< controlIndex) targetIndex (Matrices.rx angle) state

    /// Apply CRY (Controlled-RY) gate to specified control and target qubits
    ///
    /// CRY applies RY(θ) rotation to target qubit when control qubit is |1⟩
    /// Matrix: [[1, 0, 0, 0],
    ///          [0, 1, 0, 0],
    ///          [0, 0, cos(θ/2), -sin(θ/2)],
    ///          [0, 0, sin(θ/2), cos(θ/2)]]
    let applyCRY
        (controlIndex: int)
        (targetIndex: int)
        (angle: float)
        (state: StateVector.StateVector)
        : StateVector.StateVector =
        let numQubits = StateVector.numQubits state

        if controlIndex < 0 || controlIndex >= numQubits then
            failwith $"Control qubit index {controlIndex} out of range for {numQubits}-qubit state"

        if targetIndex < 0 || targetIndex >= numQubits then
            failwith $"Target qubit index {targetIndex} out of range for {numQubits}-qubit state"

        if controlIndex = targetIndex then
            failwith
                $"Control and target qubits must be different, calling applyCRY with controlIndex: {controlIndex}, targetIndex: {targetIndex}, angle: {angle}, state: {state}"

        applyControlledMatrix (1 <<< controlIndex) targetIndex (Matrices.ry angle) state

    /// Apply CRZ (Controlled-RZ) gate to specified control and target qubits
    ///
    /// CRZ applies RZ(θ) rotation to target qubit when control qubit is |1⟩
    /// Matrix: [[1, 0, 0, 0],
    ///          [0, 1, 0, 0],
    ///          [0, 0, e^(-iθ/2), 0],
    ///          [0, 0, 0, e^(iθ/2)]]
    let applyCRZ
        (controlIndex: int)
        (targetIndex: int)
        (angle: float)
        (state: StateVector.StateVector)
        : StateVector.StateVector =
        let numQubits = StateVector.numQubits state

        if controlIndex < 0 || controlIndex >= numQubits then
            failwith $"Control qubit index {controlIndex} out of range for {numQubits}-qubit state"

        if targetIndex < 0 || targetIndex >= numQubits then
            failwith $"Target qubit index {targetIndex} out of range for {numQubits}-qubit state"

        if controlIndex = targetIndex then
            failwith
                $"Control and target qubits must be different, calling applyCRZ with controlIndex: {controlIndex}, targetIndex: {targetIndex}, angle: {angle}, state: {state}"

        applyControlledMatrix (1 <<< controlIndex) targetIndex (Matrices.rz angle) state

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
            failwith
                $"SWAP qubits must be different, calling applySWAP with qubit1Index: {qubit1Index}, qubit2Index: {qubit2Index}, state: {state}"

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

        StateVector.ofAmplitudesOwned newAmplitudes

    /// Shared validation for the two-qubit Ising interaction gates
    let private validateTwoQubitPair
        (gateLabel: string)
        (qubit1Index: int)
        (qubit2Index: int)
        (state: StateVector.StateVector)
        =
        let numQubits = StateVector.numQubits state

        if qubit1Index < 0 || qubit1Index >= numQubits then
            failwith $"Qubit1 index {qubit1Index} out of range for {numQubits}-qubit state"

        if qubit2Index < 0 || qubit2Index >= numQubits then
            failwith $"Qubit2 index {qubit2Index} out of range for {numQubits}-qubit state"

        if qubit1Index = qubit2Index then
            failwith $"{gateLabel} qubits must be different"

    /// Apply RZZ gate: exp(-iθ/2·Z⊗Z)
    ///
    /// Diagonal in the computational basis: basis states where the two qubits
    /// agree get phase e^(-iθ/2), states where they differ get e^(iθ/2).
    let applyRzz
        (qubit1Index: int)
        (qubit2Index: int)
        (theta: float)
        (state: StateVector.StateVector)
        : StateVector.StateVector =
        validateTwoQubitPair "RZZ" qubit1Index qubit2Index state

        let dimension = StateVector.dimension state
        let mask1 = 1 <<< qubit1Index
        let mask2 = 1 <<< qubit2Index
        let halfTheta = theta / 2.0
        let phaseSame = Complex(cos halfTheta, -sin halfTheta) // e^(-iθ/2)
        let phaseDiff = Complex(cos halfTheta, sin halfTheta) // e^(+iθ/2)

        let newAmplitudes =
            Array.init dimension (fun i ->
                let bitsAgree = ((i &&& mask1) <> 0) = ((i &&& mask2) <> 0)
                let phase = if bitsAgree then phaseSame else phaseDiff
                phase * StateVector.getAmplitude i state)

        StateVector.ofAmplitudesOwned newAmplitudes

    /// Apply RXX gate: exp(-iθ/2·X⊗X)
    ///
    /// Couples each basis state |i⟩ with the state where BOTH qubits are
    /// flipped: cos(θ/2)·|i⟩ - i·sin(θ/2)·|i ⊕ both⟩.
    let applyRxx
        (qubit1Index: int)
        (qubit2Index: int)
        (theta: float)
        (state: StateVector.StateVector)
        : StateVector.StateVector =
        validateTwoQubitPair "RXX" qubit1Index qubit2Index state

        let dimension = StateVector.dimension state
        let flipMask = (1 <<< qubit1Index) ||| (1 <<< qubit2Index)
        let c = Complex(cos (theta / 2.0), 0.0)
        let minusISin = Complex(0.0, -sin(theta / 2.0))

        let newAmplitudes =
            Array.init dimension (fun i ->
                let j = i ^^^ flipMask

                c * StateVector.getAmplitude i state
                + minusISin * StateVector.getAmplitude j state)

        StateVector.ofAmplitudesOwned newAmplitudes

    /// Apply RYY gate: exp(-iθ/2·Y⊗Y)
    ///
    /// Like RXX but with a sign from the Y matrix elements:
    /// (Y⊗Y)|ab⟩ = -|āb̄⟩ when a = b and +|āb̄⟩ when a ≠ b.
    let applyRyy
        (qubit1Index: int)
        (qubit2Index: int)
        (theta: float)
        (state: StateVector.StateVector)
        : StateVector.StateVector =
        validateTwoQubitPair "RYY" qubit1Index qubit2Index state

        let dimension = StateVector.dimension state
        let mask1 = 1 <<< qubit1Index
        let mask2 = 1 <<< qubit2Index
        let flipMask = mask1 ||| mask2
        let c = Complex(cos (theta / 2.0), 0.0)
        let minusISin = Complex(0.0, -sin(theta / 2.0))

        let newAmplitudes =
            Array.init dimension (fun i ->
                let j = i ^^^ flipMask
                let bitsAgree = ((i &&& mask1) <> 0) = ((i &&& mask2) <> 0)
                let sign = if bitsAgree then -1.0 else 1.0

                c * StateVector.getAmplitude i state
                + sign * minusISin * StateVector.getAmplitude j state)

        StateVector.ofAmplitudesOwned newAmplitudes

    // ============================================================================
    // 7. THREE-QUBIT GATES (Depend on StateVector operations)
    // ============================================================================

    /// Apply multi-controlled Z gate (generalized CZ for n controls)
    ///
    /// Applies Z to target qubit when ALL control qubits are |1⟩
    /// This is the key gate for Grover's diffusion operator
    ///
    /// Phase table (for target=1):
    /// All controls=1, target=1 → phase flip (-1)
    /// All other states → no change
    ///
    /// CRITICAL: This must check ALL controls are 1 simultaneously
    /// Chaining CZ gates is INCORRECT and causes Grover's algorithm to fail
    let applyMultiControlledZ
        (controlIndices: int list)
        (targetIndex: int)
        (state: StateVector.StateVector)
        : StateVector.StateVector =
        let numQubits = StateVector.numQubits state

        // Validate control indices
        let invalidControl =
            controlIndices
            |> List.tryFind (fun idx -> idx < 0 || idx >= numQubits || idx = targetIndex)

        match invalidControl with
        | Some idx when idx = targetIndex ->
            failwith
                $"Control and target qubits must be distinct, calling applyMultiControlledZ with controlIndices: {controlIndices}, targetIndex: {targetIndex}, state: {state}"
        | Some idx -> failwith $"Control qubit index {idx} out of range for {numQubits}-qubit state"
        | None -> ()

        // Validate target index
        if targetIndex < 0 || targetIndex >= numQubits then
            failwith $"Target qubit index {targetIndex} out of range for {numQubits}-qubit state"

        let dimension = StateVector.dimension state
        let targetMask = 1 <<< targetIndex

        // Fold the controls into ONE mask: "all controls are |1⟩" is then a single
        // AND, instead of a List.forall over a list of masks per basis state.
        let controlMask = controlIndices |> List.fold (fun acc i -> acc ||| (1 <<< i)) 0

        // This gate is diagonal — it only flips the sign of the |controls=1, target=1⟩
        // block — so walk the amplitudes directly. The previous form materialised
        // `[| 0 .. dimension - 1 |]`, a whole second 2^n array of ints purely to have
        // something to Array.map over: 4 GB of pure waste at 30 qubits.
        let src = StateVector.amplitudesView state
        let dst = Array.zeroCreate<Complex> dimension
        let flipMask = controlMask ||| targetMask

        for i in 0 .. dimension - 1 do
            dst.[i] <- if i &&& flipMask = flipMask then -src.[i] else src.[i]

        StateVector.ofAmplitudesOwned dst

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
    let applyCCX
        (control1Index: int)
        (control2Index: int)
        (targetIndex: int)
        (state: StateVector.StateVector)
        : StateVector.StateVector =
        let numQubits = StateVector.numQubits state

        if control1Index < 0 || control1Index >= numQubits then
            failwith $"Control1 qubit index {control1Index} out of range for {numQubits}-qubit state"

        if control2Index < 0 || control2Index >= numQubits then
            failwith $"Control2 qubit index {control2Index} out of range for {numQubits}-qubit state"

        if targetIndex < 0 || targetIndex >= numQubits then
            failwith $"Target qubit index {targetIndex} out of range for {numQubits}-qubit state"

        if
            control1Index = control2Index
            || control1Index = targetIndex
            || control2Index = targetIndex
        then
            failwith
                $"CCX (Toffoli) control and target qubits must be distinct, calling applyCCX with control1Index: {control1Index}, control2Index: {control2Index}, targetIndex: {targetIndex}, state: {state}"

        applyControlledMatrix ((1 <<< control1Index) ||| (1 <<< control2Index)) targetIndex Matrices.x state

    // ============================================================================
    // 8. IN-PLACE KERNELS (for states the caller exclusively owns)
    // ============================================================================

    /// Gate kernels that overwrite the state instead of returning a new one.
    ///
    /// Every gate above allocates a fresh 2^n amplitude array, because an ordinary
    /// `StateVector` may be shared and must keep its value. Inside a circuit-execution
    /// fold nothing is shared — each intermediate state is consumed by the next gate
    /// and never read again — so the allocation is pure waste: 16 GB per gate at 30
    /// qubits, plus the page-faulting that comes with it.
    ///
    /// `StateVector.Owned` marks the states where that reasoning applies, so the
    /// ownership requirement is carried by the type rather than by a comment. These
    /// functions are internal: the public gate API keeps value semantics, which is
    /// what stops `Primitives.expectation` from reading a state a gate has rewritten.
    ///
    /// Each kernel computes exactly the same arithmetic as its allocating twin;
    /// `LocalBackendTests` pins the two against each other over whole circuits.
    module internal InPlace =

        /// Two-by-two matrix acting on the (qubit=0, qubit=1) pair of amplitudes.
        let applySingleQubitGate
            (qubitIndex: int)
            (a: Complex, b: Complex, c: Complex, d: Complex)
            (owned: StateVector.Owned)
            : unit =
            let amplitudes = StateVector.ownedAmplitudes owned
            let dimension = amplitudes.Length
            let bitMask = 1 <<< qubitIndex

            for i in 0 .. dimension - 1 do
                if i &&& bitMask = 0 then
                    let j = i ||| bitMask
                    let amp0 = amplitudes.[i]
                    let amp1 = amplitudes.[j]
                    amplitudes.[i] <- a * amp0 + b * amp1
                    amplitudes.[j] <- c * amp0 + d * amp1


        /// Same, but only where every bit of `controlMask` is set.
        let applyControlledGate
            (controlMask: int)
            (targetIndex: int)
            (a: Complex, b: Complex, c: Complex, d: Complex)
            (owned: StateVector.Owned)
            : unit =
            let amplitudes = StateVector.ownedAmplitudes owned
            let dimension = amplitudes.Length
            let targetMask = 1 <<< targetIndex

            for i in 0 .. dimension - 1 do
                if i &&& targetMask = 0 && i &&& controlMask = controlMask then
                    let j = i ||| targetMask
                    let amp0 = amplitudes.[i]
                    let amp1 = amplitudes.[j]
                    amplitudes.[i] <- a * amp0 + b * amp1
                    amplitudes.[j] <- c * amp0 + d * amp1


        /// Multiply a per-index phase in: for gates that are diagonal in the
        /// computational basis, so no amplitude ever moves.
        let applyDiagonal (phaseAt: int -> Complex) (owned: StateVector.Owned) : unit =
            let amplitudes = StateVector.ownedAmplitudes owned
            let dimension = amplitudes.Length

            for i in 0 .. dimension - 1 do
                amplitudes.[i] <- phaseAt i * amplitudes.[i]

        /// Exchange the amplitudes of |01⟩ and |10⟩ on the two given qubits.
        let applySwap (qubit1Index: int) (qubit2Index: int) (owned: StateVector.Owned) : unit =
            let amplitudes = StateVector.ownedAmplitudes owned
            let dimension = amplitudes.Length
            let mask1 = 1 <<< qubit1Index
            let mask2 = 1 <<< qubit2Index

            for i in 0 .. dimension - 1 do
                // Visit each unordered pair once: take the index with bit1 set and
                // bit2 clear, and swap it with its partner.
                if i &&& mask1 <> 0 && i &&& mask2 = 0 then
                    let j = i ^^^ mask1 ^^^ mask2
                    let temp = amplitudes.[i]
                    amplitudes.[i] <- amplitudes.[j]
                    amplitudes.[j] <- temp
