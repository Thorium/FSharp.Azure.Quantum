// Quantum Phase Estimation Example - Molecular Energy Calculation
// Demonstrates eigenvalue extraction for quantum chemistry simulations

// Reference local build (use this for development/testing)
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

// Or use published NuGet package (uncomment when package is published):
// #r "nuget: FSharp.Azure.Quantum"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.QuantumPhaseEstimator
open FSharp.Azure.Quantum.Algorithms.QuantumPhaseEstimation  // For TGate, SGate, etc.

printfn "=== Molecular Energy Calculation with Quantum Phase Estimation ==="
printfn ""
printfn "BUSINESS SCENARIO:"
printfn "A pharmaceutical company needs to calculate ground state energies of"
printfn "drug molecules to predict binding affinity. Quantum Phase Estimation (QPE)"
printfn "extracts eigenvalues from quantum systems exponentially faster than classical methods."
printfn ""

// ============================================================================
// SCENARIO 1: Simple Gate Phase Estimation (Educational)
// ============================================================================

printfn "--- Scenario 1: T-Gate Phase Estimation (Educational) ---"
printfn ""

printfn "The T-gate is a fundamental quantum gate with eigenvalue λ = e^(iπ/4)"
printfn "We use QPE to estimate the phase φ where λ = e^(2πiφ)"
printfn ""

let tGateProblem = phaseEstimator {
    unitary TGate
    precision 10      // 10 qubits for 10-bit precision
}

printfn "Running QPE circuit..."
printfn ""

match tGateProblem with
| Ok prob ->
    match estimate prob with
    | Ok result ->
        printfn "✅ SUCCESS: Phase Estimated!"
        printfn ""
        printfn "RESULTS:"
        printfn "  Estimated Phase (φ):  %.6f" result.Phase
        printfn "  Expected Phase:       0.125 (exact: 1/8)"
        printfn "  Error:                %.6f" (abs (result.Phase - 0.125))
        printfn ""
        
        let eigenvalue = result.Eigenvalue
        printfn "  Eigenvalue (λ):       %.4f + %.4fi" eigenvalue.Real eigenvalue.Imaginary
        printfn "  |λ|:                  %.6f" eigenvalue.Magnitude
        printfn "  arg(λ):               %.6f radians" eigenvalue.Phase
        printfn ""
        printfn "CIRCUIT STATISTICS:"
        printfn "  Qubits Used:          %d" result.TotalQubits
        printfn "  Gate Count:           %d" result.GateCount
        printfn "  Precision:            %d bits" result.Precision

    | Error msg ->
        printfn "❌ Execution Error: %s" msg

| Error msg ->
    printfn "❌ Builder Error: %s" msg

printfn ""
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

// ============================================================================
// SCENARIO 2: Rotation Gate Analysis (Drug Design Simulation)
// ============================================================================

printfn "--- Scenario 2: Molecular Rotation Hamiltonian ---"
printfn ""

let theta = Math.PI / 3.0  // 60-degree rotation

printfn "DRUG MOLECULE SIMULATION:"
printfn "  Modeling a simplified molecular Hamiltonian H = Rz(θ)"
printfn "  Rotation angle θ:     %.4f radians (60°)" theta
printfn "  Goal: Extract ground state energy (lowest eigenvalue)"
printfn ""

let molecularProblem = phaseEstimator {
    unitary (RotationZ theta)
    precision 12        // Higher precision for accurate energy
    targetQubits 1
}

printfn "Simulating molecular quantum dynamics..."
printfn ""

match molecularProblem with
| Ok prob ->
    match estimate prob with
    | Ok result ->
        printfn "✅ SUCCESS: Molecular Energy Extracted!"
        printfn ""
        
        // In real quantum chemistry, the phase relates to energy: E = hν = ℏω
        // For this simplified model, we demonstrate the QPE extraction process
        let energy_au = result.Phase * 2.0 * Math.PI  // Convert phase to energy (atomic units)
        
        printfn "MOLECULAR PROPERTIES:"
        printfn "  Estimated Phase (φ):  %.6f" result.Phase
        printfn "  Ground State Energy:  %.6f a.u." energy_au
        printfn "  Eigenvalue Magnitude: %.6f" result.Eigenvalue.Magnitude
        printfn ""
        printfn "PHARMACEUTICAL APPLICATION:"
        printfn "  • Lower energy = More stable molecular configuration"
        printfn "  • Predicts drug-protein binding affinity"
        printfn "  • Guides molecular design for optimal efficacy"
        printfn "  • Quantum advantage: Exponentially faster than classical DFT"
        printfn ""
        printfn "QUANTUM RESOURCES:"
        printfn "  Qubits Required:      %d qubits" result.TotalQubits
        printfn "  Gate Count:           %d gates" result.GateCount
        printfn "  Precision:            %d bits (%.4f%% accuracy)" prob.Precision (100.0 / (2.0 ** float prob.Precision))

    | Error msg ->
        printfn "❌ Execution Error: %s" msg

| Error msg ->
    printfn "❌ Builder Error: %s" msg

printfn ""
printfn "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
printfn ""

// ============================================================================
// SCENARIO 3: Phase Gate Analysis (Material Science)
// ============================================================================

printfn "--- Scenario 3: Material Science - Crystal Lattice Dynamics ---"
printfn ""

let phaseAngle = Math.PI / 4.0  // 45-degree phase shift

printfn "MATERIAL PROPERTY ANALYSIS:"
printfn "  System: Crystalline solid with periodic structure"
printfn "  Phase angle θ:        %.4f radians (45°)" phaseAngle
printfn "  Application: Predicting electronic band structure"
printfn ""

let materialProblem = phaseEstimator {
    unitary (PhaseGate phaseAngle)
    precision 12
}

match materialProblem with
| Ok problem ->
    match estimate problem with
    | Ok result ->
        printfn "✅ SUCCESS: Band Structure Eigenvalue Extracted!"
        printfn ""
        printfn "MATERIAL PROPERTIES:"
        printfn "  Bloch Phase (φ):      %.6f" result.Phase
        printfn "  Expected Phase:       %.6f" (phaseAngle / (2.0 * Math.PI))
        printfn "  Measurement Error:    %.6f" (abs (result.Phase - (phaseAngle / (2.0 * Math.PI))))
        printfn ""
        printfn "INDUSTRIAL APPLICATIONS:"
        printfn "  • Semiconductor design (optimize band gaps)"
        printfn "  • Solar cell efficiency prediction"
        printfn "  • Superconductor discovery"
        printfn "  • Battery material optimization"
        printfn ""
        printfn "QUANTUM ADVANTAGE:"
        printfn "  Classical Methods:    Hours to days (DFT calculations)"
        printfn "  QPE (Quantum):        Seconds to minutes (exponential speedup)"
        printfn "  Scalability:          Handles systems with 100+ atoms (classical: ~50 atoms)"
    
    | Error msg ->
        printfn "❌ Error: %s" msg
| Error msg ->
    printfn "❌ Problem setup error: %s" msg

printfn ""
printfn "=== Key Takeaways ==="
printfn "• Quantum Phase Estimation extracts eigenvalues exponentially faster"
printfn "• Critical for quantum chemistry, drug discovery, materials science"
printfn "• Accuracy scales as 1/2^n where n = precision qubits"
printfn "• Core subroutine in VQE, Shor's algorithm, and HHL linear solver"
printfn "• Current NISQ hardware: Limited precision due to gate errors"
printfn "• Future fault-tolerant systems: Will revolutionize computational chemistry"
