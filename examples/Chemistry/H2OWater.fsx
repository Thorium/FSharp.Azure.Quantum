// ==============================================================================
// Bond Dissociation Energy Comparison via VQE
// ==============================================================================
// Compares bond dissociation energies (BDE) of small molecules using VQE,
// computing each molecule at equilibrium and stretched geometries.
//
// BDE = E(stretched) - E(equilibrium) approximates the energy required to
// break a bond. Comparing BDEs across molecules reveals relative bond
// strengths — essential for predicting reaction selectivity, metabolic
// stability, and solvation/H-bond energetics.
//
// Water (H2O, 10 electrons) is central to drug binding: solvation penalties,
// bridging waters, and proton transfer all depend on accurate H-bond energies.
// This tool compares water's O-H bond with other biologically relevant bonds.
//
// IMPORTANT LIMITATION:
// This example uses EMPIRICAL Hamiltonian coefficients (not molecular integrals).
// Calculated energies are ILLUSTRATIVE. For production use, molecular integral
// calculation (via PySCF, Psi4, or similar) would be needed.
//
// Usage:
//   dotnet fsi H2OWater.fsx
//   dotnet fsi H2OWater.fsx -- --help
//   dotnet fsi H2OWater.fsx -- --systems h2o,hf
//   dotnet fsi H2OWater.fsx -- --input molecules.csv
//   dotnet fsi H2OWater.fsx -- --stretch-factor 2.0
//   dotnet fsi H2OWater.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Peruzzo et al. "A variational eigenvalue solver" Nat. Commun. 5, 4213 (2014)
//   [2] Cao et al. "Quantum Chemistry in the Age of Quantum Computing" Chem. Rev. (2019)
//   [3] Wikipedia: Bond-dissociation_energy
//   [4] CRC Handbook of Chemistry and Physics (experimental BDE reference values)
// ==============================================================================

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
// CLI
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "H2OWater.fsx"
    "Compare bond dissociation energies of small molecules via VQE"
    [ { Cli.OptionSpec.Name = "input"; Description = "CSV file with custom molecule definitions"; Default = Some "built-in presets" }
      { Cli.OptionSpec.Name = "systems"; Description = "Comma-separated preset names to run (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "50" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Energy convergence tolerance (Hartree)"; Default = Some "1e-4" }
      { Cli.OptionSpec.Name = "stretch-factor"; Description = "Stretch factor for dissociation geometry (x equilibrium)"; Default = Some "2.0" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputFile = args |> Cli.tryGet "input"
let systemFilter = args |> Cli.getCommaSeparated "systems"
let maxIterations = Cli.getIntOr "max-iterations" 50 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args
let stretchFactor = Cli.getFloatOr "stretch-factor" 2.0 args

// ==============================================================================
// TYPES
// ==============================================================================

/// A diatomic or triatomic molecule with a primary bond to stretch.
/// Equilibrium geometry has atoms at their standard positions; the stretched
/// geometry scales the primary bond by stretchFactor.
type BondSystem =
    { Name: string
      EquilibriumMolecule: Molecule
      /// Build a version of this molecule with the primary bond stretched.
      MakeStretched: float -> Molecule
      BondType: string
      BondLengthAngstrom: float
      BiologicalRole: string
      Description: string }

/// Result of computing one molecule's BDE via VQE.
type BdeResult =
    { System: BondSystem
      EquilibriumEnergy: float
      StretchedEnergy: float
      BdeHartree: float
      BdeKcalMol: float
      BdeEv: float
      StretchFactor: float
      Electrons: int
      ComputeTimeSeconds: float
      HasVqeFailure: bool }

// ==============================================================================
// PHYSICAL CONSTANTS
// ==============================================================================

let hartreeToKcalMol = 627.509
let hartreeToEv = 27.2114

// ==============================================================================
// BUILT-IN MOLECULE PRESETS
// ==============================================================================
// Each models a bond relevant to aqueous/biological chemistry.
// All <=3 atoms for fast VQE (2 calls per system, ~5-15s each).

/// H2O: the O-H bond. Central to solvation, H-bonding, and proton transfer.
/// Experimental BDE(O-H) = 119 kcal/mol.
let private h2oSystem : BondSystem =
    let eq : Molecule =
        { Name = "H2O (equilibrium)"
          Atoms =
            [ { Element = "O"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (0.96, 0.0, 0.0) }       // O-H bond ~0.96 A
              { Element = "H"; Position = (-0.24, 0.93, 0.0) } ]   // H-O-H angle ~104.5
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let makeStretched (factor: float) : Molecule =
        { eq with
            Name = sprintf "H2O (O-H x%.1f)" factor
            Atoms =
              [ { Element = "O"; Position = (0.0, 0.0, 0.0) }
                { Element = "H"; Position = (0.96 * factor, 0.0, 0.0) }
                { Element = "H"; Position = (-0.24, 0.93, 0.0) } ] }

    { Name = "H2O"
      EquilibriumMolecule = eq
      MakeStretched = makeStretched
      BondType = "O-H"
      BondLengthAngstrom = 0.96
      BiologicalRole = "Solvation shell, H-bond donor/acceptor"
      Description = "Water O-H bond — the universal biological solvent" }

/// HF: the H-F bond. Models strong H-bond acceptors and halogen
/// interactions in drug molecules. Experimental BDE(H-F) = 136 kcal/mol.
let private hfSystem : BondSystem =
    let eq : Molecule =
        { Name = "HF (equilibrium)"
          Atoms =
            [ { Element = "H"; Position = (0.0, 0.0, 0.0) }
              { Element = "F"; Position = (0.92, 0.0, 0.0) } ]     // H-F bond ~0.92 A
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let makeStretched (factor: float) : Molecule =
        { eq with
            Name = sprintf "HF (H-F x%.1f)" factor
            Atoms =
              [ { Element = "H"; Position = (0.0, 0.0, 0.0) }
                { Element = "F"; Position = (0.92 * factor, 0.0, 0.0) } ] }

    { Name = "HF"
      EquilibriumMolecule = eq
      MakeStretched = makeStretched
      BondType = "H-F"
      BondLengthAngstrom = 0.92
      BiologicalRole = "Strong H-bond model, halogen drug interactions"
      Description = "Hydrogen fluoride H-F bond — strongest single bond to H" }

/// LiH: the Li-H bond. Models weak ionic/polar bonds.
/// Experimental BDE(Li-H) = 57 kcal/mol.
let private lihSystem : BondSystem =
    let eq : Molecule =
        { Name = "LiH (equilibrium)"
          Atoms =
            [ { Element = "Li"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (1.60, 0.0, 0.0) } ]     // Li-H bond ~1.60 A
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let makeStretched (factor: float) : Molecule =
        { eq with
            Name = sprintf "LiH (Li-H x%.1f)" factor
            Atoms =
              [ { Element = "Li"; Position = (0.0, 0.0, 0.0) }
                { Element = "H"; Position = (1.60 * factor, 0.0, 0.0) } ] }

    { Name = "LiH"
      EquilibriumMolecule = eq
      MakeStretched = makeStretched
      BondType = "Li-H"
      BondLengthAngstrom = 1.60
      BiologicalRole = "Weak polar bond model, metal-ligand interactions"
      Description = "Lithium hydride Li-H bond — weak ionic/polar bond model" }

/// H2: the H-H bond. Simplest covalent bond, reference for all BDE work.
/// Experimental BDE(H-H) = 104 kcal/mol.
let private h2System : BondSystem =
    let eq : Molecule =
        { Name = "H2 (equilibrium)"
          Atoms =
            [ { Element = "H"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (0.74, 0.0, 0.0) } ]     // H-H bond ~0.74 A
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let makeStretched (factor: float) : Molecule =
        { eq with
            Name = sprintf "H2 (H-H x%.1f)" factor
            Atoms =
              [ { Element = "H"; Position = (0.0, 0.0, 0.0) }
                { Element = "H"; Position = (0.74 * factor, 0.0, 0.0) } ] }

    { Name = "H2"
      EquilibriumMolecule = eq
      MakeStretched = makeStretched
      BondType = "H-H"
      BondLengthAngstrom = 0.74
      BiologicalRole = "Reference covalent bond, hydrogenase substrates"
      Description = "Molecular hydrogen H-H bond — simplest covalent bond" }

/// All built-in presets keyed by lowercase name.
let private builtinPresets : Map<string, BondSystem> =
    [ h2oSystem; hfSystem; lihSystem; h2System ]
    |> List.map (fun s -> s.Name.ToLowerInvariant(), s)
    |> Map.ofList

let private presetNames =
    builtinPresets |> Map.toList |> List.map fst |> String.concat ", "

// ==============================================================================
// CSV INPUT PARSING
// ==============================================================================

/// Parse atom list from compact string format:
///   "O:0,0,0|H:0.96,0,0|H:-0.24,0.93,0"
let private parseAtoms (s: string) : Atom list =
    s.Split('|')
    |> Array.choose (fun entry ->
        let parts = entry.Trim().Split(':')
        if parts.Length = 2 then
            let coords = parts.[1].Split(',')
            if coords.Length = 3 then
                match Double.TryParse coords.[0], Double.TryParse coords.[1], Double.TryParse coords.[2] with
                | (true, x), (true, y), (true, z) ->
                    Some { Element = parts.[0].Trim(); Position = (x, y, z) }
                | _ -> None
            else None
        else None)
    |> Array.toList

/// Infer single bonds between adjacent atom pairs (simple fallback).
let private inferBonds (atoms: Atom list) : Bond list =
    [ for i in 0 .. atoms.Length - 2 do
        { Atom1 = i; Atom2 = i + 1; BondOrder = 1.0 } ]

/// Build a Molecule from an atom string, inferring bonds.
let private moleculeFromAtomString (name: string) (atomStr: string) : Molecule =
    let atoms = parseAtoms atomStr
    { Name = name
      Atoms = atoms
      Bonds = inferBonds atoms
      Charge = 0
      Multiplicity = 1 }

/// Load bond systems from a CSV file.
/// Expected columns: name, bond_type, bond_length, biological_role, description, atoms
/// OR: name, preset (to reference a built-in preset by name)
///
/// For CSV-loaded molecules, the stretched geometry uniformly scales atom 1's
/// position by the stretch factor (simple diatomic-like stretching).
let private loadSystemsFromCsv (path: string) : BondSystem list =
    let rows, errors = Data.readCsvWithHeaderWithErrors path
    if not (List.isEmpty errors) && not quiet then
        for err in errors do
            eprintfn "  Warning (CSV): %s" err

    rows
    |> List.choose (fun row ->
        let get key = row.Values |> Map.tryFind key
        let name = get "name" |> Option.defaultValue "Unknown"
        match get "preset" with
        | Some presetKey ->
            let key = presetKey.Trim().ToLowerInvariant()
            match builtinPresets |> Map.tryFind key with
            | Some sys -> Some { sys with Name = name }
            | None ->
                if not quiet then
                    eprintfn "  Warning: unknown preset '%s' (available: %s)" presetKey presetNames
                None
        | None ->
            match get "atoms" with
            | Some atomStr ->
                let bondType = get "bond_type" |> Option.defaultValue "Unknown"
                let bondLen = get "bond_length" |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None) |> Option.defaultValue 1.0
                let bio = get "biological_role" |> Option.defaultValue ""
                let desc = get "description" |> Option.defaultValue ""
                let eqMol = moleculeFromAtomString (name + " (equilibrium)") atomStr
                // Simple stretch: scale all non-first atom positions
                let makeStretched (factor: float) : Molecule =
                    let stretchedAtoms =
                        eqMol.Atoms
                        |> List.mapi (fun i atom ->
                            if i = 0 then atom
                            else
                                let (x, y, z) = atom.Position
                                { atom with Position = (x * factor, y * factor, z * factor) })
                    { eqMol with
                        Name = sprintf "%s (x%.1f)" name factor
                        Atoms = stretchedAtoms }
                Some
                    { Name = name
                      EquilibriumMolecule = eqMol
                      MakeStretched = makeStretched
                      BondType = bondType
                      BondLengthAngstrom = bondLen
                      BiologicalRole = bio
                      Description = desc }
            | None ->
                if not quiet then
                    eprintfn "  Warning: row '%s' missing required 'atoms' column" name
                None)

// ==============================================================================
// SYSTEM SELECTION
// ==============================================================================

let systems : BondSystem list =
    let allSystems =
        match inputFile with
        | Some path ->
            let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
            if not quiet then
                printfn "Loading molecules from: %s" resolved
            loadSystemsFromCsv resolved
        | None ->
            builtinPresets |> Map.toList |> List.map snd

    match systemFilter with
    | [] -> allSystems
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        allSystems
        |> List.filter (fun s ->
            let key = s.Name.ToLowerInvariant()
            filterSet |> Set.exists (fun f -> key.Contains f))

if List.isEmpty systems then
    eprintfn "Error: No systems selected. Available presets: %s" presetNames
    exit 1

// ==============================================================================
// QUANTUM BACKEND (Rule 1: all VQE via IQuantumBackend)
// ==============================================================================

let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Bond Dissociation Energy Comparison"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:      %s" backend.Name
    printfn "  Systems:      %d" systems.Length
    printfn "  VQE iters:    %d (tol: %g Ha)" maxIterations tolerance
    printfn "  Stretch:      x%.1f equilibrium bond length" stretchFactor
    printfn ""

// ==============================================================================
// VQE COMPUTATION
// ==============================================================================

/// VQE solver configuration.
let private solverConfig (backend: IQuantumBackend) (maxIter: int) (tol: float) : SolverConfig =
    { Method = GroundStateMethod.VQE
      Backend = Some backend
      MaxIterations = maxIter
      Tolerance = tol
      InitialParameters = None
      ProgressReporter = None
      ErrorMitigation = None
      IntegralProvider = None }

/// Calculate ground state energy for a molecule using VQE via IQuantumBackend.
/// Returns (Ok energy | Error message, elapsed seconds).
let private computeEnergy
    (backend: IQuantumBackend)
    (maxIter: int)
    (tol: float)
    (molecule: Molecule)
    : Result<float, string> * float =
    let startTime = DateTime.Now
    let config = solverConfig backend maxIter tol
    let result = GroundStateEnergy.estimateEnergy molecule config |> Async.RunSynchronously
    let elapsed = (DateTime.Now - startTime).TotalSeconds

    match result with
    | Ok vqeResult -> (Ok vqeResult.Energy, elapsed)
    | Error err ->
        if not quiet then
            eprintfn "  Warning: VQE failed for %s: %s" molecule.Name err.Message
        (Error (sprintf "VQE failed for %s: %s" molecule.Name err.Message), elapsed)

/// Compute BDE for one bond system: E(stretched) - E(equilibrium).
let private computeSystem
    (backend: IQuantumBackend)
    (maxIter: int)
    (tol: float)
    (stretch: float)
    (idx: int)
    (total: int)
    (sys: BondSystem)
    : BdeResult =
    let electrons = Molecule.countElectrons sys.EquilibriumMolecule

    if not quiet then
        printfn "  [%d/%d] %s (%s, %.2f A)" (idx + 1) total sys.Name sys.BondType sys.BondLengthAngstrom
        printfn "         %s" sys.Description
        printfn "         Atoms: %d  |  Electrons: %d" sys.EquilibriumMolecule.Atoms.Length electrons

    let startTime = DateTime.Now
    let mutable anyFailure = false

    /// Unwrap a VQE result, logging failures and tracking error state.
    let unwrapEnergy (label: string) (name: string) (res: Result<float, string>, elapsed: float) : float * float =
        match res with
        | Ok e ->
            if not quiet then
                printfn "         %-12s %-22s  E = %10.6f Ha  (%.1fs)" label name e elapsed
            (e, elapsed)
        | Error _ ->
            anyFailure <- true
            if not quiet then
                printfn "         %-12s %-22s  E = FAILED         (%.1fs)" label name elapsed
            (0.0, elapsed)

    let (eqE, _) = unwrapEnergy "equilibrium" sys.EquilibriumMolecule.Name (computeEnergy backend maxIter tol sys.EquilibriumMolecule)

    let stretchedMol = sys.MakeStretched stretch
    let (strE, _) = unwrapEnergy "stretched" stretchedMol.Name (computeEnergy backend maxIter tol stretchedMol)

    let totalTime = (DateTime.Now - startTime).TotalSeconds

    // BDE = E(stretched) - E(equilibrium)
    let bdeHartree = strE - eqE
    let bdeKcal = bdeHartree * hartreeToKcalMol
    let bdeEv = bdeHartree * hartreeToEv

    if not quiet then
        if anyFailure then
            printfn "         => INCOMPLETE (VQE failure — energies are unreliable)"
        else
            printfn "         => BDE = %.4f Ha = %.2f kcal/mol" bdeHartree bdeKcal
        printfn ""

    { System = sys
      EquilibriumEnergy = eqE
      StretchedEnergy = strE
      BdeHartree = bdeHartree
      BdeKcalMol = bdeKcal
      BdeEv = bdeEv
      StretchFactor = stretch
      Electrons = electrons
      ComputeTimeSeconds = totalTime
      HasVqeFailure = anyFailure }

// --- Run all systems ---

if not quiet then
    printfn "Computing bond dissociation energies..."
    printfn ""

let results =
    systems
    |> List.mapi (fun i sys -> computeSystem backend maxIterations tolerance stretchFactor i systems.Length sys)

// Sort by BDE descending (strongest bond first).
// Failed systems sink to bottom.
let ranked =
    results
    |> List.sortBy (fun r ->
        if r.HasVqeFailure then (2, infinity)
        else (0, -r.BdeKcalMol))

// ==============================================================================
// RANKED COMPARISON TABLE
// ==============================================================================

let printTable () =
    printfn "=================================================================="
    printfn "  Ranked Bond Dissociation Energies (strongest first)"
    printfn "=================================================================="
    printfn ""
    printfn "  %-4s  %-8s  %-8s  %8s  %14s  %10s  %10s"
        "#" "System" "Bond" "R (A)" "BDE (kcal/mol)" "BDE (eV)" "Time (s)"
    printfn "  %s" (String('=', 80))

    ranked
    |> List.iteri (fun i r ->
        if r.HasVqeFailure then
            printfn "  %-4d  %-8s  %-8s  %8.2f  %14s  %10s  %10.1f"
                (i + 1) r.System.Name r.System.BondType r.System.BondLengthAngstrom
                "FAILED" "FAILED" r.ComputeTimeSeconds
        else
            printfn "  %-4d  %-8s  %-8s  %8.2f  %14.2f  %10.4f  %10.1f"
                (i + 1) r.System.Name r.System.BondType r.System.BondLengthAngstrom
                r.BdeKcalMol r.BdeEv r.ComputeTimeSeconds)

    printfn ""

    // Biological roles
    printfn "  %-4s  %-8s  %4s  %s"
        "#" "System" "e-" "Biological Role"
    printfn "  %s" (String('-', 65))

    ranked
    |> List.iteri (fun i r ->
        printfn "  %-4d  %-8s  %4d  %s"
            (i + 1) r.System.Name r.Electrons r.System.BiologicalRole)

    printfn ""

// Always print the ranked comparison table — that's the primary output of this tool,
// even in --quiet mode (which only suppresses per-system progress output).
printTable ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    let successful = ranked |> List.filter (fun r -> not r.HasVqeFailure)
    match successful with
    | best :: _ ->
        let totalTime = results |> List.sumBy (fun r -> r.ComputeTimeSeconds)
        printfn "  Strongest bond:  %s (%s, BDE = %.2f kcal/mol)"
            best.System.Name best.System.BondType best.BdeKcalMol
        printfn "  Total time:      %.1f seconds" totalTime
        printfn "  Stretch factor:  x%.1f equilibrium bond length" stretchFactor
        printfn "  Quantum:         all VQE via IQuantumBackend [Rule 1 compliant]"
        printfn ""
    | [] ->
        printfn "  All systems failed VQE computation."
        printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    ranked
    |> List.mapi (fun i r ->
        [ "rank", string (i + 1)
          "system", r.System.Name
          "bond_type", r.System.BondType
          "bond_length_angstrom", sprintf "%.2f" r.System.BondLengthAngstrom
          "biological_role", r.System.BiologicalRole
          "description", r.System.Description
          "atoms", string r.System.EquilibriumMolecule.Atoms.Length
          "electrons", string r.Electrons
          "eq_energy_ha", (if r.HasVqeFailure then "FAILED" else sprintf "%.6f" r.EquilibriumEnergy)
          "stretched_energy_ha", (if r.HasVqeFailure then "FAILED" else sprintf "%.6f" r.StretchedEnergy)
          "bde_hartree", (if r.HasVqeFailure then "FAILED" else sprintf "%.6f" r.BdeHartree)
          "bde_kcal_mol", (if r.HasVqeFailure then "FAILED" else sprintf "%.2f" r.BdeKcalMol)
          "bde_ev", (if r.HasVqeFailure then "FAILED" else sprintf "%.4f" r.BdeEv)
          "stretch_factor", sprintf "%.1f" r.StretchFactor
          "compute_time_s", sprintf "%.1f" r.ComputeTimeSeconds
          "has_vqe_failure", string r.HasVqeFailure ]
        |> Map.ofList)

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path resultMaps
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header =
        [ "rank"; "system"; "bond_type"; "bond_length_angstrom"; "biological_role"
          "description"; "atoms"; "electrons"; "eq_energy_ha"; "stretched_energy_ha"
          "bde_hartree"; "bde_kcal_mol"; "bde_ev"; "stretch_factor"
          "compute_time_s"; "has_vqe_failure" ]
    let rows =
        resultMaps
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Run with --help to see all options."
    printfn "     --systems h2o,hf                 Run specific molecules"
    printfn "     --input molecules.csv            Load custom molecules from CSV"
    printfn "     --stretch-factor 3.0             Stretch further (stronger dissociation)"
    printfn "     --csv results.csv                Export ranked table as CSV"
    printfn ""
