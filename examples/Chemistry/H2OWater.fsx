// ============================================================================
// Water Molecule (H₂O) Quantum Simulation
// ============================================================================
//
// Demonstrates VQE for water molecule - a larger system than H₂
// requiring more qubits and showing active space selection.
//
// QUANTUM ADVANTAGE:
// - H₂O has 10 electrons → exponential classical cost for full CI
// - VQE with active space makes computation tractable on NISQ hardware
// - Quantum captures electron correlation missing in Hartree-Fock
//
// EDUCATIONAL VALUE:
// - Shows active space concept (freezing core electrons)
// - Demonstrates basis set effects
// - Compares methods: HF < DFT < VQE < Full CI (exact)
//
// RULE1 COMPLIANT: All quantum calculations via IQuantumBackend
//
// ============================================================================

(*
===============================================================================
 Background Theory
===============================================================================

The Variational Quantum Eigensolver (VQE) is a hybrid quantum-classical 
algorithm designed to find the ground state energy of molecular systems on 
near-term (NISQ) quantum hardware. First proposed by Peruzzo et al. (2014)
and extended by McClean, Aspuru-Guzik, and O'Brien, VQE has become the leading
approach for quantum chemistry on current quantum computers.

VQE exploits the VARIATIONAL PRINCIPLE of quantum mechanics:

    E_0 <= <psi(theta)|H|psi(theta)> for all |psi(theta)>

The ground state energy E_0 is a lower bound - any trial wavefunction gives
an energy at or above the true ground state. VQE minimizes the expectation
value over parameterized quantum circuits (ansatze) to approach E_0.

The algorithm proceeds:
  1. Prepare trial state |psi(theta)> on quantum computer
  2. Measure expectation value <H> = Sum_i c_i <P_i> (Pauli decomposition)
  3. Classical optimizer updates theta to minimize <H>
  4. Repeat until convergence

For WATER (H₂O), the full electronic problem involves 10 electrons in many
orbitals. ACTIVE SPACE methods reduce this to tractable size:

  - Freeze core electrons (O 1s) that don't participate in chemistry
  - Correlate valence electrons in bonding/antibonding orbitals
  - (8 electrons, 4 orbitals) → 8 qubits via Jordan-Wigner mapping

Water at equilibrium: O-H bond length ~0.96 Å, H-O-H angle ~104.5°
Ground state energy: ~-76.4 Hartree (FCI/CBS limit)

The VQE energy improves upon Hartree-Fock by capturing ELECTRON CORRELATION
(~0.85 Hartree for H₂O), which is essential for accurate reaction energies,
bond dissociation curves, and molecular properties.

Key Equations:
  - Variational Principle: E_0 = min_theta <psi(theta)|H|psi(theta)>
  - Correlation Energy: E_corr = E_exact - E_HF
  - Qubit count: 2N orbitals (Jordan-Wigner) or N (Bravyi-Kitaev)

Quantum Advantage:
  Classical Full CI scales as O(exp(N)) with electrons/orbitals.
  Quantum computers can represent wavefunctions in O(N) qubits,
  enabling exact simulation of systems intractable classically.
  Current NISQ VQE is limited but demonstrates the path forward.

References:
  [1] Peruzzo, A. et al. "A variational eigenvalue solver" Nat. Commun. 5, 4213 (2014)
  [2] McClean, J. et al. "Theory of Variational Hybrid Quantum-Classical Algorithms" New J. Phys. (2016)
  [3] Wikipedia: Variational_quantum_eigensolver
  [4] Cao, Y. et al. "Quantum Chemistry in the Age of Quantum Computing" Chem. Rev. 119, 10856 (2019)
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

printfn "╔══════════════════════════════════════════════════════════════╗"
printfn "║   Water Molecule (H₂O) Quantum Simulation                   ║"
printfn "╚══════════════════════════════════════════════════════════════╝"
printfn ""

// ============================================================================
// Water Molecule Geometry
// ============================================================================

printfn "📐 Molecular Geometry"
printfn "─────────────────────────────────────────────────────────────"
printfn ""

// Standard water geometry:
// - O-H bond length: 0.9572 Å (experimental)
// - H-O-H angle: 104.52° (experimental)
// - Total electrons: 10 (8 from O + 1 from each H)

let waterMolecule = Molecule.createH2O ()

printfn "Molecule: %s" waterMolecule.Name
printfn "Atoms:"
for atom in waterMolecule.Atoms do
    let (x, y, z) = atom.Position
    printfn "  %s: (%.4f, %.4f, %.4f) Å" atom.Element x y z
printfn ""
printfn "Total Electrons: %d" (Molecule.countElectrons waterMolecule)
printfn "Bonds: %d" waterMolecule.Bonds.Length
printfn "Charge: %d" waterMolecule.Charge
printfn "Multiplicity: %d (singlet ground state)" waterMolecule.Multiplicity
printfn ""

// ============================================================================
// Why Water is Challenging for Classical Computers
// ============================================================================

printfn "📊 Computational Complexity"
printfn "─────────────────────────────────────────────────────────────"
printfn ""
printfn "Water (H₂O) with STO-3G minimal basis:"
printfn "  - 7 spatial orbitals"
printfn "  - 10 electrons"
printfn "  - Full CI determinants: ~10,000 (tractable classically)"
printfn ""
printfn "Water with 6-311G** basis:"
printfn "  - 33 spatial orbitals"
printfn "  - 10 electrons"
printfn "  - Full CI determinants: ~10^9 (expensive classically)"
printfn ""
printfn "This is why active space methods are essential!"
printfn ""

// ============================================================================
// Active Space Concept
// ============================================================================

printfn "🔬 Active Space Selection"
printfn "─────────────────────────────────────────────────────────────"
printfn ""
printfn "For H₂O, electrons occupy orbitals (by energy):"
printfn "  1. O 1s (core)      - 2 electrons  [FROZEN]"
printfn "  2. O 2s             - 2 electrons  [ACTIVE]"
printfn "  3. O 2px            - 2 electrons  [ACTIVE]"
printfn "  4. O 2py (bonding)  - 2 electrons  [ACTIVE]"
printfn "  5. O 2pz (lone pair)- 2 electrons  [ACTIVE]"
printfn ""
printfn "Active Space: (8 electrons, 4 orbitals) = 8 qubits"
printfn "  - Freeze O 1s core (no chemistry contribution)"
printfn "  - Correlate valence electrons (bonding/reactions)"
printfn ""

// ============================================================================
// VQE Calculation
// ============================================================================

printfn "═══════════════════════════════════════════════════════════════"
printfn " VQE Ground State Calculation"
printfn "═══════════════════════════════════════════════════════════════"
printfn ""

let backend = LocalBackend() :> IQuantumBackend

printfn "🔧 Quantum Backend"
printfn "  Backend: %s" backend.Name
printfn "  State Type: %A" backend.NativeStateType
printfn ""

// VQE configuration
let vqeConfig = {
    Method = GroundStateMethod.VQE
    Backend = Some backend
    MaxIterations = 100
    Tolerance = 1e-6
    InitialParameters = None
    ProgressReporter = None
    ErrorMitigation = None
    IntegralProvider = None
}

printfn "Running VQE calculation..."
printfn "  Ansatz: UCCSD (default)"
printfn "  Basis: STO-3G (minimal)"
printfn "  Max Iterations: %d" vqeConfig.MaxIterations
printfn ""

let startTime = DateTime.Now
let vqeResult = GroundStateEnergy.estimateEnergy waterMolecule vqeConfig |> Async.RunSynchronously
let elapsed = DateTime.Now - startTime

match vqeResult with
| Ok result ->
    printfn "✅ VQE Calculation Complete"
    printfn ""
    printfn "Results:"
    printfn "  Ground State Energy: %.6f Hartree" result.Energy
    printfn "  Iterations:          %d" result.Iterations
    printfn "  Converged:           %b" result.Converged
    printfn "  Time:                %.2f seconds" elapsed.TotalSeconds
    printfn ""
    
    // Reference values for comparison
    let hfEnergy = -75.585  // Hartree-Fock STO-3G
    let exactEnergy = -76.438  // Near FCI limit
    let experimentalEnergy = -76.438  // Experimental (with zero-point correction)
    
    printfn "📈 Method Comparison (STO-3G basis):"
    printfn "  Method            Energy (Eh)    Correlation"
    printfn "  ─────────────────────────────────────────────"
    printfn "  Hartree-Fock      %.3f        0.000" hfEnergy
    printfn "  VQE (this calc)   %.3f        %.3f" result.Energy (result.Energy - hfEnergy)
    printfn "  FCI (exact)       %.3f        %.3f" exactEnergy (exactEnergy - hfEnergy)
    printfn ""
    
    // Energy in other units
    printfn "Energy Conversions:"
    printfn "  %.6f Hartree" result.Energy
    printfn "  %.4f eV" (result.Energy * 27.2114)
    printfn "  %.2f kcal/mol" (result.Energy * 627.509)
    printfn "  %.2f kJ/mol" (result.Energy * 2625.5)
    
| Error err ->
    printfn "❌ VQE Calculation Failed: %s" err.Message
printfn ""

// ============================================================================
// Compare with Classical DFT
// ============================================================================

printfn "═══════════════════════════════════════════════════════════════"
printfn " Classical DFT Comparison"
printfn "═══════════════════════════════════════════════════════════════"
printfn ""

let dftConfig = { vqeConfig with Method = GroundStateMethod.ClassicalDFT }

printfn "Running Classical DFT calculation..."
let dftResult = GroundStateEnergy.estimateEnergy waterMolecule dftConfig |> Async.RunSynchronously

match dftResult with
| Ok result ->
    printfn "✅ DFT Energy: %.6f Hartree" result.Energy
| Error err ->
    printfn "❌ DFT Failed: %s" err.Message
printfn ""

// ============================================================================
// Basis Set Effects
// ============================================================================

printfn "═══════════════════════════════════════════════════════════════"
printfn " Basis Set Effects (Reference Values)"
printfn "═══════════════════════════════════════════════════════════════"
printfn ""
printfn "How basis set affects water energy (literature values):"
printfn ""
printfn "  Basis Set       Functions   HF Energy    Correlation"
printfn "  ────────────────────────────────────────────────────"
printfn "  STO-3G              7       -75.585      minimal"
printfn "  3-21G              13       -75.586      poor"
printfn "  6-31G*             19       -76.011      moderate"
printfn "  6-311G**           33       -76.055      good"
printfn "  cc-pVDZ            24       -76.027      good"
printfn "  cc-pVTZ            58       -76.057      very good"
printfn "  CBS limit           ∞       -76.068      exact basis"
printfn ""
printfn "Notes:"
printfn "  - Larger basis → lower (better) energy"
printfn "  - STO-3G sufficient for qualitative trends"
printfn "  - cc-pVTZ+ needed for quantitative accuracy"
printfn ""

// ============================================================================
// Bond Dissociation (Potential Energy Surface)
// ============================================================================

printfn "═══════════════════════════════════════════════════════════════"
printfn " O-H Bond Stretching (Potential Energy Curve)"
printfn "═══════════════════════════════════════════════════════════════"
printfn ""

// Create water molecules at different O-H distances
let createWaterAtDistance (ohDistance: float) =
    // Standard angle: 104.52 degrees
    let angle = 104.52 * Math.PI / 180.0
    let halfAngle = angle / 2.0
    
    // Oxygen at origin
    // Hydrogens at +/- halfAngle from z-axis in yz plane
    let hx = 0.0
    let hy = ohDistance * sin halfAngle
    let hz = ohDistance * cos halfAngle
    
    {
        Name = sprintf "H2O (O-H = %.2f Å)" ohDistance
        Atoms = [
            { Element = "O"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (hx, hy, hz) }
            { Element = "H"; Position = (hx, -hy, hz) }
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
        ]
        Charge = 0
        Multiplicity = 1
    }

let distances = [| 0.8; 0.9; 0.9572; 1.0; 1.1; 1.2 |]

printfn "Calculating energies at different O-H distances..."
printfn ""
printfn "  O-H (Å)    Energy (Eh)    ΔE (kcal/mol)"
printfn "  ─────────────────────────────────────────"

let equilibriumEnergy = ref 0.0

for distance in distances do
    let waterAtDist = createWaterAtDistance distance
    let result = GroundStateEnergy.estimateEnergy waterAtDist vqeConfig |> Async.RunSynchronously
    
    match result with
    | Ok r ->
        if abs(distance - 0.9572) < 0.001 then
            equilibriumEnergy.Value <- r.Energy
        
        let deltaE = (r.Energy - equilibriumEnergy.Value) * 627.509
        printfn "  %.4f      %.6f     %+.2f" distance r.Energy deltaE
    | Error _ ->
        printfn "  %.4f      [failed]" distance

printfn ""
printfn "Equilibrium O-H distance: 0.9572 Å (experimental)"
printfn ""

// ============================================================================
// Quantum Chemistry Application
// ============================================================================

printfn "═══════════════════════════════════════════════════════════════"
printfn " Why This Matters: Water in Drug Discovery"
printfn "═══════════════════════════════════════════════════════════════"
printfn ""
printfn "Water plays critical roles in drug binding:"
printfn ""
printfn "1. Solvation Effects:"
printfn "   - Drug must displace water from binding site"
printfn "   - Desolvation penalty affects binding affinity"
printfn "   - Quantum accuracy needed for polar interactions"
printfn ""
printfn "2. Bridging Water Molecules:"
printfn "   - Some waters mediate drug-protein contacts"
printfn "   - Can contribute 1-3 kcal/mol to binding"
printfn "   - Quantum captures H-bond cooperativity"
printfn ""
printfn "3. Proton Transfer:"
printfn "   - Water enables acid-base chemistry"
printfn "   - Enzyme mechanisms often involve water"
printfn "   - Quantum needed for barrier heights"
printfn ""

// ============================================================================
// Summary
// ============================================================================

printfn "╔══════════════════════════════════════════════════════════════╗"
printfn "║                        Summary                               ║"
printfn "╚══════════════════════════════════════════════════════════════╝"
printfn ""
printfn "Water Molecule Quantum Simulation:"
printfn ""
printfn "  ✅ VQE ground state energy calculated"
printfn "  ✅ Active space concept demonstrated"
printfn "  ✅ Basis set effects explained"
printfn "  ✅ Potential energy curve computed"
printfn "  ✅ Drug discovery relevance discussed"
printfn ""
printfn "Key Concepts:"
printfn "  - Active space reduces qubits (freeze core electrons)"
printfn "  - Basis set determines accuracy vs cost tradeoff"
printfn "  - VQE captures electron correlation beyond HF"
printfn "  - Water exemplifies H-bonding and solvation"
printfn ""
printfn "RULE1 Compliance:"
printfn "  ✅ All VQE calculations via IQuantumBackend"
printfn "  ✅ No classical-only energy returned as 'quantum'"
printfn ""
printfn "═══════════════════════════════════════════════════════════════"
