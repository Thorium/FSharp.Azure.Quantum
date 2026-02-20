// ==============================================================================
// Antibiotic Precursor Synthesis - Alternative Route Discovery
// ==============================================================================
// Compares beta-lactam synthesis routes by computing VQE activation energies.
//
// Accepts multiple synthesis routes (built-in presets or --input CSV), runs VQE
// on each route's reactant/TS/product molecules, and outputs a ranked comparison
// table showing which route has the lowest activation barrier.
//
// Background:
// China controls ~90% of global 6-APA/7-ACA production (key antibiotic
// intermediates). Quantum chemistry can discover alternative synthesis routes
// by accurately calculating activation energies for transition states —
// a problem where classical DFT has systematic errors of 3-5 kcal/mol for
// strained ring systems like beta-lactams (~27 kcal/mol ring strain).
//
// IMPORTANT LIMITATION:
// This example uses EMPIRICAL Hamiltonian coefficients (not molecular integrals).
// Calculated energies are ILLUSTRATIVE. For production use, molecular integral
// calculation (via PySCF, Psi4, or similar) would be needed.
// See: https://qiskit.org/documentation/nature/
//
// Usage:
//   dotnet fsi AntibioticPrecursorSynthesis.fsx
//   dotnet fsi AntibioticPrecursorSynthesis.fsx -- --help
//   dotnet fsi AntibioticPrecursorSynthesis.fsx -- --routes staudinger,ring-expansion
//   dotnet fsi AntibioticPrecursorSynthesis.fsx -- --input routes.csv
//   dotnet fsi AntibioticPrecursorSynthesis.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Wikipedia: beta-Lactam (https://en.wikipedia.org/wiki/Beta-lactam)
//   [2] Wikipedia: Cephalosporin (https://en.wikipedia.org/wiki/Cephalosporin)
//   [3] Staudinger, H. "Zur Kenntniss der Ketene" Liebigs Ann. Chem. (1907)
//   [4] Reiher, M. et al. "Elucidating reaction mechanisms on quantum computers" PNAS (2017)
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

