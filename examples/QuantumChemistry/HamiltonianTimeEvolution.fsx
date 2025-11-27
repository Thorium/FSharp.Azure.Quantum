// Hamiltonian Time Evolution Example
// Demonstrates Trotter-Suzuki simulation for molecular dynamics

#r "nuget: FSharp.Azure.Quantum"

open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.LocalSimulator

printfn "=== Hamiltonian Time Evolution Simulation ==="
printfn ""

// Create H2 molecule
let h2 = Molecule.createH2 0.74

// Build molecular Hamiltonian
let hamiltonianResult = MolecularHamiltonian.build h2

match hamiltonianResult with
| Error msg ->
    printfn "✗ Hamiltonian construction failed: %s" msg
    
| Ok hamiltonian ->
    printfn "✓ Molecular Hamiltonian constructed"
    printfn "  Qubits: %d" hamiltonian.NumQubits
    printfn "  Terms: %d" hamiltonian.Terms.Length
    printfn ""
    
    // Initialize quantum state to |00...0⟩
    let initialState = StateVector.init hamiltonian.NumQubits
    printfn "Initial state: |00...0⟩"
    printfn "  Norm: %.6f" (StateVector.norm initialState)
    printfn ""
    
    // Configure time evolution simulation
    let config = {
        HamiltonianSimulation.SimulationConfig.Time = 1.0        // 1.0 atomic time units
        HamiltonianSimulation.SimulationConfig.TrotterSteps = 20 // 20 Trotter steps
        HamiltonianSimulation.SimulationConfig.TrotterOrder = 2  // 2nd order (more accurate)
    }
    
    printfn "=== Simulation Configuration ==="
    printfn "  Evolution time: %.1f a.u." config.Time
    printfn "  Trotter steps: %d" config.TrotterSteps
    printfn "  Trotter order: %d (symmetric splitting)" config.TrotterOrder
    printfn "  Time step (Δt): %.4f a.u." (config.Time / float config.TrotterSteps)
    printfn ""
    
    // Run time evolution simulation
    printfn "Running time evolution: exp(-iHt)|ψ₀⟩"
    let finalState = HamiltonianSimulation.simulate hamiltonian initialState config
    
    printfn "✓ Simulation complete"
    printfn "  Final norm: %.6f (should be 1.0 for unitary evolution)" (StateVector.norm finalState)
    printfn ""
    
    // Analyze state probabilities
    printfn "=== Final State Analysis ==="
    printfn "Computational basis state probabilities:"
    
    for i in 0 .. min 3 (StateVector.dimension finalState - 1) do
        let amplitude = StateVector.getAmplitude i finalState
        let probability = amplitude.Magnitude ** 2.0
        
        // Convert basis index to binary string
        let binaryStr = 
            [hamiltonian.NumQubits - 1 .. -1 .. 0]
            |> List.map (fun bit -> if (i &&& (1 <<< bit)) <> 0 then "1" else "0")
            |> String.concat ""
        
        if probability > 1e-4 then
            printfn "  |%s⟩: %.4f (%.2f%%)" binaryStr probability (probability * 100.0)
    
    printfn ""
    
    // Compare different Trotter orders
    printfn "=== Trotter Order Comparison ==="
    
    let config1stOrder = { config with TrotterOrder = 1 }
    let state1st = HamiltonianSimulation.simulate hamiltonian initialState config1stOrder
    
    let config2ndOrder = { config with TrotterOrder = 2 }
    let state2nd = HamiltonianSimulation.simulate hamiltonian initialState config2ndOrder
    
    printfn "  1st order Trotter norm: %.6f" (StateVector.norm state1st)
    printfn "  2nd order Trotter norm: %.6f" (StateVector.norm state2nd)
    printfn "  (Both should be ~1.0 for valid unitary evolution)"
    printfn ""
    
    printfn "=== Applications ==="
    printfn "- Molecular dynamics simulation"
    printfn "- Chemical reaction pathways"
    printfn "- Quantum annealing"
    printfn "- Adiabatic state preparation for VQE"
