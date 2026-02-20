// ==============================================================================
// Drug Fragment Ground State Energy Comparison
// ==============================================================================
// Compares ground state energies of small drug-like molecular fragments using
// VQE, ranking them by stability (energy per electron).
//
// The question: "Which pharmacological building block is most electronically
// stable, and how do fragment energies compare across functional groups?"
//
// Caffeine (C8H10N4O2, 102 electrons) is far too large for NISQ hardware.
// The Fragment Molecular Orbital (FMO) approach decomposes large drug molecules
// into small chemically meaningful pieces. This tool compares such fragments,
// letting you evaluate which functional groups contribute most to stability.
//
// Accepts multiple fragments (built-in presets or --input CSV), runs VQE on
// each, then outputs a ranked comparison table sorted by energy per electron.
//
// IMPORTANT LIMITATION:
// This example uses EMPIRICAL Hamiltonian coefficients (not molecular integrals).
// Calculated energies are ILLUSTRATIVE. For production use, molecular integral
// calculation (via PySCF, Psi4, or similar) would be needed.
//
// Usage:
//   dotnet fsi CaffeineEnergy.fsx
//   dotnet fsi CaffeineEnergy.fsx -- --help
//   dotnet fsi CaffeineEnergy.fsx -- --fragments formaldehyde,hcn
//   dotnet fsi CaffeineEnergy.fsx -- --input fragments.csv
//   dotnet fsi CaffeineEnergy.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Kitaura et al., "Fragment molecular orbital method" Chem. Phys. Lett. 313, 701 (1999)
//   [2] Cao et al., "Quantum Chemistry in the Age of Quantum Computing" Chem. Rev. (2019)
//   [3] Wikipedia: Fragment_molecular_orbital_method
//   [4] Goodman & Gilman's Pharmacological Basis of Therapeutics, 13th Ed.
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

