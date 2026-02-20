// ==============================================================================
// Quantum Tunneling and Hydrogen Embrittlement
// ==============================================================================
// VQE simulation of quantum tunneling of hydrogen in metals. WKB barrier
// penetration, isotope effects (H/D/T), classical vs quantum diffusion regimes,
// plus FeH bond-length and spin-state VQE for exchange coupling in the lattice.
//
// Usage:
//   dotnet fsi HydrogenTunneling.fsx                                     (defaults)
//   dotnet fsi HydrogenTunneling.fsx -- --help                           (show options)
//   dotnet fsi HydrogenTunneling.fsx -- --metals Fe,Pd                   (select metals)
//   dotnet fsi HydrogenTunneling.fsx -- --input custom-metals.csv
//   dotnet fsi HydrogenTunneling.fsx -- --temperature 200
//   dotnet fsi HydrogenTunneling.fsx -- --quiet --output results.json --csv out.csv
//
// References:
//   [1] Sutton, "Concepts of Materials Science" Ch.6 (Oxford, 2021)
//   [2] https://en.wikipedia.org/wiki/Hydrogen_embrittlement
//   [3] Flynn & Stoneham, Phys. Rev. B 1, 3966 (1970)
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
    "HydrogenTunneling.fsx"
    "VQE simulation of quantum tunneling and hydrogen embrittlement in metals."
    [ { Cli.OptionSpec.Name = "metals";      Description = "Comma-separated metal short names (Fe, Ni, Pd, Ti, Steel)"; Default = None }
      { Cli.OptionSpec.Name = "input";       Description = "CSV file with custom metal definitions";                    Default = None }
      { Cli.OptionSpec.Name = "temperature"; Description = "Temperature in Kelvin for diffusion analysis";              Default = Some "300" }
      { Cli.OptionSpec.Name = "output";      Description = "Write results to JSON file";                                Default = None }
      { Cli.OptionSpec.Name = "csv";         Description = "Write results to CSV file";                                 Default = None }
      { Cli.OptionSpec.Name = "quiet";       Description = "Suppress informational output";                             Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let userTemperature = args |> Cli.getFloatOr "temperature" 300.0
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

/// Metal host properties for hydrogen diffusion
type MetalHost = {
    Name: string
    ShortName: string
    BarrierHeight: float    // eV
    BarrierWidth: float     // Angstroms
    AttemptFrequency: float // Hz
    LatticeConstant: float  // Angstroms
    HydrogenSolubility: float // atomic fraction at 1 atm, 300K
}

// ==============================================================================
// PHYSICAL CONSTANTS
// ==============================================================================

let m_H = 1.6735575e-27  // proton mass (kg)
let m_D = 2.0 * m_H      // deuterium
let m_T = 3.0 * m_H      // tritium

// ==============================================================================
// BUILT-IN METAL PRESETS
// ==============================================================================

let private presetFe = {
    Name = "Iron (Fe) - BCC"; ShortName = "Fe"
    BarrierHeight = 0.04; BarrierWidth = 1.2; AttemptFrequency = 1.0e13
    LatticeConstant = 2.87; HydrogenSolubility = 1.0e-8 }

let private presetNi = {
    Name = "Nickel (Ni) - FCC"; ShortName = "Ni"
    BarrierHeight = 0.41; BarrierWidth = 1.5; AttemptFrequency = 1.0e13
    LatticeConstant = 3.52; HydrogenSolubility = 1.0e-5 }

let private presetPd = {
    Name = "Palladium (Pd) - FCC"; ShortName = "Pd"
    BarrierHeight = 0.23; BarrierWidth = 1.4; AttemptFrequency = 1.0e13
    LatticeConstant = 3.89; HydrogenSolubility = 0.6 }

let private presetTi = {
    Name = "Titanium (Ti) - HCP"; ShortName = "Ti"
    BarrierHeight = 0.54; BarrierWidth = 1.6; AttemptFrequency = 1.0e13
    LatticeConstant = 2.95; HydrogenSolubility = 0.08 }

let private presetSteel = {
    Name = "Steel (Fe-C)"; ShortName = "Steel"
    BarrierHeight = 0.05; BarrierWidth = 1.3; AttemptFrequency = 1.0e13
    LatticeConstant = 2.87; HydrogenSolubility = 2.0e-8 }

let private builtInMetals =
    [ presetFe; presetNi; presetPd; presetTi; presetSteel ]
    |> List.map (fun m -> m.ShortName.ToUpperInvariant(), m)
    |> Map.ofList

// ==============================================================================
// CSV LOADING
// ==============================================================================

let private loadMetalsFromCsv (filePath: string) : MetalHost list =
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
            match builtInMetals |> Map.tryFind (p.Trim().ToUpperInvariant()) with
            | Some m -> m
            | None -> failwithf "Unknown preset '%s' in CSV row %d" p (i + 1)
        | _ ->
            { Name              = let n = get "name" in if n = "" then failwithf "Missing name in CSV row %d" (i + 1) else n
              ShortName         = let s = get "short_name" in if s = "" then failwithf "Missing short_name in CSV row %d" (i + 1) else s
              BarrierHeight     = get "barrier_height" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 0.1
              BarrierWidth      = get "barrier_width" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 1.5
              AttemptFrequency  = get "attempt_frequency" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 1.0e13
              LatticeConstant   = get "lattice_constant" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 3.0
              HydrogenSolubility = get "hydrogen_solubility" |> fun s -> match Double.TryParse s with true, v -> v | _ -> 1.0e-6 })

// ==============================================================================
// METAL SELECTION
// ==============================================================================

let selectedMetals =
    let base' =
        match Cli.tryGet "input" args with
        | Some csvFile -> loadMetalsFromCsv csvFile
        | None -> builtInMetals |> Map.toList |> List.map snd

    match Cli.getCommaSeparated "metals" args with
    | [] -> base'
    | filter ->
        let filterSet = filter |> List.map (fun s -> s.ToUpperInvariant()) |> Set.ofList
        base' |> List.filter (fun m -> filterSet.Contains(m.ShortName.ToUpperInvariant()))

if selectedMetals.IsEmpty then
    eprintfn "ERROR: No metals selected. Check --metals filter or --input CSV."
    exit 1

// ==============================================================================
// TUNNELING CALCULATIONS
// ==============================================================================

/// WKB tunneling probability: P = exp(-2 * kappa * w)
let tunnelingProbability (mass: float) (barrierHeight_eV: float) (barrierWidth_A: float) : float =
    let V0_J = barrierHeight_eV * eV_to_J
    let w_m = barrierWidth_A * A_to_m
    let kappa = Math.Sqrt(2.0 * mass * V0_J) / hbar
    Math.Exp(-2.0 * kappa * w_m)

/// Classical Arrhenius hopping rate
let classicalHoppingRate (metal: MetalHost) (T_Kelvin: float) : float =
    let E_a_J = metal.BarrierHeight * eV_to_J
    metal.AttemptFrequency * Math.Exp(-E_a_J / (k_B * T_Kelvin))

/// Quantum tunneling rate (temperature-independent)
let quantumTunnelingRate (metal: MetalHost) (mass: float) : float =
    let P_tunnel = tunnelingProbability mass metal.BarrierHeight metal.BarrierWidth
    metal.AttemptFrequency * P_tunnel

/// Effective diffusion coefficient: D = a^2 * rate / 6
let diffusionCoefficient (metal: MetalHost) (rate: float) : float =
    let a = metal.LatticeConstant * A_to_m
    a * a * rate / 6.0

/// Crossover temperature between classical and quantum regimes
let crossoverTemperature (attemptFrequency: float) : float =
    hbar * attemptFrequency / (2.0 * Math.PI * k_B)

// ==============================================================================
// QUANTUM COMPUTATION — VQE ON FeH
// ==============================================================================

if not quiet then
    printfn "Hydrogen tunneling analysis: %d metals, T=%.0f K" selectedMetals.Length userTemperature
    printfn ""

let mutable anyVqeFailure = false

let createFeHMolecule (bondLength: float) (multiplicity: int) : Molecule =
    { Name = sprintf "FeH (M=%d)" multiplicity
      Atoms = [
          { Element = "Fe"; Position = (0.0, 0.0, 0.0) }
          { Element = "H"; Position = (0.0, 0.0, bondLength) } ]
      Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
      Charge = 0; Multiplicity = multiplicity }

/// Run VQE and return result row
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

// FeH at different bond lengths (models interstitial potential landscape)
let bondLengthResults =
    [ (1.40, "Compressed (saddle point)")
      (1.63, "Equilibrium (trap site)")
      (2.00, "Extended (delocalized)") ]
    |> List.map (fun (bl, desc) ->
        let label = sprintf "FeH R=%.2f A" bl
        runVqe label desc (createFeHMolecule bl 4)
        |> Map.add "bond_length_A" (sprintf "%.2f" bl))

// Spin state comparison at equilibrium
let quartetResult = runVqe "FeH Quartet (M=4)" "S=3/2, ferromagnetic" (createFeHMolecule 1.63 4)
let doubletResult = runVqe "FeH Doublet (M=2)" "S=1/2, reduced moment" (createFeHMolecule 1.63 2)
let spinResults = [quartetResult; doubletResult]

let spinGapRow =
    let E_q = quartetResult |> Map.tryFind "energy_hartree" |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
    let E_d = doubletResult |> Map.tryFind "energy_hartree" |> Option.bind (fun s -> match Double.TryParse s with true, v -> Some v | _ -> None)
    match E_q, E_d with
    | Some eQ, Some eD ->
        let gap_meV = (eD - eQ) * hartreeToEV * 1000.0
        if not quiet then printfn "  Spin excitation energy: %.1f meV" gap_meV
        Map.ofList [
            "quantity", "spin_gap"; "spin_gap_meV", sprintf "%.1f" gap_meV
            "quartet_hartree", sprintf "%.6f" eQ; "doublet_hartree", sprintf "%.6f" eD
            "has_vqe_failure", "false" ]
    | _ ->
        Map.ofList [ "quantity", "spin_gap"; "has_vqe_failure", "true" ]

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let printTable () =
    // Tunneling properties table
    let divM = String('-', 120)
    printfn ""
    printfn "  Hydrogen Tunneling in Metals (T=%.0f K)" userTemperature
    printfn "  %s" divM
    printfn "  %-6s %-22s %6s %5s %10s %10s %10s %10s %10s %10s"
        "Key" "Name" "V0(eV)" "w(A)" "P_H" "P_D" "Q-Rate(Hz)" "C-Rate(Hz)" "D(m2/s)" "Regime"
    printfn "  %s" divM
    for metal in selectedMetals do
        let P_H = tunnelingProbability m_H metal.BarrierHeight metal.BarrierWidth
        let P_D = tunnelingProbability m_D metal.BarrierHeight metal.BarrierWidth
        let rate_H = quantumTunnelingRate metal m_H
        let classical = classicalHoppingRate metal userTemperature
        let D_H = diffusionCoefficient metal rate_H
        let regime = if rate_H > classical then "Quantum" else "Classical"
        printfn "  %-6s %-22s %6.2f %5.1f %10.2e %10.2e %10.2e %10.2e %10.2e %10s"
            metal.ShortName metal.Name metal.BarrierHeight metal.BarrierWidth
            P_H P_D rate_H classical D_H regime
    printfn "  %s" divM

    // VQE results table
    let divV = String('-', 80)
    printfn ""
    printfn "  FeH VQE Results"
    printfn "  %s" divV
    printfn "  %-22s %-26s %12s %6s %8s %8s"
        "Molecule" "Label" "Energy(Ha)" "Iters" "Time(s)" "Status"
    printfn "  %s" divV
    let allVqe = bondLengthResults @ spinResults
    for r in allVqe do
        let mol = r |> Map.tryFind "molecule" |> Option.defaultValue "?"
        let lbl = r |> Map.tryFind "label" |> Option.defaultValue "?"
        let energy = r |> Map.tryFind "energy_hartree" |> Option.defaultValue "N/A"
        let iters = r |> Map.tryFind "iterations" |> Option.defaultValue "N/A"
        let time = r |> Map.tryFind "time_seconds" |> Option.defaultValue "N/A"
        let fail = r |> Map.tryFind "has_vqe_failure" |> Option.defaultValue "false"
        let status = if fail = "true" then "FAIL" else "OK"
        printfn "  %-22s %-26s %12s %6s %8s %8s" mol lbl energy iters time status
    printfn "  %s" divV

printTable ()

// ==============================================================================
// STRUCTURED OUTPUT (JSON / CSV)
// ==============================================================================

let tunnelingRows =
    selectedMetals
    |> List.map (fun metal ->
        let P_H = tunnelingProbability m_H metal.BarrierHeight metal.BarrierWidth
        let P_D = tunnelingProbability m_D metal.BarrierHeight metal.BarrierWidth
        let rate_H = quantumTunnelingRate metal m_H
        let D_H = diffusionCoefficient metal rate_H
        let classical = classicalHoppingRate metal userTemperature
        let regime = if rate_H > classical then "Quantum" else "Classical"
        Map.ofList [
            "metal", metal.Name; "short_name", metal.ShortName
            "barrier_height_eV", sprintf "%.2f" metal.BarrierHeight
            "barrier_width_A", sprintf "%.1f" metal.BarrierWidth
            "P_H", sprintf "%.2e" P_H; "P_D", sprintf "%.2e" P_D
            "quantum_rate_Hz", sprintf "%.2e" rate_H
            "classical_rate_Hz", sprintf "%.2e" classical
            "diffusion_m2s", sprintf "%.2e" D_H
            "dominant_regime", regime
            "temperature_K", sprintf "%.0f" userTemperature
            "has_vqe_failure", sprintf "%b" anyVqeFailure ])

let vqeAllResults = bondLengthResults @ spinResults
let allResultRows = tunnelingRows @ vqeAllResults @ [spinGapRow]

match outputPath with
| Some path ->
    Reporting.writeJson path allResultRows
    if not quiet then printfn "\nResults written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "metal"; "short_name"; "barrier_height_eV"; "barrier_width_A"; "P_H"; "P_D"
          "quantum_rate_Hz"; "classical_rate_Hz"; "diffusion_m2s"; "dominant_regime"
          "temperature_K"; "has_vqe_failure"
          "molecule"; "label"; "energy_hartree"; "iterations"; "time_seconds"
          "bond_length_A"; "quantity"; "spin_gap_meV"; "quartet_hartree"; "doublet_hartree" ]
    let rows =
        allResultRows
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()
