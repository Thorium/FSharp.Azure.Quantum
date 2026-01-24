// H2 Molecule Ground State Energy Example
// Demonstrates VQE (Variational Quantum Eigensolver) for hydrogen molecule
//
// This example shows two approaches:
// 1. Direct API: Using low-level structures for maximum control
// 2. Builder API: Using computation expressions for clean, declarative code
//
// WHEN TO USE THIS LIBRARY:
// ✓ Lightweight quantum chemistry without heavy dependencies
// ✓ Custom VQE implementations and experimentation
// ✓ Multi-backend support (Local, IonQ, Rigetti, Azure)
// ✓ Small molecules (< 10 qubits): H2, H2O, LiH, NH3
// ✓ Pure F# implementation, no Q# required
//
// WHEN TO USE Microsoft.Quantum.Chemistry INSTEAD:
// ✓ Large molecules (50+ qubits) requiring full molecular orbitals
// ✓ Production quantum chemistry pipelines
// ✓ Integration with Gaussian, PySCF, NWChem
// ✓ Advanced features (UCCSD ansatz, Jordan-Wigner, Bravyi-Kitaev)
//
// USE BOTH TOGETHER:
// - Use Microsoft.Quantum.Chemistry for accurate Hamiltonian construction
// - Use this library for flexible VQE execution on any backend
// - Bridge via FCIDump files or direct Hamiltonian conversion

