// ==============================================================================
// Protein-Ligand Binding Affinity Comparison
// ==============================================================================
// Compares multiple drug-protein binding systems by computing VQE interaction
// energies via a fragment molecular orbital (FMO) approach.
//
// Accepts multiple binding systems (built-in presets or --input CSV), runs VQE
// on each system's ligand, protein fragment, and complex, then outputs a ranked
// comparison table showing which drug candidate binds most strongly.
//
// Background:
// Binding affinity (dE = E_complex - E_protein - E_ligand) is the fundamental
// measure of drug-target interaction strength. Classical force fields approximate
// electrostatics + van der Waals but miss electron correlation, charge transfer,
// and partial covalent character — exactly the effects VQE captures.
//
// IMPORTANT LIMITATION:
// This example uses EMPIRICAL Hamiltonian coefficients (not molecular integrals).
// Calculated energies are ILLUSTRATIVE. For production use, molecular integral
// calculation (via PySCF, Psi4, or similar) would be needed.
//
// Usage:
//   dotnet fsi BindingAffinity.fsx
//   dotnet fsi BindingAffinity.fsx -- --help
//   dotnet fsi BindingAffinity.fsx -- --systems aspirin,thiol
//   dotnet fsi BindingAffinity.fsx -- --input systems.csv
//   dotnet fsi BindingAffinity.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Shirts & Mobley, "Free Energy Calculations" Methods Mol. Biol. (2017)
//   [2] Cao et al., "Quantum Chemistry in the Age of Quantum Computing" Chem. Rev. (2019)
//   [3] Wikipedia: Binding_affinity
//   [4] Harper's Illustrated Biochemistry, 28th Ed., Ch. 7-8
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

