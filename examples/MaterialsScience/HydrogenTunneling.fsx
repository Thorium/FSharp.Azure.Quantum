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
//   dotnet fsi HydrogenTunneling.fsx -- --svg                            (animated picture)
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
#load "../_common/SvgAnimation.fsx"

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
    [
        {
            Cli.OptionSpec.Name = "metals"
            Description = "Comma-separated metal short names (Fe, Ni, Pd, Ti, Steel)"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "input"
            Description = "CSV file with custom metal definitions"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "temperature"
            Description = "Temperature in Kelvin for diffusion analysis"
            Default = Some "300"
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
            Description = "Suppress informational output"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "svg"
            Description = "Draw an animated SVG (default path: _images/hydrogen-tunneling.svg)"
            Default = None
        }
    ]
    args

let quiet = Cli.hasFlag "quiet" args
let userTemperature = args |> Cli.getFloatOr "temperature" 300.0
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ==============================================================================
// DOMAIN TYPES
// ==============================================================================

/// Metal host properties for hydrogen diffusion
type MetalHost =
    {
        Name: string
        ShortName: string
        BarrierHeight: float // eV
        BarrierWidth: float // Angstroms
        AttemptFrequency: float // Hz
        LatticeConstant: float // Angstroms
        HydrogenSolubility: float // atomic fraction at 1 atm, 300K
    }

// ==============================================================================
// PHYSICAL CONSTANTS
// ==============================================================================

/// proton mass (kg)
[<Literal>]
let m_H = 1.6735575e-27

/// deuterium
let m_D = 2.0 * m_H
/// tritium
let m_T = 3.0 * m_H

// ==============================================================================
// BUILT-IN METAL PRESETS
// ==============================================================================

let private presetFe =
    {
        Name = "Iron (Fe) - BCC"
        ShortName = "Fe"
        BarrierHeight = 0.04
        BarrierWidth = 1.2
        AttemptFrequency = 1.0e13
        LatticeConstant = 2.87
        HydrogenSolubility = 1.0e-8
    }

let private presetNi =
    {
        Name = "Nickel (Ni) - FCC"
        ShortName = "Ni"
        BarrierHeight = 0.41
        BarrierWidth = 1.5
        AttemptFrequency = 1.0e13
        LatticeConstant = 3.52
        HydrogenSolubility = 1.0e-5
    }

let private presetPd =
    {
        Name = "Palladium (Pd) - FCC"
        ShortName = "Pd"
        BarrierHeight = 0.23
        BarrierWidth = 1.4
        AttemptFrequency = 1.0e13
        LatticeConstant = 3.89
        HydrogenSolubility = 0.6
    }

let private presetTi =
    {
        Name = "Titanium (Ti) - HCP"
        ShortName = "Ti"
        BarrierHeight = 0.54
        BarrierWidth = 1.6
        AttemptFrequency = 1.0e13
        LatticeConstant = 2.95
        HydrogenSolubility = 0.08
    }

