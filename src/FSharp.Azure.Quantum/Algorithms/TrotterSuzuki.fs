namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum.Core
open System.Numerics
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.CircuitBuilder

/// Trotter-Suzuki Decomposition Module
/// 
/// Implements Hamiltonian simulation for time evolution: U(t) = e^(-iHt)
/// where H is a Hermitian operator (typically a matrix).
/// 
/// **Trotter-Suzuki Formula:**
/// For H = H₁ + H₂ + ... + Hₖ (sum of terms), approximate:
///   e^(-iHt) ≈ [e^(-iH₁t/n) e^(-iH₂t/n) ... e^(-iHₖt/n)]^n
/// 
/// Error: O(t²/n) for 1st order, O(t³/n²) for 2nd order
/// 
/// **Applications:**
/// - HHL algorithm: Simulate matrix A = e^(iAt) for eigenvalue estimation
/// - Quantum chemistry: Time evolution of molecular systems
/// - Quantum dynamics: General quantum system simulation
/// 
/// **Key Features:**
/// - 1st order and 2nd order Trotter formulas
/// - Pauli decomposition for efficient circuit synthesis
/// - Configurable Trotter steps for accuracy vs. depth tradeoff
module TrotterSuzuki =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Pauli string: tensor product of Pauli operators
    /// Example: "XYZII" means X⊗Y⊗Z⊗I⊗I
    type PauliString = {
        /// Pauli operators: 'I', 'X', 'Y', 'Z' for each qubit
        Operators: char[]
        
        /// Coefficient for this term in Hamiltonian
        Coefficient: Complex
    }
    
    /// Trotter decomposition configuration
    type TrotterConfig = {
        /// Number of Trotter steps (higher = more accurate, deeper circuit)
        /// Recommendation: n ≥ ceil(‖H‖²t²/ε) for error ε
        NumSteps: int
        
        /// Time parameter for evolution e^(-iHt)
        Time: float
        
        /// Trotter order: 1 or 2
        /// Order 1: [e^(-iH₁Δt) ... e^(-iHₖΔt)]^n, error O(t²/n)
        /// Order 2: Symmetrized, error O(t³/n²)
        Order: int
    }
    
    /// Hamiltonian decomposed into Pauli terms
    type PauliHamiltonian = {
        /// List of Pauli string terms: H = Σᵢ cᵢ Pᵢ
        Terms: PauliString list
        
        /// Number of qubits this Hamiltonian acts on
        NumQubits: int
    }
    
    // ========================================================================
    // PAULI DECOMPOSITION - Convert Matrix to Pauli Basis
    // ========================================================================
    
    /// Get single Pauli matrix (2x2)
    let private getSinglePauliMatrix (pauli: char) : Complex[,] =
        match pauli with
        | 'I' -> array2D [[Complex(1.0, 0.0); Complex.Zero]; 
                          [Complex.Zero; Complex(1.0, 0.0)]]
        | 'X' -> array2D [[Complex.Zero; Complex(1.0, 0.0)]; 
                          [Complex(1.0, 0.0); Complex.Zero]]
        | 'Y' -> array2D [[Complex.Zero; Complex(0.0, -1.0)]; 
                          [Complex(0.0, 1.0); Complex.Zero]]
        | 'Z' -> array2D [[Complex(1.0, 0.0); Complex.Zero]; 
                          [Complex.Zero; Complex(-1.0, 0.0)]]
        | _ -> array2D [[Complex(1.0, 0.0); Complex.Zero]; 
                        [Complex.Zero; Complex(1.0, 0.0)]]
    
    /// Compute tensor product of two matrices
    let private tensorProduct (A: Complex[,]) (B: Complex[,]) : Complex[,] =
        let m1 = Array2D.length1 A
        let n1 = Array2D.length2 A
        let m2 = Array2D.length1 B
        let n2 = Array2D.length2 B
        
        let result = Array2D.create (m1 * m2) (n1 * n2) Complex.Zero
        
        for i1 in 0 .. m1 - 1 do
            for j1 in 0 .. n1 - 1 do
                for i2 in 0 .. m2 - 1 do
                    for j2 in 0 .. n2 - 1 do
                        result[i1 * m2 + i2, j1 * n2 + j2] <- A[i1, j1] * B[i2, j2]
        
        result
    
    /// Decompose 2x2 matrix into Pauli basis: M = c₀I + c₁X + c₂Y + c₃Z
    let private decompose2x2ToPauli (matrix: Complex[,]) : Complex * Complex * Complex * Complex =
        // Pauli matrices:
        // I = [[1,0],[0,1]], X = [[0,1],[1,0]]
        // Y = [[0,-i],[i,0]], Z = [[1,0],[0,-1]]
        
        // Coefficients: cᵢ = Tr(M·Pᵢ) / 2
        let cI = (matrix[0,0] + matrix[1,1]) / Complex(2.0, 0.0)
        let cX = (matrix[0,1] + matrix[1,0]) / Complex(2.0, 0.0)
        let cY = (Complex(0.0, 1.0) * (matrix[1,0] - matrix[0,1])) / Complex(2.0, 0.0)
        let cZ = (matrix[0,0] - matrix[1,1]) / Complex(2.0, 0.0)
        
        (cI, cX, cY, cZ)
    
    /// Decompose arbitrary Hermitian matrix into Pauli basis
    /// H = Σᵢ cᵢ Pᵢ where Pᵢ are tensor products of Pauli operators
    let decomposeMatrixToPauli (matrix: Complex[,]) : QuantumResult<PauliHamiltonian> =
        let n = Array2D.length1 matrix
        let m = Array2D.length2 matrix
        
        if n <> m then
            Error (QuantumError.ValidationError ("Matrix", "must be square"))
        elif (n &&& (n - 1)) <> 0 then
            Error (QuantumError.Other $"Matrix dimension must be power of 2, got {n}")
        else
            // Calculate number of qubits
            let numQubits = 
                let rec log2 k = if k <= 1 then 0 else 1 + log2 (k / 2)
                log2 n
            
            // For small matrices (2x2), direct decomposition
            if n = 2 then
                let (cI, cX, cY, cZ) = decompose2x2ToPauli matrix
                
                let terms = [
                    if cI.Magnitude > 1e-10 then 
                        { Operators = [|'I'|]; Coefficient = cI }
                    if cX.Magnitude > 1e-10 then
                        { Operators = [|'X'|]; Coefficient = cX }
                    if cY.Magnitude > 1e-10 then
                        { Operators = [|'Y'|]; Coefficient = cY }
                    if cZ.Magnitude > 1e-10 then
                        { Operators = [|'Z'|]; Coefficient = cZ }
                ]
                
                Ok { Terms = terms; NumQubits = 1 }
            
            // For larger matrices, use tensor product decomposition
            else
                // Generate all possible Pauli strings
                let paulis = [| 'I'; 'X'; 'Y'; 'Z' |]
                
                let rec generatePauliStrings nQubits =
                    if nQubits = 0 then [[||]]
                    elif nQubits = 1 then 
                        paulis |> Array.map (fun p -> [|p|]) |> Array.toList
                    else
                        let smaller = generatePauliStrings (nQubits - 1)
                        [ for p in paulis do
                            for rest in smaller do
                                yield Array.append [|p|] rest ]
                
                let allPauliStrings = generatePauliStrings numQubits
                
                // Calculate coefficient for each Pauli string
                // c_P = Tr(H·P) / 2^n
                let terms =
                    allPauliStrings
                    |> List.choose (fun pauliOps ->
                        // ✅ FIXED: Compute proper tensor product of Pauli matrices
                        let pauliMatrix = 
                            pauliOps
                            |> Array.map getSinglePauliMatrix
                            |> Array.reduce tensorProduct
                        
                        // Calculate coefficient: c = Tr(H·P) / 2^n
                        let trace = 
                            [ 0 .. n - 1 ]
                            |> List.fold (fun acc i ->
                                let rowSum =
                                    [ 0 .. n - 1 ]
                                    |> List.fold (fun sum j -> sum + matrix[i, j] * pauliMatrix[j, i]) Complex.Zero
                                acc + rowSum
                            ) Complex.Zero
                        
                        let coefficient = trace / Complex(float n, 0.0)
                        
                        if coefficient.Magnitude > 1e-10 then
                            Some { Operators = pauliOps; Coefficient = coefficient }
                        else
                            None
                    )
                
                Ok { Terms = terms; NumQubits = numQubits }
    
    /// Decompose diagonal matrix (fast path)
    let decomposeDiagonalMatrixToPauli (eigenvalues: float[]) : PauliHamiltonian =
        let n = eigenvalues.Length
        let numQubits = 
            let rec log2 k = if k <= 1 then 0 else 1 + log2 (k / 2)
            log2 n
        
        // For diagonal matrices, use Z basis only
        // Each eigenvalue corresponds to a computational basis state
        // H = Σᵢ λᵢ |i⟩⟨i| = Σᵢ λᵢ (I + (-1)^(bit₀)Z₀)/2 ⊗ ... ⊗ (I + (-1)^(bitₙ)Zₙ)/2
        
        // Simplified: Use sum of Z terms
        let terms =
            [ for q in 0 .. numQubits - 1 do
                let pauliOps = Array.create numQubits 'I'
                pauliOps[q] <- 'Z'
                
                // Weight by average eigenvalue contribution from this qubit position
                let weight = 
                    eigenvalues
                    |> Array.mapi (fun idx ev -> 
                        if (idx &&& (1 <<< q)) <> 0 then ev else 0.0)
                    |> Array.average
                
                if abs weight > 1e-10 then
                    yield { Operators = pauliOps; Coefficient = Complex(weight, 0.0) }
            ]
        
        { Terms = terms; NumQubits = numQubits }
    
    // ========================================================================
    // CIRCUIT SYNTHESIS - Convert Pauli Evolution to Gates
    // ========================================================================
    
    /// Synthesize circuit for e^(-iPt) where P is a Pauli string
    /// 
    /// Algorithm:
    /// 1. Change of basis: X → H, Y → S†H, Z → I
    /// 2. CNOT ladder to concentrate parity
    /// 3. RZ rotation by angle 2t (since e^(-iZt) = RZ(2t))
    /// 4. Inverse CNOT ladder
    /// 5. Inverse change of basis
    let synthesizePauliEvolution
        (pauliString: PauliString)
        (time: float)
        (qubits: int[])
        (circuit: Circuit) : Circuit =
        
        if qubits.Length <> pauliString.Operators.Length then
            failwith "Qubit count mismatch"
        
        // Extract qubits where Pauli is non-identity
        let nonIdentityIndices =
            pauliString.Operators
            |> Array.mapi (fun i op -> (i, op))
            |> Array.filter (fun (_, op) -> op <> 'I')
        
        if nonIdentityIndices.Length = 0 then
            // Global phase - ignore
            circuit
        elif nonIdentityIndices.Length = 1 then
            // Single-qubit rotation
            let (idx, op) = nonIdentityIndices[0]
            let qubit = qubits[idx]
            let angle = 2.0 * pauliString.Coefficient.Real * time
            
            match op with
            | 'X' -> circuit |> addGate (RX(qubit, angle))
            | 'Y' -> circuit |> addGate (RY(qubit, angle))
            | 'Z' -> circuit |> addGate (RZ(qubit, angle))
            | _ -> circuit
        else
            // Multi-qubit rotation - use CNOT ladder
            
            // Step 1: Change of basis
            let circ1 =
                nonIdentityIndices
                |> Array.fold (fun circ (idx, op) ->
                    let qubit = qubits[idx]
                    match op with
                    | 'X' -> circ |> addGate (H qubit)
                    | 'Y' -> circ |> addGate (SDG qubit) |> addGate (H qubit)
                    | 'Z' -> circ  // No change needed
                    | _ -> circ
                ) circuit
            
            // Step 2: CNOT ladder (entangle all qubits)
            let targetQubit = qubits[fst nonIdentityIndices[nonIdentityIndices.Length - 1]]
            let circ2 =
                [| 0 .. nonIdentityIndices.Length - 2 |]
                |> Array.fold (fun circ i ->
                    let controlQubit = qubits[fst nonIdentityIndices[i]]
                    circ |> addGate (CNOT(controlQubit, targetQubit))
                ) circ1
            
            // Step 3: RZ rotation on target qubit
            let angle = 2.0 * pauliString.Coefficient.Real * time
            let circ3 = circ2 |> addGate (RZ(targetQubit, angle))
            
            // Step 4: Inverse CNOT ladder
            let circ4 =
                [ nonIdentityIndices.Length - 2 .. -1 .. 0 ]
                |> List.fold (fun c i ->
                    let controlQubit = qubits[fst nonIdentityIndices[i]]
                    c |> addGate (CNOT(controlQubit, targetQubit))
                ) circ3
            
            // Step 5: Inverse change of basis
            [ nonIdentityIndices.Length - 1 .. -1 .. 0 ]
            |> List.fold (fun c i ->
                let (idx, op) = nonIdentityIndices[i]
                let qubit = qubits[idx]
                match op with
                | 'X' -> c |> addGate (H qubit)
                | 'Y' -> c |> addGate (H qubit) |> addGate (S qubit)
                | 'Z' -> c  // No change needed
                | _ -> c
            ) circ4
    
    // ========================================================================
    // TROTTER-SUZUKI DECOMPOSITION
    // ========================================================================
    
    /// Apply 1st order Trotter step: e^(-iH₁Δt) e^(-iH₂Δt) ... e^(-iHₖΔt)
    let private applyFirstOrderTrotterStep
        (hamiltonian: PauliHamiltonian)
        (deltaT: float)
        (qubits: int[])
        (circuit: Circuit) : Circuit =
        
        hamiltonian.Terms
        |> List.fold (fun circ term ->
            synthesizePauliEvolution term deltaT qubits circ
        ) circuit
    
    /// Apply 2nd order Trotter step: S₂(Δt) = S₁(Δt/2) S₁†(Δt/2)
    /// where S₁† means reverse order with conjugate coefficients
    let private applySecondOrderTrotterStep
        (hamiltonian: PauliHamiltonian)
        (deltaT: float)
        (qubits: int[])
        (circuit: Circuit) : Circuit =
        
        let halfDeltaT = deltaT / 2.0
        
        // Forward half-step
        let circ1 = applyFirstOrderTrotterStep hamiltonian halfDeltaT qubits circuit
        
        // Reverse half-step (reverse order)
        let reversedTerms = List.rev hamiltonian.Terms
        let reversedHamiltonian = { hamiltonian with Terms = reversedTerms }
        
        applyFirstOrderTrotterStep reversedHamiltonian halfDeltaT qubits circ1
    
    /// Synthesize circuit for Hamiltonian time evolution: U(t) = e^(-iHt)
    /// using Trotter-Suzuki decomposition
    let synthesizeHamiltonianEvolution
        (hamiltonian: PauliHamiltonian)
        (config: TrotterConfig)
        (qubits: int[])
        (circuit: Circuit) : Circuit =
        
        if qubits.Length <> hamiltonian.NumQubits then
            failwith $"Qubit count mismatch: expected {hamiltonian.NumQubits}, got {qubits.Length}"
        
        let deltaT = config.Time / float config.NumSteps
        
        // Apply Trotter steps
        [1 .. config.NumSteps]
        |> List.fold (fun circ _ ->
            if config.Order = 2 then
                applySecondOrderTrotterStep hamiltonian deltaT qubits circ
            else
                applyFirstOrderTrotterStep hamiltonian deltaT qubits circ
        ) circuit
    
    // ========================================================================
    // HIGH-LEVEL API
    // ========================================================================
    
    /// Create default Trotter configuration
    let defaultConfig = {
        NumSteps = 10
        Time = 1.0
        Order = 1
    }
    
    /// Estimate required Trotter steps for target accuracy
    /// Formula: n ≥ ‖H‖²t²/(2ε) for 1st order
    let estimateTrotterSteps (hamiltonianNorm: float) (time: float) (tolerance: float) (order: int) : int =
        if order = 1 then
            int (ceil ((hamiltonianNorm * hamiltonianNorm * time * time) / (2.0 * tolerance)))
        else // order = 2
            int (ceil ((hamiltonianNorm * hamiltonianNorm * hamiltonianNorm * time * time * time) / (12.0 * tolerance) |> sqrt))
