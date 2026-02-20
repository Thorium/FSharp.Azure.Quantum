// ==============================================================================
// Quantum Dot Energy Levels
// ==============================================================================
// VQE simulation of electronic energy levels in semiconductor quantum dots.
// Particle-in-a-box confinement model for size-tunable optical properties,
// plus CdSe/ZnS molecular cluster VQE as quantum dot building blocks.
//
// Usage:
//   dotnet fsi QuantumDot.fsx                                         (defaults)
//   dotnet fsi QuantumDot.fsx -- --help                               (show options)
//   dotnet fsi QuantumDot.fsx -- --materials CdSe,Si                  (select materials)
//   dotnet fsi QuantumDot.fsx -- --input custom-materials.csv
//   dotnet fsi QuantumDot.fsx -- --size 3.5
//   dotnet fsi QuantumDot.fsx -- --quiet --output results.json --csv out.csv
//
// References:
//   [1] Sutton, "Concepts of Materials Science" Ch.7 (Oxford, 2021)
//   [2] https://en.wikipedia.org/wiki/Quantum_dot
//   [3] Nobel Prize 2023: Bawendi, Brus, Ekimov
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
    "QuantumDot.fsx"
    "Quantum dot energy levels via particle-in-a-box model and VQE simulation."
    [ { Cli.OptionSpec.Name = "materials"; Description = "Comma-separated material short names (CdSe, InAs, Si, Ge)"; Default = None }
      { Cli.OptionSpec.Name = "input";     Description = "CSV file with custom material definitions";                  Default = None }
      { Cli.OptionSpec.Name = "size";      Description = "Dot size in nm for analysis";                                Default = Some "4.0" }
      { Cli.OptionSpec.Name = "output";    Description = "Write results to JSON file";                                 Default = None }
      { Cli.OptionSpec.Name = "csv";       Description = "Write results to CSV file";                                  Default = None }
      { Cli.OptionSpec.Name = "quiet";     Description = "Suppress informational output";                              Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let dotSize = args |> Cli.getFloatOr "size" 4.0
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

/// Semiconductor quantum dot material properties
type QDMaterial = {
    Name: string
    ShortName: string
    ElectronMass: float      // m_e*/m_e (conduction band)
    HoleMass: float          // m_h*/m_e (valence band)
    BulkBandGap: float       // eV
    DielectricConstant: float
}

// ==============================================================================
// BUILT-IN MATERIAL PRESETS
// ==============================================================================

let private presetCdSe = {
    Name = "CdSe (Cadmium Selenide)"; ShortName = "CdSe"
    ElectronMass = 0.13; HoleMass = 0.45; BulkBandGap = 1.74; DielectricConstant = 10.0 }

let private presetInAs = {
    Name = "InAs (Indium Arsenide)"; ShortName = "InAs"
    ElectronMass = 0.023; HoleMass = 0.41; BulkBandGap = 0.354; DielectricConstant = 15.15 }

let private presetSi = {
    Name = "Si (Silicon)"; ShortName = "Si"
    ElectronMass = 0.26; HoleMass = 0.36; BulkBandGap = 1.12; DielectricConstant = 11.7 }

let private presetGe = {
    Name = "Ge (Germanium)"; ShortName = "Ge"
    ElectronMass = 0.082; HoleMass = 0.28; BulkBandGap = 0.67; DielectricConstant = 16.0 }

let private builtInMaterials =
    [ presetCdSe; presetInAs; presetSi; presetGe ]
    |> List.map (fun m -> m.ShortName.ToUpperInvariant(), m)
    |> Map.ofList

// ==============================================================================
// CSV LOADING
// ==============================================================================

let private loadMaterialsFromCsv (filePath: string) : QDMaterial list =
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
              ElectronMass      = get "electron_mass" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 0.1
              HoleMass          = get "hole_mass" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 0.3
              BulkBandGap       = get "bulk_band_gap" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 1.0
              DielectricConstant = get "dielectric_constant" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 10.0 })

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
// PARTICLE-IN-A-BOX MODEL (Sutton Eq. 7.1)
// ==============================================================================

/// E = (h^2/8mL^2)(nx^2 + ny^2 + nz^2) in eV
let particleInBoxEnergy (size_nm: float) (effectiveMass: float) (nx: int) (ny: int) (nz: int) : float =
    let L = size_nm * nm_to_m
    let m_eff = effectiveMass * m_e
    let prefactor = h * h / (8.0 * m_eff * L * L)
    let quantumSum = float (nx * nx + ny * ny + nz * nz)
    prefactor * quantumSum * J_to_eV

/// Quantum confinement energy (electron + hole ground-state confinement)
let confinementEnergy (size_nm: float) (material: QDMaterial) : float =
    let E_e = particleInBoxEnergy size_nm material.ElectronMass 1 1 1
    let E_h = particleInBoxEnergy size_nm material.HoleMass 1 1 1
    E_e + E_h

/// Effective band gap with confinement and exciton correction
let effectiveBandGap (size_nm: float) (material: QDMaterial) : float =
    let E_e = particleInBoxEnergy size_nm material.ElectronMass 1 1 1
    let E_h = particleInBoxEnergy size_nm material.HoleMass 1 1 1
    let epsilon_0 = 8.854e-12
    let L = size_nm * nm_to_m
    let E_exciton = 1.8 * e_charge * e_charge / (4.0 * Math.PI * material.DielectricConstant * epsilon_0 * L)
    let E_exciton_eV = E_exciton * J_to_eV
    material.BulkBandGap + E_e + E_h - E_exciton_eV