let private presetSteel =
    {
        Name = "Steel (Fe-C)"
        ShortName = "Steel"
        BarrierHeight = 0.05
        BarrierWidth = 1.3
        AttemptFrequency = 1.0e13
        LatticeConstant = 2.87
        HydrogenSolubility = 2.0e-8
    }

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

    if rows.IsEmpty then
        failwithf "No valid rows in CSV %s" filePath

    rows
    |> List.mapi (fun i row ->
        let get key =
            row.Values |> Map.tryFind key |> Option.defaultValue ""

        match get "preset" with
        | p when not (String.IsNullOrWhiteSpace p) ->
            match builtInMetals |> Map.tryFind (p.Trim().ToUpperInvariant()) with
            | Some m -> m
            | None -> failwithf "Unknown preset '%s' in CSV row %d" p (i + 1)
        | _ ->
            {
                Name =
                    let n = get "name" in

                    if n = "" then
                        failwithf "Missing name in CSV row %d" (i + 1)
                    else
                        n
                ShortName =
                    let s = get "short_name" in

                    if s = "" then
                        failwithf "Missing short_name in CSV row %d" (i + 1)
                    else
                        s
                BarrierHeight =
                    get "barrier_height"
                    |> fun s ->
                        match Double.TryParse s with
                        | true, v -> v
                        | _ -> 0.1
                BarrierWidth =
                    get "barrier_width"
                    |> fun s ->
                        match Double.TryParse s with
                        | true, v -> v
                        | _ -> 1.5
                AttemptFrequency =
                    get "attempt_frequency"
                    |> fun s ->
                        match Double.TryParse s with
                        | true, v -> v
                        | _ -> 1.0e13
                LatticeConstant =
                    get "lattice_constant"
                    |> fun s ->
                        match Double.TryParse s with
                        | true, v -> v
                        | _ -> 3.0
                HydrogenSolubility =
                    get "hydrogen_solubility"
                    |> fun s ->
                        match Double.TryParse s with
                        | true, v -> v
                        | _ -> 1.0e-6
            })

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

        base'
        |> List.filter (fun m -> filterSet.Contains(m.ShortName.ToUpperInvariant()))

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
    {
        Name = $"FeH (M=%d{multiplicity})"
        Atoms =
            [
                {
                    Element = "Fe"
                    Position = (0.0, 0.0, 0.0)
                }
                {
                    Element = "H"
                    Position = (0.0, 0.0, bondLength)
                }
            ]
        Bonds =
            [
                {
                    Atom1 = 0
                    Atom2 = 1
                    BondOrder = 1.0
                }
            ]
        Charge = 0
        Multiplicity = multiplicity
    }

/// Run VQE and return result row
let runVqe (label: string) (description: string) (molecule: Molecule) : Map<string, string> =
    if not quiet then
        printfn "  VQE: %s — %s" label molecule.Name

    match calculateVQEEnergy backend molecule with
    | Ok(energy, iterations, time) ->
        if not quiet then
            printfn "    Energy: %.6f Ha, Iterations: %d, Time: %.2f s" energy iterations time

        Map.ofList
            [
                "molecule", molecule.Name
                "label", label
                "energy_hartree", $"%.6f{energy}"
                "iterations", $"%d{iterations}"
                "time_seconds", $"%.2f{time}"
                "has_vqe_failure", "false"
            ]
    | Error msg ->
        anyVqeFailure <- true

        if not quiet then
            eprintfn "    Error: %s" msg

        Map.ofList
            [
                "molecule", molecule.Name
                "label", label
                "energy_hartree", "N/A"
                "iterations", "N/A"
                "time_seconds", "N/A"
                "has_vqe_failure", "true"
            ]

// FeH at different bond lengths (models interstitial potential landscape)
let bondLengthResults =
    [
        (1.40, "Compressed (saddle point)")
        (1.63, "Equilibrium (trap site)")
        (2.00, "Extended (delocalized)")
    ]
    |> List.map (fun (bl, desc) ->
        let label = $"FeH R=%.2f{bl} A"

        runVqe label desc (createFeHMolecule bl 4)
        |> Map.add "bond_length_A" $"%.2f{bl}")

// Spin state comparison at equilibrium
let quartetResult =
    runVqe "FeH Quartet (M=4)" "S=3/2, ferromagnetic" (createFeHMolecule 1.63 4)

let doubletResult =
    runVqe "FeH Doublet (M=2)" "S=1/2, reduced moment" (createFeHMolecule 1.63 2)

let spinResults = [ quartetResult; doubletResult ]

let spinGapRow =
    let E_q =
        quartetResult
        |> Map.tryFind "energy_hartree"
        |> Option.bind (fun s ->
            match Double.TryParse s with
            | true, v -> Some v
            | _ -> None)

    let E_d =
        doubletResult
        |> Map.tryFind "energy_hartree"
        |> Option.bind (fun s ->
            match Double.TryParse s with
            | true, v -> Some v
            | _ -> None)

    match E_q, E_d with
    | Some eQ, Some eD ->
        let gap_meV = (eD - eQ) * hartreeToEV * 1000.0

        if not quiet then
            printfn "  Spin excitation energy: %.1f meV" gap_meV

        Map.ofList
            [
                "quantity", "spin_gap"
                "spin_gap_meV", $"%.1f{gap_meV}"
                "quartet_hartree", $"%.6f{eQ}"
                "doublet_hartree", $"%.6f{eD}"
                "has_vqe_failure", "false"
            ]
    | _ -> Map.ofList [ "quantity", "spin_gap"; "has_vqe_failure", "true" ]

