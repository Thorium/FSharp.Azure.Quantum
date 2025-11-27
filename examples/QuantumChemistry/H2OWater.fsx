// H2O (Water) Molecule Ground State Energy Example
// Demonstrates quantum chemistry calculation for a polyatomic molecule

#r "nuget: FSharp.Azure.Quantum"

open FSharp.Azure.Quantum.QuantumChemistry

// Create H2O molecule at equilibrium geometry
let h2o = Molecule.createH2O()

printfn "=== H2O (Water) Molecule Ground State Energy ==="
printfn "Molecule: %s" h2o.Name
printfn "Atoms: %d (%s, %s, %s)" 
    h2o.Atoms.Length
    h2o.Atoms.[0].Element
    h2o.Atoms.[1].Element
    h2o.Atoms.[2].Element
printfn "Bonds: %d" h2o.Bonds.Length
printfn "Electrons: %d" (Molecule.countElectrons h2o)
printfn "Charge: %d" h2o.Charge
printfn "Multiplicity: %d (singlet)" h2o.Multiplicity
printfn ""

// Calculate bond lengths
let oh1Length = Molecule.calculateBondLength h2o.Atoms.[0] h2o.Atoms.[1]
let oh2Length = Molecule.calculateBondLength h2o.Atoms.[0] h2o.Atoms.[2]

printfn "Bond lengths:"
printfn "  O-H (bond 1): %.3f Angstroms" oh1Length
printfn "  O-H (bond 2): %.3f Angstroms" oh2Length
printfn ""

// Configure solver (use automatic method selection)
let config = {
    Method = GroundStateMethod.Automatic  // Will choose best method
    MaxIterations = 200
    Tolerance = 1e-6
    InitialParameters = None
}

// Estimate ground state energy
printfn "Running ground state calculation..."
printfn "(System will automatically select VQE or Classical DFT)"
let result = GroundStateEnergy.estimateEnergy h2o config |> Async.RunSynchronously

match result with
| Ok energy ->
    printfn "✓ Ground state energy: %.6f Hartree" energy
    printfn "  Expected (experimental): -76.0 Hartree"
    printfn "  Error: %.6f Hartree" (abs(energy - (-76.0)))
    printfn ""
    
    // Energy interpretation
    printfn "Energy interpretation:"
    printfn "  Negative energy indicates a bound, stable molecule"
    printfn "  Magnitude shows strength of electronic binding"
    
| Error msg ->
    printfn "✗ Calculation failed: %s" msg

printfn ""
printfn "=== Why Water is Important ==="
printfn "- Essential for life and biochemistry"
printfn "- Bent molecular geometry (104.5° H-O-H angle)"
printfn "- Demonstrates quantum chemistry for polyatomic molecules"
printfn "- Benchmark for quantum algorithms"
