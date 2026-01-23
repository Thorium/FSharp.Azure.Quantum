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

// h2 function might collide with h2 value defined above, using explicit namespace or rename
// The error [117:15] "This value is not a function" happens because 'h2' is defined as a value on line 41
// shadowing the helper function 'h2' used in the builder. 
// We should use Molecule.createH2 in the builder or rename the value.

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
