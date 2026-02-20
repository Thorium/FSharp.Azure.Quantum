// ==============================================================================
// Antibody-Antigen CDR Contact Type Comparison
// ==============================================================================
// Compares binding energy contributions from different CDR-epitope interface
// contact types using VQE fragment molecular orbital (FMO) approach.
//
// The question: "Which type of CDR-epitope interaction contributes the most
// binding energy at a therapeutic antibody interface?"
//
// Accepts multiple contact types (built-in presets or --input CSV), runs VQE
// on each contact's antibody fragment, antigen fragment, and complex, then
// outputs a ranked comparison table.
//
// Background:
// Therapeutic antibodies (mAbs) bind antigens via CDR loops. Each interface
// comprises multiple contact types: salt bridges (Arg-Asp), H-bonds (Ser-Asn),
// van der Waals contacts, and halogen interactions. Quantum VQE captures
// electron correlation and polarisation that classical force fields miss,
// particularly for charged and dispersion-dominated contacts.
//
// IMPORTANT LIMITATION:
// This example uses EMPIRICAL Hamiltonian coefficients (not molecular integrals).
// Calculated energies are ILLUSTRATIVE. For production use, molecular integral
// calculation (via PySCF, Psi4, or similar) would be needed.
//
// Usage:
//   dotnet fsi AntibodyBinding.fsx
//   dotnet fsi AntibodyBinding.fsx -- --help
//   dotnet fsi AntibodyBinding.fsx -- --contacts salt-bridge,h-bond
//   dotnet fsi AntibodyBinding.fsx -- --input contacts.csv
//   dotnet fsi AntibodyBinding.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Roitt's Essential Immunology, 13th Ed., Ch. 3 (Antibodies)
//   [2] Cao et al., "Quantum Chemistry in the Age of Quantum Computing" Chem. Rev. (2019)
//   [3] Wikipedia: Antibody (https://en.wikipedia.org/wiki/Antibody)
//   [4] Fedorov, D.G. "Fragment Molecular Orbital Method" (2017)
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