/// Classify emission color from wavelength
let emissionColor (wavelength_nm: float) =
    if wavelength_nm > 650.0 then "Red"
    elif wavelength_nm > 590.0 then "Orange"
    elif wavelength_nm > 520.0 then "Green"
    elif wavelength_nm > 450.0 then "Blue"
    else "Violet"

// ==============================================================================
// QUANTUM COMPUTATION — VQE ON MOLECULAR CLUSTERS
// ==============================================================================

if not quiet then
    printfn "Quantum dot analysis: %d materials, dot size %.1f nm" selectedMaterials.Length dotSize
    printfn ""

let mutable anyVqeFailure = false

/// Cd-Se bond length (Angstroms)
let cdSeBondLength = 2.63

let createCdSeDimer () : Molecule =
    { Name = "CdSe_dimer"
      Atoms = [
          { Element = "Cd"; Position = (0.0, 0.0, 0.0) }
          { Element = "Se"; Position = (cdSeBondLength, 0.0, 0.0) } ]
      Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 2.0 } ]
      Charge = 0; Multiplicity = 1 }

let createCd2Se2Cluster () : Molecule =
    let d = cdSeBondLength / sqrt 2.0
    { Name = "Cd2Se2_cluster"
      Atoms = [
          { Element = "Cd"; Position = (0.0, 0.0, 0.0) }
          { Element = "Se"; Position = (d, d, 0.0) }
          { Element = "Cd"; Position = (2.0*d, 0.0, 0.0) }
          { Element = "Se"; Position = (d, -d, 0.0) } ]
      Bonds = [
          { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
          { Atom1 = 1; Atom2 = 2; BondOrder = 1.0 }
          { Atom1 = 2; Atom2 = 3; BondOrder = 1.0 }
          { Atom1 = 3; Atom2 = 0; BondOrder = 1.0 } ]
      Charge = 0; Multiplicity = 1 }

let createZnSDimer () : Molecule =
    { Name = "ZnS_dimer"
      Atoms = [
          { Element = "Zn"; Position = (0.0, 0.0, 0.0) }
          { Element = "S"; Position = (2.34, 0.0, 0.0) } ]
      Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 2.0 } ]
      Charge = 0; Multiplicity = 1 }

/// Run VQE for a molecule and return result row
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
    runVqe "CdSe Dimer" (sprintf "Cd-Se bond: %.2f A" cdSeBondLength) (createCdSeDimer())
    runVqe "Cd2Se2 Cluster" "Rhombus structure, 4 atoms" (createCd2Se2Cluster())
    runVqe "ZnS Dimer" "Comparison material, Zn-S bond: 2.34 A" (createZnSDimer())
]

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let printTable () =
    // Material confinement table
    let divM = String('-', 100)
    printfn ""
    printfn "  Quantum Dot Materials (size=%.1f nm)" dotSize
    printfn "  %s" divM
    printfn "  %-6s %-26s %6s %6s %8s %8s %10s %6s %8s"
        "Key" "Name" "m_e*" "m_h*" "Bulk(eV)" "QD(eV)" "Confine" "nm" "Color"
    printfn "  %s" divM
    for mat in selectedMaterials do
        let qdGap = effectiveBandGap dotSize mat
        let confine = qdGap - mat.BulkBandGap
        let wl = 1240.0 / qdGap
        let color = emissionColor wl
        printfn "  %-6s %-26s %6.3f %6.3f %8.3f %8.3f %10.3f %6.0f %8s"
            mat.ShortName mat.Name mat.ElectronMass mat.HoleMass mat.BulkBandGap qdGap confine wl color
    printfn "  %s" divM

    // VQE results table
    let divV = String('-', 80)
    printfn ""
    printfn "  VQE Molecular Cluster Results"
    printfn "  %s" divV
    printfn "  %-18s %-18s %12s %10s %6s %8s %8s"
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
        printfn "  %-18s %-18s %12s %10s %6s %8s %8s" mol lbl eHa eEv iters time status
    printfn "  %s" divV

printTable ()

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let materialRows =
    selectedMaterials
    |> List.map (fun mat ->
        let qdGap = effectiveBandGap dotSize mat
        let confine = qdGap - mat.BulkBandGap
        let wl = 1240.0 / qdGap
        Map.ofList [
            "material", mat.Name
            "short_name", mat.ShortName
            "electron_mass", sprintf "%.3f" mat.ElectronMass
            "hole_mass", sprintf "%.3f" mat.HoleMass
            "bulk_band_gap_eV", sprintf "%.3f" mat.BulkBandGap
            "qd_band_gap_eV", sprintf "%.3f" qdGap
            "confinement_eV", sprintf "%.3f" confine
            "wavelength_nm", sprintf "%.0f" wl
            "emission_color", emissionColor wl
            "dielectric_constant", sprintf "%.2f" mat.DielectricConstant
            "dot_size_nm", sprintf "%.1f" dotSize
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
        [ "material"; "short_name"; "electron_mass"; "hole_mass"
          "bulk_band_gap_eV"; "qd_band_gap_eV"; "confinement_eV"
          "wavelength_nm"; "emission_color"; "dielectric_constant"
          "dot_size_nm"; "has_vqe_failure"
          "molecule"; "label"; "energy_hartree"; "energy_eV"
          "iterations"; "time_seconds" ]
    let rows =
        allResultRows
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()
