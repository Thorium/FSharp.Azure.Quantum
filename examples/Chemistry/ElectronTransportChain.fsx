// ==============================================================================
// Electron Transport Chain — Redox Pair Comparison
// ==============================================================================
// Compares electron transfer energetics across multiple redox pairs using VQE,
// ranking them by Marcus theory electron transfer rate.
//
// For each redox pair, VQE computes ground state energies of the reduced and
// oxidized forms. The ionization energy (IE = E_ox - E_red) feeds into Marcus
// theory to predict ET rate constants. The comparison reveals which electron
// transfer step is kinetically fastest under given conditions.
//
// Biological context: the mitochondrial respiratory chain shuttles electrons
// through Fe2+/Fe3+ cytochromes, Fe-S clusters, and quinones. Real heme-iron
// complexes are far too large for NISQ hardware, so we model the electron
// transfer concept using small (<=4 atom) molecules whose ionization mirrors
// the 1-electron redox process in biology.
//
// IMPORTANT LIMITATION:
// This example uses EMPIRICAL Hamiltonian coefficients (not molecular integrals).
// Calculated energies are ILLUSTRATIVE. For production use, molecular integral
// calculation (via PySCF, Psi4, or similar) would be needed.
//
// Usage:
//   dotnet fsi ElectronTransportChain.fsx
//   dotnet fsi ElectronTransportChain.fsx -- --help
//   dotnet fsi ElectronTransportChain.fsx -- --systems lih,hf
//   dotnet fsi ElectronTransportChain.fsx -- --input redox-pairs.csv
//   dotnet fsi ElectronTransportChain.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Marcus, R.A. "Electron transfer reactions in chemistry" Rev. Mod. Phys. (1993)
//   [2] Gray & Winkler "Electron tunneling through proteins" Q. Rev. Biophys. (2003)
//   [3] Harper's Illustrated Biochemistry, 28th Ed., Chapters 12-13
//   [4] Wikipedia: Electron_transport_chain
//   [5] Wikipedia: Marcus_theory
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

