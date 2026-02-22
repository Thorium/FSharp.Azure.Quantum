// ==============================================================================
// Drug Metabolism Reaction Pathway Comparison
// ==============================================================================
// Compares CYP450 metabolic pathways by computing VQE activation energies.
//
// Accepts multiple metabolic pathways (built-in presets or --input CSV), runs
// VQE on each pathway's reactant/TS/product molecules, and outputs a ranked
// comparison table showing which pathway has the lowest activation barrier.
//
// Background:
// Cytochrome P450 enzymes catalyse the majority of Phase I drug metabolism.
// The rate-determining step â€” typically C-H bond activation â€” has an activation
// energy that controls drug half-life, dosing frequency, and toxic metabolite
// formation. Quantum chemistry can calculate these barriers more accurately
// than classical DFT, which systematically underestimates them by 5-10 kcal/mol.
//
// IMPORTANT LIMITATION:
// This example uses EMPIRICAL Hamiltonian coefficients (not molecular integrals).
// Calculated energies are ILLUSTRATIVE. For production use, molecular integral
// calculation (via PySCF, Psi4, or similar) would be needed.
// See: https://qiskit.org/documentation/nature/
//
// Usage:
//   dotnet fsi ReactionPathway.fsx
//   dotnet fsi ReactionPathway.fsx -- --help
//   dotnet fsi ReactionPathway.fsx -- --pathways hydroxylation,n-dealkylation
//   dotnet fsi ReactionPathway.fsx -- --input pathways.csv
//   dotnet fsi ReactionPathway.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Eyring, H. "The Activated Complex in Chemical Reactions" J. Chem. Phys. 3, 107 (1935)
//   [2] Guengerich, F.P. "Mechanisms of Cytochrome P450" Chem. Res. Toxicol. (2001)
//   [3] Reiher, M. et al. "Elucidating reaction mechanisms on quantum computers" PNAS (2017)
//   [4] Harper's Illustrated Biochemistry, 28th Ed., Ch. 53: Metabolism of Xenobiotics
//   [5] Wikipedia: Transition_state_theory
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

