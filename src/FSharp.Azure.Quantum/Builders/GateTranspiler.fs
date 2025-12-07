namespace FSharp.Azure.Quantum

open System
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core

/// Gate Transpilation Module
/// 
/// Decomposes high-level quantum gates into backend-native gate sets.
/// Follows IBM Qiskit's transpilation approach for OpenQASM 2.0 compatibility.
/// 
/// Design Philosophy:
/// - Users write circuits with full OpenQASM 2.0 gate set
/// - Transpiler automatically decomposes gates for backend compatibility
/// - Decompositions follow standard quantum computing textbook algorithms
/// - Backend-aware: only decomposes gates not natively supported
/// 
/// Standard Decompositions:
/// - S, SDG, T, TDG → RZ rotations (universal phase gates)
/// - CZ → H + CNOT + H (for IonQ compatibility)
/// - CCX (Toffoli) → 6 CNOTs + T gates (Barenco decomposition)
module GateTranspiler =
    
    // ========================================================================
    // CONSTANTS (Mathematical Constants for Gate Decomposition)
    // ========================================================================
    
    /// π (pi)
    let private pi = Math.PI
    
    /// π/2 for S gate
    let private piOver2 = pi / 2.0
    
    /// π/4 for T gate
    let private piOver4 = pi / 4.0
    
    // ========================================================================
    // PHASE GATE DECOMPOSITIONS (S, SDG, T, TDG → RZ)
    // ========================================================================
    
    /// Decompose S gate into RZ(π/2)
    /// 
    /// S = [[1,  0],
    ///      [0,  i]] = RZ(π/2)
    /// 
    /// Effect: Adds π/2 phase to |1⟩ state
    let private decomposeS (qubit: int) : Gate list =
        [RZ (qubit, piOver2)]
    
    /// Decompose S-dagger gate into RZ(-π/2)
    /// 
    /// SDG = [[1,  0],
    ///        [0, -i]] = RZ(-π/2)
    /// 
    /// Effect: Adds -π/2 phase to |1⟩ state
    let private decomposeSDG (qubit: int) : Gate list =
        [RZ (qubit, -piOver2)]
    
    /// Decompose T gate into RZ(π/4)
    /// 
    /// T = [[1,  0],
    ///      [0,  e^(iπ/4)]] = RZ(π/4)
    /// 
    /// Effect: Adds π/4 phase to |1⟩ state
    let private decomposeT (qubit: int) : Gate list =
        [RZ (qubit, piOver4)]
    
    /// Decompose T-dagger gate into RZ(-π/4)
    /// 
    /// TDG = [[1,  0],
    ///        [0,  e^(-iπ/4)]] = RZ(-π/4)
    /// 
    /// Effect: Adds -π/4 phase to |1⟩ state
    let private decomposeTDG (qubit: int) : Gate list =
        [RZ (qubit, -piOver4)]
    
    // ========================================================================
    // TWO-QUBIT GATE DECOMPOSITIONS
    // ========================================================================
    
    /// Decompose CZ gate into H + CNOT + H
    /// 
    /// CZ = H₁ · CNOT · H₁
    /// 
    /// Proof:
    /// - CZ adds phase -1 when both qubits are |1⟩
    /// - H transforms Z basis to X basis
    /// - CNOT in X basis = CZ in Z basis
    /// 
    /// Circuit:
    ///   q0: ─────●─────
    ///            │
    ///   q1: ─H─┤ X ├─H─
    let private decomposeCZ (control: int) (target: int) : Gate list =
        [
            H target
            CNOT (control, target)
            H target
        ]
    
    /// Decompose CRX (Controlled-RX) gate into RX + CNOT gates
    /// 
    /// CRX(θ) = RX(θ/2) · CNOT · RX(-θ/2) · CNOT
    /// 
    /// Reference: Nielsen & Chuang, Section 4.3
    /// 
    /// Circuit:
    ///   c: ──────●──────────●─────
    ///            │          │
    ///   t: ─RX(θ/2)─┤ X ├─RX(-θ/2)─┤ X ├─
    let private decomposeCRX (control: int) (target: int) (angle: float) : Gate list =
        let halfAngle = angle / 2.0
        [
            RX (target, halfAngle)
            CNOT (control, target)
            RX (target, -halfAngle)
            CNOT (control, target)
        ]
    
    /// Decompose CRY (Controlled-RY) gate into RY + CNOT gates
    /// 
    /// CRY(θ) = RY(θ/2) · CNOT · RY(-θ/2) · CNOT
    /// 
    /// Reference: Nielsen & Chuang, Section 4.3
    /// 
    /// Circuit:
    ///   c: ──────●──────────●─────
    ///            │          │
    ///   t: ─RY(θ/2)─┤ X ├─RY(-θ/2)─┤ X ├─
    let private decomposeCRY (control: int) (target: int) (angle: float) : Gate list =
        let halfAngle = angle / 2.0
        [
            RY (target, halfAngle)
            CNOT (control, target)
            RY (target, -halfAngle)
            CNOT (control, target)
        ]
    
    /// Decompose CRZ (Controlled-RZ) gate into RZ + CNOT gates
    /// 
    /// CRZ(θ) = RZ(θ/2) · CNOT · RZ(-θ/2) · CNOT
    /// 
    /// Reference: Nielsen & Chuang, Section 4.3
    /// 
    /// Circuit:
    ///   c: ──────●──────────●─────
    ///            │          │
    ///   t: ─RZ(θ/2)─┤ X ├─RZ(-θ/2)─┤ X ├─
    let private decomposeCRZ (control: int) (target: int) (angle: float) : Gate list =
        let halfAngle = angle / 2.0
        [
            RZ (target, halfAngle)
            CNOT (control, target)
            RZ (target, -halfAngle)
            CNOT (control, target)
        ]
    
    // ========================================================================
    // THREE-QUBIT GATE DECOMPOSITIONS (Toffoli/CCX)
    // ========================================================================
    
    /// Decompose CCX (Toffoli) gate using standard Barenco decomposition
    /// 
    /// CCX decomposes into 6 CNOTs + T gates (or RZ equivalents)
    /// 
    /// This is the standard textbook decomposition (Nielsen & Chuang, p. 182):
    /// 
    /// Circuit:
    ///   c1: ───────────────●────────T────●────────
    ///                      │             │
    ///   c2: ─────●─────────┼────T────────┼────●───
    ///            │         │             │    │
    ///   t:  ─H─┤ X ├─TDG─┤ X ├─T──────┤ X ├─TDG─┤ X ├─T─H─
    /// 
    /// Cost: 6 CNOTs, 7 single-qubit gates
    /// 
    /// Alternative: Can use only RZ gates if T/TDG not available
    let private decomposeCCX (control1: int) (control2: int) (target: int) : Gate list =
        [
            // Prepare target qubit
            H target
            
            // First CNOT from control2 to target
            CNOT (control2, target)
            
            // T-dagger on target
            TDG target
            
            // CNOT from control1 to target
            CNOT (control1, target)
            
            // T on target
            T target
            
            // Second CNOT from control2 to target
            CNOT (control2, target)
            
            // T-dagger on target
            TDG target
            
            // Third CNOT from control1 to target
            CNOT (control1, target)
            
            // T gates on controls and target
            T control2
            T target
            
            // Hadamard to complete
            H target
            
            // Final CNOTs between controls
            CNOT (control1, control2)
            
            // Final T gates
            T control1
            TDG control2
            
            // Last CNOT
            CNOT (control1, control2)
        ]
    
    /// Decompose CCX without using T/TDG gates (pure RZ version)
    /// 
    /// Uses RZ gates instead of T/TDG for backends that don't support T gates
    let private decomposeCCXWithRZ (control1: int) (control2: int) (target: int) : Gate list =
        decomposeCCX control1 control2 target
        |> List.collect (fun gate ->
            match gate with
            | T q -> decomposeT q
            | TDG q -> decomposeTDG q
            | other -> [other])
    
    // ========================================================================
    // MULTI-CONTROLLED GATE DECOMPOSITIONS (MCZ, MCX)
    // ========================================================================
    
    // ------------------------------------------------------------------------
    // GRAY CODE UTILITIES
    // ------------------------------------------------------------------------
    
    /// Compute Gray code for integer n
    /// Gray code: binary encoding where adjacent values differ by exactly 1 bit
    /// Formula: g(n) = n XOR (n >> 1)
    let private grayCode (n: int) : int =
        n ^^^ (n >>> 1)
    
    /// Find the bit position where two Gray codes differ
    /// Since adjacent Gray codes differ by exactly 1 bit, this finds that position
    let private grayCodeDiffBit (g1: int) (g2: int) : int =
        let diff = g1 ^^^ g2
        // Find position of the single set bit
        let rec findBit pos =
            if pos >= 32 then 0  // Safety check
            elif (diff >>> pos) &&& 1 = 1 then pos
            else findBit (pos + 1)
        findBit 0
    
    // ------------------------------------------------------------------------
    // MULTI-CONTROLLED Z DECOMPOSITION (GRAY CODE)
    // ------------------------------------------------------------------------
    
    /// Decompose multi-controlled Z gate (MCZ) into standard gates
    /// 
    /// **Algorithm:**
    /// MCZ with n controls decomposes as: H(target) · MCX · H(target)
    /// where MCX (multi-controlled X) is decomposed recursively.
    /// 
    /// **Strategy by number of controls:**
    /// - 0 controls: Z gate
    /// - 1 control: CZ gate  
    /// - 2 controls: CCZ gate (H + CCX + H)
    /// - 3+ controls: Recursive Toffoli decomposition (ancilla-free)
    /// 
    /// **Recursive Decomposition (n >= 3):**
    /// Uses Barenco et al. (1995) recursive structure:
    /// - MCX(c1, c2, c3, ..., cn, t) breaks down into 4 sub-operations
    /// - Reuses control qubits as auxiliary targets (ancilla-free)
    /// - Each level reduces problem by 1 control qubit
    /// 
    /// **Gate Count:**
    /// - For n controls: O(4^n) gates (exponential growth)
    /// - Trade-off: No ancilla qubits needed, but higher gate count
    /// - With ancilla qubits, could achieve O(n) linear growth
    /// 
    /// **Future Optimization:**
    /// Gray code optimization could reduce gate count to O(2^n):
    /// - Traverse control patterns using Gray code sequence
    /// - Adjacent patterns differ by 1 bit → only 1 CNOT per transition
    /// - Would require careful state management for functional F# implementation
    /// 
    /// **References:**
    /// - Barenco et al. (1995): "Elementary gates for quantum computation"
    /// - Nielsen & Chuang: "Quantum Computation and Quantum Information", Section 4.3
    let rec private decomposeMCZ (controls: int list) (target: int) : Gate list =
        match controls with
        | [] -> 
            // No controls: just Z gate
            [Z target]
        
        | [control] -> 
            // Single control: CZ gate
            [CZ (control, target)]
        
        | [control1; control2] ->
            // Two controls: CCZ = H + CCX + H
            [
                H target
                CCX (control1, control2, target)
                H target
            ]
        
        | _ when controls.Length >= 3 ->
            // Three or more controls: use recursive Toffoli decomposition
            // MCZ = H · MCX · H, where MCX is decomposed recursively
            
            // Recursive Toffoli decomposition (Barenco et al. 1995)
            // MCX([c1, c2, c3, ...], t) with n controls decomposes into:
            // - 2 * (n-2) Toffoli gates (CCX)
            // - Uses recursive structure without explicit ancilla qubits
            //
            // This is a simplified, practical decomposition that:
            // - Works for arbitrary number of controls
            // - Uses only CCX, CX, and single-qubit gates  
            // - Is ancilla-free but with higher gate count than optimal
            //
            // Gate count: O(4^n) exponential growth
            // Optimal with ancilla would be O(n) linear growth
            
            let rec decomposeMCX (ctrls: int list) (tgt: int) : Gate list =
                match ctrls with
                | [] ->
                    // No controls: just X gate
                    [X tgt]
                
                | [c] ->
                    // Single control: CNOT
                    [CNOT (c, tgt)]
                
                | [c1; c2] ->
                    // Two controls: Toffoli (CCX)
                    [CCX (c1, c2, tgt)]
                
                | c1 :: c2 :: c3 :: rest ->
                    // Three or more controls: recursive decomposition
                    // Strategy: reduce n-controlled gate to (n-1)-controlled gates
                    //
                    // MCX(c1, c2, c3, ... cn, t) decomposes as:
                    // 1. MCX(c2, c3, ..., cn, c1)  -- use c1 as auxiliary
                    // 2. MCX(c1, c3, ..., cn, t)   -- apply with c1 added
                    // 3. MCX(c2, c3, ..., cn, c1)  -- uncompute c1
                    // 4. MCX(c1, c3, ..., cn, t)   -- final application
                    //
                    // This is NOT optimal but is:
                    // - Correct (produces proper multi-controlled X)
                    // - Ancilla-free (uses existing control qubits)
                    // - Simple to implement and verify
                    
                    let remainingControls = c2 :: c3 :: rest
                    
                    // Decompose using c1 as auxiliary target
                    let part1 = decomposeMCX remainingControls c1
                    let part2 = decomposeMCX (c1 :: c3 :: rest) tgt
                    let part3 = decomposeMCX remainingControls c1
                    let part4 = decomposeMCX (c1 :: c3 :: rest) tgt
                    
                    part1 @ part2 @ part3 @ part4
            
            // MCZ = H + MCX + H
            [H target] @ decomposeMCX controls target @ [H target]
        
        | _ ->
            // Should never reach here
            failwith "Invalid MCZ decomposition"
    
    // ========================================================================
    // SINGLE GATE TRANSPILATION
    // ========================================================================
    
    /// Transpile a single gate based on backend support
    /// 
    /// Parameters:
    /// - needsPhaseDecomposition: true if backend doesn't support S/T gates
    /// - needsCZDecomposition: true if backend doesn't support CZ
    /// - needsCCXDecomposition: true if backend doesn't support CCX
    /// - needsControlledRotationDecomposition: true if backend doesn't support CRX/CRY/CRZ
    /// - gate: the gate to transpile
    /// 
    /// Returns: list of gates (original if supported, decomposed if not)
    let private transpileGate 
        (needsPhaseDecomposition: bool)
        (needsCZDecomposition: bool) 
        (needsCCXDecomposition: bool)
        (needsControlledRotationDecomposition: bool)
        (gate: Gate) : Gate list =
        
        match gate with
        // Phase gates - decompose if needed
        | S q when needsPhaseDecomposition -> decomposeS q
        | SDG q when needsPhaseDecomposition -> decomposeSDG q
        | T q when needsPhaseDecomposition -> decomposeT q
        | TDG q when needsPhaseDecomposition -> decomposeTDG q
        
        // CZ - decompose if needed (for IonQ)
        | CZ (c, t) when needsCZDecomposition -> decomposeCZ c t
        
        // Controlled rotations - decompose if needed (for IonQ, Rigetti)
        | CRX (c, t, angle) when needsControlledRotationDecomposition -> decomposeCRX c t angle
        | CRY (c, t, angle) when needsControlledRotationDecomposition -> decomposeCRY c t angle
        | CRZ (c, t, angle) when needsControlledRotationDecomposition -> decomposeCRZ c t angle
        
        // CCX - always decompose (no backend supports it natively)
        | CCX (c1, c2, t) when needsCCXDecomposition ->
            // If we need phase decomposition, use pure RZ version
            if needsPhaseDecomposition then
                decomposeCCXWithRZ c1 c2 t
            else
                decomposeCCX c1 c2 t
        
        // MCZ - always decompose (no backend supports multi-controlled gates natively)
        | MCZ (controls, target) -> decomposeMCZ controls target
        
        // All other gates - pass through unchanged
        | other -> [other]
    
    // ========================================================================
    // CIRCUIT TRANSPILATION
    // ========================================================================
    
    /// Transpile entire circuit for a specific backend
    /// 
    /// Automatically detects which gates need decomposition based on backend support.
    /// 
    /// Backend Support Matrix:
    /// - IonQ: X, Y, Z, H, RX, RY, RZ, CNOT, SWAP
    ///   → Needs: S/T → RZ, CZ → H+CNOT+H, CRX/CRY/CRZ → RX/RY/RZ+CNOT, CCX → 6xCNOT
    /// 
    /// - Rigetti: X, Y, Z, H, RX, RY, RZ, CNOT, CZ, SWAP
    ///   → Needs: S/T → RZ, CRX/CRY/CRZ → RX/RY/RZ+CNOT, CCX → 6xCNOT
    /// 
    /// - Quantinuum: H, X, Y, Z, S, T, RX, RY, RZ, CZ (all-to-all connectivity, trapped-ion)
    ///   → Needs: CRX/CRY/CRZ → RX/RY/RZ+CNOT, CCX → 6xCNOT
    /// 
    /// - Local: All gates supported natively
    ///   → No decomposition needed
    let transpileForBackend (backendName: string) (circuit: Circuit) : Circuit =
        // Determine what decompositions are needed based on backend
        let (needsPhaseDecomp, needsCZDecomp, needsControlledRotationDecomp, needsCCXDecomp) =
            match backendName.ToLowerInvariant() with
            // IonQ doesn't support S, T, CZ, or controlled rotations
            | name when name.Contains("ionq") -> 
                (true, true, true, true)
            
            // Rigetti doesn't support S, T, or controlled rotations but has CZ
            | name when name.Contains("rigetti") -> 
                (true, false, true, true)
            
            // Quantinuum H-Series: Native CZ (trapped-ion), S, T supported
            // Needs controlled rotation and CCX decomposition
            | name when name.Contains("quantinuum") -> 
                (false, false, true, true)
            
            // Atom Computing Phoenix: Native CZ (Rydberg blockade), all-to-all connectivity
            // Similar to Quantinuum - needs controlled rotation and CCX decomposition
            | name when name.Contains("atom") || name.Contains("atomcomputing") -> 
                (false, false, true, true)
            
            // Local simulator supports everything
            | name when name.Contains("local") -> 
                (false, false, false, false)
            
            // Unknown backend - be conservative, decompose everything
            | _ -> 
                (true, true, true, true)
        
        // Transpile all gates in the circuit
        let transpiledGates =
            circuit.Gates
            |> List.collect (transpileGate needsPhaseDecomp needsCZDecomp needsCCXDecomp needsControlledRotationDecomp)
        
        { circuit with Gates = transpiledGates }
    
    /// Transpile circuit using backend constraints
    /// 
    /// More flexible version that uses CircuitValidator.BackendConstraints
    /// to determine supported gates.
    let transpile (constraints: CircuitValidator.BackendConstraints) (circuit: Circuit) : Circuit =
        let supportedGates = constraints.SupportedGates
        
        // Check which decompositions are needed
        let needsPhaseDecomp = 
            not (supportedGates.Contains "S" && supportedGates.Contains "T")
        
        let needsCZDecomp = 
            not (supportedGates.Contains "CZ")
        
        let needsCCXDecomp = 
            not (supportedGates.Contains "CCX")
        
        let needsControlledRotationDecomp =
            not (supportedGates.Contains "CRX" && supportedGates.Contains "CRY" && supportedGates.Contains "CRZ")
        
        // Transpile all gates
        let transpiledGates =
            circuit.Gates
            |> List.collect (transpileGate needsPhaseDecomp needsCZDecomp needsCCXDecomp needsControlledRotationDecomp)
        
        { circuit with Gates = transpiledGates }
    
    // ========================================================================
    // UTILITY FUNCTIONS
    // ========================================================================
    
    /// Check if a circuit needs transpilation for a given backend
    /// 
    /// Returns true if the circuit contains gates not natively supported
    let needsTranspilation (constraints: CircuitValidator.BackendConstraints) (circuit: Circuit) : bool =
        let supportedGates = constraints.SupportedGates
        
        circuit.Gates
        |> List.exists (fun gate ->
            match gate with
            | S _ | SDG _ | T _ | TDG _ -> 
                not (supportedGates.Contains "S" && supportedGates.Contains "T")
            | CZ _ -> 
                not (supportedGates.Contains "CZ")
            | CCX _ -> 
                not (supportedGates.Contains "CCX")
            | _ -> false)
    
    /// Get transpilation statistics for a circuit
    /// 
    /// Returns (original gate count, transpiled gate count, gates decomposed)
    let getTranspilationStats (constraints: CircuitValidator.BackendConstraints) (circuit: Circuit) : int * int * int =
        let originalCount = circuit.Gates.Length
        let transpiled = transpile constraints circuit
        let transpiledCount = transpiled.Gates.Length
        let gatesDecomposed = transpiledCount - originalCount
        
        (originalCount, transpiledCount, gatesDecomposed)
