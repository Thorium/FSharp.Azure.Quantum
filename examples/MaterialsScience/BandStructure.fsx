// ==============================================================================
// Band Structure and Semiconductor Properties
// ==============================================================================
// VQE simulation of semiconductor band structures and dopant chemistry.
// Free-electron Fermi energy, Varshni temperature dependence, Shockley-Queisser
// solar-cell efficiency, plus SiH4/PH3 dopant-precursor VQE.
//
// Usage:
//   dotnet fsi BandStructure.fsx                                         (defaults)
//   dotnet fsi BandStructure.fsx -- --help                               (show options)
//   dotnet fsi BandStructure.fsx -- --materials Si,GaAs                  (select materials)
//   dotnet fsi BandStructure.fsx -- --input custom-materials.csv
//   dotnet fsi BandStructure.fsx -- --temperature 77
//   dotnet fsi BandStructure.fsx -- --quiet --output results.json --csv out.csv
//
// References:
//   [1] Sutton, "Concepts of Materials Science" Ch.6 (Oxford, 2021)
//   [2] https://en.wikipedia.org/wiki/Electronic_band_structure
//   [3] Ashcroft & Mermin, "Solid State Physics" (1976)
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
    "BandStructure.fsx"
    "VQE simulation of semiconductor band structures and dopant chemistry."
    [ { Cli.OptionSpec.Name = "materials";    Description = "Comma-separated material short names (Si, Ge, GaAs, InP, CdTe, ZnO)"; Default = None }
      { Cli.OptionSpec.Name = "input";        Description = "CSV file with custom material definitions";                            Default = None }
      { Cli.OptionSpec.Name = "temperature";  Description = "Temperature in Kelvin for Varshni analysis";                           Default = Some "300" }
      { Cli.OptionSpec.Name = "output";       Description = "Write results to JSON file";                                           Default = None }
      { Cli.OptionSpec.Name = "csv";          Description = "Write results to CSV file";                                            Default = None }
      { Cli.OptionSpec.Name = "quiet";        Description = "Suppress informational output";                                        Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let userTemperature = args |> Cli.getFloatOr "temperature" 300.0
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

/// Semiconductor material properties
type Semiconductor = {
    Name: string
    ShortName: string
    BandGap: float          // eV at 300K
    BandGapType: string     // "Direct" or "Indirect"
    EffectiveMassE: float   // m_e*/m_e
    EffectiveMassH: float   // m_h*/m_e
    LatticeConstant: float  // Angstroms
    Varshni_alpha: float    // meV/K
    Varshni_beta: float     // K
}

/// Metal properties for free-electron model (display-only, not filtered)
type Metal = {
    Name: string
    ValenceElectrons: int
    LatticeConstant: float
    Structure: string
}

// ==============================================================================
// BUILT-IN SEMICONDUCTOR PRESETS
// ==============================================================================

let private presetSi = {
    Name = "Silicon (Si)"; ShortName = "Si"; BandGap = 1.12; BandGapType = "Indirect"
    EffectiveMassE = 0.26; EffectiveMassH = 0.36; LatticeConstant = 5.431
    Varshni_alpha = 0.473; Varshni_beta = 636.0 }

let private presetGe = {
    Name = "Germanium (Ge)"; ShortName = "Ge"; BandGap = 0.67; BandGapType = "Indirect"
    EffectiveMassE = 0.082; EffectiveMassH = 0.28; LatticeConstant = 5.658
    Varshni_alpha = 0.477; Varshni_beta = 235.0 }

let private presetGaAs = {
    Name = "Gallium Arsenide (GaAs)"; ShortName = "GaAs"; BandGap = 1.42; BandGapType = "Direct"
    EffectiveMassE = 0.067; EffectiveMassH = 0.45; LatticeConstant = 5.653
    Varshni_alpha = 0.541; Varshni_beta = 204.0 }

let private presetInP = {
    Name = "Indium Phosphide (InP)"; ShortName = "InP"; BandGap = 1.35; BandGapType = "Direct"
    EffectiveMassE = 0.077; EffectiveMassH = 0.60; LatticeConstant = 5.869
    Varshni_alpha = 0.363; Varshni_beta = 162.0 }

let private presetCdTe = {
    Name = "Cadmium Telluride (CdTe)"; ShortName = "CdTe"; BandGap = 1.44; BandGapType = "Direct"
    EffectiveMassE = 0.096; EffectiveMassH = 0.35; LatticeConstant = 6.482
    Varshni_alpha = 0.310; Varshni_beta = 108.0 }

let private presetZnO = {
    Name = "Zinc Oxide (ZnO)"; ShortName = "ZnO"; BandGap = 3.37; BandGapType = "Direct"
    EffectiveMassE = 0.24; EffectiveMassH = 0.59; LatticeConstant = 3.25
    Varshni_alpha = 0.72; Varshni_beta = 700.0 }

let private builtInMaterials =
    [ presetSi; presetGe; presetGaAs; presetInP; presetCdTe; presetZnO ]
    |> List.map (fun m -> m.ShortName.ToUpperInvariant(), m)
    |> Map.ofList

/// Reference metals for free-electron Fermi energy (display-only)
let private referenceMetals = [
    { Name = "Copper (Cu)"; ValenceElectrons = 1; LatticeConstant = 3.615; Structure = "FCC" }
    { Name = "Silver (Ag)"; ValenceElectrons = 1; LatticeConstant = 4.086; Structure = "FCC" }
    { Name = "Gold (Au)"; ValenceElectrons = 1; LatticeConstant = 4.078; Structure = "FCC" }
    { Name = "Aluminum (Al)"; ValenceElectrons = 3; LatticeConstant = 4.050; Structure = "FCC" }
]

// ==============================================================================
// CSV LOADING
// ==============================================================================

let private loadMaterialsFromCsv (filePath: string) : Semiconductor list =
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
            { Name            = let n = get "name" in if n = "" then failwithf "Missing name in CSV row %d" (i + 1) else n
              ShortName       = let s = get "short_name" in if s = "" then failwithf "Missing short_name in CSV row %d" (i + 1) else s
              BandGap         = get "band_gap" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 1.0
              BandGapType     = let t = get "band_gap_type" in if t = "" then "Direct" else t
              EffectiveMassE  = get "effective_mass_e" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 0.1
              EffectiveMassH  = get "effective_mass_h" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 0.3
              LatticeConstant = get "lattice_constant" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 5.0
              Varshni_alpha   = get "varshni_alpha" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 0.4
              Varshni_beta    = get "varshni_beta" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 300.0 })

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
// FREE ELECTRON MODEL
// ==============================================================================

/// Fermi energy: E_F = (hbar^2/2m)(3pi^2 n)^(2/3)
let fermiEnergy (electronDensity: float) : float =
    let kF = Math.Pow(3.0 * Math.PI * Math.PI * electronDensity, 1.0/3.0)
    hbar * hbar * kF * kF / (2.0 * m_e) * J_to_eV

/// Electron density from valence electrons and lattice constant (FCC)
let electronDensity (valenceElectrons: int) (latticeConstant_A: float) : float =
    let a = latticeConstant_A * A_to_m
    4.0 * float valenceElectrons / (a * a * a)

// ==============================================================================
// BAND GAP CALCULATIONS
// ==============================================================================

/// Temperature-dependent band gap (Varshni equation)
let bandGapVsTemperature (material: Semiconductor) (T_Kelvin: float) : float =
    let E_g0 = material.BandGap + material.Varshni_alpha * 300.0 * 300.0 / (300.0 + material.Varshni_beta) / 1000.0
    E_g0 - (material.Varshni_alpha / 1000.0) * T_Kelvin * T_Kelvin / (T_Kelvin + material.Varshni_beta)

/// Intrinsic carrier concentration
let intrinsicCarrierConcentration (material: Semiconductor) (T_Kelvin: float) : float =
    let E_g = bandGapVsTemperature material T_Kelvin
    let kT = k_B * T_Kelvin * J_to_eV
    let T_ratio = T_Kelvin / 300.0
    let N_c = 2.5e19 * Math.Pow(material.EffectiveMassE, 1.5) * Math.Pow(T_ratio, 1.5)
    let N_v = 2.5e19 * Math.Pow(material.EffectiveMassH, 1.5) * Math.Pow(T_ratio, 1.5)
    Math.Sqrt(N_c * N_v) * Math.Exp(-E_g / (2.0 * kT))

/// Shockley-Queisser single-junction efficiency (simplified)
let shockleyQueisserEfficiency (bandGap_eV: float) : float =
    let x = (bandGap_eV - 1.34) / 0.5
    0.33 * Math.Exp(-x * x / 2.0)

// ==============================================================================
// QUANTUM COMPUTATION — VQE ON DOPANT MOLECULES
// ==============================================================================

if not quiet then
    printfn "Band structure analysis: %d semiconductors, T=%.0f K" selectedMaterials.Length userTemperature
    printfn ""

let mutable anyVqeFailure = false

let createSiH4 () : Molecule =
    let a = 1.480 / sqrt 3.0
    { Name = "SiH4"
      Atoms = [
          { Element = "Si"; Position = (0.0, 0.0, 0.0) }
          { Element = "H"; Position = (a, a, a) }
          { Element = "H"; Position = (-a, -a, a) }
          { Element = "H"; Position = (-a, a, -a) }
          { Element = "H"; Position = (a, -a, -a) } ]
      Bonds = [
          { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
          { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
          { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 }
          { Atom1 = 0; Atom2 = 4; BondOrder = 1.0 } ]
      Charge = 0; Multiplicity = 1 }

let createPH3 () : Molecule =
    let bondLength = 1.42
    let angleRad = 93.5 * Math.PI / 180.0
    let h_coord = bondLength * cos(angleRad / 2.0)
    let r = bondLength * sin(angleRad / 2.0)
    { Name = "PH3"
      Atoms = [
          { Element = "P"; Position = (0.0, 0.0, 0.0) }
          { Element = "H"; Position = (r, 0.0, h_coord) }
          { Element = "H"; Position = (-r * 0.5, r * 0.866, h_coord) }
          { Element = "H"; Position = (-r * 0.5, -r * 0.866, h_coord) } ]
      Bonds = [
          { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
          { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
          { Atom1 = 0; Atom2 = 3; BondOrder = 1.0 } ]
      Charge = 0; Multiplicity = 1 }

let runVqe (label: string) (description: string) (molecule: Molecule) : Map<string, string> =
    if not quiet then
        printfn "  VQE: %s — %s" label molecule.Name

    match calculateVQEEnergy backend molecule with
    | Ok (energy, iterations, time) ->
        if not quiet then
            printfn "    Energy: %.6f Ha (%.3f eV), Iterations: %d, Time: %.2f s" energy (energy * hartreeToEV) iterations time
        Map.ofList [
            "molecule", molecule.Name; "label", label
            "energy_hartree", sprintf "%.6f" energy
            "energy_eV", sprintf "%.3f" (energy * hartreeToEV)
            "iterations", sprintf "%d" iterations
            "time_seconds", sprintf "%.2f" time
            "has_vqe_failure", "false" ]
    | Error msg ->
        anyVqeFailure <- true
        if not quiet then eprintfn "    Error: %s" msg
        Map.ofList [
            "molecule", molecule.Name; "label", label
            "energy_hartree", "N/A"; "energy_eV", "N/A"
            "iterations", "N/A"; "time_seconds", "N/A"
            "has_vqe_failure", "true" ]

let vqeResults = [
    runVqe "SiH4 (Silane)" "Si CVD precursor, tetrahedral" (createSiH4())
    runVqe "PH3 (Phosphine)" "n-type dopant precursor, pyramidal" (createPH3())
    runVqe "H2 at Si-H bond length" "Surface passivation model, 1.48 A" (Molecule.createH2 1.48)
]

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let printTable () =
    // Semiconductor properties table
    let divS = String('-', 115)
    printfn ""
    printfn "  Semiconductor Band Structure (T=%.0f K)" userTemperature
    printfn "  %s" divS
    printfn "  %-6s %-24s %8s %8s %6s %6s %8s %10s %8s %6s"
        "Key" "Name" "Gap(eV)" "Type" "m_e*" "m_h*" "Gap@T" "n_i(cm-3)" "SQ(%)" "Match"
    printfn "  %s" divS
    for semi in selectedMaterials do
        let E_g_T = bandGapVsTemperature semi userTemperature
        let n_i = intrinsicCarrierConcentration semi userTemperature
        let sqEff = shockleyQueisserEfficiency semi.BandGap
        let matchQ =
            if abs(semi.BandGap - 1.34) < 0.2 then "Best"
            elif abs(semi.BandGap - 1.34) < 0.4 then "Good"
            elif abs(semi.BandGap - 1.34) < 0.7 then "Fair"
            else "Poor"
        printfn "  %-6s %-24s %8.3f %8s %6.3f %6.3f %8.4f %10.2e %8.1f %6s"
            semi.ShortName semi.Name semi.BandGap semi.BandGapType
            semi.EffectiveMassE semi.EffectiveMassH E_g_T n_i (sqEff * 100.0) matchQ
    printfn "  %s" divS

    // Reference metals (display-only, not filtered)
    let divM = String('-', 70)
    printfn ""
    printfn "  Free Electron Fermi Energy (reference metals)"
    printfn "  %s" divM
    printfn "  %-16s %4s %10s %10s"
        "Metal" "Z" "n(m-3)" "E_F(eV)"
    printfn "  %s" divM
    for metal in referenceMetals do
        let n = electronDensity metal.ValenceElectrons metal.LatticeConstant
        let E_F = fermiEnergy n
        printfn "  %-16s %4d %10.2e %10.2f" metal.Name metal.ValenceElectrons n E_F
    printfn "  %s" divM

    // VQE results table
    let divV = String('-', 85)
    printfn ""
    printfn "  VQE Dopant Molecule Results"
    printfn "  %s" divV
    printfn "  %-18s %-28s %12s %10s %6s %8s %8s"
        "Molecule" "Label" "Energy(Ha)" "Energy(eV)" "Iters" "Time(s)" "Status"
    printfn "  %s" divV
    for r in vqeResults do
        let mol = r |> Map.tryFind "molecule" |> Option.defaultValue "?"
        let lbl = r |> Map.tryFind "label" |> Option.defaultValue "?"
        let eHa = r |> Map.tryFind "energy_hartree" |> Option.defaultValue "N/A"
        let eEv = r |> Map.tryFind "energy_eV" |> Option.defaultValue "N/A"
        let iters = r |> Map.tryFind "iterations" |> Option.defaultValue "N/A"
        let time = r |> Map.tryFind "time_seconds" |> Option.defaultValue "N/A"
        let fail = r |> Map.tryFind "has_vqe_failure" |> Option.defaultValue "false"
        let status = if fail = "true" then "FAIL" else "OK"
        printfn "  %-18s %-28s %12s %10s %6s %8s %8s" mol lbl eHa eEv iters time status
    printfn "  %s" divV

printTable ()

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let semiRows =
    selectedMaterials
    |> List.map (fun semi ->
        let E_g_T = bandGapVsTemperature semi userTemperature
        let n_i = intrinsicCarrierConcentration semi userTemperature
        let sqEff = shockleyQueisserEfficiency semi.BandGap
        Map.ofList [
            "material", semi.Name; "short_name", semi.ShortName
            "band_gap_eV", sprintf "%.4f" semi.BandGap
            "band_gap_type", semi.BandGapType
            "effective_mass_e", sprintf "%.3f" semi.EffectiveMassE
            "effective_mass_h", sprintf "%.3f" semi.EffectiveMassH
            "lattice_constant_A", sprintf "%.3f" semi.LatticeConstant
            "band_gap_at_T_eV", sprintf "%.4f" E_g_T
            "temperature_K", sprintf "%.0f" userTemperature
            "carrier_concentration_cm3", sprintf "%.2e" n_i
            "sq_efficiency_pct", sprintf "%.1f" (sqEff * 100.0)
            "has_vqe_failure", sprintf "%b" anyVqeFailure ])

let allResultRows = semiRows @ vqeResults

match outputPath with
| Some path ->
    Reporting.writeJson path allResultRows
    if not quiet then printfn "\nResults written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "material"; "short_name"; "band_gap_eV"; "band_gap_type"; "effective_mass_e"
          "effective_mass_h"; "lattice_constant_A"; "band_gap_at_T_eV"
          "temperature_K"; "carrier_concentration_cm3"; "sq_efficiency_pct"
          "has_vqe_failure"
          "molecule"; "label"; "energy_hartree"; "energy_eV"
          "iterations"; "time_seconds" ]
    let rows =
        allResultRows
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()