Cli.exitIfHelp "ReactionPathway.fsx"
    "Compare CYP450 metabolic pathways by VQE activation energy"
    [ { Cli.OptionSpec.Name = "input"; Description = "CSV file with custom metabolic pathways"; Default = Some "built-in presets" }
      { Cli.OptionSpec.Name = "pathways"; Description = "Comma-separated preset names to run (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "50" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Energy convergence tolerance (Hartree)"; Default = Some "1e-4" }
      { Cli.OptionSpec.Name = "temperature"; Description = "Temperature for rate calculations (Kelvin)"; Default = Some "310" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputFile = args |> Cli.tryGet "input"
let pathwayFilter = args |> Cli.getCommaSeparated "pathways"
let maxIterations = Cli.getIntOr "max-iterations" 50 args
let tolerance = Cli.getFloatOr "tolerance" 1e-4 args
let temperature = Cli.getFloatOr "temperature" 310.0 args

// ==============================================================================
// TYPES
// ==============================================================================

/// A metabolic pathway defined by its three molecular species along the
/// reaction coordinate: separated reactants, transition state, and product.
type MetabolicPathway =
    { Name: string
      Reactants: Molecule list
      TransitionState: Molecule
      Product: Molecule
      Enzyme: string
      Description: string }

/// Result of computing a single pathway's energy profile via VQE.
type PathwayResult =
    { Pathway: MetabolicPathway
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
// BUILT-IN PATHWAY PRESETS
// ==============================================================================
// Each pathway models a different CYP450 metabolic reaction using
// NISQ-tractable model molecules (<=7 atoms per species).

/// C-H hydroxylation: CH2 + OH -> [CH...H...OH] -> CH2OH
/// The most common CYP450 reaction (~75% of Phase I metabolism).
/// Model: methylene + OH captures essential C-H bond activation
/// with NISQ-tractable molecule sizes (â‰¤5 atoms per species).
let private hydroxylationPathway : MetabolicPathway =
    let reactant : Molecule =
        { Name = "Methylene (CH2, model)"
          Atoms =
            [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (1.09, 0.0, 0.0) }
              { Element = "H"; Position = (-0.547, 0.944, 0.0) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let oxidant : Molecule =
        { Name = "Hydroxyl radical (OH)"
          Atoms =
            [ { Element = "O"; Position = (5.0, 0.0, 0.0) }
              { Element = "H"; Position = (5.97, 0.0, 0.0) } ]
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 2 }

    let ts : Molecule =
        { Name = "C-H Abstraction TS"
          Atoms =
            [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (-0.547, 0.944, 0.0) }   // spectator H
              { Element = "H"; Position = (1.35, 0.0, 0.0) }       // stretched C-H (1.09 -> 1.35)
              { Element = "O"; Position = (2.65, 0.0, 0.0) }       // O approaching
              { Element = "H"; Position = (3.3, 0.7, 0.0) } ]      // O-H
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 0.5 }    // breaking C-H
              { Atom1 = 2; Atom2 = 3; BondOrder = 0.5 }    // forming O-H
              { Atom1 = 3; Atom2 = 4; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 2 }

    let product : Molecule =
        { Name = "Hydroxymethyl (CH2OH, model)"
          Atoms =
            [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
              { Element = "H"; Position = (-0.547, 0.944, 0.0) }
              { Element = "O"; Position = (1.43, 0.0, 0.0) }
              { Element = "H"; Position = (1.83, 0.89, 0.0) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 2; Atom2 = 3; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "Hydroxylation"
      Reactants = [ reactant; oxidant ]
      TransitionState = ts
      Product = product
      Enzyme = "CYP3A4"
      Description = "C-H bond hydroxylation (most common CYP450 reaction)" }

/// N-dealkylation: CH3-NH2 + OH -> [CH2...H...OH + NH2] -> CH2O + NH3
/// Second most common CYP450 reaction. Model: methylamine N-demethylation.
let private nDealkylationPathway : MetabolicPathway =
    let reactant : Molecule =
        { Name = "Methylamine (CH3NH2)"
          Atoms =
            [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
              { Element = "N"; Position = (1.47, 0.0, 0.0) }
              { Element = "H"; Position = (-0.54, 0.94, 0.0) }
              { Element = "H"; Position = (-0.54, -0.47, 0.82) }
              { Element = "H"; Position = (1.86, 0.46, 0.82) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 }
              { Atom1 = 1; Atom2 = 4; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let oxidant : Molecule =
        { Name = "Hydroxyl radical (OH)"
          Atoms =
            [ { Element = "O"; Position = (5.0, 0.0, 0.0) }
              { Element = "H"; Position = (5.97, 0.0, 0.0) } ]
          Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 2 }

    let ts : Molecule =
        { Name = "N-Dealkylation TS"
          Atoms =
            [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
              { Element = "N"; Position = (1.55, 0.0, 0.0) }       // stretched C-N
              { Element = "H"; Position = (-0.54, 0.94, 0.0) }
              { Element = "H"; Position = (-0.54, -0.47, 0.82) }
              { Element = "O"; Position = (-1.30, 0.0, -0.80) }    // O approaching C
              { Element = "H"; Position = (-1.85, 0.0, -0.10) } ]  // transferred H
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 0.6 }   // weakening C-N
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 4; BondOrder = 0.5 }   // forming C-O
              { Atom1 = 4; Atom2 = 5; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 2 }

    let product : Molecule =
        { Name = "Formaldehyde (CH2O)"
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

    { Name = "N-Dealkylation"
      Reactants = [ reactant; oxidant ]
      TransitionState = ts
      Product = product
      Enzyme = "CYP2D6"
      Description = "Oxidative N-dealkylation (methylamine demethylation model)" }

/// Aromatic oxidation: benzene-model + OH -> epoxide TS -> phenol-model
/// CYP1A2-mediated aromatic ring oxidation. Model: simplified 3-atom ring.
let private aromaticOxidationPathway : MetabolicPathway =
    let reactant : Molecule =
        { Name = "Ethene (C2H4, arene model)"
          Atoms =
            [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
              { Element = "C"; Position = (1.34, 0.0, 0.0) }
              { Element = "H"; Position = (-0.54, 0.94, 0.0) }
              { Element = "H"; Position = (1.88, 0.94, 0.0) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 2.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 1; Atom2 = 3; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let oxidant : Molecule =
        { Name = "Oxygen atom (model)"
          Atoms =
            [ { Element = "O"; Position = (5.0, 1.0, 0.0) } ]
          Bonds = []
          Charge = 0; Multiplicity = 3 }

    let ts : Molecule =
        { Name = "Epoxidation TS"
          Atoms =
            [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
              { Element = "C"; Position = (1.40, 0.0, 0.0) }       // stretched C=C
              { Element = "O"; Position = (0.70, 0.0, 1.60) }      // O approaching from above
              { Element = "H"; Position = (-0.54, 0.94, 0.0) }
              { Element = "H"; Position = (1.94, 0.94, 0.0) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.5 }   // weakening double bond
              { Atom1 = 0; Atom2 = 2; BondOrder = 0.4 }   // forming C-O (left)
              { Atom1 = 1; Atom2 = 2; BondOrder = 0.4 }   // forming C-O (right)
              { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 }
              { Atom1 = 1; Atom2 = 4; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let product : Molecule =
        { Name = "Ethylene oxide (epoxide)"
          Atoms =
            [ { Element = "C"; Position = (0.0, 0.0, 0.0) }
              { Element = "C"; Position = (1.47, 0.0, 0.0) }
              { Element = "O"; Position = (0.74, 0.0, 1.22) }
              { Element = "H"; Position = (-0.54, 0.94, 0.0) }
              { Element = "H"; Position = (2.01, 0.94, 0.0) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 1; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 }
              { Atom1 = 1; Atom2 = 4; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "Aromatic Oxidation"
      Reactants = [ reactant; oxidant ]
      TransitionState = ts
      Product = product
      Enzyme = "CYP1A2"
      Description = "Aromatic epoxidation (arene oxide intermediate model)" }

/// S-oxidation: (CH3)2S + O -> [(CH3)2S...O] -> (CH3)2SO
/// FMO-mediated sulfoxidation. Model: dimethyl sulfide -> dimethyl sulfoxide.
let private sulfoxidationPathway : MetabolicPathway =
    let reactant : Molecule =
        { Name = "Dimethyl sulfide (model)"
          Atoms =
            [ { Element = "S"; Position = (0.0, 0.0, 0.0) }
              { Element = "C"; Position = (1.82, 0.0, 0.0) }
              { Element = "C"; Position = (-1.82, 0.0, 0.0) }
              { Element = "H"; Position = (2.30, 0.94, 0.0) }
              { Element = "H"; Position = (-2.30, 0.94, 0.0) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 1; Atom2 = 3; BondOrder = 1.0 }
              { Atom1 = 2; Atom2 = 4; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let oxidant : Molecule =
        { Name = "Oxygen atom (model)"
          Atoms =
            [ { Element = "O"; Position = (0.0, 3.0, 0.0) } ]
          Bonds = []
          Charge = 0; Multiplicity = 3 }

    let ts : Molecule =
        { Name = "Sulfoxidation TS"
          Atoms =
            [ { Element = "S"; Position = (0.0, 0.0, 0.0) }
              { Element = "C"; Position = (1.82, 0.0, 0.0) }
              { Element = "C"; Position = (-1.82, 0.0, 0.0) }
              { Element = "O"; Position = (0.0, 1.80, 0.0) }       // O approaching S
              { Element = "H"; Position = (2.30, 0.94, 0.0) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 3; BondOrder = 0.5 }    // forming S-O
              { Atom1 = 1; Atom2 = 4; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    let product : Molecule =
        { Name = "DMSO (model)"
          Atoms =
            [ { Element = "S"; Position = (0.0, 0.0, 0.0) }
              { Element = "C"; Position = (1.82, 0.0, 0.0) }
              { Element = "C"; Position = (-1.82, 0.0, 0.0) }
              { Element = "O"; Position = (0.0, 1.48, 0.0) }
              { Element = "H"; Position = (2.30, 0.94, 0.0) } ]
          Bonds =
            [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
              { Atom1 = 0; Atom2 = 3; BondOrder = 2.0 }
              { Atom1 = 1; Atom2 = 4; BondOrder = 1.0 } ]
          Charge = 0; Multiplicity = 1 }

    { Name = "Sulfoxidation"
      Reactants = [ reactant; oxidant ]
      TransitionState = ts
      Product = product
      Enzyme = "FMO3"
      Description = "S-oxidation (dimethyl sulfide -> DMSO, flavin monooxygenase)" }

/// All built-in presets keyed by lowercase name.
let private builtinPresets : Map<string, MetabolicPathway> =
    [ hydroxylationPathway; nDealkylationPathway; aromaticOxidationPathway; sulfoxidationPathway ]
    |> List.map (fun p -> p.Name.ToLowerInvariant().Replace(" ", "-"), p)
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

/// Load metabolic pathways from a CSV file.
/// Expected columns: name, enzyme, description, reactant_atoms, ts_atoms, product_atoms
/// OR: name, preset (to reference a built-in preset by name)
let private loadPathwaysFromCsv (path: string) : MetabolicPathway list =
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
            | Some pathway -> Some { pathway with Name = name }
            | None ->
                if not quiet then
                    eprintfn "  Warning: unknown preset '%s' (available: %s)" presetKey presetNames
                None
        | None ->
            match get "reactant_atoms", get "ts_atoms", get "product_atoms" with
            | Some rAtoms, Some tsAtoms, Some pAtoms ->
                let enzyme = get "enzyme" |> Option.defaultValue "Unknown"
                let desc = get "description" |> Option.defaultValue ""
                let reactant = moleculeFromAtomString (name + " reactant") rAtoms
                let ts = moleculeFromAtomString (name + " TS") tsAtoms
                let product = moleculeFromAtomString (name + " product") pAtoms
                Some
                    { Name = name
                      Reactants = [ reactant ]
                      TransitionState = ts
                      Product = product
                      Enzyme = enzyme
                      Description = desc }
            | _ ->
                if not quiet then
                    eprintfn "  Warning: row '%s' missing required columns" name
                None)

// ==============================================================================
// PATHWAY SELECTION
// ==============================================================================

let pathways : MetabolicPathway list =
    let allPathways =
        match inputFile with
        | Some path ->
            let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
            if not quiet then
                printfn "Loading pathways from: %s" resolved
            loadPathwaysFromCsv resolved
        | None ->
            builtinPresets |> Map.toList |> List.map snd

    match pathwayFilter with
    | [] -> allPathways
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        allPathways
        |> List.filter (fun p ->
            let key = p.Name.ToLowerInvariant().Replace(" ", "-")
            filterSet |> Set.exists (fun f -> key.Contains f))

if List.isEmpty pathways then
    eprintfn "Error: No pathways selected. Available presets: %s" presetNames
    exit 1

// ==============================================================================
// QUANTUM BACKEND (Rule 1: all VQE via IQuantumBackend)
// ==============================================================================

let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Drug Metabolism: Reaction Pathway Comparison"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:      %s" backend.Name
    printfn "  Pathways:     %d" pathways.Length
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

/// Interpret an activation energy barrier for metabolic context.
let private assessBarrier (eaKcal: float) : string =
    if eaKcal < 0.0 then "Negative Ea (illustrative only)"
    elif eaKcal < 5.0 then "Very fast (diffusion-limited)"
    elif eaKcal < 15.0 then "Fast (typical enzymatic)"
    elif eaKcal < 25.0 then "Moderate (rate-determining step)"
    elif eaKcal < 35.0 then "Slow (catalyst essential)"
    else "Very slow (unlikely pathway)"

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

/// Compute the full energy profile for one metabolic pathway.
let private computePathway
    (backend: IQuantumBackend)
    (maxIter: int)
    (tol: float)
    (idx: int)
    (total: int)
    (pathway: MetabolicPathway)
    : PathwayResult =
    if not quiet then
        printfn "  [%d/%d] %s (%s)" (idx + 1) total pathway.Name pathway.Enzyme
        printfn "         %s" pathway.Description

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
        pathway.Reactants
        |> List.sumBy (fun mol ->
            let (e, _) = unwrapEnergy "reactant" mol.Name (computeEnergy backend maxIter tol mol)
            e)

    let (tsE, _) = unwrapEnergy "TS" pathway.TransitionState.Name (computeEnergy backend maxIter tol pathway.TransitionState)
    let (prodE, _) = unwrapEnergy "product" pathway.Product.Name (computeEnergy backend maxIter tol pathway.Product)

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
            printfn "         => INCOMPLETE (VQE failure - energies are unreliable)"
        else
            printfn "         => Ea = %.2f kcal/mol  |  dE = %.2f kcal/mol  |  k = %.2e /s" eaKcal dEKcal rateK
        printfn ""

    { Pathway = pathway
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

// --- Run all pathways ---

if not quiet then
    printfn "Computing energy profiles..."
    printfn ""

let results =
    pathways
    |> List.mapi (fun i pathway -> computePathway backend maxIterations tolerance i pathways.Length pathway)

// Sort by activation energy ascending (lowest positive barrier = fastest pathway).
// Failed pathways sink to bottom; negative Ea flagged as illustrative.
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
    printfn "  Ranked Metabolic Pathways (by activation energy)"
    printfn "=================================================================="
    printfn ""
    printfn "  %-4s  %-22s  %-8s  %13s  %11s  %10s  %s"
        "#" "Pathway" "Enzyme" "Ea (kcal/mol)" "Rate (/s)" "Half-life" "Assessment"
    printfn "  %s" (String('=', 100))

    ranked
    |> List.iteri (fun i r ->
        printfn "  %-4d  %-22s  %-8s  %13.2f  %11.2e  %10s  %s"
            (i + 1)
            r.Pathway.Name
            r.Pathway.Enzyme
            r.ActivationEnergyKcal
            r.RateConstant
            r.HalfLife
            r.BarrierAssessment)

    printfn ""

    // Thermodynamics column
    printfn "  %-4s  %-22s  %-8s  %13s  %11s  %s"
        "#" "Pathway" "Enzyme" "dE (kcal/mol)" "Time (s)" "Thermodynamics"
    printfn "  %s" (String('-', 85))

    ranked
    |> List.iteri (fun i r ->
        let thermo = if r.ReactionEnergyKcal < 0.0 then "exothermic" else "endothermic"
        printfn "  %-4d  %-22s  %-8s  %13.2f  %11.1f  %s"
            (i + 1) r.Pathway.Name r.Pathway.Enzyme r.ReactionEnergyKcal r.ComputeTimeSeconds thermo)

    printfn ""

// Always print the ranked comparison table â€” that's the primary output of this tool,
// even in --quiet mode (which only suppresses per-pathway progress output).
printTable ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    let best = ranked |> List.head
    let totalTime = results |> List.sumBy (fun r -> r.ComputeTimeSeconds)
    printfn "  Best pathway:  %s via %s (Ea = %.2f kcal/mol)" best.Pathway.Name best.Pathway.Enzyme best.ActivationEnergyKcal
    printfn "  Total time:    %.1f seconds" totalTime
    printfn "  Quantum:       all VQE via IQuantumBackend [Rule 1 compliant]"
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    ranked
    |> List.mapi (fun i r ->
        [ "rank", string (i + 1)
          "pathway", r.Pathway.Name
          "enzyme", r.Pathway.Enzyme
          "description", r.Pathway.Description
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
        [ "rank"; "pathway"; "enzyme"; "description"; "ea_hartree"; "ea_kcal_mol"
          "de_kcal_mol"; "rate_constant_s"; "half_life"; "barrier_assessment"
          "thermodynamics"; "reactant_energy_ha"; "ts_energy_ha"; "product_energy_ha"
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
    printfn "     --pathways hydroxylation,sulfoxidation   Run specific pathways"
    printfn "     --input pathways.csv                     Load custom pathways from CSV"
    printfn "     --csv results.csv                        Export ranked table as CSV"
    printfn ""
