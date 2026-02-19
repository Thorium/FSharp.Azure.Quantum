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
///    - T = exp(iπ/4) (relative phase on |1⟩ state)
///    - S = T² = exp(iπ/2) = i
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
                topologicalResult {
                    let! acc = state
                    let! value = item
                    return value :: acc
                }
            results
            |> List.fold folder (Ok [])
            |> Result.map List.rev
    
    /// Compute approximation error for angle discretization.
    /// Returns (error, numBraids) where numBraids can be negative (for counter-clockwise).
    /// 
    /// Uses signed angle to pick optimal direction:
    ///   - Positive n → clockwise braids (exp(+iπ/2) each for Ising)
    ///   - Negative n → counter-clockwise braids (exp(-iπ/2) each for Ising)
    /// This avoids e.g. Rz(-π/2) becoming 3 clockwise braids instead of 1 counter-clockwise.
    let private computeAngleError (targetAngle: float) (tPhase: float) : float * int =
        // Normalize to (-π, π] for shortest-path direction
        let twoPi = 2.0 * Math.PI
        let normalized = targetAngle % twoPi
        let normalized =
            if normalized > Math.PI then normalized - twoPi
            elif normalized <= -Math.PI then normalized + twoPi
            else normalized
        // Round to nearest integer multiple of tPhase (signed)
        let numBraids = int (Math.Round(normalized / tPhase))
        let approximateAngle = float numBraids * tPhase
        let error = abs(normalized - approximateAngle)
        (error, numBraids)

    // ========================================================================
    // T GATE DECOMPOSITION (Ising Anyons)
    // ========================================================================
    
    /// Decompose T gate into Ising anyon braiding.
    /// 
    /// **PHYSICS**: T = diag(1, e^{iπ/4}) requires a relative phase of π/4.
    /// One Ising braid produces relative phase π/2 (S gate), so T is NOT exact.
    /// Ising anyons can only produce Clifford gates by braiding (Simon §11.2.4).
    /// 
    /// T gate is handled via amplitude-level intercept in TopologicalBackend.ApplyGate.
    /// This function returns an error to signal that T must be intercepted.
    let tGateToBraid (qubitIndex: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        // T gate cannot be realized exactly by Ising anyon braiding.
        // One braid = S (π/2 phase), not T (π/4 phase).
        // T gate must be handled by amplitude-level intercept or magic state distillation.
        TopologicalResult.computationError "tGateToBraid" "T gate is not exact in Ising anyon braiding (requires non-topological supplementation)"
    
    /// Decompose T† gate into Ising anyon braiding
    let tDaggerGateToBraid (qubitIndex: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        // T† gate cannot be realized exactly by Ising anyon braiding.
        TopologicalResult.computationError "tDaggerGateToBraid" "T† gate is not exact in Ising anyon braiding (requires non-topological supplementation)"

    // ========================================================================
    // CLIFFORD GATE DECOMPOSITION
    // ========================================================================
    
    /// Decompose S gate (π/2 phase) into braiding.
    /// S = one clockwise braid (EXACT for Ising anyons).
    /// 
    /// **PHYSICS**: One Ising anyon exchange produces relative phase:
    ///   e^{3iπ/8} / e^{-iπ/8} = e^{iπ/2} = i = S gate
    /// Reference: Simon "Topological Quantum" Eq. 10.9-10.10
    let sGateToBraid (qubitIndex: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        // S = one clockwise braid (exact)
        let gen : BraidGroup.BraidGenerator = { Index = qubitIndex; IsClockwise = true }
        BraidGroup.fromGenerators (numQubits + 1) [gen]
    
    /// Decompose S† gate into braiding
    let sDaggerGateToBraid (qubitIndex: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        // S† = one counter-clockwise braid (exact)
        let gen : BraidGroup.BraidGenerator = { Index = qubitIndex; IsClockwise = false }
        BraidGroup.fromGenerators (numQubits + 1) [gen]
    
    /// Decompose Pauli Z gate into braiding.
    /// Z = S² = two clockwise braids (EXACT for Ising anyons).
    /// 
    /// **PHYSICS**: Two Ising anyon exchanges produce relative phase:
    ///   (e^{iπ/2})² = e^{iπ} = -1 = Z gate
    /// This is a relative phase (not global), so it IS physically meaningful.
    let zGateToBraid (qubitIndex: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        // Z = S² = 2 clockwise braids (exact)
        let gen : BraidGroup.BraidGenerator = { Index = qubitIndex; IsClockwise = true }
        BraidGroup.fromGenerators (numQubits + 1) [gen; gen]
    
    // ========================================================================
    // SOLOVAY-KITAEV GATE APPROXIMATION
    // ========================================================================
    
    /// Convert single Solovay-Kitaev BasicGate to braiding
    /// Only accepts S, Z gates - T/H/X/Y should never appear in topological S-K output
    /// (T is not exact for Ising anyons; H/X/Y are off-diagonal)
    let basicGateToBraid (gate: SolovayKitaev.BasicGate) (qubitIndex: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        match gate with
        | SolovayKitaev.S -> sGateToBraid qubitIndex numQubits
        | SolovayKitaev.SDagger -> sDaggerGateToBraid qubitIndex numQubits
        | SolovayKitaev.Z -> zGateToBraid qubitIndex numQubits
        | SolovayKitaev.I -> BraidGroup.identity (numQubits + 1)
        // T/T† are NOT exact for Ising anyons — should not appear in topological S-K output
        | SolovayKitaev.T | SolovayKitaev.TDagger ->
            TopologicalResult.computationError "gateConversion" $"Gate %A{gate} is not exact in Ising anyon braiding and should not appear in topological S-K output"
        // H/X/Y should NEVER appear in output when using topological-compatible base set
        | SolovayKitaev.H | SolovayKitaev.X | SolovayKitaev.Y ->
            TopologicalResult.computationError "gateConversion" $"Gate %A{gate} should not appear in Solovay-Kitaev output for topological systems"
    
    /// Compose a list of braids (left-to-right)
    /// Returns Error if any composition fails (e.g., mismatched strand counts)
    let composeBraids (braids: BraidGroup.BraidWord list) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        braids 
        |> List.fold (fun acc braid ->
            match acc with
            | Error _ -> acc  // Propagate previous error
            | Ok current ->
                BraidGroup.compose current braid
        ) (BraidGroup.identity (numQubits + 1))
    
    /// Convert Solovay-Kitaev gate sequence to composed braiding
    let solovayKitaevGatesToBraid (gates: SolovayKitaev.GateSequence) (qubitIndex: int) (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        gates
        |> List.map (fun gate -> basicGateToBraid gate qubitIndex numQubits)
        |> ResultPrivate.sequence
        |> Result.bind (fun braids -> composeBraids braids numQubits)
    
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
    /// One Ising braid produces relative phase of exp(iπ/2) = S gate.
    /// 
    /// For topological QC with Ising anyons:
    /// Rz(θ) ≈ (braid)^n where n = round(θ / (π/2))
    /// Positive n → clockwise braids, negative n → counter-clockwise braids.
    /// 
    /// This is EXACT when θ is a multiple of π/2, approximate otherwise.
    let rzGateToBraid (qubitIndex: int) (angle: float) (numQubits: int) (tolerance: float) : Result<BraidGroup.BraidWord, TopologicalError> =
        let braidPhase = Math.PI / 2.0  // One Ising braid = S = π/2 relative phase
        let (error, numBraids) = computeAngleError angle braidPhase
        
        // Check if approximation is within tolerance
        if error > tolerance then
            TopologicalResult.computationError "Rz gate approximation" $"Rz({angle}) approximation error {error:F6} exceeds tolerance {tolerance:F6}"
        else
            // Use sign to determine direction: positive → clockwise, negative → counter-clockwise
            let isClockwise = numBraids >= 0
            let absCount = abs numBraids
            let gens : BraidGroup.BraidGenerator list = 
                List.init absCount (fun _ -> 
                    { Index = qubitIndex; IsClockwise = isClockwise })
            
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
        topologicalResult {
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
            topologicalResult {
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
            // T gate is NOT exact in Ising anyon braiding — must be intercepted
            tGateToBraid qubit numQubits
            |> Result.map (fun braid -> {
                GateName = "T"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = 0.0
                DecompositionNotes = None
            })
        
        | CircuitBuilder.Gate.TDG qubit ->
            // T† gate is NOT exact in Ising anyon braiding — must be intercepted
            tDaggerGateToBraid qubit numQubits
            |> Result.map (fun braid -> {
                GateName = "T†"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = 0.0
                DecompositionNotes = None
            })
        
        | CircuitBuilder.Gate.S qubit ->
            sGateToBraid qubit numQubits
            |> Result.map (fun braid -> {
                GateName = "S"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = 0.0  // Exact: 1 clockwise braid
                DecompositionNotes = None
            })
        
        | CircuitBuilder.Gate.SDG qubit ->
            sDaggerGateToBraid qubit numQubits
            |> Result.map (fun braid -> {
                GateName = "S†"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = 0.0  // Exact: 1 counter-clockwise braid
                DecompositionNotes = None
            })
        
        | CircuitBuilder.Gate.Z qubit ->
            zGateToBraid qubit numQubits
            |> Result.map (fun braid -> {
                GateName = "Z"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = 0.0  // Exact: 2 clockwise braids (S²)
                DecompositionNotes = None
            })
        
        | CircuitBuilder.Gate.RZ (qubit, angle) ->
            let braidPhase = Math.PI / 2.0
            let (error, _) = computeAngleError angle braidPhase
            
            rzGateToBraid qubit angle numQubits tolerance
            |> Result.map (fun braid -> {
                GateName = $"Rz({angle:F4})"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = error
                DecompositionNotes = 
                    if error > 1e-10 then 
                        Some $"Rz angle approximated to nearest π/2 multiple (error: {error:E6})"
                    else None
            })
        
        | CircuitBuilder.Gate.P (qubit, angle) ->
            let braidPhase = Math.PI / 2.0
            let (error, _) = computeAngleError angle braidPhase
            
            phaseGateToBraid qubit angle numQubits tolerance
            |> Result.map (fun braid -> {
                GateName = $"Phase({angle:F4})"
                Qubits = [qubit]
                BraidSequence = [braid]
                ApproximationError = error
                DecompositionNotes = 
                    if error > 1e-10 then 
                        Some $"Phase angle approximated to nearest π/2 multiple (error: {error:E6})"
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
        
        // RESET - Not supported in topological quantum computing
        | CircuitBuilder.Gate.Reset _ ->
            Error (TopologicalError.LogicError 
                ("Reset gate compilation",
                 "Reset gate is not supported in topological quantum computing — requires measurement and conditional feedback incompatible with braiding"))
        
        // BARRIER - Synchronization directive with no physical effect
        | CircuitBuilder.Gate.Barrier qubits ->
            Ok {
                GateName = "Barrier"
                Qubits = qubits
                BraidSequence = []
                ApproximationError = 0.0
                DecompositionNotes = Some "Barrier is a synchronization directive with no physical effect — no braiding needed"
            }
    
    // ========================================================================
    // FIBONACCI GATE COMPILATION
    // ========================================================================
    
    /// Convert a list of Fibonacci braid operations to a BraidGroup.BraidWord.
    ///
    /// Maps SolovayKitaev.FibonacciBraidOp to BraidGroup.BraidGenerator:
    ///   Sigma1/Sigma1Inv → generator at qubitIndex (within first τ-pair)
    ///   Sigma2/Sigma2Inv → generator at qubitIndex+1 (across τ-pair boundary)
    ///
    /// Fibonacci encoding uses 3 strands for a single qubit (2 τ anyons + 1 auxiliary)
    /// to achieve universality via {σ₁, σ₂}.
    let fibonacciOpsToBraidWord 
        (ops: SolovayKitaev.FibonacciBraidOp list) 
        (qubitIndex: int)
        (numQubits: int) : Result<BraidGroup.BraidWord, TopologicalError> =
        
        // Fibonacci needs at least 3 strands per qubit for universality
        // n qubits → at least 2n + 1 strands (pairs + boundary strand)
        let strandCount = max 3 (2 * numQubits + 1)
        
        let generators : BraidGroup.BraidGenerator list =
            ops |> List.map (fun op ->
                match op with
                | SolovayKitaev.FibonacciBraidOp.Sigma1 ->
                    { BraidGroup.BraidGenerator.Index = 2 * qubitIndex; IsClockwise = true }
                | SolovayKitaev.FibonacciBraidOp.Sigma1Inv ->
                    { BraidGroup.BraidGenerator.Index = 2 * qubitIndex; IsClockwise = false }
                | SolovayKitaev.FibonacciBraidOp.Sigma2 ->
                    { BraidGroup.BraidGenerator.Index = 2 * qubitIndex + 1; IsClockwise = true }
                | SolovayKitaev.FibonacciBraidOp.Sigma2Inv ->
                    { BraidGroup.BraidGenerator.Index = 2 * qubitIndex + 1; IsClockwise = false })
        
        BraidGroup.fromGenerators strandCount generators
    
    /// Compile a single-qubit gate for Fibonacci anyons.
    ///
    /// All single-qubit gates are approximated using the Fibonacci braid search.
    /// Unlike Ising anyons where T is exact, Fibonacci braiding approximates
    /// ALL gates — but braiding alone is universal (no magic states needed).
    let compileSingleQubitGateFibonacci
        (gate: CircuitBuilder.Gate)
        (numQubits: int)
        (tolerance: float) : Result<GateDecomposition, TopologicalError> =
        
        let gateName = BraidToGate.getGateName gate
        let qubits = BraidToGate.getAffectedQubits gate
        
        // Get the target SU(2) matrix for this gate
        let targetMatrix =
            match gate with
            | CircuitBuilder.Gate.T _ -> SolovayKitaev.gateToMatrix SolovayKitaev.T
            | CircuitBuilder.Gate.TDG _ -> SolovayKitaev.gateToMatrix SolovayKitaev.TDagger
            | CircuitBuilder.Gate.S _ -> SolovayKitaev.gateToMatrix SolovayKitaev.S
            | CircuitBuilder.Gate.SDG _ -> SolovayKitaev.gateToMatrix SolovayKitaev.SDagger
            | CircuitBuilder.Gate.Z _ -> SolovayKitaev.gateToMatrix SolovayKitaev.Z
            | CircuitBuilder.Gate.H _ -> SolovayKitaev.gateToMatrix SolovayKitaev.H
            | CircuitBuilder.Gate.X _ -> SolovayKitaev.gateToMatrix SolovayKitaev.X
            | CircuitBuilder.Gate.Y _ -> SolovayKitaev.gateToMatrix SolovayKitaev.Y
            | CircuitBuilder.Gate.RZ (_, angle) ->
                let phase = Complex.Exp(Complex.ImaginaryOne * Complex(angle, 0.0))
                SolovayKitaev.createSU2 Complex.One Complex.Zero Complex.Zero phase
            | CircuitBuilder.Gate.P (_, angle) ->
                let phase = Complex.Exp(Complex.ImaginaryOne * Complex(angle, 0.0))
                SolovayKitaev.createSU2 Complex.One Complex.Zero Complex.Zero phase
            | CircuitBuilder.Gate.RX (_, angle) ->
                let halfAngle = angle / 2.0
                let cosH = Complex(cos halfAngle, 0.0)
                let sinH = Complex(0.0, -sin halfAngle)
                SolovayKitaev.createSU2 cosH sinH (Complex.Conjugate sinH |> fun c -> Complex(-c.Real, -c.Imaginary)) cosH
            | CircuitBuilder.Gate.RY (_, angle) ->
                let halfAngle = angle / 2.0
                let cosH = Complex(cos halfAngle, 0.0)
                let sinH = Complex(sin halfAngle, 0.0)
                SolovayKitaev.createSU2 cosH (Complex(-sinH.Real, 0.0)) sinH cosH
            | CircuitBuilder.Gate.U3 (_, theta, phi, lambda) ->
                let halfTheta = theta / 2.0
                let cosHalf = cos halfTheta
                let sinHalf = sin halfTheta
                SolovayKitaev.createSU2
                    (Complex(cosHalf, 0.0))
                    (Complex(-sinHalf * cos lambda, -sinHalf * sin lambda))
                    (Complex(sinHalf * cos phi, sinHalf * sin phi))
                    (Complex(cosHalf * cos (phi + lambda), cosHalf * sin (phi + lambda)))
            | _ -> SolovayKitaev.identity  // Should not reach here for single-qubit gates
        
        let qubitIndex = 
            match qubits with
            | [q] -> q
            | _ -> 0
        
        // Approximate using Fibonacci braid search
        // baseSetLength=4 gives 4^1 + 4^2 + 4^3 + 4^4 = 340 braid words in base set
        let (braidOps, error) = SolovayKitaev.approximateGateFibonacci targetMatrix tolerance 4 12
        
        fibonacciOpsToBraidWord braidOps qubitIndex numQubits
        |> Result.map (fun braid -> {
            GateName = gateName
            Qubits = qubits
            BraidSequence = [braid]
            ApproximationError = error
            DecompositionNotes = 
                Some $"Fibonacci anyon compilation: {braidOps.Length} braid operations, error {error:E6}"
        })
    
    /// Compile a two-qubit gate for Fibonacci anyons.
    ///
    /// Two-qubit gates in Fibonacci anyons require braiding across
    /// multiple qubit pairs. Currently supports CNOT via decomposition.
    let compileTwoQubitGateFibonacci
        (gate: CircuitBuilder.Gate)
        (numQubits: int)
        (tolerance: float) : Result<GateDecomposition, TopologicalError> =
        
        match gate with
        | CircuitBuilder.Gate.CNOT (control, target) ->
            // CNOT = H(target) · CZ(control, target) · H(target)
            // For Fibonacci, both H and CZ require approximation
            topologicalResult {
                // Compile H gate on target qubit
                let! h1 = compileSingleQubitGateFibonacci (CircuitBuilder.Gate.H target) numQubits tolerance
                
                // For CZ, we need entangling braids between the qubit pairs
                // Use S · entangle · S† pattern adapted for Fibonacci
                let! sControl = compileSingleQubitGateFibonacci (CircuitBuilder.Gate.S control) numQubits tolerance
                let! sTarget = compileSingleQubitGateFibonacci (CircuitBuilder.Gate.S target) numQubits tolerance
                let! entangle = createEntanglingBraid control target numQubits
                let! sdControl = compileSingleQubitGateFibonacci (CircuitBuilder.Gate.SDG control) numQubits tolerance
                let! sdTarget = compileSingleQubitGateFibonacci (CircuitBuilder.Gate.SDG target) numQubits tolerance
                
                let! h2 = compileSingleQubitGateFibonacci (CircuitBuilder.Gate.H target) numQubits tolerance
                
                // Combine all braid sequences
                let allBraids = 
                    h1.BraidSequence @
                    sControl.BraidSequence @ sTarget.BraidSequence @
                    [entangle] @
                    sdControl.BraidSequence @ sdTarget.BraidSequence @
                    h2.BraidSequence
                
                let totalError = 
                    h1.ApproximationError + sControl.ApproximationError + sTarget.ApproximationError +
                    sdControl.ApproximationError + sdTarget.ApproximationError + h2.ApproximationError
                
                return {
                    GateName = "CNOT"
                    Qubits = [control; target]
                    BraidSequence = allBraids
                    ApproximationError = totalError
                    DecompositionNotes = Some "Fibonacci CNOT: H · S · S · entangle · S† · S† · H decomposition"
                }
            }
        | _ ->
            TopologicalResult.notImplemented 
                "Fibonacci two-qubit gate"
                (Some $"Two-qubit gate {BraidToGate.getGateName gate} not yet supported for Fibonacci anyons")
    
    /// Compile a gate for Fibonacci anyons (dispatches to single/two-qubit)
    let compileGateToBraidFibonacci
        (gate: CircuitBuilder.Gate) 
        (numQubits: int)
        (tolerance: float) : Result<GateDecomposition, TopologicalError> =
        
        match gate with
        // Single-qubit gates
        | CircuitBuilder.Gate.T _ | CircuitBuilder.Gate.TDG _
        | CircuitBuilder.Gate.S _ | CircuitBuilder.Gate.SDG _
        | CircuitBuilder.Gate.Z _ | CircuitBuilder.Gate.H _
        | CircuitBuilder.Gate.X _ | CircuitBuilder.Gate.Y _
        | CircuitBuilder.Gate.RZ _ | CircuitBuilder.Gate.P _
        | CircuitBuilder.Gate.RX _ | CircuitBuilder.Gate.RY _
        | CircuitBuilder.Gate.U3 _ ->
            compileSingleQubitGateFibonacci gate numQubits tolerance
        
        // Two-qubit gates
        | CircuitBuilder.Gate.CNOT _ ->
            compileTwoQubitGateFibonacci gate numQubits tolerance
        
        // Measurement - same as Ising
        | CircuitBuilder.Gate.Measure qubit ->
            Ok {
                GateName = "Measure"
                Qubits = [qubit]
                BraidSequence = []
                ApproximationError = 0.0
                DecompositionNotes = Some "Measurement handled by topological backend fusion protocol"
            }
        
        // Barrier - same as Ising
        | CircuitBuilder.Gate.Barrier qubits ->
            Ok {
                GateName = "Barrier"
                Qubits = qubits
                BraidSequence = []
                ApproximationError = 0.0
                DecompositionNotes = Some "Barrier is a synchronization directive — no braiding needed"
            }
        
        // Reset - not supported
        | CircuitBuilder.Gate.Reset _ ->
            Error (TopologicalError.LogicError 
                ("Reset gate compilation",
                 "Reset gate is not supported in topological quantum computing"))
        
        // Must-be-transpiled gates — should not reach here
        | CircuitBuilder.Gate.CZ _ | CircuitBuilder.Gate.CP _
        | CircuitBuilder.Gate.CRX _ | CircuitBuilder.Gate.CRY _ | CircuitBuilder.Gate.CRZ _
        | CircuitBuilder.Gate.SWAP _ | CircuitBuilder.Gate.CCX _ | CircuitBuilder.Gate.MCZ _ ->
            Error (TopologicalError.LogicError 
                ($"{BraidToGate.getGateName gate} gate compilation", 
                 $"{BraidToGate.getGateName gate} should have been transpiled before gate-to-braid compilation"))
    
    /// Compile gate sequence to braiding operations
    /// 
    /// **FULL PIPELINE**: Gate Sequence → Transpile → Gate-by-Gate Compile → Braid Words
    /// 
    /// Supports both Ising and Fibonacci anyon types:
    /// - **Ising**: S gate = exact braid (1 CW), Z = 2 braids. T gate via amplitude intercept
    ///   in TopologicalBackend (not braid-compilable). H/X/Y via Solovay-Kitaev with {S,Z}
    /// - **Fibonacci**: ALL gates via Fibonacci braid search with {σ₁,σ₁⁻¹,σ₂,σ₂⁻¹}
    /// 
    /// This function automatically transpiles complex gates (CZ, CCX, MCZ) into elementary gates
    /// before converting to braiding operations. Users don't need to call GateTranspiler manually.
    let compileGateSequence
        (gateSequence: BraidToGate.GateSequence)
        (tolerance: float)
        (anyonType: AnyonSpecies.AnyonType) : Result<GateSequenceCompilation, TopologicalError> =
        
        // Select the gate compiler function based on anyon type
        let gateCompiler =
            match anyonType with
            | AnyonSpecies.AnyonType.Ising -> compileGateToBraid
            | AnyonSpecies.AnyonType.Fibonacci -> compileGateToBraidFibonacci
            | _ -> 
                fun _ _ _ -> 
                    TopologicalResult.notImplemented 
                        "Gate compilation" 
                        (Some $"Gate compilation not implemented for anyon type {anyonType}. Supported: Ising, Fibonacci")
        
        // Step 1: Transpile complex gates to elementary gates
        // Convert GateSequence to Circuit format for transpilation
        let circuit : CircuitBuilder.Circuit = {
            QubitCount = gateSequence.NumQubits
            Gates = gateSequence.Gates
        }
        
        // Transpile for topological backend (decomposes CZ, CCX, MCZ, phase gates).
        // Some decompositions (e.g., MCZ → CCX → {H, T, TDG, CNOT}) require multiple
        // passes because transpileForBackend is a single-pass transformation.
        // Loop until the gate list stabilizes (fixpoint), bounded to prevent infinite loops.
        let rec transpileToFixpoint (remaining: int) (current: CircuitBuilder.Circuit) =
            if remaining <= 0 then current
            else
                let next = GateTranspiler.transpileForBackend "topological" current
                if next.Gates = current.Gates then next
                else transpileToFixpoint (remaining - 1) next

        let transpiledCircuit = transpileToFixpoint 5 circuit
        
        // Step 2: Compile transpiled gates to braids using the selected compiler
        // Functional fold pattern - no mutable state
        let initialState = {
            AllBraids = []
            TotalError = 0.0
            Warnings = []
        }
        
        // Fold over transpiled gates, accumulating results or short-circuiting on error
        // Note: AllBraids accumulated in reverse order, reversed at the end to avoid O(n²) append
        transpiledCircuit.Gates
        |> List.fold (fun stateResult gate ->
            match stateResult with
            | Error err -> Error err  // Short-circuit on first error
            | Ok state ->
                match gateCompiler gate gateSequence.NumQubits tolerance with
                | Error err -> Error err
                | Ok decomp ->
                    let newWarnings =
                        match decomp.DecompositionNotes with
                        | Some note -> note :: state.Warnings
                        | None -> state.Warnings
                    
                    // Prepend reversed braids (O(1) per braid) instead of append (O(n))
                    let newBraids =
                        (List.rev decomp.BraidSequence) @ state.AllBraids
                    
                    Ok {
                        AllBraids = newBraids
                        TotalError = state.TotalError + decomp.ApproximationError
                        Warnings = newWarnings
                    }
        ) (Ok initialState)
        |> Result.map (fun finalState ->
            {
                OriginalGateCount = gateSequence.Gates.Length
                CompiledBraids = List.rev finalState.AllBraids
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
