// ==============================================================================
// PySCF Integration for Real Molecular Integrals
// ==============================================================================
// Demonstrates how to use PySCF (via pythonnet) to compute real molecular
// integrals and plug them into FSharp.Azure.Quantum's VQE framework.
//
// ARCHITECTURE:
// The library provides a pluggable IntegralProvider interface:
//   type IntegralProvider = Molecule -> Result<MolecularIntegrals, string>
//
// This script implements an IntegralProvider using PySCF, demonstrating how
// external quantum chemistry packages can be integrated WITHOUT adding
// dependencies to the core library.
//
// PREREQUISITES:
//   1. Python 3.8+ (64-bit) installed and in PATH
//   2. pip install pyscf numpy
//   3. Python and .NET must both be 64-bit
//
// TROUBLESHOOTING:
//   "Unable to find python DLL"
//     -> Set Runtime.PythonDLL to exact path before Initialize()
//   "No module named 'pyscf'"
//     -> Install in correct Python: <your-python> -m pip install pyscf
//   "Python.Runtime.PythonException: ... BLAS ..."
//     -> Reinstall PySCF: pip uninstall pyscf && pip install pyscf
//   Crashes on PythonEngine.Initialize()
//     -> Ensure 64-bit Python with 64-bit .NET runtime
//   "Access violation" or segfault
//     -> Don't mix Python versions; use single consistent installation
//   Results don't match expected energies
//     -> Check geometry in Angstroms, correct basis set name, charge/multiplicity
//
// Usage:
//   dotnet fsi PySCFIntegration.fsx
//   dotnet fsi PySCFIntegration.fsx -- --help
//   dotnet fsi PySCFIntegration.fsx -- --molecule H2 --basis sto-3g
//   dotnet fsi PySCFIntegration.fsx -- --molecule LiH --bond-length 1.6 --basis 6-31g
//   dotnet fsi PySCFIntegration.fsx -- --mode integrals --molecule H2O
//   dotnet fsi PySCFIntegration.fsx -- --mode vqe --max-iterations 100 --tolerance 1e-6
//   dotnet fsi PySCFIntegration.fsx -- --output results.json --csv results.csv --quiet
//
// ==============================================================================

