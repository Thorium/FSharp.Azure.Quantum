// ==============================================================================
// Materials Science Common Module
// ==============================================================================
// Shared infrastructure for all MaterialsScience examples.
// Provides physical constants, unit conversions, backend initialization,
// and the VQE energy calculation helper used across all examples.
//
// Usage:
//   #load "_materialsCommon.fsx"
//   open _materialsCommon
//
// ==============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

// ==========================================================================
// PHYSICAL CONSTANTS
// ==========================================================================

/// Planck's constant (J*s)
let h = 6.62607015e-34

/// Reduced Planck's constant (J*s)
let hbar = h / (2.0 * Math.PI)

/// Electron mass (kg)
let m_e = 9.10938e-31

/// Electron charge (C)
let e_charge = 1.60218e-19

/// Boltzmann constant (J/K)
let k_B = 1.38065e-23

// ==========================================================================
// UNIT CONVERSIONS
// ==========================================================================

/// Electron volt to Joules
let eV_to_J = 1.60218e-19

/// Joules to electron volt
let J_to_eV = 1.0 / eV_to_J

/// Hartree to electron volt
let hartreeToEV = 27.2114

/// Angstrom to meters
let A_to_m = 1.0e-10

/// Nanometer to meters
let nm_to_m = 1.0e-9

// ==========================================================================
// QUANTUM BACKEND
// ==========================================================================

/// Local quantum backend for VQE simulations
let backend = LocalBackend() :> IQuantumBackend

// ==========================================================================
// VQE ENERGY CALCULATION
// ==========================================================================

/// Calculate ground state energy of a molecule using VQE.
/// Returns Ok (energy_hartree, iterations, elapsed_seconds) or Error message.
let calculateVQEEnergy (backend: IQuantumBackend) (molecule: Molecule) : Result<float * int * float, string> =
    let startTime = DateTime.Now

    let config = {
        Method = GroundStateMethod.VQE
        Backend = Some backend
        MaxIterations = 50
        Tolerance = 1e-5
        InitialParameters = None
        ProgressReporter = None
        ErrorMitigation = None
        IntegralProvider = None
    }

    try
        let result = GroundStateEnergy.estimateEnergy molecule config |> Async.RunSynchronously
        let elapsed = (DateTime.Now - startTime).TotalSeconds

        match result with
        | Ok vqeResult -> Ok (vqeResult.Energy, vqeResult.Iterations, elapsed)
        | Error err -> Error err.Message
    with
    | ex -> Error ex.Message
