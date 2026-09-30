// ==============================================================================
// Protein-Ligand Binding Affinity Comparison
// ==============================================================================
// Compares hydrogen-bonded donor-acceptor pairs, the interaction that anchors
// drugs in binding sites, by VQE interaction energy
//     dE = E(complex) - E(donor) - E(acceptor)
// (negative = bound). Each system is a real closed-shell complex: hydrogen
// fluoride donating a hydrogen bond to F, S, Cl or O acceptors, models of
// fluorinated ligands meeting backbone, cysteine, halogen and water sites.
//
// Background:
// Binding affinity is the fundamental measure of drug-target interaction
// strength. Classical force fields approximate electrostatics and dispersion;
// hydrogen bonds also involve charge transfer and polarisation, which a
// correlated calculation on the complex captures.
//
// HAMILTONIAN SOURCE:
// By default every species runs on bundled FCIDUMP integrals
// (examples/_data/chemistry/fcidump: RHF/STO-3G geometries of monomers and
// complexes optimised with PySCF, CASSCF active spaces; README.md there gives
// the method). Each monomer is CAS(2,2) and each complex
// CAS(4,4), so both sides of dE correlate the same number of orbitals.
// Energies are STO-3G totals: dE has the right sign and rough size for
// hydrogen bonds, but a minimal basis carries a large basis-set superposition
// error, and dE is an electronic energy, not a free energy.
//   --fcidump-dir DIR  use your own FCIDUMP files (<species-slug>.fcidump)
//   --empirical        run on the library's EMPIRICAL prototype Hamiltonian
//                      instead (illustrative only, clearly labelled)
// Each VQE is capped at 16 qubits (8 active orbitals).
//
// Usage:
//   dotnet fsi BindingAffinity.fsx
//   dotnet fsi BindingAffinity.fsx -- --help
//   dotnet fsi BindingAffinity.fsx -- --systems hf-dimer,hf-h2o
//   dotnet fsi BindingAffinity.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Shirts & Mobley, "Free Energy Calculations" Methods Mol. Biol. (2017)
//   [2] Cao et al., "Quantum Chemistry in the Age of Quantum Computing" Chem. Rev. (2019)
//   [3] Wikipedia: Binding_affinity; Hydrogen_fluoride (dimer)
// ==============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
// The library comes from NuGet; `dotnet fsi --define:LOCAL_BUILD <script>` uses the repo's Debug build.
#if LOCAL_BUILD
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#else
#r "nuget: FSharp.Azure.Quantum"
#endif
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
#load "../_common/ChemistryIntegrals.fs"

open System
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "BindingAffinity.fsx"
    "Compare hydrogen-bonded donor-acceptor pairs by VQE interaction energy"
    [
        {
            Cli.OptionSpec.Name = "systems"
            Description = "Comma-separated system names to run (default: all)"
            Default = Some "all"
        }
        {
            Cli.OptionSpec.Name = "max-iterations"
            Description = "Maximum VQE iterations"
            Default = Some "50"
        }
        {
            Cli.OptionSpec.Name = "tolerance"
            Description = "Energy convergence tolerance (Hartree)"
            Default = Some "1e-4"
        }
        {
            Cli.OptionSpec.Name = "temperature"
            Description = "Temperature for the Kd estimate (Kelvin)"
            Default = Some "300"
        }
        {
            Cli.OptionSpec.Name = "fcidump-dir"
            Description = "Directory with one FCIDUMP per species (<species-slug>.fcidump)"
            Default = Some "bundled examples/_data/chemistry/fcidump"
        }
        {
            Cli.OptionSpec.Name = "empirical"
            Description = "Use the EMPIRICAL prototype Hamiltonian instead of integrals (flag; illustrative only)"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "output"
            Description = "Write results to JSON file"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "csv"
            Description = "Write results to CSV file"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "quiet"
            Description = "Suppress informational output (flag)"
            Default = None
        }
    ]
    args

