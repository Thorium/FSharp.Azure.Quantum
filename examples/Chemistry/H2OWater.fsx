// ==============================================================================
// Water Molecule (H2O) Quantum Simulation
// ==============================================================================
// Demonstrates VQE for the water molecule -- a larger system than H2
// requiring more qubits and showing active space selection.
//
// QUANTUM ADVANTAGE:
//   - H2O has 10 electrons -> exponential classical cost for full CI
//   - VQE with active space makes computation tractable on NISQ hardware
//   - Quantum captures electron correlation missing in Hartree-Fock
//
// EDUCATIONAL VALUE:
//   - Shows active space concept (freezing core electrons)
//   - Demonstrates basis set effects
//   - Compares methods: HF < DFT < VQE < Full CI (exact)
//
// Usage:
//   dotnet fsi H2OWater.fsx
//   dotnet fsi H2OWater.fsx -- --bond-length 1.0
//   dotnet fsi H2OWater.fsx -- --max-iterations 200 --tolerance 1e-8
//   dotnet fsi H2OWater.fsx -- --scan 0.8,0.9,0.96,1.0,1.1,1.2
//   dotnet fsi H2OWater.fsx -- --output results.json --csv results.csv --quiet
//
// ==============================================================================

(*
Background Theory
-----------------
The Variational Quantum Eigensolver (VQE) is a hybrid quantum-classical
algorithm designed to find the ground state energy of molecular systems on
near-term (NISQ) quantum hardware. First proposed by Peruzzo et al. (2014)
and extended by McClean, Aspuru-Guzik, and O'Brien, VQE has become the leading
approach for quantum chemistry on current quantum computers.

VQE exploits the VARIATIONAL PRINCIPLE of quantum mechanics:

    E_0 <= <psi(theta)|H|psi(theta)> for all |psi(theta)>

The ground state energy E_0 is a lower bound -- any trial wavefunction gives
an energy at or above the true ground state. VQE minimizes the expectation
value over parameterized quantum circuits (ansatze) to approach E_0.

The algorithm proceeds:
  1. Prepare trial state |psi(theta)> on quantum computer
  2. Measure expectation value <H> = Sum_i c_i <P_i> (Pauli decomposition)
  3. Classical optimizer updates theta to minimize <H>
  4. Repeat until convergence

For WATER (H2O), the full electronic problem involves 10 electrons in many
orbitals. ACTIVE SPACE methods reduce this to tractable size:

  - Freeze core electrons (O 1s) that don't participate in chemistry
  - Correlate valence electrons in bonding/antibonding orbitals
  - (8 electrons, 4 orbitals) -> 8 qubits via Jordan-Wigner mapping

Water at equilibrium: O-H bond length ~0.96 A, H-O-H angle ~104.5 degrees
Ground state energy: ~-76.4 Hartree (FCI/CBS limit)

The VQE energy improves upon Hartree-Fock by capturing ELECTRON CORRELATION
(~0.85 Hartree for H2O), which is essential for accurate reaction energies,
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

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.QuantumChemistry.QuantumChemistryBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "H2OWater.fsx" "Water molecule (H2O) ground state via VQE with active space"
    [ { Cli.OptionSpec.Name = "bond-length"; Description = "O-H bond length in Angstroms"; Default = Some "0.9572" }
      { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "100" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Convergence tolerance"; Default = Some "1e-6" }
      { Cli.OptionSpec.Name = "scan"; Description = "Comma-separated O-H distances for PES scan"; Default = Some "0.8,0.9,0.9572,1.0,1.1,1.2" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let bondLength = Cli.getFloatOr "bond-length" 0.9572 args
let maxIterations = Cli.getIntOr "max-iterations" 100 args
let tolerance = Cli.getFloatOr "tolerance" 1e-6 args
let scanDistances =
    Cli.getOr "scan" "0.8,0.9,0.9572,1.0,1.1,1.2" args
    |> fun s -> s.Split(',')
    |> Array.choose (fun s ->
        match Double.TryParse(s.Trim()) with
        | true, v -> Some v
        | false, _ -> None)
    |> Array.toList

// ==============================================================================
// PART 1: Water Molecule Geometry
// ==============================================================================

if not quiet then
    printfn "============================================================"
    printfn "  Water Molecule (H2O) Quantum Simulation"
    printfn "============================================================"
    printfn ""
    printfn "Molecular Geometry"
    printfn "------------------------------------------------------------"
    printfn ""

let waterMolecule = Molecule.createH2O ()

if not quiet then
    printfn "Molecule: %s" waterMolecule.Name
    printfn "Atoms:"
    for atom in waterMolecule.Atoms do
        let (x, y, z) = atom.Position
        printfn "  %s: (%.4f, %.4f, %.4f) A" atom.Element x y z
    printfn ""
    printfn "Total Electrons: %d" (Molecule.countElectrons waterMolecule)
    printfn "Bonds: %d" waterMolecule.Bonds.Length
    printfn "Charge: %d" waterMolecule.Charge
    printfn "Multiplicity: %d (singlet ground state)" waterMolecule.Multiplicity
    printfn ""

// ==============================================================================
// PART 2: Active Space and Computational Complexity
// ==============================================================================

if not quiet then
    printfn "Computational Complexity"
    printfn "------------------------------------------------------------"
    printfn ""
    printfn "Water (H2O) with STO-3G minimal basis:"
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

    printfn "Active Space Selection"
    printfn "------------------------------------------------------------"
    printfn ""
    printfn "For H2O, electrons occupy orbitals (by energy):"
    printfn "  1. O 1s (core)       - 2 electrons  [FROZEN]"
    printfn "  2. O 2s              - 2 electrons  [ACTIVE]"
    printfn "  3. O 2px             - 2 electrons  [ACTIVE]"
    printfn "  4. O 2py (bonding)   - 2 electrons  [ACTIVE]"
    printfn "  5. O 2pz (lone pair) - 2 electrons  [ACTIVE]"
    printfn ""
    printfn "Active Space: (8 electrons, 4 orbitals) = 8 qubits"
    printfn "  - Freeze O 1s core (no chemistry contribution)"
    printfn "  - Correlate valence electrons (bonding/reactions)"
    printfn ""

// ==============================================================================
// PART 3: VQE Calculation
// ==============================================================================

if not quiet then
    printfn "============================================================"
    printfn "  VQE Ground State Calculation"
    printfn "============================================================"
    printfn ""

let backend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn "Quantum Backend:"
    printfn "  Backend: %s" backend.Name
    printfn "  State Type: %A" backend.NativeStateType
    printfn ""

let vqeConfig = {
    Method = GroundStateMethod.VQE
    Backend = Some backend
    MaxIterations = maxIterations
    Tolerance = tolerance
    InitialParameters = None
    ProgressReporter = None
    ErrorMitigation = None
    IntegralProvider = None
}

if not quiet then
    printfn "Running VQE calculation..."
    printfn "  Ansatz: UCCSD (default)"
    printfn "  Basis: STO-3G (minimal)"
    printfn "  Max Iterations: %d" vqeConfig.MaxIterations
    printfn "  Tolerance: %.2e" vqeConfig.Tolerance
    printfn ""

let startTime = DateTime.Now
let vqeResult = GroundStateEnergy.estimateEnergy waterMolecule vqeConfig |> Async.RunSynchronously
let elapsed = DateTime.Now - startTime

/// Reference energies for comparison.
let hfEnergy = -75.585
let exactEnergy = -76.438

let vqeRow =
    match vqeResult with
    | Ok result ->
        if not quiet then
            printfn "VQE Calculation Complete"
            printfn ""
            printfn "Results:"
            printfn "  Ground State Energy: %.6f Hartree" result.Energy
            printfn "  Iterations:          %d" result.Iterations
            printfn "  Converged:           %b" result.Converged
            printfn "  Time:                %.2f seconds" elapsed.TotalSeconds
            printfn ""

            printfn "Method Comparison (STO-3G basis):"
            printfn "  Method            Energy (Eh)    Correlation"
            printfn "  -----------------------------------------------"
            printfn "  Hartree-Fock      %.3f        0.000" hfEnergy
            printfn "  VQE (this calc)   %.3f        %.3f" result.Energy (result.Energy - hfEnergy)
            printfn "  FCI (exact)       %.3f        %.3f" exactEnergy (exactEnergy - hfEnergy)
            printfn ""

            printfn "Energy Conversions:"
            printfn "  %.6f Hartree" result.Energy
            printfn "  %.4f eV" (result.Energy * 27.2114)
            printfn "  %.2f kcal/mol" (result.Energy * 627.509)
            printfn "  %.2f kJ/mol" (result.Energy * 2625.5)
            printfn ""

        Some (
            [ "Section", "VQE"
              "BondLength_A", sprintf "%.4f" bondLength
              "Energy_Hartree", sprintf "%.6f" result.Energy
              "Energy_eV", sprintf "%.4f" (result.Energy * 27.2114)
              "Energy_kcal_mol", sprintf "%.2f" (result.Energy * 627.509)
              "Iterations", sprintf "%d" result.Iterations
              "Converged", sprintf "%b" result.Converged
              "Time_s", sprintf "%.2f" elapsed.TotalSeconds ]
            |> Map.ofList)
    | Error err ->
        if not quiet then
            printfn "VQE Calculation Failed: %s" err.Message
            printfn ""
        None

// ==============================================================================
// PART 4: Classical DFT Comparison
// ==============================================================================

if not quiet then
    printfn "============================================================"
    printfn "  Classical DFT Comparison"
    printfn "============================================================"
    printfn ""

let dftConfig = { vqeConfig with Method = GroundStateMethod.ClassicalDFT }

if not quiet then
    printfn "Running Classical DFT calculation..."

let dftResult = GroundStateEnergy.estimateEnergy waterMolecule dftConfig |> Async.RunSynchronously

let dftRow =
    match dftResult with
    | Ok result ->
        if not quiet then
            printfn "DFT Energy: %.6f Hartree" result.Energy
            printfn ""
        Some (
            [ "Section", "DFT"
              "BondLength_A", sprintf "%.4f" bondLength
              "Energy_Hartree", sprintf "%.6f" result.Energy
              "Energy_eV", sprintf "%.4f" (result.Energy * 27.2114)
              "Energy_kcal_mol", sprintf "%.2f" (result.Energy * 627.509)
              "Iterations", sprintf "%d" result.Iterations
              "Converged", sprintf "%b" result.Converged ]
            |> Map.ofList)
    | Error err ->
        if not quiet then
            printfn "DFT Failed: %s" err.Message
            printfn ""
        None

// ==============================================================================
// PART 5: Basis Set Effects (Reference Table)
// ==============================================================================

if not quiet then
    printfn "============================================================"
    printfn "  Basis Set Effects (Reference Values)"
    printfn "============================================================"
    printfn ""
    printfn "How basis set affects water energy (literature values):"
    printfn ""
    printfn "  Basis Set       Functions   HF Energy    Correlation"
    printfn "  -------------------------------------------------------"
    printfn "  STO-3G              7       -75.585      minimal"
    printfn "  3-21G              13       -75.586      poor"
    printfn "  6-31G*             19       -76.011      moderate"
    printfn "  6-311G**           33       -76.055      good"
    printfn "  cc-pVDZ            24       -76.027      good"
    printfn "  cc-pVTZ            58       -76.057      very good"
    printfn "  CBS limit          inf      -76.068      exact basis"
    printfn ""
    printfn "Notes:"
    printfn "  - Larger basis -> lower (better) energy"
    printfn "  - STO-3G sufficient for qualitative trends"
    printfn "  - cc-pVTZ+ needed for quantitative accuracy"
    printfn ""

// ==============================================================================
// PART 6: O-H Bond Stretching (Potential Energy Surface)
// ==============================================================================

if not quiet then
    printfn "============================================================"
    printfn "  O-H Bond Stretching (Potential Energy Curve)"
    printfn "============================================================"
    printfn ""

/// Create a water molecule at a given O-H distance (Angstroms).
/// Uses the experimental H-O-H angle of 104.52 degrees.
let createWaterAtDistance (ohDistance: float) =
    let angle = 104.52 * Math.PI / 180.0
    let halfAngle = angle / 2.0
    let hx = 0.0
    let hy = ohDistance * sin halfAngle
    let hz = ohDistance * cos halfAngle
    {
        Name = sprintf "H2O (O-H = %.2f A)" ohDistance
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

if not quiet then
    printfn "Calculating energies at different O-H distances..."
    printfn "Distances: %A" scanDistances
    printfn ""

let scanResults =
    scanDistances
    |> List.choose (fun distance ->
        let waterAtDist = createWaterAtDistance distance
        let result = GroundStateEnergy.estimateEnergy waterAtDist vqeConfig |> Async.RunSynchronously
        match result with
        | Ok r ->
            if not quiet then
                printfn "  O-H %.4f A: %.6f Hartree (%d iterations)" distance r.Energy r.Iterations
            [ "Section", sprintf "Scan_%.4f" distance
              "BondLength_A", sprintf "%.4f" distance
              "Energy_Hartree", sprintf "%.6f" r.Energy
              "Energy_eV", sprintf "%.4f" (r.Energy * 27.2114)
              "Energy_kcal_mol", sprintf "%.2f" (r.Energy * 627.509)
              "Iterations", sprintf "%d" r.Iterations
              "Converged", sprintf "%b" r.Converged ]
            |> Map.ofList
            |> Some
        | Error _ ->
            if not quiet then
                printfn "  O-H %.4f A: FAILED" distance
            None)

// Compute delta-E relative to equilibrium if available
if not quiet && scanResults.Length > 0 then
    let eqResult =
        scanResults
        |> List.tryFind (fun m ->
            match m |> Map.tryFind "BondLength_A" with
            | Some s ->
                match Double.TryParse s with
                | true, v -> abs(v - 0.9572) < 0.001
                | false, _ -> false
            | None -> false)
    match eqResult with
    | Some eqMap ->
        match eqMap |> Map.tryFind "Energy_Hartree" with
        | Some eqEStr ->
            match Double.TryParse eqEStr with
            | true, eqE ->
                printfn ""
                printfn "  Relative energies (vs equilibrium):"
                for m in scanResults do
                    let d = m |> Map.tryFind "BondLength_A" |> Option.defaultValue "?"
                    match m |> Map.tryFind "Energy_Hartree" with
                    | Some eStr ->
                        match Double.TryParse eStr with
                        | true, e ->
                            let deltaKcal = (e - eqE) * 627.509
                            printfn "    O-H %s A: %+.2f kcal/mol" d deltaKcal
                        | false, _ -> ()
                    | None -> ()
            | false, _ -> ()
        | None -> ()
    | None -> ()
    printfn ""

// ==============================================================================
// PART 7: Application Context
// ==============================================================================

if not quiet then
    printfn "============================================================"
    printfn "  Why This Matters: Water in Drug Discovery"
    printfn "============================================================"
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

// ==============================================================================
// PART 8: Summary
// ==============================================================================

if not quiet then
    printfn "============================================================"
    printfn "  Summary"
    printfn "============================================================"
    printfn ""
    printfn "Water Molecule Quantum Simulation:"
    printfn "  - VQE ground state energy calculated"
    printfn "  - Active space concept demonstrated"
    printfn "  - Basis set effects explained"
    printfn "  - Potential energy curve computed"
    printfn "  - Drug discovery relevance discussed"
    printfn ""
    printfn "Key Concepts:"
    printfn "  - Active space reduces qubits (freeze core electrons)"
    printfn "  - Basis set determines accuracy vs cost tradeoff"
    printfn "  - VQE captures electron correlation beyond HF"
    printfn "  - Water exemplifies H-bonding and solvation"
    printfn ""
    printfn "Quantum Compliance:"
    printfn "  - All VQE calculations via IQuantumBackend"
    printfn "  - No classical-only energy returned as 'quantum'"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let allResults =
    [ vqeRow; dftRow ] |> List.choose id
    |> List.append <| scanResults

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path allResults
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header = [ "Section"; "BondLength_A"; "Energy_Hartree"; "Energy_eV"; "Energy_kcal_mol";
                   "Iterations"; "Converged"; "Time_s" ]
    let rows =
        allResults
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "CSV results written to %s" path
| None -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Customize this example with CLI flags:"
    printfn "  --bond-length 1.0             Set O-H bond length (Angstroms)"
    printfn "  --max-iterations 200          Increase VQE iterations"
    printfn "  --tolerance 1e-8              Tighter convergence"
    printfn "  --scan 0.7,0.8,0.96,1.0,1.5  Custom O-H scan distances"
    printfn "  --output results.json         Export results as JSON"
    printfn "  --csv results.csv             Export results as CSV"
    printfn "  --quiet                       Suppress informational output"
    printfn "  --help                        Show full usage information"

if not quiet then
    printfn ""
    printfn "Done!"
