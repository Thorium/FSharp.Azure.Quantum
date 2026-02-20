// ==============================================================================
// Mott Metal-Insulator Transition
// ==============================================================================
// VQE simulation of the Mott transition — the abrupt change from metal to
// insulator driven by electron correlation. Applies Mott criterion and Hubbard
// model parameters across multiple materials, plus H2 dissociation as the
// textbook Mott transition model.
//
// Usage:
//   dotnet fsi MottTransition.fsx                                         (defaults)
//   dotnet fsi MottTransition.fsx -- --help                               (show options)
//   dotnet fsi MottTransition.fsx -- --materials Si,VO2                   (select materials)
//   dotnet fsi MottTransition.fsx -- --input custom-materials.csv
//   dotnet fsi MottTransition.fsx -- --hopping 0.2
//   dotnet fsi MottTransition.fsx -- --quiet --output results.json --csv out.csv
//
// References:
//   [1] Sutton, "Concepts of Materials Science" Ch.8 (Oxford, 2021)
//   [2] Mott, "Metal-Insulator Transitions" (Taylor and Francis, 1990)
//   [3] https://en.wikipedia.org/wiki/Mott_insulator
// ==============================================================================

#load "_materialsCommon.fsx"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.QuantumChemistry
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common
open _materialsCommon

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "MottTransition.fsx"
    "VQE simulation of Mott metal-insulator transition driven by electron correlation."
    [ { Cli.OptionSpec.Name = "materials"; Description = "Comma-separated material short names (Si, Ge, VO2, V2O3, NdNiO3)"; Default = None }
      { Cli.OptionSpec.Name = "input";     Description = "CSV file with custom material definitions";   Default = None }
      { Cli.OptionSpec.Name = "hopping";   Description = "Hubbard hopping parameter t (eV)";            Default = Some "0.1" }
      { Cli.OptionSpec.Name = "output";    Description = "Write results to JSON file";                   Default = None }
      { Cli.OptionSpec.Name = "csv";       Description = "Write results to CSV file";                    Default = None }
      { Cli.OptionSpec.Name = "quiet";     Description = "Suppress informational output";                Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let t_estimate = args |> Cli.getFloatOr "hopping" 0.1
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

/// Material properties relevant for Mott transition
type MottMaterial = {
    Name: string
    ShortName: string
    DielectricConstant: float
    EffectiveMass: float
    CriticalDensity: float
    TransitionType: string
    TransitionTemp: float option
}

// ==============================================================================
// PHYSICAL CONSTANTS
// ==============================================================================

/// Bohr radius (Angstroms)
let a_0_A = 0.529177

// ==============================================================================
// BUILT-IN MATERIAL PRESETS
// ==============================================================================

let private presetSi = {
    Name = "P-doped Silicon"; ShortName = "Si"
    DielectricConstant = 11.7; EffectiveMass = 0.26
    CriticalDensity = 3.7e18; TransitionType = "Doping"; TransitionTemp = None }

let private presetGe = {
    Name = "As-doped Germanium"; ShortName = "Ge"
    DielectricConstant = 16.0; EffectiveMass = 0.12
    CriticalDensity = 1.0e17; TransitionType = "Doping"; TransitionTemp = None }

let private presetVO2 = {
    Name = "Vanadium Dioxide (VO2)"; ShortName = "VO2"
    DielectricConstant = 36.0; EffectiveMass = 3.0
    CriticalDensity = 3.0e21; TransitionType = "Temperature"; TransitionTemp = Some 341.0 }

let private presetV2O3 = {
    Name = "Vanadium Sesquioxide (V2O3)"; ShortName = "V2O3"
    DielectricConstant = 12.0; EffectiveMass = 5.0
    CriticalDensity = 1.0e22; TransitionType = "Temperature"; TransitionTemp = Some 150.0 }

let private presetNdNiO3 = {
    Name = "Neodymium Nickelate (NdNiO3)"; ShortName = "NdNiO3"
    DielectricConstant = 20.0; EffectiveMass = 4.0
    CriticalDensity = 5.0e21; TransitionType = "Temperature"; TransitionTemp = Some 200.0 }

let private builtInMaterials =
    [ presetSi; presetGe; presetVO2; presetV2O3; presetNdNiO3 ]
    |> List.map (fun m -> m.ShortName.ToUpperInvariant(), m)
    |> Map.ofList

// ==============================================================================
// CSV LOADING
// ==============================================================================

let private loadMaterialsFromCsv (filePath: string) : MottMaterial list =
    let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ filePath
    let rows, errors = Data.readCsvWithHeaderWithErrors resolved
    if not (List.isEmpty errors) then
        eprintfn "WARNING: CSV parse errors in %s:" filePath
        errors |> List.iter (eprintfn "  %s")
    if rows.IsEmpty then failwithf "No valid rows in CSV %s" filePath
    rows |> List.mapi (fun i row ->
        let get key = row.Values |> Map.tryFind key |> Option.defaultValue ""
        match get "preset" with
        | p when not (String.IsNullOrWhiteSpace p) ->
            match builtInMaterials |> Map.tryFind (p.Trim().ToUpperInvariant()) with
            | Some m -> m
            | None -> failwithf "Unknown preset '%s' in CSV row %d" p (i + 1)
        | _ ->
            { Name              = let n = get "name" in if n = "" then failwithf "Missing name in CSV row %d" (i + 1) else n
              ShortName         = let s = get "short_name" in if s = "" then failwithf "Missing short_name in CSV row %d" (i + 1) else s
              DielectricConstant = get "dielectric_constant" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 10.0
              EffectiveMass     = get "effective_mass" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 1.0
              CriticalDensity   = get "critical_density" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 1.0e18
              TransitionType    = let t = get "transition_type" in if t = "" then "Doping" else t
              TransitionTemp    = get "transition_temp" |> fun s -> match Double.TryParse s with true, v -> Some v | _ -> None })

// ==============================================================================
// MATERIAL SELECTION
// ==============================================================================

let selectedMaterials =
    let base' =
        match Cli.tryGet "input" args with
        | Some csvFile -> loadMaterialsFromCsv csvFile
        | None -> builtInMaterials |> Map.toList |> List.map snd

    match Cli.getCommaSeparated "materials" args with
    | [] -> base'
    | filter ->
        let filterSet = filter |> List.map (fun s -> s.ToUpperInvariant()) |> Set.ofList
        base' |> List.filter (fun m -> filterSet.Contains(m.ShortName.ToUpperInvariant()))

if selectedMaterials.IsEmpty then
    eprintfn "ERROR: No materials selected. Check --materials filter or --input CSV."
    exit 1

// ==============================================================================
// MOTT CRITERION CALCULATIONS
// ==============================================================================

let effectiveBohrRadius (material: MottMaterial) : float =
    material.DielectricConstant * (1.0 / material.EffectiveMass) * a_0_A

let mottParameter (density_per_cm3: float) (effectiveBohr_A: float) : float =
    let n_per_A3 = density_per_cm3 * 1.0e-24
    Math.Pow(n_per_A3, 1.0/3.0) * effectiveBohr_A

let predictPhase (mottParam: float) : string =
    if mottParam > 0.26 then "METAL" else "INSULATOR"

let criticalDensity (effectiveBohr_A: float) : float =
    let n_c_per_A3 = Math.Pow(0.26 / effectiveBohr_A, 3.0)
    n_c_per_A3 * 1.0e24

// ==============================================================================
// HUBBARD MODEL PARAMETERS
// ==============================================================================

let hubbardU (material: MottMaterial) : float =
    let a_H = effectiveBohrRadius material * 1.0e-10
    let U_J = e_charge * e_charge / (4.0 * Math.PI * 8.854e-12 * material.DielectricConstant * a_H)
    U_J * J_to_eV

let bandwidth (hopping_eV: float) : float = 2.0 * 6.0 * hopping_eV

let correlationStrength (U_eV: float) (W_eV: float) : float = U_eV / W_eV

// ==============================================================================
// QUANTUM COMPUTATION
// ==============================================================================

if not quiet then
    printfn "Mott transition analysis: %d materials, hopping t=%.2f eV" selectedMaterials.Length t_estimate
    printfn ""

let mutable anyVqeFailure = false

/// Create H2 at varying separation — simplest Mott model
let createH2MottModel (separation_A: float) : Molecule =
    { Name = sprintf "H2_R=%.2fA" separation_A
      Atoms = [
          { Element = "H"; Position = (0.0, 0.0, 0.0) }
          { Element = "H"; Position = (separation_A, 0.0, 0.0) } ]
      Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
      Charge = 0; Multiplicity = 1 }

/// Run VQE for a molecule and return result row
let runVqe (label: string) (description: string) (molecule: Molecule) : Map<string, string> =
    if not quiet then
        printfn "  VQE: %s — %s" label molecule.Name

    match calculateVQEEnergy backend molecule with
    | Ok (energy, iterations, time) ->
        if not quiet then
            printfn "    Energy: %.6f Ha, Iterations: %d, Time: %.2f s" energy iterations time
        Map.ofList [
            "molecule", molecule.Name; "label", label
            "energy_hartree", sprintf "%.6f" energy
            "iterations", sprintf "%d" iterations
            "time_seconds", sprintf "%.2f" time
            "has_vqe_failure", "false" ]
    | Error msg ->
        anyVqeFailure <- true
        if not quiet then eprintfn "    Error: %s" msg
        Map.ofList [
            "molecule", molecule.Name; "label", label
            "energy_hartree", "N/A"; "iterations", "N/A"; "time_seconds", "N/A"
            "has_vqe_failure", "true" ]

// VQE: H2 dissociation as Mott transition model
let separations =
    [ (0.74, "Equilibrium (metallic)")
      (1.50, "Stretched (correlated)")
      (2.50, "Dissociating (Mott)")
      (4.00, "Separated (insulator)") ]

let vqeResults =
    separations
    |> List.map (fun (sep, regime) ->
        let model = createH2MottModel sep
        let label = sprintf "H2 R=%.2f A (%s)" sep regime
        let result = runVqe label regime model
        result |> Map.add "separation_A" (sprintf "%.2f" sep) |> Map.add "regime" regime)

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let printTable () =
    // Material properties table
    let divM = String('-', 112)
    printfn ""
    printfn "  Mott Materials (hopping t=%.2f eV)" t_estimate
    printfn "  %s" divM
    printfn "  %-6s %-26s %6s %6s %8s %10s %6s %6s %6s %10s"
        "Key" "Name" "eps_r" "m*/me" "a_H(A)" "n_c(/cm3)" "U(eV)" "W(eV)" "U/W" "Phase"
    printfn "  %s" divM
    for mat in selectedMaterials do
        let a_H = effectiveBohrRadius mat
        let U = hubbardU mat
        let W = bandwidth t_estimate
        let ratio = correlationStrength U W
        let phase = if ratio > 1.0 then "Insulator" else "Metal"
        printfn "  %-6s %-26s %6.1f %6.2f %8.1f %10.2e %6.2f %6.2f %6.2f %10s"
            mat.ShortName mat.Name mat.DielectricConstant mat.EffectiveMass a_H
            mat.CriticalDensity U W ratio phase
    printfn "  %s" divM

    // VQE results table
    let divV = String('-', 80)
    printfn ""
    printfn "  H2 Dissociation (Mott Transition Model)"
    printfn "  %s" divV
    printfn "  %-7s %-22s %12s %6s %8s %8s"
        "R(A)" "Regime" "Energy(Ha)" "Iters" "Time(s)" "Status"
    printfn "  %s" divV
    for r in vqeResults do
        let sep = r |> Map.tryFind "separation_A" |> Option.defaultValue "?"
        let regime = r |> Map.tryFind "regime" |> Option.defaultValue "?"
        let energy = r |> Map.tryFind "energy_hartree" |> Option.defaultValue "N/A"
        let iters = r |> Map.tryFind "iterations" |> Option.defaultValue "N/A"
        let time = r |> Map.tryFind "time_seconds" |> Option.defaultValue "N/A"
        let fail = r |> Map.tryFind "has_vqe_failure" |> Option.defaultValue "false"
        let status = if fail = "true" then "FAIL" else "OK"
        printfn "  %-7s %-22s %12s %6s %8s %8s" sep regime energy iters time status
    printfn "  %s" divV

printTable ()

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let materialRows =
    selectedMaterials
    |> List.map (fun mat ->
        let a_H = effectiveBohrRadius mat
        let U = hubbardU mat
        let W = bandwidth t_estimate
        let ratio = correlationStrength U W
        let phase = if ratio > 1.0 then "Insulator" else "Metal"
        Map.ofList [
            "material", mat.Name
            "short_name", mat.ShortName
            "dielectric_constant", sprintf "%.1f" mat.DielectricConstant
            "effective_mass", sprintf "%.2f" mat.EffectiveMass
            "effective_bohr_A", sprintf "%.1f" a_H
            "critical_density_per_cm3", sprintf "%.2e" mat.CriticalDensity
            "hubbard_U_eV", sprintf "%.2f" U
            "bandwidth_W_eV", sprintf "%.2f" W
            "U_over_W", sprintf "%.2f" ratio
            "predicted_phase", phase
            "hopping_t_eV", sprintf "%.2f" t_estimate
            "transition_type", mat.TransitionType
            "transition_temp_K", (match mat.TransitionTemp with Some t -> sprintf "%.0f" t | None -> "N/A")
            "has_vqe_failure", sprintf "%b" anyVqeFailure ])

let allResultRows = materialRows @ vqeResults

match outputPath with
| Some path ->
    Reporting.writeJson path allResultRows
    if not quiet then printfn "\nResults written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "material"; "short_name"; "dielectric_constant"; "effective_mass"
          "effective_bohr_A"; "critical_density_per_cm3"; "hubbard_U_eV"
          "bandwidth_W_eV"; "U_over_W"; "predicted_phase"; "hopping_t_eV"
          "transition_type"; "transition_temp_K"; "has_vqe_failure"
          "molecule"; "label"; "energy_hartree"; "iterations"
          "time_seconds"; "separation_A"; "regime" ]
    let rows =
        allResultRows
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()