let quiet = Cli.hasFlag "quiet" args
let systemFilter = args |> Cli.getCommaSeparated "systems"
let maxIterations = Cli.getIntOr "max-iterations" 50 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args
let temperature = Cli.getFloatOr "temperature" 300.0 args

let integralDirectory =
    ChemistryIntegrals.integralDirectory
        (args
         |> Cli.tryGet "fcidump-dir"
         |> Option.map (Data.resolveRelative __SOURCE_DIRECTORY__))
        (Cli.hasFlag "empirical" args)

/// Widest VQE this example runs: 2 qubits per active spatial orbital.
[<Literal>]
let maxVqeQubits = 16

[<Literal>]
let hartreeToKcalMol = 627.509

[<Literal>]
let hartreeToKJMol = 2625.5

/// Gas constant (kcal/(mol K))
[<Literal>]
let gasR_kcal = 1.987e-3

// ==============================================================================
// BINDING SYSTEMS
// ==============================================================================

/// A hydrogen-bond donor, an acceptor and their complex.
type BindingSystem =
    {
        Name: string
        Donor: Molecule
        Acceptor: Molecule
        Complex: Molecule
        InteractionType: string
        /// Literature interaction energy De (kcal/mol), where well established
        ReferenceDe: float option
        Description: string
    }

let private species = ChemistryIntegrals.loadSpecies
let private hydrogenFluoride = species "Hydrogen fluoride [CAS(2,2)]"

let private builtinSystems: BindingSystem list =
    [
        {
            Name = "HF-Dimer"
            Donor = hydrogenFluoride
            Acceptor = hydrogenFluoride
            Complex = species "HF dimer (F-H...F) [CAS(4,4)]"
            InteractionType = "F-H...F"
            ReferenceDe = Some -4.6
            Description = "Hydrogen fluoride dimer (classic H-bond benchmark)"
        }
        {
            Name = "HF-H2S"
            Donor = hydrogenFluoride
            Acceptor = species "Hydrogen sulfide [CAS(2,2)]"
            Complex = species "HF-H2S (F-H...S) [CAS(4,4)]"
            InteractionType = "F-H...S"
            ReferenceDe = None
            Description = "HF...H2S H-bond (cysteine thiol acceptor model)"
        }
        {
            Name = "HF-HCl"
            Donor = hydrogenFluoride
            Acceptor = species "Hydrogen chloride [CAS(2,2)]"
            Complex = species "HF-HCl (F-H...Cl) [CAS(4,4)]"
            InteractionType = "F-H...Cl"
            ReferenceDe = None
            Description = "HF...HCl H-bond (halogen acceptor model)"
        }
        {
            Name = "HF-H2O"
            Donor = hydrogenFluoride
            Acceptor = species "Water [CAS(2,2)]"
            Complex = species "HF-H2O (F-H...O) [CAS(4,4)]"
            InteractionType = "F-H...O"
            ReferenceDe = Some -8.7
            Description = "HF...H2O H-bond (fluorinated ligand meeting a bound water)"
        }
    ]

let private key (name: string) = ChemistryIntegrals.speciesSlug name

let systems =
    match systemFilter with
    | [] -> builtinSystems
    | filters ->
        builtinSystems
        |> List.filter (fun s -> filters |> List.exists (fun f -> (key s.Name).Contains(f.ToLowerInvariant())))

if List.isEmpty systems then
    eprintfn
        "Error: no systems selected. Available: %s"
        (builtinSystems |> List.map (fun s -> key s.Name) |> String.concat ", ")

    exit 1

// ==============================================================================
// VQE
// ==============================================================================

let backend: IQuantumBackend = LocalBackend() :> IQuantumBackend

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
    printfn "  Integrals:    %s" (ChemistryIntegrals.describeDirectory integralDirectory)
    printfn "  Measure:      dE = E(complex) - E(donor) - E(acceptor)"
    printfn ""

let energies =
    ChemistryIntegrals.EnergyCache(backend, maxIterations, tolerance, maxVqeQubits, integralDirectory)

