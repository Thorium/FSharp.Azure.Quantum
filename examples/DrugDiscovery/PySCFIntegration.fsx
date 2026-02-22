// ==============================================================================
// PySCF Integration for Real Molecular Integrals
// ==============================================================================
// Computes real molecular integrals via PySCF (pythonnet) and feeds them into
// VQE for ground-state energy estimation, comparing multiple molecules side by
// side in a ranked table ordered by correlation energy recovery.
//
// Architecture:
//   type IntegralProvider = Molecule -> Result<MolecularIntegrals, string>
// This script implements an IntegralProvider using PySCF, demonstrating how
// external quantum chemistry packages integrate WITHOUT adding dependencies
// to the core library.
//
// Accepts multiple molecules (built-in presets or --input CSV), computes PySCF
// integrals + VQE for each, then outputs a ranked comparison table.
//
// Prerequisites:
//   1. Python 3.8+ (64-bit) installed and in PATH
//   2. pip install pyscf numpy
//   3. Python and .NET must both be 64-bit
//
// Usage:
//   dotnet fsi PySCFIntegration.fsx
//   dotnet fsi PySCFIntegration.fsx -- --help
//   dotnet fsi PySCFIntegration.fsx -- --molecules h2,lih
//   dotnet fsi PySCFIntegration.fsx -- --input molecules.csv
//   dotnet fsi PySCFIntegration.fsx -- --basis 6-31g --max-iterations 100
//   dotnet fsi PySCFIntegration.fsx -- --mode integrals
//   dotnet fsi PySCFIntegration.fsx -- --output results.json --csv results.csv --quiet
//
// References:
//   [1] Sun, Q. et al. "PySCF: the Python-based simulations of chemistry framework"
//       WIREs Comput. Mol. Sci. (2018)
//   [2] Peruzzo, A. et al. "A variational eigenvalue solver on a photonic quantum
//       processor" Nature Comms. (2014)
//   [3] Wikipedia: Variational_quantum_eigensolver
//   [4] Wikipedia: Hartree-Fock_method
// ==============================================================================

#r "nuget: pythonnet, 3.0.5"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open Python.Runtime
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