// ==============================================================================
// COMPARISON TABLE (unconditional)
// ==============================================================================

let printTable () =
    // Tunneling properties table
    let divM = String('-', 120)
    printfn ""
    printfn "  Hydrogen Tunneling in Metals (T=%.0f K)" userTemperature
    printfn "  %s" divM

    printfn
        "  %-6s %-22s %6s %5s %10s %10s %10s %10s %10s %10s"
        "Key"
        "Name"
        "V0(eV)"
        "w(A)"
        "P_H"
        "P_D"
        "Q-Rate(Hz)"
        "C-Rate(Hz)"
        "D(m2/s)"
        "Regime"

    printfn "  %s" divM

    for metal in selectedMetals do
        let P_H = tunnelingProbability m_H metal.BarrierHeight metal.BarrierWidth
        let P_D = tunnelingProbability m_D metal.BarrierHeight metal.BarrierWidth
        let rate_H = quantumTunnelingRate metal m_H
        let classical = classicalHoppingRate metal userTemperature
        let D_H = diffusionCoefficient metal rate_H
        let regime = if rate_H > classical then "Quantum" else "Classical"

        printfn
            "  %-6s %-22s %6.2f %5.1f %10.2e %10.2e %10.2e %10.2e %10.2e %10s"
            metal.ShortName
            metal.Name
            metal.BarrierHeight
            metal.BarrierWidth
            P_H
            P_D
            rate_H
            classical
            D_H
            regime

    printfn "  %s" divM

    // VQE results table
    let divV = String('-', 80)
    printfn ""
    printfn "  FeH VQE Results"
    printfn "  %s" divV
    printfn "  %-22s %-26s %12s %6s %8s %8s" "Molecule" "Label" "Energy(Ha)" "Iters" "Time(s)" "Status"
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

        Map.ofList
            [
                "metal", metal.Name
                "short_name", metal.ShortName
                "barrier_height_eV", $"%.2f{metal.BarrierHeight}"
                "barrier_width_A", $"%.1f{metal.BarrierWidth}"
                "P_H", $"%.2e{P_H}"
                "P_D", $"%.2e{P_D}"
                "quantum_rate_Hz", $"%.2e{rate_H}"
                "classical_rate_Hz", $"%.2e{classical}"
                "diffusion_m2s", $"%.2e{D_H}"
                "dominant_regime", regime
                "temperature_K", $"%.0f{userTemperature}"
                "has_vqe_failure", $"%b{anyVqeFailure}"
            ])

let vqeAllResults = bondLengthResults @ spinResults
let allResultRows = tunnelingRows @ vqeAllResults @ [ spinGapRow ]

match outputPath with
| Some path ->
    Reporting.writeJson path allResultRows

    if not quiet then
        printfn "\nResults written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [
            "metal"
            "short_name"
            "barrier_height_eV"
            "barrier_width_A"
            "P_H"
            "P_D"
            "quantum_rate_Hz"
            "classical_rate_Hz"
            "diffusion_m2s"
            "dominant_regime"
            "temperature_K"
            "has_vqe_failure"
            "molecule"
            "label"
            "energy_hartree"
            "iterations"
            "time_seconds"
            "bond_length_A"
            "quantity"
            "spin_gap_meV"
            "quartet_hartree"
            "doublet_hartree"
        ]

    let rows =
        allResultRows
        |> List.map (fun m -> header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))

    Reporting.writeCsv path header rows

    if not quiet then
        printfn "Results written to %s" path
| None -> ()

