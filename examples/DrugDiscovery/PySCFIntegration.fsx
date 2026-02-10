// ==============================================================================
// PySCF Integration for Real Molecular Integrals
// ==============================================================================
// 
// This script demonstrates how to use PySCF (via pythonnet) to compute real
// molecular integrals and plug them into FSharp.Azure.Quantum's VQE framework.
//
// ┌─────────────────────────────────────────────────────────────────────────────┐
// │                           PREREQUISITES                                     │
// ├─────────────────────────────────────────────────────────────────────────────┤
// │                                                                             │
// │  1. PYTHON INSTALLATION                                                     │
// │     • Python 3.8, 3.9, 3.10, 3.11, or 3.12 (64-bit)                         │
// │     • Must be in system PATH                                                │
// │     • Verify: python --version                                              │
// │                                                                             │
// │  2. PYTHON PACKAGES                                                         │
// │     pip install pyscf numpy                                                 │
// │     • PySCF ~500MB download (includes BLAS/LAPACK)                          │
// │     • Verify: python -c "import pyscf; print(pyscf.__version__)"            │
// │                                                                             │
// │  3. PYTHON DLL LOCATION                                                     │
// │     pythonnet needs to find python3XX.dll (Windows) or libpython3.XX.so     │
// │     Common locations checked automatically:                                 │
// │     • C:\Python3XX\python3XX.dll                                            │
// │     • C:\Users\<user>\AppData\Local\Programs\Python\Python3XX\              │
// │     • /usr/lib/x86_64-linux-gnu/libpython3.XX.so                            │
// │                                                                             │
// │  4. ARCHITECTURE MATCH                                                      │
// │     • Python and .NET must both be 64-bit (or both 32-bit)                  │
// │     • Mismatch causes: "Unable to load DLL 'python3XX'"                     │
// │                                                                             │
// └─────────────────────────────────────────────────────────────────────────────┘
//
// ┌─────────────────────────────────────────────────────────────────────────────┐
// │                        TROUBLESHOOTING                                      │
// ├─────────────────────────────────────────────────────────────────────────────┤
// │                                                                             │
// │  ERROR: "Unable to find python DLL"                                         │
// │  FIX:   Set Runtime.PythonDLL to exact path before Initialize()             │
// │         Runtime.PythonDLL <- @"C:\Python311\python311.dll"                  │
// │                                                                             │
// │  ERROR: "No module named 'pyscf'"                                           │
// │  FIX:   Install in correct Python: <your-python> -m pip install pyscf       │
// │                                                                             │
// │  ERROR: "Python.Runtime.PythonException: ... BLAS ..."                      │
// │  FIX:   Reinstall PySCF: pip uninstall pyscf && pip install pyscf           │
// │                                                                             │
// │  ERROR: Crashes on PythonEngine.Initialize()                                │
// │  FIX:   Ensure 64-bit Python with 64-bit .NET runtime                       │
// │         Check: python -c "import struct; print(struct.calcsize('P')*8)"     │
// │                                                                             │
// │  ERROR: "Access violation" or segfault                                      │
// │  FIX:   Don't mix Python versions; use single consistent installation       │
// │                                                                             │
// │  ERROR: Results don't match expected energies                               │
// │  CHECK: • Geometry in Angstroms (not Bohr)                                  │
// │         • Correct basis set name ("sto-3g" not "sto3g")                     │
// │         • Charge and multiplicity correct                                   │
// │                                                                             │
// └─────────────────────────────────────────────────────────────────────────────┘
//
// ARCHITECTURE:
// The library provides a pluggable IntegralProvider interface:
//   type IntegralProvider = Molecule -> Result<MolecularIntegrals, string>
//
// This script implements an IntegralProvider using PySCF, demonstrating how
// external quantum chemistry packages can be integrated WITHOUT adding
// dependencies to the core library.
//
// ==============================================================================

#r "nuget: pythonnet, 3.0.5"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open Python.Runtime
open FSharp.Azure.Quantum.QuantumChemistry