/// Interpret an interaction energy.
let private interpretBinding (dEKcal: float) : string =
    if dEKcal < -10.0 then "Strong H-bond"
    elif dEKcal < -5.0 then "Moderate H-bond"
    elif dEKcal < -1.0 then "Weak H-bond"
    elif dEKcal < 0.0 then "Very weak"
    else "Unbound"

/// Dissociation-constant estimate exp(dE / RT), treating dE as a free energy (entropy neglected).
let private estimateKd (dEKcal: float) : string =
    if dEKcal < 0.0 then
        let kd = exp (dEKcal / (gasR_kcal * temperature))

        if kd < 1e-9 then $"%.2e{kd} M (pM)"
        elif kd < 1e-6 then $"%.2e{kd} M (nM)"
        elif kd < 1e-3 then $"%.2e{kd} M (uM)"
        else $"%.2e{kd} M (mM)"
    else
        "N/A (unbound)"

/// Result of one system.
type BindingResult =
    {
        System: BindingSystem
        /// Interaction energy in Hartree; None when a species failed
        BindingEnergy: float option
        Sources: EnergySource list
        Failures: string list
    }

let private computeSystem (index: int) (system: BindingSystem) : BindingResult =
    if not quiet then
        printfn "  [%d/%d] %s (%s)" (index + 1) systems.Length system.Name system.InteractionType
        printfn "         %s" system.Description

    let energyOf (role: string) (molecule: Molecule) =
        let result = energies.Energy molecule

        if not quiet then
            match result with
            | Ok e ->
                printfn
                    "         %-9s %-32s E = %14.6f Ha  [%s]%s"
                    role
                    molecule.Name
                    e.Energy
                    (ChemistryIntegrals.describeSource e.Source)
                    (if e.Converged then "" else " not converged")
            | Error msg -> printfn "         %-9s %-32s E = FAILED  (%s)" role molecule.Name msg

        result

    let donor = energyOf "donor" system.Donor
    let acceptor = energyOf "acceptor" system.Acceptor
    let complex = energyOf "complex" system.Complex
    let all = [ donor; acceptor; complex ]

    let failures =
        all
        |> List.choose (function
            | Error msg -> Some msg
            | Ok _ -> None)

    let computed =
        all
        |> List.choose (function
            | Ok e -> Some e
            | Error _ -> None)

    let bindingEnergy =
        match donor, acceptor, complex with
        | Ok d, Ok a, Ok c -> Some(c.Energy - d.Energy - a.Energy)
        | _ -> None

    if not quiet then
        match bindingEnergy with
        | Some dE ->
            printfn
                "         => dE = %.2f kcal/mol  |  Kd ~ %s"
                (dE * hartreeToKcalMol)
                (estimateKd (dE * hartreeToKcalMol))
        | None -> printfn "         => INCOMPLETE (a species failed VQE: no interaction energy)"

        printfn ""

    {
        System = system
        BindingEnergy = bindingEnergy
        Sources = computed |> List.map (fun e -> e.Source) |> List.distinct
        Failures = failures
    }

if not quiet then
    printfn "Computing interaction energies..."
    printfn ""

let results = systems |> List.mapi computeSystem

// Strongest binder first; incomplete systems last.
let ranked =
    results
    |> List.sortBy (fun r ->
        match r.BindingEnergy with
        | Some dE -> (0, dE)
        | None -> (1, 0.0))

let private sourceLabel (r: BindingResult) =
    match r.Sources with
    | [] -> "none"
    | sources -> sources |> List.map ChemistryIntegrals.describeSource |> String.concat " + "

// ==============================================================================
// RANKED COMPARISON TABLE
// ==============================================================================