Cli.exitIfHelp "PySCFIntegration.fsx"
    "PySCF integration for real molecular integrals via VQE"
    [ { Cli.OptionSpec.Name = "input"; Description = "CSV file with custom molecule definitions"; Default = Some "built-in presets" }
      { Cli.OptionSpec.Name = "molecules"; Description = "Comma-separated molecule names to include (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "basis"; Description = "Basis set (sto-3g, 6-31g, cc-pvdz)"; Default = Some "sto-3g" }
      { Cli.OptionSpec.Name = "mode"; Description = "Mode: integrals (compute only) or vqe (full VQE)"; Default = Some "vqe" }
      { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations"; Default = Some "100" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Convergence tolerance (Hartree)"; Default = Some "1e-6" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputFile = args |> Cli.tryGet "input"
let moleculeFilter = args |> Cli.getCommaSeparated "molecules"
let basisSet = Cli.getOr "basis" "sto-3g" args
let mode = Cli.getOr "mode" "vqe" args
let maxIterations = Cli.getIntOr "max-iterations" 100 args
let tolerance = Cli.getFloatOr "tolerance" 1e-6 args

// ==============================================================================
// TYPES
// ==============================================================================

/// A molecule preset with metadata.
type MoleculeInfo =
    { Name: string
      Molecule: Molecule
      Description: string
      ExpectedHfHartree: float option }

/// Result for one molecule after integral computation + optional VQE.
type MoleculeResult =
    { Info: MoleculeInfo
      NumOrbitals: int
      NumElectrons: int
      NuclearRepulsion: float
      HfEnergyHartree: float option
      VqeEnergyHartree: float option
      CorrelationEnergyHartree: float option
      ComputeTimeSeconds: float
      HasVqeFailure: bool
      ErrorMessage: string option
      Rank: int }

// ==============================================================================
// MOLECULE BUILDERS
// ==============================================================================

/// Create H2 molecule at specified bond length (default: 0.74 A equilibrium).
let private createH2 (bondLength: float) : Molecule =
    { Name = "H2"
      Atoms =
          [ { Element = "H"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (0.0, 0.0, bondLength) } ]
      Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
      Charge = 0
      Multiplicity = 1 }

/// Create H2O molecule at equilibrium geometry.
let private createH2O () : Molecule =
    let ohLength = 0.957
    let angle = 104.5 * Math.PI / 180.0
    { Name = "H2O"
      Atoms =
          [ { Element = "O"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (0.0, ohLength * sin(angle / 2.0), ohLength * cos(angle / 2.0)) }
            { Element = "H"; Position = (0.0, -ohLength * sin(angle / 2.0), ohLength * cos(angle / 2.0)) } ]
      Bonds =
          [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 } ]
      Charge = 0
      Multiplicity = 1 }

/// Create LiH molecule at specified bond length (default: 1.595 A equilibrium).
let private createLiH (bondLength: float) : Molecule =
    { Name = "LiH"
      Atoms =
          [ { Element = "Li"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (0.0, 0.0, bondLength) } ]
      Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
      Charge = 0
      Multiplicity = 1 }

// ==============================================================================
// BUILT-IN MOLECULE PRESETS
// ==============================================================================

let private h2Preset : MoleculeInfo =
    { Name = "H2"
      Molecule = createH2 0.74
      Description = "Hydrogen molecule, simplest diatomic"
      ExpectedHfHartree = Some -1.117 }

let private h2oPreset : MoleculeInfo =
    { Name = "H2O"
      Molecule = createH2O ()
      Description = "Water molecule (3 atoms, equilibrium geometry)"
      ExpectedHfHartree = Some -75.98 }

let private lihPreset : MoleculeInfo =
    { Name = "LiH"
      Molecule = createLiH 1.595
      Description = "Lithium hydride, heteronuclear diatomic"
      ExpectedHfHartree = Some -7.863 }

/// All built-in presets keyed by lowercase name.
let private builtinPresets : Map<string, MoleculeInfo> =
    [ h2Preset; h2oPreset; lihPreset ]
    |> List.map (fun m -> m.Name.ToLowerInvariant(), m)
    |> Map.ofList

let private presetNames =
    builtinPresets |> Map.toList |> List.map fst |> String.concat ", "

// ==============================================================================
// CSV INPUT PARSING
// ==============================================================================

/// Parse atom list from compact string: "H:0,0,0|H:0,0,0.74"
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

/// Load molecule definitions from CSV.
/// Expected columns: name, description, atoms (compact format)
/// OR: name, preset (to reference a built-in preset)
let private loadMoleculesFromCsv (path: string) : MoleculeInfo list =
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
            | Some info -> Some { info with Name = name }
            | None ->
                if not quiet then
                    eprintfn "  Warning: unknown preset '%s' (available: %s)" presetKey presetNames
                None
        | None ->
            match get "atoms" with
            | Some atomStr ->
                let atoms = parseAtoms atomStr
                if atoms.IsEmpty then
                    if not quiet then
                        eprintfn "  Warning: could not parse atoms for '%s'" name
                    None
                else
                    let description = get "description" |> Option.defaultValue ""
                    let mol : Molecule =
                        { Name = name
                          Atoms = atoms
                          Bonds = inferBonds atoms
                          Charge = 0
                          Multiplicity = 1 }
                    Some
                        { Name = name
                          Molecule = mol
                          Description = description
                          ExpectedHfHartree = None }
            | None ->
                if not quiet then
                    eprintfn "  Warning: row '%s' missing 'atoms' column" name
                None)

// ==============================================================================
// MOLECULE SELECTION
// ==============================================================================

let molecules : MoleculeInfo list =
    let allMolecules =
        match inputFile with
        | Some path ->
            let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path
            if not quiet then
                printfn "Loading molecules from: %s" resolved
            loadMoleculesFromCsv resolved
        | None ->
            builtinPresets |> Map.toList |> List.map snd

    match moleculeFilter with
    | [] -> allMolecules
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToLowerInvariant()) |> Set.ofList
        allMolecules
        |> List.filter (fun m ->
            let key = m.Name.ToLowerInvariant()
            filterSet |> Set.exists (fun fil -> key.Contains fil))

if List.isEmpty molecules then
    eprintfn "Error: No molecules selected. Available presets: %s" presetNames
    exit 1

