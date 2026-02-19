namespace FSharp.Azure.Quantum.Topological

/// F-symbols (6j symbols) for topological quantum field theories
/// 
/// The F-symbols encode the associativity of fusion. They relate different
/// bases for the fusion tree Hilbert space when fusing three anyons.
/// 
/// For three anyons a, b, c fusing to total charge d:
///   (a × b) × c = Σ_e F[a,b,c,d;e,f] × a × (b × c)
/// 
/// where the sum is over intermediate states e and f.
/// 
/// Key properties (from Simon's "Topological Quantum", Chapter 18):
/// - F-symbols are unitary matrices
/// - Must satisfy pentagon equations (consistency)
/// - Determine all topological properties together with R-matrices
/// - For Ising anyons: Very simple (mostly ±1)
/// - For Fibonacci: Involve golden ratio φ = (1+√5)/2
/// 
/// References:
/// - Steven H. Simon, "Topological Quantum" (2023), Chapter 18
/// - Kitaev, "Anyons in an exactly solved model"
[<RequireQualifiedAccess>]
module FMatrix =
    
    open System
    open System.Numerics
    
    // ========================================================================
    // F-SYMBOL TYPES
    // ========================================================================
    
    /// F-symbol index specification
    /// 
    /// F[a,b,c,d;e,f] represents the matrix element for transforming
    /// between two different fusion orderings:
    /// - Left: ((a × b)→e × c)→d
    /// - Right: (a × (b × c)→f)→d
    type FSymbolIndex = {
        /// First anyon being fused
        A: AnyonSpecies.Particle
        
        /// Second anyon being fused
        B: AnyonSpecies.Particle
        
        /// Third anyon being fused
        C: AnyonSpecies.Particle
        
        /// Final fusion outcome
        D: AnyonSpecies.Particle
        
        /// Intermediate state in left-associative fusion: (a × b)→e
        E: AnyonSpecies.Particle
        
        /// Intermediate state in right-associative fusion: (b × c)→f
        F: AnyonSpecies.Particle
    }
    
    /// F-matrix data for an anyon theory
    /// 
    /// Contains all F-symbols needed for basis transformations.
    /// Stored as a dictionary for efficient lookup.
    type FMatrixData = {
        /// Anyon type this F-matrix applies to
        AnyonType: AnyonSpecies.AnyonType
        
        /// F-symbols indexed by (a,b,c,d,e,f)
        /// Only stores non-zero entries for efficiency
        FSymbols: Map<FSymbolIndex, Complex>
        
        /// Whether this F-matrix data has been validated
        IsValidated: bool
    }
    
    // ========================================================================
    // ISING F-SYMBOLS
    // ========================================================================
    
    /// Compute F-symbols for Ising anyons
    /// 
    /// Ising F-symbols are particularly simple - all are ±1 or 0.
    /// 
    /// From Simon Table 18.1:
    /// For Ising anyons, the non-trivial F-symbols are all for σ×σ×σ→σ:
    /// - F[σ,σ,σ,σ;1,1] = 1/√2
    /// - F[σ,σ,σ,σ;1,ψ] = 1/√2
    /// - F[σ,σ,σ,σ;ψ,1] = 1/√2
    /// - F[σ,σ,σ,σ;ψ,ψ] = -1/√2
    /// 
    /// Note: σ×σ can fuse to either 1 or ψ, so e,f ∈ {1, ψ}
    /// All other F-symbols involving compatible fusion channels are 1.
    let computeIsingFSymbols () : Map<FSymbolIndex, Complex> =
        let invSqrt2 = Complex(1.0 / sqrt 2.0, 0.0)
        let minusInvSqrt2 = Complex(-1.0 / sqrt 2.0, 0.0)
        
        let sigma = AnyonSpecies.Particle.Sigma
        let psi = AnyonSpecies.Particle.Psi
        let vacuum = AnyonSpecies.Particle.Vacuum
        
        // Build map of non-trivial F-symbols
        // Trivial ones (= 1) can be computed on demand
        Map.ofList [
            // F[σ,σ,σ,σ;1,1] = 1/√2
            { A = sigma; B = sigma; C = sigma; D = sigma; E = vacuum; F = vacuum }, invSqrt2
            
            // F[σ,σ,σ,σ;1,ψ] = 1/√2  
            { A = sigma; B = sigma; C = sigma; D = sigma; E = vacuum; F = psi }, invSqrt2
            
            // F[σ,σ,σ,σ;ψ,1] = 1/√2
            { A = sigma; B = sigma; C = sigma; D = sigma; E = psi; F = vacuum }, invSqrt2
            
            // F[σ,σ,σ,σ;ψ,ψ] = -1/√2
            { A = sigma; B = sigma; C = sigma; D = sigma; E = psi; F = psi }, minusInvSqrt2
            
            // F[σ,ψ,σ,ψ;σ,σ] = -1
            { A = sigma; B = psi; C = sigma; D = psi; E = sigma; F = sigma }, Complex(-1.0, 0.0)
            
            // F[ψ,σ,ψ,σ;σ,σ] = -1
            { A = psi; B = sigma; C = psi; D = sigma; E = sigma; F = sigma }, Complex(-1.0, 0.0)
        ]
    
    // ========================================================================
    // FIBONACCI F-SYMBOLS
    // ========================================================================
    
    /// Compute F-symbols for Fibonacci anyons
    /// 
    /// Fibonacci F-symbols involve the golden ratio φ = (1+√5)/2.
    /// 
    /// From Simon Table 18.2:
    /// - F[τ,τ,τ,1;τ,τ] = φ⁻¹
    /// - F[τ,τ,τ,τ;1,τ] = F[τ,τ,τ,τ;τ,1] = φ⁻¹/²
    /// - F[τ,τ,τ,τ;τ,τ] = -φ⁻¹
    /// 
    /// where φ = (1+√5)/2 is the golden ratio.
    let computeFibonacciFSymbols () : Map<FSymbolIndex, Complex> =
        let phi = (1.0 + sqrt 5.0) / 2.0  // Golden ratio
        let phiInv = 1.0 / phi
        let sqrtPhiInv = sqrt phiInv
        
        let tau = AnyonSpecies.Particle.Tau
        let vacuum = AnyonSpecies.Particle.Vacuum
        
        Map.ofList [
            // F^{τττ}_1 matrix (1×1: only e=τ, f=τ is fusion-valid)
            // F[τ,τ,τ,1;τ,τ] = 1 (1×1 unitary matrix must have magnitude 1)
            { A = tau; B = tau; C = tau; D = vacuum; E = tau; F = tau }, 
            Complex(1.0, 0.0)
            
            // F^{τττ}_τ matrix (2×2 in the {1,τ} basis)
            // From Simon "Topological Quantum", Table 9.5:
            //   F^{τττ}_τ = [[φ⁻¹, φ⁻¹/²], [φ⁻¹/², -φ⁻¹]]
            
            // F[τ,τ,τ,τ;1,1] = φ⁻¹
            { A = tau; B = tau; C = tau; D = tau; E = vacuum; F = vacuum }, 
            Complex(phiInv, 0.0)
            
            // F[τ,τ,τ,τ;1,τ] = φ⁻¹/²
            { A = tau; B = tau; C = tau; D = tau; E = vacuum; F = tau }, 
            Complex(sqrtPhiInv, 0.0)
            
            // F[τ,τ,τ,τ;τ,1] = φ⁻¹/²
            { A = tau; B = tau; C = tau; D = tau; E = tau; F = vacuum }, 
            Complex(sqrtPhiInv, 0.0)
            
            // F[τ,τ,τ,τ;τ,τ] = -φ⁻¹
            { A = tau; B = tau; C = tau; D = tau; E = tau; F = tau }, 
            Complex(-phiInv, 0.0)
        ]
    
    // ========================================================================
    // GENERAL SU(2)_k F-SYMBOLS VIA QUANTUM 6j-SYMBOLS
    // ========================================================================
    
    // The quantum 6j-symbol (q-Racah coefficient) for SU(2)_k with
    // q = exp(iπ/(k+2)). All spin labels j are half-integers stored as
    // j_doubled (integers 0..k), so actual spin = j_doubled / 2.
    //
    // Reference: Kirillov & Reshetikhin (1989), "Representations of the
    // algebra U_q(sl(2)), q-orthogonal polynomials and invariants of links"
    // Also: Simon "Topological Quantum", Chapter 18.6
    
    /// Quantum number [n]_q = sin(n * π / (k+2)) / sin(π / (k+2))
    /// where q = exp(iπ/(k+2)).
    ///
    /// n is a non-negative integer. Returns a real number.
    let private qNumber (n: int) (k: int) : float =
        let denom = float (k + 2)
        let sinDenom = sin (Math.PI / denom)
        if abs sinDenom < 1e-15 then 0.0
        else sin (float n * Math.PI / denom) / sinDenom
    
    /// Quantum factorial [n]_q! = [1]_q * [2]_q * ... * [n]_q
    /// Returns the natural logarithm of |[n]_q!| and the sign (+1 or -1).
    ///
    /// Using log-space avoids overflow/underflow for large k.
    /// [0]_q! = 1 by convention.
    let private qFactorialLogSigned (n: int) (k: int) : float * float =
        if n <= 0 then (0.0, 1.0)  // log(1) = 0, sign = +1
        else
            let mutable logMag = 0.0
            let mutable sign = 1.0
            for i in 1 .. n do
                let qi = qNumber i k
                if qi < 0.0 then
                    sign <- -sign
                    logMag <- logMag + log (abs qi)
                elif qi > 0.0 then
                    logMag <- logMag + log qi
                else
                    // [i]_q = 0 means the factorial is zero
                    // Return -infinity to signal zero
                    logMag <- -infinity
                    sign <- 0.0
            (logMag, sign)
    
    /// Check triangle inequality: can spins (a, b, c) form a valid triad?
    /// All arguments are j_doubled values. Returns true if:
    /// 1. |a - b| <= c <= a + b (triangle inequality)
    /// 2. a + b + c is even (integer total spin)
    /// 3. (a + b + c) / 2 <= k (truncation for SU(2)_k)
    let private isValidTriad (a_d: int) (b_d: int) (c_d: int) (k: int) : bool =
        let sum = a_d + b_d + c_d
        sum % 2 = 0
        && c_d >= abs (a_d - b_d)
        && c_d <= a_d + b_d
        && sum / 2 <= k
    
    /// Log of triangle coefficient Δ(a,b,c) squared.
    /// Δ(a,b,c)² = [s-a]_q! * [s-b]_q! * [s-c]_q! / [s+1]_q!
    /// where s = (a + b + c) / 2, and a,b,c are j_doubled values
    /// converted to integer spin sums.
    ///
    /// Returns (log|Δ²|, sign).
    let private triangleCoefficientLogSigned 
        (a_d: int) (b_d: int) (c_d: int) (k: int) : float * float =
        // s = (a_d + b_d + c_d) / 2 in j_doubled units 
        // = total spin in integer units
        let s = (a_d + b_d + c_d) / 2
        // Arguments to q-factorials are in integer units
        let (log1, sgn1) = qFactorialLogSigned (s - a_d) k   // [s - a]_q!
        let (log2, sgn2) = qFactorialLogSigned (s - b_d) k   // [s - b]_q!
        let (log3, sgn3) = qFactorialLogSigned (s - c_d) k   // [s - c]_q!
        let (log4, sgn4) = qFactorialLogSigned (s + 1) k     // [s + 1]_q!
        let logVal = log1 + log2 + log3 - log4
        let sgnVal = sgn1 * sgn2 * sgn3 * sgn4
        (logVal, sgnVal)
    
    /// Compute the quantum 6j-symbol { j1 j2 j3 } for SU(2)_k.
    ///                                { j4 j5 j6 }
    ///
    /// All arguments are j_doubled values (integers 0..k).
    /// The triads that must be valid are: (j1,j2,j3), (j1,j5,j6), 
    /// (j4,j2,j6), (j4,j5,j3).
    ///
    /// Formula (q-deformed Racah):
    ///   {6j} = Δ(j1,j2,j3) * Δ(j1,j5,j6) * Δ(j4,j2,j6) * Δ(j4,j5,j3)
    ///          × Σ_z (-1)^z * [z+1]_q! / (product of 7 q-factorials)
    ///
    /// where z ranges over integers such that all factorial arguments 
    /// are non-negative.
    let private quantum6jSymbol
        (j1_d: int) (j2_d: int) (j3_d: int)
        (j4_d: int) (j5_d: int) (j6_d: int)
        (k: int) : float =
        
        // Validate all four triads
        if not (isValidTriad j1_d j2_d j3_d k) then 0.0
        elif not (isValidTriad j1_d j5_d j6_d k) then 0.0
        elif not (isValidTriad j4_d j2_d j6_d k) then 0.0
        elif not (isValidTriad j4_d j5_d j3_d k) then 0.0
        else
        
        // Half-sums of triads (in integer units, since j_doubled sums are even)
        let s1 = (j1_d + j2_d + j3_d) / 2
        let s2 = (j1_d + j5_d + j6_d) / 2
        let s3 = (j4_d + j2_d + j6_d) / 2
        let s4 = (j4_d + j5_d + j3_d) / 2
        
        // Triangle coefficients (log-space)
        let (logD1, sgnD1) = triangleCoefficientLogSigned j1_d j2_d j3_d k
        let (logD2, sgnD2) = triangleCoefficientLogSigned j1_d j5_d j6_d k
        let (logD3, sgnD3) = triangleCoefficientLogSigned j4_d j2_d j6_d k
        let (logD4, sgnD4) = triangleCoefficientLogSigned j4_d j5_d j3_d k
        
        // Prefactor = sqrt(Δ1² * Δ2² * Δ3² * Δ4²) = sqrt of product
        let logDeltaProduct = logD1 + logD2 + logD3 + logD4
        let sgnDeltaProduct = sgnD1 * sgnD2 * sgnD3 * sgnD4
        
        if sgnDeltaProduct = 0.0 || logDeltaProduct = -infinity then 0.0
        else
        
        // z ranges: z must satisfy all 7 factorial arguments >= 0
        // The 7 denominators are:
        //   [z - s1]!, [z - s2]!, [z - s3]!, [z - s4]!,
        //   [s1 + s2 + s3 - z - j3_d]! = [... - z]! (but let's use the standard form)
        //
        // Standard Racah formula denominators:
        //   D1 = z - s1,  D2 = z - s2,  D3 = z - s3,  D4 = z - s4
        //   D5 = s1 + s2 - j4_d - j5_d - z  (= j1_d + j2_d + j5_d + j6_d)/2 - z ... no)
        //
        // Using the standard parameterization with integer z:
        //   z ranges from max(s1, s2, s3, s4) to min(s1+s2-j4_d-j5_d+j3_d+j6_d, ...)
        //
        // The 7 q-factorial arguments in the denominator are:
        //   (z - s1), (z - s2), (z - s3), (z - s4),
        //   (s1 + s2 - z) = (j1_d + j2_d + j5_d + j6_d)/2 - z,
        //   (s1 + s3 - z) = (j1_d + j2_d + j4_d + j2_d + j6_d)/2 - z ... 
        //
        // Let me use the correct standard form. The upper limits come from:
        //   A = (j1_d + j2_d + j4_d + j5_d) / 2  
        //   B = (j2_d + j3_d + j5_d + j6_d) / 2
        //   C = (j1_d + j3_d + j4_d + j6_d) / 2
        // z_max = min(A, B, C)
        
        let bigA = (j1_d + j2_d + j4_d + j5_d) / 2
        let bigB = (j2_d + j3_d + j5_d + j6_d) / 2
        let bigC = (j1_d + j3_d + j4_d + j6_d) / 2
        
        let zMin = max (max s1 s2) (max s3 s4)
        let zMax = min (min bigA bigB) bigC
        
        if zMin > zMax then 0.0
        else
        
        // Sum over z in log-space, accumulating in linear space
        // Each term: (-1)^z * [z+1]_q! / ([z-s1]! [z-s2]! [z-s3]! [z-s4]! [A-z]! [B-z]! [C-z]!)
        let mutable sumReal = 0.0
        
        for z in zMin .. zMax do
            let (logNum, sgnNum) = qFactorialLogSigned (z + 1) k
            let (logD1z, sgnD1z) = qFactorialLogSigned (z - s1) k
            let (logD2z, sgnD2z) = qFactorialLogSigned (z - s2) k
            let (logD3z, sgnD3z) = qFactorialLogSigned (z - s3) k
            let (logD4z, sgnD4z) = qFactorialLogSigned (z - s4) k
            let (logD5z, sgnD5z) = qFactorialLogSigned (bigA - z) k
            let (logD6z, sgnD6z) = qFactorialLogSigned (bigB - z) k
            let (logD7z, sgnD7z) = qFactorialLogSigned (bigC - z) k
            
            let logDenom = logD1z + logD2z + logD3z + logD4z + logD5z + logD6z + logD7z
            let sgnDenom = sgnD1z * sgnD2z * sgnD3z * sgnD4z * sgnD5z * sgnD6z * sgnD7z
            
            if sgnDenom <> 0.0 && sgnNum <> 0.0 then
                let zSign = if z % 2 = 0 then 1.0 else -1.0
                let termSign = zSign * sgnNum * sgnDenom
                let termMag = exp (logNum - logDenom)
                sumReal <- sumReal + termSign * termMag
        
        // Full result: prefactor * sum
        // prefactor = sqrt(|Δ1² * Δ2² * Δ3² * Δ4²|) with overall sign
        let prefactorMag = exp (logDeltaProduct / 2.0)
        let prefactorSign = 
            if sgnDeltaProduct >= 0.0 then 1.0 else -1.0
        
        prefactorSign * prefactorMag * sumReal
    
    /// Convert an F-symbol index (with external legs a,b,c,d and intermediates e,f)
    /// to the quantum 6j-symbol convention.
    ///
    /// F^{abc}_{d;ef} relates fusion trees:
    ///   Left:  ((a × b) → e) × c → d
    ///   Right: a × ((b × c) → f) → d
    ///
    /// In the Turaev-Viro / TQFT convention, the unitary F-matrix is:
    ///   F^{abc}_{d;ef} = (-1)^{(a+b+c+d)/2} * sqrt([2e+1]_q * [2f+1]_q) * { a  b  e }
    ///                                                                        { c  d  f }
    ///
    /// The sqrt factors convert from the 6j-symbol (which is not unitary)
    /// to the unitary F-matrix needed for anyon braiding. The phase factor
    /// (-1)^{(a+b+c+d)/2} ensures correct signs for the pentagon equation,
    /// particularly when half-integer spins are present.
    
    /// Extract j_doubled from a particle, returning 0 for Vacuum.
    let private getJDoubled (p: AnyonSpecies.Particle) : int =
        match p with
        | AnyonSpecies.Particle.Vacuum -> 0
        | AnyonSpecies.Particle.SpinJ (jd, _) -> jd
        | _ -> 0  // Should not occur for SU(2)_k
    
    /// Compute all F-symbols for SU(2)_k using quantum 6j-symbols.
    ///
    /// Enumerates all valid 6-tuples (a,b,c,d,e,f) where:
    ///   - a × b → e is a valid fusion
    ///   - e × c → d is a valid fusion  
    ///   - b × c → f is a valid fusion
    ///   - a × f → d is a valid fusion
    ///
    /// The F-symbol is computed as:
    ///   F^{abc}_{d;ef} = sqrt([2e+1]_q * [2f+1]_q) * { a  b  e }
    ///                                                  { c  d  f }
    ///
    /// where the 6j-symbol uses j_doubled values internally.
    let private computeSU2kFSymbols (k: int) : Map<FSymbolIndex, Complex> =
        // Get all particles for this level
        let particles = 
            match AnyonSpecies.particles (AnyonSpecies.AnyonType.SU2Level k) with
            | Ok ps -> ps
            | Error _ -> []
        
        // For each particle, get fusion channels
        let fusionChannels (p1: AnyonSpecies.Particle) (p2: AnyonSpecies.Particle) =
            match FusionRules.channels p1 p2 (AnyonSpecies.AnyonType.SU2Level k) with
            | Ok channels -> channels
            | Error _ -> []
        
        // Build all valid F-symbol entries
        let mutable symbols = Map.empty
        
        for a in particles do
            for b in particles do
                let eChannels = fusionChannels a b
                for c in particles do
                    let fChannels = fusionChannels b c
                    for e in eChannels do
                        let dFromEC = fusionChannels e c
                        for f in fChannels do
                            let dFromAF = fusionChannels a f
                            // d must appear in both (e × c) and (a × f)
                            let commonD = 
                                Set.intersect (Set.ofList dFromEC) (Set.ofList dFromAF)
                            for d in commonD do
                                // Compute F-symbol via 6j-symbol
                                let a_d = getJDoubled a
                                let b_d = getJDoubled b
                                let c_d = getJDoubled c
                                let d_d = getJDoubled d
                                let e_d = getJDoubled e
                                let f_d = getJDoubled f
                                
                                // Quantum 6j-symbol
                                // Convention: { a  b  e }
                                //             { c  d  f }
                                let sixJ = quantum6jSymbol a_d b_d e_d c_d d_d f_d k
                                
                                // Unitary normalization factor:
                                // sqrt([2e+1]_q * [2f+1]_q)
                                // [2j+1]_q = quantum dimension = [j_doubled + 1]_q
                                let qDimE = qNumber (e_d + 1) k
                                let qDimF = qNumber (f_d + 1) k
                                let normFactor = sqrt (abs qDimE * abs qDimF)
                                
                                // Phase convention: (-1)^{(a+b+c+d)/2} where a,b,c,d are
                                // the j_doubled values of the four external legs.
                                // The sum a_d+b_d+c_d+d_d is always even (from triad
                                // constraints), so the exponent is always an integer.
                                // This phase is required for consistency with the pentagon
                                // equations when half-integer spins are present.
                                let phaseExp = (a_d + b_d + c_d + d_d) / 2
                                let phase = if phaseExp % 2 = 0 then 1.0 else -1.0
                                
                                let value = phase * normFactor * sixJ
                                
                                let index = { 
                                    A = a; B = b; C = c; D = d; E = e; F = f 
                                }
                                symbols <- symbols |> Map.add index (Complex(value, 0.0))
        
        symbols
    
    // ========================================================================
    // F-MATRIX COMPUTATION
    // ========================================================================
    
    /// Compute F-matrix data for a given anyon type
    /// 
    /// Returns all F-symbols needed for basis transformations.
    /// Non-trivial F-symbols are stored explicitly; trivial ones
    /// (= 1 for valid fusion channels, = 0 for invalid) are computed
    /// on demand via fusion rules.
    let computeFMatrix (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<FMatrixData> =
        
        match anyonType with
        | AnyonSpecies.AnyonType.Ising ->
            Ok {
                AnyonType = anyonType
                FSymbols = computeIsingFSymbols ()
                IsValidated = false  // Need to validate pentagon equations
            }
        
        | AnyonSpecies.AnyonType.Fibonacci ->
            Ok {
                AnyonType = anyonType
                FSymbols = computeFibonacciFSymbols ()
                IsValidated = false
            }
        
        | AnyonSpecies.AnyonType.SU2Level k when k = 2 ->
            // SU(2)_2 is same as Ising
            Ok {
                AnyonType = anyonType
                FSymbols = computeIsingFSymbols ()
                IsValidated = false
            }
        
        | AnyonSpecies.AnyonType.SU2Level k ->
            // General SU(2)_k F-symbols via quantum 6j-symbols (q-Racah coefficients)
            // Reference: Kirillov & Reshetikhin (1989), Simon Chapter 18.6
            Ok {
                AnyonType = anyonType
                FSymbols = computeSU2kFSymbols k
                IsValidated = false
            }
    
    // ========================================================================
    // HELPER FUNCTIONS
    // ========================================================================
    
    /// Extract particle results from fusion outcomes
    let private toParticles (outcomes: FusionRules.Outcome list) : AnyonSpecies.Particle list =
        outcomes |> List.map (fun outcome -> outcome.Result)
    
    /// Check if a fusion channel is valid (a × b → c)
    let private canFuseTo 
        (a: AnyonSpecies.Particle) 
        (b: AnyonSpecies.Particle) 
        (c: AnyonSpecies.Particle) 
        (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<bool> =
        FusionRules.fuse a b anyonType
        |> Result.map (toParticles >> List.contains c)
    
    /// Look up a specific F-symbol value
    /// 
    /// Returns the F-symbol F[a,b,c,d;e,f].
    /// 
    /// For valid fusion channels:
    /// - Returns stored value if non-trivial
    /// - Returns 1 if trivial (default)
    /// - Returns 0 if fusion channel is invalid
    let getFSymbol 
        (data: FMatrixData) 
        (index: FSymbolIndex) 
        : TopologicalResult<Complex> =
        
        // Validate that all fusion channels are physically valid
        // F[a,b,c,d;e,f] requires:
        // - (a × b) can fuse to e
        // - (e × c) can fuse to d  (left-associative path)
        // - (b × c) can fuse to f
        // - (a × f) can fuse to d  (right-associative path)
        
        let validateAllChannels () =
            topologicalResult {
                let! abToE = canFuseTo index.A index.B index.E data.AnyonType
                if not abToE then return false
                else
                    let! ecToD = canFuseTo index.E index.C index.D data.AnyonType
                    if not ecToD then return false
                    else
                        let! bcToF = canFuseTo index.B index.C index.F data.AnyonType
                        if not bcToF then return false
                        else
                            let! afToD = canFuseTo index.A index.F index.D data.AnyonType
                            return afToD
            }
        
        validateAllChannels ()
        |> Result.bind (function
            | false -> Ok Complex.Zero  // Invalid fusion channel
            | true -> 
                // Valid channel - look up value or return 1 (trivial)
                data.FSymbols
                |> Map.tryFind index
                |> Option.map Ok
                |> Option.defaultValue (Ok Complex.One)
        )
    
    // ========================================================================
    // PENTAGON EQUATION VERIFICATION
    // ========================================================================
    
    /// Get fusion channels a×b, returning empty list when fusion is undefined.
    ///
    /// Returning [] on Error is correct: undefined fusion paths contribute
    /// zero terms to the verification sums, matching the mathematical convention.
    let private fusionChannelsLocal
        (a: AnyonSpecies.Particle)
        (b: AnyonSpecies.Particle)
        (anyonType: AnyonSpecies.AnyonType)
        : AnyonSpecies.Particle list =
        match FusionRules.channels a b anyonType with
        | Ok channels -> channels
        | Error _ -> []

    /// Try to look up an F-symbol value, returning None if fusion constraints are violated.
    let private tryGetFLocal
        (data: FMatrixData)
        (a, b, c, d, e, f)
        : Complex option =
        match getFSymbol data { A = a; B = b; C = c; D = d; E = e; F = f } with
        | Ok value -> Some value
        | Error _ -> None

    /// Verify pentagon equation for four fusing anyons (a,b,c,d) with total charge e.
    ///
    /// The pentagon identity (Kitaev 2006, Appendix C) states that two distinct
    /// sequences of F-moves relating fusion trees of four anyons must agree:
    ///
    ///   F^{fcd}_{e;gl} · F^{abl}_{e;fh} = Σ_k F^{abc}_{g;fk} · F^{akd}_{e;gh} · F^{bcd}_{h;kl}
    ///
    /// In our FSymbolIndex convention F[A,B,C,D;E,F] = F^{ABC}_{D;EF}, where
    /// (A×B)→E is the left intermediate channel and (B×C)→F the right intermediate.
    ///
    /// Free indices: f ∈ a×b, g ∈ f×c, l ∈ c×d, h ∈ b×l.
    /// Summed index: k ∈ b×c.
    let verifyPentagonEquation
        (data: FMatrixData)
        (a: AnyonSpecies.Particle)
        (b: AnyonSpecies.Particle)
        (c: AnyonSpecies.Particle)
        (d: AnyonSpecies.Particle)
        (e: AnyonSpecies.Particle)
        : TopologicalResult<bool> =

        let anyonType = data.AnyonType
        let tolerance = 1e-10

        let channelsAB = fusionChannelsLocal a b anyonType
        let channelsBC = fusionChannelsLocal b c anyonType
        let channelsCD = fusionChannelsLocal c d anyonType

        // For each valid combination of free indices (f,g,l,h), compare LHS and RHS
        let deviations =
            [ for f in channelsAB do
                for g in fusionChannelsLocal f c anyonType do
                    for l in channelsCD do
                        for h in fusionChannelsLocal b l anyonType do
                            // LHS: F[f,c,d,e; g,l] · F[a,b,l,e; f,h]
                            match tryGetFLocal data (f,c,d,e,g,l), tryGetFLocal data (a,b,l,e,f,h) with
                            | Some lhs1, Some lhs2 ->
                                let lhs = lhs1 * lhs2

                                // RHS: Σ_k F[a,b,c,g; f,k] · F[a,k,d,e; g,h] · F[b,c,d,h; k,l]
                                let rhs =
                                    channelsBC
                                    |> List.choose (fun k ->
                                        match tryGetFLocal data (a,b,c,g,f,k),
                                              tryGetFLocal data (a,k,d,e,g,h),
                                              tryGetFLocal data (b,c,d,h,k,l) with
                                        | Some v1, Some v2, Some v3 -> Some (v1 * v2 * v3)
                                        | _ -> None)
                                    |> List.fold (+) Complex.Zero

                                (lhs - rhs).Magnitude
                            // F-symbol lookup returned None — the index combination violates
                            // fusion constraints and does not correspond to a valid fusion tree.
                            // Skipping is correct: absent paths contribute nothing to the equation.
                            | _ -> () ]

        match deviations with
        | [] -> Ok true  // No valid fusion paths — trivially satisfied
        | _ ->
            let maxDev = List.max deviations
            if deviations |> List.forall (fun d -> d < tolerance) then
                Ok true
            else
                Error (TopologicalError.Other
                    $"Pentagon equation violated for ({a},{b},{c},{d};{e}): max deviation {maxDev:E3} exceeds tolerance {tolerance:E3}")
    
    // ========================================================================
    // UNITARITY VERIFICATION
    // ========================================================================
    
    /// Verify unitarity of F-matrices for a given fusion process
    /// 
    /// For fixed a,b,c,d, the F-matrix F[a,b,c,d;e,f] (varying e,f)
    /// should form a unitary matrix: F F† = I
    /// 
    /// This ensures probability conservation in basis transformations.
    let verifyFMatrixUnitarity
        (data: FMatrixData)
        (a: AnyonSpecies.Particle)
        (b: AnyonSpecies.Particle)
        (c: AnyonSpecies.Particle)
        (d: AnyonSpecies.Particle)
        : TopologicalResult<bool> =
        
        // Get all valid intermediate states e (from a × b)
        let getEChannels () =
            FusionRules.fuse a b data.AnyonType
            |> Result.map toParticles
        
        // Get all valid intermediate states f (from b × c)
        let getFChannels () =
            FusionRules.fuse b c data.AnyonType
            |> Result.map toParticles
        
        // Build F-matrix as 2D array
        let buildFMatrix () =
            getEChannels ()
            |> Result.bind (fun eChannels ->
                getFChannels ()
                |> Result.bind (fun fChannels ->
                    // Collect all F-symbols, propagating any errors
                    let collectFSymbols () =
                        eChannels
                        |> List.mapi (fun i e ->
                            fChannels
                            |> List.mapi (fun j f ->
                                let index = { A = a; B = b; C = c; D = d; E = e; F = f }
                                getFSymbol data index
                                |> Result.map (fun value -> (i, j, value))
                            )
                        )
                        |> List.concat
                        |> List.fold (fun acc result ->
                            Result.bind (fun list ->
                                Result.map (fun item -> item :: list) result
                            ) acc
                        ) (Ok [])
                    
                    collectFSymbols ()
                    |> Result.map (fun values ->
                        // Create matrix and populate
                        let matrix = Array2D.zeroCreate eChannels.Length fChannels.Length
                        values |> List.iter (fun (i, j, value) ->
                            matrix.[i, j] <- value
                        )
                        matrix
                    )
                )
            )
        
        // Check F F† = I (using immutable approach)
        let checkUnitarity (matrix: Complex[,]) =
            let rows = Array2D.length1 matrix
            let cols = Array2D.length2 matrix
            
            // Compute F F† product
            let computeProduct i j =
                [0 .. cols - 1]
                |> List.fold (fun sum k ->
                    sum + matrix.[i, k] * Complex.Conjugate(matrix.[j, k])
                ) Complex.Zero
            
            // Check if product is identity
            [0 .. rows - 1]
            |> List.forall (fun i ->
                [0 .. rows - 1]
                |> List.forall (fun j ->
                    let expected = if i = j then Complex.One else Complex.Zero
                    let actual = computeProduct i j
                    Complex.Abs(actual - expected) < 1e-10
                )
            )
        
        buildFMatrix ()
        |> Result.map checkUnitarity
    
    /// Get all particles in an anyon theory
    /// 
    /// Uses the generic AnyonSpecies.particles function to enumerate
    /// all particles for any supported theory.
    let private getAllParticles (anyonType: AnyonSpecies.AnyonType) 
        : TopologicalResult<AnyonSpecies.Particle list> =
        AnyonSpecies.particles anyonType
    
    /// Validate all F-symbols by checking pentagon equations and unitarity.
    ///
    /// For Ising, Fibonacci, and SU(2)_k theories, verifies all particle
    /// 5-tuples against the pentagon equation and checks F-matrix unitarity
    /// for the non-trivial fusion sector.
    ///
    /// Returns Error if any pentagon equation is violated.
    /// Returns Ok with validated data if all checks pass.
    let validateFMatrix (data: FMatrixData)
        : TopologicalResult<FMatrixData> =

        match data.AnyonType with
        | AnyonSpecies.AnyonType.Ising
        | AnyonSpecies.AnyonType.Fibonacci
        | AnyonSpecies.AnyonType.SU2Level _ ->
            getAllParticles data.AnyonType
            |> Result.bind (fun particles ->
                // Verify pentagon equation for all 5-tuples (a,b,c,d,e)
                let pentagonResults =
                    [ for a in particles do
                      for b in particles do
                      for c in particles do
                      for d in particles do
                      for e in particles ->
                          verifyPentagonEquation data a b c d e ]

                // Collect any errors
                let firstError =
                    pentagonResults
                    |> List.tryPick (function Error e -> Some e | Ok _ -> None)

                match firstError with
                | Some err -> Error err
                | None ->
                    let allPassed = pentagonResults |> List.forall (function Ok true -> true | _ -> false)
                    if allPassed then
                        // Also spot-check unitarity for the non-trivial sector
                        let spotCheck =
                            match data.AnyonType with
                            | AnyonSpecies.AnyonType.Ising ->
                                let sigma = AnyonSpecies.Particle.Sigma
                                verifyFMatrixUnitarity data sigma sigma sigma sigma
                            | AnyonSpecies.AnyonType.Fibonacci ->
                                let tau = AnyonSpecies.Particle.Tau
                                verifyFMatrixUnitarity data tau tau tau tau
                            | AnyonSpecies.AnyonType.SU2Level k ->
                                // Spot-check unitarity for the fundamental representation
                                // j=1/2 (j_doubled=1) fusing with itself
                                let fundamental = AnyonSpecies.Particle.SpinJ(1, k)
                                verifyFMatrixUnitarity data fundamental fundamental fundamental fundamental
                        spotCheck
                        |> Result.bind (fun unitary ->
                            if unitary then
                                Ok { data with IsValidated = true }
                            else
                                Error (TopologicalError.Other
                                    $"F-matrix unitarity check failed for {data.AnyonType}"))
                    else
                        Error (TopologicalError.Other
                            $"Pentagon equation violated for {data.AnyonType}")
            )
    
    // ========================================================================
    // UTILITY FUNCTIONS
    // ========================================================================
    
    let private formatParticle p = TopologicalHelpers.formatParticle p
    let private formatComplex z = TopologicalHelpers.formatComplex z
    
    /// Display F-symbol information
    let displayFSymbol (index: FSymbolIndex) (value: Complex) : string =
        sprintf "F[%s,%s,%s,%s;%s,%s] = %s" 
            (formatParticle index.A) (formatParticle index.B) 
            (formatParticle index.C) (formatParticle index.D)
            (formatParticle index.E) (formatParticle index.F)
            (formatComplex value)
    
    /// Display all non-trivial F-symbols for an anyon theory
    let displayAllFSymbols (data: FMatrixData) : string =
        let header = sprintf "F-symbols for %A:\n" data.AnyonType
        let validated = if data.IsValidated then "(validated)" else "(not validated)"
        
        let symbols = 
            data.FSymbols
            |> Map.toList
            |> List.map (fun (idx, value) -> displayFSymbol idx value)
            |> String.concat "\n"
        
        sprintf "%s%s\n\nNon-trivial F-symbols:\n%s\n\nNote: Unlisted F-symbols are either 1 (trivial) or 0 (invalid fusion)." 
            header validated symbols