Cli.exitIfHelp "CaffeineEnergy.fsx"
    "Compare drug fragment ground state energies via VQE (FMO building blocks)"
    [ { Cli.OptionSpec.Name = "input"; Description = "CSV file with custom fragment definitions"; Default = Some "built-in presets" }
      { Cli.OptionSpec.Name = "fragments"; Description = "Comma-separated preset names to run (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "50" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Energy convergence tolerance (Hartree)"; Default = Some "1e-4" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputFile = args |> Cli.tryGet "input"
let fragmentFilter = args |> Cli.getCommaSeparated "fragments"
let maxIterations = Cli.getIntOr "max-iterations" 50 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args

// ==============================================================================
// TYPES
// ==============================================================================

/// A drug fragment defined by its molecule, functional group class, and
/// pharmacological relevance.
type DrugFragment =
    { Name: string
      Molecule: Molecule
      FunctionalGroup: string
      PharmRole: string
      Description: string }

/// Result of computing one fragment's ground state energy via VQE.
type FragmentResult =
    { Fragment: DrugFragment
      Energy: float
      EnergyPerElectron: float
      Electrons: int
      ComputeTimeSeconds: float
      HasVqeFailure: bool }

// ==============================================================================
// PHYSICAL CONSTANTS
// ==============================================================================

let hartreeToKcalMol = 627.509
let hartreeToEv = 27.2114

// ==============================================================================
// BUILT-IN FRAGMENT PRESETS
// ==============================================================================
// Each fragment models a pharmacologically relevant functional group using
// NISQ-tractable molecules (<=5 atoms). These are the building blocks from
// which larger drug molecules (caffeine, aspirin, etc.) are assembled via FMO.

/// Formaldehyde (H2CO): simplest carbonyl, models amide/peptide bonds.
/// The C=O group is the most common pharmacophore in approved drugs.
/// In caffeine, the two C=O groups anchor H-bond interactions at
/// adenosine receptors.
let private formaldehydeFragment : DrugFragment =
    let mol : Molecule =
        { Name = "Formaldehyde"
          Atoms =
            [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
              { Element = "O"; Position = (1.21, 0.0, 0.0) }      // C=O bond ~1.21 A
              { Element = "H"; Position = (-0.54, 0.94, 0.0) }
              { Element = "H"; Position = (-0.54, -0.94, 0.0) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 2.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "Formaldehyde"
      Molecule = mol
      FunctionalGroup = "Carbonyl (C=O)"
      PharmRole = "H-bond acceptor — receptor selectivity"
      Description = "Simplest carbonyl; models amide bonds in caffeine xanthine core" }

/// Hydrogen cyanide (HCN): simplest nitrile, models C-N triple bonds.
/// Nitrile groups appear in kinase inhibitors (e.g., saxagliptin) and
/// model the sp-hybridised nitrogen found in purine ring systems.
let private hcnFragment : DrugFragment =
    let mol : Molecule =
        { Name = "HCN"
          Atoms =
            [ { Element = "H"; Position = (0.0, 0.0, 0.0) }
              { Element = "C"; Position = (1.07, 0.0, 0.0) }      // C-H bond ~1.07 A
              { Element = "N"; Position = (2.22, 0.0, 0.0) } ]    // C-N triple ~1.15 A
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 1; Atom2 = 2; BondOrder = 3.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "HCN"
      Molecule = mol
      FunctionalGroup = "Nitrile (C#N)"
      PharmRole = "Electrophilic warhead — covalent inhibitors"
      Description = "Nitrile model; sp-nitrogen in purine rings and covalent drug warheads" }

/// Water (H2O): models hydroxyl groups (Ser, Thr, Tyr sidechains).
/// Hydroxyl is the prototypical H-bond donor/acceptor in drug-receptor
/// interactions. Caffeine's metabolite paraxanthine gains an -OH via
/// CYP1A2 oxidation.
let private waterFragment : DrugFragment =
    let mol : Molecule =
        { Name = "Water"
          Atoms =
            [ { Element = "O"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (0.96, 0.0, 0.0) }      // O-H bond ~0.96 A
              { Element = "H"; Position = (-0.24, 0.93, 0.0) } ]  // H-O-H angle ~104.5
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "Water"
      Molecule = mol
      FunctionalGroup = "Hydroxyl (O-H)"
      PharmRole = "H-bond donor/acceptor — universal solvent shell"
      Description = "Hydroxyl model; Ser/Thr/Tyr sidechains and metabolic oxidation products" }

/// Hydrogen sulfide (H2S): models thiol groups (Cys sidechain).
/// Thiols form disulfide bonds stabilising antibody structure and are
/// targets for covalent drugs. The S-H bond is weaker and more polarisable
/// than O-H, making it a distinct quantum chemistry challenge.
let private h2sFragment : DrugFragment =
    let mol : Molecule =
        { Name = "H2S"
          Atoms =
            [ { Element = "S"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (1.34, 0.0, 0.0) }      // S-H bond ~1.34 A
              { Element = "H"; Position = (-0.37, 1.29, 0.0) } ]  // H-S-H angle ~92
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "H2S"
      Molecule = mol
      FunctionalGroup = "Thiol (S-H)"
      PharmRole = "Covalent target — disulfide bonds, cysteine reactivity"
      Description = "Thiol model; Cys sidechain and covalent drug targets" }

/// All built-in presets keyed by lowercase name.
let private builtinPresets : Map<string, DrugFragment> =
    [ formaldehydeFragment; hcnFragment; waterFragment; h2sFragment ]
    |> List.map (fun f -> f.Name.ToLowerInvariant(), f)
    |> Map.ofList

let private presetNames =
    builtinPresets |> Map.toList |> List.map fst |> String.concat ", "

// ==============================================================================
// CSV INPUT PARSING
// ==============================================================================

/// Parse atom list from compact string format:
///   "C:0,0,0|O:0,0,1.21|H:0.94,0,-0.54"
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

/// Load fragments from a CSV file.
/// Expected columns: name, functional_group, pharm_role, description, atoms
/// OR: name, preset (to reference a built-in preset by name)
let private loadFragmentsFromCsv (path: string) : DrugFragment list =
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
            | Some frag -> Some { frag with Name = name }
            | None ->
                if not quiet then
                    eprintfn "  Warning: unknown preset '%s' (available: %s)" presetKey presetNames
                None
        | None ->
            match get "atoms" with
            | Some atomStr ->
                let funcGroup = get "functional_group" |> Option.defaultValue "Unknown"
                let role = get "pharm_role" |> Option.defaultValue ""
                let desc = get "description" |> Option.defaultValue ""
                let mol = moleculeFromAtomString name atomStr
                Some
                    { Name = name
                      Molecule = mol
                      FunctionalGroup = funcGroup
                      PharmRole = role
                      Description = desc }
            | None ->
                if not quiet then
                    eprintfn "  Warning: row '%s' missing required 'atoms' column" name
                None)

// ==============================================================================
// FRAGMENT SELECTION
// ==============================================================================

let fragments : DrugFragment list =
    let allFragments =
        match inputFile with
        | Some path ->
            let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
            if not quiet then
                printfn "Loading fragments from: %s" resolved
            loadFragmentsFromCsv resolved
        | None ->
            builtinPresets |> Map.toList |> List.map snd

    match fragmentFilter with
    | [] -> allFragments
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        allFragments
        |> List.filter (fun f ->
            let key = f.Name.ToLowerInvariant()
            filterSet |> Set.exists (fun fil -> key.Contains fil))

if List.isEmpty fragments then
    eprintfn "Error: No fragments selected. Available presets: %s" presetNames
    exit 1

// ==============================================================================
// QUANTUM BACKEND (Rule 1: all VQE via IQuantumBackend)
// ==============================================================================

let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Drug Fragment Ground State Energy Comparison"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:      %s" backend.Name
    printfn "  Fragments:    %d" fragments.Length
    printfn "  VQE iters:    %d (tol: %g Ha)" maxIterations tolerance
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

/// Compute the ground state energy profile for one fragment.
let private computeFragment
    (backend: IQuantumBackend)
    (maxIter: int)
    (tol: float)
    (idx: int)
    (total: int)
    (frag: DrugFragment)
    : FragmentResult =
    let electrons = Molecule.countElectrons frag.Molecule

    if not quiet then
        printfn "  [%d/%d] %s (%s)" (idx + 1) total frag.Name frag.FunctionalGroup
        printfn "         %s" frag.Description
        printfn "         Atoms: %d  |  Electrons: %d" frag.Molecule.Atoms.Length electrons

    let mutable anyFailure = false

    let (res, elapsed) = computeEnergy backend maxIter tol frag.Molecule

    let energy =
        match res with
        | Ok e ->
            if not quiet then
                printfn "         E = %.6f Ha  (%.1fs)" e elapsed
            e
        | Error _ ->
            anyFailure <- true
            if not quiet then
                printfn "         E = FAILED  (%.1fs)" elapsed
            0.0

    let energyPerElectron =
        if anyFailure || electrons = 0 then 0.0
        else energy / float electrons

    if not quiet then
        if not anyFailure then
            printfn "         E/electron = %.6f Ha" energyPerElectron
        printfn ""

    { Fragment = frag
      Energy = energy
      EnergyPerElectron = energyPerElectron
      Electrons = electrons
      ComputeTimeSeconds = elapsed
      HasVqeFailure = anyFailure }

// --- Run all fragments ---

if not quiet then
    printfn "Computing fragment ground state energies..."
    printfn ""

let results =
    fragments
    |> List.mapi (fun i frag -> computeFragment backend maxIterations tolerance i fragments.Length frag)

// Sort: most negative energy per electron first (most stable per electron).
// Failed fragments sink to bottom.
let ranked =
    results
    |> List.sortBy (fun r ->
        if r.HasVqeFailure then (2, infinity)
        elif r.EnergyPerElectron >= 0.0 then (1, r.EnergyPerElectron)
        else (0, r.EnergyPerElectron))

// ==============================================================================
// RANKED COMPARISON TABLE
// ==============================================================================

let printTable () =
    printfn "=================================================================="
    printfn "  Ranked Drug Fragment Energies (by energy per electron)"
    printfn "=================================================================="
    printfn ""
    printfn "  %-4s  %-16s  %-16s  %4s  %12s  %14s  %10s"
        "#" "Fragment" "Group" "e-" "E (Ha)" "E/e- (Ha)" "Time (s)"
    printfn "  %s" (String('=', 95))

    ranked
    |> List.iteri (fun i r ->
        if r.HasVqeFailure then
            printfn "  %-4d  %-16s  %-16s  %4d  %12s  %14s  %10.1f"
                (i + 1)
                r.Fragment.Name
                r.Fragment.FunctionalGroup
                r.Electrons
                "FAILED"
                "FAILED"
                r.ComputeTimeSeconds
        else
            printfn "  %-4d  %-16s  %-16s  %4d  %12.6f  %14.6f  %10.1f"
                (i + 1)
                r.Fragment.Name
                r.Fragment.FunctionalGroup
                r.Electrons
                r.Energy
                r.EnergyPerElectron
                r.ComputeTimeSeconds)

    printfn ""

    // Pharmacological roles
    printfn "  %-4s  %-16s  %s"
        "#" "Fragment" "Pharmacological Role"
    printfn "  %s" (String('-', 75))

    ranked
    |> List.iteri (fun i r ->
        printfn "  %-4d  %-16s  %s"
            (i + 1) r.Fragment.Name r.Fragment.PharmRole)

    printfn ""

// Always print the ranked comparison table — that's the primary output of this tool,
// even in --quiet mode (which only suppresses per-fragment progress output).
printTable ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    let successful = ranked |> List.filter (fun r -> not r.HasVqeFailure)
    match successful with
    | best :: _ ->
        let totalTime = results |> List.sumBy (fun r -> r.ComputeTimeSeconds)
        printfn "  Most stable (per e-):  %s (%s, E/e- = %.6f Ha)"
            best.Fragment.Name best.Fragment.FunctionalGroup best.EnergyPerElectron
        printfn "  Total time:            %.1f seconds" totalTime
        printfn "  Quantum:               all VQE via IQuantumBackend [Rule 1 compliant]"
        printfn ""
    | [] ->
        printfn "  All fragments failed VQE computation."
        printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    ranked
    |> List.mapi (fun i r ->
        [ "rank", string (i + 1)
          "fragment", r.Fragment.Name
          "functional_group", r.Fragment.FunctionalGroup
          "pharm_role", r.Fragment.PharmRole
          "description", r.Fragment.Description
          "atoms", string r.Fragment.Molecule.Atoms.Length
          "electrons", string r.Electrons
          "energy_hartree", (if r.HasVqeFailure then "FAILED" else sprintf "%.6f" r.Energy)
          "energy_per_electron_ha", (if r.HasVqeFailure then "FAILED" else sprintf "%.6f" r.EnergyPerElectron)
          "energy_kcal_mol", (if r.HasVqeFailure then "FAILED" else sprintf "%.2f" (r.Energy * hartreeToKcalMol))
          "energy_ev", (if r.HasVqeFailure then "FAILED" else sprintf "%.4f" (r.Energy * hartreeToEv))
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
        [ "rank"; "fragment"; "functional_group"; "pharm_role"; "description"
          "atoms"; "electrons"; "energy_hartree"; "energy_per_electron_ha"
          "energy_kcal_mol"; "energy_ev"; "compute_time_s"; "has_vqe_failure" ]
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
    printfn "     --fragments formaldehyde,hcn              Run specific fragments"
    printfn "     --input fragments.csv                     Load custom fragments from CSV"
    printfn "     --csv results.csv                         Export ranked table as CSV"
    printfn ""
