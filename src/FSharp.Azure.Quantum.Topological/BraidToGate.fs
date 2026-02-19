namespace FSharp.Azure.Quantum.Topological

open System
open System.Numerics
open FSharp.Azure.Quantum

/// Quantum gate compilation from topological braiding operations.
/// 
/// This module translates braiding sequences (topological operations on anyons)
/// into conventional quantum gate circuits. This is essential for:
/// 
/// 1. Running topological algorithms on gate-based quantum computers
/// 2. Simulating topological quantum computation classically
/// 3. Comparing topological vs. gate-based approaches
/// 4. Building hybrid quantum algorithms
module BraidToGate =

    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Compiled gate sequence with metadata
    type GateSequence = {
        /// Ordered list of quantum gates
        Gates: CircuitBuilder.Gate list
        
        /// Number of qubits in the circuit
        NumQubits: int
        
        /// Total accumulated phase from braiding
        TotalPhase: Complex
        
        /// Circuit depth (longest path through gates)
        Depth: int
        
        /// Number of T gates (cost metric for fault-tolerance)
        TCount: int
    }

    /// Gate compilation options
    type CompilationOptions = {
        /// Allowed gate set (e.g., Clifford+T, universal, etc.)
        TargetGateSet: Set<string>
        
        /// Optimization level: 0=none, 1=basic, 2=aggressive
        OptimizationLevel: int
        
        /// Tolerance for gate sequence approximation
        ApproximationTolerance: float
        
        /// Whether to decompose into single/two-qubit gates only
        DecomposeToBasic: bool
    }

    // ========================================================================
    // DEFAULT OPTIONS
    // ========================================================================
    
    /// Clifford+T gate set (fault-tolerant universal set)
    let cliffordPlusT = 
        Set.ofList ["H"; "CNOT"; "T"; "S"; "X"; "Y"; "Z"]
    
    /// Full universal gate set
    let universalGateSet =
        Set.ofList ["H"; "CNOT"; "T"; "S"; "X"; "Y"; "Z"; "Rz"; "Phase"; "U3"]
    
    /// Default compilation options (Clifford+T, basic optimization)
    let defaultOptions = {
        TargetGateSet = cliffordPlusT
        OptimizationLevel = 1
        ApproximationTolerance = 1e-10
        DecomposeToBasic = true
    }

    // ========================================================================
    // GATE UTILITIES
    // ========================================================================
    
    /// Get gate name as string
    let getGateName (gate: CircuitBuilder.Gate) : string =
        match gate with
        | CircuitBuilder.Gate.H _ -> "H"
        | CircuitBuilder.Gate.X _ -> "X"
        | CircuitBuilder.Gate.Y _ -> "Y"
        | CircuitBuilder.Gate.Z _ -> "Z"
        | CircuitBuilder.Gate.CNOT _ -> "CNOT"
        | CircuitBuilder.Gate.P _ -> "Phase"
        | CircuitBuilder.Gate.T _ -> "T"
        | CircuitBuilder.Gate.TDG _ -> "Tdg"
        | CircuitBuilder.Gate.S _ -> "S"
        | CircuitBuilder.Gate.SDG _ -> "Sdg"
        | CircuitBuilder.Gate.RZ _ -> "Rz"
        | CircuitBuilder.Gate.RX _ -> "Rx"
        | CircuitBuilder.Gate.RY _ -> "Ry"
        | CircuitBuilder.Gate.U3 _ -> "U3"
        | _ -> "Other"  // For other gate types not mapped
    
    /// Get qubits affected by a gate
    let getAffectedQubits (gate: CircuitBuilder.Gate) : int list =
        match gate with
        | CircuitBuilder.Gate.H q | CircuitBuilder.Gate.X q | CircuitBuilder.Gate.Y q | CircuitBuilder.Gate.Z q 
        | CircuitBuilder.Gate.P (q, _) | CircuitBuilder.Gate.T q | CircuitBuilder.Gate.TDG q 
        | CircuitBuilder.Gate.S q | CircuitBuilder.Gate.SDG q 
        | CircuitBuilder.Gate.RZ (q, _) | CircuitBuilder.Gate.U3 (q, _, _, _) -> [q]
        | CircuitBuilder.Gate.CNOT (c, t) | CircuitBuilder.Gate.CZ (c, t) | CircuitBuilder.Gate.SWAP (c, t) -> [c; t]
        | CircuitBuilder.Gate.CCX (c1, c2, t) -> [c1; c2; t]
        | _ -> []  // For other gate types
    
    /// Check if gate is a Clifford gate
    let isClifford (gate: CircuitBuilder.Gate) : bool =
        match gate with
        | CircuitBuilder.Gate.H _ | CircuitBuilder.Gate.X _ | CircuitBuilder.Gate.Y _ | CircuitBuilder.Gate.Z _ 
        | CircuitBuilder.Gate.CNOT _ | CircuitBuilder.Gate.S _ | CircuitBuilder.Gate.SDG _ -> true
        | _ -> false
    
    /// Count T gates in a sequence
    let countTGates (gates: CircuitBuilder.Gate list) : int =
        gates 
        |> List.filter (fun g -> 
            match g with 
            | CircuitBuilder.Gate.T _ | CircuitBuilder.Gate.TDG _ -> true 
            | _ -> false)
        |> List.length

    // ========================================================================
    // BRAIDING GLOBAL PHASE COMPUTATION
    // ========================================================================

    /// Compute the global braiding phase for a single braid generator.
    ///
    /// Each elementary braid σ_i (or σ_i⁻¹) contributes a global phase determined
    /// by the anyon type's R-matrix. This is the phase acquired by the anyon state
    /// under exchange, distinct from the relative phase applied by gate decomposition.
    ///
    /// - **Ising**: σ_i → exp(-iπ/8), σ_i⁻¹ → exp(iπ/8)
    ///   (from R[σ,σ;1] = exp(-iπ/8), Kitaev 2006 convention)
    /// - **Fibonacci**: σ_i → exp(4πi/5), σ_i⁻¹ → exp(-4πi/5)
    ///   (from R[τ,τ;1] = exp(4πi/5))
    /// - **Other**: σ_i → exp(-iπ/8), σ_i⁻¹ → exp(iπ/8)  (default, same as Ising)
    let braidingPhase (anyonType: AnyonSpecies.AnyonType) (isClockwise: bool) : Complex =
        let angle =
            match anyonType with
            | AnyonSpecies.AnyonType.Ising ->
                if isClockwise then -Math.PI / 8.0
                else Math.PI / 8.0
            | AnyonSpecies.AnyonType.Fibonacci ->
                if isClockwise then 4.0 * Math.PI / 5.0
                else -4.0 * Math.PI / 5.0
            | _ ->
                // Default: use Ising-like phase
                if isClockwise then -Math.PI / 8.0
                else Math.PI / 8.0
        Complex(cos angle, sin angle)

    /// Compute the total accumulated braiding phase for a sequence of generators.
    /// The total phase is the product of individual generator phases.
    let accumulateBraidingPhase
        (generators: BraidGroup.BraidGenerator list)
        (anyonType: AnyonSpecies.AnyonType) : Complex =
        generators
        |> List.fold (fun (acc: Complex) gen ->
            acc * braidingPhase anyonType gen.IsClockwise
        ) Complex.One

    // ========================================================================
    // BRAIDING TO GATE MAPPING
    // ========================================================================
    
    /// Map Ising anyon braiding phase to gate decomposition.
    /// 
    /// For Ising anyons, one exchange produces relative phase:
    ///   e^{3iπ/8} / e^{-iπ/8} = e^{iπ/2} = i = S gate
    /// (from R[σ,σ;Vacuum] = e^{-iπ/8}, R[σ,σ;Psi] = e^{3iπ/8})
    /// 
    /// Reference: Simon "Topological Quantum" Eq. 10.9-10.10
    let isingBraidingToGates (generatorIndex: int) (isClockwise: bool) : CircuitBuilder.Gate list =
        // Ising: σ_i braiding on qubit i
        // Clockwise σ_i → S gate (relative phase +π/2)
        // Counter-clockwise σ_i⁻¹ → S† gate (relative phase -π/2)
        if isClockwise then 
            [CircuitBuilder.Gate.S generatorIndex]
        else 
            [CircuitBuilder.Gate.SDG generatorIndex]
    
    /// Map Fibonacci anyon braiding phase to gate approximation.
    /// 
    /// Fibonacci braiding produces phases like exp(±4πi/5), which don't
    /// correspond to simple gates. We need Solovay-Kitaev approximation.
    let fibonacciBraidingToGates (generatorIndex: int) (isClockwise: bool) (tolerance: float) : CircuitBuilder.Gate list =
        // Fibonacci: τ×τ→1 braiding produces exp(4πi/5)
        // This requires approximation using Clifford+T gates
        let angle = 
            if isClockwise then
                4.0 * Math.PI / 5.0
            else
                -4.0 * Math.PI / 5.0
        
        // For now, use Rz gate (will implement Solovay-Kitaev later)
        [CircuitBuilder.Gate.RZ (generatorIndex, angle)]

    // ========================================================================
    // GATE SEQUENCE OPTIMIZATION
    // ========================================================================
    
    /// Cancel adjacent inverse gates (e.g., S followed by S†)
    let cancelInverses (gates: CircuitBuilder.Gate list) : CircuitBuilder.Gate list =
        let rec loop acc remaining =
            match remaining with
            | [] -> List.rev acc
            | [g] -> List.rev (g :: acc)
            | g1 :: g2 :: rest ->
                let cancels =
                    match g1, g2 with
                    | CircuitBuilder.Gate.T q1, CircuitBuilder.Gate.TDG q2 when q1 = q2 -> true
                    | CircuitBuilder.Gate.TDG q1, CircuitBuilder.Gate.T q2 when q1 = q2 -> true
                    | CircuitBuilder.Gate.S q1, CircuitBuilder.Gate.SDG q2 when q1 = q2 -> true
                    | CircuitBuilder.Gate.SDG q1, CircuitBuilder.Gate.S q2 when q1 = q2 -> true
                    | CircuitBuilder.Gate.H q1, CircuitBuilder.Gate.H q2 when q1 = q2 -> true
                    | CircuitBuilder.Gate.X q1, CircuitBuilder.Gate.X q2 when q1 = q2 -> true
                    | CircuitBuilder.Gate.Y q1, CircuitBuilder.Gate.Y q2 when q1 = q2 -> true
                    | CircuitBuilder.Gate.Z q1, CircuitBuilder.Gate.Z q2 when q1 = q2 -> true
                    | _ -> false
                
                if cancels then
                    loop acc rest  // Skip both gates
                else
                    loop (g1 :: acc) (g2 :: rest)
        
        loop [] gates
    
    /// Merge adjacent rotation gates on same qubit
    let mergeRotations (gates: CircuitBuilder.Gate list) : CircuitBuilder.Gate list =
        let rec loop acc remaining =
            match remaining with
            | [] -> List.rev acc
            | [g] -> List.rev (g :: acc)
            | g1 :: g2 :: rest ->
                let merged =
                    match g1, g2 with
                    | CircuitBuilder.Gate.RZ (q1, a1), CircuitBuilder.Gate.RZ (q2, a2) when q1 = q2 ->
                        Some (CircuitBuilder.Gate.RZ (q1, a1 + a2))
                    | CircuitBuilder.Gate.P (q1, a1), CircuitBuilder.Gate.P (q2, a2) when q1 = q2 ->
                        Some (CircuitBuilder.Gate.P (q1, a1 + a2))
                    | _ -> None
                
                match merged with
                | Some g -> loop acc (g :: rest)  // Replace both with merged
                | None -> loop (g1 :: acc) (g2 :: rest)
        
        loop [] gates
    
    /// Basic gate sequence optimization
    let optimizeBasic (gates: CircuitBuilder.Gate list) : CircuitBuilder.Gate list =
        gates
        |> cancelInverses
        |> mergeRotations
        |> cancelInverses  // Run again after merging
    
    /// Aggressive optimization (placeholder for future enhancements)
    let optimizeAggressive (gates: CircuitBuilder.Gate list) : CircuitBuilder.Gate list =
        gates
        |> optimizeBasic
        // Future: commutation-based optimization, template matching, etc.
    
    /// Optimize gate sequence based on level
    let optimizeGates (level: int) (gates: CircuitBuilder.Gate list) : CircuitBuilder.Gate list =
        match level with
        | 0 -> gates  // No optimization
        | 1 -> optimizeBasic gates
        | _ -> optimizeAggressive gates  // 2+

    // ========================================================================
    // CIRCUIT DEPTH CALCULATION
    // ========================================================================
    
    /// Calculate circuit depth (longest path through dependent gates)
    let calculateDepth (gates: CircuitBuilder.Gate list) (numQubits: int) : int =
        if numQubits = 0 then 0
        else
            // Track depth at each qubit
            let depths = Array.create numQubits 0
            
            for gate in gates do
                let qubits = getAffectedQubits gate
                match qubits with
                | [] -> ()  // Gate with no affected qubits (e.g., unrecognized gate type)
                | _ ->
                    let maxDepth = qubits |> List.map (fun q -> depths.[q]) |> List.max
                    let newDepth = maxDepth + 1
                    
                    // Update all affected qubits to new depth
                    for q in qubits do
                        depths.[q] <- newDepth
            
            Array.max depths

    // ========================================================================
    // BRAID TO GATE COMPILATION
    // ========================================================================
    
    /// Compile a single braid generator to gates
    let compileGenerator 
        (gen: BraidGroup.BraidGenerator) 
        (anyonType: AnyonSpecies.AnyonType)
        (options: CompilationOptions) : CircuitBuilder.Gate list =
        
        match anyonType with
        | AnyonSpecies.AnyonType.Ising ->
            isingBraidingToGates gen.Index gen.IsClockwise
        
        | AnyonSpecies.AnyonType.Fibonacci ->
            fibonacciBraidingToGates gen.Index gen.IsClockwise options.ApproximationTolerance
        
        | _ ->
            // For other anyon types, use generic phase gate
            let phase = 
                if gen.IsClockwise then
                    -Math.PI / 8.0  // Default: Ising-like phase
                else
                    Math.PI / 8.0
            [CircuitBuilder.Gate.RZ (gen.Index, phase)]
    
    /// Compile full braid to gate sequence
    let compileToGates 
        (braid: BraidGroup.BraidWord) 
        (anyonType: AnyonSpecies.AnyonType)
        (options: CompilationOptions) : Result<GateSequence, TopologicalError> =
        
        try
            // Compile each generator to gates
            let allGates =
                braid.Generators
                |> List.collect (fun gen ->
                    compileGenerator gen anyonType options)
            
            // Apply optimization
            let optimizedGates = optimizeGates options.OptimizationLevel allGates
            
            // Calculate metadata
            let numQubits = braid.StrandCount - 1  // n strands = n-1 qubits
            let depth = calculateDepth optimizedGates numQubits
            let tCount = countTGates optimizedGates
            
            // Accumulated global phase from the braid generators.
            // Computed from the R-matrix phases before gate optimization,
            // since gate cancellation (e.g. T·T† → I) corresponds to
            // phase cancellation (exp(-iπ/8)·exp(iπ/8) = 1).
            let totalPhase = accumulateBraidingPhase braid.Generators anyonType
            
            let sequence = {
                Gates = optimizedGates
                NumQubits = numQubits
                TotalPhase = totalPhase
                Depth = depth
                TCount = tCount
            }
            
            Ok sequence
            
        with ex ->
            TopologicalResult.computationError "operation" $"Failed to compile braid to gates: {ex.Message}"

    // ========================================================================
    // DISPLAY UTILITIES
    // ========================================================================
    
    /// Display a quantum gate in readable format
    let displayGate (gate: CircuitBuilder.Gate) : string =
        match gate with
        | CircuitBuilder.Gate.H q -> $"H(q{q})"
        | CircuitBuilder.Gate.X q -> $"X(q{q})"
        | CircuitBuilder.Gate.Y q -> $"Y(q{q})"
        | CircuitBuilder.Gate.Z q -> $"Z(q{q})"
        | CircuitBuilder.Gate.CNOT (c, t) -> $"CNOT(q{c}, q{t})"
        | CircuitBuilder.Gate.P (q, a) -> $"Phase(q{q}, {a:F4})"
        | CircuitBuilder.Gate.T q -> $"T(q{q})"
        | CircuitBuilder.Gate.TDG q -> $"T†(q{q})"
        | CircuitBuilder.Gate.S q -> $"S(q{q})"
        | CircuitBuilder.Gate.SDG q -> $"S†(q{q})"
        | CircuitBuilder.Gate.RZ (q, a) -> $"Rz(q{q}, {a:F4})"
        | CircuitBuilder.Gate.U3 (q, θ, φ, λ) -> $"U3(q{q}, θ={θ:F4}, φ={φ:F4}, λ={λ:F4})"
        | _ -> $"Gate({gate})"  // Fallback for other gate types
    
    /// Display gate sequence in readable format
    let displayGateSequence (sequence: GateSequence) : string =
        let gateLines = 
            sequence.Gates
            |> List.mapi (fun i g -> $"  {i+1}. {displayGate g}")
            |> String.concat "\n"
        
        $"""Gate Sequence ({sequence.Gates.Length} gates, {sequence.NumQubits} qubits)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Circuit depth: {sequence.Depth}
T-count: {sequence.TCount}
Total phase: {sequence.TotalPhase}

Gates:
{gateLines}"""
    
    /// Display circuit statistics
    let displayStatistics (sequence: GateSequence) : string =
        let gateTypeCounts =
            sequence.Gates
            |> List.groupBy getGateName
            |> List.map (fun (name, gates) -> $"  {name}: {gates.Length}")
            |> String.concat "\n"
        
        let cliffordCount = sequence.Gates |> List.filter isClifford |> List.length
        let nonCliffordCount = sequence.Gates.Length - cliffordCount
        
        $"""Circuit Statistics
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Total gates: {sequence.Gates.Length}
Qubits: {sequence.NumQubits}
Depth: {sequence.Depth}
T-count: {sequence.TCount}

Gate breakdown:
{gateTypeCounts}

Clifford gates: {cliffordCount}
Non-Clifford gates: {nonCliffordCount}"""