// ==============================================================================
// PYTHON INITIALIZATION
// ==============================================================================

/// Discover Python DLL dynamically via PATH.
let private discoverPythonDll () =
    let isWindows =
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows)
    let proc = new System.Diagnostics.Process()
    proc.StartInfo.FileName <- if isWindows then "where" else "which"
    proc.StartInfo.Arguments <- "python"
    proc.StartInfo.RedirectStandardOutput <- true
    proc.StartInfo.UseShellExecute <- false
    proc.StartInfo.CreateNoWindow <- true
    try
        proc.Start() |> ignore
        let output = proc.StandardOutput.ReadLine()
        proc.WaitForExit()
        if not (String.IsNullOrWhiteSpace output) then
            let pythonDir = IO.Path.GetDirectoryName(output)
            if isWindows then
                IO.Directory.GetFiles(pythonDir, "python3*.dll")
                |> Array.tryHead
            else
                let libDir = IO.Path.Combine(IO.Path.GetDirectoryName(pythonDir), "lib")
                if IO.Directory.Exists libDir then
                    IO.Directory.GetFiles(libDir, "libpython3*")
                    |> Array.filter (fun f -> f.EndsWith(".so") || f.EndsWith(".dylib"))
                    |> Array.tryHead
                else
                    None
        else
            None
    with _ -> None

/// Initialize Python runtime.
let private initializePython () =
    if not quiet then printfn "Initializing Python Runtime..."

    match discoverPythonDll () with
    | Some path ->
        Runtime.PythonDLL <- path
        if not quiet then printfn "  Using Python: %s" path
    | None ->
        if not quiet then printfn "  Using system Python (auto-detect)"

    if not (PythonEngine.IsInitialized) then
        PythonEngine.Initialize()
        if not quiet then printfn "  Python engine initialized"

        try
            use gil = Py.GIL()
            let _ = Py.Import("pyscf")
            if not quiet then printfn "  PySCF found"
        with ex ->
            if not quiet then
                printfn "  WARNING: PySCF not found. Install with: pip install pyscf"
                printfn "  Error: %s" ex.Message

/// Shutdown Python runtime.
let private shutdownPython () =
    if PythonEngine.IsInitialized then
        PythonEngine.Shutdown()

// ==============================================================================
// PYTHON HELPERS
// ==============================================================================

let private toPyString (s: string) : PyObject = new PyString(s)
let private toPyInt (i: int) : PyObject = new PyInt(i)
let private toPyFloat (f: float) : PyObject = new PyFloat(f)

// ==============================================================================
// PYSCF INTEGRAL PROVIDER
// ==============================================================================

/// Create an IntegralProvider that uses PySCF for molecular integrals.
let private createPySCFProvider (basis: string) : IntegralProvider =
    fun (molecule: Molecule) ->
        try
            use gil = Py.GIL()

            let pyscf = Py.Import("pyscf")
            let gto = pyscf.GetAttr("gto")
            let scf = pyscf.GetAttr("scf")
            let ao2mo = Py.Import("pyscf.ao2mo")
            let np = Py.Import("numpy")

            let atomString =
                molecule.Atoms
                |> List.map (fun atom ->
                    let (x, y, z) = atom.Position
                    sprintf "%s %.8f %.8f %.8f" atom.Element x y z)
                |> String.concat "; "

            let mol = gto.InvokeMethod("Mole", [||])
            mol.SetAttr("atom", toPyString atomString)
            mol.SetAttr("basis", toPyString basis)
            mol.SetAttr("charge", toPyInt molecule.Charge)
            mol.SetAttr("spin", toPyInt (molecule.Multiplicity - 1))
            mol.SetAttr("unit", toPyString "angstrom")
            mol.InvokeMethod("build", [||]) |> ignore

            let mf = scf.InvokeMethod("RHF", [| mol |])
            let hfEnergy = mf.InvokeMethod("kernel", [||])
            let hfEnergyFloat = hfEnergy.As<float>()

            let moCoeff = mf.GetAttr("mo_coeff")
            let shape = moCoeff.GetAttr("shape")
            let numOrbitals = shape.GetItem(1).As<int>()

            let nuclearRepulsion = mol.InvokeMethod("energy_nuc", [||]).As<float>()
            let numElectrons = mol.InvokeMethod("nelectron", [||]).As<int>()

            let hcore = scf.InvokeMethod("hf", [||]).InvokeMethod("get_hcore", [| mol |])
            let moCoeffT = moCoeff.GetAttr("T")
            let ctrans = np.InvokeMethod("dot", [| moCoeffT; hcore |])
            let h1eMO = np.InvokeMethod("dot", [| ctrans; moCoeff |])

            let h1Array =
                Array2D.init numOrbitals numOrbitals (fun i j ->
                    h1eMO.GetItem(toPyInt i).GetItem(toPyInt j).As<float>())

            let eriMO = ao2mo.InvokeMethod("kernel", [| mol; moCoeff |])
            let eriMO4D = ao2mo.InvokeMethod("restore", [| toPyInt 1; eriMO; toPyInt numOrbitals |])

            let g2Array =
                Array4D.init numOrbitals numOrbitals numOrbitals numOrbitals (fun p q r s ->
                    eriMO4D.GetItem(toPyInt p).GetItem(toPyInt q).GetItem(toPyInt r).GetItem(toPyInt s).As<float>())

            Ok {
                NumOrbitals = numOrbitals
                NumElectrons = numElectrons
                NuclearRepulsion = nuclearRepulsion
                OneElectron = { NumOrbitals = numOrbitals; Integrals = h1Array }
                TwoElectron = { NumOrbitals = numOrbitals; Integrals = g2Array }
                ReferenceEnergy = Some hfEnergyFloat
            }

        with ex ->
            Error (sprintf "PySCF calculation failed: %s" ex.Message)