// ==============================================================================
// ANIMATED PICTURE (--svg [path])
// ==============================================================================
// The table gives tunnelling as a WKB transmission per attempt. The picture
// shows the coherent motion for the selected metal with the fastest WKB rate:
// a proton in a symmetric double well whose barrier has that metal's height V0
// and width w, between harmonic wells at its attempt frequency. The two lowest
// states come from exact diagonalisation on a 1D grid (even and odd states on
// the half line). A proton started in the left well is their equal mix; its
// density is cos^2(phi/2) rho_left + sin^2(phi/2) rho_right with
// phi = splitting * t / hbar, so one pass from well to well takes pi hbar / splitting.

open SvgAnimation

/// hbar in eV s.
let hbar_eVs = hbar / eV_to_J

/// The double well of a metal, in eV against position in Å: the flat barrier
/// of height V0 and width w, then parabolas m_H omega^2 x^2 / 2 with
/// omega = 2 pi nu, meeting the barrier top at its edges.
let doubleWell (metal: MetalHost) =
    let curvature =
        m_H * (2.0 * Math.PI * metal.AttemptFrequency) ** 2.0 * A_to_m * A_to_m
        / eV_to_J

    let flank = sqrt (2.0 * metal.BarrierHeight / curvature)
    let centre = metal.BarrierWidth / 2.0 + flank

    let potential (x: float) =
        if abs x < metal.BarrierWidth / 2.0 then
            metal.BarrierHeight
        else
            0.5 * curvature * (abs x - centre) ** 2.0

    potential, centre, flank

/// Lowest eigenvalue and eigenvector of a symmetric tridiagonal matrix with
/// diagonal `d` and constant off-diagonal `e`: Sturm-count bisection, then
/// inverse iteration.
let lowestState (d: float[]) (e: float) =
    let n = d.Length

    let countBelow lambda =
        let mutable count = 0
        let mutable q = 1.0

        for i in 0 .. n - 1 do
            q <- d.[i] - lambda - (if i = 0 then 0.0 else e * e / q)

            if q = 0.0 then
                q <- 1e-300

            if q < 0.0 then
                count <- count + 1

        count

    let mutable lo = Array.min d - 2.0 * abs e
    let mutable hi = Array.max d + 2.0 * abs e

    for _ in 1..200 do
        let mid = 0.5 * (lo + hi)

        if countBelow mid >= 1 then hi <- mid else lo <- mid

    let lambda = 0.5 * (lo + hi)

    // (T - lambda) x = b by the Thomas algorithm.
    let solve (b: float[]) =
        let c = Array.zeroCreate n
        let y = Array.zeroCreate n
        let x = Array.zeroCreate n

        for i in 0 .. n - 1 do
            let pivot =
                let p = d.[i] - lambda - (if i = 0 then 0.0 else e * c.[i - 1])
                if p = 0.0 then 1e-300 else p

            c.[i] <- e / pivot
            y.[i] <- (b.[i] - (if i = 0 then 0.0 else e * y.[i - 1])) / pivot

        x.[n - 1] <- y.[n - 1]

        for i in n - 2 .. -1 .. 0 do
            x.[i] <- y.[i] - c.[i] * x.[i + 1]

        let scale = x |> Array.maxBy abs
        x |> Array.map (fun v -> v / scale)

    let vector = Seq.fold (fun v _ -> solve v) (Array.create n 1.0) [ 1..4 ]
    lambda, vector

/// The ground pair of the double well for a particle of `mass` on the half
/// line x > 0: energies (eV) and states normalised over the whole line.
let groundPair (potential: float -> float) (extent: float) (mass: float) =
    let points = 2000
    let step = extent / float points
    let kinetic = hbar * hbar / (2.0 * mass) / eV_to_J / (A_to_m * A_to_m) // eV Å²
    let hop = kinetic / (step * step)
    let xs = Array.init points (fun j -> (float j + 0.5) * step)
    let diagonal = xs |> Array.map (fun x -> 2.0 * hop + potential x)

    let solveWith (first: float) =
        let d = Array.copy diagonal
        d.[0] <- first + potential xs.[0]
        let energy, state = lowestState d (-hop)
        let norm = sqrt (2.0 * step * (state |> Array.sumBy (fun v -> v * v)))
        energy, state |> Array.map (fun v -> v / norm)

    // Even states mirror at x = 0 (psi(-x) = psi(x)), odd states flip sign.
    let evenEnergy, even = solveWith hop
    let oddEnergy, odd = solveWith (3.0 * hop)
    step, evenEnergy, even, oddEnergy, odd

