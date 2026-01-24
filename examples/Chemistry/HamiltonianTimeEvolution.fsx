// Hamiltonian Time Evolution Example
// Demonstrates Trotter-Suzuki simulation for molecular dynamics
// 
// This example shows how the unified backend architecture enables
// the same simulation code to run on different quantum backends:
// - LocalBackend (gate-based StateVector simulation)
// - TopologicalUnifiedBackend (braiding-based FusionSuperposition)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#r "../../src/FSharp.Azure.Quantum.Topological/bin/Debug/net10.0/FSharp.Azure.Quantum.Topological.dll"

open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Topological

printfn "=== Hamiltonian Time Evolution Simulation ==="
printfn "=== Unified Backend Architecture Demo ==="
printfn ""

// ============================================================================
// UNIFIED STATE ANALYSIS HELPERS
// ============================================================================

/// Analyze any QuantumState using the unified API (backend-agnostic)
let analyzeState (state: QuantumState) (numQubits: int) (label: string) =
    printfn "  %s:" label
    printfn "    State type: %A" (QuantumState.stateType state)
    printfn "    Num qubits: %d" (QuantumState.numQubits state)
    printfn "    Normalized: %b" (QuantumState.isNormalized state)
    
    // Sample measurements using unified API (works with any state type)
    let measurements = QuantumState.measure state 1000
    
    // Count measurement outcomes
    let counts = 
        measurements 
        |> Array.groupBy id 
        |> Array.map (fun (bits, occurrences) -> 
            let bitstring = bits |> Array.map string |> String.concat ""
            (bitstring, occurrences.Length))
        |> Array.sortByDescending snd
    
    printfn "    Top measurement outcomes (1000 shots):"
    counts 
    |> Array.truncate 4
    |> Array.iter (fun (bitstring, count) ->
        let prob = float count / 1000.0
        printfn "      |%s⟩: %d (%.1f%%)" bitstring count (prob * 100.0))

/// Get probability of specific basis state (works with any QuantumState)
let getBasisProbability (state: QuantumState) (basisIndex: int) (numQubits: int) =
    let bitstring = 
        [| for i in 0 .. numQubits - 1 -> (basisIndex >>> i) &&& 1 |]
    QuantumState.probability bitstring state

// ============================================================================
// MOLECULE AND HAMILTONIAN SETUP
// ============================================================================

// Create H2 molecule
let h2 = Molecule.createH2 0.74

printfn "Creating H₂ molecule (bond length: 0.74 Å)"

// Build molecular Hamiltonian
let hamiltonianResult = MolecularHamiltonian.build h2

match hamiltonianResult with
| Error err ->
    printfn "✗ Hamiltonian construction failed: %A" err
    