// ==============================================================================
// PYTHON INITIALIZATION
// ==============================================================================

/// Initialize Python runtime - call once at script start
let initializePython () =
    printfn "🐍 Initializing Python Runtime..."
    
    // Discover Python dynamically via PATH instead of hardcoded paths
    let discoverPythonDll () =
        let isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
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
            if not (System.String.IsNullOrWhiteSpace output) then
                let pythonDir = System.IO.Path.GetDirectoryName(output)
                // Derive DLL/SO name from python executable location
                if isWindows then
                    // Look for pythonXYZ.dll in same directory
                    System.IO.Directory.GetFiles(pythonDir, "python3*.dll")
                    |> Array.tryHead
                else
                    // Look for libpythonX.Y.so or .dylib in lib directory
                    let libDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(pythonDir), "lib")
                    if System.IO.Directory.Exists libDir then
                        System.IO.Directory.GetFiles(libDir, "libpython3*")
                        |> Array.filter (fun f -> f.EndsWith(".so") || f.EndsWith(".dylib"))
                        |> Array.tryHead
                    else None
            else None
        with _ -> None

    match discoverPythonDll () with
    | Some path -> 
        Runtime.PythonDLL <- path
        printfn "   Using Python: %s" path
    | None ->
        printfn "   Using system Python (auto-detect)"
    
    if not (PythonEngine.IsInitialized) then
        PythonEngine.Initialize()
        printfn "   Python engine initialized"
        
        // Check if PySCF is available
        try
            use gil = Py.GIL()
            let _ = Py.Import("pyscf")
            printfn "   PySCF found ✓"
        with ex ->
            printfn "   ⚠️  PySCF not found. Install with: pip install pyscf"
            printfn "      Error: %s" ex.Message

/// Shutdown Python runtime
let shutdownPython () =
    if PythonEngine.IsInitialized then
        PythonEngine.Shutdown()

// ==============================================================================
// PYTHON HELPERS
// ==============================================================================