// ==============================================================================
// QUANTUM BACKEND (Rule 1: all VQE via IQuantumBackend)
// ==============================================================================

let backend : IQuantumBackend = LocalBackend() :> IQuantumBackend

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  PySCF Integration - Molecular Integral Comparison"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:        %s" backend.Name
    printfn "  Molecules:      %d" molecules.Length
    printfn "  Basis set:      %s" basisSet
    printfn "  Mode:           %s" mode
    if mode = "vqe" then
        printfn "  Max iterations: %d" maxIterations
        printfn "  Tolerance:      %g" tolerance
    printfn ""
    printfn "  %-6s  %5s  %s" "Name" "Atoms" "Description"
    printfn "  %s" (String('-', 50))
    for m in molecules do
        printfn "  %-6s  %5d  %s" m.Name m.Molecule.Atoms.Length m.Description
    printfn ""

// ==============================================================================
// COMPUTATION
// ==============================================================================

initializePython ()

let provider = createPySCFProvider basisSet

let config =
    { Method = GroundStateMethod.VQE
      Backend = Some backend
      MaxIterations = maxIterations
      Tolerance = tolerance
      InitialParameters = None
      ProgressReporter = None
      ErrorMitigation = None
      IntegralProvider = Some provider }

/// Compute integrals (and optionally VQE) for one molecule.
let computeMolecule (info: MoleculeInfo) : MoleculeResult =
    if not quiet then
        printfn "  [%s] Computing integrals (basis: %s)..." info.Name basisSet

    let startTime = DateTime.Now

    match provider info.Molecule with
    | Error msg ->
        let elapsed = (DateTime.Now - startTime).TotalSeconds
        if not quiet then
            printfn "  [%s] ERROR: %s (%.1fs)" info.Name msg elapsed
        { Info = info; NumOrbitals = 0; NumElectrons = 0; NuclearRepulsion = 0.0
          HfEnergyHartree = None; VqeEnergyHartree = None
          CorrelationEnergyHartree = None; ComputeTimeSeconds = elapsed
          HasVqeFailure = true; ErrorMessage = Some msg; Rank = 0 }

    | Ok integrals ->
        if not quiet then
            printfn "  [%s] Integrals OK: %d orbitals, %d electrons, Enuc=%.4f Ha"
                info.Name integrals.NumOrbitals integrals.NumElectrons integrals.NuclearRepulsion

        if mode <> "vqe" then
            let elapsed = (DateTime.Now - startTime).TotalSeconds
            { Info = info
              NumOrbitals = integrals.NumOrbitals
              NumElectrons = integrals.NumElectrons
              NuclearRepulsion = integrals.NuclearRepulsion
              HfEnergyHartree = integrals.ReferenceEnergy
              VqeEnergyHartree = None
              CorrelationEnergyHartree = None
              ComputeTimeSeconds = elapsed
              HasVqeFailure = false; ErrorMessage = None; Rank = 0 }
        else
            if not quiet then
                printfn "  [%s] Running VQE..." info.Name

            let vqeResult =
                GroundStateEnergy.estimateEnergy info.Molecule config |> Async.RunSynchronously
            let elapsed = (DateTime.Now - startTime).TotalSeconds

            match vqeResult with
            | Ok result ->
                let corrEnergy =
                    integrals.ReferenceEnergy
                    |> Option.map (fun hf -> result.Energy - hf)
                if not quiet then
                    printfn "  [%s] VQE OK: %.6f Ha (%.1fs)" info.Name result.Energy elapsed
                { Info = info
                  NumOrbitals = integrals.NumOrbitals
                  NumElectrons = integrals.NumElectrons
                  NuclearRepulsion = integrals.NuclearRepulsion
                  HfEnergyHartree = integrals.ReferenceEnergy
                  VqeEnergyHartree = Some result.Energy
                  CorrelationEnergyHartree = corrEnergy
                  ComputeTimeSeconds = elapsed
                  HasVqeFailure = false; ErrorMessage = None; Rank = 0 }
            | Error err ->
                if not quiet then
                    printfn "  [%s] VQE FAILED: %s (%.1fs)" info.Name err.Message elapsed
                { Info = info
                  NumOrbitals = integrals.NumOrbitals
                  NumElectrons = integrals.NumElectrons
                  NuclearRepulsion = integrals.NuclearRepulsion
                  HfEnergyHartree = integrals.ReferenceEnergy
                  VqeEnergyHartree = None
                  CorrelationEnergyHartree = None
                  ComputeTimeSeconds = elapsed
                  HasVqeFailure = true; ErrorMessage = Some err.Message; Rank = 0 }