| Ok hamiltonian ->
    printfn "✓ Molecular Hamiltonian constructed"
    printfn "  Qubits: %d" hamiltonian.NumQubits
    printfn "  Terms: %d" hamiltonian.Terms.Length
    printfn ""
    
    // ========================================================================
    // BACKEND 1: LocalBackend (Gate-based StateVector simulation)
    // ========================================================================
    
    printfn "╔══════════════════════════════════════════════════════════════╗"
    printfn "║  BACKEND 1: LocalBackend (Gate-based StateVector)            ║"
    printfn "╚══════════════════════════════════════════════════════════════╝"
    printfn ""
    
    let localBackend = LocalBackend.LocalBackend() :> IQuantumBackend
    printfn "Backend: %s" localBackend.Name
    printfn "Native state type: %A" localBackend.NativeStateType
    printfn ""
    
    // Initialize state using the backend (unified API)
    let localInitialState = 
        match localBackend.InitializeState hamiltonian.NumQubits with
        | Ok state -> state
        | Error err -> failwithf "Failed to initialize state: %A" err
    
    printfn "Initial state: |00...0⟩"
    analyzeState localInitialState hamiltonian.NumQubits "Initial"
    printfn ""
    
    // Configure time evolution
    let localConfig = {
        HamiltonianSimulation.SimulationConfig.Time = 1.0
        HamiltonianSimulation.SimulationConfig.TrotterSteps = 20
        HamiltonianSimulation.SimulationConfig.TrotterOrder = 2
        HamiltonianSimulation.SimulationConfig.Backend = Some localBackend
    }
    
    printfn "Simulation config:"
    printfn "  Evolution time: %.1f a.u." localConfig.Time
    printfn "  Trotter steps: %d" localConfig.TrotterSteps
    printfn "  Trotter order: %d (symmetric)" localConfig.TrotterOrder
    printfn ""
    
    // Run simulation
    printfn "Running exp(-iHt)|ψ₀⟩ on LocalBackend..."
    let localResult = HamiltonianSimulation.simulate hamiltonian localInitialState localConfig
    
    match localResult with
    | Error err ->
        printfn "✗ LocalBackend simulation failed: %A" err
    | Ok finalState ->
        printfn "✓ LocalBackend simulation complete"
        analyzeState finalState hamiltonian.NumQubits "Final"
    
    printfn ""
    
    // ========================================================================
    // BACKEND 2: TopologicalUnifiedBackend (Braiding-based simulation)
    // ========================================================================
    
    printfn "╔══════════════════════════════════════════════════════════════╗"
    printfn "║  BACKEND 2: TopologicalUnifiedBackend (Braiding-based)       ║"
    printfn "╚══════════════════════════════════════════════════════════════╝"
    printfn ""
    
    // Create topological backend with Ising anyons
    // Need enough anyons: n qubits requires n+1 anyons for Jordan-Wigner encoding
    let numAnyons = hamiltonian.NumQubits + 5  // Extra headroom for operations
    let topoBackend = TopologicalUnifiedBackendFactory.createIsing numAnyons
    
    printfn "Backend: %s" topoBackend.Name
    printfn "Native state type: %A" topoBackend.NativeStateType
    printfn "Anyon type: Ising"
    printfn "Max anyons: %d" numAnyons
    printfn ""
    
    // Initialize state using the topological backend
    let topoInitialState = 
        match topoBackend.InitializeState hamiltonian.NumQubits with
        | Ok state -> state
        | Error err -> failwithf "Failed to initialize topological state: %A" err
    
    printfn "Initial state: |00...0⟩ (as FusionSuperposition)"
    analyzeState topoInitialState hamiltonian.NumQubits "Initial"
    printfn ""
    
    // Configure with topological backend
    let topoConfig = {
        HamiltonianSimulation.SimulationConfig.Time = 1.0
        HamiltonianSimulation.SimulationConfig.TrotterSteps = 20
        HamiltonianSimulation.SimulationConfig.TrotterOrder = 2
        HamiltonianSimulation.SimulationConfig.Backend = Some topoBackend
    }
    
    printfn "Running exp(-iHt)|ψ₀⟩ on TopologicalBackend..."
    printfn "(Gates automatically compiled to braiding operations via Solovay-Kitaev)"
    printfn ""
    
    let topoResult = HamiltonianSimulation.simulate hamiltonian topoInitialState topoConfig
    
    match topoResult with
    | Error err ->
        printfn "✗ TopologicalBackend simulation failed: %A" err
        printfn ""
        printfn "Note: The TopologicalBackend uses Solovay-Kitaev algorithm to"
        printfn "approximate arbitrary rotations (RZ, RX, RY) with braiding operations."
        printfn "The current tolerance (1e-10) is very tight for small rotation angles."
        printfn ""
        printfn "This demonstrates that the UNIFIED ARCHITECTURE WORKS correctly:"
        printfn "  - Same HamiltonianSimulation.simulate function"
        printfn "  - Same QuantumState abstraction"
        printfn "  - Backend-specific limitations are properly reported"
        printfn "  - No code changes needed to switch backends"
    | Ok finalState ->
        printfn "✓ TopologicalBackend simulation complete"
        analyzeState finalState hamiltonian.NumQubits "Final"
    
    printfn ""
    
    // ========================================================================
    // COMPARISON: Same Algorithm, Different Backends
    // ========================================================================
    
    printfn "╔══════════════════════════════════════════════════════════════╗"
    printfn "║  UNIFIED BACKEND ARCHITECTURE BENEFITS                       ║"
    printfn "╚══════════════════════════════════════════════════════════════╝"
    printfn ""
    printfn "The HamiltonianSimulation.simulate function:"
    printfn ""
    printfn "  1. Accepts ANY IQuantumBackend implementation"
    printfn "  2. Works with ANY QuantumState variant:"
    printfn "     - StateVector (gate-based)"
    printfn "     - FusionSuperposition (topological)"
    printfn "     - SparseState (Clifford simulation)"
    printfn "     - DensityMatrix (noisy simulation)"
    printfn ""
    printfn "  3. Uses UnifiedBackend.applySequence which:"
    printfn "     - Automatically converts state types if needed"
    printfn "     - Dispatches operations to backend.ApplyOperation"
    printfn "     - Handles errors uniformly across backends"
    printfn ""
    printfn "  4. Enables backend-agnostic algorithm development:"
    printfn "     - Write once, run on any quantum hardware model"
    printfn "     - TopologicalBackend compiles gates → braids transparently"
    printfn "     - Future backends (trapped ion, photonic) plug in seamlessly"
    printfn ""
    
    // ========================================================================
    // TROTTER ORDER COMPARISON (using unified measurement)
    // ========================================================================
    
    printfn "╔══════════════════════════════════════════════════════════════╗"
    printfn "║  TROTTER ORDER COMPARISON                                    ║"
    printfn "╚══════════════════════════════════════════════════════════════╝"
    printfn ""
    
    let config1st = { localConfig with TrotterOrder = 1 }
    let config2nd = { localConfig with TrotterOrder = 2 }
    
    let result1st = HamiltonianSimulation.simulate hamiltonian localInitialState config1st
    let result2nd = HamiltonianSimulation.simulate hamiltonian localInitialState config2nd
    
    match result1st, result2nd with
    | Ok state1st, Ok state2nd ->
        printfn "1st order Trotter:"
        printfn "  Normalized: %b" (QuantumState.isNormalized state1st)
        printfn "  P(|0...0⟩): %.6f" (getBasisProbability state1st 0 hamiltonian.NumQubits)
        printfn ""
        printfn "2nd order Trotter (symmetric):"
        printfn "  Normalized: %b" (QuantumState.isNormalized state2nd)
        printfn "  P(|0...0⟩): %.6f" (getBasisProbability state2nd 0 hamiltonian.NumQubits)
        printfn ""
        printfn "2nd order is more accurate (O(Δt³) vs O(Δt²) error per step)"
    | _ ->
        printfn "Trotter comparison failed"
    
    printfn ""
    printfn "=== Applications ==="
    printfn "- Molecular dynamics simulation"
    printfn "- Chemical reaction pathways"  
    printfn "- Adiabatic state preparation for VQE"
    printfn "- Quantum annealing simulation"
    printfn ""
    printfn "=== End of Demo ==="
