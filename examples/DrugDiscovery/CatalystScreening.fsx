// ==============================================================================
// Catalyst Screening for beta-Lactam Synthesis
// ==============================================================================
// Compares Lewis acid catalyst binding energies via VQE, ranking catalysts by
// their ability to stabilize transition states in antibiotic precursor synthesis.
//
// The question: "Which Lewis acid catalyst most strongly activates the carbonyl
// electrophile, and how does that translate to barrier reduction?"
//
// Lewis acids (electron acceptors) coordinate to carbonyl oxygen, stabilising
// the transition state and lowering activation barriers by 10-15 kcal/mol.
// Binding energy (E_complex - E_catalyst - E_substrate) is the screening metric:
// more negative = stronger binding = better catalysis.
//
// Accepts multiple catalysts (built-in presets or --input CSV), runs VQE on each
// (catalyst alone, substrate alone, catalyst-substrate complex = 2 VQE calls per
// catalyst plus 1 shared substrate call), then outputs a ranked comparison table.
//
// IMPORTANT LIMITATION:
// This example uses EMPIRICAL Hamiltonian coefficients (not molecular integrals).
// Calculated energies are ILLUSTRATIVE. For production use, molecular integral
// calculation (via PySCF, Psi4, or similar) would be needed.
//
// NISQ Constraints:
// Due to exponential scaling, we use minimal models:
//   - 2-4 atoms per molecule (4-8 qubits per fragment)
//   - Simplified catalyst models (metal + H for charge balance)
//   - Practical runtime: ~2-10 seconds per VQE calculation
//
// Usage:
//   dotnet fsi CatalystScreening.fsx
//   dotnet fsi CatalystScreening.fsx -- --help
//   dotnet fsi CatalystScreening.fsx -- --catalysts bf3,zncl2
//   dotnet fsi CatalystScreening.fsx -- --input catalysts.csv
//   dotnet fsi CatalystScreening.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Georg, G.I. "The Organic Chemistry of beta-Lactams" VCH Publishers (1993)
//   [2] Palomo, C. et al. "Asymmetric Synthesis of beta-Lactams" Chem. Rev. (2005)
//   [3] Wikipedia: Staudinger_reaction
//   [4] Wikipedia: Beta-lactam_antibiotic
// ==============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
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