if not quiet then
    printfn ""

let moleculeResults = molecules |> List.map computeMolecule

shutdownPython ()

// Sort: successful first (by correlation energy, more negative = better), then failures.
let ranked =
    moleculeResults
    |> List.sortBy (fun r ->
        if r.HasVqeFailure then (2, 0.0)
        elif r.VqeEnergyHartree.IsSome then
            (0, r.VqeEnergyHartree.Value)
        else (1, 0.0))
    |> List.mapi (fun i r -> { r with Rank = i + 1 })

// ==============================================================================
// RANKED COMPARISON TABLE
// ==============================================================================

let printTable () =
    let isVqeMode = mode = "vqe"
    let title =
        if isVqeMode then "Molecular Ground-State Energies (PySCF + VQE)"
        else "Molecular Integrals (PySCF)"

    printfn "=================================================================="
    printfn "  %s" title
    printfn "=================================================================="
    printfn ""

    if isVqeMode then
        printfn "  %-4s  %-6s  %5s  %5s  %12s  %12s  %12s  %8s"
            "#" "Name" "Atoms" "MOs" "HF (Ha)" "VQE (Ha)" "Corr (Ha)" "Time (s)"
        printfn "  %s" (String('=', 80))

        for r in ranked do
            if r.HasVqeFailure then
                printfn "  %-4d  %-6s  %5d  %5d  %12s  %12s  %12s  %8.1f"
                    r.Rank r.Info.Name r.Info.Molecule.Atoms.Length r.NumOrbitals
                    (r.HfEnergyHartree |> Option.map (sprintf "%.6f") |> Option.defaultValue "FAILED")
                    "FAILED" "FAILED" r.ComputeTimeSeconds
            else
                printfn "  %-4d  %-6s  %5d  %5d  %12s  %12s  %12s  %8.1f"
                    r.Rank r.Info.Name r.Info.Molecule.Atoms.Length r.NumOrbitals
                    (r.HfEnergyHartree |> Option.map (sprintf "%.6f") |> Option.defaultValue "N/A")
                    (r.VqeEnergyHartree |> Option.map (sprintf "%.6f") |> Option.defaultValue "N/A")
                    (r.CorrelationEnergyHartree |> Option.map (sprintf "%.6f") |> Option.defaultValue "N/A")
                    r.ComputeTimeSeconds
    else
        printfn "  %-4s  %-6s  %5s  %5s  %5s  %12s  %12s  %8s"
            "#" "Name" "Atoms" "MOs" "Elec" "Enuc (Ha)" "HF (Ha)" "Time (s)"
        printfn "  %s" (String('=', 75))

        for r in ranked do
            if r.HasVqeFailure then
                printfn "  %-4d  %-6s  %5d  %5s  %5s  %12s  %12s  %8.1f"
                    r.Rank r.Info.Name r.Info.Molecule.Atoms.Length
                    "FAIL" "FAIL" "FAILED" "FAILED" r.ComputeTimeSeconds
            else
                printfn "  %-4d  %-6s  %5d  %5d  %5d  %12.6f  %12s  %8.1f"
                    r.Rank r.Info.Name r.Info.Molecule.Atoms.Length
                    r.NumOrbitals r.NumElectrons r.NuclearRepulsion
                    (r.HfEnergyHartree |> Option.map (sprintf "%.6f") |> Option.defaultValue "N/A")
                    r.ComputeTimeSeconds

    printfn ""

    let successful = ranked |> List.filter (fun r -> not r.HasVqeFailure)
    let failed = ranked |> List.filter (fun r -> r.HasVqeFailure)
    if not successful.IsEmpty then
        let totalTime = ranked |> List.sumBy (fun r -> r.ComputeTimeSeconds)
        printfn "  Computation Summary:"
        printfn "  %s" (String('-', 55))
        printfn "    Successful:    %d / %d molecules" successful.Length molecules.Length
        if not failed.IsEmpty then
            printfn "    Failed:        %d" failed.Length
        printfn "    Basis set:     %s" basisSet
        printfn "    Total time:    %.1f seconds" totalTime
        if isVqeMode then
            printfn "    Max iterations:%d" maxIterations
            printfn "    Tolerance:     %g Ha" tolerance
        printfn ""