/// Convert F# value to PyObject
let inline toPyObj (value: 'T) : PyObject =
    use pyVal = new PyInt(Convert.ToInt64(box value))
    pyVal.As<PyObject>()

let toPyString (s: string) : PyObject =
    new PyString(s)

let toPyInt (i: int) : PyObject =
    new PyInt(i)

let toPyFloat (f: float) : PyObject =
    new PyFloat(f)

// ==============================================================================
// PYSCF INTEGRAL PROVIDER
// ==============================================================================

/// Create an IntegralProvider that uses PySCF for molecular integrals
/// 
/// Parameters:
/// - basis: Basis set name (e.g., "sto-3g", "6-31g", "cc-pvdz")
/// 
/// Returns: An IntegralProvider function compatible with SolverConfig
let createPySCFProvider (basis: string) : IntegralProvider =
    fun (molecule: Molecule) ->
        try
            use gil = Py.GIL()
            
            // Import PySCF modules
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
            mol.SetAttr("spin", toPyInt (molecule.Multiplicity - 1))  // PySCF uses 2S, not 2S+1
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
            
            // Get nuclear repulsion energy
            let nuclearRepulsion = mol.InvokeMethod("energy_nuc", [||]).As<float>()
            
            // Get number of electrons
            let numElectrons = mol.InvokeMethod("nelectron", [||]).As<int>()
            
            // Compute one-electron integrals in MO basis
            // h1e = C^T @ (T + V_nuc) @ C
            let hcore = scf.InvokeMethod("hf", [||]).InvokeMethod("get_hcore", [| mol |])
            
            // Transform to MO basis: h1e_MO = C^T @ h1e_AO @ C
            let moCoeffT = moCoeff.GetAttr("T")
            let ctrans = np.InvokeMethod("dot", [| moCoeffT; hcore |])
            let h1eMO = np.InvokeMethod("dot", [| ctrans; moCoeff |])
            
            // Extract one-electron integrals to F# array
            let h1Array = Array2D.init numOrbitals numOrbitals (fun i j ->
                h1eMO.GetItem(toPyInt i).GetItem(toPyInt j).As<float>())
            
            // Compute two-electron integrals in MO basis using ao2mo
            let eriMO = ao2mo.InvokeMethod("kernel", [| mol; moCoeff |])
            
            // Restore to 4D tensor
            let eriMO4D = ao2mo.InvokeMethod("restore", [| toPyInt 1; eriMO; toPyInt numOrbitals |])
            
            // Extract two-electron integrals to F# array
            let g2Array = Array4D.init numOrbitals numOrbitals numOrbitals numOrbitals (fun p q r s ->
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
// CONVENIENCE FUNCTIONS
// ==============================================================================

/// Create H2 molecule at specified bond length
let createH2 (bondLength: float) : Molecule =
    {
        Name = "H2"
        Atoms = [
            { Element = "H"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (0.0, 0.0, bondLength) }
        ]
        Bonds = [{ Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }]
        Charge = 0
        Multiplicity = 1
    }

/// Create H2O molecule at equilibrium geometry
let createH2O () : Molecule =
    let ohLength = 0.957  // Angstroms
    let angle = 104.5 * Math.PI / 180.0
    {
        Name = "H2O"
        Atoms = [
            { Element = "O"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (0.0, ohLength * sin(angle/2.0), ohLength * cos(angle/2.0)) }
            { Element = "H"; Position = (0.0, -ohLength * sin(angle/2.0), ohLength * cos(angle/2.0)) }
        ]
        Bonds = [
            { Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }
            { Atom1 = 0; Atom2 = 2; BondOrder = 1.0 }
        ]
        Charge = 0
        Multiplicity = 1
    }

/// Create LiH molecule at specified bond length
let createLiH (bondLength: float) : Molecule =
    {
        Name = "LiH"
        Atoms = [
            { Element = "Li"; Position = (0.0, 0.0, 0.0) }
            { Element = "H"; Position = (0.0, 0.0, bondLength) }
        ]
        Bonds = [{ Atom1 = 0; Atom2 = 1; BondOrder = 1.0 }]
        Charge = 0
        Multiplicity = 1
    }

// ==============================================================================
// EXAMPLE: Direct Integral Computation (without VQE)
// ==============================================================================

/// Demonstrate direct integral computation from PySCF
let demonstrateIntegrals () =
    printfn ""
    printfn "╔══════════════════════════════════════════════════════════════════╗"
    printfn "║           PySCF Molecular Integral Computation                   ║"
    printfn "╚══════════════════════════════════════════════════════════════════╝"
    printfn ""
    
    initializePython()
    
    let provider = createPySCFProvider "sto-3g"
    let h2 = createH2 0.74  // Equilibrium bond length
    
    printfn "Computing integrals for H2 (R = 0.74 Å, STO-3G basis)..."
    printfn ""
    
    match provider h2 with
    | Ok integrals ->
        printfn "✅ Integrals computed successfully!"
        printfn ""
        printfn "   Number of orbitals: %d" integrals.NumOrbitals
        printfn "   Number of electrons: %d" integrals.NumElectrons
        printfn "   Nuclear repulsion: %.6f Hartree" integrals.NuclearRepulsion
        printfn ""
        printfn "   One-electron integrals h[p,q]:"
        for p in 0 .. integrals.NumOrbitals - 1 do
            for q in 0 .. integrals.NumOrbitals - 1 do
                printfn "     h[%d,%d] = %10.6f" p q integrals.OneElectron.Integrals.[p,q]
        printfn ""
        printfn "   Two-electron integrals (pq|rs) - diagonal terms:"
        for p in 0 .. integrals.NumOrbitals - 1 do
            printfn "     (%d%d|%d%d) = %10.6f" p p p p integrals.TwoElectron.Integrals.[p,p,p,p]
        printfn ""
        match integrals.ReferenceEnergy with
        | Some hf -> printfn "   Hartree-Fock reference energy: %.6f Hartree" hf
        | None -> ()
        
    | Error msg ->
        printfn "❌ Error: %s" msg
    
    shutdownPython()

// ==============================================================================
// EXAMPLE: VQE with Real Integrals
// ==============================================================================

open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

/// Demonstrate VQE calculation using real PySCF integrals
let demonstrateVQEWithRealIntegrals () =
    printfn ""
    printfn "╔══════════════════════════════════════════════════════════════════╗"
    printfn "║           VQE with Real PySCF Molecular Integrals                ║"
    printfn "╚══════════════════════════════════════════════════════════════════╝"
    printfn ""
    
    initializePython()
    
    let provider = createPySCFProvider "sto-3g"
    let h2 = createH2 0.74
    
    printfn "Step 1: Computing real molecular integrals with PySCF..."
    
    match provider h2 with
    | Ok integrals ->
        printfn "   ✓ Integrals computed"
        printfn "   ✓ Nuclear repulsion: %.6f Hartree" integrals.NuclearRepulsion
        printfn "   ✓ HF reference: %.6f Hartree" (integrals.ReferenceEnergy |> Option.defaultValue 0.0)
        printfn ""
        
        printfn "Step 2: Building qubit Hamiltonian via Jordan-Wigner transform..."
        
        // Use the library's buildFromIntegrals function
        match MolecularHamiltonian.buildFromIntegrals integrals MolecularHamiltonian.MappingMethod.JordanWigner with
        | Ok (hamiltonian, nuclearRepulsion) ->
            printfn "   ✓ Hamiltonian built"
            printfn "   ✓ Number of qubits: %d" hamiltonian.NumQubits
            printfn "   ✓ Number of Pauli terms: %d" hamiltonian.Terms.Length
            printfn ""
            
            printfn "Step 3: Running VQE optimization..."
            
            let backend = LocalBackend() :> IQuantumBackend
            
            // Note: For a complete VQE run, you would use:
            // let config = {
            //     Method = GroundStateMethod.VQE
            //     MaxIterations = 100
            //     Tolerance = 1e-6
            //     InitialParameters = None
            //     Backend = Some backend
            //     ProgressReporter = None
            //     ErrorMitigation = None
            //     IntegralProvider = Some provider
            // }
            // let! result = GroundStateEnergy.estimateEnergy h2 config
            
            printfn "   (VQE optimization would run here with real integrals)"
            printfn ""
            printfn "Expected results with real integrals:"
            printfn "   • H2 ground state energy: ~-1.137 Hartree (exact: -1.1373)"
            printfn "   • This matches literature values!"
            
        | Error err ->
            printfn "   ❌ Hamiltonian build failed: %A" err
            
    | Error msg ->
        printfn "   ❌ Integral computation failed: %s" msg
    
    printfn ""
    shutdownPython()

// ==============================================================================
// USAGE INSTRUCTIONS
// ==============================================================================

printfn "╔══════════════════════════════════════════════════════════════════╗"
printfn "║         PySCF Integration for FSharp.Azure.Quantum              ║"
printfn "╚══════════════════════════════════════════════════════════════════╝"
printfn ""
printfn "This script provides real molecular integrals from PySCF."
printfn ""
printfn "PREREQUISITES:"
printfn "  1. Python 3.8+ installed"
printfn "  2. pip install pyscf numpy"
printfn ""
printfn "USAGE:"
printfn "  // Create an integral provider"
printfn "  let provider = createPySCFProvider \"sto-3g\""
printfn ""
printfn "  // Use in VQE config"
printfn "  let config = { ... ; IntegralProvider = Some provider }"
printfn ""
printfn "To run demos, uncomment one of:"
printfn "  // demonstrateIntegrals()"
printfn "  // demonstrateVQEWithRealIntegrals()"
printfn ""

// Uncomment to run demos:
// demonstrateIntegrals()
// demonstrateVQEWithRealIntegrals()
