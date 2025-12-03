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
    
    /// Decompose multi-controlled Z gate (MCZ) into standard gates
    /// 
    /// MCZ decomposes as: H(target) · MCX · H(target)
    /// where MCX is multi-controlled NOT
    /// 
    /// Strategy (recursive decomposition using ancilla-free method):
    /// - 0 controls: Z gate
    /// - 1 control: CZ gate
    /// - 2 controls: CCZ gate (H + CCX + H)
    /// - n controls: Use V-chain decomposition (requires n-2 ancilla qubits)
    ///               OR Gray-code optimization (ancilla-free but more gates)
    /// 
    /// For now: Simple decomposition for n <= 2, error for n > 2
    /// TODO: Implement full Gray-code decomposition for arbitrary n
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
        | _ ->
            // More than 2 controls: requires advanced decomposition
            // For now, return an error - this should be rare in practice
            failwith $"MCZ with {List.length controls} controls not yet supported in transpiler. Use at most 2 controls or implement Gray-code decomposition."
    
    // ========================================================================
    // SINGLE GATE TRANSPILATION
    // ========================================================================
    
    /// Transpile a single gate based on backend support
    /// 
    /// Parameters:
    /// - needsPhaseDecomposition: true if backend doesn't support S/T gates
    /// - needsCZDecomposition: true if backend doesn't support CZ
    /// - needsCCXDecomposition: true if backend doesn't support CCX
    /// - gate: the gate to transpile
    /// 
    /// Returns: list of gates (original if supported, decomposed if not)
    let private transpileGate 
        (needsPhaseDecomposition: bool)
        (needsCZDecomposition: bool) 
        (needsCCXDecomposition: bool)
        (gate: Gate) : Gate list =
        
        match gate with
        // Phase gates - decompose if needed
        | S q when needsPhaseDecomposition -> decomposeS q
        | SDG q when needsPhaseDecomposition -> decomposeSDG q
        | T q when needsPhaseDecomposition -> decomposeT q
        | TDG q when needsPhaseDecomposition -> decomposeTDG q
        
        // CZ - decompose if needed (for IonQ)
        | CZ (c, t) when needsCZDecomposition -> decomposeCZ c t
        
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
    ///   → Needs: S/T → RZ, CZ → H+CNOT+H, CCX → 6xCNOT
    /// 
    /// - Rigetti: X, Y, Z, H, RX, RY, RZ, CNOT, CZ, SWAP
    ///   → Needs: S/T → RZ, CCX → 6xCNOT
    /// 
    /// - Local: All gates supported natively
    ///   → No decomposition needed
    let transpileForBackend (backendName: string) (circuit: Circuit) : Circuit =
        // Determine what decompositions are needed based on backend
        let (needsPhaseDecomp, needsCZDecomp, needsCCXDecomp) =
            match backendName.ToLowerInvariant() with
            // IonQ doesn't support S, T, or CZ
            | name when name.Contains("ionq") -> 
                (true, true, true)
            
            // Rigetti doesn't support S, T but has CZ
            | name when name.Contains("rigetti") -> 
                (true, false, true)
            
            // Local simulator supports everything
            | name when name.Contains("local") -> 
                (false, false, false)
            
            // Unknown backend - be conservative, decompose everything
            | _ -> 
                (true, true, true)
        
        // Transpile all gates in the circuit
        let transpiledGates =
            circuit.Gates
            |> List.collect (transpileGate needsPhaseDecomp needsCZDecomp needsCCXDecomp)
        
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
        
        // Transpile all gates
        let transpiledGates =
            circuit.Gates
            |> List.collect (transpileGate needsPhaseDecomp needsCZDecomp needsCCXDecomp)
        
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
