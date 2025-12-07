namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum.Core

// ============================================================================
// RESULT COMPUTATION EXPRESSION (for clean error handling)
// ============================================================================

[<AutoOpen>]
module private ResultHelpers =
    type ResultBuilder() =
        member _.Bind(x, f) = Result.bind f x
        member _.Return(x) = Ok x
        member _.ReturnFrom(x) = x
        member _.Zero() = Ok ()
    
    let result = ResultBuilder()

/// QUBO ↔ Ising conversion for D-Wave quantum annealing.
///
/// This module provides conversions between:
/// - QUBO (Quadratic Unconstrained Binary Optimization): x ∈ {0, 1}
/// - Ising model (D-Wave native format): s ∈ {-1, +1}
///
/// Mathematical transformation:
///   x = (1 + s) / 2  maps {-1, +1} → {0, 1}
///   s = 2x - 1       maps {0, 1} → {-1, +1}
///
/// Energy relationship:
///   E_QUBO(x) = ∑ Q_ij * x_i * x_j
///   E_Ising(s) = ∑ h_i * s_i + ∑ J_ij * s_i * s_j + offset
///
/// Design rationale:
/// - D-Wave hardware uses Ising formulation (spins)
/// - QAOA and optimization problems naturally expressed as QUBO
/// - Conversion allows seamless use of D-Wave backend with existing QAOA solvers
module QuboToIsing =
    
    open FSharp.Azure.Quantum.Backends.DWaveTypes
    
    // ============================================================================
    // QUBO → ISING CONVERSION
    // ============================================================================
    
    /// Convert QUBO to Ising model
    ///
    /// Transformation derivation:
    ///   Let x = (1 + s) / 2, then x_i * x_j = ((1 + s_i) / 2) * ((1 + s_j) / 2)
    ///
    /// For diagonal terms (i = j):
    ///   Q_ii * x_i² = Q_ii * x_i  (since x² = x for binary)
    ///               = Q_ii * (1 + s_i) / 2
    ///               = Q_ii/2 + Q_ii/2 * s_i
    ///   → Linear: h_i += Q_ii/2
    ///   → Offset: +Q_ii/2
    ///
    /// For off-diagonal terms (i ≠ j):
    ///   Q_ij * x_i * x_j = Q_ij * ((1 + s_i) / 2) * ((1 + s_j) / 2)
    ///                    = Q_ij/4 * (1 + s_i + s_j + s_i*s_j)
    ///   → Linear: h_i += Q_ij/4, h_j += Q_ij/4
    ///   → Quadratic: J_ij = Q_ij/4
    ///   → Offset: +Q_ij/4
    ///
    /// Parameters:
    /// - qubo: QUBO matrix as Map<(int * int), float>
    ///         Convention: Upper triangle (i <= j) or symmetric
    ///
    /// Returns: IsingProblem with linear coeffs, quadratic coeffs, and offset
    ///
    /// Example:
    ///   QUBO: E = -5*x0*x1 - 3*x1*x2 + 2*x0 + 4*x1 + x2
    ///   Matrix: Q = {(0,0)→2, (1,1)→4, (2,2)→1, (0,1)→-5, (1,2)→-3}
    ///
    ///   Ising: h = {0→-0.25, 1→-0.25, 2→-0.25}
    ///          J = {(0,1)→-1.25, (1,2)→-0.75}
    ///          offset = 1.75
    let quboToIsing (qubo: Map<(int * int), float>) : IsingProblem =
        let addToMap key value map =
            let current = Map.tryFind key map |> Option.defaultValue 0.0
            Map.add key (current + value) map
        
        let (finalLinear, finalQuadratic, finalOffset) =
            qubo
            |> Map.fold (fun (linear, quadratic, offset) (i, j) q_ij ->
                if i = j then
                    // Diagonal term: Q_ii * x_i  (x² = x for binary)
                    // Transformation: Q_ii * (1 + s_i) / 2 = Q_ii/2 + (Q_ii/2) * s_i
                    // Linear: h_i += Q_ii/2
                    // Offset: +Q_ii/2
                    (addToMap i (q_ij / 2.0) linear, quadratic, offset + (q_ij / 2.0))
                else
                    // Off-diagonal term: Q_ij * x_i * x_j
                    // Transformation: Q_ij * ((1 + s_i) / 2) * ((1 + s_j) / 2)
                    //               = Q_ij/4 * (1 + s_i + s_j + s_i*s_j)
                    // Linear: h_i += Q_ij/4, h_j += Q_ij/4
                    // Quadratic: J_ij = Q_ij/4
                    // Offset: +Q_ij/4
                    let (iMin, jMax) = if i < j then (i, j) else (j, i)
                    let newLinear = addToMap i (q_ij / 4.0) linear |> addToMap j (q_ij / 4.0)
                    let newQuadratic = addToMap (iMin, jMax) (q_ij / 4.0) quadratic
                    (newLinear, newQuadratic, offset + (q_ij / 4.0))
            ) (Map.empty, Map.empty, 0.0)
        
        {
            LinearCoeffs = finalLinear
            QuadraticCoeffs = finalQuadratic
            Offset = finalOffset
        }
    
    // ============================================================================
    // ISING → QUBO SOLUTION CONVERSION
    // ============================================================================
    
    /// Convert Ising spin solution to QUBO binary solution
    ///
    /// Transformation: s ∈ {-1, +1} → x ∈ {0, 1}
    ///   x = (1 + s) / 2
    ///   x = 0 when s = -1
    ///   x = 1 when s = +1
    ///
    /// Parameters:
    /// - spins: Ising solution as Map<int, int> (spin values {-1, +1})
    ///
    /// Returns: QUBO solution as Map<int, int> (binary values {0, 1})
    ///
    /// Example:
    ///   Ising: {0→1, 1→-1, 2→1}  (spins)
    ///   QUBO:  {0→1, 1→0,  2→1}  (binary)
    let isingToQubo (spins: Map<int, int>) : Map<int, int> =
        spins
        |> Map.map (fun _ spin ->
            // x = (1 + s) / 2
            (1 + spin) / 2
        )
    
    /// Convert QUBO binary solution to Ising spin solution
    ///
    /// Transformation: x ∈ {0, 1} → s ∈ {-1, +1}
    ///   s = 2x - 1
    ///   s = -1 when x = 0
    ///   s = +1 when x = 1
    ///
    /// Parameters:
    /// - binary: QUBO solution as Map<int, int> (binary values {0, 1})
    ///
    /// Returns: Ising solution as Map<int, int> (spin values {-1, +1})
    let quboToIsingSolution (binary: Map<int, int>) : Map<int, int> =
        binary
        |> Map.map (fun _ x ->
            // s = 2x - 1
            2 * x - 1
        )
    
    // ============================================================================
    // ENERGY CALCULATIONS
    // ============================================================================
    
    /// Calculate Ising energy for a given spin configuration
    ///
    /// Energy formula:
    ///   E(s) = ∑ h_i * s_i + ∑ J_ij * s_i * s_j + offset
    ///
    /// Parameters:
    /// - problem: Ising problem specification
    /// - spins: Spin configuration (qubit index → {-1, +1})
    ///
    /// Returns: Total Ising energy (float)
    ///
    /// Example:
    ///   problem: h = {0→1.0, 1→-0.5}, J = {(0,1)→-2.0}, offset = 0.5
    ///   spins: {0→1, 1→-1}
    ///   energy = 1.0*1 + (-0.5)*(-1) + (-2.0)*1*(-1) + 0.5
    ///          = 1.0 + 0.5 + 2.0 + 0.5 = 4.0
    let isingEnergy (problem: IsingProblem) (spins: Map<int, int>) : float =
        
        // Linear energy: ∑ h_i * s_i
        let linearEnergy = 
            problem.LinearCoeffs
            |> Map.toSeq
            |> Seq.sumBy (fun (i, h_i) ->
                let s_i = Map.tryFind i spins |> Option.defaultValue 0 |> float
                h_i * s_i
            )
        
        // Quadratic energy: ∑ J_ij * s_i * s_j
        let quadraticEnergy = 
            problem.QuadraticCoeffs
            |> Map.toSeq
            |> Seq.sumBy (fun ((i, j), j_ij) ->
                let s_i = Map.tryFind i spins |> Option.defaultValue 0 |> float
                let s_j = Map.tryFind j spins |> Option.defaultValue 0 |> float
                j_ij * s_i * s_j
            )
        
        linearEnergy + quadraticEnergy + problem.Offset
    
    /// Calculate QUBO energy for a given binary configuration
    ///
    /// Energy formula:
    ///   E(x) = ∑ Q_ij * x_i * x_j
    ///
    /// Parameters:
    /// - qubo: QUBO matrix as Map<(int * int), float>
    /// - binary: Binary configuration (qubit index → {0, 1})
    ///
    /// Returns: Total QUBO energy (float)
    ///
    /// Example:
    ///   qubo: {(0,0)→2.0, (0,1)→-5.0, (1,1)→4.0}
    ///   binary: {0→1, 1→1}
    ///   energy = 2.0*1*1 + (-5.0)*1*1 + 4.0*1*1
    ///          = 2.0 - 5.0 + 4.0 = 1.0
    let quboEnergy (qubo: Map<(int * int), float>) (binary: Map<int, int>) : float =
        qubo
        |> Map.toSeq
        |> Seq.sumBy (fun ((i, j), q_ij) ->
            let x_i = Map.tryFind i binary |> Option.defaultValue 0 |> float
            let x_j = Map.tryFind j binary |> Option.defaultValue 0 |> float
            q_ij * x_i * x_j
        )
    
    // ============================================================================
    // VALIDATION AND UTILITIES
    // ============================================================================
    
    /// Validate that spins are in {-1, +1}
    ///
    /// Parameters:
    /// - spins: Spin configuration to validate
    ///
    /// Returns: QuantumResult<unit> - Ok if valid, Error with message if invalid
    let validateSpins (spins: Map<int, int>) : QuantumResult<unit> =
        let invalidSpins = 
            spins
            |> Map.toSeq
            |> Seq.filter (fun (_, spin) -> spin <> -1 && spin <> 1)
            |> Seq.toList
        
        if List.isEmpty invalidSpins then
            Ok ()
        else
            let invalidQubits = invalidSpins |> List.map fst
            Error (QuantumError.Other $"Invalid spin values (must be -1 or +1) for qubits: {invalidQubits}")
    
    /// Validate that binary values are in {0, 1}
    ///
    /// Parameters:
    /// - binary: Binary configuration to validate
    ///
    /// Returns: QuantumResult<unit> - Ok if valid, Error with message if invalid
    let validateBinary (binary: Map<int, int>) : QuantumResult<unit> =
        let invalidBits = 
            binary
            |> Map.toSeq
            |> Seq.filter (fun (_, bit) -> bit <> 0 && bit <> 1)
            |> Seq.toList
        
        if List.isEmpty invalidBits then
            Ok ()
        else
            let invalidQubits = invalidBits |> List.map fst
            Error (QuantumError.Other $"Invalid binary values (must be 0 or 1) for qubits: {invalidQubits}")
    
    /// Verify that QUBO→Ising→QUBO conversion preserves optimal solution energy
    ///
    /// This is a correctness check for the conversion algorithms.
    ///
    /// Parameters:
    /// - qubo: Original QUBO problem
    /// - binary: QUBO solution to test
    ///
    /// Returns: QuantumResult<float> - Ok with energy difference (should be ~0.0) or Error
    ///
    /// Example usage:
    ///   let qubo = Map.ofList [((0,1), -5.0); ((0,0), 2.0)]
    ///   let solution = Map.ofList [(0, 1); (1, 1)]
    ///   match verifyConversion qubo solution with
    ///   | Ok diff when abs diff < 1e-10 -> printfn "✅ Conversion correct"
    ///   | Ok diff -> printfn "⚠️ Energy difference: %.10f" diff
    ///   | Error e -> printfn "❌ Error: %s" e
    let verifyConversion (qubo: Map<(int * int), float>) (binary: Map<int, int>) : QuantumResult<float> =
        result {
            // Validate binary solution
            do! validateBinary binary
            
            // Calculate QUBO energy
            let quboEnergyVal = quboEnergy qubo binary
            
            // Convert QUBO to Ising
            let ising = quboToIsing qubo
            
            // Convert binary solution to spins
            let spins = quboToIsingSolution binary
            
            // Validate spins
            do! validateSpins spins
            
            // Calculate Ising energy
            let isingEnergyVal = isingEnergy ising spins
            
            // Energy difference (should be ~0.0 for correct conversion)
            let diff = abs (quboEnergyVal - isingEnergyVal)
            return diff
        }
