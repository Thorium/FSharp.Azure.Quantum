// ==============================================================================
// Electron Transport Chain — Redox Couple Comparison
// ==============================================================================
// Ranks model redox couples of the respiratory chain by VQE reduction energy
// and compares the ranking with the chain's order of standard potentials.
//
// The chain moves electrons from NADH (E0' = -0.32 V) through flavins and
// ubiquinone (~0 V) to oxygen (+0.82 V); each carrier is reduced by the one
// before it. Most of its couples are two-electron, two-proton steps, so each
// couple here is modelled by a small closed-shell molecule A and its reduced
// form AH2, and scored by the reduction energy
//     dE_red = E(AH2) - E(A) - E(H2)
// (more negative = stronger oxidant = later in the chain). Real heme and
// flavin cofactors are far too large for this; the models keep the chemistry
// of the reduced bond (aromatic ring, C=C, quinone, peroxide).
//
// HAMILTONIAN SOURCE:
// By default every species runs on bundled FCIDUMP integrals
// (examples/_data/chemistry/fcidump: RHF/STO-3G geometries optimised with PySCF,
// CASSCF active spaces; README.md there gives the method).
// Each couple keeps the same total active space on both sides: A and H2 are
// CAS(2,2), AH2 is CAS(4,4), and each water of the peroxide couple is CAS(2,2).
// Energies are STO-3G totals, so dE_red is close to an RHF/STO-3G reaction
// energy plus the active-space correlation. It gives signs and trends; a
// minimal basis is known to be poor for peroxides, and gas-phase reaction
// energies are not solution redox potentials.
//   --fcidump-dir DIR  use your own FCIDUMP files (<species-slug>.fcidump)
//   --empirical        run on the library's EMPIRICAL prototype Hamiltonian
//                      instead (illustrative only, clearly labelled)
// Each VQE is capped at 16 qubits (8 active orbitals).
//
// Usage:
//   dotnet fsi ElectronTransportChain.fsx
//   dotnet fsi ElectronTransportChain.fsx -- --help
//   dotnet fsi ElectronTransportChain.fsx -- --systems nad,ubiquinone
//   dotnet fsi ElectronTransportChain.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Harper's Illustrated Biochemistry, 28th Ed., Chapters 12-13 (standard potentials)
//   [2] Wikipedia: Electron_transport_chain
//   [3] NIST Chemistry WebBook (gas-phase enthalpies of formation)
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
    "ElectronTransportChain.fsx"
    "Rank electron transport chain redox couples by VQE reduction energy"
    [
        {
            Cli.OptionSpec.Name = "systems"
            Description = "Comma-separated couple names to run (default: all)"
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

// ==============================================================================
// REDOX COUPLES
// ==============================================================================

/// A two-electron, two-proton couple: Oxidized + H2 -> Reduced.
type RedoxCouple =
    {
        Name: string
        Oxidized: Molecule list
        Reduced: Molecule list
        /// Biological couple the model stands for
        Biology: string
        /// Standard potential of that couple at pH 7 (V)
        E0Prime: float
        /// Gas-phase reaction enthalpy of the model reaction from tabulated enthalpies of formation (kcal/mol)
        ReferenceDeltaH: float option
        Description: string
    }

let private species = ChemistryIntegrals.loadSpecies
let private hydrogen = species "Hydrogen (H2) [CAS(2,2)]"

let private builtinCouples: RedoxCouple list =
    [
        {
            Name = "NAD+/NADH"
            Oxidized = [ species "Pyridine [CAS(2,2)]" ]
            Reduced = [ species "1,4-Dihydropyridine [CAS(4,4)]" ]
            Biology = "NAD+/NADH (complex I donor)"
            E0Prime = -0.32
            ReferenceDeltaH = None
            Description = "Pyridine -> 1,4-dihydropyridine, the nicotinamide ring reduction"
        }
        {
            Name = "Fumarate/Succinate"
            Oxidized = [ species "Ethylene [CAS(2,2)]" ]
            Reduced = [ species "Ethane [CAS(4,4)]" ]
            Biology = "Fumarate/succinate (complex II)"
            E0Prime = 0.03
            ReferenceDeltaH = Some -32.6
            Description = "Ethylene -> ethane, the C=C reduction of fumarate"
        }
        {
            Name = "Ubiquinone/Ubiquinol"
            Oxidized = [ species "p-Benzoquinone [CAS(2,2)]" ]
            Reduced = [ species "Hydroquinone [CAS(4,4)]" ]
            Biology = "Ubiquinone/ubiquinol (Q cycle, complexes I-III)"
            E0Prime = 0.045
            ReferenceDeltaH = None
            Description = "p-Benzoquinone -> hydroquinone, the quinone head group"
        }
        {
            Name = "H2O2/H2O"
            Oxidized = [ species "Hydrogen peroxide [CAS(2,2)]" ]
            Reduced = [ species "Water [CAS(2,2)]"; species "Water [CAS(2,2)]" ]
            Biology = "Peroxide/water (complex IV, O2 -> H2O)"
            E0Prime = 1.36
            ReferenceDeltaH = Some -83.0
            Description = "H2O2 -> 2 H2O, the second half of oxygen reduction"
        }
    ]

let private key (name: string) = ChemistryIntegrals.speciesSlug name

let couples =
    match systemFilter with
    | [] -> builtinCouples
    | filters ->
        builtinCouples
        |> List.filter (fun c -> filters |> List.exists (fun f -> (key c.Name).Contains(f.ToLowerInvariant())))

if List.isEmpty couples then
    eprintfn
        "Error: no couples selected. Available: %s"
        (builtinCouples |> List.map (fun c -> key c.Name) |> String.concat ", ")

    exit 1

// ==============================================================================
// VQE
// ==============================================================================

let backend: IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Electron Transport Chain: Redox Couple Comparison"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:      %s" backend.Name
    printfn "  Couples:      %d" couples.Length
    printfn "  VQE iters:    %d (tol: %g Ha)" maxIterations tolerance
    printfn "  Integrals:    %s" (ChemistryIntegrals.describeDirectory integralDirectory)
    printfn "  Measure:      dE_red = E(reduced) - E(oxidized) - E(H2)"
    printfn ""

let energies =
    ChemistryIntegrals.EnergyCache(backend, maxIterations, tolerance, maxVqeQubits, integralDirectory)

/// Result of one couple.
type CoupleResult =
    {
        Couple: RedoxCouple
        /// Reduction energy in Hartree; None when a species failed
        ReductionEnergy: float option
        Sources: EnergySource list
        Failures: string list
    }

let private computeCouple (index: int) (couple: RedoxCouple) : CoupleResult =
    if not quiet then
        printfn "  [%d/%d] %s — %s" (index + 1) couples.Length couple.Name couple.Biology
        printfn "         %s" couple.Description

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

    let oxidized =
        (couple.Oxidized |> List.map (energyOf "oxidized"))
        @ [ energyOf "reductant" hydrogen ]

    let reduced = couple.Reduced |> List.map (energyOf "reduced")
    let all = oxidized @ reduced

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

    let total (results: Result<ChemistryIntegrals.SpeciesEnergy, string> list) =
        results
        |> List.sumBy (function
            | Ok e -> e.Energy
            | Error _ -> 0.0)

    let reductionEnergy =
        if failures.IsEmpty then
            Some(total reduced - total oxidized)
        else
            None

    if not quiet then
        match reductionEnergy with
        | Some dE -> printfn "         => dE_red = %.6f Ha = %.1f kcal/mol" dE (dE * hartreeToKcalMol)
        | None -> printfn "         => INCOMPLETE (a species failed VQE: no reduction energy)"

        printfn ""

    {
        Couple = couple
        ReductionEnergy = reductionEnergy
        Sources = computed |> List.map (fun e -> e.Source) |> List.distinct
        Failures = failures
    }

if not quiet then
    printfn "Computing reduction energies..."
    printfn ""

let results = couples |> List.mapi computeCouple

// Weakest oxidant first (least negative dE_red), as the chain orders its carriers.
let ranked =
    results
    |> List.sortBy (fun r ->
        match r.ReductionEnergy with
        | Some dE -> (0, -dE)
        | None -> (1, 0.0))

let private sourceLabel (r: CoupleResult) =
    match r.Sources with
    | [] -> "none"
    | sources -> sources |> List.map ChemistryIntegrals.describeSource |> String.concat " + "

// ==============================================================================
// RANKED COMPARISON TABLE
// ==============================================================================

let printTable () =
    printfn "=================================================================="
    printfn "  Redox Couples by Reduction Energy (weakest oxidant first)"
    printfn "=================================================================="
    printfn ""

    printfn
        "  %-4s  %-22s  %16s  %16s  %9s  %s"
        "#"
        "Couple"
        "dE_red (kcal/mol)"
        "ref dH (kcal/mol)"
        "E0' (V)"
        "Hamiltonian"

    printfn "  %s" (String('=', 100))

    ranked
    |> List.iteri (fun i r ->
        let reference =
            r.Couple.ReferenceDeltaH
            |> Option.map (sprintf "%.1f")
            |> Option.defaultValue "-"

        let computed =
            r.ReductionEnergy
            |> Option.map (fun dE -> sprintf "%.1f" (dE * hartreeToKcalMol))
            |> Option.defaultValue "INCOMPLETE"

        printfn
            "  %-4d  %-22s  %16s  %16s  %9.2f  %s"
            (i + 1)
            r.Couple.Name
            computed
            reference
            r.Couple.E0Prime
            (sourceLabel r))

    printfn ""

    ranked
    |> List.filter (fun r -> not r.Failures.IsEmpty)
    |> List.iter (fun r -> printfn "  %s: %s" r.Couple.Name (String.concat "; " (List.distinct r.Failures)))

    let complete = ranked |> List.filter (fun r -> r.ReductionEnergy.IsSome)
    let computedOrder = complete |> List.map (fun r -> r.Couple.Name)

    let biologicalOrder =
        complete
        |> List.sortBy (fun r -> r.Couple.E0Prime)
        |> List.map (fun r -> r.Couple.Name)

    if complete.Length > 1 then
        printfn "  Computed order:    %s" (String.concat " < " computedOrder)
        printfn "  Biological order:  %s (by E0')" (String.concat " < " biologicalOrder)

        printfn
            "  %s"
            (if computedOrder = biologicalOrder then
                 "The computed ranking reproduces the chain's order."
             else
                 "The computed ranking differs from the chain's order; see the notes below.")

    if ranked |> List.exists (fun r -> r.Sources |> List.contains EmpiricalHamiltonian) then
        ChemistryIntegrals.empiricalNote |> List.iter (printfn "%s")

    printfn ""
    printfn "  Notes: gas-phase reaction energies of small models, not solution potentials. STO-3G"
    printfn "  with a small active space misses measured reaction enthalpies (ref dH column) by tens"
    printfn "  of kcal/mol, and badly underestimates the energy released by the peroxide couple."
    printfn ""

printTable ()

if not quiet then
    printfn "  Quantum:  all VQE via IQuantumBackend [Rule 1 compliant]"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    ranked
    |> List.mapi (fun i r ->
        let value format =
            match r.ReductionEnergy with
            | Some dE -> format dE
            | None -> "INCOMPLETE"

        [
            "rank", string (i + 1)
            "couple", r.Couple.Name
            "biology", r.Couple.Biology
            "description", r.Couple.Description
            "reduction_energy_ha", value (sprintf "%.6f")
            "reduction_energy_kcal_mol", value (fun dE -> sprintf "%.2f" (dE * hartreeToKcalMol))
            "reference_dh_kcal_mol",
            r.Couple.ReferenceDeltaH
            |> Option.map (sprintf "%.1f")
            |> Option.defaultValue ""
            "e0_prime_v", $"%.3f{r.Couple.E0Prime}"
            "hamiltonian", sourceLabel r
        ]
        |> Map.ofList)

let header =
    [
        "rank"
        "couple"
        "biology"
        "description"
        "reduction_energy_ha"
        "reduction_energy_kcal_mol"
        "reference_dh_kcal_mol"
        "e0_prime_v"
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
    printfn "     --systems nad,ubiquinone          Run specific couples"
    printfn "     --fcidump-dir ./fcidumps          Your own FCIDUMP integrals"
    printfn "     --csv results.csv                 Export the table as CSV"
    printfn ""
