// H2 Molecule Ground State Energy Example
// Demonstrates VQE (Variational Quantum Eigensolver) for hydrogen molecule

#r "nuget: FSharp.Azure.Quantum"

open FSharp.Azure.Quantum.QuantumChemistry

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
    MaxIterations = 100
    Tolerance = 1e-6
    InitialParameters = None
}

// Estimate ground state energy
printfn "Running VQE calculation..."
let result = GroundStateEnergy.estimateEnergy h2 config |> Async.RunSynchronously

match result with
| Ok energy ->
    printfn "✓ Ground state energy: %.6f Hartree" energy
    printfn "  Expected (experimental): -1.174 Hartree"
    printfn "  Error: %.6f Hartree" (abs(energy - (-1.174)))
    printfn ""
    
    // Convert to other units
    let eV = energy * 27.2114  // 1 Hartree = 27.2114 eV
    printfn "  In electron volts: %.6f eV" eV
    
| Error msg ->
    printfn "✗ Calculation failed: %s" msg

printfn ""
printfn "=== Comparison: VQE vs Classical DFT ==="

// Run with Classical DFT for comparison
let configDFT = { config with Method = GroundStateMethod.ClassicalDFT }
let resultDFT = GroundStateEnergy.estimateEnergy h2 configDFT |> Async.RunSynchronously

match resultDFT with
| Ok energyDFT ->
    printfn "Classical DFT: %.6f Hartree" energyDFT
| Error msg ->
    printfn "Classical DFT failed: %s" msg

printfn ""
printfn "=== Automatic Method Selection ==="

// Let the system choose the best method
let configAuto = { config with Method = GroundStateMethod.Automatic }
let resultAuto = GroundStateEnergy.estimateEnergy h2 configAuto |> Async.RunSynchronously

match resultAuto with
| Ok energyAuto ->
    printfn "Automatic method: %.6f Hartree" energyAuto
    printfn "(System chose best method based on molecule size)"
| Error msg ->
    printfn "Automatic method failed: %s" msg