// Always print -- this is the primary output.
printTable ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    let successCount = ranked |> List.filter (fun r -> not r.HasVqeFailure) |> List.length
    if successCount > 0 then
        printfn "  Backend:      %s" backend.Name
        printfn "  Quantum:      VQE via IQuantumBackend [Rule 1 compliant]"
        printfn ""
    else
        printfn "  All computations failed. Check Python/PySCF installation."
        printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    ranked
    |> List.map (fun r ->
        [ "rank", string r.Rank
          "name", r.Info.Name
          "description", r.Info.Description
          "num_atoms", string r.Info.Molecule.Atoms.Length
          "num_orbitals", string r.NumOrbitals
          "num_electrons", string r.NumElectrons
          "nuclear_repulsion_hartree", sprintf "%.6f" r.NuclearRepulsion
          "basis_set", basisSet
          "hf_energy_hartree",
            (r.HfEnergyHartree |> Option.map (sprintf "%.6f") |> Option.defaultValue "")
          "vqe_energy_hartree",
            (r.VqeEnergyHartree |> Option.map (sprintf "%.6f") |> Option.defaultValue "")
          "correlation_energy_hartree",
            (r.CorrelationEnergyHartree |> Option.map (sprintf "%.6f") |> Option.defaultValue "")
          "mode", mode
          "max_iterations", string maxIterations
          "tolerance", sprintf "%g" tolerance
          "backend", backend.Name
          "compute_time_s", sprintf "%.1f" r.ComputeTimeSeconds
          "error", (r.ErrorMessage |> Option.defaultValue "")
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
        [ "rank"; "name"; "description"; "num_atoms"; "num_orbitals"; "num_electrons"
          "nuclear_repulsion_hartree"; "basis_set"; "hf_energy_hartree"
          "vqe_energy_hartree"; "correlation_energy_hartree"; "mode"
          "max_iterations"; "tolerance"; "backend"; "compute_time_s"
          "error"; "has_vqe_failure" ]
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
    printfn "     --molecules h2,lih                       Run specific molecules"
    printfn "     --input molecules.csv                    Load custom molecules from CSV"
    printfn "     --basis 6-31g                            Use different basis set"
    printfn "     --mode integrals                         Compute integrals only (no VQE)"
    printfn "     --csv results.csv                        Export ranked table as CSV"
    printfn ""