(*
===============================================================================
 Background Theory
===============================================================================

The hydrogen molecule (H₂) is the simplest neutral molecule and the "hydrogen atom
of quantum chemistry"—the first system where quantum computers can outperform
pen-and-paper calculations. The H₂ ground state energy problem asks: what is the
lowest energy eigenvalue of the molecular Hamiltonian H? This determines chemical
properties like bond length (0.74 Å), binding energy (4.75 eV), and vibrational
frequency. Exact classical solution is possible for H₂ but scales exponentially
for larger molecules, motivating quantum approaches.

The Variational Quantum Eigensolver (VQE) is a hybrid quantum-classical algorithm
for finding ground state energies. It uses the variational principle: for any trial
state |ψ(θ)⟩, the energy E(θ) = ⟨ψ(θ)|H|ψ(θ)⟩ ≥ E₀ (ground state energy). VQE
prepares |ψ(θ)⟩ on a quantum computer, measures E(θ), and uses a classical optimizer
to minimize over θ. For H₂, a simple ansatz with one parameter (the bond angle)
suffices to reach chemical accuracy (±1.6 mHartree ≈ ±1 kcal/mol).

Key Equations:
  - Molecular Hamiltonian: H = Σᵢ hᵢⱼ aᵢ†aⱼ + ½Σᵢⱼₖₗ hᵢⱼₖₗ aᵢ†aⱼ†aₖaₗ + E_nuc
  - Variational principle: E(θ) = ⟨ψ(θ)|H|ψ(θ)⟩ ≥ E₀ for all θ
  - H₂ exact ground state energy: E₀ = -1.137 Hartree (at equilibrium)
  - Chemical accuracy: |E_computed - E_exact| < 1.6 mHartree ≈ 1 kcal/mol
  - Qubit mapping (Jordan-Wigner): aᵢ† → (Xᵢ - iYᵢ)/2 · Πⱼ<ᵢ Zⱼ

Quantum Advantage:
  While H₂ can be solved classically, it demonstrates the VQE workflow that scales
  to intractable molecules. The number of parameters in a full CI expansion grows
  as O(N⁴) for N orbitals; quantum computers handle this natively via superposition.
  For molecules like FeMoCo (nitrogen fixation catalyst, ~100 orbitals), classical
  simulation is impossible, but VQE on fault-tolerant quantum computers could solve
  it. H₂ serves as a benchmark: achieving chemical accuracy on H₂ validates the
  entire VQE pipeline (ansatz, optimizer, error mitigation, hardware).

References:
  [1] Peruzzo et al., "A variational eigenvalue solver on a photonic quantum
      processor", Nat. Commun. 5, 4213 (2014). https://doi.org/10.1038/ncomms5213
  [2] O'Malley et al., "Scalable Quantum Simulation of Molecular Energies",
      Phys. Rev. X 6, 031007 (2016). https://doi.org/10.1103/PhysRevX.6.031007
  [3] McArdle et al., "Quantum computational chemistry", Rev. Mod. Phys. 92,
      015003 (2020). https://doi.org/10.1103/RevModPhys.92.015003
  [4] Wikipedia: Hydrogen_molecule
      https://en.wikipedia.org/wiki/Hydrogen_molecule
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"


open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

printfn "================================================================"
printfn "APPROACH 1: Direct API (Low-Level)"
printfn "================================================================"

// Create H2 molecule at equilibrium bond length (0.74 Angstroms)
let h2 = Molecule.createH2 0.74

printfn "=== H2 Molecule Ground State Energy ==="
printfn "Molecule: %s" h2.Name
printfn "Atoms: %d" h2.Atoms.Length
printfn "Bonds: %d" h2.Bonds.Length
printfn "Electrons: %d" (Molecule.countElectrons h2)
printfn ""

// Configure VQE solver
let config = {
    Method = GroundStateMethod.VQE
    Backend = Some (LocalBackend() :> IQuantumBackend)
    MaxIterations = 100
    Tolerance = 1e-6
    InitialParameters = None
    ProgressReporter = None
    ErrorMitigation = None  // No error mitigation for local simulator
}

// Estimate ground state energy
printfn "Running VQE calculation..."
let result = GroundStateEnergy.estimateEnergy h2 config |> Async.RunSynchronously

match result with
| Ok vqeResult ->
    printfn "✓ Ground state energy: %.6f Hartree" vqeResult.Energy
    printfn "  Expected (experimental): -1.174 Hartree"
    printfn "  Error: %.6f Hartree" (abs(vqeResult.Energy - (-1.174)))
    printfn "  Iterations: %d" vqeResult.Iterations
    printfn "  Converged: %b" vqeResult.Converged
    printfn ""
    
    // Convert to other units
    let eV = vqeResult.Energy * 27.2114  // 1 Hartree = 27.2114 eV
    printfn "  In electron volts: %.6f eV" eV
    
| Error err ->
    printfn "✗ Calculation failed: %A" err.Message

printfn ""
printfn "=== Comparison: VQE vs Classical DFT ==="

// Run with Classical DFT for comparison
let configDFT = { config with Method = GroundStateMethod.ClassicalDFT }
let resultDFT = GroundStateEnergy.estimateEnergy h2 configDFT |> Async.RunSynchronously

match resultDFT with
| Ok vqeResultDFT ->
    printfn "Classical DFT: %.6f Hartree" vqeResultDFT.Energy
| Error err ->
    printfn "Classical DFT failed: %A" err.Message

printfn ""
printfn "=== Automatic Method Selection ==="

// Let the system choose the best method
let configAuto = { config with Method = GroundStateMethod.Automatic }
let resultAuto = GroundStateEnergy.estimateEnergy h2 configAuto |> Async.RunSynchronously

match resultAuto with
| Ok vqeResultAuto ->
    printfn "Automatic method: %.6f Hartree" vqeResultAuto.Energy
    printfn "(System chose best method based on molecule size)"
| Error err ->
    printfn "Automatic method failed: %A" err.Message


printfn ""
printfn "================================================================"
printfn "APPROACH 2: Builder API (Declarative)"
printfn "================================================================"

// Example 1: Simple H2 ground state at equilibrium bond length
printfn "=== Example 1: H2 Ground State at 0.74 Å ==="

let h2Problem = quantumChemistry {
    molecule (Molecule.createH2 0.74) 
    basis "sto-3g"
    ansatz UCCSD
}

printfn "Problem created successfully!"
printfn "Molecule: %s" h2Problem.Molecule.Value.Name
printfn "Basis: %s" h2Problem.Basis.Value
printfn "Ansatz: %A" h2Problem.Ansatz.Value

// Example 2: H2O (water) ground state
printfn "\n=== Example 2: H2O Ground State ===" 

let h2oProblem = quantumChemistry {
    molecule (Molecule.createH2O ())  // Standard geometry
    basis "sto-3g"
    ansatz HEA  // Hardware-efficient ansatz (faster)
    maxIterations 150
}

printfn "Problem created successfully!"
printfn "Molecule: %s" h2oProblem.Molecule.Value.Name
printfn "Number of atoms: %d" h2oProblem.Molecule.Value.Atoms.Length

// Example 3: LiH (lithium hydride) - Manually constructed since createLiH helper doesn't exist
printfn "\n=== Example 3: LiH Ground State ==="

let createLiH (bondLength: float) =
    {
        Name = "LiH"
        Atoms = [
            { Element = "Li"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (0.0, 0.0, bondLength) }
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
        ]
        Charge = 0
        Multiplicity = 1
    }

let lihProblem = quantumChemistry {
    molecule (createLiH 1.6)
    basis "sto-3g"
    ansatz UCCSD
    optimizer "COBYLA"
}

printfn "Problem created successfully!"
printfn "Molecule: %s" lihProblem.Molecule.Value.Name
printfn "Optimizer: %s" lihProblem.Optimizer.Value.Method

// Example 4: Conditional basis selection (demonstrates control flow)
printfn "\n=== Example 4: Conditional Basis Selection ==="

let smallMolecule = true

// Compute basis selection outside computation expression
let selectedBasis = if smallMolecule then "sto-3g" else "6-31g"

let conditionalProblem = quantumChemistry {
    molecule (Molecule.createH2 0.74)
    basis selectedBasis  // Use pre-computed value
    ansatz UCCSD
}

printfn "Problem created with basis: %s" conditionalProblem.Basis.Value

// Example 5: Bond length scan (demonstrates for loop)
printfn "\n=== Example 5: Bond Length Scan Setup ==="

let bondLengths = [0.6; 0.7; 0.74; 0.8; 0.9]

printfn "Creating problems for bond lengths: %A" bondLengths

for distance in bondLengths do
    let scanProblem = quantumChemistry {
        molecule (Molecule.createH2 distance)
        basis "sto-3g"
        ansatz UCCSD
    }
    printfn "  Distance %.2f Å: Problem ready" distance

// Example 6: Energy Convergence Plotting
printfn "\n=== Example 6: Energy Convergence During VQE Optimization ==="
printfn ""
printfn "The VQEResult includes EnergyHistory for tracking optimization progress."
printfn "This can be used to verify convergence and tune hyperparameters."
printfn ""

// Recalculate with explicit iteration tracking (using a generic molecule)
let configWithHistory = {
    Method = GroundStateMethod.VQE
    Backend = Some (LocalBackend() :> IQuantumBackend)
    MaxIterations = 30  // More iterations to show convergence
    Tolerance = 1e-8
    InitialParameters = None
    ProgressReporter = None
    ErrorMitigation = None
}

// Create a custom molecule for demonstration
let h2Custom = Molecule.createH2 0.75  // Slightly off equilibrium

let resultWithHistory = GroundStateEnergy.estimateEnergy h2Custom configWithHistory |> Async.RunSynchronously

match resultWithHistory with
| Ok vqeResult ->
    printfn "Final energy: %.6f Hartree" vqeResult.Energy
    printfn "Iterations: %d" vqeResult.Iterations  
    printfn "Converged: %b" vqeResult.Converged
    printfn ""
    printfn "Energy History (iteration -> energy):"
    printfn "-----------------------------------------"
    
    // Print ASCII convergence plot
    if vqeResult.EnergyHistory.Length > 0 then
        let energies = vqeResult.EnergyHistory |> List.map snd
        let minE = energies |> List.min
        let maxE = energies |> List.max
        let range = maxE - minE
        
        if range > 0.0 then
            printfn ""
            printfn "ASCII Convergence Plot:"
            printfn ""
            for (iteration, energy) in vqeResult.EnergyHistory do
                let normalized = (energy - minE) / range
                let barWidth = int (normalized * 40.0)
                let bar = String.replicate barWidth "#"
                printfn "  %3d | %.6f | %s" iteration energy bar
        else
            printfn ""
            for (iteration, energy) in vqeResult.EnergyHistory do
                printfn "  %3d | %.6f Hartree" iteration energy
    else
        printfn "  (Single-point calculation - no iteration history)"
    
    printfn ""
    printfn "Tip: Export EnergyHistory to CSV for external plotting tools:"
    printfn @"     vqeResult.EnergyHistory |> List.iter (fun (i,e) -> printfn ""%%d,%%0.6f"" i e)"
    
| Error err ->
    printfn "Calculation failed: %A" err.Message

printfn ""
printfn "Done!"