#r "nuget: pythonnet, 3.0.5"
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
// CLI CONFIGURATION
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "PySCFIntegration.fsx" "PySCF integration for real molecular integrals via VQE"
    [ { Cli.OptionSpec.Name = "molecule"; Description = "Molecule to compute: H2, H2O, LiH (default: H2)"; Default = Some "H2" }
      { Cli.OptionSpec.Name = "basis"; Description = "Basis set: sto-3g, 6-31g, cc-pvdz (default: sto-3g)"; Default = Some "sto-3g" }
      { Cli.OptionSpec.Name = "bond-length"; Description = "Bond length in Angstroms for diatomics (default: equilibrium)"; Default = Some "equilibrium" }
      { Cli.OptionSpec.Name = "mode"; Description = "Mode: integrals (compute only), vqe (full VQE run) (default: vqe)"; Default = Some "vqe" }
      { Cli.OptionSpec.Name = "max-iterations"; Description = "Maximum VQE iterations (default: 100)"; Default = Some "100" }
      { Cli.OptionSpec.Name = "tolerance"; Description = "Convergence tolerance in Hartree (default: 1e-6)"; Default = Some "1e-6" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let moleculeName = Cli.getOr "molecule" "H2" args
let basisSet = Cli.getOr "basis" "sto-3g" args
let mode = Cli.getOr "mode" "vqe" args
let maxIterations = Cli.getIntOr "max-iterations" 100 args
let tolerance = Cli.getFloatOr "tolerance" 1e-6 args

// ==============================================================================
// PYTHON INITIALIZATION
// ==============================================================================

/// Discover Python DLL dynamically via PATH
let discoverPythonDll () =
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

/// Initialize Python runtime
let initializePython () =
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

/// Shutdown Python runtime
let shutdownPython () =
    if PythonEngine.IsInitialized then
        PythonEngine.Shutdown()

// ==============================================================================
// PYTHON HELPERS
// ==============================================================================

let toPyString (s: string) : PyObject = new PyString(s)
let toPyInt (i: int) : PyObject = new PyInt(i)
let toPyFloat (f: float) : PyObject = new PyFloat(f)

// ==============================================================================
// PYSCF INTEGRAL PROVIDER
// ==============================================================================

/// Create an IntegralProvider that uses PySCF for molecular integrals.
/// The basis parameter specifies the basis set (e.g., "sto-3g", "6-31g", "cc-pvdz").
/// Returns an IntegralProvider function compatible with SolverConfig.
let createPySCFProvider (basis: string) : IntegralProvider =
    fun (molecule: Molecule) ->
        try
            use gil = Py.GIL()

            let pyscf = Py.Import("pyscf")
            let gto = pyscf.GetAttr("gto")
            let scf = pyscf.GetAttr("scf")
            let ao2mo = Py.Import("pyscf.ao2mo")
            let np = Py.Import("numpy")

            // Build molecule geometry string for PySCF
            let atomString =
                molecule.Atoms
                |> List.map (fun atom ->
                    let (x, y, z) = atom.Position
                    sprintf "%s %.8f %.8f %.8f" atom.Element x y z)
                |> String.concat "; "

            // Create PySCF molecule object
            let mol = gto.InvokeMethod("Mole", [||])
            mol.SetAttr("atom", toPyString atomString)
            mol.SetAttr("basis", toPyString basis)
            mol.SetAttr("charge", toPyInt molecule.Charge)
            mol.SetAttr("spin", toPyInt (molecule.Multiplicity - 1))
            mol.SetAttr("unit", toPyString "angstrom")
            mol.InvokeMethod("build", [||]) |> ignore

            // Run Restricted Hartree-Fock to get molecular orbitals
            let mf = scf.InvokeMethod("RHF", [| mol |])
            let hfEnergy = mf.InvokeMethod("kernel", [||])
            let hfEnergyFloat = hfEnergy.As<float>()

            // Get molecular orbital coefficients
            let moCoeff = mf.GetAttr("mo_coeff")
            let shape = moCoeff.GetAttr("shape")
            let numOrbitals = shape.GetItem(1).As<int>()

            let nuclearRepulsion = mol.InvokeMethod("energy_nuc", [||]).As<float>()
            let numElectrons = mol.InvokeMethod("nelectron", [||]).As<int>()

            // Compute one-electron integrals in MO basis: h1e_MO = C^T @ h1e_AO @ C
            let hcore = scf.InvokeMethod("hf", [||]).InvokeMethod("get_hcore", [| mol |])
            let moCoeffT = moCoeff.GetAttr("T")
            let ctrans = np.InvokeMethod("dot", [| moCoeffT; hcore |])
            let h1eMO = np.InvokeMethod("dot", [| ctrans; moCoeff |])

            let h1Array =
                Array2D.init numOrbitals numOrbitals (fun i j ->
                    h1eMO.GetItem(toPyInt i).GetItem(toPyInt j).As<float>())

            // Compute two-electron integrals in MO basis using ao2mo
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
// MOLECULE BUILDERS
// ==============================================================================

/// Create H2 molecule at specified bond length (default: 0.74 A equilibrium)
let createH2 (bondLength: float) : Molecule =
    { Name = "H2"
      Atoms =
          [ { Element = "H"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (0.0, 0.0, bondLength) } ]
      Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
      Charge = 0
      Multiplicity = 1 }

/// Create H2O molecule at equilibrium geometry
let createH2O () : Molecule =
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

/// Create LiH molecule at specified bond length (default: 1.6 A equilibrium)
let createLiH (bondLength: float) : Molecule =
    { Name = "LiH"
      Atoms =
          [ { Element = "Li"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (0.0, 0.0, bondLength) } ]
      Bonds = [ { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 } ]
      Charge = 0
      Multiplicity = 1 }

// ==============================================================================
// BUILD SELECTED MOLECULE
// ==============================================================================

/// Default equilibrium bond lengths (Angstroms)
let defaultBondLength (name: string) =
    match name.ToUpperInvariant() with
    | "H2" -> 0.74
    | "LIH" -> 1.595
    | _ -> 1.0

let bondLength =
    match Cli.tryGet "bond-length" args with
    | Some s ->
        match Double.TryParse(s) with
        | true, v -> v
        | _ -> defaultBondLength moleculeName
    | None -> defaultBondLength moleculeName

let molecule =
    match moleculeName.ToUpperInvariant() with
    | "H2" -> createH2 bondLength
    | "H2O" -> createH2O ()
    | "LIH" -> createLiH bondLength
    | other -> failwithf "Unknown molecule: %s. Supported: H2, H2O, LiH" other

// ==============================================================================
// MAIN COMPUTATION
// ==============================================================================

if not quiet then
    printfn "============================================================"
    printfn "  PySCF Integration for FSharp.Azure.Quantum"
    printfn "============================================================"
    printfn ""
    printfn "Configuration:"
    printfn "  Molecule:       %s" molecule.Name
    printfn "  Atoms:          %d" molecule.Atoms.Length
    printfn "  Basis set:      %s" basisSet
    printfn "  Mode:           %s" mode
    if mode = "vqe" then
        printfn "  Max iterations: %d" maxIterations
        printfn "  Tolerance:      %g" tolerance
    printfn ""

initializePython ()

let provider = createPySCFProvider basisSet
let resultsList = System.Collections.Generic.List<Map<string, string>>()

if not quiet then
    printfn "Computing integrals for %s (basis: %s)..." molecule.Name basisSet
    printfn ""

match provider molecule with
| Ok integrals ->
    if not quiet then
        printfn "Integrals computed successfully"
        printfn "  Number of orbitals:  %d" integrals.NumOrbitals
        printfn "  Number of electrons: %d" integrals.NumElectrons
        printfn "  Nuclear repulsion:   %.6f Hartree" integrals.NuclearRepulsion
        printfn ""

    if not quiet then
        printfn "One-electron integrals h[p,q]:"
        for p in 0 .. integrals.NumOrbitals - 1 do
            for q in 0 .. integrals.NumOrbitals - 1 do
                printfn "  h[%d,%d] = %10.6f" p q integrals.OneElectron.Integrals.[p, q]
        printfn ""
        printfn "Two-electron integrals (pq|rs) -- diagonal terms:"
        for p in 0 .. integrals.NumOrbitals - 1 do
            printfn "  (%d%d|%d%d) = %10.6f" p p p p integrals.TwoElectron.Integrals.[p, p, p, p]
        printfn ""

    match integrals.ReferenceEnergy with
    | Some hf ->
        if not quiet then printfn "  Hartree-Fock reference energy: %.6f Hartree" hf
    | None -> ()

    let integralResults =
        [ "molecule", molecule.Name
          "basis_set", basisSet
          "num_orbitals", string integrals.NumOrbitals
          "num_electrons", string integrals.NumElectrons
          "nuclear_repulsion_hartree", sprintf "%.6f" integrals.NuclearRepulsion
          "hf_reference_hartree",
          (integrals.ReferenceEnergy
           |> Option.map (sprintf "%.6f")
           |> Option.defaultValue "N/A") ]
        |> Map.ofList

    resultsList.Add(integralResults)

    // VQE mode: run full VQE with PySCF integrals
    if mode = "vqe" then
        if not quiet then
            printfn ""
            printfn "------------------------------------------------------------"
            printfn "  VQE with Real PySCF Integrals"
            printfn "------------------------------------------------------------"
            printfn ""

        let backend = LocalBackend() :> IQuantumBackend

        if not quiet then
            printfn "  Backend: %s" backend.Name
            printfn ""

        let config =
            { Method = GroundStateMethod.VQE
              Backend = Some backend
              MaxIterations = maxIterations
              Tolerance = tolerance
              InitialParameters = None
              ProgressReporter = None
              ErrorMitigation = None
              IntegralProvider = Some provider }

        if not quiet then
            printfn "Running VQE with PySCF integrals..."

        let startTime = DateTime.Now
        let vqeResult = GroundStateEnergy.estimateEnergy molecule config |> Async.RunSynchronously
        let elapsed = (DateTime.Now - startTime).TotalSeconds

        match vqeResult with
        | Ok result ->
            if not quiet then
                printfn "  VQE converged"
                printfn "  Ground state energy: %.6f Hartree" result.Energy
                printfn "  Computation time:    %.2f s" elapsed
                printfn ""

                match integrals.ReferenceEnergy with
                | Some hf ->
                    let correlationEnergy = result.Energy - hf
                    printfn "  HF reference:       %.6f Hartree" hf
                    printfn "  Correlation energy:  %.6f Hartree" correlationEnergy
                    printfn "  Correlation (%%):    %.2f%%" (abs correlationEnergy / abs hf * 100.0)
                | None -> ()

            let vqeMap =
                [ "stage", "vqe_result"
                  "molecule", molecule.Name
                  "basis_set", basisSet
                  "vqe_energy_hartree", sprintf "%.6f" result.Energy
                  "elapsed_seconds", sprintf "%.2f" elapsed
                  "hf_reference_hartree",
                  (integrals.ReferenceEnergy
                   |> Option.map (sprintf "%.6f")
                   |> Option.defaultValue "N/A")
                  "correlation_energy_hartree",
                  (integrals.ReferenceEnergy
                   |> Option.map (fun hf -> sprintf "%.6f" (result.Energy - hf))
                   |> Option.defaultValue "N/A")
                  "max_iterations", string maxIterations
                  "tolerance", sprintf "%g" tolerance ]
                |> Map.ofList

            resultsList.Add(vqeMap)

        | Error err ->
            if not quiet then
                printfn "  VQE calculation failed: %A" err.Message

            let errMap =
                [ "stage", "vqe_error"
                  "molecule", molecule.Name
                  "basis_set", basisSet
                  "error", sprintf "%A" err.Message ]
                |> Map.ofList

            resultsList.Add(errMap)

| Error msg ->
    if not quiet then
        printfn "Error computing integrals: %s" msg
        printfn ""
        printfn "Ensure prerequisites are met:"
        printfn "  1. Python 3.8+ (64-bit) installed and in PATH"
        printfn "  2. pip install pyscf numpy"
        printfn "  3. Python and .NET architecture must match (both 64-bit)"

    let errMap =
        [ "stage", "integral_error"
          "molecule", molecule.Name
          "basis_set", basisSet
          "error", msg ]
        |> Map.ofList

    resultsList.Add(errMap)

shutdownPython ()

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let allResults = resultsList |> Seq.toList

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path allResults
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header =
        [ "stage"
          "molecule"
          "basis_set"
          "num_orbitals"
          "num_electrons"
          "nuclear_repulsion_hartree"
          "hf_reference_hartree"
          "vqe_energy_hartree"
          "correlation_energy_hartree"
          "elapsed_seconds"
          "error" ]

    let rows =
        allResults
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))

    Reporting.writeCsv path header rows
    if not quiet then printfn "CSV written to %s" path
| None -> ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    printfn ""
    printfn "============================================================"
    printfn "  Summary"
    printfn "============================================================"
    printfn ""
    printfn "  Computed real molecular integrals via PySCF"
    printfn "  Molecule: %s  Basis: %s" molecule.Name basisSet
    if mode = "vqe" then
        printfn "  VQE ground state energy calculated with real integrals"
    printfn "  All computation via IQuantumBackend (quantum compliant)"
    printfn ""

if argv.Length = 0 && not quiet then
    printfn "Tip: Run with -- --help to see available options"
    printfn "     --molecule H2O --basis 6-31g  (try different molecules/bases)"
    printfn "     --mode integrals              (compute integrals only)"
    printfn "     --output results.json         (structured output)"
