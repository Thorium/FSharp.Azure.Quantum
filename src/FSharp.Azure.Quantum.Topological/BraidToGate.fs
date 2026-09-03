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
    let rec getAffectedQubits (gate: CircuitBuilder.Gate) : int list =
        match gate with
        | CircuitBuilder.Gate.H q | CircuitBuilder.Gate.X q | CircuitBuilder.Gate.Y q | CircuitBuilder.Gate.Z q 
        | CircuitBuilder.Gate.P (q, _) | CircuitBuilder.Gate.T q | CircuitBuilder.Gate.TDG q 
        | CircuitBuilder.Gate.S q | CircuitBuilder.Gate.SDG q 
        | CircuitBuilder.Gate.RZ (q, _) | CircuitBuilder.Gate.U3 (q, _, _, _)
        | CircuitBuilder.Gate.RX (q, _) | CircuitBuilder.Gate.RY (q, _)
        | CircuitBuilder.Gate.Measure q | CircuitBuilder.Gate.Reset q -> [q]
        | CircuitBuilder.Gate.CNOT (c, t) | CircuitBuilder.Gate.CZ (c, t) | CircuitBuilder.Gate.SWAP (c, t)
        | CircuitBuilder.Gate.CP (c, t, _) | CircuitBuilder.Gate.CRX (c, t, _)
        | CircuitBuilder.Gate.CRY (c, t, _) | CircuitBuilder.Gate.CRZ (c, t, _)
        | CircuitBuilder.Gate.RXX (c, t, _) | CircuitBuilder.Gate.RYY (c, t, _)
        | CircuitBuilder.Gate.RZZ (c, t, _) -> [c; t]
        | CircuitBuilder.Gate.CCX (c1, c2, t) -> [c1; c2; t]
        | CircuitBuilder.Gate.MCZ (controls, target) -> controls @ [target]
        | CircuitBuilder.Gate.Barrier qubits -> qubits
        | CircuitBuilder.Gate.Conditional (q, inner) -> q :: getAffectedQubits inner
    
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

    /// R-symbols R[1/2,1/2; j=0] and R[1/2,1/2; j=1] for the SU(2)_k qubit
    /// encoding (the two fusion channels of a pair of j=1/2 anyons).
    /// Same construction as SolovayKitaev.computeSU2kSigmaMatrices.
    let private su2HalfSpinRSymbols (k: int) : Complex * Complex =
        let halfSpin = AnyonSpecies.Particle.SpinJ(1, k)
        let j0 = AnyonSpecies.Particle.SpinJ(0, k)
        let j1 = AnyonSpecies.Particle.SpinJ(2, k)
        match RMatrix.computeRMatrix (AnyonSpecies.AnyonType.SU2Level k) with
        | Error e -> failwith $"R-matrix computation failed for SU(2)_%d{k}: %A{e}"
        | Ok rData ->
            let getR c =
                (RMatrix.getRSymbol rData { RMatrix.A = halfSpin; RMatrix.B = halfSpin; RMatrix.C = c }) |> Result.defaultWith (fun e -> failwith $"R-symbol lookup failed for SU(2)_%d{k}: %A{e}")
            (getR j0, getR j1)

    /// Compute the global braiding phase for a single braid generator.
    ///
    /// Each elementary braid σ_i (or σ_i⁻¹) contributes a global phase determined
    /// by the anyon type's R-matrix: the phase acquired by the VACUUM fusion
    /// channel (the |0⟩ component of the encoded qubit). The gate decomposition
    /// applies the remaining relative channel phase (a P gate), so
    /// TotalPhase · gates reproduces the exact anyonic unitary.
    ///
    /// - **Ising**: σ_i → exp(-iπ/8), σ_i⁻¹ → exp(iπ/8)
    ///   (from R[σ,σ;1] = exp(-iπ/8), Kitaev 2006 convention)
    /// - **Fibonacci**: σ_i → exp(4πi/5), σ_i⁻¹ → exp(-4πi/5)
    ///   (from R[τ,τ;1] = exp(4πi/5))
    /// - **SU(2)_k**: σ_i → R[1/2,1/2;j=0], σ_i⁻¹ → conjugate
    let braidingPhase (anyonType: AnyonSpecies.AnyonType) (isClockwise: bool) : Complex =
        match anyonType with
        | AnyonSpecies.AnyonType.Ising ->
            let angle = if isClockwise then -Math.PI / 8.0 else Math.PI / 8.0
            Complex(cos angle, sin angle)
        | AnyonSpecies.AnyonType.Fibonacci ->
            let angle = if isClockwise then 4.0 * Math.PI / 5.0 else -4.0 * Math.PI / 5.0
            Complex(cos angle, sin angle)
        | AnyonSpecies.AnyonType.SU2Level k ->
            let r0, _ = su2HalfSpinRSymbols k
            if isClockwise then r0 else Complex.Conjugate r0

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
    let isingBraidingToGates (generatorIndex: int) (isClockwise: bool) (strandCount: int) : CircuitBuilder.Gate list =
        // Braid generators use LEAF indexing (GateToBraid convention): qubit q
        // occupies leaves (2q, 2q+1), so the within-pair exchange of qubit q is
        // generator σ_{2q} (even index) and maps to S/S† on qubit q = index / 2.
        // The encoding has 2(n+1) strands: n qubit pairs plus one parity pair at
        // leaves (2n, 2n+1). Odd indices are cross-pair braids, which have no
        // single-qubit gate equivalent in this encoding.
        if generatorIndex % 2 <> 0 then
            failwith $"Braid generator σ_{generatorIndex} is a cross-pair exchange (odd leaf index) and cannot be mapped to a single-qubit gate in the Ising σ-pair encoding"
        elif generatorIndex = strandCount - 2 then
            // Within-pair exchange of the PARITY pair (leaves 2n, 2n+1): the pair's
            // fusion channel is Vacuum or ψ according to the parity of the encoded
            // data qubits, so the braid applies R[σ,σ;1] = e^{-iπ/8} to even-parity
            // terms and R[σ,σ;ψ] = e^{3iπ/8} to odd-parity terms — a relative phase
            // of i on the odd-parity subspace, NOT a global phase (the vacuum-channel
            // factor e^{-iπ/8} is what accumulateBraidingPhase tracks). Realize it by
            // computing the parity onto the last qubit with a CNOT ladder, applying
            // S (or S† for the inverse braid), and uncomputing.
            let numQubits = max 0 (strandCount / 2 - 1)
            if numQubits = 0 then
                []
            else
                let last = numQubits - 1
                let ladder = [ for j in 0 .. numQubits - 2 -> CircuitBuilder.Gate.CNOT (j, last) ]
                let phaseGate =
                    if isClockwise then CircuitBuilder.Gate.S last
                    else CircuitBuilder.Gate.SDG last
                ladder @ [ phaseGate ] @ List.rev ladder
        elif generatorIndex >= strandCount - 1 then
            failwith $"Braid generator σ_{generatorIndex} is out of range for {strandCount} strands"
        else
        let qubitIndex = generatorIndex / 2
        // Clockwise within-pair braid → S gate (relative phase +π/2)
        // Counter-clockwise → S† gate (relative phase -π/2)
        if isClockwise then
            [CircuitBuilder.Gate.S qubitIndex]
        else
            [CircuitBuilder.Gate.SDG qubitIndex]
    
    /// Map Fibonacci anyon braiding to its exact diagonal gate.
    ///
    /// σ₁ acts on the qubit fusion basis as diag(R¹_ττ, Rτ_ττ)
    /// = diag(e^{4πi/5}, e^{-3πi/5}) = e^{4πi/5} · diag(1, e^{3πi/5}):
    /// the vacuum-channel factor e^{4πi/5} is tracked by accumulateBraidingPhase
    /// and the RELATIVE channel phase is P(3π/5). (The previous code emitted the
    /// global 4π/5 as the relative angle — every braid was off by e^{iπ/5} on |1⟩.)
    let fibonacciBraidingToGates (generatorIndex: int) (isClockwise: bool) (tolerance: float) : CircuitBuilder.Gate list =
        // Fibonacci uses the same 2-leaves-per-qubit indexing as Ising (σ₁ of
        // qubit q = leaf index 2q, σ₂ = leaf index 2q+1 crossing to the auxiliary/
        // next pair). Only the within-pair σ₁ has a single-qubit diagonal action;
        // σ₂ mixes fusion channels via the F-matrix and cannot be represented as
        // a single-qubit phase gate.
        if generatorIndex % 2 <> 0 then
            failwith $"Fibonacci braid generator σ_{generatorIndex} (cross-pair σ₂ exchange) mixes fusion channels via the F-matrix and cannot be mapped to a phase gate"
        let qubitIndex = generatorIndex / 2
        // arg(Rτ/R¹) = -3π/5 - 4π/5 = -7π/5 ≡ +3π/5 (mod 2π)
        let angle =
            if isClockwise then
                3.0 * Math.PI / 5.0
            else
                -3.0 * Math.PI / 5.0
        [CircuitBuilder.Gate.P (qubitIndex, angle)]

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
    
    /// Check if two gates operate on disjoint qubit sets (commutation criterion).
    /// 
    /// Two gates commute if they act on completely disjoint sets of qubits.
    /// This is sufficient (but not necessary) for commutativity.
    let gatesCommute (g1: CircuitBuilder.Gate) (g2: CircuitBuilder.Gate) : bool =
        let q1 = getAffectedQubits g1 |> Set.ofList
        let q2 = getAffectedQubits g2 |> Set.ofList
        Set.intersect q1 q2 |> Set.isEmpty
    
    /// Commutation-based cancellation: move inverse gates towards each other
    /// through commuting intermediate gates, then cancel them.
    /// 
    /// Example: T(q0) H(q1) T†(q0) → H(q1)
    /// Because T(q0) and H(q1) act on disjoint qubits, they commute.
    /// After commutation: T(q0) T†(q0) H(q1) → H(q1)
    let commutationCancellation (gates: CircuitBuilder.Gate list) : CircuitBuilder.Gate list =
        let arr = gates |> Array.ofList
        let removed = Array.create arr.Length false
        let mutable changed = true
        
        while changed do
            changed <- false
            for i in 0 .. arr.Length - 2 do
                if not removed.[i] then
                    // Look forward for a cancelling gate, skipping over commuting ones
                    let mutable j = i + 1
                    let mutable allCommute = true
                    let mutable foundCancel = false
                    
                    while j < arr.Length && allCommute && not foundCancel do
                        if removed.[j] then
                            j <- j + 1
                        else
                            let cancels =
                                match arr.[i], arr.[j] with
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
                                removed.[i] <- true
                                removed.[j] <- true
                                foundCancel <- true
                                changed <- true
                            elif gatesCommute arr.[i] arr.[j] then
                                j <- j + 1  // Skip this gate, it commutes
                            else
                                allCommute <- false  // Blocked by non-commuting gate
        
        [ for i in 0 .. arr.Length - 1 do
            if not removed.[i] then yield arr.[i] ]
    
    /// Template matching: recognize known circuit identities and replace
    /// with more efficient equivalents.
    /// 
    /// Known patterns:
    /// - S S = Z (two S gates = one Z gate)
    /// - T T T T = Z (four T gates = one Z gate)
    /// - T T = S (two T gates = one S gate)
    /// - H Z H = X (Hadamard-Z-Hadamard = X)
    /// - H X H = Z (Hadamard-X-Hadamard = Z)
    let templateMatching (gates: CircuitBuilder.Gate list) : CircuitBuilder.Gate list =
        let rec loop acc remaining =
            match remaining with
            | [] -> List.rev acc
            | [g] -> List.rev (g :: acc)
            // S S → Z
            | CircuitBuilder.Gate.S q1 :: CircuitBuilder.Gate.S q2 :: rest when q1 = q2 ->
                loop acc (CircuitBuilder.Gate.Z q1 :: rest)
            // T T → S
            | CircuitBuilder.Gate.T q1 :: CircuitBuilder.Gate.T q2 :: rest when q1 = q2 ->
                loop acc (CircuitBuilder.Gate.S q1 :: rest)
            // TDG TDG → SDG
            | CircuitBuilder.Gate.TDG q1 :: CircuitBuilder.Gate.TDG q2 :: rest when q1 = q2 ->
                loop acc (CircuitBuilder.Gate.SDG q1 :: rest)
            // SDG SDG → Z
            | CircuitBuilder.Gate.SDG q1 :: CircuitBuilder.Gate.SDG q2 :: rest when q1 = q2 ->
                loop acc (CircuitBuilder.Gate.Z q1 :: rest)
            // H Z H → X
            | CircuitBuilder.Gate.H q1 :: CircuitBuilder.Gate.Z q2 :: CircuitBuilder.Gate.H q3 :: rest 
                when q1 = q2 && q2 = q3 ->
                loop acc (CircuitBuilder.Gate.X q1 :: rest)
            // H X H → Z
            | CircuitBuilder.Gate.H q1 :: CircuitBuilder.Gate.X q2 :: CircuitBuilder.Gate.H q3 :: rest
                when q1 = q2 && q2 = q3 ->
                loop acc (CircuitBuilder.Gate.Z q1 :: rest)
            | g :: rest ->
                loop (g :: acc) rest
        
        loop [] gates
    
    /// Aggressive optimization with commutation-based cancellation and template matching.
    /// 
    /// Applies a multi-pass strategy:
    /// 1. Basic optimization (adjacent cancellation + rotation merging)
    /// 2. Commutation-based cancellation (cancel through commuting gates)
    /// 3. Template matching (replace known patterns with simpler equivalents)
    /// 4. Final basic pass (clean up any new cancellation opportunities)
    let optimizeAggressive (gates: CircuitBuilder.Gate list) : CircuitBuilder.Gate list =
        gates
        |> optimizeBasic                // Pass 1: adjacent cancellation + rotation merge
        |> commutationCancellation      // Pass 2: cancel through commuting gates
        |> templateMatching             // Pass 3: replace known patterns
        |> optimizeBasic                // Pass 4: final cleanup
    
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
    
    /// Compile a single braid generator to gates.
    /// `strandCount` is the braid word's strand count, needed to distinguish the
    /// Ising parity-pair exchange (parity-controlled phase) from qubit exchanges.
    let compileGenerator
        (gen: BraidGroup.BraidGenerator)
        (anyonType: AnyonSpecies.AnyonType)
        (strandCount: int)
        (options: CompilationOptions) : CircuitBuilder.Gate list =

        match anyonType with
        | AnyonSpecies.AnyonType.Ising ->
            isingBraidingToGates gen.Index gen.IsClockwise strandCount

        | AnyonSpecies.AnyonType.Fibonacci ->
            fibonacciBraidingToGates gen.Index gen.IsClockwise options.ApproximationTolerance

        | AnyonSpecies.AnyonType.SU2Level k ->
            // SU(2)_k shares the 2-leaves-per-qubit indexing; within-pair (even)
            // exchanges act as σ₁ = diag(R[1/2,1/2;0], R[1/2,1/2;1]) on the qubit
            // (see SolovayKitaev.computeSU2kSigmaMatrices), cross-pair (odd)
            // exchanges mix channels via the F-matrix and have no gate equivalent.
            // The vacuum-channel factor R[1/2,1/2;0] is tracked by
            // accumulateBraidingPhase; the gate applies the relative channel phase.
            // (The previous code emitted a flat Ising-like ±π/8 placeholder.)
            if gen.Index % 2 <> 0 then
                failwith $"Braid generator σ_{gen.Index} (cross-pair exchange) cannot be mapped to a single-qubit gate for {anyonType}"
            let r0, r1 = su2HalfSpinRSymbols k
            let relative = (r1 / r0).Phase
            let angle = if gen.IsClockwise then relative else -relative
            [CircuitBuilder.Gate.P (gen.Index / 2, angle)]
    
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
                    compileGenerator gen anyonType braid.StrandCount options)

            // Apply optimization
            let optimizedGates = optimizeGates options.OptimizationLevel allGates

            // Calculate metadata
            let numQubits =
                match anyonType with
                // Ising σ-pair encoding (GateToBraid convention): 2(n+1) strands
                // for n qubits (one leaf pair per qubit plus a parity pair)
                | AnyonSpecies.AnyonType.Ising -> max 1 (braid.StrandCount / 2 - 1)
                // Fibonacci/SU(2)_k convention (fibonacciOpsToBraidWord /
                // su2kOpsToBraidWord): 2n+1 strands for n qubits
                | AnyonSpecies.AnyonType.Fibonacci | AnyonSpecies.AnyonType.SU2Level _ -> max 1 ((braid.StrandCount - 1) / 2)
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

    // ========================================================================
    // EXECUTION ON GATE BACKENDS
    //
    // The reverse of the gate->braid path used by the topological backend:
    // these helpers compile a braid word to a gate circuit so a topological
    // (braid) program can run on a gate-based simulator or gate hardware.
    // Useful for cross-validating braid programs against the gate model.
    // ========================================================================

    /// Build a gate-based circuit from a compiled braid gate sequence.
    let toCircuit (sequence: GateSequence) : CircuitBuilder.Circuit =
        let qubits = max 1 sequence.NumQubits
        CircuitBuilder.empty qubits
        |> CircuitBuilder.addGates sequence.Gates

    /// Compile a braid word directly to a gate-based circuit (braid -> gates -> Circuit).
    let compileToCircuit
        (braid: BraidGroup.BraidWord)
        (anyonType: AnyonSpecies.AnyonType)
        (options: CompilationOptions)
        : Result<CircuitBuilder.Circuit, TopologicalError> =
        compileToGates braid anyonType options
        |> Result.map toCircuit

    /// Execute a braid on a GATE-based backend by compiling it to a gate circuit
    /// and running it. This is the wired braid->gate path: a topological program
    /// can be run/validated on any IQuantumBackend (local simulator or gate cloud).
    let executeOnGateBackend
        (backend: Core.BackendAbstraction.IQuantumBackend)
        (braid: BraidGroup.BraidWord)
        (anyonType: AnyonSpecies.AnyonType)
        (options: CompilationOptions) =
        compileToCircuit braid anyonType options
        |> Result.bind (fun circuit ->
            (backend.ExecuteToState (Core.CircuitAbstraction.wrapCircuit circuit)) |> Result.mapError (fun qerr -> TopologicalError.BackendError ("gate-backend", qerr.Message)))