Cli.exitIfHelp "AntibioticPrecursorSynthesis.fsx"
    "Compare beta-lactam synthesis routes by VQE activation energy"
    [ { Cli.OptionSpec.Name = "input"; Description = "CSV file with custom synthesis routes"; Default = Some "built-in presets" }
      { Cli.OptionSpec.Name = "routes"; Description = "Comma-separated preset names to run (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "50" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Energy convergence tolerance (Hartree)"; Default = Some "1e-4" }
      { Cli.OptionSpec.Name = "temperature"; Description = "Temperature for rate calculations (Kelvin)"; Default = Some "310" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputFile = args |> Cli.tryGet "input"
let routeFilter = args |> Cli.getCommaSeparated "routes"
let maxIterations = Cli.getIntOr "max-iterations" 50 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args
let temperature = Cli.getFloatOr "temperature" 310.0 args

// ==============================================================================
// TYPES
// ==============================================================================

/// A synthesis route defined by its three molecular species along the
/// reaction coordinate: separated reactants, transition state, and product.
type SynthesisRoute =
    { Name: string
      Reactants: Molecule list
      TransitionState: Molecule
      Product: Molecule
      Description: string }

/// Result of computing a single route's energy profile via VQE.
type RouteResult =
    { Route: SynthesisRoute
      ReactantEnergy: float
      TsEnergy: float
      ProductEnergy: float
      ActivationEnergyHartree: float
      ActivationEnergyKcal: float
      ReactionEnergyKcal: float
      RateConstant: float
      HalfLife: string
      BarrierAssessment: string
      ComputeTimeSeconds: float
      /// True if any VQE computation in this route returned an error.
      HasVqeFailure: bool }

// ==============================================================================
// PHYSICAL CONSTANTS
// ==============================================================================

let kB = 1.380649e-23           // Boltzmann constant (J/K)
let hPlanck = 6.62607015e-34    // Planck constant (J*s)
let gasR = 8.314                // Gas constant (J/(mol*K))
let hartreeToKcalMol = 627.509  // 1 Hartree in kcal/mol
let hartreeToKJMol = 2625.5     // 1 Hartree in kJ/mol

// ==============================================================================
// BUILT-IN ROUTE PRESETS
// ==============================================================================
// Each route models a different approach to beta-lactam ring formation using
// NISQ-tractable model molecules (<=5 atoms per species, <=10 qubits).

// --- Shared molecules used by multiple routes ---

/// Formaldehyde (H2C=O) — used as ketene-analogue reactant in Staudinger
/// and Lewis acid routes.
let private formaldehyde : Molecule =
    { Name = "Formaldehyde (H2C=O)"
      Atoms =
        [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
          { Element = "O"; Position = (0.0, 0.0, 1.21) }
          { Element = "H"; Position = (0.94, 0.0, -0.54) }
          { Element = "H"; Position = (-0.94, 0.0, -0.54) } ]
      Bonds =
        [ { Atom1 = 0; Atom2 = 1; BondOrder = 2.0 }
          { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
          { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 } ]
      Charge = 0; Multiplicity = 1 }

/// Ammonia (NH3) — used as imine-analogue reactant in Staudinger
/// and Lewis acid routes.
let private ammonia : Molecule =
    { Name = "Ammonia (NH3)"
      Atoms =
        [ { Element = "N"; Position = (5.0, 0.0, 0.0) }
          { Element = "H"; Position = (5.0, 0.94, 0.38) }
          { Element = "H"; Position = (5.0, -0.47, 0.82) }
          { Element = "H"; Position = (5.0, -0.47, -0.44) } ]
      Bonds =
        [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
          { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
          { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 } ]
      Charge = 0; Multiplicity = 1 }

/// Formamide (simplified) — shared product of Staudinger and Lewis acid routes.
let private formamide : Molecule =
    { Name = "Formamide (simplified)"
      Atoms =
        [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
          { Element = "O"; Position = (0.0, 0.0, 1.23) }
          { Element = "H"; Position = (0.94, 0.0, -0.54) }
          { Element = "N"; Position = (-1.20, 0.0, -0.50) }
          { Element = "H"; Position = (-1.80, 0.82, -0.30) } ]
      Bonds =
        [ { Atom1 = 0; Atom2 = 1; BondOrder = 2.0 }
          { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
          { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 }
          { Atom1 = 3; Atom2 = 4; BondOrder = 1.0 } ]
      Charge = 0; Multiplicity = 1 }

/// Staudinger [2+2] cycloaddition: ketene + imine -> beta-lactam.
/// The classic route; models amide C-N bond formation via formaldehyde + ammonia.
let private staudingerRoute : SynthesisRoute =
    let ts : Molecule =
        { Name = "C-N Bond Formation TS"
          Atoms =
            [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
              { Element = "O"; Position = (0.0, 0.0, 1.30) }
              { Element = "H"; Position = (0.94, 0.0, -0.54) }
              { Element = "N"; Position = (1.80, 0.0, 0.0) }
              { Element = "H"; Position = (2.40, 0.0, 0.82) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.5 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 3; BondOrder = 0.5 }
              { Atom1 = 3; Atom2 = 4; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "Staudinger [2+2]"
      Reactants = [ formaldehyde; ammonia ]
      TransitionState = ts
      Product = formamide
      Description = "Ketene + imine cycloaddition (amide C-N bond formation model)" }

/// Ring expansion: azetidine (3-membered C ring) -> beta-lactam.
/// Models nitrogen insertion into a strained ring via C-N TS.
let private ringExpansionRoute : SynthesisRoute =
    let reactant : Molecule =
        { Name = "Azetidine (model)"
          Atoms =
            [ { Element = "N"; Position = (0.0, 0.0, 0.0) }
              { Element = "C"; Position = (1.20, 0.0, 0.70) }
              { Element = "C"; Position = (0.0, 1.20, 0.70) }
              { Element = "H"; Position = (-0.90, 0.0, -0.40) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 1; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let ts : Molecule =
        { Name = "Ring Expansion TS"
          Atoms =
            [ { Element = "N"; Position = (0.0, 0.0, 0.0) }
              { Element = "C"; Position = (1.40, 0.0, 0.50) }
              { Element = "C"; Position = (0.0, 1.40, 0.50) }
              { Element = "O"; Position = (2.10, 0.0, 1.30) }
              { Element = "H"; Position = (-0.90, 0.0, -0.40) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 0.8 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 1; Atom2 = 2; BondOrder = 0.5 }
              { Atom1 = 1; Atom2 = 3; BondOrder = 1.5 }
              { Atom1 = 0; Atom2 = 4; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let product : Molecule =
        { Name = "2-Azetidinone (beta-lactam)"
          Atoms =
            [ { Element = "N"; Position = (0.0, 0.0, 0.0) }
              { Element = "C"; Position = (1.35, 0.0, 0.0) }
              { Element = "C"; Position = (0.0, 1.35, 0.0) }
              { Element = "O"; Position = (2.10, 0.0, 0.95) }
              { Element = "H"; Position = (-0.90, 0.0, -0.40) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 1; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 1; Atom2 = 3; BondOrder = 2.0 }
              { Atom1 = 0; Atom2 = 4; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "Ring Expansion"
      Reactants = [ reactant ]
      TransitionState = ts
      Product = product
      Description = "Azetidine ring expansion to beta-lactam via C=O insertion" }

/// Lewis acid catalyzed Staudinger: same reactants but TS stabilized by BF3
/// (modeled as shorter C-N bond distance and adjusted bond orders).
let private lewisAcidRoute : SynthesisRoute =
    // Lewis acid stabilized TS: shorter C-N distance (1.60 vs 1.80 A),
    // stronger partial bonds reflect BF3 coordination lowering the barrier.
    let ts : Molecule =
        { Name = "Lewis Acid TS (BF3-stabilized)"
          Atoms =
            [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
              { Element = "O"; Position = (0.0, 0.0, 1.28) }
              { Element = "H"; Position = (0.94, 0.0, -0.54) }
              { Element = "N"; Position = (1.60, 0.0, 0.0) }
              { Element = "H"; Position = (2.20, 0.0, 0.82) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.6 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 3; BondOrder = 0.7 }
              { Atom1 = 3; Atom2 = 4; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "Lewis Acid Catalyzed"
      Reactants = [ formaldehyde; ammonia ]
      TransitionState = ts
      Product = formamide
      Description = "BF3-catalyzed Staudinger (stabilized TS, lower barrier)" }

/// Enzymatic cleavage model: penicillin acylase mechanism.
/// Models O-nucleophile attacking amide C (serine hydroxyl -> acyl-enzyme).
let private enzymaticRoute : SynthesisRoute =
    let reactant : Molecule =
        { Name = "Amide substrate (model)"
          Atoms =
            [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
              { Element = "O"; Position = (0.0, 0.0, 1.22) }
              { Element = "N"; Position = (1.33, 0.0, -0.20) }
              { Element = "H"; Position = (1.80, 0.0, 0.60) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 2.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 2; Atom2 = 3; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let ts : Molecule =
        { Name = "Acylation TS"
          Atoms =
            [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
              { Element = "O"; Position = (0.0, 0.0, 1.35) }
              { Element = "N"; Position = (1.50, 0.0, -0.10) }
              { Element = "O"; Position = (-1.70, 0.0, -0.20) }
              { Element = "H"; Position = (-2.30, 0.0, 0.55) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.5 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 0.6 }
              { Atom1 = 0; Atom2 = 3; BondOrder = 0.5 }
              { Atom1 = 3; Atom2 = 4; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let product : Molecule =
        { Name = "Acyl-enzyme (model)"
          Atoms =
            [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
              { Element = "O"; Position = (0.0, 0.0, 1.22) }
              { Element = "O"; Position = (-1.35, 0.0, -0.20) }
              { Element = "H"; Position = (-1.80, 0.0, 0.55) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 2.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 2; Atom2 = 3; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "Enzymatic Cleavage"
      Reactants = [ reactant ]
      TransitionState = ts
      Product = product
      Description = "Penicillin acylase model (serine nucleophilic acylation)" }

/// All built-in presets keyed by lowercase name.
let private builtinPresets : Map<string, SynthesisRoute> =
    [ staudingerRoute; ringExpansionRoute; lewisAcidRoute; enzymaticRoute ]
    |> List.map (fun r -> r.Name.ToLowerInvariant().Replace(" ", "-").Replace("[", "").Replace("]", ""), r)
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

/// Infer single bonds between all adjacent atom pairs (simple fallback).
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

/// Load synthesis routes from a CSV file.
/// Expected columns: name, description, reactant_atoms, ts_atoms, product_atoms
/// OR: name, preset (to reference a built-in preset by name)
let private loadRoutesFromCsv (path: string) : SynthesisRoute list =
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
            | Some route -> Some { route with Name = name }
            | None ->
                if not quiet then
                    eprintfn "  Warning: unknown preset '%s' (available: %s)" presetKey presetNames
                None
        | None ->
            match get "reactant_atoms", get "ts_atoms", get "product_atoms" with
            | Some rAtoms, Some tsAtoms, Some pAtoms ->
                let desc = get "description" |> Option.defaultValue ""
                let reactant = moleculeFromAtomString (name + " reactant") rAtoms
                let ts = moleculeFromAtomString (name + " TS") tsAtoms
                let product = moleculeFromAtomString (name + " product") pAtoms
                Some
                    { Name = name
                      Reactants = [ reactant ]
                      TransitionState = ts
                      Product = product
                      Description = desc }
            | _ ->
                if not quiet then
                    eprintfn "  Warning: row '%s' missing required columns (reactant_atoms, ts_atoms, product_atoms or preset)" name
                None)

// ==============================================================================
// ROUTE SELECTION
// ==============================================================================

let routes : SynthesisRoute list =
    let allRoutes =
        match inputFile with
        | Some path ->
            let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
            if not quiet then
                printfn "Loading routes from: %s" resolved
            loadRoutesFromCsv resolved
        | None ->
            builtinPresets |> Map.toList |> List.map snd

    match routeFilter with
    | [] -> allRoutes
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        allRoutes
        |> List.filter (fun r ->
            let key = r.Name.ToLowerInvariant().Replace(" ", "-").Replace("[", "").Replace("]", "")
            filterSet |> Set.exists (fun f -> key.Contains f))

if List.isEmpty routes then
    eprintfn "Error: No routes selected. Available presets: %s" presetNames
    exit 1

// ==============================================================================
// QUANTUM BACKEND (Rule 1: all VQE via IQuantumBackend)
// ==============================================================================

let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Antibiotic Precursor Synthesis: Route Comparison"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:      %s" backend.Name
    printfn "  Routes:       %d" routes.Length
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

/// Interpret an activation energy barrier.
let private assessBarrier (eaKcal: float) : string =
    if eaKcal < 0.0 then "Negative Ea (illustrative only)"
    elif eaKcal < 15.0 then "Low barrier (fast)"
    elif eaKcal < 25.0 then "Moderate barrier"
    elif eaKcal < 35.0 then "High (needs catalyst)"
    elif eaKcal < 50.0 then "Very high (catalyst essential)"
    else "Extreme (alt. route needed)"

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

/// Compute the full energy profile for one synthesis route.
let private computeRoute (backend: IQuantumBackend) (maxIter: int) (tol: float) (idx: int) (total: int) (route: SynthesisRoute) : RouteResult =
    if not quiet then
        printfn "  [%d/%d] %s" (idx + 1) total route.Name
        printfn "         %s" route.Description

    let startTime = DateTime.Now
    let mutable anyFailure = false

    /// Unwrap a VQE result, logging failures and tracking error state.
    let unwrapEnergy (label: string) (name: string) (res: Result<float, string>, elapsed: float) : float * float =
        match res with
        | Ok e ->
            if not quiet then
                printfn "         %-8s %-20s  E = %10.6f Ha  (%.1fs)" label name e elapsed
            (e, elapsed)
        | Error _ ->
            anyFailure <- true
            if not quiet then
                printfn "         %-8s %-20s  E = FAILED         (%.1fs)" label name elapsed
            (0.0, elapsed)

    // Reactant energy = sum of separated species
    let reactantEnergy =
        route.Reactants
        |> List.sumBy (fun mol ->
            let (e, _) = unwrapEnergy "reactant" mol.Name (computeEnergy backend maxIter tol mol)
            e)

    let (tsE, _) = unwrapEnergy "TS" route.TransitionState.Name (computeEnergy backend maxIter tol route.TransitionState)
    let (prodE, _) = unwrapEnergy "product" route.Product.Name (computeEnergy backend maxIter tol route.Product)

    let totalTime = (DateTime.Now - startTime).TotalSeconds

    // Activation energy
    let eaHartree = tsE - reactantEnergy
    let eaKcal = eaHartree * hartreeToKcalMol

    // Reaction energy
    let dEKcal = (prodE - reactantEnergy) * hartreeToKcalMol

    // Rate constant via Eyring equation: k = (kB*T/h) * exp(-Ea/(R*T))
    let eaJoules = eaHartree * hartreeToKJMol * 1000.0  // Hartree -> kJ/mol -> J/mol
    let kBT_h = kB * temperature / hPlanck
    let rateK = kBT_h * exp(-eaJoules / (gasR * temperature))

    if not quiet then
        if anyFailure then
            printfn "         => INCOMPLETE (VQE failure — energies are unreliable)"
        else
            printfn "         => Ea = %.2f kcal/mol  |  dE = %.2f kcal/mol  |  k = %.2e /s" eaKcal dEKcal rateK
        printfn ""

    { Route = route
      ReactantEnergy = reactantEnergy
      TsEnergy = tsE
      ProductEnergy = prodE
      ActivationEnergyHartree = eaHartree
      ActivationEnergyKcal = eaKcal
      ReactionEnergyKcal = dEKcal
      RateConstant = rateK
      HalfLife = formatHalfLife rateK
      BarrierAssessment = if anyFailure then "VQE FAILED" else assessBarrier eaKcal
      ComputeTimeSeconds = totalTime
      HasVqeFailure = anyFailure }

// --- Run all routes ---

if not quiet then
    printfn "Computing energy profiles..."
    printfn ""

let results =
    routes
    |> List.mapi (fun i route -> computeRoute backend maxIterations tolerance i routes.Length route)

// Sort by activation energy ascending (lowest positive barrier = fastest route).
// Failed routes sink to the bottom; negative Ea values (unphysical with empirical
// Hamiltonians) sort below positive ones but above failures.
let ranked =
    results
    |> List.sortBy (fun r ->
        if r.HasVqeFailure then (2, infinity)
        elif r.ActivationEnergyKcal < 0.0 then (1, r.ActivationEnergyKcal)
        else (0, r.ActivationEnergyKcal))

// ==============================================================================
// RANKED COMPARISON TABLE
// ==============================================================================

let printTable () =
    printfn "=================================================================="
    printfn "  Ranked Synthesis Routes (by activation energy)"
    printfn "=================================================================="
    printfn ""
    printfn "  %-4s  %-26s  %13s  %11s  %10s  %s"
        "#" "Route" "Ea (kcal/mol)" "Rate (/s)" "Half-life" "Assessment"
    printfn "  %s" (String('=', 90))

    ranked
    |> List.iteri (fun i r ->
        printfn "  %-4d  %-26s  %13.2f  %11.2e  %10s  %s"
            (i + 1)
            r.Route.Name
            r.ActivationEnergyKcal
            r.RateConstant
            r.HalfLife
            r.BarrierAssessment)

    printfn ""

    // Thermodynamics column
    printfn "  %-4s  %-26s  %13s  %11s  %s"
        "#" "Route" "dE (kcal/mol)" "Time (s)" "Thermodynamics"
    printfn "  %s" (String('-', 78))

    ranked
    |> List.iteri (fun i r ->
        let thermo = if r.ReactionEnergyKcal < 0.0 then "exothermic" else "endothermic"
        printfn "  %-4d  %-26s  %13.2f  %11.1f  %s"
            (i + 1) r.Route.Name r.ReactionEnergyKcal r.ComputeTimeSeconds thermo)

    printfn ""

// Always print the ranked comparison table — that's the primary output of this tool,
// even in --quiet mode (which only suppresses per-route progress output).
printTable ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    let best = ranked |> List.head
    let totalTime = results |> List.sumBy (fun r -> r.ComputeTimeSeconds)
    printfn "  Best route:  %s (Ea = %.2f kcal/mol)" best.Route.Name best.ActivationEnergyKcal
    printfn "  Total time:  %.1f seconds" totalTime
    printfn "  Quantum:     all VQE via IQuantumBackend [Rule 1 compliant]"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    ranked
    |> List.mapi (fun i r ->
        [ "rank", string (i + 1)
          "route", r.Route.Name
          "description", r.Route.Description
          "ea_hartree", sprintf "%.6f" r.ActivationEnergyHartree
          "ea_kcal_mol", sprintf "%.2f" r.ActivationEnergyKcal
          "de_kcal_mol", sprintf "%.2f" r.ReactionEnergyKcal
          "rate_constant_s", sprintf "%.2e" r.RateConstant
          "half_life", r.HalfLife
          "barrier_assessment", r.BarrierAssessment
          "thermodynamics", (if r.ReactionEnergyKcal < 0.0 then "exothermic" else "endothermic")
          "reactant_energy_ha", sprintf "%.6f" r.ReactantEnergy
          "ts_energy_ha", sprintf "%.6f" r.TsEnergy
          "product_energy_ha", sprintf "%.6f" r.ProductEnergy
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
        [ "rank"; "route"; "description"; "ea_hartree"; "ea_kcal_mol"; "de_kcal_mol"
          "rate_constant_s"; "half_life"; "barrier_assessment"; "thermodynamics"
          "reactant_energy_ha"; "ts_energy_ha"; "product_energy_ha"
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
    printfn "     --routes staudinger-2+2,lewis-acid-catalyzed   Run specific routes"
    printfn "     --input routes.csv                             Load custom routes from CSV"
    printfn "     --csv results.csv                              Export ranked table as CSV"
    printfn ""
