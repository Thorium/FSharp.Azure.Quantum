namespace FSharp.Azure.Quantum.Core

open System
open System.Numerics
open FSharp.Azure.Quantum.LocalSimulator

/// Conversion functions between different quantum state representations
/// 
/// Note: Topological conversions (FusionSuperposition ↔ StateVector) are implemented
/// in FSharp.Azure.Quantum.Topological package to avoid circular dependencies.
module QuantumStateConversion =
    
    // ========================================================================
    // CONVERSION: StateVector ↔ SparseState
    // ========================================================================
    
    /// Convert StateVector to SparseState
    let stateVectorToSparse 
        (sv: StateVector.StateVector) 
        : Map<int, Complex> * int =
        
        let n = StateVector.numQubits sv
        let dimension = 1 <<< n
        let epsilon = 1e-12
        
        let nonZeroAmplitudes =
            [0 .. dimension - 1]
            |> List.choose (fun i ->
                let amplitude = StateVector.getAmplitude i sv
                if amplitude.Magnitude > epsilon then
                    Some (i, amplitude)
                else
                    None
            )
            |> Map.ofList
        
        (nonZeroAmplitudes, n)
    
    /// Convert SparseState to StateVector
    let sparseToStateVector 
        (amplitudes: Map<int, Complex>) 
        (n: int) 
        : StateVector.StateVector =
        
        let dimension = 1 <<< n
        let amplitudeArray = Array.create dimension Complex.Zero
        
        amplitudes
        |> Map.iter (fun i amp -> amplitudeArray.[i] <- amp)
        
        StateVector.create amplitudeArray
    
    // ========================================================================
    // GENERIC CONVERSION DISPATCHER
    // ========================================================================
    
    /// Convert between any two QuantumState types.
    /// Returns Result.Error for unsupported conversion paths.
    let convert 
        (targetType: QuantumStateType) 
        (state: QuantumState) 
        : Result<QuantumState, QuantumError> =
        
        let sourceType = QuantumState.stateType state
        
        if sourceType = targetType then
            Ok state
        else
            match (sourceType, targetType, state) with
            // StateVector ↔ SparseState
            | (QuantumStateType.GateBased, QuantumStateType.Sparse, QuantumState.StateVector sv) ->
                let (amps, n) = stateVectorToSparse sv
                Ok (QuantumState.SparseState (amps, n))
            
            | (QuantumStateType.Sparse, QuantumStateType.GateBased, QuantumState.SparseState (amps, n)) ->
                let sv = sparseToStateVector amps n
                Ok (QuantumState.StateVector sv)
            
            // FusionSuperposition conversions require Topological package
            | (QuantumStateType.TopologicalBraiding, _, _)
            | (_, QuantumStateType.TopologicalBraiding, _) ->
                Error (QuantumError.NotImplemented (
                    $"Conversion between {sourceType} and {targetType}",
                    Some "Requires the FSharp.Azure.Quantum.Topological package"))
            
            // Unsupported conversions
            | _ ->
                Error (QuantumError.NotImplemented (
                    $"Conversion from {sourceType} to {targetType}",
                    None))
    
    /// Smart conversion: Only convert if necessary, prefer native type.
    /// Returns Result.Error for unsupported conversion paths.
    let convertSmart 
        (preferredType: QuantumStateType) 
        (state: QuantumState) 
        : Result<QuantumState, QuantumError> =
        
        let currentType = QuantumState.stateType state
        
        // If already in preferred type, no conversion needed
        if currentType = preferredType then
            Ok state
        // If mixed backend, state type doesn't matter
        elif preferredType = QuantumStateType.Mixed then
            Ok state
        // Otherwise, convert to preferred type
        else
            convert preferredType state