/// A value on the grid at |x| by linear interpolation, zero beyond it.
let onGrid (step: float) (values: float[]) (x: float) =
    let s = abs x / step - 0.5

    if s <= 0.0 then
        values.[0]
    elif s >= float (values.Length - 1) then
        0.0
    else
        let j = int s
        let f = s - float j
        values.[j] * (1.0 - f) + values.[j + 1] * f

/// A quantity in the unit (from smallest up) that keeps it at 1 or more.
let withUnit (units: (float * string) list) (value: float) =
    let unit, name =
        units
        |> List.rev
        |> List.tryFind (fun (u, _) -> value >= u)
        |> Option.defaultValue units.Head

    sprintf "%.3g %s" (value / unit) name

let duration =
    withUnit [ 1e-15, "fs"; 1e-12, "ps"; 1e-9, "ns"; 1e-6, "µs"; 1e-3, "ms"; 1.0, "s" ]

let energyText = withUnit [ 1e-9, "neV"; 1e-6, "µeV"; 1e-3, "meV"; 1.0, "eV" ]

/// A tick spacing of 1, 2 or 5 times a power of ten, giving about `count` ticks.
let niceStep (range: float) (count: int) =
    let raw = range / float count
    let magnitude = 10.0 ** floor (log10 raw)

    [ 1.0; 2.0; 5.0; 10.0 ]
    |> List.map (fun f -> f * magnitude)
    |> List.find (fun s -> s >= raw)

/// Splittings below this (eV) are lost in the solver's rounding.
[<Literal>]
let smallestSplitting = 1e-9