Cli.exitIfHelp "CatalystScreening.fsx"
    "VQE screening of Lewis acid catalysts for beta-lactam synthesis"
    [ { Cli.OptionSpec.Name = "input"; Description = "CSV file with custom catalyst definitions"; Default = Some "built-in presets" }
      { Cli.OptionSpec.Name = "catalysts"; Description = "Comma-separated preset names to run (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "50" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Energy convergence tolerance (Hartree)"; Default = Some "1e-4" }
      { Cli.OptionSpec.Name = "temperature"; Description = "Industrial process temperature (K)"; Default = Some "310" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputFile = args |> Cli.tryGet "input"
let catalystFilter = args |> Cli.getCommaSeparated "catalysts"
let maxIterations = Cli.getIntOr "max-iterations" 50 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args
let temperature = Cli.getFloatOr "temperature" 310.0 args

// ==============================================================================
// TYPES
// ==============================================================================

/// A catalyst defined by its molecule model, Lewis acidity class, and industrial use.
type CatalystInfo =
    { Name: string
      Formula: string
      Molecule: Molecule
      LewisAcidity: string
      SelectivityNotes: string
      IndustrialUse: string }

/// Result of screening one catalyst via VQE binding energy calculation.
type CatalystResult =
    { Catalyst: CatalystInfo
      CatalystEnergy: float
      SubstrateEnergy: float
      ComplexEnergy: float
      BindingEnergyHartree: float
      BindingEnergyKcal: float
      EstimatedBarrierReduction: float
      EstimatedBarrier: float
      ComputeTimeSeconds: float
      HasVqeFailure: bool }

// ==============================================================================
// PHYSICAL CONSTANTS
// ==============================================================================

let hartreeToKcalMol = 627.509
let uncatalyzedBarrier = 30.0  // kcal/mol (literature estimate for Staudinger [2+2])

// ==============================================================================
// BUILT-IN CATALYST PRESETS
// ==============================================================================
// Each catalyst is modelled as a diatomic metal-hydride for NISQ tractability.
// The metal centre represents the Lewis acidic site; H balances charge.

/// Create a single-atom catalyst model (metal + H for charge balance).
let private createCatalyst element bondLength name formula acidity notes indUse : CatalystInfo =
    let mol : Molecule =
        { Name = sprintf "%s (model)" element
          Atoms =
            [ { Element = element; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (bondLength, 0.0, 0.0) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }
    { Name = name
      Formula = formula
      Molecule = mol
      LewisAcidity = acidity
      SelectivityNotes = notes
      IndustrialUse = indUse }

/// H2 baseline â€” no catalyst.
let private noCatalystPreset : CatalystInfo =
    { Name = "No Catalyst (Baseline)"
      Formula = "H2"
      Molecule =
        { Name = "H2"
          Atoms =
            [ { Element = "H"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (0.74, 0.0, 0.0) } ]
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }
      LewisAcidity = "None"
      SelectivityNotes = "Uncatalyzed reaction"
      IndustrialUse = "Baseline comparison" }

/// BH â€” Boron hydride model for BF3.
let private bf3Preset =
    createCatalyst "B" 1.23 "Boron (BF3 model)" "BF3" "Strong"
        "Highly reactive, may cause side reactions" "Staudinger synthesis"

/// AlH â€” Aluminum hydride model for AlCl3.
let private alcl3Preset =
    createCatalyst "Al" 1.65 "Aluminum (AlCl3 model)" "AlCl3" "Strong"
        "Classical Friedel-Crafts, can polymerize" "Alkylation, acylation"

/// ZnH â€” Zinc hydride model for ZnCl2.
let private zncl2Preset =
    createCatalyst "Zn" 1.54 "Zinc (ZnCl2 model)" "ZnCl2" "Moderate"
        "Milder, better selectivity, biocompatible" "Organic synthesis"

/// TiH â€” Titanium hydride model for TiCl4.
let private ticl4Preset =
    createCatalyst "Ti" 1.78 "Titanium (TiCl4 model)" "TiCl4" "Strong"
        "Oxophilic, excellent for carbonyls" "Ziegler-Natta catalysis"

/// All built-in presets keyed by lowercase formula.
let private builtinPresets : Map<string, CatalystInfo> =
    [ noCatalystPreset; bf3Preset; alcl3Preset; zncl2Preset; ticl4Preset ]
    |> List.map (fun c -> c.Formula.ToLowerInvariant(), c)
    |> Map.ofList

let private presetNames =
    builtinPresets |> Map.toList |> List.map fst |> String.concat ", "

// ==============================================================================
// CSV INPUT PARSING
// ==============================================================================

/// Parse atom list from compact string format:
///   "B:0,0,0|H:1.23,0,0"
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

/// Load catalysts from a CSV file.
/// Expected columns: name, formula, lewis_acidity, selectivity, industrial_use, atoms
/// OR: name, preset (to reference a built-in preset by name)
let private loadCatalystsFromCsv (path: string) : CatalystInfo list =
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
            | Some cat -> Some { cat with Name = name }
            | None ->
                if not quiet then
                    eprintfn "  Warning: unknown preset '%s' (available: %s)" presetKey presetNames
                None
        | None ->
            match get "atoms" with
            | Some atomStr ->
                let formula = get "formula" |> Option.defaultValue name
                let acidity = get "lewis_acidity" |> Option.defaultValue "Unknown"
                let selectivity = get "selectivity" |> Option.defaultValue ""
                let indUse = get "industrial_use" |> Option.defaultValue ""
                let mol = moleculeFromAtomString name atomStr
                Some
                    { Name = name
                      Formula = formula
                      Molecule = mol
                      LewisAcidity = acidity
                      SelectivityNotes = selectivity
                      IndustrialUse = indUse }
            | None ->
                if not quiet then
                    eprintfn "  Warning: row '%s' missing required 'atoms' column" name
                None)

// ==============================================================================
// CATALYST SELECTION
// ==============================================================================

let catalysts : CatalystInfo list =
    let allCatalysts =
        match inputFile with
        | Some path ->
            let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
            if not quiet then
                printfn "Loading catalysts from: %s" resolved
            loadCatalystsFromCsv resolved
        | None ->
            builtinPresets |> Map.toList |> List.map snd

    match catalystFilter with
    | [] -> allCatalysts
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        allCatalysts
        |> List.filter (fun c ->
            let key = c.Formula.ToLowerInvariant()
            let nameKey = c.Name.ToLowerInvariant()
            filterSet |> Set.exists (fun fil -> key.Contains fil || nameKey.Contains fil))

if List.isEmpty catalysts then
    eprintfn "Error: No catalysts selected. Available presets: %s" presetNames
    exit 1

// ==============================================================================
// SUBSTRATE: CO (model carbonyl electrophile)
// ==============================================================================

let carbonyl : Molecule =
    { Name = "CO (carbonyl model)"
      Atoms =
        [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
          { Element = "O"; Position = (1.13, 0.0, 0.0) } ]
      Bonds =
        [ { Atom1 = 0; Atom2 = 1; BondOrder = 3.0 } ]
      Charge = 0; Multiplicity = 1 }

// ==============================================================================
// QUANTUM BACKEND (Rule 1: all VQE via IQuantumBackend)
// ==============================================================================

let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Catalyst Screening for beta-Lactam Synthesis"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:      %s" backend.Name
    printfn "  Catalysts:    %d" catalysts.Length
    printfn "  Temperature:  %.1f K" temperature
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

/// Unwrap energy result, tracking failure.
let private unwrapEnergy (res: Result<float, string>) (anyFailure: byref<bool>) : float =
    match res with
    | Ok e -> e
    | Error _ ->
        anyFailure <- true
        0.0

/// Screen one catalyst: compute catalyst energy, complex energy, derive binding energy.
let private screenCatalyst
    (backend: IQuantumBackend)
    (maxIter: int)
    (tol: float)
    (substrateEnergy: float)
    (substrateOk: bool)
    (idx: int)
    (total: int)
    (catInfo: CatalystInfo)
    : CatalystResult =

    if not quiet then
        printfn "  [%d/%d] %s (%s)" (idx + 1) total catInfo.Name catInfo.Formula
        printfn "         Lewis acidity: %s" catInfo.LewisAcidity

    let mutable anyFailure = not substrateOk

    // Catalyst energy
    let (catRes, catTime) = computeEnergy backend maxIter tol catInfo.Molecule
    let catEnergy = unwrapEnergy catRes &anyFailure

    if not quiet then
        match catRes with
        | Ok e -> printfn "         E_cat = %.6f Ha  (%.1fs)" e catTime
        | Error _ -> printfn "         E_cat = FAILED  (%.1fs)" catTime

    // Catalyst-substrate complex (offset substrate by 3 A)
    let complex : Molecule =
        { Name = sprintf "%s + CO" catInfo.Formula
          Atoms =
            catInfo.Molecule.Atoms @
            (carbonyl.Atoms |> List.map (fun a ->
                let (x, y, z) = a.Position
                { a with Position = (x + 3.0, y, z) }))
          Bonds =
            catInfo.Molecule.Bonds @
            (carbonyl.Bonds |> List.map (fun b ->
                { b with
                    Atom1 = b.Atom1 + catInfo.Molecule.Atoms.Length
                    Atom2 = b.Atom2 + catInfo.Molecule.Atoms.Length }))
          Charge = 0; Multiplicity = 1 }

    let (complexRes, complexTime) = computeEnergy backend maxIter tol complex
    let complexEnergy = unwrapEnergy complexRes &anyFailure

    if not quiet then
        match complexRes with
        | Ok e -> printfn "         E_cpx = %.6f Ha  (%.1fs)" e complexTime
        | Error _ -> printfn "         E_cpx = FAILED  (%.1fs)" complexTime

    // Binding energy
    let bindingHartree =
        if anyFailure then 0.0
        else complexEnergy - catEnergy - substrateEnergy
    let bindingKcal = bindingHartree * hartreeToKcalMol

    // Estimated barrier reduction
    let reduction =
        if anyFailure then 0.0
        elif bindingKcal < -50.0 then 15.0
        elif bindingKcal < -20.0 then 12.0
        elif bindingKcal < -5.0 then 8.0
        else 0.0
    let barrier = uncatalyzedBarrier - reduction

    if not quiet then
        if not anyFailure then
            printfn "         E_bind = %.4f Ha (%.1f kcal/mol)" bindingHartree bindingKcal
        printfn ""

    { Catalyst = catInfo
      CatalystEnergy = catEnergy
      SubstrateEnergy = substrateEnergy
      ComplexEnergy = complexEnergy
      BindingEnergyHartree = bindingHartree
      BindingEnergyKcal = bindingKcal
      EstimatedBarrierReduction = reduction
      EstimatedBarrier = barrier
      ComputeTimeSeconds = catTime + complexTime
      HasVqeFailure = anyFailure }

// --- Compute substrate energy once ---

if not quiet then
    printfn "Computing substrate energy (CO carbonyl model)..."

let (substrateRes, substrateTime) = computeEnergy backend maxIterations tolerance carbonyl
let substrateOk = Result.isOk substrateRes
let substrateEnergy =
    match substrateRes with
    | Ok e ->
        if not quiet then printfn "  E_substrate = %.6f Ha  (%.1fs)" e substrateTime
        e
    | Error _ ->
        if not quiet then printfn "  E_substrate = FAILED  (%.1fs)" substrateTime
        0.0

if not quiet then printfn ""

// --- Screen all catalysts ---

if not quiet then
    printfn "Screening catalysts..."
    printfn ""

let results =
    catalysts
    |> List.mapi (fun i cat ->
        screenCatalyst backend maxIterations tolerance substrateEnergy substrateOk i catalysts.Length cat)

// Sort: most negative binding energy first (strongest binder). Failed â†’ bottom.
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
    printfn "  Ranked Catalysts (by binding energy)"
    printfn "=================================================================="
    printfn ""
    printfn "  %-4s  %-22s  %-10s  %14s  %14s  %12s  %10s"
        "#" "Catalyst" "Acidity" "E_bind (Ha)" "E_bind (kcal)" "Est.Ea (kcal)" "Time (s)"
    printfn "  %s" (String('=', 100))

    ranked
    |> List.iteri (fun i r ->
        if r.HasVqeFailure then
            printfn "  %-4d  %-22s  %-10s  %14s  %14s  %12s  %10.1f"
                (i + 1)
                (sprintf "%s (%s)" r.Catalyst.Name r.Catalyst.Formula)
                r.Catalyst.LewisAcidity
                "FAILED"
                "FAILED"
                "FAILED"
                r.ComputeTimeSeconds
        else
            printfn "  %-4d  %-22s  %-10s  %14.6f  %14.2f  %12.1f  %10.1f"
                (i + 1)
                (sprintf "%s (%s)" r.Catalyst.Name r.Catalyst.Formula)
                r.Catalyst.LewisAcidity
                r.BindingEnergyHartree
                r.BindingEnergyKcal
                r.EstimatedBarrier
                r.ComputeTimeSeconds)

    printfn ""

    // Selectivity notes sub-table
    printfn "  %-4s  %-22s  %s"
        "#" "Catalyst" "Selectivity / Industrial Use"
    printfn "  %s" (String('-', 75))

    ranked
    |> List.iteri (fun i r ->
        printfn "  %-4d  %-22s  %s / %s"
            (i + 1)
            (sprintf "%s" r.Catalyst.Formula)
            r.Catalyst.SelectivityNotes
            r.Catalyst.IndustrialUse)

    printfn ""

// Always print the ranked comparison table â€” that's the primary output of this tool,
// even in --quiet mode (which only suppresses per-catalyst progress output).
printTable ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    let successful = ranked |> List.filter (fun r -> not r.HasVqeFailure)
    match successful with
    | best :: _ ->
        let totalTime = (results |> List.sumBy (fun r -> r.ComputeTimeSeconds)) + substrateTime
        printfn "  Best catalyst:  %s (%s, E_bind = %.2f kcal/mol)"
            best.Catalyst.Name best.Catalyst.Formula best.BindingEnergyKcal
        printfn "  Total time:     %.1f seconds" totalTime
        printfn "  Quantum:        all VQE via IQuantumBackend [Rule 1 compliant]"
        printfn ""
    | [] ->
        printfn "  All catalyst screenings failed VQE computation."
        printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    ranked
    |> List.mapi (fun i r ->
        [ "rank", string (i + 1)
          "catalyst", r.Catalyst.Formula
          "catalyst_name", r.Catalyst.Name
          "lewis_acidity", r.Catalyst.LewisAcidity
          "selectivity", r.Catalyst.SelectivityNotes
          "industrial_use", r.Catalyst.IndustrialUse
          "catalyst_energy_hartree", (if r.HasVqeFailure then "FAILED" else sprintf "%.6f" r.CatalystEnergy)
          "substrate_energy_hartree", (if r.HasVqeFailure then "FAILED" else sprintf "%.6f" r.SubstrateEnergy)
          "complex_energy_hartree", (if r.HasVqeFailure then "FAILED" else sprintf "%.6f" r.ComplexEnergy)
          "binding_energy_hartree", (if r.HasVqeFailure then "FAILED" else sprintf "%.6f" r.BindingEnergyHartree)
          "binding_energy_kcal_mol", (if r.HasVqeFailure then "FAILED" else sprintf "%.2f" r.BindingEnergyKcal)
          "estimated_barrier_reduction_kcal", (if r.HasVqeFailure then "FAILED" else sprintf "%.1f" r.EstimatedBarrierReduction)
          "estimated_barrier_kcal", (if r.HasVqeFailure then "FAILED" else sprintf "%.1f" r.EstimatedBarrier)
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
        [ "rank"; "catalyst"; "catalyst_name"; "lewis_acidity"; "selectivity"
          "industrial_use"; "catalyst_energy_hartree"; "substrate_energy_hartree"
          "complex_energy_hartree"; "binding_energy_hartree"; "binding_energy_kcal_mol"
          "estimated_barrier_reduction_kcal"; "estimated_barrier_kcal"
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
    printfn "     --catalysts bf3,zncl2                   Run specific catalysts"
    printfn "     --input catalysts.csv                   Load custom catalysts from CSV"
    printfn "     --csv results.csv                       Export ranked table as CSV"
    printfn ""
