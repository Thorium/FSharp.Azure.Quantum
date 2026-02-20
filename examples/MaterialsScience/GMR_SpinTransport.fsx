// ==============================================================================
// Giant Magnetoresistance (GMR) and Spin Transport
// ==============================================================================
// VQE simulation of GMR exchange coupling and spin-dependent transport.
// Mott two-current model, RKKY interlayer coupling, plus Fe2 dimer VQE
// for ferromagnetic vs antiferromagnetic spin-state energetics.
//
// Usage:
//   dotnet fsi GMR_SpinTransport.fsx                                   (defaults)
//   dotnet fsi GMR_SpinTransport.fsx -- --help                         (show options)
//   dotnet fsi GMR_SpinTransport.fsx -- --materials Fe,Co              (select ferromagnets)
//   dotnet fsi GMR_SpinTransport.fsx -- --input custom-materials.csv
//   dotnet fsi GMR_SpinTransport.fsx -- --layers 5 --temperature 300
//   dotnet fsi GMR_SpinTransport.fsx -- --quiet --output results.json --csv out.csv
//
// References:
//   [1] Sutton, "Concepts of Materials Science" Ch.7 (Oxford, 2021)
//   [2] https://en.wikipedia.org/wiki/Giant_magnetoresistance
//   [3] Baibich et al. PRL 61, 2472 (1988)
//   [4] Nobel Prize 2007: Fert & Gruenberg
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
    "GMR_SpinTransport.fsx"
    "VQE simulation of GMR exchange coupling and spin-dependent transport."
    [ { Cli.OptionSpec.Name = "materials";    Description = "Comma-separated ferromagnet short names (Fe, Co, Ni, Permalloy)"; Default = None }
      { Cli.OptionSpec.Name = "input";        Description = "CSV file with custom ferromagnet definitions";                    Default = None }
      { Cli.OptionSpec.Name = "layers";       Description = "Number of magnetic layers for multilayer calculation";            Default = Some "3" }
      { Cli.OptionSpec.Name = "temperature";  Description = "Temperature in Kelvin";                                          Default = Some "300" }
      { Cli.OptionSpec.Name = "output";       Description = "Write results to JSON file";                                     Default = None }
      { Cli.OptionSpec.Name = "csv";          Description = "Write results to CSV file";                                      Default = None }
      { Cli.OptionSpec.Name = "quiet";        Description = "Suppress informational output";                                  Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let numLayers = args |> Cli.getIntOr "layers" 3
let userTemperature = args |> Cli.getFloatOr "temperature" 300.0
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

/// Ferromagnetic material properties for GMR
type FerromagneticMaterial = {
    Name: string
    ShortName: string
    SpinPolarization: float     // P at Fermi level
    ExchangeSplitting: float    // eV
    MajorityResistivity: float  // uOhm*cm
    MinorityResistivity: float  // uOhm*cm
    CurieTemperature: float     // K
    MagneticMoment: float       // mu_B per atom
}

/// Non-magnetic spacer (display-only, not filtered)
type SpacerMaterial = {
    Name: string
    FermiWavevector: float  // nm^-1
    Resistivity: float      // uOhm*cm
}

// ==============================================================================
// BUILT-IN FERROMAGNET PRESETS
// ==============================================================================

let private presetFe = {
    Name = "Iron (Fe)"; ShortName = "Fe"
    SpinPolarization = 0.45; ExchangeSplitting = 2.2
    MajorityResistivity = 5.0; MinorityResistivity = 15.0
    CurieTemperature = 1043.0; MagneticMoment = 2.22 }

let private presetCo = {
    Name = "Cobalt (Co)"; ShortName = "Co"
    SpinPolarization = 0.42; ExchangeSplitting = 1.8
    MajorityResistivity = 4.0; MinorityResistivity = 12.0
    CurieTemperature = 1388.0; MagneticMoment = 1.72 }

let private presetNi = {
    Name = "Nickel (Ni)"; ShortName = "Ni"
    SpinPolarization = 0.33; ExchangeSplitting = 0.6
    MajorityResistivity = 3.0; MinorityResistivity = 8.0
    CurieTemperature = 627.0; MagneticMoment = 0.62 }

let private presetPermalloy = {
    Name = "Permalloy (Ni80Fe20)"; ShortName = "Permalloy"
    SpinPolarization = 0.38; ExchangeSplitting = 0.8
    MajorityResistivity = 4.0; MinorityResistivity = 10.0
    CurieTemperature = 850.0; MagneticMoment = 1.00 }

let private builtInMaterials =
    [ presetFe; presetCo; presetNi; presetPermalloy ]
    |> List.map (fun m -> m.ShortName.ToUpperInvariant(), m)
    |> Map.ofList

/// Reference spacer materials (display-only)
let private spacerCr = { Name = "Chromium (Cr)"; FermiWavevector = 12.0; Resistivity = 12.9 }
let private spacerCu = { Name = "Copper (Cu)"; FermiWavevector = 13.6; Resistivity = 1.7 }

// ==============================================================================
// CSV LOADING
// ==============================================================================

let private loadMaterialsFromCsv (filePath: string) : FerromagneticMaterial list =
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
            { Name                = let n = get "name" in if n = "" then failwithf "Missing name in CSV row %d" (i + 1) else n
              ShortName           = let s = get "short_name" in if s = "" then failwithf "Missing short_name in CSV row %d" (i + 1) else s
              SpinPolarization    = get "spin_polarization" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 0.3
              ExchangeSplitting   = get "exchange_splitting" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 1.0
              MajorityResistivity = get "majority_resistivity" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 4.0
              MinorityResistivity = get "minority_resistivity" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 12.0
              CurieTemperature    = get "curie_temperature" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 800.0
              MagneticMoment      = get "magnetic_moment" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 1.0 })

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
// MOTT TWO-CURRENT MODEL
// ==============================================================================

/// R_parallel = 2rR / (r + R)
let resistanceParallel (r: float) (bigR: float) : float =
    2.0 * r * bigR / (r + bigR)

/// R_antiparallel = (r + R) / 2
let resistanceAntiparallel (r: float) (bigR: float) : float =
    (r + bigR) / 2.0

/// GMR = (R_AP - R_P) / R_P
let gmrRatio (r: float) (bigR: float) : float =
    let R_P = resistanceParallel r bigR
    let R_AP = resistanceAntiparallel r bigR
    (R_AP - R_P) / R_P

/// Spin asymmetry alpha = R/r
let spinAsymmetry (r: float) (bigR: float) : float = bigR / r

// ==============================================================================
// RKKY INTERLAYER COUPLING
// ==============================================================================

/// J(d) ~ cos(2k_F * d) / d^2
let rkkyCoupling (spacer: SpacerMaterial) (thickness_nm: float) : float =
    Math.Cos(2.0 * spacer.FermiWavevector * thickness_nm) / (thickness_nm * thickness_nm)

let couplingType (coupling: float) : string =
    if coupling > 0.01 then "Ferromagnetic"
    elif coupling < -0.01 then "Antiferromagnetic"
    else "Weak"

// ==============================================================================
// QUANTUM COMPUTATION — VQE ON Fe2 DIMER
// ==============================================================================

if not quiet then
    printfn "GMR analysis: %d ferromagnets, %d layers, T=%.0f K" selectedMaterials.Length numLayers userTemperature
    printfn ""

let mutable anyVqeFailure = false

let feFeBondLength = 2.02

let createFe2Dimer (multiplicity: int) : Molecule =
    { Name = sprintf "Fe2_M%d" multiplicity
      Atoms = [
          { Element = "Fe"; Position = (0.0, 0.0, 0.0) }
          { Element = "Fe"; Position = (feFeBondLength, 0.0, 0.0) } ]
      Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
      Charge = 0; Multiplicity = multiplicity }

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

let highSpinResult = runVqe "Fe2 Septet (M=7)" "S=3, ferromagnetic" (createFe2Dimer 7)
let lowSpinResult = runVqe "Fe2 Singlet (M=1)" "S=0, antiferromagnetic" (createFe2Dimer 1)
let tripletResult = runVqe "Fe2 Triplet (M=3)" "S=1, intermediate" (createFe2Dimer 3)

let vqeResults = [highSpinResult; lowSpinResult; tripletResult]

let exchangeRow =
    let E_high = highSpinResult |> Map.tryFind "energy_hartree" |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
    let E_low = lowSpinResult |> Map.tryFind "energy_hartree" |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
    match E_high, E_low with
    | Some eH, Some eL ->
        let J_Ha = eL - eH
        let J_eV = J_Ha * hartreeToEV
        let J_meV = J_eV * 1000.0
        let J_K = J_eV * 11604.5
        let kind = if J_Ha > 0.0 then "Ferromagnetic" else "Antiferromagnetic"
        if not quiet then printfn "  Exchange coupling J = %.1f meV (%s)" J_meV kind
        Map.ofList [
            "quantity", "exchange_coupling"
            "J_hartree", sprintf "%.6f" J_Ha; "J_eV", sprintf "%.3f" J_eV
            "J_meV", sprintf "%.1f" J_meV; "J_K", sprintf "%.0f" J_K
            "coupling_type", kind; "has_vqe_failure", "false" ]
    | _ ->
        Map.ofList [ "quantity", "exchange_coupling"; "has_vqe_failure", "true" ]

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let printTable () =
    // GMR properties table
    let divG = String('-', 110)
    printfn ""
    printfn "  GMR Ferromagnetic Materials (layers=%d, T=%.0f K)" numLayers userTemperature
    printfn "  %s" divG
    printfn "  %-10s %-22s %6s %6s %8s %8s %8s %8s %6s"
        "Key" "Name" "r(uO)" "R(uO)" "R/r" "R_P" "R_AP" "GMR(%)" "P"
    printfn "  %s" divG
    for fm in selectedMaterials do
        let R_P = resistanceParallel fm.MajorityResistivity fm.MinorityResistivity
        let R_AP = resistanceAntiparallel fm.MajorityResistivity fm.MinorityResistivity
        let gmr = gmrRatio fm.MajorityResistivity fm.MinorityResistivity
        let alpha = spinAsymmetry fm.MajorityResistivity fm.MinorityResistivity
        printfn "  %-10s %-22s %6.1f %6.1f %8.2f %8.2f %8.2f %8.1f %6.2f"
            fm.ShortName fm.Name fm.MajorityResistivity fm.MinorityResistivity
            alpha R_P R_AP (gmr * 100.0) fm.SpinPolarization
    printfn "  %s" divG

    // RKKY coupling table (Cr spacer)
    let divR = String('-', 50)
    printfn ""
    printfn "  RKKY Coupling (Cr spacer)"
    printfn "  %s" divR
    printfn "  %6s %10s %18s" "d(nm)" "J(a.u.)" "Type"
    printfn "  %s" divR
    for d in [0.5; 0.8; 1.0; 1.5; 2.0; 3.0] do
        let J = rkkyCoupling spacerCr d
        let kind = couplingType J
        printfn "  %6.1f %10.3f %18s" d J kind
    printfn "  %s" divR

    // VQE results table
    let divV = String('-', 80)
    printfn ""
    printfn "  Fe2 VQE Exchange Coupling"
    printfn "  %s" divV
    printfn "  %-18s %-24s %12s %6s %8s %8s"
        "Molecule" "Label" "Energy(Ha)" "Iters" "Time(s)" "Status"
    printfn "  %s" divV
    for r in vqeResults do
        let mol = r |> Map.tryFind "molecule" |> Option.defaultValue "?"
        let lbl = r |> Map.tryFind "label" |> Option.defaultValue "?"
        let energy = r |> Map.tryFind "energy_hartree" |> Option.defaultValue "N/A"
        let iters = r |> Map.tryFind "iterations" |> Option.defaultValue "N/A"
        let time = r |> Map.tryFind "time_seconds" |> Option.defaultValue "N/A"
        let fail = r |> Map.tryFind "has_vqe_failure" |> Option.defaultValue "false"
        let status = if fail = "true" then "FAIL" else "OK"
        printfn "  %-18s %-24s %12s %6s %8s %8s" mol lbl energy iters time status
    printfn "  %s" divV

printTable ()

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let gmrRows =
    selectedMaterials
    |> List.map (fun fm ->
        let R_P = resistanceParallel fm.MajorityResistivity fm.MinorityResistivity
        let R_AP = resistanceAntiparallel fm.MajorityResistivity fm.MinorityResistivity
        let gmr = gmrRatio fm.MajorityResistivity fm.MinorityResistivity
        Map.ofList [
            "material", fm.Name; "short_name", fm.ShortName
            "majority_resistivity", sprintf "%.1f" fm.MajorityResistivity
            "minority_resistivity", sprintf "%.1f" fm.MinorityResistivity
            "spin_asymmetry", sprintf "%.2f" (spinAsymmetry fm.MajorityResistivity fm.MinorityResistivity)
            "spin_polarization", sprintf "%.2f" fm.SpinPolarization
            "R_parallel", sprintf "%.2f" R_P
            "R_antiparallel", sprintf "%.2f" R_AP
            "gmr_pct", sprintf "%.1f" (gmr * 100.0)
            "layers", sprintf "%d" numLayers
            "temperature_K", sprintf "%.0f" userTemperature
            "has_vqe_failure", sprintf "%b" anyVqeFailure ])

let allResultRows = gmrRows @ vqeResults @ [exchangeRow]

match outputPath with
| Some path ->
    Reporting.writeJson path allResultRows
    if not quiet then printfn "\nResults written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "material"; "short_name"; "majority_resistivity"; "minority_resistivity"
          "spin_asymmetry"; "spin_polarization"; "R_parallel"; "R_antiparallel"
          "gmr_pct"; "layers"; "temperature_K"; "has_vqe_failure"
          "molecule"; "label"; "energy_hartree"; "iterations"; "time_seconds"
          "quantity"; "J_hartree"; "J_eV"; "J_meV"; "J_K"; "coupling_type" ]
    let rows =
        allResultRows
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()
