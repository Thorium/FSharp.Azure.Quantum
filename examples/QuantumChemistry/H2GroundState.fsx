// H2 Ground State Calculation using Quantum Chemistry Builder (TKT-79)
// Demonstrates the F# computation expression API for quantum chemistry

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"


open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder

// Example 1: Simple H2 ground state at equilibrium bond length
printfn "=== Example 1: H2 Ground State at 0.74 Å ==="

let h2Problem = quantumChemistry {
    molecule (h2 0.74)
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
    molecule (h2o 0.96 104.5)  // Standard geometry
    basis "sto-3g"
    ansatz HEA  // Hardware-efficient ansatz (faster)
    maxIterations 150
}

printfn "Problem created successfully!"
printfn "Molecule: %s" h2oProblem.Molecule.Value.Name
printfn "Number of atoms: %d" h2oProblem.Molecule.Value.Atoms.Length

// Example 3: LiH (lithium hydride)
printfn "\n=== Example 3: LiH Ground State ==="

let lihProblem = quantumChemistry {
    molecule (lih 1.6)
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
    molecule (h2 0.74)
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
        molecule (h2 distance)
        basis "sto-3g"
        ansatz UCCSD
    }
    printfn "  Distance %.2f Å: Problem ready" distance

printfn "\n✅ All examples completed successfully!"
printfn "The Quantum Chemistry Builder (TKT-79) is working correctly."
