namespace FSharp.Azure.Quantum.Topological

/// Braid Group Representation Module for Topological Quantum Computing
///
/// The braid group B_n describes all possible ways to braid n strands (anyons) in 2D space.
/// Each braid can be built from elementary generators σ_i (exchange strands i and i+1).
///
/// Key Properties:
/// - **Generators**: σ_1, σ_2, ..., σ_{n-1} (elementary exchanges)
/// - **Relations**: 
///   - Far commutativity: σ_i σ_j = σ_j σ_i for |i-j| ≥ 2
///   - Yang-Baxter: σ_i σ_{i+1} σ_i = σ_{i+1} σ_i σ_{i+1}
/// - **Inverses**: σ_i^{-1} corresponds to braiding in opposite direction
///
/// Physical Interpretation:
/// - σ_i = braid strand i under strand i+1 (clockwise)
/// - σ_i^{-1} = braid strand i over strand i+1 (counter-clockwise)
/// - Composition = sequential braiding operations
///
/// Mathematical Reference:
/// - Kassel & Turaev "Braid Groups" (2008)
/// - Freedman et al. "Topological Quantum Computation" (2003)
[<RequireQualifiedAccess>]
module BraidGroup =
    
    open System.Numerics
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Elementary braid generator σ_i or its inverse σ_i^{-1}
    type BraidGenerator = {
        /// Index i (braids strands i and i+1, where 0 ≤ i < n-1)
        Index: int
        
        /// true = σ_i (clockwise), false = σ_i^{-1} (counter-clockwise)
        IsClockwise: bool
    }
    
    /// A braid word: sequence of elementary generators
    /// Example: σ_1 σ_2 σ_1^{-1} = [σ_1, σ_2, σ_1^{-1}]
    type BraidWord = {
        /// Number of strands
        StrandCount: int
        
        /// Sequence of generators (left to right execution order)
        Generators: BraidGenerator list
    }
    
    /// Result of applying a braid to an anyon state
    type BraidApplicationResult = {
        /// The braid that was applied
        AppliedBraid: BraidWord
        
        /// Resulting quantum amplitude (from R-matrices)
        Phase: Complex
        
        /// Execution steps for debugging
        Steps: string list
    }
    
    // ========================================================================
    // BRAID WORD CONSTRUCTION
    // ========================================================================
    
    /// Create an elementary generator σ_i (clockwise)
    let sigma (i: int) : BraidGenerator =
        { Index = i; IsClockwise = true }
    
    /// Create an inverse generator σ_i^{-1} (counter-clockwise)
    let sigmaInv (i: int) : BraidGenerator =
        { Index = i; IsClockwise = false }
    
    /// Create an empty braid (identity) for n strands
    let identity (n: int) : TopologicalResult<BraidWord> =
        if n < 2 then
            TopologicalResult.validationError "strandCount" "Must have at least 2 strands"
        else
            Ok { StrandCount = n; Generators = [] }
    
    /// Create a braid word from a list of generators
    let fromGenerators (n: int) (generators: BraidGenerator list) : TopologicalResult<BraidWord> =
        // Validate all generator indices
        let invalidGen = generators |> List.tryFind (fun g -> g.Index < 0 || g.Index >= n - 1)
        
        match invalidGen with
        | Some g -> 
            TopologicalResult.validationError "generatorIndex" $"Generator index {g.Index} out of range for {n} strands (valid: 0 to {n-2})"
        | None ->
            Ok { StrandCount = n; Generators = generators }
    
    /// Compose two braids (sequential application: first then second)
    let compose (first: BraidWord) (second: BraidWord) : TopologicalResult<BraidWord> =
        if first.StrandCount <> second.StrandCount then
            TopologicalResult.validationError "strandCount" $"Cannot compose braids with different strand counts: {first.StrandCount} ≠ {second.StrandCount}"
        else
            Ok {
                StrandCount = first.StrandCount
                Generators = first.Generators @ second.Generators
            }
    
    /// Invert a braid (reverse order and flip all generators)
    let inverse (braid: BraidWord) : BraidWord =
        {
            StrandCount = braid.StrandCount
            Generators = 
                braid.Generators 
                |> List.rev 
                |> List.map (fun g -> { g with IsClockwise = not g.IsClockwise })
        }
    
    // ========================================================================
    // BRAID GROUP RELATIONS
    // ========================================================================
    
    /// Check if two generators commute (far commutativity relation)
    /// σ_i and σ_j commute if |i - j| ≥ 2
    let doCommute (g1: BraidGenerator) (g2: BraidGenerator) : bool =
        abs (g1.Index - g2.Index) >= 2
    
    /// Check if three consecutive generators satisfy Yang-Baxter equation
    /// σ_i σ_{i+1} σ_i = σ_{i+1} σ_i σ_{i+1}
    let isYangBaxterTriple (g1: BraidGenerator) (g2: BraidGenerator) (g3: BraidGenerator) : bool =
        // All must be clockwise (or all counter-clockwise) for this simple check
        let allClockwise = g1.IsClockwise && g2.IsClockwise && g3.IsClockwise
        let allCounter = not g1.IsClockwise && not g2.IsClockwise && not g3.IsClockwise
        
        if not (allClockwise || allCounter) then
            false
        else
            // Check pattern: i, i+1, i or i+1, i, i+1
            (g1.Index = g3.Index && g2.Index = g1.Index + 1) ||
            (g1.Index = g3.Index && g2.Index = g1.Index - 1)
    
    /// Simplify a braid word using braid relations
    /// - Remove σ_i σ_i^{-1} pairs (cancellation)
    /// - Apply far commutativity
    /// Returns simplified braid (may not be fully reduced)
    let simplify (braid: BraidWord) : BraidWord =
        let rec removeAdjacentInverses (gens: BraidGenerator list) : BraidGenerator list =
            match gens with
            | [] -> []
            | [g] -> [g]
            | g1 :: g2 :: rest ->
                // Check if g1 and g2 are inverses of each other
                if g1.Index = g2.Index && g1.IsClockwise <> g2.IsClockwise then
                    // Cancel them out
                    removeAdjacentInverses rest
                else
                    g1 :: removeAdjacentInverses (g2 :: rest)
        
        { braid with Generators = removeAdjacentInverses braid.Generators }
    
    // ========================================================================
    // APPLYING BRAIDS TO ANYON STATES
    // ========================================================================
    
    /// Apply a single generator to anyon state, computing R-matrix phase
    /// 
    /// This applies the R-matrix for braiding anyons at positions i and i+1:
    /// - For σ_i: multiply by R[a_i, a_{i+1}; c]
    /// - For σ_i^{-1}: multiply by R[a_i, a_{i+1}; c]^{-1} = R[a_i, a_{i+1}; c]*
    let private applyGeneratorWithRData
        (generator: BraidGenerator)
        (anyons: AnyonSpecies.Particle list)
        (fusionChannel: AnyonSpecies.Particle)
        (rData: RMatrix.RMatrixData)
        : TopologicalResult<Complex> =
        
        let i = generator.Index
        
        // Validate index
        if i < 0 || i >= anyons.Length - 1 then
            TopologicalResult.logicError "operation" $"Generator index {i} out of range for {anyons.Length} anyons"
        else
            // Get anyons being braided
            let a = anyons.[i]
            let b = anyons.[i + 1]
            
            // Get R-matrix element
            let rIndex = { RMatrix.A = a; RMatrix.B = b; RMatrix.C = fusionChannel }
            match RMatrix.getRSymbol rData rIndex with
            | Error err -> Error err
            | Ok rValue ->
                // For inverse, take complex conjugate (since R is unitary)
                let phase = if generator.IsClockwise then rValue else Complex.Conjugate rValue
                Ok phase

    /// Apply a single generator to anyon state, computing R-matrix phase
    /// 
    /// Public wrapper that computes the R-matrix. For bulk operations,
    /// prefer applyBraid which computes R-matrix once.
    let applyGenerator
        (generator: BraidGenerator)
        (anyons: AnyonSpecies.Particle list)
        (fusionChannel: AnyonSpecies.Particle)
        (anyonType: AnyonSpecies.AnyonType)
        : TopologicalResult<Complex> =
        
        match RMatrix.computeRMatrix anyonType with
        | Error err -> Error err
        | Ok rData -> applyGeneratorWithRData generator anyons fusionChannel rData
    
    /// Apply a full braid word to anyon state
    /// 
    /// Computes the total phase accumulated by braiding according to the braid word.
    /// This is the product of all R-matrix elements for each generator.
    let applyBraid
        (braid: BraidWord)
        (anyons: AnyonSpecies.Particle list)
        (fusionChannel: AnyonSpecies.Particle)
        (anyonType: AnyonSpecies.AnyonType)
        : TopologicalResult<BraidApplicationResult> =
        
        // Validate input
        if anyons.Length <> braid.StrandCount then
            TopologicalResult.logicError "operation" $"Braid has {braid.StrandCount} strands but {anyons.Length} anyons provided"
        else
            // Compute R-matrix once for the entire braid word
            match RMatrix.computeRMatrix anyonType with
            | Error err -> Error err
            | Ok rData ->
            
            // Apply each generator sequentially, accumulating phase.
            // Steps are collected in reverse (O(1) prepend) and reversed at the end.
            let rec applyGenerators (gens: BraidGenerator list) (currentPhase: Complex) (revSteps: string list) =
                match gens with
                | [] -> Ok (currentPhase, List.rev revSteps)
                | g :: rest ->
                    match applyGeneratorWithRData g anyons fusionChannel rData with
                    | Error err -> Error err
                    | Ok genPhase ->
                        let newPhase = currentPhase * genPhase
                        let step = 
                            let dir = if g.IsClockwise then "σ" else "σ⁻¹"
                            $"{dir}_{g.Index}: phase × {genPhase.Magnitude:F3}∠{System.Math.Atan2(genPhase.Imaginary, genPhase.Real) * 180.0 / System.Math.PI:F1}°"
                        applyGenerators rest newPhase (step :: revSteps)
            
            match applyGenerators braid.Generators Complex.One [] with
            | Error err -> Error err
            | Ok (finalPhase, steps) ->
                Ok {
                    AppliedBraid = braid
                    Phase = finalPhase
                    Steps = steps
                }
    
    // ========================================================================
    // WELL-KNOWN BRAIDS
    // ========================================================================
    
    /// Full twist: σ_1 σ_2 ... σ_{n-1} σ_1 σ_2 ... σ_{n-1}
    /// All strands rotate 360° around center
    let fullTwist (n: int) : TopologicalResult<BraidWord> =
        if n < 2 then
            TopologicalResult.logicError "operation" "Full twist requires at least 2 strands"
        else
            let generators = 
                [
                    for _ in 1..2 do
                    for i in 0 .. n-2 do
                        yield sigma i
                ]
            Ok { StrandCount = n; Generators = generators }
    
    /// Exchange two adjacent strands: σ_i
    let exchange (i: int) (n: int) : TopologicalResult<BraidWord> =
        fromGenerators n [sigma i]
    
    /// Full exchange (swap) of strands i and i+1: σ_i
    /// Note: In anyon physics, a single crossing already performs the exchange
    let swap (i: int) (n: int) : TopologicalResult<BraidWord> =
        exchange i n
    
    /// Cyclic permutation: move strand 0 to position n-1
    /// σ_{n-2} σ_{n-3} ... σ_1 σ_0
    let cyclicPermutation (n: int) : TopologicalResult<BraidWord> =
        if n < 2 then
            TopologicalResult.logicError "operation" "Cyclic permutation requires at least 2 strands"
        else
            let generators = [ for i in n-2 .. -1 .. 0 -> sigma i ]
            Ok { StrandCount = n; Generators = generators }
    
    // ========================================================================
    // VERIFICATION
    // ========================================================================
    
    /// Verify that composing a braid with its inverse gives identity (trivial phase)
    let verifyInverse
        (braid: BraidWord)
        (anyons: AnyonSpecies.Particle list)
        (fusionChannel: AnyonSpecies.Particle)
        (anyonType: AnyonSpecies.AnyonType)
        : TopologicalResult<bool> =
        
        let invBraid = inverse braid
        
        match compose braid invBraid with
        | Error err -> Error err
        | Ok composition ->
        
        match applyBraid composition anyons fusionChannel anyonType with
        | Error err -> Error err
        | Ok result ->
            // Phase should be ~1 (identity)
            let deviation = (result.Phase - Complex.One).Magnitude
            Ok (deviation < 1e-9)
    
    /// Verify Yang-Baxter equation: σ_i σ_{i+1} σ_i = σ_{i+1} σ_i σ_{i+1}
    /// for a single fusion channel.
    let verifyYangBaxter
        (i: int)
        (n: int)
        (anyons: AnyonSpecies.Particle list)
        (fusionChannel: AnyonSpecies.Particle)
        (anyonType: AnyonSpecies.AnyonType)
        : TopologicalResult<bool> =
        
        if i < 0 || i >= n - 2 then
            TopologicalResult.logicError "operation" $"Yang-Baxter check requires 0 ≤ i < {n-2}, got {i}"
        else
            // Left side: σ_i σ_{i+1} σ_i
            let leftGens = [sigma i; sigma (i+1); sigma i]
            
            // Right side: σ_{i+1} σ_i σ_{i+1}
            let rightGens = [sigma (i+1); sigma i; sigma (i+1)]
            
            match fromGenerators n leftGens, fromGenerators n rightGens with
            | Ok leftBraid, Ok rightBraid ->
                match applyBraid leftBraid anyons fusionChannel anyonType,
                      applyBraid rightBraid anyons fusionChannel anyonType with
                | Ok leftResult, Ok rightResult ->
                    let deviation = (leftResult.Phase - rightResult.Phase).Magnitude
                    Ok (deviation < 1e-9)
                | Error err, _ | _, Error err -> Error err
            | Error err, _ | _, Error err -> Error err

    /// Compute all valid pairwise fusion channels for anyons at positions
    /// involved in the Yang-Baxter braiding (positions i, i+1, i+2).
    ///
    /// The fusionChannel parameter in applyBraid represents the pairwise
    /// intermediate channel c in R[a,b;c]. For a valid Yang-Baxter test,
    /// we need all c such that a_i × a_{i+1} → c is allowed.
    let private pairwiseFusionChannels
        (anyons: AnyonSpecies.Particle list)
        (i: int)
        (anyonType: AnyonSpecies.AnyonType)
        : TopologicalResult<AnyonSpecies.Particle list> =
        
        let a = anyons.[i]
        let b = anyons.[i + 1]
        FusionRules.channels a b anyonType

    /// Verify Yang-Baxter equation across ALL valid fusion channels.
    ///
    /// This is the comprehensive version: instead of testing a single
    /// fusion channel, it computes all valid pairwise fusion channels
    /// for the anyons being braided and verifies the Yang-Baxter
    /// equation holds for each.
    ///
    /// Returns Ok true only if the equation is satisfied for every channel.
    /// Returns the first failure details if any channel violates Yang-Baxter.
    let verifyYangBaxterAllChannels
        (i: int)
        (n: int)
        (anyons: AnyonSpecies.Particle list)
        (anyonType: AnyonSpecies.AnyonType)
        : TopologicalResult<bool> =
        
        if i < 0 || i >= n - 2 then
            TopologicalResult.logicError "operation" $"Yang-Baxter check requires 0 ≤ i < {n-2}, got {i}"
        else
            match pairwiseFusionChannels anyons i anyonType with
            | Error err -> Error err
            | Ok channels ->
                channels
                |> List.fold (fun acc channel ->
                    match acc with
                    | Error err -> Error err
                    | Ok false -> Ok false
                    | Ok true -> verifyYangBaxter i n anyons channel anyonType
                ) (Ok true)
    
    // ========================================================================
    // DISPLAY UTILITIES
    // ========================================================================
    
    /// Format a generator for display
    let private formatGenerator (g: BraidGenerator) : string =
        if g.IsClockwise then
            $"σ_{g.Index}"
        else
            $"σ_{g.Index}⁻¹"
    
    /// Display a braid word in mathematical notation
    let displayBraid (braid: BraidWord) : string =
        if braid.Generators.IsEmpty then
            $"Identity ({braid.StrandCount} strands)"
        else
            let word = 
                braid.Generators 
                |> List.map formatGenerator 
                |> String.concat " "
            $"{word} ({braid.StrandCount} strands)"
    
    /// Display braid application result with phase information
    let displayBraidResult (result: BraidApplicationResult) : string =
        let magnitude = result.Phase.Magnitude
        let angle = System.Math.Atan2(result.Phase.Imaginary, result.Phase.Real) * 180.0 / System.Math.PI
        
        let header = $"Braid: {displayBraid result.AppliedBraid}"
        let phaseInfo = $"Final Phase: {magnitude:F6}∠{angle:F2}°"
        
        let steps =
            if result.Steps.IsEmpty then
                ""
            else
                "Steps:\n  " + String.concat "\n  " result.Steps
        
        header + "\n" + phaseInfo + "\n" + steps