Cli.exitIfHelp "AntibodyBinding.fsx"
    "Compare CDR-epitope contact type binding energies via VQE (antibody FMO approach)"
    [ { Cli.OptionSpec.Name = "input"; Description = "CSV file with custom contact systems"; Default = Some "built-in presets" }
      { Cli.OptionSpec.Name = "contacts"; Description = "Comma-separated preset names to run (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "50" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Energy convergence tolerance (Hartree)"; Default = Some "1e-4" }
      { Cli.OptionSpec.Name = "temperature"; Description = "Temperature for Kd estimation (Kelvin)"; Default = Some "300" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputFile = args |> Cli.tryGet "input"
let contactFilter = args |> Cli.getCommaSeparated "contacts"
let maxIterations = Cli.getIntOr "max-iterations" 50 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args
let temperature = Cli.getFloatOr "temperature" 300.0 args

// ==============================================================================
// TYPES
// ==============================================================================

/// A CDR-epitope contact type modelled as antibody fragment + antigen fragment.
type ContactSystem =
    { Name: string
      AntibodyFragment: Molecule
      AntigenFragment: Molecule
      ContactType: string
      CdrRegion: string
      Description: string }

/// Result of computing one contact's energy profile via VQE.
type ContactResult =
    { Contact: ContactSystem
      AntibodyEnergy: float
      AntigenEnergy: float
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
// BUILT-IN CONTACT TYPE PRESETS
// ==============================================================================
// Each system models a different CDR-epitope interaction type using
// NISQ-tractable model fragments (<=3 atoms per fragment, <=5 atom complex).
// Complexes >5 atoms cause VQE timeouts on LocalBackend.

/// Salt bridge model (Arg+...Asp-): LiH + HF.
/// LiH models the electropositive character of guanidinium (Arg sidechain).
/// HF models the electronegative carboxylate (Asp/Glu sidechain).
/// Salt bridges contribute ~3-5 kcal/mol at CDR3-epitope interfaces.
let private saltBridgeContact : ContactSystem =
    let antibody : Molecule =
        { Name = "LiH (Arg+ model)"
          Atoms =
            [ { Element = "Li"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (1.60, 0.0, 0.0) } ]  // Li-H bond ~1.60 A
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let antigen : Molecule =
        { Name = "HF (Asp- model)"
          Atoms =
            [ { Element = "H"; Position = (3.40, 0.0, 0.0) }     // H...F gap ~1.8 A
              { Element = "F"; Position = (4.32, 0.0, 0.0) } ]
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "Salt-Bridge"
      AntibodyFragment = antibody
      AntigenFragment = antigen
      ContactType = "Salt bridge"
      CdrRegion = "CDR3"
      Description = "Arg-Asp ionic contact (LiH...HF model, CDR3-epitope)" }

/// Hydrogen bond model (Ser-OH...Asn-C=O): HF donor + H2O acceptor.
/// HF models the strong H-bond donor (sidechain NH or OH).
/// H2O models the acceptor oxygen (backbone C=O or Asn/Gln sidechain).
/// H-bonds contribute ~1-3 kcal/mol per contact.
let private hBondContact : ContactSystem =
    let antibody : Molecule =
        { Name = "HF (NH donor model)"
          Atoms =
            [ { Element = "H"; Position = (0.0, 0.0, 0.0) }
              { Element = "F"; Position = (0.92, 0.0, 0.0) } ]
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let antigen : Molecule =
        { Name = "H2O (C=O acceptor model)"
          Atoms =
            [ { Element = "O"; Position = (2.72, 0.0, 0.0) }       // F-H...O distance ~1.8 A
              { Element = "H"; Position = (3.35, 0.76, 0.0) }
              { Element = "H"; Position = (3.35, -0.76, 0.0) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "H-Bond"
      AntibodyFragment = antibody
      AntigenFragment = antigen
      ContactType = "Hydrogen bond"
      CdrRegion = "CDR2"
      Description = "Ser/Tyr-OH...Asn/Gln C=O (HF...H2O model, CDR2-epitope)" }

/// Van der Waals / CH-pi dispersion model: LiH + H2.
/// Models the weak dispersion interactions from hydrophobic CDR contacts
/// (Leu, Ile, Val sidechains packed against epitope).
/// Each contributes only ~0.5-2 kcal/mol but they accumulate.
let private dispersionContact : ContactSystem =
    let antibody : Molecule =
        { Name = "LiH (CH model)"
          Atoms =
            [ { Element = "Li"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (1.60, 0.0, 0.0) } ]
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let antigen : Molecule =
        { Name = "H2 (CH model)"
          Atoms =
            [ { Element = "H"; Position = (3.50, 0.0, 0.0) }     // ~1.9 A gap (van der Waals)
              { Element = "H"; Position = (4.24, 0.0, 0.0) } ]   // H-H bond 0.74 A
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "Dispersion"
      AntibodyFragment = antibody
      AntigenFragment = antigen
      ContactType = "Van der Waals"
      CdrRegion = "CDR1"
      Description = "Leu/Ile hydrophobic packing (LiH...H2 dispersion model, CDR1)" }

/// Halogen bond model: HCl + H2O.
/// Models halogenated epitope residue interacting with CDR backbone.
/// Relevant for synthetic antigens and drug-modified epitopes.
/// Halogen bonds: ~1-4 kcal/mol depending on halogen.
let private halogenContact : ContactSystem =
    let antibody : Molecule =
        { Name = "H2O (backbone model)"
          Atoms =
            [ { Element = "O"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (0.59, 0.76, 0.0) }
              { Element = "H"; Position = (0.59, -0.76, 0.0) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let antigen : Molecule =
        { Name = "HCl (halogen model)"
          Atoms =
            [ { Element = "Cl"; Position = (2.80, 0.0, 0.0) }     // O...Cl distance ~2.8 A
              { Element = "H"; Position = (4.08, 0.0, 0.0) } ]    // H-Cl bond 1.28 A
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "Halogen-Bond"
      AntibodyFragment = antibody
      AntigenFragment = antigen
      ContactType = "Halogen bond"
      CdrRegion = "CDR3"
      Description = "Backbone O...Cl-R halogen contact (H2O...HCl model, CDR3)" }

/// All built-in presets keyed by lowercase name.
let private builtinPresets : Map<string, ContactSystem> =
    [ saltBridgeContact; hBondContact; dispersionContact; halogenContact ]
    |> List.map (fun s -> s.Name.ToLowerInvariant(), s)
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

/// Load contact systems from a CSV file.
/// Expected columns: name, contact_type, cdr_region, description, antibody_atoms, antigen_atoms
/// OR: name, preset (to reference a built-in preset by name)
let private loadContactsFromCsv (path: string) : ContactSystem list =
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
            match get "antibody_atoms", get "antigen_atoms" with
            | Some abAtoms, Some agAtoms ->
                let contactType = get "contact_type" |> Option.defaultValue "Unknown"
                let cdr = get "cdr_region" |> Option.defaultValue "Unknown"
                let desc = get "description" |> Option.defaultValue ""
                let antibody = moleculeFromAtomString (name + " antibody") abAtoms
                let antigen = moleculeFromAtomString (name + " antigen") agAtoms
                Some
                    { Name = name
                      AntibodyFragment = antibody
                      AntigenFragment = antigen
                      ContactType = contactType
                      CdrRegion = cdr
                      Description = desc }
            | _ ->
                if not quiet then
                    eprintfn "  Warning: row '%s' missing required columns" name
                None)

// ==============================================================================
// CONTACT SELECTION
// ==============================================================================

let contacts : ContactSystem list =
    let allContacts =
        match inputFile with
        | Some path ->
            let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
            if not quiet then
                printfn "Loading contact systems from: %s" resolved
            loadContactsFromCsv resolved
        | None ->
            builtinPresets |> Map.toList |> List.map snd

    match contactFilter with
    | [] -> allContacts
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        allContacts
        |> List.filter (fun s ->
            let key = s.Name.ToLowerInvariant()
            filterSet |> Set.exists (fun f -> key.Contains f))

if List.isEmpty contacts then
    eprintfn "Error: No contact systems selected. Available presets: %s" presetNames
    exit 1

// ==============================================================================
// QUANTUM BACKEND (Rule 1: all VQE via IQuantumBackend)
// ==============================================================================

let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Antibody-Antigen CDR Contact Type Comparison"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:      %s" backend.Name
    printfn "  Contacts:     %d" contacts.Length
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

/// Build a complex molecule from antibody + antigen fragments.
let private buildComplex (contact: ContactSystem) : Molecule =
    let offsetBonds =
        contact.AntigenFragment.Bonds
        |> List.map (fun b ->
            { b with
                Atom1 = b.Atom1 + contact.AntibodyFragment.Atoms.Length
                Atom2 = b.Atom2 + contact.AntibodyFragment.Atoms.Length })
    { Name = sprintf "%s complex" contact.Name
      Atoms = contact.AntibodyFragment.Atoms @ contact.AntigenFragment.Atoms
      Bonds = contact.AntibodyFragment.Bonds @ offsetBonds
      Charge = 0
      Multiplicity = 1 }

/// Interpret binding energy for antibody interface context.
let private interpretContact (dEKcal: float) : string =
    if dEKcal < -4.0 then "Strong contact (key CDR driver)"
    elif dEKcal < -2.0 then "Moderate contact (supporting)"
    elif dEKcal < -0.5 then "Weak contact (supplementary)"
    elif dEKcal < 0.0 then "Very weak (marginal contribution)"
    else "Unfavorable (destabilising)"

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

/// Compute the full binding energy profile for one contact type.
let private computeContact
    (backend: IQuantumBackend)
    (maxIter: int)
    (tol: float)
    (temp: float)
    (idx: int)
    (total: int)
    (contact: ContactSystem)
    : ContactResult =
    if not quiet then
        printfn "  [%d/%d] %s (%s, %s)" (idx + 1) total contact.Name contact.ContactType contact.CdrRegion
        printfn "         %s" contact.Description

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

    let (abEnergy, _) = unwrapEnergy "antibody" contact.AntibodyFragment.Name (computeEnergy backend maxIter tol contact.AntibodyFragment)
    let (agEnergy, _) = unwrapEnergy "antigen" contact.AntigenFragment.Name (computeEnergy backend maxIter tol contact.AntigenFragment)

    let complex = buildComplex contact
    let (complexE, _) = unwrapEnergy "complex" complex.Name (computeEnergy backend maxIter tol complex)

    let totalTime = (DateTime.Now - startTime).TotalSeconds

    // Binding energy: E_complex - E_antibody - E_antigen
    let dEHartree = complexE - abEnergy - agEnergy
    let dEKcal = dEHartree * hartreeToKcalMol
    let dEKJ = dEHartree * hartreeToKJMol

    let interp = if anyFailure then "VQE FAILED" else interpretContact dEKcal
    let (kd, kdStr) = if anyFailure then (infinity, "N/A (VQE failed)") else estimateKd dEKcal temp

    if not quiet then
        if anyFailure then
            printfn "         => INCOMPLETE (VQE failure - energies are unreliable)"
        else
            printfn "         => dE = %.2f kcal/mol  |  Kd ~ %s" dEKcal kdStr
        printfn ""

    { Contact = contact
      AntibodyEnergy = abEnergy
      AntigenEnergy = agEnergy
      ComplexEnergy = complexE
      BindingEnergyHartree = dEHartree
      BindingEnergyKcal = dEKcal
      BindingEnergyKJ = dEKJ
      EstimatedKd = kd
      KdStr = kdStr
      Interpretation = interp
      ComputeTimeSeconds = totalTime
      HasVqeFailure = anyFailure }

// --- Run all contacts ---

if not quiet then
    printfn "Computing CDR-epitope contact energies..."
    printfn ""

let results =
    contacts
    |> List.mapi (fun i contact -> computeContact backend maxIterations tolerance temperature i contacts.Length contact)

// Sort: most negative binding energy first (strongest contact).
// Failed contacts sink to bottom.
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
    printfn "  Ranked CDR-Epitope Contact Contributions (by binding energy)"
    printfn "=================================================================="
    printfn ""
    printfn "  %-4s  %-16s  %-14s  %-5s  %13s  %13s  %s"
        "#" "Contact" "Type" "CDR" "dE (kcal/mol)" "dE (kJ/mol)" "Interpretation"
    printfn "  %s" (String('=', 105))

    ranked
    |> List.iteri (fun i r ->
        printfn "  %-4d  %-16s  %-14s  %-5s  %13.2f  %13.2f  %s"
            (i + 1)
            r.Contact.Name
            r.Contact.ContactType
            r.Contact.CdrRegion
            r.BindingEnergyKcal
            r.BindingEnergyKJ
            r.Interpretation)

    printfn ""

    // Dissociation constants
    printfn "  %-4s  %-16s  %-14s  %20s  %10s"
        "#" "Contact" "Type" "Estimated Kd" "Time (s)"
    printfn "  %s" (String('-', 75))

    ranked
    |> List.iteri (fun i r ->
        printfn "  %-4d  %-16s  %-14s  %20s  %10.1f"
            (i + 1) r.Contact.Name r.Contact.ContactType r.KdStr r.ComputeTimeSeconds)

    printfn ""

// Always print the ranked comparison table — that's the primary output of this tool,
// even in --quiet mode (which only suppresses per-contact progress output).
printTable ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    let best = ranked |> List.head
    let totalTime = results |> List.sumBy (fun r -> r.ComputeTimeSeconds)
    printfn "  Strongest contact: %s (%s, %s, dE = %.2f kcal/mol)"
        best.Contact.Name best.Contact.ContactType best.Contact.CdrRegion best.BindingEnergyKcal
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
          "contact", r.Contact.Name
          "contact_type", r.Contact.ContactType
          "cdr_region", r.Contact.CdrRegion
          "description", r.Contact.Description
          "binding_energy_hartree", sprintf "%.6f" r.BindingEnergyHartree
          "binding_energy_kcal_mol", sprintf "%.2f" r.BindingEnergyKcal
          "binding_energy_kj_mol", sprintf "%.2f" r.BindingEnergyKJ
          "estimated_kd", r.KdStr
          "interpretation", r.Interpretation
          "antibody_energy_ha", sprintf "%.6f" r.AntibodyEnergy
          "antigen_energy_ha", sprintf "%.6f" r.AntigenEnergy
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
        [ "rank"; "contact"; "contact_type"; "cdr_region"; "description"
          "binding_energy_hartree"; "binding_energy_kcal_mol"; "binding_energy_kj_mol"
          "estimated_kd"; "interpretation"; "antibody_energy_ha"; "antigen_energy_ha"
          "complex_energy_ha"; "compute_time_s"; "temperature_k"; "has_vqe_failure" ]
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
    printfn "     --contacts salt-bridge,h-bond             Run specific contact types"
    printfn "     --input contacts.csv                      Load custom contacts from CSV"
    printfn "     --csv results.csv                         Export ranked table as CSV"
    printfn ""