let printTable () =
    printfn "=================================================================="
    printfn "  Ranked Interaction Energies (strongest first)"
    printfn "=================================================================="
    printfn ""

    printfn
        "  %-4s  %-10s  %-10s  %13s  %13s  %13s  %-16s  %s"
        "#"
        "System"
        "Type"
        "dE (kcal/mol)"
        "dE (kJ/mol)"
        "ref De"
        "Interpretation"
        "Hamiltonian"

    printfn "  %s" (String('=', 110))

    ranked
    |> List.iteri (fun i r ->
        let reference =
            r.System.ReferenceDe |> Option.map (sprintf "%.1f") |> Option.defaultValue "-"

        match r.BindingEnergy with
        | Some dE ->
            let kcal = dE * hartreeToKcalMol

            printfn
                "  %-4d  %-10s  %-10s  %13.2f  %13.2f  %13s  %-16s  %s"
                (i + 1)
                r.System.Name
                r.System.InteractionType
                kcal
                (dE * hartreeToKJMol)
                reference
                (interpretBinding kcal)
                (sourceLabel r)
        | None ->
            printfn
                "  %-4d  %-10s  %-10s  %13s  %13s  %13s  %-16s  %s"
                (i + 1)
                r.System.Name
                r.System.InteractionType
                "INCOMPLETE"
                "-"
                reference
                "-"
                (sourceLabel r))

    printfn ""

    ranked
    |> List.filter (fun r -> not r.Failures.IsEmpty)
    |> List.iter (fun r -> printfn "  %s: %s" r.System.Name (String.concat "; " (List.distinct r.Failures)))

    if ranked |> List.exists (fun r -> r.Sources |> List.contains EmpiricalHamiltonian) then
        ChemistryIntegrals.empiricalNote |> List.iter (printfn "%s")

    printfn "  ref De: literature interaction energies (kcal/mol) where well established."
    printfn "  STO-3G interaction energies carry a large basis-set superposition error."
    printfn ""

printTable ()

if not quiet then
    match ranked |> List.tryFind (fun r -> r.BindingEnergy.IsSome) with
    | Some best ->
        printfn
            "  Strongest binder:  %s (%s, dE = %.2f kcal/mol; %s)"
            best.System.Name
            best.System.InteractionType
            (best.BindingEnergy.Value * hartreeToKcalMol)
            (sourceLabel best)
    | None -> printfn "  Strongest binder:  none (no system completed)"

    printfn "  Quantum:           all VQE via IQuantumBackend [Rule 1 compliant]"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    ranked
    |> List.mapi (fun i r ->
        let value format =
            match r.BindingEnergy with
            | Some dE -> format dE
            | None -> "INCOMPLETE"

        [
            "rank", string (i + 1)
            "system", r.System.Name
            "interaction_type", r.System.InteractionType
            "description", r.System.Description
            "binding_energy_hartree", value (sprintf "%.6f")
            "binding_energy_kcal_mol", value (fun dE -> sprintf "%.2f" (dE * hartreeToKcalMol))
            "binding_energy_kj_mol", value (fun dE -> sprintf "%.2f" (dE * hartreeToKJMol))
            "reference_de_kcal_mol", r.System.ReferenceDe |> Option.map (sprintf "%.1f") |> Option.defaultValue ""
            "estimated_kd", value (fun dE -> estimateKd (dE * hartreeToKcalMol))
            "temperature_k", sprintf "%.1f" temperature
            "hamiltonian", sourceLabel r
        ]
        |> Map.ofList)

let header =
    [
        "rank"
        "system"
        "interaction_type"
        "description"
        "binding_energy_hartree"
        "binding_energy_kcal_mol"
        "binding_energy_kj_mol"
        "reference_de_kcal_mol"
        "estimated_kd"
        "temperature_k"
        "hamiltonian"
    ]

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path resultMaps

    if not quiet then
        printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let rows =
        resultMaps
        |> List.map (fun m -> header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))

    Reporting.writeCsv path header rows

    if not quiet then
        printfn "Results written to %s" path
| None -> ()

if argv.Length = 0 && not quiet then
    printfn "Tip: Run with --help to see all options."
    printfn "     --systems hf-dimer,hf-h2o          Run specific systems"
    printfn "     --fcidump-dir ./fcidumps           Your own FCIDUMP integrals"
    printfn "     --csv results.csv                  Export ranked table as CSV"
    printfn ""
