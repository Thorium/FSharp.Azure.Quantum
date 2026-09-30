// ==============================================================================
// Antibiotic Precursor Synthesis - Alternative Route Discovery
// ==============================================================================
// Compares synthesis routes to the beta-lactam ring by VQE activation energy
// and reaction energy.
//
// Each route is a balanced reaction that forms 2-azetidinone, the parent
// beta-lactam of penicillins and cephalosporins, from small real molecules. VQE
// computes the ground-state energy of every species:
//   dE = sum E(products) - sum E(reactants)    reaction energy, every route
//   Ea = E(transition state) - sum E(reactants) activation energy, for routes
//                                               with a transition-state FCIDUMP
//
// Background:
// China controls ~90% of global 6-APA/7-ACA production (key antibiotic
// intermediates). Alternative routes to the strained four-membered lactam
// ring (~27 kcal/mol ring strain) are screened on barriers and thermodynamics.
//
// ACTIVATION ENERGIES:
// A route has an activation energy when a transition-state FCIDUMP named after
// it exists: "<route-slug>-ts.fcidump" (e.g. kinugasa-ts.fcidump) in the
// integral folder (bundled or --fcidump-dir). Routes without one report their
// reaction energy only and are marked "no TS". To produce a TS FCIDUMP for a
// route's rate-determining step (see examples/_data/chemistry/fcidump/README.md):
//   1. locate a first-order saddle point with PySCF + geomeTRIC
//      (pyscf.geomopt.geometric_solver.optimize(mf, transition=True,
//      hessian="first")) from a constrained-optimisation guess, at the same
//      level as the reactants (RHF/STO-3G here);
//   2. validate it: exactly one imaginary frequency (analytic RHF Hessian), and
//      downhill optimisations along that mode reach the reactant and product
//      basins of the step;
//   3. write the FCIDUMP with the reactants' total active space (two CAS(2,2)
//      reactants -> CAS(4,4) TS), so Ea compares like with like.
// The bundled TSs were made this way; a route without one (ring expansion) gets
// Ea as soon as its TS FCIDUMP is added. Each bundled TS is one elementary step:
// Kinugasa's is the nitrone + alkyne cycloaddition to 4-isoxazoline, the first
// step before the rearrangement to the lactam. Catalysts (Cu for Kinugasa, Co/Rh for
// carbonylation, activating agents for lactamisation) lower real barriers; the
// bundled steps are uncatalysed gas-phase models.
//
// HAMILTONIAN SOURCE:
// By default every species runs on bundled FCIDUMP integrals
// (examples/_data/chemistry/fcidump: RHF/STO-3G geometries optimised with PySCF,
// CASCI active spaces; README.md there gives the method).
// Each reaction keeps the same total active space on both sides: two CAS(2,2)
// reactants form one CAS(4,4) product, and one CAS(4,4) reactant forms two CAS(2,2)
// products. Energies are STO-3G totals, so dE is close to an RHF/STO-3G reaction
// energy plus the active-space correlation. It gives signs and trends, not
// kcal/mol accuracy.
//   --fcidump-dir DIR  use your own FCIDUMP files (<species-slug>.fcidump)
//   --empirical        run on the library's EMPIRICAL prototype Hamiltonian
//                      instead (illustrative only, clearly labelled)
// Each VQE is capped at 16 qubits (8 active orbitals).
//
// Usage:
//   dotnet fsi AntibioticPrecursorSynthesis.fsx
//   dotnet fsi AntibioticPrecursorSynthesis.fsx -- --help
//   dotnet fsi AntibioticPrecursorSynthesis.fsx -- --routes staudinger,kinugasa
//   dotnet fsi AntibioticPrecursorSynthesis.fsx -- --input routes.csv --fcidump-dir ./fcidumps
//   dotnet fsi AntibioticPrecursorSynthesis.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Staudinger, H. "Zur Kenntniss der Ketene" Liebigs Ann. Chem. 356, 51 (1907)
//   [2] Kinugasa, M.; Hashimoto, S. J. Chem. Soc., Chem. Commun. 466 (1972)
//   [3] Alper, H. et al. "Carbonylation of aziridines to beta-lactams" J. Am. Chem. Soc. (1983)
//   [4] Wikipedia: beta-Lactam (https://en.wikipedia.org/wiki/Beta-lactam)
//   [5] Reiher, M. et al. "Elucidating reaction mechanisms on quantum computers" PNAS (2017)
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
    "AntibioticPrecursorSynthesis.fsx"
    "Compare beta-lactam synthesis routes by VQE activation energy (routes with a <route-slug>-ts.fcidump) and reaction energy"
    [
        {
            Cli.OptionSpec.Name = "input"
            Description = "CSV file with custom routes (name, description, reactant_atoms, product_atoms, or preset)"
            Default = Some "built-in presets"
        }
        {
            Cli.OptionSpec.Name = "routes"
            Description = "Comma-separated preset names to run (default: all)"
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
            Description =
                "Directory with one FCIDUMP per species (<species-slug>.fcidump); <route-slug>-ts.fcidump, a validated transition state with the reactants' total active space, adds the route's Ea (how to make one: header, fcidump/README.md)"
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
let inputFile = args |> Cli.tryGet "input"
let routeFilter = args |> Cli.getCommaSeparated "routes"
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
// ROUTES
// ==============================================================================

/// A balanced reaction forming the beta-lactam ring.
type SynthesisRoute =
    {
        Name: string
        Reactants: Molecule list
        Products: Molecule list
        Description: string
    }

/// Species name of a route's transition state; its FCIDUMP is "<route-slug>-ts.fcidump".
let transitionStateName (route: SynthesisRoute) = $"{route.Name} TS"

/// The route's transition state: the bundled geometry when there is one, else the reactants'
/// atoms (an FCIDUMP carries its own integrals; the geometry is only descriptive).
let private transitionState (route: SynthesisRoute) : Molecule =
    let name = transitionStateName route

    let bundledGeometry =
        IO.Path.Combine(ChemistryIntegrals.bundledDirectory, ChemistryIntegrals.speciesSlug name + ".xyz")

    if IO.File.Exists bundledGeometry then
        ChemistryIntegrals.loadSpecies name
    else
        {
            Name = name
            Atoms = route.Reactants |> List.collect (fun m -> m.Atoms)
            Bonds = []
            Charge = 0
            Multiplicity = 1
        }

let private species = ChemistryIntegrals.loadSpecies

let private builtinRoutes: SynthesisRoute list =
    [
        {
            Name = "Staudinger [2+2]"
            Reactants = [ species "Ketene [CAS(2,2)]"; species "Methanimine [CAS(2,2)]" ]
            Products = [ species "2-Azetidinone [CAS(4,4)]" ]
            Description = "Ketene + imine [2+2] cycloaddition (Staudinger 1907)"
        }
        {
            Name = "Ring Expansion"
            Reactants = [ species "Aziridine [CAS(2,2)]"; species "Carbon monoxide [CAS(2,2)]" ]
            Products = [ species "2-Azetidinone [CAS(4,4)]" ]
            Description = "Carbonylative ring expansion of aziridine (Co/Rh catalysed)"
        }
        {
            Name = "Kinugasa"
            Reactants = [ species "Formaldonitrone [CAS(2,2)]"; species "Acetylene [CAS(2,2)]" ]
            Products = [ species "2-Azetidinone [CAS(4,4)]" ]
            Description = "Nitrone + terminal alkyne coupling (Cu catalysed, Kinugasa 1972)"
        }
        {
            Name = "beta-Amino Acid Cyclization"
            Reactants = [ species "beta-Alanine [CAS(4,4)]" ]
            Products = [ species "2-Azetidinone [CAS(2,2)]"; species "Water [CAS(2,2)]" ]
            Description = "Dehydrative lactamisation of beta-alanine (activating agent in practice)"
        }
    ]

let private routeKey (name: string) = ChemistryIntegrals.speciesSlug name

// ==============================================================================
// CSV INPUT
// ==============================================================================

/// Parse "C:0,0,0|O:0,0,1.21|H:0.94,0,-0.54" into atoms.
let private parseAtoms (s: string) : Atom list =
    s.Split '|'
    |> Array.choose (fun entry ->
        match entry.Trim().Split ':' with
        | [| element; coords |] ->
            match coords.Split ',' |> Array.map Double.TryParse with
            | [| (true, x); (true, y); (true, z) |] ->
                Some
                    {
                        Element = element.Trim()
                        Position = (x, y, z)
                    }
            | _ -> None
        | _ -> None)
    |> Array.toList

let private moleculeFromAtoms (name: string) (atoms: string) : Molecule =
    {
        Name = name
        Atoms = parseAtoms atoms
        Bonds = []
        Charge = 0
        Multiplicity = 1
    }

/// Routes from CSV: name, description, reactant_atoms, product_atoms (one species each), or name, preset.
let private loadRoutesFromCsv (path: string) : SynthesisRoute list =
    let rows, errors = Data.readCsvWithHeaderWithErrors path

    if not quiet then
        errors |> List.iter (eprintfn "  Warning (CSV): %s")

    rows
    |> List.choose (fun row ->
        let get key = row.Values |> Map.tryFind key
        let name = get "name" |> Option.defaultValue "Unknown"

        match get "preset", get "reactant_atoms", get "product_atoms" with
        | Some preset, _, _ ->
            builtinRoutes
            |> List.tryFind (fun r -> routeKey r.Name = routeKey preset)
            |> Option.map (fun r -> { r with Name = name })
        | None, Some reactant, Some product ->
            Some
                {
                    Name = name
                    Reactants = [ moleculeFromAtoms (name + " reactant") reactant ]
                    Products = [ moleculeFromAtoms (name + " product") product ]
                    Description = get "description" |> Option.defaultValue ""
                }
        | _ ->
            if not quiet then
                eprintfn "  Warning: row '%s' needs preset, or reactant_atoms and product_atoms" name

            None)

let routes: SynthesisRoute list =
    let all =
        match inputFile with
        | Some path -> loadRoutesFromCsv (Data.resolveRelative __SOURCE_DIRECTORY__ path)
        | None -> builtinRoutes

    match routeFilter with
    | [] -> all
    | filters ->
        all
        |> List.filter (fun r ->
            filters
            |> List.exists (fun f -> (routeKey r.Name).Contains(f.ToLowerInvariant())))

if List.isEmpty routes then
    eprintfn
        "Error: no routes selected. Presets: %s"
        (builtinRoutes |> List.map (fun r -> routeKey r.Name) |> String.concat ", ")

    exit 1

// ==============================================================================
// VQE
// ==============================================================================

let backend: IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Antibiotic Precursor Synthesis: Route Comparison"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:      %s" backend.Name
    printfn "  Routes:       %d" routes.Length
    printfn "  VQE iters:    %d (tol: %g Ha)" maxIterations tolerance
    printfn "  Integrals:    %s" (ChemistryIntegrals.describeDirectory integralDirectory)
    printfn "  Measures:     Ea = E(TS) - E(reactants) where a <route>-ts.fcidump exists;"
    printfn "                dE = E(products) - E(reactants) for every route"
    printfn ""

let energies =
    ChemistryIntegrals.EnergyCache(backend, maxIterations, tolerance, maxVqeQubits, integralDirectory)

/// Energy profile of one route.
type RouteResult =
    {
        Route: SynthesisRoute
        /// Reaction energy in Hartree; None when a species failed
        ReactionEnergy: float option
        /// Activation energy in Hartree; Error says why there is none ("no TS" or a failure)
        ActivationEnergy: Result<float, string>
        Sources: EnergySource list
        Failures: string list
        ComputeTimeSeconds: float
    }

let private computeRoute (index: int) (route: SynthesisRoute) : RouteResult =
    if not quiet then
        printfn "  [%d/%d] %s" (index + 1) routes.Length route.Name
        printfn "         %s" route.Description

    let energyOf (role: string) (molecule: Molecule) =
        let result = energies.Energy molecule

        if not quiet then
            match result with
            | Ok e ->
                printfn
                    "         %-8s %-32s E = %14.6f Ha  (%s)  [%s]%s"
                    role
                    molecule.Name
                    e.Energy
                    (if e.Reused then "cached" else sprintf "%5.1fs" e.Seconds)
                    (ChemistryIntegrals.describeSource e.Source)
                    (if e.Converged then "" else " not converged")
            | Error msg -> printfn "         %-8s %-32s E = FAILED  (%s)" role molecule.Name msg

        result

    let reactants = route.Reactants |> List.map (energyOf "reactant")

    // The TS runs only when its FCIDUMP exists; a missing file means "no TS", not a failure.
    let ts = transitionState route

    let tsEnergy =
        match ChemistryIntegrals.tryFcidump maxVqeQubits integralDirectory ts with
        | Some _ -> Some(energyOf "TS" ts)
        | None -> None

    let products = route.Products |> List.map (energyOf "product")
    let all = reactants @ products @ Option.toList tsEnergy

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

    let reactantsComplete =
        reactants
        |> List.forall (function
            | Ok _ -> true
            | Error _ -> false)

    let productsComplete =
        products
        |> List.forall (function
            | Ok _ -> true
            | Error _ -> false)

    let reactionEnergy =
        if reactantsComplete && productsComplete then
            Some(total products - total reactants)
        else
            None

    let activationEnergy =
        match tsEnergy with
        | None -> Error $"no TS ({ChemistryIntegrals.fcidumpFileName ts} not found)"
        | Some(Error msg) -> Error $"TS failed: {msg}"
        | Some(Ok _) when not reactantsComplete -> Error "a reactant failed"
        | Some(Ok e) -> Ok(e.Energy - total reactants)

    if not quiet then
        match activationEnergy with
        | Ok ea -> printfn "         => Ea = %.6f Ha = %.1f kcal/mol" ea (ea * hartreeToKcalMol)
        | Error why -> printfn "         => Ea: %s" why

        match reactionEnergy with
        | Some dE -> printfn "         => dE = %.6f Ha = %.1f kcal/mol" dE (dE * hartreeToKcalMol)
        | None -> printfn "         => INCOMPLETE (a species failed VQE: no reaction energy)"

        printfn ""

    {
        Route = route
        ReactionEnergy = reactionEnergy
        ActivationEnergy = activationEnergy
        Sources = computed |> List.map (fun e -> e.Source) |> List.distinct
        Failures = failures
        ComputeTimeSeconds = computed |> List.sumBy (fun e -> if e.Reused then 0.0 else e.Seconds)
    }

if not quiet then
    printfn "Computing activation and reaction energies..."
    printfn ""

let results = routes |> List.mapi computeRoute

// Routes with an activation energy first (lowest barrier first), then the rest by
// reaction energy (most exothermic first); incomplete routes last.
let ranked =
    results
    |> List.sortBy (fun r ->
        match r.ActivationEnergy, r.ReactionEnergy with
        | Ok ea, _ -> (0, ea)
        | Error _, Some dE -> (1, dE)
        | Error _, None -> (2, 0.0))

let private sourceLabel (r: RouteResult) =
    match r.Sources with
    | [] -> "none"
    | sources -> sources |> List.map ChemistryIntegrals.describeSource |> String.concat " + "

let private assess (dEKcal: float) =
    if dEKcal < -20.0 then "exothermic (favourable)"
    elif dEKcal < 0.0 then "mildly exothermic"
    elif dEKcal < 20.0 then "mildly endothermic"
    else "endothermic (needs activation/driving force)"

// ==============================================================================
// RANKED COMPARISON TABLE
// ==============================================================================

let printTable () =
    printfn "=================================================================="
    printfn "  Ranked Synthesis Routes (lowest barrier first; routes without a TS by dE)"
    printfn "=================================================================="
    printfn ""

    printfn
        "  %-4s  %-28s  %13s  %14s  %-44s  %s"
        "#"
        "Route"
        "Ea (kcal/mol)"
        "dE (kcal/mol)"
        "Thermodynamics"
        "Hamiltonian"

    printfn "  %s" (String('=', 132))

    ranked
    |> List.iteri (fun i r ->
        let ea =
            match r.ActivationEnergy with
            | Ok ea -> sprintf "%.1f" (ea * hartreeToKcalMol)
            | Error why when why.StartsWith "no TS" -> "no TS"
            | Error _ -> "FAILED"

        let dE, thermodynamics =
            match r.ReactionEnergy with
            | Some dE -> sprintf "%.1f" (dE * hartreeToKcalMol), assess (dE * hartreeToKcalMol)
            | None -> "INCOMPLETE", "a species failed"

        printfn "  %-4d  %-28s  %13s  %14s  %-44s  %s" (i + 1) r.Route.Name ea dE thermodynamics (sourceLabel r))

    printfn ""

    let withoutTs =
        ranked
        |> List.filter (fun r ->
            match r.ActivationEnergy with
            | Error why -> why.StartsWith "no TS"
            | Ok _ -> false)

    if not withoutTs.IsEmpty then
        printfn
            "  no TS: no validated transition-state FCIDUMP for %s; see the header for how to add one."
            (withoutTs
             |> List.map (fun r ->
                 $"{r.Route.Name} ({ChemistryIntegrals.speciesSlug (transitionStateName r.Route)}.fcidump)")
             |> String.concat ", ")

    printfn ""

    ranked
    |> List.filter (fun r -> not r.Failures.IsEmpty)
    |> List.iter (fun r -> printfn "  %s: %s" r.Route.Name (String.concat "; " (List.distinct r.Failures)))

    if ranked |> List.exists (fun r -> r.Sources |> List.contains EmpiricalHamiltonian) then
        ChemistryIntegrals.empiricalNote |> List.iter (printfn "%s")

    printfn ""

printTable ()

if not quiet then
    match
        results
        |> List.choose (fun r ->
            match r.ActivationEnergy with
            | Ok ea -> Some(r, ea)
            | Error _ -> None)
        |> List.sortBy snd
    with
    | (best, ea) :: _ ->
        printfn
            "  Lowest barrier:         %s (Ea = %.1f kcal/mol; %s)"
            best.Route.Name
            (ea * hartreeToKcalMol)
            (sourceLabel best)
    | [] -> printfn "  Lowest barrier:         none (no route has a transition-state FCIDUMP)"

    match
        results
        |> List.filter (fun r -> r.ReactionEnergy.IsSome)
        |> List.sortBy (fun r -> r.ReactionEnergy.Value)
    with
    | best :: _ ->
        printfn
            "  Most exothermic route:  %s (dE = %.1f kcal/mol; %s)"
            best.Route.Name
            (best.ReactionEnergy.Value * hartreeToKcalMol)
            (sourceLabel best)
    | [] -> printfn "  Most exothermic route:  none (every route is INCOMPLETE)"

    printfn "  Total VQE time:         %.1f seconds" (results |> List.sumBy (fun r -> r.ComputeTimeSeconds))
    printfn "  Quantum:                all VQE via IQuantumBackend [Rule 1 compliant]"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    ranked
    |> List.mapi (fun i r ->
        let value format =
            match r.ReactionEnergy with
            | Some dE -> format dE
            | None -> "INCOMPLETE"

        [
            "rank", string (i + 1)
            "route", r.Route.Name
            "description", r.Route.Description
            "reactants", r.Route.Reactants |> List.map (fun m -> m.Name) |> String.concat " + "
            "products", r.Route.Products |> List.map (fun m -> m.Name) |> String.concat " + "
            "reaction_energy_ha", value (sprintf "%.6f")
            "reaction_energy_kcal_mol", value (fun dE -> sprintf "%.2f" (dE * hartreeToKcalMol))
            "activation_energy_kcal_mol",
            (match r.ActivationEnergy with
             | Ok ea -> sprintf "%.2f" (ea * hartreeToKcalMol)
             | Error why -> why)
            "compute_time_s", sprintf "%.1f" r.ComputeTimeSeconds
            "hamiltonian", sourceLabel r
        ]
        |> Map.ofList)

let header =
    [
        "rank"
        "route"
        "description"
        "reactants"
        "products"
        "activation_energy_kcal_mol"
        "reaction_energy_ha"
        "reaction_energy_kcal_mol"
        "compute_time_s"
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
    printfn "     --routes staudinger,kinugasa        Run specific routes"
    printfn "     --fcidump-dir ./fcidumps            Your own FCIDUMP integrals"
    printfn "     --csv results.csv                   Export ranked table as CSV"
    printfn ""