let drawTunnelling (path: string) (metal: MetalHost) =
    let potential, centre, flank = doubleWell metal

    let oscillatorLength =
        sqrt (hbar / (m_H * 2.0 * Math.PI * metal.AttemptFrequency)) / A_to_m

    let extent = centre + max flank (8.0 * oscillatorLength)
    let step, e0, even, e1, odd = groundPair potential extent m_H
    let splitting = e1 - e0
    let _, d0, _, d1, _ = groundPair potential extent m_D
    let splittingD = d1 - d0

    if splitting < smallestSplitting then
        eprintfn
            "No picture drawn: the %s splitting (%s) is below what the grid solver resolves (1 neV)."
            metal.ShortName
            (energyText splitting)
    else
        let passTime = Math.PI * hbar_eVs / splitting

        if not quiet then
            printfn ""

            printfn
                "  Double well for %s: ground level %.2f meV, splitting %s"
                metal.ShortName
                (e0 * 1000.0)
                (energyText splitting)

            printfn "    Well-to-well tunnelling time (pi hbar / splitting): %s" (duration passTime)

            if splittingD >= smallestSplitting then
                printfn "    Deuterium in the same wells: %s" (duration (Math.PI * hbar_eVs / splittingD))

        // One full cycle, left to right and back; the loop closes it.
        let frames = 30
        let timeAt k = 2.0 * passTime * float k / float frames
        let phase k = splitting * timeAt k / hbar_eVs
        let rightWeight k = sin (phase k / 2.0) ** 2.0

        // Probability in the right well: 1/2 - S cos(phi), S = overlap of the pair on x > 0.
        let overlap = step * Array.fold2 (fun acc a b -> acc + a * b) 0.0 even odd
        let pRight k = 0.5 - overlap * cos (phase k)

        let rhoLeft x =
            let v = onGrid step even x - float (sign x) * onGrid step odd x
            0.5 * v * v

        let rhoRight x =
            let v = onGrid step even x + float (sign x) * onGrid step odd x
            0.5 * v * v

        let width, height = 760.0, 500.0
        let x0, x1, yTop, yBottom = 64.0, 500.0, 104.0, 364.0
        let xMax = centre + 1.5 * flank
        let eMax = potential xMax

        let px x =
            x0 + (x1 - x0) * (x + xMax) / (2.0 * xMax)

        let py (eV: float) = yBottom - (yBottom - yTop) * eV / eMax

        let title = $"Hydrogen tunnelling in %s{metal.ShortName}: one proton, two sites"
        let pic = Picture(width, height, frames, 16.0, title, hold = 0.05)
        pic.Text(24.0, 30.0, title, size = 18.0, bold = true)

        pic.Text(
            24.0,
            50.0,
            "Classically the proton would stay in one well. Quantum mechanically it leaks through the barrier and back.",
            size = 12.5,
            fill = grey
        )

        pic.Rect(x0, yTop, x1 - x0, yBottom - yTop, fill = panel, stroke = frameColour)

        let eStep = niceStep (eMax * 1000.0) 5

        for i in 0 .. int (floor (eMax * 1000.0 / eStep + 1e-9)) do
            let meV = float i * eStep
            pic.Line(x0, py (meV / 1000.0), x1, py (meV / 1000.0), stroke = light)
            pic.Text(x0 - 6.0, py (meV / 1000.0) + 4.0, $"%g{meV}", size = 11.0, fill = grey, anchor = "end")

        let xStep = niceStep (2.0 * xMax) 6

        for i in -int(floor (xMax / xStep)) .. int (floor (xMax / xStep)) do
            let x = float i * xStep
            pic.Line(px x, yBottom, px x, yBottom + 4.0, stroke = grey)
            pic.Text(px x, yBottom + 17.0, sprintf "%g" (Math.Round(x, 6)), size = 11.0, fill = grey, anchor = "middle")

        pic.Text((x0 + x1) / 2.0, yBottom + 36.0, "position (Å)", size = 12.0, anchor = "middle")

        pic.Text(
            20.0,
            (yTop + yBottom) / 2.0,
            "energy (meV)",
            size = 12.0,
            anchor = "middle",
            transform = sprintf "rotate(-90 20 %s)" (num ((yTop + yBottom) / 2.0))
        )

        // The cloud: the proton's probability density as columns standing on
        // the ground level. Each column is drawn from y = -(ground level) upward in a
        // flipped frame, so only its height moves.
        let columns = 48
        let columnWidth = (x1 - x0) / float columns

        let columnX c =
            -xMax + 2.0 * xMax * (float c + 0.5) / float columns

        let groundY = py e0

        let density k x =
            (1.0 - rightWeight k) * rhoLeft x + rightWeight k * rhoRight x

        let peak =
            [
                for k in 0 .. frames - 1 do
                    for c in 0 .. columns - 1 -> density k (columnX c)
            ]
            |> List.max

        let cloudScale = min (groundY - yTop - 24.0) ((yBottom - yTop) * 0.55) / peak

        for c in 0 .. columns - 1 do
            let heights = Array.init frames (fun k -> density k (columnX c) * cloudScale)

            pic.Element(
                "rect",
                [
                    "x", num (x0 + columnWidth * float c)
                    "y", num (-groundY)
                    "width", num (columnWidth + 0.3)
                    "fill", colour 0
                    "opacity", "0.45"
                    "transform", "scale(1,-1)"
                ],
                animate = [ ("height", heights |> Array.map num) ]
            )

        // The potential and the ground level on top of the cloud.
        let samples = 240

        let curve =
            [
                for i in 0..samples ->
                    let x = -xMax + 2.0 * xMax * float i / float samples
                    sprintf "%s%s %s" (if i = 0 then "M" else "L") (num (px x)) (num (py (potential x)))
            ]
            |> String.concat " "

        pic.Path(curve, stroke = ink, width = 2.0)
        pic.Line(x0, groundY, x1, groundY, stroke = colour 0, width = 1.2, dash = "5 4")

        pic.Text(
            px 0.0,
            groundY + 15.0,
            sprintf "ground level %.1f meV" (e0 * 1000.0),
            size = 11.0,
            fill = colour 0,
            anchor = "middle"
        )

        pic.Text(
            px 0.0,
            py metal.BarrierHeight - 8.0,
            sprintf "barrier %.0f meV, %.1f Å wide" (metal.BarrierHeight * 1000.0) metal.BarrierWidth,
            size = 11.5,
            anchor = "middle"
        )

        pic.Text(px (-centre), yTop + 16.0, "left well", size = 11.5, fill = grey, anchor = "middle")
        pic.Text(px centre, yTop + 16.0, "right well", size = 11.5, fill = grey, anchor = "middle")

        pic.FrameText(
            x0,
            yTop - 12.0,
            Array.init frames (fun k ->
                if k = 0 then
                    "t = 0: the proton starts in the left well"
                else
                    sprintf "t = %s  (%.2f of a well-to-well pass)" (duration (timeAt k)) (timeAt k / passTime)),
            size = 12.5,
            bold = true
        )

        // Bars: probability in each well.
        let barLeft, barRight = 560.0, 740.0
        let barWidth = 50.0
        let pyP p = yBottom - (yBottom - yTop) * p

        pic.Text(
            (barLeft + barRight) / 2.0,
            yTop - 30.0,
            "where the proton is",
            size = 12.5,
            bold = true,
            anchor = "middle"
        )

        pic.Line(barLeft, yBottom, barRight, yBottom, stroke = grey)

        let wells = [ "left well", (fun k -> 1.0 - pRight k); "right well", pRight ]

        for i, (name, probability) in List.indexed wells do
            let bx = barLeft + 30.0 + float i * (barWidth + 40.0)
            let values = Array.init frames probability

            pic.Rect(
                bx,
                pyP values.[0],
                barWidth,
                yBottom - pyP values.[0],
                fill = colour i,
                animate =
                    [
                        "y", values |> Array.map pyP
                        "height", values |> Array.map (fun p -> yBottom - pyP p)
                    ]
            )

            pic.FrameText(
                bx + barWidth / 2.0,
                yTop - 10.0,
                values |> Array.map (fun p -> sprintf "%.0f%%" (p * 100.0)),
                size = 12.0,
                fill = colour i,
                bold = true,
                anchor = "middle"
            )

            pic.Text(bx + barWidth / 2.0, yBottom + 17.0, name, size = 12.0, anchor = "middle")

        // The numbers behind the motion.
        let notesY = yBottom + 64.0

        pic.Text(
            x0,
            notesY,
            sprintf
                "Tunnel splitting %s: one pass from well to well takes %s."
                (energyText splitting)
                (duration passTime),
            size = 12.5,
            bold = true
        )

        pic.Text(
            x0,
            notesY + 20.0,
            (if splittingD >= smallestSplitting then
                 sprintf
                     "Deuterium, twice as heavy, in the same wells: splitting %s, one pass takes %s."
                     (energyText splittingD)
                     (duration (Math.PI * hbar_eVs / splittingD))
             else
                 "Deuterium, twice as heavy, in the same wells: too slow for this solver to resolve."),
            size = 12.0
        )

        pic.Text(
            x0,
            notesY + 40.0,
            sprintf
                "Model: barrier height and width from the table; harmonic wells at the attempt frequency (%g Hz); exact 1D states on a grid."
                metal.AttemptFrequency,
            size = 11.0,
            fill = grey
        )

        pic.Progress(x0, height - 14.0, barRight - x0)
        pic.Save(path, quiet = quiet)

match svgPath (IO.Path.Combine(__SOURCE_DIRECTORY__, "_images", "hydrogen-tunneling.svg")) with
| Some path -> drawTunnelling path (selectedMetals |> List.maxBy (fun metal -> quantumTunnelingRate metal m_H))
| None -> ()
