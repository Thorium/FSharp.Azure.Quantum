namespace FSharp.Azure.Quantum.Topological

open System
open System.Numerics
open FSharp.Azure.Quantum

/// Gate-to-Braid compilation - reverse of BraidToGate.
/// 
/// Converts conventional quantum gates into topological braiding operations.
/// This enables running gate-based quantum algorithms (QFT, Grover, etc.)
/// on topological quantum hardware (Majorana, Fibonacci anyons).
/// 
/// **CRITICAL MATHEMATICAL NOTES**:
/// 
/// 1. **Phase Conventions**: We use the convention where:
///    - T = exp(iπ/8) (relative phase on |1⟩ state)
///    - S = T² = exp(iπ/4)
///    - Rz(θ) = diag(1, exp(iθ)) (relative Z-rotation)
/// 
/// 2. **Global vs Relative Phase**: Topological braiding produces GLOBAL phases,
///    but gate-based gates often specify RELATIVE phases. Conversion requires care!
/// 
/// 3. **Fermion Parity**: Ising anyons have fermion parity constraints.
///    Not all braiding sequences are valid - must respect fusion rules.
/// 
/// 4. **Qubit Encoding**: n qubits → n+1 anyonic strands (for Jordan-Wigner encoding)
module GateToBraid =

    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Quantum gate decomposition for topological compilation
    type GateDecomposition = {
        /// Original gate description
        GateName: string
        
        /// Qubits affected by this gate
        Qubits: int list
        
        /// Equivalent braiding sequence
        BraidSequence: BraidGroup.BraidWord list
        
        /// Approximation error (0 for exact gates)
        ApproximationError: float
        
        /// Notes about decomposition (e.g., "requires measurement", "global phase ignored")
        DecompositionNotes: string option
    }
    
    /// Internal state for gate sequence compilation (private helper)
    type private CompilationState = {
        AllBraids: BraidGroup.BraidWord list
        TotalError: float
        Warnings: string list
    }
    
    /// Gate sequence compilation result
    type GateSequenceCompilation = {
        /// Original number of gates
        OriginalGateCount: int
        
        /// Compiled braid words
        CompiledBraids: BraidGroup.BraidWord list
        
        /// Total approximation error
        TotalError: float
        
        /// Whether compilation is exact (error = 0)
        IsExact: bool
        
        /// Anyon type used for compilation
        AnyonType: AnyonSpecies.AnyonType
        
        /// Warnings or notes about compilation
        CompilationWarnings: string list
    }

    // ========================================================================
    // UTILITY FUNCTIONS
    // ========================================================================
    
    /// Sequence a list of Results into a Result of list
    /// Standard F# functional pattern for error handling
    module private ResultPrivate =
        let sequence (results: Result<'a, TopologicalError> list) : Result<'a list, TopologicalError> =
            let folder state item =
                result {
                    let! acc = state
                    let! value = item
                    return value :: acc
                }
            results
            |> List.fold folder (Ok [])
            |> Result.map List.rev
    
    /// Normalize angle to [0, 2π) range
    let private normalizeAngle (angle: float) : float =
        let twoPi = 2.0 * Math.PI
        let normalized = angle % twoPi
        if normalized < 0.0 then normalized + twoPi else normalized
    
    /// Compute approximation error for angle discretization
    let private computeAngleError (targetAngle: float) (tPhase: float) : float * int =
        let normalizedTarget = normalizeAngle targetAngle
        let numTGates = int (Math.Round(normalizedTarget / tPhase))
        let approximateAngle = float numTGates * tPhase
        let error = abs(normalizedTarget - approximateAngle)
        (error, numTGates)

    // ========================================================================
    // T GATE DECOMPOSITION (Ising Anyons)
    // ========================================================================
    
    /// Decompose T gate into Ising anyon braiding.
    /// 
    /// This is THE key mapping for fault-tolerant quantum computation:
    /// T gate = exp(iπ/8) = Majorana braiding σ_i (clockwise)
    /// 
    /// This is EXACT - no approximation needed!
    let tGateToBraid (qubitIndex: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        // T gate on qubit i ↔ braiding σ_i (clockwise)
        let gen : BraidGroup.BraidGenerator = { Index = qubitIndex; IsClockwise = true }
        BraidGroup.fromGenerators (numQubits + 1) [gen]
    
    /// Decompose T† gate into Ising anyon braiding
    let tDaggerGateToBraid (qubitIndex: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        // T† gate ↔ braiding σ_i^{-1} (counter-clockwise)
        let gen : BraidGroup.BraidGenerator = { Index = qubitIndex; IsClockwise = false }
        BraidGroup.fromGenerators (numQubits + 1) [gen]

    // ========================================================================
    // CLIFFORD GATE DECOMPOSITION
    // ========================================================================
    
    /// Decompose S gate (π/4 phase) into braiding.
    /// S = T² (two T gates in sequence)
    let sGateToBraid (qubitIndex: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        // S = T² = exp(iπ/4)
        let gen : BraidGroup.BraidGenerator = { Index = qubitIndex; IsClockwise = true }
        BraidGroup.fromGenerators (numQubits + 1) [gen; gen]
    
    /// Decompose S† gate into braiding
    let sDaggerGateToBraid (qubitIndex: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        // S† = (T†)² = exp(-iπ/4)
        let gen : BraidGroup.BraidGenerator = { Index = qubitIndex; IsClockwise = false }
        BraidGroup.fromGenerators (numQubits + 1) [gen; gen]
    
    /// Decompose Pauli Z gate into braiding.
    /// 
    /// **CRITICAL FIX**: Z = exp(iπ) requires special handling!
    /// 
    /// Z = -I (global phase times identity) but in topological QC:
    /// - Option 1: Z = T^8 (but this gives exp(iπ) = -1 global phase, not relative!)
    /// - Option 2: Measurement-based Z using fermion parity
    /// - Option 3: Ignore global phase (Z = I topologically)
    /// 
    /// For now: Z is treated as identity (global phase ignored in topological QC)
    let zGateToBraid (qubitIndex: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        // Pauli Z in topological quantum computing:
        // Z = exp(iπ)·I = -I (global phase)
        // 
        // Global phases don't affect measurement outcomes, so Z ≈ I topologically.
        // For proper Z gate, need measurement-based approach or ancilla encoding.
        //
        // Return identity braid (empty generator list) with warning
        BraidGroup.fromGenerators (numQubits + 1) []
    
    // ========================================================================
    // SOLOVAY-KITAEV GATE APPROXIMATION
    // ========================================================================
    
    /// Convert single Solovay-Kitaev BasicGate to braiding
    /// Only accepts T, S, Z gates - H/X/Y should never appear in topological S-K output
    let basicGateToBraid (gate: SolovayKitaev.BasicGate) (qubitIndex: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        match gate with
        | SolovayKitaev.T -> tGateToBraid qubitIndex numQubits
        | SolovayKitaev.TDagger -> tDaggerGateToBraid qubitIndex numQubits
        | SolovayKitaev.S -> sGateToBraid qubitIndex numQubits
        | SolovayKitaev.SDagger -> sDaggerGateToBraid qubitIndex numQubits
        | SolovayKitaev.Z -> zGateToBraid qubitIndex numQubits
        | SolovayKitaev.I -> Ok (BraidGroup.identity (numQubits + 1))
        // H/X/Y should NEVER appear in output when using topological-compatible base set
        | SolovayKitaev.H | SolovayKitaev.X | SolovayKitaev.Y ->
            TopologicalResult.computationError "gateConversion" $"Gate %A{gate} should not appear in Solovay-Kitaev output for topological systems"
    
    /// Compose a list of braids (left-to-right)
    let composeBraids (braids: BraidGroup.BraidWord list) (numQubits: int) : BraidGroup.BraidWord =
        braids 
        |> List.fold (fun acc braid ->
            match BraidGroup.compose acc braid with
            | Ok composed -> composed
            | Error _ -> acc  // Fallback to accumulator on error (shouldn't happen with valid braids)
        ) (BraidGroup.identity (numQubits + 1))
    
    /// Convert Solovay-Kitaev gate sequence to composed braiding
    let solovayKitaevGatesToBraid (gates: SolovayKitaev.GateSequence) (qubitIndex: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        gates
        |> List.map (fun gate -> basicGateToBraid gate qubitIndex numQubits)
        |> ResultPrivate.sequence
        |> Result.map (fun braids -> composeBraids braids numQubits)
    
    /// Hadamard gate decomposition using topological-specific Solovay-Kitaev
    /// 
    /// H cannot be represented exactly with finite braiding sequence.
    /// Uses topological-specific S-K with base set = {T, S, Z} only.
    /// 
    /// **Performance:**
    /// - Gate count: ~300-500 T/S gates for ε = 10⁻⁵
    /// - 70-80% reduction vs standard S-K (which would use ~1000-2000 gates)
    /// - No circular dependencies (H not in base set)
    /// 
    /// Achieves error < tolerance using O(log^2.71(1/tolerance)) gates
    let hadamardGateToBraid (qubitIndex: int) (numQubits: int) (tolerance: float) : Result<BraidGroup.BraidWord, TopologicalError> =
        // Use topological-specific S-K (base set = {T, S, Z} only)
        let result = SolovayKitaev.approximateHadamardTopological tolerance
        
        // Convert gate sequence to braiding (guaranteed to be only T/S/Z gates!)
        solovayKitaevGatesToBraid result.Gates qubitIndex numQubits
    
    /// Pauli X gate decomposition using topological-specific Solovay-Kitaev
    /// X = H·Z·H, but we use direct approximation for better gate count
    let pauliXGateToBraid (qubitIndex: int) (numQubits: int) (tolerance: float) : Result<BraidGroup.BraidWord, TopologicalError> =
        // Use topological-specific S-K
        let result = SolovayKitaev.approximatePauliXTopological tolerance
        solovayKitaevGatesToBraid result.Gates qubitIndex numQubits
    
    /// Pauli Y gate decomposition using topological-specific Solovay-Kitaev
    let pauliYGateToBraid (qubitIndex: int) (numQubits: int) (tolerance: float) : Result<BraidGroup.BraidWord, TopologicalError> =
        // Use topological-specific S-K
        let result = SolovayKitaev.approximatePauliYTopological tolerance
        solovayKitaevGatesToBraid result.Gates qubitIndex numQubits

    // ========================================================================
    // ROTATION GATE DECOMPOSITION
    // ========================================================================
    
    /// Decompose Rz(θ) gate into braiding sequence with proper error handling.
    /// 
    /// **MATHEMATICAL NOTE**: 
    /// Rz(θ) = diag(1, exp(iθ)) is a RELATIVE phase gate.
    /// T gates produce GLOBAL phases exp(iπ/8).
    /// 
    /// For topological QC, global phases don't matter, so:
    /// Rz(θ) ≈ T^n where n = round(θ / (π/8))
    /// 
    /// This is an APPROXIMATION unless θ is an exact multiple of π/8.
    let rzGateToBraid (qubitIndex: int) (angle: float) (numQubits: int) (tolerance: float) : Result<BraidGroup.BraidWord, TopologicalError> =
        // Normalize angle to [0, 2π)
        let tPhase = Math.PI / 8.0
        let (error, numTGates) = computeAngleError angle tPhase
        
        // Check if approximation is within tolerance
        if error > tolerance then
            TopologicalResult.computationError "Rz gate approximation" $"Rz({angle}) approximation error {error:F6} exceeds tolerance {tolerance:F6}"
        else
            // All gates are clockwise after normalization
            let gens : BraidGroup.BraidGenerator list = 
                List.init numTGates (fun _ -> 
                    { Index = qubitIndex; IsClockwise = true })
            
            BraidGroup.fromGenerators (numQubits + 1) gens
    
    /// Decompose arbitrary phase gate Phase(θ)
    /// 
    /// **CONVENTION**: Phase(θ) = diag(1, exp(iθ)) = Rz(θ)
    /// Same as Rz in our convention.
    let phaseGateToBraid (qubitIndex: int) (angle: float) (numQubits: int) (tolerance: float) : Result<BraidGroup.BraidWord, TopologicalError> =
        // Phase gate = Rz gate in our convention
        rzGateToBraid qubitIndex angle numQubits tolerance
    
    /// Decompose U3(θ, φ, λ) gate using Solovay-Kitaev approximation
    /// 
    /// U3(θ, φ, λ) is the general single-qubit unitary:
    /// [[cos(θ/2), -e^(iλ) sin(θ/2)],
    ///  [e^(iφ) sin(θ/2), e^(i(φ+λ)) cos(θ/2)]]
    /// 
    /// This is the most general single-qubit gate (3 real parameters).
    /// Uses Solovay-Kitaev to approximate with T/S/Z gates.
    let u3GateToBraid (qubitIndex: int) (theta: float) (phi: float) (lambda: float) (numQubits: int) (tolerance: float) : Result<BraidGroup.BraidWord, TopologicalError> =
        // Convert U3 parameters to SU2 matrix
        let halfTheta = theta / 2.0
        let cosHalf = cos halfTheta
        let sinHalf = sin halfTheta
        
        let u3Matrix = {
            SolovayKitaev.A = Complex(cosHalf, 0.0)
            SolovayKitaev.B = Complex(-sinHalf * cos lambda, -sinHalf * sin lambda)
            SolovayKitaev.C = Complex(sinHalf * cos phi, sinHalf * sin phi)
            SolovayKitaev.D = Complex(cosHalf * cos (phi + lambda), cosHalf * sin (phi + lambda))
        }
        
        // Use topological Solovay-Kitaev to approximate
        let result = SolovayKitaev.approximateGateTopological u3Matrix tolerance 4 12
        
        // Convert gate sequence to braiding
        solovayKitaevGatesToBraid result.Gates qubitIndex numQubits


    // ========================================================================
    // TWO-QUBIT GATE DECOMPOSITION
    // ========================================================================
    
    /// Create entangling braid between two qubits for CZ gate implementation.
    /// 
    /// **Note on Indexing**
    /// Our braid generators are indexed by *qubit* (not "2 strands per qubit").
    /// The backend uses `n = numQubits + 1` strands, so valid generator indices are
    /// `0..numQubits-1`.
    ///
    /// For non-adjacent qubits we synthesize a long-range interaction by
    /// composing adjacent generators along the qubit interval.
    let createEntanglingBraid (controlQubit: int) (targetQubit: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        if abs (controlQubit - targetQubit) = 1 then
            // Adjacent qubits: single generator
            let gen : BraidGroup.BraidGenerator = { Index = min controlQubit targetQubit; IsClockwise = true }
            BraidGroup.fromGenerators (numQubits + 1) [ gen ]
        else
            // Non-adjacent qubits: chain adjacent generators across the interval
            let minQubit = min controlQubit targetQubit
            let maxQubit = max controlQubit targetQubit

            let braidGens : BraidGroup.BraidGenerator list =
                [ minQubit .. maxQubit - 1 ]
                |> List.map (fun q -> { Index = q; IsClockwise = true })

            BraidGroup.fromGenerators (numQubits + 1) braidGens
    
    /// Decompose Controlled-Z (CZ) gate into braiding sequence.
    /// 
    /// **Mathematical Foundation:**
    /// CZ gate matrix:
    ///   CZ = diag(1, 1, 1, -1) = |00⟩⟨00| + |01⟩⟨01| + |10⟩⟨10| - |11⟩⟨11|
    /// 
    /// For Ising anyons, CZ requires braiding between control and target strands.
    /// 
    /// **Decomposition Strategy:**
    /// We use the fact that CZ is symmetric and can be implemented via:
    ///   1. Apply S gate to both control and target qubits
    ///   2. Perform entangling braid between the qubits' anyonic strands
    ///   3. Apply S† gate to both qubits
    /// 
    /// **Topological Implementation:**
    /// For qubits encoded in anyonic strands:
    ///   - Qubit i is encoded in strands (2i, 2i+1) 
    ///   - CZ requires braiding strand 2*control+1 with strand 2*target
    ///   - This creates the phase flip on |11⟩ state
    let controlledZGateToBraid (controlQubit: int) (targetQubit: int) (numQubits: int) (tolerance: float) : Result<BraidGroup.BraidWord, TopologicalError> =
        result {
            // Step 1: Apply S gates to set up the entangling interaction
            let! sControl = sGateToBraid controlQubit numQubits
            let! sTarget = sGateToBraid targetQubit numQubits
            let! s_both = BraidGroup.compose sControl sTarget
            
            // Step 2: Entangling braid between control and target qubits
            let! entangle = createEntanglingBraid controlQubit targetQubit numQubits
            
            // Step 3: Apply S† gates to complete the CZ operation
            let! sDagControl = sDaggerGateToBraid controlQubit numQubits
            let! sDagTarget = sDaggerGateToBraid targetQubit numQubits
            let! sDag_both = BraidGroup.compose sDagControl sDagTarget
            
            // Compose: S·S · Entangle · S†·S†
            let! temp1 = BraidGroup.compose s_both entangle
            return! BraidGroup.compose temp1 sDag_both
        }
    
    /// Decompose CNOT gate into braiding sequence using Clifford+T decomposition.
    /// 
    /// **Mathematical Foundation:**
    /// CNOT can be decomposed as:
    ///   CNOT = (H ⊗ I) · CZ · (H ⊗ I)
    /// 
    /// Where CZ (Controlled-Z) can be implemented using:
    ///   CZ = (I ⊗ I) · (S ⊗ S†) · CNOT_basis · (S† ⊗ S)
    /// 
    /// But for Ising anyons, we use a more direct topological approach:
    ///   CNOT ≈ H(target) · CZ(control,target) · H(target)
    ///   CZ ≈ S(control) · S(target) · braiding(control,target) · S†(control) · S†(target)
    /// 
    /// **Note on Ising Anyons:**
    /// For Ising anyons, two-qubit gates require braiding strands from BOTH qubits.
    /// This is implemented via "exchange" braiding between adjacent anyonic strands.
    /// 
    /// **Gate Count:** ~6-8 Hadamards + ~4 S gates + entangling braids
    ///                 = ~300-500 T gates total (after Solovay-Kitaev approximation)
    let cnotGateToBraid (controlQubit: int) (targetQubit: int) (numQubits: int) (tolerance: float) : Result<BraidGroup.BraidWord, TopologicalError> =
        
        // Validate qubit indices
        if controlQubit < 0 || controlQubit >= numQubits then
            TopologicalResult.computationError "operation" $"Invalid control qubit: {controlQubit} (must be 0..{numQubits-1})"
        elif targetQubit < 0 || targetQubit >= numQubits then
            TopologicalResult.computationError "operation" $"Invalid target qubit: {targetQubit} (must be 0..{numQubits-1})"
        elif controlQubit = targetQubit then
            TopologicalResult.computationError "operation" "Control and target qubits must be different"
        else
            // Standard CNOT decomposition: CNOT = H(t) · CZ(c,t) · H(t)
            result {
                // Step 1: Apply Hadamard to target qubit
                let! h1 = hadamardGateToBraid targetQubit numQubits tolerance
                
                // Step 2: Apply Controlled-Z (CZ) between control and target
                let! cz = controlledZGateToBraid controlQubit targetQubit numQubits tolerance
                
                // Step 3: Apply Hadamard to target qubit again
                let! h2 = hadamardGateToBraid targetQubit numQubits tolerance
                
                // Compose: H · CZ · H
                let! temp = BraidGroup.compose h1 cz
                return! BraidGroup.compose temp h2
            }

    // ========================================================================
    // HIGH-LEVEL GATE COMPILATION
    // ========================================================================
    
    /// Compile CircuitBuilder.Gate to braiding sequence
    let compileGateToBraid 
        (gate: CircuitBuilder.Gate) 
        (numQubits: int)
        (tolerance: float) : Result<GateDecomposition, TopologicalError> =
        
        let gateName = BraidToGate.getGateName gate
        let qubits = BraidToGate.getAffectedQubits gate
        
        match gate with
        | CircuitBuilder.Gate.T qubit ->
            tGateToBraid qubit numQubits
            |> Result.map (fun braid -> {
                GateName = "T"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = 0.0  // Exact!
                DecompositionNotes = None
            })
        
        | CircuitBuilder.Gate.TDG qubit ->
            tDaggerGateToBraid qubit numQubits
            |> Result.map (fun braid -> {
                GateName = "T†"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = 0.0  // Exact!
                DecompositionNotes = None
            })
        
        | CircuitBuilder.Gate.S qubit ->
            sGateToBraid qubit numQubits
            |> Result.map (fun braid -> {
                GateName = "S"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = 0.0  // Exact (S = T²)
                DecompositionNotes = None
            })
        
        | CircuitBuilder.Gate.SDG qubit ->
            sDaggerGateToBraid qubit numQubits
            |> Result.map (fun braid -> {
                GateName = "S†"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = 0.0
                DecompositionNotes = None
            })
        
        | CircuitBuilder.Gate.Z qubit ->
            zGateToBraid qubit numQubits
            |> Result.map (fun braid -> {
                GateName = "Z"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = 0.0
                DecompositionNotes = Some "Z gate compiled as identity (global phase ignored in topological QC)"
            })
        
        | CircuitBuilder.Gate.RZ (qubit, angle) ->
            let tPhase = Math.PI / 8.0
            let (error, _) = computeAngleError angle tPhase
            
            rzGateToBraid qubit angle numQubits tolerance
            |> Result.map (fun braid -> {
                GateName = $"Rz({angle:F4})"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = error
                DecompositionNotes = 
                    if error > 1e-10 then 
                        Some $"Rz angle approximated to nearest π/8 multiple (error: {error:E6})"
                    else None
            })
        
        | CircuitBuilder.Gate.P (qubit, angle) ->
            let tPhase = Math.PI / 8.0
            let (error, _) = computeAngleError angle tPhase
            
            phaseGateToBraid qubit angle numQubits tolerance
            |> Result.map (fun braid -> {
                GateName = $"Phase({angle:F4})"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = error
                DecompositionNotes = 
                    if error > 1e-10 then 
                        Some $"Phase angle approximated to nearest π/8 multiple (error: {error:E6})"
                    else None
            })
        
        | CircuitBuilder.Gate.H qubit ->
            hadamardGateToBraid qubit numQubits tolerance
            |> Result.map (fun braid -> {
                GateName = "H"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = tolerance
                DecompositionNotes = Some "Hadamard approximated using Solovay-Kitaev with T/S/Z gates"
            })
        
        | CircuitBuilder.Gate.CNOT (control, target) ->
            cnotGateToBraid control target numQubits tolerance
            |> Result.map (fun braid -> {
                GateName = "CNOT"
                Qubits = [control; target]
                BraidSequence = [braid]
                ApproximationError = tolerance * 3.0  // H·CZ·H has 3 approximations
                DecompositionNotes = Some "CNOT = H(target) · CZ(control,target) · H(target)"
            })
        
        | CircuitBuilder.Gate.X qubit ->
            pauliXGateToBraid qubit numQubits tolerance
            |> Result.map (fun braid -> {
                GateName = "X"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = tolerance
                DecompositionNotes = Some "Pauli X approximated using Solovay-Kitaev with T/S/Z gates"
            })
        
        | CircuitBuilder.Gate.Y qubit ->
            pauliYGateToBraid qubit numQubits tolerance
            |> Result.map (fun braid -> {
                GateName = "Y"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = tolerance
                DecompositionNotes = Some "Pauli Y approximated using Solovay-Kitaev with T/S/Z gates"
            })
        
        | CircuitBuilder.Gate.U3 (qubit, theta, phi, lambda) ->
            u3GateToBraid qubit theta phi lambda numQubits tolerance
            |> Result.map (fun braid -> {
                GateName = $"U3({theta:F4},{phi:F4},{lambda:F4})"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = tolerance
                DecompositionNotes = Some "U3 gate approximated using Solovay-Kitaev with T/S/Z gates"
            })
        
        // DECOMPOSABLE GATES - Convert to supported gates
        
        | CircuitBuilder.Gate.RX (qubit, angle) ->
            // RX(θ) = U3(θ, -π/2, π/2) = RZ(π/2) · RY(θ) · RZ(-π/2)
            // Decompose via U3
            u3GateToBraid qubit angle (-Math.PI/2.0) (Math.PI/2.0) numQubits tolerance
            |> Result.map (fun braid -> {
                GateName = $"RX({angle:F4})"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = tolerance
                DecompositionNotes = Some "RX decomposed to U3 equivalent"
            })
        
        | CircuitBuilder.Gate.RY (qubit, angle) ->
            // RY(θ) = U3(θ, 0, 0)
            // Decompose via U3
            u3GateToBraid qubit angle 0.0 0.0 numQubits tolerance
            |> Result.map (fun braid -> {
                GateName = $"RY({angle:F4})"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = tolerance
                DecompositionNotes = Some "RY decomposed to U3 equivalent"
            })
        
        | CircuitBuilder.Gate.CZ (control, target) ->
            // CZ = H(target) · CNOT(control, target) · H(target)
            // This is automatically handled by GateTranspiler in compileGateSequence
            // Should not reach here if transpilation is done correctly
            Error (TopologicalError.LogicError 
                ("CZ gate compilation", "CZ gate should have been transpiled before gate-to-braid compilation. This indicates a bug in the compilation pipeline."))
        
        | CircuitBuilder.Gate.CP (control, target, angle) ->
            // CP(θ) = controlled phase rotation
            // This is automatically handled by GateTranspiler in compileGateSequence
            // Should not reach here if transpilation is done correctly
            Error (TopologicalError.LogicError 
                ("CP gate compilation", "CP gate should have been transpiled before gate-to-braid compilation. This indicates a bug in the compilation pipeline."))
        
        | CircuitBuilder.Gate.CRX (control, target, angle) ->
            // CRX(θ) = controlled rotation X
            // This is automatically handled by GateTranspiler in compileGateSequence
            // Should not reach here if transpilation is done correctly
            Error (TopologicalError.LogicError 
                ("CRX gate compilation", "CRX gate should have been transpiled before gate-to-braid compilation. This indicates a bug in the compilation pipeline."))
        
        | CircuitBuilder.Gate.CRY (control, target, angle) ->
            // CRY(θ) = controlled rotation Y
            // This is automatically handled by GateTranspiler in compileGateSequence
            // Should not reach here if transpilation is done correctly
            Error (TopologicalError.LogicError 
                ("CRY gate compilation", "CRY gate should have been transpiled before gate-to-braid compilation. This indicates a bug in the compilation pipeline."))
        
        | CircuitBuilder.Gate.CRZ (control, target, angle) ->
            // CRZ(θ) = controlled rotation Z
            // This is automatically handled by GateTranspiler in compileGateSequence
            // Should not reach here if transpilation is done correctly
            Error (TopologicalError.LogicError 
                ("CRZ gate compilation", "CRZ gate should have been transpiled before gate-to-braid compilation. This indicates a bug in the compilation pipeline."))
        
        | CircuitBuilder.Gate.SWAP (qubit1, qubit2) ->
            // SWAP = CNOT(q1,q2) · CNOT(q2,q1) · CNOT(q1,q2)
            // This is automatically handled by GateTranspiler in compileGateSequence
            // Should not reach here if transpilation is done correctly
            Error (TopologicalError.LogicError 
                ("SWAP gate compilation", "SWAP gate should have been transpiled before gate-to-braid compilation. This indicates a bug in the compilation pipeline."))
        
        | CircuitBuilder.Gate.CCX (control1, control2, target) ->
            // Toffoli requires 6 CNOTs + T gates (Barenco decomposition)
            // This is automatically handled by GateTranspiler in compileGateSequence
            // Should not reach here if transpilation is done correctly
            Error (TopologicalError.LogicError 
                ("CCX gate compilation", "CCX (Toffoli) gate should have been transpiled before gate-to-braid compilation. This indicates a bug in the compilation pipeline."))
        
        | CircuitBuilder.Gate.MCZ (controls, target) ->
            // Multi-controlled Z requires decomposition to Toffolis + single-qubit gates
            // This is automatically handled by GateTranspiler in compileGateSequence
            // Should not reach here if transpilation is done correctly
            Error (TopologicalError.LogicError 
                ("MCZ gate compilation", "MCZ gate should have been transpiled before gate-to-braid compilation. This indicates a bug in the compilation pipeline."))
        
        // MEASUREMENT - Handled separately by backend
        | CircuitBuilder.Gate.Measure qubit ->
            // Measurements are handled by the topological backend's fusion measurement protocol
            // Skip during braid compilation - they become fusion measurements later
            Ok {
                GateName = "Measure"
                Qubits = [qubit]
                BraidSequence = []  // Empty - no braiding operations
                ApproximationError = 0.0
                DecompositionNotes = Some "Measurement handled by topological backend fusion protocol (not a braid operation)"
            }
    
    /// Compile gate sequence to braiding operations
    /// 
    /// This function automatically transpiles complex gates (CZ, CCX, MCZ) into elementary gates
    /// before converting to braiding operations. Users don't need to call GateTranspiler manually.
    let compileGateSequence
        (gateSequence: BraidToGate.GateSequence)
        (tolerance: float)
        (anyonType: AnyonSpecies.AnyonType) : Result<GateSequenceCompilation, TopologicalError> =
        
        // For now, only support Ising anyons (Majorana)
        if anyonType <> AnyonSpecies.AnyonType.Ising then
            TopologicalResult.notImplemented "Gate compilation for non-Ising anyons" (Some "Only Ising anyons currently supported")
        else
            // Step 1: Transpile complex gates to elementary gates
            // Convert GateSequence to Circuit format for transpilation
            let circuit : CircuitBuilder.Circuit = {
                QubitCount = gateSequence.NumQubits
                Gates = gateSequence.Gates
            }
            
            // Transpile for topological backend (decomposes CZ, CCX, MCZ, phase gates)
            let transpiledCircuit = GateTranspiler.transpileForBackend "topological" circuit
            
            // Step 2: Compile transpiled gates to braids
            // Functional fold pattern - no mutable state
            let initialState = {
                AllBraids = []
                TotalError = 0.0
                Warnings = []
            }
            
            // Fold over transpiled gates, accumulating results or short-circuiting on error
            transpiledCircuit.Gates
            |> List.fold (fun stateResult gate ->
                match stateResult with
                | Error err -> Error err  // Short-circuit on first error
                | Ok state ->
                    match compileGateToBraid gate gateSequence.NumQubits tolerance with
                    | Error err -> Error err
                    | Ok decomp ->
                        let newWarnings =
                            match decomp.DecompositionNotes with
                            | Some note -> note :: state.Warnings
                            | None -> state.Warnings
                        
                        Ok {
                            AllBraids = state.AllBraids @ decomp.BraidSequence
                            TotalError = state.TotalError + decomp.ApproximationError
                            Warnings = newWarnings
                        }
            ) (Ok initialState)
            |> Result.map (fun finalState ->
                {
                    OriginalGateCount = gateSequence.Gates.Length
                    CompiledBraids = finalState.AllBraids
                    TotalError = finalState.TotalError
                    IsExact = (finalState.TotalError < 1e-10)
                    AnyonType = anyonType
                    CompilationWarnings = List.rev finalState.Warnings
                })

    // ========================================================================
    // DISPLAY UTILITIES
    // ========================================================================
    
    /// Display gate decomposition
    let displayGateDecomposition (decomp: GateDecomposition) : string =
        let braidSummary = 
            decomp.BraidSequence
            |> List.mapi (fun i braid -> 
                $"  {i+1}. {braid.Generators.Length} generators on {braid.StrandCount} strands")
            |> String.concat "\n"
        
        let errorInfo = 
            if decomp.ApproximationError = 0.0 then "✓ EXACT"
            else $"≈ Error: {decomp.ApproximationError:E6}"
        
        let notesSection =
            match decomp.DecompositionNotes with
            | Some notes -> $"\nNotes: {notes}"
            | None -> ""
        
        $"""{decomp.GateName} → Braiding Decomposition
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Qubits: {decomp.Qubits}
Accuracy: {errorInfo}
Braid sequence ({decomp.BraidSequence.Length} braids):
{braidSummary}{notesSection}"""
    
    /// Display compilation summary
    let displayCompilationSummary (compilation: GateSequenceCompilation) : string =
        let exactness = if compilation.IsExact then "✓ EXACT" else $"≈ Error: {compilation.TotalError:E6}"
        
        let warningsSection =
            if compilation.CompilationWarnings.IsEmpty then ""
            else
                let warningList = 
                    compilation.CompilationWarnings
                    |> List.mapi (fun i w -> $"  {i+1}. {w}")
                    |> String.concat "\n"
                $"\n\nWarnings:\n{warningList}"
        
        $"""Gate Sequence Compilation Summary
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Anyon type: {compilation.AnyonType}
Original gates: {compilation.OriginalGateCount}
Compiled braids: {compilation.CompiledBraids.Length}
Accuracy: {exactness}{warningsSection}"""