Cli.exitIfHelp "ElectronTransportChain.fsx"
    "Compare redox pair electron transfer rates via VQE + Marcus theory"
    [ { Cli.OptionSpec.Name = "input"; Description = "CSV file with custom redox pairs"; Default = Some "built-in presets" }
      { Cli.OptionSpec.Name = "systems"; Description = "Comma-separated preset names to run (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "50" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Energy convergence tolerance (Hartree)"; Default = Some "1e-4" }
      { Cli.OptionSpec.Name = "temperature"; Description = "Temperature in Kelvin"; Default = Some "310" }
      { Cli.OptionSpec.Name = "lambda"; Description = "Reorganization energy (eV)"; Default = Some "0.7" }
      { Cli.OptionSpec.Name = "coupling"; Description = "Electronic coupling H_AB (eV)"; Default = Some "0.01" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputFile = args |> Cli.tryGet "input"
let systemFilter = args |> Cli.getCommaSeparated "systems"
let maxIterations = Cli.getIntOr "max-iterations" 50 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args
let temperature = Cli.getFloatOr "temperature" 310.0 args
let reorganizationEnergy_eV = Cli.getFloatOr "lambda" 0.7 args
let electronicCoupling_eV = Cli.getFloatOr "coupling" 0.01 args

// ==============================================================================
// TYPES
// ==============================================================================

/// A redox pair: a molecule in its reduced (neutral) and oxidized (cation) forms.
/// VQE computes both states; the ionization energy feeds Marcus theory.
type RedoxPair =
    { Name: string
      ReducedMolecule: Molecule
      OxidizedMolecule: Molecule
      BiologicalAnalogue: string
      Description: string }

/// Result of computing one redox pair's energetics via VQE.
type RedoxResult =
    { Pair: RedoxPair
      ReducedEnergy: float
      OxidizedEnergy: float
      IonizationEnergyHartree: float
      IonizationEnergyEv: float
      MarcusRate: float
      HalfLife: string
      RateAssessment: string
      ComputeTimeSeconds: float
      HasVqeFailure: bool }

// ==============================================================================
// PHYSICAL CONSTANTS
// ==============================================================================

let kB = 1.380649e-23           // Boltzmann constant (J/K)
let hbar = 1.054571817e-34      // Reduced Planck constant (J*s)
let eV_to_J = 1.60218e-19       // eV to Joules
let hartreeToEV = 27.2114        // 1 Hartree = 27.2114 eV
let hartreeToKcalMol = 627.509   // 1 Hartree in kcal/mol

// ==============================================================================
// BUILT-IN REDOX PAIR PRESETS
// ==============================================================================
// Each pair models a 1-electron oxidation: neutral molecule → cation + e-.
// Real ETC carriers (cytochrome Fe2+→Fe3+, ubiquinone, Fe-S clusters) are too
// large for NISQ. These small-molecule analogues capture the same physics:
// VQE on two charge states to get the ionization energy.

/// LiH: simplest heteronuclear diatomic. Ionization removes the valence
/// electron from the Li-H bond, modelling 1-electron metal-ligand redox
/// (analogous to Fe-N bond in cytochrome heme).
let private lihPair : RedoxPair =
    let reduced : Molecule =
        { Name = "LiH (neutral)"
          Atoms =
            [ { Element = "Li"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (1.60, 0.0, 0.0) } ]   // Li-H bond ~1.60 A
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let oxidized : Molecule =
        { Name = "LiH+ (cation)"
          Atoms = reduced.Atoms
          Bonds = reduced.Bonds
          Charge = 1; Multiplicity = 2 }

    { Name = "LiH"
      ReducedMolecule = reduced
      OxidizedMolecule = oxidized
      BiologicalAnalogue = "Metal-ligand bond (Fe-N in heme)"
      Description = "Lithium hydride ionization — metal-ligand 1e- transfer model" }

/// HF: strongly polar bond with high ionization energy. Models electron
/// transfer in high-potential carriers like cytochrome a3 (E0 = +0.55 V).
let private hfPair : RedoxPair =
    let reduced : Molecule =
        { Name = "HF (neutral)"
          Atoms =
            [ { Element = "H"; Position = (0.0, 0.0, 0.0) }
              { Element = "F"; Position = (0.92, 0.0, 0.0) } ]   // H-F bond ~0.92 A
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let oxidized : Molecule =
        { Name = "HF+ (cation)"
          Atoms = reduced.Atoms
          Bonds = reduced.Bonds
          Charge = 1; Multiplicity = 2 }

    { Name = "HF"
      ReducedMolecule = reduced
      OxidizedMolecule = oxidized
      BiologicalAnalogue = "High-potential carrier (cyt a3, E0 +0.55V)"
      Description = "Hydrogen fluoride ionization — high-potential 1e- transfer model" }

/// H2: homonuclear diatomic, lowest ionization energy among presets.
/// Models low-potential carriers like NADH (E0 = -0.32 V) where electrons
/// are easily donated.
let private h2Pair : RedoxPair =
    let reduced : Molecule =
        { Name = "H2 (neutral)"
          Atoms =
            [ { Element = "H"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (0.74, 0.0, 0.0) } ]   // H-H bond ~0.74 A
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let oxidized : Molecule =
        { Name = "H2+ (cation)"
          Atoms = reduced.Atoms
          Bonds = reduced.Bonds
          Charge = 1; Multiplicity = 2 }

    { Name = "H2"
      ReducedMolecule = reduced
      OxidizedMolecule = oxidized
      BiologicalAnalogue = "Low-potential donor (NADH, E0 -0.32V)"
      Description = "Hydrogen ionization — low-potential 1e- donor model" }

/// H2O: lone-pair ionization removes a non-bonding electron. Models
/// water oxidation at Complex IV (the terminal step: 2H2O → O2 + 4H+ + 4e-).
let private h2oPair : RedoxPair =
    let reduced : Molecule =
        { Name = "H2O (neutral)"
          Atoms =
            [ { Element = "O"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (0.96, 0.0, 0.0) }       // O-H bond ~0.96 A
              { Element = "H"; Position = (-0.24, 0.93, 0.0) } ]   // H-O-H angle ~104.5
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let oxidized : Molecule =
        { Name = "H2O+ (cation)"
          Atoms = reduced.Atoms
          Bonds = reduced.Bonds
          Charge = 1; Multiplicity = 2 }

    { Name = "H2O"
      ReducedMolecule = reduced
      OxidizedMolecule = oxidized
      BiologicalAnalogue = "Water oxidation (Complex IV terminal step)"
      Description = "Water ionization — lone-pair 1e- removal model" }

/// All built-in presets keyed by lowercase name.
let private builtinPresets : Map<string, RedoxPair> =
    [ lihPair; hfPair; h2Pair; h2oPair ]
    |> List.map (fun p -> p.Name.ToLowerInvariant(), p)
    |> Map.ofList

let private presetNames =
    builtinPresets |> Map.toList |> List.map fst |> String.concat ", "

// ==============================================================================
// CSV INPUT PARSING
// ==============================================================================

/// Parse atom list from compact string format:
///   "Li:0,0,0|H:1.6,0,0"
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
let private moleculeFromAtomString (name: string) (atomStr: string) (charge: int) (mult: int) : Molecule =
    let atoms = parseAtoms atomStr
    { Name = name
      Atoms = atoms
      Bonds = inferBonds atoms
      Charge = charge
      Multiplicity = mult }

/// Load redox pairs from a CSV file.
/// Expected columns: name, biological_analogue, description, atoms, charge_oxidized, multiplicity_oxidized
/// OR: name, preset (to reference a built-in preset by name)
let private loadPairsFromCsv (path: string) : RedoxPair list =
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
            | Some pair -> Some { pair with Name = name }
            | None ->
                if not quiet then
                    eprintfn "  Warning: unknown preset '%s' (available: %s)" presetKey presetNames
                None
        | None ->
            match get "atoms" with
            | Some atomStr ->
                let bio = get "biological_analogue" |> Option.defaultValue ""
                let desc = get "description" |> Option.defaultValue ""
                let chargeOx = get "charge_oxidized" |> Option.bind (fun s -> match Int32.TryParse s with true, v -> Some v | _ -> None) |> Option.defaultValue 1
                let multOx = get "multiplicity_oxidized" |> Option.bind (fun s -> match Int32.TryParse s with true, v -> Some v | _ -> None) |> Option.defaultValue 2
                let reduced = moleculeFromAtomString (name + " (neutral)") atomStr 0 1
                let oxidized = moleculeFromAtomString (name + " (cation)") atomStr chargeOx multOx
                Some
                    { Name = name
                      ReducedMolecule = reduced
                      OxidizedMolecule = oxidized
                      BiologicalAnalogue = bio
                      Description = desc }
            | None ->
                if not quiet then
                    eprintfn "  Warning: row '%s' missing required 'atoms' column" name
                None)

// ==============================================================================
// SYSTEM SELECTION
// ==============================================================================

let systems : RedoxPair list =
    let allSystems =
        match inputFile with
        | Some path ->
            let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
            if not quiet then
                printfn "Loading redox pairs from: %s" resolved
            loadPairsFromCsv resolved
        | None ->
            builtinPresets |> Map.toList |> List.map snd

    match systemFilter with
    | [] -> allSystems
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        allSystems
        |> List.filter (fun p ->
            let key = p.Name.ToLowerInvariant()
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
    printfn "  Electron Transport Chain: Redox Pair Comparison"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:      %s" backend.Name
    printfn "  Systems:      %d" systems.Length
    printfn "  VQE iters:    %d (tol: %g Ha)" maxIterations tolerance
    printfn "  Temperature:  %.1f K" temperature
    printfn "  Lambda:       %.2f eV  |  H_AB: %.3f eV" reorganizationEnergy_eV electronicCoupling_eV
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

/// Marcus theory ET rate: k = (2pi/hbar) |H_AB|^2 (4pi*lambda*kT)^(-1/2) exp(-(dG+lambda)^2 / 4*lambda*kT).
/// Here dG is the ionization energy used as the driving force.
let private marcusRate (dG_eV: float) (lambda_eV: float) (coupling_eV: float) (tempK: float) : float =
    let lambda_J = lambda_eV * eV_to_J
    let hab_J = coupling_eV * eV_to_J
    let dg_J = dG_eV * eV_to_J
    let kBT = kB * tempK
    let prefactor = 2.0 * Math.PI / hbar * hab_J * hab_J
    let density = 1.0 / sqrt(4.0 * Math.PI * lambda_J * kBT)
    let exponent = -((dg_J + lambda_J) ** 2.0) / (4.0 * lambda_J * kBT)
    prefactor * density * exp(exponent)

/// Format a half-life from a rate constant.
let private formatHalfLife (k: float) : string =
    if k > 1e-30 && k < 1e30 then
        let hl = 0.693 / k
        if hl < 1e-9 then sprintf "%.1e ns" (hl * 1e9)
        elif hl < 1e-6 then sprintf "%.1e us" (hl * 1e6)
        elif hl < 1e-3 then sprintf "%.1e ms" (hl * 1e3)
        elif hl < 1.0 then sprintf "%.2f s" hl
        elif hl < 60.0 then sprintf "%.1f s" hl
        elif hl < 3600.0 then sprintf "%.1f min" (hl / 60.0)
        else sprintf "%.1f h" (hl / 3600.0)
    else "N/A"

/// Interpret an ET rate.
let private assessRate (k: float) : string =
    if k <= 0.0 || k > 1e30 then "Invalid"
    elif k > 1e12 then "Ultrafast (sub-ps)"
    elif k > 1e9 then "Very fast (ns)"
    elif k > 1e6 then "Fast (us)"
    elif k > 1e3 then "Moderate (ms)"
    elif k > 1.0 then "Slow (s)"
    else "Very slow"

/// Compute the full redox energetics for one pair.
let private computeSystem
    (backend: IQuantumBackend)
    (maxIter: int)
    (tol: float)
    (lambda_eV: float)
    (coupling_eV: float)
    (tempK: float)
    (idx: int)
    (total: int)
    (pair: RedoxPair)
    : RedoxResult =
    if not quiet then
        printfn "  [%d/%d] %s" (idx + 1) total pair.Name
        printfn "         %s" pair.Description

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

    let (redE, _) = unwrapEnergy "reduced" pair.ReducedMolecule.Name (computeEnergy backend maxIter tol pair.ReducedMolecule)
    let (oxE, _) = unwrapEnergy "oxidized" pair.OxidizedMolecule.Name (computeEnergy backend maxIter tol pair.OxidizedMolecule)

    let totalTime = (DateTime.Now - startTime).TotalSeconds

    // Ionization energy
    let ieHartree = oxE - redE
    let ieEv = ieHartree * hartreeToEV

    // Marcus rate using IE as driving force (negative = spontaneous electron loss)
    let drivingForce_eV = -abs(ieEv)   // Electron transfer is thermodynamically driven
    let rate = marcusRate drivingForce_eV lambda_eV coupling_eV tempK

    if not quiet then
        if anyFailure then
            printfn "         => INCOMPLETE (VQE failure — energies are unreliable)"
        else
            printfn "         => IE = %.4f eV  |  k_ET = %.2e /s" ieEv rate
        printfn ""

    { Pair = pair
      ReducedEnergy = redE
      OxidizedEnergy = oxE
      IonizationEnergyHartree = ieHartree
      IonizationEnergyEv = ieEv
      MarcusRate = rate
      HalfLife = if anyFailure then "N/A" else formatHalfLife rate
      RateAssessment = if anyFailure then "VQE FAILED" else assessRate rate
      ComputeTimeSeconds = totalTime
      HasVqeFailure = anyFailure }

// --- Run all systems ---

if not quiet then
    printfn "Computing redox pair energies..."
    printfn ""

let results =
    systems
    |> List.mapi (fun i pair ->
        computeSystem backend maxIterations tolerance reorganizationEnergy_eV electronicCoupling_eV temperature i systems.Length pair)

// Sort by Marcus rate descending (fastest ET first).
// Failed systems sink to bottom.
let ranked =
    results
    |> List.sortBy (fun r ->
        if r.HasVqeFailure then (2, infinity)
        else (0, -r.MarcusRate))

// ==============================================================================
// RANKED COMPARISON TABLE
// ==============================================================================

let printTable () =
    printfn "=================================================================="
    printfn "  Ranked Redox Pairs (by Marcus ET rate)"
    printfn "=================================================================="
    printfn ""
    printfn "  %-4s  %-10s  %12s  %12s  %10s  %s"
        "#" "System" "IE (eV)" "Rate (/s)" "Half-life" "Assessment"
    printfn "  %s" (String('=', 78))

    ranked
    |> List.iteri (fun i r ->
        if r.HasVqeFailure then
            printfn "  %-4d  %-10s  %12s  %12s  %10s  %s"
                (i + 1) r.Pair.Name "FAILED" "FAILED" "N/A" "VQE FAILED"
        else
            printfn "  %-4d  %-10s  %12.4f  %12.2e  %10s  %s"
                (i + 1) r.Pair.Name r.IonizationEnergyEv r.MarcusRate r.HalfLife r.RateAssessment)

    printfn ""

    // Biological analogues
    printfn "  %-4s  %-10s  %11s  %s"
        "#" "System" "Time (s)" "Biological Analogue"
    printfn "  %s" (String('-', 70))

    ranked
    |> List.iteri (fun i r ->
        printfn "  %-4d  %-10s  %11.1f  %s"
            (i + 1) r.Pair.Name r.ComputeTimeSeconds r.Pair.BiologicalAnalogue)

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
        printfn "  Fastest ET:    %s (k = %.2e /s, IE = %.4f eV)"
            best.Pair.Name best.MarcusRate best.IonizationEnergyEv
        printfn "  Total time:    %.1f seconds" totalTime
        printfn "  Marcus params: lambda = %.2f eV, H_AB = %.3f eV, T = %.1f K"
            reorganizationEnergy_eV electronicCoupling_eV temperature
        printfn "  Quantum:       all VQE via IQuantumBackend [Rule 1 compliant]"
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
          "system", r.Pair.Name
          "biological_analogue", r.Pair.BiologicalAnalogue
          "description", r.Pair.Description
          "reduced_energy_ha", (if r.HasVqeFailure then "FAILED" else sprintf "%.6f" r.ReducedEnergy)
          "oxidized_energy_ha", (if r.HasVqeFailure then "FAILED" else sprintf "%.6f" r.OxidizedEnergy)
          "ie_hartree", (if r.HasVqeFailure then "FAILED" else sprintf "%.6f" r.IonizationEnergyHartree)
          "ie_ev", (if r.HasVqeFailure then "FAILED" else sprintf "%.4f" r.IonizationEnergyEv)
          "marcus_rate_per_s", (if r.HasVqeFailure then "FAILED" else sprintf "%.2e" r.MarcusRate)
          "half_life", r.HalfLife
          "rate_assessment", r.RateAssessment
          "lambda_ev", sprintf "%.2f" reorganizationEnergy_eV
          "coupling_ev", sprintf "%.3f" electronicCoupling_eV
          "temperature_k", sprintf "%.1f" temperature
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
        [ "rank"; "system"; "biological_analogue"; "description"
          "reduced_energy_ha"; "oxidized_energy_ha"; "ie_hartree"; "ie_ev"
          "marcus_rate_per_s"; "half_life"; "rate_assessment"
          "lambda_ev"; "coupling_ev"; "temperature_k"
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
    printfn "     --systems lih,hf                Run specific redox pairs"
    printfn "     --input redox-pairs.csv         Load custom pairs from CSV"
    printfn "     --lambda 1.0 --coupling 0.05    Adjust Marcus theory parameters"
    printfn "     --csv results.csv               Export ranked table as CSV"
    printfn ""