Cli.exitIfHelp "BindingAffinity.fsx"
    "Compare protein-ligand binding affinities by VQE fragment molecular orbital approach"
    [ { Cli.OptionSpec.Name = "input"; Description = "CSV file with custom binding systems"; Default = Some "built-in presets" }
      { Cli.OptionSpec.Name = "systems"; Description = "Comma-separated preset names to run (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "50" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Energy convergence tolerance (Hartree)"; Default = Some "1e-4" }
      { Cli.OptionSpec.Name = "temperature"; Description = "Temperature for Kd estimation (Kelvin)"; Default = Some "300" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputFile = args |> Cli.tryGet "input"
let systemFilter = args |> Cli.getCommaSeparated "systems"
let maxIterations = Cli.getIntOr "max-iterations" 50 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args
let temperature = Cli.getFloatOr "temperature" 300.0 args

// ==============================================================================
// TYPES
// ==============================================================================

/// A protein-ligand binding system defined by three molecular species:
/// ligand (drug fragment), protein (binding site fragment), and their complex.
type BindingSystem =
    { Name: string
      Ligand: Molecule
      ProteinFragment: Molecule
      InteractionType: string
      Description: string }

/// Result of computing one binding system's energy profile via VQE.
type BindingResult =
    { System: BindingSystem
      LigandEnergy: float
      ProteinEnergy: float
      ComplexEnergy: float
      BindingEnergyHartree: float
      BindingEnergyKcal: float
      BindingEnergyKJ: float
      EstimatedKd: float
      KdStr: string
      Interpretation: string
      ComputeTimeSeconds: float
      HasVqeFailure: bool }

// ==============================================================================
// PHYSICAL CONSTANTS
// ==============================================================================

let hartreeToKcalMol = 627.509
let hartreeToKJMol = 2625.5
let gasR_kcal = 1.987e-3    // Gas constant in kcal/(mol*K)

// ==============================================================================
// BUILT-IN BINDING SYSTEM PRESETS
// ==============================================================================
// Each system models a different non-covalent interaction type using
// NISQ-tractable model fragments (<=3 atoms per fragment, <=5 atom complex).
// Complexes >5 atoms cause VQE timeouts on LocalBackend.

/// HF dimer: classic hydrogen bond benchmark.
/// F-H...F-H is the simplest, strongest neutral H-bond.
/// Literature: ~4.6 kcal/mol binding energy (CCSD(T)/CBS).
let private hfDimerSystem : BindingSystem =
    let ligand : Molecule =
        { Name = "HF (donor)"
          Atoms =
            [ { Element = "H"; Position = (0.0, 0.0, 0.0) }
              { Element = "F"; Position = (0.92, 0.0, 0.0) } ]
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let protein : Molecule =
        { Name = "HF (acceptor)"
          Atoms =
            [ { Element = "F"; Position = (2.72, 0.0, 0.0) }     // F...H distance ~1.8 A
              { Element = "H"; Position = (3.64, 0.0, 0.0) } ]
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "HF-Dimer"
      Ligand = ligand
      ProteinFragment = protein
      InteractionType = "F-H...F"
      Description = "Hydrogen fluoride dimer (classic H-bond benchmark)" }

/// Water...HF: water as H-bond donor to fluoride.
/// Models OH...F interaction found in fluorinated drug binding.
let private waterHfSystem : BindingSystem =
    let ligand : Molecule =
        { Name = "Water (donor)"
          Atoms =
            [ { Element = "O"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (0.96, 0.0, 0.0) }       // donor H
              { Element = "H"; Position = (-0.24, 0.93, 0.0) } ]   // spectator H
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let protein : Molecule =
        { Name = "HF (acceptor)"
          Atoms =
            [ { Element = "F"; Position = (2.76, 0.0, 0.0) }     // O-H...F distance ~1.8 A
              { Element = "H"; Position = (3.68, 0.0, 0.0) } ]
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "Water-HF"
      Ligand = ligand
      ProteinFragment = protein
      InteractionType = "O-H...F"
      Description = "Water...HF H-bond (fluorinated drug binding model)" }

/// H2S...HF: sulfur as H-bond acceptor.
/// Models cysteine thiol interactions — weaker than O-H donor.
let private h2sHfSystem : BindingSystem =
    let ligand : Molecule =
        { Name = "HF (donor)"
          Atoms =
            [ { Element = "H"; Position = (0.0, 0.0, 0.0) }
              { Element = "F"; Position = (0.92, 0.0, 0.0) } ]
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let protein : Molecule =
        { Name = "H2S (acceptor)"
          Atoms =
            [ { Element = "S"; Position = (2.80, 0.0, 0.0) }     // F-H...S distance ~1.88 A
              { Element = "H"; Position = (3.60, 0.75, 0.0) }
              { Element = "H"; Position = (3.60, -0.75, 0.0) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "HF-H2S"
      Ligand = ligand
      ProteinFragment = protein
      InteractionType = "F-H...S"
      Description = "HF...H2S H-bond (cysteine thiol interaction model)" }

/// HCl...HF: chlorine as H-bond acceptor.
/// Models halogen interactions in drug-receptor binding.
let private hclHfSystem : BindingSystem =
    let ligand : Molecule =
        { Name = "HF (donor)"
          Atoms =
            [ { Element = "H"; Position = (0.0, 0.0, 0.0) }
              { Element = "F"; Position = (0.92, 0.0, 0.0) } ]
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let protein : Molecule =
        { Name = "HCl (acceptor)"
          Atoms =
            [ { Element = "Cl"; Position = (2.90, 0.0, 0.0) }    // F-H...Cl distance ~1.98 A
              { Element = "H"; Position = (4.18, 0.0, 0.0) } ]
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "HF-HCl"
      Ligand = ligand
      ProteinFragment = protein
      InteractionType = "F-H...Cl"
      Description = "HF...HCl halogen H-bond (halogenated drug model)" }

/// All built-in presets keyed by lowercase name.
let private builtinPresets : Map<string, BindingSystem> =
    [ hfDimerSystem; waterHfSystem; h2sHfSystem; hclHfSystem ]
    |> List.map (fun s -> s.Name.ToLowerInvariant().Replace(" ", "-"), s)
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

/// Load binding systems from a CSV file.
/// Expected columns: name, interaction_type, description, ligand_atoms, protein_atoms
/// OR: name, preset (to reference a built-in preset by name)
let private loadSystemsFromCsv (path: string) : BindingSystem list =
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
            | Some system -> Some { system with Name = name }
            | None ->
                if not quiet then
                    eprintfn "  Warning: unknown preset '%s' (available: %s)" presetKey presetNames
                None
        | None ->
            match get "ligand_atoms", get "protein_atoms" with
            | Some lAtoms, Some pAtoms ->
                let interType = get "interaction_type" |> Option.defaultValue "Unknown"
                let desc = get "description" |> Option.defaultValue ""
                let ligand = moleculeFromAtomString (name + " ligand") lAtoms
                let protein = moleculeFromAtomString (name + " protein") pAtoms
                Some
                    { Name = name
                      Ligand = ligand
                      ProteinFragment = protein
                      InteractionType = interType
                      Description = desc }
            | _ ->
                if not quiet then
                    eprintfn "  Warning: row '%s' missing required columns" name
                None)

// ==============================================================================
// SYSTEM SELECTION
// ==============================================================================

let systems : BindingSystem list =
    let allSystems =
        match inputFile with
        | Some path ->
            let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
            if not quiet then
                printfn "Loading binding systems from: %s" resolved
            loadSystemsFromCsv resolved
        | None ->
            builtinPresets |> Map.toList |> List.map snd

    match systemFilter with
    | [] -> allSystems
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        allSystems
        |> List.filter (fun s ->
            let key = s.Name.ToLowerInvariant().Replace(" ", "-")
            filterSet |> Set.exists (fun f -> key.Contains f))

if List.isEmpty systems then
    eprintfn "Error: No binding systems selected. Available presets: %s" presetNames
    exit 1

// ==============================================================================
// QUANTUM BACKEND (Rule 1: all VQE via IQuantumBackend)
// ==============================================================================

let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Protein-Ligand Binding Affinity Comparison"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:      %s" backend.Name
    printfn "  Systems:      %d" systems.Length
    printfn "  VQE iters:    %d (tol: %g Ha)" maxIterations tolerance
    printfn "  Temperature:  %.1f K (%.1f C)" temperature (temperature - 273.15)
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

/// Build a complex molecule from ligand + protein fragments.
let private buildComplex (system: BindingSystem) : Molecule =
    let offsetBonds =
        system.ProteinFragment.Bonds
        |> List.map (fun b ->
            { b with
                Atom1 = b.Atom1 + system.Ligand.Atoms.Length
                Atom2 = b.Atom2 + system.Ligand.Atoms.Length })
    { Name = sprintf "%s complex" system.Name
      Atoms = system.Ligand.Atoms @ system.ProteinFragment.Atoms
      Bonds = system.Ligand.Bonds @ offsetBonds
      Charge = 0
      Multiplicity = 1 }

/// Interpret binding energy for drug discovery context.
let private interpretBinding (dEKcal: float) : string =
    if dEKcal < -10.0 then "Strong binding (drug-like affinity)"
    elif dEKcal < -5.0 then "Moderate binding (lead compound)"
    elif dEKcal < -1.0 then "Weak binding (hit compound)"
    elif dEKcal < 0.0 then "Very weak binding"
    else "Unfavorable (no binding)"

/// Estimate dissociation constant Kd from binding energy.
/// dG ~ dE (neglecting entropy), Kd = exp(dG / RT).
let private estimateKd (dEKcal: float) (tempK: float) : float * string =
    let rt = gasR_kcal * tempK  // kcal/mol
    if dEKcal < 0.0 then
        let kd = exp(dEKcal / rt)  // dimensionless ratio; interpret as molar
        let kdStr =
            if kd < 1e-9 then sprintf "%.2e M (picomolar)" kd
            elif kd < 1e-6 then sprintf "%.2e M (nanomolar)" kd
            elif kd < 1e-3 then sprintf "%.2e M (micromolar)" kd
            else sprintf "%.2e M (millimolar)" kd
        (kd, kdStr)
    else
        (infinity, "N/A (unfavorable)")

/// Compute the full binding energy profile for one system.
let private computeSystem
    (backend: IQuantumBackend)
    (maxIter: int)
    (tol: float)
    (temp: float)
    (idx: int)
    (total: int)
    (system: BindingSystem)
    : BindingResult =
    if not quiet then
        printfn "  [%d/%d] %s (%s)" (idx + 1) total system.Name system.InteractionType
        printfn "         %s" system.Description

    let startTime = DateTime.Now
    let mutable anyFailure = false

    /// Unwrap a VQE result, logging failures and tracking error state.
    let unwrapEnergy (label: string) (name: string) (res: Result<float, string>, elapsed: float) : float * float =
        match res with
        | Ok e ->
            if not quiet then
                printfn "         %-10s %-22s  E = %10.6f Ha  (%.1fs)" label name e elapsed
            (e, elapsed)
        | Error _ ->
            anyFailure <- true
            if not quiet then
                printfn "         %-10s %-22s  E = FAILED         (%.1fs)" label name elapsed
            (0.0, elapsed)

    let (ligandE, _) = unwrapEnergy "ligand" system.Ligand.Name (computeEnergy backend maxIter tol system.Ligand)
    let (proteinE, _) = unwrapEnergy "protein" system.ProteinFragment.Name (computeEnergy backend maxIter tol system.ProteinFragment)

    let complex = buildComplex system
    let (complexE, _) = unwrapEnergy "complex" complex.Name (computeEnergy backend maxIter tol complex)

    let totalTime = (DateTime.Now - startTime).TotalSeconds

    // Binding energy: E_complex - E_protein - E_ligand
    let dEHartree = complexE - proteinE - ligandE
    let dEKcal = dEHartree * hartreeToKcalMol
    let dEKJ = dEHartree * hartreeToKJMol

    let interp = if anyFailure then "VQE FAILED" else interpretBinding dEKcal
    let (kd, kdStr) = if anyFailure then (infinity, "N/A (VQE failed)") else estimateKd dEKcal temp

    if not quiet then
        if anyFailure then
            printfn "         => INCOMPLETE (VQE failure - energies are unreliable)"
        else
            printfn "         => dE = %.2f kcal/mol  |  Kd ~ %s" dEKcal kdStr
        printfn ""

    { System = system
      LigandEnergy = ligandE
      ProteinEnergy = proteinE
      ComplexEnergy = complexE
      BindingEnergyHartree = dEHartree
      BindingEnergyKcal = dEKcal
      BindingEnergyKJ = dEKJ
      EstimatedKd = kd
      KdStr = kdStr
      Interpretation = interp
      ComputeTimeSeconds = totalTime
      HasVqeFailure = anyFailure }

// --- Run all systems ---

if not quiet then
    printfn "Computing binding energies..."
    printfn ""

let results =
    systems
    |> List.mapi (fun i system -> computeSystem backend maxIterations tolerance temperature i systems.Length system)

// Sort: most negative binding energy first (strongest binder).
// Failed systems sink to bottom.
let ranked =
    results
    |> List.sortBy (fun r ->
        if r.HasVqeFailure then (2, infinity)
        elif r.BindingEnergyKcal >= 0.0 then (1, r.BindingEnergyKcal)
        else (0, r.BindingEnergyKcal))

// ==============================================================================
// RANKED COMPARISON TABLE
// ==============================================================================

let printTable () =
    printfn "=================================================================="
    printfn "  Ranked Binding Affinities (by binding energy)"
    printfn "=================================================================="
    printfn ""
    printfn "  %-4s  %-20s  %-10s  %13s  %13s  %s"
        "#" "System" "Type" "dE (kcal/mol)" "dE (kJ/mol)" "Interpretation"
    printfn "  %s" (String('=', 95))

    ranked
    |> List.iteri (fun i r ->
        printfn "  %-4d  %-20s  %-10s  %13.2f  %13.2f  %s"
            (i + 1)
            r.System.Name
            r.System.InteractionType
            r.BindingEnergyKcal
            r.BindingEnergyKJ
            r.Interpretation)

    printfn ""

    // Dissociation constants
    printfn "  %-4s  %-20s  %-10s  %20s  %10s"
        "#" "System" "Type" "Estimated Kd" "Time (s)"
    printfn "  %s" (String('-', 75))

    ranked
    |> List.iteri (fun i r ->
        printfn "  %-4d  %-20s  %-10s  %20s  %10.1f"
            (i + 1) r.System.Name r.System.InteractionType r.KdStr r.ComputeTimeSeconds)

    printfn ""

// Always print the ranked comparison table — that's the primary output of this tool,
// even in --quiet mode (which only suppresses per-system progress output).
printTable ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    let best = ranked |> List.head
    let totalTime = results |> List.sumBy (fun r -> r.ComputeTimeSeconds)
    printfn "  Strongest binder:  %s (%s, dE = %.2f kcal/mol)" best.System.Name best.System.InteractionType best.BindingEnergyKcal
    printfn "  Total time:        %.1f seconds" totalTime
    printfn "  Quantum:           all VQE via IQuantumBackend [Rule 1 compliant]"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    ranked
    |> List.mapi (fun i r ->
        [ "rank", string (i + 1)
          "system", r.System.Name
          "interaction_type", r.System.InteractionType
          "description", r.System.Description
          "binding_energy_hartree", sprintf "%.6f" r.BindingEnergyHartree
          "binding_energy_kcal_mol", sprintf "%.2f" r.BindingEnergyKcal
          "binding_energy_kj_mol", sprintf "%.2f" r.BindingEnergyKJ
          "estimated_kd", r.KdStr
          "interpretation", r.Interpretation
          "ligand_energy_ha", sprintf "%.6f" r.LigandEnergy
          "protein_energy_ha", sprintf "%.6f" r.ProteinEnergy
          "complex_energy_ha", sprintf "%.6f" r.ComplexEnergy
          "compute_time_s", sprintf "%.1f" r.ComputeTimeSeconds
          "temperature_k", sprintf "%.1f" temperature
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
        [ "rank"; "system"; "interaction_type"; "description"; "binding_energy_hartree"
          "binding_energy_kcal_mol"; "binding_energy_kj_mol"; "estimated_kd"
          "interpretation"; "ligand_energy_ha"; "protein_energy_ha"; "complex_energy_ha"
          "compute_time_s"; "temperature_k"; "has_vqe_failure" ]
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
    printfn "     --systems hf-dimer,water-hf               Run specific systems"
    printfn "     --input systems.csv                       Load custom binding systems from CSV"
    printfn "     --csv results.csv                         Export ranked table as CSV"
    printfn ""
