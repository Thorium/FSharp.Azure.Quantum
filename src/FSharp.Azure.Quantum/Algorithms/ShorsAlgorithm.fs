namespace FSharp.Azure.Quantum.Algorithms

open System
open System.Numerics

/// Shor's Algorithm Module
/// 
/// Implements quantum integer factorization using period-finding.
/// Given an integer N, finds non-trivial factors p and q such that N = p × q.
/// 
/// Uses Quantum Phase Estimation (QPE) to find the period r of modular exponentiation:
///   a^r ≡ 1 (mod N)
/// 
/// Once r is found, factors can be extracted classically using gcd operations.
/// 
/// This is the quantum algorithm that breaks RSA encryption in polynomial time.
[<Obsolete("Use FSharp.Azure.Quantum.Algorithms.ShorsUnified for state-based Shor's implementation with full quantum period-finding. ShorsUnified successfully factors numbers like 15, 21, 35 using QPEUnified. This module will be removed in a future version.")>]
module ShorsAlgorithm =
    
    open FSharp.Azure.Quantum.Algorithms.ShorsTypes
    
    open FSharp.Azure.Quantum.LocalSimulator
    open FSharp.Azure.Quantum.Algorithms.QuantumPhaseEstimation
    
    // ========================================================================
    // CLASSICAL NUMBER THEORY HELPERS
    // ========================================================================
    
    /// Compute greatest common divisor using Euclidean algorithm
    let rec private gcd a b =
        if b = 0 then a
        else gcd b (a % b)
    
    /// Modular exponentiation: (base^exp) mod m
    let private modPow (baseNum: int) (exp: int) (modulus: int) : int =
        int (bigint.ModPow(bigint baseNum, bigint exp, bigint modulus))
    
    /// Check if number is prime (simple trial division)
    let private isPrime n =
        if n < 2 then false
        elif n = 2 then true
        elif n % 2 = 0 then false
        else
            let limit = int (sqrt (float n))
            [2..limit] |> List.forall (fun i -> n % i <> 0)
    
    /// Check if number is even
    let private isEven n = n % 2 = 0
    
    /// Convert continued fraction convergent to (numerator, denominator)
    let private continuedFractionConvergent (phi: float) (maxDenom: int) : (int * int) option =
        // Simple continued fraction approximation
        // Find s/r such that |phi - s/r| is minimized
        [1 .. maxDenom]
        |> List.map (fun denom ->
            let num = int (round (phi * float denom))
            let error = abs (phi - float num / float denom)
            (num, denom, error))
        |> List.minBy (fun (_, _, error) -> error)
        |> fun (num, denom, _) -> 
            if denom > 0 then Some (num, denom) else None
    
    // ========================================================================
    // QUANTUM PERIOD-FINDING (CLASSICAL HELPERS ONLY)
    // ========================================================================
    
    /// Modular multiplication operator: U|y⟩ = |ay mod N⟩
    /// This is the unitary for which we find the period
    type private ModularMultiplicationUnitary = {
        Base: int      // a
        Modulus: int   // N
    }
    
    /// Create unitary operator for modular exponentiation
    /// U|x⟩ = |a^x mod N⟩
    let private createModularExpUnitary a n : UnitaryOperator =
        // For educational implementation, we use phase estimation on a^x
        // In practice, this would be a complex quantum circuit implementing
        // modular arithmetic. For simulation, we approximate with phase gates.
        
        // The period r is encoded as phase: U^r = I → e^(2πi·0) = 1
        // So we need U such that U^r|ψ⟩ = e^(2πi·s/r)|ψ⟩ for eigenvalue s/r
        
        // Simplified: use custom phase that encodes period information
        CustomUnitary (fun state -> state)  // Placeholder for full implementation
    
    /// Extract period from phase estimate and histogram
    /// This is called by ShorsBackendAdapter after executing the quantum circuit
    let extractPeriodFromPhaseAndHistogram
        (histogram: Map<int, int>)
        (precisionQubits: int)
        (n: int) : (int * float) option =
        
        // Find most frequent measurement
        let mostFrequent =
            histogram
            |> Map.toSeq
            |> Seq.maxBy snd
            |> fst
        
        // Convert measured value to phase estimate
        let phaseEstimate = float mostFrequent / float (1 <<< precisionQubits)
        
        // Use continued fraction to extract period r from phase s/r
        match continuedFractionConvergent phaseEstimate n with
        | Some (s, r) when r > 0 && r < n ->
            Some (r, phaseEstimate)
        | _ ->
            None
    
    //========================================================================
    // FACTOR EXTRACTION FROM PERIOD
    // ========================================================================
    
    /// Extract factors from period r
    /// Given a^r ≡ 1 (mod N), compute gcd(a^(r/2) ± 1, N)
    let private extractFactorsFromPeriod (a: int) (r: int) (n: int) : (int * int) option =
        // Check if r is even
        if not (isEven r) then
            None
        else
            // Compute a^(r/2) mod N
            let halfR = r / 2
            let aToHalfR = modPow a halfR n
            
            // Check if a^(r/2) ≢ -1 (mod N)
            if aToHalfR = n - 1 then
                None
            else
                // Compute gcd(a^(r/2) + 1, N) and gcd(a^(r/2) - 1, N)
                let factor1 = gcd (aToHalfR + 1) n
                let factor2 = gcd (abs (aToHalfR - 1)) n
                
                // Check if we found non-trivial factors
                if factor1 > 1 && factor1 < n then
                    Some (factor1, n / factor1)
                elif factor2 > 1 && factor2 < n then
                    Some (factor2, n / factor2)
                else
                    None
    
    // ========================================================================
    // CLASSICAL POST-PROCESSING ONLY
    // ========================================================================
    // 
    // Note: The main execute() function has been moved to ShorsBackendAdapter
    // to enforce Rule 1 (all execution must go through IQuantumBackend).
    // 
    // This module now contains only classical helpers for:
    // - Number theory (gcd, modular exponentiation, primality)
    // - Period extraction from phase estimates
    // - Factor extraction from periods
    // ========================================================================
